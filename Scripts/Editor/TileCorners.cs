namespace AkteEuropaReborn.Editor;

using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using Godot;

/// <summary>
/// ⭐ 11.11.2026 — bug-768: DIE ECKEN JEDER KACHEL, aus den gelieferten Karten
/// GELERNT. Die Grundlage des Eck-Generators (<see cref="MapLandscape"/>).
///
/// <para><b>Der Anlass</b> ist eine Spielermeldung zu 0.7.0: »der Karteneditor
/// macht noch wilde sachen. leuchtende dreiecke verteilt auf der karte. das
/// wasser ist teils auch noch wild in festlandschaft verteilt.« Die bisherige
/// Kachelwahl (<see cref="TileModel"/> + <see cref="TileSeams"/>) wuerfelt je
/// Zelle aus einem Fach (Klasse, Hangbyte, Wassermaske, Wasserabstand) — sie
/// weiss nicht, welcher BILDSTOFF an welcher Kachelecke liegt, und legt darum
/// Eck- und Diagonaluebergaenge mitten in fremdes Gelaende.</para>
///
/// <para><b>GEMESSEN, an allen 37 Kachelsaetzen mit gelieferten Karten</b>
/// (<c>scratchpad/gen/a6.py</c>, nachgerechnet hier in <see cref="Learn"/>): die
/// Originale sind ein reines ECK-KACHELSYSTEM. Verbindet man je Nachbarpaar die
/// zwei Ecken, die sich beruehren (waagrecht: NO=NW', SO=SW'; senkrecht:
/// SW=NW', SO=NO'), und nur bei Paaren, die mindestens <see cref="MinPair"/>-mal
/// vorkommen, zerfallen die Ecken in 2..4 grosse Klassen — die BILDSTOFFE. Bei
/// Kachelsatz 47: Gras (90..97), Wasser (0..7), Sand (8..15), Geroell (148..);
/// danach traegt ein Mehrheitsvotum jede Ecke in einen Stoff. Die Probe:
/// <b>99,996 %</b> aller Eckstoesse der Originale passen (NET02+NET07, 75.773
/// Zellen); ueber alle 37 Saetze 99,959..100,000 %.</para>
///
/// <para>Und zweitens: die Stoffe bilden in JEDEM Satz eine KETTE, mit Wasser am
/// Ende — bei 47 Wasser–Sand–Gras–Geroell. Eine Kachel mischt nur zwei
/// NACHBARN der Kette (es gibt keine Kachel Wasser/Gras oder Sand/Geroell), und
/// keine mischt drei. Daraus folgt die Regel des Generators: der Stoff an zwei
/// Ecken derselben Zelle unterscheidet sich hoechstens um eine Kettenstufe.</para>
///
/// <para><b>Hangbytes 9..14 sind DIAGONALEN</b> (bug-769, gemessen ueber alle
/// 44 Karten, a5.py): 9 nur SO hoeher (17.548 von 17.552), 10 nur SW (16.937 von
/// 16.943), 11 nur NW (15.401 von 15.404), 12 nur NO (14.634 von 14.635), 13 SO+NW
/// (73 von 73), 14 SW+NO (55 von 57). <see cref="MapTerrain.HangByte"/> kennt nur
/// die vier Geraden und setzt an jeder aeusseren Plateauecke 0.</para>
///
/// <para><b>UNSERE SETZUNG</b> sind die zwei Lernschwellen (<see cref="MinPair"/>,
/// <see cref="MinSeedShare"/>) — gewaehlt so, dass auf allen 37 Saetzen eine
/// Kette herauskommt; die Passquote ist danach der Beleg, nicht die Wahl.</para>
/// </summary>
public sealed class TileCorners
{
    /// <summary>Ab wie vielen Vorkommen ein Nachbarpaar zwei Ecken verbindet.
    /// UNSERE SETZUNG: mit 1 verschmelzen bei 47 durch Einzelfehler alle Stoffe
    /// (4 Komponenten, eine mit 3308 Ecken), mit 3 sind es die vier Stoffe.</summary>
    public const int MinPair = 3;

    /// <summary>Wie gross eine Komponente sein muss, um ein Stoff zu sein —
    /// Anteil an den Bodenzellen. UNSERE SETZUNG: 0,5 %, damit bei Kachelsatz 09
    /// (eine Karte, 5.903 Bodenzellen) der vierte Stoff (114 Zellen) erkannt wird.
    /// </summary>
    public const double MinSeedShare = 0.005;

