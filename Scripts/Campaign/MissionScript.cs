namespace AkteEuropaReborn.Campaign;

using System;
using System.Collections.Generic;
using Godot;

/// <summary>
/// The campaign's mission scripts.
///
/// In the original these are not data but CODE: the mission number out of the
/// map header indexes a jump table (@0x4A5B0C, 35 entries), and each mission
/// gets its own straight-line block of 542..2660 bytes that runs every tick.
/// Read with `aekernel-tools/mission_logic.py --decode`, every block turns out
/// to be the same shape — a state machine over its own word variables:
///
///     if v[n] == 0 and &lt;condition&gt;:   &lt;effects&gt;;  v[n]++
///
/// with a small vocabulary the game names itself: `game_time()` in minutes
/// (@0x4CF570), `obj_owner(n)` (@0x4D0780), `g_robot_class_count(class, player)`
/// (@0x4CF980) and `g_buildings_count(class, player)` (@0x4CFB10), a one-shot
/// `take_flag(n)` out of the map (@0x4D0700), text, sound, and `mission_end`.
///
/// ⚠ <b>This file is a RUNTIME, not a decompiler.</b> The rules it executes are
/// carried in Data/mission_scripts.json, and each one records the address of
/// the block it was translated from, so the translation can be checked against
/// `--decode`. Where a block uses something not in the vocabulary below, the
/// rule is left out rather than approximated — an absent rule shows up as a
/// mission that never ends, an invented one would quietly play differently.
/// </summary>
public sealed class MissionScript
{
    public const string Path = "res://Data/mission_scripts.json";

    /// <summary>What a rule asks. Everything here is measured; the class
    /// numbers are the game's own (unit class 0 is the sum of classes 1..4,
    /// building class 0 is every building).</summary>
    public sealed class Cond
    {
        public string Kind = "";      // var | time_gt | time_after | obj_owner | units | buildings
        public int A, B, C;           // meaning depends on Kind
        public string Op = "==";
    }

    public sealed class Act
    {
        public string Kind = "";      // inc | set | text | end
        public int A, B;
    }

    public sealed class Rule
    {
        public int Once = -1;         // the variable that latches this rule, -1 = every tick
        public readonly List<Cond> When = new();
        public readonly List<Act> Then = new();
    }

    public sealed class Script
    {
        public int Mission;
        public string Block = "";     // where it was translated from
        public readonly List<Rule> Rules = new();
    }

    // ---- state ------------------------------------------------------------

    private readonly Script _script;
    private readonly int[] _var = new int[512];      // v[n], the block's own words
    private double _seconds;
    private bool _ended;

    public bool Ended => _ended;

    /// <summary>
    /// Does this script decide the mission at all?
    ///
    /// ⚠ Most of the 33 are translated only in part, and a partial script must
    /// NOT take the decision away from the fallback: a mission whose script has
    /// no `end` rule would otherwise be unwinnable. So the script is
    /// authoritative only once it can actually finish.
    ///
    /// ⚠ And an `end` rule alone is not enough. Several of the original's end
    /// conditions are chains over the block's OWN variables — mission 7 wants
    /// `v[102] == 2 and v[12] == 2` on top of its two destroyed objects, and
    /// v[102] is a stage counter that only the untranslated part of the block
    /// ever raises. Such a rule is read correctly and can still never fire, so
    /// letting it decide would make the mission unwinnable — the exact opposite
    /// of the truncated-chain bug it fixes. So an end rule counts only when
    /// every variable it tests is one that some translated rule writes.
    /// </summary>
    public bool Decides
    {
        get
        {
            foreach (var r in _script.Rules)
            {
                if (!Ends(r)) continue;
                bool reachable = true;
                foreach (var c in r.When)
                    if (c.Kind == "var" && !Writes(c.A)) { reachable = false; break; }
                if (reachable) return true;
            }
            return false;
        }
    }

    private static bool Ends(Rule r)
    {
        foreach (var a in r.Then) if (a.Kind == "end") return true;
        return false;
    }

    /// <summary>Does any translated rule ever write v[n]? A rule's `once` latch
    /// counts — that is how the original raises most of them.</summary>
    private bool Writes(int n)
    {
        foreach (var r in _script.Rules)
        {
            if (r.Once == n) return true;
            foreach (var a in r.Then)
                if ((a.Kind == "inc" || a.Kind == "set") && a.A == n) return true;
        }
        return false;
    }

    /// <summary>Which end rules cannot fire because a variable they test is
    /// never written — for the harness, so the gap is visible instead of just
    /// showing up as a mission that runs forever.</summary>
    public List<int> UnreachableVars()
    {
        var list = new List<int>();
        foreach (var r in _script.Rules)
        {
            if (!Ends(r)) continue;
            foreach (var c in r.When)
                if (c.Kind == "var" && !Writes(c.A) && !list.Contains(c.A)) list.Add(c.A);
        }
        list.Sort();
        return list;
    }
    public bool Success { get; private set; }
    public int Mission => _script.Mission;
    public int RulesFired { get; private set; }

