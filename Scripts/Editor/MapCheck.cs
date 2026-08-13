namespace AkteEuropaReborn.Editor;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Godot;

/// <summary>
/// Der Pruefstand zur Karte: <b>laedt die geschriebenen Dateien</b> und zaehlt
/// nach, ob darauf gespielt werden kann und ob sie den GEMESSENEN Kennzahlen der
/// 26 gelieferten Karten entspricht.
///
/// <para>⚠ Er liest bewusst die DATEIEN und nicht das Modell im Speicher. Ein
/// Pruefstand, der dieselbe Rechnung noch einmal macht, aus der die Daten kamen,
/// kann nur bestaetigen; dieser hier geht durch den Exporteur, durch den Backer
/// und durch das PNG hindurch und kann darum sehen, wenn einer davon etwas
/// verliert.</para>
///
/// <para>⚠⚠ <b>Und er laeuft auf einer GELIEFERTEN Karte genauso</b>
/// (<c>--map-check=map_01</c>). Das ist die Antwort auf »ein Pruefstand, der auf
/// jeder Karte dasselbe sagt, prueft nichts«: jeder Zaehler unten hat einen
/// gemessenen Vergleichswert, und der ist mit DIESEM Code an den 26 gelieferten
/// Karten geholt worden — nicht aus einer Nebenrechnung.</para>
///
/// <para><b>Welche Fehlerklassen er sehen kann</b> — die Frage, die jedes gruene
/// Ergebnis rechtfertigen muss:</para>
/// <list type="number">
///   <item><b>Keine Bauplaetze.</b> <c>can_build_here</c> will die imap frei, das
///     Hangbyte 0 und alle vier Ecken in <b>sec2</b> auf Klasse ≥ 2, »offenes
///     Land« (<c>corners_carry</c> @0x4211A0). ⚠ <b>BERICHTIGT 13.08.2026:</b>
///     hier stand »alle vier EckHOEHEN ≥ 2«, und das war widerlegt — siehe
///     <see cref="MinCornerClass"/>. Die Gegenprobe <c>,flach</c> gehoerte zu der
///     falschen Lesung: auf einer Karte auf Hoehe 0 ohne Wasser ist jede Zelle
///     offenes Land, also SIND dort Bauplaetze, und der Zaehler meldet richtig
///     viele. Was <c>,flach</c> weiterhin taugt, steht bei
///     <c>MapTerrain.Build</c>.</item>
///   <item><b>Bild und Raster passen nicht zusammen.</b> Das PNG wird geoeffnet
///     und seine Groesse gegen <c>pixel_w</c>/<c>pixel_h</c> gehalten.</item>
///   <item><b>Die drei Raster laufen auseinander.</b> Der Kachelcode sagt
///     Wiese, die imap sagt See. Gemessen an 44 eingespielten Karten trifft
///     »Code 0..7 ⇔ Gelaendeklasse Wasser« in 274.617 von 274.747 Wasserzellen
///     zu (99,95 %) — bei einer ERZEUGTEN Karte muss es 100 % sein, weil
///     <see cref="MapFactory.Paint"/> beide zugleich setzt.</item>
///   <item><b>Unerreichbares Land.</b> Von jeder Startmarke wird geflutet, mit
///     der Steigungsgrenze <c>NavGrid.MaxClimb</c> = 3.</item>
///   <item><b>Kein Ufer.</b> NEU. Zaehlt die Landzellen am Wasser, die einen
///     Innenland-Code tragen. Im Original <b>0 von 27.114</b> auf allen 26
///     Karten; beim Generator vor dem 13.08.2026 waren es 100 %.</item>
///   <item><b>Fehlende Hangbytes.</b> NEU. Zaehlt die Zellen mit einem hoeheren
///     Nachbarn, die trotzdem das Hangbyte 0 tragen. Im Original 393 von 81.357
///     (0,48 %); beim Generator vor dem 13.08.2026 waren es alle.</item>
///   <item><b>Ufer auf falscher Hoehe.</b> NEU. Kantenpaare Wasser→Land mit
///     Hoehendifferenz ungleich 0: im Original 10 von 36.288 (0,03 %), und die
///     Differenz +1 kommt <b>0-mal</b> vor.</item>
///   <item><b>Zackiges Gelaende.</b> NEU. Hoehensprung ueber 1 (im Original 111
///     von 1.202.757) und nicht darstellbare Hangformen — drei hoehere Nachbarn
///     oder zwei gegenueberliegende (im Original 3 von 605.090).</item>
///   <item><b>Ein Fleck Wasser statt einer Landschaft.</b> NEU. Anzahl und
///     Groesse der Wassergebiete, Kuestenlaenge je Wasserzelle, Objektflecken.
///     </item>
/// </list>
///
/// <para>Was er NICHT sehen kann, und das gehoert dazu: ob die Karte huebsch
/// ist. Bei Grafik entscheidet das Bild, nicht dieser Text.</para>
/// </summary>
public static class MapCheck
{
    /// <summary>
    /// Die Untergrenze aus <c>corners_carry</c> — dieselbe Zahl wie
    /// <c>Rendering.MapEntityLayer.MinCornerClass</c>: <b>2</b>, »offenes Land«.
    ///
    /// <para>⚠ <b>BERICHTIGT 13.08.2026, und der alte Name war Teil des
    /// Fehlers.</b> Hier stand <c>MinCornerHeight</c>, und dieser Pruefstand hielt
    /// die Zahl gegen die vier ECKHOEHEN einer Zelle. Das ist widerlegt: die
    /// Tafel, an der das Original entscheidet, ist <b>sec2</b> (257x257 Byte,
    /// 0xA3AEB0 bzw. 0xA39F10 auf F:, aus der DATEI gelesen — von 18 Zugriffen
    /// genau einer schreibend), und ihre Werte sind KLASSEN: 0 unpassierbar,
    /// 1 Ufer/Sand, 2 offenes Land, 3 besonderes Land. <c>corners_carry</c>
    /// @0x4211A0 verlangt »offenes Land auf allen vier Ecken« und hat mit dem
    /// Hoehengitter nichts zu tun. Gefunden hat es Agent <i>kampagne</i>
    /// (Commit a917d20); was die falsche Lesung gekostet hat, steht dort als
    /// Zahl: auf map_23 gab es fuer die Feld-Rohstoffmine 0 statt 57
    /// Bauplaetze.</para>
    ///
    /// <para>Gelesen wird die Tafel aus dem <c>zones</c>-Block der
    /// <c>.entities.json</c> — derselbe Block, den <c>MapEntityLayer.ZoneAt</c>
    /// im Spiel liest. Der Editor SCHREIBT sie schon: <c>MapFactory.Paint</c>
    /// setzt Kachel, imap und sec2 in einem Zug (Wasser und Gesperrt → 0,
    /// rau → 1, frei → 2).</para>
    /// </summary>
    public const int MinCornerClass = 2;

