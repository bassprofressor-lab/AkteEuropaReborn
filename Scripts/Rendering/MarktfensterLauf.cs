namespace AkteEuropaReborn.Rendering;

using Godot;

/// <summary>
/// <c>--marktfenster-check</c> (K11, 13.09.2026) — das Geschäftszentrum,
/// Fensterart 33, mit echten Mausklicks. Siehe <see cref="UI.MarktView"/> und
/// Simulation/Marktfenster.cs.
///
/// <para>Gemessen: ohne Fahrzeug auf einer Platte geht nichts auf; mit geht
/// Art 33 (Kacheln, 720×520, Ereignis 33) und NICHT das Basisfenster auf;
/// Zeile 0 ist gewählt; Klick auf Zeile 1 wählt sie; »Bestellen« ohne Geld ist
/// STILL (kein Kauf, keine Meldung); mit Geld kauft Befehl 530 (Konto −Preis,
/// verkauft, eine Zeile weniger); Doppelklick kauft; fährt das Fahrzeug weg,
/// schliesst der Takt das Fenster. Nullmodell <c>--marktfenster-alt</c>: kein
/// Art-33-Fenster, das Basisfenster will auf.</para>
///
/// <para>⚠ EINGRIFFE: das Fahrzeug wird auf die Platte VERSETZT und wieder
/// herunter; das Geld wird gesetzt; das Regal wird durch Nachschubtakte
/// gefüllt.</para>
/// </summary>
public partial class MapViewer
{
    private bool _marktfensterCheck;

