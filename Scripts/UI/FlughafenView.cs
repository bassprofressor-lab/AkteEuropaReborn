namespace AkteEuropaReborn.UI;

using System;
using System.Collections.Generic;
using Godot;

/// <summary>
/// <b>FENSTERART 5 — DER FLUGHAFEN, mit den Möbeln des Originals</b>
/// (19.09.2026, nach <c>berichte/flughafenfenster-art5-fable.md</c>).
///
/// <para><b>Seine Meldung:</b> »wenn ich auf den flughafen anwähle kommt
/// irgendein baue von uns, statt das originale«. Er hatte recht, und es war
/// ein halb erledigter Auftrag: am 12.09. (bug-211) ging am Flughafen das
/// ZWEITE Fenster aus — aber das eine, das blieb, war unser Godot-Eigenbau.
/// Die Kacheln aus <c>WINDOWS.CWW</c> hatten bis heute nur der Nachschubposten
/// (Art 31, <see cref="SupplyShopView"/>) und die Mine (Art 18,
/// <see cref="MineView"/>).</para>
///
/// <para><b>Beleglage.</b> Der Zeichner C <c>0x465050</c> und F <c>0x463940</c>
/// sind per <c>cfind --diff</c> <b>befehlsgleich</b> (1797 Befehle, 100 %);
/// jede Datenadresse ist Befehl für Befehl nach F gepaart. Druckhandler
/// C <c>0x4485D0</c> Arm <c>0x44999F</c> / F <c>0x4475D0</c>, Treffertest
/// C <c>0x45CB60</c> Arm <c>0x45D1F7</c> / F <c>0x45B6D0</c>, Anleger
/// C <c>0x457A00</c> / F <c>0x4566A0</c>.</para>
///
/// <para><b>Was GELESEN ist</b> und hier steht: die Breite 360 (18 Kacheln,
/// @0x457A91); die drei <b>reiterabhängigen Höhen</b> 240 / 340 / 320
/// (@0x465084/97/AA) — das Fenster wächst und schrumpft beim Reiterwechsel;
/// jede Zeile mit ihrem x/y; die Hangarliste mit zehn Zeilen à 15 px ab
/// y = 100; die sechs Knöpfe in zwei Reihen (y = 270 und y = 295); die
/// Plakette <c>0xAA + n</c> der Staffel; und die Währung — <b>Taste 13
/// @0x449D14 prüft nur TEILE und Platz, nie Geld</b>.</para>
///
/// <para>⚠⚠ <b>GRAU gibt es im Original nicht.</b> Jede Sperre ist dort eine
/// MELDUNG (Fensterart 13) oder ein stiller Nicht-Effekt. Wo wir hier dimmen,
/// ist das <b>unsere Setzung</b> — sie steht, weil ein Knopf, dessen Mechanik
/// bei uns fehlt, sonst wie ein kaputter aussähe und nicht wie ein
/// ungebauter. Der Hinweistext sagt in jedem Fall, was fehlt.</para>
///
/// <para><b>UNSERE SETZUNGEN, benannt:</b></para>
/// <list type="bullet">
///   <item>Die <b>Staffelplakette</b> ist bei uns die Ziffer als Text. Das
///   Original hat dafür Glyphen <c>0xAA + n</c> in <c>FONT.CWD</c>; welches
///   Bild darin steckt, ist ungelesen.</item>
///   <item><b>»Angriff« startet bei uns OHNE Ziel.</b> Das Original öffnet den
///   Kartenschirm (Art 3) im Zielwahl-Modus und schickt die ganze Staffel auf
///   das angeklickte Ziel. Solange das nicht gebaut ist, tut der Knopf, was
///   unser alter »Starten«-Knopf tat — sonst wäre eine gekaufte Maschine im
///   Hangar gefangen, denn das Original hat <b>gar keinen</b>
///   »Aussenden«-Knopf. Das ist der Zustand <c>--zielwahl-aus</c>, und der
///   Hinweistext sagt es.</item>
///   <item><b>»Handsteuerung« und »Recycle«</b> sind gezeichnet und gesperrt:
///   die Handsteuerung Luft ist gelesen, aber nicht gebaut; der Recycle-Arm
///   (<c>0x40163B</c>) ist ungelesen.</item>
///   <item>Die <b>Farbe 0x94</b> des hervorgehobenen Zeilentexts ist aus
///   <c>01.PAL</c> nachgeschlagen (247, 253, 14) — das Original nennt das Paar
///   0x94/0x96, wir malen einfarbig und nehmen wie überall den ersten.</item>
/// </list>
///
/// <para>Gegenschalter <c>--flughafenfenster-alt</c> stellt unser Godot-Fenster
/// wieder her. Prüfstand <c>--flughafenfenster-check</c> (bug-211) und
/// <c>--bombe-check</c>.</para>
/// </summary>
public sealed partial class FlughafenView : Control
{
    /// <summary>18 Kacheln = 360 Bildpunkte (@0x457A91).</summary>
    public const int WTiles = 18;

