namespace AkteEuropaReborn.Import;

using System;
using System.IO;
using Godot;

/// <summary>
/// A .CWP sprite file from the original game — terrain tiles in the first
/// directory, map objects in the second.
///
/// Ported from the Python decoder that was reverse-engineered out of GAME.EXE
/// (loader `Check_cwp` @0x4c8ad0, frame blitter @0x4ac1b0) and verified by
/// exporting all 38,393 frames of the 23 shipped files without a single bad
/// offset or overrun.
///
/// FILE LAYOUT — every field in the order the loader reads it:
///
///     0x00  3      "CWP"
///     0x03  1      version
///     0x04  u16    frame count      (directory 1: terrain)
///     0x06  u16    object count     (directory 2: map objects)
///     0x08  u32    size of the pixel blob
///     0x0c  0x23a0 aux table (not needed for pixels)
///     ...   n1*4   directory 1, u32 offsets into the blob
///     ...   n2*4   directory 2, likewise
///     ...          the blob itself
///
/// Frame i is `blob[dir1[i]]`; a map object with grid code C is
/// `blob[dir2[C - 10000]]` — both directories index the same blob.
///
/// FRAME GRAMMAR — from the blitter:
///
///     byte 0   number of scanlines = the sprite's height
///     byte 1   y offset, the top margin the blitter adds
///     per row: len, leftoff, mode, then `len` literal palette indices
///              mode 0 = every byte is opaque, 0xFF included
///              mode 1 = 0xFF inside the run means transparent
///     a row with len 0 still carries its three header bytes
///
/// The width is not stored: it is `max(leftoff + len)` over the rows.
/// Everything outside a row's run is transparent.
/// </summary>
public sealed class CwpFile : IBuildingPatterns
{
    private readonly byte[] _d;
    private readonly int _blobOff;
    private readonly uint[] _dir1;
    private readonly uint[] _dir2;
    private readonly int _typeTabOff;      // -1 when the file has no tail
    private readonly int _patternOff;
    private readonly int _animOff;         // -1 together with the two above

    public int FrameCount => _dir1.Length;
    public int ObjectCount => _dir2.Length;

    /// <summary>Grid codes at or above this are map objects, and index dir2.</summary>
    public const int ObjectCodeBase = 10000;

    private const int AuxSize = 0x23a0;

    // ---- the side tables behind the blob -----------------------------------
    //
    // Check_cwp @0x4C8680 (the F: build; the other one has it at 0x4c8ad0)
    // reads the file strictly in order, and after the pixel blob come six more
    // fixed blocks.  Two of them are the buildings:
    //
    //     0x28   1000 B  -> 0xbb3200   100 building types, 10 B each
    //     0x29  72000 B  -> 0xb96b98   400 patterns, 180 B each
    //     0x2a   3600 B  -> 0xbacba0   150 CELL ANIMATIONS, 24 B each
    //     0x2b 1600 B  |  0x2c 5000 B, u16, 5000 B
    //
    // Measured, not assumed: over all 23 shipped .CWP the sum
    //   12 + 0x23a0 + frames*4 + objects*4 + blob + 1000+72000+3600+1600+5000+2+5000
    // hits the file length EXACTLY, 23 of 23.
    /// <summary>
    /// <b>⭐⭐⭐ DIE VARIANTENTAFEL DER BODENSYNTHESE</b> — der Aux-Block ab
    /// Offset <c>0x0c</c>, den <c>0x4C8DAD</c> nach <c>0xBAA800</c> liest.
    ///
    /// <para>2280 Sätze zu vier Byte: <c>word</c> Grundkachel, <c>byte[+2]</c>
    /// Variantenzahl, <c>byte[+3]</c> ungedeutet. <b>2280 = 8 · 15 · 19</b> —
    /// Geländeklasse × Eckenmuster × Schrägenart, und genau so wird der Index
    /// gerechnet (siehe <c>MapBaker.SyntheseKachel</c>). Dass die drei
    /// Dimensionen die Tafelgrösse auf den Satz genau ausfüllen, ist die erste
    /// Bestätigung der Formel; die zweite steht im Kopf von
    /// <c>SyntheseKachel</c>.</para>
    ///
    /// <para>Der Block lag seit jeher in <see cref="AuxSize"/> — gelesen, aber
    /// nie gedeutet.</para></summary>
    public const int BodenSatzCount = 2280;

