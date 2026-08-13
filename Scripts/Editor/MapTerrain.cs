namespace AkteEuropaReborn.Editor;

using System;
using System.Collections.Generic;
using System.Text;

/// <summary>
/// Das GELAENDE einer erzeugten Karte: Hoehen, Wasser, Gelaendeklassen und
/// Hangbytes. Kein Kachelcode — den holt <see cref="MapGenerator"/> aus
/// <see cref="TileModel"/>, weil er am Kachelsatz haengt und dieses Gelaende
/// nicht.
///
/// <para><b>Die Messlatte sind die 26 gelieferten Karten</b>
/// (<c>aekernel-tools/map_stats.py</c>, <c>map_flagrule.py</c>). Was hier steht,
/// ist entweder eine GEMESSENE Zahl aus diesen Karten oder ausdruecklich UNSERE
/// SETZUNG. Die gemessenen Schranken, jede mit ihrer Zahl:</para>
///
/// <list type="number">
/// <item><b>Hoehensprung zwischen Nachbarn: 0 oder 1.</b> Von 1.202.757
///   Nachbarpaaren sind 1.091.407 gleich hoch (90,74 %), 111.239 springen um 1
///   (9,25 %) und <b>111 um 2 oder mehr</b> — 0,0092 %. Ein Sprung ueber 1 ist
///   also praktisch verboten (<see cref="MaxStep"/>).</item>
/// <item><b>Keine Zelle hat drei hoehere Nachbarn.</b> Ueber alle 605.090
///   Zellen: 523.733 ohne hoeheren Nachbarn, 51.364 mit einem, 31.990 mit zwei
///   ANGRENZENDEN Richtungen, <b>3</b> mit zwei gegenueberliegenden und
///   <b>0</b> mit drei oder vier. Das ist keine Marotte, sondern die Grenze der
///   Darstellung: das Hangbyte kennt nur diese Faelle
///   (<see cref="HangByte"/>).</item>
/// <item><b>Das Hangbyte sagt, welche Nachbarn hoeher sind.</b> 1 Ost, 2 Sued,
///   3 West, 4 Nord, 5 Ost+Sued, 6 Sued+West, 7 West+Nord, 8 Ost+Nord —
///   Trefferquote 98,9..99,0 % bei den vier Einzelrichtungen. Umgekehrt: von
///   81.357 Zellen MIT hoeherem Nachbarn tragen nur 393 das Hangbyte 0
///   (0,48 %). Die Hangbytes 9..18 kommen fast nur auf Klasse »rau« vor und
///   lassen sich aus der Hoehe nicht ableiten — die setzt der Editor nicht.</item>
/// <item><b>Wasser liegt auf EINER Hoehe.</b> Auf 22 der 26 Karten traegt eine
///   einzige Hoehenstufe ueber 99,5 % aller Wasserzellen; der Spiegel liegt bei
///   15 Karten auf 0, bei 9 auf 1, bei DM_3 auf 4 und bei DM_4 auf 6. Und
///   118.948 von 118.999 Wasserzellen (99,96 %) haben KEINEN hoeheren
///   Nachbarn.</item>
/// <item><b>Der Strand liegt auf Wasserhoehe.</b> Von 36.288 Kantenpaaren
///   Wasser→Land haben 36.278 die Hoehendifferenz 0 (99,97 %), und die
///   Differenz +1 kommt <b>0-mal</b> vor. Ein Ufer, das eine Stufe ueber dem
///   Wasser liegt — was der Generator bis heute baute — gibt es im Original
///   nicht.</item>
/// <item><b>Gelaendeklassen, getrennt nach Hanglage.</b> Ebene Landzellen
///   (Hangbyte 0, 379.592 Stueck): frei 75,2 %, rau 11,0 %, gesperrt 13,8 %.
///   Hangzellen (106.499): frei 30,7 %, rau <b>52,5 %</b>, gesperrt 16,9 % —
///   und <b>0</b> davon Wasser. »Rau« ist also ueberwiegend das Hangmaterial.
///   Und »gesperrt« ist das Waldstueck: 67.642 von 70.433 gesperrten Zellen
///   tragen einen Objektcode (96,04 %), waehrend »rau« das in 413 von 97.724
///   Faellen tut (0,42 %).</item>
/// </list>
///
/// <para><b>UNSERE SETZUNG ist die ERZEUGUNGSART.</b> Das Original hat keinen
/// Editor, den wir gelesen haetten; woher seine Karten kommen, sagen sie nicht.
/// Gewaehlt ist darum ein Rauschfeld (Wertrauschen, drei Oktaven), aus dem
/// Hoehen und Wasser durch <b>Quantilabbildung</b> auf die gemessene Verteilung
/// fallen. ⚠ Damit ist das Hoehenhistogramm KEIN Befund, sondern eine
/// Konstruktion — es trifft die Messlatte, weil es darauf abgebildet wurde. Was
/// den Prueflauf wert ist, sind die Zahlen, die NICHT eingestellt wurden:
/// Anteil ebener Flaechen, Anzahl und Groesse der Hangkanten, Anzahl und Groesse
/// der Wassergebiete, Kuestenlaenge, und vor allem die Zahl der Zellen, deren
/// Hangform sich nicht darstellen laesst.</para>
/// </summary>
public sealed class MapTerrain
{
    // ---- gemessene Schranken -------------------------------------------------

