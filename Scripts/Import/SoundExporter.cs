namespace AkteEuropaReborn.Import;

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

/// <summary>
/// The fifth extractor: the game's sound, out of the player's own installation.
///
///   Sound/sNNNN.wav    one file per filled slot of <see cref="SoundBank"/>
///   Sound/sounds.json  the index, so nothing has to guess a file name
///   Sound/N.mid        the six music files, copied through
///
/// The WAVs carry the original bytes verbatim — 8-bit PCM in a RIFF file is
/// unsigned, which is exactly how the bank stores it, so only a 44-byte header
/// is put in front. Anything that can play a .wav can therefore play these,
/// which matters when the next step is deciding by ear which sound is which.
///
/// The slot number is kept as the file name: the game calls its sounds by that
/// number, and 1508 of the 2000 slots are empty. Renumbering them would throw
/// away the one piece of identity they have.
/// </summary>
public sealed class SoundExporter
{
    private readonly string _dst;

    public int Written, Music;
    public long Bytes;

    public SoundExporter(string soundDir) => _dst = soundDir.TrimEnd('/', '\\');

    public static string FileName(int index) => $"s{index:0000}.wav";

    /// <summary>Writes every filled slot as a .wav and an index beside them.</summary>
    public void Write(SoundBank bank, Action<string>? say = null)
    {
        Directory.CreateDirectory(_dst);

        var sb = new StringBuilder(1 << 16);
        sb.Append("{\"_note\":\"SOUNDS.CWN: u32 count=2000, then 2000 x {i32 offset, i32 length}, ");
        sb.Append("samples from 0x3e84. Loader @0x4c1990, format from the WAVEFORMATEX @0x4c19ed: ");
        sb.Append("22050 Hz, 8 bit, mono, unsigned\",");
        sb.Append("\"_preloaded\":\"a NEGATIVE length in the directory means the original does not ");
        sb.Append("build a DirectSound buffer for it up front (@0x4c1a8b test/jle) - those are the ");
        sb.Append("speech samples. The magnitude is the length either way\",");
        sb.Append($"\"rate\":{SoundBank.SampleRate},\"bits\":{SoundBank.Bits},");
        sb.Append($"\"channels\":{SoundBank.Channels},\"slots\":{SoundBank.SlotCount},");
        sb.Append($"\"contiguous\":{(bank.Contiguous ? "true" : "false")},");
        sb.Append("\"sounds\":[");

        bool first = true;
        foreach (var e in bank.Entries)
        {
            byte[] pcm = bank.Read(e);
            string path = $"{_dst}/{FileName(e.Index)}";
            using (var w = new FileStream(path, FileMode.Create, FileAccess.Write))
            {
                w.Write(SoundBank.WavHeader(pcm.Length), 0, 44);
                w.Write(pcm, 0, pcm.Length);
            }
            Written++;
            Bytes += pcm.Length;

            if (!first) sb.Append(',');
            first = false;
            sb.Append($"{{\"index\":{e.Index},\"file\":\"{FileName(e.Index)}\",");
            sb.Append($"\"offset\":{e.Offset},\"bytes\":{e.Length},");
            sb.Append($"\"seconds\":{e.Seconds.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture)},");
            sb.Append($"\"preloaded\":{(e.Preloaded ? "true" : "false")}}}");
        }
        sb.Append("]}");
        File.WriteAllText(_dst + "/sounds.json", sb.ToString(), new UTF8Encoding(false));

        say?.Invoke($"Klaenge: {Written} von {SoundBank.SlotCount} Plaetzen, " +
                    $"{bank.Preloaded} vorgeladen / {bank.OnDemand} bei Bedarf, " +
                    $"{Bytes / 1024 / 1024} MB = {bank.TotalBytes / SoundBank.SampleRate / 60} min" +
                    (bank.Contiguous ? " — Kette lueckenlos bis aufs Dateiende"
                                     : " — ACHTUNG: Kette nicht lueckenlos"));
    }

