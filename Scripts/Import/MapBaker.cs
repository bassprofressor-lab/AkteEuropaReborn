namespace AkteEuropaReborn.Import;

using System;
using System.Collections.Generic;
using Godot;

/// <summary>
/// Bakes a map file into the picture the game draws: terrain, the elevation
/// skirt, and the objects on top.
///
/// Ported from cwm_render.py, which is where the placement was worked out:
///
///  * the map is drawn as a RECTANGLE — `x = col*40`, `y = row*20` — the
///    isometric look comes from the tile art, not from a staggered grid;
///  * a raised cell is shifted up by `elev*15`, and a sprite is anchored with
///    `-50 + its own y offset` so its foot sits on the bottom of the cell;
///  * a grid code below 1666 is a terrain frame from the tileset's first
///    directory, a code of 10000 or more an object from the second.
///
/// Three passes, and the middle one is the reason maps stopped having holes:
///
///  0. the ELEVATION SKIRT — a cell raised by E is stacked from level 0 up to
///     E, because the vertical face it exposes underneath would otherwise be
///     see-through. Tiles are 20 px tall against a 15 px step, so the 5 px
///     overlap closes the column completely.
///  A/B. an opaque backdrop under every partial tile, then the cell's own
///     terrain detail.
///  C. the objects, top row to bottom row, so nearer ones overlap farther ones.
///
/// A backdrop tile must genuinely fill a 40x20 cell. Coverage alone is not
/// enough: sliver ridges (40x5) and cliff faces (40x35) report full coverage of
/// their own box and still leave three quarters of the cell empty, which is
/// exactly the bug that produced black gaps before.
/// </summary>
public sealed class MapBaker
{
    public const int TileW = 40, TileH = 20, ElevStep = 15, GroundMax = 1666;
    public const int BlitAnchor = -50;

    private readonly CwmFile _map;
    private readonly CwpFile _tiles;
    private readonly PalFile _pal;

    private sealed class Sprite
    {
        public int W, H, YOff;
        public byte[] Rgba = Array.Empty<byte>();     // premultiplied by nothing: a or 0
        public bool Full;                             // fills a whole ground cell
    }

    private readonly Dictionary<int, Sprite?> _frame = new();
    private readonly Dictionary<int, Sprite?> _object = new();

    public int Width => _map.Width;
    public int Height => _map.Height;
    public int PixelW { get; private set; }
    public int PixelH { get; private set; }

    /// <summary>Wieviele Zellen NUR über die Zeichenlage aus Sektion 20 in den
    /// verzahnten Durchgang kommen (<c>imap == 0xFFFF</c> und Lagenbyte ≥ 100)
    /// — Brücken und Rampen. Über alle Karten gemessen: 578. Steht in der
    /// Meldung, damit ein Neubacken sichtbar macht, ob die Regel greift.
    /// </summary>
    public int LagenZellen;
    public int OriginY { get; private set; }

    public MapBaker(CwmFile map, CwpFile tiles, PalFile pal)
    {
        _map = map; _tiles = tiles; _pal = pal;
    }

    /// <summary>How many object cells the last bake left to the live renderer
    /// because a building stands on them — see <see cref="BuildingCells"/>.</summary>
    public int BuildingCellsSkipped { get; private set; }

    /// <summary>Cells whose object tile the map only carries because a BUILDING
    /// stands there — those the live renderer draws, so they must not be baked
    /// into the picture.</summary>
    public int MissedBuildingCells { get; private set; }

    /// <summary>
    /// The grid cells that belong to a standing building.
    ///
    /// <para><b>Why this exists.</b> The original writes a building's tiles into
    /// the map grid as <c>tile + 0x2710</c> (@0x4C8E2D), so to the baker they
    /// look like any other object and used to be burnt into the map picture. A
    /// destroyed building could then never stop being drawn — the pixels were
    /// part of the map. Now the baker leaves them out and the renderer draws the
    /// building itself, which is what lets it show its RUIN when it falls (see
    /// <see cref="BuildingPatterns.RuinPattern"/>).</para>
    ///
    /// <para>A cell is only claimed when the grid code there is EXACTLY the
    /// building's own tile plus <see cref="CwpFile.ObjectCodeBase"/>. Anything
    /// else the map put on that cell stays baked, and the count of cells that did
    /// NOT match is reported as <see cref="MissedBuildingCells"/> — if a reading
    /// were wrong, that number would not be zero.</para>
    /// </summary>
    /// <summary>
    /// ⭐⭐⭐ 24.08.2026 — <b>DIE KULISSENBAUTEN GEHOEREN IN DIE AUFRAGENDE
    /// SCHICHT, NICHT INS GELAENDEBILD.</b>
    ///
    /// <para>Gemeldet: »ich sehe schon Gebaeude im Fog of War trotzdem«, und
    /// belegt mit einem Bild aus dem Let's Play (8:53): im unerkundeten Gebiet
    /// ist NICHTS als der blanke Boden — kein Baum, keine Kiste, keine Fabrik,
    /// nur ein Kran statt zwei.</para>
    ///
    /// <para>Der Nebelriegel an den Gebaeudesaetzen half nicht, und die Messung
    /// sagte warum: <c>fog-verborgen: 4 von 4 Gebaeuden</c> — sie WAREN
    /// verborgen, das Bauwerk stand trotzdem da. Es steckt im gebackenen
    /// Kartenbild, weil <c>b.IsBuilt == 0</c> hier bisher schlicht uebersprungen
    /// wurde und die Zellen damit ganz normal ins Gelaende geblittet
    /// wurden.</para>
    ///
    /// <para>Ein solcher Bau ist aber kein Boden: er ragt auf, verdeckt
    /// Einheiten und muss im Nebel verschwinden koennen. Er kommt darum in die
    /// zweite Ebene, wie ein Baum. <see cref="Kulisse"/> haelt seine
    /// Zellen.</para>
    /// </summary>
    private bool[] Kulisse = System.Array.Empty<bool>();

