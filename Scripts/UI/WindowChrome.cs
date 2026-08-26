namespace AkteEuropaReborn.UI;

using Godot;

/// <summary>
/// <b>DIE FENSTERMÖBEL DES ORIGINALS</b> — der Rahmen, den <c>0x455E50</c>
/// zeichnet, und der Knopf, den <c>0x456670</c> zeichnet, beide aus den
/// 20x20-Kacheln von <c>WINDOWS.CWW</c>.
///
/// <para>⭐ <b>Warum es diese Klasse gibt.</b> Gemeldet am 25.08.2026: »Du hast
/// wie ein eigenes Kaufmenü gebaut, das hat nichts mit dem Original zu tun. Da
/// musst du irgendwo die Grafiken finden dazu.« Die Grafiken lagen in
/// <c>WINDOWS.CWW</c>, aus der wir bis dahin nur die acht Bilder der Windfahne
/// genommen hatten. Der Ausgeber ist
/// <see cref="AkteEuropaReborn.Import.InterfaceExporter.WriteWindowChrome"/>,
/// und dort steht auch, welche Kachel wofür da ist und woher das belegt
/// ist.</para>
///
/// <para><b>Das Raster ist 20 Punkte</b>, und zwar überall: die Fenstergrösse
/// jeder Fensterart ist ein Vielfaches davon (Art 31 = 260 x 100 = 13 x 5), der
/// Rahmen belegt die äusserste Kachelreihe ringsum, der Rest ist Innenfläche.
/// Die Kachelnummern hier sind die des Originals, nicht unsere.</para>
///
/// <para>⚠ <b>Die Auswahl ist gewürfelt, aber nicht zufällig.</b> Der Zeichner
/// setzt den Würfel vor jedem Abschnitt neu (<c>srand(W)</c>, <c>srand(W+5)</c>,
/// <c>srand(W+10)</c>, beim Knopf <c>srand(x·y)</c>), damit dasselbe Fenster
/// jedesmal dasselbe Muster trägt. <see cref="Rand"/> ist genau der Würfel der
/// Microsoft-Laufzeit, den die EXE von 1997 benutzt hat — nur so kommt
/// Kachel für Kachel dasselbe heraus.</para>
/// </summary>
public static class WindowChrome
{
    /// <summary>Kantenlänge einer Kachel in Punkten des Originals.</summary>
    public const int Cell = 20;

    /// <summary>Wieviele Kacheln je Zeile im ausgegebenen Bogen stehen —
    /// dieselbe Zahl wie <c>InterfaceExporter.ChromeAtlasCols</c>.</summary>
    public const int AtlasCols = 20;

    // Die Kachelnummern, alle aus 0x455E50 / 0x456670 gelesen.
    public const int LeftEdge = 0;            // 0..2   linker Rand
    public const int TopPlain = 3;            // 3..5   Oberkante ohne Titel
    public const int RightEdge = 6;           // 6..8   rechter Rand
    public const int BottomEdge = 9;          // 9..11  Unterkante
    public const int CornerTopLeftPlain = 12; // Ecke oben links ohne Titel
    public const int CloseBox = 13;           // das Schliesskreuz
    public const int CornerBottomRight = 14;
    public const int CornerBottomLeft = 15;
    public const int Fill = 16;               // 16..24 Innenfläche, neun Muster
    public const int ButtonLeft = 0x19;       // 25..30 (Zustand + 2·Wurf)
    public const int ButtonMid = 0x1F;        // 31..36
    public const int ButtonRight = 0x25;      // 37..42
    public const int TitleLeft = 0x2B;        // 43..45
    public const int TitleMid = 0x2E;         // 46..48
    public const int TitleRight = 0x31;       // 49..51