    /// <summary>Groesster Hoehensprung zwischen zwei Nachbarn. GEMESSEN: 111 von
    /// 1.202.757 Paaren gehen darueber (0,0092 %).</summary>
    public const int MaxStep = 1;

    /// <summary>Auf welcher Stufe der Wasserspiegel liegt. UNSERE Setzung — 0 ist
    /// der haeufigste Fall (15 der 26 Karten), aber keine Notwendigkeit.</summary>
    public const int WaterLevel = 0;

    /// <summary>Die gemessene Hoehenverteilung ueber alle 26 Karten (605.090
    /// Zellen), Stufe 0..7 in Prozent. Auf sie wird das Rauschfeld abgebildet.
    /// </summary>
    public static readonly double[] ElevShare =
        { 0.3583, 0.2974, 0.1054, 0.0722, 0.0833, 0.0332, 0.0424, 0.0079 };

    /// <summary>Wasseranteil der 26 Karten: Median 18,55 %, Viertelwerte 10,32 %
    /// und 25,94 %, Spanne 4,05..60,66 %. Der Median ist der Vorgabewert.</summary>
    public const double WaterShareDefault = 0.1855;

    /// <summary>
    /// Gelaendeklassen, aufgeschluesselt nach Hanglage UND Wasserabstand.
    ///
    /// <para>⚠ Am 13.08.2026 stand hier nur »eben: frei 75,2 %, rau 11,0 %,
    /// gesperrt 13,8 %«, und das war eine irregefuehrende Zusammenfassung: die
    /// 11,0 % »rau« bestehen fast ganz aus dem STRAND. Aufgeschluesselt sagen
    /// dieselben 26 Karten:</para>
    /// <code>
    ///   eben, Wasserabstand 1:  27.114 Zellen  frei  0,2 %  rau 97,2 %  gesperrt  2,6 %
    ///   eben, Wasserabstand 2:  25.148 Zellen  frei 48,9 %  rau 48,4 %  gesperrt  2,7 %
    ///   eben, Wasserabstand ≥3: 327.330 Zellen frei 83,4 %  rau  1,0 %  gesperrt 15,6 %
    ///   Hang, Wasserabstand 2:     525 Zellen  frei  0,2 %  rau 99,8 %  gesperrt  0,0 %
    ///   Hang, Wasserabstand ≥3: 105.974 Zellen frei 30,8 %  rau 52,2 %  gesperrt 16,9 %
    ///   Hang, Wasserabstand 1:       0 Zellen  — der Strand ist NIE ein Hang
    /// </code>
    /// <para>»Rau« im ebenen Binnenland ist also 1,0 % und nicht 11,0 %. Mit den
    /// 11,0 % bekam der Generator im Binnenland elfmal zu viele Geroellzellen,
    /// und weil das Fach (rau, eben, kein Wasser daneben, Abstand ≥3) in den
    /// Originalen zu 7,28 % Uferkacheln enthaelt (26 von 357 Beobachtungen),
    /// standen davon 122 blaue Kacheln im Binnenland — gegen 3 bzw. 7 in den
    /// beiden gelieferten Karten desselben Kachelsatzes.</para>
    /// </summary>
    public static readonly double[][] ClassShare =
    {
        //                    gesperrt, rau     (der Rest ist frei)
        /* eben, Abstand 1 */ new[] { 0.026, 0.972 },
        /* eben, Abstand 2 */ new[] { 0.027, 0.484 },
        /* eben, Abstand ≥3*/ new[] { 0.156, 0.010 },
        /* Hang, Abstand 1 */ new[] { 0.000, 1.000 },
        /* Hang, Abstand 2 */ new[] { 0.000, 0.998 },
        /* Hang, Abstand ≥3*/ new[] { 0.169, 0.522 },
    };

