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
                TablesWritten += 2;
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
            var baker = new MapBaker(m, CwpFile.Load(cwp), PalFile.Load(pal));
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
                $"{doc.Entities.Count} Einheiten, {doc.Buildings.Count} Gebaeude");
        }
        catch (Exception e) { say($"{outName}: {e.Message}"); }
    }

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
