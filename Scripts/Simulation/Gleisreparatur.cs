namespace AkteEuropaReborn.Rendering;

using System.Text;
using Godot;
using AkteEuropaReborn.Simulation.Commands;

/// <summary>
/// ⭐⭐ <b>DER SCHRAUBENSCHLUESSEL UEBER ZERSCHOSSENEM GLEIS</b> (23.09.2026,
/// bug-368, berichte/k21-gleisreparatur.md).
///
/// <para>Gemeldet in K21: »Mit dem Bauer kann man normalerweise Schienen
/// reparieren, da kommt dann so ein Reparatur Icon, wenn man mit dem Bauer
/// ueber die zerstoerte Strecke geht — fehlt bei uns.« Die Reparaturkette
/// (Simulation/RailRepair.cs) war seit August gebaut; es fehlte ihr ANFANG —
/// dort stand »woher der erste Auftrag kommt, ist ungelesen«. Jetzt gelesen,
/// C-EXE, jede Stelle selbst:</para>
/// <code>
/// Zeigerwahl 0x4315D0:
///   keine Befehlsart (dword[0x502ACC] == 0) -> 0x431B19
///   Maus ueber der Bedienleiste                -> Zeigerart 3        @0x431B2D
///   gewaehlt (word[0x4FA0C8]) &lt; 8000 und
///   byte[+0x0E] == 0x49 (Teil 73, Boden-Techniker)                  @0x431B6E
///   und 0x4B0A60(MausSpalte, MausZeile): irgendein Gleisplatz dieser
///       Zelle mit Bild >= 100 (3000 Plaetze ab 0xC2C220)
///   -> Zeigerart 22 (0x16), zurueck — VOR Angriff, Strg und Fahrt   @0x431B93
/// Zeigerart 22 -> Tafel 0x4A9BEC -> 0x4A9BA1 mov dl,0x11 -> BILD 17
/// Klick 0x4370BF, Tafel 0x437994[22] = 0x437836:
///   Befehl 0x1D = 29, P1 = gewaehlte Einheit, P2/P3 = Zelle unter der Maus
/// Behandler 0x4C3683 (Tafel 0x4C4D54, Eintrag 28):
///   0x4103B0(Einheit): +0x14 := 0 (Auftrag), +0x1A := 0xFF
///   +0x48 := Spalte, +0x49 := Zeile, word[+0x40] := 1
/// Takt 0x408267 (Teileverteiler, NUR Teil 73 — Tafel 0x40A188/0x40A16C):
///   word[+0x40] == 0 -> nichts; sonst hinfahren / arbeiten (RailRepair.cs)
/// 0x409A9A: nichts mehr kaputt -> word[+0x40] := 0 und @0x409AB5
///   faze := 0 — UNBEDINGT, ohne Vergleich mit 3.
/// </code>
///
/// <para>⚠ UNSERE SETZUNGEN:</para>
/// <list type="bullet">
/// <item>Der Befehl geht an ALLE gewaehlten eigenen Boden-Techniker, nicht nur
///   an die eine gewaehlte Einheit des Originals — bei uns kann man mehrere
///   waehlen. Der ZEIGER fragt wie das Original nur die erste (<c>_selected</c>).</item>
/// <item>Geklickt wird mit rechts wie jeder Befehl bei uns (derselbe Klickarm
///   0x4370BF traegt auch das Absetzen, Zeigerart 12).</item>
/// <item>faze := 0 bleibt bei uns an »war 3« gebunden (RailFreight.RailRepair):
///   unser Automat startet bei 0 sofort einen Zug, auch wenn noch einer rollt.</item>
/// <item>Ohne den Befehl beginnt die Arbeit bei uns NICHT mehr von selbst, wenn
///   ein Boden-Techniker zufaellig auf dem Bruch stehen bleibt — das Original
///   verlangt word[+0x40] != 0. Mit <c>--reparaturzeiger-aus</c> gilt der alte
///   Stand (Zeiger aus, Selbststart an).</item>
/// </list>
/// </summary>
public partial class MapEntityLayer
{
    /// <summary>Wie oft Befehl 29 angenommen wurde — fuer den Pruefstand.</summary>
    public int Gleisreparaturbefehle;

