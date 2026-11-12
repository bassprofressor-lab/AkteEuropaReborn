namespace AkteEuropaReborn.Editor;

using System;
using System.Collections.Generic;

/// <summary>
/// ⭐ 11.11.2026 — bug-768/769/770: DER ECK-GENERATOR. Baut Kachel, Hoehe,
/// Hangbyte und Gelaendeklasse einer erzeugten Karte aus einem Raster von
/// ECKEN, wie es die gelieferten Karten tragen (<see cref="TileCorners"/>).
///
/// <para><b>Warum.</b> Spielermeldung zu 0.7.0: »leuchtende dreiecke verteilt
/// auf der karte. das wasser ist teils auch noch wild in festlandschaft
/// verteilt. denk dran, eine map sollte auch multiplayer spielbar sein.« Im
/// Fensterbild der alten Karte (<c>--generator-alt</c>) zu sehen: Eckkacheln in
/// fremdem Gelaende, einzelne Wasserzellen im Land, rechteckige Hangbloecke um
/// die Startplaetze und ein schiefer Zweierstart ((12,12) gegen (75,50) auf
/// 96x96).</para>
///
/// <para><b>Der Weg</b> (jeder Schritt mit seiner Regel):</para>
/// <list type="number">
///   <item>Startplaetze ZUERST, auf einer Ellipse mit gleichem Winkelabstand —
///     UNSERE SETZUNG (bug-770). Ihre Umgebung ist Land, eben und waldfrei.</item>
///   <item>Wasser auf dem ECKraster (w+1)x(h+1): Rauschfeld, Rangschwelle auf
///     den Wasseranteil, dreimal Mehrheitsglaettung, Wasserflecken unter
///     <see cref="MinWaterCorners"/> und Inseln unter <see cref="MinIslandCorners"/>
///     weg, getrennte Landmassen mit Startplaetzen durch Landbruecken
///     verbunden — UNSERE SETZUNG; die Messlatte (isolierte Wasserzellen) steht
///     in <see cref="MapCheck"/>.</item>
///   <item>Die Stoffe als KONTUREN eines Feldes (Rauschen + Wasserabstand),
///     Rangschwellen auf die GEMESSENEN Stoffanteile, dann die Kettenregel:
///     zwei Nachbarecken unterscheiden sich um hoechstens eine Kettenstufe
///     (GEMESSEN: keine Kachel mischt Nicht-Nachbarn der Kette). Sattel und
///     Kombinationen ohne Kachel werden abgesenkt.</item>
///   <item>Hoehen nur im Binnenland, Quantile der gemessenen Hoehenverteilung
///     (gedeckelt bei <see cref="MaxLevel"/> — UNSERE SETZUNG), geglaettet, dann
///     die Reparatur: Sprung ≤ 1 zu allen ACHT Nachbarn, und jede Hangzelle
///     muss ein Hangbyte nach der Achter-Regel (bug-769) haben, fuer das die
///     Originale an DIESER Eckkombination eine Kachel tragen — sonst wird der
///     hoehere Nachbar abgesenkt. Absenken ist monoton, die Schleife endet.</item>
///   <item>Klassen: gelernt je Eckkombination (Ufer = rau, 100 % im Original),
///     Hang rau mit dem gemessenen Anteil, Wald mit dem gemessenen Anteil auf
///     einstoffigen ebenen Zellen.</item>
///   <item>Jeder Startplatz von jedem anderen fuer FAHRZEUGE (nur frei)
///     erreichbar — sonst wird ein Weg durch Rau/Wald freigeschlagen.</item>
/// </list>
/// </summary>
public sealed class MapLandscape
{
    /// <summary>Kleinster Wasserfleck in Ecken. UNSERE SETZUNG.</summary>
    public const int MinWaterCorners = 30;
    /// <summary>Kleinste Insel ohne Startplatz in Ecken. UNSERE SETZUNG.</summary>
    public const int MinIslandCorners = 60;
    /// <summary>Kleinster Stofffleck in Ecken. UNSERE SETZUNG.</summary>
    public const int MinPatchCorners = 8;
    /// <summary>Hoechste Hoehenstufe. UNSERE SETZUNG — die Originale gehen bis 7,
    /// aber jede Stufe mehr ist ein Hangring mehr um einen Huegel.</summary>
    public const int MaxLevel = 3;
    /// <summary>Kuestenebene: Zellen bis zu diesem Abstand vom Ufer bleiben auf
    /// Wasserhoehe — GEMESSEN (<see cref="MapTerrain.CoastalPlain"/>).</summary>
    public const int CoastalPlain = MapTerrain.CoastalPlain;

    /// <summary>bug-770: <c>--generator-korridor-aus</c> — keine wasserfreien,
    /// freigeschlagenen Linien zwischen benachbarten Startplaetzen.</summary>
    public static bool KorridorAus
        => Array.IndexOf(Core.CommandLine.Args, "--generator-korridor-aus") >= 0;

    public int W { get; }
    public int H { get; }
    public int[] Code { get; }
    public int[] Elev { get; }
    public int[] Flag { get; }
    public byte[] Class { get; }
    /// <summary>bug-772: die sec2-Klasse je Knoten [y*CornerW+x], (W+1)x(H+1) —
    /// das Eckraster, aus dem das Original selbst seine Kachel rechnet.</summary>
    public int[] CornerSec { get; private set; } = Array.Empty<int>();
    public int CornerW { get; private set; }

    /// <summary>Linke obere Ecke je Startplatz (Spieler = Index).</summary>
    public List<(int Player, int Col, int Row)> Plots { get; } = new();

    private MapLandscape(int w, int h)
    {
        W = w; H = h;
        Code = new int[w * h]; Elev = new int[w * h]; Flag = new int[w * h]; Class = new byte[w * h];
    }

    private static readonly int[] D8c = { 1, 1, 0, -1, -1, -1, 0, 1 };
    private static readonly int[] D8r = { 0, 1, 1, 1, 0, -1, -1, -1 };
    private static readonly int[] D4c = { 1, 0, -1, 0 };
    private static readonly int[] D4r = { 0, 1, 0, -1 };

    // ========================================================================

