namespace AkteEuropaReborn.Rendering;

using Godot;

/// <summary>
/// <c>--bahnhoffenster-check</c> (Kampagne 21) — geht den Weg des Spielers mit
/// echten Klicks: Bahnhofsfenster (Fensterart 2) → »Transportsystem« → Karte in
/// Betriebsart 1 → Linie → Fensterart 4 → Befehl 500; dann »Transportieren« →
/// Karte in Betriebsart 5 → Basis → die Einheit fährt mit der Bahn und steht an
/// der Tür; dann »Aussenden« (504) und die Zugfarbe. (22.09.2026,
/// berichte/bahnhof-transport-fable.md §7.)
///
/// <para>⚠ EINGRIFFE: die neutralen Karvina-Gebäude gehören dem Spieler (der
/// Zustand nach ihrer Einnahme), und zwei Fahrzeuge werden über die Einfahrt in
/// Bahnhof 55 gestellt. Nullmodelle: <c>--bahnhoffenster-alt</c> (kein
/// Originalfenster), <c>--verlegung-depot-alt</c> (Einheit landet in der Garage
/// statt an der Tür), <c>--bahnschalter-matrix-alt</c> (Handschalter geht
/// verloren).</para>
/// </summary>
public partial class MapViewer
{
    private bool _bahnhoffensterCheck;

    private async System.Threading.Tasks.Task BahnhoffensterLauf()
    {
        for (int i = 0; i < 5; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        var sb = new System.Text.StringBuilder("bahnhoffenster-check\n");
        bool ok = true;
        void Soll(bool b, string was) { sb.Append($"  {(b ? "ok  " : "⚠ FALSCH")} {was}\n"); ok &= b; }
        void Ende() { sb.Append(ok ? "  BESTANDEN" : "  DURCHGEFALLEN"); GD.Print(sb.ToString()); GetTree().Quit(0); }

        var f = _gebaeudeFenster;
        int gegeben = _entities.K21PlaetzeGeben();
        int bhf = _entities.GebaeudeIndexVonPlatz(55), basis = _entities.GebaeudeIndexVonPlatz(0);
        sb.Append($"  ⚠ EINGRIFF: {gegeben} Karvina-Gebaeude an den Spieler\n");
        if (f == null || bhf < 0 || basis < 0 || _kartenschirm == null)
        { sb.Append("  KEIN URTEIL — ist das K21? (Bahnhof 55 / Basis 0 fehlen)\n"); ok = false; Ende(); return; }
        int drin = _entities.DepotProbeBefuellen(bhf, 2);
        sb.Append($"  ⚠ EINGRIFF: {drin} Fahrzeuge ueber die Einfahrt in Bahnhof 55\n");

        UI.WindowManager.Mausquelle = () => new Vector2(300, 200);
        _entities.PostenAnwaehlenWieKlick(bhf);
        for (int t = 0; t <= UI.WindowManager.BilderAuf + 1; t++) UI.WindowManager.Takt();
        for (int i = 0; i < 3; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

        var bv = f.BahnhofAnsicht;
        var soll = new Vector2(600, 560);
        if (UI.BuildingWindow.BahnhoffensterAlt)
            sb.Append("  ⚠ NULLMODELL --bahnhoffenster-alt: hier MUSS das Originalfenster fehlen\n");
        Soll(f.Visible && f.ZeigtOriginalBahnhof, "Bahnhofsfenster offen, mit den Kacheln des Originals");
        Soll(f.Size == soll, $"Mass {f.Size} = {soll} (300x280 aus 0x45767F/0x457688, Massstab 2)");
        Soll(bv != null && bv.Zeilen == drin && drin == 2, $"Zeilen {bv?.Zeilen} = {drin} Eingefahrene");
        if (bv == null || !f.ZeigtOriginalBahnhof) { Ende(); return; }

        var leinwand = GetViewport().GetVisibleRect().Size;
        var fenstermass = (Vector2)GetWindow().Size;
        var faktor = new Vector2(fenstermass.X / leinwand.X, fenstermass.Y / leinwand.Y);
        async System.Threading.Tasks.Task Klick(Vector2 punkt, MouseButton knopf = MouseButton.Left)
        {
            var p = punkt * faktor;
            GetViewport().PushInput(new InputEventMouseMotion { Position = p, GlobalPosition = p });
            GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = knopf, Pressed = true, Position = p, GlobalPosition = p });
            GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = knopf, Pressed = false, Position = p, GlobalPosition = p });
            for (int i = 0; i < 3; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }

