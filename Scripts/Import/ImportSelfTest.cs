namespace AkteEuropaReborn.Import;

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Godot;

/// <summary>
/// Checks the ported decoders against the Python tooling that produced the
/// content the game has been running on all along. A port is only worth
/// anything if it agrees pixel for pixel with what was verified before, so
/// this compares against the reference PNGs rather than eyeballing a sprite.
///
/// Run:  Godot --path &lt;proj&gt; -- --selftest-cwp=&lt;aekernel-Ordner&gt;
/// </summary>
public static class ImportSelfTest
{
    /// <summary>Frames cwp_decode.py wrote to cwp_out/ — the reference set.</summary>
    private static readonly int[] Sample =
        { 0, 1, 2, 3, 4, 5, 6, 7, 50, 200, 800, 1200, 1600 };

    public static int RunCwp(string aekernel)
    {
        aekernel = aekernel.TrimEnd('/', '\\');
        string cwp = aekernel + "/DATA/01.CWP";
        string pal = aekernel + "/DATA/01.PAL";
        string refDir = aekernel + "/cwp_out";
        if (!File.Exists(cwp) || !File.Exists(pal))
        {
            GD.PrintErr($"selftest-cwp: {cwp} oder {pal} fehlt");
            return 2;
        }

        var p = PalFile.Load(pal);
        var f = CwpFile.Load(cwp);
        GD.Print($"selftest-cwp: {Path.GetFileName(cwp)} — {f.FrameCount} Kacheln, " +
                 $"{f.ObjectCount} Objekte");

        int ok = 0, bad = 0, skipped = 0;
        var notes = new List<string>();
        foreach (int i in Sample)
        {
            if (i >= f.FrameCount) { skipped++; continue; }
            var fr = f.DecodeFrame(i);
            string refPng = $"{refDir}/frame_{i:0000}.png";
            if (!File.Exists(refPng)) { skipped++; continue; }

            var want = Image.LoadFromFile(refPng);
            if (want == null) { skipped++; continue; }
            var got = CwpFile.ToImage(fr, p);

            if (want.GetWidth() != got.GetWidth() || want.GetHeight() != got.GetHeight())
            {
                bad++;
                notes.Add($"Rahmen {i}: {got.GetWidth()}x{got.GetHeight()} statt " +
                          $"{want.GetWidth()}x{want.GetHeight()}");
                continue;
            }

            int diff = 0;
            for (int y = 0; y < want.GetHeight(); y++)
                for (int x = 0; x < want.GetWidth(); x++)
                {
                    var a = want.GetPixel(x, y);
                    var b = got.GetPixel(x, y);
                    // both fully transparent counts as equal whatever the rgb is
                    if (a.A < 0.5f && b.A < 0.5f) continue;
                    if (a != b) diff++;
                }
            if (diff == 0) ok++;
            else { bad++; notes.Add($"Rahmen {i}: {diff} Pixel abweichend"); }
        }

        GD.Print($"selftest-cwp: {ok} Rahmen deckungsgleich, {bad} abweichend, " +
                 $"{skipped} ohne Vergleichsbild");
        foreach (string n in notes) GD.Print("   " + n);
        return bad == 0 && ok > 0 ? 0 : 1;
    }

    /// <summary>Read every map file with the ported CWM reader and hold the
    /// result against the entities.json the Python tooling wrote — the same
    /// files the game has been running on. Entities are compared by their raw
    /// 78 bytes, which is as exact as a check can get.</summary>
    public static int RunCwm(string aekernel)
    {
        aekernel = aekernel.TrimEnd('/', '\\');
        string levels = aekernel + "/LEVELS";
        string mapOut = aekernel + "/map_out";
        if (!Directory.Exists(levels)) { GD.PrintErr("selftest-cwm: LEVELS fehlt"); return 2; }

        int files = 0, bad = 0, entOk = 0, entBad = 0, bldOk = 0, bldBad = 0, noRef = 0;
        var secCount = new Dictionary<int, int>();
        var paths = new List<string>(Directory.GetFiles(levels, "*.CWM"));
        foreach (string dm in new[] { "1.DM", "3.DM", "4.DM" })
            if (File.Exists(levels + "/" + dm)) paths.Add(levels + "/" + dm);
        paths.Sort();

        foreach (string path in paths)
        {
            CwmFile m;
            try { m = CwmFile.Load(path); }
            catch (Exception e) { bad++; GD.PrintErr($"   {Path.GetFileName(path)}: {e.Message}"); continue; }
            files++;
            // The real criterion is that the file is consumed exactly: how
            // many sections it carries varies. A .CWM stops after 38; of the
            // saved missions 1.DM holds all 131 while 3.DM and 4.DM simply end
            // after 123 — both readers agree, the files are just shorter.
            secCount[m.Sections.Count] = secCount.GetValueOrDefault(m.Sections.Count) + 1;
            if (m.TrailingBytes != 0)
            {
                bad++;
                GD.PrintErr($"   {m.Stem}: {m.TrailingBytes} Bytes nach der letzten Sektion uebrig");
            }

            string refPath = $"{mapOut}/map_{m.Stem}.entities.json";
            if (!File.Exists(refPath)) { noRef++; continue; }
            var root = ReadJson(refPath);
            if (root == null) { noRef++; continue; }

            // entities, byte for byte
            var mine = CwmData.Entities(m);
            if (root.TryGetValue("entities", out var ev) && ev.VariantType == Variant.Type.Array)
            {
                var want = ev.AsGodotArray();
                if (want.Count != mine.Count)
                {
                    entBad++;
                    GD.PrintErr($"   {m.Stem}: {mine.Count} Entities statt {want.Count}");
                }
                else
                {
                    int diff = 0;
                    for (int i = 0; i < want.Count; i++)
                    {
                        var w = want[i].AsGodotDictionary<string, Variant>();
                        string hex = w.TryGetValue("raw", out var rv) ? rv.AsString() : "";
                        if (hex.Length == 0) continue;
                        if (!string.Equals(hex, ToHex(mine[i].Raw), StringComparison.OrdinalIgnoreCase))
                            diff++;
                    }
                    if (diff == 0) entOk++;
                    else { entBad++; GD.PrintErr($"   {m.Stem}: {diff} Entities weichen ab"); }
                }
            }

            // buildings: type, owner, name and position
            var mineB = CwmData.Buildings(m);
            if (root.TryGetValue("buildings", out var bv) && bv.VariantType == Variant.Type.Array)
            {
                var want = bv.AsGodotArray();
                int diff = 0;
                if (want.Count != mineB.Count) diff = 9999;
                else
                    for (int i = 0; i < want.Count; i++)
                    {
                        var w = want[i].AsGodotDictionary<string, Variant>();
                        var g = mineB[i];
                        if (GetI(w, "slot") != g.Slot || GetI(w, "type") != g.Type ||
                            GetI(w, "owner") != g.Owner || GetI(w, "col") != g.Col ||
                            GetI(w, "row") != g.Row || GetI(w, "cis_typ") != g.CisTyp ||
                            GetI(w, "hp") != g.Hp || GetI(w, "hp_max") != g.HpMax ||
                            (w.TryGetValue("name", out var nv) ? nv.AsString() : "") != g.Name)
                            diff++;
                    }
                if (diff == 0) bldOk++;
                else
                {
                    bldBad++;
                    GD.PrintErr($"   {m.Stem}: Gebaeude weichen ab ({(diff == 9999 ? $"{mineB.Count} statt {want.Count}" : diff + " Stueck")})");
                }
            }
        }

        var shape = new List<string>();
        foreach (var kv in secCount) shape.Add($"{kv.Value}x{kv.Key}");
        shape.Sort();
        GD.Print($"selftest-cwm: Sektionszahlen {string.Join(" ", shape)}");
        GD.Print($"selftest-cwm: {files} Kartendateien gelesen, {bad} mit Restbytes; " +
                 $"Entities {entOk} gleich / {entBad} abweichend; " +
                 $"Gebaeude {bldOk} gleich / {bldBad} abweichend; {noRef} ohne Vergleichsdatei");
        return bad == 0 && entBad == 0 && bldBad == 0 && entOk > 0 ? 0 : 1;
    }