    private MissionScript(Script s) { _script = s; }

    /// <summary>The script of a mission, or null when none is carried for it.
    /// A missing script is not an error — most of the 33 are not translated
    /// yet, and a mission without one simply never ends by itself.</summary>
    public static MissionScript? For(int mission)
    {
        var all = Load();
        return all.TryGetValue(mission, out var s) ? new MissionScript(s) : null;
    }

    private static Dictionary<int, Script>? _cache;

    private static Dictionary<int, Script> Load()
    {
        if (_cache != null) return _cache;
        _cache = new Dictionary<int, Script>();
        if (!FileAccess.FileExists(Path)) return _cache;
        using var f = FileAccess.Open(Path, FileAccess.ModeFlags.Read);
        if (f == null) return _cache;
        var json = new Json();
        if (json.Parse(f.GetAsText()) != Error.Ok ||
            json.Data.VariantType != Variant.Type.Dictionary) return _cache;
        var root = json.Data.AsGodotDictionary<string, Variant>();
        if (!root.TryGetValue("missions", out var mv) ||
            mv.VariantType != Variant.Type.Dictionary) return _cache;

        foreach (var kv in mv.AsGodotDictionary<string, Variant>())
        {
            if (!int.TryParse(kv.Key, out int m)) continue;
            var body = kv.Value.AsGodotDictionary<string, Variant>();
            var s = new Script { Mission = m };
            if (body.TryGetValue("block", out var bv)) s.Block = bv.AsString();
            if (!body.TryGetValue("rules", out var rv) ||
                rv.VariantType != Variant.Type.Array) continue;
            foreach (var r in rv.AsGodotArray())
            {
                var rd = r.AsGodotDictionary<string, Variant>();
                var rule = new Rule();
                if (rd.TryGetValue("once", out var ov)) rule.Once = ov.AsInt32();
                if (rd.TryGetValue("when", out var wv) && wv.VariantType == Variant.Type.Array)
                    foreach (var c in wv.AsGodotArray())
                    {
                        var cd = c.AsGodotDictionary<string, Variant>();
                        rule.When.Add(new Cond
                        {
                            Kind = cd.TryGetValue("kind", out var k) ? k.AsString() : "",
                            A = cd.TryGetValue("a", out var a) ? a.AsInt32() : 0,
                            B = cd.TryGetValue("b", out var b) ? b.AsInt32() : 0,
                            C = cd.TryGetValue("c", out var c2) ? c2.AsInt32() : 0,
                            Op = cd.TryGetValue("op", out var o) ? o.AsString() : "==",
                        });
                    }
                if (rd.TryGetValue("then", out var tv) && tv.VariantType == Variant.Type.Array)
                    foreach (var a in tv.AsGodotArray())
                    {
                        var ad = a.AsGodotDictionary<string, Variant>();
                        rule.Then.Add(new Act
                        {
                            Kind = ad.TryGetValue("kind", out var k) ? k.AsString() : "",
                            A = ad.TryGetValue("a", out var x) ? x.AsInt32() : 0,
                            B = ad.TryGetValue("b", out var y) ? y.AsInt32() : 0,
                        });
                    }
                s.Rules.Add(rule);
            }
            _cache[m] = s;
        }
        GD.Print($"Missionsskripte: {_cache.Count} geladen");
        return _cache;
    }

    // ---- what the world has to answer -------------------------------------

    /// <summary>The four questions a rule can ask of the running game. Supplied
    /// by the layer that owns the entities, so this file stays free of them.
    /// </summary>
    public Func<int, int>? ObjOwner;                 // building slot -> owner
    public Func<int, int, int>? UnitCount;           // class, player -> count
    public Func<int, int, int>? BuildingCount;       // class, player -> count
    /// <summary>`count_objects(typ, besitzer)` @0x4CFA70 — laeuft ueber 255
    /// Saetze der Objekttabelle 0xc06910 und zaehlt die mit passendem Typ in
    /// `+4` und Besitzer in `+5`. Die haeufigste Endbedingung der Kampagne.
    /// </summary>
    public Func<int, int, int>? ObjectCount;         // type, owner -> count
    public Action<int>? ShowText;                    // helpg.txt id

    /// <summary>Minutes since the mission started — the original's clock
    /// (`game_time()` counts 60·(hour + 24·day) + minute).</summary>
    public int Minutes => (int)(_seconds / 60.0);

