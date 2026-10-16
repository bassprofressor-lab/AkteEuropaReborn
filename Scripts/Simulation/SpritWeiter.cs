using System.Collections.Generic;
using System.Text;
using Godot;

namespace AkteEuropaReborn.Rendering;

/// <summary>
/// ⭐ <b>bug-585 — nach dem Tanken fährt sie weiter</b> (KayelGee R6, 14.10.2026:
/// »Wenn Einheiten während eines Bewegungsbefehls leer laufen und danach wieder
/// aufgetankt werden (durch Nachschubhelikopter), dann sollten sie automatisch
/// weiterfahren, ohne extra Befehl.«).
///
/// <para><b>Gelesen (C = Temp\opencode\aekernel\GAME.EXE, 22.01.1998):</b></para>
/// <list type="bullet">
/// <item><c>order()</c> @0x40B070 setzt <c>+0x14 = 2</c> (»drive«), Ziel
/// <c>+0x18/+0x19</c>, Wegzeiger <c>+0x1A</c>; der Weg selbst liegt im Puffer
/// 0x7AEC38 je Einheit. Ein Sprittest steht dort NICHT.</item>
/// <item>»on square« @0x407AA7..0x407AFF: Sprit −1; bei 0 (und Klasse +0x0A ≠ 1)
/// nur <c>word[+0x06] = 0</c> (kolik, @0x407AF2) und <c>byte[+0x04] = 0xFF</c>
/// (Richtung »steht«, @0x407AF8), dann Sprung ans Ende 0x409F0A. Auftrag, Ziel,
/// Wegzeiger und Wegpuffer werden NICHT angefasst.</item>
/// <item>Stehzweig @0x407EA0: @0x407F09 <c>cmp word[+0x2E],0 / jg</c> — bei Sprit
/// ≤ 0 (außer Klasse 1) Sprung an 0x409EEE vorbei an der Auftragsweiche
/// (Tafel 0x40A0D8, Index 0x40A130). Mit Sprit > 0 läuft die Weiche wieder,
/// Auftrag 2 = »drive« @0x408ABC liest den Weg am alten Zeiger weiter.</item>
/// <item>Die Abgabe des Treibstoffhelis @0x4248D5/0x424946 schreibt beim Kunden
/// nur <c>byte[+0x44] = 0</c> und <c>Sprit := Sprit_max</c> — keinen Auftrag.</item>
/// </list>
/// <para>Also: <b>im Original fährt die Einheit nach dem Tanken von selbst
/// weiter</b>, auf ihrem alten Weg. Bei uns warf der Nulldurchgang <c>Path</c> weg
/// (25.08.2026), und nichts holte ihn zurück.</para>
///
/// <para><b>Nachbau:</b> beim Nulldurchgang wird der Restweg gemerkt
/// (<see cref="Entity.SpritWeg"/>), und im Einheitentakt bekommt sie ihn zurück,
/// sobald wieder Sprit im Tank ist. ⚠ SETZUNG: der gemerkte Weg geht nicht in den
/// Spielstand (wie jeder andere Weg bei uns auch, SaveState.cs).
/// Gegenschalter <c>--sprit-weiter-alt</c>; Prüfstand <c>--sprit-weiter-check</c>.</para>
/// </summary>
public partial class MapEntityLayer
{
    /// <summary><c>--sprit-weiter-alt</c>: Stand vor bug-585 (der Weg ist weg).</summary>
    public static bool SpritWeiterAlt;

    /// <summary>Wie oft eine Einheit ihren gemerkten Weg wieder aufgenommen hat.</summary>
    public static int SpritWeiterGefahren;

    /// <summary>Beim Nulldurchgang (»on square«): den Restweg merken.</summary>
    private void SpritWegMerken(Entity e)
    {
        e.SpritWeg = null;
        if (SpritWeiterAlt || e.Path == null || e.PathIdx >= e.Path.Count) return;
        e.SpritWeg = e.Path;
        e.SpritIdx = e.PathIdx;
    }

