namespace AkteEuropaReborn.Editor;

using System;
using System.Collections.Generic;
using Godot;
using AkteEuropaReborn.Import;

/// <summary>
/// DIE NEUTRALEN GEBAEUDE einer erzeugten Karte — das, was sie von einer
/// Landschaft mit zwei Basen zu einer EROBERUNGSKARTE macht.
///
/// <para><b>Warum es das gibt.</b> Eine erzeugte Karte trug bis zum 14.08.2026
/// genau vier Gebaeude: je Spieler eine Basis und eine Fabrik. Die gelieferten
/// Gefechtskarten tragen ein Vielfaches davon, und zwar herrenlos: auf
/// <c>map_NET02</c> sind es <b>58</b> neutrale (Eigner 11) und 11 Kulissen,
/// auf <c>map_NET04</c> 68, auf <c>map_NET05</c> 92. Genau die sind der Grund,
/// warum <c>SkirmishAi.StartSkirmish</c> diese Karten »EROBERUNGSKARTE« nennt
/// und die Truppen der Karte stehen laesst: sie sind das Werkzeug, und die
/// neutralen Gebaeude sind der Preis. Ohne sie ist eine erzeugte Karte im
/// Gefecht leer.</para>
///
/// <para>⚠ Der Anlass war ein anderer Befund desselben Tages: der Generator
/// hatte EINZELNE Gebaeudekacheln als Bewuchs verstreut (siehe
/// <see cref="TileModel"/>), was wie zerstueckelte Gebaeude aussah. Nach der
/// Behebung war die Karte richtig — und leerer als vorher. Dies ist die
/// Gegenbewegung, und sie setzt GANZE Gebaeude statt Kachelstuecke.</para>
///
/// <para><b>Was hier gemessen ist</b> (alle Zahlen aus den sieben gelieferten
/// NET-Karten, gelesen aus der Fassung unter <c>user://data/Maps</c>, also der,
/// die die Engine laedt):</para>
/// <list type="bullet">
///   <item><b>Die Dichte.</b> Neutrale Gebaeude je 1000 begehbare Zellen:
///     NET01 1,20 · NET02 1,59 · NET03 2,07 · NET04 3,77 · NET05 1,92 ·
///     NET06 1,37 · NET08 1,75 — Median <b>1,75</b>. ⚠ <c>map_NET07</c> hat
///     <b>null</b> und faellt heraus; es ist keine Eroberungskarte (der
///     Gefechtsschirm sagt dort selbst »nichts Neutrales zu besetzen«). Die
///     DM-Karten liegen bei 0,15..0,41 und sind eine andere Familie.</item>
///   <item><b>Die Arten und ihre Haeufigkeit</b>, ueber 416 neutrale Gebaeude:
///     Typ 2 (72), 3 (66), 4 (51), 10 (42), 9 (39), 1 (38), 12 (36), 8 (33),
///     6 (12), 16 (9), 11 (9), 15 (8). ⚠ Auf <c>map_NET02</c> stehen von fast
///     jeder Art genau sieben, was nach einer Regel aussah — <b>zurueckgezogen</b>:
///     NET05 hat 15 und 17, NET06 hat 17. Es ist keine.</item>
///   <item><b>Tueren und Trefferpunkte sind je Art KONSTANT</b>, ohne
///     Gegenbeispiel: Typ 1 → eine Tuer (4,2), 1200 TP, 38 von 38 · Typ 3 →
///     (2,3)+(5,3), 1000, 66/66 · Typ 4 → dieselben Tueren, 800, 51/51 ·
///     Typ 8 → <b>keine</b> Tuer, 1000, 33/33 · Typ 9 → (5,4), 39/39 ·
///     Typ 12 → (1,3), 36/36. Sie sind darum abgeschrieben und nicht
///     gewuerfelt.</item>
///   <item><b>Die Lager sind leer:</b> 387 von 416 tragen (0,0,0).</item>
///   <item><b>Der naechste Nachbar:</b> min 1,0, 5-%-Quantil <b>7,3</b>,
///     Median 13,0 Zellen.</item>
/// </list>
///
/// <para><b>UNSERE Setzung</b> ist alles Uebrige und wird nicht anders
/// dargestellt: dass ueberhaupt gewuerfelt wird; welche Art auf welche Zelle
/// kommt; der Mindestabstand zum Kartenrand (gemessen ist dort min 0, Median
/// 31 — eine 0 wuerde ein Gebaeude an die Kante setzen, was auf einer
/// erzeugten Karte unschoen ist); und dass die Startplaetze der Spieler
/// freigehalten werden.</para>
///
/// <para>Der Platz muss <c>can_build_here</c> @0x4203C0 auf der ganzen
/// Grundflaeche erfuellen — dieselben zwei Fragen, die
/// <see cref="MapDeposits"/> stellt, und aus derselben Quelle: die imap frei,
/// das Hangbyte 0, alle vier Ecken sec2 ≥ 2. Wiederverwendet, nicht kopiert,
/// damit nicht zwei Fassungen derselben Pruefung auseinanderlaufen.</para>
/// </summary>
public static class MapNeutrals
{
    /// <summary>Der herrenlose Eigner. 416 von 416 neutralen Gebaeuden der
    /// NET-Karten tragen ihn, und <c>SkirmishAi.NeutralPrizes</c> zaehlt genau
    /// diesen.</summary>
    public const int NeutralOwner = 11;

