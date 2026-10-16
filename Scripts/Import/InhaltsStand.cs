namespace AkteEuropaReborn.Import;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

/// <summary>
/// ⭐ 16.10.2026 — <b>DER INHALTSSTAND</b>: welcher Stand des Importeurs die
/// eingelesenen Inhalte unter <c>user://data</c> geschrieben hat, je TEIL.
///
/// <para><b>Anlass, Spielerwunsch:</b> »Können wir einen Prüfer im Installer
/// einbauen, ob Karten gebacken werden müssen, wenn z. B. schon jemand eine
/// ältere Version installiert hat? Sonst muss man das immer händisch machen.«
/// Bis heute prüfte <see cref="Core.Content.Ready"/> nur eine Kanarienvogel-Datei
/// (<c>Maps/map_NET07.entities.json</c>); war sie da, las das Spiel nie wieder
/// ein — auch dann nicht, wenn sich der Kartenbacker, die Spielstandleser oder
/// die Gebaeudemuster inzwischen geaendert hatten. Wer das wusste, liess die
/// <c>--reexport-*</c>-Schalter von Hand laufen; wer nicht, spielte mit alten
/// Daten.</para>
///
/// <para><b>Wie es geht:</b> Diese Tafel fuehrt je Teil eine ganze Zahl, den
/// NOETIGEN Stand (<see cref="Noetig"/>). <see cref="ContentBuilder"/> schreibt
/// nach einem erfolgreichen Vollimport und nach jedem erfolgreichen Teil-Neuexport
/// den ERREICHTEN Stand nach <c>user://data/inhalt_stand.json</c> (dazu
/// Spielversion, Datum und die Quelle). Beim Start vergleicht das Hauptmenue
/// beides (<see cref="Veraltet"/>) und bietet an, genau die veralteten Teile neu
/// einzulesen.</para>
///
/// <para>⚠⚠ <b>DIE REGEL FUER JEDEN, DER AM IMPORTEUR ARBEITET:</b> Wer etwas
/// aendert, das die geschriebenen Dateien eines Teils anders ausfallen laesst —
/// <see cref="MapBaker"/>, <see cref="EntitiesJson"/>, die Cwm-Leser,
/// <see cref="BuildingPatterns"/>, <see cref="NebelMasken"/>,
/// <see cref="UnitsExporter"/>, <see cref="InterfaceExporter"/>,
/// <see cref="CatalogueExporter"/>, <see cref="ExeTables"/>, die Text- und
/// Briefingausgeber, <see cref="SoundExporter"/> oder die Schreibwege in
/// <see cref="ContentBuilder"/> —, <b>erhoeht in <see cref="Noetig"/> die Zahl
/// dieses Teils um eins</b>, im selben Commit. Betrifft die Aenderung mehrere
/// Teile, alle. Sonst bleibt der Spieler auf dem alten Stand, ohne dass ihn
/// jemand fragt — genau das war der Zustand vor dieser Datei. Welche Quelldatei
/// zu welchem Teil gehoert, steht an <see cref="Teile"/>. Die Stichtage der
/// Rueckdatierung (<see cref="Teil.Stichtag"/>) werden dabei NICHT angefasst:
/// sie beschreiben den Stand 0.6.9 und sind Geschichte.</para>
///
/// <para><b>Fehlt die Datei</b> bei vorhandenem Inhalt, wurde mit einer Fassung
/// vor dem Stempel eingelesen. Dann gilt grundsaetzlich Stand 0 = alles
/// veraltet. ⚠ UNSERE SETZUNG darueber, die <b>Rueckdatierung</b>: ein Teil,
/// dessen Merkdateien alle JUENGER sind als die letzte Importeur-Aenderung
/// dieses Teils bis 0.6.9 (<c>d67dfdd</c>, aus <c>git log</c> abgelesen), wurde
/// mit dem Importeur von 0.6.9 geschrieben und bekommt Stand 1 gutgeschrieben.
/// Ohne sie muesste jeder, der 0.6.9 frisch eingelesen hat, beim Update alles
/// noch einmal einlesen, obwohl sich am Importeur nichts geaendert hat.
/// Gegenschalter <c>--inhaltsstand-streng</c> (ohne Stempel = Stand 0, ohne
/// Gutschrift); <c>--inhaltsstand-aus</c> schaltet den ganzen Pruefer ab (der
/// alte Weg: nur der Kanarienvogel).</para>
/// </summary>
public static class InhaltsStand
{
    /// <summary>Der Dateiname unter der Datenwurzel.</summary>
    public const string Datei = "inhalt_stand.json";

