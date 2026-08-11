namespace AkteEuropaReborn.Import;

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Godot;

/// <summary>
/// Turns a player's own Akte Europa installation into the content the remake
/// runs on, writing it into <c>user://data</c>. This is the piece that makes
/// the OpenRA model real: the build we hand out carries none of the 1997 game.
///
/// It uses the four ported readers and the baker, every one of which was
/// checked against the tooling that produced the content the game has been
/// running on: CWP and PAL, the CWM container, ROBO.CWR and the GAME.EXE
/// tables, plus <see cref="MapBaker"/>.
///
/// What it does NOT yet produce is listed by <see cref="Missing"/> and shown to
/// the player, because a half-filled content folder that pretends to be
/// complete is worse than one that says what it is.
/// </summary>
public sealed class ContentBuilder
{
    public readonly List<string> Log = new();
    public int MapsBaked, TablesWritten, EntitiesWritten, SpriteFrames;
    public int SoundsWritten, MusicWritten;

    /// <summary>The (chassis, weapon) pairs actually placed on the maps. They
    /// fall out of the baking anyway, so the sprite export composes exactly the
    /// combinations the game will ask for and nothing else.</summary>
    private readonly SortedSet<(int UnitType, int Weapon)> _combos = new();

    /// <summary>What the catalogue needs from the maps: how often each unit
    /// type occurs and how the buildings break down by type.</summary>
    private readonly CatalogueExporter.Tally _tally = new();

    /// <summary>The design list lives in sec47, which only a .DM carries — and
    /// they do not all hold the same number of designs, so all of them are kept
    /// and the fullest one wins.</summary>
    private readonly List<CwmFile> _designSources = new();

    /// <summary>The campaign missions, by their file number.</summary>
    private readonly List<(int Number, string Map, CwmFile File)> _missions = new();

    private readonly Core.ContentSources.Source _src;
    private readonly string _dst;      // user://data, as an OS path

    public ContentBuilder(Core.ContentSources.Source source)
    {
        _src = source;
        _dst = ProjectSettings.GlobalizePath(Core.Content.UserRoot).TrimEnd('/', '\\');
    }

    /// <summary>A single folder — the case the folder picker produces.</summary>
    public ContentBuilder(string sourceDir)
        : this(Core.ContentSources.FromFolder(sourceDir)
               ?? new Core.ContentSources.Source
               {
                   Kind = Core.ContentSources.Kind.Installation,
                   Label = sourceDir,
                   Roots = { sourceDir.TrimEnd('/', '\\') },
                   Exe = Core.ContentSources.ExeIn(sourceDir),
                   Cabinet = Core.ContentSources.CabinetIn(sourceDir),
               })
    { }

    /// <summary>The saved missions that exist only as .DM, under the names the
    /// game already uses for them — they would otherwise collide with the
    /// campaign levels 1, 3 and 4.</summary>
    private static readonly (string Stem, string Name)[] DmStems =
    {
        ("1", "DM_1"), ("3", "DM_3"), ("4", "DM_4"),
    };

    /// <summary>A program file: lying loose in an installation, inside the
    /// cabinet on a disc. The cabinet is opened once and kept, because it is
    /// 47 MB and five of these come out of it.</summary>
    private IscFile? _cab;
    private bool _cabTried;

    private byte[]? Asset(string name)
    {
        string? loose = Find(name) ?? Find("DATA/" + name);
        if (loose != null) return File.ReadAllBytes(loose);
        return Cabinet()?.Extract(name);
    }

    /// <summary>The cabinet, opened once and kept. Separate from
    /// <see cref="Asset"/> because SOUNDS.CWN wants unpacking to a file rather
    /// than into an array.</summary>
    private IscFile? Cabinet()
    {
        if (!_cabTried)
        {
            _cabTried = true;
            try { if (_src.Cabinet != null) _cab = IscFile.Load(_src.Cabinet); }
            catch (Exception) { _cab = null; }
        }
        return _cab;
    }

    /// <summary>The first root that holds this relative path, or null. The
    /// discs are searched in the order they were found, so CD1 answers for
    /// everything it has and CD2 fills in the rest.</summary>
    private string? Find(string rel)
    {
        foreach (string r in _src.Roots)
            if (File.Exists(r + "/" + rel)) return r + "/" + rel;
        return null;
    }