    /// <summary>Wie tief die flache Kuestenebene reicht. GEMESSEN: von den
    /// Landzellen mit Wasserabstand 1, 2 und 3 liegen 26.769 von 27.114
    /// (98,73 %), 25.188 von 25.673 (98,11 %) und 22.981 von 23.551 (97,58 %)
    /// GENAU auf dem Wasserspiegel.</summary>
    public const int CoastalPlain = 3;

    // ---- das Ergebnis --------------------------------------------------------

    public int W { get; }
    public int H { get; }
    public int[] Elev { get; }
    /// <summary>0 frei, 1 rau, 2 wasser, 3 gesperrt — die vier Klassen der imap.</summary>
    public byte[] Class { get; }
    public int[] Flag { get; }

    // ---- Zaehler fuer den Bericht -------------------------------------------

    public int BeachCells { get; private set; }
    public int StepLowered { get; private set; }
    public int NotchLowered { get; private set; }
    public int Rounds { get; private set; }
    /// <summary>Zellen, deren Hangform das Hangbyte nicht ausdruecken kann —
    /// drei hoehere Nachbarn oder zwei gegenueberliegende. Im Original 3 von
    /// 605.090; hier MUSS es 0 sein, sonst hat die Reparatur nicht gegriffen.</summary>
    public int Unrepresentable { get; private set; }
    /// <summary>Nachbarpaare mit einem Sprung ueber <see cref="MaxStep"/>. Im
    /// Original 111 von 1.202.757; hier muss es 0 sein.</summary>
    public int OverSteps { get; private set; }

    private MapTerrain(int w, int h)
    {
        W = w; H = h;
        Elev = new int[w * h];
        Class = new byte[w * h];
        Flag = new int[w * h];
    }

    // ---- Bauen ---------------------------------------------------------------

