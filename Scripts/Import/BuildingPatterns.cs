namespace AkteEuropaReborn.Import;

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Godot;
using GDict = Godot.Collections.Dictionary;

/// <summary>
/// The building patterns of one tileset, as the engine sees them at run time.
///
/// <para>The originals live in the .CWP — the loader reads 100 type rows of 10
/// bytes and 400 patterns of 180 right behind the pixel blob (see
/// <see cref="CwpFile"/>). The engine never opens a .CWP, so the import writes
/// them out per tileset and this class reads them back.</para>
///
/// <para>Both sides implement <see cref="IBuildingPatterns"/>, which is what
/// lets the self-test hold the exported file against the original file cell by
/// cell instead of trusting the exporter.</para>
/// </summary>
public sealed class BuildingPatterns : IBuildingPatterns
{
    private readonly Dictionary<int, CwpFile.BuildingType> _types = new();
    private readonly Dictionary<int, ushort[]> _tiles = new();   // pattern -> 60 tiles
    private readonly Dictionary<int, bool[]> _blocks = new();    // pattern -> 60 flags
    private readonly Dictionary<int, CwpFile.CellAnim> _anims = new();  // row -> animation

    public int Tileset { get; private set; }
    public bool HasBuildings => _types.Count > 0;

    public IEnumerable<int> Types => _types.Keys;

    public CwpFile.BuildingType GetBuildingType(int typ)
        => _types.TryGetValue(typ, out var t) ? t : default;

    public int PatternTile(int pattern, int x, int y)
        => (uint)x < CwpFile.PatternWidth && (uint)y < CwpFile.PatternHeight
           && _tiles.TryGetValue(pattern, out var a)
           ? a[x * CwpFile.PatternHeight + y] : 0;

    public bool PatternBlocks(int pattern, int x, int y)
        => (uint)x < CwpFile.PatternWidth && (uint)y < CwpFile.PatternHeight
           && _blocks.TryGetValue(pattern, out var a)
           && a[x * CwpFile.PatternHeight + y];

    public CwpFile.CellAnim GetAnimRow(int row)
        => _anims.TryGetValue(row, out var a) ? a : default;

    // ---- writing (import side) ---------------------------------------------

