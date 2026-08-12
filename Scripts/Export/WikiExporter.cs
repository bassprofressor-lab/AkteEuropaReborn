using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Godot;

namespace AkteEuropaReborn.Export;

/// <summary>
/// <b>Das Wiki — Frontmatter und Fließtext je Einheit, Gebäude und Mission,
/// als Astro-Content-Collection.</b>
///
/// <para>Aufruf: <c>--wiki-export=&lt;ordner&gt;</c>. Geschrieben wird</para>
/// <code>
///   &lt;ordner&gt;/einheiten/&lt;name&gt;.md   + &lt;name&gt;.png
///   &lt;ordner&gt;/gebaeude/&lt;name&gt;.md    + &lt;name&gt;.png
///   &lt;ordner&gt;/missionen/&lt;nn-name&gt;.md + &lt;nn-name&gt;.png
///   &lt;ordner&gt;/einheiten/alle-entwuerfe.md   (Sammeltabelle für den Rest)
///   &lt;ordner&gt;/content.config.ts             (Schema-Vorschlag mit image())
/// </code>
///
/// <para><b>Warum das in der Engine steht und nicht als Python-Werkzeug
/// daneben:</b> die Werte eines Entwurfs — Preis, Trefferpunkte, Angriff,
/// Reichweite — rechnet <see cref="Simulation.DesignMath"/> aus, und die Bilder
/// setzt <see cref="Import.UnitsExporter"/> aus Fahrwerk und Turm zusammen. Ein
/// zweites Werkzeug müsste beides nachbauen und würde früher oder später
/// abweichen; am 14.08.2026 hat genau so eine Nachbildung eine falsche Antwort
/// geliefert (andere Linie, Abstand 7 statt 122). Das Wiki liest deshalb
/// dieselben Quellen wie das Spiel.</para>
///
/// <para>⚠ <b>Was hier NICHT passiert: erfinden.</b> Der Fließtext beschreibt,
/// was in den Daten steht, und nennt die Herkunft. Wo eine Zahl unsere Setzung
/// ist, sagt der Text das — er dichtet keine Spielwelt dazu.</para>
/// </summary>
public static class WikiExporter
{
    private const string Front = "---";

    public static void Run(string dst, Action<string>? say = null)
    {
        Directory.CreateDirectory(dst);
        int units = WriteUnits(Path.Combine(dst, "einheiten"), say);
        int builds = WriteBuildings(Path.Combine(dst, "gebaeude"), say);
        int miss = WriteMissions(Path.Combine(dst, "missionen"), say);
        WriteSchema(dst);
        WriteReadme(dst, units, builds, miss);
        say?.Invoke($"wiki-export: {units} Einheiten, {builds} Gebaeude, {miss} Missionen -> {dst}");
    }

    // ---- Einheiten ----------------------------------------------------------

    private static int WriteUnits(string dir, Action<string>? say)
    {
        Directory.CreateDirectory(dir);
        UI.UnitStatBook.Load();
        int n = 0;
        var rest = new List<UI.UnitStatBook.Entry>();
        foreach (var e in UI.UnitStatBook.All())
        {
            string slug = Slug(e.Name);
            if (slug.Length == 0) continue;
            string? img = UnitImage(e, dir, slug);
            if (img == null) { rest.Add(e); continue; }
            File.WriteAllText(Path.Combine(dir, slug + ".md"), UnitPage(e, slug, img),
                              new UTF8Encoding(false));
            n++;
        }
        if (rest.Count > 0)
            File.WriteAllText(Path.Combine(dir, "alle-entwuerfe.md"), RestTable(rest),
                              new UTF8Encoding(false));
        WriteComboTable(dir, say);
        say?.Invoke($"  Einheiten: {n} mit Bild, {rest.Count} in der Sammeltabelle");
        return n;
    }

    /// <summary>Das Bild eines Entwurfs neben die Seite legen. Genommen wird das
    /// zusammengesetzte Bild (Fahrwerk + Turm) in Blickrichtung 2, das der
    /// Import schon geschrieben hat; für Fußsoldaten deren eigenes Bild.
    /// Gibt den Dateinamen zurück oder <c>null</c>, wenn es kein Bild gibt —
    /// dann landet der Entwurf in der Sammeltabelle statt auf einer leeren
    /// Seite.</summary>
    private static string? UnitImage(UI.UnitStatBook.Entry e, string dir, string slug)
    {
        int turret = TurretRow(e.Weapon);
        string[] tries =
        {
            $"Units/composed/{e.Propulsion}_{turret}/f2.png",
            $"Units/infantry/{e.Propulsion}/f2.png",
            $"Units/{e.Propulsion}/f2.png",
        };
        foreach (string rel in tries)
        {
            string p = Core.Content.Path(rel);
            if (!Godot.FileAccess.FileExists(p)) continue;
            var img = Image.LoadFromFile(p);
            if (img == null) continue;
            string outp = Path.Combine(dir, slug + ".png");
            img.SavePng(outp);
            return slug + ".png";
        }
        return null;
    }