    /// <summary>
    /// Ein Gelaende bauen. <paramref name="flat"/> ist die GEGENPROBE: alles auf
    /// Hoehe 0, kein Wasser, alles frei.
    ///
    /// <para>⚠ <b>BERICHTIGT 13.08.2026.</b> Hier stand, auf so einer Karte muesse
    /// der Pruefstand 0 Bauplaetze melden, weil <c>can_build_here</c> vier
    /// Eckhoehen ≥ 2 verlange. Die Lesung ist widerlegt (siehe
    /// <see cref="MapCheck.MinCornerClass"/>): geprueft wird die sec2-KLASSE, und
    /// auf einer flachen, wasserfreien Karte ist jede Zelle »offenes Land«. Die
    /// flache Karte hat also viele Bauplaetze — gemessen 3843 von 4096 auf
    /// 64x64, und die fehlenden 253 sind der letzte Rand, dem der vierte Eckpunkt
    /// fehlt.</para>
    ///
    /// <para>Wozu <paramref name="flat"/> weiter taugt: als degenerierte Probe,
    /// bei der jeder Gelaendezaehler seinen Grenzwert melden MUSS — 0 Wasser,
    /// 0 Uferzellen, 0 Zellen mit hoeherem Nachbarn, 0 Hangkanten, 100 % ebene
    /// Zellen und 0,00 % harte Naehte. Ein Zaehler, der dort etwas anderes sagt,
    /// rechnet falsch.</para>
    /// </summary>
    public static MapTerrain Build(int w, int h, uint seed, double waterShare,
                                   bool flat, Action<string> say)
    {
        var t = new MapTerrain(w, h);
        int n = w * h;

        if (flat)
        {
            say("FLACH (Gegenprobe): alles auf Hoehe 0, kein Wasser, alles frei");
            return t;
        }

        // ---- 1. das Rauschfeld ------------------------------------------------
        // UNSERE Setzung. Die Wellenlaenge ist an der gemessenen Form der
        // Hangkanten gewaehlt: die 26 Karten haben je Karte 1..34 (Median 9)
        // zusammenhaengende Hangkantengebiete mit im Mittel 573 Zellen — also
        // WENIGE, GROSSE Terrassen und keine Streuzacken. Ein Feld mit
        // Grundwellenlaenge min(Breite,Hoehe)/3 und drei Oktaven trifft diese
        // Groessenordnung; die feinste Oktave hat dann noch min/12 Zellen.
        double baseWave = Math.Max(6.0, Math.Min(w, h) / 3.0);
        var f = new double[n];
        for (int r = 0; r < h; r++)
            for (int c = 0; c < w; c++)
                f[r * w + c] = Fbm(c / baseWave, r / baseWave, seed, 3);

        // ---- 2. Wasser und Hoehen durch Quantilabbildung ---------------------
        // ⚠ Das Histogramm ist damit KEIN Befund (siehe Klassenkommentar): es
        // trifft die gemessene Verteilung, weil es darauf abgebildet wird.
        var order = new int[n];
        for (int i = 0; i < n; i++) order[i] = i;
        Array.Sort(order, (a, b) => f[a].CompareTo(f[b]));

        int water = (int)Math.Round(Math.Clamp(waterShare, 0.0, 0.9) * n);
        // die Stufengrenzen aus der gemessenen Verteilung
        var bound = new int[8];
        double acc = 0;
        for (int k = 0; k < 8; k++) { acc += ElevShare[k]; bound[k] = (int)Math.Round(acc * n); }
        bound[7] = n;
        for (int rank = 0, k = 0; rank < n; rank++)
        {
            while (k < 7 && rank >= bound[k]) k++;
            t.Elev[order[rank]] = k;
        }
        for (int rank = 0; rank < water; rank++)
        {
            t.Class[order[rank]] = 2;
            t.Elev[order[rank]] = WaterLevel;
        }

        // ---- 3. die KUESTENEBENE liegt auf Wasserhoehe -----------------------
        // ⚠ Nicht nur der eine Uferring. GEMESSEN: von den Landzellen mit
        // Wasserabstand 1, 2 und 3 liegen 26.769 von 27.114 (98,73 %), 25.188
        // von 25.673 (98,11 %) und 22.981 von 23.551 (97,58 %) GENAU auf dem
        // Wasserspiegel. Das Original hat also eine drei Zellen tiefe flache
        // Kuestenebene, und nicht ein Ufer, hinter dem es gleich hochgeht.
        // Der Beleg dafuer, dass es kein Zufall ist: von 27.114 Uferzellen
        // traegt KEINE ein Hangbyte ungleich 0 — 0 Gegenbeispiele.
        {
            // ⚠ Deckel CoastalPlain+1, nicht der Vorgabewert: mit dem waere
            // »Abstand > 3« nie wahr und die GANZE Karte kaeme auf Hoehe 0.
            var d0 = TileModel.WaterDistance(w, h, t.Class, CoastalPlain + 1);
            for (int i = 0; i < n; i++)
            {
                if (t.Class[i] == 2 || d0[i] > CoastalPlain || t.Elev[i] == WaterLevel) continue;
                t.Elev[i] = WaterLevel;
                t.BeachCells++;
            }
        }

        // ---- 4. Reparieren, und zwar nur nach UNTEN -------------------------
        // Zwei gemessene Schranken sind einzuhalten: kein Sprung ueber 1, und
        // keine Zelle mit drei hoeheren oder zwei gegenueberliegenden hoeheren
        // Nachbarn. Beide werden durch ABSENKEN erfuellt und nie durch Anheben:
        // Anheben koennte den Strand ueber den Wasserspiegel schieben und die
        // Regel aus Punkt 3 wieder brechen, und es kann in eine Schaukel
        // laufen. Absenken ist monoton und endet darum sicher.
        t.Repair();

        // ---- 5. Hangbytes aus den Hoehen (die gemessene Regel) ---------------
        t.SetFlags();

        say($"Gelaende {w}x{h}: Rauschfeld Wellenlaenge {baseWave:0.0} Zellen, 3 Oktaven, " +
            $"Samen {seed}; Wasser {water} Zellen ({100.0 * water / n:0.0} %), " +
            $"Kuestenebene (Wasserabstand ≤ {CoastalPlain}) auf Wasserhoehe gezogen: " +
            $"{t.BeachCells} Zellen");
        say($"Reparatur in {t.Rounds} Runden: {t.StepLowered} Zellen wegen Sprung > {MaxStep} " +
            $"abgesenkt, {t.NotchLowered} wegen nicht darstellbarer Hangform; " +
            $"danach {t.OverSteps} Sprünge > {MaxStep} und {t.Unrepresentable} nicht " +
            "darstellbare Zellen (im Original 111 von 1.202.757 bzw. 3 von 605.090)");
        return t;
    }

