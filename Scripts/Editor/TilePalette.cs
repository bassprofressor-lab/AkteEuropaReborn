namespace AkteEuropaReborn.Editor;

using System;
using System.Collections.Generic;
using System.Text;
using AkteEuropaReborn.Import;

/// <summary>
/// Welche Kachelcodes eines Kachelsatzes Wasser sind und welche ebener Boden —
/// die kleinste Auskunft, die ein Karteneditor braucht, um ueberhaupt etwas
/// malen zu koennen.
///
/// <para><b>Wasser: die Codes 0..7.</b> GEMESSEN, nicht geraten. Ueber alle 44
/// eingespielten Karten (1.282.512 Zellen) gegen den <c>terrain</c>-Block, den
/// <see cref="CwmData.Terrain"/> aus der imap ableitet:</para>
/// <code>
///   Code 0..7  ->  Wasser 274.617 | gesperrt 1.464 | frei 21
///   Code &gt; 7   ->  Wasser    130 | frei 653.314 | rau 206.523 | gesperrt 146.443
/// </code>
/// <para>Also: 99,95 % aller Wasserzellen tragen einen Code aus 0..7, und die
/// acht Codes sind fast gleich haeufig (34.022 bis 34.540 Vorkommen) — es ist
/// eine achtstellige Wasseranimation, die die Karten zufaellig streuen. Das
/// deckt sich mit dem, was im Handoff steht: <c>0xFFFC</c> sitzt immer auf
/// einer Kachel mit Code ≤ 7.</para>
///
/// <para><b>Boden: ein Block von acht aufeinanderfolgenden Codes</b>, und der
/// liegt NICHT bei jedem Kachelsatz an derselben Stelle. Aus derselben Zaehlung,
/// die haeufigsten freien Codes je Kachelsatz:</para>
/// <code>
///   90..97   Kachelsatz  1..4, 7, 9..11, 13, 20..25, 32, 33, 43, 47
///   66..73   Kachelsatz 14..19, 45, 46
///   145..152 Kachelsatz 26, 27, 44
///   63..70   Kachelsatz 28          73..80  Kachelsatz 5
/// </code>
/// <para>Darum wird der Block nicht tabelliert, sondern <b>im Kachelsatz
/// selbst gesucht</b>: <see cref="FindGround"/> nimmt den ersten Lauf von acht
/// aufeinanderfolgenden Codes ab 8, deren Bilder eine ganze Bodenzelle fuellen.
/// »Fuellt eine ganze Zelle« ist dabei genau der Test, den
/// <see cref="MapBaker"/> fuer seinen Untergrund anlegt (≥ 99 % deckende Pixel,
/// mindestens 40 breit, 18..22 hoch) — steht er hier anders, bekaeme die Karte
/// schwarze Loecher.</para>
///
/// <para>⚠ <b>UNSERE Setzung ist die AUSWAHL unter den Laeufen.</b> Kachelsatz
/// 01 hat fuenf davon — 8..15, 90..97, 160..167, 220..298, 838..845 — und die
/// Kachel sagt selbst nicht, welcher davon Wiese ist und welcher Geroell. Die
/// Zaehlung oben sagt nur, welche Codes die gelieferten Karten als frei
/// begehbar FUEHREN, und dort kommen bei Kachelsatz 1 sowohl 90..97 (die
/// haeufigsten) als auch 8 und 12 vor; in den Kachelsaetzen 6, 8, 12 und 16 ist
/// der Block 8..15 sogar der haeufigste. Beides ist also Boden.
///
/// <para>Genommen wird darum <b>der erste Lauf</b> — eine Regel ohne Geschmack,
/// die zweimal dasselbe liefert — und wer einen anderen will, nennt ihn:
/// <c>--map-new=…,boden=&lt;n&gt;</c>. <see cref="Describe"/> zaehlt alle
/// gefundenen auf, damit die Wahl sichtbar ist. Am Ende entscheidet das
/// BILD, nicht diese Klasse.</para>
/// </summary>
public sealed class TilePalette
{
    /// <summary>Die acht Wasserkacheln — bei jedem Kachelsatz dieselben.</summary>
    public static readonly int[] Water = { 0, 1, 2, 3, 4, 5, 6, 7 };

    /// <summary>Wie viele Codes ein Bodenblock hat. Acht, in jedem der 35
    /// gemessenen Kachelsaetze.</summary>
    public const int GroundBlock = 8;

    /// <summary>Der erste Code, der kein Wasser mehr sein kann.</summary>
    public const int FirstNonWater = 8;

    public int[] Ground { get; private set; } = Array.Empty<int>();
    public int Tileset { get; }

    /// <summary>Alle gefundenen Laeufe, fuer die Meldung — damit sichtbar ist,
    /// ob der genommene der einzige war.</summary>
    public readonly List<(int First, int Last)> Runs = new();

    /// <summary>Welcher der gefundenen Laeufe genommen wurde.</summary>
    public int Pick { get; private set; }

    public TilePalette(CwpFile cwp, int tileset, int pick = 0)
    {
        Tileset = tileset;
        Pick = pick;
        FindGround(cwp);
    }

    /// <summary>Fuellt das Bild eine ganze 40x20-Bodenzelle? Wortgleich mit
    /// <c>MapBaker.Make</c> — Deckung allein genuegt nicht, ein 40x5-Grat
    /// meldet volle Deckung SEINES Kastens und laesst die Zelle leer.</summary>
    private static bool FillsCell(CwpFile.Frame? f)
    {
        if (f == null || f.IsEmpty) return false;
        if (f.Width < MapBaker.TileW || f.Height < 18 || f.Height > 22) return false;
        int opaque = 0;
        for (int i = 0; i < f.Width * f.Height; i++) if (f.Opaque[i]) opaque++;
        return 100.0 * opaque / (f.Width * f.Height) >= 99.0;
    }

    private void FindGround(CwpFile cwp)
    {
        int limit = Math.Min(cwp.FrameCount, MapBaker.GroundMax);
        int runStart = -1;
        for (int i = FirstNonWater; i <= limit; i++)
        {
            bool full = i < limit && Full(cwp, i);
            if (full) { if (runStart < 0) runStart = i; continue; }
            if (runStart >= 0)
            {
                if (i - runStart >= GroundBlock) Runs.Add((runStart, i - 1));
                runStart = -1;
            }
        }
        if (Runs.Count > 0)
        {
            Pick = Math.Clamp(Pick, 0, Runs.Count - 1);
            var (first, _) = Runs[Pick];
            Ground = new int[GroundBlock];
            for (int k = 0; k < GroundBlock; k++) Ground[k] = first + k;
        }
    }

    private static bool Full(CwpFile cwp, int i)
    {
        try { return FillsCell(cwp.DecodeFrame(i)); }
        catch (Exception) { return false; }
    }

    public bool Ok => Ground.Length == GroundBlock;

    public string Describe()
    {
        var sb = new StringBuilder();
        sb.Append($"Kachelsatz {Tileset:00}: Wasser {Water[0]}..{Water[^1]}, ");
        sb.Append(Ok ? $"Boden {Ground[0]}..{Ground[^1]} (Lauf {Pick})" : "KEIN Bodenblock gefunden");
        sb.Append("  (volle Bodenlaeufe ab 8: ");
        for (int i = 0; i < Runs.Count && i < 6; i++)
            sb.Append(i > 0 ? ", " : "").Append($"{Runs[i].First}..{Runs[i].Last}");
        if (Runs.Count > 6) sb.Append($", … {Runs.Count} insgesamt");
        sb.Append(')');
        return sb.ToString();
    }
}