    /// <summary>Steht der Schraubenschluessel hier? Zeigerart 22 @0x431B44..0x431B93:
    /// die gewaehlte Einheit traegt Teil 73, und auf der Zelle unter der Maus liegt
    /// ein zerschossenes Gleisstueck (0x4B0A60, jede Linie).</summary>
    public bool ReparaturzeigerHier(Vector2 mapPos)
    {
        if (ReparaturzeigerAus) return false;
        if (_selected < 0 || _selected >= _entities.Count) return false;
        var e = _entities[_selected];
        if (e.Dead || e.IsBuilding || e.IsProp || e.Owner != ViewPlayer) return false;
        if (e.Equipment != RailRepairPart) return false;
        return CellAt(mapPos) is { } z && RailBrokenAt(z.X, z.Y);
    }

    /// <summary>Der Klickarm 0x437836: Befehl 29 an die gewaehlten Boden-Techniker.
    /// false, wenn hier kein Schraubenschluessel steht — dann geht der Klick an
    /// Einnehmen/Angreifen/Fahren weiter.</summary>
    public bool PostGleisreparaturKlick(Vector2 mapPos)
    {
        if (!ReparaturzeigerHier(mapPos) || CellAt(mapPos) is not { } z) return false;
        int n = 0;
        foreach (int i in _sel)
        {
            if (i < 0 || i >= _entities.Count) continue;
            var e = _entities[i];
            if (e.Dead || e.IsBuilding || e.IsProp || e.Owner != ViewPlayer) continue;
            if (e.Equipment != RailRepairPart) continue;
            if (Emit(CommandRecord.Make(CommandOp.RailRepairOrder, (byte)ViewPlayer,
                                        (short)i, (short)z.X, (short)z.Y))) n++;
        }
        if (n > 0) GD.Print($"gleisreparatur: Befehl 29 an {n} Boden-Techniker, Zelle ({z.X},{z.Y}) (Original 0x437836)");
        return n > 0;
    }

    /// <summary>Der Behandler 0x4C3683.</summary>
    private bool ApplyRailRepairOrder(in CommandRecord c)
    {
        int i = c.P1;
        if (i < 0 || i >= _entities.Count) return false;
        var e = _entities[i];
        if (e.Dead || e.IsBuilding || e.IsProp) return false;
        // 0x4103B0: der laufende Auftrag faellt weg (+0x14 := 0).
        e.RailWork = 0;
        e.Path = null;
        e.Reserved = null;
        e.Target = -1;
        // +0x48/+0x49 := Zelle, word[+0x40] := 1
        e.RailGoal = new Vector2I(c.P2, c.P3);
        Gleisreparaturbefehle++;
        return true;
    }

    // ---- der Pruefstand --gleisreparatur-check -------------------------------
    //
    // K21: bricht ein Gleisstueck von Linie 4 (57 <-> Basis), gibt einem eigenen
    // Landfahrzeug Teil 73, stellt es 6 Zellen daneben, waehlt es und fragt den
    // ZEIGER ueber dem Bruch und daneben. Dann der KLICK ueber den Spielerweg
    // (PostGleisreparaturKlick) — und nach 60 s: ist das Gleis heil, faze 0?
    //   Soll:        Zeiger Reparatur ueber dem Bruch, nicht daneben; Befehl 1x;
    //                Linie heil, faze != 3.
    //   Nullmodell --reparaturzeiger-aus: kein Zeiger, kein Befehl, Gleis bleibt
    //                kaputt (das Fahrzeug steht ja NICHT darauf).
    private float _grZeit = -1f;
    private int _grLinie = -1, _grFahrzeug = -1;
    private Vector2I _grBruch;
    private string _grZeigerDrauf = "", _grZeigerDaneben = "";
    private bool _grKlick, _grFertig;

    /// <summary>Aus <see cref="GleisbruchTakt"/>, je Bild.</summary>
    private void GleisreparaturCheckTakt(float dt)
    {
        if (!GleisreparaturCheckAn || _grFertig) return;
        if (_grZeit < 0f)
        {
            _grZeit = 0f;
            GleisreparaturCheckAufbau();
            return;
        }
        _grZeit += dt;
        if (!_grKlick && _grZeit >= 1f && _grFahrzeug >= 0)
        {
            _grKlick = true;
            var p = CellCenter(_grBruch.X, _grBruch.Y);
            _grZeigerDrauf = CursorHintAt(p).ToString();
            _grZeigerDaneben = CursorHintAt(CellCenter(_grBruch.X + 3, _grBruch.Y + 3)).ToString();
            bool ok = PostGleisreparaturKlick(p);
            GD.Print($"gleisreparatur-check: Klick auf ({_grBruch.X},{_grBruch.Y}) -> {(ok ? "Befehl 29 abgesetzt" : "KEIN Befehl")}");
        }
    }

