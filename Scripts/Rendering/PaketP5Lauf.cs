namespace AkteEuropaReborn.Rendering;

using System.Collections.Generic;
using Godot;

/// <summary>
/// ⭐ 29.10.2026 — <b>Paket P5 Tueren der Testerrunde 0.6.12</b> (bug-648, bug-657, bug-660,
/// Fremdsoldat; <c>berichte/bau-p5-0-6-12.md</c>): Schalter und Pruefstaende an einer Stelle.
/// Simulation/TuerTrupp.cs traegt den Bau.
/// </summary>
public partial class MapViewer
{
    private bool _p5Trupp, _p5Verteil;

    /// <summary>Die Schalter von P5; true = verbraucht.</summary>
    private bool P5Schalter(string a)
    {
        switch (a)
        {
            case "--tuer-trupp-alt": MapEntityLayer.TuerTruppAlt = true; return true;              // bug-648
            case "--tuer-fremdsoldat-alt": MapEntityLayer.TuerFremdsoldatAlt = true; return true;  // Entscheidung 9
            case "--einnahme-ein-ziel-alt": MapEntityLayer.EinnahmeEinZielAlt = true; return true; // bug-660
            case "--vorplatz-stempel-alt": MapEntityLayer.VorplatzStempelAlt = true; return true;  // bug-660
            case "--torlog": MapEntityLayer.Torlog = true; return true;                            // bug-657
            case "--tuer-trupp-check": _p5Trupp = true; return true;
            case "--einnahme-verteil-check": _p5Verteil = true; return true;
        }
        return false;
    }

    /// <summary>Die synchronen Pruefstaende von P5; true = einer lief (und beendet).</summary>
    private bool P5Pruefstaende()
    {
        string? zeile = null;
        if (_p5Trupp) zeile = _entities.TuerTruppCheck();
        else if (_p5Verteil) zeile = _entities.EinnahmeVerteilCheck();
        if (zeile == null) return false;
        GD.Print(zeile);
        GetTree().Quit(0);
        return true;
    }
}