    /// <summary>Die Farben, mit denen der Zeichner seine Schrift setzt —
    /// Palettenindizes aus <c>DATA/01.PAL</c>, unskaliert.
    ///
    /// <para><b>Titel</b> 0x96/0xa9 (@0x47D3C1/0x47D3C9): das Goldgelb, das der
    /// Spieler im Bildschirmfoto sieht. <b>Fliesstext und Knopfbeschriftung</b>
    /// laufen über 0xFE/0x24 — beim Knopf steht im Befehlsstrom 0x20/0x24, und
    /// <c>0x4BA2D7</c> setzt 0x20 auf 0xFE um: <b>0x20 heisst »die eigene Farbe
    /// des Glyphen«</b>, nicht »grün«. Wer das übersieht, malt die
    /// Knopfbeschriftung türkis.</para></summary>
    public static readonly Color TitleColour = Color.Color8(244, 184, 28);   // 0x96
    public static readonly Color TextColour = Color.Color8(235, 231, 231);   // 0xFE

    private static Texture2D? _atlas;
    private static bool _tried;

    /// <summary>Der Kachelbogen, <c>UI/window_chrome.png</c>. Null, solange der
    /// Spieler seine Inhalte nicht eingelesen hat — jeder Zeichner muss das
    /// abfangen und darf dann NICHTS malen statt etwas Erfundenes.</summary>
    public static Texture2D? Atlas
    {
        get
        {
            if (_tried) return _atlas;
            _tried = true;
            string p = Core.Content.Path("UI/window_chrome.png");
            if (ResourceLoader.Exists(p)) _atlas = ResourceLoader.Load<Texture2D>(p);
            if (_atlas == null && FileAccess.FileExists(p))
            {
                // Eingelesene Inhalte haben keinen Godot-Importschritt, der
                // ResourceLoader sieht sie also nicht — dieselbe Falle wie bei
                // der Schrift und den Zugbildern.
                var img = Image.LoadFromFile(p);
                if (img != null) _atlas = ImageTexture.CreateFromImage(img);
            }
            if (_atlas == null)
                GD.Print("WindowChrome: UI/window_chrome.png fehlt — "
                         + "die Fenster bleiben bei den Godot-Moebeln");
            return _atlas;
        }
    }

    /// <summary>Erneut nachsehen, nachdem eingelesen wurde.</summary>
    public static void Forget() { _tried = false; _atlas = null; }

    /// <summary>
    /// <b>Der Würfel der Microsoft-Laufzeit</b>, Zeichen für Zeichen:
    /// <c>seed = seed·214013 + 2531011</c>, Rückgabe <c>(seed &gt;&gt; 16) &amp;
    /// 0x7FFF</c>. Ein anderer Würfel gäbe eine andere Musterung — sie sähe
    /// stimmig aus und wäre trotzdem nicht die des Originals.
    /// </summary>
    public struct Rand
    {
        private uint _s;
        public Rand(int seed) { _s = unchecked((uint)seed); }
        public int Next()
        {
            _s = unchecked(_s * 214013u + 2531011u);
            return (int)((_s >> 16) & 0x7FFF);
        }
        public int Mod(int n) => Next() % n;
    }

    /// <summary>Der Ausschnitt einer Kachel im Bogen.</summary>
    public static Rect2 Source(int tile)
        => new(new Vector2(tile % AtlasCols * Cell, tile / AtlasCols * Cell),
               new Vector2(Cell, Cell));

    /// <summary>Eine Kachel, in Fensterpunkten des Originals angegeben.</summary>
    public static void Tile(CanvasItem ci, int tile, int x, int y, int scale)
    {
        var tex = Atlas;
        if (tex == null) return;
        ci.DrawTextureRectRegion(
            tex, new Rect2(x * scale, y * scale, Cell * scale, Cell * scale),
            Source(tile));
    }

