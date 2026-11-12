namespace AkteEuropaReborn.Editor;

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Godot;
using AkteEuropaReborn.Import;

/// <summary>
/// ⭐ 11.11.2026 — bug-801: WAS DIE KACHELSAETZE SIND, fuer den Spieler.
///
/// <para><b>Die Spielermeldung</b> (11.11.2026): »Woher weiß der Spieler die
/// unterschiede bei den Kachelsätzen? das sollte beschrieben sein!« — bis heute
/// stand im Auswahlfeld nur die Nummer (01..47).</para>
///
/// <para><b>Was GEMESSEN ist</b> (scratchpad/editorui/maps.py, 11.11.2026, über
/// <c>user://data/Maps/map_*.json</c>, Feld <c>tileset</c>, Titel aus
/// <c>campaign.json</c> bzw. dem Feld <c>mission</c> der NET-Karten): welche
/// gelieferte Karte welchen Satz benutzt — das steht unten in <see cref="Belege"/>.
/// Ebenfalls gemessen, aber erst zur Laufzeit: die Stoffkette und ihre Farben
/// (<see cref="TileCorners"/>: Kette der Bildstoffe, Anteil je Stoff, Farbe =
/// Mittel der Kachel, die an allen vier Ecken diesen Stoff traegt) und welche der
/// 16 Gebaeudearten der Satz traegt (<see cref="CwpFile.GetBuildingType"/>).</para>
///
/// <para>⚠ <b>UNSERE SETZUNG sind die NAMEN und KURZBESCHREIBUNGEN</b> (»Wüste mit
/// Küste«, »Winter« …). Das Original benennt seine Kachelsaetze nirgends — es
/// gibt nur die Nummern NN.CWP. Die Namen sind nach Augenschein der
/// Kartenbilder gesetzt (Kontaktbogen scratchpad/editorui/kachelsaetze.png,
/// je ein 320x180-Ausschnitt; im Spiel 448x252 der ersten Karte des Satzes) und nach den
/// Missionstiteln. Wer einen Namen fuer falsch haelt, aendert ihn hier.</para>
///
/// <para>Die Vorschaubilder sind AUSSCHNITTE DER GEBACKENEN ORIGINALKARTE
/// (<c>map_XX.png</c>) — also das, was der Spieler im Spiel sieht. Gebacken wird
/// einmal im Hintergrund und unter <c>user://cache/editor/</c> abgelegt
/// (UNSERE SETZUNG: der Ordner; er ist ein Zwischenspeicher und darf fehlen).</para>
/// </summary>
public static class KachelsatzInfo
{
    /// <summary>Ein Kachelsatz: Name und Beschreibung (UNSERE SETZUNG), die
    /// Karten, die ihn benutzen (GEMESSEN), und die Vorschauquelle.</summary>
    public readonly record struct Satz(string Name, string Text, string Belege, string Vorschau);