    /// <summary>The per-map game state, held against the entities.json the
    /// Python tooling wrote — the same files the game has been running on.
    ///
    /// The comparison is structural and complete: the generated document is
    /// parsed back and walked against the reference key by key, element by
    /// element, including the full zone grid and every spatial cell. A sample
    /// would prove nothing here; these are exactly the places where an
    /// off-by-one hides.</summary>
    public static int RunEntities(string aekernel)
    {
        aekernel = aekernel.TrimEnd('/', '\\');
        string levels = aekernel + "/LEVELS";
        if (!Directory.Exists(levels)) { GD.PrintErr("selftest-ent: LEVELS fehlt"); return 2; }

        // Two places hold Python output. map_out is the working folder, but a
        // later .DM export overwrote four of its files: 10.DM through 13.DM
        // share their stem with 10.CWM through 13.CWM. The copy that went into
        // the project is the .CWM one, so both are offered and the reference
        // that names the file actually being read wins.
        var refDirs = new[]
        {
            aekernel + "/map_out",
            ProjectSettings.GlobalizePath("res://Assets/Legacy/Maps"),
        };

        var paths = new List<string>(Directory.GetFiles(levels, "*.CWM"));
        paths.AddRange(Directory.GetFiles(levels, "*.DM"));
        paths.Sort();

        int ok = 0, bad = 0, noRef = 0;
        var perKey = new Dictionary<string, (int Ok, int Bad)>();

        foreach (string path in paths)
        {
            string stem = Path.GetFileNameWithoutExtension(path);
            CwmFile m;
            string mine;
            try
            {
                m = CwmFile.Load(path);
                mine = EntitiesJson.Write(m);
            }
            catch (Exception e) { bad++; GD.PrintErr($"   {stem}: {e.Message}"); continue; }

            using var gotDoc = JsonDocument.Parse(mine);
            var got = gotDoc.RootElement;

            JsonDocument? wantDoc = null;
            foreach (string dir in refDirs)
            {
                string p = $"{dir}/map_{stem}.entities.json";
                if (!File.Exists(p)) continue;
                var cand = JsonDocument.Parse(File.ReadAllText(p));
                if (Belongs(cand.RootElement, m)) { wantDoc = cand; break; }
                cand.Dispose();
            }
            if (wantDoc == null) { noRef++; continue; }

            var notes = new List<string>();
            bool same = true;
            foreach (var prop in wantDoc.RootElement.EnumerateObject())
            {
                var diffs = new List<string>();
                if (got.TryGetProperty(prop.Name, out var g)) Compare(prop.Name, prop.Value, g, diffs, 6);
                else diffs.Add($"{prop.Name}: fehlt");
                var (o, b) = perKey.GetValueOrDefault(prop.Name);
                perKey[prop.Name] = diffs.Count == 0 ? (o + 1, b) : (o, b + 1);
                if (diffs.Count > 0) { same = false; notes.AddRange(diffs); }
            }
            foreach (var prop in got.EnumerateObject())
                if (!wantDoc.RootElement.TryGetProperty(prop.Name, out _))
                { same = false; notes.Add($"{prop.Name}: zusaetzlicher Schluessel"); }
            wantDoc.Dispose();

            if (same) ok++;
            else
            {
                bad++;
                GD.PrintErr($"   {stem}: {notes.Count} Abweichung(en)");
                for (int i = 0; i < notes.Count && i < 10; i++) GD.PrintErr("      " + notes[i]);
            }
        }

        var line = new List<string>();
        foreach (var kv in perKey) line.Add($"{kv.Key} {kv.Value.Ok}/{kv.Value.Ok + kv.Value.Bad}");
        GD.Print("selftest-ent: " + string.Join(" · ", line));
        GD.Print($"selftest-ent: {ok} Karten deckungsgleich, {bad} abweichend, " +
                 $"{noRef} ohne Vergleichsdatei");
        return bad == 0 && ok > 0 ? 0 : 1;
    }

    /// <summary>Does this reference document describe the file just read? Two
    /// map files can share a stem (10.CWM and 10.DM), so the name and the size
    /// decide before anything is compared.</summary>
    private static bool Belongs(JsonElement d, CwmFile m)
        => d.TryGetProperty("mission", out var mi) && mi.GetString() == m.Mission &&
           d.TryGetProperty("width", out var w) && w.GetInt32() == m.Width &&
           d.TryGetProperty("height", out var h) && h.GetInt32() == m.Height;

