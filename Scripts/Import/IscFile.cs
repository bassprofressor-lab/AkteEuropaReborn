namespace AkteEuropaReborn.Import;

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

/// <summary>
/// The InstallShield 3 cabinet the original CD ships its program files in
/// (<c>DATA1.CAB</c>, magic <c>ISc(</c>).
///
/// It matters because the discs keep <c>DATA\</c> and <c>LEVELS\</c> loose but
/// put everything else inside this archive: GAME.EXE, ROBO.CWR, ANIM.CWA,
/// FONT.CWD, PANEL.DTA. Installing from CD without reading it would leave the
/// importer without the exe tables and without a single sprite.
///
/// Layout, read out of the file itself and proven against the reference copies
/// in the tooling (see <see cref="ImportSelfTest.RunIsc"/>):
///
///   header  +0x00 u32 'ISc('   +0x04 u32 version (0x01000004)
///           +0x0c u32 descriptor offset      +0x14 u32 first data offset
///   descriptor  +0x28 u32 file count
///   record, 42 bytes, one per file, immediately before the name pool:
///           +0x00 u32 name offset (pool-relative)
///           +0x0a u32 size, unpacked      +0x0e u32 size, packed
///           +0x16/+0x1a DOS date and time +0x26 u32 data offset, absolute
///
/// The record table is not addressed by a header field but recognised: the
/// first record must point at the start of the data, because the files lie
/// back to back in table order — which also gives the check that the last one
/// ends exactly on the end of the file.
///
/// The compression is plain <b>raw DEFLATE</b> per file, written without a
/// final block, so a decompressor reports a truncated stream after delivering
/// exactly the announced number of bytes. Reading to the announced size and
/// stopping is therefore correct, not a workaround.
/// </summary>
public sealed class IscFile
{
    public const uint Signature = 0x28635349;      // 'ISc('
    public const int RecordSize = 42;

    public sealed class Entry
    {
        public int Index;
        public string Name = "";
        public int Size, Packed;
        public long Offset;
    }

    public string Path { get; private set; } = "";
    public uint Version { get; private set; }
    public long DataStart { get; private set; }
    public long TableStart { get; private set; }
    public readonly List<Entry> Files = new();

    public static bool LooksLikeCabinet(string path)
    {
        try
        {
            using var f = File.OpenRead(path);
            var b = new byte[4];
            return f.Read(b, 0, 4) == 4 && BitConverter.ToUInt32(b, 0) == Signature;
        }
        catch { return false; }
    }

    public static IscFile Load(string path)
    {
        var a = new IscFile { Path = path };
        using var f = File.OpenRead(path);

        var head = Read(f, 0, 0x20);
        if (BitConverter.ToUInt32(head, 0) != Signature)
            throw new InvalidDataException("Keine ISc(-Datei");
        a.Version = BitConverter.ToUInt32(head, 4);
        long desc = BitConverter.ToUInt32(head, 0x0c);
        a.DataStart = BitConverter.ToUInt32(head, 0x14);

        int count = (int)BitConverter.ToUInt32(Read(f, desc, 0x40), 0x28);
        if (count <= 0 || count > 4096) throw new InvalidDataException("Dateizahl unplausibel");

        // everything between the descriptor and the data: table then name pool
        var blob = Read(f, desc, (int)(a.DataStart - desc));
        int table = FindTable(blob, a.DataStart, count);
        a.TableStart = desc + table;

        int poolStart = table + RecordSize * count;
        int nameBase = poolStart - (int)BitConverter.ToUInt32(blob, table);

        for (int i = 0; i < count; i++)
        {
            int o = table + i * RecordSize;
            int nameOff = nameBase + (int)BitConverter.ToUInt32(blob, o);
            a.Files.Add(new Entry
            {
                Index = i,
                Name = ZString(blob, nameOff),
                Size = (int)BitConverter.ToUInt32(blob, o + 0x0a),
                Packed = (int)BitConverter.ToUInt32(blob, o + 0x0e),
                Offset = BitConverter.ToUInt32(blob, o + 0x26),
            });
        }
        return a;
    }

    /// <summary>The record table, recognised rather than addressed: the first
    /// record points at the start of the data.</summary>
    private static int FindTable(byte[] blob, long dataStart, int count)
    {
        int need = RecordSize * count;
        for (int p = 0; p + need <= blob.Length; p += 2)
        {
            if (BitConverter.ToUInt32(blob, p + 0x26) != dataStart) continue;
            uint size = BitConverter.ToUInt32(blob, p + 0x0a);
            uint packed = BitConverter.ToUInt32(blob, p + 0x0e);
            if (packed > 0 && packed <= size) return p;
        }
        throw new InvalidDataException("Satztabelle nicht gefunden");
    }

    /// <summary>Zero gaps and the last file ending exactly on the end of the
    /// file — the sharpest check there is on the record reading.</summary>
    public bool IsContiguous(out int gaps, out long end)
    {
        var byOffset = new List<Entry>(Files);
        byOffset.Sort((a, b) => a.Offset.CompareTo(b.Offset));
        long p = DataStart;
        gaps = 0;
        foreach (var e in byOffset)
        {
            if (e.Offset != p) gaps++;
            p = e.Offset + e.Packed;
        }
        end = p;
        return gaps == 0 && end == new FileInfo(Path).Length;
    }

    public Entry? Find(string name)
    {
        foreach (var e in Files)
            if (string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase)) return e;
        return null;
    }

    /// <summary>Unpack one file. The stream carries no final block, so it is
    /// read to the announced size and that is where it ends.</summary>
    public byte[] Extract(Entry e)
    {
        using var f = File.OpenRead(Path);
        var body = Read(f, e.Offset, e.Packed);
        var outp = new byte[e.Size];
        using var ms = new MemoryStream(body);
        using var z = new DeflateStream(ms, CompressionMode.Decompress);
        int got = 0;
        while (got < e.Size)
        {
            int n = z.Read(outp, got, e.Size - got);
            if (n <= 0) break;
            got += n;
        }
        if (got != e.Size)
            throw new InvalidDataException($"{e.Name}: {got} statt {e.Size} Bytes");
        return outp;
    }

    public byte[]? Extract(string name)
    {
        var e = Find(name);
        return e == null ? null : Extract(e);
    }

    private static byte[] Read(FileStream f, long at, int n)
    {
        var b = new byte[n];
        f.Seek(at, SeekOrigin.Begin);
        int got = 0;
        while (got < n)
        {
            int r = f.Read(b, got, n - got);
            if (r <= 0) break;
            got += r;
        }
        if (got != n) throw new EndOfStreamException();
        return b;
    }

    private static string ZString(byte[] b, int at)
    {
        int e = at;
        while (e < b.Length && b[e] != 0) e++;
        return Cp437.GetString(b, at, e - at);
    }
}