    // GEMESSEN 11.11.2026 (maps.py): Kachelsatz -> Karten. DM-Karten weggelassen
    // (sie sind Spielstaende der Kampagnenkarten und tragen deren Satz).
    // Name/Text: UNSERE SETZUNG nach Augenschein (kachelsaetze.png).
    private static readonly Dictionary<int, Satz> Tafel = new()
    {
        // ---- die Saetze der Mehrspielerkarten zuerst (sie sind fuer das Gefecht gemacht)
        [47] = new("Wüste mit Küste", "Heller Sand, Dünen und Geröll, dazu Meer mit Sandstrand. Trocken, kaum Bewuchs.",
                   "NET02 »Sands of doom«, NET07 »Desert death«", "map_NET07"),
        [43] = new("Grasland mit Felsufer", "Grüne Wiesen, Wald, Geröllfelder und Felsufer an Seen und Flüssen.",
                   "NET03 »Deep forest«, NET05 »River combat«", "map_NET03"),
        [44] = new("Schneeschmelze", "Schnee und Eis mit großen Flächen aus freigetautem Erdboden; Bäche.",
                   "NET06 »Spring streams«, NET08 »Iceworld«", "map_NET06"),
        [45] = new("Tropische Inseln", "Sattgrünes Land mit dichtem Palmenbewuchs, Inseln im Meer.",
                   "NET04 »Islands«", "map_NET04"),
        [46] = new("Wiesen und Sumpf", "Sattgrüne Wiesen, Buschwerk, wenig Wasser.",
                   "NET01 »Swamp fight«", "map_NET01"),
        // ---- die Kampagnensaetze (je Mission einer, 25 und 27 mehrfach)
        [1]  = new("Grasland mit Felsufer", "Gras, Geröll und Felsufer, Seen.", "K1 »Airborne Ambush«", "map_01"),
        [2]  = new("Grasland mit Felsufer", "Gras, Geröll und Felsufer, viel Wasser.", "K2 »Hidden Bases«", "map_02"),
        [3]  = new("Grasland mit Wald", "Gras, Wald und Felsufer.", "K3 »Wood raiders«", "map_03"),
        [4]  = new("Sandküste", "Heller Sand am Meer, wenig Bewuchs.", "K4 »Sabotage«", "map_04"),
        [5]  = new("Sandküste", "Heller Sand am Meer, wenig Bewuchs.", "K5 »Production«", "map_05"),
        [6]  = new("Wüste", "Sand mit Geröll- und Trockengrasflecken, Wasser.", "K6 »Desert Radar«", "map_06"),
        [7]  = new("Grasland mit Felsküste", "Gras und Felsküste am Meer.", "K7 »Bretagne«", "map_07"),
        [8]  = new("Sandküste", "Heller Sand, verzweigte Buchten.", "K8 »The gate«", "map_08"),
        [9]  = new("Sand und Steppe", "Sand mit gelbgrünem Trockengras, Küste.", "K9 »Invasion«", "map_09"),
        [10] = new("Sand und Steppe", "Sand mit Trockengras, Pässe.", "K10 »Narrow Pass«", "map_10"),
        [11] = new("Sandküste mit Hafen", "Heller Sand am Meer, Hafenanlagen.", "K11 »Trading Center«", "map_11"),
        [12] = new("Sand und Steppe", "Sand mit Trockengras, Küste.", "K12 »Backslash«", "map_12"),
        [13] = new("Sand und Steppe", "Sand mit Trockengras, Gebirge.", "K13 »Pyrenees«", "map_13"),
        [14] = new("Grasland mit Küste", "Gras bis ans Meer, ohne Felsufer.", "K14 »Col. Hullman«", "map_14"),
        [15] = new("Wiesenland", "Sattgrüne Wiesen mit Erdflächen.", "K15 »Minefields«", "map_15"),
        [16] = new("Wiesenland", "Sattgrüne Wiesen, Täler.", "K16 »Death Valley«", "map_16"),
        [17] = new("Wiesenland mit Küste", "Sattgrüne Wiesen am Meer.", "K17 »Black Sea«", "map_17"),
        [18] = new("Wiesenland", "Sattgrüne Wiesen, Hügel.", "K18 »Hillbase«", "map_18"),
        [19] = new("Wiesenland", "Sattgrüne Wiesen, Flussdelta.", "K19 »Danube Delta«", "map_19"),
        [20] = new("Grasland mit Felsufer", "Gras, breite Geröllfelder, Wasser.", "K20 »Airstrike«", "map_20"),
        [21] = new("Grasland mit Felsufer", "Gras, Geröll und Felsufer, Stausee.", "K21 »The Dam«", "map_21"),
        [22] = new("Wiesenland", "Sattgrüne Wiesen, Bucht.", "K22 »North Bay«", "map_22"),
        [23] = new("Grasland mit Fels", "Gras und Geröllfelder.", "K23 »Prospectors«", "map_23"),
        [24] = new("Grasland mit Fels", "Gras, Wald und Geröll.", "K24 »Forest Flames«", "map_24"),
        [25] = new("Felsland", "Überwiegend Geröll und Fels, Wasser.", "K25 »Chanel Tunnel«, K31 »Coast Assault«", "map_25"),
        [26] = new("Winter", "Schnee und Eis, Meer mit Eisschollen.", "K26 »Scandinavia«", "map_26"),
        [27] = new("Winter", "Schnee und Eis, Meer mit Eisschollen.",
                   "K27 »Sea wolves«, K29 »Icefield«, K30 »Last Snow«", "map_27"),
        [28] = new("Winter", "Schnee und Eis, Meer mit Eisschollen.", "K28 »Polar Circle«", "map_28"),
        [32] = new("Grasland mit Felsufer", "Gras und Felsufer.", "K32 »ODIN's eye«", "map_32"),
        [33] = new("Grasland mit Felsufer", "Gras, Geröll und Felsküste.", "K33 »Scottland«", "map_33"),
    };

    /// <summary>Die Reihenfolge der Auswahl: erst die Mehrspielersaetze, dann
    /// die Kampagnensaetze nach Nummer. UNSERE SETZUNG.</summary>
    public static int Rang(int ts) => ts >= 43 ? ts - 100 : ts;

