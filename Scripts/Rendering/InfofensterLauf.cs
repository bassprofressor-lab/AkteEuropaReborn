namespace AkteEuropaReborn.Rendering;

using System.Text;
using System.Text.RegularExpressions;
using Godot;

/// <summary>
/// ⭐⭐ <b>PAKET 2 INFOFENSTER — Schalter, Lokator-Verdrahtung und Prüfstand</b>
/// (04.10.2026, bug-420..424, <c>berichte/infofenster-fable.md</c>).
///
/// <para><b><c>--infofenster-check</c></b> (K1, kopflos; mit <c>--shot=…</c> im Fenster auch
/// Bilder). Fünf Teile, jeder mit seinem Gegenschalter als Nullmodell — unter dem
/// Gegenschalter MUSS sein Teil durchfallen:</para>
/// <list type="number">
/// <item><b>Name</b> (bug-421): Platz 0 gewählt → Streifen = Rangzeichen + »Panzer«.
///   Nullmodell <c>--panelname-alt</c> → »SCHWERE BORDKANONE«.</item>
/// <item><b>Leerzweig</b> (bug-422): ohne Auswahl sechs Zeilen an y 45/61/74/87/100/113, alle
///   im Feld (Unterkante ≤ 137), Zahlen gleich der zweiten Rechnung (alter Fließtext);
///   GEBÄUDE gewählt → dieselben Zeilen. Nullmodelle <c>--leerzweig-alt</c>,
///   <c>--gebaeudetext-im-block</c>.</item>
/// <item><b>Schrift</b> (bug-423): Höhe und Breite bei 26 = 2 × bei 13, für die Blockschrift
///   UND die Fensterschrift. Nullmodell <c>--schrift-unskaliert</c>.</item>
/// <item><b>Lokator</b> (bug-424): Kachelfenster 560×280, Treffertest wie <c>0x45FC4B</c>, und
///   ECHTE Klicks (PushInput): Knopf ohne Zeile tut nichts, Zeile 0 + leerer Name + »Sichern«
///   → <c>OnSave(0, "NONAME")</c>, Fenster zu. Nullmodell <c>--lokatorfenster-alt</c>.</item>
/// <item><b>Bild</b> (bug-420, nur im Fenster): an einem Punkt, an dem Fahrwerks- UND
///   Aufsatzbild durchsichtig sind, MUSS PANEL.DTA (55,47,47) zu sehen sein. Nullmodell
///   <c>--bildmulde-alt</c> → (19,19,15).</item>
/// </list>
/// </summary>
public partial class MapViewer
{
    /// <summary><c>--bildmulde-alt</c> — die 0x2F-Mulde unter dem Einheitenbild im Block, wie bis
    /// zum 04.10.2026. Siehe <c>UI.PortraitBank.DrawPictures</c>.</summary>
    public static bool BildmuldeAlt;

    private bool _infofensterCheck;
    private UI.LokatorChrome? _lokatorChrome;
    private CanvasLayer? _lokatorChromeLayer;

    private bool InfofensterSchalter(string a)
    {
        switch (a)
        {
            case "--infofenster-check": _infofensterCheck = true; return true;
            case "--bildmulde-alt": BildmuldeAlt = true; return true;
            case "--panelname-alt": MapEntityLayer.PanelnameAlt = true; return true;
            case "--leerzweig-alt": MapEntityLayer.LeerzweigAlt = true; return true;
            case "--gebaeudetext-im-block": MapEntityLayer.GebaeudetextImBlock = true; return true;
            case "--aufbauteil-alt": MapEntityLayer.AufbauteilAlt = true; return true;
            case "--lokatorfenster-alt": UI.LokatorChrome.Alt = true; return true;
            // gelesen in UI.WindowChrome.SchriftUnskaliert (vor ParseCmdline gebraucht)
            case "--schrift-unskaliert": return true;
        }
        return false;
    }

