namespace AkteEuropaReborn.Rendering;

using System.Collections.Generic;
using Godot;

/// <summary>
/// ⭐ 29.10.2026, bug-669 — <b>DER SICHTRADIUS</b> (Spielerentscheidung 29.10.: »wie im
/// Original«, Gelaendehoehe + Sicht − 1). Bericht <c>berichte/bau-p4-0-6-12.md</c> §2.
///
/// <para><b>Am 29.10. selbst nachgelesen, beide Bauten.</b> Die Nebelrunde <c>0x4205B0</c>
/// hat je Aufdecker einen Arm:</para>
/// <code>
///   Einheiten (alle 8 Spieler × 1000 Saetze, also auch Schiffe):
///     C 0x42076F  +0x09 == 0xFF -> weiter (leer/tot)    F 0x41F932
///     C 0x42077D  UKOL +0x14 &gt;= 0x32 -> weiter         F 0x41F93D
///     C 0x4207AF  call 0x401AAF -> 0x41D0E0 (Spalte +0x00, Zeile +0x01)   F 0x41F96F -> 0x41C2A0
///     C 0x4207BC  cl = Sicht +0x2C                       F 0x41F97C
///     C 0x4207C8  add ax,cx ; 0x4207CF dec ax            F 0x41F988 / 0x41F98F
///     C 0x4207D4  call 0x401258 -> 0x4200C0 (Stempel)    F 0x41F994
///   Flugzeuge (sec19, 200 Saetze):
///     C 0x420822  Typ 0 / &gt;= 13 -> weiter ; 0x42084E uk 0 / 2 -> weiter
///     C 0x42085D  al = +0x24 ; 0x42086F dec ax ; 0x420874 Stempel   (F 0x41FA1D / 0x41FA2F / 0x41FA34)
///                 — KEIN Ruf auf 0x41D0E0: die Hoehe zaehlt fuer Flieger NICHT
///   Gebaeude: push 0xA (0x4206AB) — zehn, ohne Hoehe
///   Stempel C 0x4200C8 cmp si,0x13 ; jle ; mov si,0x13 — oben auf 19 geklemmt, unten nicht
///                 (ein Radius &lt; 0 stempelt nichts)
/// </code>
/// <para><b>Was »Gelaendehoehe« ist:</b> <c>0x41D0E0</c> (F <c>0x41C2A0</c>) liefert
/// <c>byte[dword[0x677E20] + (Zeile·Breite + Spalte)·4 + 2]</c> — Byte 2 des vierbytigen
/// Kachelsatzes der ZELLE, auf der der Satz steht (Spalte/Zeile +0x00/+0x01, bei uns
/// <c>Col/Row</c>, der Anker). Das ist die Hoehenstufe, die auch jede Kachel um
/// Stufe·15 px hebt (0x4B4785) — bei uns <c>ElevOf</c>.</para>
///
/// <para><b>Der Befund:</b> fuer Bodeneinheiten ist das bei uns seit dem 11.08.2026
/// gebaut (<c>FogGrid.UnitRadius</c>, <c>Watchers</c>); der Kommentar »NICHT gebaut, wir
/// nehmen die blosse Sicht« ueber <c>CellDistance</c> war veraltet. ⭐ Falsch war der
/// FLIEGER: <c>Watchers</c> gab <c>Sicht − 1</c> UND die Hoehe der Zelle darunter an
/// <c>UnitRadius</c> weiter, das noch einmal 1 abzog — Radius <c>Sicht + Hoehe − 2</c>
/// statt <c>Sicht − 1</c>. Behoben (Hoehe 1 an UnitRadius). Gegenschalter
/// <c>--sichtradius-alt</c>; Messschalter <c>--sichtradius-bloss</c> (Nullmodell »blosse
/// Sicht« fuer Bodeneinheiten). Pruefstand <c>--sichtradius-check</c>.</para>
/// <para>⚠ Nicht gebaut (Nebenbefund): das Original laesst Flieger mit uk 0/2 nicht
/// aufdecken; bei uns ist nur <c>Stored</c> ausgenommen — unser <c>Order 0</c> heisst
/// »in der Luft untaetig« (MapEntityLayer UpdateAircraft) und ist nicht das uk 0 des
/// Originals.</para>
/// </summary>
public partial class MapEntityLayer
{
    /// <summary><c>--sichtradius-alt</c> — Flieger wieder mit Hoehe und doppeltem −1.</summary>
    public static bool SichtradiusAlt;

    /// <summary><c>--sichtradius-bloss</c> — MESSSCHALTER/Nullmodell: Bodeneinheiten decken
    /// mit der blossen Sicht auf (ohne Hoehe und −1).</summary>
    public static bool SichtradiusBloss;

    public static bool SichtradiusCheckAn;

    /// <summary>Stempelt nur den EINEN Aufdecker, den <c>Watchers</c> fuer die Zelle
    /// (c, r) liefert, in ein frisches Gitter und misst den Radius (Mittelzeile nach
    /// rechts, Mittelspalte nach unten). −1 = kein Aufdecker auf der Zelle.</summary>
    private (int Radius, int Senkrecht, int Zellen, int Sicht, int Hoehe) SrMessen(int c, int r, bool flieger)
    {
        (int, int, int, int)? w = null;
        foreach (var x in Watchers(ViewPlayer))
            if (x.Col == c && x.Row == r) { w = x; if (!flieger) break; }
        if (w == null || _fog == null) return (-1, -1, 0, 0, 0);
        var g = new Simulation.FogGrid(_fog.Width, _fog.Height);
        g.Update(new List<(int, int, int, int)> { w.Value });
        int rad = 0, senk = 0;
        while (g.IsStamped(c + rad + 1, r)) rad++;
        while (g.IsStamped(c, r + senk + 1)) senk++;
        var (_, _, st, _) = g.Counts();
        if (!g.IsStamped(c, r)) rad = senk = -1;
        return (rad, senk, st, w.Value.Item3, w.Value.Item4);
    }