    /// <summary>Die Klassen setzen. Getrennt von <see cref="Build"/>, weil sie
    /// die Hangbytes brauchen — und die Hangbytes die fertigen Hoehen.</summary>
    public void SetClasses(uint seed, Action<string> say)
    {
        // ⚠ Die Anteile werden ueber den RANG gesetzt und nicht ueber eine
        // Schwelle auf den Feldwert. Am 13.08.2026 stand hier »u < 0,138«, und
        // das ergab 16 gesperrte Zellen statt 1250: der Mittelwert zweier
        // Rauschoktaven liegt dicht um 0,5, ein Wert unter 0,138 kommt darin
        // fast nie vor. Der Rang trifft den Anteil unabhaengig von der Form der
        // Verteilung — und der Anteil ist damit KONSTRUIERT, nicht getroffen.
        // Was der Pruefstand wert ist, sind die Fleckenzahl und die
        // Fleckengroesse, und die stellt niemand ein.
        //
        // UNSERE Setzung ist das Rauschfeld; GEMESSEN sind die sechs Faecher in
        // ClassShare. Ein Rauschfeld statt eines Wurfs je Zelle, damit Wald und
        // Geroell FLECKEN bilden — die 26 Karten haben je Karte 27..1377
        // Objektflecken (Median 226) mit im Mittel 11,1 Zellen, nicht gestreute
        // Einzelzellen.
        double wave = 6.0;
        var dist = TileModel.WaterDistance(W, H, Class);
        var group = new List<int>[ClassShare.Length];
        for (int k = 0; k < group.Length; k++) group[k] = new List<int>();
        var u = new double[W * H];
        for (int r = 0; r < H; r++)
            for (int c = 0; c < W; c++)
            {
                int i = r * W + c;
                if (Class[i] == 2) continue;
                // grob + fein, damit die Flecken ausgefranst sind und einzelne
                // Zellen abfallen (im Original 2..908 Einzelzellen je Karte)
                u[i] = 0.72 * Fbm(c / wave, r / wave, seed ^ 0x51EDu, 2)
                     + 0.28 * Fbm(c / 2.0, r / 2.0, seed ^ 0xA37Bu, 1);
                group[Bucket(Flag[i] != 0, dist[i])].Add(i);
            }
        var hist = new int[4];
        foreach (byte g in Class) if (g == 2) hist[2]++;
        for (int k = 0; k < group.Length; k++)
            Assign(group[k], u, ClassShare[k][0], ClassShare[k][1], hist);
        int n = W * H;
        say($"Klassenfaecher (eben/Hang x Wasserabstand 1/2/≥3): " +
            string.Join(" ", Array.ConvertAll(group, g => g.Count.ToString())));
        say($"Klassen: frei {hist[0]} ({100.0 * hist[0] / n:0.0} %), rau {hist[1]} " +
            $"({100.0 * hist[1] / n:0.0} %), wasser {hist[2]} ({100.0 * hist[2] / n:0.0} %), " +
            $"gesperrt {hist[3]} ({100.0 * hist[3] / n:0.0} %) " +
            "— gemessen ueber 26 Karten: 52,5 / 16,2 / 19,7 / 11,6 %");
    }

    /// <summary>Welches der sechs gemessenen Faecher von <see cref="ClassShare"/>
    /// eine Zelle trifft.</summary>
    private static int Bucket(bool slope, int dist)
        => (slope ? 3 : 0) + (dist <= 1 ? 0 : dist == 2 ? 1 : 2);

    /// <summary>Die tiefsten <paramref name="pBlocked"/> Raenge werden Wald, die
    /// naechsten <paramref name="pRough"/> Geroell, der Rest freies Land.</summary>
    private void Assign(List<int> idx, double[] u, double pBlocked, double pRough, int[] hist)
    {
        idx.Sort((a, b) => u[a].CompareTo(u[b]));
        int nb = (int)Math.Round(pBlocked * idx.Count);
        int nr = (int)Math.Round(pRough * idx.Count);
        for (int k = 0; k < idx.Count; k++)
        {
            byte cl = k < nb ? (byte)3 : k < nb + nr ? (byte)1 : (byte)0;
            Class[idx[k]] = cl;
            hist[cl]++;
        }
    }

    // ---- die gemessene Hangbyte-Regel ---------------------------------------

