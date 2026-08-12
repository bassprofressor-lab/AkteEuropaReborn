namespace AkteEuropaReborn.Editor;

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using Godot;

/// <summary>
/// Der Pruefstand zur erzeugten Karte: <b>laedt die geschriebenen Dateien</b>
/// und zaehlt nach, ob darauf ueberhaupt gespielt werden kann.
///
/// <para>⚠ Er liest bewusst die DATEIEN und nicht das Modell im Speicher. Ein
/// Pruefstand, der dieselbe Rechnung noch einmal macht, aus der die Daten kamen,
/// kann nur bestaetigen; dieser hier geht durch den Exporteur, durch den Backer
/// und durch das PNG hindurch und kann darum sehen, wenn einer davon etwas
/// verliert.</para>
///
/// <para><b>Welche Fehlerklassen er sehen kann</b> — die Frage, die jedes gruene
/// Ergebnis rechtfertigen muss:</para>
/// <list type="number">
///   <item><b>Keine Bauplaetze.</b> Die Falle, an der eine flach erzeugte Karte
///     stirbt: <c>can_build_here</c> @0x4203C0 will Hangbyte 0 und alle vier
///     Eckhoehen ≥ 2 (@0x420360). Gegenprobe: <c>--map-new=…,flach</c> erzeugt
///     dieselbe Karte auf Hoehe 0, und dieser Zaehler muss dann 0 melden.</item>
///   <item><b>Bild und Raster passen nicht zusammen.</b> Das PNG wird geoeffnet
///     und seine Groesse gegen <c>pixel_w</c>/<c>pixel_h</c> gehalten.</item>
///   <item><b>Die drei Raster laufen auseinander.</b> Der Kachelcode sagt
///     Wiese, die imap sagt See. Gemessen an 44 eingespielten Karten trifft
///     »Code 0..7 ⇔ Gelaendeklasse Wasser« in 274.617 von 274.747 Wasserzellen
///     zu (99,95 %) — bei einer ERZEUGTEN Karte muss es 100 % sein, weil
///     <see cref="MapFactory.Paint"/> beide zugleich setzt. Jede Abweichung ist
///     ein Fehler im Schreibweg.</item>
///   <item><b>Unerreichbares Land.</b> Von jeder Startmarke wird geflutet, mit
///     der Steigungsgrenze <c>NavGrid.MaxClimb</c> = 3. Eine Karte, deren
///     Startplatz auf einer Insel im See liegt, faellt hier auf.</item>
/// </list>
///
/// <para>Was er NICHT sehen kann, und das gehoert dazu: ob die Karte huebsch
/// ist. Bei Grafik entscheidet das Bild, nicht dieser Text.</para>
/// </summary>
public static class MapCheck
{
    /// <summary>Die Untergrenze aus <c>can_build_here</c> — dieselbe Zahl wie
    /// <c>Rendering.MapEntityLayer.MinCornerHeight</c>.</summary>
    public const int MinCornerHeight = 2;

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

        foreach (string p in new[] { metaPath, entPath, pngPath })
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

        var code = new int[w * h];
        var elev = new int[w * h];
        var flag = new int[w * h];
        var seen = new bool[w * h];
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
        if (tiles != w * h || missing != 0 || outside != 0) { say("  ^ FEHLER"); bad++; }

        // ---- das Gelaende aus der .entities.json ---------------------------
        using var ent = JsonDocument.Parse(File.ReadAllText(entPath));
        var ground = new byte[w * h];
        int at = 0;
        foreach (var pair in ent.RootElement.GetProperty("terrain").GetProperty("rle").EnumerateArray())
        {
            var it = pair.EnumerateArray();
            it.MoveNext(); byte v = (byte)it.Current.GetInt32();
            it.MoveNext(); int run = it.Current.GetInt32();
            for (int k = 0; k < run && at < ground.Length; k++) ground[at++] = v;
        }
        if (at != w * h) { say($"  Gelaendeblock deckt {at} von {w * h} Zellen — FEHLER"); bad++; }