    private bool InfofensterPruefstand()
    {
        if (!_infofensterCheck) return false;
        _ = InfofensterLauf();
        return true;
    }

    /// <summary>bug-424 — den Kachel-Lokator aufmachen (siehe <see cref="ZeigeLokator"/>).</summary>
    private void LokatorChromeZeigen(int zeile)
    {
        if (_lokatorChrome == null)
        {
            _lokatorChromeLayer = new CanvasLayer { Layer = 94 };
            AddChild(_lokatorChromeLayer);
            _lokatorChrome = new UI.LokatorChrome();
            _lokatorChromeLayer.AddChild(_lokatorChrome);
            _lokatorChrome.Rows = () =>
            {
                var l = new System.Collections.Generic.List<(string, bool)>();
                foreach (var m in _entities.Marks) l.Add((m.Name, !m.Leer));
                return l;
            };
            _lokatorChrome.OnLocate = i =>
            {
                var z = _entities.MarkTarget(i);
                if (z == null) return;
                _camera.Position = _entities.RailCellPoint(z.Value.X, z.Value.Y);
                ClampCamera();
            };
            _lokatorChrome.OnSave = (i, name) =>
            {
                // ⚠ Die MITTE des Ausschnitts, nicht die Mausstelle (0x438BD0).
                var mitte = _entities.CellAt(_camera.Position);
                if (mitte == null) return;
                GD.Print(_entities.SetMark(i, mitte.Value, name));
            };
        }
        _lokatorChrome.Open(zeile);
    }