    private bool[] BuildingCells(ushort[] code)
    {
        int w = Width, h = Height;
        var claimed = new bool[w * h];
        Kulisse = new bool[w * h];
        BuildingCellsSkipped = MissedBuildingCells = 0;
        if (!_tiles.HasBuildings) return claimed;

        foreach (var b in CwmData.Buildings(_map))
        {
            // ⚠ Kulisse (IsBuilt == 0) wird NICHT beansprucht — der Zeichner
            // stellt sie nicht her —, aber ihre Zellen werden gemerkt, damit
            // der Backofen sie in die zweite Ebene legt statt ins Gelaende.
            if (b.IsBuilt == 0)
            {
                var kt = _tiles.GetBuildingType(b.Type);
                if (kt.IsEmpty) continue;
                for (int x = 0; x < CwpFile.PatternWidth; x++)
                    for (int y = 0; y < CwpFile.PatternHeight; y++)
                    {
                        int t = _tiles.PatternTile(kt.FirstPattern, x, y);
                        if (t == 0) continue;
                        int c = b.Col + x, r = b.Row + y;
                        if (c < 0 || c >= w || r < 0 || r >= h) continue;
                        int i = r * w + c;
                        if (code[i] == t + CwpFile.ObjectCodeBase) Kulisse[i] = true;
                    }
                continue;
            }
            var bt = _tiles.GetBuildingType(b.Type);
            if (bt.IsEmpty) continue;
            for (int x = 0; x < CwpFile.PatternWidth; x++)
                for (int y = 0; y < CwpFile.PatternHeight; y++)
                {
                    int t = _tiles.PatternTile(bt.FirstPattern, x, y);
                    if (t == 0) continue;
                    int c = b.Col + x, r = b.Row + y;
                    if (c < 0 || c >= w || r < 0 || r >= h) continue;
                    int i = r * w + c;
                    if (code[i] == t + CwpFile.ObjectCodeBase)
                    { claimed[i] = true; BuildingCellsSkipped++; }
                    else MissedBuildingCells++;
                }
        }
        return claimed;
    }

    /// <summary>
    /// The pixels the last bake did NOT draw because a building stands there —
    /// one flag per pixel of the finished picture, or null when nothing was
    /// skipped.
    ///
    /// <para>Why a pixel mask and not the cell list: a building tile is far
    /// taller than its cell and hangs upwards over its neighbours, so "which
    /// cells belong to a building" does not say which pixels changed. This is
    /// filled by <see cref="MarkSkipped"/>, which walks exactly the pixels
    /// <see cref="Blit"/> would have written.</para>
    ///
    /// <para>Who needs it: <c>selftest-bake</c> holds our picture against the
    /// Python reference, and that reference still BAKES its buildings. Without
    /// this mask the test can only say "4 % of the pixels differ" and not
    /// whether the ground underneath is still right.</para>
    /// </summary>
    public bool[]? SkippedPixels { get; private set; }

    private void MarkSkipped(Sprite? s, int col, int row, int elev)
    {
        if (s == null) return;
        SkippedPixels ??= new bool[PixelW * PixelH];
        int sx = col * TileW;
        int sy = OriginY + row * TileH - elev * ElevStep + BlitAnchor + s.YOff;
        for (int y = 0; y < s.H; y++)
        {
            int dy = sy + y;
            if (dy < 0 || dy >= PixelH) continue;
            int srcRow = y * s.W * 4;
            for (int x = 0; x < s.W; x++)
            {
                if (s.Rgba[srcRow + x * 4 + 3] == 0) continue;   // transparent
                int dx = sx + x;
                if (dx < 0 || dx >= PixelW) continue;
                SkippedPixels[dy * PixelW + dx] = true;
            }
        }
    }

