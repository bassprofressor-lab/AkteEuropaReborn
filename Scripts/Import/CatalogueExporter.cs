namespace AkteEuropaReborn.Import;

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

/// <summary>
/// The tables the interface reads: what a unit is called, what it can be built
/// from, what a weapon does, what there is to research, what the buildings are
/// named and what the order menu says.
///
/// Where each comes from:
///
///   unit_designs.json    CWM sec47, 1600 x 46 — only a .DM carries it, the
///                        campaign levels stop at section 39
///   orders.json          GAME.EXE, 40 x 30, latin-1
///   building_types.json  GAME.EXE, 16 x 20, cp437, plus the doors and the
///                        counts aggregated over every map's sec3
///   weapons.json         the component stats rows that deal damage
///   research.json        component stats rows 65..88
///   unit_catalog.json    the stats row of every unit type that appears, with
///                        how often it does and which categories it was seen in
///   infantry.json        the designs that ride on the infantry propulsions
///
/// Ported from design_export.py, weapons_export.py, research_export.py,
/// infantry_designs.py and speed_export.py.
/// </summary>
public sealed class CatalogueExporter
{
    private readonly ExeTables? _exe;
    private readonly string _dst;

    public int Designs, Weapons, WeaponTypes, Technologies, Units, BuildingTypes, InfantryDesigns;
    public int InfantryArms;

    public CatalogueExporter(ExeTables? exe, string mapsDir)
    {
        _exe = exe;
        _dst = mapsDir.TrimEnd('/', '\\');
    }

    public const int DesignStride = 46;

    /// <summary>What one map contributes: how often a unit type occurs, in
    /// which categories, and how the buildings break down by type.</summary>
    public sealed class Tally
    {
        public readonly Dictionary<int, int> UnitCount = new();
        public readonly Dictionary<int, SortedSet<int>> UnitCategories = new();
        public readonly Dictionary<int, int> BuildingCount = new();
        /// <summary>How often each door count was seen per type — the value the
        /// table reports is the most frequent one, not whichever record came
        /// first.</summary>
        public readonly Dictionary<int, Dictionary<int, int>> BuildingDoors = new();

        public int DoorsOf(int type)
        {
            if (!BuildingDoors.TryGetValue(type, out var hist)) return 0;
            int best = 0, bestN = -1;
            foreach (var kv in hist)
                if (kv.Value > bestN || (kv.Value == bestN && kv.Key < best))
                { best = kv.Key; bestN = kv.Value; }
            return best;
        }

        public void Add(EntitiesJson.Doc d)
        {
            foreach (var e in d.Entities)
            {
                UnitCount[e.UnitType] = UnitCount.GetValueOrDefault(e.UnitType) + 1;
                if (!UnitCategories.TryGetValue(e.UnitType, out var set))
                    UnitCategories[e.UnitType] = set = new SortedSet<int>();
                set.Add(e.Attack);
            }
            foreach (var b in d.Buildings)
            {
                BuildingCount[b.Type] = BuildingCount.GetValueOrDefault(b.Type) + 1;
                if (!BuildingDoors.TryGetValue(b.Type, out var hist))
                    BuildingDoors[b.Type] = hist = new Dictionary<int, int>();
                hist[b.Doors] = hist.GetValueOrDefault(b.Doors) + 1;
            }
        }
    }

    /// <summary>The campaign: mission number, the map it runs on, its title.
    ///
    /// The order is the file numbering, and that is not an assumption. The
    /// saved game `1.DM` calls its mission **"Mission 26"**, and its elevation
    /// grid is level 26's — the game numbers its missions by the level file.
    /// Checked over all thirteen saves: eight match a level's elevation exactly
    /// and the five Chanel Tunnel saves match level 25 on 95% of 39,600 cells
    /// (elevation moves a little during play), and not one of them contradicts
    /// the rule.
    ///
    /// What is NOT here is the unlock schedule. It lives in the exe as a run of
    /// call sites rather than a table, so reading it needs an instruction
    /// decoder; `campaign.json` in the development tree still carries it as
    /// derived metadata.</summary>
    public void WriteCampaign(IEnumerable<(int Number, string Map, CwmFile File)> missions,
                              Action<string>? say = null)
        => WriteCampaign(Rows(missions), say);

    private static IEnumerable<Row> Rows(IEnumerable<(int Number, string Map, CwmFile File)> ms)
    {
        foreach (var (n, map, f) in ms)
            yield return new Row(n, map, f.Mission, f.Width, f.Height, f.Tileset);
    }

    /// <summary>One line of the campaign list, whatever it was read from.</summary>
    public readonly record struct Row(int Number, string Map, string Title,
                                      int Width, int Height, int Tileset);

    /// <summary>The same list, but from rows rather than from open .CWM files —
    /// so it can be rewritten from the maps ALREADY imported, without a disc in
    /// the drive. Everything the list carries (title, size, tileset) is in the
    /// per-map <c>map_NN.json</c> the baker wrote.</summary>
    public void WriteCampaign(IEnumerable<Row> missions, Action<string>? say = null)
    {
        var list = new SortedDictionary<int, Row>();
        foreach (var r in missions) list[r.Number] = r;
        var names = _exe?.MissionNameList() ?? new List<string>();
        var sb = new StringBuilder();
        sb.Append("{\"_note\":\"the campaign in file order — the level file number IS the ");
        sb.Append("mission number\",");
        sb.Append("\"_evidence\":\"1.DM is called 'Mission 26' and its elevation grid is ");
        sb.Append("level 26's; 13 of 13 saves identify with a level and none contradicts\",");
        sb.Append("\"_open\":\"the per-mission unlock schedule is not derived here — it is a ");
        sb.Append("run of call sites in the exe, not a table\",\"missions\":[");
        bool first = true;
        foreach (var kv in list)
        {
            if (!first) sb.Append(',');
            first = false;
            sb.Append($"{{\"index\":{kv.Key},\"map\":\"{Esc(kv.Value.Map)}\",");
            sb.Append($"\"title\":\"{Esc(kv.Value.Title)}\",");
            // the campaign's own name for this slot — "Mission 7" and the like,
            // straight out of the table the counter indexes
            if (kv.Key < names.Count) sb.Append($"\"slot_name\":\"{Esc(names[kv.Key])}\",");
            sb.Append($"\"width\":{kv.Value.Width},\"height\":{kv.Value.Height},");
            sb.Append($"\"tileset\":{kv.Value.Tileset}}}");
            Missions++;
        }
        sb.Append("]}");
        File.WriteAllText(_dst + "/campaign.json", sb.ToString(), new UTF8Encoding(false));
        say?.Invoke($"Kampagne: {Missions} Missionen");
    }