    public static Satz Of(int ts)
        => Tafel.TryGetValue(ts, out var s) ? s
           : new Satz($"Kachelsatz {ts:00}", "Keine gelieferte Karte benutzt diesen Satz — keine Beschreibung.", "—", "");

    /// <summary>Die Zeile im Auswahlfeld.</summary>
    public static string Zeile(int ts)
    {
        var s = Of(ts);
        return $"{s.Name}  ({ts:00} · {s.Belege})";
    }

    // ---- die gemessenen Teile, im Hintergrund --------------------------------

    /// <summary>Was zur Laufzeit gemessen wird.</summary>
    public sealed class Messung
    {
        public Image? Bild;
        /// <summary>Je Stoff der Kette: Farbe und Anteil (TileCorners).</summary>
        public readonly List<(Color Farbe, double Anteil, bool Wasser)> Stoffe = new();
        public string Kette = "";
        /// <summary>Die Namen der Gebaeudearten 1..16, die der Satz NICHT traegt.</summary>
        public readonly List<string> FehlendeGebaeude = new();
        public int Gebaeudearten;
        public int Gegenstaende;
        public string Fehler = "";
    }

    private static readonly Dictionary<int, Messung> _fertig = new();
    private static readonly HashSet<int> _laeuft = new();

    /// <summary>Die Messung, wenn sie schon da ist; sonst wird sie im
    /// Hintergrund angestossen und <paramref name="fertig"/> danach auf dem
    /// Hauptfaden gerufen.</summary>
    public static Messung? Hole(int ts, Action<int> fertig)
    {
        lock (_fertig)
        {
            if (_fertig.TryGetValue(ts, out var m)) return m;
            if (!_laeuft.Add(ts)) return null;
        }
        string userRoot = ProjectSettings.GlobalizePath(Core.Content.UserRoot).TrimEnd('/', '\\');
        string cache = ProjectSettings.GlobalizePath("user://cache/editor");
        var files = MapGenerator.FindTileset(ts, null);
        GebaeudeName(1);   // Namen auf dem Hauptfaden laden, nicht im Hintergrund
        TileCorners.Vorbereiten();   // A2: Ordner auf dem Hauptfaden aufloesen
        // ⭐ 12.11.2026 — A2 (Fable-Pruefung): im Faden NUR reine .NET-Arbeit.
        // Bis heute lud der Faden Bilder (Image) und las Dateien ueber
        // FileAccess/DirAccess; beim Schliessen des Schirms kurz nach dem Oeffnen
        // endete das mit 0xC0000005. Das Vorschaubild entsteht jetzt auf dem
        // Hauptfaden, im verzoegerten Rueckruf. Gegenschalter: keiner noetig.
        Task.Run(() =>
        {
            var m = new Messung();
            try { MesseKachelsatz(m, files, ts); }
            catch (Exception e) { m.Fehler = e.Message; }
            Callable.From(() =>
            {
                try { MesseBild(ts, m, userRoot, cache); }
                catch (Exception e) { if (m.Fehler.Length == 0) m.Fehler = e.Message; }
                lock (_fertig) { _fertig[ts] = m; _laeuft.Remove(ts); }
                fertig(ts);
            }).CallDeferred();
        });
        return null;
    }

    /// <summary>Hauptfaden: das Vorschaubild.</summary>
    private static void MesseBild(int ts, Messung m, string userRoot, string cache)
    {
        // 1. das Vorschaubild: zuerst der Zwischenspeicher, sonst aus der Karte
        string cached = $"{cache}/kachelsatz2_{ts:00}.png";
        if (File.Exists(cached)) m.Bild = Image.LoadFromFile(cached);
        else if (Of(ts).Vorschau.Length > 0)
        {
            string png = $"{userRoot}/Maps/{Of(ts).Vorschau}.png";
            if (File.Exists(png))
            {
                var big = Image.LoadFromFile(png);
                if (big != null && !big.IsEmpty())
                {
                    m.Bild = Ausschnitt(big, 448, 252);
                    big.Dispose();
                    try { Directory.CreateDirectory(cache); m.Bild.SavePng(cached); } catch { }
                }
            }
        }

    }