    /// <summary><c>--inhaltsstand-aus</c>: der Pruefer meldet nie etwas veraltet
    /// (und Nullmodell des Pruefstands).</summary>
    public static bool Aus;

    /// <summary><c>--inhaltsstand-streng</c>: ohne Stempel gilt Stand 0, keine
    /// Rueckdatierung ueber die Dateizeiten.</summary>
    public static bool Streng;

    /// <summary>Was ein Teil zum Neu-Einlesen braucht.</summary>
    public enum Bedarf
    {
        /// <summary>Nichts von der CD — liest die schon eingelesenen Karten.</summary>
        Nichts,
        /// <summary>Programmdateien (GAME.EXE, Kabinett, 01.PAL …) — CD 1 oder
        /// eine Installation.</summary>
        Programm,
        /// <summary>ALLE Karten bzw. Kachelsaetze — beide CDs (Missionen 16–33
        /// liegen nur auf CD 2).</summary>
        AlleKarten,
    }

    /// <summary>Ein Teil der Inhalte.</summary>
    /// <param name="Name">Schluessel in der Stempeldatei — NIE umbenennen.</param>
    /// <param name="Anzeige">Fuer den Spieler.</param>
    /// <param name="Weg">Der Teil-Neuexport, mit dem er erneuert wird.</param>
    /// <param name="Merk">Merkdateien (relativ, <c>*</c> erlaubt im Dateinamen)
    /// fuer die Rueckdatierung — die AELTESTE zaehlt.</param>
    /// <param name="Stichtag">Letzte Importeur-Aenderung dieses Teils bis 0.6.9;
    /// null = ohne Zeitvergleich gutschreiben, sobald die Merkdateien da sind.</param>
    /// <param name="Quellen">Welche Dateien des Importeurs zu ihm gehoeren — fuer
    /// den Entwickler, der die Zahl erhoehen muss.</param>
    public sealed record Teil(string Name, string Anzeige, Bedarf Bedarf,
                              Func<ContentBuilder, Action<string>?, bool> Weg,
                              string[] Merk, DateTimeOffset? Stichtag, string Quellen);

    private static DateTimeOffset T(string iso) => DateTimeOffset.Parse(iso);