    /// <summary>
    /// Das Hangbyte aus den vier Nachbarhoehen, nach der gemessenen Regel:
    /// 1 Ost, 2 Sued, 3 West, 4 Nord, 5 Ost+Sued, 6 Sued+West, 7 West+Nord,
    /// 8 Ost+Nord, sonst 0. Zwei gegenueberliegende oder drei hoehere Nachbarn
    /// haben KEIN Hangbyte — dafuer gibt <see cref="HangByte"/> −1 zurueck, und
    /// genau die Faelle raeumt <see cref="Repair"/> weg.
    /// </summary>
    public static int HangByte(bool east, bool south, bool west, bool north)
    {
        int n = (east ? 1 : 0) + (south ? 1 : 0) + (west ? 1 : 0) + (north ? 1 : 0);
        if (n == 0) return 0;
        if (n == 1) return east ? 1 : south ? 2 : west ? 3 : 4;
        if (n == 2)
        {
            if (east && south) return 5;
            if (south && west) return 6;
            if (west && north) return 7;
            if (east && north) return 8;
            return -1;                       // Ost+West oder Sued+Nord: 3 von 605.090
        }
        return -1;                           // drei oder vier: 0 von 605.090
    }

    private void SetFlags()
    {
        Unrepresentable = 0;
        for (int r = 0; r < H; r++)
            for (int c = 0; c < W; c++)
            {
                int i = r * W + c, e = Elev[i];
                int f = HangByte(Up(c + 1, r, e), Up(c, r + 1, e), Up(c - 1, r, e), Up(c, r - 1, e));
                if (f < 0) { Unrepresentable++; f = 0; }
                Flag[i] = f;
            }
    }

    private bool Up(int c, int r, int e)
        => c >= 0 && c < W && r >= 0 && r < H && Elev[r * W + c] > e;

    private bool TouchesWater(int c, int r)
    {
        int[] dc = { 1, -1, 0, 0 }, dr = { 0, 0, 1, -1 };
        for (int k = 0; k < 4; k++)
        {
            int nc = c + dc[k], nr = r + dr[k];
            if (nc >= 0 && nc < W && nr >= 0 && nr < H && Class[nr * W + nc] == 2) return true;
        }
        return false;
    }

    /// <summary>
    /// Absenken, bis beide gemessenen Schranken halten. Monoton — jede Runde
    /// senkt mindestens eine Zelle um mindestens 1, und unter 0 geht es nicht,
    /// also endet die Schleife. Die Rundenzahl steht im Bericht, damit ein
    /// Abbruch an der Obergrenze nicht als Erfolg durchgeht.
    /// </summary>
    private void Repair(bool[]? locked = null)
    {
        int limit = 8 * (W + H) + 64;
        bool Free(int i) => locked == null || !locked[i];
        for (Rounds = 0; Rounds < limit; Rounds++)
        {
            int changed = 0;

            // (a) kein Sprung ueber MaxStep — hohe Zelle auf Nachbar+MaxStep
            for (int r = 0; r < H; r++)
                for (int c = 0; c < W; c++)
                {
                    int i = r * W + c, lo = Elev[i];
                    if (!Free(i)) continue;
                    int[] dc = { 1, -1, 0, 0 }, dr = { 0, 0, 1, -1 };
                    for (int k = 0; k < 4; k++)
                    {
                        int nc = c + dc[k], nr = r + dr[k];
                        if (nc < 0 || nc >= W || nr < 0 || nr >= H) continue;
                        lo = Math.Min(lo, Elev[nr * W + nc]);
                    }
                    if (Elev[i] > lo + MaxStep) { Elev[i] = lo + MaxStep; StepLowered++; changed++; }
                }

            // (b) keine nicht darstellbare Hangform — die HOEHEREN Nachbarn auf
            //     die eigene Hoehe. Das fuellt die Kerbe von oben statt die
            //     Zelle von unten anzuheben und laesst den Strand in Ruhe.
            for (int r = 0; r < H; r++)
                for (int c = 0; c < W; c++)
                {
                    int i = r * W + c, e = Elev[i];
                    if (HangByte(Up(c + 1, r, e), Up(c, r + 1, e),
                                 Up(c - 1, r, e), Up(c, r - 1, e)) >= 0) continue;
                    int[] dc = { 1, -1, 0, 0 }, dr = { 0, 0, 1, -1 };
                    for (int k = 0; k < 4; k++)
                    {
                        int nc = c + dc[k], nr = r + dr[k];
                        if (nc < 0 || nc >= W || nr < 0 || nr >= H) continue;
                        int j = nr * W + nc;
                        if (Elev[j] > e && Free(j)) { Elev[j] = e; NotchLowered++; changed++; }
                    }
                }

            if (changed == 0) break;
        }

        // nachzaehlen, statt es zu behaupten
        OverSteps = 0;
        for (int r = 0; r < H; r++)
            for (int c = 0; c < W; c++)
            {
                int i = r * W + c;
                if (c + 1 < W && Math.Abs(Elev[i + 1] - Elev[i]) > MaxStep) OverSteps++;
                if (r + 1 < H && Math.Abs(Elev[i + W] - Elev[i]) > MaxStep) OverSteps++;
            }
        SetFlags();
    }

