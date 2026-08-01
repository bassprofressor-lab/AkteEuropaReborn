namespace AkteEuropaReborn.Import;

using System.IO;

/// <summary>
/// A .PAL palette from the original game.
///
/// Layout, recovered from GAME.EXE and verified against all 23 palettes:
/// an 8-byte header, then 256 entries of three bytes. The values are **full
/// 8-bit RGB** — there is no VGA &lt;&lt;2 scaling, which was the first wrong
/// guess and would have made every sprite four times too dark.
/// </summary>
public sealed class PalFile
{
    public readonly byte[] R = new byte[256];
    public readonly byte[] G = new byte[256];
    public readonly byte[] B = new byte[256];

    public const int HeaderSize = 8;
    public const int FileSize = HeaderSize + 256 * 3;

    private PalFile() { }

    public static PalFile Load(string path) => FromBytes(File.ReadAllBytes(path));

    public static PalFile FromBytes(byte[] raw)
    {
        if (raw.Length < FileSize)
            throw new InvalidDataException($"PAL zu kurz: {raw.Length} statt {FileSize} Bytes");
        var p = new PalFile();
        for (int i = 0; i < 256; i++)
        {
            int o = HeaderSize + i * 3;
            p.R[i] = raw[o];
            p.G[i] = raw[o + 1];
            p.B[i] = raw[o + 2];
        }
        return p;
    }
}