    private Sprite? Make(CwpFile.Frame? f)
    {
        if (f == null || f.IsEmpty) return null;
        var s = new Sprite { W = f.Width, H = f.Height, YOff = f.YOffset };
        s.Rgba = new byte[f.Width * f.Height * 4];
        int opaque = 0;
        for (int i = 0; i < f.Width * f.Height; i++)
        {
            if (!f.Opaque[i]) continue;
            byte p = f.Pixels[i];
            s.Rgba[i * 4 + 0] = _pal.R[p];
            s.Rgba[i * 4 + 1] = _pal.G[p];
            s.Rgba[i * 4 + 2] = _pal.B[p];
            s.Rgba[i * 4 + 3] = 255;
            opaque++;
        }
        double cov = 100.0 * opaque / (f.Width * f.Height);
        s.Full = cov >= 99.0 && f.Width >= TileW && f.Height >= 18 && f.Height <= 22;
        return s;
    }

    private Sprite? Frame(int i)
    {
        if (_frame.TryGetValue(i, out var s)) return s;
        s = i >= 0 && i < _tiles.FrameCount ? Make(_tiles.DecodeFrame(i)) : null;
        _frame[i] = s;
        return s;
    }

    private Sprite? ObjectSprite(int code)
    {
        if (_object.TryGetValue(code, out var s)) return s;
        s = null;
        try { s = Make(_tiles.DecodeObject(code)); }
        catch (ArgumentOutOfRangeException) { }
        _object[code] = s;
        return s;
    }

    // ---- the canvas --------------------------------------------------------

    private byte[] _canvas = Array.Empty<byte>();

    /// <summary>
    /// <b>WIE HOCH RAGT EIN OBJEKT ÜBER SEINE ZELLE?</b> — die Messung, aus der
    /// die Schwelle für »verdeckt Einheiten« kommen muss.
    ///
    /// <para>Anlass: gemeldet als »im Original verdecken z. B. auch Bäume
    /// Einheiten, bei uns nicht«. Der Grund steht in <see cref="Bake"/>,
    /// Durchgang C: Objekte werden ins Kartenbild <b>eingebacken</b> und liegen
    /// damit unter allem; nur GEBÄUDE sind ausgenommen und werden lebend
    /// gezeichnet. Ein eingebackener Baum <i>kann</i> nichts verdecken.</para>
    ///
    /// <para>Für Gebäudekacheln gibt es die gemessene Schwelle
    /// <c>MapEntityLayer.FlachBisPx = 25</c> — flach bleibt im Boden, Aufragendes
    /// kommt ins Zeilenfach. Für Objekte fehlt die entsprechende Zahl, und
    /// geraten wird sie nicht. Diese Zeile zählt aus, wie weit jedes
    /// vorkommende Objekt über die UNTERKANTE seiner Zelle hinausragt; die
    /// Schwelle gehört dann in eine Lücke der Verteilung, wie bei den
    /// Gebäudekacheln auch.</para>
    ///
    /// <para>Gerechnet wie in <see cref="Blit"/>: das Sprite beginnt bei
    /// <c>row·TileH + BlitAnchor + YOff</c> gegen den Zellursprung, die Zelle
    /// endet bei <c>row·TileH + TileH</c>. Der Überstand nach oben ist also
    /// <c>TileH − (BlitAnchor + YOff)</c> … minus dem, was das Sprite selbst
    /// nach unten reicht — hier gezählt wird die <b>sichtbare Höhe über der
    /// Zellunterkante</b>: <c>(BlitAnchor + YOff + H) </c> ist die Unterkante des
    /// Sprites, und <c>TileH</c> die der Zelle.</para></summary>
    /// <returns>Je Objektcode: Breite, Höhe, YOff und der Überstand über der
    /// Zellunterkante, dazu wie oft der Code auf dieser Karte vorkommt.</returns>
    public List<(int Code, int W, int H, int YOff, int Rise, int Count)> ObjectHeights()
    {
        var zahl = new Dictionary<int, int>();
        var rec = _map.Records;
        for (int i = 0; i < Width * Height; i++)
        {
            int c = BitConverter.ToUInt16(rec, i * 4);
            if (c >= GroundMax) zahl[c] = zahl.TryGetValue(c, out int n) ? n + 1 : 1;
        }

        var raus = new List<(int, int, int, int, int, int)>();
        foreach (var kv in zahl)
        {
            var s = ObjectSprite(kv.Key);
            if (s == null) continue;
            int oben = BlitAnchor + s.YOff;           // Oberkante gegen den Zellursprung
            int rise = TileH - oben;                  // sichtbare Hoehe ueber der Zellunterkante
            raus.Add((kv.Key, s.W, s.H, s.YOff, rise, kv.Value));
        }
        raus.Sort((a, b) => a.Item5 != b.Item5 ? a.Item5 - b.Item5 : a.Item1 - b.Item1);
        return raus;
    }