    /// <summary>Entwurfswaffe → Aufsatz, dieselbe Abbildung wie im Zeichner
    /// (Zeilen 1..19 sind die Geschütze, 65..79 die Ausrüstung).</summary>
    private static int TurretRow(int w)
    {
        if (w is >= 1 and <= 19) return w + 20;
        return w switch
        {
            66 => 40, 67 => 41, 68 => 42, 70 => 43, 65 => 44, 69 => 45, 71 => 46,
            72 => 47, 73 => 48, 74 => 49, 75 => 50, 76 => 51, 77 => 52, 78 => 53,
            79 => 54, _ => 0,
        };
    }

    private static string UnitPage(UI.UnitStatBook.Entry e, string slug, string img)
    {
        // Das Original haengt an jeden Bauteilnamen ein »(n)« — die Ausbaustufe
        // aus Satz +0x01. Im Spiel gehoert sie dazu; auf einer Wiki-Seite liest
        // sie sich als Rauschen, also steht sie als eigenes Feld daneben.
        var (waffe, waffeStufe) = SplitLabel(UI.UnitStatBook.ComponentLongLabel(e.Weapon));
        var (fahrwerk, fahrwerkStufe) = SplitLabel(UI.UnitStatBook.ComponentLongLabel(e.Propulsion));
        var (ausr, ausrStufe) = SplitLabel(UI.UnitStatBook.ComponentLongLabel(e.Equip));
        var sb = new StringBuilder();
        sb.AppendLine(Front);
        sb.AppendLine($"title: {Yaml(e.Name)}");
        sb.AppendLine($"slug: {slug}");
        sb.AppendLine("art: einheit");
        sb.AppendLine($"bild: ./{img}");
        sb.AppendLine($"fahrwerk: {Yaml(fahrwerk.Length > 0 ? fahrwerk : e.Propulsion.ToString())}");
        sb.AppendLine($"fahrwerkZeile: {e.Propulsion}");
        if (fahrwerkStufe >= 0) sb.AppendLine($"fahrwerkStufe: {fahrwerkStufe}");
        if (waffe.Length > 0) sb.AppendLine($"waffe: {Yaml(waffe)}");
        sb.AppendLine($"waffeZeile: {e.Weapon}");
        if (waffeStufe >= 0) sb.AppendLine($"waffeStufe: {waffeStufe}");
        if (ausr.Length > 0) sb.AppendLine($"ausruestung: {Yaml(ausr)}");
        if (ausrStufe >= 0) sb.AppendLine($"ausruestungStufe: {ausrStufe}");
        sb.AppendLine("preis:");
        sb.AppendLine($"  waffen: {e.CostW}");
        sb.AppendLine($"  fahrwerk: {e.CostF}");
        sb.AppendLine($"  spezial: {e.CostS}");
        sb.AppendLine("werte:");
        sb.AppendLine($"  trefferpunkte: {e.Hp}");
        sb.AppendLine($"  angriff: {e.Attack}");
        sb.AppendLine($"  verteidigung: {e.Defence}");
        sb.AppendLine($"  reichweite: {e.Range}");
        sb.AppendLine($"  sicht: {e.Sight}");
        sb.AppendLine($"  nachladen: {e.Reload}");
        sb.AppendLine($"  geschwindigkeit: {e.Speed}");
        sb.AppendLine($"  sprit: {e.Fuel}");
        sb.AppendLine($"  munition: {e.Ammo}");
        sb.AppendLine("quelle: \"GAME.EXE, Entwurfstabelle sec47 und Bauteiltabelle\"");
        sb.AppendLine(Front);
        sb.AppendLine();

        bool armed = e.Attack > 0 && e.Range > 0;
        sb.Append($"**{e.Name}** ");
        sb.Append(fahrwerk.Length > 0 ? $"steht auf dem Fahrwerk {fahrwerk}" : "ist ein Entwurf");
        if (waffe.Length > 0) sb.Append($" und trägt {waffe}");
        sb.AppendLine(".");
        sb.AppendLine();
        if (armed)
        {
            sb.AppendLine($"Er greift mit **{e.Attack}** an, hält **{e.Hp}** Trefferpunkte aus " +
                          $"und verteidigt mit **{e.Defence}**. Seine Reichweite beträgt " +
                          $"**{e.Range}** Felder, nachgeladen wird alle **{e.Reload}**.");
        }
        else
        {
            sb.AppendLine($"Der Entwurf ist **unbewaffnet** — er hat weder Angriffswert noch " +
                          $"Reichweite und kommt im Original gar nicht erst in den Kampfblock. " +
                          $"Er hält **{e.Hp}** Trefferpunkte aus.");
        }
        sb.AppendLine();
        sb.AppendLine($"Er sieht **{e.Sight}** Felder weit, fährt mit Tempo **{e.Speed}** und " +
                      $"führt **{e.Fuel}** Sprit und **{e.Ammo}** Schuss mit.");
        sb.AppendLine();
        sb.AppendLine($"Gebaut wird er in der **Basis** — nicht in der Fabrik, die nur die Teile " +
                      $"herstellt — und kostet **{e.CostW}** Waffen-, **{e.CostF}** Fahrwerk- und " +
                      $"**{e.CostS}** Spezialteile.");
        sb.AppendLine();
        sb.AppendLine("<small>Alle Zahlen sind aus der Entwurfstabelle des Originals gelesen " +
                      "und mit dessen eigener Preisformel gerechnet.</small>");
        return sb.ToString();
    }

