namespace AkteEuropaReborn.Import;

using System;
using System.IO;

/// <summary>
/// ANIM.CWA — every effect the game draws: explosions, muzzle flashes, flying
/// rockets, debris and scorch marks, in one blob of 5000 frames.
///
/// Layout, from the loader @0x435730 (see CAB_ASSETS_RE.md):
///
///     0      u32      size of the pixel blob
///     4      4000     the SEQUENCE table — 1000 x [u16 count][u16 first frame]
///     4004   20000    the FRAME table — 5000 u32, blob-relative
///     24004           the blob
///
/// ⚠ CAB_ASSETS_RE.md gives the blob at 0x5DE4 = 24036, which is 32 too far:
/// 4 + 4000 + 20000 = 24004, and 24004 + 951109 is EXACTLY the length of the
/// shipped file. Read 32 bytes late every sequence decodes to one huge piece of
/// nonsense — a 312x177 "muzzle flash" instead of 30x27 — which is how the
/// error was found. The note is wrong; the arithmetic and the file agree.
///
/// The fixup loop @0x4357ab turns the stored frame values into direct pointers
/// by adding the blob base, which is why they are blob-relative here.
///
/// A frame has the same grammar as everything else in this game, so it goes
/// through the shared decoder in <see cref="CwpFile"/>.
/// </summary>
public sealed class AnimFile
{
    public const int SeqTableOff = 4, SeqCount = 1000;
    public const int FrameTableOff = 4004, FrameCount = 5000;
    public const int BlobOff = 24004;

    private readonly byte[] _d;

    /// <summary>The size the header announces — 951109, and with the blob at
    /// 24004 that is the file to the byte.</summary>
    public int DeclaredBlobSize { get; private set; }

    /// <summary>How much blob there really is; equal to the declared size on an
    /// intact file, smaller on a truncated one.</summary>
    public int Usable { get; private set; }

    private AnimFile(byte[] d, int declared, int usable)
    {
        _d = d; DeclaredBlobSize = declared; Usable = usable;
    }

    public static AnimFile Load(string path) => FromBytes(File.ReadAllBytes(path));

    public static AnimFile FromBytes(byte[] d)
    {
        if (d.Length < BlobOff) throw new InvalidDataException("ANIM.CWA zu kurz");
        int size = (int)BitConverter.ToUInt32(d, 0);
        return new AnimFile(d, size, Math.Min(size, d.Length - BlobOff));
    }

    /// <summary>How many frames a sequence has, and where it starts.</summary>
    public (int Count, int Start) Sequence(int id)
    {
        if (id < 0 || id >= SeqCount) return (0, 0);
        int o = SeqTableOff + id * 4;
        return (BitConverter.ToUInt16(_d, o), BitConverter.ToUInt16(_d, o + 2));
    }

    /// <summary>Decode one frame, or null where the slot is empty.</summary>
    public CwpFile.Frame? Frame(int index)
    {
        if (index < 0 || index >= FrameCount) return null;
        uint off = BitConverter.ToUInt32(_d, FrameTableOff + index * 4);
        if (off == 0xFFFFFFFF || off >= (uint)Usable) return null;
        int at = BlobOff + (int)off;
        if (at + 2 > _d.Length) return null;
        int rows = _d[at];
        if (rows == 0 || rows > 200) return null;
        var f = CwpFile.DecodeFrameAt(_d, at);
        return f.Width == 0 || f.Width > 400 ? null : f;
    }
}