    public void Tick(double dt)
    {
        if (_ended) return;
        _seconds += dt;

        foreach (var r in _script.Rules)
        {
            if (r.Once >= 0 && r.Once < _var.Length && _var[r.Once] != 0) continue;
            bool all = true;
            foreach (var c in r.When)
                if (!Test(c)) { all = false; break; }
            if (!all) continue;

            foreach (var a in r.Then) Do(a);
            if (r.Once >= 0 && r.Once < _var.Length) _var[r.Once]++;
            RulesFired++;
            if (_ended) return;
        }
    }

    private bool Cmp(int lhs, string op, int rhs) => op switch
    {
        "==" => lhs == rhs,
        "!=" => lhs != rhs,
        ">" => lhs > rhs,
        ">=" => lhs >= rhs,
        "<" => lhs < rhs,
        "<=" => lhs <= rhs,
        _ => false,
    };

    private bool Test(Cond c) => c.Kind switch
    {
        // v[a] <op> b
        "var" => c.A >= 0 && c.A < _var.Length && Cmp(_var[c.A], c.Op, c.B),
        // game_time() <op> a
        "time_gt" => Cmp(Minutes, c.Op == "==" ? ">" : c.Op, c.A),
        // game_time() > v[a] + b
        "time_after" => c.A >= 0 && c.A < _var.Length && Minutes > _var[c.A] + c.B,
        // obj_owner(a) <op> b
        "obj_owner" => ObjOwner != null && Cmp(ObjOwner(c.A), c.Op, c.B),
        // g_robot_class_count(a, b) <op> c
        "units" => UnitCount != null && Cmp(UnitCount(c.A, c.B), c.Op, c.C),
        // g_buildings_count(a, b) <op> c
        "buildings" => BuildingCount != null && Cmp(BuildingCount(c.A, c.B), c.Op, c.C),
        // count_objects(a, b) <op> c
        "objects" => ObjectCount != null && Cmp(ObjectCount(c.A, c.B), c.Op, c.C),
        _ => false,
    };

    private void Do(Act a)
    {
        switch (a.Kind)
        {
            case "inc":
                if (a.A >= 0 && a.A < _var.Length) _var[a.A]++;
                break;
            case "set":
                if (a.A >= 0 && a.A < _var.Length) _var[a.A] = a.B;
                break;
            case "text":
                ShowText?.Invoke(a.A);
                break;
            case "end":
                _ended = true;
                Success = a.A != 0;
                GD.Print($"Missionsskript {_script.Mission}: " +
                         (Success ? "Mission erfolgreich beendet" : "Mission gescheitert") +
                         $" nach {Minutes} Minuten, {RulesFired + 1} Regeln");
                break;
        }
    }

    /// <summary>Every building slot the script watches, so the harness can show
    /// what those look like right now — the quickest way to see whether a
    /// translated condition is even asking about the right things.</summary>
    public List<int> WatchedSlots()
    {
        var list = new List<int>();
        foreach (var r in _script.Rules)
            foreach (var c in r.When)
                if (c.Kind == "obj_owner" && !list.Contains(c.A)) list.Add(c.A);
        list.Sort();
        return list;
    }

    /// <summary>Every condition the script's end rules ask about, so the
    /// harness can try to make them true and check the whole chain. The
    /// operator comes along: since the conditions are read out of the EXE with
    /// their real comparison, "gone" is no longer the only thing an end rule
    /// can want — mission 18 stops the advance at FEWER THAN THREE units left,
    /// and mission 3 wants a research complex that is still THERE.</summary>
    public List<Cond> EndConds()
    {
        var list = new List<Cond>();
        foreach (var r in _script.Rules)
        {
            if (!Ends(r)) continue;
            foreach (var c in r.When) list.Add(c);
        }
        return list;
    }

    /// <summary>How many matching things the harness has to leave standing to
    /// make this condition true, or -1 when it cannot be arranged at all.
    ///
    /// Destroying is not the only lever, and treating it as the only one is how
    /// three inverted conditions stayed hidden: mission 3 wants a research
    /// complex that is still THERE, mission 23 exactly five raw-material mines,
    /// mission 2 exactly two objects. So the harness aims at a TARGET COUNT and
    /// destroys or hands over until it is met.</summary>
    public static int TargetCount(Cond c) => c.Op switch
    {
        "==" => c.C,
        "!=" => c.C == 0 ? 1 : -1,          // "nicht null" -> eines genuegt
        "<" => Math.Max(0, c.C - 1),
        "<=" => Math.Max(0, c.C),
        ">" => c.C + 1,
        ">=" => c.C,
        _ => -1,
    };

    /// <summary>For the harness: what the script is doing right now.</summary>
    public string Line() =>
        $"Skript M{_script.Mission} ({_script.Block}): {RulesFired} Regeln, " +
        $"{Minutes} min" + (_ended ? (Success ? ", GEWONNEN" : ", VERLOREN") : "");
}
