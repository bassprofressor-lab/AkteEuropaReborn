namespace AkteEuropaReborn.Import;

using System;
using System.Collections.Generic;

/// <summary>
/// <b>DER TON DER FILME</b> — die zweite Hälfte von <see cref="RplFile"/>.
///
/// <para>Zwei Formen, und das Kopffeld <c>sound_format</c> sagt welche:</para>
/// <list type="bullet">
///   <item><b>34 Filme: 4-Bit-IMA-ADPCM</b>, 22050 Hz, stereo. Ein Nibbel ist
///   eine Abtastung, das Verhältnis roh zu PCM ist damit <b>genau 4,00</b>.</item>
///   <item><b><c>INTRO.RPL</c>: rohes PCM s16le.</b> Über 13.230.000 Byte
///   byteweise identisch mit ffmpegs Ausgabe — dort ist nichts zu dekodieren,
///   nur durchzureichen.</item>
/// </list>
///
/// <para>⚠⚠ <b>DIE TONSTÜCKE HABEN KEINEN EIGENEN KOPF.</b> Vorhersage und
/// Schrittweite laufen über die Stückgrenzen hinweg weiter — ein einzelnes
/// Stück ist deshalb <b>nicht</b> für sich dekodierbar. Wer bei Bild 500
/// einsteigt, muss den Ton von Bild 0 an mitrechnen. Genau darum nimmt
/// <see cref="DecodeAll"/> den ganzen Film und nicht ein Stück.</para>
///
/// <para><b>Die Tafeln stehen in <c>WINSTR.DLL</c></b> — nicht in EDEC oder
/// WINSDEC, die tragen keine. Gefunden wurden dort 89 Schrittweiten bis 32767
/// und eine Indextafel, die auf 16 Einträge verdoppelt ist. ⚠ <b>Dass die
/// Tafeln wie beim üblichen IMA aussehen, heisst nicht, dass die RECHNUNG
/// dieselbe ist</b> — siehe die Warnung unten.</para>
///
/// <para>⚠⚠ <b>DIESE FASSUNG IST NOCH NICHT RICHTIG — Stand 20.08.2026.</b>
/// Sie rechnet mit dem GEWÖHNLICHEN IMA-Satz, und das Original benutzt einen
/// eigenen: ffprobe nennt den Strom <c>adpcm_ima_escape</c>, »ADPCM IMA Acorn
/// Escape«, Kennung <b>0x65 = 101</b> — und genau die 101 steht im Kopffeld
/// <c>sound_format</c>. Gemessen gegen ffmpeg: die Stille am Anfang stimmt,
/// ab Abtastung <b>734</b> läuft es auseinander (1.047.647 von 1.048.576
/// daneben). <b>Der Ton bleibt deshalb stumm, bis die Regel aus
/// <c>WINSTR.DLL</c> gelesen ist</b> — dort liegen die Tafeln, in EDEC und
/// WINSDEC nicht. Eine geratene Tonkurve wäre hörbarer Unsinn, genau wie ein
/// Klang an der falschen Stelle.</para>
///
/// <para>⚠ <b>Die Kanäle wechseln je Nibbel</b>: das untere Nibbel eines Bytes
/// gehört dem linken Kanal, das obere dem rechten, jeder mit eigener Vorhersage
/// und eigener Schrittweite. Das folgt zwingend aus dem gemessenen Verhältnis
/// 4,00 — ein Byte trägt zwei Abtastungen, und bei stereo sind das eine je
/// Kanal.</para>
/// </summary>
public static class RplAudio
{
    /// <summary>Die 89 Schrittweiten aus <c>WINSTR.DLL</c>
    /// (VA 0x10010070 / 0x10010270). ⚠ Die Tafel ist die des üblichen IMA —
    /// die Rechnung darum herum ist es nicht.</summary>
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

    /// <summary>Die Indexschritte. ⚠ In <c>WINSTR.DLL</c> steht sie auf
    /// <b>16 Einträge verdoppelt</b> (0x10010030 / 0x10010230) — die zweite
    /// Hälfte ist dieselbe wie die erste, damit der Dekoder das Vorzeichenbit
    /// nicht ausmaskieren muss.</summary>
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

    private static short Step(ref Channel c, int nibble)
    {
        int step = StepTable[c.Index];
        // die uebliche IMA-Rekonstruktion: step/8 + step/4·b2 + step/2·b1 + step·b0
        int diff = step >> 3;
        if ((nibble & 1) != 0) diff += step >> 2;
        if ((nibble & 2) != 0) diff += step >> 1;
        if ((nibble & 4) != 0) diff += step;
        if ((nibble & 8) != 0) c.Predictor -= diff; else c.Predictor += diff;
        if (c.Predictor > 32767) c.Predictor = 32767;
        else if (c.Predictor < -32768) c.Predictor = -32768;
        c.Index += IndexTable[nibble & 15];
        if (c.Index < 0) c.Index = 0;
        else if (c.Index > 88) c.Index = 88;
        return (short)c.Predictor;
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
        var outp = new List<short>(1 << 20);
        bool roh = f.SoundFormat == 1 || f.SoundBits == 16;   // INTRO.RPL
        Channel l = default, r = default;
        for (int i = 0; i < n; i++)
        {
            byte[] a = f.ReadAudio(i);
            if (a.Length == 0) continue;
            if (roh)
            {
                for (int k = 0; k + 1 < a.Length; k += 2)
                    outp.Add(BitConverter.ToInt16(a, k));
                continue;
            }
            if (f.SoundChannels >= 2)
            {
                foreach (byte b in a)
                {
                    outp.Add(Step(ref l, b & 0x0F));
                    outp.Add(Step(ref r, b >> 4));
                }
            }
            else
            {
                foreach (byte b in a)
                {
                    outp.Add(Step(ref l, b & 0x0F));
                    outp.Add(Step(ref l, b >> 4));
                }
            }
        }
        return outp.ToArray();
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