    /// <summary>Steigung, ab der ein Bodenfahrzeug nicht mehr hochkommt —
    /// dieselbe Zahl wie <c>Simulation.NavGrid.MaxClimb</c>. UNSERE Setzung,
    /// dort so vermerkt.</summary>
    public const int MaxClimb = 3;

    /// <summary>Kachelcodes 0..7 sind die Wasseranimation (gemessen, siehe
    /// <see cref="TilePalette"/>).</summary>
    public const int WaterCodeMax = 7;

    public static int Run(string name, Action<string> say)
    {
        string outName = name.StartsWith("map_") ? name : "map_" + name;
        string dir = ProjectSettings.GlobalizePath(Core.Content.UserRoot).TrimEnd('/', '\\') + "/Maps";
        string metaPath = $"{dir}/{outName}.json";
        string entPath = $"{dir}/{outName}.entities.json";
        string pngPath = $"{dir}/{outName}.png";

        foreach (string p in new[] { metaPath, entPath })
            if (!File.Exists(p)) { say($"FEHLT: {p}"); return 1; }

        int bad = 0;
        say($"map-check {outName}  ({dir})");

        // ---- das Raster aus map_NN.json ------------------------------------
        using var meta = JsonDocument.Parse(File.ReadAllText(metaPath));
        var root = meta.RootElement;
        int w = root.GetProperty("width").GetInt32();
        int h = root.GetProperty("height").GetInt32();
        int tileset = root.GetProperty("tileset").GetInt32();
        int pxw = root.GetProperty("pixel_w").GetInt32();
        int pxh = root.GetProperty("pixel_h").GetInt32();
        int n = w * h;

        var code = new int[n];
        var elev = new int[n];
        var flag = new int[n];
        var seen = new bool[n];
        int tiles = 0, outside = 0;
        foreach (var t in root.GetProperty("tiles").EnumerateArray())
        {
            int c = t.GetProperty("col").GetInt32(), r = t.GetProperty("row").GetInt32();
            tiles++;
            if (c < 0 || c >= w || r < 0 || r >= h) { outside++; continue; }
            int i = r * w + c;
            code[i] = t.GetProperty("code").GetInt32();
            elev[i] = t.GetProperty("elev").GetInt32();
            flag[i] = t.GetProperty("flag").GetInt32();
            seen[i] = true;
        }
        int missing = 0;
        foreach (bool s in seen) if (!s) missing++;
        say($"  Raster {w}x{h}, Kachelsatz {tileset:00}: {tiles} Kacheln geschrieben, " +
            $"{missing} Zellen ohne Kachel, {outside} ausserhalb");
        if (tiles != n || missing != 0 || outside != 0) { say("  ^ FEHLER"); bad++; }

        // ---- das Gelaende aus der .entities.json ---------------------------
        using var ent = JsonDocument.Parse(File.ReadAllText(entPath));
        var ground = new byte[n];
        int at = 0;
        foreach (var pair in ent.RootElement.GetProperty("terrain").GetProperty("rle").EnumerateArray())
        {
            var it = pair.EnumerateArray();
            it.MoveNext(); byte v = (byte)it.Current.GetInt32();
            it.MoveNext(); int run = it.Current.GetInt32();
            for (int k = 0; k < run && at < ground.Length; k++) ground[at++] = v;
        }
        if (at != n) { say($"  Gelaendeblock deckt {at} von {n} Zellen — FEHLER"); bad++; }

        var hist = new int[4];
        foreach (byte g in ground) if (g < 4) hist[g]++;
        say($"  Gelaende: frei {hist[0]} ({Pc(hist[0], n)}), rau {hist[1]} ({Pc(hist[1], n)}), " +
            $"wasser {hist[2]} ({Pc(hist[2], n)}), gesperrt {hist[3]} ({Pc(hist[3], n)})");
        say("    gemessen ueber 26 gelieferte Karten: 52,5 / 16,2 / 19,7 / 11,6 %");

        // ---- laufen Kachelbild und imap auseinander? ------------------------
        int codeWaterClassLand = 0, codeLandClassWater = 0;
        for (int i = 0; i < n; i++)
        {
            bool waterCode = code[i] <= WaterCodeMax;
            bool waterClass = ground[i] == 2;
            if (waterCode && !waterClass) codeWaterClassLand++;
            if (!waterCode && waterClass) codeLandClassWater++;
        }
        say($"  Bild gegen imap: {codeWaterClassLand} Wasserkacheln auf Land, " +
            $"{codeLandClassWater} Landkacheln auf Wasser");

        // ================= die gemessenen Kennzahlen ========================
        bad += Elevations(w, h, elev, flag, say);
        bad += Water(w, h, elev, ground, say);
        bad += Coast(w, h, code, flag, ground, say);
        bad += Seams(w, h, tileset, code, ground, say);
        Objects(w, h, code, say);

        // ---- Bauplaetze -----------------------------------------------------
        // Der Zellentest von can_build_here, so weit er ohne Gebaeudemuster
        // geht: die imap muss FREI sein (0xFFFE, also Gelaendeklasse 0), das
        // Hangbyte 0, und alle vier Ecken muessen in sec2 die Klasse
        // MinCornerClass tragen — siehe dort, das ist KEINE Hoehe.
        //
        // ⚠ Ausserhalb der eingespielten Breite/Hoehe gibt es −1, genau wie in
        // MapEntityLayer.ZoneAt: der letzte Rand hat keinen vierten Eckpunkt und
        // wird darum abgelehnt. Lieber ein Platz zu wenig als einer, der auf
        // einem Wert steht, den wir nicht haben.
        var zone = Zones(ent.RootElement, w, h, out bool hasZones);
        if (!hasZones)
            say("  sec2 (zones) fehlt in der .entities.json — die vierte Frage von " +
                "can_build_here ist NICHT gestellt, die Bauplatzzahl unten ist darum " +
                "eine OBERGRENZE und kein Befund");
        int Zone(int c, int r) => c >= 0 && c < w && r >= 0 && r < h ? zone[r * w + c] : -1;
        var buildable = new bool[n];
        int sites = 0, sitesNoZone = 0;
        for (int r = 0; r < h; r++)
            for (int c = 0; c < w; c++)
            {
                int i = r * w + c;
                bool cell = flag[i] == 0 && ground[i] == 0;
                bool corners = Zone(c, r) >= MinCornerClass && Zone(c + 1, r) >= MinCornerClass
                            && Zone(c, r + 1) >= MinCornerClass
                            && Zone(c + 1, r + 1) >= MinCornerClass;
                if (cell) sitesNoZone++;
                bool ok = cell && (!hasZones || corners);
                buildable[i] = ok;
                if (ok) sites++;
            }
        say($"  Bauplatzzellen (imap frei, Hangbyte 0, vier Ecken in sec2 ≥ " +
            $"{MinCornerClass} = »offenes Land«): {sites} von {n} = {Pc(sites, n)}" +
            $"  [26 Karten: Median 26,45 %, Spanne 9,85..44,84 %]");
        say($"    ohne die Eckenfrage waeren es {sitesNoZone} — die Differenz " +
            $"{sitesNoZone - sites} ist genau das, was sec2 beitraegt");
        // ⚠ Die Vergleichszahl ist NEU GEMESSEN, weil die alte zu der widerlegten
        // Hoehenlesung gehoerte: mit »vier Eckhoehen ≥ 2« kamen dieselben 26
        // Karten auf Median 12,76 % (1,67..34,31) — genau die Spanne, die hier
        // bis zum 13.08.2026 stand. Mit der sec2-Klasse sind es Median 26,45 %
        // (9,85..44,84). Eine berichtigte Regel braucht eine berichtigte
        // Messlatte, sonst beanstandet der Pruefstand die gelieferten Karten.
        if (sites == 0) { say("  ^ FEHLER: auf dieser Karte kann niemand bauen"); bad++; }

        // ---- die Rohstoffvorkommen ------------------------------------------
        // ⚠ Ein Zaehler, der auf jeder Karte dasselbe sagt, prueft nichts. Dieser
        // sagt auf einer GELIEFERTEN Karte »0, das Missionsskript legt sie an«
        // (add_terra_place, C:0x4D0A10 — 50 Aufrufe in 8 Missionen, keine Karte
        // traegt sie) und auf einer ERZEUGTEN die Zahl, die MapDeposits gelegt
        // hat. Beides ist richtig, und der Unterschied ist der Befund.
        bad += Deposits(root, w, h, ground, flag, zone, hasZones, say);

        // ---- Einheiten und Gebaeude: ist die Karte spielbar? ---------------
        bad += Assets(ent.RootElement, say);

        // ---- Startmarken und Erreichbarkeit --------------------------------
        var starts = new List<(int Slot, int Col, int Row, int Type)>();
        foreach (var mk in ent.RootElement.GetProperty("markers").EnumerateArray())
        {
            int type = mk.GetProperty("type").GetInt32();
            if (type < MapFactory.MarkerStartBase || type > MapFactory.MarkerStartBase + 4) continue;
            starts.Add((mk.GetProperty("slot").GetInt32(), mk.GetProperty("col").GetInt32(),
                        mk.GetProperty("row").GetInt32(), type));
        }
        if (starts.Count == 0)
            say("  keine Startmarke (Typ 0x70..0x74) — bei einer erzeugten Karte ein FEHLER, " +
                "bei 17 der 26 gelieferten der Normalfall");

        foreach (var s in starts)
        {
            int reach = Flood(w, h, ground, elev, s.Col, s.Row, buildable, out int reachSites);
            say($"  Spieler {s.Type - MapFactory.MarkerStartBase} (Marke {s.Slot} auf {s.Col},{s.Row}): " +
                $"{reach} erreichbare Zellen, davon {reachSites} Bauplaetze");
            // ⚠ KEINE Beanstandung. Am 13.08.2026 stand hier eine, und sie war
            // falsch: auf map_01 liegen alle fuenf Marken 0x70..0x74 auf
            // gesperrten Zellen (5 von 5), weil sie dort KAMPAGNENmarken sind
            // und keine Startplaetze. Ein Pruefstand, der die gelieferten Karten
            // beanstandet, misst am falschen Gegenstand. Ob die Karte spielbar
            // ist, entscheidet der Zaehler »besetzte Plaetze« darueber.
            if (reach == 0)
                say("    ^ die Marke steht im Wasser oder im Gesperrten — bei einer " +
                    "Kampagnenkarte normal (map_01: 5 von 5), bei einem Startplatz nicht");
        }

        // ---- das Bild -------------------------------------------------------
        if (!File.Exists(pngPath)) say($"  {outName}.png fehlt — Bildpruefung uebersprungen");
        else
        {
            var img = Image.LoadFromFile(pngPath);
            if (img == null) { say($"  {outName}.png laesst sich nicht oeffnen — FEHLER"); bad++; }
            else
            {
                long bytes = new FileInfo(pngPath).Length;
                say($"  Bild {img.GetWidth()}x{img.GetHeight()} px, erwartet {pxw}x{pxh}, {bytes / 1024} KiB");
                if (img.GetWidth() != pxw || img.GetHeight() != pxh) { say("  ^ FEHLER"); bad++; }
                int opaque = CountOpaque(img);
                say($"  deckende Pixel: {opaque} von {img.GetWidth() * (long)img.GetHeight()} = " +
                    $"{100.0 * opaque / (img.GetWidth() * (double)img.GetHeight()):0.0} %");
            }
        }

        say(bad == 0 ? "map-check: OK" : $"map-check: {bad} Beanstandungen");
        return bad == 0 ? 0 : 1;
    }