    /// <summary>Codes 0..7: Wasseranimation (TilePalette). Ab GroundMax Objekt.</summary>
    public const int GroundMax = Import.MapBaker.GroundMax;

    public int Tileset { get; private set; }
    public int Labels { get; private set; }
    public int WaterLabel { get; private set; } = -1;
    /// <summary>Kettenstufe je Stoff (0 = Wasser). Nur gueltig, wenn <see cref="IsChain"/>.</summary>
    public int[] Depth { get; private set; } = Array.Empty<int>();
    /// <summary>Stoff je Kettenstufe.</summary>
    public int[] LabelOfDepth { get; private set; } = Array.Empty<int>();
    public int MaxDepth => LabelOfDepth.Length - 1;
    public bool IsChain { get; private set; }
    /// <summary>bug-772: die sec2-Klasse (0..3) je Stoff — GEMESSEN.</summary>
    public int[] SecOfLabel { get; private set; } = Array.Empty<int>();
    /// <summary>Anteil der Knoten, deren sec2-Wert zu ihrem Stoff passt.</summary>
    public double SecAgreement { get; private set; }
    public string ChainWhy { get; private set; } = "";
    public List<string> Sources { get; } = new();
    public int GroundCells { get; private set; }
    public long PassOk { get; private set; }
    public long PassBad { get; private set; }
    /// <summary>Eckanteil je Stoff (ueber alle Bodenkacheln, gewichtet).</summary>
    public double[] LabelShare { get; private set; } = Array.Empty<double>();
    /// <summary>Objektzellen (Wald, Fels) je Landzelle — gemessen.</summary>
    public double ObjectShare { get; private set; }
    /// <summary>Anteil »rau« unter den Hangzellen (Hangbyte ≠ 0, kein Objekt) — gemessen.</summary>
    public double SlopeRoughShare { get; private set; }

    /// <summary>Code → vier Stoffe (NW, NO, SW, SO), −1 = unbekannt.</summary>
    private readonly Dictionary<int, sbyte[]> _corners = new();
    private readonly Dictionary<long, Bucket> _table = new();
    private readonly Dictionary<int, Bucket> _objects = new();
    private readonly Dictionary<int, int[]> _quadClass = new();
    private readonly HashSet<int> _buildingTiles = new();

    private sealed class Bucket
    {
        public readonly List<int> Codes = new();
        public readonly List<int> Weight = new();
        public int Total;
        public void Add(int code, int n)
        {
            int i = Codes.IndexOf(code);
            if (i < 0) { Codes.Add(code); Weight.Add(n); }
            else Weight[i] += n;
            Total += n;
        }
    }

    // ---- Schluessel ----------------------------------------------------------

    /// <summary>Vier Stoffe zu einem Schluessel. −1 (unbekannt) wird 15.</summary>
    public static int Quad(int nw, int ne, int sw, int se)
        => (nw & 15) | ((ne & 15) << 4) | ((sw & 15) << 8) | ((se & 15) << 12);

    public static int QuadAt(int quad, int corner) { int v = (quad >> (4 * corner)) & 15; return v == 15 ? -1 : v; }

    private static long Key(int quad, int flag) => ((long)quad << 8) | (uint)(flag & 255);

    public int Corner(int code, int k) => _corners.TryGetValue(code, out var q) ? q[k] : -1;
    public bool Knows(int code) => _corners.ContainsKey(code);

    public int QuadOf(int code)
        => _corners.TryGetValue(code, out var q) ? Quad(q[0], q[1], q[2], q[3]) : Quad(-1, -1, -1, -1);

    public bool Has(int quad, int flag) => _table.ContainsKey(Key(quad, flag));

    // ---- Lernen --------------------------------------------------------------

    private sealed class Grid
    {
        public int W, H;
        public int[] Code = Array.Empty<int>(), Flag = Array.Empty<int>();
        public byte[] Cls = Array.Empty<byte>();
        /// <summary>sec2-Knoten [y*W+x] (nur x&lt;W, y&lt;H), -1 = fehlt.</summary>
        public int[] Zone = Array.Empty<int>();
    }