    /// <summary>
    /// <b>AB WANN RAGT EIN OBJEKT AUF</b> — also ab wann es eine Einheit
    /// verdecken kann und darum nicht mehr in den Boden gebacken werden darf.
    ///
    /// <para>⚠⚠ <b>18.08.2026 — DIE FRAGE WAR FALSCH GESTELLT.</b> Hier stand
    /// bis heute <c>RagtAbPx = 25</c>, und der Kommentar sagte selbst, dass die
    /// 25 von den GEBÄUDEkacheln <b>übernommen</b> und nicht gemessen war. Die
    /// Messung, die sie hätte tragen sollen, gibt es auch gar nicht: über 36
    /// Karten und 13.491 Objektbilder sitzt bei 20 px der grösste Haufen, und
    /// <b>darüber läuft die Verteilung ohne Lücke bis 70 px durch</b> (siehe
    /// <see cref="ObjectHeights"/>). Eine Lücke, in die eine Schwelle gehört,
    /// ist da nicht.</para>
    ///
    /// <para><b>Weil das Original gar keine Höhe fragt.</b> Sein Zeichner
    /// (@0x4B4150) teilt die Zellen nach der BELEGUNGSKARTE auf: der flache
    /// Durchgang @0x4B41EB überspringt jede Zelle ab Belegung 14000, und der
    /// verzahnte Durchgang @0x4B43BB — der, in dem Einheiten und Kacheln
    /// zeilenweise abwechseln — nimmt ausdrücklich die Belegungen
    /// <b>50000..63999</b> (@0x4B446C). Das ist die ganze Regel. Sie steht
    /// samt Adressen bei <see cref="MapForest.ImZeilenfach"/>.</para>
    ///
    /// <para>Gemessen, was der Wechsel ausmacht: von 68.391 Objektzellen der 23
    /// mitgelieferten <c>.CWM</c> kommen jetzt <b>38.306</b> ins Fach (37.231
    /// Wald + 1.075 Objekt) statt der bisherigen Schätzung — und 14.710 Zellen,
    /// die nur Bodenschmuck sind, bleiben im Bild, wo sie hingehören.</para>
    ///
    /// <para>⚠ <see cref="ObjectHeights"/> bleibt stehen. Nicht als Schwelle,
    /// sondern als Prüfstand: er zeigt, dass die Höhe die Frage NICHT
    /// beantworten kann.</para></summary>

    /// <summary>Die zweite Ebene: nur die aufragenden Objekte, alles andere
    /// durchsichtig. <c>null</c>, solange keines vorkam.</summary>
    private byte[]? _objects;

    /// <summary>Wo die aufragenden Objekte in der zweiten Ebene liegen — je
    /// Eintrag die Zelle und das Rechteck im Bild. Der Zeichner braucht beides:
    /// die Zelle für das Zeilenfach, das Rechteck zum Ausschneiden.
    ///
    /// <para><c>Kohle</c> ist der Platz der VERKOHLTEN Fassung im Streifen
    /// (<see cref="BurntAtlas"/>) oder −1, wenn die Zelle kein Wald ist;
    /// <c>KX/KY</c> ist, wo sie hin muss — sie hat einen eigenen Anschlag,
    /// weil der verkohlte Baum ein anderes Bild ist als der grüne.
    /// <c>Asche</c> und <c>AX/AY</c> sind dasselbe für die Kachel, die
    /// ÜBRIGBLEIBT, wenn das Feuer aus ist (Stumpf bzw. blanker Boden).</para>
    /// </summary>
    /// <para>⭐⭐ 24.08.2026 — <c>Imap</c> und <c>Code</c> kamen dazu. Das
    /// Original fuehrt zerstoerbare Kartenobjekte in einer EIGENEN Brandliste
    /// (0xC03A30, 6 Byte je Eintrag) mit einer ART, und die Art entscheidet
    /// ueber die Verhaltensklasse (Arttafel 0xBB3B60) und ueber die
    /// Ersatzkachel: <b>Grundkachel + 10001 = brennt</b>, <b>+ 10002 =
    /// zerstoert</b>. Unser Objektsatz trug bisher nur Lage und Bilder — damit
    /// war das ganze Teilsystem nicht baubar (siehe
    /// Rendering/BrennendeObjekte.cs).
    ///
    /// <para><c>Imap</c> ist der rohe Belegungswert aus Sektion 6: 50000..55999
    /// Wald, 61000..63999 zerstoerbares Objekt — daraus faellt die Art. <c>Code</c>
    /// ist die Kachelnummer derselben Zelle, auf die das Original die
    /// +10001/+10002 rechnet.</para></summary>
    public readonly List<(int Col, int Row, int X, int Y, int W, int H,
                          int Kohle, int KX, int KY,
                          int Asche, int AX, int AY,
                          int Imap, int Code, int Art,
                          int Klasse, int Basis)> Objects = new();

