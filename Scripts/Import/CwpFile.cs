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
public sealed class CwpFile
{
    private readonly byte[] _d;
    private readonly int _blobOff;
    private readonly uint[] _dir1;
    private readonly uint[] _dir2;

    public int FrameCount => _dir1.Length;
    public int ObjectCount => _dir2.Length;

    /// <summary>Grid codes at or above this are map objects, and index dir2.</summary>
    public const int ObjectCodeBase = 10000;

    private const int AuxSize = 0x23a0;

    private CwpFile(byte[] d, int blobOff, uint[] dir1, uint[] dir2)
    {
        _d = d; _blobOff = blobOff; _dir1 = dir1; _dir2 = dir2;
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

        return new CwpFile(d, blobOff, dir1, dir2);
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