    public static MapLandscape Build(int w, int h, uint seed, double waterShare, int players,
                                     int plotW, int plotH, TileCorners tc, TileSeams? seams,
                                     Action<string> say, bool sym = false)
    {
        var L = new MapLandscape(w, h);
        int CW = w + 1, CH = h + 1, NV = CW * CH;
        // ⭐ 12.11.2026 — P6/D6 (Fable-Pruefung): PUNKTSYMMETRIE (180°). Jede Ecke v hat
        // ihr Gegenstueck MV(v), jede Zelle i ihr MC(i). Die Felder werden vom
        // »kanonischen« Teil (kleinerer Index) gespiegelt; Hangbytes rechnet FlagAt
        // aus den gespiegelten Hoehen neu (sie drehen sich mit). UNSERE SETZUNG.
        L._sym = sym;
        int MV(int v) => NV - 1 - v;
        void SymV<T>(T[] a) { if (sym) for (int v = 0; v < NV; v++) if (MV(v) < v) a[v] = a[MV(v)]; }

        // ---- 1. Startplaetze --------------------------------------------------
        var centers = L.PlaceStarts(players, plotW, plotH, seed, say);
        if (sym) centers = L.StartsSpiegeln(plotW, plotH, say);
        double zrx = plotW / 2.0 + 5, zry = plotH / 2.0 + 5;
        bool InZone(double x, double y, double scale)
        {
            foreach (var (cx, cy) in centers)
            {
                double dx = (x - cx) / (zrx * scale), dy = (y - cy) / (zry * scale);
                if (dx * dx + dy * dy <= 1.0) return true;
            }
            return false;
        }

        // bug-770: die VERBINDUNGSLINIEN benachbarter Startplaetze (Ring, bei
        // zweien die eine Strecke). Auf ihnen bleibt kein Wasser, und Rau/Wald
        // wird dort freigeschlagen — so ist der Fahrweg zum naechsten Gegner fuer
        // alle gleich lang (gemessen ohne: Fahrweg-Streuung 0,213 auf 09/4).
        // UNSERE SETZUNG; Gegenschalter --generator-korridor-aus.
        var seg = new List<(double X1, double Y1, double X2, double Y2)>();
        if (!KorridorAus && centers.Count >= 2)
        {
            int k = centers.Count == 2 ? 1 : centers.Count;
            for (int p = 0; p < k; p++)
            {
                var a0 = centers[p]; var b0 = centers[(p + 1) % centers.Count];
                seg.Add((a0.X, a0.Y, b0.X, b0.Y));
            }
        }
        bool NearSeg(double x, double y, double rad)
        {
            foreach (var (x1, y1, x2, y2) in seg)
            {
                double dx = x2 - x1, dy = y2 - y1, l2 = dx * dx + dy * dy;
                double t = l2 <= 0 ? 0 : Math.Clamp(((x - x1) * dx + (y - y1) * dy) / l2, 0, 1);
                double ex = x - (x1 + t * dx), ey = y - (y1 + t * dy);
                if (ex * ex + ey * ey <= rad * rad) return true;
            }
            return false;
        }

        // ---- 2. Wasser auf dem Eckraster -------------------------------------
        // Rauschkoordinaten pixeltreu: eine Zeile ist 20 px, eine Spalte 40 px —
        // sonst waeren alle Flecken im Bild doppelt so breit wie hoch.
        double waveL = Math.Max(12.0, Math.Min(2.0 * w, h) / 2.0);
        var water = new bool[NV];
        {
            var f = new double[NV];
            var order = new int[NV];
            for (int y = 0; y < CH; y++)
                for (int x = 0; x < CW; x++)
                {
                    int v = y * CW + x;
                    f[v] = Noise(2.0 * x / waveL, y / waveL, seed ^ 0x1A4Du, 4);
                    if (InZone(x, y, 1.7) || NearSeg(x, y, 3.5)) f[v] += 2.0;
                    order[v] = v;
                }
            SymV(f);                                                    // P6
            Array.Sort(order, (a, b) => f[a] != f[b] ? f[a].CompareTo(f[b]) : a.CompareTo(b));
            int nWater = (int)Math.Round(Math.Clamp(waterShare, 0.0, 0.8) * NV);
            for (int k = 0; k < nWater; k++) water[order[k]] = true;
        }
        for (int pass = 0; pass < 3; pass++) water = Majority(water, CW, CH);
        for (int y = 0; y < CH; y++)
            for (int x = 0; x < CW; x++)
                if (InZone(x, y, 1.7) || NearSeg(x, y, 3.5)) water[y * CW + x] = false;
        int removedWater = RemoveSmall(water, CW, CH, true, MinWaterCorners, null);
        var startCorner = new List<int>();
        foreach (var (cx, cy) in centers)
            startCorner.Add((int)Math.Round(cy) * CW + (int)Math.Round(cx));
        int removedIslands = RemoveSmall(water, CW, CH, false, MinIslandCorners, startCorner);
        int bridged = Bridge(water, CW, CH, startCorner, Math.Max(MinIslandCorners * 4, NV / 33));
        int removedWater2 = RemoveSmall(water, CW, CH, true, MinWaterCorners, null);
        int nW = 0; foreach (bool b in water) if (b) nW++;
        say($"Eckgelaende {w}x{h}: Wasser {nW} von {NV} Ecken ({100.0 * nW / NV:0.0} %, Soll " +
            $"{100 * waterShare:0.0} %), Wellenlaenge {waveL:0} Halbzellen; weg: {removedWater + removedWater2} " +
            $"Wasserflecken < {MinWaterCorners} Ecken, {removedIslands} Inseln < {MinIslandCorners} Ecken; " +
            $"{bridged} Landbruecken-Ecken (UNSERE SETZUNG)");

        // ---- 3. Stoffe als Konturen ------------------------------------------
        var depth = new int[NV];
        {
            var dist = ChebDist(water, CW, CH, 12);
            const double Dw = 6.0;
            double waveM = Math.Max(8.0, Math.Min(2.0 * w, h) / 5.0);
            var g = new double[NV];
            var land = new List<int>();
            for (int y = 0; y < CH; y++)
                for (int x = 0; x < CW; x++)
                {
                    int v = y * CW + x;
                    if (water[v]) continue;
                    g[v] = 0.6 * Noise(2.0 * x / waveM, y / waveM, seed ^ 0x5A7Du, 4)
                         + 0.4 * Math.Min(dist[v], Dw) / Dw;
                    land.Add(v);
                }
            SymV(g);                                                    // P6
            land.Sort((a, b) => g[a] != g[b] ? g[a].CompareTo(g[b]) : a.CompareTo(b));
            double landShare = 0;
            for (int d = 1; d <= tc.MaxDepth; d++) landShare += tc.LabelShare[tc.LabelOfDepth[d]];
            int at = 0;
            for (int d = 1; d <= tc.MaxDepth; d++)
            {
                int take = d == tc.MaxDepth ? land.Count - at
                    : (int)Math.Round(land.Count * tc.LabelShare[tc.LabelOfDepth[d]] / Math.Max(1e-9, landShare));
                for (int k = 0; k < take && at < land.Count; k++) depth[land[at++]] = d;
            }
        }
        int lowered = Repair(depth, CW, CH);
        int specks = 0;
        for (int round = 0; round < 3; round++)
        {
            int s = Despeckle(depth, CW, CH, MinPatchCorners);
            specks += s;
            lowered += Repair(depth, CW, CH);
            if (s == 0) break;
        }
        int saddle = 0, noTile = 0, left = 0;
        var locked = new bool[NV];
        void FixCells()
        {
            for (int round = 0; round < 30; round++)
            {
                int fixedNow = 0;
                for (int r = 0; r < h; r++)
                    for (int c = 0; c < w; c++)
                    {
                        int a = depth[r * CW + c], b = depth[r * CW + c + 1];
                        int d = depth[(r + 1) * CW + c], e = depth[(r + 1) * CW + c + 1];
                        bool sad = a == e && b == d && a != b;
                        int quad = QuadOf(tc, a, b, d, e);
                        bool miss = !tc.Has(quad, 0);
                        if (!sad && !miss) continue;
                        if (sad) saddle++; else noTile++;
                        int mx = Math.Max(Math.Max(a, b), Math.Max(d, e));
                        if (mx == 0) continue;
                        var four = new[] { r * CW + c, r * CW + c + 1, (r + 1) * CW + c, (r + 1) * CW + c + 1 };
                        bool any = false;
                        foreach (int v in four)
                            if (depth[v] == mx && !locked[v]) { depth[v] = mx - 1; any = true; }
                        // die hohen Ecken sind gesperrt (Startplatz): dann die
                        // niedrigen heben statt die hohen senken
                        if (!any)
                            foreach (int v in four)
                                if (depth[v] < mx && depth[v] > 0 && !locked[v]) depth[v] = mx;
                        fixedNow++;
                    }
                lowered += Repair(depth, CW, CH, locked);
                if (fixedNow == 0) { left = 0; break; }
                if (round == 29) left = fixedNow;
            }
        }
        FixCells();

        // bug-772: die Startplaetze auf »offenes Land« (sec2 >= 2), sonst
        // verweigert corners_carry (@0x4211A0) dort jeden Bau — die kleinste
        // Kettenstufe mit sec2 >= 2, in der Startzone gesperrt, nach aussen
        // stufenweise angeglichen (Wasser bleibt Wasser).
        int dStar = -1;
        for (int d = 0; d <= tc.MaxDepth && dStar < 0; d++)
            if (tc.SecOfLabel[tc.LabelOfDepth[d]] >= 2) dStar = d;
        int raised = 0;
        if (dStar > 0 && centers.Count > 0)
        {
            for (int y = 0; y < CH; y++)
                for (int x = 0; x < CW; x++)
                {
                    if (!InZone(x, y, 1.15)) continue;
                    int v = y * CW + x;
                    locked[v] = true;
                    if (depth[v] > 0 && depth[v] < dStar) { depth[v] = dStar; raised++; }
                }
            for (int guard = 0; guard < 16; guard++)
            {
                int ch2 = 0;
                for (int y = 0; y < CH; y++)
                    for (int x = 0; x < CW; x++)
                    {
                        int v = y * CW + x;
                        for (int k = 0; k < 8; k++)
                        {
                            int nx = x + D8c[k], ny = y + D8r[k];
                            if (nx < 0 || nx >= CW || ny < 0 || ny >= CH) continue;
                            int u = ny * CW + nx;
                            if (depth[u] > 0 && depth[u] < depth[v] - 1) { depth[u] = depth[v] - 1; ch2++; raised++; }
                        }
                    }
                if (ch2 == 0) break;
            }
            FixCells();
        }
        if (sym)
        {
            // P6: exakt spiegeln, dann die Kettenregel noch einmal (die Naht in der Mitte)
            for (int round = 0; round < 4; round++)
            {
                int diff = 0;
                for (int v = 0; v < NV; v++)
                    if (MV(v) < v && depth[v] != depth[MV(v)]) { depth[v] = Math.Min(depth[v], depth[MV(v)]); depth[MV(v)] = depth[v]; diff++; }
                if (diff == 0) break;
                Repair(depth, CW, CH, locked);
                FixCells();
            }
        }
        L.CornerSec = new int[NV];
        for (int v = 0; v < NV; v++) L.CornerSec[v] = tc.SecOfLabel[tc.LabelOfDepth[depth[v]]];
        L.CornerW = CW;

        // Wasser, wie es nach der Stoffwahl dasteht (ein Sattel kann eine
        // Strandecke zu Wasser gemacht haben)
        for (int v = 0; v < NV; v++) water[v] = depth[v] == 0;
        {
            var hist = new int[tc.MaxDepth + 1];
            foreach (int d in depth) hist[d]++;
            var parts = new List<string>();
            for (int d = 0; d <= tc.MaxDepth; d++)
                parts.Add($"{tc.Name(tc.LabelOfDepth[d])} {100.0 * hist[d] / NV:0.0}%");
            say($"Stoffe ({tc.ChainWhy}): {string.Join(", ", parts)}; Kettenregel {lowered} Ecken abgesenkt, " +
                $"{specks} Flecken < {MinPatchCorners} Ecken aufgeloest, {saddle} Sattel und {noTile} " +
                $"Kombinationen ohne Kachel bereinigt, {left} uebrig; Startzonen auf sec2 >= 2: {raised} Ecken gehoben");
        }

        // ---- 4. Zellen: Eckkombination, Ufer, Kuestenabstand -----------------
        int n = w * h;
        var quadOf = new int[n];
        var waterCell = new bool[n];
        var shore = new bool[n];          // mindestens eine Wasserecke
        var uniform = new int[n];         // Stoff, wenn alle vier gleich, sonst −1
        for (int r = 0; r < h; r++)
            for (int c = 0; c < w; c++)
            {
                int i = r * w + c;
                int a = depth[r * CW + c], b = depth[r * CW + c + 1];
                int d = depth[(r + 1) * CW + c], e = depth[(r + 1) * CW + c + 1];
                quadOf[i] = QuadOf(tc, a, b, d, e);
                waterCell[i] = a == 0 && b == 0 && d == 0 && e == 0;
                shore[i] = !waterCell[i] && (a == 0 || b == 0 || d == 0 || e == 0);
                uniform[i] = a == b && b == d && d == e ? tc.LabelOfDepth[a] : -1;
            }
        var coast = CellDist(shore, waterCell, w, h, CoastalPlain + 2);

        // ---- 5. Hoehen --------------------------------------------------------
        int slopeCells = L.Heights(seed, tc, quadOf, waterCell, coast,
                                   (c, r) => InZone(c + 0.5, r + 0.5, 1.3), say);

        // ---- 6. Klassen, Hangrau, Wald ---------------------------------------
        int landCells = 0, roughSlope = 0, forest = 0;
        var slopeIdx = new List<int>();
        for (int i = 0; i < n; i++)
        {
            if (waterCell[i]) { L.Class[i] = 2; continue; }
            landCells++;
            int cl = tc.ClassOf(quadOf[i]);
            L.Class[i] = (byte)(cl == 2 ? 1 : cl);
            if (shore[i]) L.Class[i] = 1;
            if (L.Flag[i] != 0 && L.Class[i] == 0) slopeIdx.Add(i);
        }
        {
            var key = new double[n];
            foreach (int i in slopeIdx)
                key[i] = Noise(2.0 * (i % w) / 6.0, (i / w) / 6.0, seed ^ 0x2A3Bu, 2);
            slopeIdx.Sort((a, b) => key[a] != key[b] ? key[b].CompareTo(key[a]) : a.CompareTo(b));
            int want = (int)Math.Round(tc.SlopeRoughShare * slopeIdx.Count);
            for (int k = 0; k < want; k++) { L.Class[slopeIdx[k]] = 1; roughSlope++; }
        }
        var forestCell = new bool[n];
        {
            var cand = new List<int>();
            var key = new double[n];
            for (int r = 0; r < h; r++)
                for (int c = 0; c < w; c++)
                {
                    int i = r * w + c;
                    if (L.Class[i] != 0 || L.Flag[i] != 0 || uniform[i] < 0) continue;
                    if (coast[i] <= 1) continue;                  // kein Wald direkt am Ufer
                    if (uniform[i] == tc.WaterLabel || !tc.HasObjects(uniform[i])) continue;
                    if (InZone(c + 0.5, r + 0.5, 1.4)) continue;
                    key[i] = 0.75 * Noise(2.0 * c / 10.0, r / 10.0, seed ^ 0xF0E5u, 2)
                           + 0.25 * Noise(2.0 * c / 3.0, r / 3.0, seed ^ 0x0F5Eu, 1);
                    cand.Add(i);
                }
            cand.Sort((a, b) => key[a] != key[b] ? key[b].CompareTo(key[a]) : a.CompareTo(b));
            int want = Math.Min((int)Math.Round(tc.ObjectShare * landCells), cand.Count * 6 / 10);
            for (int k = 0; k < want; k++) { forestCell[cand[k]] = true; forest++; }
        }

        // Korridore freischlagen (siehe oben): Rau (nicht Ufer) und Wald weg
        int corridor = 0;
        if (seg.Count > 0)
            for (int r = 0; r < h; r++)
                for (int c = 0; c < w; c++)
                {
                    int i = r * w + c;
                    if (!NearSeg(c + 0.5, r + 0.5, 1.6)) continue;
                    if (forestCell[i]) { forestCell[i] = false; forest--; corridor++; }
                    if (L.Class[i] == 1 && !shore[i]) { L.Class[i] = 0; corridor++; }
                }
        if (seg.Count > 0) say($"Korridore zwischen benachbarten Startplaetzen: {seg.Count} Strecken, {corridor} Zellen frei");

        if (sym)
            for (int i = 0; i < n; i++)
            {
                int j = n - 1 - i;                                      // P6: Zelle (w-1-c, h-1-r)
                if (j >= i) continue;
                L.Class[i] = L.Class[j];
                forestCell[i] = forestCell[j];
            }

        // ---- 7. Kacheln -------------------------------------------------------
        Func<int, int, bool, double>? seamCost = seams == null ? null : seams.Cost;
        int tries = seams == null ? 1 : Math.Min(seams.Tries, 12);
        int notfall = 0;
        for (int r = 0; r < h; r++)
            for (int c = 0; c < w; c++)
            {
                int i = r * w + c;
                uint roll = MapTerrain.Hash(c, r, seed ^ 0xC0DEu);
                if (forestCell[i])
                {
                    int obj = tc.PickObject(uniform[i], roll);
                    if (obj >= 0) { L.Code[i] = obj; L.Class[i] = 3; continue; }
                }
                int west = c > 0 && L.Code[i - 1] < TileCorners.GroundMax ? L.Code[i - 1] : -1;
                int north = r > 0 && L.Code[i - w] < TileCorners.GroundMax ? L.Code[i - w] : -1;
                int code = tc.Pick(quadOf[i], L.Flag[i], roll, seamCost, tries, west, north);
                if (code < 0)
                {
                    // darf nach der Reparatur nicht vorkommen — gezaehlt, nicht versteckt
                    notfall++;
                    code = tc.Pick(quadOf[i], 0, roll);
                    L.Flag[i] = 0;
                    if (code < 0) code = 0;
                }
                L.Code[i] = code;
            }
        say($"Klassen: {landCells} Landzellen, {slopeCells} Hangzellen, davon {roughSlope} rau " +
            $"(gemessen {100 * tc.SlopeRoughShare:0.0} %), Wald {forest} ({100.0 * forest / Math.Max(1, landCells):0.0} %, " +
            $"gemessen {100 * tc.ObjectShare:0.0} %); Kacheln ohne Fach {notfall} (muss 0 sein)");

        // ---- 8. Startplaetze verbinden ----------------------------------------
        int carved = L.Connect(tc, quadOf, shore, say);
        say($"Fahrzeugverbindung der Startplaetze: {carved} Zellen freigeschlagen (UNSERE SETZUNG)");
        return L;
    }