    /// <summary>Poolfaden: Gebaeudearten, Gegenstaende, Stoffkette — kein Godot-Aufruf.</summary>
    private static void MesseKachelsatz(Messung m, (string Cwp, string Pal)? files, int ts)
    {
        // 2. Gebaeudearten und Gegenstaende aus dem Kachelsatz
        if (files == null) return;
        var cwp = CwpFile.Load(files.Value.Cwp);
        var pal = PalFile.Load(files.Value.Pal);
        m.Gegenstaende = cwp.ObjectCount;
        for (int t = 1; t <= 16; t++)
        {
            if (!cwp.GetBuildingType(t).IsEmpty) m.Gebaeudearten++;
            else m.FehlendeGebaeude.Add(GebaeudeName(t));
        }

        // 3. die Stoffkette aus TileCorners — GEMESSEN an den gelieferten Karten
        var tc = TileCorners.Learn(ts, _ => { }, cwp);
        if (tc == null) return;
        m.Kette = tc.IsChain ? $"{tc.Labels} Stoffe, Kette" : $"{tc.Labels} Stoffe";
        var order = tc.IsChain ? tc.LabelOfDepth : null;
        for (int k = 0; k < tc.Labels; k++)
        {
            int label = order != null ? order[k] : k;
            int code = tc.UniformCode(label);
            var farbe = new Color(0.5f, 0.5f, 0.5f);
            if (code >= 0 && code < cwp.FrameCount) farbe = Mittelfarbe(cwp.DecodeFrame(code), pal);
            double share = label < tc.LabelShare.Length ? tc.LabelShare[label] : 0;
            m.Stoffe.Add((farbe, share, label == tc.WaterLabel));
        }
    }

    /// <summary>Der Ausschnitt mit der groessten Farbstreuung aus neun Lagen —
    /// damit nicht reines Wasser oder der schwarze Rand gezeigt wird. Dieselbe
    /// Wahl wie thumbs.py, mit dem die Namen gesetzt wurden.</summary>
    private static Image Ausschnitt(Image big, int w, int h)
    {
        Rect2I best = new(0, 0, Math.Min(w, big.GetWidth()), Math.Min(h, big.GetHeight()));
        double bestScore = double.MinValue;
        foreach (float fx in new[] { 0.25f, 0.5f, 0.75f })
            foreach (float fy in new[] { 0.3f, 0.5f, 0.7f })
            {
                int x = Math.Clamp((int)(big.GetWidth() * fx - w / 2f), 0, Math.Max(0, big.GetWidth() - w));
                int y = Math.Clamp((int)(big.GetHeight() * fy - h / 2f), 0, Math.Max(0, big.GetHeight() - h));
                double s = 0, s2 = 0; int n = 0, dark = 0;
                for (int yy = 0; yy < h; yy += 8)
                    for (int xx = 0; xx < w; xx += 8)
                    {
                        var c = big.GetPixel(Math.Min(x + xx, big.GetWidth() - 1), Math.Min(y + yy, big.GetHeight() - 1));
                        double v = c.R + c.G + c.B;
                        if (v < 0.12) dark++;
                        s += v; s2 += v * v; n++;
                    }
                double score = Math.Sqrt(Math.Max(0, s2 / n - (s / n) * (s / n))) - dark * 0.02;
                if (score > bestScore) { bestScore = score; best = new Rect2I(x, y, w, h); }
            }
        return big.GetRegion(best);
    }

    private static Color Mittelfarbe(CwpFile.Frame f, PalFile pal)
    {
        double r = 0, g = 0, b = 0; int n = 0;
        for (int i = 0; i < f.Count; i++)
        {
            if (!f.Opaque[i]) continue;
            byte p = f.Pixels[i];
            r += pal.R[p]; g += pal.G[p]; b += pal.B[p]; n++;
        }
        return n == 0 ? new Color(0.5f, 0.5f, 0.5f)
                      : new Color((float)(r / n / 255), (float)(g / n / 255), (float)(b / n / 255));
    }

    // ---- die Gebaeudenamen ---------------------------------------------------

    private static Dictionary<int, string>? _namen;

    /// <summary>Der Name einer Gebaeudeart aus <c>building_types.json</c>
    /// (GELESEN: GAME.EXE 0x4fccfc). Ueber 16 kennt die EXE keine Namen —
    /// »Kulisse Typ N« ist UNSERE SETZUNG.</summary>
    public static string GebaeudeName(int typ)
    {
        if (_namen == null)
        {
            var namen = new Dictionary<int, string>();
            try
            {
                string p = ProjectSettings.GlobalizePath(Core.Content.UserRoot) + "Maps/building_types.json";
                if (File.Exists(p))
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(p));
                    foreach (var kv in doc.RootElement.GetProperty("types").EnumerateObject())
                        if (int.TryParse(kv.Name, out int t) && kv.Value.TryGetProperty("name", out var n))
                            namen[t] = n.GetString() ?? "";
                }
            }
            catch { }
            _namen = namen;
        }
        return _namen.TryGetValue(typ, out string? s) && s.Length > 0 ? s : $"Kulisse Typ {typ}";
    }
}