    /// <summary>Walk two parsed JSON values together. Numbers are held against
    /// each other as numbers, so 34 and 34.0 count as equal — everything else
    /// has to match exactly.</summary>
    private static void Compare(string path, JsonElement a, JsonElement b, List<string> diffs, int max)
    {
        if (diffs.Count >= max) return;

        switch (a.ValueKind)
        {
            case JsonValueKind.Object:
                if (b.ValueKind != JsonValueKind.Object) { diffs.Add($"{path}: kein Objekt"); return; }
                foreach (var p in a.EnumerateObject())
                {
                    if (!b.TryGetProperty(p.Name, out var bv))
                    { diffs.Add($"{path}.{p.Name}: fehlt"); if (diffs.Count >= max) return; continue; }
                    Compare($"{path}.{p.Name}", p.Value, bv, diffs, max);
                    if (diffs.Count >= max) return;
                }
                foreach (var p in b.EnumerateObject())
                    if (!a.TryGetProperty(p.Name, out _))
                    { diffs.Add($"{path}.{p.Name}: zuviel"); if (diffs.Count >= max) return; }
                return;

            case JsonValueKind.Array:
            {
                if (b.ValueKind != JsonValueKind.Array) { diffs.Add($"{path}: keine Liste"); return; }
                int na = a.GetArrayLength(), nb = b.GetArrayLength();
                if (na != nb) { diffs.Add($"{path}: {nb} Eintraege statt {na}"); return; }
                var ea = a.EnumerateArray();
                var eb = b.EnumerateArray();
                for (int i = 0; ea.MoveNext() && eb.MoveNext(); i++)
                {
                    Compare($"{path}[{i}]", ea.Current, eb.Current, diffs, max);
                    if (diffs.Count >= max) return;
                }
                return;
            }

            case JsonValueKind.Number:
                if (b.ValueKind != JsonValueKind.Number) { diffs.Add($"{path}: keine Zahl"); return; }
                if (Math.Abs(a.GetDouble() - b.GetDouble()) > 1e-9)
                    diffs.Add($"{path}: {b.GetRawText()} statt {a.GetRawText()}");
                return;

            case JsonValueKind.String:
                if (a.GetString() != b.GetString())
                    diffs.Add($"{path}: '{b}' statt '{a}'");
                return;

            default:
                if (a.ValueKind != b.ValueKind) diffs.Add($"{path}: {b.ValueKind} statt {a.ValueKind}");
                return;
        }
    }

    /// <summary>The unit sprites the importer wrote, held against the ones the
    /// Python tooling produced — pixel for pixel, on the artifact itself rather
    /// than on a fresh export, so what is checked is what the game will load.
    ///
    /// Two differences are expected and named rather than tolerated silently:
    /// pictures the imported set has in addition (the discs carry more maps and
    /// therefore more chassis/weapon combinations), and the two cases written
    /// up in <see cref="UnitsExporter"/> — unit_type 149, which the reference
    /// index does not list at all, and the seven empty facings of 168.</summary>
    public static int RunUnits()
    {
        string got = ProjectSettings.GlobalizePath(Core.Content.UserRoot + "Units");
        string want = ProjectSettings.GlobalizePath(Core.Content.DevRoot + "Units");
        if (!Directory.Exists(got) || !Directory.Exists(want))
        {
            GD.Print("selftest-units: nichts zu vergleichen — noch kein Import gelaufen");
            return 0;
        }

        var known = new HashSet<string>();
        for (int f = 0; f < 8; f++) known.Add($"149/f{f}.png");
        for (int f = 1; f < 8; f++) known.Add($"168/f{f}.png");

        int ok = 0, bad = 0, missing = 0, extra = 0, expected = 0;
        var notes = new List<string>();
        var mine = new HashSet<string>(Relative(got));
        foreach (string rel in Relative(want))
        {
            if (!mine.Remove(rel))
            {
                // A frame the reference has and we do not is only a problem if
                // there is something ON it. The exporter stops writing blank
                // pictures — a chassis without directions leaves seven of its
                // eight facings empty, and a file full of nothing claimed a
                // facing that does not exist.
                var refImg = Image.LoadFromFile($"{want}/{rel}");
                if (known.Contains(rel) || (refImg != null && IsBlank(refImg))) expected++;
                else { missing++; if (notes.Count < 8) notes.Add("fehlt: " + rel); }
                continue;
            }
            var a = Image.LoadFromFile($"{want}/{rel}");
            var b = Image.LoadFromFile($"{got}/{rel}");
            if (a == null || b == null) { missing++; continue; }
            if (SamePixels(a, b)) ok++;
            else { bad++; if (notes.Count < 8) notes.Add("abweichend: " + rel); }
        }
        extra = mine.Count;

        GD.Print($"selftest-units: {ok} Bilder deckungsgleich, {bad} abweichend, " +
                 $"{missing} fehlen, {expected} bekannte Ausnahmen, {extra} zusaetzlich (CD 2)");
        foreach (string n in notes) GD.PrintErr("   " + n);
        return bad == 0 && missing == 0 && ok > 0 ? 0 : 1;
    }

    /// <summary>The interface and the effects the importer wrote, against the
    /// ones the Python tooling produced — the font atlas, the side panel and
    /// every effect frame, pixel for pixel, plus the BMFont description line by
    /// line.</summary>
    public static int RunInterface()
    {
        int bad = 0, ok = 0, missing = 0;
        foreach (string sub in new[] { "UI", "Effects" })
        {
            string got = ProjectSettings.GlobalizePath(Core.Content.UserRoot + sub);
            string want = ProjectSettings.GlobalizePath(Core.Content.DevRoot + sub);
            if (!Directory.Exists(got) || !Directory.Exists(want)) continue;
            var mine = new HashSet<string>(Relative(got));
            foreach (string rel in Relative(want))
            {
                if (!mine.Contains(rel))
                { missing++; GD.PrintErr($"   fehlt: {sub}/{rel}"); continue; }
                var a = Image.LoadFromFile($"{want}/{rel}");
                var b = Image.LoadFromFile($"{got}/{rel}");
                if (a == null || b == null) { missing++; continue; }
                if (SamePixels(a, b)) ok++;
                else { bad++; GD.PrintErr($"   abweichend: {sub}/{rel}"); }
            }
        }

        int fntDiff = 0;
        string fa = ProjectSettings.GlobalizePath(Core.Content.DevRoot + "UI/akte_font.fnt");
        string fb = ProjectSettings.GlobalizePath(Core.Content.UserRoot + "UI/akte_font.fnt");
        if (File.Exists(fa) && File.Exists(fb))
        {
            var la = File.ReadAllLines(fa);
            var lb = File.ReadAllLines(fb);
            for (int i = 0; i < Math.Max(la.Length, lb.Length); i++)
            {
                string x = i < la.Length ? la[i] : "";
                string y = i < lb.Length ? lb[i] : "";
                if (x.TrimEnd() != y.TrimEnd()) fntDiff++;
            }
        }

        GD.Print($"selftest-ui: {ok} Bilder deckungsgleich, {bad} abweichend, " +
                 $"{missing} fehlen; Schriftbeschreibung {fntDiff} Zeilen abweichend");
        return bad == 0 && missing == 0 && fntDiff == 0 && ok > 0 ? 0 : 1;
    }