    // ---- Eingriffe von aussen (Startplaetze, spaeter der Pinsel) -------------

    /// <summary>
    /// Liegt in diesem Rechteck samt <paramref name="margin"/> Zellen Rand
    /// KEINE Wasserzelle? Das ist die Bedingung, unter der ein Startplatz
    /// planiert werden darf, ohne die gemessene Ufer-Regel zu brechen: Wasser
    /// haette am Ufer die Hoehendifferenz 0 zu fordern (36.278 von 36.288), ein
    /// planierter Bauplatz aber die Hoehe ≥ 2.
    /// </summary>
    public bool WaterFree(int col, int row, int w, int h, int margin)
    {
        for (int r = row - margin; r <= row + h - 1 + margin; r++)
            for (int c = col - margin; c <= col + w - 1 + margin; c++)
            {
                if (c < 0 || c >= W || r < 0 || r >= H) return false;   // Kartenrand zaehlt wie Wasser
                if (Class[r * W + c] == 2) return false;
            }
        return true;
    }

    /// <summary>
    /// Eine Hochebene fuer einen Bauplatz: das Rechteck auf
    /// <paramref name="level"/>, frei und ohne Hangbyte, dazu ein abgestufter
    /// Vorplatz, damit kein Sprung ueber <see cref="MaxStep"/> entsteht — Ring
    /// <c>k</c> kommt auf mindestens <c>level − k</c>.
    ///
    /// <para>⚠ UNSERE Setzung, und keine kleine: die gelieferten Karten sind von
    /// Hand gebaut, ihre Basen stehen dort, wo das Gelaende schon passte. Hier
    /// wird das Gelaende passend GEMACHT. Der Preis steht im Pruefstand: eine
    /// planierte Flaeche hebt den Anteil ebener Zellen.</para>
    /// </summary>
    public void Plateau(int col, int row, int w, int h, int level, bool[] locked)
    {
        for (int r = row; r < row + h; r++)
            for (int c = col; c < col + w; c++)
            {
                if (c < 0 || c >= W || r < 0 || r >= H) continue;
                int i = r * W + c;
                Elev[i] = level;
                Class[i] = 0;
                Flag[i] = 0;
                locked[i] = true;
            }
        // Der Vorplatz muss von BEIDEN Seiten greifen. ⚠ Am 13.08.2026 hob er nur
        // an (»mindestens level − k«) und liess Zellen ueber dem Plateau stehen:
        // eine Zelle auf Hoehe 5 direkt neben dem planierten 2 gab einen Sprung
        // von 3 — der Pruefstand meldete danach 10 Sprünge > 1. Ring k wird
        // darum auf [level − k, level + k] geklemmt, und das reicht bis zur
        // hoechsten Stufe (7), also bis k = 6.
        for (int k = 1; k <= 6; k++)
            for (int r = row - k; r < row + h + k; r++)
                for (int c = col - k; c < col + w + k; c++)
                {
                    if (c < 0 || c >= W || r < 0 || r >= H) continue;
                    int i = r * W + c;
                    if (Class[i] == 2 || locked[i]) continue;
                    Elev[i] = Math.Clamp(Elev[i], Math.Max(0, level - k), level + k);
                }
    }

    /// <summary>Nach fremden Eingriffen: noch einmal reparieren (die planierten
    /// Zellen bleiben unangetastet), Hangbytes neu setzen und die drei gemessenen
    /// Schranken NACHZAEHLEN statt sie zu behaupten.</summary>
    public void Refresh(bool[]? locked, Action<string> say)
    {
        Repair(locked);
        SetFlags();
        OverSteps = 0;
        for (int r = 0; r < H; r++)
            for (int c = 0; c < W; c++)
            {
                int i = r * W + c;
                if (c + 1 < W && Math.Abs(Elev[i + 1] - Elev[i]) > MaxStep) OverSteps++;
                if (r + 1 < H && Math.Abs(Elev[i + W] - Elev[i]) > MaxStep) OverSteps++;
            }
        int beachBad = 0;
        for (int r = 0; r < H; r++)
            for (int c = 0; c < W; c++)
            {
                int i = r * W + c;
                if (Class[i] != 2) continue;
                int[] dc = { 1, -1, 0, 0 }, dr = { 0, 0, 1, -1 };
                for (int k = 0; k < 4; k++)
                {
                    int nc = c + dc[k], nr = r + dr[k];
                    if (nc < 0 || nc >= W || nr < 0 || nr >= H) continue;
                    int j = nr * W + nc;
                    if (Class[j] != 2 && Elev[j] != Elev[i]) beachBad++;
                }
            }
        BeachBad = beachBad;
        say($"nach den Eingriffen: {OverSteps} Sprünge > {MaxStep} (Original 111 von 1.202.757), " +
            $"{Unrepresentable} nicht darstellbare Hangformen (Original 3 von 605.090), " +
            $"{beachBad} Ufer nicht auf Wasserhoehe (Original 10 von 36.288)");
    }