    /// <summary>
    /// Rauschen fuer den Eck-Generator. UNSERE SETZUNG wie das alte
    /// <see cref="MapTerrain.Fbm"/>, aber JE OKTAVE GEDREHT und mit einer
    /// Domaenenverzerrung: reines Wertrauschen auf einem achsenparallelen Gitter
    /// gab im Bild gerade, rechteckige Geroellkanten (11.11.2026, bug-768).
    /// Deterministisch aus dem Samen — kein <c>Random</c>.
    /// </summary>
    public static double Noise(double x, double y, uint seed, int octaves)
    {
        // Verzerrung: die Lage um bis zu ~0,7 Wellenlaengen verschieben
        double wx = x + 0.7 * (Raw(x * 0.9 + 5.2, y * 0.9 + 1.3, seed ^ 0x9E37u, 2) - 0.5);
        double wy = y + 0.7 * (Raw(x * 0.9 - 3.7, y * 0.9 + 8.1, seed ^ 0x7F4Au, 2) - 0.5);
        return Raw(wx, wy, seed, octaves);
    }

    private static double Raw(double x, double y, uint seed, int octaves)
    {
        double sum = 0, amp = 1, total = 0, freq = 1;
        for (int o = 0; o < octaves; o++)
        {
            double a = 0.5236 + 1.1 * o, ca = Math.Cos(a), sa = Math.Sin(a);
            double rx = (x * ca - y * sa) * freq + 13.7 * o, ry = (x * sa + y * ca) * freq - 7.3 * o;
            sum += amp * Value(rx, ry, seed + (uint)o * 7919u);
            total += amp; amp *= 0.5; freq *= 2;
        }
        return sum / total;
    }