    public string SichtradiusCheck()
    {
        var sb = new System.Text.StringBuilder("sichtradius-check"
            + (SichtradiusAlt ? " — GEGENSCHALTER --sichtradius-alt" : "")
            + (SichtradiusBloss ? " — NULLMODELL --sichtradius-bloss" : "") + "\n");
        if (_nav == null || _fog == null) return sb.Append("  KEIN URTEIL: keine Karte/kein Nebel").ToString();
        Simulation.FogGrid.Load();
        // ---- die Zellen: eben (Stufe 0) und die hoechste Stufe ----------------
        int hoch = 0;
        for (int r = 2; r < _nav.Height - 2; r++)
            for (int c = 2; c < _nav.Width - 2; c++)
                hoch = Mathf.Max(hoch, ElevOf(c, r));
        sb.AppendLine($"  Karte {_nav.Width}x{_nav.Height}, hoechste Stufe {hoch}");
        if (hoch < 2) return sb.Append("  KEIN URTEIL: keine Huegel (Stufe >= 2) auf dieser Karte").ToString();
        // eine bewegliche, eigene Bodeneinheit mit Sicht >= 2
        int ui = -1;
        for (int i = 0; i < _entities.Count; i++)
        {
            var e = _entities[i];
            if (e.IsBuilding || e.IsProp || e.Dead || !e.Mobile || e.Owner != ViewPlayer || Untergestellt(e)) continue;
            if (e.Sight < 2 || e.Ukol >= 50) continue;
            ui = i; break;
        }
        if (ui < 0) return sb.Append("  KEIN URTEIL: keine eigene Bodeneinheit mit Sicht >= 2").ToString();
        var u = _entities[ui];
        Vector2I? Zelle(int stufe)
        {
            for (int r = 3; r < _nav.Height - 3; r++)
                for (int c = 3; c < _nav.Width - 3; c++)
                    if (ElevOf(c, r) == stufe && _nav.CanEnter(c, r, u.Move) && _nav.OccupantAt(c, r) < 0)
                        return new Vector2I(c, r);
            return null;
        }
        bool ok = true;
        int c0 = u.Col, r0 = u.Row;
        foreach (int stufe in new[] { 0, hoch / 2, hoch })
        {
            if (Zelle(stufe) is not { } z) { sb.AppendLine($"  Stufe {stufe}: keine befahrbare freie Zelle"); continue; }
            ProbeVersetzen(ui, z.X, z.Y);
            int soll = Mathf.Min(19, stufe + u.Sight - 1), bloss = Mathf.Min(19, u.Sight);
            var m = SrMessen(z.X, z.Y, false);
            bool t = m.Radius == soll && m.Senkrecht == soll;
            ok &= t;
            sb.AppendLine($"  Bodeneinheit Platz {u.Slot} ({LabelOf(u)}, Sicht {u.Sight}) auf ({z.X},{z.Y}) Stufe {ElevOf(z.X, z.Y)}: "
                        + $"Radius waagerecht {m.Radius} / senkrecht {m.Senkrecht}, {m.Zellen} Zellen — Soll Hoehe+Sicht−1 = {soll}, "
                        + $"Nullmodell blosse Sicht = {bloss} -> {(t ? "ja" : "NEIN")}");
        }
        ProbeVersetzen(ui, c0, r0);
        // ---- ein Flieger: eigener in der Luft, sonst ein fremder geliehen (Eingriff) ----
        Special f = null;
        foreach (var sp in _special) if (!sp.Dead && !sp.Stored && sp.Kind < 13 && sp.Sight >= 2) { f = sp; break; }
        if (f == null) sb.AppendLine("  Flieger: keiner in der Luft — Teil entfaellt (KEIN URTEIL fuer Flieger)");
        else
        {
            int fo = f.Owner, fc = f.Col, fr = f.Row;
            if (fo != ViewPlayer) sb.AppendLine($"  EINGRIFF: Flieger Platz {f.Slot} (Spieler {fo}) zaehlt fuer die Messung als Spieler {ViewPlayer}");
            f.Owner = ViewPlayer;
            foreach (int stufe in new[] { 0, hoch })
            {
                if (Zelle(stufe) is not { } z) continue;
                f.Col = z.X; f.Row = z.Y;
                int soll = Mathf.Min(19, f.Sight - 1), alt = Mathf.Min(19, f.Sight - 2 + stufe);
                var m = SrMessen(z.X, z.Y, true);
                bool t = m.Radius == soll;
                ok &= t;
                sb.AppendLine($"  Flieger Platz {f.Slot} ({f.Name}, Sicht {f.Sight}) ueber ({z.X},{z.Y}) Stufe {stufe}: Radius {m.Radius} — "
                            + $"Soll Sicht−1 = {soll} (ohne Hoehe, 0x42086F), alter Stand Sicht+Hoehe−2 = {alt} -> {(t ? "ja" : "NEIN")}");
            }
            f.Owner = fo; f.Col = fc; f.Row = fr;
        }
        sb.Append(ok ? "  BESTANDEN" : "  DURCHGEFALLEN");
        return sb.ToString();
    }
}