    public bool Run(Action<string>? progress = null)
    {
        void Say(string s) { Log.Add(s); progress?.Invoke(s); GD.Print("import: " + s); }

        Say(_src.Describe());
        Directory.CreateDirectory(_dst + "/Maps");

        // ---- the tables that live in the exe --------------------------------
        // On a CD the executable is not lying about: it sits in DATA1.CAB, and
        // the disc build is not the one the tables were read out of, so the
        // reader looks for them by content. Both was measured, see RunIsc.
        try
        {
            byte[]? exe;
            string where;
            if (_src.Exe != null) { exe = File.ReadAllBytes(_src.Exe); where = _src.Exe; }
            else { exe = Asset("GAME.EXE"); where = (_src.Cabinet ?? "?") + " → GAME.EXE"; }
            if (exe == null)
            {
                Say("GAME.EXE weder als Datei noch im Kabinett gefunden.");
            }
            else
            {
                var t = ExeTables.FromBytes(exe);
                _exe = t;
                WriteShips(t);
                WriteAircraft(t);
                WriteSightCircle(t);
                TablesWritten += 3;
                Say($"Tabellen aus {where} geschrieben (Schiffe, Flugzeuge)" +
                    (t.Relocated ? " — anderer Programmstand, Tabellen inhaltlich gefunden" : ""));
                // a Werft can build designs that stand on no map, so their
                // chassis and weapon belong in the sprite set as well
                // (@0x4b2b20 writes exactly these two components)
                foreach (var d in t.Ships())
                    _combos.Add((d.Chassis, d.Weapon != 0
                        ? t.StatsFor(d.Weapon)?.ComponentId ?? 0 : 0));
            }
        }
        catch (Exception e) { Say("GAME.EXE: " + e.Message); }

        // ---- the maps -------------------------------------------------------
        // Whatever the source actually carries, not a fixed list: CD 2 brings
        // the campaign levels 16 to 33, which no single installation had.
        foreach (var (stem, path) in Levels("*.CWM"))
            BakeOne(path, "map_" + stem, Say);
        foreach (var (stem, name) in DmStems)
        {
            string? p = Find($"LEVELS/{stem}.DM");
            if (p != null) BakeOne(p, "map_" + name, Say);
        }

        // ---- the unit sprites ------------------------------------------------
        // ROBO.CWR is in the cabinet on a disc and lies loose in an
        // installation; the palette is the terrain one, as in the game.
        try
        {
            byte[]? robo = Asset("ROBO.CWR");
            string? pal = Find("DATA/01.PAL");
            if (robo == null || pal == null)
            {
                Say("ROBO.CWR oder 01.PAL nicht gefunden — keine Einheitengrafiken");
            }
            else
            {
                var ex = new UnitsExporter(CwrFile.FromBytes(robo), PalFile.Load(pal), _exe,
                                           _dst + "/Units");
                ex.Run(_combos, Say);
                SpriteFrames = ex.Frames;
                Say($"Einheitengrafiken: {ex.Frames} Bilder geschrieben");
            }
        }
        catch (Exception e) { Say("ROBO.CWR: " + e.Message); }

        // ---- the interface and the effects -----------------------------------
        // FONT.CWD, PANEL.DTA and ANIM.CWA all come out of the same cabinet as
        // ROBO.CWR, and all three are drawn in the terrain palette.
        try
        {
            string? palPath = Find("DATA/01.PAL");
            if (palPath == null) { Say("01.PAL fehlt — keine Oberflaeche"); }
            else
            {
                var pal = PalFile.Load(palPath);
                var ui = new InterfaceExporter(pal, _dst + "/UI", _dst + "/Effects");
                var font = Asset("FONT.CWD");
                var font2 = Asset("FONT2.CWD");
                var panel = Asset("PANEL.DTA");
                var anim = Asset("ANIM.CWA");
                if (font != null) ui.WriteFont(font);
                // the briefing screen's own typeface — same layout, thinner
                // letters; the loader @0x45bddc reads it right before BRIEFG.DAT
                if (font2 != null)
                    ui.WriteFont(font2, InterfaceExporter.Font2Name, "FONT2.CWD");
                if (panel != null) ui.WritePanel(panel);
                if (anim != null) ui.WriteEffects(AnimFile.FromBytes(anim));
                Say($"Oberflaeche: {ui.Fonts} Schriften mit {ui.Glyphs} Glyphen, " +
                    $"Panel {(panel != null ? "ja" : "nein")}, " +
                    $"{ui.Effects} Effekte mit {ui.EffectFrames} Bildern");
            }
        }
        catch (Exception e) { Say("Oberflaeche: " + e.Message); }

        // ---- the design and catalogue tables ---------------------------------
        try
        {
            var cat = new CatalogueExporter(_exe, _dst + "/Maps");
            cat.Run(_tally, _designSources, Say);
            cat.WriteCampaign(_missions, Say);
            TablesWritten += 8;
        }
        catch (Exception e) { Say("Katalogtabellen: " + e.Message); }

        // ---- the mission briefings -------------------------------------------
        // Out of the same cabinet, and the last thing the campaign was missing:
        // until now it opened a mission without a word of what it was about.
        try
        {
            var txt = Asset("BRIEFG.TXT");
            if (txt == null) Say("BRIEFG.TXT fehlt — keine Missionstexte");
            else
            {
                var br = new BriefingExporter(_dst + "/Maps", _dst + "/UI");
                br.Write(txt, Say);
                TablesWritten++;
                var dat = Asset("BRIEFG.DAT");
                string? bgPal = Find("DATA/01.PAL");
                if (dat != null && bgPal != null) br.WriteBackdrop(dat, PalFile.Load(bgPal), Say);
                // the radar monitor on that same screen — MAP.DAT is 13 MB, so
                // it goes through the path rather than Asset(), like SOUNDS.CWN
                // loose in an installation, inside DATA1.CAB on a disc — 13 MB,
                // so Asset() may hold it whole where SOUNDS.CWN may not
                string? mapDat = Find("MAP.DAT") ?? Find("DATA/MAP.DAT");
                byte[]? radar = mapDat != null ? File.ReadAllBytes(mapDat) : Asset("MAP.DAT");
                if (radar != null && bgPal != null)
                    br.WriteRadar(radar, PalFile.Load(bgPal), Say);
            }
        }
        catch (Exception e) { Say("Briefings: " + e.Message); }

        // ---- die Hilfe- und Untermissionstexte ---------------------------------
        // Aus derselben Quelle wie BRIEFG.TXT, und das Stück, das der Kampagne
        // ihren tutorialartigen Anfang gibt: Mission 1 ruft daraus siebzehn
        // Fenster auf. Siehe HelpExporter.
        try
        {
            var help = Asset("HELPG.TXT");
            if (help == null) Say("HELPG.TXT fehlt — keine Hilfetexte");
            else
            {
                var hx = new HelpExporter(_dst + "/UI");
                hx.Write(help, Say);
                TablesWritten++;
            }
        }
        catch (Exception e) { Say("Hilfetexte: " + e.Message); }

        // ---- the sound bank ---------------------------------------------------
        // SOUNDS.CWN is 79 MB and lies loose beside the exe, so it is opened as
        // a stream and never goes through Asset(), which reads a whole file into
        // memory. The six .MID files are copied through: the original plays them
        // with MCI and so do we, which means they are wanted as they are.
        string? unpacked = null;
        try
        {
            string? snd = Find("SOUNDS.CWN") ?? Find("sounds.cwn") ?? Find("DATA/SOUNDS.CWN");
            // On the DISCS it is not lying about: SOUNDS.CWN, MAP.DAT and the
            // rest are inside DATA1.CAB. Found the hard way — a CD install would
            // have come out silent. It is unpacked to a file rather than into
            // memory, because 79 MB and because the reader wants to seek.
            if (snd == null && Cabinet() is { } cab && cab.Find("SOUNDS.CWN") != null)
            {
                unpacked = _dst + "/SOUNDS.CWN.tmp";
                Say("SOUNDS.CWN liegt im Kabinett — wird ausgepackt (79 MB)");
                long n = cab.ExtractTo("SOUNDS.CWN", unpacked);
                if (n > 0) snd = unpacked;
            }
            if (snd == null) Say("SOUNDS.CWN nicht gefunden — kein Ton");
            else
            {
                using var bank = new SoundBank(snd);
                var se = new SoundExporter(_dst + "/Sound");
                se.Write(bank, Say);
                if (_exe != null) se.WriteWeaponSounds(_exe, Say);
                se.WriteMusic(n => Find(n) ?? Find("DATA/" + n), Asset, Say);
                SoundsWritten = se.Written;
                MusicWritten = se.Music;
            }
        }
        catch (Exception e) { Say("Ton: " + e.Message); }
        finally
        {
            // the unpacked copy has done its job once the WAVs are written
            if (unpacked != null)
                try { File.Delete(unpacked); } catch (Exception) { /* leave it */ }
        }

        Say($"fertig: {MapsBaked} Karten, {EntitiesWritten} Spielstaende, " +
            $"{TablesWritten} Tabellen, {SpriteFrames} Einheitenbilder, " +
            $"{SoundsWritten} Klaenge, {MusicWritten} Musikstuecke");
        foreach (string m in Missing()) Say("fehlt noch: " + m);
        return MapsBaked > 0;
    }