    /// <summary>Alle gelieferten Karten dieses Kachelsatzes einlesen (dieselben
    /// Quellen wie <see cref="TileModel.Learn"/>, selbst erzeugte uebergangen).
    /// <c>null</c>, wenn keine Karte den Satz benutzt.</summary>
    public static TileCorners? Learn(int tileset, Action<string> say,
                                     Import.IBuildingPatterns? patterns = null)
    {
        var tc = new TileCorners { Tileset = tileset };
        // Baeume UND Gebaeudekacheln stehen als 10000+Kachel im Raster; nur die
        // Gebaeudekacheln sind kein Gelaende (siehe TileModel._buildingTiles).
        if (patterns != null && patterns.HasBuildings)
            for (int typ = 0; typ < Import.CwpFile.BuildingTypeCount; typ++)
            {
                var bt = patterns.GetBuildingType(typ);
                if (bt.IsEmpty) continue;
                for (int k = 0; k < bt.PatternCount; k++)
                    for (int x = 0; x < Import.CwpFile.PatternWidth; x++)
                        for (int y = 0; y < Import.CwpFile.PatternHeight; y++)
                        {
                            int t = patterns.PatternTile(bt.FirstPattern + k, x, y);
                            if (t != 0) tc._buildingTiles.Add(t + Import.CwpFile.ObjectCodeBase);
                        }
            }
        var grids = new List<Grid>();
        foreach (string dir in new[] { Core.Content.UserRoot + "Maps", Core.Content.DevRoot + "Maps" })
        {
            if (!DirAccess.DirExistsAbsolute(dir)) continue;
            foreach (string f in DirAccess.GetFilesAt(dir))
            {
                if (!f.StartsWith("map_") || !f.EndsWith(".json") || f.Contains(".entities.")) continue;
                string stem = f[..^".json".Length];
                if (tc.Sources.Contains(stem)) continue;
                var g = Read(dir, stem, tileset);
                if (g == null) continue;
                grids.Add(g);
                tc.Sources.Add(stem);
            }
        }
        if (grids.Count == 0)
        {
            say($"Eckmodell: Kachelsatz {tileset:00} — keine gelieferte Karte, nichts zu lernen");
            return null;
        }
        tc.Build(grids);
        say(tc.Describe());
        return tc;
    }

    private static Grid? Read(string dir, string stem, int tileset)
    {
        string metaPath = $"{dir}/{stem}.json", entPath = $"{dir}/{stem}.entities.json";
        if (!FileAccess.FileExists(metaPath) || !FileAccess.FileExists(entPath)) return null;
        string text = FileAccess.GetFileAsString(metaPath);
        if (text.Length == 0) return null;
        // selbst erzeugt: NIE lernen (siehe TileModel.Feed, 13.08.2026)
        if (text.Contains("\"mission\":\"Editor ", StringComparison.Ordinal)) return null;
        int at = text.IndexOf("\"tileset\":", StringComparison.Ordinal);
        if (at < 0) return null;
        {
            int e = at + "\"tileset\":".Length, v = 0; bool any = false;
            while (e < text.Length && text[e] == ' ') e++;
            while (e < text.Length && text[e] >= '0' && text[e] <= '9') { v = v * 10 + (text[e++] - '0'); any = true; }
            if (!any || v != tileset) return null;
        }
        try
        {
            using var meta = JsonDocument.Parse(text);
            var root = meta.RootElement;
            int w = root.GetProperty("width").GetInt32(), h = root.GetProperty("height").GetInt32();
            var g = new Grid { W = w, H = h, Code = new int[w * h], Flag = new int[w * h], Cls = new byte[w * h] };
            foreach (var t in root.GetProperty("tiles").EnumerateArray())
            {
                int c = t.GetProperty("col").GetInt32(), r = t.GetProperty("row").GetInt32();
                if (c < 0 || c >= w || r < 0 || r >= h) continue;
                g.Code[r * w + c] = t.GetProperty("code").GetInt32();
                g.Flag[r * w + c] = t.GetProperty("flag").GetInt32();
            }
            using var ent = JsonDocument.Parse(FileAccess.GetFileAsString(entPath));
            g.Zone = new int[w * h];
            Array.Fill(g.Zone, -1);
            if (ent.RootElement.TryGetProperty("zones", out var zs) && zs.TryGetProperty("grid", out var zgrid))
            {
                // ⚠ zeilenweise: grid[y][x] — an NET07/map_09 gegen die Ecken
                // geprueft (spaltenweise gelesen streut es ueber alle Werte)
                int y = 0;
                foreach (var row in zgrid.EnumerateArray())
                {
                    int x = 0;
                    if (y < h)
                        foreach (var v in row.EnumerateArray()) { if (x < w) g.Zone[y * w + x] = v.GetInt32(); x++; }
                    y++;
                }
            }
            int a2 = 0;
            foreach (var pair in ent.RootElement.GetProperty("terrain").GetProperty("rle").EnumerateArray())
            {
                var it = pair.EnumerateArray();
                it.MoveNext(); byte v = (byte)it.Current.GetInt32();
                it.MoveNext(); int run = it.Current.GetInt32();
                for (int k = 0; k < run && a2 < w * h; k++) g.Cls[a2++] = v;
            }
            return a2 == w * h ? g : null;
        }
        catch (Exception) { return null; }
    }