    /// <summary>Die Höhe je Reiter, in Kacheln: Lager 12 (240), Hangar 17
    /// (340), Produktion 16 (320) — @0x465084/97/AA.</summary>
    public static readonly int[] HTilesJeReiter = { 12, 17, 16 };

    public const int Scale = 2;

    private const int TitleX = 10, TitleY = 2;
    private const int TextX = 20, WertX = 160, WertX2 = 100;

    // ---- der Kopf, in jedem Reiter gleich ---------------------------------
    private const int YEnergie = 23;
    private const int BarX = 80, BarY = 25, BarW = 260, BarH = 10;
    private const int BarFuellX = 84, BarFuellY = 29, BarFuellW = 252, BarFuellH = 2;

    // ---- die drei Reiter --------------------------------------------------
    private const int ReiterY = 40, ReiterTiles = 4;
    private static readonly int[] ReiterX = { 20, 100, 180 };
    private static readonly string[] ReiterWort = { "Lager", "Hangar", "Produktion" };

    // ---- Reiter »Lager« ---------------------------------------------------
    private const int YStatus = 63, YTeile = 78, YWaffe = 78, YFahrwerk = 93,
                      YSpezial = 108, YPlatz = 123, YErweiter = 138, YKonto = 168;
    private const int RechtsX = 320;              // rechtsbuendig
    private const int KontoWertX = 88;
    private const int LagerKnopfY = 185, LagerKnopfX = 20, LagerKnopf2X = 240;

    // ---- Reiter »Hangar« --------------------------------------------------
    private const int ListeX = 20, ListeY = 80, ListeWTiles = 11, ListeHTiles = 9;
    private const int BildX = 280, BildY = 80, BildTiles = 3;
    private const int RollX = 240, RollY = 80, RollTiles = 9;
    private const int ZeileY0 = 100, ZeileH = 15, ZeilenSicht = 10;
    private const int PlaketteX = 32, NameX = 50;
    private const int BalkenX = 30, BalkenW = 195, BalkenH = 14;
    private const int EX = 270, EY = 144;
    private const int EBarX = 280, EBarY = 145, EBarW = 60, EBarH = 10;
    private const int EFuellX = 284, EFuellY = 149, EFuellW = 52, EFuellH = 2;
    private const int DetailX = 265;
    private const int YAus1 = 160, YAus2 = 175, YAV = 190, YGeschw = 205,
                      YSicht = 220, YBombe = 235;

    // ---- die sechs Knoepfe (5 Kacheln = 100 px) ---------------------------
    private const int KnopfTiles = 5;
    private static readonly int[] KnopfX = { 20, 130, 240, 20, 130, 240 };
    private static readonly int[] KnopfY = { 270, 270, 270, 295, 295, 295 };

    // ---- Reiter »Produktion« ---------------------------------------------
    private const int PreisRechtsX = 222;
    private const int ProdDetailY0 = 145;
    private const int BestandY = 273;
    private const int ProdKnopfX = 240, ProdKnopfY = 270;

    /// <summary>0x94 aus <c>DATA/01.PAL</c> — der hervorgehobene Zeilentext.
    /// Das Original nennt das Paar 0x94/0x96; wir nehmen den ersten, wie beim
    /// Titel auch.</summary>
    private static readonly Color HighColour = Color.Color8(247, 253, 14);

    public Action? OnClose;
    public Action? OnRepair;
    public Action? OnVerbessern;
    public Action? OnProduzieren;

    /// <summary>Welcher Reiter angeklickt wurde (0..2) — das Fenster wechselt
    /// die Höhe, darum muss der Halter es erfahren.</summary>
    public Action<int>? OnReiter;

    /// <summary>Eine Hangarzeile wurde gewählt (Zeilennummer, nicht
    /// sec19-Platz).</summary>
    public Action<int>? OnWahl;

    /// <summary>Der ZWEITE Klick auf die schon gewählte Zeile — Beitritt oder
    /// Austritt der Staffel (@0x44A028).</summary>
    public Action<int>? OnStaffelUmschalten;

    /// <summary>Eine Produktionszeile wurde gewählt.</summary>
    public Action<int>? OnProdWahl;

    public Action? OnAngriff;
    public Action? OnPatrouille;
    public Action? OnHandsteuerung;
    public Action? OnBombeWechseln;
    public Action? OnGruppieren;
    public Action? OnRecycle;

    private BuildingWindow.Stand? _stand;
    private int _held = -1;
    private int _rollstand;
    private int _prodWahl;

    /// <summary>⚠⚠ 19.09.2026 — <b>DAS ZIEHEN HATTE ICH VERGESSEN.</b> Seine
    /// Meldung: »allgemein hängt das fenster wie unten am bildschirm, kann es
    /// auch nicht verschieben«. <see cref="MineView"/> und
    /// <see cref="DepotView"/> haben dieses Feld von Anfang an; beim Bauen
    /// dieses Fensters ist es untergegangen, und damit war das grösste Fenster
    /// des Spiels das einzige, das man nicht wegschieben kann.</summary>
    private bool _zieht;

