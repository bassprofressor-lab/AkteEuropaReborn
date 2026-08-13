namespace AkteEuropaReborn.Editor;

using System;
using System.Collections.Generic;
using System.Text.Json;
using Godot;

/// <summary>
/// Welche Kachel legt das ORIGINAL auf eine Zelle? Nicht geraten, sondern aus
/// den gelieferten Karten desselben Kachelsatzes ABGELESEN.
///
/// <para><b>Warum es diese Klasse gibt.</b> <see cref="TilePalette"/> kennt
/// genau zwei Kachelarten: die acht Wasserkacheln und den ersten Block von acht
/// ganzen Bodenkacheln. Damit malt der Generator eine Wiese und einen Teich,
/// und zwischen beiden steht nichts. Die gelieferten Karten tragen aber im
/// Mittel <b>650 verschiedene Bodencodes je Karte</b> (Streubreite 183..1217
/// ueber die 26 Karten), und die acht haeufigsten decken davon nur 32,0 %.
/// Der Rest sind Uebergaenge: Ufer, Haenge, Kanten.</para>
///
/// <para>⚠ <b>Die haerteste Zahl des Tages:</b> von den <b>27.114</b> Landzellen,
/// die in den 26 Karten an eine Wasserzelle grenzen, tragen <b>0</b> einen
/// Innenland-Code — auf jeder einzelnen Karte 0, kein Gegenbeispiel. Das
/// Original setzt AUSNAHMSLOS eine Uferkachel zwischen Wiese und Wasser. Der
/// bisherige Generator verletzt das auf 100 % seiner Uferzellen, und genau das
/// ist zu sehen: »eine fertige flache Karte mit nem Fleck Wasser drin«.</para>
///
/// <para><b>Der Schluessel.</b> Gemessen ueber alle 26 Karten bestimmt das Trio
/// <i>(Gelaendeklasse, Hangbyte, welche der vier Nachbarn Wasser sind)</i> die
/// Kachelwahl. Der Beleg ist eine KREUZPROBE, nicht die Anpassungsguete: eine
/// Tabelle, die nur aus <b>einer</b> Karte gelernt ist, findet fuer
/// <b>99,3 bis 99,9 %</b> der Zellen einer ANDEREN Karte desselben Kachelsatzes
/// einen Eintrag (sechs Paare: NET03↔NET05 ts43, NET06↔NET08 ts44,
/// NET02↔NET07 ts47). Der Schluessel ist also nicht an eine Karte angepasst.
/// </para>
///
/// <para><b>Was hier gemessen und was UNSERE Setzung ist.</b> Gemessen sind der
/// Schluessel, die Codeliste je Schluessel und ihre Haeufigkeiten. UNSERE
/// Setzung ist allein, dass gewuerfelt wird, und zwar <i>gewichtet nach den
/// gemessenen Haeufigkeiten</i> — welche der acht Wiesenvarianten das Original
/// auf eine bestimmte Zelle gelegt haette, sagen die Daten nicht.</para>
/// </summary>
public sealed class TileModel
{
    /// <summary>Ab hier ist ein Code ein Objekt (Baum, Fels) und keine
    /// Bodenkachel — <c>MapBaker.GroundMax</c>.</summary>
    public const int ObjectCodeMin = Import.MapBaker.GroundMax;

    /// <summary>Ein Schluessel: Gelaendeklasse (0..3), Hangbyte, Wassermaske der
    /// vier Nachbarn (Bit 0 Ost, 1 Sued, 2 West, 3 Nord — ausserhalb der Karte
    /// zaehlt wie Wasser, weil das Original seine Raender so behandelt).</summary>
    private readonly Dictionary<int, Bucket> _t = new();

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

    public int Tileset { get; private set; }
    public int SourceMaps { get; private set; }
    public int SourceCells { get; private set; }
    public List<string> Sources { get; } = new();
    /// <summary>Karten, die uebergangen wurden, weil sie selbst erzeugt sind —
    /// siehe die Warnung in <see cref="Feed"/>.</summary>
    public List<string> Skipped { get; } = new();
    public int Keys => _t.Count;

