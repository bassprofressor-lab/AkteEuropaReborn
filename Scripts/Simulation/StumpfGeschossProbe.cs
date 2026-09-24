namespace AkteEuropaReborn.Rendering;

using System.Collections.Generic;
using Godot;

/// <summary>
/// <c>--stumpf-geschoss-probe</c> — der Pruefstand zu <b>bug-367</b> (23.09.2026),
/// seiner Frage aus K21: »Kann es sein, dass wenn Baeume abgebrannt sind, die
/// Raketen trotzdem an den abgebrannten Stuempfen haengen bleiben?«
/// Lesung: <c>berichte/k21-raketen-stumpf.md</c>.
///
/// <para><b>Was er misst.</b> Phase A: jeder eigene Schuetze mit Flug-Geschoss
/// schiesst auf die Zelle HINTER jedem Baum in Reichweite; gemerkt wird jeder
/// Schuss, der an einem LEBENDEN Baum haengenbleibt (Schwelle 40, nicht der
/// Boden). Dann werden genau diese Baeume abgebrannt — abwechselnd als Stumpf
/// (imap 0xFFFE) und als stehender verkohlter Stamm (0xFFFF) — und Phase B
/// schiesst dieselben Schuesse noch einmal.</para>
///
/// <para><b>Soll:</b> in Phase B bleibt KEIN Schuss mehr an dieser Stelle haengen
/// (Original: beide Werte fallen in den Leer-Zweig @0x452EBE). Nullmodell
/// <c>--stumpf-haelt-geschoss</c>: alle bleiben wieder haengen.</para>
///
/// <para>⚠ Messfalle: ein Schuss, der in Phase B weiterfliegt, kann danach am
/// Boden oder am NAECHSTEN Baum haengen. Gezaehlt wird darum nur ein Halt
/// GENAU auf der alten Baumzelle (<see cref="GeschossHaltZelle"/>).</para>
/// </summary>
public partial class MapEntityLayer
{
    private int _stumpfProbeBilder;
    private bool _stumpfProbeFertig;

    /// <summary>Aus <c>_Process</c>: true heisst, dieser Rahmen gehoert der Probe.</summary>
    private bool StumpfGeschossProbeTakt()
    {
        if (!StumpfGeschossProbeAn || _stumpfProbeFertig) return false;
        if (++_stumpfProbeBilder < 10) return false;       // Karte und Objekte stehen
        _stumpfProbeFertig = true;
        GD.Print(StumpfGeschossProbe());
        GetTree().Quit(0);
        return true;
    }

    /// <summary>Wie viele Waldzellen ihre Geschossschwelle beim Abbrennen verloren haben.</summary>
    public int SchwellenGeloescht;

    /// <summary>Die Zelle, an der das letzte Geschoss an einem Hindernis haengenblieb
    /// (gesetzt im Geschosstakt). ⚠ Erster Anlauf mass »innerhalb einer Zelle um die
    /// alte Haltestelle« — im dichten Wald hielt dann der NAECHSTE Baum, und die
    /// Probe fiel durch, obwohl die Schwelle weg war.</summary>
    public Vector2I GeschossHaltZelle = new(-1, -1);
    private readonly Dictionary<string, int> _stumpfGrund = new();

    private sealed class StumpfFall
    {
        public int Schuetze, Col, Row;
        public Vector2I Halt;
        public List<Kartenobjekt> Baeume = new();
    }

    /// <summary>Einen Schuss abgeben und fliegen lassen. Gibt die Zelle zurueck,
    /// an der er an einem Hindernis (nicht am Boden) haengenblieb, sonst null.</summary>
    private Vector2I? StumpfSchuss(int si, int col, int row, out bool geschossen)
    {
        var s = _entities[si];
        s.Cooldown = 0f;
        if (s.AmmoMax > 0) s.Ammo = s.AmmoMax;
        _shots.Clear();
        geschossen = FireAtAusfuehren(si, col, row);
        if (!geschossen || _shots.Count == 0) return null;
        var q = _shots[0];
        for (int t = 0; t < 200 && _shots.Count > 0; t++)
        {
            int g = GeschossGestoppt, b = GeschossBoden;
            SimTickFuerProbe();
            if (GeschossGestoppt > g)
                return GeschossBoden > b ? null : GeschossHaltZelle;   // Boden ist kein Hindernis
        }
        return null;
    }