    /// <summary>Wie viele Knöpfe zuletzt bedienbar waren — für den Prüfstand.
    /// ⚠ Hier gesetzt und nicht erst im Zeichner: der Prüfstand liest die Zahl,
    /// bevor ein Bild gelaufen ist. Genau daran hat der Minen-Prüfstand am
    /// 08.09. »0 Knoepfe« gemeldet.</summary>
    public int Buttons { get; private set; }

    /// <summary>Zeichnet dieses Fenster gerade mit den Kacheln des Originals?
    /// Ohne diese Auskunft wäre »gebaut« eine Behauptung — ohne
    /// <c>WINDOWS.CWW</c> fällt der Halter auf die Godot-Möbel zurück, und das
    /// sähe von aussen aus wie ein nicht gebautes Fenster.</summary>
    public static bool Usable => WindowChrome.Atlas != null;

    public FlughafenView()
    {
        MouseFilter = MouseFilterEnum.Stop;
        ProcessMode = ProcessModeEnum.Always;
        Masse(0);
    }

    private void Masse(int reiter)
    {
        int h = HTilesJeReiter[Mathf.Clamp(reiter, 0, 2)];
        CustomMinimumSize = new Vector2(WTiles * WindowChrome.Cell * Scale,
                                        h * WindowChrome.Cell * Scale);
        Size = CustomMinimumSize;
    }

    private int HTiles => HTilesJeReiter[Mathf.Clamp(_stand?.Reiter ?? 0, 0, 2)];

    public void Zeige(BuildingWindow.Stand s)
    {
        _stand = s;
        Masse(s.Reiter);
        // Der Rollstand muss die gewaehlte Zeile im Blick halten, sonst wandert
        // die Auswahl aus dem Fenster, wenn der Halter sie von aussen setzt.
        if (s.Reiter == 1)
        {
            int n = s.HangarZeilen.Count;
            _rollstand = Mathf.Clamp(_rollstand, 0, Mathf.Max(0, n - ZeilenSicht));
            if (s.HangarWahl < _rollstand) _rollstand = s.HangarWahl;
            else if (s.HangarWahl >= _rollstand + ZeilenSicht)
                _rollstand = s.HangarWahl - ZeilenSicht + 1;
            _rollstand = Mathf.Max(0, _rollstand);
        }
        Buttons = BedienbareKnoepfe(s);
        QueueRedraw();
    }

    /// <summary>Welche der sechs Knöpfe des Hangarreiters bei uns wirklich
    /// etwas tun. ⚠ Der Prüfstand fragt DIESE Rechnung, nicht eine zweite.</summary>
    private static int BedienbareKnoepfe(BuildingWindow.Stand s)
    {
        if (s.Reiter != 1) return s.Reiter == 0 ? 1 : 1;   // Reparieren bzw. Produzieren
        int n = 0;
        var z = Gewaehlt(s);
        if (z != null) n++;                                 // Angriff (ohne Ziel)
        n++;                                                // Patrouille
        if (z != null && z.Bomber) n++;                     // Bombe wechseln
        if (z != null) n++;                                 // Gruppieren
        return n;
    }

    private static BuildingWindow.HangarZeile? Gewaehlt(BuildingWindow.Stand s)
        => s.HangarWahl >= 0 && s.HangarWahl < s.HangarZeilen.Count
            ? s.HangarZeilen[s.HangarWahl] : null;

    private static int FontSize => WindowChrome.FontCell * Scale;

    private void Text(Font f, int x, int y, string t, Color c)
        => DrawString(f, new Vector2(x * Scale, y * Scale + f.GetAscent(FontSize)),
                      t, HorizontalAlignment.Left, -1, FontSize, c);

    /// <summary>Rechtsbündig an <paramref name="rechts"/> — so setzt das
    /// Original die Lagerzahlen (x = 320) und die Preise (x = 222).</summary>
    private void TextRechts(Font f, int rechts, int y, string t, Color c)
    {
        float w = f.GetStringSize(t, HorizontalAlignment.Left, -1, FontSize).X;
        DrawString(f, new Vector2(rechts * Scale - w, y * Scale + f.GetAscent(FontSize)),
                   t, HorizontalAlignment.Left, -1, FontSize, c);
    }

    private void Rechteck(int x, int y, int w, int h, Color c)
        => DrawRect(new Rect2(x * Scale, y * Scale, w * Scale, h * Scale), c, true);

    /// <summary>Die Staffelplakette. ⚠ UNSERE Setzung: das Original malt den
    /// Glyphen <c>0xAA + n</c> aus FONT.CWD, wir schreiben die Ziffer.</summary>
    private static string Plakette(int staffel)
        => staffel == 0xFF ? " " : staffel.ToString();