    /// <summary>Neutrale Gebaeude je 1000 begehbare Zellen — der MEDIAN der
    /// sieben NET-Karten (Spanne 1,20..3,77).</summary>
    public const double PerThousandWalkable = 1.75;

    /// <summary>Mindestabstand zweier neutraler Gebaeude. Gemessen ist das
    /// 5-%-Quantil des naechsten Nachbarn 7,3 Zellen (Median 13,0); die 7 ist
    /// daraus abgerundet. Ein Median als Untergrenze wuerde die Haelfte der
    /// gemessenen Paare verbieten.</summary>
    public const int SpacingMin = 7;

    /// <summary>⚠ UNSERE Setzung: so weit bleibt ein neutrales Gebaeude vom
    /// Kartenrand weg. Gemessen ist dort <b>min 0</b> (Median 31) — das
    /// Original setzt also durchaus an die Kante. Auf einer erzeugten Karte
    /// sieht ein halb abgeschnittenes Gebaeude nach Fehler aus, deshalb 3.</summary>
    public const int EdgeMin = 3;

    /// <summary>Wie weit ein neutrales Gebaeude von einem Startplatz wegbleibt.
    /// ⚠ UNSERE Setzung, aus dem Wettkampf begruendet und nicht gemessen: ein
    /// Preis direkt vor der eigenen Tuer ist keiner.</summary>
    public const int StartGap = 14;

    /// <summary>Das Original faellt nach 255 Gebaeudeplaetzen aus — dieselbe
    /// Schranke, die <c>Resources.cs</c> schon nennt.</summary>
    public const int SlotsMax = 255;

    /// <summary>
    /// Eine Gebaeudeart, wie die gelieferten Karten sie fuehren.
    /// <paramref name="Weight"/> ist die gemessene Haeufigkeit ueber 416
    /// neutrale Gebaeude, <paramref name="Hp"/> und <paramref name="Doors"/>
    /// sind je Art konstant (Zahlen im Kopfkommentar).
    /// </summary>
    private readonly record struct Kind(int Typ, int Weight, int Hp, (int Col, int Row)[] Doors);

    private static readonly Kind[] Kinds =
    {
        new(1,  38, 1200, new[] { (4, 2) }),
        new(2,  72, 1000, new[] { (2, 3), (5, 3) }),
        new(3,  66, 1000, new[] { (2, 3), (5, 3) }),
        new(4,  51,  800, new[] { (2, 3), (5, 3) }),
        new(6,  12, 1000, new[] { (2, 3) }),
        new(8,  33, 1000, Array.Empty<(int, int)>()),
        new(9,  39, 1000, new[] { (5, 4) }),
        new(10, 42, 1000, new[] { (5, 3) }),
        new(11,  9, 1000, Array.Empty<(int, int)>()),
        new(12, 36, 1000, new[] { (1, 3) }),
        new(15,  8, 1000, new[] { (2, 4) }),
        new(16,  9, 1000, new[] { (2, 3) }),
    };