    private static double Value(double x, double y, uint seed)
    {
        int x0 = (int)Math.Floor(x), y0 = (int)Math.Floor(y);
        double fx = x - x0, fy = y - y0;
        double sx = fx * fx * fx * (fx * (fx * 6 - 15) + 10), sy = fy * fy * fy * (fy * (fy * 6 - 15) + 10);
        double a = H01(x0, y0, seed), b = H01(x0 + 1, y0, seed);
        double c = H01(x0, y0 + 1, seed), d = H01(x0 + 1, y0 + 1, seed);
        return (a + (b - a) * sx) * (1 - sy) + (c + (d - c) * sx) * sy;
    }

    private static double H01(int x, int y, uint seed) => MapTerrain.Hash(x, y, seed) / 4294967296.0;

    private static int QuadOf(TileCorners tc, int a, int b, int d, int e)
        => TileCorners.Quad(tc.LabelOfDepth[a], tc.LabelOfDepth[b], tc.LabelOfDepth[d], tc.LabelOfDepth[e]);

    // ---- Symmetrie (P6, 12.11.2026) -------------------------------------------

    private bool _sym;

    private bool SymOk()
    {
        int n = W * H;
        for (int i = 0; i < n; i++) if (Elev[i] != Elev[n - 1 - i]) return false;
        return true;
    }

