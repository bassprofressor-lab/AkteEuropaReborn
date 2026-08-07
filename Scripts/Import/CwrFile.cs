namespace AkteEuropaReborn.Import;

using System;
using System.Collections.Generic;
using System.IO;
using Godot;

/// <summary>
/// ROBO.CWR — the unit sprite bank: propulsions, weapons, ship hulls, aircraft,
/// rail cars and every foot soldier, all in one file.
///
/// Recovered from GAME.EXE (loader @0x429020, frame blitter family 0x4ac1b0,
/// frame-pointer table at 0x7a4ff0, part table at 0x77c870, the fixup loop
/// @0x429105 that turns the stored values into direct byte offsets).
///
/// FILE LAYOUT, in the order the loader reads it:
///
///     0x000    3      "CWR"
///     0x003    u32    size of the pixel blob
///     0x007    604    part table — 151 entries of [u16 a][u16 base frame]
///     0x263    1020   aux
///     0x65F    40000  the frame table: 10000 u32 byte offsets into the blob,
///                     0xFFFFFFFF marking an empty slot
///     0xA29F   1760   aux
///     0xA97F   u32    size of the trailing per-frame meta block
///     0xA983          the blob — this is what the frame offsets index
///
/// A frame has the same grammar as a terrain sprite (see <see cref="CwpFile"/>),
/// which is why both share one reader.
///
/// A part owns a run of frames: its base is in the part table and its length is
/// the distance to the next populated base. Eight frames make one block — the
/// eight facings — so a part with 48 frames carries six blocks.
///
/// Composition, from the draw code @0x429c80: `frame = base[component] +
/// block*8 + facing`.
/// </summary>
public sealed class CwrFile
{
    private readonly byte[] _d;
    private readonly int _blobOff;
    private readonly uint[] _frameOff;
    private readonly byte[] _partTbl;

    public const int PartCount = 151;
    public const int FrameSlots = 10000;
    public const int Facings = 8;

    private const int PartTblOff = 7, PartTblSize = 0x25c;
    private const int AuxTblOff = 0x263, AuxTblSize = 1020;
    private const int FrameTblOff = 0x65f;
    private const int BlobOff = 0xa983;

    /// <summary>The 1020-byte table the loader puts at VA 0x7a3c48 — for a long
    /// time filed as "aux, purpose unknown". It addresses the INFANTRY, which
    /// the 151-entry part table does not cover: the draw code @0x42a874 reads
    /// `word[type*34 + 0x7a3c48]` as a base frame and then follows the same
    /// `base + block*8 + facing` rule as everything else.
    ///
    /// Record, 34 bytes: +0x00 u16 base, +0x02 u16 animation count,
    /// +0x04 ten u16 animation starts, +0x18 ten u8 lengths.</summary>
    public byte[] Aux { get; private set; } = Array.Empty<byte>();

    public const int InfantryStride = 34;

    public int BlobSize { get; private set; }

    private CwrFile(byte[] d, int blobOff, uint[] frameOff, byte[] partTbl, int blobSize)
    {
        _d = d; _blobOff = blobOff; _frameOff = frameOff; _partTbl = partTbl;
        BlobSize = blobSize;
    }

    public static CwrFile Load(string path) => FromBytes(File.ReadAllBytes(path));

    public static CwrFile FromBytes(byte[] d)
    {
        if (d.Length < BlobOff) throw new InvalidDataException("ROBO.CWR zu kurz");
        if (d[0] != 'C' || d[1] != 'W' || d[2] != 'R')
            throw new InvalidDataException("Keine CWR-Datei");
        int blobSize = (int)BitConverter.ToUInt32(d, 3);

        var part = new byte[PartTblSize];
        Array.Copy(d, PartTblOff, part, 0, PartTblSize);

        var off = new uint[FrameSlots];
        for (int i = 0; i < FrameSlots; i++)
            off[i] = BitConverter.ToUInt32(d, FrameTblOff + i * 4);

        if (BlobOff + blobSize > d.Length)
            throw new InvalidDataException(
                $"CWR-Blob passt nicht: {BlobOff}+{blobSize} > {d.Length}");
        var aux = new byte[AuxTblSize];
        Array.Copy(d, AuxTblOff, aux, 0, AuxTblSize);
        return new CwrFile(d, BlobOff, off, part, blobSize) { Aux = aux };
    }