    /// <summary>
    /// The buildings of one tileset as JSON. Only the cells that carry a tile
    /// or block are written — over the shipped files a pattern holds about
    /// fifteen of its sixty, so the sparse form is both smaller and easier to
    /// read than sixty mostly-zero numbers.
    /// </summary>
    public static string Write(CwpFile cwp, int tileset)
    {
        var sb = new StringBuilder(1 << 16);
        sb.Append("{\"_note\":\"building patterns of tileset ").Append(tileset);
        sb.Append(", from <nn>.CWP: 100 type rows of 10 bytes and 400 patterns of 180 ");
        sb.Append("behind the pixel blob. A pattern is 10x6; its first 120 bytes are ");
        sb.Append("tiles (u16, add 10000 for the grid code), its last 60 the block mask ");
        sb.Append("(only 0 and 255 occur). The two rasters differ on purpose: the ");
        sb.Append("build-site test walks the TILES, add_building stamps the imap from ");
        sb.Append("the MASK, so roof and facade hang over walkable ground.\",");
        sb.Append("\"tileset\":").Append(tileset).Append(',');
        sb.Append("\"pattern_w\":").Append(CwpFile.PatternWidth);
        sb.Append(",\"pattern_h\":").Append(CwpFile.PatternHeight).Append(',');
        sb.Append("\"types\":[");

        bool firstType = true;
        for (int typ = 0; typ < CwpFile.BuildingTypeCount; typ++)
        {
            var bt = cwp.GetBuildingType(typ);
            if (bt.IsEmpty) continue;
            if (!firstType) sb.Append(',');
            firstType = false;
            sb.Append("{\"typ\":").Append(typ);
            sb.Append(",\"count\":").Append(bt.PatternCount);
            sb.Append(",\"first\":").Append(bt.FirstPattern);
            sb.Append(",\"tile_pattern\":").Append(bt.TilePattern);
            sb.Append(",\"anim_count\":").Append(bt.AnimCount);
            sb.Append(",\"anim_first\":").Append(bt.AnimFirst);

            // the type's animation rows, written out with the row index so the
            // engine and the self-test can address them the way the original
            // does — the index is what the type row points at.
            if (bt.AnimCount > 0)
            {
                sb.Append(",\"anims\":[");
                for (int k = 0; k < bt.AnimCount; k++)
                {
                    int row = bt.AnimFirst + k;
                    var a = cwp.GetAnimRow(row);
                    if (k > 0) sb.Append(',');
                    sb.Append("{\"i\":").Append(row);
                    sb.Append(",\"dx\":").Append(a.Dx).Append(",\"dy\":").Append(a.Dy);
                    sb.Append(",\"mode\":").Append(a.Mode);
                    sb.Append(",\"last\":").Append(a.LastPhase);
                    sb.Append(",\"tiles\":[");
                    for (int t = 0; t < CwpFile.AnimTileCount; t++)
                    {
                        if (t > 0) sb.Append(',');
                        sb.Append(a.Tiles == null ? 0 : a.Tiles[t]);
                    }
                    sb.Append("]}");
                }
                sb.Append(']');
            }

            sb.Append(",\"patterns\":[");

            // every pattern the type owns, plus the tile pattern if it sits
            // outside that run — add_building takes its tiles from there.
            var wanted = new List<int>();
            for (int k = 0; k < bt.PatternCount; k++) wanted.Add(bt.FirstPattern + k);
            if (bt.TilePattern > 0 && !wanted.Contains(bt.TilePattern)) wanted.Add(bt.TilePattern);

            bool firstPat = true;
            foreach (int pat in wanted)
            {
                if (!firstPat) sb.Append(',');
                firstPat = false;
                sb.Append("{\"i\":").Append(pat).Append(",\"cells\":[");
                bool firstCell = true;
                for (int x = 0; x < CwpFile.PatternWidth; x++)
                    for (int y = 0; y < CwpFile.PatternHeight; y++)
                    {
                        int tile = cwp.PatternTile(pat, x, y);
                        bool blk = cwp.PatternBlocks(pat, x, y);
                        if (tile == 0 && !blk) continue;
                        if (!firstCell) sb.Append(',');
                        firstCell = false;
                        sb.Append('[').Append(x).Append(',').Append(y).Append(',')
                          .Append(tile).Append(',').Append(blk ? 1 : 0).Append(']');
                    }
                sb.Append("]}");
            }
            sb.Append("]}");
        }
        sb.Append("]}");
        return sb.ToString();
    }

    // ---- the pictures ------------------------------------------------------
    //
    // A raised building has to be DRAWN, and the map is a baked image: object
    // tiles are decoded while baking and never again. So the import puts the
    // tiles the buildable types need into one atlas per tileset, and the engine
    // stamps them into the baked image exactly the way MapBaker.Blit does —
    // sx = col*TileW, sy = originY + row*TileH − elev*ElevStep − 50 + yoff.
    //
    // Only the three buildable types are exported. Everything else is already
    // in the baked picture.

    /// <summary>The types a builder can raise — see Construction.cs.</summary>
    public static readonly int[] BuildableTypes = { 5, 7, 15 };

    // ---- the doors ---------------------------------------------------------
    //
    // Read 07.08.2026 out of the building draw @0x42B25C and its jump table
    // @0x42bdd8 (16 entries, one per type):
    //
    //     al = byte[+0x34]                 ; door count, 0 = no door at all
    //     dl = byte[+0x37 + door*3] & 0x7f ; the door's STATE
    //     if (dl >= 4) -> its own branch
    //     jmp [ (type-1)*4 + 0x42bdd8 ]    ; per-type branch, sets a tile number
    //     ...
    //     @0x42B338:  frame = tile*4 + word[0x7a44fe] + state
    //
    // The *4 and the mask against 4 agree: a door has FOUR phases. And the five
    // types that jump into the empty branch are exactly the five the stat table
    // gives 0 doors — two tables read apart, no contradiction.