    private static bool IsGround(int code) => code >= 0 && code < GroundMax;

    private void Build(List<Grid> grids)
    {
        // ---- 1. Zaehlen ------------------------------------------------------
        var cnt = new Dictionary<int, int>();
        var links = new Dictionary<int, Dictionary<int, int>>();   // Ecke (code*4+k) → Ecke → n
        void Link(int x, int y, int n)
        {
            if (!links.TryGetValue(x, out var lx)) links[x] = lx = new();
            if (!links.TryGetValue(y, out var ly)) links[y] = ly = new();
            lx[y] = lx.GetValueOrDefault(y) + n;
            ly[x] = ly.GetValueOrDefault(x) + n;
        }
        var pairs = new Dictionary<long, int>();
        foreach (var g in grids)
            for (int r = 0; r < g.H; r++)
                for (int c = 0; c < g.W; c++)
                {
                    int i = r * g.W + c, a = g.Code[i];
                    if (!IsGround(a)) continue;
                    cnt[a] = cnt.GetValueOrDefault(a) + 1;
                    if (c + 1 < g.W && IsGround(g.Code[i + 1]))
                    { long k = ((long)a << 20) | ((long)g.Code[i + 1] << 1); pairs[k] = pairs.GetValueOrDefault(k) + 1; }
                    if (r + 1 < g.H && IsGround(g.Code[i + g.W]))
                    { long k = ((long)a << 20) | ((long)g.Code[i + g.W] << 1) | 1; pairs[k] = pairs.GetValueOrDefault(k) + 1; }
                }
        foreach (var (k, n) in pairs)
        {
            int a = (int)(k >> 20), b = (int)((k >> 1) & 0x7FFFF);
            if ((k & 1) == 0) { Link(a * 4 + 1, b * 4 + 0, n); Link(a * 4 + 3, b * 4 + 2, n); }
            else { Link(a * 4 + 2, b * 4 + 0, n); Link(a * 4 + 3, b * 4 + 1, n); }
        }
        int ground = 0;
        foreach (var n in cnt.Values) ground += n;
        GroundCells = ground;

        // ---- 2. Union-Find ueber starke Paare --------------------------------
        var par = new Dictionary<int, int>();
        int Find(int x)
        {
            if (!par.TryGetValue(x, out int p)) { par[x] = x; return x; }
            while (p != x) { int pp = par[p]; par[x] = pp; x = p; p = pp; }
            return x;
        }
        foreach (var (x, lx) in links)
            foreach (var (y, n) in lx)
                if (n >= MinPair) { int ra = Find(x), rb = Find(y); if (ra != rb) par[ra] = rb; }
        var comp = new Dictionary<int, List<int>>();
        foreach (int x in links.Keys)
        {
            int r = Find(x);
            if (!comp.TryGetValue(r, out var l)) comp[r] = l = new();
            l.Add(x);
        }
        var seeds = new List<(double Cells, List<int> Members)>();
        foreach (var l in comp.Values)
        {
            double cells = 0;
            foreach (int x in l) cells += cnt.GetValueOrDefault(x / 4);
            cells /= 4;
            if (cells >= MinSeedShare * ground) seeds.Add((cells, l));
        }
        seeds.Sort((p, q) => q.Cells != p.Cells ? q.Cells.CompareTo(p.Cells) : p.Members[0].CompareTo(q.Members[0]));
        if (seeds.Count > 14) seeds.RemoveRange(14, seeds.Count - 14);
        Labels = seeds.Count;
        var lab = new Dictionary<int, int>();
        for (int s = 0; s < seeds.Count; s++) foreach (int x in seeds[s].Members) lab[x] = s;

        // ---- 3. Mehrheitsvotum ----------------------------------------------
        var keys = new List<int>(links.Keys);
        keys.Sort();
        for (int round = 0; round < 10; round++)
        {
            int changed = 0;
            var next = new Dictionary<int, int>(lab);
            var vote = new int[Math.Max(1, Labels)];
            foreach (int x in keys)
            {
                Array.Clear(vote);
                bool any = false;
                foreach (var (y, n) in links[x])
                    if (lab.TryGetValue(y, out int ly)) { vote[ly] += n; any = true; }
                if (!any) continue;
                int best = 0;
                for (int s = 1; s < Labels; s++) if (vote[s] > vote[best]) best = s;
                if (!lab.TryGetValue(x, out int old) || old != best) { next[x] = best; changed++; }
            }
            lab = next;
            if (changed == 0) break;
        }
        foreach (int code in cnt.Keys)
        {
            var q = new sbyte[4];
            for (int k = 0; k < 4; k++) q[k] = (sbyte)(lab.TryGetValue(code * 4 + k, out int l) ? l : -1);
            _corners[code] = q;
        }
        WaterLabel = _corners.TryGetValue(0, out var wq) ? wq[0] : -1;

        // ---- 3b. Stoff -> sec2-Klasse, GEMESSEN ---------------------------
        // ⭐ 11.11.2026 (bug-772): das Original rechnet seine Kachel selbst aus
        // einem ECKraster — sec2, 257x257 Knoten, Klassen 0..3 (MapBaker.
        // SyntheseKachel, 0x41FA10). Je Knoten der Stoff, den die Mehrheit der
        // anliegenden Bodenkacheln zeigt, gegen den sec2-Wert: NET07 und map_09
        // 1:1 ohne Fehlgriff (Wasser 0, Strand 1, Hauptboden 2, Zusatzstoff 3).
        var zc = new int[Math.Max(1, Labels), 4];
        foreach (var g in grids)
            for (int y = 0; y < g.H; y++)
                for (int x = 0; x < g.W; x++)
                {
                    int z = g.Zone[y * g.W + x];
                    if (z < 0 || z > 3) continue;
                    var vote = new int[Math.Max(1, Labels)];
                    void V(int c, int r, int k)
                    {
                        if (c < 0 || c >= g.W || r < 0 || r >= g.H) return;
                        int code = g.Code[r * g.W + c];
                        if (!IsGround(code) || !_corners.TryGetValue(code, out var q) || q[k] < 0) return;
                        vote[q[k]]++;
                    }
                    V(x - 1, y - 1, 3); V(x, y - 1, 2); V(x - 1, y, 1); V(x, y, 0);
                    int best = -1, bn = 0;
                    for (int l = 0; l < Labels; l++) if (vote[l] > bn) { bn = vote[l]; best = l; }
                    if (best >= 0) zc[best, z]++;
                }
        SecOfLabel = new int[Labels];
        long zOk = 0, zAll = 0;
        for (int l = 0; l < Labels; l++)
        {
            int best = l == WaterLabel ? 0 : 2;
            for (int z = 0; z < 4; z++) if (zc[l, z] > zc[l, best]) best = z;
            SecOfLabel[l] = best;
            for (int z = 0; z < 4; z++) { zAll += zc[l, z]; if (z == best) zOk += zc[l, z]; }
        }
        SecAgreement = zAll > 0 ? (double)zOk / zAll : 0;

        // ---- 4. Tafel, Objekte, Klassen, Passquote ---------------------------
        var shareCount = new double[Math.Max(1, Labels)];
        long land = 0, objectCells = 0, slope = 0, slopeRough = 0;
        foreach (var g in grids)
            for (int r = 0; r < g.H; r++)
                for (int c = 0; c < g.W; c++)
                {
                    int i = r * g.W + c, a = g.Code[i];
                    if (g.Cls[i] != 2) land++;
                    if (!IsGround(a))
                    {
                        if (_buildingTiles.Contains(a)) continue;              // Gebaeudekachel
                        objectCells++;
                        int mat = g.Cls[i] == 3 ? ObjectMaterial(g, c, r) : -1;
                        if (mat >= 0) Slot(_objects, mat).Add(a, 1);
                        continue;
                    }
                    var q = _corners[a];
                    for (int k = 0; k < 4; k++) if (q[k] >= 0) shareCount[q[k]] += 0.25;
                    int quad = Quad(q[0], q[1], q[2], q[3]);
                    int fl = g.Flag[i];
                    if (!_table.TryGetValue(Key(quad, fl), out var b)) _table[Key(quad, fl)] = b = new Bucket();
                    b.Add(a, 1);
                    if (!_quadClass.TryGetValue(quad, out var qc)) _quadClass[quad] = qc = new int[4];
                    if (g.Cls[i] < 4) qc[g.Cls[i]]++;
                    if (fl != 0) { slope++; if (g.Cls[i] == 1) slopeRough++; }
                    // Passquote: Ost- und Suednachbar
                    if (c + 1 < g.W && IsGround(g.Code[i + 1])) Pass(a, g.Code[i + 1], true);
                    if (r + 1 < g.H && IsGround(g.Code[i + g.W])) Pass(a, g.Code[i + g.W], false);
                }
        double tot = 0;
        foreach (double v in shareCount) tot += v;
        LabelShare = new double[Labels];
        for (int s = 0; s < Labels; s++) LabelShare[s] = tot > 0 ? shareCount[s] / tot : 0;
        ObjectShare = land > 0 ? (double)objectCells / land : 0;
        SlopeRoughShare = slope > 0 ? (double)slopeRough / slope : 0;

        // ---- 5. Die Kette ---------------------------------------------------
        FindChain();
    }

