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
    private bool[] BuildingCells(ushort[] code)
    {
        int w = Width, h = Height;
        var claimed = new bool[w * h];
        BuildingCellsSkipped = MissedBuildingCells = 0;
        if (!_tiles.HasBuildings) return claimed;

        foreach (var b in CwmData.Buildings(_map))
        {
            if (b.IsBuilt == 0) continue;                 // scenery / script slot
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
    /// <para><b>Gemessen</b> mit <see cref="ObjectHeights"/> über alle 36
    /// Karten, 13.491 verschiedene Objektbilder: bei <b>20 px</b> — genau der
    /// Zellhöhe <see cref="TileH"/> — sitzen <b>7228 Bilder auf 40.582
    /// Zellen</b>, der mit Abstand grösste Haufen. Alles darunter ist flaches
    /// Bodendetail (1..19 px, zusammen 181 Bilder, mit mehreren Lücken).</para>
    ///
    /// <para>⚠ <b>Anders als bei den Gebäudekacheln gibt es oben KEINE
    /// Lücke</b>: die Verteilung läuft von 20 bis 70 px durch. Ein »gemessener
    /// Sprung« wie bei <c>MapEntityLayer.FlachBisPx</c> ist hier also nicht zu
    /// haben, und das gehört gesagt statt verschwiegen.</para>
    ///
    /// <para><b>Warum trotzdem 25 und nicht 20:</b> 21..25 px sind ein Überstand
    /// von einem bis fünf Bildpunkten — damit lässt sich keine Einheit
    /// verdecken, es kostet aber 3.300 Zellen im lebenden Durchgang. Und 25 ist
    /// die Zahl, die für die GEBÄUDEkacheln bereits gemessen ist
    /// (<c>FlachBisPx</c>); eine Regel für beide ist besser als zwei
    /// Zahlen für dieselbe Frage. ⚠ Insofern ist die 25 hier <b>übernommen</b>,
    /// nicht neu gemessen.</para>
    ///
    /// <para>Damit gehen <b>79.925</b> von 125.116 Objektzellen in die zweite
    /// Ebene, <b>45.191</b> bleiben im Boden.</para></summary>
    public const int RagtAbPx = 25;

    /// <summary>Die zweite Ebene: nur die aufragenden Objekte, alles andere
    /// durchsichtig. <c>null</c>, solange keines vorkam.</summary>
    private byte[]? _objects;

    /// <summary>Wo die aufragenden Objekte in der zweiten Ebene liegen — je
    /// Eintrag die Zelle und das Rechteck im Bild. Der Zeichner braucht beides:
    /// die Zelle für das Zeilenfach, das Rechteck zum Ausschneiden.</summary>
    public readonly List<(int Col, int Row, int X, int Y, int W, int H)> Objects = new();

    /// <summary>Die zweite Ebene als Bild, oder <c>null</c>, wenn kein Objekt
    /// aufragte. ⚠ Erst nach <see cref="Bake"/> gefüllt.</summary>
    public Image? ObjectLayer()
        => _objects == null ? null
         : Image.CreateFromData(PixelW, PixelH, false, Image.Format.Rgba8, _objects);

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
        // ⚠⚠ 18.08.2026 — UND AUFRAGENDE OBJEKTE EBENSO. Gemeldet: »im Original
        // verdecken z. B. auch Baeume Einheiten, bei uns nicht«. Der Grund stand
        // genau hier: ein eingebackener Baum liegt UNTER allem, was danach
        // gezeichnet wird, und kann darum nichts verdecken — im Gegensatz zum
        // Gebaeude, das seit jeher ausgenommen ist.
        //
        // Aufragende Objekte kommen jetzt in eine ZWEITE Ebene mit
        // Durchsichtigkeit (<see cref="ObjectLayer"/>), und der Zeichner setzt
        // sie im Zeilenfach zwischen die Einheiten — dieselbe Loesung wie bei
        // den Gebaeudekacheln (dort: flach in den Boden, Aufragendes ins Fach).
        //
        // Die Schwelle steht bei <see cref="RagtAbPx"/> und ist dort begruendet.
        Objects.Clear();
        var isBuilding = BuildingCells(code);
        if (objects)
            for (int r = 0; r < h; r++)
                for (int c = 0; c < w; c++)
                {
                    int i = r * w + c;
                    if (code[i] < GroundMax) continue;
                    if (isBuilding[i]) { MarkSkipped(ObjectSprite(code[i]), c, r, elev[i]); continue; }
                    var sp = ObjectSprite(code[i]);
                    if (sp != null && TileH - (BlitAnchor + sp.YOff) > RagtAbPx)
                    {
                        BlitTo(_objects ??= new byte[PixelW * PixelH * 4], sp, c, r, elev[i]);
                        Objects.Add((c, r, c * TileW,
                                     OriginY + r * TileH - elev[i] * ElevStep + BlitAnchor + sp.YOff,
                                     sp.W, sp.H));
                        continue;
                    }
                    Blit(sp, c, r, elev[i]);
                }

        return Image.CreateFromData(PixelW, PixelH, false, Image.Format.Rgba8, _canvas);
    }
}