    public int Missions;

    public void Run(Tally tally, IEnumerable<CwmFile> maps, Action<string>? say = null)
    {
        Directory.CreateDirectory(_dst);
        WriteDesigns(maps, tally, say);
        WriteBuildingTypes(tally, say);
        WriteCatalogue(tally, say);
        RunExeOnly(say);
    }

    /// <summary>
    /// The tables that come out of GAME.EXE alone — no map is read for these,
    /// so they can be rewritten without a level in reach.
    ///
    /// ⚠ Kept apart from <see cref="Run"/> on purpose. The design list, the
    /// building types and the unit catalogue are counted OFF THE MAPS, and
    /// writing them from an empty tally replaces good files with empty ones.
    /// That happened once, on 2026-08-06, the first time the tables were
    /// re-exported on their own.
    /// </summary>
    public void RunExeOnly(Action<string>? say = null)
    {
        Directory.CreateDirectory(_dst);
        WriteOrders(say);
        WriteWeapons(say);
        WriteResearch(say);
        WriteComponentStats(say);
        WriteDiplomacy(say);
        WriteResources(say);
        WriteMissionPlans(say);
        WriteSchedule(say);
    }

    /// <summary>
    /// The per-mission unlock schedule, in the shape
    /// <see cref="Campaign.CampaignManager"/> already reads.
    ///
    /// A file of this name has shipped since July, written by an earlier Python
    /// tool. ⚠ It is INCOMPLETE, and measurably so: it hands design 52 to the
    /// player from mission 8 where the game gives it from mission 6, and design
    /// 51 from mission 15 where the game gives it from 12. The end state after
    /// mission 33 is the same, which is why it went unnoticed — only the
    /// missions in between were poorer than the original's.
    ///
    /// Written from <see cref="ExeTables.MissionUnlocks"/>, so it now comes out
    /// of the player's own GAME.EXE like every other table.
    ///
    /// <para>⚠ 10.08.2026 — until today this wrote a QUARTER of the schedule.
    /// The busiest of the four setters, `set_part(player, part, value)` with
    /// 1037 of 1533 call sites in the mission blocks, was read as a
    /// two-argument call and then dropped on the floor here. It now comes out
    /// as <c>components</c> / <c>components_off</c>, a list of
    /// <c>[Spieler, Bauteil]</c> pairs per state — and unlike the other three
    /// it is PER PLAYER, because the original writes it per player
    /// (`[0x5045a0 + 58*(part + 200*player)]`) while design, ship and aircraft
    /// each loop over all eight inside the setter.</para>
    ///
    /// <para>⚠ It is DATA, not a barrier. Who reads the ownership byte in the
    /// original was measured before this was written: thirteen readers, and
    /// every one of them is a menu — the construction screen's three pickers
    /// (»Fahrwerk«/»Verbesserung«/»Aufbauteil« @0x46C490), the chassis list
    /// @0x455870, `research_offer_refresh` @0x4AA950, and the market module
    /// @0x4C0860..0x4C0E60, which uses it to choose WHICH design turns up for
    /// sale, not to forbid anything. Neither `build_in_base` nor the production
    /// button looks at it. Same finding as for the sec47 release byte.</para>
    /// </summary>
    private void WriteSchedule(Action<string>? say)
    {
        if (_exe == null || !_exe.HasMissionPlans) return;
        var all = _exe.MissionUnlocks;
        if (all.Count == 0) return;

        var sb = new StringBuilder();
        sb.Append("{\"_note\":\"Freischalt-Fahrplan der Kampagne, aus dem Missionsaufbau ");
        sb.Append($"der GAME.EXE gelesen (Verteiler @0x{_exe.VyrobaCaseTable:x}). Abgeleitete ");
        sb.Append("Metadaten, kein Originalinhalt.\",");
        sb.Append("\"_mapping\":\"Zustand N == Mission N == Leveldatei NN.CWM; die Nummer steht ");
        sb.Append("im Kopf jeder Karte und ist durch 13 von 13 Spielstaenden bestaetigt.\",");
        sb.Append("\"states\":[");
        bool first = true;
        int rows = 0;
        foreach (var m in all.Keys)
        {
            // ⚠ Mission 0 is left out on purpose. Its block switches on designs
            // 50..99, ships 0..9 and aircraft 0..9 — everything — but NO map
            // carries mission number 0: the number sits in the file header and
            // runs 1..15 for the campaign and 51..58 for the NET maps, over 23
            // of 23 files. That block never executes, and letting it into a
            // schedule that accumulates `state <= mission` hands the player the
            // whole roster in mission 1.
            if (m == 0) continue;
            var ranges = all[m];
            var veh = new SortedSet<int>();
            var shp = new SortedSet<int>();
            var air = new SortedSet<int>();
            // the component rows of this state, in the order the block writes
            // them — a state may switch one on and another off, so both lists
            // are kept and applied in that order
            var comp = new SortedSet<(int Player, int Part)>();
            var compOff = new SortedSet<(int Player, int Part)>();
            foreach (var r in ranges)
            {
                if (r.Kind == "part")
                {
                    for (int x = r.From; x <= r.To; x++)
                    {
                        var key = (r.Player, x);
                        if (r.Value != 0) { comp.Add(key); compOff.Remove(key); }
                        else { compOff.Add(key); comp.Remove(key); }
                        rows++;
                    }
                    continue;
                }
                var into = r.Kind switch
                {
                    "design" => veh,
                    "ship" => shp,
                    "aircraft" => air,
                    _ => null,
                };
                if (into == null) continue;
                for (int x = r.From; x <= r.To; x++)
                { if (r.Value != 0) into.Add(x); else into.Remove(x); rows++; }
            }
            if (veh.Count == 0 && shp.Count == 0 && air.Count == 0 &&
                comp.Count == 0 && compOff.Count == 0) continue;
            if (!first) sb.Append(',');
            first = false;
            sb.Append($"{{\"state\":{m},\"vehicles\":[{string.Join(",", veh)}],");
            sb.Append($"\"ships\":[{string.Join(",", shp)}],\"ships_off\":[],");
            sb.Append($"\"aircraft\":[{string.Join(",", air)}],");
            sb.Append($"\"components\":[{Pairs(comp)}],");
            sb.Append($"\"components_off\":[{Pairs(compOff)}]}}");
        }
        sb.Append("]}");
        File.WriteAllText(_dst + "/campaign_schedule.json", sb.ToString(), new UTF8Encoding(false));
        say?.Invoke($"Fahrplan: {all.Count} Missionen, {rows} Freischaltungen");
    }