public partial class MapEntityLayer
{
    /// <summary>
    /// <b>--tuer-trupp-check</b> (bug-648 K6 + Fremdsoldat). Teil A: drei Soldaten des
    /// Basisbesitzers (bevorzugt die Forscher, Marke 193) stehen ruhend auf Tuer 0 einer
    /// Basis; 20 Takte. Soll: 3/3 in der Garage, im selben Takt, Tuerzelle leer.
    /// Nullmodell <c>--tuer-trupp-alt</c>: 1/3 (nur der Stempeltraeger, nach dem Torzyklus),
    /// 2 bleiben stehen, Zelle ungestempelt. Teil B: ein FREMDER Soldat auf derselben Tuer,
    /// 3 Takte. Soll: geloescht (tot, verbraucht, keine Leiche), eine Flamme, Treffer 50 auf
    /// die Zelle links. Nullmodell <c>--tuer-fremdsoldat-alt</c>: er steht weiter da.
    /// Teil C (wenn ein Depot da ist): fremder Soldat auf Depottuer 0 — geloescht, OHNE Flamme.
    /// <para>⚠ EINGRIFFE: Lage der Probanden, UKOL 0; fehlen Soldaten, werden fremde
    /// uebernommen (Besitzer umgeschrieben); die Garage der Basis wird vorher geleert,
    /// wenn sie keine drei Plaetze frei hat.</para>
    /// </summary>
    public string TuerTruppCheck()
    {
        int m = UI.SkirmishSetup.CampaignMission;
        var sb = new System.Text.StringBuilder($"tuer-trupp-check K{m}"
            + (TuerTruppAlt ? " — NULLMODELL --tuer-trupp-alt" : "")
            + (TuerFremdsoldatAlt ? " — NULLMODELL --tuer-fremdsoldat-alt" : "") + "\n");
        if (_nav == null) return sb.Append("  KEIN URTEIL: keine Karte").ToString();
        int bi = -1;
        for (int pass = 0; pass < 2 && bi < 0; pass++)
            for (int i = 0; i < _entities.Count; i++)
            {
                var b = _entities[i];
                if (!b.IsBuilding || b.Dead || b.BType != 1 || b.Built == 0 || b.DoorCells.Count == 0) continue;
                if (pass == 0 && b.Owner != ViewPlayer) continue;
                bi = i; break;
            }
        if (bi < 0) return sb.Append("  KEIN URTEIL: keine Basis mit Tuer").ToString();
        var basis = _entities[bi];
        int p = basis.Owner;
        int tc = basis.Col + basis.DoorCells[0].Col, tr = basis.Row + basis.DoorCells[0].Row;
        if (GarageBelegt(basis) > DepotSlots - 3)
        {
            sb.AppendLine($"  EINGRIFF: Garage der Basis geleert ({basis.Garage.Count} Saetze, {basis.Depot.Count} Zeilen)");
            basis.Garage.Clear(); basis.Depot.Clear();
        }
        sb.AppendLine($"  Basis Platz {basis.Slot} (Spieler {p}, TP {basis.Hp}), Tuer 0 ({tc},{tr}), Garage {GarageBelegt(basis)}/{DepotSlots}");

        // --- die drei Probanden ---
        var probe = new List<int>();
        void Nimm(System.Func<Entity, bool> pass)
        {
            for (int i = 0; i < _entities.Count && probe.Count < 3; i++)
            {
                var e = _entities[i];
                if (e.IsBuilding || e.IsProp || e.Dead || e.Infantry < 0 || Untergestellt(e) || probe.Contains(i)) continue;
                if (pass(e)) probe.Add(i);
            }
        }
        Nimm(e => e.Mark == 193);
        Nimm(e => e.Owner == p);
        Nimm(e => true);
        if (probe.Count < 3) return sb.Append($"  KEIN URTEIL: nur {probe.Count} Soldaten auf der Karte").ToString();
        foreach (int i in probe)
        {
            var e = _entities[i];
            if (e.Owner != p) { sb.AppendLine($"  EINGRIFF: Soldat Platz {e.Slot} (Marke {e.Mark}) Spieler {e.Owner} -> {p}"); e.Owner = e.Team = p; }
            e.Path = null; e.Orders.Clear(); e.Target = -1; e.Ukol = UkolFrei;
            ProbeVersetzen(i, tc, tr);
        }
        int stempel0 = _nav.OccupantAt(tc, tr);
        sb.AppendLine($"  3 Soldaten (Plaetze {string.Join(",", probe.ConvertAll(i => _entities[i].Slot))}) auf die Tuer gestellt, Stempel {stempel0}");
        // ⚠ Gemessen wird die ANKUNFT in der Garage je Takt (hoechster Stand), nicht der
        // Endstand: eine KI-Basis schickt Eingefahrene wieder hinaus (AiEmptyDepots).
        int drinTakt = -1, ein0 = Eingefahren, maxDrin = 0, ersterTakt = -1, draussenNach1 = -1;
        for (int t = 1; t <= 20; t++)
        {
            foreach (int i in probe) { var e = _entities[i]; if (e.InGebaeude == null) { e.Path = null; e.Orders.Clear(); } }
            SimTickFuerProbe();
            int d = probe.FindAll(i => basis.Garage.Contains(_entities[i])).Count;
            if (t == 1) draussenNach1 = probe.FindAll(i => !_entities[i].Dead && _entities[i].InGebaeude == null && _entities[i].Col == tc && _entities[i].Row == tr).Count;
            if (d > 0 && ersterTakt < 0) ersterTakt = t;
            if (d == 3 && drinTakt < 0) drinTakt = t;
            maxDrin = System.Math.Max(maxDrin, d);
        }
        int drin = probe.FindAll(i => basis.Garage.Contains(_entities[i])).Count;
        int ausgesandt = probe.FindAll(i => _entities[i].Ukol == UkolVerlaesst || (_entities[i].InGebaeude == null && !(_entities[i].Col == tc && _entities[i].Row == tr))).Count;
        int stempel = _nav.OccupantAt(tc, tr);
        sb.AppendLine($"  A eigene: hoechstens {maxDrin}/3 zugleich in der Garage (Soll 3), erster drin Takt {ersterTakt}, alle drin ab Takt {drinTakt} (Soll 1), "
                    + $"nach Takt 1 noch auf der Tuer {draussenNach1} (Soll 0); Endstand {drin}/3 drin, {ausgesandt} wieder ausgesandt/weg "
                    + $"(KI-Basis: AiEmptyDepots), Stempel der Tuerzelle {stempel}, Einfahrten {Eingefahren - ein0}, davon Infanteriearm {TuerTruppEingefahren}");
        bool okA = maxDrin == 3 && drinTakt == 1 && draussenNach1 == 0;

        // --- B: ein fremder Soldat ---
        int fi = -1;
        for (int i = 0; i < _entities.Count; i++)
        {
            var e = _entities[i];
            if (e.IsBuilding || e.IsProp || e.Dead || e.Infantry < 0 || Untergestellt(e) || probe.Contains(i) || e.Owner == p) continue;
            fi = i; break;
        }
        if (fi < 0)
            for (int i = 0; i < _entities.Count; i++)
            {
                var e = _entities[i];
                if (e.IsBuilding || e.IsProp || e.Dead || e.Infantry < 0 || Untergestellt(e) || probe.Contains(i)) continue;
                int neu = p == 0 ? 1 : 0;
                sb.AppendLine($"  EINGRIFF: Soldat Platz {e.Slot} Spieler {e.Owner} -> {neu} (Fremder)");
                e.Owner = e.Team = neu; fi = i; break;
            }
        bool okB = false;
        if (fi < 0) sb.AppendLine("  B fremder: KEIN Soldat mehr — entfaellt");
        else
        {
            var f = _entities[fi];
            f.Path = null; f.Orders.Clear(); f.Target = -1; f.Ukol = UkolFrei;
            ProbeVersetzen(fi, tc, tr);
            int links = GebaeudeAufZelle(tc - 1, tr);
            int hpLinks = links >= 0 ? _entities[links].Hp : -1;
            int fl0 = TuerFremdFlammen, fx0 = _effects.Count;
            for (int t = 0; t < 3; t++) SimTickFuerProbe();
            int hpNach = links >= 0 ? _entities[links].Hp : -1;
            sb.AppendLine($"  B fremder Soldat Platz {f.Slot} (Spieler {f.Owner}) auf der Basistuer: tot {f.Dead} verbraucht {f.Verbraucht} "
                        + $"(Soll beide True), Flammen {TuerFremdFlammen - fl0} (Soll 1), Zelle links ({tc - 1},{tr}) = "
                        + (links >= 0 ? $"{BuildingTypeName(_entities[links].BType)} Platz {_entities[links].Slot}, TP {hpLinks} -> {hpNach}" : "kein Gebaeude")
                        + $", Effekte +{_effects.Count - fx0}");
            okB = f.Dead && f.Verbraucht && TuerFremdFlammen - fl0 == 1 && (links < 0 || hpNach < hpLinks);
        }

        // --- C: Depot ---
        string c = "  C Depot: keines auf der Karte — entfaellt";
        bool okC = true;
        int di = -1;
        for (int i = 0; i < _entities.Count; i++)
        {
            var b = _entities[i];
            if (b.IsBuilding && !b.Dead && b.BType == 5 && b.Built > 0 && b.DoorCells.Count > 0) { di = i; break; }
        }
        int artMerk = -1;
        if (di < 0 && fi >= 0)
        {
            // ⚠ EINGRIFF (nachgestellte Lage): kein Depot auf der Karte — die Basis spielt fuer
            // Teil C ein Depot (Art 5), danach zurueck.
            di = bi; artMerk = basis.BType; basis.BType = 5;
            sb.AppendLine("  EINGRIFF: kein Depot auf der Karte — die Basis wird fuer Teil C als Depot (Art 5) gefuehrt");
        }
        if (di >= 0)
        {
            var dep = _entities[di];
            int qc = dep.Col + dep.DoorCells[0].Col, qr = dep.Row + dep.DoorCells[0].Row;
            int gi = -1;
            for (int i = 0; i < _entities.Count; i++)
            {
                var e = _entities[i];
                if (e.IsBuilding || e.IsProp || e.Dead || e.Infantry < 0 || Untergestellt(e) || probe.Contains(i)) continue;
                gi = i; break;
            }
            if (gi >= 0)
            {
                var g = _entities[gi];
                if (g.Owner == dep.Owner) { int neu = dep.Owner == 0 ? 1 : 0; sb.AppendLine($"  EINGRIFF: Soldat Platz {g.Slot} Spieler {g.Owner} -> {neu}"); g.Owner = g.Team = neu; }
                g.Path = null; g.Orders.Clear(); g.Target = -1; g.Ukol = UkolFrei;
                ProbeVersetzen(gi, qc, qr);
                int fl0 = TuerFremdFlammen;
                for (int t = 0; t < 3; t++) SimTickFuerProbe();
                okC = g.Dead && g.Verbraucht && TuerFremdFlammen == fl0;
                c = $"  C fremder Soldat Platz {g.Slot} auf Depottuer ({qc},{qr}) (Depot Spieler {dep.Owner}): tot {g.Dead} verbraucht {g.Verbraucht} (Soll True), "
                  + $"Flammen {TuerFremdFlammen - fl0} (Soll 0)";
            }
        }
        if (artMerk >= 0) basis.BType = artMerk;
        sb.AppendLine(c);
        sb.Append(okA && okB && okC ? "  BESTANDEN" : "  DURCHGEFALLEN");
        return sb.ToString();
    }