    private async System.Threading.Tasks.Task MarktfensterLauf()
    {
        for (int i = 0; i < 5; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        var sb = new System.Text.StringBuilder("marktfenster-check\n");
        int fehler = 0;
        void Soll(bool b, string was) { sb.Append(b ? "  ok     " : "  FEHLER ").Append(was).Append('\n'); if (!b) fehler++; }
        bool alt = MapEntityLayer.MarktfensterAlt;
        if (alt) sb.Append("  ⚠ NULLMODELL --marktfenster-alt: Art 33 MUSS ausbleiben\n");

        int idx = _entities.MarktIndex();
        var f = _gebaeudeFenster;
        if (idx < 0 || f == null) { GD.Print(sb.Append("  KEIN URTEIL: kein Geschaeftszentrum")); GetTree().Quit(0); return; }
        int regal = _entities.MarktRegalFuellen();
        sb.Append($"  ⚠ EINGRIFF: Regal durch Nachschubtakte gefuellt, {regal} Angebote\n");
        var platz = _entities.MarktPlatz(idx);

        UI.WindowManager.Mausquelle = () => new Vector2(400, 200);
        async System.Threading.Tasks.Task Warten(int n = 3)
        { for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
        async System.Threading.Tasks.Task Anklicken()
        {
            _entities.MarktAnklicken(idx);
            for (int t = 0; t <= UI.WindowManager.BilderAuf + 1; t++) UI.WindowManager.Takt();
            await Warten();
        }

        // ---- 1. ohne Fahrzeug: nichts -------------------------------------
        _entities.MarktFahrzeugSetzen(idx, false);
        await Anklicken();
        Soll(UI.WindowManager.Offen(33, platz) == null, "ohne Fahrzeug auf einer Platte geht kein Fenster auf (0x432569)");

        // ---- 2. mit Fahrzeug: Art 33 --------------------------------------
        sb.Append("  ⚠ EINGRIFF: " + _entities.MarktFahrzeugSetzen(idx, true) + "\n");
        Soll(_entities.MarktFahrzeugDa(idx), "Fahrzeug steht auf der Platte (0,0)");
        int ereignisse = UI.WindowManager.EreignisGesetzt;
        await Anklicken();
        var mv = f.MarktAnsicht;
        if (alt)
        {
            Soll(UI.WindowManager.Offen(33, platz) == null && !f.ZeigtOriginalMarkt, "kein Art-33-Fenster (Nullmodell)");
            Soll(_entities.BuildPanelWanted, "das Basisfenster will auf (Nullmodell)");
            GD.Print(sb.Append(fehler == 0 ? "marktfenster-check: IN ORDNUNG" : $"marktfenster-check: {fehler} FEHLER"));
            GetTree().Quit(0);
            return;
        }
        var soll = new Vector2(UI.MarktView.WTiles * UI.WindowChrome.Cell * UI.MarktView.Scale,
                               UI.MarktView.HTiles * UI.WindowChrome.Cell * UI.MarktView.Scale);
        Soll(UI.WindowManager.Offen(33, platz) != null && f.Visible && f.ZeigtOriginalMarkt,
             "Fensterart 33 offen, mit den Kacheln des Originals");
        Soll(f.Size == soll, $"Mass {f.Size} = {soll} (360x260 aus 0x45B05A/0x45B063, Massstab 2)");
        Soll(UI.WindowManager.EreignisGesetzt > ereignisse && UI.WindowManager.EreignisZuletzt == 33,
             $"Ereignisbyte 33 (zuletzt {UI.WindowManager.EreignisZuletzt}) — Mission 11 Regel 1");
        Soll(!_entities.BuildPanelWanted, "das Basisfenster bleibt zu");
        if (mv == null) { GD.Print(sb.Append("marktfenster-check: FEHLER (keine Ansicht)")); GetTree().Quit(0); return; }
        Soll(mv.Anzahl == _entities.MarktRegal().Count, $"Zeilen {mv.Anzahl} = Regal {_entities.MarktRegal().Count}");
        Soll(mv.Auswahl == 1000 && mv.Regalplatz == _entities.MarktRegal()[0].Nr,
             $"beim Oeffnen ist Zeile 0 gewaehlt (Auswahl {mv.Auswahl}, Regalplatz {mv.Regalplatz})");

        var leinwand = GetViewport().GetVisibleRect().Size;
        var fenstermass = (Vector2)GetWindow().Size;
        var faktor = new Vector2(fenstermass.X / leinwand.X, fenstermass.Y / leinwand.Y);
        async System.Threading.Tasks.Task Klick(Vector2 punkt, bool doppel = false)
        {
            var p = punkt * faktor;
            GetViewport().PushInput(new InputEventMouseMotion { Position = p, GlobalPosition = p });
            GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = p, GlobalPosition = p });
            GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = p, GlobalPosition = p });
            if (doppel)
                GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, DoubleClick = true, Position = p, GlobalPosition = p });
            await Warten();
        }
        Vector2 Mitte(Rect2 r) => r.Position + r.Size / 2f;

        // ---- 3. Zeile 1 waehlen -------------------------------------------
        if (mv.Anzahl >= 2)
        {
            int klang = UI.WindowManager.ElementklangGespielt;
            await Klick(Mitte(mv.FeldAufDemSchirm(1001)));
            Soll(mv.LetzterTreffer == 1001 && mv.Auswahl == 1001 && mv.Regalplatz == _entities.MarktRegal()[1].Nr,
                 $"Klick auf Zeile 1: Treffer {mv.LetzterTreffer}, Auswahl {mv.Auswahl}");
            Soll(UI.WindowManager.ElementklangGespielt == klang + 1, "Klang 306 einmal");
        }
        if (_shotPath.Length > 0)
        {
            GetViewport().GetTexture().GetImage().SavePng(_shotPath);
            sb.Append($"  Bild nach {_shotPath}\n");
        }

        // ---- 4. ohne Geld: still ------------------------------------------
        var gewaehlt = _entities.MarktRegal()[mv.Auswahl - 1000];
        int preis = gewaehlt.Price;
        _entities.MarktGeld = preis - 1;
        int still = _entities.MarktKaufStill, zeilen = mv.Anzahl, meldungen = UI.WindowManager.Anzahl;
        await Klick(Mitte(mv.FeldAufDemSchirm(1)));
        for (int t = 0; t < 4; t++) _entities.SimTickFuerProbe();
        await Warten();
        Soll(mv.LetzterTreffer == 1 && mv.Bestellungen >= 1, "»Bestellen« getroffen, Befehl 530 abgesetzt");
        Soll(!gewaehlt.Sold && _entities.MarktGeld == preis - 1 && _entities.MarktKaufStill == still + 1,
             $"zu wenig Geld: nichts gekauft, still (Konto {_entities.MarktGeld}, still {_entities.MarktKaufStill - still})");
        Soll(UI.WindowManager.Anzahl == meldungen, "kein Meldungsfenster");

        // ---- 5. mit Geld: gekauft -----------------------------------------
        _entities.MarktGeld = preis + 1000;
        int kaeufe = _entities.MarktKaeufe;
        await Klick(Mitte(mv.FeldAufDemSchirm(1)));
        for (int t = 0; t < 4; t++) _entities.SimTickFuerProbe();
        await Warten();
        Soll(gewaehlt.Sold && _entities.MarktGeld == 1000 && _entities.MarktKaeufe == kaeufe + 1,
             $"gekauft: verkauft {gewaehlt.Sold}, Konto {_entities.MarktGeld} (1000)");
        Soll(mv.Anzahl == zeilen - 1, $"das Fenster ist neu gemalt: {mv.Anzahl} Zeilen ({zeilen - 1})");

        // ---- 6. Doppelklick kauft -----------------------------------------
        if (mv.Anzahl >= 1)
        {
            var erste = _entities.MarktRegal()[0];
            _entities.MarktGeld = erste.Price + 5;
            kaeufe = _entities.MarktKaeufe;
            await Klick(Mitte(mv.FeldAufDemSchirm(1000)), doppel: true);
            for (int t = 0; t < 4; t++) _entities.SimTickFuerProbe();
            await Warten();
            Soll(erste.Sold && _entities.MarktKaeufe == kaeufe + 1, "Doppelklick auf Zeile 0 = Bestellen");
        }

        // ---- 7. Fahrzeug weg: der Takt schliesst --------------------------
        sb.Append("  ⚠ EINGRIFF: " + _entities.MarktFahrzeugSetzen(idx, false) + "\n");
        for (int t = 0; t < 3; t++) _entities.SimTickFuerProbe();
        for (int t = 0; t <= UI.WindowManager.BilderZu + 1; t++) UI.WindowManager.Takt();
        await Warten();
        Soll(UI.WindowManager.Offen(33, platz) == null && !f.Visible && _entities.MarktfensterTaktZu > 0,
             $"ohne Fahrzeug schliesst der Takt das Fenster (0x43E90C, {_entities.MarktfensterTaktZu}x)");

        GD.Print(sb.Append(fehler == 0 ? "marktfenster-check: IN ORDNUNG" : $"marktfenster-check: {fehler} FEHLER"));
        GetTree().Quit(0);
    }
}