    private static string RestTable(List<UI.UnitStatBook.Entry> rest)
    {
        var sb = new StringBuilder();
        sb.AppendLine(Front);
        sb.AppendLine("title: \"Weitere Entwürfe\"");
        sb.AppendLine("slug: alle-entwuerfe");
        sb.AppendLine("art: sammelseite");
        sb.AppendLine(Front);
        sb.AppendLine();
        sb.AppendLine("Diese Entwürfe stehen in der Tabelle des Originals, es gibt für sie aber " +
                      "kein zusammengesetztes Bild — meist, weil ihr Fahrwerk oder ihr Aufsatz " +
                      "nicht in den Bildbänken liegt. Die Zahlen sind dieselben wie auf den " +
                      "Einzelseiten.");
        sb.AppendLine();
        sb.AppendLine("| Entwurf | Fahrwerk | Waffe | TP | Angriff | Vert. | Reichw. | Preis (W/F/S) |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|");
        rest.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
        foreach (var e in rest)
            sb.AppendLine($"| {e.Name} | {e.Propulsion} | {e.Weapon} | {e.Hp} | {e.Attack} | " +
                          $"{e.Defence} | {e.Range} | {e.CostW}/{e.CostF}/{e.CostS} |");
        return sb.ToString();
    }

    // ---- Gebäude ------------------------------------------------------------

    private static int WriteBuildings(string dir, Action<string>? say)
    {
        Directory.CreateDirectory(dir);
        var root = LoadJson("Maps/building_types.json");
        if (root == null || !root.TryGetValue("types", out var tv)) return 0;
        var types = tv.AsGodotDictionary<string, Variant>();
        int n = 0;
        foreach (var kv in types)
        {
            if (!int.TryParse(kv.Key, out int typ)) continue;
            var t = kv.Value.AsGodotDictionary<string, Variant>();
            string name = t.TryGetValue("name", out var nv) ? nv.AsString() : "";
            if (name.Length == 0) continue;
            string slug = Slug(name);
            var sb = new StringBuilder();
            sb.AppendLine(Front);
            sb.AppendLine($"title: {Yaml(name)}");
            sb.AppendLine($"slug: {slug}");
            sb.AppendLine("art: gebaeude");
            sb.AppendLine($"typ: {typ}");
            foreach (string f in new[] { "hp", "doors", "door_count", "sight_col", "sight_row", "count" })
                if (t.TryGetValue(f, out var v)) sb.AppendLine($"{f}: {v.AsInt32()}");
            string bild = BuildingImage(typ, dir, slug);
            if (bild.Length > 0) sb.AppendLine($"bild: ./{bild}");
            sb.AppendLine("quelle: \"GAME.EXE, Gebaeudetabelle 0x4FDCC4\"");
            sb.AppendLine(Front);
            sb.AppendLine();
            sb.AppendLine($"**{name}** ist Gebäudetyp **{typ}** des Spiels.");
            sb.AppendLine();
            if (t.TryGetValue("hp", out var hp))
                sb.AppendLine($"Es hält **{hp.AsInt32()}** Trefferpunkte aus. Wie lange eine " +
                              "Übernahme dauert, hängt genau daran: eine angeschlagene Anlage " +
                              "fällt schneller.");
            sb.AppendLine();
            sb.AppendLine("Ein Gebäude sieht im Original **zehn Felder** weit — die Zahl steht " +
                          "wörtlich im Code und gilt für alle Arten gleich.");
            File.WriteAllText(Path.Combine(dir, slug + ".md"), sb.ToString(), new UTF8Encoding(false));
            n++;
        }
        say?.Invoke($"  Gebaeude: {n}");
        return n;
    }

