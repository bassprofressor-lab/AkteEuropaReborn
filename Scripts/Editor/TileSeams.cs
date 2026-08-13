namespace AkteEuropaReborn.Editor;

using System;
using System.Collections.Generic;
using AkteEuropaReborn.Import;

/// <summary>
/// <b>Beruehren sich die Bildpunkte?</b> — der Farbabstand ueber die NAHT
/// zwischen zwei Nachbarkacheln.
///
/// <para><b>Der Anlass ist ein Bild und keine Zahl.</b> Am 13.08.2026 meldete
/// <see cref="MapCheck"/> auf der erzeugten <c>map_neu01</c> jeden Zaehler gruen:
/// Landzellen am Wasser mit Innenland-Code 0 von 295, Zellen mit hoeherem
/// Nachbarn ohne Hangbyte 0 von 2380, Hoehensprung ueber 1 null, Wasseranteil
/// 18,55 % — Median der 26 gelieferten Karten. Der Ausschnitt derselben Karte
/// neben dem einer gelieferten (<c>aekernel-tools/map_crop.py --paar</c>) sah
/// trotzdem gescheckt aus: Wiese, Geroell, Wiese, ein Mauerstueck, ein Stueck
/// Treppe, jedes in seinem 40x20-Rechteck, und die Kanten setzten sich im
/// Nachbarn nicht fort.</para>
///
/// <para><b>Warum kein Zaehler das sehen konnte.</b> Der Schluessel von
/// <see cref="TileModel"/> — Gelaendeklasse, Hangbyte, Wassermaske,
/// Wasserabstand — sagt, welche Codes das Original in dieser LAGE benutzt. Er
/// sagt nicht, welchen davon es neben welchen legt. Innerhalb eines Fachs wurde
/// gewuerfelt, und ein Wurf je Zelle kennt seinen Nachbarn nicht.</para>
///
/// <para><b>GEMESSEN, an den Pixeln, ueber alle 26 gelieferten Karten</b>
/// (<c>aekernel-tools/map_seams.py --alle</c>, Stichprobe 25.000 Naehte je
/// Karte): der Anteil HARTER Naehte — mittlerer Farbabstand
/// |dR|+|dG|+|dB| ueber die zwei Randpixelspalten groesser als
/// <see cref="Hard"/> — liegt bei <b>Median 0,58 %</b>, Spanne
/// <b>0,00 .. 3,23 %</b> (0,00 % bei map_14, map_15, NET01, NET04; 3,23 % bei
/// NET07). Die erzeugte Karte lag bei <b>8,65 %</b> — mehr als das Doppelte der
/// schlechtesten gelieferten und das Fuenfzehnfache des Medians.</para>
///
/// <para>⚠ Der mittlere Farbabstand SELBST taugt dagegen NICHT als Schranke: er
/// liegt in den gelieferten Karten zwischen 19,7 (NET08) und 85,0 (NET07), und
/// die erzeugte Karte traf mit 71,8 mitten hinein. Ein Zaehler, der auf jeder
/// Karte dasselbe sagt, prueft nichts — es traegt der ANTEIL der harten Naehte,
/// nicht der Mittelwert.</para>
///
/// <para>⚠ Gemessen wird nur zwischen zwei BODENKACHELN (Code ueber 7 und unter
/// <c>MapBaker.GroundMax</c>) und nur auf Land. Ein Baum ueber der Wiese hat
/// bauartbedingt eine harte Kante, und ein Ufer SOLL eine haben — beides waere
/// ein erfundener Befund.</para>
///
/// <para><b>UNSERE SETZUNG ist, was daraus folgt:</b> der Generator waehlt aus
/// seinem gemessenen Fach nicht mehr einen einzigen Wurf, sondern den besten aus
/// <see cref="TriesDefault"/> gewichteten Wuerfen — gemessen an der Naht zu den beiden
/// bereits gesetzten Nachbarn im Westen und Norden. Das Original hat keinen
/// Editor, aus dem eine Wahlregel abzulesen waere; gemessen ist nur die
/// Schranke, die dabei herauskommen muss.</para>
/// </summary>
public sealed class TileSeams
{
    /// <summary>Wie viele Pixelspalten (bzw. -zeilen) der Naht gemittelt werden.
    /// Dieselbe Zahl wie in <c>map_seams.py</c>, sonst sind die Zahlen nicht
    /// vergleichbar.</summary>
    public const int Strip = 2;