    public override void _Draw()
    {
        var s = _stand;
        var font = WindowChrome.LegacyFont;
        if (s == null || WindowChrome.Atlas == null || font == null) return;

        WindowChrome.Paint(this, WTiles, HTiles, Scale);

        // Der Titel steht so in der EXE — »Flughafen « mit dem Leerzeichen,
        // hinter das der Name kommt (Gebaeude +0x1B).
        Text(font, TitleX, TitleY, "Flughafen " + s.Name, WindowChrome.TitleColour);

        // ---- der Zustandsbalken, in jedem Reiter (@0x4650.., Rahmen 260x10) --
        Text(font, TextX, YEnergie, "Energie :", WindowChrome.TitleColour);
        Rechteck(BarX, BarY, BarW, BarH, new Color(0, 0, 0));
        if (s.HpMax > 0)
            Rechteck(BarFuellX, BarFuellY,
                     BarFuellW * Mathf.Clamp(s.Hp, 0, s.HpMax) / s.HpMax, BarFuellH,
                     WindowChrome.LineColour);

        // ---- die drei Reiter, gedrueckt = aktiv ------------------------------
        for (int i = 0; i < 3; i++)
        {
            bool aktiv = s.Reiter == i;
            WindowChrome.PaintButton(this, ReiterX[i], ReiterY, ReiterTiles, Scale, aktiv);
            float w = font.GetStringSize(ReiterWort[i], HorizontalAlignment.Left, -1,
                                         FontSize).X;
            int bx = ReiterX[i] + (int)((ReiterTiles * WindowChrome.Cell * Scale - w)
                                        / 2 / Scale);
            Text(font, bx, ReiterY + (aktiv ? 4 : 3), ReiterWort[i],
                 aktiv ? WindowChrome.TitleColour : WindowChrome.TextColour);
        }

        switch (s.Reiter)
        {
            case 1: HangarZeichnen(s, font); break;
            case 2: ProduktionZeichnen(s, font); break;
            default: LagerZeichnen(s, font); break;
        }
    }

    // ======================= Reiter »Lager« ================================

    private void LagerZeichnen(BuildingWindow.Stand s, Font font)
    {
        Text(font, TextX, YStatus, "Status", WindowChrome.TitleColour);
        // 0 »aktiv« / 1 »reparieren« / 3 »forschen« bei x = 100, 2
        // »vergroessern N%« bei x = 160 — zwei verschiedene Spalten, so steht
        // es im Zeichner.
        if (s.Status.StartsWith("vergr", StringComparison.OrdinalIgnoreCase))
            Text(font, WertX, YStatus, s.Status, WindowChrome.TextColour);
        else
            Text(font, WertX2, YStatus, s.Status, WindowChrome.TextColour);

        Text(font, TextX, YTeile, "Teile gelagert", WindowChrome.TitleColour);
        // ⚠ Die drei Klammern sind die ZEICHEN des Originals fuer die drei
        // Teilearten: ] Waffe, [ Fahrwerk, { Spezial. Sie stehen so in den
        // Zeichenketten und sind keine Verzierung.
        TextRechts(font, RechtsX, YWaffe, $"] {s.StockW}", WindowChrome.TextColour);
        TextRechts(font, RechtsX, YFahrwerk, $"[ {s.StockF}", WindowChrome.TextColour);
        TextRechts(font, RechtsX, YSpezial, $"{{ {s.StockS}", WindowChrome.TextColour);

        Text(font, TextX, YPlatz, "Lagerplatz", WindowChrome.TitleColour);
        Text(font, WertX, YPlatz, $"{s.HangarZeilen.Count}/{s.HangarPlaetze}",
             WindowChrome.TextColour);

        Text(font, TextX, YErweiter, "Erweiterungskosten", WindowChrome.TitleColour);
        Text(font, WertX, YErweiter, s.AusbauKosten.ToString(), WindowChrome.TextColour);

        // ⚠ Der Kontostand steht hier OHNE »$« — das Original schreibt an
        // dieser Stelle die nackte Zahl. Der Flughafen zahlt ohnehin in Teilen.
        Text(font, TextX, YKonto, "Kontostand", WindowChrome.TitleColour);
        Text(font, KontoWertX, YKonto, s.Geld.ToString(), WindowChrome.TextColour);

        // ⚠⚠ 19.09.2026 — HIER STAND EINE 0, und das war ein Fehler beim
        // Umbenennen der zwei Knopfhelfer: aus dem INDEX 0 (der auf KnopfX[0]
        // = 20 zeigte) wurde beim Wechsel auf KnopfFrei die X-KOORDINATE 0.
        // »Verbessern« ragte damit links aus dem Fenster heraus — genau so
        // gemeldet: »verbessern ragt raus aus dem kasten unter lager«.
        // Die Lesung sagt (20, 185), 5 Kacheln.
        KnopfFrei(font, LagerKnopfX, LagerKnopfY, "Verbessern", false,
              "Der Lagerausbau des Flughafens ist bei uns nicht gebaut.");
        KnopfFrei(font, LagerKnopf2X, LagerKnopfY, "Reparieren", true, null);
    }