    /// <summary>
    /// Die Teile, in der Reihenfolge, in der sie neu eingelesen werden. ⚠ Die
    /// Reihenfolge ist Absicht: die Karten schreiben auch die Spielstaende, die
    /// Kampagnenliste liest die eingelesenen Karten, die Einheitenbilder die
    /// eingelesenen Spielstaende (<see cref="ContentBuilder.ReexportUnits"/>).
    ///
    /// <para>Stichtage gelesen am 16.10.2026 mit
    /// <c>git log -1 --format=%cI d67dfdd -- &lt;Quellen&gt;</c>; die Zeilen von
    /// <c>ContentBuilder.cs</c> einzeln den Teilen zugeordnet (ffe931c =
    /// Briefing-Knoepfe, 3451b29 = Nebelmasken bei den Gebaeuden, d3ee054 =
    /// Mauszeiger in der Oberflaeche, 7c3862b = nur Fortschrittszaehlung).</para>
    /// </summary>
    public static readonly Teil[] Teile =
    {
        new("karten", "Karten", Bedarf.AlleKarten, (b, p) => b.ReexportMaps(p),
            new[] { "Maps/map_NET07.json", "Maps/map_NET07.png" },
            T("2026-10-02T07:12:21+02:00"),
            "MapBaker, MapForest, CwpFile, CwmFile/CwmData/CwmSections/CwmExtra, EntitiesJson, PalFile, ContentBuilder.ExportMap/MapMeta"),
        new("spielstaende", "Spielstaende der Karten", Bedarf.AlleKarten, (b, p) => b.ReexportStates(p),
            new[] { "Maps/map_NET07.entities.json" },
            T("2026-09-22T20:31:25+02:00"),
            "CwmFile/CwmData/CwmSections/CwmExtra, EntitiesJson, Escape124, Cp437"),
        new("gebaeude", "Gebaeude und Kachelatlas", Bedarf.AlleKarten, (b, p) => b.ReexportBuildings(p),
            new[] { "Buildings/tileset_*_tiles.json" },
            T("2026-10-12T15:54:20+02:00"),
            "BuildingPatterns, NebelMasken, CwpFile, PalFile, ContentBuilder.ExportBuildingPatterns"),
        new("kachelsaetze", "Kachelsaetze fuer den Editor", Bedarf.AlleKarten, (b, p) => b.ReexportTilesets(p),
            new[] { "DATA/*.CWP" },
            null,   // eine Kopie, die Dateizeit ist die von der CD (1997)
            "ContentBuilder.CopyTileset"),
        new("tabellen", "Tabellen (Waffen, Entwuerfe, Kataloge, Schiffe, Flugzeuge)", Bedarf.AlleKarten,
            (b, p) => b.ReexportTables(p),
            new[] { "Maps/weapons.json", "Maps/aircraft.json" },
            T("2026-10-12T09:47:36+02:00"),
            "CatalogueExporter, ExeTables, SoundExporter.WriteWeaponSounds, ContentBuilder.WriteShips/WriteAircraft"),
        new("kampagne", "Kampagnenliste", Bedarf.Nichts, (b, p) => b.ReexportCampaign(p),
            new[] { "Maps/campaign.json" },
            T("2026-10-12T09:47:36+02:00"),
            "CatalogueExporter.WriteCampaign, ExeTables"),
        new("einheiten", "Einheitengrafiken", Bedarf.Programm, (b, p) => b.ReexportUnits(p),
            new[] { "Units/units_index.json" },
            T("2026-10-12T09:47:36+02:00"),
            "UnitsExporter, CwrFile, PalFile, ExeTables"),
        new("oberflaeche", "Oberflaeche, Effekte und Mauszeiger", Bedarf.Programm, (b, p) => b.ReexportEffects(p),
            new[] { "Effects/effects_index.json", "UI/window_chrome_index.json" },
            T("2026-10-04T19:56:28+02:00"),
            "InterfaceExporter, AnimFile, CwrFile, PalFile, ExeTables"),
        new("hilfe", "Hilfe, Missionsziele und Enzyklopaedie", Bedarf.Programm, (b, p) => b.ReexportHelp(p),
            new[] { "UI/help.json" },
            T("2026-10-02T07:12:21+02:00"),
            "HelpExporter, ObjectivesExporter, EncyclopediaExporter, Cp437"),
        new("briefings", "Briefings", Bedarf.Programm, (b, p) => b.ReexportBriefings(p),
            new[] { "Maps/briefings.json" },
            T("2026-10-13T22:49:58+02:00"),
            "BriefingExporter, MissionTechExporter, MissionTechTable, ContentBuilder.BriefingsSchreiben"),
        new("ton", "Klaenge und Musik", Bedarf.Programm, (b, p) => b.ReexportSound(p),
            new[] { "Sound/music.json" },
            T("2026-09-19T17:24:18+02:00"),
            "SoundExporter, SoundBank, IscFile"),
    };

    /// <summary>
    /// ⚠⚠ <b>DER NOETIGE STAND JE TEIL.</b> Erhoehen, sobald sich der Importeur
    /// des Teils aendert (Regel oben). Stand 1 = der Importeur von 0.6.9
    /// (<c>d67dfdd</c>). ⭐ Am 16.10.2026 nachgesehen:
    /// <c>git log d67dfdd..a44a957 -- Scripts/Import Scripts/Editor</c> ist LEER —
    /// seit 0.6.9 hat sich an keinem Teil etwas geaendert, also steht alles auf 1.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, int> Noetig = new Dictionary<string, int>
    {
        ["karten"] = 1,
        ["spielstaende"] = 1,
        ["gebaeude"] = 1,
        ["kachelsaetze"] = 1,
        ["tabellen"] = 1,
        ["kampagne"] = 1,
        ["einheiten"] = 1,
        ["oberflaeche"] = 1,
        ["hilfe"] = 1,
        ["briefings"] = 1,
        ["ton"] = 1,
    };

    /// <summary>Der Stand, den die Rueckdatierung einem Teil ohne Stempel
    /// gutschreibt: der von 0.6.9. NIE aendern.</summary>
    public const int StandVon069 = 1;

    public static Teil? TeilNach(string name) => Teile.FirstOrDefault(t => t.Name == name);

    /// <summary>Die Spielversion fuer den Stempel. Im Hauptfaden gesetzt (der
    /// Import laeuft auf einem Nebenlaeufer, ProjectSettings gehoert dorthin
    /// nicht).</summary>
    public static string Version = "?";