    /// <summary>
    /// Die acht haeufigsten Codes auf freien, ebenen Zellen ohne Wassernachbar —
    /// der INNENLAND-BLOCK, wie ihn <see cref="MapCheck"/> aus einer Karte
    /// herausliest. Er ist der bessere Rueckfall als
    /// <see cref="TilePalette.Ground"/>: dessen Regel »der erste Lauf von acht
    /// ganzen Bodenkacheln« liefert bei Kachelsatz 01 den Block 8..15, waehrend
    /// die gelieferte Karte dort 90..97 benutzt (360 von 909 Innenlandzellen).
    /// Beides sind ganze Bodenkacheln; welche Wiese ist, sagt nur die Karte.
    /// </summary>
    public List<int> InnerBlock()
    {
        var codes = new List<int>();
        if (_t.TryGetValue(Key(0, 0, 0, DistCap), out var b))
        {
            var pairs = new List<(int Code, int W)>();
            for (int i = 0; i < b.Codes.Count; i++)
                if (b.Codes[i] < ObjectCodeMin) pairs.Add((b.Codes[i], b.Weight[i]));
            pairs.Sort((x, y) => y.W.CompareTo(x.W));
            for (int i = 0; i < 8 && i < pairs.Count; i++) codes.Add(pairs[i].Code);
            codes.Sort();
        }
        return codes;
    }

    /// <summary>Wie weit der Wasserabstand im Schluessel gezaehlt wird, bevor er
    /// »weit weg« heisst.
    ///
    /// <para>⚠ GEMESSEN und nicht gewaehlt. Der Strand des Originals ist ZWEI
    /// Zellen breit, und die zweite Reihe hat eigene Kacheln: bei Kachelsatz 01
    /// liegen die Codes 274 (6 Vorkommen), 277 (8) und 280 (2) auf Zellen mit
    /// Wasserabstand GENAU 2 — 16 von 16, kein Gegenbeispiel. Ohne den Abstand
    /// im Schluessel landen sie in demselben Fach wie das Binnenland, und der
    /// Generator streut dann Uferkacheln mitten in die Wiese: gemessen 146 von
    /// 449 blauen Landkacheln ohne Wasser im Umkreis von zwei Zellen, waehrend
    /// map_01 dort 0 von 161 hat.</para>
    ///
    /// <para>Die Kreuzprobe kostet das fast nichts: mit Abstand im Schluessel
    /// deckt eine Tabelle aus einer Karte 99,91 % einer anderen desselben
    /// Kachelsatzes, ohne 99,93 %.</para></summary>
    public const int DistCap = 3;

    private static int Key(int cls, int flag, int wmask, int dist)
        => (cls << 14) | (flag << 6) | (wmask << 2) | Math.Clamp(dist, 0, DistCap);

    // ---- Lernen --------------------------------------------------------------

    /// <summary>
    /// Alle gelieferten Karten dieses Kachelsatzes einlesen und die Tabelle
    /// aufstellen. Gesucht wird dort, wo die ENGINE sucht — <c>user://data</c>
    /// zuerst, dann der Entwicklungsbaum (<c>Core.Content.Path</c>) —, denn eine
    /// Tabelle aus dem Entwicklungsbaum waere nicht die, die zum Bild passt.
    /// </summary>
    public static TileModel? Learn(int tileset, Action<string> say, string? onlyMap = null)
    {
        var mdl = new TileModel { Tileset = tileset };
        foreach (string dir in new[] { Core.Content.UserRoot + "Maps", Core.Content.DevRoot + "Maps" })
        {
            if (!DirAccess.DirExistsAbsolute(dir)) continue;
            foreach (string f in DirAccess.GetFilesAt(dir))
            {
                if (!f.StartsWith("map_") || !f.EndsWith(".json") || f.Contains(".entities.")) continue;
                string stem = f[..^".json".Length];
                if (onlyMap != null && stem != onlyMap && stem != "map_" + onlyMap) continue;
                if (mdl.Sources.Contains(stem)) continue;       // user:// gewinnt
                mdl.Feed(dir, stem, tileset);
            }
        }
        if (mdl.SourceMaps == 0)
        {
            say($"Kachelsatz {tileset:00}: keine gelieferte Karte benutzt ihn — " +
                "keine gemessene Kacheltabelle, es bleibt beim Bodenblock aus TilePalette");
            return null;
        }
        say($"Kacheltabelle GEMESSEN aus {mdl.SourceMaps} gelieferten Karte(n) " +
            $"[{string.Join(" ", mdl.Sources)}], {mdl.SourceCells} Zellen, {mdl.Keys} Schluessel" +
            (mdl.Skipped.Count > 0
                ? $"; SELBST ERZEUGTE uebergangen: {string.Join(" ", mdl.Skipped)}"
                : ""));
        return mdl;
    }