    /// <summary>Kantenpaare Wasser→Land mit Hoehendifferenz ungleich 0. Im
    /// Original 10 von 36.288 (0,03 %).</summary>
    public int BeachBad { get; private set; }

    // ---- das Rauschfeld: UNSERE Setzung, aber fest verdrahtet ----------------

    /// <summary>Wertrauschen mit glatter Ueberblendung. Kein <c>Random</c>: zwei
    /// Laeufe mit demselben Samen ergeben dieselbe Karte, sonst kann kein
    /// Pruefstand eine Aenderung von einem Wurf unterscheiden.</summary>
    public static double Fbm(double x, double y, uint seed, int octaves)
    {
        double sum = 0, amp = 1, total = 0, freq = 1;
        for (int o = 0; o < octaves; o++)
        {
            sum += amp * Value(x * freq, y * freq, seed + (uint)o * 7919u);
            total += amp;
            amp *= 0.5;
            freq *= 2;
        }
        return sum / total;
    }

    private static double Value(double x, double y, uint seed)
    {
        int x0 = (int)Math.Floor(x), y0 = (int)Math.Floor(y);
        double fx = x - x0, fy = y - y0;
        double sx = fx * fx * (3 - 2 * fx), sy = fy * fy * (3 - 2 * fy);
        double a = Hash01(x0, y0, seed), b = Hash01(x0 + 1, y0, seed);
        double c = Hash01(x0, y0 + 1, seed), d = Hash01(x0 + 1, y0 + 1, seed);
        return (a + (b - a) * sx) * (1 - sy) + (c + (d - c) * sx) * sy;
    }

    public static uint Hash(int x, int y, uint seed)
    {
        unchecked
        {
            uint n = (uint)(x * 374761393) + (uint)(y * 668265263) + seed * 362437u;
            n = (n ^ (n >> 13)) * 1274126177u;
            return n ^ (n >> 16);
        }
    }

    private static double Hash01(int x, int y, uint seed) => Hash(x, y, seed) / 4294967296.0;

    // ---- Auskunft ------------------------------------------------------------

    /// <summary>Die Zahlen, die NICHT durch die Quantilabbildung eingestellt
    /// wurden — die einzigen, die als Befund taugen.</summary>
    public string Describe()
    {
        int n = W * H;
        var eh = new int[8];
        foreach (int e in Elev) if (e >= 0 && e < 8) eh[e]++;
        long pairs = 0, same = 0;
        int plateau = 0;
        for (int r = 0; r < H; r++)
            for (int c = 0; c < W; c++)
            {
                int i = r * W + c;
                if (c + 1 < W) { pairs++; if (Elev[i + 1] == Elev[i]) same++; }
                if (r + 1 < H) { pairs++; if (Elev[i + W] == Elev[i]) same++; }
                bool flat = true;
                int[] dc = { 1, -1, 0, 0 }, dr = { 0, 0, 1, -1 };
                for (int k = 0; k < 4 && flat; k++)
                {
                    int nc = c + dc[k], nr = r + dr[k];
                    if (nc >= 0 && nc < W && nr >= 0 && nr < H && Elev[nr * W + nc] != Elev[i]) flat = false;
                }
                if (flat) plateau++;
            }
        var sb = new StringBuilder();
        sb.Append("Hoehen ");
        for (int k = 0; k < 8; k++) sb.Append($"{k}:{100.0 * eh[k] / n:0.0}% ");
        sb.Append($"| gleich hohe Nachbarpaare {100.0 * same / pairs:0.0} % (gemessen 90,7 %), ");
        sb.Append($"ebene Zellen {100.0 * plateau / n:0.0} % (gemessen 77,4 %, Spanne 54,7..94,2)");
        return sb.ToString();
    }
}