    /// <summary>Ein Satz der Variantentafel: Grundkachel und wieviele
    /// Varianten darauf folgen. Anzahl 0 heisst »hier gibt es nichts«.</summary>
    public (int Basis, int Anzahl) Bodenvariante(int index)
    {
        if (index < 0 || index >= BodenSatzCount) return (0, 0);
        int o = 0x0c + index * 4;
        return (BitConverter.ToUInt16(_d, o), _d[o + 2]);
    }

    public const int BuildingTypeCount = 100, BuildingTypeStride = 10;
    public const int PatternCount = 400, PatternStride = 180;

    /// <summary>The third tail block: the cell animations. 3600 / 24 = 150.</summary>
    public const int AnimRowCount = 150, AnimRowStride = 24;

    /// <summary>How many tiles a row can hold — the 24 bytes are 4 header and
    /// ten u16.</summary>
    public const int AnimTileCount = (AnimRowStride - 4) / 2;

    /// <summary>The build loop reads ten columns and six rows, and no more —
    /// `cmp cx, 0xa` @0x42052C and `cmp [0x541e4c], 6` @0x42053D.</summary>
    public const int PatternWidth = 10, PatternHeight = 6;

    /// <summary>A pattern's 180 bytes are two rasters, and the offset of the
    /// second is in the code itself: 0xb96c10 − 0xb96b98 = 0x78.</summary>
    private const int MaskOffset = 0x78;   // = 120 = PatternWidth * PatternHeight * 2

    /// <summary>True when the file carries the building tail. Every shipped
    /// .CWP does; the check exists so a truncated file fails loudly.</summary>
    public bool HasBuildings => _typeTabOff >= 0;

    private CwpFile(byte[] d, int blobOff, uint[] dir1, uint[] dir2,
                    int typeTabOff, int patternOff, int animOff)
    {
        _d = d; _blobOff = blobOff; _dir1 = dir1; _dir2 = dir2;
        _typeTabOff = typeTabOff; _patternOff = patternOff; _animOff = animOff;
    }

    public static CwpFile Load(string path) => FromBytes(File.ReadAllBytes(path));

    public static CwpFile FromBytes(byte[] d)
    {
        if (d.Length < 0x0c + AuxSize)
            throw new InvalidDataException("CWP zu kurz");
        if (d[0] != 'C' || d[1] != 'W' || d[2] != 'P')
            throw new InvalidDataException("Keine CWP-Datei");

        int frameCount = BitConverter.ToUInt16(d, 4);
        int objCount = BitConverter.ToUInt16(d, 6);
        uint blobSize = BitConverter.ToUInt32(d, 8);

        int dir1Off = 0x0c + AuxSize;
        int dir2Off = dir1Off + frameCount * 4;
        int blobOff = dir2Off + objCount * 4;
        if (blobOff + blobSize > d.Length)
            throw new InvalidDataException(
                $"CWP-Blob passt nicht: {blobOff}+{blobSize} > {d.Length}");

        var dir1 = new uint[frameCount];
        for (int i = 0; i < frameCount; i++) dir1[i] = BitConverter.ToUInt32(d, dir1Off + i * 4);
        var dir2 = new uint[objCount];
        for (int i = 0; i < objCount; i++) dir2[i] = BitConverter.ToUInt32(d, dir2Off + i * 4);

        // the building tail, in the loader's own order
        int typeTabOff = blobOff + (int)blobSize;
        int patternOff = typeTabOff + BuildingTypeCount * BuildingTypeStride;
        int animOff = patternOff + PatternCount * PatternStride;
        if (patternOff + PatternCount * PatternStride > d.Length)
            typeTabOff = patternOff = animOff = -1;
        else if (animOff + AnimRowCount * AnimRowStride > d.Length)
            animOff = -1;                       // patterns still usable

        return new CwpFile(d, blobOff, dir1, dir2, typeTabOff, patternOff, animOff);
    }

    // ---- buildings ---------------------------------------------------------