    /// <summary>The fire-sound table out of the exe: 22 rows, each naming the
    /// sound a weapon class shoots with (see
    /// <see cref="ExeTables.FireSoundTable"/>). Written beside the sounds so the
    /// game can give every weapon its own report instead of one bang for all.
    ///
    /// The plausibility check is written into the file rather than kept in a
    /// notebook: the bases must be even and inside the bank's first block.
    /// </summary>
    public void WriteWeaponSounds(ExeTables t, Action<string>? say = null)
    {
        Directory.CreateDirectory(_dst);
        if (!t.FireSoundsFound)
        {
            say?.Invoke("Waffenklaenge: Tabelle in diesem Programmstand nicht gefunden — " +
                        "nichts geschrieben (jede Waffe bleibt still statt falsch)");
            return;
        }
        int[] v = t.FireSounds();
        int good = 0;
        for (int i = 0; i < v.Length; i++) if (v[i] >= 0 && v[i] < 40 && v[i] % 2 == 0) good++;

        var sb = new StringBuilder(1024);
        sb.Append("{\"_note\":\"fire sounds. A weapon component names a sound CLASS in its stats ");
        sb.Append("record at +0x1c; the class indexes this table (0x4f98f2, stride 22) and the first ");
        sb.Append("u16 of the row is the base sound. The game plays base or base+1 at random ");
        sb.Append("(@0x40c4c0: mov cl,[edx*2+0x5045bc]; 11*cl; mov di,[ecx*2+0x4f98f2]; rand&1)\",");
        sb.Append($"\"_check\":\"{good} of {v.Length} rows are even and below 40, which is the size ");
        sb.Append("of the bank's first block (40 sounds = 20 classes of two)\",");
        sb.Append($"\"stats_field\":{ExeTables.StatsSoundClass},\"base\":[");
        for (int i = 0; i < v.Length; i++) { if (i > 0) sb.Append(','); sb.Append(v[i]); }
        sb.Append("]}");
        File.WriteAllText(_dst + "/weapon_sounds.json", sb.ToString(), new UTF8Encoding(false));
        say?.Invoke($"Waffenklaenge: {v.Length} Klassen, {good} davon plausibel (gerade und unter 40)");
    }

    /// <summary>0.MID .. 5.MID, copied through unchanged.
    ///
    /// The original plays them with MCI — <c>"open sequencer!%s alias playerSnd"</c>
    /// and <c>"play playerSnd from %d notify"</c>, with the name built from
    /// <c>"\%d.mid"</c> @0x4f7918 (used @0x416bfb). We do the same, so the files
    /// are wanted as they are; nothing is transcoded and nothing is invented.
    /// </summary>
    /// <param name="find">a loose file, as in an installation</param>
    /// <param name="asset">the same name out of the cabinet, which is where the
    /// discs keep them — 0.MID..5.MID are in DATA1.CAB beside SOUNDS.CWN, and a
    /// CD install would have come out mute without this</param>
    public void WriteMusic(Func<string, string?> find, Func<string, byte[]?>? asset = null,
                           Action<string>? say = null)
    {
        Directory.CreateDirectory(_dst);
        var names = new List<string>();
        for (int i = 0; i <= 9; i++)
        {
            string? src = find($"{i}.MID") ?? find($"{i}.mid");
            if (src != null) File.Copy(src, $"{_dst}/{i}.mid", true);
            else
            {
                byte[]? b = asset?.Invoke($"{i}.MID");
                if (b == null || b.Length == 0) continue;
                File.WriteAllBytes($"{_dst}/{i}.mid", b);
            }
            names.Add($"{i}.mid");
            Music++;
        }
        if (Music == 0) { say?.Invoke("Musik: keine .MID gefunden"); return; }
        File.WriteAllText(_dst + "/music.json",
            "{\"_note\":\"the original plays these through MCI: open sequencer, play from N notify " +
            "(name built from \\\"\\\\%d.mid\\\" @0x4f7918, used @0x416bfb)\"," +
            "\"tracks\":[\"" + string.Join("\",\"", names) + "\"]}",
            new UTF8Encoding(false));
        say?.Invoke($"Musik: {Music} MIDI-Stuecke kopiert");
    }
}