    /// <summary>
    /// Die Trefferpunkte einer Gebaeudeart, GEMESSEN — je Art konstant, ohne
    /// Gegenbeispiel (Zahlen im Kopfkommentar). Eine Art, die in den
    /// gelieferten Karten nie herrenlos vorkommt, bekommt 1000: das ist der
    /// Wert von zehn der zwoelf gemessenen Arten, und er ist damit die einzige
    /// Zahl, die hier als Rueckfall etwas belegt.
    ///
    /// <para>Sie steht hier und nicht im <see cref="MapEditOverlay"/>, damit es
    /// die Tafel nur EINMAL gibt. Zwei Fassungen derselben Messung laufen
    /// auseinander.</para>
    /// </summary>
    public static int HpOf(int typ)
    {
        foreach (var k in Kinds) if (k.Typ == typ) return k.Hp;
        return 1000;
    }

    /// <summary>Die Tuerzellen einer Gebaeudeart, GEMESSEN. Eine unbekannte Art
    /// bekommt KEINE Tuer — lieber eine ehrliche Luecke als eine erfundene Tuer
    /// an einer Wand, durch die dann niemand kommt.</summary>
    public static (int Col, int Row)[] DoorsOf(int typ)
    {
        foreach (var k in Kinds) if (k.Typ == typ) return k.Doors;
        return Array.Empty<(int, int)>();
    }