    private ExeTables? _exe;

    /// <summary>Every level the sources hold, by stem, first root winning.</summary>
    private List<(string Stem, string Path)> Levels(string pattern)
    {
        var seen = new Dictionary<string, string>();
        foreach (string r in _src.Roots)
        {
            string dir = r + "/LEVELS";
            if (!Directory.Exists(dir)) continue;
            foreach (string p in Directory.GetFiles(dir, pattern))
            {
                string stem = Path.GetFileNameWithoutExtension(p).ToUpperInvariant();
                if (!seen.ContainsKey(stem)) seen[stem] = p;
            }
        }
        var list = new List<(string, string)>();
        foreach (var kv in seen) list.Add((kv.Key, kv.Value));
        list.Sort((a, b) => string.CompareOrdinal(a.Item1, b.Item1));
        return list;
    }

    /// <summary>
    /// Rewrite only the `.entities.json` of every level the sources hold and
    /// leave the baked pictures where they are.
    ///
    /// The pictures are 400 MB and take minutes; the game state is small and
    /// changes whenever a section is newly understood — the passability out of
    /// the imap was the case that made this worth having. A map the sources do
    /// not carry keeps the file it has, and the runtime says so when it loads a
    /// state that predates a block it wants.
    /// </summary>
    public bool ReexportStates(Action<string>? progress = null)
    {
        void Say(string s) { GD.Print("reexport: " + s); progress?.Invoke(s); }
        Directory.CreateDirectory($"{_dst}/Maps");

        int done = 0, failed = 0;
        void One(string path, string outName)
        {
            try
            {
                var m = CwmFile.Load(path);
                var doc = EntitiesJson.Decode(m);
                File.WriteAllText($"{_dst}/Maps/{outName}.entities.json",
                                  EntitiesJson.Write(doc, outName["map_".Length..]),
                                  new UTF8Encoding(false));
                done++;
                var t = doc.Terrain;
                Say($"{outName}: frei {t.Histogram[0]} / grob {t.Histogram[1]} / " +
                    $"wasser {t.Histogram[2]} / gesperrt {t.Histogram[3]}, " +
                    $"{t.Inferred} abgeleitet, {doc.InfantryCells.Count} Infanteriezellen");
            }
            catch (Exception e) { failed++; Say($"{outName}: {e.Message}"); }
        }

        foreach (var (stem, path) in Levels("*.CWM")) One(path, "map_" + stem);
        foreach (var (stem, name) in DmStems)
        {
            string? p = Find($"LEVELS/{stem}.DM");
            if (p != null) One(p, "map_" + name);
        }

        Say($"fertig: {done} Spielstaende neu geschrieben, {failed} fehlgeschlagen");
        return done > 0 && failed == 0;
    }