    /// <summary>
    /// Das Bild eines Gebäudes — sein erstes Muster, aus den Kacheln des ersten
    /// Kachelsatzes zusammengesetzt, der diesen Typ überhaupt führt.
    ///
    /// <para>Gerechnet wird mit <see cref="Import.MapBaker"/>s eigener Regel
    /// <c>y = zeile·20 + Kachel.YOff + BlitAnchor</c> — dieselbe, mit der die
    /// Karte gebacken wird. Ein Gebäude sieht je Kachelsatz verschieden aus;
    /// genommen wird der erste, der es hat, und die Nummer steht im
    /// Frontmatter.</para></summary>
    private static string BuildingImage(int typ, string dir, string slug)
    {
        for (int ts = 1; ts <= 60; ts++)
        {
            var meta = LoadJson($"Buildings/tileset_{ts:00}.json");
            if (meta == null || !meta.TryGetValue("types", out var tv)) continue;
            // ⚠ `types` ist hier eine LISTE mit einem Feld `typ`, nicht ein
            // Woerterbuch nach Typnummer — anders als in building_types.json.
            Godot.Collections.Dictionary<string, Variant>? t = null;
            foreach (var item in tv.AsGodotArray())
            {
                var d0 = item.AsGodotDictionary<string, Variant>();
                if (d0.TryGetValue("typ", out var ty) && ty.AsInt32() == typ) { t = d0; break; }
            }
            if (t == null || !t.TryGetValue("patterns", out var pv)) continue;
            var pats = pv.AsGodotArray();
            if (pats.Count == 0) continue;
            var pat = pats[0].AsGodotDictionary<string, Variant>();
            if (!pat.TryGetValue("cells", out var cv)) continue;

            var tiles = LoadJson($"Buildings/tileset_{ts:00}_tiles.json");
            if (tiles == null || !tiles.TryGetValue("tiles", out var tlv)) continue;
            var tl = tlv.AsGodotDictionary<string, Variant>();
            string atlasPath = Core.Content.Path($"Buildings/tileset_{ts:00}_tiles.png");
            if (!Godot.FileAccess.FileExists(atlasPath)) continue;
            var atlas = Image.LoadFromFile(atlasPath);
            if (atlas == null) continue;

            const int blit = -50, tw = 40, th = 20, pad = 120;
            int maxC = 0, maxR = 0;
            foreach (var c in cv.AsGodotArray())
            {
                var a = c.AsGodotArray();
                maxC = Mathf.Max(maxC, a[0].AsInt32());
                maxR = Mathf.Max(maxR, a[1].AsInt32());
            }
            var img = Image.CreateEmpty((maxC + 1) * tw + 40, (maxR + 1) * th + pad + 60,
                                        false, atlas.GetFormat());
            img.Fill(new Color(0, 0, 0, 0));
            bool any = false;
            foreach (var c in cv.AsGodotArray())
            {
                var a = c.AsGodotArray();
                int col = a[0].AsInt32(), row = a[1].AsInt32(), id = a[2].AsInt32();
                if (!tl.TryGetValue(id.ToString(), out var ev)) continue;
                var e = ev.AsGodotArray();
                int x = e[0].AsInt32(), y = e[1].AsInt32(), w = e[2].AsInt32(),
                    h = e[3].AsInt32(), yoff = e[4].AsInt32();
                if (w <= 0 || h <= 0) continue;
                img.BlitRect(atlas, new Rect2I(x, y, w, h),
                             new Vector2I(col * tw + 20, row * th + yoff + blit + pad));
                any = true;
            }
            if (!any) continue;
            var box = img.GetUsedRect();
            if (box.Size.X > 0 && box.Size.Y > 0)
            {
                var cut = Image.CreateEmpty(box.Size.X, box.Size.Y, false, img.GetFormat());
                cut.BlitRect(img, box, Vector2I.Zero);
                img = cut;
            }
            img.SavePng(Path.Combine(dir, slug + ".png"));
            return slug + ".png";
        }
        return "";
    }