    /// <summary>`[Spieler, Bauteil]` pairs as JSON.</summary>
    private static string Pairs(SortedSet<(int Player, int Part)> set)
    {
        var sb = new StringBuilder();
        foreach (var (p, x) in set)
        {
            if (sb.Length > 0) sb.Append(',');
            sb.Append('[').Append(p).Append(',').Append(x).Append(']');
        }
        return sb.ToString();
    }

    /// <summary>
    /// What each computer player of a campaign mission produces.
    ///
    /// ⚠ This does NOT come out of a map: a campaign level stops at section 38
    /// and carries no build programme at all. The game builds one at mission
    /// start by running straight-line code picked by the mission number
    /// (<see cref="ExeTables.MissionPlans"/>), so the only way to have it is to
    /// read that code — which is what this writes out.
    ///
    /// A line's <c>what</c> is the design row WITHIN THE PLAYER, exactly as the
    /// original computes it (<c>design + 200*player</c> @0x4BB258): to look it
    /// up in unit_designs.json, add <c>200*player</c>.
    /// </summary>
    private void WriteMissionPlans(Action<string>? say)
    {
        if (_exe == null || !_exe.HasMissionPlans) return;
        var all = _exe.MissionPlans;
        var sb = new StringBuilder();
        sb.Append("{\"_note\":\"AI build programmes, read out of GAME.EXE's mission setup ");
        sb.Append($"(dispatch @0x{_exe.VyrobaCaseTable:x}, add_vyroba @0x{_exe.AddVyroba:x}). ");
        sb.Append("A line is [kind,what,third]; kind 0 = unit design (add 200*player to index ");
        sb.Append("unit_designs), 1 = aircraft, 2 = unused by ai_production.\",\"missions\":{");
        bool firstM = true;
        int lines = 0;
        foreach (var m in all.Keys)
        {
            if (!firstM) sb.Append(',');
            firstM = false;
            sb.Append($"\"{m}\":{{");
            bool firstP = true;
            foreach (var p in all[m].Keys)
            {
                if (!firstP) sb.Append(',');
                firstP = false;
                sb.Append($"\"{p}\":[");
                var rows = all[m][p];
                for (int i = 0; i < rows.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    sb.Append($"[{rows[i].Kind},{rows[i].What},{rows[i].Third}]");
                    lines++;
                }
                sb.Append(']');
            }
            sb.Append('}');
        }
        sb.Append("}}");
        File.WriteAllText(_dst + "/mission_plans.json", sb.ToString(), new UTF8Encoding(false));
        say?.Invoke($"Bauplaene: {all.Count} Missionen, {lines} Zeilen");
    }

    public int ResourceLevelsWritten;

    /// <summary>
    /// The skirmish option "Rohstoffe: keine / wenige / normal / viele" — what
    /// each setting puts into which building.
    ///
    /// See <see cref="ExeTables.ResourceLevels"/> for the reading. The one thing
    /// worth repeating here: the routine has exactly ONE caller, in the
    /// game-start message handler, so this belongs to a skirmish and must not
    /// touch a campaign mission — those keep the stores their level file gives
    /// them.
    /// </summary>
    private void WriteResources(Action<string>? say)
    {
        if (_exe == null || !_exe.HasResourceTables)
        {
            say?.Invoke("Rohstoffe: in dieser GAME.EXE kein fill_resources gefunden — uebersprungen");
            return;
        }
        var exe = _exe;
        var levels = exe.ResourceLevels();
        var sb = new StringBuilder();
        sb.Append("{\"_note\":\"the skirmish option 'Rohstoffe', read out of GAME.EXE ");
        sb.Append("fill_resources — which building type gets what at each of the four ");
        sb.Append("settings. The type mapping is read BACK from the routine's own jump ");
        sb.Append("table, not assigned here; the four names come from the menu code that ");
        sb.Append("switches the same option variable.\",");
        sb.Append("\"_scope\":\"skirmish and network only — the routine has exactly one ");
        sb.Append("caller, in the game-start message handler, so a campaign mission keeps ");
        sb.Append("the stores its level file gives it\",");
        sb.Append($"\"_found_in_this_exe\":{{\"dispatch\":\"0x{exe.ResourceDispatch:x}\",");
        sb.Append($"\"option\":\"0x{exe.ResourceOptionVar:x}\",");
        sb.Append($"\"stores\":\"0x{exe.ResourceStoreTable:x}\",");
        sb.Append($"\"factory\":\"0x{exe.ResourceFactoryTable:x}\",");
        sb.Append($"\"mine\":\"0x{exe.ResourceMineTable:x}\"}},");
        sb.Append($"\"slots_scanned\":{ExeTables.ResourceSlotsScanned},\"fill\":{{");
        bool f1 = true;
        for (int t = 0; t <= ExeTables.BuildingTypeCount; t++)
        {
            if (!f1) sb.Append(',');
            f1 = false;
            sb.Append($"\"{t}\":\"{exe.ResourceFillOf(t).ToString().ToLowerInvariant()}\"");
        }
        sb.Append("},\"levels\":[");
        bool f2 = true;
        foreach (var l in levels)
        {
            if (!f2) sb.Append(',');
            f2 = false;
            sb.Append($"{{\"level\":{l.Level},\"name\":\"{Esc(l.Name)}\",");
            sb.Append($"\"weapons\":{l.Weapons},\"chassis\":{l.Chassis},");
            sb.Append($"\"special\":{l.Special},\"terranium\":{l.Terranium},");
            sb.Append($"\"deposit\":{l.Deposit}}}");
            ResourceLevelsWritten++;
        }
        sb.Append("]}");
        File.WriteAllText(_dst + "/resources.json", sb.ToString(), new UTF8Encoding(false));
        say?.Invoke($"Rohstoffe: {ResourceLevelsWritten} Stufen (" +
                    string.Join("/", levels.ConvertAll(l => l.Name)) + ")");
    }