    /// <summary>
    /// Die neutralen Gebaeude legen. Gibt zurueck, wie viele es wurden.
    /// </summary>
    /// <param name="firstSlot">Der erste freie Gebaeudeplatz — die Startbasen
    /// haben die davor.</param>
    /// <param name="keepClear">Die Startplaetze, um die herum nichts gesetzt
    /// wird.</param>
    /// <param name="want">−1 = aus der gemessenen Dichte rechnen.</param>
    public static int Place(CwmFile m, uint seed, CwpFile? cwp, int firstSlot,
                            IReadOnlyList<(int Col, int Row)> keepClear,
                            Action<string> say, int want = -1)
    {
        var imap = m.Sec(6);
        var zone = m.Sec(2);
        if (imap == null || zone == null)
        { say("Neutrale Gebaeude: die Karte hat kein sec2/sec6 — nichts gesetzt"); return 0; }
        if (cwp == null || !cwp.HasBuildings)
        {
            say("Neutrale Gebaeude: der Kachelsatz traegt keinen Gebaeudeteil — " +
                "ohne Grundflaechen laesst sich keine setzen");
            return 0;
        }

        int w = m.Width, h = m.Height;

        // ⚠ Nur die Arten, die DIESER Kachelsatz auch hat. Die Haeufigkeiten
        // sind ueber alle NET-Karten gemessen, und die benutzen fuenf
        // verschiedene Kachelsaetze; Satz 44 und 46 kennen zum Beispiel nur
        // 1..17. Eine Art zu setzen, die der Satz nicht hat, gaebe ein Gebaeude
        // ohne ein einziges Bild.
        var kinds = new List<(Kind K, List<(int Dx, int Dy)> Foot, int FootW, int FootH)>();
        foreach (var k in Kinds)
        {
            var bt = cwp.GetBuildingType(k.Typ);
            if (bt.IsEmpty) continue;
            var foot = new List<(int Dx, int Dy)>();
            int fw = 0, fh = 0;
            for (int dx = 0; dx < CwpFile.PatternWidth; dx++)
                for (int dy = 0; dy < CwpFile.PatternHeight; dy++)
                    if (cwp.PatternTile(bt.FirstPattern, dx, dy) != 0)
                    { foot.Add((dx, dy)); fw = Math.Max(fw, dx + 1); fh = Math.Max(fh, dy + 1); }
            if (foot.Count == 0) continue;
            kinds.Add((k, foot, fw, fh));
        }
        if (kinds.Count == 0)
        { say($"Neutrale Gebaeude: Kachelsatz {m.Tileset:00} hat keine der gemessenen Arten"); return 0; }

        // Die Grundmenge ist dieselbe wie bei den Vorkommen: »imap frei«, ohne
        // die Hangbyte-Frage. Nur so ist die gemessene Dichte vergleichbar.
        int walkable = 0;
        for (int c = 0; c < w; c++)
            for (int r = 0; r < h; r++)
                if (MapDeposits.ImapFree(m, imap, c, r)) walkable++;

        if (want < 0) want = (int)Math.Round(PerThousandWalkable * walkable / 1000.0);
        want = Math.Min(want, Math.Max(0, SlotsMax - firstSlot));
        if (want <= 0)
        { say($"Neutrale Gebaeude: 0 gewuenscht ({walkable} begehbare Zellen)"); return 0; }

        // Rand und Abstand schrumpfen mit der Karte mit — dieselbe Ueberlegung
        // wie bei den Vorkommen: auf einer 64x64-Karte waeren die gemessenen
        // Werte unerfuellbar, und gar kein Gebaeude ist schlimmer als ein
        // enger Abstand. Die Meldung nennt die BENUTZTEN Werte.
        int edge = Math.Min(EdgeMin, Math.Max(0, (Math.Min(w, h) - 4) / 4));
        int spacing = Math.Min(SpacingMin, Math.Max(2, Math.Min(w, h) / 6));
        int startGap = Math.Min(StartGap, Math.Max(spacing, Math.Min(w, h) / 4));

        // Die Bewerber, in einer aus dem Samen gewuerfelten Reihenfolge —
        // derselbe Weg wie bei MapDeposits, damit dieselbe Karte bei demselben
        // Samen dieselbe Karte bleibt.
        var cand = new List<(uint Key, int Col, int Row)>();
        for (int c = edge; c < w - edge; c++)
            for (int r = edge; r < h - edge; r++)
                cand.Add((MapTerrain.Hash(c, r, seed ^ 0x4E55Au), c, r));
        cand.Sort((x, y) => x.Key != y.Key ? x.Key.CompareTo(y.Key)
                          : x.Col != y.Col ? x.Col.CompareTo(y.Col)
                          : x.Row.CompareTo(y.Row));

        long sq = (long)spacing * spacing, sqStart = (long)startGap * startGap;
        var placed = new List<(int Col, int Row, int Typ)>();
        int slot = firstSlot, tried = 0, blockedSite = 0, blockedNear = 0;
        int totalWeight = 0;
        foreach (var (k, _, _, _) in kinds) totalWeight += k.Weight;

        // ⚠ DIE MISCHUNG WIRD VORHER AUFGETEILT, NICHT JE ZELLE GEWUERFELT —
        // und das ist eine Berichtigung vom selben Tag, mit Zahlen.
        //
        // Zuerst stand hier: je Bewerber EINE Art wuerfeln und den Platz
        // wegwerfen, wenn sie nicht passt. Das bevorzugt kleine Grundflaechen,
        // und zwar messbar. Auf einer 254x254-Karte mit 70 Gebaeuden kam heraus:
        // Typ 8 (3x4) zwoelfmal statt der erwarteten sechs, Typ 4 dreizehnmal
        // statt neun — waehrend Typ 9 (der Flughafen) und Typ 10 je dreimal
        // statt sieben kamen. Der Flughafen ist im Gefecht die Quelle der
        // Flugzeuge; ihn auf ein Drittel zu druecken ist keine Kleinigkeit.
        //
        // Jetzt wird die Zahl je Art VORAB nach den gemessenen Gewichten
        // aufgeteilt (groesster Rest), und an jeder Stelle werden die noch
        // offenen Arten in gewuerfelter Reihenfolge PROBIERT. Damit entscheidet
        // die Grundflaeche nur noch darueber, WO eine Art landet, nicht mehr
        // WIE OFT.
        var need = new int[kinds.Count];
        {
            var rest = new List<(double Frac, int Idx)>();
            int given = 0;
            for (int i = 0; i < kinds.Count; i++)
            {
                double exact = (double)want * kinds[i].K.Weight / totalWeight;
                need[i] = (int)Math.Floor(exact);
                given += need[i];
                rest.Add((exact - need[i], i));
            }
            rest.Sort((a, b) => b.Frac != a.Frac ? b.Frac.CompareTo(a.Frac)
                                                 : a.Idx.CompareTo(b.Idx));
            for (int i = 0; given < want && i < rest.Count; i++, given++) need[rest[i].Idx]++;
        }
        var target = (int[])need.Clone();

        foreach (var cd in cand)
        {
            if (placed.Count >= want || slot >= SlotsMax) break;
            tried++;

            bool tooClose = false;
            foreach (var (pc, pr, _) in placed)
            {
                long dx = cd.Col - pc, dy = cd.Row - pr;
                if (dx * dx + dy * dy < sq) { tooClose = true; break; }
            }
            if (!tooClose)
                foreach (var (sc, sr) in keepClear)
                {
                    long dx = cd.Col - sc, dy = cd.Row - sr;
                    if (dx * dx + dy * dy < sqStart) { tooClose = true; break; }
                }
            if (tooClose) { blockedNear++; continue; }

            // Die noch offenen Arten, in einer aus der Zelle gewuerfelten
            // Reihenfolge — probiert wird, bis eine passt. ⚠ UNSERE Setzung
            // bleibt, WELCHE Art wohin kommt; die Daten sagen nur, wie viele
            // von jeder.
            var order = new List<int>();
            for (int i = 0; i < kinds.Count; i++) if (need[i] > 0) order.Add(i);
            if (order.Count == 0) break;
            order.Sort((a, b) =>
            {
                uint ha = MapTerrain.Hash(cd.Col * 61 + a, cd.Row, seed ^ 0xB17DAu);
                uint hb = MapTerrain.Hash(cd.Col * 61 + b, cd.Row, seed ^ 0xB17DAu);
                return ha != hb ? ha.CompareTo(hb) : a.CompareTo(b);
            });

            int chosen = -1;
            foreach (int i in order)
            {
                var e = kinds[i];
                // Jede Zelle der Grundflaeche muss can_build_here erfuellen —
                // die Tuerzellen ausdruecklich mit, denn sie bleiben begehbar
                // und muessen erreichbar sein.
                bool ok = cd.Col + e.FootW <= w - edge && cd.Row + e.FootH <= h - edge;
                if (ok)
                    foreach (var (fx, fy) in e.Foot)
                    {
                        int c = cd.Col + fx, r = cd.Row + fy;
                        if (!MapDeposits.Free(m, imap, c, r) ||
                            !MapDeposits.Corners(zone, w, h, c, r)) { ok = false; break; }
                    }
                if (ok) { chosen = i; break; }
            }
            if (chosen < 0) { blockedSite++; continue; }

            var pick = kinds[chosen];
            if (!MapFactory.PutBuilding(m, slot, pick.K.Typ, NeutralOwner, 0,
                                        cd.Col, cd.Row, pick.FootW, pick.FootH,
                                        pick.K.Hp, "", pick.K.Doors))
                continue;
            placed.Add((cd.Col, cd.Row, pick.K.Typ));
            need[chosen]--;
            slot++;
        }

        // Soll GEGEN Ist je Art. Nur »Ist« zu drucken hiesse, einen Ausfall
        // genau der Art, die das Gelaende nicht hergibt, unsichtbar zu machen —
        // und der Flughafen ist die Art, bei der das weh taete.
        var hist = new SortedDictionary<int, (int Soll, int Ist)>();
        for (int i = 0; i < kinds.Count; i++) hist[kinds[i].K.Typ] = (target[i], target[i] - need[i]);
        double per = 1000.0 * placed.Count / Math.Max(1, walkable);
        say($"Neutrale Gebaeude (Eigner {NeutralOwner}): {placed.Count} von {want} gewuenscht, " +
            $"{walkable} begehbare Zellen ergeben {per:0.00} je 1000 " +
            $"(Messlatte Median 1,75; NET-Karten 1,20..3,77)");
        say($"   Arten: {string.Join(" ", Names(hist))}");
        say($"   {cand.Count} Bewerber geprueft {tried}, kein Bauplatz {blockedSite}, " +
            $"zu nah {blockedNear}; Rand >= {edge} (⚠ unsere Setzung, gemessen min 0), " +
            $"Abstand >= {spacing} (Messlatte 7,3 als 5-%-Quantil), " +
            $"Abstand zum Startplatz >= {startGap} (⚠ unsere Setzung)");
        if (placed.Count < want)
            say($"Neutrale Gebaeude: ⚠ {want - placed.Count} weniger als gewuenscht — das " +
                "Gelaende gibt keine weiteren Stellen her, die Rand UND Abstand UND Bauplatz erfuellen");
        return placed.Count;
    }

    private static IEnumerable<string> Names(SortedDictionary<int, (int Soll, int Ist)> hist)
    {
        foreach (var kv in hist)
            yield return kv.Value.Soll == kv.Value.Ist
                ? $"Typ{kv.Key}x{kv.Value.Ist}"
                : $"Typ{kv.Key}x{kv.Value.Ist}(von {kv.Value.Soll})";
    }
}