    /// <summary>Base frame of a component, or -1 where the part owns none.</summary>
    public int PartBase(int comp)
    {
        if (comp < 0 || comp >= PartCount) return -1;
        int a = BitConverter.ToUInt16(_partTbl, comp * 4);
        int b = BitConverter.ToUInt16(_partTbl, comp * 4 + 2);
        if (a == 0 && b == 0 && comp != 1) return -1;
        return b;
    }

    /// <summary>How many frames a part owns: up to the next populated base.
    /// The last one has no successor and is taken as six blocks.</summary>
    public int PartFrameCount(int comp)
    {
        int b = PartBase(comp);
        if (b < 0) return 0;
        for (int j = comp + 1; j < PartCount; j++)
        {
            int bj = BitConverter.ToUInt16(_partTbl, j * 4 + 2);
            if (bj > b) return bj - b;
        }
        return 48;
    }

    /// <summary>How many 48-frame POSE GROUPS a part owns.
    ///
    /// The draw code (@0x429b10..0x429b40) builds the hull's frame as
    ///     base_frame[chassis] + facing + slope_block + (entity[+0x11] &amp; 7) * 0x30
    /// — `imul ax, ax, 0x30`, so a group is 48 frames, six blocks of eight
    /// facings. The count is in the part table's FIRST u16, which every
    /// populated part carries as <c>256 * groups</c>: 48 frames → 256,
    /// 96 → 512, 144 → 768, 384 → 2048. Checked against the frame counts of
    /// all sixteen land chassis, no exception.
    ///
    /// This is what makes a Läufer look like a Läufer: its part owns EIGHT
    /// groups and the maps spread its 59 units over all of them, while the
    /// exporter used to write group 0 for everybody.</summary>
    public int PartGroups(int comp)
    {
        if (comp < 0 || comp >= PartCount) return 1;
        int a = BitConverter.ToUInt16(_partTbl, comp * 4);
        int g = a / 256;
        return g < 1 ? 1 : g;
    }

    public readonly record struct Part(int Component, int BaseFrame, int Frames, int Blocks);

    public List<Part> PopulatedParts()
    {
        var list = new List<Part>();
        for (int c = 0; c < PartCount; c++)
        {
            int b = PartBase(c);
            if (b < 0) continue;
            int n = PartFrameCount(c);
            list.Add(new Part(c, b, n, Math.Max(1, n / Facings)));
        }
        return list;
    }

    /// <summary>Decode one frame, or null where the slot is empty or the data
    /// is not a plausible sprite — the bank has holes.</summary>
    public CwpFile.Frame? DecodeFrame(int index)
    {
        if (index < 0 || index >= FrameSlots) return null;
        uint off = _frameOff[index];
        if (off == 0xFFFFFFFF || off >= (uint)BlobSize) return null;
        int at = _blobOff + (int)off;
        int rows = _d[at];
        if (rows == 0 || rows > 200) return null;
        var f = CwpFile.DecodeFrameAt(_d, at);
        if (f.Width == 0 || f.Width > 400) return null;
        return f;
    }