    public int DiplomacyMissions;

    /// <summary>
    /// Who fights whom in the campaign, and which slot stands aside.
    ///
    /// The only table here that is not a table: it is the code of
    /// <c>mission_init</c> @0x487c40, which the map loader runs on every start.
    /// See <see cref="ExeTables.CampaignDiplomacy"/> for the whole reading and
    /// for why player 7 is the neutral one in all 33 missions.
    ///
    /// This retires a guess. Until now the remake decided who was playing by
    /// asking which slot owned a headquarters — written down as ours, and known
    /// to be wrong on map_07. The alliances were in the executable the whole
    /// time; nobody had read the mission scripts.
    /// </summary>
    private void WriteDiplomacy(Action<string>? say)
    {
        if (_exe == null || !_exe.HasCampaignDiplomacy)
        {
            say?.Invoke("Diplomatie: in dieser GAME.EXE kein mission_init gefunden — uebersprungen");
            return;
        }
        var exe = _exe;
        var sb = new StringBuilder();
        sb.Append("{\"_note\":\"the campaign's alliances and neutral slots, read out of ");
        sb.Append("GAME.EXE mission_init, which the map loader runs on every start. ");
        sb.Append("set_relation writes the player record's +0x15 BOTH WAYS, set_neutral ");
        sb.Append("writes byte[0xb38d38+player] — the field the .DM files fill from ");
        sb.Append("sec106 and the takeover scan tests.\",");
        sb.Append($"\"_found_in_this_exe\":{{\"dispatch\":\"0x{exe.DiplomacyDispatch:x}\",");
        sb.Append($"\"index\":\"0x{exe.DiplomacyIndex:x}\",\"table\":\"0x{exe.DiplomacyTable:x}\",");
        sb.Append($"\"set_neutral\":\"0x{exe.SetNeutralAt:x}\",");
        sb.Append($"\"field\":\"0x{exe.NeutralField:x}\"}},");
        sb.Append($"\"_evidence\":\"the neutral slot is MEASURED: player {exe.NeutralPlayer} is ");
        sb.Append("the only one allied with everybody in all 33 matrices, and ");
        sb.Append($"{exe.NeutralSitesConst} of the {exe.NeutralSites} call sites of ");
        sb.Append("set_neutral push exactly him (the odd one out is the loop that clears ");
        sb.Append("all eight). Checked against aekernel/campaign_diplomacy.py and ");
        sb.Append("diplo_relocate.py: 33 of 33 missions identical, and identical across ");
        sb.Append("the two different builds of GAME.EXE on this machine.\",");
        sb.Append("\"missions\":[");
        bool first = true;
        for (int m = 1; m <= ExeTables.CampaignMissions; m++)
        {
            var d = _exe.CampaignDiplomacy(m);
            if (d == null) continue;
            if (!first) sb.Append(',');
            first = false;
            sb.Append($"{{\"mission\":{m},\"allied\":[");
            for (int a = 0; a < 8; a++)
            {
                if (a > 0) sb.Append(',');
                sb.Append('[');
                for (int b = 0; b < 8; b++)
                {
                    if (b > 0) sb.Append(',');
                    sb.Append(d.Allied[a, b] ? '1' : '0');
                }
                sb.Append(']');
            }
            sb.Append("],\"neutral\":[");
            bool f2 = true;
            for (int p = 0; p < 8; p++)
                if (d.Neutral[p]) { if (!f2) sb.Append(','); f2 = false; sb.Append(p); }
            sb.Append("]}");
            DiplomacyMissions++;
        }
        sb.Append("]}");
        File.WriteAllText(_dst + "/campaign_diplomacy.json", sb.ToString(), new UTF8Encoding(false));
        say?.Invoke($"Diplomatie: {DiplomacyMissions} Missionen, neutraler Spieler {exe.NeutralPlayer} " +
                    $"(mission_init @0x{exe.DiplomacyDispatch:x}, {exe.NeutralSitesConst}/{exe.NeutralSites} Aufrufstellen)");
    }

    public int ComponentRows;