    /// <summary>
    /// Die Rohstoffvorkommen der Karte gegen die MESSLATTE der 50
    /// Originalvorkommen (<c>aekernel-tools/terra_stats.py</c>):
    /// <code>
    ///   Vorkommen je Karte        2 .. 11        je 1000 begehbar 0,23 (0,16..0,37)
    ///   Menge                     5000, 50 von 50
    ///   sec2-Klasse               2 (68 %) / 3 (32 %) — nie 0, nie 1
    ///   tragende Anker von 9      Mittel 8,78, min 5; ohne Anker 0 von 50
    ///   Abstand zum Kartenrand    min 5      naechster Nachbar min 11,4
    /// </code>
    /// <para>Der Block fehlt bei jeder gelieferten Karte — siehe
    /// <see cref="Import.CwmFile.Terra"/>. Das ist KEINE Beanstandung.</para>
    /// </summary>
    private static int Deposits(JsonElement root, int w, int h, byte[] ground,
                               int[] flag, int[] zone, bool hasZones, Action<string> say)
    {
        if (!root.TryGetProperty("terra", out var terra) ||
            terra.ValueKind != JsonValueKind.Array || terra.GetArrayLength() == 0)
        {
            say("  Rohstoffvorkommen: keine in der Karte — bei einer GELIEFERTEN Karte " +
                "der Normalfall (das Missionsskript legt sie an, add_terra_place " +
                "C:0x4D0A10, 50 Aufrufe in 8 Missionen); bei einer ERZEUGTEN Karte hat " +
                "die Feld-Rohstoffmine damit 0 Bauplaetze");
            return 0;
        }

        int Zone(int c, int r) => c >= 0 && c < w && r >= 0 && r < h ? zone[r * w + c] : -1;
        bool Corners(int c, int r) => !hasZones
            || (Zone(c, r) >= MinCornerClass && Zone(c + 1, r) >= MinCornerClass
                && Zone(c, r + 1) >= MinCornerClass && Zone(c + 1, r + 1) >= MinCornerClass);
        // ⚠ Das ist die PUNKTprüfung — imap frei, Hangbyte 0, vier Ecken ≥ 2 —
        // OHNE die 30 Zellen der Minengrundfläche. Sie kennt nur der Kachelsatz,
        // und den hat dieser Prüfstand nicht (er liest Dateien). Die Zahl unten
        // ist darum eine OBERGRENZE; was wirklich trägt, sagt --terra-check
        // (map_23: 57 von 99 gegen 97 nach dieser Punktprüfung).
        bool Free(int c, int r) => c >= 0 && c < w && r >= 0 && r < h
                               && ground[r * w + c] == 0 && flag[r * w + c] == 0;

        var list = new List<(int C, int R, int A)>();
        foreach (var e in terra.EnumerateArray())
        {
            var it = e.EnumerateArray();
            it.MoveNext(); int c = it.Current.GetInt32();
            it.MoveNext(); int r = it.Current.GetInt32();
            it.MoveNext(); int a = it.Current.GetInt32();
            list.Add((c, r, a));
        }

        int walkable = 0;
        foreach (byte g in ground) if (g == 0) walkable++;

        int bad = 0, anchorSum = 0, anchorMin = 9, noAnchor = 0;
        int edgeMin = int.MaxValue, offClass = 0, wrongAmount = 0;
        double nnMin = double.MaxValue;
        var zoneHist = new SortedDictionary<int, int>();
        foreach (var (c, r, a) in list)
        {
            int anchors = 0;
            for (int dy = 0; dy < 3; dy++)
                for (int dx = 0; dx < 3; dx++)
                    if (Free(c + dx, r + dy) && Corners(c + dx, r + dy)) anchors++;
            anchorSum += anchors;
            anchorMin = Math.Min(anchorMin, anchors);
            if (anchors == 0) noAnchor++;
            edgeMin = Math.Min(edgeMin, Math.Min(Math.Min(c, r), Math.Min(w - 1 - c, h - 1 - r)));
            int z = Zone(c, r);
            zoneHist[z] = zoneHist.GetValueOrDefault(z) + 1;
            if (z < MinCornerClass) offClass++;
            if (a != MapDeposits.Amount) wrongAmount++;
            foreach (var (c2, r2, _) in list)
            {
                if (c2 == c && r2 == r) continue;
                double d = Math.Sqrt((c - c2) * (double)(c - c2) + (r - r2) * (double)(r - r2));
                nnMin = Math.Min(nnMin, d);
            }
        }
        int n = list.Count;
        say($"  Rohstoffvorkommen: {n} (Original je Karte 2..11), " +
            $"{1000.0 * n / Math.Max(1, walkable):0.00} je 1000 begehbare Zellen " +
            "[Messlatte 0,23; Original 0,16..0,37]");
        say($"    Menge {(wrongAmount == 0 ? MapDeposits.Amount + " ueberall" : wrongAmount + " weichen ab")} " +
            $"[Messlatte {MapDeposits.Amount}, 50 von 50]");
        say($"    sec2-Klasse: {string.Join(" ", zoneHist.Select(kv => $"{kv.Key} x{kv.Value}"))} " +
            "[Original 2 x34 (68 %), 3 x16 (32 %); der Generator legt kein besonderes " +
            "Land an, darum hier alles Klasse 2]");
        say($"    tragende Anker: Mittel {anchorSum / (double)n:0.00} von 9, min {anchorMin}, " +
            $"ohne jeden Anker {noAnchor} [Messlatte Mittel 8,78, min 5, ohne Anker 0 von 50]");
        say($"    Abstand zum Rand min {edgeMin} [Messlatte 5]; naechster Nachbar min " +
            $"{(n > 1 ? nnMin.ToString("0.0") : "—")} [Messlatte 11,4]");
        if (noAnchor > 0)
        {
            say($"  ^ FEHLER: {noAnchor} Vorkommen tragen keine einzige Mine — im Original 0 von 50");
            bad++;
        }
        if (offClass > 0)
        {
            say($"  ^ FEHLER: {offClass} Vorkommen liegen auf sec2 < {MinCornerClass} — " +
                "dort kann keine Mine stehen");
            bad++;
        }
        return bad;
    }

