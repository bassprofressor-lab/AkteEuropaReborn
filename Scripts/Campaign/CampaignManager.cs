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
    /// everything states 1..N have unlocked, minus what a state took away.
    ///
    /// <para>⚠ <b>Components are the fourth list, and the biggest one</b> (since
    /// 10.08.2026). `set_part(player, part, value)` @0x4D0520 has 1037 of the
    /// 1533 setter call sites in the mission blocks — four times as many as the
    /// design setter — and writes »this player OWNS this part« into sec46 +0x00.
    /// Unlike the other three it is per player, so the set is keyed by
    /// (player, part); <see cref="Unlocks.Components"/> is player 0, the human.
    /// </para>
    ///
    /// <para>⚠ <b>It is data, not a barrier</b>, and that was measured before it
    /// was written down: the ownership byte has thirteen readers in the
    /// original and every one of them is a menu — the construction screen's
    /// three pickers @0x46C490, the chassis list @0x455870,
    /// `research_offer_refresh` @0x4AA950 and the market module
    /// @0x4C0860..0x4C0E60, which uses it to pick WHICH design is offered for
    /// sale. `build_in_base`, `build_in_airport` and the production button do
    /// not look at it. Anything that turns this list into a refusal is stricter
    /// than the original — the same trap the release byte sec47 already
    /// set.</para></summary>
    public sealed class Unlocks
    {
        public readonly SortedSet<int> Ships = new();
        public readonly SortedSet<int> Aircraft = new();
        public readonly SortedSet<int> Vehicles = new();

        /// <summary>Every (player, part) the schedule has switched on.</summary>
        public readonly SortedSet<(int Player, int Part)> Parts = new();

        /// <summary>The human player's parts — the ones the construction screen
        /// of the original would offer.</summary>
        public SortedSet<int> Components
        {
            get
            {
                var s = new SortedSet<int>();
                foreach (var (p, x) in Parts) if (p == 0) s.Add(x);
                return s;
            }
        }

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
                ApplyParts(st, "components", u.Parts, true);
                ApplyParts(st, "components_off", u.Parts, false);
            }
        }
        _unlockCache[mission] = u;
        if (u.Known)
        {
            // What the schedule hands the HUMAN by this mission. Printed
            // because it is new and because the number is the whole point: the
            // components are four times the rest of the schedule put together,
            // and until 10.08.2026 the file carried none of them.
            var mine = u.Components;
            int others = u.Parts.Count - mine.Count;
            GD.Print($"campaign: Fahrplan M{mission} — {mine.Count} Bauteile beim Menschen" +
                     (others > 0 ? $", {others} bei den Computerspielern" : "") +
                     (mine.Count > 0 ? $" ({string.Join(",", mine)})" : ""));
        }
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

    /// <summary>A component entry is a pair <c>[Spieler, Bauteil]</c>. Written
    /// that way rather than as a bare number because the original writes this
    /// one table per player — the same mission hands the human parts 1, 4, 5, …
    /// and one of its computer opponents part 67.
    ///
    /// <para>⚠ A triple <c>[Spieler, Bauteil, Wert]</c> is accepted too, and
    /// entries with a <c>null</c> in them are skipped. That is not politeness:
    /// the file shipped in <c>res://Data/</c> — the fallback when nothing has
    /// been imported — was written by an older Python tool in exactly that
    /// shape, and it leaves a null wherever it could not resolve a register.
    /// Read as pairs those nulls turn into »player 0 owns part 0«.</para>
    /// </summary>
    private static void ApplyParts(Godot.Collections.Dictionary<string, Variant> st,
                                   string key, SortedSet<(int, int)> into, bool add)
    {
        if (!st.TryGetValue(key, out var v) || v.VariantType != Variant.Type.Array) return;
        foreach (var e in v.AsGodotArray())
        {
            if (e.VariantType != Variant.Type.Array) continue;
            var a = e.AsGodotArray();
            if (a.Count < 2) continue;
            if (a[0].VariantType == Variant.Type.Nil || a[1].VariantType == Variant.Type.Nil)
                continue;
            bool on = add;
            if (a.Count >= 3 && a[2].VariantType != Variant.Type.Nil)
                on = add && a[2].AsInt32() != 0;
            var pair = (a[0].AsInt32(), a[1].AsInt32());
            if (on) into.Add(pair); else into.Remove(pair);
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