    /// <summary>The stats array as raw rows, counted from the ARRAY base
    /// (0x5045a0) rather than from record 0.
    ///
    /// It is written out whole, unread, on purpose. The design arithmetic
    /// @0x4b1fb0 reaches into fourteen different offsets of a row — +0x0d, +0x0e,
    /// +0x10..+0x14, +0x16, +0x18, +0x1a, +0x1e, +0x20..+0x22 — and only two of
    /// them have a name anyone has earned (hp_max at +0x1a, the component id at
    /// +0x0d). Naming the other twelve here would be invention; carrying the
    /// bytes across and letting <see cref="Simulation.DesignMath"/> apply the
    /// original's own formula to them is not.
    ///
    /// Rows 0..199 = player block 0. The array continues with a block per player
    /// (a technology state each), but only block 0 is filled in the file; the
    /// others are copied into place at start-up (rep movsd @0x4b22ed).</summary>
    private void WriteComponentStats(Action<string>? say)
    {
        if (_exe == null) return;
        var sb = new StringBuilder(1 << 16);
        sb.Append($"{{\"_note\":\"component stats rows from GAME.EXE @VA 0x{_exe.StatsBase - 0x1a:x}");
        sb.Append(", stride 58, counted from the ARRAY base (rows 0..199 = player block 0)\",");
        sb.Append("\"_use\":\"the design arithmetic @0x4b1fb0 indexes this with a bare component ");
        sb.Append("number - weapon, propulsion and equipment alike - to derive a design record's ");
        sb.Append("tail from +0x1a; see Scripts/Simulation/DesignMath.cs\",");
        sb.Append($"\"stride\":{ExeTables.StatsStride},\"rows\":{{");
        bool first = true;
        for (int row = 0; row < 200; row++)
        {
            var r = _exe.ComponentRow(row);
            if (r.Length != ExeTables.StatsStride) continue;
            if (CwmExtra.AllZero(r, 0, r.Length)) continue;
            if (!first) sb.Append(',');
            first = false;
            sb.Append($"\"{row}\":\"{Hex(r, 0, r.Length)}\"");
            ComponentRows++;
        }
        sb.Append("}}");
        File.WriteAllText(_dst + "/component_stats.json", sb.ToString(), new UTF8Encoding(false));
        say?.Invoke($"Bauteil-Stats: {ComponentRows} Zeilen");
    }

    // ---- sec47: the design list --------------------------------------------

    private readonly Dictionary<int, (string Name, int Weapon, int Propulsion, int Body)> _designs = new();

    /// <summary>sec47 — 1600 records of 46 (dest 0x51ce20): +0x02 name,
    /// +0x17 weapon, +0x18 propulsion, +0x19 body. The offsets were pinned by
    /// the designs whose name spells its own components out
    /// ("H-Cannon-81-165").
    ///
    /// Only a .DM carries the section, and the saved missions do not all hold
    /// the same number of designs, so the fullest list wins rather than the
    /// first — the Python export picked 3.DM by hand for the same reason.
    /// </summary>
    private void WriteDesigns(IEnumerable<CwmFile> maps, Tally tally, Action<string>? say)
    {
        CwmFile? src = null;
        byte[]? s47 = null;
        int best = -1;
        foreach (var m in maps)
        {
            var s = m.Sec(47);
            if (s == null || s.Length < DesignStride) continue;
            int named = 0;
            for (int i = 0; (i + 1) * DesignStride <= s.Length; i++)
                if (Cp437.GetString(s, i * DesignStride + 2, 20).Length > 0) named++;
            if (named > best) { best = named; src = m; s47 = s; }
        }
        if (s47 == null)
        {
            say?.Invoke("Designliste: keine Karte mit sec47 dabei (nur .DM tragen sie)");
            return;
        }

        var sb = new StringBuilder(1 << 18);
        sb.Append($"{{\"_note\":\"unit designs from CWM sec47 (1600 x 46) of {Esc(src!.Stem)}\",");
        sb.Append("\"_fields\":\"name +0x02, weapon +0x17, propulsion +0x18, body +0x19\",");
        sb.Append("\"_choice\":\"the list belongs to a saved game, so the saves differ; ");
        sb.Append("the fullest one is taken — OUR choice, not a rule from the data\",");
        sb.Append("\"designs\":{");
        bool first = true;
        for (int i = 0; (i + 1) * DesignStride <= s47.Length; i++)
        {
            int o = i * DesignStride;
            if (CwmExtra.AllZero(s47, o, DesignStride)) continue;
            string nm = Cp437.GetString(s47, o + 2, 20);
            if (nm.Length == 0) continue;
            int weap = s47[o + 0x17], prop = s47[o + 0x18], body = s47[o + 0x19];
            _designs[i] = (nm, weap, prop, body);
            if (!first) sb.Append(',');
            first = false;
            sb.Append($"\"{i}\":{{\"name\":\"{Esc(nm)}\",\"weapon\":{weap},");
            sb.Append($"\"propulsion\":{prop},\"body\":{body},");
            sb.Append($"\"flags\":[{s47[o]},{s47[o + 1]}],\"raw\":\"{Hex(s47, o, DesignStride)}\"}}");
            Designs++;
        }
        sb.Append("}}");
        File.WriteAllText(_dst + "/unit_designs.json", sb.ToString(), new UTF8Encoding(false));
        say?.Invoke($"Designliste: {Designs} Entwuerfe aus {src.Stem}");
        WriteInfantry(say);
    }

    // ---- the exe string tables ---------------------------------------------

    private void WriteOrders(Action<string>? say)
    {
        if (_exe == null) return;
        var list = _exe.Orders();
        var sb = new StringBuilder();
        sb.Append($"{{\"_note\":\"order vocabulary from GAME.EXE @VA 0x{_exe.OrderBase:x}, stride 30\",");
        sb.Append("\"orders\":[");
        for (int i = 0; i < list.Count; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append($"\"{Esc(list[i])}\"");
        }
        sb.Append("]}");
        File.WriteAllText(_dst + "/orders.json", sb.ToString(), new UTF8Encoding(false));
        int named = 0;
        foreach (string s in list) if (s.Length > 0) named++;
        say?.Invoke($"Befehle: {named} Eintraege");
    }

