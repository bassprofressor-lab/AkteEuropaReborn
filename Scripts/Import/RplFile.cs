namespace AkteEuropaReborn.Import;

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

/// <summary>
/// <b>DIE FILME DES ORIGINALS</b> — der Behälter <c>.RPL</c>, gelesen am
/// 20.08.2026.
///
/// <para>Es ist <b>ARMovie</b> in der Fassung von Eidos, die sich selbst
/// <c>ESCAPE 1.0</c> nennt: 320×240, Bildformat <b>124</b>. Über alle 35 Filme
/// beider CDs zusammen <b>894.583.232 Byte, 104.488 Bilder, 70 min 43 s</b> —
/// der mit Abstand grösste Posten des Originals.</para>
///
/// <para><b>Der Kopf ist Klartext</b>, zeilenweise: <c>ARMovie</c>, Name,
/// Copyright, Autor (<c>ESCAPE 1.0</c>), dann siebzehn Zahlen, je eine Zeile.
/// ⚠ <b>Die Versätze sind VARIABEL</b> — Zeile 1 ist je Film verschieden lang.
/// Wer feste Versätze nimmt, liest Müll; deshalb wird hier gezählt statt
/// gesprungen.</para>
///
/// <para><b>Der Katalog</b> steht am Dateiende, eine Zeile je Bild:
/// <c>VideoVersatz,VideoGrösse;TonGrösse</c>. Der Ton folgt unmittelbar hinter
/// dem Bild, und der nächste Abschnitt beginnt auf 4 ausgerichtet —
/// <b>0 Verstösse über alle 104.488 Einträge</b>. Das erste Doppelwort des
/// Bildes wiederholt die Videogrösse, ebenfalls 104.488 von 104.488.</para>
///
/// <para>⚠ <b>Eine Falle, in allen 35 Dateien gleich:</b> das Kopffeld
/// <c>number_of_chunks</c> ist um <b>eins zu klein</b>. Maßgeblich ist die Zahl
/// der Katalogzeilen, nicht das Feld.</para>
///
/// <para>⚠ <b>Kein Schlüsselbild-Verzeichnis</b> (<c>offset_to_key_frames</c>
/// zeigt ins Leere). Vorwärts überspringen ist billig, Springen teuer — wer an
/// eine Stelle will, muss von vorn dekodieren.</para>
///
/// <para><b>Wo die Filme liegen:</b> auf beiden CDs unter <c>MOVIES\</c> und,
/// wenn der Spieler das Original installiert hat, zusätzlich in dessen
/// <c>Movies\</c>-Ordner (dort 17 Dateien, 412 MB — die übrigen nur auf CD 2).
/// ⚠ <b>Wir liefern keinen einzigen Film aus</b>; sie bleiben beim Spieler, wie
/// Gelände, Einheiten und Karten auch.</para>
/// </summary>
public sealed class RplFile
{
    /// <summary>Eine Zeile des Katalogs — ein Bild.</summary>
    public readonly struct Chunk
    {
        public readonly long VideoAt;
        public readonly int VideoSize, AudioSize;
        public Chunk(long at, int vs, int a) { VideoAt = at; VideoSize = vs; AudioSize = a; }
        public long AudioAt => VideoAt + VideoSize;
    }

    public string Path { get; }
    public string Name { get; } = "";
    public string Author { get; } = "";
    public int Width { get; }
    public int Height { get; }
    public int BitsPerPixel { get; }
    public double Fps { get; }
    public int VideoFormat { get; }
    public int SoundFormat { get; }
    public int SoundRate { get; }
    public int SoundChannels { get; }
    public int SoundBits { get; }

    /// <summary>Was im Kopf steht — ⚠ um eins zu klein, siehe Klassenkommentar.
    /// Für die Wahrheit <see cref="Chunks"/> zählen.</summary>
    public int ChunksClaimed { get; }

    public IReadOnlyList<Chunk> Chunks => _chunks;
    private readonly List<Chunk> _chunks = new();

    /// <summary>Die siebzehn Kopfzahlen unter ihren Namen des Formats, roh —
    /// damit ein späterer Leser nicht dieselbe Zeilenzählerei wiederholen
    /// muss.</summary>
    public IReadOnlyDictionary<string, string> Header => _header;
    private readonly Dictionary<string, string> _header = new();

    private static readonly string[] Fields =
    {
        "video_format", "width", "height", "bits_per_pixel", "fps",
        "sound_format", "sound_rate", "sound_channels", "sound_bits",
        "frames_per_chunk", "number_of_chunks", "even_chunk_size",
        "odd_chunk_size", "offset_to_chunk_cat", "offset_to_sprite",
        "size_of_sprite", "offset_to_key_frames",
    };

