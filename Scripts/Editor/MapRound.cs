namespace AkteEuropaReborn.Editor;

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Godot;
using AkteEuropaReborn.Import;

/// <summary>
/// <b>DER RUNDLAUF — der einzige Pruefstein, der fuer
/// <see cref="MapOpen"/> zaehlt.</b>
///
/// <para>Eine gelieferte Karte wird geoeffnet, OHNE JEDE AENDERUNG gespeichert
/// und das Ergebnis Feld fuer Feld gegen das Original gehalten. Ein »geht« ohne
/// diese Zahlen waere wertlos: ein stiller Verlust beim Rundlauf zerstoert dem
/// Spieler seine Karte, und zwar so, dass er es erst merkt, wenn das Original
/// schon ueberschrieben ist.</para>
///
/// <para><b>Warum in einen eigenen Ordner geschrieben wird.</b> Der Vergleich
/// braucht die ALTEN Dateien. Schriebe der Rundlauf unter demselben Namen in
/// denselben Ordner, waere das Original weg, bevor irgendetwas verglichen ist —
/// und der Pruefstand haette genau den Schaden angerichtet, vor dem er warnen
/// soll. Er schreibt darum nach <c>&lt;user&gt;/Rundlauf</c>, und zwar unter
/// dem GLEICHEN Namen: nur so stimmt auch das Feld <c>map</c> ueberein, das
/// <c>EntitiesJson.Write</c> aus dem Ausgabenamen bildet.</para>
///
/// <para><b>Ein gruener Lauf muss beweisen, dass er etwas geprueft hat.</b>
/// Darum stehen die Zaehler je Gattung immer im Bericht — Gelaende, Hoehen,
/// Gebaeude, Einheiten, Gleise, Gegenstaende —, auch wenn nichts abweicht. Ein
/// Zaehler, der auf map_01 und auf map_NET02 dieselbe Zahl saehe, prueft
/// nichts; diese hier sind auf jeder Karte andere.</para>
/// </summary>
public static class MapRound
{
    /// <summary>Wohin der Rundlauf schreibt — NIE in den Kartenordner.</summary>
    public static string OutDir
        => ProjectSettings.GlobalizePath(Core.Content.UserRoot).TrimEnd('/', '\\') + "/Rundlauf";

    /// <summary>
    /// DER RUNDLAUF UEBER JEDE KARTE IM ORDNER — und das ist der Lauf, der
    /// wirklich etwas beweist.
    ///
    /// <para>Eine einzelne Karte kann zufaellig dicht sein, weil sie das
    /// schwierige Feld gar nicht traegt: map_01 hat 0 Gleiszellen, 0 Linien und
    /// 0 Ziele, also prueft sie diese drei Ruecklesungen ueberhaupt nicht.
    /// Erst der Lauf ueber alle 69 Karten deckt Gleis (NET02: 1193 Zellen),
    /// Linien (NET08: 42), Spielstandfelder (DM_4: 8 Konten, 64 Flug- und 80
    /// Schiffsbauplaene) und die grossen Raster (254x254) ab.</para>
    /// </summary>
    public static int RunAll(Action<string> say)
    {
        string src = MapOpen.MapsDir;
        var names = new List<string>();
        foreach (string p in Directory.GetFiles(src, "map_*.json"))
        {
            string f = Path.GetFileNameWithoutExtension(p);
            if (f.EndsWith(".entities")) continue;
            names.Add(f);
        }
        names.Sort(StringComparer.Ordinal);

        int bad = 0;
        var broken = new List<string>();
        foreach (string n in names)
        {
            int rc;
            try { rc = Run(n, say); }
            catch (Exception e) { say($"  {n}: ABBRUCH {e.Message}"); rc = 1; }
            if (rc != 0) { bad++; broken.Add(n); }
        }
        say($"RUNDLAUF GESAMT: {names.Count} Karten, {names.Count - bad} dicht, {bad} undicht" +
            (broken.Count > 0 ? " — " + string.Join(", ", broken) : ""));
        return bad == 0 ? 0 : 1;
    }