    /// <summary>The design arithmetic against the game's own records.
    ///
    /// Every named design in sec47 carries a tail from +0x1a that the original
    /// derives from the three chosen components (routine @0x4b1fb0). This runs
    /// that derivation over the components alone and holds the result against
    /// the bytes the game stored — all fifteen fields, every record. It is the
    /// check that says whether a design the player draws up gets the price and
    /// the hit points the game would have given it.</summary>
    public static int RunDesigns()
    {
        Simulation.DesignMath.Load();
        if (!Simulation.DesignMath.Ready)
        {
            GD.PrintErr("selftest-designs: Maps/component_stats.json fehlt");
            return 1;
        }

        string path = Core.Content.Path("Maps/unit_designs.json");
        if (!Godot.FileAccess.FileExists(path))
        {
            GD.PrintErr("selftest-designs: Maps/unit_designs.json fehlt");
            return 1;
        }
        using var f = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Read);
        var json = new Json();
        if (f == null || json.Parse(f.GetAsText()) != Error.Ok) return 1;
        var root = json.Data.AsGodotDictionary<string, Variant>();
        if (!root.TryGetValue("designs", out var dv)) return 1;

        int ok = 0, bad = 0, skipped = 0;
        var perField = new SortedDictionary<int, int>();
        foreach (var kv in dv.AsGodotDictionary<string, Variant>())
        {
            var d = kv.Value.AsGodotDictionary<string, Variant>();
            string raw = d.TryGetValue("raw", out var rv) ? rv.AsString() : "";
            if (raw.Length < 0x2e * 2) { skipped++; continue; }
            int weapon = d["weapon"].AsInt32(), prop = d["propulsion"].AsInt32(),
                equip = d["body"].AsInt32();

            var want = Simulation.DesignMath.FromRecordHex(raw).Tail;
            var got = Simulation.DesignMath.Compute(weapon, prop, equip).Tail;
            bool good = true;
            for (int i = 0; i < want.Length; i++)
                if (want[i] != got[i])
                {
                    good = false;
                    perField[0x1a + i] = perField.GetValueOrDefault(0x1a + i) + 1;
                }
            if (good) ok++;
            else
            {
                if (bad < 5)
                    GD.PrintErr($"   abweichend: Slot {kv.Key} \"{d["name"].AsString()}\" " +
                                $"(W{weapon}/P{prop}/E{equip})");
                bad++;
            }
        }