    // ---- Missionen ----------------------------------------------------------

    private static int WriteMissions(string dir, Action<string>? say)
    {
        Directory.CreateDirectory(dir);
        var root = LoadJson("Maps/campaign.json");
        if (root == null || !root.TryGetValue("missions", out var mv)) return 0;
        // Der Einsatzbericht des Originals — das ist der eigentliche Text einer
        // Mission, und er steht schon eingespielt da.
        var briefs = new Dictionary<int, List<string>>();
        var br = LoadJson("Maps/briefings.json");
        if (br != null && br.TryGetValue("briefings", out var bv))
            foreach (var kv in bv.AsGodotDictionary<string, Variant>())
                if (int.TryParse(kv.Key, out int bi))
                {
                    var d = kv.Value.AsGodotDictionary<string, Variant>();
                    if (!d.TryGetValue("paragraphs", out var pv)) continue;
                    var list = new List<string>();
                    foreach (var p in pv.AsGodotArray()) list.Add(p.AsString());
                    briefs[bi] = list;
                }
        int n = 0;
        foreach (var item in mv.AsGodotArray())
        {
            var m = item.AsGodotDictionary<string, Variant>();
            int idx = m.TryGetValue("index", out var iv) ? iv.AsInt32() : 0;
            string title = m.TryGetValue("title", out var tv) ? tv.AsString() : $"Mission {idx}";
            string slug = $"{idx:00}-{Slug(title)}";
            var sb = new StringBuilder();
            sb.AppendLine(Front);
            sb.AppendLine($"title: {Yaml(title)}");
            sb.AppendLine($"slug: {slug}");
            sb.AppendLine("art: mission");
            sb.AppendLine($"nummer: {idx}");
            foreach (string f in new[] { "map", "tileset", "width", "height" })
                if (m.TryGetValue(f, out var v))
                    sb.AppendLine($"{f}: {(v.VariantType == Variant.Type.String ? Yaml(v.AsString()) : v.AsInt32().ToString())}");
            int pay = Campaign.CampaignManager.PayFor(idx);
            if (pay > 0) sb.AppendLine($"bezahlung: {pay}");
            string bild = MissionImage(idx, dir, slug);
            if (bild.Length > 0) sb.AppendLine($"bild: ./{bild}");
            sb.AppendLine("quelle: \"Kartendateien des Originals und GAME.EXE\"");
            sb.AppendLine(Front);
            sb.AppendLine();
            sb.AppendLine($"**Mission {idx} — {title}.**");
            sb.AppendLine();
            if (m.TryGetValue("width", out var w) && m.TryGetValue("height", out var h))
                sb.AppendLine($"Die Karte misst **{w.AsInt32()} × {h.AsInt32()}** Felder.");
            if (pay > 0)
            {
                sb.AppendLine();
                sb.AppendLine($"Für die Mission zahlt der Auftraggeber **{pay} $**. Das ist ein " +
                              "fester Betrag je Mission — er hängt nicht daran, wieviel unterwegs " +
                              "zerstört wurde. Der Kontostand geht in die nächste Mission mit.");
            }
            if (briefs.TryGetValue(idx, out var paras) && paras.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("## Einsatzbericht");
                sb.AppendLine();
                foreach (string p in paras)
                {
                    if (p.Trim().Length == 0) continue;
                    sb.AppendLine(p.Trim());
                    sb.AppendLine();
                }
                sb.AppendLine("<small>Wortlaut des Originals, aus <code>BRIEFG.DAT</code> " +
                              "eingespielt — nicht nacherzählt.</small>");
            }
            File.WriteAllText(Path.Combine(dir, slug + ".md"), sb.ToString(), new UTF8Encoding(false));
            n++;
        }
        say?.Invoke($"  Missionen: {n}");
        return n;
    }