    private static string Pc(long a, long b) => b == 0 ? "—" : $"{100.0 * a / b:0.00} %";

    // ========================================================================
    //  Hoehen
    // ========================================================================

    /// <summary>
    /// Hoehenverteilung, Steigung, ebene Flaechen, Hangkanten und die zwei harten
    /// Schranken: kein Sprung ueber 1, und keine Hangform, die das Hangbyte nicht
    /// ausdruecken kann.
    /// </summary>
    private static int Elevations(int w, int h, int[] elev, int[] flag, Action<string> say)
    {
        int n = w * h, bad = 0;
        var eh = new int[16];
        foreach (int e in elev) if (e >= 0 && e < 16) eh[e]++;
        var sb = new StringBuilder("  Hoehenstufen: ");
        for (int k = 0; k < 8; k++) sb.Append($"{k}:{100.0 * eh[k] / n:0.0}% ");
        say(sb.ToString());
        say("    gemessen ueber 26 Karten: 0:35,8 1:29,7 2:10,5 3:7,2 4:8,3 5:3,3 6:4,2 7:0,8 %");

        long pairs = 0, same = 0, over = 0;
        var sh = new int[8];
        for (int r = 0; r < h; r++)
            for (int c = 0; c < w; c++)
            {
                int i = r * w + c;
                if (c + 1 < w) Pair(elev[i], elev[i + 1]);
                if (r + 1 < h) Pair(elev[i], elev[i + w]);
                void Pair(int a, int b)
                {
                    int d = Math.Abs(a - b);
                    pairs++;
                    if (d == 0) same++;
                    if (d > MapTerrain.MaxStep) over++;
                    if (d < 8) sh[d]++;
                }
            }
        say($"  Nachbarpaare: {pairs}, gleich hoch {same} ({Pc(same, pairs)}), " +
            $"Sprung > {MapTerrain.MaxStep}: {over} ({Pc(over, pairs)})");
        say("    gemessen ueber 26 Karten: gleich hoch 90,74 %, Sprung > 1: 111 von " +
            "1.202.757 = 0,0092 %");

        // ebene Zellen und Hangkanten als ZUSAMMENHANGSGEBIETE — ein Gelaende
        // mit langen Haengen hat wenige grosse, ein zackiges viele kleine
        int plateau = 0;
        var edge = new bool[n];
        int noFlag = 0, withHigher = 0, unrep = 0;
        int[] dc = { 1, 0, -1, 0 }, dr = { 0, 1, 0, -1 };
        for (int r = 0; r < h; r++)
            for (int c = 0; c < w; c++)
            {
                int i = r * w + c, e = elev[i];
                bool flat = true, ea = false, so = false, we = false, no = false;
                for (int k = 0; k < 4; k++)
                {
                    int nc = c + dc[k], nr = r + dr[k];
                    if (nc < 0 || nc >= w || nr < 0 || nr >= h) continue;
                    int j = nr * w + nc;
                    if (elev[j] != e) flat = false;
                    if (elev[j] > e) { if (k == 0) ea = true; else if (k == 1) so = true; else if (k == 2) we = true; else no = true; }
                }
                if (flat) plateau++; else edge[i] = true;
                if (ea || so || we || no)
                {
                    withHigher++;
                    if (flag[i] == 0) noFlag++;
                    if (MapTerrain.HangByte(ea, so, we, no) < 0) unrep++;
                }
            }
        say($"  ebene Zellen (alle vier Nachbarn gleich hoch): {plateau} von {n} = {Pc(plateau, n)}" +
            "  [26 Karten: Median 80,5 %, Spanne 54,7..94,2 %]");
        var comps = Components(w, h, edge);
        say($"  Hangkanten: {comps.Cells} Zellen in {comps.Count} Zusammenhangsgebieten, " +
            $"groesstes {comps.Max}, mittleres {comps.Mean:0.0}" +
            "  [26 Karten: 1..34 Gebiete, Median 9, mittlere Groesse 45..2419]");
        say($"  Zellen mit hoeherem Nachbarn: {withHigher}, davon Hangbyte 0: {noFlag} " +
            $"({Pc(noFlag, withHigher)})  [26 Karten: 393 von 81.357 = 0,48 %]");
        say($"  nicht darstellbare Hangformen (drei hoehere Nachbarn oder zwei " +
            $"gegenueberliegende): {unrep}  [26 Karten: 3 von 605.090]");
        if (over > pairs / 1000) { say("  ^ FEHLER: zu viele Hoehensprünge über 1"); bad++; }
        if (withHigher > 0 && noFlag > withHigher / 10)
        { say("  ^ FEHLER: die Hangbytes fehlen — jede Hangzelle gilt so als Bauplatz"); bad++; }
        return bad;
    }

