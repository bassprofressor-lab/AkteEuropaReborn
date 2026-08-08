namespace AkteEuropaReborn.Campaign;

using System.Collections.Generic;
using Godot;

/// <summary>
/// The campaign: which missions there are, which one comes next, and how far
/// the player has got.
///
/// The mission list is derived at import time (see
/// <c>Import.CatalogueExporter.WriteCampaign</c>) and the order is the level
/// file numbering — which is the game's own numbering, not a guess: the saved
/// game `1.DM` is called "Mission 26" and its elevation grid is level 26's.
///
/// Progress is one number in <c>user://campaign.cfg</c>: the highest mission
/// the player has finished. Nothing else is kept between missions, because
/// nothing else carries over — every level file brings its own starting
/// position.
/// </summary>
public static class CampaignManager
{
    public sealed class Mission
    {
        public int Index;
        public string Map = "", Title = "";
        public int Width, Height, Tileset;
        public string Label => $"{Index:00} — {(Title.Length > 0 ? Title : Map)}";
    }

    private static List<Mission>? _missions;

    /// <summary>The missions in order; empty when nothing has been imported.</summary>
    public static IReadOnlyList<Mission> Missions => _missions ??= Load();

    /// <summary>Forget the cached list — used after an import.</summary>
    public static void Forget() => _missions = null;

    public const string SavePath = "user://campaign.cfg";

    /// <summary>The highest mission the player has finished; 0 at the start,
    /// so the next one is the first.</summary>
    public static int Completed
    {
        get
        {
            var c = new ConfigFile();
            return c.Load(SavePath) == Error.Ok
                ? (int)c.GetValue("campaign", "completed", 0) : 0;
        }
        set
        {
            var c = new ConfigFile();
            c.Load(SavePath);
            c.SetValue("campaign", "completed", value);
            c.Save(SavePath);
        }
    }

    /// <summary>The mission to play next, or null once the campaign is over.
    /// A mission the imported content does not have is skipped instead of
    /// blocking the rest — someone with only disc 1 gets 1 to 15.</summary>
    public static Mission? Next()
    {
        int done = Completed;
        foreach (var m in Missions)
            if (m.Index > done) return m;
        return null;
    }

    public static Mission? ByIndex(int index)
    {
        foreach (var m in Missions)
            if (m.Index == index) return m;
        return null;
    }

    /// <summary>Record a mission as finished. Only ever moves forward, so
    /// replaying an early one does not throw the progress away.</summary>
    public static void Finished(int index)
    {
        if (index > Completed) Completed = index;
    }

    public static void Reset() => Completed = 0;

    // ---- the unlock schedule ------------------------------------------------

    /// <summary>What a mission may build.
    ///
    /// The schedule comes out of the campaign state machine @0x4884a6 and is
    /// carried as derived metadata in <c>res://Data/campaign_schedule.json</c> —
    /// it is our reading of the binary, not content from it, which is why it
    /// ships with the engine instead of being imported.
    ///
    /// What was missing until now was which state belongs to which mission.
    /// The map loader settles it: it indexes the mission-name table with the
    /// campaign counter itself (@0x41e25e, `21*counter + 0x4f81c0`), and the
    /// entries read "Mission 1" … "Mission 33". State N is mission N.
    ///
    /// A state's lists are what it unlocks, so the set for a mission is
    /// everything states 1..N have unlocked, minus what a state took away.</summary>
    public sealed class Unlocks
    {
        public readonly SortedSet<int> Ships = new();
        public readonly SortedSet<int> Aircraft = new();
        public readonly SortedSet<int> Vehicles = new();
        public bool Known;
    }

    public const string SchedulePath = "res://Data/campaign_schedule.json";

    private static Godot.Collections.Array? _states;
    private static readonly Dictionary<int, Unlocks> _unlockCache = new();