    private void Pass(int a, int b, bool horizontal)
    {
        var p = _corners[a]; var q = _corners[b];
        int a1 = horizontal ? p[1] : p[2], b1 = q[0], a2 = p[3], b2 = horizontal ? q[2] : q[1];
        if (a1 == b1 && a1 >= 0) PassOk++; else PassBad++;
        if (a2 == b2 && a2 >= 0) PassOk++; else PassBad++;
    }

    /// <summary>Der Stoff UNTER einer Objektzelle — nur wenn er EINDEUTIG ist:
    /// alle Ecken aller Bodenkacheln der acht Nachbarn tragen denselben
    /// Landstoff (mindestens zwei solche Nachbarn), sonst −1.
    /// <para>⚠ 11.11.2026 (bug-768): zuerst stand hier die Mehrheit der vier
    /// Nachbarn. Damit kamen Objekte, die im Original AM WASSER stehen und
    /// selbst Wasserpixel tragen (Codes 107xx/104xx bei Satz 47), als »Wald auf
    /// Sand« in die Tafel — im Bild blaue Rechtecke mitten im Strand.</para>
    /// </summary>
    private int ObjectMaterial(Grid g, int c, int r)
    {
        int label = -1, seen = 0;
        for (int dr = -1; dr <= 1; dr++)
            for (int dc = -1; dc <= 1; dc++)
            {
                if (dc == 0 && dr == 0) continue;
                int cc = c + dc, rr = r + dr;
                if (cc < 0 || cc >= g.W || rr < 0 || rr >= g.H) continue;
                int code = g.Code[rr * g.W + cc];
                if (!IsGround(code)) continue;
                if (!_corners.TryGetValue(code, out var q)) return -1;
                for (int k = 0; k < 4; k++)
                {
                    if (q[k] < 0 || q[k] == WaterLabel) return -1;
                    if (label < 0) label = q[k];
                    else if (q[k] != label) return -1;
                }
                seen++;
            }
        return seen >= 2 ? label : -1;
    }