    /// <summary>Eine Karte in die Tabelle einruehren, wenn sie den Kachelsatz
    /// benutzt. Der Kachelsatz wird VOR dem Zerlegen aus dem Text gefischt —
    /// eine 2,4-MB-Karte zu zerlegen, um sie dann wegzuwerfen, kostet sonst bei
    /// jedem Lauf ueber alle 47 Karten Sekunden.</summary>
    private void Feed(string dir, string stem, int tileset)
    {
        string metaPath = $"{dir}/{stem}.json", entPath = $"{dir}/{stem}.entities.json";
        if (!FileAccess.FileExists(metaPath) || !FileAccess.FileExists(entPath)) return;
        string text = FileAccess.GetFileAsString(metaPath);
        if (text.Length == 0) return;

        // ⚠⚠ EINE SELBST ERZEUGTE KARTE DARF NICHT IN DIE TABELLE.
        // Am 13.08.2026 tat sie es: der zweite Lauf von --map-new=neu01 lernte
        // aus [map_01 map_neu01] und damit aus seiner eigenen Ausgabe des ersten
        // Laufs — die Rueckfaelle fielen von 210 auf 8, und das sah wie ein
        // Fortschritt aus. Es war die Bestaetigung einer Ableitung durch sich
        // selbst. Erkannt wird eine erzeugte Karte am Missionstext, den
        // MapGenerator.Build setzt: er beginnt mit "Editor ".
        if (text.Contains("\"mission\":\"Editor ", StringComparison.Ordinal))
        {
            Skipped.Add(stem);
            return;
        }

        int at = text.IndexOf("\"tileset\":", StringComparison.Ordinal);
        if (at < 0) return;
        {
            int e = at + "\"tileset\":".Length, v = 0; bool any = false;
            while (e < text.Length && (text[e] == ' ')) e++;
            while (e < text.Length && text[e] >= '0' && text[e] <= '9') { v = v * 10 + (text[e++] - '0'); any = true; }
            if (!any || v != tileset) return;
        }

        try
        {
            using var meta = JsonDocument.Parse(text);
            var root = meta.RootElement;
            int w = root.GetProperty("width").GetInt32(), h = root.GetProperty("height").GetInt32();
            int n = w * h;
            var code = new int[n]; var flag = new int[n];
            foreach (var t in root.GetProperty("tiles").EnumerateArray())
            {
                int c = t.GetProperty("col").GetInt32(), r = t.GetProperty("row").GetInt32();
                if (c < 0 || c >= w || r < 0 || r >= h) continue;
                int i = r * w + c;
                code[i] = t.GetProperty("code").GetInt32();
                flag[i] = t.GetProperty("flag").GetInt32();
            }

            using var ent = JsonDocument.Parse(FileAccess.GetFileAsString(entPath));
            var g = new byte[n];
            int a2 = 0;
            foreach (var pair in ent.RootElement.GetProperty("terrain").GetProperty("rle").EnumerateArray())
            {
                var it = pair.EnumerateArray();
                it.MoveNext(); byte v = (byte)it.Current.GetInt32();
                it.MoveNext(); int run = it.Current.GetInt32();
                for (int k = 0; k < run && a2 < n; k++) g[a2++] = v;
            }
            if (a2 != n) return;

            var dist = WaterDistance(w, h, g);
            for (int r = 0; r < h; r++)
                for (int c = 0; c < w; c++)
                {
                    int i = r * w + c;
                    Slot(Key(g[i], flag[i], WaterMask(w, h, g, c, r), dist[i])).Add(code[i], 1);
                }
            SourceMaps++;
            SourceCells += n;
            Sources.Add(stem);
        }
        catch (Exception) { /* eine unlesbare Karte ist kein Grund, keine Karte zu bauen */ }
    }

    private Bucket Slot(int key)
    {
        if (!_t.TryGetValue(key, out var b)) _t[key] = b = new Bucket();
        return b;
    }

    /// <summary>
    /// Die Wassermaske einer Zelle: Bit 0 Ost, 1 Sued, 2 West, 3 Nord.
    ///
    /// <para>⚠ <b>Ausserhalb der Karte gilt NICHT als Wasser</b>, und das ist
    /// gemessen und nicht gewaehlt. Am 13.08.2026 stand es umgekehrt hier, und
    /// der Fehler war teuer: bei Kachelsatz 01 fielen dadurch die 72 Zellen des
    /// Ostrandes in denselben Schluessel wie die echten Uferzellen, der Schluessel
    /// »frei, eben, Wasser im Osten« bekam die Innenland-Codes 97, 94 und 90 —
    /// und der Generator malte damit Wiese direkt an Wasser, in 78 von 295
    /// Uferzellen. Die Kreuzprobe sagt dasselbe: mit »Rand ist Wasser« deckt eine
    /// Tabelle aus einer Karte 99,77 % einer anderen desselben Kachelsatzes,
    /// ohne 99,93 % (sechs Paare, 336.000 Zellen).</para>
    /// </summary>
    public static int WaterMask(int w, int h, byte[] ground, int c, int r)
    {
        int m = 0;
        int[] dc = { 1, 0, -1, 0 }, dr = { 0, 1, 0, -1 };
        for (int k = 0; k < 4; k++)
        {
            int nc = c + dc[k], nr = r + dr[k];
            if (nc < 0 || nc >= w || nr < 0 || nr >= h) continue;
            if (ground[nr * w + nc] == 2) m |= 1 << k;
        }
        return m;
    }