    // ======================= Reiter »Hangar« ===============================

    private void HangarZeichnen(BuildingWindow.Stand s, Font font)
    {
        WindowChrome.PaintInnerFrame(this, ListeX, ListeY, ListeWTiles, ListeHTiles, Scale);
        WindowChrome.PaintInnerFrame(this, BildX, BildY, BildTiles, BildTiles, Scale);

        int n = s.HangarZeilen.Count;
        // Der Rollbalken erscheint erst ueber zehn Maschinen (@0x465...).
        if (n > ZeilenSicht)
            WindowChrome.PaintScrollbar(this, RollX, RollY, RollTiles, Scale,
                                        _rollstand, Mathf.Max(1, n - ZeilenSicht));

        var gewaehlt = Gewaehlt(s);

        // ⚠⚠ NUR DIE BELEGTEN PLAETZE. Das Original laeuft die Stellplaetze bis
        // zum ersten 0xFF und zeichnet KEINE leeren Zeilen — unser altes
        // Fenster schrieb dort »1. —«, und das sah wie eine Aussage aus.
        for (int i = 0; i < ZeilenSicht; i++)
        {
            int k = _rollstand + i;
            if (k >= n) break;
            var z = s.HangarZeilen[k];
            int y = ZeileY0 + i * ZeileH;

            if (k == s.HangarWahl)
                Rechteck(BalkenX, y, BalkenW, BalkenH, WindowChrome.LineColour);

            // Die Farbe sagt, ob diese Maschine zur Staffelmarke des FENSTERS
            // gehoert — daran sieht der Spieler, was »Angriff« mitnimmt.
            var col = z.Staffel != 0xFF && z.Staffel == s.StaffelWahl
                    ? HighColour : WindowChrome.TextColour;

            Text(font, PlaketteX, y, Plakette(z.Staffel), WindowChrome.TitleColour);
            string wort = z.Name;
            if (z.Bomber && !MapEntityLayerBombenetikettKaputt)
                wort += Rendering.Bombensorten.Wort(z.Bombe);
            Text(font, NameX, y, wort, col);
        }

        if (gewaehlt != null)
        {
            if (PortraitBank.Ready && gewaehlt.Bild > 0)
                PortraitBank.DrawPictures(this,
                    new Rect2(BildX * Scale, BildY * Scale,
                              BildTiles * WindowChrome.Cell * Scale,
                              BildTiles * WindowChrome.Cell * Scale),
                    gewaehlt.Bild, 0);

            Text(font, EX, EY, "E", WindowChrome.TextColour);
            Rechteck(EBarX, EBarY, EBarW, EBarH, new Color(0, 0, 0));
            if (gewaehlt.HpMax > 0)
                Rechteck(EFuellX, EFuellY,
                         EFuellW * Mathf.Clamp(gewaehlt.Hp, 0, gewaehlt.HpMax)
                         / gewaehlt.HpMax, EFuellH, WindowChrome.LineColour);

            // ⚠ Eine Ausruestungszeile steht NUR, wenn sie belegt ist (»nur
            // wenn ≠ 0« im Zeichner) — eine leere Zeile ist keine Aussage.
            if (gewaehlt.Ausruestung1.Length > 0)
                Text(font, DetailX, YAus1, gewaehlt.Ausruestung1, WindowChrome.TextColour);
            if (gewaehlt.Ausruestung2.Length > 0)
                Text(font, DetailX, YAus2, gewaehlt.Ausruestung2, WindowChrome.TextColour);
            if (gewaehlt.Angriff >= 0)
                Text(font, DetailX, YAV, $"A/V {gewaehlt.Angriff}/{gewaehlt.Verteidigung}",
                     WindowChrome.TextColour);
            if (gewaehlt.Tempo >= 0)
                Text(font, DetailX, YGeschw, $"Geschw. {gewaehlt.Tempo}",
                     WindowChrome.TextColour);
            if (gewaehlt.Sicht >= 0)
                Text(font, DetailX, YSicht, $"Sicht {gewaehlt.Sicht}",
                     WindowChrome.TextColour);

            // ⭐ Das BOMBENETIKETT. Im Original ist genau diese Stelle KAPUTT
            // (@0x466690 vergleicht 0/1/2 statt 45/46/47) und zeigt darum immer
            // den Text, der noch im Puffer steht. Wir zeichnen es richtig; wer
            // den Fehler sehen will, nimmt --bombenetikett-kaputt.
            if (gewaehlt.Bomber)
                Text(font, DetailX, YBombe,
                     MapEntityLayerBombenetikettKaputt
                         ? gewaehlt.Bombe.ToString()
                         : Rendering.Bombensorten.Wort(gewaehlt.Bombe).TrimStart(),
                     WindowChrome.TextColour);
        }

        bool hat = gewaehlt != null;
        Knopf(font, 0, KnopfY[0], "Angriff", hat,
              hat ? "⚠ UNSERE Setzung: startet OHNE Ziel. Das Original oeffnet den "
                  + "Kartenschirm (Art 3) im Zielwahl-Modus und schickt die ganze "
                  + "Staffel auf das angeklickte Ziel — das ist gelesen, aber noch "
                  + "nicht gebaut (--zielwahl-aus)."
                  : "Es steht keine Maschine im Hangar.");
        Knopf(font, 1, KnopfY[1], s.Patrouille ? "Patrouille AN" : "Patrouille AUS", true,
              "Der Knopf und die Flagge (Gebaeude +0x43, Befehl 537) sind gelesen; "
              + "was der Flugtakt mit ihr tut, ist UNGELESEN — die Wirkung bleibt "
              + "unsere AirPatrol.");
        Knopf(font, 2, KnopfY[2], "Handsteuerung", false,
              "Gelesen (Befehl 502 Modus 6, uk 3, Pfeile ±6°, A/Z Hoehe ±2, Strg "
              + "schiesst), aber noch nicht gebaut.");
        // »Bombe wechseln« zeigt das Original NUR bei einem Bomber.
        if (gewaehlt is { Bomber: true })
            Knopf(font, 3, KnopfY[3], "Bombe wechseln", !MapEntityLayerBombeFest47,
                  MapEntityLayerBombeFest47
                      ? "--bombe-fest-47: der Knopf ist gesperrt, jeder Bomber traegt 47."
                      : "45 Gasbombe -> 46 Loeschmittel -> 47 Bombe. Bei gesetzter "
                      + "Staffel dreht er JEDEN Bomber der Staffel mit.");
        Knopf(font, 4, KnopfY[4], Plakette(s.StaffelWahl) + " Gruppieren", hat,
              hat ? "Dreht die Staffelmarke durch die benutzten Nummern plus eine "
                  + "freie. Ein ZWEITER Klick auf die gewaehlte Zeile nimmt die "
                  + "Maschine auf oder heraus."
                  : "Es steht keine Maschine im Hangar.");
        Knopf(font, 5, KnopfY[5], "Recycle", false,
              "Befehl 535 ist gelesen, sein Arm (0x40163B) nicht — deshalb nicht "
              + "gebaut.");
    }