    /// <summary>Door tile number per building type, or 0 for a type with no
    /// door of its own. The three factories (2..4) pick per door index and take
    /// door 0 from a table via <c>byte[+0x19]</c>; Mine and Feld-Rohstoffmine
    /// read theirs from the table at 0x878ad5. Those four are left at 0 here
    /// because their number is not a constant.</summary>
    public static int DoorTile(int typ, int door = 0) => typ switch
    {
        1 => 10,      // Basis
        5 => 17,      // Depot
        6 => 13,      // Bahnstation
        9 => 12,      // Flughafen
        12 => 18,     // Feldbahnhof
        16 => 16,     // Werft-Station (then `shl bl,2` and a special case)

        // The three factories carry TWO doors and branch on the door index
        // (@0x42B2B0, @0x42B2D1, @0x42B2F2 — all three built the same way):
        // door 1 is a constant, door 0 comes from the table at 0x87a2c5,
        // indexed `byte[+0x19] * 14`. That table is filled at run time, so
        // door 0 has to wait; door 1 we can draw today.
        2 => door == 1 ? 14 : 0,      // Waffen-Fabrik
        3 => door == 1 ? 15 : 0,      // Fahrwerk-Fabrik
        4 => door == 1 ? 11 : 0,      // Spezial-Fabrik

        _ => 0,       // 7,8,11,13,14 have none; 10 and 15 read table 0x878ad5
    };

    /// <summary>A door has four animation phases — 0 shut, 3 open.</summary>
    public const int DoorPhases = 4;

    // ---- what the two unread type fields turned out to be ------------------
    //
    // ⚠ WITHDRAWN, 08.08.2026: the note of 07.08. read fields +0x04 and +0x06 of
    // the type row as "very probably the door cell in the pattern", because they
    // are non-zero only for types 2, 3, 9, 10 and 15 and hold values 0..14.
    //
    // They are not. They are `count` and `first` of the CELL ANIMATION table —
    // see CwpFile.CellAnim for the code that says so, and cwp_anim_render.py for
    // the pictures that settle it: the two factories run CONVEYOR BELTS and a
    // spinning drum, the mine turns a paddle wheel, the Feld-Rohstoffmine a
    // drum, and the airfield runs a light along its runway. Not one of the
    // fifteen rows in a tileset is a door.
    //
    // The distribution that made the door reading look good is a coincidence of
    // subject matter: those five are the INDUSTRIAL types, and industry is what
    // has moving parts. Types 1, 5, 6, 12 and 16 have doors as well (see
    // DoorTile) and own no animation row at all — which the earlier note would
    // have had to explain and did not.
    //
    // The door of a factory therefore still hangs on the run-time table at
    // 0x87a2c5, indexed byte[+0x19], exactly where it hung before.

    /// <summary>
    /// Which of the exported door pictures a type shows in a given phase.
    ///
    /// <para>The original computes an ANIM.CWA frame,
    /// <c>tile*4 + word[0x7a44fe] + phase</c> (@0x42B338), where 0x7a44fe is
    /// the <b>first frame of sequence 301</b> — the loader @0x435710 reads the
    /// 4000-byte sequence table to 0x7a4048, and 0x7a44fe − 0x7a4048 = 1206 =
    /// 301*4 + 2. Since the import writes that whole sequence out as
    /// <c>Effects/door/f00..f75</c>, the base falls away here and the picture
    /// number is simply <c>tile*4 + phase</c>.</para>
    ///
    /// <para><b>The arithmetic closes</b>: sequence 301 holds <b>76 frames =
    /// 19 doors × 4 phases</b>, and the highest tile the jump table hands out
    /// is 18 — 18*4+3 = 75, exactly the last frame.</para>
    ///
    /// <para>Returns -1 for a type with no door, and for the four types whose
    /// tile is not a constant (the three factories and the two mines).</para>
    /// </summary>
    public static int DoorPicture(int typ, int phase, int door = 0)
    {
        int tile = DoorTile(typ, door);
        if (tile == 0) return -1;
        return tile * DoorPhases + Mathf.Clamp(phase, 0, DoorPhases - 1);
    }