    private static Bucket Slot(Dictionary<int, Bucket> d, int k)
    {
        if (!d.TryGetValue(k, out var b)) d[k] = b = new Bucket();
        return b;
    }

    /// <summary>Die Kanten des Stoffgraphen (Kacheln mit genau zwei Stoffen,
    /// gewichtet nach Zellen) und daraus die Kette ab Wasser.</summary>
    private void FindChain()
    {
        IsChain = false;
        if (Labels < 2 || WaterLabel < 0) { ChainWhy = $"{Labels} Stoffe, Wasser {WaterLabel}"; return; }
        var w = new int[Labels, Labels];
        foreach (var (key, b) in _table)
        {
            int quad = (int)(key >> 8);
            int x = -1, y = -1; bool bad = false;
            for (int k = 0; k < 4; k++)
            {
                int l = QuadAt(quad, k);
                if (l < 0) { bad = true; break; }
                if (x < 0 || x == l) x = l; else if (y < 0 || y == l) y = l; else bad = true;
            }
            if (bad || y < 0) continue;
            w[x, y] += b.Total; w[y, x] += b.Total;
        }
        int min = Math.Max(20, GroundCells / 1000);
        var deg = new int[Labels];
        int edges = 0;
        for (int a = 0; a < Labels; a++)
            for (int b = a + 1; b < Labels; b++)
                if (w[a, b] >= min) { deg[a]++; deg[b]++; edges++; }
        if (edges != Labels - 1) { ChainWhy = $"{edges} Kanten bei {Labels} Stoffen"; return; }
        for (int a = 0; a < Labels; a++) if (deg[a] > 2) { ChainWhy = $"Stoff {a} hat {deg[a]} Nachbarn"; return; }
        if (deg[WaterLabel] != 1) { ChainWhy = "Wasser liegt nicht am Ende der Kette"; return; }
        var order = new List<int> { WaterLabel };
        int prev = -1, cur = WaterLabel;
        while (order.Count < Labels)
        {
            int nx = -1;
            for (int b = 0; b < Labels; b++)
                if (b != cur && b != prev && w[cur, b] >= min) { nx = b; break; }
            if (nx < 0) break;
            order.Add(nx); prev = cur; cur = nx;
        }
        if (order.Count != Labels) { ChainWhy = "Kette nicht zusammenhaengend"; return; }
        LabelOfDepth = order.ToArray();
        Depth = new int[Labels];
        for (int d = 0; d < order.Count; d++) Depth[order[d]] = d;
        IsChain = true;
        ChainWhy = "Kette " + string.Join("–", order.ConvertAll(l => Name(l)));
    }