        foreach (var kv in perField)
            GD.PrintErr($"   +0x{kv.Key:x2}: {kv.Value} abweichend");
        GD.Print($"selftest-designs: {ok} von {ok + bad} Entwuerfen exakt gerechnet, " +
                 $"{bad} abweichend" + (skipped > 0 ? $", {skipped} ohne Rohsatz" : ""));
        return bad == 0 && ok > 0 ? 0 : 1;
    }

    /// <summary>The briefings this reader gets out of the cabinet, against the
    /// ones the Python tooling got out of the same bytes — title and every
    /// paragraph, word for word. Two readers, one file, one decision about what
    /// a bare CR means.</summary>
    public static int RunBriefings()
    {
        string want = Core.Content.Path("Maps/briefings.json");
        if (!Godot.FileAccess.FileExists(want))
        {
            GD.PrintErr("selftest-briefings: Maps/briefings.json fehlt");
            return 1;
        }

        byte[]? raw = null;
        foreach (string cab in Core.ContentSources.Cabinets())
        {
            try { raw = IscFile.Load(cab).Extract("BRIEFG.TXT"); }
            catch { /* next cabinet */ }
            if (raw != null) break;
        }
        if (raw == null)
        {
            GD.Print("selftest-briefings: kein Kabinett im Laufwerk — uebersprungen");
            return 0;
        }

        var mine = new Dictionary<int, BriefingExporter.Briefing>();
        foreach (var b in BriefingExporter.Parse(raw)) mine[b.Mission] = b;

        using var f = Godot.FileAccess.Open(want, Godot.FileAccess.ModeFlags.Read);
        var json = new Json();
        if (f == null || json.Parse(f.GetAsText()) != Error.Ok) return 1;
        var root = json.Data.AsGodotDictionary<string, Variant>();
        if (!root.TryGetValue("briefings", out var bv)) return 1;

        int ok = 0, bad = 0;
        foreach (var kv in bv.AsGodotDictionary<string, Variant>())
        {
            if (!int.TryParse(kv.Key, out int no)) continue;
            var d = kv.Value.AsGodotDictionary<string, Variant>();
            if (!mine.TryGetValue(no, out var got))
            { bad++; GD.PrintErr($"   Mission {no} fehlt beim C#-Leser"); continue; }

            string title = d["title"].AsString();
            var paras = d["paragraphs"].AsGodotArray();
            bool good = title == got.Title && paras.Count == got.Paragraphs.Count;
            for (int i = 0; good && i < paras.Count; i++)
                good = paras[i].AsString() == got.Paragraphs[i];
            if (good) ok++;
            else
            {
                if (bad < 3)
                    GD.PrintErr($"   Mission {no} abweichend: Titel \"{got.Title}\" gg. \"{title}\", " +
                                $"{got.Paragraphs.Count} gg. {paras.Count} Absaetze");
                bad++;
            }
        }
        GD.Print($"selftest-briefings: {ok} von {ok + bad} Missionstexten gleich, {bad} abweichend");
        return bad == 0 && ok > 0 ? 0 : 1;
    }

    /// <summary>True if nothing at all is drawn on the picture.</summary>
    private static bool IsBlank(Image img)
    {
        for (int y = 0; y < img.GetHeight(); y++)
            for (int x = 0; x < img.GetWidth(); x++)
                if (img.GetPixel(x, y).A > 0f) return false;
        return true;
    }

    private static IEnumerable<string> Relative(string root)
    {
        foreach (string p in Directory.GetFiles(root, "*.png", SearchOption.AllDirectories))
            yield return Path.GetRelativePath(root, p).Replace('\\', '/');
    }

    private static bool SamePixels(Image a, Image b)
    {
        if (a.GetWidth() != b.GetWidth() || a.GetHeight() != b.GetHeight()) return false;
        if (a.GetFormat() != Image.Format.Rgba8) a.Convert(Image.Format.Rgba8);
        if (b.GetFormat() != Image.Format.Rgba8) b.Convert(Image.Format.Rgba8);
        var x = a.GetData();
        var y = b.GetData();
        if (x.Length != y.Length) return false;
        for (int i = 0; i + 3 < x.Length; i += 4)
        {
            if (x[i + 3] == 0 && y[i + 3] == 0) continue;   // both clear
            if (x[i] != y[i] || x[i + 1] != y[i + 1] ||
                x[i + 2] != y[i + 2] || x[i + 3] != y[i + 3]) return false;
        }
        return true;
    }

    /// <summary>The InstallShield cabinet on the original CD, held against the
    /// copies the tooling extracted with an outside tool — byte for byte, which
    /// is the only standard worth having for an unpacker.
    ///
    /// It also answers the question the CD raised: the disc is from September
    /// 1997 and carries a different GAME.EXE than the January 1998 one every
    /// table was read out of. Both builds' tables are decoded and compared.</summary>
    public static int RunIsc(string aekernel, string cab)
    {
        aekernel = aekernel.TrimEnd('/', '\\');
        if (!File.Exists(cab)) { GD.Print($"selftest-isc: {cab} nicht eingelegt — uebersprungen"); return 0; }

        IscFile a;
        try { a = IscFile.Load(cab); }
        catch (Exception e) { GD.PrintErr("selftest-isc: " + e.Message); return 1; }

        bool contiguous = a.IsContiguous(out int gaps, out long end);
        GD.Print($"selftest-isc: {a.Files.Count} Dateien, Version {a.Version:x8}; " +
                 $"lueckenlos {(contiguous ? "ja" : $"NEIN ({gaps} Luecken, Ende {end})")}");

        int ok = 0, bad = 0, miss = 0;
        string refDir = aekernel + "/cab_out";
        if (Directory.Exists(refDir))
            foreach (string p in Directory.GetFiles(refDir))
            {
                var e = a.Find(Path.GetFileName(p));
                if (e == null) { miss++; GD.PrintErr($"   {Path.GetFileName(p)}: nicht im Kabinett"); continue; }
                byte[] got;
                try { got = a.Extract(e); }
                catch (Exception ex) { bad++; GD.PrintErr($"   {e.Name}: {ex.Message}"); continue; }
                var want = File.ReadAllBytes(p);
                if (want.Length == got.Length && want.AsSpan().SequenceEqual(got)) ok++;
                else { bad++; GD.PrintErr($"   {e.Name}: {got.Length} statt {want.Length} Bytes"); }
            }
        GD.Print($"selftest-isc: {ok} Dateien deckungsgleich, {bad} abweichend, {miss} nicht gefunden");

        // the two builds' tables, decoded and compared record by record
        int tOk = 0, tBad = 0;
        var cdExe = a.Extract("GAME.EXE");
        string refExe = aekernel + "/GAME.EXE";
        if (cdExe != null && File.Exists(refExe))
        {
            var cd = ExeTables.FromBytes(cdExe);
            var re = ExeTables.Load(refExe);
            GD.Print($"selftest-isc: CD-Exe {cdExe.Length} Bytes, Tabellen " +
                     $"{(cd.Relocated ? "verschoben und wiedergefunden" : "an den bekannten Adressen")} " +
                     $"(Stats {cd.StatsBase:x}, Schiffe {cd.ShipBase:x}, Flugzeuge {cd.AircraftBase:x})");
            if (re.Relocated) { tBad++; GD.PrintErr("   die RE-Exe sollte NICHT verschoben sein"); }

            var s1 = cd.Ships(); var s2 = re.Ships();
            for (int i = 0; i < s2.Count; i++)
                if (i < s1.Count && s1[i].Name == s2[i].Name && s1[i].Chassis == s2[i].Chassis &&
                    s1[i].Energie == s2[i].Energie && s1[i].Fuel == s2[i].Fuel) tOk++;
                else { tBad++; GD.PrintErr($"   Schiff {i} weicht ab"); }

            var a1 = cd.Aircraft(); var a2 = re.Aircraft();
            for (int i = 0; i < a2.Count; i++)
                if (i < a1.Count && a1[i].Name == a2[i].Name && a1[i].Hp == a2[i].Hp &&
                    a1[i].Airframe == a2[i].Airframe && a1[i].Fuel == a2[i].Fuel) tOk++;
                else { tBad++; GD.PrintErr($"   Flugzeug {i} weicht ab"); }

            for (int ut = 1; ut < 200; ut++)
            {
                var x = cd.StatsFor(ut); var y = re.StatsFor(ut);
                if (x == null || y == null) continue;
                if (x.HpMax == y.HpMax && x.Name == y.Name && x.ComponentId == y.ComponentId) tOk++;
                else { tBad++; if (tBad < 5) GD.PrintErr($"   Stats {ut} weicht ab: '{x.Name}'/'{y.Name}'"); }
            }
        }
        GD.Print($"selftest-isc: {tOk} Tabellensaetze in beiden Builds gleich, {tBad} abweichend");

        return bad == 0 && miss == 0 && tBad == 0 && contiguous && ok > 0 ? 0 : 1;
    }

    /// <summary>The sprite bank. Two independent checks: the part table against
    /// the base frames the Python export recorded, and rendered frames against
    /// the PNGs that same export wrote — the hulls and the rail cars are single
    /// parts on the shared canvas, so they compare one to one.</summary>
    public static int RunCwr(string aekernel)
    {
        aekernel = aekernel.TrimEnd('/', '\\');
        string cwrPath = aekernel + "/cab_out/ROBO.CWR";
        string palPath = aekernel + "/DATA/01.PAL";
        string mapPath = aekernel + "/unit_sprite_map.json";
        if (!File.Exists(cwrPath) || !File.Exists(palPath))
        {
            GD.PrintErr("selftest-cwr: ROBO.CWR oder 01.PAL fehlt");
            return 2;
        }

        var cwr = CwrFile.Load(cwrPath);
        var pal = PalFile.Load(palPath);
        var parts = cwr.PopulatedParts();
        int decoded = 0;
        for (int i = 0; i < CwrFile.FrameSlots; i++)
            if (cwr.DecodeFrame(i) != null) decoded++;
        GD.Print($"selftest-cwr: {parts.Count} belegte Parts, {decoded} von " +
                 $"{CwrFile.FrameSlots} Rahmen dekodierbar");

        // 1) the part table against the recorded base frames
        int tblOk = 0, tblBad = 0;
        if (File.Exists(mapPath))
        {
            var root = ReadJson(mapPath);
            if (root != null && root.TryGetValue("units", out var uv) &&
                uv.VariantType == Variant.Type.Dictionary)
            {
                var tbl = uv.AsGodotDictionary<string, Variant>();
                foreach (var key in tbl.Keys)
                {
                    var u = tbl[key].AsGodotDictionary<string, Variant>();
                    int comp = GetI(u, "component_id");
                    int want = GetI(u, "base_frame");
                    int got = cwr.PartBase(comp);
                    if (got == want) tblOk++;
                    else
                    {
                        tblBad++;
                        GD.PrintErr($"   unit_type {key}: Part {comp} Basis {got} statt {want}");
                    }
                }
            }
        }
        GD.Print($"selftest-cwr: Part-Tabelle {tblOk} Basiswerte gleich, {tblBad} abweichend");

        // 2) rendered frames against the exported PNGs
        string units = ProjectSettings.GlobalizePath("res://Assets/Legacy/Units");
        int imgOk = 0, imgBad = 0, imgSkip = 0;
        var byUt = new Dictionary<int, int>();       // unit_type -> component
        if (File.Exists(mapPath))
        {
            var root = ReadJson(mapPath);
            if (root != null && root.TryGetValue("units", out var uv2) &&
                uv2.VariantType == Variant.Type.Dictionary)
            {
                var us = uv2.AsGodotDictionary<string, Variant>();
                foreach (var key in us.Keys)
                    if (int.TryParse(key, out int ut))
                        byUt[ut] = GetI(us[key].AsGodotDictionary<string, Variant>(), "component_id");
            }
        }

        // hulls: one propulsion part per unit_type, block 0
        foreach (var kv in byUt)
            for (int f = 0; f < CwrFile.Facings; f++)
                Compare($"{units}/hull/{kv.Key}/f{f}.png", cwr.PartImage(kv.Value, f, pal),
                        ref imgOk, ref imgBad, ref imgSkip);

        // rail cars: parts 57 and 58, straight off the part table
        foreach (int part in new[] { 57, 58 })
            for (int f = 0; f < CwrFile.Facings; f++)
                Compare($"{units}/train/{part}/f{f}.png", cwr.PartImage(part, f, pal),
                        ref imgOk, ref imgBad, ref imgSkip);

        GD.Print($"selftest-cwr: {imgOk} Bilder deckungsgleich, {imgBad} abweichend, " +
                 $"{imgSkip} ohne Vergleichsbild");
        return tblBad == 0 && imgBad == 0 && imgOk > 0 ? 0 : 1;
    }

    /// <summary>The tables inside GAME.EXE, against the JSON the Python
    /// extractors wrote.</summary>
    public static int RunExe(string aekernel)
    {
        aekernel = aekernel.TrimEnd('/', '\\');
        string exe = aekernel + "/GAME.EXE";
        if (!File.Exists(exe)) { GD.PrintErr("selftest-exe: GAME.EXE fehlt"); return 2; }
        var t = ExeTables.Load(exe);

        int ok = 0, bad = 0;

        // stats: hp_max, name, successor and the corrected component id
        string statsPath = aekernel + "/unit_stats.json";
        if (File.Exists(statsPath))
        {
            var root = ReadJson(statsPath);
            if (root != null)
                foreach (var key in root.Keys)
                {
                    if (!int.TryParse(key, out int ut)) continue;
                    var w = root[key].AsGodotDictionary<string, Variant>();
                    var g = t.StatsFor(ut);
                    if (g == null) { bad++; continue; }
                    string wn = w.TryGetValue("name", out var nv) ? nv.AsString().Trim() : "";
                    string ws = w.TryGetValue("succ_name", out var sv) ? sv.AsString().Trim() : "";
                    if (GetI(w, "hp_max") == g.HpMax && GetI(w, "component_id") == g.ComponentId &&
                        wn == g.Name && ws == g.SuccName) ok++;
                    else
                    {
                        bad++;
                        GD.PrintErr($"   stats {ut}: hp {g.HpMax}/{GetI(w, "hp_max")} " +
                                    $"comp {g.ComponentId}/{GetI(w, "component_id")} " +
                                    $"name '{g.Name}'/'{wn}'");
                    }
                }
        }
        GD.Print($"selftest-exe: Stats {ok} Records gleich, {bad} abweichend");

        // ships: the whole default table including the derived tech threshold
        int sOk = 0, sBad = 0;
        string shipsPath = aekernel + "/map_out/ships.json";
        if (File.Exists(shipsPath))
        {
            var root = ReadJson(shipsPath);
            var mine = t.Ships();
            if (root != null && root.TryGetValue("default", out var dv) &&
                dv.VariantType == Variant.Type.Array)
            {
                var want = dv.AsGodotArray();
                for (int i = 0; i < want.Count && i < mine.Count; i++)
                {
                    var w = want[i].AsGodotDictionary<string, Variant>();
                    var g = mine[i];
                    bool same = GetI(w, "weapon") == g.Weapon && GetI(w, "chassis") == g.Chassis &&
                                GetI(w, "cost_w") == g.CostW && GetI(w, "cost_ch") == g.CostF &&
                                GetI(w, "cost_sp") == g.CostS && GetI(w, "speed") == g.Speed &&
                                GetI(w, "energie") == g.Energie && GetI(w, "attack") == g.Attack &&
                                GetI(w, "defence") == g.Defence && GetI(w, "sight") == g.Sight &&
                                GetI(w, "ammo") == g.Ammo && GetI(w, "fuel") == g.Fuel &&
                                GetI(w, "reload") == g.Reload && GetI(w, "tech") == g.Tech &&
                                (w.TryGetValue("name", out var nv2) ? nv2.AsString().Trim() : "") == g.Name;
                    if (same) sOk++;
                    else { sBad++; GD.PrintErr($"   Schiff {i} '{g.Name}' weicht ab"); }
                }
            }
        }
        GD.Print($"selftest-exe: Schiffe {sOk} Designs gleich, {sBad} abweichend");

        // aircraft templates against the exported catalogue
        int aOk = 0, aBad = 0;
        string airPath = ProjectSettings.GlobalizePath(
            "res://Assets/Legacy/Maps/aircraft.json");
        if (File.Exists(airPath))
        {
            var root = ReadJson(airPath);
            var mine = t.Aircraft();
            if (root != null && root.TryGetValue("types", out var tv) &&
                tv.VariantType == Variant.Type.Array)
            {
                var want = tv.AsGodotArray();
                for (int i = 0; i < want.Count && i < mine.Count; i++)
                {
                    var w = want[i].AsGodotDictionary<string, Variant>();
                    var g = mine[i];
                    bool same = GetI(w, "hp") == g.Hp && GetI(w, "payload") == g.Payload &&
                                GetI(w, "airframe") == g.Airframe && GetI(w, "attack") == g.Attack &&
                                GetI(w, "defence") == g.Defence && GetI(w, "sight") == g.Sight &&
                                GetI(w, "ammo") == g.Ammo && GetI(w, "fuel") == g.Fuel;
                    if (same) aOk++;
                    else { aBad++; GD.PrintErr($"   Flugzeug {i} '{g.Name}' weicht ab"); }
                }
            }
        }
        GD.Print($"selftest-exe: Flugzeuge {aOk} Vorlagen gleich, {aBad} abweichend");

        return bad == 0 && sBad == 0 && aBad == 0 && ok > 0 && sOk > 0 ? 0 : 1;
    }

    /// <summary>Bake maps with the ported baker and hold the result against the
    /// PNGs the Python baker produced — the very pictures the game draws. The
    /// comparison is on the raw RGBA bytes, because these are up to
    /// 10160x5285 and a per-pixel call would take all afternoon.</summary>
    public static int RunBake(string aekernel, string[] stems)
    {
        aekernel = aekernel.TrimEnd('/', '\\');
        int ok = 0, bad = 0, skip = 0;
        foreach (string stem in stems)
        {
            string cwm = File.Exists($"{aekernel}/LEVELS/{stem}.CWM")
                ? $"{aekernel}/LEVELS/{stem}.CWM" : $"{aekernel}/LEVELS/{stem}.DM";
            if (!File.Exists(cwm)) { skip++; continue; }
            var m = CwmFile.Load(cwm);
            string cwp = $"{aekernel}/DATA/{m.Tileset:00}.CWP";
            string pal = $"{aekernel}/DATA/{m.Tileset:00}.PAL";
            if (!File.Exists(cwp) || !File.Exists(pal)) { skip++; continue; }

            var baker = new MapBaker(m, CwpFile.Load(cwp), PalFile.Load(pal));
            var got = baker.Bake();

            string refPng = ProjectSettings.GlobalizePath(
                $"res://Assets/Legacy/Maps/map_{stem}.png");
            if (!File.Exists(refPng)) { skip++; continue; }
            var want = Image.LoadFromFile(refPng);
            if (want == null) { skip++; continue; }

            if (want.GetWidth() != got.GetWidth() || want.GetHeight() != got.GetHeight())
            {
                bad++;
                GD.PrintErr($"   {stem}: {got.GetWidth()}x{got.GetHeight()} statt " +
                            $"{want.GetWidth()}x{want.GetHeight()}");
                continue;
            }
            if (want.GetFormat() != Image.Format.Rgba8) want.Convert(Image.Format.Rgba8);
            var a = want.GetData();
            var b = got.GetData();
            long diff = 0;
            for (int i = 0; i + 3 < a.Length; i += 4)
            {
                // both transparent counts as equal whatever sits in the colour
                if (a[i + 3] == 0 && b[i + 3] == 0) continue;
                if (a[i] != b[i] || a[i + 1] != b[i + 1] ||
                    a[i + 2] != b[i + 2] || a[i + 3] != b[i + 3]) diff++;
            }
            if (diff == 0)
            {
                ok++;
                GD.Print($"   {stem}: {got.GetWidth()}x{got.GetHeight()} deckungsgleich");
            }
            else
            {
                bad++;
                double pct = 100.0 * diff / (a.Length / 4.0);
                GD.PrintErr($"   {stem}: {diff} Pixel abweichend ({pct:0.000}%)");
            }
        }
        GD.Print($"selftest-bake: {ok} Karten deckungsgleich, {bad} abweichend, {skip} uebersprungen");
        return bad == 0 && ok > 0 ? 0 : 1;
    }

    private static void Compare(string refPng, Image got, ref int ok, ref int bad, ref int skip)
    {
        if (!File.Exists(refPng)) { skip++; return; }
        var want = Image.LoadFromFile(refPng);
        if (want == null) { skip++; return; }
        if (want.GetWidth() != got.GetWidth() || want.GetHeight() != got.GetHeight())
        {
            bad++;
            GD.PrintErr($"   {Path.GetFileName(refPng)}: Groesse weicht ab");
            return;
        }
        int diff = 0;
        for (int y = 0; y < want.GetHeight(); y++)
            for (int x = 0; x < want.GetWidth(); x++)
            {
                var a = want.GetPixel(x, y);
                var b = got.GetPixel(x, y);
                if (a.A < 0.5f && b.A < 0.5f) continue;
                if (a != b) diff++;
            }
        if (diff == 0) ok++;
        else { bad++; GD.PrintErr($"   {refPng[(refPng.Length - 24)..]}: {diff} Pixel"); }
    }

    private static Godot.Collections.Dictionary<string, Variant>? ReadJson(string path)
    {
        var json = new Json();
        if (json.Parse(File.ReadAllText(path)) != Error.Ok) return null;
        return json.Data.VariantType == Variant.Type.Dictionary
            ? json.Data.AsGodotDictionary<string, Variant>() : null;
    }

    private static int GetI(Godot.Collections.Dictionary<string, Variant> d, string k)
        => d.TryGetValue(k, out var v) && v.VariantType != Variant.Type.Nil ? v.AsInt32() : 0;

    private static string ToHex(byte[] b)
    {
        var sb = new System.Text.StringBuilder(b.Length * 2);
        foreach (byte x in b) sb.Append(x.ToString("x2"));
        return sb.ToString();
    }

    /// <summary>Decode every frame of every CWP the folder holds, to prove the
    /// layout formula holds beyond the sample — the same check the Python
    /// validator did (0 bad offsets, 0 overruns over all 23 files).</summary>
    public static int RunCwpSweep(string aekernel)
    {
        string dataDir = aekernel.TrimEnd('/', '\\') + "/DATA";
        if (!Directory.Exists(dataDir)) { GD.PrintErr("selftest-cwp: DATA fehlt"); return 2; }
        int files = 0, frames = 0, objects = 0, failed = 0;
        foreach (string path in Directory.GetFiles(dataDir, "*.CWP"))
        {
            CwpFile f;
            try { f = CwpFile.Load(path); }
            catch (Exception e) { failed++; GD.PrintErr($"   {Path.GetFileName(path)}: {e.Message}"); continue; }
            files++;
            for (int i = 0; i < f.FrameCount; i++)
            {
                try { var fr = f.DecodeFrame(i); frames++; if (fr.Width > 64) throw new Exception($"Breite {fr.Width}"); }
                catch (Exception e) { failed++; GD.PrintErr($"   {Path.GetFileName(path)} Rahmen {i}: {e.Message}"); break; }
            }
            for (int i = 0; i < f.ObjectCount; i++)
            {
                try { f.DecodeObject(CwpFile.ObjectCodeBase + i); objects++; }
                catch (Exception e) { failed++; GD.PrintErr($"   {Path.GetFileName(path)} Objekt {i}: {e.Message}"); break; }
            }
        }
        GD.Print($"selftest-cwp: {files} Dateien, {frames} Kacheln und {objects} Objekte " +
                 $"dekodiert, {failed} Fehler");
        return failed == 0 ? 0 : 1;
    }

    /// <summary>The sound bank, both halves of it.
    ///
    /// First the reader on its own: the directory of SOUNDS.CWN must tile the
    /// file exactly — no gap, no overlap, last end on the file size. That is the
    /// check that proved the layout in the first place, so it is the one worth
    /// running against another copy of the game.
    ///
    /// Then the export against the Python reference, byte for byte:
    /// <c>python sounds_cwn.py dump &lt;SOUNDS.CWN&gt; &lt;refdir&gt;</c> writes the same
    /// 492 files, and every byte of every one of them has to match — WAVs are
    /// compared as bytes, not as sound, because a decoder that drops the last
    /// frame or shifts a sample would still play.</summary>
    /// <param name="refDir">Where sounds_cwn.py dumped its WAVs.</param>
    /// <param name="source">The player's installation, or the SOUNDS.CWN
    /// itself. Optional — without it only the export is compared, because an
    /// installation sits on a fixed drive and
    /// <see cref="Core.ContentSources"/> only ever looks at removable ones.</param>
    public static int RunSounds(string refDir, string? source = null)
    {
        refDir = refDir.TrimEnd('/', '\\');
        if (!Directory.Exists(refDir))
        { GD.PrintErr($"selftest-sounds: {refDir} gibt es nicht"); return 2; }

        // ---- the reader against the player's own copy -----------------------
        int rc = 0;
        string? cwn = null;
        if (!string.IsNullOrWhiteSpace(source))
        {
            if (File.Exists(source)) cwn = source;
            else
                foreach (string n in new[] { "SOUNDS.CWN", "sounds.cwn", "DATA/SOUNDS.CWN" })
                    if (cwn == null && File.Exists(source.TrimEnd('/', '\\') + "/" + n))
                        cwn = source.TrimEnd('/', '\\') + "/" + n;
        }

        // on a disc it is not lying about — unpack it out of DATA1.CAB, which is
        // exactly the path a CD install takes
        string? temp = null;
        if (cwn == null && !string.IsNullOrWhiteSpace(source))
        {
            string? cab = Core.ContentSources.CabinetIn(source);
            if (cab != null)
                try
                {
                    var isc = IscFile.Load(cab);
                    if (isc.Find("SOUNDS.CWN") != null)
                    {
                        temp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "aer_sounds_test.cwn");
                        if (isc.ExtractTo("SOUNDS.CWN", temp) > 0) cwn = temp;
                        GD.Print($"selftest-sounds: aus dem Kabinett {cab} ausgepackt");
                    }
                }
                catch (Exception e) { GD.PrintErr("selftest-sounds: Kabinett — " + e.Message); }
        }

        if (cwn == null) GD.Print("selftest-sounds: keine SOUNDS.CWN angegeben — Leserprobe uebersprungen");
        else
        {
            try
            {
                using var bank = new SoundBank(cwn);
                GD.Print($"selftest-sounds: {Path.GetFileName(cwn)} — {bank.Entries.Count} von " +
                         $"{SoundBank.SlotCount} Plaetzen, {bank.Preloaded} vorgeladen / " +
                         $"{bank.OnDemand} bei Bedarf, " +
                         $"{bank.TotalBytes / SoundBank.SampleRate / 60} min; Kette " +
                         (bank.Contiguous ? "lueckenlos bis aufs Dateiende" : "GEBROCHEN"));
                if (!bank.Contiguous) rc = 1;
            }
            catch (Exception e) { GD.PrintErr("selftest-sounds: " + e.Message); rc = 1; }
            finally
            {
                if (temp != null) try { File.Delete(temp); } catch (Exception) { }
            }
        }

        // ---- the export against the Python reference ------------------------
        string got = ProjectSettings.GlobalizePath(Core.Content.UserRoot + "Sound");
        if (!Directory.Exists(got))
        { GD.PrintErr("selftest-sounds: user://data/Sound fehlt — noch kein Import gelaufen"); return 1; }

        int ok = 0, bad = 0, missing = 0;
        foreach (string want in Directory.GetFiles(refDir, "s*.wav"))
        {
            string name = Path.GetFileName(want);
            string mine = got + "/" + name;
            if (!File.Exists(mine)) { missing++; GD.PrintErr($"   fehlt: {name}"); continue; }
            var a = File.ReadAllBytes(want);
            var b = File.ReadAllBytes(mine);
            if (a.Length == b.Length && a.AsSpan().SequenceEqual(b)) { ok++; continue; }
            bad++;
            GD.PrintErr($"   abweichend: {name} ({a.Length} gegen {b.Length} Bytes)");
        }

        GD.Print($"selftest-sounds: {ok} Klaenge byte-genau gleich, {bad} abweichend, {missing} fehlen");
        return rc == 0 && bad == 0 && missing == 0 && ok > 0 ? 0 : 1;
    }
}