    /// <summary>How many pictures the door sequence holds — 19 doors × 4.</summary>
    public const int DoorPictureCount = 76;

    /// <summary>
    /// The pattern that shows a type as a RUIN, or −1 when it has none.
    ///
    /// <para><b>Read from the game, 07.08.2026.</b> A building record (76 bytes,
    /// array @0xC06910) carries its picture in <c>byte[+0x0a]</c>. The game names
    /// that field itself: the draw routine @0x4C95E0 bounds it against the type's
    /// pattern count (field +0x00 @0xBB41A0) and aborts with
    /// <c>"try to draw nonexisting frame of building"</c> @0x539630. So a type's
    /// <c>PatternCount</c> patterns are its FRAMES, and the drawn pattern is
    /// <c>FirstPattern + frame</c>.</para>
    ///
    /// <para>The frame is set @0x4CBBF0:
    /// <c>frame = (word[+0x26] − word[+0x16]) · count / word[+0x26]</c> — a
    /// proportional mapping of a quantity onto the whole run, rewritten only when
    /// it changes. Frame 0 is therefore the untouched building and the LAST frame
    /// the fully spent one.</para>
    ///
    /// <para>Confirmed by looking, not by argument: rendering the first and the
    /// last pattern of every type of tileset 06 gives an intact building on one
    /// side and a field of rubble on the other, for all seven types. ⚠ A type with
    /// a single pattern (type 17) has no ruin — first and last are the same
    /// picture — and returns −1 here.</para>
    /// </summary>
    /// <remarks>Takes the interface, not the file, so the .CWP and the exported
    /// JSON answer from the same code — the self-test plays them against each
    /// other and a ruin read one way has to match the other.</remarks>
    public static int RuinPattern(IBuildingPatterns p, int typ)
    {
        var bt = p.GetBuildingType(typ);
        if (bt.IsEmpty || bt.PatternCount < 2) return -1;
        return bt.FirstPattern + bt.PatternCount - 1;
    }

    /// <summary>One tile in the atlas.</summary>
    public readonly struct AtlasTile
    {
        public readonly int X, Y, W, H, YOff;
        public AtlasTile(int x, int y, int w, int h, int yoff)
        { X = x; Y = y; W = w; H = h; YOff = yoff; }
    }

    private readonly Dictionary<int, AtlasTile> _atlas = new();
    public Image? AtlasImage;

    public bool TryGetTile(int gridCode, out AtlasTile t) => _atlas.TryGetValue(gridCode, out t);