    private void GleisreparaturCheckAufbau()
    {
        // ein heiles Stueck (kein Mast) auf Linie 4, sonst irgendeiner Linie
        RailCell? ziel = null;
        foreach (int bevorzugt in new[] { 4, -1 })
        {
            foreach (var c in _railCells)
            {
                if (c.Frame == 255 || c.Broken || c.Pylon) continue;
                if (bevorzugt >= 0 && c.Line != bevorzugt) continue;
                if (_nav != null && !_nav.CanEnter(c.Col, c.Row, Simulation.NavGrid.MoveClass.Vehicle)) continue;
                ziel = c; break;
            }
            if (ziel != null) break;
        }
        if (ziel == null) { GD.Print("gleisreparatur-check: kein Gleis"); _grFertig = true; return; }
        _grLinie = ziel.Line;
        _grBruch = new Vector2I(ziel.Col, ziel.Row);
        RailHit(ziel.Col, ziel.Row, 999);
        GD.Print($"gleisreparatur-check: Linie {_grLinie} gebrochen bei ({ziel.Col},{ziel.Row}) — " +
                 $"{RailBrokenOnLine(_grLinie)} Stuecke kaputt, faze {RailFazeOf(_grLinie)}");
        for (int i = 0; i < _entities.Count; i++)
        {
            var e = _entities[i];
            if (e.Dead || e.IsBuilding || e.IsProp || !e.Mobile || e.Owner != ViewPlayer) continue;
            if (e.Move != Simulation.NavGrid.MoveClass.Vehicle) continue;
            var frei = _nav?.NearestFree(new Vector2I(ziel.Col + 6, ziel.Row), e.Move, i);
            if (frei == null) continue;
            e.Equipment = RailRepairPart;       // Kruecke: auf K21 traegt keines Teil 73
            _nav?.ClearOccupant(e.Col, e.Row, i);
            e.Col = frei.Value.X; e.Row = frei.Value.Y;
            e.Pos = CellCenter(e.Col, e.Row);
            e.Path = null; e.Reserved = null; e.Target = -1; e.RailGoal = null; e.RailWork = 0;
            e.Goal = frei.Value;
            _nav?.SetOccupant(e.Col, e.Row, i);
            _sel.Clear(); _sel.Add(i); _selected = i;
            _grFahrzeug = i;
            GD.Print($"gleisreparatur-check: Fahrzeug {i} (Platz {e.Slot}) traegt Teil 73, " +
                     $"steht auf ({e.Col},{e.Row}), gewaehlt");
            return;
        }
        GD.Print("gleisreparatur-check: kein eigenes Landfahrzeug");
        _grFertig = true;
    }

    private int RailFazeOf(int slot)
    {
        foreach (var l in _railLines) if (l.Slot == slot) return l.Faze;
        return -1;
    }

    public string GleisreparaturCheckLine()
    {
        if (!GleisreparaturCheckAn) return "";
        int kaputt = _grLinie >= 0 ? RailBrokenOnLine(_grLinie) : -1;
        int faze = RailFazeOf(_grLinie);
        bool alt = ReparaturzeigerAus;
        bool zeigerOk = alt ? _grZeigerDrauf != nameof(Hint.Reparatur)
                            : _grZeigerDrauf == nameof(Hint.Reparatur) && _grZeigerDaneben != nameof(Hint.Reparatur);
        bool soll = alt
            ? zeigerOk && Gleisreparaturbefehle == 0 && kaputt > 0
            : zeigerOk && Gleisreparaturbefehle >= 1 && kaputt == 0 && faze != 3;
        var sb = new StringBuilder();
        sb.Append($"gleisreparatur-check{(alt ? " (--reparaturzeiger-aus)" : "")}: Linie {_grLinie}, Bruch ({_grBruch.X},{_grBruch.Y}) — ");
        sb.Append($"Zeiger drauf {_grZeigerDrauf}, daneben {_grZeigerDaneben}; Befehl 29 {Gleisreparaturbefehle}x; ");
        sb.Append($"repariert {RailRepairsDone}, Kette {RailRepairChained}; noch {kaputt} kaputt, faze {faze}");
        if (_grFahrzeug >= 0 && _grFahrzeug < _entities.Count)
        {
            var e = _entities[_grFahrzeug];
            sb.Append($"; Fahrzeug auf ({e.Col},{e.Row}), Ziel {(e.RailGoal is { } g ? $"({g.X},{g.Y})" : "keines")}");
        }
        sb.Append(alt ? (soll ? " — Nullmodell BESTANDEN" : " — Nullmodell DURCHGEFALLEN")
                      : (soll ? " — BESTANDEN" : " — DURCHGEFALLEN"));
        return sb.ToString();
    }
}