    private void WriteBuildingTypes(Tally tally, Action<string>? say)
    {
        if (_exe == null) return;
        // ⚠ An empty tally means no level was read, not that the game has no
        // buildings. Writing the table anyway replaces a good file with an
        // empty one — which is exactly what happened on 2026-08-06.
        if (tally.BuildingCount.Count == 0)
        { say?.Invoke("Gebaeudetypen: keine Karte gezaehlt — Datei bleibt, wie sie ist"); return; }
        var names = _exe.BuildingNames();
        var sb = new StringBuilder();
        sb.Append("{\"_note\":\"building type names from GAME.EXE, 16 entries of 20 bytes; ");
        sb.Append("the map field typ is 1-BASED (name = table[typ-1])\",");
        sb.Append("\"_filter\":\"only typ 0..16 counts as a building type: sec3 also holds ");
        sb.Append("entries with owner 255 and hp 700/700 whose names are places or Init0..N ");
        sb.Append("(what an early reading took for triggers), and their typ byte runs up to 74 ");
        sb.Append("— beyond the 16 names the exe carries\",");
        sb.Append($"\"_source\":\"GAME.EXE 0x{_exe.BuildingNameBase:x} + CWM sec3\",\"types\":{{");
        bool first = true;
        var seen = new SortedSet<int>(tally.BuildingCount.Keys);
        // The three buildable types must be in the file even when no map of
        // this install happens to carry one: a Depot, a Generator or a
        // Feld-Rohstoffmine can be RAISED, and then its hit points and doors
        // have to come from somewhere.
        seen.Add(5); seen.Add(7); seen.Add(15);
        foreach (int t in seen)
        {
            if (t > ExeTables.BuildingTypeCount) continue;
            string nm = t >= 1 && t - 1 < names.Count ? names[t - 1] : "";
            if (!first) sb.Append(',');
            first = false;
            sb.Append($"\"{t}\":{{\"name\":\"{Esc(nm)}\",");
            sb.Append($"\"doors\":{tally.DoorsOf(t)},");
            sb.Append($"\"count\":{(tally.BuildingCount.TryGetValue(t, out int bc) ? bc : 0)}");
            // from the exe's own 10-byte stat row — what a NEW building of this
            // type is made of (add_building @0x4C8D60)
            var st = _exe.BuildingStats(t);
            sb.Append($",\"hp\":{st.Hp},\"door_count\":{st.DoorCount},\"door_cells\":[");
            for (int i = 0; i < st.Doors.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append($"{{\"col\":{st.Doors[i].Col},\"row\":{st.Doors[i].Row}}}");
            }
            sb.Append("]");
            // and where the type watches from — the fog update @0x4205B0 looks
            // this up per type instead of computing it, and then stamps a circle
            // of a FIXED ten cells for every building there is
            var (sc, sr) = _exe.SightCentre(t);
            sb.Append($",\"sight_col\":{sc},\"sight_row\":{sr}");
            sb.Append('}');
            BuildingTypes++;
        }
        sb.Append("},");
        sb.Append($"\"sight_radius\":{ExeTables.BuildingSightRadius},");
        sb.Append("\"_sight\":\"sight_col/sight_row are the offset from the ");
        sb.Append("building's corner cell to the point it watches from, read per ");
        sb.Append("type at 0x4206AB..0x4206D6; sight_radius is the `push 0xa` in ");
        sb.Append("front of the stamper — the same for every type, radar post ");
        sb.Append("included\"}");
        File.WriteAllText(_dst + "/building_types.json", sb.ToString(), new UTF8Encoding(false));
        say?.Invoke($"Gebaeudetypen: {BuildingTypes}");
    }

    // ---- the stats rows -----------------------------------------------------