    public RplFile(string path)
    {
        Path = path;
        byte[] head;
        long size;
        using (var f = File.OpenRead(path))
        {
            size = f.Length;
            head = new byte[(int)Math.Min(4096, size)];
            f.ReadExactly(head, 0, head.Length);
        }

        var lines = SplitLines(head, out var lengths);
        if (lines.Count < 4 + Fields.Length || lines[0] != "ARMovie")
            throw new InvalidDataException($"{path}: kein ARMovie (erste Zeile {Quote(lines.Count > 0 ? lines[0] : "")})");

        Name = lines[1];
        Author = lines[3];
        for (int i = 0; i < Fields.Length; i++)
        {
            string ln = lines[4 + i];
            int sp = ln.IndexOf(' ');
            _header[Fields[i]] = (sp < 0 ? ln : ln[..sp]).Trim();
        }

        VideoFormat = Num("video_format");
        Width = Num("width");
        Height = Num("height");
        BitsPerPixel = Num("bits_per_pixel");
        Fps = double.TryParse(_header["fps"], System.Globalization.NumberStyles.Float,
                              System.Globalization.CultureInfo.InvariantCulture, out double f2) ? f2 : 0;
        SoundFormat = Num("sound_format");
        SoundRate = Num("sound_rate");
        SoundChannels = Num("sound_channels");
        SoundBits = Num("sound_bits");
        ChunksClaimed = Num("number_of_chunks");

        long catAt = Num("offset_to_chunk_cat");
        if (catAt <= 0 || catAt >= size)
            throw new InvalidDataException($"{path}: Katalogversatz {catAt} liegt ausserhalb der Datei ({size})");

        using (var f = File.OpenRead(path))
        {
            f.Position = catAt;
            var buf = new byte[size - catAt];
            f.ReadExactly(buf, 0, buf.Length);
            foreach (string raw in Encoding.Latin1.GetString(buf).Split('\n'))
            {
                string ln = raw.Trim();
                if (ln.Length == 0) continue;
                int semi = ln.IndexOf(';');
                int comma = ln.IndexOf(',');
                if (semi < 0 || comma < 0 || comma > semi) continue;
                if (long.TryParse(ln[..comma], out long vo) &&
                    int.TryParse(ln[(comma + 1)..semi], out int vs) &&
                    int.TryParse(ln[(semi + 1)..], out int au))
                    _chunks.Add(new Chunk(vo, vs, au));
            }
        }
        _ = lengths;
    }

    private int Num(string key)
        => _header.TryGetValue(key, out string? v) && int.TryParse(v, out int n) ? n : 0;

    private static string Quote(string s) => "\"" + s + "\"";

    /// <summary>Zeilen aus dem Kopf, mit ihren Längen — die Längen sind der
    /// Grund, warum hier gezählt und nicht gesprungen wird.</summary>
    private static List<string> SplitLines(byte[] head, out List<int> lengths)
    {
        var outp = new List<string>();
        lengths = new List<int>();
        int start = 0;
        for (int i = 0; i < head.Length && outp.Count < 40; i++)
        {
            if (head[i] != (byte)'\n') continue;
            outp.Add(Encoding.Latin1.GetString(head, start, i - start).TrimEnd('\r'));
            lengths.Add(i - start + 1);
            start = i + 1;
        }
        return outp;
    }

    /// <summary>Die Rohbytes eines Bildes.</summary>
    public byte[] ReadVideo(int frame)
    {
        var c = _chunks[frame];
        var buf = new byte[c.VideoSize];
        using var f = File.OpenRead(Path);
        f.Position = c.VideoAt;
        f.ReadExactly(buf, 0, buf.Length);
        return buf;
    }

    /// <summary>Die Rohbytes des Tons eines Bildes. ⚠ Bei den 34 Filmen mit
    /// IMA-ADPCM tragen die Stücke <b>keinen eigenen Kopf</b> — sie sind nur im
    /// Zusammenhang dekodierbar, nicht einzeln.</summary>
    public byte[] ReadAudio(int frame)
    {
        var c = _chunks[frame];
        var buf = new byte[c.AudioSize];
        if (buf.Length == 0) return buf;
        using var f = File.OpenRead(Path);
        f.Position = c.AudioAt;
        f.ReadExactly(buf, 0, buf.Length);
        return buf;
    }

    /// <summary>Die zwei Doppelworte am Anfang eines Bildes: Fahnen und die
    /// wiederholte Grösse.</summary>
    public (uint Flags, uint Size)? FrameHead(int frame)
    {
        var c = _chunks[frame];
        if (c.VideoSize < 8) return null;
        var b = new byte[8];
        using var f = File.OpenRead(Path);
        f.Position = c.VideoAt;
        f.ReadExactly(b, 0, 8);
        return (BitConverter.ToUInt32(b, 0), BitConverter.ToUInt32(b, 4));
    }

    /// <summary>Die Selbstprüfung des Behälters: Ausrichtung auf 4 und die
    /// wiederholte Grösse. ⚠ Beide müssen 0 ergeben — gemessen sind es
    /// 0 von 104.488.</summary>
    public (int Chunks, int MisAligned, int SizeMismatch) Check()
    {
        int align = 0, sizeBad = 0;
        using var f = File.OpenRead(Path);
        var b = new byte[8];
        for (int i = 0; i < _chunks.Count; i++)
        {
            var c = _chunks[i];
            if (i + 1 < _chunks.Count)
            {
                long next = (c.VideoAt + c.VideoSize + c.AudioSize + 3) & ~3L;
                if (next != _chunks[i + 1].VideoAt) align++;
            }
            if (c.VideoSize >= 8)
            {
                f.Position = c.VideoAt;
                f.ReadExactly(b, 0, 8);
                if (BitConverter.ToUInt32(b, 4) != (uint)c.VideoSize) sizeBad++;
            }
        }
        return (_chunks.Count, align, sizeBad);
    }
}
