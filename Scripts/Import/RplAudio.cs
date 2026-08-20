namespace AkteEuropaReborn.Import;

using System;
using System.IO;

/// <summary>
/// <b>DER TON DER FILME</b> — die zweite Hälfte von <see cref="RplFile"/>.
///
/// <para>Zwei Formen, und das Kopffeld <c>sound_format</c> sagt welche:</para>
/// <list type="bullet">
///   <item><b>34 Filme: <c>sound_format</c> 101 = 0x65</b>, 4 Bit, 22050 Hz,
///   stereo. Ein Nibbel ist eine Abtastung, das Verhältnis roh zu PCM ist damit
///   <b>genau 4,00</b>. ffprobe nennt den Strom <c>adpcm_ima_escape</c>,
///   »ADPCM IMA Acorn Escape«, Kennung 0x0065.</item>
///   <item><b><c>INTRO.RPL</c>: <c>sound_format</c> 1, 16 Bit — rohes PCM
///   s16le.</b> 28.193.132 Byte byteweise identisch mit ffmpegs Ausgabe — dort
///   ist nichts zu dekodieren, nur durchzureichen.</item>
/// </list>
///
/// <para><b>WOHER DIESE FASSUNG STAMMT: aus <c>F:\Akte Europa\WINSTR.DLL</c>
/// disassembliert</b> (32-bit PE, Basis 0x10000000, 74.240 B) — nicht aus
/// ffmpeg portiert und nicht geraten. Der Tondekoder liegt dort <b>zweimal</b>,
/// Rumpf für Rumpf gleich:</para>
/// <list type="bullet">
///   <item><b>Stereo @0x100011D0</b> — Tafeln 0x10010070 (89 Schrittweiten) und
///   0x10010030 (16 Indexschritte); Zustand: Vorhersage 0x10010207/0x10010210
///   (je ein <c>word</c>), Schrittindex 0x1001020C/0x10010214 (je ein
///   <c>byte</c>).</item>
///   <item><b>Mono @0x10002450</b> — zweite Tafelkopie 0x10010270/0x10010230,
///   Zustand 0x10010407/0x1001040C. Dort laufen <b>beide</b> Nibbel eines Bytes
///   durch <b>denselben</b> Zustand.</item>
/// </list>
/// <para>Ausgewählt wird an <c>cmp dword ptr [ecx+0x1C], 1</c> @0x10002E07:
/// mehr als ein Kanal → Stereo-Rumpf, sonst Mono-Rumpf. Ist die Rohfahne
/// <c>[ecx+0x50]</c> gesetzt (@0x10002DE6), wird gar nicht dekodiert, sondern
/// mit <c>rep movsd</c> @0x100050F0 durchgereicht — das ist der PCM-Weg von
/// <c>INTRO.RPL</c>. ⚠ Beide Fahnen kommen als Argumente von
/// <c>Streamer_InitSound</c> (@0x100025C0, <c>[eax+0x50]</c> @0x1000262F,
/// <c>[edi+0x1C]</c> @0x1000264F); die DLL liest den Kopf der <c>.RPL</c>
/// nicht selbst.</para>
///
/// <para>⚠⚠ <b>WORIN SICH »IMA ESCAPE« VOM GEWÖHNLICHEN IMA UNTERSCHEIDET —
/// EINE EINZIGE ZEILE, UND SIE IST DIE GANZE SACHE.</b> Die Tafeln sind die
/// des üblichen IMA, die <b>Rekonstruktion ist es nicht</b>:</para>
/// <code>
///   üblicher IMA: diff = step/8 + (b0 ? step/4 : 0) + (b1 ? step/2 : 0) + (b2 ? step : 0)
///   IMA Escape:   diff = ( (b2 ? step*4 : 0) + (b1 ? step*2 : 0) + (b0 ? step : 0) ) / 4
///               = (step * delta) &gt;&gt; 2        mit delta = Nibbel &amp; 7
/// </code>
/// <para><b>Zwei Unterschiede in dieser einen Zeile:</b> (1) der Sockel
/// <c>step/8</c> fehlt <b>ganz</b>; (2) es wird <b>erst multipliziert, dann
/// geschoben</b> — einmal abgeschnitten statt dreimal. Beides zusammen ist an
/// <c>step=7, delta=3</c> abzulesen: üblicher IMA 4, ffmpegs
/// Multiplikationsform <c>((2·delta+1)·step)&gt;&gt;3</c> 6, <b>Escape 5</b> —
/// und 5 steht in ffmpegs Ausgabe. Gelesen an @0x10001225…0x10001247:
/// <c>test eax,4 / mov ebx,edx / shl ebx,2</c> · <c>test eax,2 / mov ecx,edx /
/// add ecx,ecx / add ebx,ecx</c> · <c>test eax,1 / add ebx,edx</c> ·
/// <c>shr ebx,2</c>. ⚠ Genau daran ist die Fassung vom Vormittag gescheitert:
/// sie hatte die Tafeln richtig und die Rechnung falsch.</para>
///
/// <para>⚠ <b>Das obere Nibbel kommt ZUERST.</b> @0x100011E7:
/// <c>mov al,[esi] / shr al,4</c>, und dieses Ergebnis wird als erstes
/// geschrieben (@0x10001284). Das obere Nibbel gehört also dem <b>ersten</b>
/// Kanal (links), das untere dem zweiten (rechts); die frühere Fassung hatte es
/// umgekehrt.</para>
///
/// <para><b>Der Rest ist der übliche IMA</b>, und zwar genau so: Schrittweite
/// <b>vor</b> der Indexfortschreibung gegriffen (<c>mov edx,[ecx*4+0x10010070]</c>
/// steht vor <c>add ecx,[eax*4+0x10010030]</c>); Index geklemmt auf 0…88
/// (<c>cmp ecx,-1</c> → 0, <c>cmp ecx,0x59</c> → 0x58); Vorzeichenbit 8 zieht
/// ab, sonst wird addiert; Vorhersage geklemmt auf −32768…32767
/// (<c>cmp ecx,0x8000</c> → 0x7FFF, <c>cmp ecx,0xFFFF7FFF</c> → 0xFFFF8000) und
/// als <c>short</c> abgelegt.</para>
///
/// <para><b>Die Indextafel ist auf 16 Einträge verdoppelt</b>, und der Grund
/// steht im Code: gegriffen wird mit dem <b>ganzen</b> Nibbel
/// (<c>[eax*4+0x10010030]</c>, <c>eax</c> = 0…15), das Vorzeichenbit wird also
/// <b>nicht</b> ausmaskiert. Die zweite Hälfte ist Wort für Wort die erste;
/// beide Tafelkopien wurden gegen das <c>.data</c>-Abbild geprüft und sind
/// gleich.</para>
///
/// <para>⚠⚠ <b>DIE TONSTÜCKE HABEN KEINEN EIGENEN KOPF.</b> Vorhersage und
/// Schrittindex laufen über die Stückgrenzen hinweg weiter — ein einzelnes
/// Stück ist deshalb <b>nicht</b> für sich dekodierbar. Wer bei Bild 500
/// einsteigt, muss den Ton von Bild 0 an mitrechnen. Genau darum nimmt
/// <see cref="DecodeAll"/> den ganzen Film und nicht ein Stück.</para>
///
/// <para>⚠ <b>Im Original läuft der Zustand sogar über FILMgrenzen weiter.</b>
/// Er steht in sechs festen Zellen der DLL, und ein roher Dword-Scan über
/// <c>.text</c> findet <b>16</b> Zugriffe darauf — <b>alle</b> im Dekoder
/// selbst, <b>kein einziger</b> setzt zurück. Er fängt nur deshalb bei null an,
/// weil das <c>.data</c>-Abbild an diesen sechs Stellen null enthält (geprüft).
/// Wir setzen je Film auf null zurück: das ist beim ersten gespielten Film das
/// Verhalten des Originals und deckt sich mit ffmpeg. <b>Unsere Setzung</b>,
/// bewusst und hier benannt.</para>
///
/// <para><b>Abnahme (20.08.2026):</b> gegen <c>ffmpeg 8.1.1</c> (<c>-vn -f
/// s16le</c>) <b>byteweise identisch über alle 35 Filme in voller Länge</b> —
/// 34 dekodierte und <c>INTRO.RPL</c> durchgereicht, zusammen
/// <b>374.214.076 Byte = 93.553.519 Abtastungen je Kanal</b>, 0 daneben; davon
/// entfallen auf die 34 dekodierten Filme <b>346.020.944 Byte =
/// 86.505.236 Abtastungen je Kanal</b>. Der Prüfstand im Baum ist
/// <c>--selftest-rpl</c>.</para>
/// </summary>
public static class RplAudio
{
    /// <summary>Die 89 Schrittweiten aus <c>WINSTR.DLL</c>
    /// (VA 0x10010070, zweite Kopie 0x10010270 — Wort für Wort gleich).
    /// ⚠ Die Tafel ist die des üblichen IMA — die Rechnung darum herum ist es
    /// nicht, siehe Klassenkommentar.</summary>
    private static readonly int[] StepTable =
    {
        7, 8, 9, 10, 11, 12, 13, 14, 16, 17, 19, 21, 23, 25, 28, 31, 34, 37,
        41, 45, 50, 55, 60, 66, 73, 80, 88, 97, 107, 118, 130, 143, 157, 173,
        190, 209, 230, 253, 279, 307, 337, 371, 408, 449, 494, 544, 598, 658,
        724, 796, 876, 963, 1060, 1166, 1282, 1411, 1552, 1707, 1878, 2066,
        2272, 2499, 2749, 3024, 3327, 3660, 4026, 4428, 4871, 5358, 5894,
        6484, 7132, 7845, 8630, 9493, 10442, 11487, 12635, 13899, 15289,
        16818, 18500, 20350, 22385, 24623, 27086, 29794, 32767,
    };