    /// <summary>11.11.2026 bug-801: der kleinste Code einer Kachel, die an allen
    /// vier Ecken diesen Stoff traegt, oder -1 — fuer die Farbprobe der
    /// Kachelsatzbeschreibung (<see cref="KachelsatzInfo"/>).</summary>
    public int UniformCode(int label)
    {
        int best = int.MaxValue;
        foreach (var (code, q) in _corners)
            if (q[0] == label && q[1] == label && q[2] == label && q[3] == label && code < best) best = code;
        return best == int.MaxValue ? -1 : best;
    }

    /// <summary>Ein Stoffname fuer die Meldung: der kleinste Code einer Kachel,
    /// die an allen vier Ecken diesen Stoff traegt.</summary>
    public string Name(int label)
    {
        int best = int.MaxValue;
        foreach (var (code, q) in _corners)
            if (q[0] == label && q[1] == label && q[2] == label && q[3] == label && code < best) best = code;
        return label == WaterLabel ? $"Wasser({best})" : $"S{label}({best})";
    }

    // ---- Benutzen ------------------------------------------------------------

    /// <summary>Die gelernte Gelaendeklasse dieser Eckkombination (Mehrheit in
    /// den Originalen, Wald ausgenommen): 0 frei, 1 rau, 2 Wasser.</summary>
    public int ClassOf(int quad)
    {
        if (!_quadClass.TryGetValue(quad, out var qc)) return 0;
        int best = 0;
        for (int k = 1; k < 3; k++) if (qc[k] > qc[best]) best = k;
        return best;
    }

    public bool HasObjects(int label) => _objects.TryGetValue(label, out var b) && b.Total >= 8;

    public int PickObject(int label, uint roll)
        => _objects.TryGetValue(label, out var b) && Draw(b, roll, out int code) ? code : -1;

    /// <summary>Eine Kachel fuer diese Ecken und dieses Hangbyte, gewichtet nach
    /// den Originalen; mit <paramref name="seam"/> der beste aus
    /// <paramref name="tries"/> Wuerfen an der Pixelnaht zu West und Nord
    /// (dieselbe Wahl wie <see cref="TileModel"/>, UNSERE SETZUNG).</summary>
    public int Pick(int quad, int flag, uint roll, Func<int, int, bool, double>? seam = null,
                    int tries = 1, int west = -1, int north = -1)
    {
        if (!_table.TryGetValue(Key(quad, flag), out var b) || b.Total == 0) return -1;
        if (!Draw(b, roll, out int code)) return -1;
        if (seam == null || tries <= 1 || b.Codes.Count == 1 || (west < 0 && north < 0)) return code;
        double best = CostOf(code);
        if (best < 0) return code;
        for (int k = 1; k < tries; k++)
        {
            if (!Draw(b, Mix(roll, (uint)k), out int cand)) continue;
            double c = CostOf(cand);
            if (c >= 0 && c < best) { best = c; code = cand; }
        }
        return code;

        double CostOf(int cand)
        {
            double sum = 0; int n = 0;
            if (west >= 0) { double v = seam!(west, cand, true); if (v >= 0) { sum += v; n++; } }
            if (north >= 0) { double v = seam!(north, cand, false); if (v >= 0) { sum += v; n++; } }
            return n == 0 ? -1 : sum / n;
        }
    }

    private static bool Draw(Bucket b, uint roll, out int code)
    {
        code = -1;
        if (b.Total == 0) return false;
        int pick = (int)(roll % (uint)b.Total);
        for (int i = 0; i < b.Codes.Count; i++)
        {
            pick -= b.Weight[i];
            if (pick < 0) { code = b.Codes[i]; return true; }
        }
        code = b.Codes[^1];
        return true;
    }