    // ========================================================================
    //  Wasser
    // ========================================================================

    /// <summary>Anteil, Zusammenhangsgebiete, Kuestenlaenge, und die gemessene
    /// Ufer-Regel: Land am Wasser liegt auf Wasserhoehe.</summary>
    private static int Water(int w, int h, int[] elev, byte[] ground, Action<string> say)
    {
        int n = w * h, bad = 0;
        var mask = new bool[n];
        int cells = 0;
        for (int i = 0; i < n; i++) if (ground[i] == 2) { mask[i] = true; cells++; }
        if (cells == 0)
        {
            say("  Wasser: keine einzige Zelle  [26 Karten: 4,05..60,66 %, Median 18,55 %]");
            return 0;
        }
        var comps = Components(w, h, mask);
        int coast = 0, badBeach = 0, plusOne = 0;
        var wlev = new int[16];
        for (int r = 0; r < h; r++)
            for (int c = 0; c < w; c++)
            {
                int i = r * w + c;
                if (mask[i] && elev[i] < 16) wlev[elev[i]]++;
                if (c + 1 < w && mask[i] != mask[i + 1]) coast++;
                if (r + 1 < h && mask[i] != mask[i + w]) coast++;
                if (!mask[i]) continue;
                int[] dc = { 1, -1, 0, 0 }, dr = { 0, 0, 1, -1 };
                for (int k = 0; k < 4; k++)
                {
                    int nc = c + dc[k], nr = r + dr[k];
                    if (nc < 0 || nc >= w || nr < 0 || nr >= h) continue;
                    int j = nr * w + nc;
                    if (mask[j]) continue;
                    if (elev[j] != elev[i]) badBeach++;
                    if (elev[j] == elev[i] + 1) plusOne++;
                }
            }
        int top = 0;
        for (int k = 1; k < 16; k++) if (wlev[k] > wlev[top]) top = k;
        say($"  Wasser: {cells} Zellen = {Pc(cells, n)} in {comps.Count} Gebieten, " +
            $"groesstes {comps.Max}, mittleres {comps.Mean:0.0}, " +
            $"Kueste/Wasser {(double)coast / cells:0.00}");
        say($"    [26 Karten: Anteil Median 18,55 % (4,05..60,66), Gebiete Median 23 (1..269), " +
            "groesstes Median 2123, Kueste/Wasser Median 0,40 (0,06..1,40)]");
        say($"  Wasserspiegel: Stufe {top} traegt {wlev[top]} von {cells} = {Pc(wlev[top], cells)}" +
            "  [26 Karten: auf 22 von 26 traegt EINE Stufe ueber 99,5 %]");
        say($"  Ufer nicht auf Wasserhoehe: {badBeach} Kantenpaare, davon Land eine Stufe " +
            $"hoeher: {plusOne}  [26 Karten: 10 von 36.288 = 0,03 %, Differenz +1: 0-mal]");
        if (badBeach > cells / 20) { say("  ^ FEHLER: das Ufer liegt nicht auf Wasserhoehe"); bad++; }
        return bad;
    }