    /// <summary>Die Indexschritte, <b>auf 16 Einträge verdoppelt</b>
    /// (VA 0x10010030 / 0x10010230). Die Verdopplung ist kein Zierrat: der
    /// Dekoder greift mit dem ganzen Nibbel zu und maskiert das Vorzeichenbit
    /// nicht aus.</summary>
    private static readonly int[] IndexTable =
    {
        -1, -1, -1, -1, 2, 4, 6, 8,
        -1, -1, -1, -1, 2, 4, 6, 8,
    };

    /// <summary>Der laufende Zustand EINES Kanals. Er überlebt die
    /// Stückgrenzen — siehe Klassenkommentar.</summary>
    private struct Channel
    {
        public int Predictor;
        public int Index;
    }

    /// <summary>
    /// Ein Nibbel — Befehl für Befehl @0x100011E5…0x10001287 in
    /// <c>WINSTR.DLL</c>.
    /// </summary>
    private static short Step(ref Channel c, int nibble)
    {
        // mov edx,[ecx*4+0x10010070] -- die Schrittweite VOR der Fortschreibung
        int step = StepTable[c.Index];

        // add ecx,[eax*4+0x10010030] / cmp ecx,-1 -> 0 / cmp ecx,0x59 -> 0x58
        int idx = c.Index + IndexTable[nibble & 15];
        if (idx < 0) idx = 0;
        else if (idx > 88) idx = 88;
        c.Index = idx;

        // ⚠ HIER liegt der Unterschied zum gewoehnlichen IMA: erst
        // multiplizieren (shl/add), dann EINMAL schieben -- und kein step>>3.
        int diff = 0;
        if ((nibble & 4) != 0) diff = step << 2;
        if ((nibble & 2) != 0) diff += step << 1;
        if ((nibble & 1) != 0) diff += step;
        diff >>= 2;

        // test eax,8 / sub bzw. add, dann klemmen und als word ablegen
        int p = (nibble & 8) != 0 ? c.Predictor - diff : c.Predictor + diff;
        if (p > 32767) p = 32767;
        else if (p < -32768) p = -32768;
        c.Predictor = p;
        return (short)p;
    }