    /// <summary>Ab welchem mittleren Farbabstand eine Naht HART heisst. Dieselbe
    /// Zahl wie in <c>map_seams.py</c>.</summary>
    public const double Hard = 120.0;

    /// <summary>Wie viele gewichtete Wuerfe je Zelle verglichen werden. UNSERE
    /// Setzung. 1 ist das alte Verhalten; hoehere Werte kosten Rechenzeit und
    /// nehmen der gemessenen Haeufigkeitsverteilung Gewicht.
    ///
    /// <para><b>Der Vorgabewert ist GEMESSEN und nicht gewaehlt.</b> Die Reihe auf
    /// Kachelsatz 47, 160x120 Zellen — dem haertesten Fall, denn seine zwei
    /// gelieferten Karten sind selbst die kantigsten (NET02 1,75 %, NET07
    /// 3,23 %):</para>
    /// <code>
    ///   Wuerfe   harte Naehte   Farbabstand   verschiedene Bodencodes
    ///        1       43,05 %         114,9                       595
    ///        8        9,00 %          84,2                       598
    ///       24        4,23 %          75,3                       580
    ///       48        3,11 %          73,1                       588
    /// </code>
    /// <para>⚠ Der Handel, den man hier erwarten wuerde — Naht gegen Vielfalt —
    /// <b>tritt nicht ein</b>: die Zahl verschiedener Bodencodes bleibt zwischen
    /// 580 und 598 (26 Karten: Median 622, Spanne 183..1217), und der Anteil der
    /// Objektkacheln bleibt bei allen vier Werten auf 13,18 % in 410 Flecken.
    /// Das war zu pruefen, weil »bester aus n« die gemessenen Haeufigkeiten
    /// verschiebt; gemessen verschiebt es sie nicht sichtbar. 48 ist genommen,
    /// weil erst dort der haerteste Kachelsatz in die Spanne der gelieferten
    /// Karten kommt. Ueber <c>,naht=&lt;n&gt;</c> bleibt die Zahl einstellbar,
    /// damit die Reihe nachpruefbar ist.</para>
    /// </summary>
    public const int TriesDefault = 48;

    /// <summary>Die Zahl fuer DIESEN Lauf, siehe <see cref="TriesDefault"/>.</summary>
    public int Tries { get; set; } = TriesDefault;

    /// <summary>Codes 0..7 sind die Wasseranimation, ab
    /// <c>MapBaker.GroundMax</c> ist es ein Objekt — beides ist von der Messung
    /// ausgenommen.</summary>
    public const int WaterCodeMax = 7;

    private readonly CwpFile _cwp;
    private readonly PalFile _pal;

    /// <summary>Je Code die vier Randstreifen als RGB. <c>null</c> heisst: der
    /// Rahmen liess sich nicht dekodieren (dann wird die Naht uebergangen und
    /// nicht mit 0 bewertet — sonst waere ein kaputter Rahmen die beste Wahl).
    /// </summary>
    private readonly Dictionary<int, Edge?> _edge = new();
    private readonly Dictionary<long, double> _cost = new();

    public int Codes => _edge.Count;
    public int Undecodable { get; private set; }
    /// <summary>Wie oft <see cref="Cost"/> eine echte Zahl liefern konnte.</summary>
    public long Measured { get; private set; }
    public long Skipped { get; private set; }

    private sealed class Edge
    {
        public int W, H;
        /// <summary>RGB der Randstreifen, je <see cref="Strip"/> tief.
        /// <c>East[y*Strip+k]</c> ist die k-te Spalte von rechts.</summary>
        public int[] East = Array.Empty<int>(), West = Array.Empty<int>();
        public int[] South = Array.Empty<int>(), North = Array.Empty<int>();
        public bool[] EastOk = Array.Empty<bool>(), WestOk = Array.Empty<bool>();
        public bool[] SouthOk = Array.Empty<bool>(), NorthOk = Array.Empty<bool>();
    }

    public TileSeams(CwpFile cwp, PalFile pal) { _cwp = cwp; _pal = pal; }

    /// <summary>Gilt dieser Code fuer die Nahtmessung? Wasser und Objekte nicht —
    /// siehe der Klassenkommentar.</summary>
    public static bool IsGround(int code) => code > WaterCodeMax && code < MapBaker.GroundMax;