    /// <summary>
    /// <b>DER STREIFEN MIT DEN VERKOHLTEN BÄUMEN.</b>
    ///
    /// <para>Ein brennender Baum ist im Original keine Zutat, sondern ein
    /// KACHELTAUSCH: <c>zapal</c> @0x4CACE5 schreibt die verkohlte Fassung an
    /// die Stelle des grünen Baums (Rechnung und Adressen bei
    /// <see cref="MapForest.Verkohlt"/>). Diese Kachel muss also im Bild
    /// liegen, bevor irgendetwas brennt.</para>
    ///
    /// <para><b>Warum ein Streifen und keine dritte Ebene:</b> beide Fassungen
    /// hängen nur an der Baumart (0, 19 oder 38) und der Geländeart (0..18) —
    /// also höchstens <b>2 × 57</b> verschiedene Bilder je Karte, egal
    /// wie viele Bäume darauf stehen. Eine ganze zweite Leinwand dafür wäre bei
    /// map_01 rund 10 MB für 563 Zellen. Der Streifen hängt darum UNTEN an
    /// <c>&lt;karte&gt;.objects.png</c> an; seine Y-Werte liegen ab
    /// <see cref="PixelH"/>.</para>
    ///
    /// <para>Je Eintrag: der Kachelcode und sein Rechteck im Streifen, dazu
    /// <c>YOff</c> — der Zeichner braucht ihn, um die Kachel gegen die Zelle zu
    /// setzen, und der ist bei der verkohlten Fassung ein anderer als beim
    /// grünen Baum.</para></summary>
    public readonly List<(int Code, int X, int Y, int W, int H, int YOff)> BurntAtlas = new();

    private byte[]? _burnt;                 // der Streifen, PixelW x _burntH
    private int _burntH;

    /// <summary>Die zweite Ebene als Bild, oder <c>null</c>, wenn kein Objekt
    /// aufragte. ⚠ Erst nach <see cref="Bake"/> gefüllt. Ist ein Streifen mit
    /// verkohlten Bäumen entstanden, hängt er unten an.</summary>
    public Image? ObjectLayer()
    {
        if (_objects == null) return null;
        if (_burnt == null || _burntH <= 0)
            return Image.CreateFromData(PixelW, PixelH, false, Image.Format.Rgba8, _objects);
        var alles = new byte[PixelW * (PixelH + _burntH) * 4];
        Array.Copy(_objects, alles, _objects.Length);
        Array.Copy(_burnt, 0, alles, _objects.Length, _burnt.Length);
        return Image.CreateFromData(PixelW, PixelH + _burntH, false, Image.Format.Rgba8, alles);
    }

    /// <summary>Wie <see cref="Blit"/>, aber auf eine übergebene Leinwand — und
    /// ⚠ mit DURCHSICHTIGKEIT: die zweite Ebene liegt über dem Kartenbild, ein
    /// undurchsichtiger Rand würde den Boden ringsum ausstanzen.</summary>
    private void BlitTo(byte[] dst, Sprite? s, int col, int row, int elev)
    {
        if (s == null) return;
        int sx = col * TileW;
        int sy = OriginY + row * TileH - elev * ElevStep + BlitAnchor + s.YOff;
        for (int y = 0; y < s.H; y++)
        {
            int dy = sy + y;
            if (dy < 0 || dy >= PixelH) continue;
            int srcRow = y * s.W * 4;
            int dstRow = dy * PixelW * 4;
            for (int x = 0; x < s.W; x++)
            {
                if (s.Rgba[srcRow + x * 4 + 3] == 0) continue;
                int dx = sx + x;
                if (dx < 0 || dx >= PixelW) continue;
                int d = dstRow + dx * 4, o = srcRow + x * 4;
                dst[d] = s.Rgba[o];
                dst[d + 1] = s.Rgba[o + 1];
                dst[d + 2] = s.Rgba[o + 2];
                dst[d + 3] = 255;
            }
        }
    }

    private void Blit(Sprite? s, int col, int row, int elev)
    {
        if (s == null) return;
        int sx = col * TileW;
        int sy = OriginY + row * TileH - elev * ElevStep + BlitAnchor + s.YOff;
        for (int y = 0; y < s.H; y++)
        {
            int dy = sy + y;
            if (dy < 0 || dy >= PixelH) continue;
            int srcRow = y * s.W * 4;
            int dstRow = dy * PixelW * 4;
            for (int x = 0; x < s.W; x++)
            {
                if (s.Rgba[srcRow + x * 4 + 3] == 0) continue;   // transparent
                int dx = sx + x;
                if (dx < 0 || dx >= PixelW) continue;
                int d = dstRow + dx * 4, o = srcRow + x * 4;
                _canvas[d] = s.Rgba[o];
                _canvas[d + 1] = s.Rgba[o + 1];
                _canvas[d + 2] = s.Rgba[o + 2];
                _canvas[d + 3] = 255;
            }
        }
    }