    /// <summary>
    /// The canvas a unit part is drawn on: a four-pixel left margin and the
    /// frame's own y offset placing it vertically. <b>64x56 is the minimum</b>,
    /// not the size — a frame wider or taller than that gets a canvas of its
    /// own, grown to the right and downwards only.
    ///
    /// ⚠ It used to be a hard 64x56 and everything past it was dropped on the
    /// floor. Measured over the 3,439 frames of the 81 populated parts: 192
    /// lose visible pixels off the right edge, up to 140 of them. The player
    /// found it — "die Schiffe scheinen wie vorne leicht abgeschnitten" — and
    /// the landing craft (component 73) is exactly that: 84 px wide against a
    /// 64 px canvas, so bow and stern were cut away. The two big warships are
    /// worse: component 100 (unit_type 157) needs <b>204 x 126</b> and 101
    /// (158) 178 x 114, and twenty of them stand on the maps.
    ///
    /// Growing right and down leaves the anchor where it is — a pixel that sat
    /// at (30, 55) still sits there — so nothing has to move with this, and the
    /// 94 % of frames that fit come out byte for byte as before.
    /// </summary>
    public const int CanvasW = 64, CanvasH = 56, CanvasXPad = 4;

    /// <summary>The canvas one frame needs: never smaller than the shared
    /// minimum, never larger than the frame demands.</summary>
    public (int W, int H) CanvasFor(int frameIndex)
    {
        var f = DecodeFrame(frameIndex);
        if (f == null) return (CanvasW, CanvasH);
        return (Math.Max(CanvasW, f.Width + CanvasXPad),
                Math.Max(CanvasH, f.YOffset + f.Height));
    }

    public Image FacingImage(int frameIndex, PalFile pal)
    {
        var f = DecodeFrame(frameIndex);
        var (cw, ch) = f == null
            ? (CanvasW, CanvasH)
            : (Math.Max(CanvasW, f.Width + CanvasXPad),
               Math.Max(CanvasH, f.YOffset + f.Height));
        return FacingImage(frameIndex, pal, cw, ch);
    }

    /// <summary>Same frame on a canvas of a given size — used when a hull and a
    /// turret have to share one, so the layers still line up.</summary>
    public Image FacingImage(int frameIndex, PalFile pal, int canvasW, int canvasH)
    {
        var img = Image.CreateEmpty(canvasW, canvasH, false, Image.Format.Rgba8);
        img.Fill(new Color(0, 0, 0, 0));
        var f = DecodeFrame(frameIndex);
        if (f == null) return img;
        for (int y = 0; y < f.Height; y++)
            for (int x = 0; x < f.Width; x++)
            {
                int o = y * f.Width + x;
                if (!f.Opaque[o]) continue;
                int cx = x + CanvasXPad, cy = f.YOffset + y;
                if (cx < 0 || cx >= canvasW || cy < 0 || cy >= canvasH) continue;
                byte i = f.Pixels[o];
                img.SetPixel(cx, cy, Color.Color8(pal.R[i], pal.G[i], pal.B[i], 255));
            }
        return img;
    }

    /// <summary>`frame = base[component] + group*48 + block*8 + facing`, the
    /// draw code's own formula (@0x429fa6..0x429fc3). `group` is the unit's
    /// pose out of entity +0x11; `block` is the slope block, which the walker
    /// is explicitly denied (@0x429af4 forces it to 0 for chassis 0x11).</summary>
    public Image PartImage(int comp, int facing, PalFile pal, int block = 0, int group = 0)
    {
        int i = PartFrame(comp, facing, block, group);
        return i < 0
            ? Image.CreateEmpty(CanvasW, CanvasH, false, Image.Format.Rgba8)
            : FacingImage(i, pal);
    }

    /// <summary>The frame index behind a part, facing, block and pose group —
    /// so a caller can ask <see cref="CanvasFor"/> before it draws.</summary>
    public int PartFrame(int comp, int facing, int block = 0, int group = 0)
    {
        int b = PartBase(comp);
        if (b < 0) return -1;
        // A part that owns one group has no pose to choose; asking for one
        // would run into the NEXT part's frames.
        int g = PartGroups(comp);
        if (group < 0 || group >= g) group = 0;
        return b + group * (g > 1 ? GroupFrames : 0) + block * Facings + facing;
    }

    public const int GroupFrames = 48;
}