    private async System.Threading.Tasks.Task Bilder(int n)
    {
        for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private bool MitBild => DisplayServer.GetName() != "headless";

    /// <summary>Ein Bild — ⚠ erst nach drei Bildern: die Sichtfeldtextur traegt den zuletzt
    /// FERTIGEN Rahmen, ein sofortiger Schuss zeigte den Zustand VOR der Anwahl (04.10.).</summary>
    private async System.Threading.Tasks.Task Schuss(StringBuilder sb, string name)
    {
        if (_shotPath.Length == 0 || !MitBild) return;
        await Bilder(3);
        string pfad = _shotPath.Replace(".png", $"_{name}.png");
        GetViewport().GetTexture().GetImage().SavePng(pfad);
        sb.Append($"  Bild {name} -> {pfad}\n");
    }

    private async System.Threading.Tasks.Task InfofensterLauf()
    {
        await Bilder(5);
        // ⚠ EINGRIFF: alle Hilfetexte als gesehen — sonst deckt die Kontexthilfe den Block zu.
        for (int i = 0; i < 400; i++) UI.HelpWindow.MerkeGezeigt(i);
        var sb = new StringBuilder("infofenster-check (bug-420..424)\n");
        sb.Append("  Schalter: "
                + $"{(MapEntityLayer.PanelnameAlt ? "--panelname-alt " : "")}"
                + $"{(MapEntityLayer.LeerzweigAlt ? "--leerzweig-alt " : "")}"
                + $"{(MapEntityLayer.GebaeudetextImBlock ? "--gebaeudetext-im-block " : "")}"
                + $"{(UI.WindowChrome.SchriftUnskaliert ? "--schrift-unskaliert " : "")}"
                + $"{(UI.LokatorChrome.Alt ? "--lokatorfenster-alt " : "")}"
                + $"{(BildmuldeAlt ? "--bildmulde-alt " : "")}keine weiteren\n");

        // ---- 1. der Name -----------------------------------------------------
        // K1: Platz 0, der »Panzer« der Meldung. Anderswo die erste eigene Einheit.
        bool k1 = UI.SkirmishSetup.CampaignMission == 1;
        int i0 = k1 ? _entities.IndexOfSlot(0) : _entities.ErsteEigeneEinheit();
        _entities.BlockAuswahl(i0);
        var st = _entities.BlockStand();
        string streifen = st.Streifen.Split('\n')[0];
        string soll = _entities.EntwurfsNameVon(i0);
        bool rangDa = streifen.Length == soll.Length + 1;
        bool nameOk = i0 >= 0 && soll.Length > 0 && streifen.EndsWith(soll)
                      && streifen.Length <= soll.Length + 1 && (!k1 || soll == "Panzer");
        sb.Append($"  1 Name: {_entities.EinheitKurz(i0)}\n"
                + $"    Streifen »{streifen}« ({streifen.Length} Zeichen, Rangzeichen "
                + $"{(rangDa ? "davor" : "FEHLT")}), sichtbare Zeilen {st.SichtbareZeilen}, "
                + $"Zeile 2 {(st.Zeilen.Count > 0 ? st.Zeilen[0] : "keine")}: "
                + $"{(nameOk ? "ok" : $"FALSCH (erwartet Rangzeichen + »{soll}«)")}\n");
        await Schuss(sb, "einheit");

        // ---- 5. das Bild (nur im Fenster) ------------------------------------
        string bildZeile;
        bool bildOk = true;
        if (!MitBild) bildZeile = "nicht gemessen (kopflos)";
        else
        {
            await Bilder(3);
            (bildOk, bildZeile) = BildgrundMessen();
        }
        sb.Append($"  5 Bild: {bildZeile}\n");

        // ---- 2. der Leerzweig ------------------------------------------------
        _entities.BlockAuswahl(-1);
        var leer = _entities.BlockStand();
        var alt = _entities.MissionSummaryAlt().Split('\n');
        bool leerOk = LeerzweigPruefen(sb, "ohne Auswahl", leer, alt);
        await Bilder(2);
        await Schuss(sb, "leer");

        int gb = _entities.ErstesEigenesGebaeude();
        if (gb < 0) gb = _entities.ErstesGebaeude();
        bool gebOk;
        // ⚠ K1 hat KEIN Gebaeude mit Bauwerk — der Gebaeudefall wird dort nicht entschieden,
        // sondern auf einer Karte mit Gebaeude (K3) geprueft. Er faellt hier darum nicht durch.
        if (gb < 0) { gebOk = true; sb.Append("  2b Gebaeude: keins auf der Karte — UNGEPRUEFT (K3 nehmen)\n"); }
        else
        {
            _entities.BlockAuswahl(gb);
            var g = _entities.BlockStand();
            sb.Append($"  2b Gebaeude {_entities.GebaeudeKurz(gb)}:\n");
            gebOk = LeerzweigPruefen(sb, "Gebaeude gewaehlt", g, alt)
                    && string.Join("|", g.Zeilen) == string.Join("|", leer.Zeilen);
            await Bilder(2);
            await Schuss(sb, "gebaeude");
        }

        // ---- 3. die Schrift --------------------------------------------------
        bool schriftOk = true;
        foreach (var (wer, f) in new[] { ("Block", _legacyFont), ("Fenster", UI.WindowChrome.LegacyFont) })
        {
            if (f == null) { schriftOk = false; sb.Append($"  3 Schrift {wer}: KEINE\n"); continue; }
            float h13 = f.GetHeight(13), h26 = f.GetHeight(26);
            float w13 = f.GetStringSize("Panzer", HorizontalAlignment.Left, -1, 13).X;
            float w26 = f.GetStringSize("Panzer", HorizontalAlignment.Left, -1, 26).X;
            bool ok = h13 > 0 && Mathf.IsEqualApprox(h26, 2 * h13) && Mathf.IsEqualApprox(w26, 2 * w13);
            schriftOk &= ok;
            sb.Append($"  3 Schrift {wer}: Hoehe 13->{h13:0} 26->{h26:0}, »Panzer« 13->{w13:0} "
                    + $"26->{w26:0}: {(ok ? "waechst mit (2x)" : "WAECHST NICHT")}\n");
        }

        // ---- 4. der Lokator --------------------------------------------------
        bool lokOk = await LokatorPruefen(sb);

        // ---- Bilder der anderen Fenster mit der neuen Schrift ----------------
        if (_shotPath.Length > 0 && MitBild)
        {
            _entities.BlockAuswahl(i0);
            ZeigeGruppen(1);
            await Bilder(3);
            await Schuss(sb, "gruppenfenster");
            if (_groupChrome != null) _groupChrome.Visible = false;
            if (_groupWin != null) _groupWin.Visible = false;
            int eg = _entities.ErstesEigenesGebaeude();
            if (eg >= 0)
            {
                UI.WindowManager.Mausquelle = () => new Vector2(200, 120);
                _entities.PostenAnwaehlenWieKlick(eg);
                for (int t = 0; t <= UI.WindowManager.BilderAuf + 1; t++) UI.WindowManager.Takt();
                await Bilder(3);
                await Schuss(sb, "gebaeudefenster");
            }
        }

        bool alles = nameOk && leerOk && gebOk && schriftOk && lokOk && bildOk;
        sb.Append($"  Ergebnis: Name {Ja(nameOk)}, Leerzweig {Ja(leerOk)}, Gebaeude {(gb < 0 ? "ungeprueft" : Ja(gebOk))}, "
                + $"Schrift {Ja(schriftOk)}, Lokator {Ja(lokOk)}, Bild {(MitBild ? Ja(bildOk) : "-")}\n");
        sb.Append(alles ? "  BESTANDEN" : "  DURCHGEFALLEN");
        GD.Print(sb.ToString());
        GetTree().Quit(0);
    }

    private static bool LeerzweigPruefen(StringBuilder sb, string fall,
        (string Streifen, System.Collections.Generic.List<string> Zeilen, float Hoehe, float Unterkante,
         int SichtbareZeilen) s, string[] alt)
    {
        bool anzahl = s.Zeilen.Count == 6;
        bool lage = anzahl;
        for (int i = 0; lage && i < 6; i++)
            lage = s.Zeilen[i].StartsWith($"(11,{MapEntityLayer.LeerzweigY[i]}) ");
        // Der Block endet im vertieften Feld bei y = 43 + 94 = 137 (panel_index.json).
        bool imFeld = s.Unterkante > 0 && s.Unterkante <= 137f;
        // Die Zahlen gegen die zweite, unabhaengige Rechnung (alter Fliesstext).
        bool zahlen = anzahl && alt.Length == 6;
        for (int i = 1; zahlen && i < 6; i++)
            zahlen = Zahl(s.Zeilen[i]) == Zahl(alt[i]);
        sb.Append($"  2 Leerzweig ({fall}): {s.Zeilen.Count} feste Zeilen, Streifen »{s.Streifen.Replace('\n', '/')}« "
                + $"(Hoehe {s.Hoehe:0}, sichtbar {s.SichtbareZeilen}), Unterkante {s.Unterkante:0.#}/137\n");
        foreach (var z in s.Zeilen) sb.Append($"      {z}\n");
        sb.Append($"    sechs {Ja(anzahl)}, Lage 45/61/74/87/100/113 {Ja(lage)}, im Feld {Ja(imFeld)}, "
                + $"Zahlen = zweite Rechnung {Ja(zahlen)}\n");
        return anzahl && lage && imFeld && zahlen;
    }

    private static string Zahl(string s)
    {
        var m = Regex.Matches(s, @"-?\d+");
        return m.Count > 0 ? m[m.Count - 1].Value : "";
    }

    /// <summary>bug-420: einen Punkt suchen, an dem beide Bilder durchsichtig sind, und dort
    /// auf dem Schirm nachsehen, was durchscheint.</summary>
    private (bool Ok, string Zeile) BildgrundMessen()
    {
        if (_panelPortrait == null || !_panelPortrait.Visible) return (false, "kein Bildfeld sichtbar");
        var p = _entities.PanelPortrait();
        var a = UI.PortraitBank.Picture(p.ChassisPic)?.GetImage();
        var b = p.TurretPic > 0 ? UI.PortraitBank.Picture(p.TurretPic)?.GetImage() : null;
        if (a == null) return (false, $"Fahrwerksbild {p.ChassisPic} nicht ladbar");
        int bw = a.GetWidth(), bh = a.GetHeight();
        Vector2I? texel = null;
        // Ein Punkt, dessen ganze 5x5-Nachbarschaft in BEIDEN Bildern durchsichtig ist — das
        // gestreckte Sichtfeld mischt sonst den Bildrand in die Farbe.
        bool Leer(Image? im, int x, int y)
            => im == null || x < 0 || y < 0 || x >= im.GetWidth() || y >= im.GetHeight()
               || im.GetPixel(x, y).A <= 0.01f;
        for (int y = 2; y < bh - 2 && texel == null; y++)
            for (int x = 2; x < bw - 2; x++)
            {
                bool frei = true;
                for (int dy = -2; frei && dy <= 2; dy++)
                    for (int dx = -2; frei && dx <= 2; dx++)
                        frei = Leer(a, x + dx, y + dy) && Leer(b, x + dx, y + dy);
                if (!frei) continue;
                texel = new Vector2I(x, y);
                break;
            }
        if (texel == null) return (false, "kein durchsichtiger Punkt im Bild");
        var box = _panelPortrait.Size;
        float s = Mathf.Min(box.X / bw, box.Y / bh);
        var off = (box - new Vector2(bw, bh) * s) / 2f;
        var lokal = off + (new Vector2(texel.Value.X, texel.Value.Y) + new Vector2(0.5f, 0.5f)) * s;
        var bild = GetViewport().GetTexture().GetImage();
        // ⚠ Das Sichtfeld ist 1600x900 und wird auf das Fenster GESTRECKT; die Textur hat
        // Fenstergroesse. Ohne diesen Faktor las der erste Lauf 180 Punkte zu hoch.
        var faktor = new Vector2(bild.GetWidth() / GetViewportRect().Size.X,
                                 bild.GetHeight() / GetViewportRect().Size.Y);
        var punkt = (_panelPortrait.GetGlobalTransformWithCanvas() * lokal) * faktor;
        var c = bild.GetPixel((int)punkt.X, (int)punkt.Y);
        int r8 = (int)Mathf.Round(c.R * 255), g8 = (int)Mathf.Round(c.G * 255), b8 = (int)Mathf.Round(c.B * 255);
        bool panel = r8 == 55 && g8 == 47 && b8 == 47;
        bool mulde = r8 == 19 && g8 == 19 && b8 == 15;
        GD.Print($"infofenster-bild-diagnose: Lage {_panelPortrait.Position} global {_panelPortrait.GlobalPosition} "
               + $"mitLeinwand {_panelPortrait.GetGlobalTransformWithCanvas().Origin} Mass {_panelPortrait.Size} "
               + $"Sicht {GetViewportRect().Size} Fenster {GetWindow().Size} Bild {bild.GetSize()}");
        return (panel, $"Texel {texel} (Bilder {p.ChassisPic}/{p.TurretPic}) -> Schirm {punkt.X:0},{punkt.Y:0}: "
                     + $"({r8},{g8},{b8}) = {(panel ? "PANEL.DTA 0x2C, durchsichtig" : mulde ? "MULDE 0x2F" : "?")}");
    }

    private async System.Threading.Tasks.Task<bool> LokatorPruefen(StringBuilder sb)
    {
        ZeigeLokator(-1);
        for (int t = 0; t <= UI.WindowManager.BilderAuf + 1; t++) UI.WindowManager.Takt();
        await Bilder(2);
        var lc = _lokatorChrome;
        if (UI.LokatorChrome.Alt || lc == null || !lc.Visible)
        {
            sb.Append($"  4 Lokator: {(_locator != null && _locator.Visible ? "alter Aufbau (Godot-Moebel)" : "nicht offen")} — KEINE Kacheln\n");
            await Schuss(sb, "lokator");
            if (_locator != null) { _locator.Visible = false; UI.WindowManager.Wegnehmen(UI.WindowManager.Offen(UI.WindowManager.ArtMerkpunkte)); }
            return false;
        }
        int S = UI.LokatorChrome.Scale;
        bool mass = lc.Size == new Vector2(560, 280);
        var tests = new (Vector2 P, int Soll, string Was)[]
        {
            (new(30 * S + 1, 30 * S + 1), 1000, "Zeile 0"),
            (new(30 * S + 1, (30 + 15 * 3) * S + 1), 1003, "Zeile 3"),
            (new(30 * S + 1, 105 * S + 1), 1, "Lokalisieren"),
            (new(150 * S + 1, 105 * S + 1), 2, "Sichern"),
            (new(559, 1), -2, "Kreuz"),
            (new(20, 10), -5, "Titel"),
            (new(5, 250), 0, "Flaeche"),
        };
        bool hits = true;
        var hz = new StringBuilder();
        foreach (var t in tests)
        {
            int h = lc.Hit(t.P);
            hits &= h == t.Soll;
            hz.Append($"{t.Was} {h}{(h == t.Soll ? "" : $"≠{t.Soll}")} · ");
        }
        sb.Append($"  4 Lokator: Kachelfenster {lc.Size.X:0}x{lc.Size.Y:0} an {lc.Position} (Soll 560x280: {Ja(mass)})\n"
                + $"    Treffer: {hz}{Ja(hits)}\n");

        // Echte Klicks — mit eigenen Zaehlern statt der Wirkung (⚠ Falle »Klick kam nicht an«).
        var merkL = lc.OnLocate; var merkS = lc.OnSave;
        int lokalisiert = 0; int gesichertZeile = -1; string gesichertName = "";
        lc.OnLocate = _ => lokalisiert++;
        lc.OnSave = (i, n) => { gesichertZeile = i; gesichertName = n; };
        async System.Threading.Tasks.Task Klick(Vector2 lokal)
        {
            var punkt = lc.GetGlobalTransformWithCanvas() * lokal;
            GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = punkt }, true);
            GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = punkt }, true);
            await Bilder(2);
        }
        await Klick(new Vector2(30 * S + 10, 105 * S + 10));          // Lokalisieren ohne Zeile
        bool offenNachKnopf = lc.Visible;
        bool ohneZeile = lokalisiert == 0 && offenNachKnopf && lc.Zeile < 0;
        await Klick(new Vector2(30 * S + 10, 30 * S + 5));            // Zeile 0
        bool zeileGewaehlt = lc.Zeile == 0;
        await Bilder(2);
        await Schuss(sb, "lokator");
        // Den Namen leeren — über echte Tasten.
        for (int i = 0; i < 25 && lc.Eingabe.Length > 0; i++)
        {
            GetViewport().PushInput(new InputEventKey { Keycode = Key.Backspace, Pressed = true });
            await Bilder(1);
        }
        bool leer = lc.Eingabe.Length == 0;
        await Klick(new Vector2(150 * S + 10, 105 * S + 10));         // Sichern
        for (int t = 0; t <= UI.WindowManager.BilderZu + 1; t++) UI.WindowManager.Takt();
        await Bilder(1);
        bool gesichert = gesichertZeile == 0 && gesichertName == "NONAME";
        bool zu = !lc.Visible;
        lc.OnLocate = merkL; lc.OnSave = merkS;
        sb.Append($"    Klickweg: Lokalisieren ohne Zeile -> {lokalisiert} Rufe, Fenster offen {Ja(offenNachKnopf)} ({Ja(ohneZeile)}); "
                + $"Zeile 0 -> Eingabemodus {Ja(zeileGewaehlt)}; Name geleert {Ja(leer)}; "
                + $"Sichern -> OnSave({gesichertZeile}, »{gesichertName}«) {Ja(gesichert)}; danach zu {Ja(zu)}\n");
        return mass && hits && ohneZeile && zeileGewaehlt && leer && gesichert && zu;
    }
}