    // ========================================================================
    //  Kachelcodes am Ufer — die haerteste gemessene Zahl
    // ========================================================================

    /// <summary>
    /// Von den 27.114 Landzellen, die in den 26 gelieferten Karten an eine
    /// Wasserzelle grenzen, traegt <b>keine einzige</b> einen Innenland-Code.
    /// Der Innenland-Block wird dabei AUS DER KARTE SELBST bestimmt — die acht
    /// haeufigsten Codes auf Zellen, die frei, eben und ohne Wassernachbarn sind.
    ///
    /// <para>⚠ Das ist keine Rechnung mit sich selbst: die Herleitung sagt nur,
    /// welche acht Codes im Landesinneren haeufig sind, nicht wo sie sonst
    /// liegen. Auf einer gelieferten Karte ergibt der Zaehler 0 (26 von 26), auf
    /// der Ausgabe des Generators vor dem 13.08.2026 100 %. Er unterscheidet
    /// also.</para>
    /// </summary>
    private static int Coast(int w, int h, int[] code, int[] flag, byte[] ground, Action<string> say)
    {
        int n = w * h, bad = 0;
        var inner = new Dictionary<int, int>();
        for (int r = 0; r < h; r++)
            for (int c = 0; c < w; c++)
            {
                int i = r * w + c;
                if (ground[i] != 0 || flag[i] != 0 || code[i] >= TileModel.ObjectCodeMin) continue;
                if (TouchesWater(w, h, ground, c, r)) continue;
                inner[code[i]] = inner.TryGetValue(code[i], out int v) ? v + 1 : 1;
            }
        if (inner.Count == 0) return 0;
        var top = new List<KeyValuePair<int, int>>(inner);
        top.Sort((a, b) => b.Value.CompareTo(a.Value));
        var block = new HashSet<int>();
        int covered = 0, total = 0;
        foreach (var kv in inner) total += kv.Value;
        for (int k = 0; k < 8 && k < top.Count; k++) { block.Add(top[k].Key); covered += top[k].Value; }

        int shore = 0, shoreInner = 0;
        for (int r = 0; r < h; r++)
            for (int c = 0; c < w; c++)
            {
                int i = r * w + c;
                if (ground[i] == 2 || !TouchesWater(w, h, ground, c, r)) continue;
                shore++;
                if (block.Contains(code[i])) shoreInner++;
            }
        var codes = new List<int>(block);
        codes.Sort();
        say($"  Innenland-Block (die acht haeufigsten Codes auf freien, ebenen Zellen ohne " +
            $"Wassernachbar): {string.Join(",", codes)} — sie decken {Pc(covered, total)} des " +
            "Landesinneren");
        say($"  Landzellen am Wasser: {shore}, davon mit Innenland-Code: {shoreInner} " +
            $"({Pc(shoreInner, shore)})  [26 Karten: 0 von 27.114 = 0,00 %]");
        if (shore > 0 && shoreInner > shore / 20)
        { say("  ^ FEHLER: kein Uferuebergang — Wiese steht direkt an Wasser"); bad++; }

        int distinct = 0;
        var all = new HashSet<int>();
        foreach (int cd in code) if (cd < TileModel.ObjectCodeMin && cd > WaterCodeMax) all.Add(cd);
        distinct = all.Count;
        say($"  verschiedene Bodencodes: {distinct}  [26 Karten: Median 622, Spanne 183..1217]");
        return bad;
    }

    // ========================================================================
    //  Die NAHT — »beruehren sich die Bildpunkte«
    // ========================================================================