    /// <summary>P6: die zweite Haelfte der Platten als Spiegelbild der ersten (180° um die
    /// Kartenmitte). Bei ungerader Spielerzahl geht das nicht — dann bleibt es beim
    /// Ring und das wird gesagt.</summary>
    private List<(double X, double Y)> StartsSpiegeln(int plotW, int plotH, Action<string> say)
    {
        var o = new List<(double, double)>();
        int k = Plots.Count;
        if (k % 2 == 1)
            say($"Symmetrie: {k} Spieler ist ungerade — Startplaetze nur auf dem Ring, nicht gespiegelt");
        else
            for (int p = k / 2; p < k; p++)
            {
                var q = Plots[p - k / 2];
                Plots[p] = (Plots[p].Player, W - q.Col - plotW, H - q.Row - plotH);
            }
        foreach (var (_, c, r) in Plots) o.Add((c + plotW / 2.0, r + plotH / 2.0));
        if (k % 2 == 0) say($"Symmetrie: {k} Startplaetze paarweise um die Kartenmitte gespiegelt (180°)");
        return o;
    }

    // ---- Startplaetze --------------------------------------------------------

    /// <summary>Die Plaetze auf eine Ellipse mit gleichem Winkelabstand — UNSERE
    /// SETZUNG (bug-770). Zwei Spieler: gegenueberliegende Ecken; ab drei ein
    /// Kreis mit einem aus dem Samen gedrehten Anfangswinkel. Rueckgabe: die
    /// Plattenmitten. Passt ein Platz nicht (zu nah an einem anderen), wird die
    /// Spielerzahl verkleinert und das gemeldet.</summary>
    private List<(double X, double Y)> PlaceStarts(int players, int plotW, int plotH, uint seed,
                                                   Action<string> say)
    {
        var o = new List<(double, double)>();
        if (players <= 0) return o;
        double cx = W / 2.0, cy = H / 2.0;
        double mx = plotW / 2.0 + 4 + W / 14.0, my = plotH / 2.0 + 4 + H / 14.0;
        double a = cx - mx, b = cy - my;
        if (a < 2 || b < 2)
        {
            say($"zu klein fuer Startplaetze: {W}x{H}");
            return o;
        }
        for (int n = players; n >= 1; n--)
        {
            var pts = new List<(double X, double Y)>();
            if (n == 1) pts.Add((cx - 0.85 * a, cy - 0.85 * b));
            else if (n == 2)
            {
                bool flip = (MapTerrain.Hash(7, 11, seed ^ 0x57A7u) & 1) != 0;
                double sx = flip ? 1 : -1;
                pts.Add((cx + sx * 0.85 * a, cy - 0.85 * b));
                pts.Add((cx - sx * 0.85 * a, cy + 0.85 * b));
            }
            else
            {
                // nur SYMMETRISCHE Lagen: Spitze oben oder um eine halbe Teilung
                // gedreht (bei gerader Zahl beides achsensymmetrisch zum
                // Kartenrechteck) — ein frei gewuerfelter Winkel gab bei vier
                // Spielern Randabstaende 23/13/14/16 (Streuung 0,237)
                bool half = (MapTerrain.Hash(3, 5, seed ^ 0x57A7u) & 1) != 0;
                double t0 = -Math.PI / 2 + (half ? Math.PI / n : 0);
                for (int p = 0; p < n; p++)
                {
                    double t = t0 + 2 * Math.PI * p / n;
                    pts.Add((cx + a * Math.Cos(t), cy + b * Math.Sin(t)));
                }
            }
            // ganze Zellen, Platte im Rand
            var plots = new List<(int Col, int Row)>();
            foreach (var (x, y) in pts)
                plots.Add((Math.Clamp((int)Math.Round(x - plotW / 2.0), 3, W - plotW - 3),
                           Math.Clamp((int)Math.Round(y - plotH / 2.0), 3, H - plotH - 4)));
            double minD = double.MaxValue;
            for (int i = 0; i < plots.Count; i++)
                for (int j = i + 1; j < plots.Count; j++)
                {
                    double dx = plots[i].Col - plots[j].Col, dy = plots[i].Row - plots[j].Row;
                    // ⚠ Platten duerfen sich samt Rand nicht beruehren
                    if (Math.Abs(dx) < plotW + 6 && Math.Abs(dy) < plotH + 6) minD = 0;
                    minD = Math.Min(minD, Math.Sqrt(dx * dx + dy * dy));
                }
            if (minD < plotW + 4 && n > 1)
            {
                say($"Startplaetze: {n} Spieler passen auf {W}x{H} nicht (Abstand {minD:0.0} < {plotW + 4}) " +
                    "— es wird einer weniger versucht");
                continue;
            }
            for (int p = 0; p < plots.Count; p++)
            {
                Plots.Add((p, plots[p].Col, plots[p].Row));
                o.Add((plots[p].Col + plotW / 2.0, plots[p].Row + plotH / 2.0));
            }
            break;
        }
        return o;
    }

