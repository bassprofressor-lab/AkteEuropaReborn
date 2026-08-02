namespace AkteEuropaReborn.Import;

using System;
using System.Collections.Generic;
using System.IO;

/// <summary>
/// SOUNDS.CWN — the game's whole sound bank, 79,573,697 bytes of it, and the
/// last big file of the original nobody had opened.
///
/// <para><b>The layout, read off the loader @0x4c1990</b> (reached through the
/// thunk <c>0x4020f4</c> from the sound init @0x418780, which only calls it when
/// <c>byte[0x53c928]</c> says sound is on):</para>
///
/// <code>
///   +0x00  u32   count = 2000
///   +0x04  2000 x { i32 offset, i32 length }      = 16000 bytes
///   0x3e84 = 16004 .. EOF                          the sample bytes
/// </code>
///
/// The count is stated twice, which is why it can be trusted: the file's first
/// dword is 2000, and the reading loop walks <c>esi</c> from 4 to <c>0x3e84</c>
/// seeking and reading two dwords a time. The RAM copy at <c>0xb4be90</c> is
/// cleared with <c>0xfa0</c> = 4000 dwords, which is the same 2000 x 8 bytes.
///
/// <para><b>492 of the 2000 slots are filled</b> (the highest used index is
/// 1433), and they tile the file without a hole: walking them in order gives
/// <b>0 mismatches and a last end of exactly 79,573,697</b> — the file size.
/// That is what makes the layout certain rather than plausible.</para>
///
/// <para><b>The sign of the length is a flag, not damage.</b> @0x4c1a8b does
/// <c>test eax,eax; jle</c> and skips the DirectSound buffer for a negative
/// length. The magnitude is the real length either way (the tiling proves it).
/// So: <b>202 positive</b> entries, 195.6 seconds together — the effects, which
/// the original preloads into buffers; <b>290 negative</b>, 57 minutes — the
/// speech, which it reads when it needs it. We keep the same split.</para>
///
/// <para><b>The format is stated by the WAVEFORMATEX @0x4c19ed</b>:
/// <c>wFormatTag=1, nChannels=1, nSamplesPerSec=0x5622 (22050),
/// nAvgBytesPerSec=0x5622, nBlockAlign=1, wBitsPerSample=8</c> — one byte per
/// sample, so <b>22050 Hz, 8 bit, mono, unsigned</b>. The bytes agree: mean
/// 127.5 over a sample, spread 6..217, i.e. centred on 128. (The DirectSound
/// primary buffer @0x4c1860 runs at 22050/16/stereo, but that is the mixer, not
/// the samples.)</para>
///
/// <para>The file is 79 MB, so it is read through a stream and never held whole.
/// </para>
/// </summary>
public sealed class SoundBank : IDisposable
{
    /// <summary>Slots in the directory. Both the file head and the loader's
    /// loop bound say 2000.</summary>
    public const int SlotCount = 2000;

    /// <summary>Where the sample bytes start: 4 + 2000 * 8.</summary>
    public const int DataStart = 4 + SlotCount * 8;

    /// <summary>From the WAVEFORMATEX @0x4c19ed.</summary>
    public const int SampleRate = 22050, Bits = 8, Channels = 1;

    /// <summary>One filled slot.</summary>
    public readonly struct Entry
    {
        /// <summary>The directory slot — this is the number the game calls the
        /// sound by, so gaps are kept rather than renumbered.</summary>
        public readonly int Index;
        public readonly long Offset;
        public readonly int Length;

        /// <summary>The original creates a DirectSound buffer for this one up
        /// front (its length was stored positive). False for the speech, which
        /// it reads on demand.</summary>
        public readonly bool Preloaded;

        public Entry(int index, long offset, int length, bool preloaded)
        { Index = index; Offset = offset; Length = length; Preloaded = preloaded; }

        public double Seconds => (double)Length / SampleRate;
    }

    private readonly FileStream _f;

    /// <summary>The filled slots, in file order.</summary>
    public readonly List<Entry> Entries = new();

    /// <summary>What the header claims — 2000 on every copy seen.</summary>
    public readonly int Declared;

    /// <summary>True when the entries tile the file exactly: no gap, no overlap,
    /// last end == file size. This is the check that proved the format, so it is
    /// carried into the reader instead of being left in a notebook.</summary>
    public readonly bool Contiguous;

    public long FileSize => _f.Length;

    public int Preloaded { get { int n = 0; foreach (var e in Entries) if (e.Preloaded) n++; return n; } }
    public int OnDemand => Entries.Count - Preloaded;

    /// <summary>Total sample bytes, which at 1 byte per sample is also the
    /// number of samples.</summary>
    public long TotalBytes { get { long n = 0; foreach (var e in Entries) n += e.Length; return n; } }

    public SoundBank(string path)
    {
        _f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var head = new byte[DataStart];
        if (_f.Read(head, 0, head.Length) != head.Length)
            throw new InvalidDataException("SOUNDS.CWN: Kopf unvollstaendig");

        Declared = BitConverter.ToInt32(head, 0);
        if (Declared != SlotCount)
            throw new InvalidDataException(
                $"SOUNDS.CWN: Kopf sagt {Declared} Eintraege, der Lader @0x4c1990 laeuft ueber {SlotCount}");

        for (int i = 0; i < SlotCount; i++)
        {
            int off = BitConverter.ToInt32(head, 4 + i * 8);
            int len = BitConverter.ToInt32(head, 8 + i * 8);
            if (off <= 0 || len == 0) continue;              // free slot
            // negative length = "do not preload", the magnitude is the length
            Entries.Add(new Entry(i, off, Math.Abs(len), len > 0));
        }

        long cur = DataStart;
        bool ok = Entries.Count > 0;
        foreach (var e in Entries)
        {
            if (e.Offset != cur) { ok = false; break; }
            cur = e.Offset + e.Length;
        }
        Contiguous = ok && cur == _f.Length;
    }

    /// <summary>The raw sample bytes of one entry — unsigned 8 bit, 22050 Hz,
    /// mono, exactly as they lie in the file.</summary>
    public byte[] Read(in Entry e)
    {
        var buf = new byte[e.Length];
        _f.Seek(e.Offset, SeekOrigin.Begin);
        int got = 0;
        while (got < buf.Length)
        {
            int n = _f.Read(buf, got, buf.Length - got);
            if (n <= 0) throw new EndOfStreamException($"SOUNDS.CWN: Klang {e.Index} reicht ueber das Dateiende");
            got += n;
        }
        return buf;
    }

    /// <summary>A 44-byte canonical RIFF header for one of these. WAV stores
    /// 8-bit PCM unsigned, which is what the file already holds, so the sample
    /// bytes are copied through untouched.</summary>
    public static byte[] WavHeader(int dataBytes)
    {
        var h = new byte[44];
        void Str(int at, string s) { for (int i = 0; i < s.Length; i++) h[at + i] = (byte)s[i]; }
        void U32(int at, int v) { BitConverter.GetBytes(v).CopyTo(h, at); }
        void U16(int at, int v) { BitConverter.GetBytes((ushort)v).CopyTo(h, at); }

        Str(0, "RIFF"); U32(4, 36 + dataBytes); Str(8, "WAVE");
        Str(12, "fmt "); U32(16, 16); U16(20, 1);            // PCM
        U16(22, Channels); U32(24, SampleRate);
        U32(28, SampleRate * Channels * Bits / 8);           // avg bytes/s
        U16(32, Channels * Bits / 8); U16(34, Bits);
        Str(36, "data"); U32(40, dataBytes);
        return h;
    }

    public void Dispose() => _f.Dispose();
}
