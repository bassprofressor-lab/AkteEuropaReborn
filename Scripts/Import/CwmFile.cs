namespace AkteEuropaReborn.Import;

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

/// <summary>
/// A map file: `NN.CWM` is a campaign level, `N.DM` a full saved state.
///
/// Both are the same container — a small header followed by a run of sections
/// that the loader (`map_loader` @0x41e070) reads one after the other through
/// the same (de)compressing reader (@0x4023a6). Section 1 is the terrain
/// record grid and is W*H*4 bytes; sections 2 onwards have fixed decoded sizes
/// (see <see cref="CwmSections"/>). A .DM carries all 131 with nothing left
/// over; a .CWM stops after 38.
///
/// The compression is a plain run marker: the first byte of a chunk is the
/// marker; wherever it appears, the next byte is a count and the one after it
/// the value to repeat — a count of zero means the marker byte itself. Small
/// sections (up to 0x64 bytes) are stored raw.
///
/// Ported from the Python reader and checked against it, see ImportSelfTest.
/// </summary>
public sealed class CwmFile
{
    public string Stem = "";
    public string Mission = "";
    public string Comment = "";
    public int Tileset, Width, Height;
    public bool Compressed;
    public int TrailingBytes;

    /// <summary>Section 1 is at index 0 — the terrain record grid.</summary>
    public readonly List<byte[]> Sections = new();

    /// <summary>Section numbers as the documentation names them: sec1 is the
    /// record grid, so `Sec(3)` is the building table.</summary>
    public byte[]? Sec(int number)
        => number >= 1 && number - 1 < Sections.Count ? Sections[number - 1] : null;

    public byte[] Records => Sections.Count > 0 ? Sections[0] : Array.Empty<byte>();

    private sealed class Reader
    {
        private readonly byte[] _d;
        public int P;
        public Reader(byte[] d) { _d = d; }
        public int Left => _d.Length - P;
        public byte U8() => _d[P++];
        public ushort U16() { ushort v = BitConverter.ToUInt16(_d, P); P += 2; return v; }
        public uint U32() { uint v = BitConverter.ToUInt32(_d, P); P += 4; return v; }
        public byte[] Take(int n)
        {
            if (n < 0 || P + n > _d.Length) throw new EndOfStreamException();
            var b = new byte[n];
            Array.Copy(_d, P, b, 0, n);
            P += n;
            return b;
        }
    }

    /// <summary>One run-length chunk: marker, then value/count pairs.</summary>
    private static byte[] DecodeChunk(byte[] body)
    {
        byte marker = body[0];
        var outp = new List<byte>(body.Length * 2);
        int i = 1, len = body.Length;
        while (i < len)
        {
            byte b = body[i];
            if (b == marker)
            {
                byte cnt = body[i + 1];
                if (cnt != 0)
                {
                    byte v = body[i + 2];
                    for (int k = 0; k < cnt; k++) outp.Add(v);
                    i += 3;
                }
                else { outp.Add(marker); i += 2; }
            }
            else { outp.Add(b); i++; }
        }
        return outp.ToArray();
    }

    private static byte[] ReadSection(Reader r, int outSize, bool compressed)
    {
        if (!(compressed && outSize > 0x64)) return r.Take(outSize);
        var outp = new List<byte>(outSize);
        while (outp.Count < outSize)
        {
            int size = (int)r.U32();
            var body = r.Take(size - 4);
            outp.AddRange(DecodeChunk(body));
        }
        return outp.ToArray();
    }

    private static string CStr(byte[] b) => Cp437.GetString(b, 0, b.Length);

    public static CwmFile Load(string path)
    {
        var m = FromBytes(File.ReadAllBytes(path));
        m.Stem = Path.GetFileNameWithoutExtension(path);
        return m;
    }

    public static CwmFile FromBytes(byte[] d)
    {
        var r = new Reader(d);
        var m = new CwmFile();
        byte magic = r.U8();
        if (magic != 0x43) throw new InvalidDataException("Keine CWM/DM-Datei");
        byte comp = r.U8();
        r.U8();                       // sub
        r.U8();                       // b3
        m.Tileset = r.U8();
        m.Compressed = comp == 1;
        m.Comment = CStr(r.Take(21));
        r.U16();                      // always 1
        m.Mission = CStr(r.Take(21));
        m.Width = r.U16();
        m.Height = r.U16();
        int flag = r.U16();
        if (flag == 0xffff) r.Take(0x14);

        m.Sections.Add(ReadSection(r, m.Width * m.Height * 4, m.Compressed));
        foreach (int size in CwmSections.Sizes)
        {
            try { m.Sections.Add(ReadSection(r, size, m.Compressed)); }
            catch (Exception) { break; }        // a .CWM simply ends early
        }
        m.TrailingBytes = r.Left;
        return m;
    }
}