    /// <summary>Das Radarbild der Mission — das Original schliesst darauf ein
    /// Fadenkreuz über dem Einsatzort in Europa; genommen wird das letzte der
    /// zehn Bilder.</summary>
    private static string MissionImage(int idx, string dir, string slug)
    {
        for (int f = 9; f >= 0; f--)
        {
            string p = Core.Content.Path($"UI/radar/{idx}/f{f}.png");
            if (!Godot.FileAccess.FileExists(p)) continue;
            var img = Image.LoadFromFile(p);
            if (img == null) continue;
            img.SavePng(Path.Combine(dir, slug + ".png"));
            return slug + ".png";
        }
        return "";
    }

    // ---- das Schema ---------------------------------------------------------

    private static void WriteSchema(string dst)
    {
        const string ts = """
        // Vorschlag für die Content-Collections. Die Bilder liegen NEBEN den
        // Markdown-Dateien, damit `image()` sie optimieren kann.
        import { defineCollection, z } from 'astro:content';
        import { glob } from 'astro/loaders';

        const einheiten = defineCollection({
          loader: glob({ pattern: '**/*.md', base: './src/content/einheiten' }),
          schema: ({ image }) => z.object({
            title: z.string(),
            slug: z.string(),
            art: z.literal('einheit').or(z.literal('sammelseite')),
            bild: image().optional(),
            fahrwerk: z.string().optional(),
            fahrwerkZeile: z.number().optional(),
            waffe: z.string().optional(),
            waffeZeile: z.number().optional(),
            ausruestung: z.string().optional(),
            preis: z.object({
              waffen: z.number(), fahrwerk: z.number(), spezial: z.number(),
            }).optional(),
            werte: z.object({
              trefferpunkte: z.number(), angriff: z.number(), verteidigung: z.number(),
              reichweite: z.number(), sicht: z.number(), nachladen: z.number(),
              geschwindigkeit: z.number(), sprit: z.number(), munition: z.number(),
            }).optional(),
            quelle: z.string().optional(),
          }),
        });

        const gebaeude = defineCollection({
          loader: glob({ pattern: '**/*.md', base: './src/content/gebaeude' }),
          schema: ({ image }) => z.object({
            title: z.string(),
            slug: z.string(),
            art: z.literal('gebaeude'),
            typ: z.number(),
            bild: image().optional(),
            hp: z.number().optional(),
            doors: z.number().optional(),
            quelle: z.string().optional(),
          }),
        });

        const missionen = defineCollection({
          loader: glob({ pattern: '**/*.md', base: './src/content/missionen' }),
          schema: ({ image }) => z.object({
            title: z.string(),
            slug: z.string(),
            art: z.literal('mission'),
            nummer: z.number(),
            map: z.string().optional(),
            tileset: z.number().optional(),
            width: z.number().optional(),
            height: z.number().optional(),
            bezahlung: z.number().optional(),
            bild: image().optional(),
            quelle: z.string().optional(),
          }),
        });

        export const collections = { einheiten, gebaeude, missionen };
        """;
        File.WriteAllText(Path.Combine(dst, "content.config.ts"), ts, new UTF8Encoding(false));
    }