    /// <summary>One row of the 100-entry type table. The type number is the
    /// game's own <c>typ</c>, 1-based: 1 Basis, 2..4 the factories, 5 Depot,
    /// 7 Generator, 10 Mine, 15 Feld-Rohstoffmine, 16 Werft-Station.</summary>
    public readonly struct BuildingType
    {
        /// <summary>How many patterns this type owns (field +0x00).</summary>
        public readonly int PatternCount;
        /// <summary>Index of the first (field +0x02); they run consecutively.
        /// Verified over all 23 files: 387 of 387 hand-offs without a gap.</summary>
        public readonly int FirstPattern;
        /// <summary>Field +0x08 — the second pattern index, which add_building
        /// @0x4C8D60 takes as the tile set (`word[0xbb3208 + typ*10]`).</summary>
        public readonly int TilePattern;

        /// <summary>Field +0x04 — how many CELL ANIMATIONS the type owns, as a
        /// BYTE. The walk @0x4D5830 reads it with <c>mov bl, byte[10*typ +
        /// 0xbb41a4]</c> and returns at once when it is zero.</summary>
        public readonly int AnimCount;

        /// <summary>Field +0x06 — the first of those rows (word, read signed by
        /// the original: <c>movsx ecx, ax</c>).</summary>
        public readonly int AnimFirst;

        public bool IsEmpty => PatternCount == 0;

        public BuildingType(int n, int first, int tile, int animCount = 0, int animFirst = 0)
        {
            PatternCount = n; FirstPattern = first; TilePattern = tile;
            AnimCount = animCount; AnimFirst = animFirst;
        }
    }

    public BuildingType GetBuildingType(int typ)
    {
        if (!HasBuildings || typ < 0 || typ >= BuildingTypeCount) return default;
        int p = _typeTabOff + typ * BuildingTypeStride;
        return new BuildingType(BitConverter.ToUInt16(_d, p),
                                BitConverter.ToUInt16(_d, p + 2),
                                BitConverter.ToUInt16(_d, p + 8),
                                _d[p + 4],
                                BitConverter.ToUInt16(_d, p + 6));
    }

    // ---- the cell animations -----------------------------------------------
    //
    // Read 08.08.2026 out of the two fields of the type row nobody had read yet.
    // The game names the thing itself: the profiler section around the call is
    // "animations of the buildings" @0x4f763c, right before "Flip pages".
    //
    //   driver  @0x4D5D10 : for all 255 buildings, if type != 0 and byte[+0x0a]
    //                       < 100, call the walk below
    //   walk    @0x4D5830 : for j in 0 .. type.AnimCount-1
    //                         ph = byte[bld + 0x0b + j]      ; 0xff = off
    //                         cell = (bld.x + row[0], bld.y + row[1])
    //                         ph++
    //                         if (ph <= row[3]) map[cell] = row.Tiles[ph]
    //                         else if (row[2] == 1) { map[cell] = pattern cell; ph = 0; }
    //                         else ph = 0xff
    //
    // So a row cycles: the plain pattern tile, then Tiles[1..LastPhase], then
    // the plain tile again. Tiles[0] is 0 in every shipped file, which is the
    // same statement from the data side.
    //
    // What they ARE, seen and not argued (cwp_anim_render.py): conveyor belts
    // and a spinning drum on the two factories, the paddle wheel of the mine,
    // the drum of the Feld-Rohstoffmine, and a light running along the runway of
    // the airfield. They are NOT doors — see the note in BuildingPatterns.

    /// <summary>One row of the 150-entry animation table.</summary>
    public readonly struct CellAnim
    {
        /// <summary>The cell inside the type's pattern, column-major like every
        /// other pattern read here. Nailed down by the reset branch @0x4D5940,
        /// which restores <c>word[(90*first + 6*Dx + Dy)*2 + 0xb97b38]</c>.</summary>
        public readonly int Dx, Dy;

        /// <summary>Field +2. 1 = loop (every shipped row), 2 = one-shot with a
        /// reset branch of its own; anything else stops after one pass.</summary>
        public readonly int Mode;

        /// <summary>Field +3 — the highest phase. The cycle is LastPhase+1 long
        /// because phase 0 shows the pattern's own tile.</summary>
        public readonly int LastPhase;

        /// <summary>Ten u16 tile codes; index 0 is the unused phase-0 slot. Add
        /// <see cref="ObjectCodeBase"/> for the grid code.</summary>
        public readonly ushort[] Tiles;

        public bool IsEmpty => Tiles == null || LastPhase == 0;

        public CellAnim(int dx, int dy, int mode, int last, ushort[] tiles)
        { Dx = dx; Dy = dy; Mode = mode; LastPhase = last; Tiles = tiles; }

        /// <summary>The tile a phase shows, or 0 for "the pattern's own".</summary>
        public int TileAt(int phase)
            => Tiles == null || phase <= 0 || phase > LastPhase || phase >= Tiles.Length
               ? 0 : Tiles[phase];
    }

    public bool HasAnimations => _animOff >= 0;