    /// <summary>
    /// Decodes every object tile the buildable types use and lays them out in a
    /// single column-per-tile atlas. Returns null when the tileset carries none
    /// of those types.
    /// </summary>
    public static (Image? Png, string Json) WriteAtlas(CwpFile cwp, PalFile pal, int tileset)
    {
        // gather the distinct grid codes of the buildable types' first patterns
        var codes = new SortedSet<int>();
        foreach (int typ in BuildableTypes)
        {
            var bt = cwp.GetBuildingType(typ);
            if (bt.IsEmpty) continue;
            for (int k = 0; k < bt.PatternCount; k++)
                for (int x = 0; x < CwpFile.PatternWidth; x++)
                    for (int y = 0; y < CwpFile.PatternHeight; y++)
                    {
                        int t = cwp.PatternTile(bt.FirstPattern + k, x, y);
                        if (t != 0) codes.Add(t);
                    }
        }

        // and the STANDING and the RUINED picture of every type, buildable or
        // not. Two reasons this is no longer optional: a building one cannot
        // build can still be shot to pieces, and since the baker stopped burning
        // buildings into the map picture (see MapBaker.BuildingCells) the
        // renderer needs the standing one too — it is the only source there is.
        for (int typ = 0; typ < CwpFile.BuildingTypeCount; typ++)
        {
            var bt = cwp.GetBuildingType(typ);
            if (bt.IsEmpty) continue;
            foreach (int pat in new[] { bt.FirstPattern, RuinPattern(cwp, typ) })
            {
                if (pat < 0) continue;
                for (int x = 0; x < CwpFile.PatternWidth; x++)
                    for (int y = 0; y < CwpFile.PatternHeight; y++)
                    {
                        int t = cwp.PatternTile(pat, x, y);
                        if (t != 0) codes.Add(t);
                    }
            }

            // and every phase of every cell animation the type owns — those
            // tiles are not in any pattern, they only exist in the 24-byte rows.
            for (int k = 0; k < bt.AnimCount; k++)
            {
                var a = cwp.GetAnimRow(bt.AnimFirst + k);
                if (a.Tiles == null) continue;
                for (int ph = 1; ph <= a.LastPhase && ph < a.Tiles.Length; ph++)
                    if (a.Tiles[ph] != 0) codes.Add(a.Tiles[ph]);
            }
        }
        if (codes.Count == 0) return (null, "");

        var frames = new List<(int Code, CwpFile.Frame F)>();
        foreach (int c in codes)
        {
            try
            {
                var f = cwp.DecodeObject(c + CwpFile.ObjectCodeBase);
                if (!f.IsEmpty) frames.Add((c, f));
            }
            catch (ArgumentOutOfRangeException) { }
        }
        if (frames.Count == 0) return (null, "");

        int aw = 0, ah = 0;
        foreach (var (_, f) in frames) { aw = Math.Max(aw, f.Width); ah += f.Height; }
        var img = Image.CreateEmpty(aw, ah, false, Image.Format.Rgba8);

        var sb = new StringBuilder();
        sb.Append("{\"_note\":\"object tiles of the buildable types, so a raised ");
        sb.Append("building can be stamped into the baked map picture the way the ");
        sb.Append("original stamps its tiles into the map\",\"tileset\":").Append(tileset);
        sb.Append(",\"tiles\":{");
        int at = 0; bool first = true;
        foreach (var (code, f) in frames)
        {
            for (int y = 0; y < f.Height; y++)
                for (int x = 0; x < f.Width; x++)
                {
                    int i = y * f.Width + x;
                    if (!f.Opaque[i]) continue;
                    byte p = f.Pixels[i];
                    img.SetPixel(x, at + y, Color.Color8(pal.R[p], pal.G[p], pal.B[p]));
                }
            if (!first) sb.Append(',');
            first = false;
            sb.Append($"\"{code}\":[0,{at},{f.Width},{f.Height},{f.YOffset}]");
            at += f.Height;
        }
        sb.Append("}}");
        return (img, sb.ToString());
    }

    /// <summary>Reads the atlas written by <see cref="WriteAtlas"/>.</summary>
    public void LoadAtlas(GDict d, Image? png)
    {
        AtlasImage = png;
        if (!d.TryGetValue("tiles", out var tv) || tv.VariantType != Variant.Type.Dictionary)
            return;
        foreach (var kv in tv.AsGodotDictionary())
        {
            if (!int.TryParse(kv.Key.AsString(), out int code)) continue;
            if (kv.Value.VariantType != Variant.Type.Array) continue;
            var a = kv.Value.AsGodotArray();
            if (a.Count < 5) continue;
            _atlas[code] = new AtlasTile(a[0].AsInt32(), a[1].AsInt32(), a[2].AsInt32(),
                                         a[3].AsInt32(), a[4].AsInt32());
        }
    }

    // ---- reading (run time) ------------------------------------------------