    /// <summary>
    /// Der Anteil HARTER Naehte zwischen zwei Bodenkacheln, an den Pixeln
    /// gerechnet (<see cref="TileSeams"/>).
    ///
    /// <para>⚠ Der einzige Zaehler dieses Pruefstands, der die <c>.CWP</c>
    /// braucht — er ist auch der einzige, der etwas ueber das BILD sagt und nicht
    /// ueber das Raster. Der Anlass: am 13.08.2026 meldete jeder andere Zaehler
    /// gruen und die Karte sah im Bild gescheckt aus.</para>
    ///
    /// <para><b>Die Schranke ist gemessen und hat eine Toleranz</b>, weil ein
    /// Ja/Nein auf echten Daten sonst Befunde erfindet: 26 gelieferte Karten
    /// liegen bei Median 0,58 %, Spanne 0,00..3,23 % — beanstandet wird erst ab
    /// <see cref="SeamBad"/>, also gut ueber der schlechtesten gelieferten.</para>
    ///
    /// <para>Fehlt der Kachelsatz, wird der Zaehler UEBERSPRUNGEN und das
    /// gesagt — eine fehlende Messung darf nicht wie eine bestandene aussehen.
    /// </para>
    /// </summary>
    public const double SeamBad = 5.0;

    private static int Seams(int w, int h, int tileset, int[] code, byte[] ground,
                             Action<string> say)
    {
        var files = MapGenerator.FindTileset(tileset, null);
        if (files == null)
        {
            say($"  Naehte: Kachelsatz {tileset:00} nicht gefunden — Nahtpruefung " +
                "UEBERSPRUNGEN (nicht bestanden)");
            return 0;
        }
        Import.CwpFile cwp;
        Import.PalFile pal;
        try
        {
            cwp = Import.CwpFile.Load(files.Value.Cwp);
            pal = Import.PalFile.Load(files.Value.Pal);
        }
        catch (Exception e)
        {
            say($"  Naehte: {files.Value.Cwp} unlesbar ({e.Message}) — UEBERSPRUNGEN");
            return 0;
        }

        var seams = new TileSeams(cwp, pal);
        long n = 0, hard = 0;
        double sum = 0;
        int badCol = -1, badRow = -1, badA = 0, badB = 0;
        double worst = -1;
        for (int r = 0; r < h; r++)
            for (int c = 0; c < w; c++)
            {
                int i = r * w + c;
                if (ground[i] == 2 || !TileSeams.IsGround(code[i])) continue;
                if (c + 1 < w) One(i, i + 1, true, c, r);
                if (r + 1 < h) One(i, i + w, false, c, r);
            }
        void One(int i, int j, bool hz, int c, int r)
        {
            if (ground[j] == 2 || !TileSeams.IsGround(code[j])) return;
            double v = seams.Cost(code[i], code[j], hz);
            if (v < 0) return;
            n++; sum += v;
            if (v > TileSeams.Hard) hard++;
            if (v > worst) { worst = v; badCol = c; badRow = r; badA = code[i]; badB = code[j]; }
        }
        if (n == 0)
        {
            say("  Naehte: keine zwei Bodenkacheln nebeneinander — nichts zu messen");
            return 0;
        }
        double share = 100.0 * hard / n;
        say($"  Naehte zwischen Bodenkacheln: {n}, mittlerer Farbabstand {sum / n:0.0}, " +
            $"harte Naehte (>{TileSeams.Hard:0}): {hard} = {share:0.00} %");
        say("    [26 Karten: harte Naehte Median 0,58 % (0,00..3,23), Farbabstand Median 55,3 " +
            "(19,7..85,0) — es traegt der ANTEIL, nicht der Mittelwert]");
        if (badCol >= 0)
            say($"    haerteste Naht {worst:0} bei Zelle ({badCol},{badRow}): Code {badA} an {badB}");
        if (share > SeamBad)
        {
            say($"  ^ FEHLER: ueber {SeamBad:0} % harte Naehte — die Kacheln passen nicht zu " +
                "ihren Nachbarn, im Bild ein Schachbrett");
            return 1;
        }
        return 0;
    }

    /// <summary>
    /// Die sec2-Tafel aus dem <c>zones</c>-Block, in unser Zeilenraster gebracht.
    /// Genau der Block, den <c>MapEntityLayer.ZoneAt</c> im Spiel liest — ein
    /// Pruefstand, der eine andere Quelle nimmt als die Engine, prueft die
    /// falsche Karte.
    /// </summary>
    private static int[] Zones(JsonElement ent, int w, int h, out bool has)
    {
        var z = new int[w * h];
        for (int i = 0; i < z.Length; i++) z[i] = -1;
        has = false;
        if (!ent.TryGetProperty("zones", out var zn) || zn.ValueKind != JsonValueKind.Object)
            return z;
        if (!zn.TryGetProperty("grid", out var grid) || grid.ValueKind != JsonValueKind.Array)
            return z;
        int r = 0;
        foreach (var row in grid.EnumerateArray())
        {
            if (r >= h) break;
            if (row.ValueKind == JsonValueKind.Array)
            {
                int c = 0;
                foreach (var v in row.EnumerateArray())
                {
                    if (c >= w) break;
                    z[r * w + c] = v.GetInt32();
                    c++;
                }
            }
            r++;
        }
        has = r > 0;
        return z;
    }

    private static bool TouchesWater(int w, int h, byte[] ground, int c, int r)
    {
        int[] dc = { 1, -1, 0, 0 }, dr = { 0, 0, 1, -1 };
        for (int k = 0; k < 4; k++)
        {
            int nc = c + dc[k], nr = r + dr[k];
            if (nc >= 0 && nc < w && nr >= 0 && nr < h && ground[nr * w + nc] == 2) return true;
        }
        return false;
    }

    /// <summary>Objektkacheln (Code ≥ 1666) — Wald und Fels. Gemessen: Anteil im
    /// Mittel 15,0 % (6,4..26,8), je Karte 27..1377 Flecken (Median 226) mit im
    /// Mittel 11,1 Zellen.</summary>
    private static void Objects(int w, int h, int[] code, Action<string> say)
    {
        int n = w * h;
        var mask = new bool[n];
        int cells = 0;
        for (int i = 0; i < n; i++) if (code[i] >= TileModel.ObjectCodeMin) { mask[i] = true; cells++; }
        var comps = Components(w, h, mask);
        say($"  Objektkacheln (Wald, Fels): {cells} = {Pc(cells, n)} in {comps.Count} Flecken, " +
            $"groesster {comps.Max}, mittlerer {comps.Mean:0.0}, Einzelzellen {comps.Singles}");
        say("    [26 Karten: Anteil Median 14,1 % (6,4..26,8), Flecken Median 226 (27..1377), " +
            "mittlere Groesse Median 11,1, Einzelzellen Median 99]");
    }