    /// <summary>
    /// Der Abstand jeder Zelle zum naechsten Wasser in Zellenschritten, bei
    /// <see cref="DistCap"/> abgeschnitten. Vierer-Nachbarschaft, wie alles
    /// andere hier — dieselbe Rechnung, mit der die Vergleichszahlen an den 26
    /// gelieferten Karten geholt wurden.
    /// </summary>
    /// <param name="cap">Bei welchem Wert abgeschnitten wird. Vorgabe ist
    /// <see cref="DistCap"/> — der Schluessel braucht nicht mehr. ⚠ Wer den
    /// Abstand zum RECHNEN braucht (etwa fuer die Kuestenebene in
    /// <see cref="MapTerrain.CoastalPlain"/>), muss einen hoeheren Deckel
    /// angeben: mit dem Vorgabewert ist »Abstand &gt; 3« nie wahr, und genau
    /// daran wurde am 13.08.2026 die ganze Karte auf Hoehe 0 gezogen (96,7 %
    /// der Zellen auf Stufe 0 statt der gemessenen 35,8 %).</param>
    public static int[] WaterDistance(int w, int h, byte[] ground, int cap = DistCap)
    {
        int n = w * h;
        var d = new int[n];
        var open = new Queue<int>();
        for (int i = 0; i < n; i++)
        {
            if (ground[i] == 2) { d[i] = 0; open.Enqueue(i); }
            else d[i] = cap;
        }
        int[] dc = { 1, -1, 0, 0 }, dr = { 0, 0, 1, -1 };
        while (open.Count > 0)
        {
            int i = open.Dequeue();
            if (d[i] >= cap) continue;
            int c = i % w, r = i / w;
            for (int k = 0; k < 4; k++)
            {
                int nc = c + dc[k], nr = r + dr[k];
                if (nc < 0 || nc >= w || nr < 0 || nr >= h) continue;
                int j = nr * w + nc;
                if (d[j] > d[i] + 1) { d[j] = d[i] + 1; open.Enqueue(j); }
            }
        }
        return d;
    }

    // ---- Benutzen ------------------------------------------------------------

    public bool Has(int cls, int flag, int wmask, int dist) => _t.ContainsKey(Key(cls, flag, wmask, dist));

    /// <summary>
    /// Der Code fuer eine Zelle, gewichtet gewuerfelt aus dem, was das Original
    /// in dieser Lage benutzt. <paramref name="roll"/> ist eine Zahl aus dem
    /// festen Wuerfel des Generators — kein <c>Random</c>, damit zwei Laeufe
    /// dieselbe Karte ergeben und ein Pruefstand eine Aenderung sehen kann.
    ///
    /// <para>Rueckfall in drei Stufen, jede gezaehlt (<see cref="Fallbacks"/>):
    /// erst der genaue Schluessel, dann derselbe ohne Hangbyte, dann derselbe
    /// ohne Wassermaske. Findet auch das nichts, gibt es −1 und der Aufrufer
    /// nimmt seinen Bodenblock.</para>
    /// </summary>
    public int Pick(int cls, int flag, int wmask, int dist, uint roll)
    {
        if (Take(Key(cls, flag, wmask, dist), roll, out int code)) return code;
        Fallbacks++;
        // ⚠ Der Wasserabstand faellt NIE weg. Am 13.08.2026 durfte er, als
        // letzte Stufe, und das kostete den Befund: fuer eine Uferzelle mit
        // duennem Fach griff der Rueckfall auf das BINNENLAND-Fach und legte
        // Wiese direkt an Wasser — 25 von 295 Uferzellen (8,47 %), gegen 0 von
        // 27.114 in den 26 gelieferten Karten. Klasse und Abstand bleiben
        // stehen; aufgegeben werden nur Hangbyte und Wassermaske, und danach
        // greift das zusammengefasste Fach DESSELBEN Abstands.
        if (flag != 0 && Take(Key(cls, 0, wmask, dist), roll, out code)) return code;
        if (wmask != 0 && Take(Key(cls, flag, 0, dist), roll, out code)) return code;
        if (Take(Key(cls, 0, 0, dist), roll, out code)) return code;
        if (TakeFrom(Merged(cls, dist), roll, out code)) return code;
        Missing++;
        return -1;
    }