    /// <summary>The component name table, read whole.
    ///
    /// ⚠ CORRECTED 2026-08-06. This used to scan rows 100..199 and keep only
    /// rows that deal damage, which yielded SIX components (21, 24, 26, 27, 28,
    /// 38) out of the 56 the table names. Everything else fell back to the
    /// default and every unit in the game showed "2x Maschinengewehr".
    ///
    /// The named rows and what they carry:
    ///     1..19    the weapons          -> components 21..39
    ///     65..79   the equipment        -> components 40..54
    ///     81..88   the abilities        (no component, no sprite)
    ///     140..145 an ABBREVIATED SECOND LIST of six weapons — the only thing
    ///              the old scan ever saw
    ///     160..175 the propulsions      -> components 1..18
    ///     190..199 the infantry arms    (component 0, keyed by row)
    ///
    /// ⚠ The two weapon lists CONTRADICT each other: row 5 calls component 25
    /// "2x Maschinengewehr" and component 24 "Maschinengewehr", while row 140
    /// calls component 24 "2x Maschinengewehr". The maps decide — over the 29
    /// imported entities.json BOTH 24 (190 units) and 25 (131) occur at +0x0c,
    /// so they are two different weapons and the 1..19 block is the real list.
    /// The 140 block is carried as `alt_rows` for the record, never as a name.
    ///
    /// Fields: +0x04 damage, +0x06 raw range. The range in tiles is
    /// `range_raw / 10`, which is OUR scaling and not recovered data.</summary>
    private void WriteWeapons(Action<string>? say)
    {
        if (_exe == null) return;
        var sb = new StringBuilder();
        sb.Append($"{{\"source\":\"GAME.EXE component table @VA 0x{_exe.StatsBase:x}, stride 58\",");
        sb.Append("\"component_id_rule\":\"component_id(record N) = record[N-1][+0x2d] ");
        sb.Append("(tail describes the successor)\",");
        sb.Append("\"fields\":\"damage=+0x04, range_raw=+0x06 (tentative, monotone with weapon class)\",");
        sb.Append("\"range_scaling\":\"range_tiles = range_raw / 10  (our choice, not recovered data)\",");
        // Rows 1..19 are the weapon TYPES a design is built from — the value in
        // a design record's +0x17. They are not the same thing as the weapon
        // components below, which is why both belong in this file: the design
        // screen picks a row, the sprite work needs a component.
        sb.Append("\"_types\":\"stats rows 1..19: the weapons a design can carry (record +0x17)\",");
        sb.Append("\"types\":{");
        bool ft = true;
        for (int row = 1; row <= 19; row++)
        {
            var s = _exe.StatsFor(row);
            if (s == null || s.Name.Length == 0) continue;
            if (!ft) sb.Append(',');
            ft = false;
            sb.Append($"\"{row}\":{{\"name\":\"{Esc(s.Name)}\",\"damage\":{s.Raw[4]},");
            sb.Append($"\"range_raw\":{s.Raw[6]}}}");
            WeaponTypes++;
        }
        sb.Append("},");
        // No default any more: a component this table does not name is shown as
        // a gap, not as somebody else's weapon.
        sb.Append("\"_no_default\":\"an unnamed component is a gap, not the MG\",");
        sb.Append("\"weapons\":{");
        bool first = true;
        foreach (var (from, to, kind) in new[]
                 { (1, 19, "weapon"), (65, 79, "equipment"), (160, 175, "propulsion") })
            for (int row = from; row <= to; row++)
            {
                var s = _exe.StatsFor(row);
                if (s == null || s.Raw.Length < 58) continue;
                if (s.Name.Length == 0 || s.Name == "(nichts)" || s.ComponentId == 0) continue;
                int dmg = s.Raw[4], rng = s.Raw[6];
                if (!first) sb.Append(',');
                first = false;
                sb.Append($"\"{s.ComponentId}\":{{\"name\":\"{Esc(s.Name)}\",\"row\":{row},");
                sb.Append($"\"kind\":\"{kind}\",\"damage\":{dmg},\"range_raw\":{rng},");
                sb.Append($"\"range_tiles\":{Tiles(rng)}}}");
                Weapons++;
            }
        sb.Append("},");
        // The infantry arms carry no component id — they are keyed by row, the
        // way the infantry draw branch reaches them.
        sb.Append("\"infantry_arms\":{");
        first = true;
        for (int row = 190; row <= 199; row++)
        {
            var s = _exe.StatsFor(row);
            if (s == null || s.Raw.Length < 58 || s.Name.Length == 0) continue;
            if (!first) sb.Append(',');
            first = false;
            sb.Append($"\"{row}\":{{\"name\":\"{Esc(s.Name)}\",\"damage\":{s.Raw[4]},");
            sb.Append($"\"range_raw\":{s.Raw[6]},\"range_tiles\":{Tiles(s.Raw[6])}}}");
            InfantryArms++;
        }
        sb.Append("},");
        // Kept for the record, deliberately NOT a name source — see the summary.
        sb.Append("\"_alt_rows\":\"rows 140..145, the abbreviated second weapon ");
        sb.Append("list; contradicts rows 1..19 on component 24 and is not used\",");
        sb.Append("\"alt_rows\":{");
        first = true;
        for (int row = 140; row <= 145; row++)
        {
            var s = _exe.StatsFor(row);
            if (s == null || s.Raw.Length < 58 || s.Name.Length == 0) continue;
            if (!first) sb.Append(',');
            first = false;
            sb.Append($"\"{row}\":{{\"name\":\"{Esc(s.Name)}\",\"component\":{s.ComponentId}}}");
        }
        sb.Append("}}");
        File.WriteAllText(_dst + "/weapons.json", sb.ToString(), new UTF8Encoding(false));
        say?.Invoke($"Waffen: {Weapons} Komponenten, {WeaponTypes} Bauarten, "
                    + $"{InfantryArms} Infanteriewaffen");
    }

    private static string Tiles(int raw)
        => (raw / 10.0).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>The technologies are stats rows 65..88; a design's equipment
    /// value IS that row number. Rows up to 79 are mountable on a design, the
    /// rest is pure ability research.</summary>
    private void WriteResearch(Action<string>? say)
    {
        if (_exe == null) return;
        var sb = new StringBuilder();
        sb.Append("{\"_note\":\"technologies = component stats rows 65..88; a design's ");
        sb.Append("equipment value is that row number\",");
        sb.Append("\"_cost\":\"NOT recovered\",\"technologies\":{");
        bool first = true;
        for (int ut = 65; ut <= 88; ut++)
        {
            var s = _exe.StatsFor(ut);
            if (s == null || s.Name.Length == 0 || s.Name == "(nichts)") continue;
            if (!first) sb.Append(',');
            first = false;
            sb.Append($"\"{ut}\":{{\"unit_type\":{ut},\"name\":\"{Esc(s.Name)}\",");
            sb.Append($"\"class1\":{s.Raw[0x09]},\"class2\":{s.Raw[0x0a]},");
            sb.Append($"\"equipment\":{(ut <= 79 ? "true" : "false")}}}");
            Technologies++;
        }
        sb.Append("}}");
        File.WriteAllText(_dst + "/research.json", sb.ToString(), new UTF8Encoding(false));
        say?.Invoke($"Forschung: {Technologies} Technologien");
    }

    // ---- the unit catalogue -------------------------------------------------