    private static uint Mix(uint roll, uint k)
    {
        unchecked
        {
            uint n = roll + k * 2654435761u;
            n = (n ^ (n >> 15)) * 2246822519u;
            n = (n ^ (n >> 13)) * 3266489917u;
            return n ^ (n >> 16);
        }
    }

    /// <summary>
    /// Das Hangbyte aus den ACHT Nachbarn, nach der gemessenen Regel (bug-769):
    /// Geraden wie <see cref="MapTerrain.HangByte"/> (1 O, 2 S, 3 W, 4 N, 5 O+S,
    /// 6 S+W, 7 W+N, 8 O+N); ist keine Gerade hoeher, die Diagonalen: 9 SO,
    /// 10 SW, 11 NW, 12 NO, 13 SO+NW, 14 SW+NO. Alles andere (zwei
    /// gegenueberliegende Geraden, drei Geraden, zwei benachbarte oder drei
    /// Diagonalen ohne Gerade) ist nicht darstellbar: −1.
    /// </summary>
    public static int HangByte8(bool e, bool se, bool s, bool sw, bool wst, bool nw, bool n, bool ne)
    {
        int o = MapTerrain.HangByte(e, s, wst, n);
        if (o != 0) return o;
        if (e || s || wst || n) return -1;                // zwei gegenueber / drei
        int d = (se ? 1 : 0) + (sw ? 1 : 0) + (nw ? 1 : 0) + (ne ? 1 : 0);
        if (d == 0) return 0;
        if (d == 1) return se ? 9 : sw ? 10 : nw ? 11 : 12;
        if (d == 2 && se && nw) return 13;
        if (d == 2 && sw && ne) return 14;
        return -1;
    }

    // ---- Messen --------------------------------------------------------------

    /// <summary>Passquote der Eckstoesse eines Rasters: je Nachbarpaar zweier
    /// BEKANNTER Bodenkacheln die zwei beruehrenden Ecken. <paramref name="unknown"/>
    /// zaehlt Paare mit einer Kachel, die das Modell nicht kennt.</summary>
    public (long Ok, long Bad, long Unknown) Measure(int w, int h, int[] code)
    {
        long ok = 0, bad = 0, unk = 0;
        for (int r = 0; r < h; r++)
            for (int c = 0; c < w; c++)
            {
                int a = code[r * w + c];
                if (!IsGround(a)) continue;
                for (int dir = 0; dir < 2; dir++)
                {
                    int nc = c + (dir == 0 ? 1 : 0), nr = r + (dir == 1 ? 1 : 0);
                    if (nc >= w || nr >= h) continue;
                    int b = code[nr * w + nc];
                    if (!IsGround(b)) continue;
                    if (!_corners.TryGetValue(a, out var p) || !_corners.TryGetValue(b, out var q)) { unk++; continue; }
                    int a1 = dir == 0 ? p[1] : p[2], b1 = q[0];
                    int a2 = p[3], b2 = dir == 0 ? q[2] : q[1];
                    if (a1 == b1 && a1 >= 0) ok++; else bad++;
                    if (a2 == b2 && a2 >= 0) ok++; else bad++;
                }
            }
        return (ok, bad, unk);
    }

    public string Describe()
    {
        var sb = new StringBuilder();
        sb.Append($"Eckmodell Kachelsatz {Tileset:00} GELERNT aus [{string.Join(" ", Sources)}], ");
        sb.Append($"{GroundCells} Bodenzellen, {Labels} Stoffe ({ChainWhy}); ");
        sb.Append($"Passquote der Eckstoesse in den Originalen {100.0 * PassOk / Math.Max(1, PassOk + PassBad):0.000} %; ");
        sb.Append($"{_table.Count} Faecher (Ecken x Hangbyte); Stoffanteile ");
        for (int s = 0; s < Labels; s++) sb.Append($"{Name(s)} {100 * LabelShare[s]:0.0}% ");
        sb.Append($"; Objekte {100 * ObjectShare:0.0} % der Landzellen, Hang rau {100 * SlopeRoughShare:0.0} %");
        sb.Append($"; sec2 je Stoff [{string.Join(" ", Array.ConvertAll(SecOfLabel, v => v.ToString()))}], " +
                  $"Knoten passend {100 * SecAgreement:0.00} %");
        return sb.ToString();
    }
}