    /// <summary>
    /// <b>--einnahme-verteil-check</b> (bug-660 K18). Teil A: drei eigene Fahrzeuge waehlen und
    /// den Einnahmebefehl auf ein fremdes einnehmbares Gebaeude geben (<see cref="PostCapture"/>),
    /// 5 Takte. Soll: drei VERSCHIEDENE Ziele, keines auf einer Tuer, eines = Einnahmezelle.
    /// Nullmodell <c>--einnahme-ein-ziel-alt</c>: alle drei dasselbe Ziel. Teil B: Vorplatz —
    /// Zelle 1: Fahrzeug V1; Zelle 2: Fahrzeug V2, darauf ein Soldat (Stempel = Soldat);
    /// <see cref="VorplatzRaeumen"/>. Soll: V1 und V2 losgeschickt, der Soldat nicht.
    /// Nullmodell <c>--vorplatz-stempel-alt</c>: V2 bleibt, der Soldat wird geschickt.
    /// <para>⚠ EINGRIFFE: Auswahl, Lage, Besitzer bei Bedarf.</para>
    /// </summary>
    public string EinnahmeVerteilCheck()
    {
        var sb = new System.Text.StringBuilder("einnahme-verteil-check"
            + (EinnahmeEinZielAlt ? " — NULLMODELL --einnahme-ein-ziel-alt" : "")
            + (VorplatzStempelAlt ? " — NULLMODELL --vorplatz-stempel-alt" : "") + "\n");
        if (_nav == null) return sb.Append("  KEIN URTEIL: keine Karte").ToString();
        int bi = -1;
        for (int i = 0; i < _entities.Count; i++)
        {
            var b = _entities[i];
            if (!Capturable(b) || b.Owner == ViewPlayer || b.DoorCells.Count == 0) continue;
            bi = i; break;
        }
        if (bi < 0) return sb.Append("  KEIN URTEIL: kein fremdes einnehmbares Gebaeude").ToString();
        var geb = _entities[bi];
        var zelle = new List<Vector2I>(CaptureWatchCells(geb))[0];
        var fz = new List<int>();
        for (int pass = 0; pass < 2 && fz.Count < 3; pass++)
            for (int i = 0; i < _entities.Count && fz.Count < 3; i++)
            {
                var e = _entities[i];
                if (e.IsBuilding || e.IsProp || e.Dead || e.Infantry >= 0 || !e.Mobile || e.GameUnitType != 0 || Untergestellt(e) || fz.Contains(i)) continue;
                if (pass == 0 && e.Owner != ViewPlayer) continue;
                if (e.Owner != ViewPlayer) { sb.AppendLine($"  EINGRIFF: Fahrzeug Platz {e.Slot} Spieler {e.Owner} -> {ViewPlayer}"); e.Owner = e.Team = ViewPlayer; }
                fz.Add(i);
            }
        if (fz.Count < 3) return sb.Append("  KEIN URTEIL: keine drei Fahrzeuge").ToString();
        sb.AppendLine($"  Gebaeude {BuildingTypeName(geb.BType)} Platz {geb.Slot} (Spieler {geb.Owner}), Einnahmezelle ({zelle.X},{zelle.Y})");
        _sel.Clear();
        foreach (int i in fz) { _sel.Add(i); var e = _entities[i]; e.Path = null; e.Orders.Clear(); e.Target = -1; }
        // Klickpunkt: eine Zelle, auf der Pick wirklich dieses Gebaeude findet.
        // ⚠ EINGRIFF: der Nebel wird fuer den Klick ausgeblendet (PickOhneNebel).
        bool nebelMerk = PickOhneNebel; PickOhneNebel = true;
        Vector2 klick = geb.Pos;
        for (int dy = -1; dy <= 5 && Pick(klick) != bi; dy++)
            for (int dx = -1; dx <= 5; dx++)
                if (Pick(CellCenter(geb.Col + dx, geb.Row + dy)) == bi) { klick = CellCenter(geb.Col + dx, geb.Row + dy); break; }
        if (Pick(klick) != bi) { PickOhneNebel = nebelMerk; return sb.Append("  KEIN URTEIL: kein Klickpunkt trifft das Gebaeude").ToString(); }
        bool gesetzt = PostCapture(klick);
        PickOhneNebel = nebelMerk;
        for (int t = 0; t < 5; t++) SimTickFuerProbe();
        var ziele = new List<Vector2I>();
        foreach (int i in fz) ziele.Add(_entities[i].Goal);
        bool aufTuer = false;
        foreach (var z in ziele)
            foreach (var (dc, dr) in geb.DoorCells) if (geb.Col + dc == z.X && geb.Row + dr == z.Y) aufTuer = true;
        int verschieden = new HashSet<Vector2I>(ziele).Count;
        sb.AppendLine($"  A Einnahmebefehl ({(gesetzt ? "gesetzt" : "NICHT gesetzt")}): Ziele {string.Join(" ", ziele.ConvertAll(z => $"({z.X},{z.Y})"))}, "
                    + $"verschieden {verschieden} (Soll 3), Einnahmezelle dabei {ziele.Contains(zelle)} (Soll True), auf einer Tuer {aufTuer} (Soll False), "
                    + $"verteilt {EinnehmerVerteilt}");
        bool okA = gesetzt && verschieden == 3 && ziele.Contains(zelle) && !aufTuer;

        // --- B: Vorplatz ---
        int tx = geb.Col + geb.DoorCol, ty = geb.Row + geb.DoorRow;
        var z1 = new Vector2I(tx - 1, ty + 2); var z2 = new Vector2I(tx + 1, ty + 2);
        int si = -1;
        for (int i = 0; i < _entities.Count; i++)
        {
            var e = _entities[i];
            if (e.IsBuilding || e.IsProp || e.Dead || e.Infantry < 0 || Untergestellt(e)) continue;
            si = i; break;
        }
        bool okB = false;
        if (si < 0) sb.AppendLine("  B Vorplatz: kein Soldat — entfaellt");
        else
        {
            var s = _entities[si];
            if (s.Owner != ViewPlayer) { sb.AppendLine($"  EINGRIFF: Soldat Platz {s.Slot} Spieler {s.Owner} -> {ViewPlayer}"); s.Owner = s.Team = ViewPlayer; }
            int v1 = fz[0], v2 = fz[1];
            foreach (int i in new[] { v1, v2, si }) { var e = _entities[i]; e.Path = null; e.Orders.Clear(); e.Target = -1; e.Ukol = UkolFrei; }
            ProbeVersetzen(v1, z1.X, z1.Y);
            ProbeVersetzen(v2, z2.X, z2.Y);
            ProbeVersetzen(si, z2.X, z2.Y);          // Stempel = Soldat
            VorplatzRaeumen(geb, ViewPlayer);
            bool w1 = _entities[v1].Path is { Count: > 0 }, w2 = _entities[v2].Path is { Count: > 0 }, ws = s.Path is { Count: > 0 };
            sb.AppendLine($"  B Vorplatz: Stempel ({z2.X},{z2.Y}) = {_nav.OccupantAt(z2.X, z2.Y)} (Soldat {si}); losgeschickt V1 {w1} (Soll True), "
                        + $"V2 {w2} (Soll True), Soldat {ws} (Soll False); gefunden {VorplatzGefunden}, geschickt {VorplatzGeschickt}");
            okB = w1 && w2 && !ws;
        }
        sb.Append(okA && okB ? "  BESTANDEN" : "  DURCHGEFALLEN");
        return sb.ToString();
    }
}