    /// <summary>Every unit type that appears on a map, with its stats row.
    ///
    /// Wider than the older hand-made catalogue, which listed sixteen: this one
    /// covers whatever the maps actually contain, which now includes the discs'
    /// levels 16..33. `speed_raw` and `component_id` both carry the tail
    /// off-by-one — a record describes its SUCCESSOR — so both are read from
    /// the PREDECESSOR record.</summary>
    private void WriteCatalogue(Tally tally, Action<string>? say)
    {
        if (_exe == null) return;
        // See WriteBuildingTypes: an empty tally is a missing level, not a
        // missing game. Leave the file alone rather than emptying it.
        if (tally.UnitCount.Count == 0)
        { say?.Invoke("Einheitenkatalog: keine Karte gezaehlt — Datei bleibt, wie sie ist"); return; }
        var sb = new StringBuilder();
        sb.Append("{\"_note\":\"unit catalogue from the maps' entities plus the GAME.EXE ");
        sb.Append("component stats\",");
        sb.Append("\"_component_id_note\":\"component_id(N) = record[N-1][+0x2d]\",");
        sb.Append("\"_speed_note\":\"speed_raw = record[unit_type-1][+0x30] (same tail off-by-one)\",");
        // the older export repeated this on every unit; it belongs once, and it
        // is a guess either way — said so rather than dressed up
        sb.Append("\"_stats_labels\":\"tentative: A~cost B~weight/buildtime C~speed ");
        sb.Append("D,E,F~armor/terrain (unconfirmed)\",");
        sb.Append("\"units\":{");
        bool first = true;
        foreach (var kv in new SortedDictionary<int, int>(tally.UnitCount))
        {
            int ut = kv.Key;
            var s = _exe.StatsFor(ut);
            if (s == null) continue;
            var prev = _exe.StatsFor(ut - 1);
            int speed = prev != null && prev.Raw.Length > 0x30 ? prev.Raw[0x30] : 0;
            var r = s.Raw;
            if (!first) sb.Append(',');
            first = false;
            sb.Append($"\"{ut}\":{{\"unit_type\":{ut},\"hp_max\":{s.HpMax},");
            sb.Append($"\"tier\":\"{Tier(s.HpMax)}\",\"categories\":[");
            bool fc = true;
            foreach (int c in tally.UnitCategories.GetValueOrDefault(ut) ?? new SortedSet<int>())
            {
                if (!fc) sb.Append(',');
                fc = false;
                sb.Append(c);
            }
            sb.Append($"],\"count\":{kv.Value},\"name\":\"{Esc(s.Name)}\",");
            sb.Append($"\"succ_name\":\"{Esc(s.SuccName)}\",\"component_id\":{s.ComponentId},");
            sb.Append($"\"table_category\":\"{TableCategory(s.ComponentId)}\",");
            sb.Append($"\"stat_A_off07\":{r[0x07]},\"class1_off09\":{r[0x09]},");
            sb.Append($"\"class2_off0a\":{r[0x0a]},\"stat_B_off2e\":{r[0x2e]},");
            sb.Append($"\"stat_C_off30\":{r[0x30]},\"stat_D_off31\":{r[0x31]},");
            sb.Append($"\"stat_E_off32\":{r[0x32]},\"stat_F_off33\":{r[0x33]},");
            sb.Append($"\"speed_raw\":{speed}}}");
            Units++;
        }
        sb.Append("}}");
        File.WriteAllText(_dst + "/unit_catalog.json", sb.ToString(), new UTF8Encoding(false));
        say?.Invoke($"Einheitenkatalog: {Units} Typen");
    }

    /// <summary>The tier is ours, read off the hit points the data gives.</summary>
    private static string Tier(int hp) => hp switch
    {
        0 => "scenery/non-combat",
        <= 100 => "light",
        <= 350 => "medium",
        <= 450 => "heavy",
        <= 900 => "fortified",
        _ => "command/super",
    };

    /// <summary>Which part of the sprite bank the chassis lives in: the ship
    /// hulls are parts 70..76 and 100/101, everything else with a component is
    /// a propulsion, and a unit type without one carries no chassis at all.
    /// </summary>
    private static string TableCategory(int comp) => comp switch
    {
        0 => "body_class",
        >= 70 and <= 76 => "body_chassis",
        100 or 101 => "body_chassis",
        _ => "propulsion",
    };

    // ---- infantry -----------------------------------------------------------

    /// <summary>The foot soldiers are the designs riding on propulsion 148 and
    /// 149. Their sprite set follows from the weapon: `spodek = (weapon*2 -
    /// 124) &amp; 0xFF`, proven from the spawn code, with the two exceptions
    /// the same code makes.</summary>
    private void WriteInfantry(Action<string>? say)
    {
        var sb = new StringBuilder();
        sb.Append("{\"_note\":\"the infantry designs — sec47 entries whose propulsion is 148/149\",");
        sb.Append("\"_rule\":\"spodek = (weapon*2 - 124) & 0xFF, except 185 -> 22 and 186 -> 20\",");
        sb.Append("\"_confidence\":\"proven from the spawn code\",");
        sb.Append("\"_blocks\":\"0..7 walk, 9..10 fire, 11 standing, 12..14 dying\",");
        sb.Append("\"_open\":\"infantry hit points are nowhere in the data\",\"designs\":[");
        bool first = true;
        int ordinal = 0;
        // the design list repeats every entry per player block, so the twelve
        // distinct foot soldiers are found by NAME, not by counting records
        var byName = new HashSet<string>();
        foreach (var kv in new SortedDictionary<int, (string Name, int Weapon, int Propulsion, int Body)>(_designs))
        {
            var d = kv.Value;
            if (d.Propulsion != 148 && d.Propulsion != 149) continue;
            if (!byName.Add(d.Name)) continue;
            int set = d.Weapon switch
            {
                185 => 22,
                186 => 20,
                _ => (d.Weapon * 2 - 124) & 0xFF,
            };
            var w = _exe?.StatsFor(d.Weapon);
            if (!first) sb.Append(',');
            first = false;
            sb.Append($"{{\"ordinal\":{ordinal++},\"name\":\"{Esc(d.Name)}\",");
            sb.Append($"\"propulsion\":{d.Propulsion},\"weapon_row\":{d.Weapon},");
            sb.Append($"\"weapon_name\":\"{Esc(w?.Name ?? "")}\",");
            sb.Append($"\"armed\":{(w != null && w.Raw.Length > 6 && w.Raw[4] > 0 ? "true" : "false")},");
            sb.Append($"\"damage\":{(w != null && w.Raw.Length > 4 ? w.Raw[4] : 0)},");
            sb.Append($"\"range_raw\":{(w != null && w.Raw.Length > 6 ? w.Raw[6] : 0)},");
            sb.Append($"\"sets\":[{set},{set + 1}]}}");
            InfantryDesigns++;
        }
        sb.Append("]}");
        File.WriteAllText(_dst + "/infantry.json", sb.ToString(), new UTF8Encoding(false));
        say?.Invoke($"Infanterie-Entwuerfe: {InfantryDesigns}");
    }

    // ---- helpers ------------------------------------------------------------

    private static string Hex(byte[] b, int at, int len)
    {
        var sb = new StringBuilder(len * 2);
        for (int i = at; i < at + len && i < b.Length; i++) sb.Append(b[i].ToString("x2"));
        return sb.ToString();
    }

    private static string Esc(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