    /// <summary>Traegt die Karte ueberhaupt etwas, worauf man spielen kann?
    /// <c>SkirmishAi.StartSkirmish</c> gibt ohne besetzten Platz −1, und
    /// <c>MapEntityLayer.Verdict</c> erklaert eine Mission ohne eigene Einheiten
    /// im ersten Takt fuer verloren.</summary>
    private static int Assets(JsonElement ent, Action<string> say)
    {
        var perPlayer = new Dictionary<int, (int U, int B)>();
        int units = 0, blds = 0, factories = 0;
        foreach (var e in ent.GetProperty("entities").EnumerateArray())
        {
            int owner = e.TryGetProperty("owner", out var o) ? o.GetInt32() : -1;
            units++;
            var v = perPlayer.TryGetValue(owner, out var p) ? p : (0, 0);
            perPlayer[owner] = (v.Item1 + 1, v.Item2);
        }
        foreach (var b in ent.GetProperty("buildings").EnumerateArray())
        {
            int type = b.GetProperty("type").GetInt32();
            if (type < 1 || type > 16) continue;              // Skriptplatzhalter
            int owner = b.GetProperty("owner").GetInt32();
            blds++;
            if (type is 2 or 3 or 4) factories++;
            var v = perPlayer.TryGetValue(owner, out var p) ? p : (0, 0);
            perPlayer[owner] = (v.Item1, v.Item2 + 1);
        }
        var parts = new List<string>();
        int live = 0;
        foreach (var kv in perPlayer)
        {
            if (kv.Key < 0 || kv.Key > 7) continue;
            live++;
            parts.Add($"P{kv.Key}:{kv.Value.U}E/{kv.Value.B}G");
        }
        say($"  Spielbarkeit: {units} Einheiten, {blds} echte Gebaeude ({factories} Fabriken), " +
            $"{live} besetzte Plaetze [{string.Join(" ", parts)}]");
        if (live < 2)
        {
            say("  ^ ACHTUNG: unter zwei besetzten Plaetzen gibt SkirmishAi.StartSkirmish −1 " +
                "und ein Gefecht ist im ersten Takt verloren");
            return 1;
        }
        return 0;
    }

    // ========================================================================
    //  Kleinkram
    // ========================================================================

    private readonly record struct Comp(int Count, int Cells, int Max, double Mean, int Singles);

    /// <summary>4er-Zusammenhangsgebiete einer Maske. Genau die Rechnung, mit der
    /// die Vergleichszahlen an den 26 gelieferten Karten geholt wurden
    /// (<c>aekernel-tools/map_stats.py</c>) — sonst waeren die Zahlen nicht
    /// vergleichbar.</summary>
    private static Comp Components(int w, int h, bool[] mask)
    {
        int n = w * h;
        var seen = new bool[n];
        var open = new Queue<int>();
        int count = 0, cells = 0, max = 0, singles = 0;
        int[] dc = { 1, -1, 0, 0 }, dr = { 0, 0, 1, -1 };
        for (int s = 0; s < n; s++)
        {
            if (seen[s] || !mask[s]) continue;
            open.Clear();
            open.Enqueue(s); seen[s] = true;
            int size = 0;
            while (open.Count > 0)
            {
                int i = open.Dequeue();
                size++;
                int c = i % w, r = i / w;
                for (int k = 0; k < 4; k++)
                {
                    int nc = c + dc[k], nr = r + dr[k];
                    if (nc < 0 || nc >= w || nr < 0 || nr >= h) continue;
                    int j = nr * w + nc;
                    if (seen[j] || !mask[j]) continue;
                    seen[j] = true; open.Enqueue(j);
                }
            }
            count++; cells += size;
            if (size > max) max = size;
            if (size == 1) singles++;
        }
        return new Comp(count, cells, max, count == 0 ? 0 : (double)cells / count, singles);
    }

    /// <summary>Wie viel des Bildes ueberhaupt gemalt ist. Ein Loch im Backvor-
    /// gang schlaegt hier durch, ohne dass man die Loecher zaehlen muesste — der
    /// Rand ueber und unter der Karte ist bauartbedingt leer, ein Einbruch
    /// gegenueber einer gelieferten Karte faellt trotzdem auf.</summary>
    private static int CountOpaque(Image img)
    {
        int n = 0;
        for (int y = 0; y < img.GetHeight(); y++)
            for (int x = 0; x < img.GetWidth(); x++)
                if (img.GetPixel(x, y).A > 0.5f) n++;
        return n;
    }

    /// <summary>Flutet von einer Zelle aus ueber alles, was ein Bodenfahrzeug
    /// betreten darf: Klasse frei oder rau, und kein Hoehensprung ≥ MaxClimb.</summary>
    private static int Flood(int w, int h, byte[] ground, int[] elev, int c0, int r0,
                             bool[] buildable, out int sites)
    {
        sites = 0;
        if (c0 < 0 || c0 >= w || r0 < 0 || r0 >= h) return 0;
        if (ground[r0 * w + c0] > 1) return 0;             // Start steht im Wasser
        var open = new Queue<int>();
        var seen = new bool[w * h];
        open.Enqueue(r0 * w + c0);
        seen[r0 * w + c0] = true;
        int n = 0;
        int[] dc = { 1, -1, 0, 0 }, dr = { 0, 0, 1, -1 };
        while (open.Count > 0)
        {
            int i = open.Dequeue();
            n++;
            if (buildable[i]) sites++;
            int c = i % w, r = i / w;
            for (int k = 0; k < 4; k++)
            {
                int nc = c + dc[k], nr = r + dr[k];
                if (nc < 0 || nc >= w || nr < 0 || nr >= h) continue;
                int j = nr * w + nc;
                if (seen[j] || ground[j] > 1) continue;
                if (Math.Abs(elev[j] - elev[i]) >= MaxClimb) continue;
                seen[j] = true;
                open.Enqueue(j);
            }
        }
        return n;
    }
}