    // ---- Eckraster -----------------------------------------------------------

    private static bool[] Majority(bool[] m, int cw, int ch)
    {
        var o = new bool[m.Length];
        for (int y = 0; y < ch; y++)
            for (int x = 0; x < cw; x++)
            {
                int cnt = 0, v = y * cw + x;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = x + dx, ny = y + dy;
                        bool b = nx < 0 || nx >= cw || ny < 0 || ny >= ch ? m[v] : m[ny * cw + nx];
                        if (b) cnt++;
                    }
                o[v] = cnt >= 5;
            }
        return o;
    }

    /// <summary>Gebiete (Vierer) des Werts <paramref name="val"/> unter
    /// <paramref name="min"/> Ecken umkehren — ausser sie tragen einen Startplatz.
    /// Rueckgabe: umgekehrte Ecken.</summary>
    private static int RemoveSmall(bool[] m, int cw, int ch, bool val, int min, List<int>? keep)
    {
        int nv = m.Length, flipped = 0;
        var seen = new bool[nv];
        var q = new Queue<int>();
        var comp = new List<int>();
        for (int s = 0; s < nv; s++)
        {
            if (seen[s] || m[s] != val) continue;
            comp.Clear(); q.Clear();
            q.Enqueue(s); seen[s] = true;
            while (q.Count > 0)
            {
                int v = q.Dequeue(); comp.Add(v);
                int x = v % cw, y = v / cw;
                for (int k = 0; k < 4; k++)
                {
                    int nx = x + D4c[k], ny = y + D4r[k];
                    if (nx < 0 || nx >= cw || ny < 0 || ny >= ch) continue;
                    int u = ny * cw + nx;
                    if (seen[u] || m[u] != val) continue;
                    seen[u] = true; q.Enqueue(u);
                }
            }
            if (comp.Count >= min) continue;
            if (keep != null && comp.Exists(keep.Contains)) continue;
            foreach (int v in comp) m[v] = !val;
            flipped += comp.Count;
        }
        return flipped;
    }

    private static int[] LandComponents(bool[] water, int cw, int ch, out List<int> sizes)
    {
        var id = new int[water.Length];
        Array.Fill(id, -1);
        sizes = new List<int>();
        var q = new Queue<int>();
        for (int s = 0; s < water.Length; s++)
        {
            if (water[s] || id[s] >= 0) continue;
            int cid = sizes.Count, size = 0;
            q.Enqueue(s); id[s] = cid;
            while (q.Count > 0)
            {
                int v = q.Dequeue(); size++;
                int x = v % cw, y = v / cw;
                for (int k = 0; k < 4; k++)
                {
                    int nx = x + D4c[k], ny = y + D4r[k];
                    if (nx < 0 || nx >= cw || ny < 0 || ny >= ch) continue;
                    int u = ny * cw + nx;
                    if (water[u] || id[u] >= 0) continue;
                    id[u] = cid; q.Enqueue(u);
                }
            }
            sizes.Add(size);
        }
        return id;
    }

    /// <summary>Landbruecken: jede Landmasse mit einem Startplatz und jede
    /// grosse (≥ <paramref name="big"/> Ecken) wird mit der des ersten Platzes
    /// verbunden — kuerzester Weg durchs Wasser, fuenf Ecken breit.</summary>
    private static int Bridge(bool[] water, int cw, int ch, List<int> starts, int big)
    {
        if (starts.Count == 0) return 0;
        int made = 0;
        for (int guard = 0; guard < 24; guard++)
        {
            var id = LandComponents(water, cw, ch, out var sizes);
            int main = id[starts[0]];
            if (main < 0) return made;
            int target = -1;
            foreach (int s in starts) if (id[s] >= 0 && id[s] != main) { target = id[s]; break; }
            if (target < 0)
                for (int k = 0; k < sizes.Count; k++)
                    if (k != main && sizes[k] >= big) { target = k; break; }
            if (target < 0) return made;
            // BFS ab der Hauptmasse bis zur Zielmasse
            var prev = new int[water.Length];
            Array.Fill(prev, -2);
            var q = new Queue<int>();
            for (int v = 0; v < water.Length; v++) if (id[v] == main) { prev[v] = -1; q.Enqueue(v); }
            int hit = -1;
            while (q.Count > 0 && hit < 0)
            {
                int v = q.Dequeue();
                int x = v % cw, y = v / cw;
                for (int k = 0; k < 4; k++)
                {
                    int nx = x + D4c[k], ny = y + D4r[k];
                    if (nx < 0 || nx >= cw || ny < 0 || ny >= ch) continue;
                    int u = ny * cw + nx;
                    if (prev[u] != -2) continue;
                    prev[u] = v;
                    if (id[u] == target) { hit = u; break; }
                    q.Enqueue(u);
                }
            }
            if (hit < 0) return made;
            for (int v = prev[hit]; v >= 0 && id[v] != main; v = prev[v])
            {
                int x = v % cw, y = v / cw;
                for (int dy = -2; dy <= 2; dy++)
                    for (int dx = -2; dx <= 2; dx++)
                    {
                        int nx = x + dx, ny = y + dy;
                        if (nx < 0 || nx >= cw || ny < 0 || ny >= ch) continue;
                        int u = ny * cw + nx;
                        if (water[u]) { water[u] = false; made++; }
                    }
            }
        }
        return made;
    }

    /// <summary>Abstand jeder Ecke zum Wasser in Achterschritten, gedeckelt.</summary>
    private static int[] ChebDist(bool[] water, int cw, int ch, int cap)
    {
        var d = new int[water.Length];
        var q = new Queue<int>();
        for (int v = 0; v < water.Length; v++)
        {
            if (water[v]) { d[v] = 0; q.Enqueue(v); } else d[v] = cap;
        }
        while (q.Count > 0)
        {
            int v = q.Dequeue();
            if (d[v] >= cap) continue;
            int x = v % cw, y = v / cw;
            for (int k = 0; k < 8; k++)
            {
                int nx = x + D8c[k], ny = y + D8r[k];
                if (nx < 0 || nx >= cw || ny < 0 || ny >= ch) continue;
                int u = ny * cw + nx;
                if (d[u] > d[v] + 1) { d[u] = d[v] + 1; q.Enqueue(u); }
            }
        }
        return d;
    }

    /// <summary>Die Kettenregel: keine Ecke mehr als eine Stufe ueber ihrem
    /// niedrigsten Achternachbarn. Nur Absenken — monoton, endet sicher.</summary>
    private static int Repair(int[] depth, int cw, int ch, bool[]? locked = null)
    {
        int total = 0;
        for (int guard = 0; guard < 64; guard++)
        {
            int changed = 0;
            for (int y = 0; y < ch; y++)
                for (int x = 0; x < cw; x++)
                {
                    int v = y * cw + x, m = depth[v];
                    if (m == 0 || (locked != null && locked[v])) continue;
                    for (int k = 0; k < 8; k++)
                    {
                        int nx = x + D8c[k], ny = y + D8r[k];
                        if (nx < 0 || nx >= cw || ny < 0 || ny >= ch) continue;
                        m = Math.Min(m, depth[ny * cw + nx]);
                    }
                    if (depth[v] > m + 1) { depth[v] = m + 1; changed++; }
                }
            total += changed;
            if (changed == 0) break;
        }
        return total;
    }

    /// <summary>Kleine Stoffflecken (Vierer, ohne Wasser) bekommen die haeufigste
    /// Stufe ihres Randes.</summary>
    private static int Despeckle(int[] depth, int cw, int ch, int min)
    {
        int nv = depth.Length, changed = 0;
        var seen = new bool[nv];
        var q = new Queue<int>();
        var comp = new List<int>();
        for (int s = 0; s < nv; s++)
        {
            if (seen[s] || depth[s] == 0) continue;
            int val = depth[s];
            comp.Clear(); q.Enqueue(s); seen[s] = true;
            var border = new Dictionary<int, int>();
            while (q.Count > 0)
            {
                int v = q.Dequeue(); comp.Add(v);
                int x = v % cw, y = v / cw;
                for (int k = 0; k < 4; k++)
                {
                    int nx = x + D4c[k], ny = y + D4r[k];
                    if (nx < 0 || nx >= cw || ny < 0 || ny >= ch) continue;
                    int u = ny * cw + nx;
                    if (depth[u] != val) { border[depth[u]] = border.GetValueOrDefault(depth[u]) + 1; continue; }
                    if (seen[u]) continue;
                    seen[u] = true; q.Enqueue(u);
                }
            }
            if (comp.Count >= min || border.Count == 0) continue;
            int best = -1, bn = -1;
            foreach (var (d, cnt) in border)
                if (d > 0 && (cnt > bn || (cnt == bn && d < best))) { bn = cnt; best = d; }
            if (best < 0) continue;
            foreach (int v in comp) depth[v] = best;
            changed += comp.Count;
        }
        return changed;
    }

    /// <summary>Zellenabstand (Vierer) zu den Ufer- und Wasserzellen, gedeckelt.</summary>
    private static int[] CellDist(bool[] shore, bool[] water, int w, int h, int cap)
    {
        var d = new int[w * h];
        var q = new Queue<int>();
        for (int i = 0; i < w * h; i++)
        {
            if (shore[i] || water[i]) { d[i] = 0; q.Enqueue(i); } else d[i] = cap;
        }
        while (q.Count > 0)
        {
            int i = q.Dequeue();
            if (d[i] >= cap) continue;
            int c = i % w, r = i / w;
            for (int k = 0; k < 4; k++)
            {
                int nc = c + D4c[k], nr = r + D4r[k];
                if (nc < 0 || nc >= w || nr < 0 || nr >= h) continue;
                int j = nr * w + nc;
                if (d[j] > d[i] + 1) { d[j] = d[i] + 1; q.Enqueue(j); }
            }
        }
        return d;
    }

    // ---- Hoehen ----------------------------------------------------------------

    private int Heights(uint seed, TileCorners tc, int[] quadOf, bool[] waterCell, int[] coast,
                        Func<int, int, bool> zone, Action<string> say)
    {
        int w = W, h = H, n = w * h;
        var elig = new List<int>();
        var key = new double[n];
        double waveE = Math.Max(8.0, Math.Min(2.0 * w, h) / 3.0);
        for (int r = 0; r < h; r++)
            for (int c = 0; c < w; c++)
            {
                int i = r * w + c;
                if (waterCell[i] || coast[i] <= CoastalPlain || zone(c, r)) continue;
                key[i] = Noise(2.0 * c / waveE, r / waveE, seed ^ 0xE1E7u, 3);
                elig.Add(i);
            }
        if (_sym) for (int i = 0; i < n; i++) if (n - 1 - i < i) key[i] = key[n - 1 - i];   // P6
        elig.Sort((a, b) => key[a] != key[b] ? key[a].CompareTo(key[b]) : a.CompareTo(b));
        // gemessene Hoehenverteilung, oben bei MaxLevel gedeckelt (UNSERE SETZUNG)
        var share = new double[MaxLevel + 1];
        for (int k = 0; k < MapTerrain.ElevShare.Length; k++) share[Math.Min(k, MaxLevel)] += MapTerrain.ElevShare[k];
        {
            int at = 0;
            for (int lv = 0; lv <= MaxLevel; lv++)
            {
                int take = lv == MaxLevel ? elig.Count - at : (int)Math.Round(share[lv] * elig.Count);
                for (int k = 0; k < take && at < elig.Count; k++) Elev[elig[at++]] = lv;
            }
        }
        var isElig = new bool[n];
        foreach (int i in elig) isElig[i] = true;
        // zweimal Mehrheit (Modus der Achterumgebung) — runde Huegel statt Zacken
        for (int pass = 0; pass < 2; pass++)
        {
            var next = (int[])Elev.Clone();
            Span<int> votes = stackalloc int[MaxLevel + 1];
            foreach (int i in elig)
            {
                votes.Clear();
                int c = i % w, r = i / w;
                for (int dr = -1; dr <= 1; dr++)
                    for (int dc = -1; dc <= 1; dc++)
                    {
                        int nc = c + dc, nr = r + dr;
                        if (nc < 0 || nc >= w || nr < 0 || nr >= h) continue;
                        votes[Elev[nr * w + nc]]++;
                    }
                int best = Elev[i];
                for (int lv = 0; lv <= MaxLevel; lv++) if (votes[lv] > votes[best]) best = lv;
                next[i] = best;
            }
            Array.Copy(next, Elev, n);
        }

        // Reparatur: Sprung ≤ 1 zu allen acht Nachbarn; jede Hangzelle braucht
        // ein darstellbares Hangbyte (bug-769) MIT Kachel in dieser Eckkombination
        int rounds = 0, stepLow = 0, tileLow = 0;
        for (; rounds < 400; rounds++)
        {
            if (_sym)                                                   // P6: Paare auf das Minimum
                for (int i = 0; i < n; i++)
                {
                    int j = n - 1 - i;
                    if (j < i && Elev[i] != Elev[j]) { int mn = Math.Min(Elev[i], Elev[j]); Elev[i] = Elev[j] = mn; }
                }
            int changed = 0;
            for (int i = 0; i < n; i++)
            {
                int c = i % w, r = i / w, m = Elev[i];
                for (int k = 0; k < 8; k++)
                {
                    int nc = c + D8c[k], nr = r + D8r[k];
                    if (nc < 0 || nc >= w || nr < 0 || nr >= h) continue;
                    m = Math.Min(m, Elev[nr * w + nc]);
                }
                if (Elev[i] > m + 1) { Elev[i] = m + 1; stepLow++; changed++; }
            }
            for (int i = 0; i < n; i++)
            {
                int c = i % w, r = i / w;
                int f = FlagAt(c, r);
                if (f >= 0 && (f == 0 || tc.Has(quadOf[i], f))) continue;
                int e = Elev[i];
                for (int k = 0; k < 8; k++)
                {
                    int nc = c + D8c[k], nr = r + D8r[k];
                    if (nc < 0 || nc >= w || nr < 0 || nr >= h) continue;
                    int j = nr * w + nc;
                    if (Elev[j] > e) { Elev[j] = e; tileLow++; changed++; }
                }
            }
            if (changed == 0 && (!_sym || SymOk())) break;
        }
        int slopes = 0, unrep = 0;
        var lv0 = new int[MaxLevel + 1];
        for (int i = 0; i < n; i++)
        {
            int f = FlagAt(i % w, i / w);
            if (f < 0) { unrep++; f = 0; }
            Flag[i] = f;
            if (f != 0) slopes++;
            lv0[Math.Clamp(Elev[i], 0, MaxLevel)]++;
        }
        say($"Hoehen: Stufen 0..{MaxLevel} = {string.Join(" / ", Array.ConvertAll(lv0, v => $"{100.0 * v / n:0.0}%"))}, " +
            $"Reparatur {rounds} Runden ({stepLow} wegen Sprung, {tileLow} wegen Hangbyte ohne Kachel); " +
            $"{slopes} Hangzellen ({100.0 * slopes / n:0.0} %, Original 17,6 %), {unrep} nicht darstellbar (muss 0 sein)");
        return slopes;
    }

    private int FlagAt(int c, int r)
    {
        int e = Elev[r * W + c];
        bool Up(int dc, int dr)
        {
            int nc = c + dc, nr = r + dr;
            return nc >= 0 && nc < W && nr >= 0 && nr < H && Elev[nr * W + nc] > e;
        }
        return TileCorners.HangByte8(Up(1, 0), Up(1, 1), Up(0, 1), Up(-1, 1),
                                     Up(-1, 0), Up(-1, -1), Up(0, -1), Up(1, -1));
    }

    // ---- Verbindung ------------------------------------------------------------

    /// <summary>Flutung fuer gewoehnliches Fahrwerk (nur frei), acht Nachbarn mit
    /// der Eckenregel der Suchkarte — dieselbe Regel wie <see cref="MapCheck"/>.</summary>
    public static bool[] VehicleFlood(int w, int h, byte[] cls, IEnumerable<int> sources)
    {
        var seen = new bool[w * h];
        var q = new Queue<int>();
        foreach (int s in sources)
            if (s >= 0 && s < w * h && !seen[s]) { seen[s] = true; q.Enqueue(s); }
        bool Weich(int c, int r) => c >= 0 && c < w && r >= 0 && r < h && cls[r * w + c] <= 1;
        while (q.Count > 0)
        {
            int i = q.Dequeue();
            int c = i % w, r = i / w;
            for (int k = 0; k < 8; k++)
            {
                int nc = c + D8c[k], nr = r + D8r[k];
                if (nc < 0 || nc >= w || nr < 0 || nr >= h) continue;
                int j = nr * w + nc;
                if (seen[j] || cls[j] != 0) continue;
                if ((k & 1) != 0 && (!Weich(nc, r) || !Weich(c, nr))) continue;
                seen[j] = true; q.Enqueue(j);
            }
        }
        return seen;
    }

    private int Connect(TileCorners tc, int[] quadOf, bool[] shore, Action<string> say)
    {
        if (Plots.Count < 2) return 0;
        int w = W, h = H, carved = 0;
        int Center(int p) => (Plots[p].Row + 2) * w + Plots[p].Col + 3;
        for (int guard = 0; guard < Plots.Count * 2; guard++)
        {
            var reach = VehicleFlood(w, h, Class, new[] { Center(0) });
            int miss = -1;
            for (int p = 1; p < Plots.Count; p++) if (!reach[Center(p)]) { miss = p; break; }
            if (miss < 0) return carved;
            // 0-1-BFS: frei kostet 0, Rau/Wald 1, Ufer 4, Wasser gesperrt
            var dist = new int[w * h];
            var prev = new int[w * h];
            Array.Fill(dist, int.MaxValue);
            var dq = new LinkedList<int>();
            for (int i = 0; i < w * h; i++) if (reach[i]) { dist[i] = 0; prev[i] = -1; dq.AddLast(i); }
            int goal = Center(miss);
            while (dq.Count > 0)
            {
                int i = dq.First!.Value; dq.RemoveFirst();
                if (i == goal) break;
                int c = i % w, r = i / w;
                for (int k = 0; k < 4; k++)
                {
                    int nc = c + D4c[k], nr = r + D4r[k];
                    if (nc < 0 || nc >= w || nr < 0 || nr >= h) continue;
                    int j = nr * w + nc;
                    if (Class[j] == 2) continue;
                    int cost = Class[j] == 0 ? 0 : shore[j] ? 4 : 1;
                    if (dist[i] + cost >= dist[j]) continue;
                    dist[j] = dist[i] + cost; prev[j] = i;
                    if (cost == 0) dq.AddFirst(j); else dq.AddLast(j);
                }
            }
            if (dist[goal] == int.MaxValue)
            {
                say($"Startplatz {miss}: auch durch Rau/Wald nicht erreichbar — nur ueber Wasser");
                return carved;
            }
            for (int i = goal; i >= 0; i = prev[i])
            {
                int c = i % w, r = i / w;
                for (int k = -1; k < 4; k++)
                {
                    int nc = k < 0 ? c : c + D4c[k], nr = k < 0 ? r : r + D4r[k];
                    if (nc < 0 || nc >= w || nr < 0 || nr >= h) continue;
                    int j = nr * w + nc;
                    if (Class[j] == 0 || Class[j] == 2 || (k >= 0 && shore[j])) continue;
                    if (Class[j] == 3)
                    {
                        int code = tc.Pick(quadOf[j], Flag[j], MapTerrain.Hash(nc, nr, 0xCA7Eu));
                        if (code < 0) continue;
                        Code[j] = code;
                    }
                    Class[j] = 0;
                    carved++;
                }
                if (prev[i] == -1) break;
            }
        }
        return carved;
    }
}