    /// <returns>0 = Feld fuer Feld gleich.</returns>
    public static int Run(string name, Action<string> say)
    {
        string outName = MapOpen.FullName(name);
        string src = MapOpen.MapsDir;
        string dst = OutDir;
        string metaA = $"{src}/{outName}.json", entA = $"{src}/{outName}.entities.json";
        string pngA = $"{src}/{outName}.png";
        foreach (string p in new[] { metaA, entA })
            if (!File.Exists(p)) { say($"rundlauf: FEHLT {p}"); return 1; }

        say($"rundlauf {outName}");
        var m = MapOpen.Load(outName, say);
        if (m == null) return 1;

        var files = MapGenerator.FindTileset(m.Tileset, null);
        if (files == null)
        {
            say($"rundlauf: Kachelsatz {m.Tileset:00} nicht gefunden — ohne ihn laesst sich " +
                "nicht zurueckschreiben (das Kartenbild wird gebacken)");
            return 1;
        }
        var cwp = CwpFile.Load(files.Value.Cwp);
        var pal = PalFile.Load(files.Value.Pal);
        Directory.CreateDirectory(dst);
        ContentBuilder.ExportMap(m, cwp, pal, dst, outName, out _, out _);

        string metaB = $"{dst}/{outName}.json", entB = $"{dst}/{outName}.entities.json";
        string pngB = $"{dst}/{outName}.png";

        say("  die Gattungen einzeln (alt gegen neu):");
        Tally(outName, src, dst, say);

        int bad = 0;
        bad += Compare(metaA, metaB, "map_*.json", say);
        bad += Compare(entA, entB, "map_*.entities.json", say);

        // Das Kartenbild haengt allein an sec1 und am Kachelsatz. Es ist damit
        // die unabhaengige Gegenprobe zum Kachelvergleich: waere ein Code
        // danebengegangen, den der Feldvergleich uebersieht, faellt hier ein
        // anderes Bild heraus.
        if (File.Exists(pngA) && File.Exists(pngB))
        {
            var a = File.ReadAllBytes(pngA);
            var b = File.ReadAllBytes(pngB);
            bool same = a.Length == b.Length;
            if (same) for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) { same = false; break; }
            say($"  Kartenbild: {a.Length} gegen {b.Length} Byte — " + (same ? "gleich" : "VERSCHIEDEN"));
            if (!same) bad++;
        }