    /// <summary>Every cell gets the nearest tile that really fills it, so the
    /// bake has an opaque floor under coasts, transitions and object cells.</summary>
    private int[] BuildBase(ushort[] code, byte[] elev)
    {
        int w = Width, h = Height;
        var grid = new int[w * h];
        for (int i = 0; i < w * h; i++)
        {
            grid[i] = -1;
            if (code[i] < GroundMax && Frame(code[i])?.Full == true) grid[i] = code[i];
        }
        var holes = new Queue<int>();
        for (int i = 0; i < w * h; i++) if (grid[i] < 0) holes.Enqueue(i);
        int guard = 0, limit = 200000 + w * h * 4;
        int[] dc = { 1, -1, 0, 0, 1, -1, 1, -1 };
        int[] dr = { 0, 0, 1, -1, 1, -1, -1, 1 };
        while (holes.Count > 0 && guard++ < limit)
        {
            int i = holes.Dequeue();
            if (grid[i] >= 0) continue;
            int c = i % w, r = i / w, found = -1;
            for (int k = 0; k < 8 && found < 0; k++)
            {
                int nc = c + dc[k], nr = r + dr[k];
                if (nc < 0 || nc >= w || nr < 0 || nr >= h) continue;
                if (grid[nr * w + nc] >= 0) found = grid[nr * w + nc];
            }
            if (found < 0) holes.Enqueue(i);
            else grid[i] = found;
        }
        return grid;
    }

