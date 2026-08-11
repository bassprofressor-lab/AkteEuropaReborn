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
    /// <summary>
    /// The patterns against the maps — the measurement that decides which
    /// pattern a placed building actually wears.
    ///
    /// <para><c>add_building</c> @0x4C8D60 takes its TILES from
    /// <c>word[0xbb3208 + typ*10] − 2</c> and its block MASK from
    /// <c>word[0xbb3202 + typ*10]</c>: two different patterns. Rather than
    /// trust that reading, this walks every building of every level, lays each
    /// of the type's patterns over the map's own tile codes, and reports which
    /// one fits. If the reading is right, the winner is the same offset
    /// everywhere.</para>
    ///
    /// <para>A map cell holds the grid code; codes at or above 10000 are
    /// objects and the pattern stores <c>code − 10000</c> (the original's
    /// <c>add ax, 0x2710</c>).</para>
    ///
    /// <para><b>RESULT (07.08.2026, 684 buildings over 23 levels): a placed
    /// building wears its type's FIRST pattern — 684 of 684, not one
    /// counterexample, and no other offset occurs at all.</b> 677 of them cover
    /// it whole; the other seven wear the same pattern and miss between one and
    /// four single cells (22/26, 25/26, 25/26, 13/14, 29/30, 36/39, 37/38),
    /// which is the map author having overwritten a tile or two. So the
    /// <c>TilePattern − 2</c> that add_building computes is NOT what a standing
    /// building shows — what that index is for is still open.</para>
    /// </summary>
    public static int RunBuildPatterns(string aekernel)
    {
        aekernel = aekernel.TrimEnd('/', '\\');
        string levels = aekernel + "/LEVELS", data = aekernel + "/DATA";
        if (!Directory.Exists(levels)) { GD.PrintErr("selftest-build: LEVELS fehlt"); return 2; }
        if (!Directory.Exists(data)) { GD.PrintErr("selftest-build: DATA fehlt"); return 2; }

        var cwpCache = new Dictionary<int, CwpFile?>();
        var offsets = new Dictionary<int, int>();     // winner − FirstPattern, counted
        int buildings = 0, matched = 0, unmatched = 0, noPattern = 0;
        var byType = new Dictionary<int, (int Ok, int No)>();

        var paths = new List<string>(Directory.GetFiles(levels, "*.CWM"));
        paths.Sort();

        foreach (string path in paths)
        {
            CwmFile m;
            try { m = CwmFile.Load(path); } catch { continue; }
            if (!cwpCache.TryGetValue(m.Tileset, out var cwp))
            {
                string p = $"{data}/{m.Tileset:00}.CWP";
                try { cwp = File.Exists(p) ? CwpFile.Load(p) : null; } catch { cwp = null; }
                cwpCache[m.Tileset] = cwp;
            }
            if (cwp == null || !cwp.HasBuildings) continue;

            var rec = m.Records;
            foreach (var b in CwmData.Buildings(m))
            {
                if (b.IsBuilt == 0 || b.Type <= 0) continue;
                var bt = cwp.GetBuildingType(b.Type);
                if (bt.IsEmpty) { noPattern++; continue; }
                buildings++;

                // Search ALL patterns, not just the type's own run: if a
                // building ever wore one from outside, counting only the run
                // would hide it instead of showing it.
                int best = -1, bestHits = -1;
                int nearPat = -1, nearHits = -1, nearCells = 0;   // the closest miss
                for (int pat = 0; pat < CwpFile.PatternCount; pat++)
                {
                    int hits = 0, cells = 0;
                    for (int x = 0; x < CwpFile.PatternWidth; x++)
                        for (int y = 0; y < CwpFile.PatternHeight; y++)
                        {
                            int tile = cwp.PatternTile(pat, x, y);
                            if (tile == 0) continue;
                            cells++;
                            int c = b.Col + x, r = b.Row + y;
                            if (c < 0 || r < 0 || c >= m.Width || r >= m.Height) continue;
                            int code = BitConverter.ToUInt16(rec, (r * m.Width + c) * 4);
                            if (code - CwpFile.ObjectCodeBase == tile) hits++;
                        }
                    if (cells > 0 && hits == cells && hits > bestHits) { bestHits = hits; best = pat; }
                    if (cells > 0 && hits > nearHits) { nearHits = hits; nearPat = pat; nearCells = cells; }
                }

                var t = byType.TryGetValue(b.Type, out var v) ? v : (0, 0);
                if (best >= 0)
                {
                    matched++;
                    int off = best - bt.FirstPattern;
                    offsets[off] = offsets.TryGetValue(off, out int n) ? n + 1 : 1;
                    byType[b.Type] = (t.Item1 + 1, t.Item2);
                }
                else
                {
                    unmatched++;
                    byType[b.Type] = (t.Item1, t.Item2 + 1);
                    GD.Print($"   ohne Muster: {Path.GetFileName(path)} slot {b.Slot} typ {b.Type} " +
                             $"bei ({b.Col},{b.Row}) — bestes Muster {nearPat} (= first{nearPat - bt.FirstPattern:+0;-0;+0}) " +
                             $"trifft {nearHits} von {nearCells} Zellen");
                }
            }
        }

        RunDoorTable(aekernel, paths);

        GD.Print($"selftest-build: {buildings} Gebaeude auf {paths.Count} Karten — " +
                 $"{matched} decken ihr Muster ganz, {unmatched} bis auf einzelne Kacheln " +
                 $"(alle mit demselben Versatz, siehe oben), {noPattern} Typen ohne Muster " +
                 $"im Tileset");
        var keys = new List<int>(offsets.Keys); keys.Sort();
        foreach (int off in keys)
            GD.Print($"   Musterversatz {off,3} gegen 'first': {offsets[off]} Gebaeude");
        if (unmatched > 0)
        {
            var bad = new List<int>();
            foreach (var kv in byType) if (kv.Value.No > 0) bad.Add(kv.Key);
            bad.Sort();
            foreach (int t in bad)
                GD.Print($"   typ {t}: {byType[t].Ok} passend, {byType[t].No} nicht");
        }
        return unmatched;
    }

    /// <summary>
    /// The exe's building stat table against the maps — two sources that share
    /// nothing.
    ///
    /// <para>The doors of a NEW building come from the 10-byte row
    /// <c>add_building</c> reads (found by shape, see
    /// <c>ExeTables.BuildingStatBase</c>). The doors of a PLACED building sit
    /// in its sec3 record. If the table was found and read right, the two agree
    /// for every type on every map.</para>
    /// </summary>
    private static void RunDoorTable(string aekernel, List<string> paths)
    {
        string exePath = aekernel.TrimEnd('/', '\\') + "/GAME.EXE";
        if (!File.Exists(exePath))
        {
            GD.Print("selftest-build Tueren: GAME.EXE fehlt — ungeprueft");
            return;
        }
        ExeTables exe;
        try { exe = ExeTables.Load(exePath); }
        catch { GD.Print("selftest-build Tueren: GAME.EXE unlesbar — ungeprueft"); return; }
        if (exe.BuildingStatBase == 0)
        {
            GD.Print("selftest-build Tueren: Statustabelle nicht gefunden — ungeprueft");
            return;
        }

        int same = 0, differ = 0;
        foreach (string path in paths)
        {
            CwmFile m;
            try { m = CwmFile.Load(path); } catch { continue; }
            foreach (var b in CwmData.Buildings(m))
            {
                if (b.IsBuilt == 0 || b.Type < 1 || b.Type > 16) continue;
                var st = exe.BuildingStats(b.Type);
                if (st.DoorCount == b.Doors) same++;
                else
                {
                    differ++;
                    if (differ <= 5)
                        GD.PrintErr($"   {Path.GetFileName(path)} slot {b.Slot} typ {b.Type}: " +
                                    $"Karte {b.Doors} Tueren, EXE {st.DoorCount}");
                }
            }
        }
        GD.Print($"selftest-build Tueren: EXE-Tabelle gegen sec3 — {same} gleich, {differ} abweichend " +
                 $"(Tabelle bei 0x{exe.BuildingStatBase:x})");
    }

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
                            // the capture fields: the door and the two gates
                            GetI(w, "doors") != g.Doors || GetI(w, "built") != g.IsBuilt ||
                            GetI(w, "door_col") != g.DoorCol || GetI(w, "door_row") != g.DoorRow ||
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
    /// <summary>
    /// `--selftest-rail=<dir>` — die zurueckgerechnete Bahnstrecke gegen die
    /// gespeicherte halten.
    ///
    /// <para>Kein Levelfile traegt sec122, also muss das Start-y einer Linie aus
    /// den beiden Endgebaeuden zurueckgerechnet werden
    /// (<c>CwmExtra.SolveStartY</c>). Ob das stimmt, laesst sich nur dort
    /// pruefen, wo die Antwort danebensteht: die drei Spielstaende `1.DM`,
    /// `3.DM` und `4.DM` haben sec122. Also wird sie dort <b>ignoriert</b>,
    /// blind gerechnet und mit dem gespeicherten Wert verglichen.</para>
    ///
    /// <para>⚠ Das ist der einzige ehrliche Pruefstand dafuer. Auf einer
    /// Kampagnenkarte laesst sich das Ergebnis mit nichts vergleichen — dort
    /// saehe ein falsches Gleis genauso aus wie ein richtiges.</para>
    /// </summary>
    public static int RunRail(string dir)
    {
        int files = 0, total = 0, solved = 0, right = 0, wrong = 0, unsure = 0;
        var bad = new List<string>();
        foreach (string stem in new[] { "1", "3", "4" })
        {
            string p = dir.TrimEnd('/', '\\') + "/LEVELS/" + stem + ".DM";
            if (!System.IO.File.Exists(p)) continue;
            files++;
            var m = CwmFile.Load(p);
            var blds = CwmData.Buildings(m);
            var stored = CwmExtra.Links(m, blds);
            var blind = CwmExtra.Links(m, blds, ignoreStoredY: true);
            for (int i = 0; i < stored.Count && i < blind.Count; i++)
            {
                if (!stored[i].Y1.HasValue) continue;
                total++;
                if (!blind[i].Y1.HasValue) { unsure++; continue; }
                solved++;
                if (blind[i].Y1 == stored[i].Y1) right++;
                else
                {
                    wrong++;
                    if (bad.Count < 5)
                        bad.Add($"{stem}.DM Linie {stored[i].Slot}: gespeichert " +
                                $"{stored[i].Y1}, gerechnet {blind[i].Y1}");
                }
            }
        }
        // ⚠ Und die eigentliche Frage: steht das y vielleicht laengst in sec34?
        // Die Routine @0x4B0FE0 liest `sec34 +0x03` und haelt `wert >> 1` gegen
        // ein Gebaeudefeld — das sieht nach einem y in halben Zeilen aus.
        foreach (string stem in new[] { "1", "3", "4" })
        {
            string p2 = dir.TrimEnd('/', '\\') + "/LEVELS/" + stem + ".DM";
            if (!System.IO.File.Exists(p2)) continue;
            var m2 = CwmFile.Load(p2);
            var s34 = m2.Sec(34);
            var ls = CwmExtra.Links(m2, CwmData.Buildings(m2));
            if (s34 == null) continue;
            int same3 = 0, same5 = 0, n = 0;
            foreach (var l in ls)
            {
                if (!l.Y1.HasValue) continue;
                int o = l.Slot * CwmExtra.SpojStride;
                if (o + 8 > s34.Length) continue;
                n++;
                if (s34[o + 3] == (l.Y1.Value & 0xFF)) same3++;
                if (s34[o + 5] == (l.Y2!.Value & 0xFF)) same5++;
            }
            GD.Print($"   {stem}.DM: {n} Linien — sec34 +0x03 == y1: {same3}, " +
                     $"+0x05 == y2: {same5}");
        }
        // ⚠ Und die Frage, die der Spieler stellt: wo ist das GLEIS?
        // Der Waggon bringt sein Schienenstueck selbst mit, aber die Strecke
        // dazwischen muss irgendwo herkommen. Hier stehen die Kachelcodes der
        // Karte auf der Route gegen die sechs Zellen daneben.
        foreach (string lv in new[] { "NET05", "NET02" })
        {
            string p3 = dir.TrimEnd('/', '\\') + "/LEVELS/" + lv + ".CWM";
            if (!System.IO.File.Exists(p3)) continue;
            var m3 = CwmFile.Load(p3);
            var ls3 = CwmExtra.Links(m3, CwmData.Buildings(m3));
            var rec = m3.Records;
            int w3 = m3.Width, h3 = m3.Height;
            var onRoute = new Dictionary<int, int>();
            var beside = new Dictionary<int, int>();
            int n3 = 0;
            foreach (var l in ls3)
            {
                if (l.Route == null) continue;
                foreach (var (cx, cy) in l.Route)
                {
                    int r0 = (int)cy;
                    void Tally(int c, int r, Dictionary<int, int> into)
                    {
                        if (c < 0 || c >= w3 || r < 0 || r >= h3) return;
                        int v = System.BitConverter.ToUInt16(rec, (r * w3 + c) * 4);
                        into[v] = into.GetValueOrDefault(v) + 1;
                    }
                    Tally(cx, r0, onRoute);
                    Tally(cx + 6, r0, beside);
                    n3++;
                }
            }
            string Top(Dictionary<int, int> t)
            {
                var l2 = new List<KeyValuePair<int, int>>(t);
                l2.Sort((a, b) => b.Value.CompareTo(a.Value));
                var sb2 = new System.Text.StringBuilder();
                for (int k = 0; k < 6 && k < l2.Count; k++)
                    sb2.Append($"{l2[k].Key}x{l2[k].Value} ");
                return sb2.ToString();
            }
            GD.Print($"   {lv}: {n3} Routenpunkte, {onRoute.Count} verschiedene Codes AUF der Strecke");
            GD.Print($"      auf  der Strecke: {Top(onRoute)}");
            GD.Print($"      6 Zellen daneben: {Top(beside)}");
        }
        if (files == 0) { GD.Print("selftest-rail: keine .DM in " + dir); return 0; }
        GD.Print($"selftest-rail: {files} Spielstaende, {total} Linien mit gespeichertem y; " +
                 $"{solved} eindeutig zurueckgerechnet, davon {right} RICHTIG, {wrong} falsch; " +
                 $"{unsure} nicht eindeutig (werden nicht gelegt)");
        foreach (string b in bad) GD.PrintErr("   " + b);
        return wrong == 0 ? 0 : 1;
    }

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

    /// <summary>Does every weapon a map actually mounts have a name?
    ///
    /// This exists because the answer was NO for a long time without anything
    /// saying so: the exporter read six of the fifty components the stats table
    /// names, and `WeaponOf` quietly handed out component 24's name to the rest,
    /// so 890 of 1446 armed units were labelled "2x Maschinengewehr". A silent
    /// fallback cannot fail a test, so the fallback is gone and this counts
    /// instead — over the imported game states, which is the population that
    /// matters, not over the table.
    ///
    /// One gap is known and expected: component 61 on two units. It is reported
    /// by number rather than smoothed over.</summary>
    public static int RunWeapons()
    {
        string path = Core.Content.Path("Maps/weapons.json");
        if (!Godot.FileAccess.FileExists(path))
        {
            GD.PrintErr("selftest-weapons: Maps/weapons.json fehlt");
            return 1;
        }
        using var wf = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Read);
        var wj = new Json();
        if (wf == null || wj.Parse(wf.GetAsText()) != Error.Ok) return 1;
        var wroot = wj.Data.AsGodotDictionary<string, Variant>();
        if (!wroot.TryGetValue("weapons", out var wv)) return 1;
        var named = new HashSet<int>();
        foreach (var kv in wv.AsGodotDictionary<string, Variant>())
            if (int.TryParse(kv.Key, out int c)) named.Add(c);

        // the "types" block the design screen reads — empty on the stale file
        int types = wroot.TryGetValue("types", out var tv)
            ? tv.AsGodotDictionary<string, Variant>().Count : 0;

        string dir = Core.Content.Path("Maps");
        int armed = 0, ok = 0;
        var gaps = new SortedDictionary<int, int>();
        foreach (string p in Godot.DirAccess.GetFilesAt(dir))
        {
            if (!p.EndsWith(".entities.json")) continue;
            using var f = Godot.FileAccess.Open($"{dir}/{p}", Godot.FileAccess.ModeFlags.Read);
            var json = new Json();
            if (f == null || json.Parse(f.GetAsText()) != Error.Ok) continue;
            var root = json.Data.AsGodotDictionary<string, Variant>();
            if (!root.TryGetValue("entities", out var ev)) continue;
            foreach (var e in ev.AsGodotArray())
            {
                var d = e.AsGodotDictionary<string, Variant>();
                string raw = d.TryGetValue("raw", out var rv) ? rv.AsString() : "";
                if (raw.Length < 0x0d * 2) continue;
                int comp = Convert.ToInt32(raw.Substring(0x0c * 2, 2), 16);
                if (comp == 0) continue;           // unarmed, nothing to name
                armed++;
                if (named.Contains(comp)) ok++;
                else gaps[comp] = gaps.GetValueOrDefault(comp) + 1;
            }
        }

        foreach (var kv in gaps)
            GD.Print($"   Luecke: Bauteil {kv.Key} auf {kv.Value} Einheiten ohne Namen");
        GD.Print($"selftest-weapons: {named.Count} Bauteile benannt, {types} Bauarten; " +
                 $"{ok} von {armed} bewaffneten Einheiten mit echtem Namen, " +
                 $"{armed - ok} Luecke");
        // 2 of 1446 is the measured floor (component 61); anything worse is a
        // regression, and a missing "types" block breaks the design screen.
        return armed > 0 && types > 0 && armed - ok <= 2 ? 0 : 1;
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

        // the campaign's diplomacy — and this one is not a table but CODE, so
        // the two sides really do read it differently: campaign_diplomacy.py
        // disassembles mission_init @0x487c40 with Capstone, ExeTables walks the
        // same branches by byte pattern. Agreement therefore means something.
        int dOk = 0, dBad = 0, dFields = 0;
        string diploPath = aekernel + "/campaign_diplomacy.json";
        if (!File.Exists(diploPath))
            GD.Print("selftest-exe: campaign_diplomacy.json fehlt — " +
                     "'python campaign_diplomacy.py --json > campaign_diplomacy.json'");
        else if (!t.HasCampaignDiplomacy)
        {
            dBad++;
            GD.PrintErr("   Diplomatie: diese GAME.EXE traegt mission_init nicht an " +
                        "den bekannten Adressen");
        }
        else
        {
            var root = ReadJson(diploPath);
            if (root != null && root.TryGetValue("missions", out var mv) &&
                mv.VariantType == Variant.Type.Array)
            {
                foreach (var entry in mv.AsGodotArray())
                {
                    var w = entry.AsGodotDictionary<string, Variant>();
                    int mission = GetI(w, "mission");
                    var g = t.CampaignDiplomacy(mission);
                    if (g == null)
                    {
                        dBad++;
                        GD.PrintErr($"   Diplomatie M{mission}: nicht gelesen");
                        continue;
                    }
                    var notes = new List<string>();
                    var wa = w["allied"].AsGodotArray();
                    for (int a = 0; a < 8; a++)
                    {
                        var row = wa[a].AsGodotArray();
                        for (int b = 0; b < 8; b++)
                        {
                            dFields++;
                            bool want = row[b].AsInt32() != 0;
                            if (want != g.Allied[a, b])
                                notes.Add($"verbuendet[{a},{b}] {g.Allied[a, b]} statt {want}");
                        }
                    }
                    var wn = new bool[8];
                    foreach (var p in w["neutral"].AsGodotArray()) wn[p.AsInt32()] = true;
                    for (int p = 0; p < 8; p++)
                    {
                        dFields++;
                        if (wn[p] != g.Neutral[p])
                            notes.Add($"neutral[{p}] {g.Neutral[p]} statt {wn[p]}");
                    }
                    if (notes.Count == 0) dOk++;
                    else
                    {
                        dBad++;
                        GD.PrintErr($"   Diplomatie M{mission}: {notes.Count} Abweichung(en) — " +
                                    string.Join(", ", notes.GetRange(0, Math.Min(4, notes.Count))));
                    }
                }
            }
        }
        GD.Print($"selftest-exe: Diplomatie {dOk} Missionen gleich, {dBad} abweichend " +
                 $"({dFields} Felder geprueft)");

        // the skirmish's resource option: four levels of five numbers, plus the
        // type mapping the routine's own jump table gives
        int rOk = 0, rBad = 0;
        string resPath = aekernel + "/resources.json";
        if (!File.Exists(resPath))
            GD.Print("selftest-exe: resources.json fehlt — " +
                     "'python resources_export.py --json > resources.json'");
        else if (!t.HasResourceTables)
        {
            rBad++;
            GD.PrintErr("   Rohstoffe: diese GAME.EXE traegt kein fill_resources");
        }
        else
        {
            var root = ReadJson(resPath);
            var mine = t.ResourceLevels();
            if (root != null && root.TryGetValue("levels", out var lv) &&
                lv.VariantType == Variant.Type.Array)
            {
                var want = lv.AsGodotArray();
                for (int i = 0; i < want.Count && i < mine.Count; i++)
                {
                    var w = want[i].AsGodotDictionary<string, Variant>();
                    var g = mine[i];
                    string wn = w.TryGetValue("name", out var nv) ? nv.AsString() : "";
                    if (GetI(w, "weapons") == g.Weapons && GetI(w, "chassis") == g.Chassis &&
                        GetI(w, "special") == g.Special && GetI(w, "terranium") == g.Terranium &&
                        GetI(w, "deposit") == g.Deposit && wn == g.Name) rOk++;
                    else
                    {
                        rBad++;
                        GD.PrintErr($"   Rohstoffe Stufe {i} '{g.Name}'/'{wn}': " +
                                    $"{g.Weapons}/{g.Chassis}/{g.Special} T{g.Terranium} " +
                                    $"V{g.Deposit} gegen {GetI(w, "weapons")}/" +
                                    $"{GetI(w, "chassis")}/{GetI(w, "special")} " +
                                    $"T{GetI(w, "terranium")} V{GetI(w, "deposit")}");
                    }
                }
            }
            if (root != null && root.TryGetValue("fill", out var fv) &&
                fv.VariantType == Variant.Type.Dictionary)
                foreach (var kv in fv.AsGodotDictionary<string, Variant>())
                {
                    if (!int.TryParse(kv.Key, out int ty)) continue;
                    string g = t.ResourceFillOf(ty).ToString().ToLowerInvariant();
                    if (g == kv.Value.AsString()) rOk++;
                    else
                    {
                        rBad++;
                        GD.PrintErr($"   Rohstoffe Typ {ty}: '{g}' statt '{kv.Value.AsString()}'");
                    }
                }
        }
        GD.Print($"selftest-exe: Rohstoffe {rOk} Werte gleich, {rBad} abweichend");

        // the computer players' build programmes, which live as code in the exe
        // and not in any map — held line by line against mission_setup.py
        int pOk = 0, pBad = 0;
        string plansPath = aekernel + "/mission_plans.json";
        if (!File.Exists(plansPath))
        {
            GD.Print("selftest-exe: mission_plans.json fehlt — " +
                     "mit `mission_setup.py <GAME.EXE> --json <datei>` erzeugen");
        }
        else
        {
            var root = ReadJson(plansPath);
            if (root != null)
                foreach (var key in root.Keys)
                {
                    if (!int.TryParse(key, out int mission)) continue;
                    var want = root[key].AsGodotDictionary<string, Variant>();
                    var got = t.MissionPlan(mission);
                    if (got == null)
                    {
                        pBad++;
                        GD.PrintErr($"   Bauplan Mission {mission}: gar keiner gelesen");
                        continue;
                    }
                    foreach (var pk in want.Keys)
                    {
                        if (!int.TryParse(pk, out int player)) continue;
                        var wrows = want[pk].AsGodotArray();
                        if (!got.TryGetValue(player, out var grows))
                        {
                            pBad++;
                            GD.PrintErr($"   Bauplan Mission {mission} Spieler {player}: fehlt");
                            continue;
                        }
                        bool same = wrows.Count == grows.Count;
                        for (int i = 0; same && i < wrows.Count; i++)
                        {
                            var w = wrows[i].AsGodotArray();
                            same = w.Count == 3 && w[0].AsInt32() == grows[i].Kind &&
                                   w[1].AsInt32() == grows[i].What &&
                                   w[2].AsInt32() == grows[i].Third;
                        }
                        if (same) pOk++;
                        else
                        {
                            pBad++;
                            GD.PrintErr($"   Bauplan Mission {mission} Spieler {player}: " +
                                        $"{grows.Count} Zeilen statt {wrows.Count}");
                        }
                    }
                }
            GD.Print($"selftest-exe: Bauplaene {pOk} Programme gleich, {pBad} abweichend " +
                     $"(Verteiler 0x{t.VyrobaCaseTable:X}, add_vyroba 0x{t.AddVyroba:X})");
        }

        // the per-mission unlock schedule, held against mission_unlocks.py.
        // Compared by EFFECT, not by the ranges themselves: the two readers may
        // cut a run differently and still switch exactly the same rows on.
        int uOk = 0, uBad = 0;
        string unlockPath = aekernel + "/mission_unlocks.json";
        if (!File.Exists(unlockPath))
        {
            GD.Print("selftest-exe: mission_unlocks.json fehlt — " +
                     "mit `mission_unlocks.py <GAME.EXE>` erzeugen");
        }
        else
        {
            var kindOf = new System.Collections.Generic.Dictionary<string, string>
            { { "vehicles", "design" }, { "ships", "ship" }, { "aircraft", "aircraft" } };
            var root = ReadJson(unlockPath);
            var got = t.MissionUnlocks;
            if (root != null)
                foreach (var key in root.Keys)
                {
                    if (!int.TryParse(key, out int mission)) continue;
                    var want = root[key].AsGodotDictionary<string, Variant>();
                    foreach (var kk in want.Keys)
                    {
                        if (!kindOf.TryGetValue(kk, out string? kind)) continue;
                        var wEff = new System.Collections.Generic.SortedDictionary<int, int>();
                        foreach (var r in want[kk].AsGodotArray())
                        {
                            var a = r.AsGodotArray();
                            for (int x = a[0].AsInt32(); x <= a[1].AsInt32(); x++)
                                wEff[x] = a[2].AsInt32();
                        }
                        var gEff = new System.Collections.Generic.SortedDictionary<int, int>();
                        if (got.TryGetValue(mission, out var rows))
                            foreach (var r in rows)
                                if (r.Kind == kind)
                                    for (int x = r.From; x <= r.To; x++) gEff[x] = r.Value;
                        bool same = wEff.Count == gEff.Count;
                        if (same)
                            foreach (var kv in wEff)
                                if (!gEff.TryGetValue(kv.Key, out int v) || v != kv.Value)
                                { same = false; break; }
                        if (same) uOk++;
                        else
                        {
                            uBad++;
                            GD.PrintErr($"   Fahrplan Mission {mission} {kk}: " +
                                        $"{gEff.Count} Zeilen statt {wEff.Count}");
                        }
                    }
                }
            GD.Print($"selftest-exe: Fahrplan {uOk} Listen gleich, {uBad} abweichend");
        }

        return bad == 0 && sBad == 0 && aBad == 0 && dBad == 0 && rBad == 0 && pBad == 0 &&
               uBad == 0 && ok > 0 && sOk > 0 ? 0 : 1;
    }

    /// <summary>Bake maps with the ported baker and hold the result against the
    /// PNGs the Python baker produced — the very pictures the game draws. The
    /// comparison is on the raw RGBA bytes, because these are up to
    /// 10160x5285 and a per-pixel call would take all afternoon.
    ///
    /// <para>⚠ <b>The buildings are exempt, and that is not a loophole.</b> Since
    /// 07.08.2026 our baker leaves a standing building out of the picture so the
    /// renderer can draw it and show its ruin (see
    /// <see cref="MapBaker.SkippedPixels"/>); the Python reference still bakes
    /// its buildings in. Comparing the two therefore reported "4 % of the pixels
    /// differ" on every map that has a building — measured 08.08.2026, and the
    /// difference mask is five building-shaped blobs and nothing else. The test
    /// now skips exactly the pixels the baker says it did not draw, and
    /// <b>prints how many</b>: an exemption that could hide a real error has to
    /// be visible.</para></summary>
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
            var exempt = baker.SkippedPixels;
            long diff = 0, skipped = 0, underBuilding = 0;
            for (int i = 0; i + 3 < a.Length; i += 4)
            {
                // both transparent counts as equal whatever sits in the colour
                if (a[i + 3] == 0 && b[i + 3] == 0) continue;
                bool same = a[i] == b[i] && a[i + 1] == b[i + 1] &&
                            a[i + 2] == b[i + 2] && a[i + 3] == b[i + 3];
                if (exempt != null && exempt[i / 4])
                {
                    skipped++;
                    if (!same) underBuilding++;
                    continue;
                }
                if (!same) diff++;
            }
            string note = skipped == 0 ? "" :
                $", {skipped} Pixel unter Gebaeuden ausgenommen ({underBuilding} davon abweichend)";
            if (diff == 0)
            {
                ok++;
                GD.Print($"   {stem}: {got.GetWidth()}x{got.GetHeight()} deckungsgleich{note}");
            }
            else
            {
                bad++;
                double pct = 100.0 * diff / (a.Length / 4.0);
                GD.PrintErr($"   {stem}: {diff} Pixel abweichend ({pct:0.000}%){note}");
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
        failed += RunCwpBuildings(aekernel, dataDir);
        return failed == 0 ? 0 : 1;
    }

    /// <summary>
    /// The building side tables, against the Python reader — rule 6, byte for
    /// byte and not by eye.
    ///
    /// <para><c>cwp_building_ref.py</c> writes one line per (file, type):
    /// count, first pattern, tile pattern, how many cells carry a tile, how
    /// many the mask blocks, and FNV-1a/32 over every pattern byte the type
    /// owns. We build the same lines here and compare them. A single wrong byte
    /// anywhere moves the hash.</para>
    ///
    /// <para>Two properties are checked on top, because they are what the
    /// reading of the table rests on: the pattern indices <b>chain without a
    /// gap</b> (first + count == the next type's first, skipping the entries
    /// with count 0, which carry an index but own nothing), and the mask holds
    /// <b>only 0 and 255</b>.</para>
    /// </summary>
    private static int RunCwpBuildings(string aekernel, string dataDir)
    {
        string refPath = aekernel.TrimEnd('/', '\\') + "/cwp_building_ref.txt";
        if (!File.Exists(refPath))
        {
            GD.Print("selftest-cwp: cwp_building_ref.txt fehlt — Gebaeudetabellen " +
                     "ungeprueft (erzeugen mit aekernel-tools/cwp_building_ref.py)");
            return 0;
        }

        var mine = new List<string>();
        int chainOk = 0, chainBad = 0, maskOther = 0;

        var paths = new List<string>(Directory.GetFiles(dataDir, "*.CWP"));
        paths.Sort((a, b) => string.CompareOrdinal(
            Path.GetFileName(a).ToUpperInvariant(), Path.GetFileName(b).ToUpperInvariant()));

        foreach (string path in paths)
        {
            CwpFile f;
            try { f = CwpFile.Load(path); } catch { continue; }
            if (!f.HasBuildings) continue;
            string name = Path.GetFileName(path).ToUpperInvariant();

            var used = new List<(int Typ, CwpFile.BuildingType Bt)>();
            for (int typ = 0; typ < CwpFile.BuildingTypeCount; typ++)
            {
                var bt = f.GetBuildingType(typ);
                if (!bt.IsEmpty) used.Add((typ, bt));
            }

            for (int i = 0; i + 1 < used.Count; i++)
            {
                if (used[i].Bt.FirstPattern + used[i].Bt.PatternCount == used[i + 1].Bt.FirstPattern)
                    chainOk++;
                else
                {
                    chainBad++;
                    GD.PrintErr($"   {name} typ {used[i].Typ}: {used[i].Bt.FirstPattern}" +
                                $"+{used[i].Bt.PatternCount} != {used[i + 1].Bt.FirstPattern}");
                }
            }

            foreach (var (typ, bt) in used)
            {
                int tiles = 0, masks = 0;
                uint h = 2166136261u;                       // FNV-1a/32
                for (int k = 0; k < bt.PatternCount; k++)
                {
                    int pat = bt.FirstPattern + k;
                    for (int x = 0; x < CwpFile.PatternWidth; x++)
                        for (int y = 0; y < CwpFile.PatternHeight; y++)
                        {
                            if (f.PatternTile(pat, x, y) != 0) tiles++;
                            if (f.PatternBlocks(pat, x, y)) masks++;
                        }
                    foreach (byte b in f.PatternBytes(pat)) { h = (h ^ b) * 16777619u; }
                    maskOther += f.MaskBytesOtherThanZeroOr255(pat);
                }

                // the cell animations the type owns, hashed the same way
                uint ah = 2166136261u;
                for (int k = 0; k < bt.AnimCount; k++)
                    foreach (byte b in f.AnimRowBytes(bt.AnimFirst + k))
                        ah = (ah ^ b) * 16777619u;

                mine.Add($"{name} {typ} {bt.PatternCount} {bt.FirstPattern} " +
                         $"{bt.TilePattern} {tiles} {masks} {h:x8} " +
                         $"{bt.AnimCount} {bt.AnimFirst} {ah:x8}");
            }
        }

        var theirs = new List<string>(File.ReadAllLines(refPath));
        theirs.RemoveAll(string.IsNullOrWhiteSpace);

        int same = 0, diff = 0;
        int n = System.Math.Min(mine.Count, theirs.Count);
        for (int i = 0; i < n; i++)
        {
            if (mine[i] == theirs[i].Trim()) same++;
            else
            {
                diff++;
                if (diff <= 5) GD.PrintErr($"   Zeile {i + 1}: C# «{mine[i]}» ≠ py «{theirs[i].Trim()}»");
            }
        }
        if (mine.Count != theirs.Count)
        {
            diff += System.Math.Abs(mine.Count - theirs.Count);
            GD.PrintErr($"   Zeilenzahl: C# {mine.Count}, py {theirs.Count}");
        }

        GD.Print($"selftest-cwp Gebaeude: {same} Zeilen deckungsgleich, {diff} abweichend; " +
                 $"Musterkette {chainOk} lueckenlos / {chainBad} gebrochen; " +
                 $"Maskenbytes ausser 0 und 255: {maskOther}");
        return diff + chainBad + maskOther + RunExportedPatterns(paths);
    }

    /// <summary>
    /// The exported <c>Buildings/tileset_nn.json</c> against the .CWP it came
    /// from — cell for cell, both rasters, every pattern of every type.
    ///
    /// <para>The reader above is checked against Python; this checks the
    /// EXPORTER, which is the other place a building could quietly lose its
    /// shape. The engine only ever sees these files, so an error here would
    /// never show up in the .CWP tests.</para>
    /// </summary>
    private static int RunExportedPatterns(List<string> cwpPaths)
    {
        string dir = ProjectSettings.GlobalizePath(Core.Content.UserRoot)
                         .TrimEnd('/', '\\') + "/Buildings";
        if (!Directory.Exists(dir))
        {
            GD.Print("selftest-cwp Muster-Export: noch nichts exportiert — uebersprungen");
            return 0;
        }

        int files = 0, cells = 0, bad = 0, typesSame = 0, typesBad = 0, anims = 0;
        foreach (string path in cwpPaths)
        {
            // DATA/<nn>.CWP -> Buildings/tileset_<nn>.json
            string stem = Path.GetFileNameWithoutExtension(path);
            string json = $"{dir}/tileset_{stem}.json";
            if (!File.Exists(json)) continue;

            CwpFile cwp;
            try { cwp = CwpFile.Load(path); } catch { continue; }
            if (!cwp.HasBuildings) continue;
            var exported = BuildingPatterns.Load(json);
            if (exported == null) { bad++; GD.PrintErr($"   {stem}: JSON unlesbar"); continue; }
            files++;

            for (int typ = 0; typ < CwpFile.BuildingTypeCount; typ++)
            {
                var a = cwp.GetBuildingType(typ);
                var b = exported.GetBuildingType(typ);
                // A type with count 0 owns no pattern. It still carries a first
                // index (and sometimes a tile index) in the .CWP, but nothing
                // reads them, so the export drops the row — that is not a
                // difference worth reporting.
                if (a.PatternCount == 0 && b.PatternCount == 0) continue;
                if (a.PatternCount != b.PatternCount || a.FirstPattern != b.FirstPattern
                    || a.TilePattern != b.TilePattern
                    || a.AnimCount != b.AnimCount || a.AnimFirst != b.AnimFirst)
                {
                    typesBad++;
                    GD.PrintErr($"   {stem} typ {typ}: CWP {a.PatternCount}/{a.FirstPattern}/" +
                                $"{a.TilePattern}/{a.AnimCount}@{a.AnimFirst} != JSON " +
                                $"{b.PatternCount}/{b.FirstPattern}/{b.TilePattern}/" +
                                $"{b.AnimCount}@{b.AnimFirst}");
                    continue;
                }
                if (a.IsEmpty) continue;
                typesSame++;

                // the cell animations, field for field and tile for tile
                for (int k = 0; k < a.AnimCount; k++)
                {
                    int row = a.AnimFirst + k;
                    var ra = cwp.GetAnimRow(row);
                    var rb = exported.GetAnimRow(row);
                    anims++;
                    bool same = ra.Dx == rb.Dx && ra.Dy == rb.Dy && ra.Mode == rb.Mode
                                && ra.LastPhase == rb.LastPhase;
                    for (int t = 0; same && t < CwpFile.AnimTileCount; t++)
                        same = ra.TileAt(t) == rb.TileAt(t);
                    if (!same)
                    {
                        bad++;
                        if (bad <= 5)
                            GD.PrintErr($"   {stem} Animation {row}: CWP ({ra.Dx},{ra.Dy}) " +
                                        $"Modus {ra.Mode} bis {ra.LastPhase} != JSON " +
                                        $"({rb.Dx},{rb.Dy}) Modus {rb.Mode} bis {rb.LastPhase}");
                    }
                }

                for (int k = 0; k < a.PatternCount; k++)
                {
                    int pat = a.FirstPattern + k;
                    for (int x = 0; x < CwpFile.PatternWidth; x++)
                        for (int y = 0; y < CwpFile.PatternHeight; y++)
                        {
                            cells++;
                            if (cwp.PatternTile(pat, x, y) != exported.PatternTile(pat, x, y)
                                || cwp.PatternBlocks(pat, x, y) != exported.PatternBlocks(pat, x, y))
                            {
                                bad++;
                                if (bad <= 5)
                                    GD.PrintErr($"   {stem} Muster {pat} ({x},{y}): CWP " +
                                                $"{cwp.PatternTile(pat, x, y)}/{cwp.PatternBlocks(pat, x, y)}" +
                                                $" != JSON {exported.PatternTile(pat, x, y)}/" +
                                                $"{exported.PatternBlocks(pat, x, y)}");
                            }
                        }
                }
            }
        }

        GD.Print($"selftest-cwp Muster-Export: {files} Tilesets, {typesSame} Typen gleich / " +
                 $"{typesBad} abweichend, {cells} Zellen und {anims} Animationszeilen " +
                 $"geprueft, {bad} abweichend");
        return bad + typesBad;
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

    /// <summary>
    /// The passability against the file it came out of.
    ///
    /// Two things are checked over every map, and both are claims that can be
    /// wrong rather than opinions that cannot:
    ///
    /// (1) <b>Every derived class agrees with the raw imap cell</b> — the
    ///     rewritten grid is re-read straight from sec6 and compared, so a
    ///     packing or row/column slip shows up as a count, not as a hunch.
    /// (2) <b>0xFFFC is water</b>: no tile whose ground code is &lt;= 7 may come
    ///     out as anything but water, and 0xFFFC itself must sit on water.
    ///
    /// ⚠ The second half needed correcting the moment it was measured on all 36
    /// maps instead of the five it was found on. It is <b>not</b> "no counter
    /// example": 86 of 169,421 0xFFFC cells sit on a tile whose code is above 7.
    /// 84 of them are OBJECT cells (code &gt;= 10000, 35 different codes, always
    /// in groups of four or five) — piers and bridge heads standing in the
    /// water, which is exactly where they belong. That leaves <b>2 cells in the
    /// whole set</b> on plain land, and they are reported rather than explained
    /// away. The first direction is clean: not one water tile comes out as free
    /// or rough.
    /// </summary>
    public static int RunTerrain(string aekernel)
    {
        aekernel = aekernel.TrimEnd('/', '\\');
        string levels = aekernel + "/LEVELS";
        if (!Directory.Exists(levels)) { GD.PrintErr("selftest-terrain: LEVELS fehlt"); return 2; }

        var paths = new List<string>(Directory.GetFiles(levels, "*.CWM"));
        paths.AddRange(Directory.GetFiles(levels, "*.DM"));
        paths.Sort();

        int maps = 0, cells = 0, mismatch = 0, waterCells = 0;
        int waterOnProp = 0, waterOnPlainLand = 0, wetNotWater = 0;
        long inferred = 0;
        var unknown = new SortedSet<int>();

        foreach (string path in paths)
        {
            string stem = Path.GetFileNameWithoutExtension(path);
            CwmFile m;
            CwmData.TerrainGrid g;
            try
            {
                m = CwmFile.Load(path);
                g = CwmData.Terrain(m, CwmData.Entities(m));
            }
            catch (Exception e) { mismatch++; GD.PrintErr($"   {stem}: {e.Message}"); continue; }

            var imap = m.Sec(6);
            var rec = m.Records;
            if (imap == null || g.Cells.Length == 0) continue;
            maps++;
            inferred += g.Inferred;
            foreach (int v in g.Unknown) unknown.Add(v);

            int n = imap.Length / 2;
            for (int row = 0; row < m.Height; row++)
                for (int col = 0; col < m.Width; col++)
                {
                    cells++;
                    int i = col * 256 + row;
                    int v = i < n ? BitConverter.ToUInt16(imap, i * 2) : 0xFFFF;
                    var got = (CwmData.Ground)g.Cells[row * m.Width + col];

                    // (1) the class the export wrote must be the class the cell says
                    var want = v == 0xFFFE ? CwmData.Ground.Free
                             : v == 0xFFFD ? CwmData.Ground.Rough
                             : v == 0xFFFC ? CwmData.Ground.Water
                             : v < 8000 || v is >= 10000 and < 14000 ? got   // occupied: inferred
                             : CwmData.Ground.Blocked;
                    if (got != want) mismatch++;

                    // (2) 0xFFFC exactly on the water tiles
                    int ro = (row * m.Width + col) * 4;
                    if (ro + 4 > rec.Length) continue;
                    int code = BitConverter.ToUInt16(rec, ro);
                    bool wet = code <= NavGridWaterCodeMax;
                    if (v == 0xFFFC)
                    {
                        waterCells++;
                        if (!wet) { if (code >= 10000) waterOnProp++; else waterOnPlainLand++; }
                    }
                    else if (wet && v is 0xFFFE or 0xFFFD) wetNotWater++;
                }
        }

        GD.Print($"selftest-terrain: {maps} Karten, {cells} Zellen, {mismatch} abweichend; " +
                 $"0xFFFC {waterCells} Zellen, davon {waterOnProp} auf einer Objektzelle " +
                 $"(Stege/Brueckenkoepfe im Wasser) und {waterOnPlainLand} auf blankem Land; " +
                 $"{wetNotWater} Wasserkacheln als frei/grob gemeldet; " +
                 $"{inferred} Zellen aus ihrem Besetzer abgeleitet" +
                 (unknown.Count > 0 ? $"; unbekannte imap-Werte: {string.Join(",", unknown)}" : ""));
        // The gate is the derivation and the direction that matters for play: a
        // water tile must never come out passable for a land unit. The handful
        // of 0xFFFC cells on dry tiles is measured and named, not gated on.
        return mismatch == 0 && wetNotWater == 0 && maps > 0 ? 0 : 1;
    }

    /// <summary>Ground tile codes 0..7 are the animated water cycle.</summary>
    private const int NavGridWaterCodeMax = 7;
}