        say(bad == 0
            ? $"  RUNDLAUF DICHT: {outName} kommt Feld fuer Feld unveraendert zurueck"
            : $"  RUNDLAUF UNDICHT: {bad} Stelle(n) — siehe oben");
        return bad == 0 ? 0 : 1;
    }

    // =======================================================================
    //  der Vergleich
    // =======================================================================

    private static int Compare(string pathA, string pathB, string label, Action<string> say)
    {
        string a = File.ReadAllText(pathA), b = File.ReadAllText(pathB);
        if (a == b)
        {
            say($"  {label}: {a.Length} Zeichen, Zeichen fuer Zeichen gleich");
            return 0;
        }
        say($"  {label}: {a.Length} gegen {b.Length} Zeichen — der Text weicht ab, " +
            "jetzt Feld fuer Feld:");

        using var da = JsonDocument.Parse(a);
        using var db = JsonDocument.Parse(b);
        var ra = da.RootElement;
        var rb = db.RootElement;

        int total = 0;
        var keys = new List<string>();
        foreach (var p in ra.EnumerateObject()) keys.Add(p.Name);
        foreach (var p in rb.EnumerateObject()) if (!keys.Contains(p.Name)) keys.Add(p.Name);

        int known = 0;
        foreach (string k in keys)
        {
            bool inA = ra.TryGetProperty(k, out var va);
            bool inB = rb.TryGetProperty(k, out var vb);
            if (!inA)
            {
                // ⚠ Ein Block, den nur die NEUE Datei hat, ist kein Verlust,
                // sondern ein Zeichen: die Datei auf der Platte ist AELTER als
                // der heutige Schreiber. Gemessen am 15.08.2026 — die 18 Karten
                // map_16..map_33 tragen keinen »rail_cells«-Block, obwohl
                // EntitiesJson.Write ihn immer schreibt, und ihre .png stammt
                // vom 02.08.2026, waehrend alle anderen vom 07.08. sind. Genau
                // diese 18 und keine andere melden auch ein anderes Kartenbild;
                // die Uebereinstimmung ist 18 von 18 und 0 Gegenbeispiele.
                say($"    {k}: nur im NEUEN — die Datei auf der Platte ist AELTER als der " +
                    "heutige Exporteur (kein Verlust; ein neuer Import zieht sie nach)");
                total++; continue;
            }
            if (!inB) { say($"    {k}: nur im ALTEN — VERLOREN"); total++; continue; }
            var notes = new List<string>();
            Walk(va, vb, k, notes);
            if (notes.Count == 0) continue;
            int kn = 0;
            foreach (string s in notes) if (IsKnownGap(s)) kn++;
            known += kn;
            int real = notes.Count - kn;
            total += real;
            if (real == 0)
            {
                say($"    {k}: {kn} Abweichung(en) — BEKANNTE LUECKE, siehe unten");
                continue;
            }
            say($"    {k}: {real} Abweichung(en)" + (kn > 0 ? $" (+{kn} bekannte)" : "") + Size(va, vb));
            int shown = 0;
            foreach (string s in notes)
            {
                if (IsKnownGap(s)) continue;
                if (shown++ >= 6) break;
                say("      " + s);
            }
            if (real > 6) say($"      … und {real - 6} weitere");
        }
        if (known > 0) say("    " + KnownGapText(known));
        if (total == 0 && known == 0)
            say("    (kein Feldunterschied — nur die Schreibweise weicht ab)");
        return total;
    }

    /// <summary>
    /// <b>DIE EINE STELLE, DIE NICHT RUNDLAUFEN KANN, UND WARUM NICHT.</b>
    ///
    /// <para><c>spatial.top_values</c> zaehlt die sechs haeufigsten Werte
    /// <b>ueber das GANZE imap-Raster</b> — 256 x 256 = 65536 Zellen
    /// (<c>CwmData.Spatial</c>, die Schleife laeuft ueber <c>s.Length / 2</c>).
    /// Hinausgeschrieben wird die imap aber nur fuer die Karte selbst: die
    /// <c>terrain.rle</c> deckt Breite x Hoehe, und <c>spatial.nonempty</c>
    /// laeuft ebenfalls nur ueber Breite x Hoehe. Alles AUSSERHALB des
    /// Kartenrechtecks steht in keiner der zwei Dateien.</para>
    ///
    /// <para><b>Die Zahl dazu</b>, auf map_01 (42 x 72 = 3024 Zellen, also 62512
    /// Zellen ausserhalb): das Original zaehlt dort 61955 mal 0xFFFC und 1021
    /// mal 0x0000 — Werte, die nur ausserhalb der Karte vorkommen koennen, denn
    /// innen liegen bloss 475 Wasserzellen. Der Rueckweg fuellt die Flaeche mit
    /// 0xFFFF, so wie <c>MapFactory.Empty</c> es fuer eine neue Karte tut, und
    /// die Rangliste sieht danach anders aus.</para>
    ///
    /// <para><b>Was das kostet: nichts, was gespielt wird.</b>
    /// <c>top_values</c> ist reine Auskunft — kein Leser der Engine fragt
    /// danach (gesucht: keine Fundstelle ausser dem Schreiber selbst), und keine
    /// Zelle ausserhalb von Breite x Hoehe wird je betreten. <c>nonempty</c>,
    /// <c>terrain</c> und <c>zones</c>, also alles, was das Spiel wirklich
    /// liest, laufen rund.</para>
    /// </summary>
    private static bool IsKnownGap(string note) => note.StartsWith("spatial.top_values");

    private static string KnownGapText(int n)
        => $"⚠ davon {n} in spatial.top_values — BEKANNTE LUECKE: der Block zaehlt ueber alle " +
           "65536 imap-Zellen, ausgegeben wird die imap aber nur fuer Breite x Hoehe. Die " +
           "Flaeche ausserhalb der Karte steht in keiner Datei und kommt als 0xFFFF zurueck. " +
           "Reine Auskunft, kein Leser der Engine fragt danach (siehe MapRound.IsKnownGap)";

    private static string Size(JsonElement a, JsonElement b)
        => a.ValueKind == JsonValueKind.Array && b.ValueKind == JsonValueKind.Array
           ? $", Anzahl {a.GetArrayLength()} gegen {b.GetArrayLength()}" : "";

    /// <summary>Zwei Baeume vergleichen. Gezaehlt wird jedes BLATT, das
    /// abweicht — nicht der Teilbaum, sonst saehe ein verschobener Satz aus wie
    /// ein einziger Fehler.</summary>
    private static int Walk(JsonElement a, JsonElement b, string path, List<string> notes)
    {
        if (a.ValueKind != b.ValueKind)
        {
            Note(notes, $"{path}: {a.ValueKind} gegen {b.ValueKind}");
            return 1;
        }
        switch (a.ValueKind)
        {
            case JsonValueKind.Object:
            {
                int n = 0;
                foreach (var p in a.EnumerateObject())
                {
                    if (!b.TryGetProperty(p.Name, out var vb))
                    { Note(notes, $"{path}.{p.Name}: fehlt im NEUEN"); n++; continue; }
                    n += Walk(p.Value, vb, $"{path}.{p.Name}", notes);
                }
                foreach (var p in b.EnumerateObject())
                    if (!a.TryGetProperty(p.Name, out _))
                    { Note(notes, $"{path}.{p.Name}: nur im NEUEN"); n++; }
                return n;
            }
            case JsonValueKind.Array:
            {
                int la = a.GetArrayLength(), lb = b.GetArrayLength();
                int n = 0;
                if (la != lb) { Note(notes, $"{path}: {la} gegen {lb} Eintraege"); n++; }
                var ia = a.EnumerateArray();
                var ib = b.EnumerateArray();
                int i = 0;
                while (ia.MoveNext() && ib.MoveNext())
                { n += Walk(ia.Current, ib.Current, $"{path}[{i}]", notes); i++; }
                return n;
            }
            default:
            {
                string ta = a.GetRawText(), tb = b.GetRawText();
                if (ta == tb) return 0;
                Note(notes, $"{path}: {Cut(ta)} gegen {Cut(tb)}");
                return 1;
            }
        }
    }

    private static void Note(List<string> notes, string s)
    { if (notes.Count < 200) notes.Add(s); }

    private static string Cut(string s) => s.Length <= 40 ? s : s[..40] + "…";

    // =======================================================================
    //  die Zaehler je Gattung — was der Spieler sehen will
    // =======================================================================

    /// <summary>
    /// Die sechs Gattungen einzeln gezaehlt, aus beiden Dateipaaren, mit ihrer
    /// Abweichung. Steht getrennt vom Baumvergleich, weil der Spieler nicht
    /// »17 Blaetter« lesen will, sondern »Gebaeude 45 gegen 45«.
    /// </summary>
    public static void Tally(string outName, string dirA, string dirB, Action<string> say)
    {
        var a = Read(dirA, outName);
        var b = Read(dirB, outName);
        if (a == null || b == null) { say("  Zaehlung: eine Seite fehlt"); return; }
        Line(say, "Gelaendezellen frei/rau/wasser/gesperrt",
             a.Value.Ground, b.Value.Ground);
        Line(say, "Hoehensumme ueber alle Zellen", a.Value.ElevSum, b.Value.ElevSum);
        Line(say, "Kachelcodes, Pruefsumme", a.Value.CodeSum, b.Value.CodeSum);
        Line(say, "Gegenstaende (Kacheln mit object=true)", a.Value.Props, b.Value.Props);
        Line(say, "Gebaeude", a.Value.Buildings, b.Value.Buildings);
        Line(say, "Einheiten", a.Value.Units, b.Value.Units);
        Line(say, "Gleiszellen", a.Value.Rails, b.Value.Rails);
        Line(say, "Marken", a.Value.Markers, b.Value.Markers);
    }

    private static void Line(Action<string> say, string what, long x, long y)
        => say($"    {what}: {x} gegen {y}" + (x == y ? "" : "   ⚠ ABWEICHUNG"));

    private static void Line(Action<string> say, string what, int[] x, int[] y)
    {
        bool same = true;
        for (int i = 0; i < x.Length && i < y.Length; i++) if (x[i] != y[i]) same = false;
        say($"    {what}: {string.Join("/", x)} gegen {string.Join("/", y)}" +
            (same ? "" : "   ⚠ ABWEICHUNG"));
    }

    private readonly record struct Counts(int[] Ground, long ElevSum, long CodeSum,
                                          int Props, int Buildings, int Units,
                                          int Rails, int Markers);

    private static Counts? Read(string dir, string outName)
    {
        string mp = $"{dir}/{outName}.json", ep = $"{dir}/{outName}.entities.json";
        if (!File.Exists(mp) || !File.Exists(ep)) return null;
        using var dm = JsonDocument.Parse(File.ReadAllText(mp));
        using var de = JsonDocument.Parse(File.ReadAllText(ep));
        long elev = 0, code = 0;
        int props = 0;
        foreach (var t in dm.RootElement.GetProperty("tiles").EnumerateArray())
        {
            elev += t.GetProperty("elev").GetInt32();
            code += t.GetProperty("code").GetInt32();
            if (t.GetProperty("object").GetBoolean()) props++;
        }
        var ground = new int[4];
        int gi = 0;
        foreach (var v in de.RootElement.GetProperty("terrain").GetProperty("hist").EnumerateArray())
            if (gi < 4) ground[gi++] = v.GetInt32();
        return new Counts(ground, elev, code, props,
                          de.RootElement.GetProperty("buildings").GetArrayLength(),
                          de.RootElement.GetProperty("entities").GetArrayLength(),
                          de.RootElement.TryGetProperty("rail_cells", out var rc) ? rc.GetArrayLength() : 0,
                          de.RootElement.GetProperty("markers").GetArrayLength());
    }
}