        // ---- 1. Transportsystem -> Karte Betriebsart 1 -> Linie 2 -> Fensterart 4
        // ⚠ Die ANKUNFT mitmessen: wo liegt das Fenster, und wer liegt unter dem Punkt?
        var ziel1 = bv.FeldAufDemSchirm(1);
        GetViewport().PushInput(new InputEventMouseMotion { Position = ziel1 * faktor, GlobalPosition = ziel1 * faktor });
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        var unter = GetViewport().GuiGetHoveredControl();
        sb.Append($"  Fenster {f.GetGlobalRect()}, Ansicht {bv.GetGlobalRect()}, Leinwand {leinwand}, Faktor {faktor}; "
                + $"unter {ziel1}: {(unter == null ? "NICHTS" : unter.GetPath() + " sichtbar " + unter.IsVisibleInTree())}\n");
        await Klick(ziel1);
        Soll(bv.LetzterTreffer == 1, $"Klick auf »Transportsystem« kam an (Treffer {bv.LetzterTreffer})");
        Soll(_kartenschirm.Visible && _kartenschirm.Betriebsart == 1,
             $"Karte offen in Betriebsart {_kartenschirm.Betriebsart} (Soll 1, »{_kartenschirm.TitelText}«)");
        var zellen = _entities.LinienZellen(2);
        if (zellen == null || zellen.Count == 0) { sb.Append("  Linie 2 hat keine Zellen\n"); ok = false; Ende(); return; }
        var mitte = zellen[zellen.Count / 2];
        int mc = Mathf.RoundToInt(mitte.X), mr = Mathf.RoundToInt(mitte.Y);
        int linie = _entities.TransportsystemLinieBei(mc, mr);
        Soll(linie == 2, $"Maus ueber Linie 2 findet Linie {linie} (beide Enden eigen)");
        KartenschirmKlickBahn(mc, mr);
        var tl = _transportlinie;
        Soll(tl != null && tl.Visible && tl.Linie == 2, $"Fensterart 4 offen fuer Linie {tl?.Linie}");
        if (tl == null || !tl.Visible) { Ende(); return; }
        for (int i = 0; i < 2; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (_shotPath.Length > 0)
        {
            string p1 = _shotPath.Replace(".png", "-linie.png");
            GetViewport().GetTexture().GetImage().SavePng(p1);
            sb.Append($"  Bild (Karte + Fensterart 4) nach {p1}\n");
        }
        int befehle = _entities.BahnschalterBefehle;
        await Klick(tl.FeldAufDemSchirm(1, 1));                     // Fahrwerk -> Knoten 2
        var m = _entities.RailModeOf(2);
        Soll(tl.LetzterTreffer == 6 && _entities.BahnschalterBefehle == befehle + 1,
             $"Klick in die Tafel: Treffer {tl.LetzterTreffer} (Soll 6), Befehle 500: {_entities.BahnschalterBefehle - befehle}");
        Soll(m != null && m[1] == 1, $"Linie 2, Fahrwerk: Schalter {(m == null ? "-" : m[1].ToString())} (Soll 1 = zu Knoten 2)");
        for (int t = 0; t < 200; t++) _entities.SimTickFuerProbe();
        m = _entities.RailModeOf(2);
        if (MapEntityLayer.BahnschalterMatrixAlt)
            sb.Append("  ⚠ NULLMODELL --bahnschalter-matrix-alt: hier MUSS der Schalter zurueckfallen\n");
        Soll(m != null && m[1] == 1, $"... und nach 200 Takten noch {(m == null ? "-" : m[1].ToString())} (Handschalter bleibt)");
        tl.Visible = false;
        KartenschirmZu();

        // ---- 2. Transportieren -> Karte Betriebsart 5 -> Basis
        await Klick(bv.FeldAufDemSchirm(3));
        Soll(!_kartenschirm.Visible, "»Transportieren« ohne Markierung: keine Karte (Meldung)");
        await Klick(bv.FeldAufDemSchirm(4));
        Soll(bv.Markiert == 1, $"Zeile 0 markiert ({bv.Markiert})");
        await Klick(bv.FeldAufDemSchirm(3));
        Soll(_kartenschirm.Visible && _kartenschirm.Betriebsart == 5,
             $"Karte offen in Betriebsart {_kartenschirm.Betriebsart} (Soll 5, »{_kartenschirm.TitelText}«)");
        int verlegt = _entities.BahnVerlegtEinheiten;
        var bz = _entities.GebaeudeZelle(basis);
        KartenschirmKlickBahn(bz.X, bz.Y);
        Soll(_entities.BahnVerlegtEinheiten == verlegt + 1,
             $"Klick auf die Basis: {_entities.BahnVerlegtEinheiten - verlegt} Befehl(e) 518 (keine Verbindung: {_entities.BahnKeineVerbindung})");
        Soll(!_kartenschirm.Visible, "Planungskarte danach zu (0x4471A0)");
        Soll(_entities.GarageZahl(bhf) == 1, $"Bahnhof hat noch {_entities.GarageZahl(bhf)} Einheit(en)");

        int an = _entities.BahnAnTuerAngekommen, n = 0;
        while (_entities.BahnAnTuerAngekommen == an && n < 15000) { _entities.SimTickFuerProbe(); n++; }
        if (MapEntityLayer.VerlegungDepotAlt)
            sb.Append("  ⚠ NULLMODELL --verlegung-depot-alt: hier MUSS »an der Tuer« fehlschlagen\n");
        Soll(_entities.BahnAnTuerAngekommen == an + 1,
             $"die Einheit steht nach {n} Takten ({n / 50f:0.0} s) an der Tuer der Basis (Garage Basis {_entities.GarageZahl(basis)})");

        // ---- 3. Aussenden (504) aus dem Bahnhof
        f.Refresh();
        for (int i = 0; i < 2; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await Klick(bv.FeldAufDemSchirm(4));
        int aus = _entities.DepotAussendenBefehle;
        await Klick(bv.FeldAufDemSchirm(2));
        Soll(_entities.DepotAussendenBefehle == aus + 1, "»Aussenden« schickt einen Befehl 504");

        // ---- 4. Zugfarbe
        int farbe = _entities.ZugBesitzerFarbe(2);
        Soll(farbe == _entities.ViewPlayer, $"Linie 2 (Knoten 1 = Bahnhof 55, jetzt eigen): Farbe {farbe}");
        sb.Append($"  {_entities.BahnNeutraleLinienZeile()}\n");

        if (_shotPath.Length > 0)
        {
            GetViewport().GetTexture().GetImage().SavePng(_shotPath);
            sb.Append($"  Bild nach {_shotPath}\n");
        }
        Ende();
    }
}