    /// <summary>Einmal je Takt für eine Einheit mit gemerktem Weg.</summary>
    private void SpritWeiterTakt(Entity e)
    {
        // Ein neuer Weg (Befehl, Ausweichen …) ersetzt den gemerkten.
        if (e.Path != null) { e.SpritWeg = null; return; }
        if (e.FuelMax > 0 && e.Fuel <= 0) return;          // 0x407F09: noch trocken
        var weg = e.SpritWeg!;
        e.SpritWeg = null;
        if (e.SpritIdx >= weg.Count) return;
        e.Path = weg;
        e.PathIdx = e.SpritIdx;
        e.Reserved = null;
        e.StepCost = 0; e.Progress = 0;
        e.WaitTime = 0;
        SpritWeiterGefahren++;
    }

    // ---------------- Prüfstand --sprit-weiter-check -----------------------------

    /// <summary>
    /// Kopflos, synchron: ein eigenes Fahrzeug bekommt über den echten Absender
    /// (<see cref="PostMove"/>) einen Fahrbefehl, der Tank wird auf 3 gestellt, es
    /// fährt leer, steht 3 s, dann tankt es ein Probeheli (Art 13, wie
    /// HeliTempoProbe) auf, und gemessen wird, ob es OHNE neuen Befehl sein Ziel
    /// erreicht. <b>Nullmodell <c>--sprit-weiter-alt</c>: es MUSS stehen bleiben.</b>
    /// </summary>
    public string SpritWeiterCheck()
    {
        var sb = new StringBuilder("sprit-weiter-check (bug-585)\n");
        sb.AppendLine($"  Schalter: sprit-weiter-alt {SpritWeiterAlt}");
        if (SpritWeiterAlt) sb.AppendLine("  ⚠ NULLMODELL --sprit-weiter-alt: Teil 4 MUSS durchfallen");
        if (_nav == null) return sb.Append("  kein Gitter\n  Ergebnis: DURCHGEFALLEN").ToString();
        bool alles = true;
        void Soll(bool ok, string was) { alles &= ok; sb.AppendLine($"  {(ok ? "ok  " : "FEHL")} {was}"); }

        for (int t = 0; t < 2 * SimHz; t++) SimTickFuerProbe();
        int u = -1;
        foreach (int i in AnEigene())
        {
            var c = _entities[i];
            if (c.Move == Simulation.NavGrid.MoveClass.Vehicle && c.Infantry < 0 && c.FuelMax > 0) { u = i; break; }
        }
        if (u < 0) return sb.Append("  kein eigenes Fahrzeug mit Tank\n  Ergebnis: DURCHGEFALLEN").ToString();
        var e = _entities[u];
        var von = new Vector2I(e.Col, e.Row);

        // ein Ziel 10..14 Zellen weit mit Weg von mindestens 8 Schritten
        Vector2I? ziel = null;
        var offs = new List<Vector2I>();
        for (int dy = -14; dy <= 14; dy++)
            for (int dx = -14; dx <= 14; dx++)
                if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) >= 10) offs.Add(new Vector2I(dx, dy));
        offs.Sort((p, q) => p.LengthSquared().CompareTo(q.LengthSquared()));
        foreach (var o in offs)
        {
            var z = von + o;
            if (!_nav.InBounds(z.X, z.Y) || !_nav.IsFree(z.X, z.Y, e.Move, u)) continue;
            var p = _nav.FindPath(von, z, e.Move, u);
            if (p != null && p.Count >= 8) { ziel = z; break; }
        }
        if (ziel == null) return sb.Append($"  kein Ziel 10..14 Zellen um ({von.X},{von.Y})\n  Ergebnis: DURCHGEFALLEN").ToString();

        // Teil 1: der Befehl, mit vollem Tank abgeschickt (der Absender weist
        // eine trockene Einheit ab), danach Tank 3
        e.Fuel = e.FuelMax;
        _sel.Clear(); _selAir = -1; _sel.Add(u); _selected = u;
        PostMove(CellCenter(ziel.Value.X, ziel.Value.Y));
        int n = 0;
        while (e.Path == null && n < 10) { SimTickFuerProbe(); n++; }
        Soll(e.Path != null, $"Teil 1: Einheit Platz {e.Slot} ({von.X},{von.Y}) -> Ziel ({e.Goal.X},{e.Goal.Y}), "
                           + $"Weg {(e.Path == null ? 0 : e.Path.Count)} Schritte nach {n} Takten");
        if (e.Path == null) return sb.Append("  Ergebnis: DURCHGEFALLEN").ToString();
        var goal = e.Goal;
        e.Fuel = 3;

        // Teil 2: sie faehrt leer
        n = 0;
        while (e.Fuel > 0 && n < 60 * SimHz && !e.Dead) { SimTickFuerProbe(); n++; }
        var leer = new Vector2I(e.Col, e.Row);
        Soll(e.Fuel == 0 && leer != goal, $"Teil 2: leer nach {n} Takten auf ({leer.X},{leer.Y}), "
                                       + $"Weg {(e.Path == null ? "weg" : "da")}, gemerkt {(e.SpritWeg == null ? "nein" : $"{e.SpritWeg.Count - e.SpritIdx} Schritte")}");

        // Teil 3: 3 s trocken — sie steht
        for (int t = 0; t < 3 * SimHz; t++) SimTickFuerProbe();
        Soll(e.Col == leer.X && e.Row == leer.Y && e.Path == null,
             $"Teil 3: nach 3 s trocken auf ({e.Col},{e.Row}), Weg {(e.Path == null ? "keiner" : "⚠ da")}");

        // Teil 4: der Probeheli tankt sie, ohne neuen Befehl
        int slot = 0;
        foreach (var s in _special) slot = Mathf.Max(slot, s.Slot + 1);
        var start = e.Pos + new Vector2(10 * TileW, 0);
        if (start.X > MapZellSize().X * TileW - TileW) start = e.Pos - new Vector2(10 * TileW, 0);
        var heli = new Special
        {
            Slot = slot, Kind = 13, Name = "Probeheli", TypeName = "Probeheli",
            Col = e.Col, Row = e.Row, Stored = false, Owner = e.Owner, Pos = start,
            Speed = 8, StufeUnten = 0, Stufe = 0,
            Hp = 100, HpMax = 100, Fuel = 2000, FuelMax = 2000,
            Cargo = SupplyCargoFull,
        };
        _special.Add(heli);
        n = 0;
        while (e.Fuel == 0 && n < 120 * SimHz && !e.Dead) { SimTickFuerProbe(); n++; }
        Soll(e.Fuel > 0, $"Teil 4a: vom Heli getankt nach {n} Takten, Sprit {e.Fuel}/{e.FuelMax}");
        int gef0 = SpritWeiterGefahren;
        n = 0;
        while ((e.Col != goal.X || e.Row != goal.Y) && n < 60 * SimHz && !e.Dead) { SimTickFuerProbe(); n++; }
        bool an = e.Col == goal.X && e.Row == goal.Y;
        Soll(an, $"Teil 4b: {(an ? "am Ziel" : "⚠ NICHT am Ziel")} ({e.Col},{e.Row}) nach {n} Takten, "
               + $"wieder aufgenommen {SpritWeiterGefahren - gef0}x");
        Soll(e.Goal == goal, $"Teil 4c: Ziel unveraendert ({e.Goal.X},{e.Goal.Y}) — der Pruefstand schickt nach Teil 1 keinen Befehl");
        heli.Dead = true;

        sb.AppendLine($"  Zaehler: wieder aufgenommen gesamt {SpritWeiterGefahren}, kein Sprit gemeldet {OhneSpritGemeldet}");
        bool ergebnis = SpritWeiterAlt ? !an : alles;
        sb.AppendLine(SpritWeiterAlt
            ? $"  NULLMODELL: {(an ? "⚠ sie kam trotzdem an — der Pruefstand misst nichts" : "sie bleibt stehen, wie vor bug-585")}"
            : "  NULLMODELL: --sprit-weiter-alt muss in Teil 4b stehen bleiben");
        if (SpritWeiterAlt)
            return sb.Append($"  Ergebnis: {(ergebnis ? "NULLMODELL ROT wie erwartet (Teil 4b faellt)" : "⚠ NULLMODELL GRUEN — der Pruefstand misst nichts")}").ToString();
        return sb.Append($"  Ergebnis: {(ergebnis ? "BESTANDEN" : "DURCHGEFALLEN")}").ToString();
    }
}