    public static Unlocks UnlocksFor(int mission)
    {
        if (_unlockCache.TryGetValue(mission, out var hit)) return hit;
        var u = new Unlocks();
        _states ??= LoadStates();
        if (_states != null)
        {
            u.Known = true;
            foreach (var item in _states)
            {
                if (item.VariantType != Variant.Type.Dictionary) continue;
                var st = item.AsGodotDictionary<string, Variant>();
                int id = st.TryGetValue("state", out var sv) ? sv.AsInt32() : -1;
                if (id < 0 || id > mission) continue;
                Apply(st, "ships", u.Ships, true);
                Apply(st, "ships_off", u.Ships, false);
                Apply(st, "aircraft", u.Aircraft, true);
                Apply(st, "vehicles", u.Vehicles, true);
            }
        }
        _unlockCache[mission] = u;
        return u;
    }

    /// <summary>A schedule entry is either a bare number or a small array whose
    /// first element is the number.</summary>
    private static void Apply(Godot.Collections.Dictionary<string, Variant> st,
                              string key, SortedSet<int> into, bool add)
    {
        if (!st.TryGetValue(key, out var v) || v.VariantType != Variant.Type.Array) return;
        foreach (var e in v.AsGodotArray())
        {
            int n = e.VariantType == Variant.Type.Array
                ? (e.AsGodotArray().Count > 0 ? e.AsGodotArray()[0].AsInt32() : -1)
                : e.AsInt32();
            if (n < 0) continue;
            if (add) into.Add(n); else into.Remove(n);
        }
    }

    private static Godot.Collections.Array? LoadStates()
    {
        // ⚠ The imported schedule wins over the one shipped in Data/. That file
        // was written by an earlier tool and is incomplete: it hands design 52
        // to the player from mission 8 where the game gives it from mission 6,
        // and design 51 from 15 where the game gives it from 12. The end state
        // after mission 33 matches, which is why nobody noticed — the missions
        // in between were simply poorer than the original's.
        string path = Core.Content.Path("Maps/campaign_schedule.json");
        if (!FileAccess.FileExists(path)) path = SchedulePath;
        if (!FileAccess.FileExists(path)) return null;
        using var f = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        if (f == null) return null;
        var json = new Json();
        if (json.Parse(f.GetAsText()) != Error.Ok ||
            json.Data.VariantType != Variant.Type.Dictionary) return null;
        var root = json.Data.AsGodotDictionary<string, Variant>();
        if (!root.TryGetValue("states", out var sv) || sv.VariantType != Variant.Type.Array)
            return null;
        var arr = sv.AsGodotArray();
        GD.Print($"campaign: Fahrplan mit {arr.Count} Zustaenden geladen");
        return arr;
    }

    private static List<Mission> Load()
    {
        var list = new List<Mission>();
        string path = Core.Content.Path("Maps/campaign.json");
        if (!FileAccess.FileExists(path)) return list;
        using var f = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        if (f == null) return list;
        var json = new Json();
        if (json.Parse(f.GetAsText()) != Error.Ok ||
            json.Data.VariantType != Variant.Type.Dictionary) return list;
        var root = json.Data.AsGodotDictionary<string, Variant>();
        if (!root.TryGetValue("missions", out var mv) || mv.VariantType != Variant.Type.Array)
            return list;
        foreach (var item in mv.AsGodotArray())
        {
            if (item.VariantType != Variant.Type.Dictionary) continue;
            var d = item.AsGodotDictionary<string, Variant>();
            list.Add(new Mission
            {
                Index = Get(d, "index"),
                Map = d.TryGetValue("map", out var mp) ? mp.AsString() : "",
                Title = d.TryGetValue("title", out var t) ? t.AsString() : "",
                Width = Get(d, "width"),
                Height = Get(d, "height"),
                Tileset = Get(d, "tileset"),
            });
        }
        list.Sort((a, b) => a.Index.CompareTo(b.Index));
        GD.Print($"campaign: {list.Count} Missionen aus {path}");
        return list;
    }

    private static int Get(Godot.Collections.Dictionary<string, Variant> d, string k)
        => d.TryGetValue(k, out var v) && v.VariantType != Variant.Type.Nil ? v.AsInt32() : 0;
}