    public static void VersionMerken()
    {
        try
        {
            var v = Godot.ProjectSettings.GetSetting("application/config/version");
            Version = v.VariantType == Godot.Variant.Type.Nil ? "?" : v.AsString();
        }
        catch (Exception) { }
    }

    // ---- lesen ----------------------------------------------------------------

    /// <summary>Was in der Stempeldatei steht, oder null.</summary>
    public sealed class Stempel
    {
        public Dictionary<string, int> Teile = new();
        public string Version = "", Datum = "";
        public List<string> QuelleWurzeln = new();
        public string? QuelleExe, QuelleKabinett;
    }

    public static Stempel? Lesen(string wurzel)
    {
        string p = Pfad(wurzel);
        if (!File.Exists(p)) return null;
        try
        {
            var n = JsonNode.Parse(File.ReadAllText(p))!.AsObject();
            var s = new Stempel
            {
                Version = n["spielversion"]?.GetValue<string>() ?? "",
                Datum = n["datum"]?.GetValue<string>() ?? "",
            };
            if (n["teile"] is JsonObject t)
                foreach (var kv in t)
                    if (kv.Value != null && kv.Value.GetValueKind() == JsonValueKind.Number)
                        s.Teile[kv.Key] = kv.Value.GetValue<int>();
            if (n["quelle"] is JsonObject q)
            {
                if (q["wurzeln"] is JsonArray a)
                    foreach (var r in a) if (r != null) s.QuelleWurzeln.Add(r.GetValue<string>());
                s.QuelleExe = q["exe"]?.GetValue<string>();
                s.QuelleKabinett = q["kabinett"]?.GetValue<string>();
            }
            return s;
        }
        catch (Exception e)
        {
            // ⚠ eine kaputte Datei ist KEIN »alles aktuell«: sie zaehlt wie keine
            Godot.GD.PrintErr($"inhaltsstand: {p} nicht lesbar ({e.Message}) — gilt als fehlend");
            return null;
        }
    }

    public static string Pfad(string wurzel) => wurzel.TrimEnd('/', '\\') + "/" + Datei;

    /// <summary>Der Stand, den ein Teil hat: aus dem Stempel, sonst ueber die
    /// Rueckdatierung (0 oder <see cref="StandVon069"/>).</summary>
    public static int StandVon(string wurzel, Teil t, Stempel? s, bool streng)
    {
        if (s != null) return s.Teile.TryGetValue(t.Name, out int n) ? n : 0;
        if (streng) return 0;
        return Rueckdatiert(wurzel, t) ? StandVon069 : 0;
    }

    /// <summary>Sind alle Merkdateien des Teils da und juenger als sein
    /// Stichtag? ⚠ UNSERE SETZUNG (siehe Klassenkommentar).</summary>
    public static bool Rueckdatiert(string wurzel, Teil t)
    {
        DateTime? aeltest = AeltesteMerkdatei(wurzel, t);
        if (aeltest == null) return false;
        if (t.Stichtag == null) return true;
        return new DateTimeOffset(aeltest.Value.ToUniversalTime(), TimeSpan.Zero) >= t.Stichtag.Value;
    }

    /// <summary>Die Schreibzeit der aeltesten Merkdatei, oder null, wenn eine
    /// fehlt.</summary>
    public static DateTime? AeltesteMerkdatei(string wurzel, Teil t)
    {
        DateTime? min = null;
        string w = wurzel.TrimEnd('/', '\\');
        foreach (string m in t.Merk)
        {
            var files = new List<string>();
            if (m.Contains('*'))
            {
                string dir = w + "/" + Path.GetDirectoryName(m)!.Replace('\\', '/');
                try { if (Directory.Exists(dir)) files.AddRange(Directory.GetFiles(dir, Path.GetFileName(m))); }
                catch (Exception) { }
            }
            else if (File.Exists(w + "/" + m)) files.Add(w + "/" + m);
            if (files.Count == 0) return null;
            foreach (string f in files)
            {
                DateTime d = File.GetLastWriteTimeUtc(f);
                if (min == null || d < min) min = d;
            }
        }
        return min;
    }