    /// <summary>
    /// Der ganze Ton eines Films als 16-Bit-PCM, verschränkt (links, rechts).
    ///
    /// <para>⚠ Der ganze Film, nicht ein Stück — die Vorhersage läuft über die
    /// Grenzen weiter. <paramref name="bisBild"/> begrenzt nur, wie weit
    /// gelesen wird; angefangen wird immer bei Bild 0.</para>
    /// </summary>
    public static short[] DecodeAll(RplFile f, int bisBild = int.MaxValue)
    {
        int n = Math.Min(bisBild, f.Chunks.Count);
        bool roh = f.SoundFormat == 1 || f.SoundBits == 16;
        bool stereo = f.SoundChannels >= 2;

        long rohBytes = 0;
        for (int i = 0; i < n; i++) rohBytes += f.Chunks[i].AudioSize;
        if (rohBytes == 0) return Array.Empty<short>();

        // roh: 2 Byte = 1 Abtastung. ADPCM: 1 Byte = 2 Nibbel = 2 Abtastungen.
        var outp = new short[roh ? rohBytes / 2 : rohBytes * 2];
        int w = 0;

        Channel a = default, b = default;
        // ⚠ EIN Griff an die Datei fuer den ganzen Film. RplFile.ReadAudio
        // oeffnet je Stueck neu; bei 5000 Stuecken ist das der ganze Lauf.
        using var s = File.OpenRead(f.Path);
        var buf = Array.Empty<byte>();
        for (int i = 0; i < n; i++)
        {
            var ch = f.Chunks[i];
            int len = ch.AudioSize;
            if (len == 0) continue;
            if (buf.Length < len) buf = new byte[Math.Max(len, 1 << 16)];
            s.Position = ch.AudioAt;
            s.ReadExactly(buf, 0, len);

            if (roh)
            {
                for (int k = 0; k + 1 < len; k += 2)
                    outp[w++] = BitConverter.ToInt16(buf, k);
                continue;
            }
            for (int k = 0; k < len; k++)
            {
                byte by = buf[k];
                // ⚠ oberes Nibbel ZUERST -- shr al,4 @0x100011E9
                outp[w++] = Step(ref a, by >> 4);
                if (stereo) outp[w++] = Step(ref b, by & 0x0F);
                else outp[w++] = Step(ref a, by & 0x0F);
            }
        }
        return w == outp.Length ? outp : outp[..w];
    }

    /// <summary>Wieviele Abtastungen je Kanal auf ein Bild kommen — für den
    /// Gleichlauf. ⚠ Nicht konstant: die Stücke sind verschieden lang.</summary>
    public static int SamplesOfFrame(RplFile f, int frame)
    {
        int bytes = f.Chunks[frame].AudioSize;
        if (bytes == 0) return 0;
        bool roh = f.SoundFormat == 1 || f.SoundBits == 16;
        int ch = Math.Max(1, f.SoundChannels);
        return roh ? bytes / (2 * ch) : bytes / ch;
    }
}