        var hist = new int[4];
        foreach (byte g in ground) if (g < 4) hist[g]++;
        say($"  Gelaende: frei {hist[0]}, rau {hist[1]}, wasser {hist[2]}, gesperrt {hist[3]}");

        // ---- laufen Kachelbild und imap auseinander? ------------------------
        int codeWaterClassLand = 0, codeLandClassWater = 0;
        for (int i = 0; i < w * h; i++)
        {
            bool waterCode = code[i] <= WaterCodeMax;
            bool waterClass = ground[i] == 2;
            if (waterCode && !waterClass) codeWaterClassLand++;
            if (!waterCode && waterClass) codeLandClassWater++;
        }
        say($"  Bild gegen imap: {codeWaterClassLand} Wasserkacheln auf Land, " +
            $"{codeLandClassWater} Landkacheln auf Wasser");
        if (codeWaterClassLand + codeLandClassWater > 0) { say("  ^ FEHLER"); bad++; }

        // ---- Bauplaetze -----------------------------------------------------
        // Der Zellentest von can_build_here @0x4203C0, so weit er ohne
        // Gebaeudemuster geht: die imap muss FREI sein (0xFFFE, also
        // Gelaendeklasse 0 — rau, Wasser und gesperrt scheiden alle aus), das
        // Hangbyte 0, und alle vier Eckhoehen ≥ 2.
        // Ausserhalb der Karte ist die Hoehe 0 (NavGrid.ElevAt), der aeussere
        // Rand kann also nie ein Bauplatz sein.
        int Elev(int c, int r) => c >= 0 && c < w && r >= 0 && r < h ? elev[r * w + c] : 0;
        var buildable = new bool[w * h];
        int sites = 0;
        for (int r = 0; r < h; r++)
            for (int c = 0; c < w; c++)
            {
                int i = r * w + c;
                bool ok = flag[i] == 0 && ground[i] == 0
                          && Elev(c, r) >= MinCornerHeight && Elev(c, r + 1) >= MinCornerHeight
                          && Elev(c + 1, r) >= MinCornerHeight && Elev(c + 1, r + 1) >= MinCornerHeight;
                buildable[i] = ok;
                if (ok) sites++;
            }
        say($"  Bauplatzzellen (imap frei, Hangbyte 0, vier Ecken ≥ {MinCornerHeight}): " +
            $"{sites} von {w * h} = {100.0 * sites / (w * h):0.0} %");
        if (sites == 0) { say("  ^ FEHLER: auf dieser Karte kann niemand bauen"); bad++; }

        // ---- Startmarken und Erreichbarkeit --------------------------------
        var starts = new List<(int Slot, int Col, int Row, int Type)>();
        foreach (var mk in ent.RootElement.GetProperty("markers").EnumerateArray())
        {
            int type = mk.GetProperty("type").GetInt32();
            if (type < MapFactory.MarkerStartBase || type > MapFactory.MarkerStartBase + 4) continue;
            starts.Add((mk.GetProperty("slot").GetInt32(), mk.GetProperty("col").GetInt32(),
                        mk.GetProperty("row").GetInt32(), type));
        }
        if (starts.Count == 0) { say("  keine Startmarke (Typ 0x70..0x74) — FEHLER"); bad++; }

        foreach (var s in starts)
        {
            int reach = Flood(w, h, ground, elev, s.Col, s.Row, buildable, out int reachSites);
            say($"  Spieler {s.Type - MapFactory.MarkerStartBase} (Marke {s.Slot} auf {s.Col},{s.Row}): " +
                $"{reach} erreichbare Zellen, davon {reachSites} Bauplaetze");
            if (reach == 0 || reachSites == 0) { say("  ^ FEHLER"); bad++; }
        }

        // ---- das Bild -------------------------------------------------------
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

        say(bad == 0 ? "map-check: OK" : $"map-check: {bad} Beanstandungen");
        return bad == 0 ? 0 : 1;
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