    // ===================== Reiter »Produktion« =============================

    private void ProduktionZeichnen(BuildingWindow.Stand s, Font font)
    {
        WindowChrome.PaintInnerFrame(this, ListeX, ListeY, ListeWTiles, ListeHTiles, Scale);
        WindowChrome.PaintInnerFrame(this, BildX, BildY, BildTiles, BildTiles, Scale);

        var liste = s.Angebote;
        if (liste.Count > ZeilenSicht)
            WindowChrome.PaintScrollbar(this, RollX, RollY, RollTiles, Scale,
                                        _rollstand, Mathf.Max(1, liste.Count - ZeilenSicht));

        _prodWahl = Mathf.Clamp(_prodWahl, 0, Mathf.Max(0, liste.Count - 1));
        for (int i = 0; i < ZeilenSicht; i++)
        {
            int k = _rollstand + i;
            if (k >= liste.Count) break;
            var a = liste[k];
            int y = ZeileY0 + i * ZeileH;
            if (k == _prodWahl)
                Rechteck(BalkenX, y, BalkenW, BalkenH, WindowChrome.LineColour);
            Text(font, PlaketteX, y, a.Name, WindowChrome.TextColour);
            // ⚠ Der Preis steht in TEILEN, rechtsbuendig an x = 222. Taste 13
            // @0x449D14 prueft nur Teile und Hangarplatz — NIE Geld. Beides zu
            // verlangen hiesse doppelt zahlen (Fehler C12 vom 17.08.2026).
            if (a.KostenW >= 0)
                TextRechts(font, PreisRechtsX, y,
                           $"] {a.KostenW} [ {a.KostenF} {{ {a.KostenS}",
                           a.Bezahlbar ? WindowChrome.TextColour : WindowChrome.AlarmColour);
        }

        if (_prodWahl < liste.Count)
        {
            var a = liste[_prodWahl];
            if (PortraitBank.Ready && a.Bild > 0)
                PortraitBank.DrawPictures(this,
                    new Rect2(BildX * Scale, BildY * Scale,
                              BildTiles * WindowChrome.Cell * Scale,
                              BildTiles * WindowChrome.Cell * Scale), a.Bild, 0);

            int y = ProdDetailY0;
            // ⚠ Die Energie steht hier OHNE Balken — anders als im Hangar.
            if (a.Energie >= 0)
                Text(font, DetailX, y, $"Energie : {a.Energie}", WindowChrome.TextColour);
            if (a.Ausruestung1.Length > 0)
                Text(font, DetailX, y + 15, a.Ausruestung1, WindowChrome.TextColour);
            if (a.Ausruestung2.Length > 0)
                Text(font, DetailX, y + 30, a.Ausruestung2, WindowChrome.TextColour);
            if (a.Angriff >= 0)
                Text(font, DetailX, y + 45, $"A/V {a.Angriff}/{a.Verteidigung}",
                     WindowChrome.TextColour);
            if (a.Tempo >= 0)
                Text(font, DetailX, y + 60, $"Geschw. {a.Tempo}", WindowChrome.TextColour);
            if (a.Sicht >= 0)
                Text(font, DetailX, y + 75, $"Sicht {a.Sicht}", WindowChrome.TextColour);
        }

        // Die Bestandszeile unten — dieselben drei Zeichen wie im Lagerreiter.
        Text(font, BalkenX, BestandY, $"] {s.StockW}  [ {s.StockF}  {{ {s.StockS}",
             WindowChrome.TextColour);

        bool geht = _prodWahl < liste.Count && liste[_prodWahl].Bezahlbar;
        KnopfFrei(font, ProdKnopfX, ProdKnopfY, "Produzieren", geht,
              _prodWahl < liste.Count ? liste[_prodWahl].PreisQuelle : null);
    }