    // ---- die ARTTAFEL der zerstoerbaren Kartenobjekte ----------------------
    //
    // ⭐⭐⭐ 24.08.2026 — Block 0x2b des Anhangs, 1600 Byte, den unser Leser bis
    // heute nur in seiner Groessenrechnung mitgezaehlt hat.
    //
    // `Check_cwp` laedt ihn nach 0xBB3B60 (@0x4C8F17, `cmp eax, 0x640`), und
    // von dort liest ihn das Brandwesen der zerstoerbaren Objekte:
    //
    //     byte[art*8 + 0xBB3B60]  Verhaltensklasse 0/1/2   @0x4C9FEC
    //     word[art*8 + 0xBB3B62]  Grundkachel              @0x4CA593 / @0x4CA76A
    //
    // Auf die Grundkachel rechnet das Original **+10001 = brennt** (@0x4CA59B)
    // und **+10002 = zerstoert** (@0x4CA772). 1600 / 8 = 200 Arten; ueber alle
    // 36 Karten kommen 126 verschiedene vor.
    //
    // ⚠ Die Tafel steckt in der KACHELDATEI, ist also je Tileset eine andere —
    // dieselbe Artnummer bedeutet auf zwei Tilesets nicht dasselbe.

    /// <summary>200 Arten à 8 Byte — Block 0x2b hinter den Zellanimationen.</summary>
    public const int ObjTypeCount = 200, ObjTypeStride = 8;

    private int ObjTypeOff => _animOff < 0 ? -1 : _animOff + AnimRowCount * AnimRowStride;

    /// <summary>Ist die Arttafel da?</summary>
    public bool HasObjTypes
        => ObjTypeOff >= 0 && ObjTypeOff + ObjTypeCount * ObjTypeStride <= _d.Length;

    /// <summary>Verhaltensklasse und Grundkachel einer Objektart, oder
    /// <c>(-1, -1)</c>.</summary>
    public (int Klasse, int Grundkachel) ObjType(int art)
    {
        if (!HasObjTypes || art < 0 || art >= ObjTypeCount) return (-1, -1);
        int at = ObjTypeOff + art * ObjTypeStride;
        return (_d[at], BitConverter.ToUInt16(_d, at + 2));
    }

    public CellAnim GetAnimRow(int row)
    {
        if (!HasAnimations || (uint)row >= AnimRowCount) return default;
        int p = _animOff + row * AnimRowStride;
        var tiles = new ushort[AnimTileCount];
        for (int i = 0; i < AnimTileCount; i++)
            tiles[i] = BitConverter.ToUInt16(_d, p + 4 + i * 2);
        return new CellAnim(_d[p], _d[p + 1], _d[p + 2], _d[p + 3], tiles);
    }

    /// <summary>An animation row's raw 24 bytes — for the self-test.</summary>
    public ReadOnlySpan<byte> AnimRowBytes(int row)
        => !HasAnimations || (uint)row >= AnimRowCount
           ? ReadOnlySpan<byte>.Empty
           : new ReadOnlySpan<byte>(_d, _animOff + row * AnimRowStride, AnimRowStride);

    /// <summary>The tile of a pattern cell, or 0 for "draw nothing". This is
    /// the raster the build-site test walks — it checks a cell only where a
    /// tile stands (@0x42041E). Add <see cref="ObjectCodeBase"/> to get the
    /// grid code, exactly as the original does with its `add ax, 0x2710`.
    /// </summary>
    public int PatternTile(int pattern, int x, int y)
    {
        if (!HasBuildings || (uint)x >= PatternWidth || (uint)y >= PatternHeight
            || (uint)pattern >= PatternCount) return 0;
        return BitConverter.ToUInt16(_d, _patternOff + pattern * PatternStride
                                         + (x * PatternHeight + y) * 2);
    }

    /// <summary>Whether a pattern cell BLOCKS. This is a different raster from
    /// the tiles and deliberately so: add_building stamps the imap from the
    /// mask, so roof and facade hang over ground one can still walk on.
    /// Measured over all files: 7,199 cells carry both, 20,684 only a tile —
    /// the overhang — and 586 only the mask. Only 0 and 255 ever occur.
    /// </summary>
    public bool PatternBlocks(int pattern, int x, int y)
    {
        if (!HasBuildings || (uint)x >= PatternWidth || (uint)y >= PatternHeight
            || (uint)pattern >= PatternCount) return false;
        return _d[_patternOff + pattern * PatternStride + MaskOffset
                  + x * PatternHeight + y] != 0;
    }