    public static BuildingPatterns? Load(string path)
    {
        if (!File.Exists(path)) return null;
        var json = Json.ParseString(File.ReadAllText(path));
        if (json.VariantType != Variant.Type.Dictionary) return null;
        return FromDict(json.AsGodotDictionary());
    }

    public static BuildingPatterns FromDict(GDict d)
    {
        var bp = new BuildingPatterns { Tileset = GetI(d, "tileset") };
        if (!d.TryGetValue("types", out var tv) || tv.VariantType != Variant.Type.Array)
            return bp;

        foreach (var entry in tv.AsGodotArray())
        {
            if (entry.VariantType != Variant.Type.Dictionary) continue;
            var t = entry.AsGodotDictionary();
            int typ = GetI(t, "typ");
            bp._types[typ] = new CwpFile.BuildingType(
                GetI(t, "count"), GetI(t, "first"), GetI(t, "tile_pattern"),
                GetI(t, "anim_count"), GetI(t, "anim_first"));

            if (t.TryGetValue("anims", out var av) && av.VariantType == Variant.Type.Array)
                foreach (var ae in av.AsGodotArray())
                {
                    if (ae.VariantType != Variant.Type.Dictionary) continue;
                    var a = ae.AsGodotDictionary();
                    var tiles = new ushort[CwpFile.AnimTileCount];
                    if (a.TryGetValue("tiles", out var tv2) && tv2.VariantType == Variant.Type.Array)
                    {
                        var arr = tv2.AsGodotArray();
                        for (int k = 0; k < tiles.Length && k < arr.Count; k++)
                            tiles[k] = (ushort)arr[k].AsInt32();
                    }
                    bp._anims[GetI(a, "i")] = new CwpFile.CellAnim(
                        GetI(a, "dx"), GetI(a, "dy"), GetI(a, "mode"), GetI(a, "last"), tiles);
                }

            if (!t.TryGetValue("patterns", out var pv) || pv.VariantType != Variant.Type.Array)
                continue;
            foreach (var pe in pv.AsGodotArray())
            {
                if (pe.VariantType != Variant.Type.Dictionary) continue;
                var p = pe.AsGodotDictionary();
                int idx = GetI(p, "i");
                var tiles = new ushort[CwpFile.PatternWidth * CwpFile.PatternHeight];
                var blocks = new bool[tiles.Length];
                if (p.TryGetValue("cells", out var cv) && cv.VariantType == Variant.Type.Array)
                    foreach (var ce in cv.AsGodotArray())
                    {
                        if (ce.VariantType != Variant.Type.Array) continue;
                        var c = ce.AsGodotArray();
                        if (c.Count < 4) continue;
                        int x = c[0].AsInt32(), y = c[1].AsInt32();
                        if ((uint)x >= CwpFile.PatternWidth || (uint)y >= CwpFile.PatternHeight)
                            continue;
                        int at = x * CwpFile.PatternHeight + y;
                        tiles[at] = (ushort)c[2].AsInt32();
                        blocks[at] = c[3].AsInt32() != 0;
                    }
                bp._tiles[idx] = tiles;
                bp._blocks[idx] = blocks;
            }
        }
        return bp;
    }

    private static int GetI(GDict d, string k, int def = 0)
        => d.TryGetValue(k, out var v) && v.VariantType != Variant.Type.Nil ? v.AsInt32() : def;
}

/// <summary>What the build-site test needs, so that the original .CWP and the
/// exported file can stand in for each other — and be held against each
/// other.</summary>
public interface IBuildingPatterns
{
    bool HasBuildings { get; }
    CwpFile.BuildingType GetBuildingType(int typ);
    int PatternTile(int pattern, int x, int y);
    bool PatternBlocks(int pattern, int x, int y);

    /// <summary>One row of the cell-animation table. Both sides answer, so the
    /// self-test can hold the exported JSON against the .CWP here too.</summary>
    CwpFile.CellAnim GetAnimRow(int row);
}