    /// <summary>
    /// <b>Der Fensterrahmen</b>, Kachel für Kachel in der Reihenfolge des
    /// Originals (0x455E50). <paramref name="w"/> und <paramref name="h"/> sind
    /// KACHELN, nicht Punkte.
    ///
    /// <para>⚠ Die Reihenfolge ist nicht beliebig: die Innenfläche wird NACH
    /// der Unterkante gemalt und das Schliesskreuz zuletzt, weil sie im
    /// Original denselben Würfelstrom teilen. Wer umsortiert, bekommt andere
    /// Kacheln.</para>
    /// </summary>
    public static void Paint(CanvasItem ci, int w, int h, int scale, bool titled = true)
    {
        if (Atlas == null || w <= 2 || h <= 2) return;

        var r = new Rand(w);                                   // srand(W) @0x455E61
        for (int i = 1; i < h - 1; i++)
        {
            Tile(ci, LeftEdge + r.Mod(3), 0, Cell * i, scale);
            Tile(ci, RightEdge + r.Mod(3), Cell * (w - 1), Cell * i, scale);
        }

        r = new Rand(w + 5);                                   // srand(W+5) @0x455F21
        if (titled)
        {
            for (int c = 1; c < w - 2; c++)
                Tile(ci, TitleMid + r.Mod(3), Cell * c, 0, scale);
            Tile(ci, TitleLeft + r.Mod(3), 0, 0, scale);
            Tile(ci, TitleRight + r.Mod(3), Cell * (w - 2), 0, scale);
        }
        else
        {
            Tile(ci, CornerTopLeftPlain, 0, 0, scale);
            for (int c = 1; c < w - 1; c++)
                Tile(ci, TopPlain + r.Mod(3), Cell * c, 0, scale);
        }
        for (int c = 1; c < w - 1; c++)
            Tile(ci, BottomEdge + r.Mod(3), Cell * c, Cell * (h - 1), scale);
        Tile(ci, CornerBottomRight, Cell * (w - 1), Cell * (h - 1), scale);
        for (int y = 1; y < h - 1; y++)
            for (int c = 1; c < w - 1; c++)
                Tile(ci, Fill + r.Mod(9), Cell * c, Cell * y, scale);

        r = new Rand(w + 10);                                  // srand(W+10) @0x456192
        Tile(ci, CloseBox, Cell * (w - 1), 0, scale);
        Tile(ci, CornerBottomLeft, 0, Cell * (h - 1), scale);
    }

    /// <summary>
    /// <b>Ein Knopf</b> (0x456670): linke Kappe, <paramref name="wTiles"/>-2
    /// Mittelstücke, rechte Kappe — alles 20 Punkte hoch.
    /// <paramref name="pressed"/> ist der Zustand, den das Original als
    /// <c>word</c> aus dem Fenstersatz holt und auf die Kachelnummer addiert.
    /// </summary>
    public static void PaintButton(CanvasItem ci, int x, int y, int wTiles,
                                   int scale, bool pressed = false)
    {
        if (Atlas == null || wTiles < 2) return;
        int st = pressed ? 1 : 0;
        var r = new Rand(x * y);                               // srand(x*y) @0x45668C
        Tile(ci, ButtonLeft + st + 2 * r.Mod(3), x, y, scale);
        Tile(ci, ButtonRight + st + 2 * r.Mod(3), x + Cell * (wTiles - 1), y, scale);
        for (int i = 1; i < wTiles - 1; i++)
            Tile(ci, ButtonMid + st + 2 * r.Mod(3), x + Cell * i, y, scale);
    }

    /// <summary>Die Schrift des Originals, <c>UI/akte_font.fnt</c>. Sie wird
    /// hier selbst geladen, weil die Gebäudefenster nicht über
    /// <c>MapViewer.ApplyLegacyFont</c> laufen.</summary>
    public static Font? LegacyFont
    {
        get
        {
            if (_fontTried) return _font;
            _fontTried = true;
            string path = Core.Content.Path("UI/akte_font.fnt");
            if (ResourceLoader.Exists(path)) _font = ResourceLoader.Load<Font>(path);
            if (_font == null && FileAccess.FileExists(path))
            {
                var bmp = new FontFile();
                if (bmp.LoadBitmapFont(path) == Error.Ok) _font = bmp;
            }
            return _font;
        }
    }

    private static Font? _font;
    private static bool _fontTried;

    /// <summary>Die Zellhöhe der Originalschrift (FONT.CWD, 13 Punkte).</summary>
    public const int FontCell = 13;
}