    /// <summary>Alle Beobachtungen einer Klasse in einem Wasserabstand, ueber
    /// Hangbyte und Wassermaske hinweg — das letzte Fach des Rueckfalls. Es ist
    /// breit genug, dass <see cref="MinSupport"/> es nicht mehr aussortiert, und
    /// haelt trotzdem Ufer und Binnenland getrennt.</summary>
    private Bucket Merged(int cls, int dist)
    {
        int mk = (cls << 4) | Math.Clamp(dist, 0, DistCap);
        if (_merged.TryGetValue(mk, out var b)) return b;
        b = new Bucket();
        foreach (var kv in _t)
        {
            if ((kv.Key >> 14) != cls || (kv.Key & 3) != Math.Clamp(dist, 0, DistCap)) continue;
            for (int i = 0; i < kv.Value.Codes.Count; i++) b.Add(kv.Value.Codes[i], kv.Value.Weight[i]);
        }
        _merged[mk] = b;
        return b;
    }

    private readonly Dictionary<int, Bucket> _merged = new();

    public int Fallbacks { get; private set; }
    public int Missing { get; private set; }

    /// <summary>
    /// Wie viele gemessene Zellen ein Fach mindestens haben muss, damit daraus
    /// gewuerfelt wird.
    ///
    /// <para>⚠ Der Anlass ist gemessen: Kachelsatz 01 hat nur EINE gelieferte
    /// Karte (map_01, 3024 Zellen), und in ihr kommt der Code 220 genau viermal
    /// vor — dreimal am Wasser, einmal im Binnenland. Fuer ein Fach, in dem er
    /// die einzige Beobachtung ist, hiess »gewichtet nach den gemessenen
    /// Haeufigkeiten« dann: immer 220. Auf einer 128x84-Karte wurden daraus
    /// <b>570</b> Zellen, und weil 220 eine Uferkachel ist, standen 570 blaue
    /// Kacheln mitten im Land (an den Pixeln gezaehlt,
    /// <c>aekernel-tools/map_bluetiles.py</c>; map_01 hat dort 0 von 161).</para>
    ///
    /// <para>Ein Fach unter dieser Schranke ist keine gemessene Verteilung,
    /// sondern ein Einzelfall. Es wird uebergangen, und der Rueckfall nimmt den
    /// groeberen Schluessel — mit seiner viel breiteren Stichprobe.</para>
    /// </summary>
    public const int MinSupport = 8;

    /// <summary>Wie oft ein Fach wegen zu duenner Stichprobe uebergangen wurde.</summary>
    public int ThinKeys { get; private set; }

    private bool Take(int key, uint roll, out int code)
    {
        code = -1;
        if (!_t.TryGetValue(key, out var b) || b.Total == 0) return false;
        if (b.Total < MinSupport) { ThinKeys++; return false; }
        return TakeFrom(b, roll, out code);
    }

    private static bool TakeFrom(Bucket b, uint roll, out int code)
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

    /// <summary>Alle Codes, die das Original in dieser Lage benutzt — fuer die
    /// Meldung und fuer den Pruefstand, der zaehlen soll, ob eine erzeugte Zelle
    /// einen Code traegt, den das Original dort ueberhaupt kennt.</summary>
    public IReadOnlyList<int> CodesFor(int cls, int flag, int wmask, int dist)
        => _t.TryGetValue(Key(cls, flag, wmask, dist), out var b) ? b.Codes : Array.Empty<int>();

    /// <summary>Nur die Objektcodes einer Klasse — die Baeume und Felsen. In den
    /// 26 Karten tragen 67.642 von 70.433 gesperrten Zellen einen Objektcode
    /// (96,04 %), waehrend Klasse »rau« das fast nie tut (413 von 97.724 =
    /// 0,42 %). »Gesperrt« ist also das WALDSTUECK und »rau« eine Bodenart.</summary>
    public string Describe()
    {
        int codes = 0, cells = 0, obj = 0;
        foreach (var b in _t.Values)
        {
            codes += b.Codes.Count; cells += b.Total;
            foreach (int c in b.Codes) if (c >= ObjectCodeMin) obj++;
        }
        return $"Kachelsatz {Tileset:00}: {Keys} Schluessel, {codes} Codes " +
               $"({obj} davon Objektcodes), {cells} gemessene Zellen aus " +
               $"{SourceMaps} Karte(n)";
    }
}