    /// <summary>
    /// <b>DER PRUEFER.</b> Welche Teile unter <paramref name="wurzel"/> veraltet
    /// sind. Leer, wenn der Pruefer aus ist, wenn dort gar nichts eingelesen ist
    /// (der Erstimport ist ein anderer Schirm) oder wenn alles aktuell ist.
    /// <paramref name="noetig"/> ersetzt die Tafel — nur fuer den Pruefstand.
    /// </summary>
    public static List<Teil> Veraltet(string wurzel, IReadOnlyDictionary<string, int>? noetig = null,
                                      bool? streng = null)
    {
        var res = new List<Teil>();
        if (Aus) return res;
        if (!File.Exists(wurzel.TrimEnd('/', '\\') + "/Maps/map_NET07.entities.json")) return res;
        noetig ??= Noetig;
        var s = Lesen(wurzel);
        bool st = streng ?? Streng;
        foreach (var t in Teile)
        {
            int soll = noetig.TryGetValue(t.Name, out int n) ? n : 0;
            if (StandVon(wurzel, t, s, st) < soll) res.Add(t);
        }
        return res;
    }

    /// <summary>Eine Zeile fuer das Protokoll: »karten 0/1, …«.</summary>
    public static string Beschreiben(string wurzel)
    {
        var s = Lesen(wurzel);
        var sb = new StringBuilder(s == null ? "kein Stempel" + (Streng ? " (streng)" : " (Rueckdatierung)")
                                             : $"Stempel {s.Version} {s.Datum}");
        sb.Append(':');
        foreach (var t in Teile)
            sb.Append($" {t.Name} {StandVon(wurzel, t, s, Streng)}/{(Noetig.TryGetValue(t.Name, out int n) ? n : 0)}");
        return sb.ToString();
    }

    // ---- schreiben ------------------------------------------------------------

    private static readonly object Schloss = new();

