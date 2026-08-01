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
    private const int BlitAnchor = -50;

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

        // pass C — objects, back to front
        if (objects)
            for (int r = 0; r < h; r++)
                for (int c = 0; c < w; c++)
                {
                    int i = r * w + c;
                    if (code[i] < GroundMax) continue;
                    Blit(ObjectSprite(code[i]), c, r, elev[i]);
                }

        return Image.CreateFromData(PixelW, PixelH, false, Image.Format.Rgba8, _canvas);
    }
}