    /// <summary>Die Anleitung fürs Paket — wer das hochlädt, soll nicht raten
    /// müssen, wohin was gehört.</summary>
    private static void WriteReadme(string dst, int units, int builds, int miss)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Akte Europa Reborn — Wiki-Inhalte");
        sb.AppendLine();
        sb.AppendLine("Erzeugt aus der Engine mit `--wiki-export=<ordner>`. Alle Zahlen und");
        sb.AppendLine("Texte stammen aus dem eingespielten Originalspiel, nicht aus zweiter Hand.");
        sb.AppendLine();
        sb.AppendLine("## Was drin ist");
        sb.AppendLine();
        sb.AppendLine("| Ordner | Inhalt |");
        sb.AppendLine("|---|---|");
        sb.AppendLine($"| `einheiten/` | {units} Entwürfe mit Bild, je eine Seite. Dazu " +
                      "`alle-entwuerfe.md` (Entwürfe ohne Bild) und " +
                      "`alle-bauteil-kombinationen.md` (die 601 Rohzeilen der Tabelle). |");
        sb.AppendLine($"| `gebaeude/` | {builds} Gebäudearten mit Bild. |");
        sb.AppendLine($"| `missionen/` | {miss} Missionen mit Radarbild und dem " +
                      "Einsatzbericht im Wortlaut des Originals. |");
        sb.AppendLine("| `content.config.ts` | Vorschlag für die Content-Collections. |");
        sb.AppendLine();
        sb.AppendLine("Jede Seite ist Markdown mit YAML-Frontmatter; das Bild liegt **neben**");
        sb.AppendLine("der Datei und wird im Frontmatter als `bild: ./name.png` genannt.");
        sb.AppendLine();
        sb.AppendLine("## Einbauen (Astro)");
        sb.AppendLine();
        sb.AppendLine("```");
        sb.AppendLine("src/content/einheiten/   <- Inhalt von einheiten/");
        sb.AppendLine("src/content/gebaeude/    <- Inhalt von gebaeude/");
        sb.AppendLine("src/content/missionen/   <- Inhalt von missionen/");
        sb.AppendLine("src/content.config.ts    <- content.config.ts (oder in eine");
        sb.AppendLine("                            vorhandene Datei einarbeiten)");
        sb.AppendLine("```");
        sb.AppendLine();
        sb.AppendLine("Die Bilder bleiben bei den Markdown-Dateien, damit Astros `image()`");
        sb.AppendLine("sie optimieren kann. Das mitgelieferte Schema erwartet genau diese Lage.");
        sb.AppendLine();
        sb.AppendLine("## Was die Frontmatter-Felder bedeuten");
        sb.AppendLine();
        sb.AppendLine("* `waffeZeile`, `fahrwerkZeile` — die Zeilennummer in der Bauteiltabelle");
        sb.AppendLine("  des Originals. Nützlich zum Querverweisen, nicht zum Anzeigen.");
        sb.AppendLine("* `waffeStufe`, `fahrwerkStufe` — die Ausbaustufe, die das Original hinter");
        sb.AppendLine("  jeden Bauteilnamen in Klammern schreibt.");
        sb.AppendLine("* `preis` — Waffen-, Fahrwerk- und Spezialteile, mit der Preisformel des");
        sb.AppendLine("  Originals gerechnet. Einheiten kosten keine Währung, sondern Teile.");
        sb.AppendLine("* `bezahlung` (Missionen) — ein fester Betrag je Mission, aus GAME.EXE");
        sb.AppendLine("  gelesen; er hängt nicht am Spielverlauf.");
        sb.AppendLine();
        sb.AppendLine("## Woher die Sachen kommen");
        sb.AppendLine();
        sb.AppendLine("* Werte der Entwürfe: Entwurfstabelle sec47 und Bauteiltabelle, gerechnet");
        sb.AppendLine("  mit der Preis- und Wertformel des Originals.");
        sb.AppendLine("* Bilder der Einheiten: zusammengesetzt aus Fahrwerk und Aufsatz,");
        sb.AppendLine("  Blickrichtung 2.");
        sb.AppendLine("* Bilder der Gebäude: erstes Muster aus dem ersten Kachelsatz, der die Art");
        sb.AppendLine("  führt — mit derselben Rechnung, mit der die Karte gebacken wird.");
        sb.AppendLine("* Einsatzberichte: `BRIEFG.DAT`, Wortlaut unverändert.");
        sb.AppendLine("* Radarbilder: `MAP.DAT`, letztes der zehn Bilder je Mission.");
        sb.AppendLine();
        sb.AppendLine("⚠ Der Fließtext beschreibt, **was in den Daten steht**. Es ist keine");
        sb.AppendLine("Spielwelt dazuerfunden; wo eine Zahl unsere eigene Setzung ist, sagt der");
        sb.AppendLine("Text das. Wer Lore ergänzen will, sollte das sichtbar getrennt tun.");
        sb.AppendLine();
        sb.AppendLine("⚠ Neu erzeugen ersetzt den Ordner. Eigene Ergänzungen also nicht direkt");
        sb.AppendLine("in diese Dateien schreiben, sondern in eigene Felder oder eigene Seiten.");
        File.WriteAllText(Path.Combine(dst, "README.md"), sb.ToString(), new UTF8Encoding(false));
    }

    // ---- Kleinkram ----------------------------------------------------------

    /// <summary>Eine JSON-Datei aus dem eingespielten Inhalt lesen — derselbe
    /// Weg, den auch <see cref="Campaign.CampaignManager"/> nimmt.</summary>
    private static Godot.Collections.Dictionary<string, Variant>? LoadJson(string rel)
    {
        string path = Core.Content.Path(rel);
        if (!Godot.FileAccess.FileExists(path)) return null;
        using var f = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Read);
        if (f == null) return null;
        var json = new Json();
        if (json.Parse(f.GetAsText()) != Error.Ok ||
            json.Data.VariantType != Variant.Type.Dictionary) return null;
        return json.Data.AsGodotDictionary<string, Variant>();
    }

    /// <summary>»Zwillingslaser (0)« → («Zwillingslaser», 0). Ohne Klammer
    /// kommt −1 zurück.</summary>
    private static (string Name, int Stufe) SplitLabel(string s)
    {
        int i = s.LastIndexOf(" (", StringComparison.Ordinal);
        if (i < 0 || !s.EndsWith(")", StringComparison.Ordinal)) return (s, -1);
        string num = s[(i + 2)..^1];
        return int.TryParse(num, out int n) ? (s[..i], n) : (s, -1);
    }

    /// <summary>
    /// Die ROHE Bauteil-Kombinationstabelle des Originals als eine Seite.
    ///
    /// <para>⚠ Nicht zu verwechseln mit den Entwürfen: das Spiel führt rund
    /// achtzig benannte Entwürfe (Chaingunner, Laser Trooper …), die auf den
    /// Einzelseiten stehen. <c>unit_designs.json</c> trägt daneben <b>601</b>
    /// Zeilen — jede Kombination aus Fahrwerk, Waffe und Aufbau, die die Tabelle
    /// hergibt, mit automatisch gebildeten Namen wie »H-Cannon-81-165«. Sie
    /// gehören dazu, aber nicht auf je eine eigene Seite.</para></summary>
    private static void WriteComboTable(string dir, Action<string>? say)
    {
        var root = LoadJson("Maps/unit_designs.json");
        if (root == null || !root.TryGetValue("designs", out var dv)) return;
        var designs = dv.AsGodotDictionary<string, Variant>();
        var sb = new StringBuilder();
        sb.AppendLine(Front);
        sb.AppendLine("title: \"Bauteil-Kombinationen\"");
        sb.AppendLine("slug: alle-bauteil-kombinationen");
        sb.AppendLine("art: sammelseite");
        sb.AppendLine($"anzahl: {designs.Count}");
        sb.AppendLine("quelle: \"GAME.EXE, Entwurfstabelle sec47 (Rohzeilen)\"");
        sb.AppendLine(Front);
        sb.AppendLine();
        sb.AppendLine($"Neben den benannten Entwürfen führt das Original **{designs.Count}** " +
                      "Rohzeilen — jede mögliche Kombination aus Fahrwerk, Waffe und Aufbau. " +
                      "Ihre Namen sind maschinell gebildet (»H-Cannon-81-165« heisst: Waffe " +
                      "H-Cannon, Aufbau 81, Fahrwerk 165). Sie stehen hier vollständig, " +
                      "damit nichts fehlt.");
        sb.AppendLine();
        sb.AppendLine("| Zeile | Name | Waffe | Fahrwerk | Aufbau |");
        sb.AppendLine("|---|---|---|---|---|");
        var keys = new List<string>();
        foreach (var k in designs.Keys) keys.Add(k);
        keys.Sort((a, b) => int.TryParse(a, out int x) && int.TryParse(b, out int y)
                                ? x - y : string.CompareOrdinal(a, b));
        foreach (string k in keys)
        {
            var d = designs[k].AsGodotDictionary<string, Variant>();
            string name = d.TryGetValue("name", out var nv) ? nv.AsString() : "";
            int w = d.TryGetValue("weapon", out var wv) ? wv.AsInt32() : 0;
            int p = d.TryGetValue("propulsion", out var pv) ? pv.AsInt32() : 0;
            int b = d.TryGetValue("body", out var bv) ? bv.AsInt32() : 0;
            sb.AppendLine($"| {k} | {name} | {w} | {p} | {b} |");
        }
        File.WriteAllText(Path.Combine(dir, "alle-bauteil-kombinationen.md"),
                          sb.ToString(), new UTF8Encoding(false));
        say?.Invoke($"  Bauteil-Kombinationen: {designs.Count} in einer Tabelle");
    }

    private static string Yaml(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    private static string Slug(string s)
    {
        var sb = new StringBuilder();
        foreach (char c in s.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c)) sb.Append(c switch
            {
                'ä' => "ae", 'ö' => "oe", 'ü' => "ue", _ => c.ToString(),
            });
            else if (c == 'ß') sb.Append("ss");
            else if (sb.Length > 0 && sb[^1] != '-') sb.Append('-');
        }
        return sb.ToString().Trim('-');
    }
}