    /// <summary>
    /// Rewrite only `campaign.json` — the list of missions and the maps they run
    /// on. Nothing is baked and no level is decoded.
    ///
    /// <para>⚠ It reads the maps ALREADY IMPORTED, not the sources. That is the
    /// whole point: the list was written once, on 2026-08-07, when only disc 1's
    /// fifteen levels were in — and the second disc's import on 08-10 brought
    /// map_16..map_33 in but LEFT THE LIST ALONE, because only the full
    /// <see cref="Run"/> calls <c>WriteCampaign</c>. Eighteen missions sat on
    /// disk unreachable. Rebuilding from the imported maps also means a player
    /// needs no disc in the drive to repair it.</para>
    ///
    /// <para>Everything the list carries is in the baker's own `map_NN.json`:
    /// `mission` (the title), `tileset`, `width`, `height`. Only the slot names
    /// ("Mission 7" and the like) come out of GAME.EXE, and they are optional —
    /// without an exe the list is written without them rather than not at
    /// all.</para>
    ///
    /// <para>A numeric stem is a campaign level; `map_DM_1` and friends are
    /// saved games and stay out, exactly as in <see cref="BakeOne"/>.</para>
    /// </summary>
    public bool ReexportCampaign(Action<string>? progress = null)
    {
        void Say(string s) { GD.Print("reexport-campaign: " + s); progress?.Invoke(s); }

        // the slot names only — a missing exe costs the "slot_name" field and
        // nothing else, so it is not a reason to give up
        try
        {
            byte[]? exe = _src.Exe != null ? File.ReadAllBytes(_src.Exe) : Asset("GAME.EXE");
            if (exe != null) _exe = ExeTables.FromBytes(exe);
            else Say("GAME.EXE nicht gefunden — Liste ohne die Namen der Missionsplaetze");
        }
        catch (Exception e) { Say("GAME.EXE: " + e.Message); }

        string dir = _dst + "/Maps";
        if (!Directory.Exists(dir)) { Say($"{dir} gibt es nicht — nichts importiert"); return false; }

        var rows = new List<CatalogueExporter.Row>();
        int skipped = 0;
        foreach (string p in Directory.GetFiles(dir, "map_*.json"))
        {
            string name = Path.GetFileNameWithoutExtension(p);
            // map_NN.entities.json also matches the pattern — its stem still
            // ends in ".entities", and it is not the meta file
            if (name.EndsWith(".entities", StringComparison.Ordinal)) continue;
            string stem = name["map_".Length..];
            if (!int.TryParse(stem, out int no) || no <= 0) { skipped++; continue; }
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(p));
                var r = doc.RootElement;
                rows.Add(new CatalogueExporter.Row(
                    no, name,
                    r.TryGetProperty("mission", out var t) ? t.GetString() ?? "" : "",
                    Int(r, "width"), Int(r, "height"), Int(r, "tileset")));
            }
            catch (Exception e) { Say($"{name}: {e.Message}"); }
        }

        if (rows.Count == 0) { Say("keine eingespielte Kampagnenkarte gefunden"); return false; }

        var cat = new CatalogueExporter(_exe, dir);
        cat.WriteCampaign(rows, Say);
        rows.Sort((a, b) => a.Number.CompareTo(b.Number));
        Say($"fertig: {cat.Missions} Missionen ({rows[0].Number}..{rows[^1].Number}), "
            + $"{skipped} Spielstaende uebergangen");
        return cat.Missions > 0;

        static int Int(System.Text.Json.JsonElement e, string k)
            => e.TryGetProperty(k, out var v) && v.TryGetInt32(out int i) ? i : 0;
    }

    /// <summary>Nur die Hilfetexte neu schreiben. HELPG.TXT ist 101 KB und
    /// steht in jeder Installation und in jedem Kabinett; ein voller Import
    /// dafür wäre Minuten für eine Sekunde Arbeit.</summary>
    public bool ReexportHelp(Action<string>? progress = null)
    {
        void Say(string s) { GD.Print("reexport-help: " + s); progress?.Invoke(s); }
        try
        {
            var raw = Asset("HELPG.TXT");
            if (raw == null) { Say("HELPG.TXT nicht gefunden"); return false; }
            var hx = new HelpExporter(_dst + "/UI");
            hx.Write(raw, Say);
            return hx.Texts > 0;
        }
        catch (Exception e) { Say(e.Message); return false; }
    }

    /// <summary>
    /// Rewrite only the catalogue tables out of GAME.EXE — weapons, research,
    /// designs, units, building types. Nothing is decoded from a level and no
    /// picture is touched, so it costs a second instead of minutes.
    ///
    /// Worth having for the same reason as <see cref="ReexportStates"/>: a table
    /// changes whenever a row of the stats block is newly understood, and the
    /// weapon names were read from six of fifty components until 2026-08-06.
    /// </summary>
    public bool ReexportTables(Action<string>? progress = null)
    {
        void Say(string s) { GD.Print("reexport-tables: " + s); progress?.Invoke(s); }
        try
        {
            byte[]? exe = _src.Exe != null ? File.ReadAllBytes(_src.Exe) : Asset("GAME.EXE");
            if (exe == null) { Say("GAME.EXE nicht gefunden"); return false; }
            _exe = ExeTables.FromBytes(exe);
        }
        catch (Exception e) { Say("GAME.EXE: " + e.Message); return false; }

        Directory.CreateDirectory($"{_dst}/Maps");

        // The building types and the unit catalogue are counted off the LEVELS,
        // so they are only rewritten when levels are in reach — and the counter
        // below decides it, not a hope. Point this at the discs for the full
        // set; an installation carries fewer levels and says so.
        int levels = 0;
        foreach (var (stem, path) in Levels("*.CWM"))
        {
            try { _tally.Add(EntitiesJson.Decode(CwmFile.Load(path))); levels++; }
            catch (Exception e) { Say($"{stem}: {e.Message}"); }
        }
        foreach (var (stem, _name) in DmStems)
        {
            string? p = Find($"LEVELS/{stem}.DM");
            if (p == null) continue;
            try { _tally.Add(EntitiesJson.Decode(CwmFile.Load(p))); levels++; }
            catch (Exception e) { Say($"{stem}: {e.Message}"); }
        }
        Say($"{levels} Karten gezaehlt");

        try
        {
            var cat = new CatalogueExporter(_exe, _dst + "/Maps");
            if (levels > 0) cat.Run(_tally, _designSources, Say);
            else
            {
                // ⚠ RunExeOnly, not Run: without a level the tally is empty and
                // Run would replace good tables with empty ones. That happened
                // once, on 2026-08-06, the first time these were re-exported on
                // their own — building_types.json and unit_catalog.json went to
                // zero entries without a word of complaint.
                Say("keine Karten in den Quellen — nur die Tabellen aus GAME.EXE");
                cat.RunExeOnly(Say);
            }
            Say($"fertig: {cat.Weapons} Bauteile benannt, {cat.WeaponTypes} Bauarten, "
                + $"{cat.InfantryArms} Infanteriewaffen");
            return cat.Weapons > 0;
        }
        catch (Exception e) { Say("Katalogtabellen: " + e.Message); return false; }
    }

    /// <summary>
    /// Rewrite only <c>Buildings/tileset_nn.*</c> — the patterns, the cell
    /// animations and the tile atlas — straight from the .CWP of every tileset
    /// in reach.
    ///
    /// <para>Why this exists: those files are normally written as a side effect
    /// of baking a map, and baking all 26 takes minutes. Nothing here touches a
    /// baked picture, so a change that only adds tiles to the atlas (as the cell
    /// animations did on 08.08.2026) does not need a full import.</para>
    ///
    /// <para>⚠ It is NOT a substitute for one when the BAKER changes — a cell
    /// the baker decides to leave to the renderer is burnt into the map picture,
    /// and only a re-bake moves that line.</para>
    /// </summary>
    public bool ReexportBuildings(Action<string>? progress = null)
    {
        void Say(string s) { GD.Print("reexport-buildings: " + s); progress?.Invoke(s); }
        int ok = 0, missing = 0;
        for (int tileset = 0; tileset < 100; tileset++)
        {
            string? cwp = Find($"DATA/{tileset:00}.CWP");
            string? pal = Find($"DATA/{tileset:00}.PAL");
            if (cwp == null || pal == null) { if (cwp != null) missing++; continue; }
            try
            {
                WriteBuildingPatterns(CwpFile.Load(cwp), PalFile.Load(pal), tileset);
                ok++;
            }
            catch (Exception e) { Say($"{tileset:00}: {e.Message}"); missing++; }
        }
        Say($"{ok} Tilesets geschrieben, {missing} uebersprungen");
        return ok > 0 && missing == 0;
    }

    /// <summary>
    /// Rewrite only the unit pictures. Needs the (unit_type, weapon) pairs the
    /// maps actually use, so the levels are decoded for their entities — but
    /// nothing is baked and no picture of a map is touched.
    /// </summary>
    public bool ReexportUnits(Action<string>? progress = null)
    {
        void Say(string s) { GD.Print("reexport-units: " + s); progress?.Invoke(s); }
        try
        {
            byte[]? exe = _src.Exe != null ? File.ReadAllBytes(_src.Exe) : Asset("GAME.EXE");
            if (exe != null)
            {
                var t = ExeTables.FromBytes(exe);
                _exe = t;
                // a Werft can build designs that stand on no map, so their
                // chassis and weapon belong in the sprite set too — leaving them
                // out shrank hull/154..156 back to the old canvas
                foreach (var d in t.Ships())
                    _combos.Add((d.Chassis, d.Weapon != 0
                        ? t.StatsFor(d.Weapon)?.ComponentId ?? 0 : 0));
            }
        }
        catch (Exception e) { Say("GAME.EXE: " + e.Message); }

        // ⚠ The combos come from the IMPORTED game states, not from the LEVELS
        // folder. The first version read the levels and quietly shrank the set:
        // the folder here holds 23 of the 44 maps, so the pairs that only exist
        // on discs 2's missions vanished and the index files were rewritten
        // without them. What has already been imported is the complete list, and
        // it needs no disc in the drive.
        int maps = 0;
        foreach (string p in Directory.GetFiles(_dst + "/Maps", "*.entities.json"))
        {
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(p));
                if (!doc.RootElement.TryGetProperty("entities", out var arr) ||
                    arr.ValueKind != System.Text.Json.JsonValueKind.Array) continue;
                foreach (var e in arr.EnumerateArray())
                {
                    if (!e.TryGetProperty("raw", out var rv)) continue;
                    string hex = rv.GetString() ?? "";
                    if (hex.Length < 0x0d * 2) continue;
                    int ut = Convert.ToInt32(hex.Substring(0x0f * 2, 2), 16);
                    int weap = Convert.ToInt32(hex.Substring(0x0c * 2, 2), 16);
                    _combos.Add((ut, weap));
                }
                maps++;
            }
            catch (Exception e) { Say($"{Path.GetFileName(p)}: {e.Message}"); }
        }
        Say($"{maps} eingespielte Karten gelesen, {_combos.Count} Kombinationen");

        byte[]? robo = Asset("ROBO.CWR");
        string? pal = Find("DATA/01.PAL");
        if (robo == null || pal == null) { Say("ROBO.CWR oder 01.PAL nicht gefunden"); return false; }

        Directory.CreateDirectory(_dst + "/Units");
        var ex = new UnitsExporter(CwrFile.FromBytes(robo), PalFile.Load(pal), _exe, _dst + "/Units");
        ex.Run(_combos, Say);
        SpriteFrames = ex.Frames;
        Say($"fertig: {ex.Frames} Bilder, {ex.Hulls} Fahrwerke, {ex.Turrets} Waffen, {ex.Combos} Kombinationen");
        return ex.Frames > 0;
    }

    private void BakeOne(string path, string outName, Action<string> say)
    {
        if (!File.Exists(path)) return;
        try
        {
            var m = CwmFile.Load(path);
            string? cwp = Find($"DATA/{m.Tileset:00}.CWP");
            string? pal = Find($"DATA/{m.Tileset:00}.PAL");
            if (cwp == null || pal == null)
            {
                say($"{outName}: Tileset {m.Tileset:00} fehlt");
                return;
            }
            var cwpFile = CwpFile.Load(cwp);
            var palFile = PalFile.Load(pal);
            // The building patterns belong to the TILESET, not the map, so they
            // are written once per tileset — several maps share one. Without
            // them nothing can be built: the engine never opens a .CWP.
            WriteBuildingPatterns(cwpFile, palFile, m.Tileset);
            var baker = new MapBaker(m, cwpFile, palFile);
            var img = baker.Bake();
            img.SavePng($"{_dst}/Maps/{outName}.png");
            File.WriteAllText($"{_dst}/Maps/{outName}.json", MapMeta(m, baker), new UTF8Encoding(false));
            // the game state: units, buildings, markers, rails, zones. Without
            // it a map draws but nothing stands on it — and Content.Ready looks
            // for exactly this file to decide whether content exists at all.
            var doc = EntitiesJson.Decode(m);
            File.WriteAllText($"{_dst}/Maps/{outName}.entities.json",
                              EntitiesJson.Write(doc, outName["map_".Length..]),
                              new UTF8Encoding(false));
            // the weapon is entity +0x0c; the chassis is the unit_type
            foreach (var e in doc.Entities)
                _combos.Add((e.UnitType, e.Raw.Length > 0x0c ? e.Raw[0x0c] : 0));
            _tally.Add(doc);
            if (m.Sec(47) != null) _designSources.Add(m);
            // A numeric stem on a .CWM is a campaign mission and its number is
            // the mission number (see CatalogueExporter.WriteCampaign). The
            // extension matters: `1.DM` has the stem "1" as well, and letting
            // it through puts the saved state of level 26 in as mission 1.
            if (Path.GetExtension(path).Equals(".CWM", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(m.Stem, out int no) && no > 0)
                _missions.Add((no, outName, m));
            MapsBaked++;
            EntitiesWritten++;
            say($"{outName}: {img.GetWidth()}x{img.GetHeight()} gebacken, " +
                $"{doc.Entities.Count} Einheiten, {doc.Buildings.Count} Gebaeude, " +
                $"{baker.BuildingCellsSkipped} Gebaeudekacheln dem Renderer " +
                $"ueberlassen ({baker.MissedBuildingCells} nicht erkannt)");
        }
        catch (Exception e) { say($"{outName}: {e.Message}"); }
    }

    /// <summary>Tilesets already written this run — several maps share one.</summary>
    private readonly HashSet<int> _tilesetsWritten = new();

    /// <summary>The buildable types and their patterns, once per tileset. A
    /// file without the building tail is skipped in silence: every shipped
    /// .CWP has it, and a missing one simply means nothing can be built
    /// there.</summary>
    private void WriteBuildingPatterns(CwpFile cwp, PalFile pal, int tileset)
    {
        if (!cwp.HasBuildings || !_tilesetsWritten.Add(tileset)) return;
        Directory.CreateDirectory($"{_dst}/Buildings");
        File.WriteAllText($"{_dst}/Buildings/tileset_{tileset:00}.json",
                          BuildingPatterns.Write(cwp, tileset), new UTF8Encoding(false));
        // and the pictures, so a raised building can actually be seen
        var (png, meta) = BuildingPatterns.WriteAtlas(cwp, pal, tileset);
        if (png != null && meta.Length > 0)
        {
            png.SavePng($"{_dst}/Buildings/tileset_{tileset:00}_tiles.png");
            File.WriteAllText($"{_dst}/Buildings/tileset_{tileset:00}_tiles.json",
                              meta, new UTF8Encoding(false));
        }
        BuildingTilesets++;
    }

    /// <summary>How many tileset pattern files this run wrote.</summary>
    public int BuildingTilesets;

    /// <summary>The map's own description: size, tileset, and the per-cell grid
    /// the game needs for elevation and walkability.</summary>
    private static string MapMeta(CwmFile m, MapBaker b)
    {
        var sb = new StringBuilder(1 << 20);
        sb.Append('{');
        sb.Append($"\"map\":\"{Esc(m.Stem)}\",\"mission\":\"{Esc(m.Mission)}\",");
        sb.Append($"\"tileset\":{m.Tileset},\"width\":{m.Width},\"height\":{m.Height},");
        sb.Append($"\"tile_w\":{MapBaker.TileW},\"tile_h\":{MapBaker.TileH},");
        sb.Append($"\"elev_step\":{MapBaker.ElevStep},");
        sb.Append($"\"pixel_w\":{b.PixelW},\"pixel_h\":{b.PixelH},\"origin_y\":{b.OriginY},");
        sb.Append("\"tiles\":[");
        var rec = m.Records;
        bool first = true;
        for (int r = 0; r < m.Height; r++)
            for (int c = 0; c < m.Width; c++)
            {
                int o = (r * m.Width + c) * 4;
                int code = BitConverter.ToUInt16(rec, o);
                int elev = rec[o + 2], flag = rec[o + 3];
                bool isObj = code >= MapBaker.GroundMax;
                if (!first) sb.Append(',');
                first = false;
                sb.Append($"{{\"col\":{c},\"row\":{r},\"code\":{code},\"elev\":{elev},");
                sb.Append($"\"flag\":{flag},\"object\":{(isObj ? "true" : "false")},");
                sb.Append($"\"sx\":{c * MapBaker.TileW},");
                sb.Append($"\"sy\":{b.OriginY + r * MapBaker.TileH - elev * MapBaker.ElevStep - 50}}}");
            }
        sb.Append("]}");
        return sb.ToString();
    }

    private static string Esc(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private void WriteShips(ExeTables t)
    {
        var sb = new StringBuilder();
        sb.Append("{\"_source\":\"GAME.EXE 0x52eda0\",\"default\":[");
        var list = t.Ships();
        for (int i = 0; i < list.Count; i++)
        {
            var d = list[i];
            if (i > 0) sb.Append(',');
            sb.Append($"{{\"slot\":{i},\"player\":0,\"index\":{d.Index},\"enable\":{d.Enable},");
            sb.Append($"\"name\":\"{Esc(d.Name)}\",\"weapon\":{d.Weapon},\"chassis\":{d.Chassis},");
            sb.Append($"\"variant\":{d.Variant},\"cost_w\":{d.CostW},\"cost_ch\":{d.CostF},");
            sb.Append($"\"cost_sp\":{d.CostS},\"speed\":{d.Speed},\"energie\":{d.Energie},");
            sb.Append($"\"attack\":{d.Attack},\"defence\":{d.Defence},\"range1\":{d.Range1},");
            sb.Append($"\"range2\":{d.Range2},\"sight\":{d.Sight},\"ammo\":{d.Ammo},");
            sb.Append($"\"fuel\":{d.Fuel},\"reload\":{d.Reload},\"tech\":{d.Tech}}}");
        }
        sb.Append("],\"missions\":{},\"docks\":{}}");
        File.WriteAllText(_dst + "/Maps/ships.json", sb.ToString(), new UTF8Encoding(false));
    }

    /// <summary>The shape the game reveals around a unit — 20 x 20 spans out of
    /// the executable, so the fog of war opens exactly the cells the original
    /// opens. See <see cref="ExeTables.SightCircleTable"/> for how the main
    /// loop's own "unexplored" step led to it.</summary>
    private void WriteSightCircle(ExeTables t)
    {
        if (!t.SightCircleFound)
        {
            Log.Add("Sichtkreis: Tabelle in diesem Programmstand nicht gefunden");
            return;
        }
        int[] v = t.SightCircle();
        var sb = new StringBuilder(1 << 12);
        sb.Append("{\"_note\":\"the reveal shape, 20x20 u16 at 0x4f8a48: row = sight radius, ");
        sb.Append("column = steps in from the circle's rim, value = half-width. Read by the ");
        sb.Append("stamp @0x4200c0, which clamps the radius to 19; the step that calls it is ");
        sb.Append("the main loop's own 'unexplored' @0x4205b0, run every fifth tick\",");
        sb.Append($"\"radii\":{ExeTables.SightRadii},\"max_radius\":{ExeTables.SightMax},\"span\":[");
        for (int i = 0; i < v.Length; i++) { if (i > 0) sb.Append(','); sb.Append(v[i]); }
        sb.Append("]}");
        File.WriteAllText(_dst + "/Maps/sight_circle.json", sb.ToString(), new UTF8Encoding(false));
    }

    private void WriteAircraft(ExeTables t)
    {
        var sb = new StringBuilder();
        sb.Append("{\"_source\":\"GAME.EXE 0x51b021\",\"types\":[");
        var list = t.Aircraft();
        for (int i = 0; i < list.Count; i++)
        {
            var a = list[i];
            if (i > 0) sb.Append(',');
            sb.Append($"{{\"index\":{a.Index},\"name\":\"{Esc(a.Name)}\",\"short\":\"{Esc(a.Short)}\",");
            sb.Append($"\"hp\":{a.Hp},\"payload\":{a.Payload},\"airframe\":{a.Airframe},");
            sb.Append($"\"attack\":{a.Attack},\"defence\":{a.Defence},\"sight\":{a.Sight},");
            sb.Append($"\"ammo\":{a.Ammo},\"fuel\":{a.Fuel}}}");
        }
        sb.Append("]}");
        File.WriteAllText(_dst + "/Maps/aircraft.json", sb.ToString(), new UTF8Encoding(false));
    }

    /// <summary>What the importer cannot produce yet, named so the screen can
    /// say it. Each line is a Python extractor still to be ported; the readers
    /// they need are all in place.</summary>
    /// <summary>What the importer cannot derive — now nothing. The list stays
    /// so the screen can go on asking, and so the next gap has somewhere to be
    /// written down instead of being discovered by a black screen.</summary>
    public static List<string> Missing() => new();
}