    /// <summary>Bake, and hand back the finished picture.</summary>
    public Image Bake(bool fill = true, bool objects = true)
    {
        int w = Width, h = Height;
        var rec = _map.Records;
        var code = new ushort[w * h];
        var elev = new byte[w * h];
        var flag = new byte[w * h];
        int maxElev = 0;
        for (int i = 0; i < w * h; i++)
        {
            int o = i * 4;
            code[i] = BitConverter.ToUInt16(rec, o);
            elev[i] = rec[o + 2];
            flag[i] = rec[o + 3];
            if (elev[i] > maxElev) maxElev = elev[i];
        }

        OriginY = maxElev * ElevStep + 60;
        PixelW = w * TileW;
        PixelH = h * TileH + OriginY + 40;
        _canvas = new byte[PixelW * PixelH * 4];

        var basis = fill ? BuildBase(code, elev) : null;

        // pass 0 — the elevation skirt
        if (basis != null)
            for (int r = 0; r < h; r++)
                for (int c = 0; c < w; c++)
                {
                    int i = r * w + c;
                    if (basis[i] < 0) continue;
                    var s = Frame(basis[i]);
                    for (int lvl = 0; lvl <= elev[i]; lvl++) Blit(s, c, r, lvl);
                }

        // passes A and B — backdrop, then the cell's own detail
        for (int r = 0; r < h; r++)
            for (int c = 0; c < w; c++)
            {
                int i = r * w + c;
                bool isObj = code[i] >= GroundMax;
                int b = basis != null ? basis[i] : -1;
                bool ownIsFull = !isObj && Frame(code[i])?.Full == true;
                if (b >= 0 && !ownIsFull) Blit(Frame(b), c, r, elev[i]);
                if (!isObj) Blit(Frame(code[i]), c, r, elev[i]);
            }

        // pass C — objects, back to front. Buildings are left out: they are
        // drawn live so they can fall down. See BuildingCells.
        //
        // ⚠⚠ 18.08.2026 — UND DIE ZELLEN DES VERZAHNTEN DURCHGANGS EBENSO.
        // Gemeldet: »im Original verdecken z. B. auch Baeume Einheiten, bei uns
        // nicht«. Der Grund stand genau hier: ein eingebackener Baum liegt
        // UNTER allem, was danach gezeichnet wird, und kann darum nichts
        // verdecken — im Gegensatz zum Gebaeude, das seit jeher ausgenommen ist.
        //
        // WELCHE Zellen aufragen, entscheidet seit heute die BELEGUNGSKARTE der
        // Kartendatei und keine geratene Pixelschwelle mehr: das Original
        // zeichnet in seinem verzahnten Durchgang @0x4B43BB ausdruecklich die
        // Belegungen 50000..63999 (@0x4B446C) und sonst nichts. Begruendung,
        // Adressen und Messung stehen bei MapForest.ImZeilenfach.
        //
        // ⚠ Ein WALD kann brennen, und Brennen ist im Original ein
        // Kacheltausch. Darum wandert jede Waldzelle zusaetzlich mit ihrer
        // VERKOHLTEN Fassung in den Streifen — siehe BurntAtlas.
        Objects.Clear();
        BurntAtlas.Clear();
        _burnt = null; _burntH = 0;
        var isBuilding = BuildingCells(code);
        // Kachelcode der verkohlten Fassung -> Platz im Streifen. Hoechstens 57
        // je Karte, darum eine kleine Tafel und kein Bild je Zelle.
        var kohleSlot = new Dictionary<int, int>();
        var kohleSpr = new List<Sprite>();

        // Eine Ersatzkachel in den Streifen legen — oder ihren Platz
        // wiederfinden, denn dieselben paar Codes kommen tausendfach vor.
        // −1, wenn der Kachelsatz sie nicht hat; dann bleibt der Baum eben
        // stehen, statt dass ein Loch entsteht.
        int Streifenplatz(int tileCode)
        {
            if (kohleSlot.TryGetValue(tileCode, out int slot)) return slot;
            var ks = ObjectSprite(tileCode);
            if (ks == null) return -1;
            slot = kohleSpr.Count;
            kohleSlot[tileCode] = slot;
            kohleSpr.Add(ks);
            BurntAtlas.Add((tileCode, 0, 0, ks.W, ks.H, ks.YOff));
            return slot;
        }
        if (objects)
            for (int r = 0; r < h; r++)
                for (int c = 0; c < w; c++)
                {
                    int i = r * w + c;
                    if (code[i] < GroundMax) continue;
                    if (isBuilding[i]) { MarkSkipped(ObjectSprite(code[i]), c, r, elev[i]); continue; }
                    var sp = ObjectSprite(code[i]);
                    int imap = MapForest.Imap(_map, c, r);
                    // ⚠⚠ 19.08.2026 — die ZEICHENLAGE aus Sektion 20 entscheidet
                    // mit. Siehe MapForest.ImZeilenfach; sie bringt die 578
                    // Zellen mit `imap == 0xFFFF` herein, die das Original
                    // aufragen laesst und wir bisher haben durchfallen lassen.
                    //
                    // ⚠ DIESE AENDERUNG WIRKT ERST NACH EINEM NEUEN EINLESEN.
                    // Die gebackenen Karten liegen fertig im Nutzerordner; wer
                    // sie nicht neu backt, sieht nichts davon. Genau das ist
                    // schon einmal als »Baeume verdecken nicht« gemeldet worden
                    // und wurde zweimal falsch erklaert. Damit es beim naechsten
                    // Mal auffaellt, wird gezaehlt und gesagt.
                    int lage = MapForest.Lage(_map, c, r);
                    if (imap == 0xFFFF && lage >= 100) LagenZellen++;
                    // ⚠ 24.08.2026 — hier stand `|| Kulisse[i]`, damit
                    // Kulissenbauten in die zweite Ebene wandern und im Nebel
                    // verschwinden koennen. Zurueckgenommen am selben Abend:
                    // unter ihren Zellen liegt kein gemalter Boden (siehe
                    // Durchgang A/B), also blieb ein Loch. Die Zellenliste
                    // `Kulisse` bleibt stehen — sie ist richtig gelesen und die
                    // halbe Arbeit fuer den Tag, an dem der Boden da ist.
                    if (sp != null && MapForest.ImZeilenfach(imap, lage))
                    {
                        BlitTo(_objects ??= new byte[PixelW * PixelH * 4], sp, c, r, elev[i]);

                        // Die verkohlte Fassung — nur fuer WALD; ein
                        // zerstoerbares Objekt (61000..) hat eine andere
                        // Mechanik (Schadensstufen, @0x40D4FB) und ist hier
                        // nicht dran.
                        int kohle = -1, kx = 0, ky = 0;
                        int asche = -1, ax = 0, ay = 0;
                        if (MapForest.IstWald(imap))
                        {
                            // Zwei Ersatzkacheln je Waldzelle: die VERKOHLTE,
                            // solange sie brennt (zapal @0x4CACE5), und die
                            // ABGEBRANNTE, wenn das Feuer aus ist (Brandtakt
                            // @0x4CA424, Protokollzeile »dohorel forest«).
                            kohle = Streifenplatz(MapForest.Verkohlt(code[i], flag[i]));
                            asche = Streifenplatz(MapForest.Abgebrannt(code[i], flag[i]));
                            if (kohle >= 0)
                            {
                                kx = c * TileW;
                                ky = OriginY + r * TileH - elev[i] * ElevStep
                                     + BlitAnchor + kohleSpr[kohle].YOff;
                            }
                            if (asche >= 0)
                            {
                                ax = c * TileW;
                                ay = OriginY + r * TileH - elev[i] * ElevStep
                                     + BlitAnchor + kohleSpr[asche].YOff;
                            }
                        }
                        // ⭐⭐⭐ 24.08.2026 — DIE ZWEI ERSATZBILDER EINES
                        // ZERSTOERBAREN OBJEKTS, nach demselben Muster wie beim
                        // Wald. Das Original tauscht die Kachel:
                        //   Grundkachel + 10001 = brennt      (@0x4CA59B)
                        //   Grundkachel + 10002 = zerstoert   (@0x4CA772)
                        // und weil `code = 10000 + Grundkachel` ist (an allen
                        // fuenf Objekten von map_01 nachgeprueft), sind das
                        // schlicht die beiden NAECHSTEN Kachelcodes.
                        //
                        // ⚠ Sie gehen in dieselben Felder wie beim Wald
                        // (burnt/ash): der Zeichner kann dann beides gleich
                        // behandeln. Unterschieden wird ueber `imap`, nicht
                        // ueber »hat eine verkohlte Kachel«.
                        if (imap >= 61000 && imap < 64000)
                        {
                            kohle = Streifenplatz(code[i] + 1);
                            asche = Streifenplatz(code[i] + 2);
                            if (kohle >= 0)
                            {
                                kx = c * TileW;
                                ky = OriginY + r * TileH - elev[i] * ElevStep
                                     + BlitAnchor + kohleSpr[kohle].YOff;
                            }
                            if (asche >= 0)
                            {
                                ax = c * TileW;
                                ay = OriginY + r * TileH - elev[i] * ElevStep
                                     + BlitAnchor + kohleSpr[asche].YOff;
                            }
                        }

                        // ⭐⭐⭐ 24.08.2026 — DIE ART EINES ZERSTOERBAREN OBJEKTS.
                        //
                        // Der Belegungswert eines solchen Objekts ist
                        // 61000 + INDEX in eine eigene Liste; das Original haelt
                        // sie bei 0xC03A30, 6 Byte je Eintrag, rund 2000 Plaetze
                        // (0xC03A30…0xC06912, Anzahl @0x41F2E1). Und das ist
                        // SEKTION 4 der Kartendatei: 0x2EE0 = 12000 Byte
                        // = 2000 x 6, byteweise dieselbe Groesse.
                        //
                        // Der Eintrag: +0 Spalte, +1 Zeile, +2 ART, +3 Zustand.
                        // Die ART ist die Zeile in der Arttafel 0xBB3B60 und
                        // entscheidet ueber Verhaltensklasse und Grundkachel —
                        // siehe Rendering/BrennendeObjekte.cs.
                        int art = -1;
                        if (imap >= 61000 && imap < 64000)
                        {
                            var s4 = _map.Sec(4);
                            int k = (imap - 61000) * 6;
                            if (s4 != null && k + 3 < s4.Length) art = s4[k + 2];
                        }
                        // ⭐ Und gleich aufgeloest: Verhaltensklasse und
                        // Grundkachel stehen in der Arttafel der KACHELDATEI
                        // (CwpFile.ObjType). Sie hier nachzuschlagen erspart es,
                        // die Tafel je Tileset in die Laufzeit zu schleppen —
                        // und sie ist je Tileset eine andere, waere dort also
                        // ein zweiter Zuordnungskreis.
                        int klasse = -1, grundkachel = -1;
                        if (art >= 0) (klasse, grundkachel) = _tiles.ObjType(art);
                        Objects.Add((c, r, c * TileW,
                                     OriginY + r * TileH - elev[i] * ElevStep + BlitAnchor + sp.YOff,
                                     sp.W, sp.H, kohle, kx, ky, asche, ax, ay,
                                     imap, code[i], art, klasse, grundkachel));
                        continue;
                    }
                    Blit(sp, c, r, elev[i]);
                }

        // Der Streifen: die gesammelten verkohlten Bilder nebeneinander, mit
        // Umbruch an PixelW. Er haengt unten an der zweiten Ebene an, seine
        // Y-Werte beginnen darum bei PixelH.
        if (kohleSpr.Count > 0)
        {
            int zx = 0, zy = 0, zeileH = 0;
            for (int k = 0; k < kohleSpr.Count; k++)
            {
                var ks = kohleSpr[k];
                if (zx + ks.W > PixelW && zx > 0) { zx = 0; zy += zeileH; zeileH = 0; }
                var e = BurntAtlas[k];
                BurntAtlas[k] = (e.Code, zx, PixelH + zy, ks.W, ks.H, ks.YOff);
                zx += ks.W;
                if (ks.H > zeileH) zeileH = ks.H;
            }
            _burntH = zy + zeileH;
            _burnt = new byte[PixelW * _burntH * 4];
            for (int k = 0; k < kohleSpr.Count; k++)
            {
                var ks = kohleSpr[k];
                var e = BurntAtlas[k];
                for (int y = 0; y < ks.H; y++)
                {
                    int dy = e.Y - PixelH + y;
                    if (dy < 0 || dy >= _burntH) continue;
                    for (int x = 0; x < ks.W; x++)
                    {
                        int o = (y * ks.W + x) * 4;
                        if (ks.Rgba[o + 3] == 0) continue;
                        int dx = e.X + x;
                        if (dx < 0 || dx >= PixelW) continue;
                        int d = (dy * PixelW + dx) * 4;
                        _burnt[d] = ks.Rgba[o];
                        _burnt[d + 1] = ks.Rgba[o + 1];
                        _burnt[d + 2] = ks.Rgba[o + 2];
                        _burnt[d + 3] = 255;
                    }
                }
            }
            // ⚠ Ohne aufragende Objekte gibt es keine zweite Ebene, an die der
            // Streifen haengen koennte — und ohne Waldzelle gibt es keinen
            // Streifen. Beides zusammen kann also nicht vorkommen; die Zusage
            // steht hier, damit ObjectLayer sie nicht pruefen muss.
            _objects ??= new byte[PixelW * PixelH * 4];
        }

        return Image.CreateFromData(PixelW, PixelH, false, Image.Format.Rgba8, _canvas);
    }
}