    /// <summary>
    /// Den erreichten Stand der genannten Teile eintragen (= <see cref="Noetig"/>).
    /// Gibt es noch keinen Stempel, werden die uebrigen Teile mit dem Stand
    /// eingetragen, den die Rueckdatierung ihnen gibt — damit ein einzelner
    /// Teil-Neuexport nicht still alle anderen als »Stand 0« festschreibt und
    /// auch nicht als aktuell.
    /// </summary>
    public static void Erreicht(string wurzel, IEnumerable<string> teile, Core.ContentSources.Source? quelle)
    {
        lock (Schloss)
        {
            try
            {
                Directory.CreateDirectory(wurzel);
                var alt = Lesen(wurzel);
                var stand = new Dictionary<string, int>();
                foreach (var t in Teile) stand[t.Name] = StandVon(wurzel, t, alt, Streng);
                if (alt != null) foreach (var kv in alt.Teile) stand[kv.Key] = kv.Value;
                var neu = teile.ToList();
                foreach (string n in neu)
                    stand[n] = Noetig.TryGetValue(n, out int v) ? v : 0;

                var o = new JsonObject
                {
                    ["_wozu"] = "Welcher Stand des Importeurs diese Inhalte geschrieben hat — siehe Scripts/Import/InhaltsStand.cs",
                    ["spielversion"] = Version,
                    ["datum"] = DateTimeOffset.Now.ToString("yyyy-MM-ddTHH:mm:sszzz"),
                    ["zuletzt"] = new JsonArray(neu.Select(x => (JsonNode?)JsonValue.Create(x)).ToArray()),
                };
                var tj = new JsonObject();
                foreach (var kv in stand.OrderBy(k => k.Key, StringComparer.Ordinal)) tj[kv.Key] = kv.Value;
                o["teile"] = tj;

                // die Quelle merken, damit der Pruefer sie beim naechsten Mal
                // selbst anbieten kann — ohne das Zwischenlager (das ist danach weg)
                var wurzeln = new List<string>();
                if (quelle != null)
                    foreach (string r in quelle.Roots)
                        if (!r.Replace('\\', '/').Contains("/cd_zwischenlager", StringComparison.OrdinalIgnoreCase))
                            wurzeln.Add(r);
                if (wurzeln.Count == 0 && alt != null) wurzeln.AddRange(alt.QuelleWurzeln);
                var q = new JsonObject { ["wurzeln"] = new JsonArray(wurzeln.Select(x => (JsonNode?)JsonValue.Create(x)).ToArray()) };
                string? exe = quelle?.Exe, cab = quelle?.Cabinet;
                if (exe != null && exe.Contains("cd_zwischenlager", StringComparison.OrdinalIgnoreCase)) exe = null;
                if (cab != null && cab.Contains("cd_zwischenlager", StringComparison.OrdinalIgnoreCase)) cab = null;
                q["exe"] = exe ?? alt?.QuelleExe;
                q["kabinett"] = cab ?? alt?.QuelleKabinett;
                o["quelle"] = q;

                string p = Pfad(wurzel);
                File.WriteAllText(p + ".tmp", o.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }),
                                  new UTF8Encoding(false));
                File.Move(p + ".tmp", p, true);
                Godot.GD.Print($"inhaltsstand: {string.Join(", ", neu)} auf aktuellem Stand eingetragen ({p})");
            }
            catch (Exception e) { Godot.GD.PrintErr("inhaltsstand: Stempel nicht geschrieben: " + e.Message); }
        }
    }

    // ---- die Quelle -----------------------------------------------------------

    /// <summary>
    /// Wo die Teile neu eingelesen werden koennen, ohne dass der Spieler etwas
    /// waehlen muss — in dieser Reihenfolge: die eingelegten CDs, dann die beim
    /// letzten Einlesen gemerkte Quelle (soweit noch vorhanden), dann eine
    /// Installation auf der Platte (<see cref="ContentBuilder.ZeigerQuellen"/>).
    /// Alles, was davon da ist, wird zu EINER Quelle vereint (CDs zuerst).
    /// ⚠ UNSERE Reihenfolge.
    /// </summary>
    public static Core.ContentSources.Source? QuelleFinden(string wurzel)
    {
        var wurzeln = new List<string>();
        string? exe = null, cab = null;
        var cds = Core.ContentSources.Discs();
        if (cds != null) { wurzeln.AddRange(cds.Roots); cab ??= cds.Cabinet; exe ??= cds.Exe; }
        var s = Lesen(wurzel);
        if (s != null)
        {
            foreach (string r in s.QuelleWurzeln)
                if (Directory.Exists(r) && !wurzeln.Contains(r, StringComparer.OrdinalIgnoreCase)) wurzeln.Add(r);
            if (s.QuelleExe != null && File.Exists(s.QuelleExe)) exe ??= s.QuelleExe;
            if (s.QuelleKabinett != null && File.Exists(s.QuelleKabinett)) cab ??= s.QuelleKabinett;
        }
        try
        {
            foreach (var q in ContentBuilder.ZeigerQuellen())
                foreach (string r in q.Roots)
                    if (!wurzeln.Contains(r, StringComparer.OrdinalIgnoreCase)
                        && Core.ContentSources.ExeIn(r) != null)
                    {
                        wurzeln.Add(r);
                        exe ??= q.Exe;
                        cab ??= q.Cabinet;
                    }
        }
        catch (Exception) { }
        if (wurzeln.Count == 0) return null;
        var src = new Core.ContentSources.Source
        {
            Kind = cds != null ? Core.ContentSources.Kind.Disc : Core.ContentSources.Kind.Installation,
            Label = string.Join(" + ", wurzeln),
            Exe = exe,
            Cabinet = cab,
        };
        src.Roots.AddRange(wurzeln);
        foreach (string r in wurzeln) { src.Exe ??= Core.ContentSources.ExeIn(r); src.Cabinet ??= Core.ContentSources.CabinetIn(r); }
        return src;
    }

    /// <summary>Die Kampagnenmissionen, die unter <paramref name="wurzel"/>
    /// eingelesen sind (numerische <c>Maps/map_NN.json</c>).</summary>
    public static SortedSet<int> EingeleseneMissionen(string wurzel)
    {
        var set = new SortedSet<int>();
        string dir = wurzel.TrimEnd('/', '\\') + "/Maps";
        if (!Directory.Exists(dir)) return set;
        foreach (string p in Directory.GetFiles(dir, "map_*.json"))
        {
            string n = Path.GetFileNameWithoutExtension(p);
            if (n.EndsWith(".entities", StringComparison.Ordinal)) continue;
            if (int.TryParse(n["map_".Length..], out int no) && no >= 1 && no <= Core.DiscCoverage.CampaignCount)
                set.Add(no);
        }
        return set;
    }

    /// <summary>Welche eingelesenen Missionen eine Quelle NICHT traegt — fuer
    /// die Teile mit <see cref="Bedarf.AlleKarten"/>. Leer = sie deckt alles.</summary>
    public static List<int> FehlendeKarten(string wurzel, Core.ContentSources.Source src)
    {
        var hat = Core.DiscCoverage.MissionsIn(src);
        return EingeleseneMissionen(wurzel).Where(m => !hat.Contains(m)).ToList();
    }
}
