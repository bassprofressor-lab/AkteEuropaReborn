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

    // Der INNENRAHMEN (0x456A50), gelesen am 09.09.2026 — siehe
    // <see cref="PaintInnerFrame"/>. Acht Kacheln, keine Musterwahl.
    public const int InnerLeft = 0x34;         // 52  linke Kante
    public const int InnerTop = 0x35;          // 53  Oberkante
    public const int InnerRight = 0x36;        // 54  rechte Kante
    public const int InnerBottom = 0x37;       // 55  Unterkante
    public const int InnerTopLeft = 0x38;      // 56
    public const int InnerTopRight = 0x39;     // 57
    public const int InnerBottomRight = 0x3A;  // 58
    public const int InnerBottomLeft = 0x3B;   // 59

    // Der Rollbalken (0x456FF0), gelesen am 09.09.2026 — siehe
    // <see cref="PaintScrollbar"/>.
    public const int ScrollCap = 0x3C;        // 60  obere Kappe
    public const int ScrollTrack = 0x3D;      // 61..62  die Bahn, zwei Muster
    public const int ScrollBottom = 0x3F;     // 63  untere Kappe
    public const int ScrollGrip = 0x40;       // 64  der Griff

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

    /// <summary>Die drei Farben der LISTENFENSTER, aus denselben
    /// Palettenplätzen gelesen wie die zwei darüber (09.09.2026, <c>01.PAL</c>).
    ///
    /// <para><b>0x8C</b> zieht in Art 27 die Trennlinie unter den Spaltenköpfen
    /// (@0x47BC97) und den Auswahlbalken (@0x47BEEB). <b>0x97</b> und
    /// <b>0x9B</b> färben den Zustandsbruch, sobald er unter 50 % bzw. unter
    /// 10 % fällt (@0x47C04D, @0x47C041).</para>
    ///
    /// <para>⚠ Das Original nennt jeweils ein PAAR (0x97/0x99, 0x9B/0x9D), weil
    /// sein Textzeichner zweifarbig malt. Wir malen einfarbig und nehmen den
    /// ersten — genau wie beim Titel, der 0x96 aus dem Paar 0x96/0xA9
    /// nimmt.</para></summary>
    public static readonly Color LineColour = Color.Color8(127, 119, 99);    // 0x8C
    public static readonly Color WarnColour = Color.Color8(243, 150, 35);    // 0x97
    public static readonly Color AlarmColour = Color.Color8(216, 44, 52);    // 0x9B

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

    /// <summary>
    /// <b>DER INNENRAHMEN</b> (<c>0x456A50</c>, Stummel <c>0x4024AF</c>) — der
    /// vertiefte Kasten IM Fenster, in dem die Forschungsergebnisse (Art 29)
    /// ihre Namen zeigen.
    ///
    /// <para><c>0x456A50(x, y, Kacheln breit, Kacheln hoch, Fläche, Breite)</c>,
    /// acht Kacheln und <b>keine</b> Musterwahl:</para>
    /// <code>
    ///   0x456AD5  0x35 Oberkante    auf (x + 20·i, y),           i = 1 … w−2
    ///   0x456AEA  0x37 Unterkante   auf (x + 20·i, y + 20·(h−1))
    ///   0x456B39  0x34 linke Kante  auf (x,               y + 20·j), j = 1 … h−2
    ///   0x456B4A  0x36 rechte Kante auf (x + 20·(w−1),    y + 20·j)
    ///   0x456BBD  0x38 oben  links     0x456BA7  0x39 oben  rechts
    ///   0x456B95  0x3B unten links     0x456B83  0x3A unten rechts
    /// </code>
    ///
    /// <para>⚠ Unter 2 Kacheln in einer Richtung malt er GAR NICHTS
    /// (<c>cmp di,1 / jle</c> @0x456A7B, dasselbe für die Höhe @0x456A85), und
    /// die Innenfläche lässt er frei — dort steht die Füllung des Fensters
    /// selbst.</para>
    ///
    /// <para>⚠ <c>srand(w·h)</c> @0x456A73 steht da, aber <b>kein einziges
    /// <c>rand()</c> folgt</b>: der Rahmen hat keine Musterwahl. Der Würfel wird
    /// nur gestellt und am Ende wieder auf <c>time(0)</c> gesetzt — ein Rest aus
    /// einer Vorlage, kein Verhalten, das wir nachbauen müssten.</para>
    /// </summary>
    public static void PaintInnerFrame(CanvasItem ci, int x, int y,
                                       int wTiles, int hTiles, int scale)
    {
        if (Atlas == null || wTiles <= 1 || hTiles <= 1) return;   // @0x456A7B/85
        int rechts = x + Cell * (wTiles - 1), unten = y + Cell * (hTiles - 1);

        for (int i = 1; i < wTiles - 1; i++)                       // @0x456ACB
        {
            Tile(ci, InnerTop, x + Cell * i, y, scale);
            Tile(ci, InnerBottom, x + Cell * i, unten, scale);
        }
        for (int j = 1; j < hTiles - 1; j++)                       // @0x456B2F
        {
            Tile(ci, InnerLeft, x, y + Cell * j, scale);
            Tile(ci, InnerRight, rechts, y + Cell * j, scale);
        }
        Tile(ci, InnerBottomRight, rechts, unten, scale);          // @0x456B83
        Tile(ci, InnerBottomLeft, x, unten, scale);                // @0x456B95
        Tile(ci, InnerTopRight, rechts, y, scale);                 // @0x456BA7
        Tile(ci, InnerTopLeft, x, y, scale);                       // @0x456BBD
    }

    /// <summary>
    /// <b>DER ROLLBALKEN</b> (<c>0x456FF0</c>, Stummel <c>0x402581</c>), Kachel
    /// für Kachel. Alle drei Listenfenster des Originals — Einheitenliste
    /// (Art 22), Gebäudeliste (Art 27), Forschungsergebnisse (Art 29) — rufen
    /// denselben; darum steht er hier und nicht in einem einzelnen Fenster.
    ///
    /// <para><c>0x456FF0(x, y, Kacheln, Rollstand, Anzahl, Fläche, Breite)</c>:</para>
    /// <code>
    ///   0x457010  cmp si, 2 / jle       ; unter 3 Kacheln wird NICHTS gemalt
    ///   0x457003  srand(x·y)            ; derselbe Wuerfel wie beim Knopf
    ///   0x457027  Kachel 0x3C  auf (x, y)                      obere Kappe
    ///   0x457046  Kachel 0x3F  auf (x, y + 20·(Kacheln−1))     untere Kappe
    ///   0x457068  Kachel 0x3D + rand()%2  auf (x, y + 20·i), i = 1 … Kacheln−2
    ///   0x4570AB  Kachel 0x40  auf (x, y + 20 + (Kacheln−3)·20·Stand/(Anzahl−1))
    /// </code>
    ///
    /// <para>⚠ <b>Die Kappen sind keine Knöpfe.</b> Sie werden gemalt, aber
    /// <see cref="ScrollHit"/> trifft sie nicht — der ganze Klickstreifen ist
    /// genau der Weg, den der GRIFF zurücklegen kann. Das ist keine Vermutung:
    /// die drei Trefferfelder stossen lückenlos aneinander und decken zusammen
    /// <c>y+20 … y+20·(Kacheln−1)</c> ab, also genau die Griffbahn.</para>
    ///
    /// <para>⚠ <b>Zwei eigene Setzungen, beide zum Schutz.</b> Das Original
    /// teilt bei <c>Anzahl = 1</c> durch null (<c>idiv</c> @0x4570C5) und
    /// begrenzt den Stand nicht — ein zu grosser Stand malt den Griff aus dem
    /// Fenster heraus. Wir fangen beides ab. Aufrufer des Originals kommen nie
    /// dorthin, weil sie den Balken erst ab <c>Anzahl ≥ 2</c> überhaupt
    /// zeichnen; wer ihn hier ohne diese Bedingung ruft, soll trotzdem kein
    /// Loch sehen.</para>
    ///
    /// <para>⚠ <c>0x4570DB</c> setzt zum Schluss <c>srand(time(0))</c> — das
    /// Original gibt dem Spiel seinen Würfel zurück, nachdem es ihn für die
    /// Bahnmuster festgenagelt hat. Bei uns ist <see cref="Rand"/> ein
    /// örtlicher Wert, also entfällt das ersatzlos.</para>
    /// </summary>
    /// <param name="hTiles">Höhe des Balkens in KACHELN (Art 22/27: 11,
    /// Art 29: 9).</param>
    /// <param name="stand">Der Rollstand, 0 … <paramref name="anzahl"/>−1.</param>
    /// <param name="anzahl">Wieviele Stellungen es gibt — die Aufrufer des
    /// Originals geben <c>Einträge − (Zeilen−1)</c>.</param>
    public static void PaintScrollbar(CanvasItem ci, int x, int y, int hTiles,
                                      int stand, int anzahl, int scale)
    {
        if (Atlas == null || hTiles <= 2 || anzahl <= 0) return;

        var r = new Rand(x * y);                               // srand(x*y) @0x457003
        Tile(ci, ScrollCap, x, y, scale);                      // @0x457027
        Tile(ci, ScrollBottom, x, y + Cell * (hTiles - 1), scale);   // @0x457046
        for (int i = 1; i < hTiles - 1; i++)                   // @0x457068…0x4570A4
            Tile(ci, ScrollTrack + r.Mod(2), x, y + Cell * i, scale);

        Tile(ci, ScrollGrip, x, ScrollGripY(y, hTiles, stand, anzahl), scale);
    }

    /// <summary>Das y des Griffs, <c>y + 20 + (Kacheln−3)·20·Stand/(Anzahl−1)</c>
    /// (@0x4570AD…0x4570CD).
    ///
    /// <para>⚠ UNSERE Setzung ist nur der Schutz vor der Null und die
    /// Begrenzung des Standes — die Rechnung selbst ist die des Originals.
    /// Eigene Methode, damit der Prüfstand dieselbe Zeile misst, die auch
    /// malt.</para></summary>
    public static int ScrollGripY(int y, int hTiles, int stand, int anzahl)
    {
        int spanne = ScrollSpan(hTiles);
        int g = anzahl > 1
            ? spanne * Mathf.Clamp(stand, 0, anzahl - 1) / (anzahl - 1)
            : 0;
        return y + Cell + g;
    }

    /// <summary>Die Griffbahn in Punkten: <c>(Kacheln−3)·20</c>
    /// (@0x4570AD, und dieselbe Zahl noch einmal @0x45720E als Höhe des
    /// Trefferfelds).</summary>
    public static int ScrollSpan(int hTiles) => Mathf.Max(0, (hTiles - 3) * Cell);

    /// <summary>
    /// <b>Der Klickweg des Rollbalkens</b> (<c>0x457140</c>), die drei
    /// Trefferfelder in der Reihenfolge, in der das Original sie prüft.
    ///
    /// <code>
    ///   0x457198  hoch:  (x, y+20,              20, 10)  → Stand = 0
    ///   0x4571E3  tief:  (x, y+20·Kacheln−30,   20, 10)  → Stand = Anzahl−1
    ///   0x45722F  Bahn:  (x, y+30,   20, (Kacheln−3)·20) → anteilig:
    ///             0x457245  Stand = (Maus_y − y − 30) · Anzahl / ((Kacheln−3)·20)
    /// </code>
    ///
    /// <para>Der Treffertest selbst ist <c>0x455CF0(x, y, w, h)</c> und meint
    /// <c>x ≤ mx &lt; x+w</c>, <c>y ≤ my &lt; y+h</c> — Maus bei
    /// <c>[0x8B62A4]</c>/<c>[0x8B62A0]</c>. Genau dieses »kleiner, nicht
    /// kleinergleich« ist der Grund, warum die drei Felder lückenlos
    /// aneinanderstossen.</para>
    ///
    /// <para>⚠ Das Original prüft zusätzlich <c>word[0x502AC8] ≠ 0</c> (die
    /// Maustaste steht) und wertet den Balken IN DERSELBEN Funktion aus, in der
    /// es ihn zeichnet. Wir trennen beides, wie bei allen unseren Fenstern: der
    /// Aufrufer fragt hier und malt mit <see cref="PaintScrollbar"/>.</para>
    /// </summary>
    /// <param name="punkt">Der Klick in FENSTERPUNKTEN, also schon durch den
    /// Vergrösserungsfaktor geteilt — wie bei <c>MineView.Hit</c>.</param>
    /// <returns>Der neue Rollstand, oder <b>−1</b>, wenn nicht getroffen.</returns>
    public static int ScrollHit(int x, int y, int hTiles, int anzahl, Vector2 punkt)
    {
        if (anzahl <= 0 || hTiles <= 2) return -1;               // @0x45714C, @0x457014

        if (RollbalkenAlt)
        {
            // NULLMODELL --rollbalken-alt: die naheliegende Fehllesung, die
            // Pfeile lägen auf den KAPPEN und wären 20 hoch. Sie sieht auf dem
            // Bild richtig aus — und lässt zwischen den drei Feldern eine
            // Lücke, weil die Bahn erst bei y+30 anfängt. Genau das findet
            // WindowChromeCheck.
            if (Feld(punkt, x, y, Cell, Cell)) return 0;
            if (Feld(punkt, x, y + Cell * (hTiles - 1), Cell, Cell))
                return anzahl - 1;
        }
        else
        {
            if (Feld(punkt, x, y + Cell, Cell, 10)) return 0;        // @0x457198
            if (Feld(punkt, x, y + Cell * hTiles - 30, Cell, 10))    // @0x4571E3
                return anzahl - 1;
        }

        int spanne = ScrollSpan(hTiles);
        if (spanne > 0 && Feld(punkt, x, y + 30, Cell, spanne))  // @0x45722F
            return Mathf.Clamp((int)(punkt.Y - y - 30) * anzahl / spanne,
                               0, anzahl - 1);                   // @0x457245
        return -1;
    }

    /// <summary><c>0x455CF0</c> — Punkt im Rechteck, mit dem <c>&lt;</c> des
    /// Originals an der rechten und unteren Kante.</summary>
    private static bool Feld(Vector2 p, int x, int y, int w, int h)
        => p.X >= x && p.X < x + w && p.Y >= y && p.Y < y + h;

    /// <summary><c>--rollbalken-alt</c> — das NULLMODELL zu
    /// <c>--rollbalken-check</c>: die Trefferfelder der Pfeile liegen auf den
    /// Kappen und sind 20 hoch, statt bei <c>y+20</c> zu beginnen und 10 hoch
    /// zu sein. Das ist die Lesung, die man bekommt, wenn man vom BILD auf den
    /// Klick schliesst — und sie ist falsch.</summary>
    public static bool RollbalkenAlt;

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
