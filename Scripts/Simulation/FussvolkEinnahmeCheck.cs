namespace AkteEuropaReborn.Rendering;

using Godot;

/// <summary>
/// <b>--fussvolk-einnahme-check</b> (29.10.2026, bug-653, KayelGee R8 K11) — siehe
/// <c>Capture.StanderAt</c>. Ein eigener Infanterist steht auf der Einnahmezelle
/// (Tuer 0 + 1 Zeile) eines fremden, einnehmbaren Gebaeudes; 600 Takte. Soll:
/// Fortschritt 0, Besitzer unveraendert. Gegenprobe im selben Lauf: ein eigenes
/// FAHRZEUG auf derselben Zelle — Fortschritt &gt; 0 (sonst sagt die erste 0 nichts).
/// Nullmodell <c>--fussvolk-einnahme-alt</c>: der Soldat nimmt ein (Fortschritt &gt; 0
/// oder Besitzerwechsel) → DURCHGEFALLEN.
/// <para>⚠ EINGRIFFE: Lage (je Takt festgeheftet), TP 30000 der beiden Probanden; das
/// Gebaeude wird nicht veraendert.</para>
/// </summary>
public partial class MapEntityLayer
{
    public static bool FussvolkEinnahmeCheckAn;

    public string FussvolkEinnahmeCheck()
    {
        var sb = new System.Text.StringBuilder("fussvolk-einnahme-check"
            + (FussvolkEinnahmeAlt ? " — NULLMODELL --fussvolk-einnahme-alt" : "") + "\n");
        if (_nav == null) return sb.Append("  KEIN URTEIL: keine Karte").ToString();
        int bi = -1;
        for (int i = 0; i < _entities.Count; i++)
        {
            var b = _entities[i];
            if (!Capturable(b) || b.Owner == ViewPlayer || b.BType == 1) continue;
            var (_, f) = CaptureCells(b);
            if (!_nav.InBounds(f.X, f.Y) || _nav.OccupantAt(f.X, f.Y) >= 0) continue;
            bi = i; break;
        }
        if (bi < 0) return sb.Append("  KEIN URTEIL: kein freies fremdes Gebaeude").ToString();
        var geb = _entities[bi];
        var (tuer, front) = CaptureCells(geb);
        int fi = -1, vi = -1;
        for (int i = 0; i < _entities.Count; i++)
        {
            var e = _entities[i];
            if (e.IsBuilding || e.IsProp || e.Dead || e.Owner != ViewPlayer || Untergestellt(e) || !e.Mobile) continue;
            if (e.Infantry >= 0 && fi < 0) fi = i;
            else if (e.Infantry < 0 && e.GameUnitType == 0 && vi < 0) vi = i;
        }
        // fehlt einer, wird ein fremder uebernommen (Eingriff) — sonst KEIN URTEIL
        int Fremd(bool fuss)
        {
            for (int i = 0; i < _entities.Count; i++)
            {
                var e = _entities[i];
                if (e.IsBuilding || e.IsProp || e.Dead || Untergestellt(e) || !e.Mobile) continue;
                if (fuss ? e.Infantry < 0 : e.Infantry >= 0 || e.GameUnitType != 0) continue;
                sb.AppendLine($"  EINGRIFF: {(fuss ? "Infanterist" : "Fahrzeug")} Platz {e.Slot} (Spieler {e.Owner}) -> Spieler {ViewPlayer}");
                e.Owner = e.Team = ViewPlayer;
                return i;
            }
            return -1;
        }
        if (fi < 0) fi = Fremd(true);
        if (vi < 0) vi = Fremd(false);
        if (fi < 0) return sb.Append("  KEIN URTEIL: kein Infanterist").ToString();
        int besitzer0 = geb.Owner;
        sb.AppendLine($"  Gebaeude Platz {geb.Slot} Art {geb.BType} (Spieler {besitzer0}, TP {geb.Hp}), Tuer ({tuer.X},{tuer.Y}), Einnahmezelle ({front.X},{front.Y})");

        int Lauf(int idx, int takte)
        {
            var u = _entities[idx];
            u.HpMax = u.Hp = 30000;
            u.Path = null; u.Orders.Clear(); u.Target = -1;
            ProbeVersetzen(idx, front.X, front.Y);
            int maxFort = 0;
            for (int t = 0; t < takte; t++)
            {
                if (u.Col != front.X || u.Row != front.Y) { u.Path = null; ProbeVersetzen(idx, front.X, front.Y); }
                SimTickFuerProbe();
                maxFort = Mathf.Max(maxFort, geb.CaptureProgress);
                if (geb.Owner != besitzer0) break;
            }
            // wegstellen
            var weg = _nav!.NearestFree(front + new Vector2I(0, 4), u.Move, idx) ?? front + new Vector2I(0, 4);
            ProbeVersetzen(idx, weg.X, weg.Y);
            return maxFort;
        }
        int ab0 = FussvolkEinnahmeAbgewiesen;
        int fortF = Lauf(fi, 600);
        bool besitzF = geb.Owner == besitzer0;
        sb.AppendLine($"  Infanterist Platz {_entities[fi].Slot}: hoechster Fortschritt {fortF} (Soll 0), Besitzer {besitzer0} -> {geb.Owner} "
                    + $"(Soll unveraendert), vom Tor abgewiesen {FussvolkEinnahmeAbgewiesen - ab0}x");
        bool fussOk = fortF == 0 && besitzF;
        int fortV = -1;
        if (besitzF && vi >= 0)
        {
            for (int t = 0; t < 30; t++) SimTickFuerProbe();      // Fortschritt abklingen lassen
            fortV = Lauf(vi, 600);
            var fv = _entities[vi];
            sb.AppendLine($"  Gegenprobe Fahrzeug Platz {fv.Slot} ({LabelOf(fv)}, Fahrwerk {fv.UnitType}, Belegung der Zelle "
                        + $"{_nav.OccupantAt(front.X, front.Y)}): hoechster Fortschritt {fortV} (Soll > 0)");
        }
        else sb.AppendLine("  Gegenprobe Fahrzeug: entfaellt (" + (vi < 0 ? "kein eigenes Fahrzeug" : "Gebaeude schon eingenommen") + ")");
        bool ok = fussOk && fortV > 0;
        sb.Append(ok ? "  BESTANDEN" : "  DURCHGEFALLEN");
        return sb.ToString();
    }
}