    // =========================== Knoepfe ===================================

    /// <summary>Ein Knopf an einer der sechs festen Stellen des Hangarreiters
    /// (Index 0..5). <paramref name="hinweis"/> steht im Hinweistext, nicht im
    /// Bild — siehe <see cref="_GetTooltip"/>.</summary>
    private void Knopf(Font font, int index, int y, string wort, bool geht, string? hinweis)
        => KnopfAn(font, KnopfX[index], y, wort, geht);

    /// <summary>Ein Knopf an einer freien Stelle (Lager- und
    /// Produktionsreiter).</summary>
    private void KnopfFrei(Font font, int x, int y, string wort, bool geht, string? hinweis)
        => KnopfAn(font, x, y, wort, geht);

    private void KnopfAn(Font font, int x, int y, string wort, bool geht)
    {
        bool held = _held >= 0 && KnopfX.Length > _held
                    && KnopfX[_held] == x && KnopfY[_held] == y;
        WindowChrome.PaintButton(this, x, y, KnopfTiles, Scale, held);
        float w = font.GetStringSize(wort, HorizontalAlignment.Left, -1, FontSize).X;
        int bx = x + (int)((KnopfTiles * WindowChrome.Cell * Scale - w) / 2 / Scale);
        var col = WindowChrome.TextColour;
        // ⚠ UNSERE Setzung — das Original dimmt nicht, es meldet. Siehe Kopf.
        if (!geht) col = new Color(col.R, col.G, col.B, 0.45f);
        Text(font, bx, y + (held ? 4 : 3), wort, col);
    }

    // ==================== Treffer und Bedienung ============================

    /// <summary>−2 Schliesskreuz, 1..3 die Reiter, 10..15 die sechs Knöpfe,
    /// 20 »Verbessern«, 21 »Reparieren«/»Produzieren«, 1000+k eine Listenzeile,
    /// 0 sonst (ziehen). Dieselbe Rechnung, die der Prüfstand fragt.</summary>
    public int Hit(Vector2 p)
    {
        float x = p.X / Scale, y = p.Y / Scale;
        int w = WTiles * WindowChrome.Cell;
        if (x >= w - 20 && x < w && y >= 0 && y < 20) return -2;

        for (int i = 0; i < 3; i++)
            if (x >= ReiterX[i] && x < ReiterX[i] + ReiterTiles * WindowChrome.Cell
                && y >= ReiterY && y < ReiterY + 20) return i + 1;

        var s = _stand;
        if (s == null) return 0;

        if (s.Reiter == 0)
        {
            if (Im(x, y, 20, LagerKnopfY)) return 20;
            if (Im(x, y, 240, LagerKnopfY)) return 21;
            return 0;
        }

        if (s.Reiter == 2)
        {
            if (Im(x, y, ProdKnopfX, ProdKnopfY)) return 21;
            int k = Zeile(x, y, s.Angebote.Count);
            return k >= 0 ? 1000 + k : 0;
        }

        for (int i = 0; i < 6; i++)
            if (Im(x, y, KnopfX[i], KnopfY[i])) return 10 + i;
        int z = Zeile(x, y, s.HangarZeilen.Count);
        return z >= 0 ? 1000 + z : 0;
    }

    private static bool Im(float x, float y, int kx, int ky)
        => x >= kx && x < kx + KnopfTiles * WindowChrome.Cell && y >= ky && y < ky + 20;

    /// <summary>Welche Listenzeile getroffen ist — <c>(my − 100) / 15 +
    /// Rollstand</c>, das Feld (20,100,220×150) des Originals.</summary>
    private int Zeile(float x, float y, int n)
    {
        if (x < ListeX || x >= ListeX + 220 || y < ZeileY0 || y >= ZeileY0 + ZeilenSicht * ZeileH)
            return -1;
        int k = (int)((y - ZeileY0) / ZeileH) + _rollstand;
        return k >= 0 && k < n ? k : -1;
    }