    private Edge? EdgeOf(int code)
    {
        if (_edge.TryGetValue(code, out var e)) return e;
        Edge? made = null;
        try
        {
            var f = _cwp.DecodeFrame(code);
            if (!f.IsEmpty)
            {
                made = new Edge { W = f.Width, H = f.Height };
                made.East = new int[f.Height * Strip]; made.EastOk = new bool[f.Height * Strip];
                made.West = new int[f.Height * Strip]; made.WestOk = new bool[f.Height * Strip];
                made.South = new int[f.Width * Strip]; made.SouthOk = new bool[f.Width * Strip];
                made.North = new int[f.Width * Strip]; made.NorthOk = new bool[f.Width * Strip];
                for (int y = 0; y < f.Height; y++)
                    for (int k = 0; k < Strip; k++)
                    {
                        Put(f, f.Width - 1 - k, y, made.East, made.EastOk, y * Strip + k);
                        Put(f, k, y, made.West, made.WestOk, y * Strip + k);
                    }
                for (int x = 0; x < f.Width; x++)
                    for (int k = 0; k < Strip; k++)
                    {
                        Put(f, x, f.Height - 1 - k, made.South, made.SouthOk, x * Strip + k);
                        Put(f, x, k, made.North, made.NorthOk, x * Strip + k);
                    }
            }
        }
        catch (Exception) { made = null; }
        if (made == null) Undecodable++;
        _edge[code] = made;
        return made;
    }

    private void Put(CwpFile.Frame f, int x, int y, int[] rgb, bool[] ok, int at)
    {
        if (x < 0 || x >= f.Width || y < 0 || y >= f.Height) return;
        int o = y * f.Width + x;
        if (!f.Opaque[o]) return;
        byte i = f.Pixels[o];
        rgb[at] = (_pal.R[i] << 16) | (_pal.G[i] << 8) | _pal.B[i];
        ok[at] = true;
    }

    /// <summary>
    /// Der mittlere Farbabstand ueber die Naht, oder <c>-1</c> wenn sie sich
    /// nicht messen laesst (Wasser, Objekt, unlesbarer Rahmen, kein
    /// gemeinsamer Streifen).
    /// </summary>
    /// <param name="horizontal">true: <paramref name="a"/> liegt WESTLICH von
    /// <paramref name="b"/>. false: <paramref name="a"/> liegt NOERDLICH.</param>
    public double Cost(int a, int b, bool horizontal)
    {
        if (!IsGround(a) || !IsGround(b)) { Skipped++; return -1; }
        long key = ((long)a << 21) | ((long)b << 1) | (horizontal ? 1L : 0L);
        if (_cost.TryGetValue(key, out double v)) { if (v >= 0) Measured++; else Skipped++; return v; }
        v = Compute(a, b, horizontal);
        _cost[key] = v;
        if (v >= 0) Measured++; else Skipped++;
        return v;
    }

    private double Compute(int a, int b, bool horizontal)
    {
        var ea = EdgeOf(a); var eb = EdgeOf(b);
        if (ea == null || eb == null) return -1;
        long sum = 0; int n = 0;
        if (horizontal)
        {
            int rows = Math.Min(ea.H, eb.H);
            for (int y = 0; y < rows; y++)
                for (int k = 0; k < Strip; k++)
                    Add(ea.East, ea.EastOk, y * Strip + k, eb.West, eb.WestOk, y * Strip + k);
        }
        else
        {
            int cols = Math.Min(ea.W, eb.W);
            for (int x = 0; x < cols; x++)
                for (int k = 0; k < Strip; k++)
                    Add(ea.South, ea.SouthOk, x * Strip + k, eb.North, eb.NorthOk, x * Strip + k);
        }
        return n == 0 ? -1 : (double)sum / n;

        void Add(int[] p, bool[] pok, int pi, int[] q, bool[] qok, int qi)
        {
            if (pi >= p.Length || qi >= q.Length || !pok[pi] || !qok[qi]) return;
            int x = p[pi], y = q[qi];
            sum += Math.Abs(((x >> 16) & 255) - ((y >> 16) & 255))
                 + Math.Abs(((x >> 8) & 255) - ((y >> 8) & 255))
                 + Math.Abs((x & 255) - (y & 255));
            n++;
        }
    }

    public string Describe()
        => $"Nahtmodell: {Codes} Kacheln dekodiert ({Undecodable} unlesbar), " +
           $"{_cost.Count} Nahtpaare gerechnet, {Measured} messbar, {Skipped} uebergangen " +
           $"(Wasser, Objekt oder unlesbar); harte Naht ab Farbabstand {Hard:0} " +
           "— gemessen an 26 Karten: Median 0,58 %, Spanne 0,00..3,23 %";
}