    public string StumpfGeschossProbe()
    {
        var sb = new System.Text.StringBuilder("stumpf-geschoss-probe"
            + (StumpfHaeltGeschoss ? " (NULLMODELL --stumpf-haelt-geschoss)" : "") + "\n");
        // Das eigene Ziel soll nicht mitspielen: keine Gegner, die zurueckschiessen.
        var faelle = new List<StumpfFall>();
        int schuesseA = 0;
        for (int k = 0; k < _entities.Count && faelle.Count < 40; k++)
        {
            var x = _entities[k];
            // ⚠ JEDER Schuetze, nicht nur die eigenen: die fuenf Startgeschuetze stehen
            // im Freien und haben kaum einen Baum in Reichweite (zweiter Anlauf: 1 Schuss).
            if (x.Dead || x.IsProp || x.IsBuilding) continue;
            if (x.Weapon <= 0 || x.Attack <= 0) continue;
            int xart = Simulation.DesignMath.SoundClass(WeaponRowOf(x.Weapon));
            if (FlightKind(xart) == null) continue;
            int weit = Mathf.Clamp(x.Range - 1, 2, 12), nah = Mathf.Max(2, x.RangeMin + 1);
            // ⚠ Erster Anlauf: Rundumschlag in 16 Richtungen — 83 Schuesse, 0 an einem
            // Baum, weil die Startartillerie im Freien steht. Jetzt gezielt: jeder
            // Baum in Reichweite, Ziel eine Zelle DAHINTER — am Ende der Bahn ist
            // das Geschoss am tiefsten.
            var baeume = new List<Kartenobjekt>();
            foreach (var e in _objDraw)
            {
                if (!e.IstWald || e.Abgebrannt) continue;
                int dc = e.Col - x.Col, dr = e.Row - x.Row;
                float dist = Mathf.Sqrt(dc * dc + dr * dr);
                if (dist < nah || dist > weit - 1) continue;
                baeume.Add(e);
            }
            int versuche = 0;
            foreach (var e in baeume)
            {
                if (faelle.Count >= 40 || versuche++ >= 60 || schuesseA >= 400) break;
                if (SchwelleAn(e.Col, e.Row) != 40) continue;
                float dc = e.Col - x.Col, dr = e.Row - x.Row, len = Mathf.Sqrt(dc * dc + dr * dr);
                int zc = e.Col + Mathf.RoundToInt(dc / len), zr = e.Row + Mathf.RoundToInt(dr / len);
                if (!AufKarte(zc, zr) || _objSchwelle.ContainsKey(zc * 1024 + zr)) continue;
                var halt = StumpfSchuss(k, zc, zr, out bool ok);
                if (!ok) { _stumpfGrund[FireAtGrund] = _stumpfGrund.GetValueOrDefault(FireAtGrund) + 1; continue; }
                schuesseA++;
                if (halt is not { } h) continue;
                // Nur ein Halt an einem LEBENDEN Baum zaehlt.
                var f = new StumpfFall { Schuetze = k, Col = zc, Row = zr, Halt = h };
                foreach (var b in _objDraw)
                    if (b.IstWald && !b.Abgebrannt && b.Col == h.X && b.Row == h.Y
                        && SchwelleAn(b.Col, b.Row) == 40)
                        f.Baeume.Add(b);
                if (f.Baeume.Count > 0) faelle.Add(f);
            }
        }
        sb.AppendLine($"   Phase A (lebende Baeume): {schuesseA} Schuesse, {faelle.Count} blieben an einem Baum haengen");
        foreach (var kv in _stumpfGrund) sb.AppendLine($"     kein Schuss: {kv.Value}x »{kv.Key}«");
        if (faelle.Count == 0)
            return sb.Append("   ⚠ DURCHGEFALLEN — kein Schuss blieb an einem Baum haengen; "
                           + "die Probe sagt auf dieser Karte nichts.\n").ToString();

        // Abbrennen: abwechselnd Stumpf und stehender verkohlter Stamm.
        int stumpf = 0, stamm = 0, schwelleVor = SchwellenGeloescht, n = 0;
        var gebrannt = new HashSet<Kartenobjekt>();
        foreach (var f in faelle)
            foreach (var e in f.Baeume)
            {
                if (!gebrannt.Add(e)) continue;
                e.Abgebrannt = true;
                e.Steht = (n++ & 1) == 1;
                if (e.Steht) stamm++; else stumpf++;
                ZelleNachBrandFreigeben(e);
            }
        int schwelleNachher = 0;
        foreach (var e in gebrannt) schwelleNachher += SchwelleAn(e.Col, e.Row);
        sb.AppendLine($"   abgebrannt: {stumpf} Stumpf (0xFFFE), {stamm} verkohlter Stamm (0xFFFF); "
                    + $"Schwelle geloescht {SchwellenGeloescht - schwelleVor}x, Summe der Schwellen danach {schwelleNachher}");

        // Phase B: dieselben Schuesse.
        int nochDa = 0, nochDaStamm = 0, frei = 0, schuesseB = 0;
        foreach (var f in faelle)
        {
            var halt = StumpfSchuss(f.Schuetze, f.Col, f.Row, out bool ok);
            if (!ok) continue;
            schuesseB++;
            bool gleich = halt is { } h && h == f.Halt;
            if (gleich)
            {
                nochDa++;
                foreach (var e in f.Baeume) if (e.Steht) { nochDaStamm++; break; }
            }
            else frei++;
        }
        sb.AppendLine($"   Phase B (abgebrannt): {schuesseB} Schuesse, {nochDa} blieben an derselben Stelle haengen "
                    + $"(davon {nochDaStamm} mit verkohltem Stamm), {frei} flogen weiter");

        bool urteil = StumpfHaeltGeschoss
            // ⚠ nicht »alle«: der Treffpunkt streut (Phase A: 40 von 42 bis 43 hielten am
            // Baum, gemessen 23.09.) — das Nullmodell muss nur die grosse Mehrheit zeigen.
            ? schuesseB > 0 && nochDa * 4 >= schuesseB * 3
            : nochDa == 0 && schuesseB > 0 && schwelleNachher == 0;
        sb.AppendLine(StumpfHaeltGeschoss
            ? (urteil ? "   Nullmodell BESTANDEN — mit dem Gegenschalter haengen sie wieder am Stumpf (Soll ≥ 75 %)"
                      : "   ⚠ Nullmodell DURCHGEFALLEN — der Gegenschalter holt den alten Stand nicht zurueck")
            : (urteil ? "   BESTANDEN — kein Geschoss haengt mehr an einem abgebrannten Baum (Original @0x452EBE)"
                      : "   ⚠ DURCHGEFALLEN"));
        return sb.ToString();
    }
}