    /// <summary>A pattern's raw 180 bytes — for the self-test, which hashes
    /// them against the Python reader.</summary>
    public ReadOnlySpan<byte> PatternBytes(int pattern)
        => !HasBuildings || (uint)pattern >= PatternCount
           ? ReadOnlySpan<byte>.Empty
           : new ReadOnlySpan<byte>(_d, _patternOff + pattern * PatternStride, PatternStride);

    /// <summary>How many of a pattern's mask bytes are neither 0 nor 255.
    /// Measured over all 23 files: none are. If this ever returns non-zero the
    /// mask is not the plain yes/no raster we read it as.</summary>
    public int MaskBytesOtherThanZeroOr255(int pattern)
    {
        if (!HasBuildings || (uint)pattern >= PatternCount) return 0;
        int at = _patternOff + pattern * PatternStride + MaskOffset, bad = 0;
        for (int i = 0; i < PatternWidth * PatternHeight; i++)
            if (_d[at + i] != 0 && _d[at + i] != 255) bad++;
        return bad;
    }

    /// <summary>One decoded sprite: palette indices plus an opacity mask.</summary>
    public sealed class Frame
    {
        public int Width, Height, YOffset;
        public byte[] Pixels = Array.Empty<byte>();   // palette index, row-major
        public bool[] Opaque = Array.Empty<bool>();

        public int Count => Width * Height;
        public bool IsEmpty => Width <= 0 || Height <= 0;
    }

    public Frame DecodeFrame(int index) => DecodeAt(_blobOff + (int)_dir1[index]);

    public Frame DecodeObject(int gridCode)
    {
        int i = gridCode - ObjectCodeBase;
        if (i < 0 || i >= _dir2.Length)
            throw new ArgumentOutOfRangeException(nameof(gridCode),
                $"Objektcode {gridCode} liegt ausserhalb von dir2 (0..{_dir2.Length - 1})");
        return DecodeAt(_blobOff + (int)_dir2[i]);
    }

    private Frame DecodeAt(int at) => DecodeFrameAt(_d, at);

    /// <summary>The frame grammar, shared with ROBO.CWR: the sprite bank uses
    /// the same blitter family, so the same reader serves both.</summary>
    public static Frame DecodeFrameAt(byte[] d, int at)
    {
        int p = at;
        int rows = d[p];
        int yoff = d[p + 1];
        p += 2;

        // first pass: read the row headers, which is also where the width comes from
        var leftoff = new byte[rows];
        var mode = new byte[rows];
        var runAt = new int[rows];
        var runLen = new byte[rows];
        int width = 0;
        for (int y = 0; y < rows; y++)
        {
            byte len = d[p];
            leftoff[y] = d[p + 1];
            mode[y] = d[p + 2];
            p += 3;
            runAt[y] = p;
            runLen[y] = len;
            p += len;
            if (len > 0) width = Math.Max(width, leftoff[y] + len);
        }

        var f = new Frame { Width = width, Height = rows, YOffset = yoff };
        if (f.IsEmpty) return f;
        f.Pixels = new byte[width * rows];
        f.Opaque = new bool[width * rows];

        for (int y = 0; y < rows; y++)
        {
            int len = runLen[y];
            bool skipFf = mode[y] == 1;          // in-run transparency
            for (int k = 0; k < len; k++)
            {
                byte b = d[runAt[y] + k];
                if (skipFf && b == 0xFF) continue;
                int x = leftoff[y] + k;
                if (x < 0 || x >= width) continue;
                int o = y * width + x;
                f.Pixels[o] = b;
                f.Opaque[o] = true;
            }
        }
        return f;
    }

    /// <summary>Paint a decoded frame through a palette.</summary>
    public static Image ToImage(Frame f, PalFile pal)
    {
        int w = Math.Max(f.Width, 1), h = Math.Max(f.Height, 1);
        var img = Image.CreateEmpty(w, h, false, Image.Format.Rgba8);
        img.Fill(new Color(0, 0, 0, 0));
        for (int y = 0; y < f.Height; y++)
            for (int x = 0; x < f.Width; x++)
            {
                int o = y * f.Width + x;
                if (!f.Opaque[o]) continue;
                byte i = f.Pixels[o];
                img.SetPixel(x, y, Color.Color8(pal.R[i], pal.G[i], pal.B[i], 255));
            }
        return img;
    }
}