    public override void _GuiInput(InputEvent @event)
    {
        var s = _stand;
        if (s == null) return;
        if (@event is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left)
        {
            int t = Hit(mb.Position);
            if (mb.Pressed)
            {
                _held = t >= 10 && t <= 15 ? t - 10 : -1;
                // Ein Druck auf die Flaeche (nicht auf Knopf, Reiter oder
                // Zeile) beginnt das Ziehen — wie bei Mine und Depot.
                if (t == 0) _zieht = true;
                QueueRedraw();
                AcceptEvent();
                return;
            }
            _held = -1;
            QueueRedraw();
            if (_zieht) { _zieht = false; AcceptEvent(); return; }
            AcceptEvent();
            switch (t)
            {
                case -2: OnClose?.Invoke(); return;
                case 1: OnReiter?.Invoke(0); return;
                case 2: OnReiter?.Invoke(1); return;
                case 3: OnReiter?.Invoke(2); return;
                case 10: OnAngriff?.Invoke(); return;
                case 11: OnPatrouille?.Invoke(); return;
                case 12: OnHandsteuerung?.Invoke(); return;
                case 13: OnBombeWechseln?.Invoke(); return;
                case 14: OnGruppieren?.Invoke(); return;
                case 15: OnRecycle?.Invoke(); return;
                case 20: OnVerbessern?.Invoke(); return;
                case 21:
                    if (s.Reiter == 2) OnProduzieren?.Invoke();
                    else OnRepair?.Invoke();
                    return;
            }
            if (t >= 1000)
            {
                int k = t - 1000;
                if (s.Reiter == 2) { _prodWahl = k; OnProdWahl?.Invoke(k); QueueRedraw(); }
                // ⭐ Der ZWEITE Klick auf die schon gewaehlte Zeile schaltet die
                // Staffel um (@0x44A028) — nicht ein Doppelklick, sondern
                // wirklich »nochmal auf dieselbe«.
                else if (k == s.HangarWahl) OnStaffelUmschalten?.Invoke(k);
                else OnWahl?.Invoke(k);
            }
        }
        else if (@event is InputEventMouseButton rad
                 && rad.Pressed
                 && rad.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
        {
            int n = s.Reiter == 2 ? s.Angebote.Count : s.HangarZeilen.Count;
            int max = Mathf.Max(0, n - ZeilenSicht);
            _rollstand = Mathf.Clamp(_rollstand
                + (rad.ButtonIndex == MouseButton.WheelUp ? -1 : 1), 0, max);
            QueueRedraw();
            AcceptEvent();
        }
    }

    /// <summary>Die Zugbewegung. ⚠ Verschoben wird der HALTER
    /// (<see cref="BuildingWindow"/>), nicht dieses Kind — und danach wird in
    /// den Schirm gezwungen, damit man es nicht hinausziehen kann.</summary>
    public override void _Input(InputEvent @event)
    {
        if (!_zieht) return;
        if (@event is InputEventMouseMotion mm)
        {
            if (GetParent() is Control eltern)
            {
                eltern.Position += mm.Relative;
                var vp = GetViewportRect().Size;
                eltern.Position = new Vector2(
                    Mathf.Clamp(eltern.Position.X, 0, Mathf.Max(0, vp.X - eltern.Size.X)),
                    Mathf.Clamp(eltern.Position.Y, 0, Mathf.Max(0, vp.Y - eltern.Size.Y)));
            }
            AcceptEvent();
        }
        else if (@event is InputEventMouseButton up
                 && up.ButtonIndex == MouseButton.Left && !up.Pressed)
        { _zieht = false; AcceptEvent(); }
    }

    public override string _GetTooltip(Vector2 pos) => Hit(pos) switch
    {
        -2 => "Fenster schliessen",
        1 => "Reiter »Lager«",
        2 => "Reiter »Hangar«",
        3 => "Reiter »Produktion«",
        10 => "⚠ Startet bei uns OHNE Ziel — die Zielwahl ueber den Kartenschirm "
            + "ist gelesen, aber nicht gebaut.",
        11 => "Patrouille (Befehl 537). Die Flagge ist gelesen, ihre Wirkung im "
            + "Flugtakt nicht.",
        12 => "Handsteuerung — gelesen, noch nicht gebaut.",
        13 => "45 Gasbombe -> 46 Loeschmittel -> 47 Bombe, staffelweit.",
        14 => "Staffelmarke weiterdrehen; zweiter Klick auf die Zeile nimmt auf "
            + "oder heraus.",
        15 => "Recycle — der Arm ist ungelesen, deshalb nicht gebaut.",
        20 => "Der Lagerausbau des Flughafens ist bei uns nicht gebaut.",
        _ => "",
    };

    // Die zwei Schalter liegen am Betrachter; hierher gespiegelt, damit der
    // Zeichner nicht in Rendering greifen muss.
    private static bool MapEntityLayerBombeFest47
        => Rendering.MapEntityLayer.BombeFest47;
    private static bool MapEntityLayerBombenetikettKaputt
        => Rendering.MapEntityLayer.BombenetikettKaputt;
}
