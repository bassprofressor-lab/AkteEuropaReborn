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
        public string Kind = "";      // inc | set | text | end | find_unit | set_time | space_in
        //                               | money | sound | close_texts | order | add_target
        //                               | remove_unit | sell_unit | change_owner
        //                               | set_relation | stop_transport
        /// <summary>D gibt es nur fuer `order` — der Befehlsbus fuehrt dort
        /// vier Felder (Einheit, ukol, x, y).</summary>
        public int A, B, C, D, E;

        /// <summary>`space_in` only: the design numbers to drop, in order. They
        /// index sec47 as <c>typ + 200*player</c> — the same table the design
        /// screen and the factories use, which is why mission 14's single byte
        /// 191 resolves to a design the game itself names "Col.Hullman".
        /// </summary>
        public int[] Types = Array.Empty<int>();
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
        /// <summary>The state the mission STARTS in, out of the setup block
        /// (`aekernel-tools/mission_initvars.py`). Without it half of every
        /// chain over a block variable is missing: mission 7 wants
        /// `v[102] == 2` and its block raises v[102] exactly once — because the
        /// setup starts it at 1. v[101+k] is the k-th objective's state
        /// (1 = open, 10 = done), v[131+k] its text number.</summary>
        public readonly Dictionary<int, int> Init = new();
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
                if ((a.Kind == "inc" || a.Kind == "set" ||
                     a.Kind == "find_unit" || a.Kind == "set_time" ||
                     a.Kind == "take_var" || a.Kind == "set_units" ||
                     a.Kind == "set_store") && a.A == n) return true;
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

    private MissionScript(Script s)
    {
        _script = s;
        foreach (var kv in s.Init)
            if (kv.Key >= 0 && kv.Key < _var.Length) _var[kv.Key] = kv.Value;
    }

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
            if (body.TryGetValue("init", out var iv) &&
                iv.VariantType == Variant.Type.Dictionary)
                foreach (var e in iv.AsGodotDictionary<string, Variant>())
                    if (int.TryParse(e.Key, out int n)) s.Init[n] = e.Value.AsInt32();
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
                        var act = new Act
                        {
                            Kind = ad.TryGetValue("kind", out var k) ? k.AsString() : "",
                            A = ad.TryGetValue("a", out var x) ? x.AsInt32() : 0,
                            B = ad.TryGetValue("b", out var y) ? y.AsInt32() : 0,
                            C = ad.TryGetValue("c", out var z) ? z.AsInt32() : 0,
                            D = ad.TryGetValue("d", out var w) ? w.AsInt32() : 0,
                            E = ad.TryGetValue("e", out var e5) ? e5.AsInt32() : 0,
                        };
                        if (ad.TryGetValue("typen", out var ty) &&
                            ty.VariantType == Variant.Type.Array)
                        {
                            var arr = ty.AsGodotArray();
                            var list = new int[arr.Count];
                            for (int n = 0; n < arr.Count; n++) list[n] = arr[n].AsInt32();
                            act.Types = list;
                        }
                        rule.Then.Add(act);
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
    public Action<int, int, int, int>? ShowText;     // id, art, x, y (640x480)

    /// <summary>`find_unit(spieler, marke)` @0x4D0F20 — the index of that
    /// player's first unit carrying <b>marke</b> in record byte +0x43, or
    /// 0xFFFF. That byte is how the campaign points at ONE named unit: map_03
    /// carries exactly one with 193 and mission 3 asks for 193, map_06 one with
    /// 194 and mission 6 asks for 194, and so on through fifteen missions.
    /// Empty records (+0x09 == 0xFF) and busy ones (ukol +0x14 >= 100) are
    /// skipped.</summary>
    public Func<int, int, int>? FindUnit;            // player, mark -> index

    /// <summary>A byte of one unit's record, by the index `find_unit` returned.
    /// Only +0x00 (column) and +0x01 (row) are answered — those are the two the
    /// campaign asks about, and they are the ones the engine holds. Anything
    /// else comes back -1 rather than a guess.</summary>
    public Func<int, int, int>? UnitField;           // index, offset -> byte

    /// <summary>A word out of one building record (255 x 76 @0xc06914). Only
    /// the four stores are answered: +0x28 Waffen, +0x2a Fahrwerk, +0x2c
    /// Spezial parts and +0x2e raw Terranium (GAMESTATE_RE 3.82). Mission 5
    /// marks two of them at its start and wins when BOTH have grown — which is
    /// objective #005 word for word, "Wiederaufnahme der Produktion".</summary>
    public Func<int, int, int>? StoreField;          // building slot, offset -> value

    /// <summary>`space_in` puts one unit of design <b>typ</b> on the map for
    /// <b>player</b>, at or next to (col, row) — @0x4C1600 asks @0x4012AD for a
    /// free place beside the cell and gives up with "Incredible error ...no free
    /// place for new robot" when there is none. The design number is the sec47
    /// row <c>typ + 200*player</c>.</summary>
    public Action<int, int, int, int>? SpaceInSpawn;   // typ, col, row, player

    // ---- 11.08.2026: was der tutorialartige Ablauf zusätzlich braucht -------
    // Jeder Haken darf fehlen; dann ist die Bedingung falsch bzw. die Wirkung
    // bleibt aus, und `Line()` sagt es. Lieber eine sichtbare Lücke als eine
    // Regel, die etwas anderes tut als das Original.
    public Func<int>? Selection;                     // -> angewähltes Objekt
    public Func<int, int, int>? MarkCount;           // marke, spieler -> Anzahl
    public Func<int, int, bool>? UnitHasMark;        // einheit, marke
    public Func<int, int>? MoneyOf;                  // spieler -> Kontostand
    public Func<int, int, int>? TerrainAt;           // x, y -> Geländebyte
    public Action<int, int>? AddMoney;               // betrag, spieler
    public Action<int>? PlaySound;                   // 600 / 601
    public Action? CloseTexts;                       // close_message_windows()
    public Action<int, int, int, int>? OrderUnit;    // einheit, ukol, x, y
    public Action<int, int, int, int, int>? AddTarget;  // spieler, art, vorrang, wort, c
    public Action<int>? RemoveUnit;                  // einheit
    public Action<int>? SellUnit;                    // einheit
    public Action<int, int>? ChangeOwner;            // einheit, spieler
    public Action<int, int, int>? SetRelation;       // a, b, wert
    public Action<int>? StopTransport;               // einheit

    /// <summary>Minutes since the mission started — the original's clock
    /// (`game_time()` counts 60·(hour + 24·day) + minute).</summary>
    public int Minutes => (int)(_seconds / 60.0);

    // ---- reinforcements ----------------------------------------------------

    /// <summary>
    /// One flight of reinforcements on its way in.
    ///
    /// `space_in(player, x, y, &amp;types[], count)` @0x4C17C0 does NOT put units
    /// on the map. It takes one of <b>twenty</b> slots of a queue of 32-byte
    /// records at 0xB49E50 (@0x4C01D0, "More mer_ships needed" when full) and
    /// fills it with x = <b>-10</b> — off the map — y, the target x, the type
    /// bytes and the player. The queue is stepped once per game tick by
    /// @0x4C0260, and only when x has crawled all the way to the target does
    /// kind 3 hand each type to @0x4C1600.
    ///
    /// So a reinforcement ARRIVES LATE, and how late depends on how far into
    /// the map it is going. That is the whole reason mission 14 has a stage
    /// counter between "order Hullman" and "look for Hullman".
    /// </summary>
    private sealed class Incoming
    {
        public int X = -10, Y, Target, Rest, Player;
        public int[] Types = Array.Empty<int>();
    }

    private readonly List<Incoming> _incoming = new();

    /// <summary>The queue @0xB49E50 has twenty slots and the original refuses
    /// the twenty-first out loud.</summary>
    public const int SpaceInSlots = 20;

    /// <summary>How many flights are still on their way — for the harness.</summary>
    public int Incomings => _incoming.Count;

    /// <summary>
    /// One tick of the queue, in the original's own integer arithmetic
    /// (@0x4C04EF..0x4C0580):
    ///
    ///     d = target - x
    ///     d &gt; 10          ->  x++                       (full speed)
    ///     otherwise       ->  rest += max(1, 4*d) as a byte
    ///                         x += rest/40, rest %= 40   (braking)
    ///     x == target     ->  drop, slot freed
    ///
    /// ⚠ The TICK RATE is ours, not the original's — this runs per frame, the
    /// original per game tick, and that has never been measured (handoff
    /// 09.08.). It changes WHEN reinforcements land, not WHETHER.
    /// </summary>
    private void TickIncoming()
    {
        for (int i = _incoming.Count - 1; i >= 0; i--)
        {
            var r = _incoming[i];
            int d = r.Target - r.X;
            if (d > 10) { r.X++; continue; }
            int step = d * 4;
            if (step < 1) step = 1;
            r.Rest = (r.Rest + step) & 0xFF;
            if (r.Rest > 0x27) { r.X += r.Rest / 0x28; r.Rest %= 0x28; }
            if (r.X != r.Target) continue;
            Drop(r);
            _incoming.RemoveAt(i);
        }
    }

    private void Drop(Incoming r)
    {
        foreach (int t in r.Types) SpaceInSpawn?.Invoke(t, r.X, r.Y, r.Player);
        GD.Print($"Verstaerkung eingetroffen: {r.Types.Length} Einheiten " +
                 $"fuer Spieler {r.Player} auf ({r.X}, {r.Y})");
    }

    /// <summary>Land everything still in the air at once. For the harness only:
    /// the flight takes tens of ticks, and a check that asks "is Hullman there"
    /// after one tick would answer no for a reason that has nothing to do with
    /// the condition it is testing.</summary>
    public int FlushIncoming()
    {
        int n = _incoming.Count;
        foreach (var r in _incoming) { r.X = r.Target; Drop(r); }
        _incoming.Clear();
        return n;
    }

    public void Tick(double dt)
    {
        if (_ended) return;
        _seconds += dt;
        TickIncoming();

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
        // find_unit(a, b) <op> c   — c = 65535 heisst "es gibt sie nicht"
        "unit_index" => FindUnit != null && Cmp(FindUnit(c.A, c.B), c.Op, c.C),
        // Feld +b der Einheit, deren Index in v[a] steht, <op> c.
        // ⚠ Ein unbekanntes Feld (-1) macht die Bedingung FALSCH, nicht wahr:
        // eine Kette, die nicht beantwortet werden kann, darf nicht gewinnen.
        // v[a] <op> Feld +c des Gebaeudesatzes b
        "var_vs_store" => StoreField != null && c.A >= 0 && c.A < _var.Length &&
                          StoreField(c.B, c.C) >= 0 &&
                          Cmp(_var[c.A], c.Op, StoreField(c.B, c.C)),
        "unit_field" => UnitField != null && c.A >= 0 && c.A < _var.Length &&
                        UnitField(_var[c.A], c.B) >= 0 &&
                        Cmp(UnitField(_var[c.A], c.B), c.Op, c.C),

        // ---- 11.08.2026: die Glieder des tutorialartigen Ablaufs -----------
        //
        // ⚠ Alle fünf antworten mit FALSCH, wenn ihr Haken nicht hängt. Eine
        // Bedingung, die nicht beantwortet werden kann, darf nicht gewinnen —
        // dieselbe Regel wie oben bei `unit_field`.

        // Was ist angewählt? 0x1F40 (8000) ist die Zahl der Einheitenplätze,
        // »kleiner« heisst also »eine Einheit ist angewählt«; ab 0x2710
        // (10000) ist es eine GRUPPE. Mission 1 hängt daran ihre Fenster
        // #002..#004 und #011.
        "selected" => Selection != null && Cmp(Selection(), c.Op, c.B),
        // Feld +c des Einheitensatzes a. Mission 1 fragt so nach der ZEILE
        // (+0x01) ihres Startpanzers (Satz 0): < 30 heisst »auf dem Weg zur
        // Brücke«, < 20 »am Hafen«.
        "unit_pos" => UnitField != null && UnitField(c.A, c.C) >= 0 &&
                      Cmp(UnitField(c.A, c.C), c.Op, c.B),
        // Die Art des letzten Ereignisses. Der Block, der es liest, verbraucht
        // es — genau das tut `Do` unten auch.
        "event" => Cmp(LastEvent, c.Op, c.B),
        // count_units_with_mark(a, b): wieviele Einheiten des Spielers b tragen
        // die Entwurfsnummer a? Mission 1 zählt so die EINGENOMMENEN neutralen.
        "units_mark" => MarkCount != null && Cmp(MarkCount(c.A, c.B), c.Op, c.C),
        // unit_has_mark(a, b) -> 1/0
        "unit_is" => UnitHasMark != null &&
                     Cmp(UnitHasMark(c.A, c.B) ? 1 : 0, c.Op, c.C),
        // get_money(a)
        "money_of" => MoneyOf != null && Cmp(MoneyOf(c.A), c.Op, c.B),
        // terrain_at(a, b) — Mission 1 prüft damit, ob der Panzer auf der
        // Brücke steht (> 4)
        "terrain" => TerrainAt != null && Cmp(TerrainAt(c.A, c.B), c.Op, c.C),
        _ => false,
    };

    /// <summary>Die Art des letzten Ereignisses (0x539930). Wird gesetzt, wo
    /// das Ereignis entsteht, und von der Regel, die es liest, wieder auf 0
    /// gestellt — das Original tut genau dasselbe (`mov byte [0x539930], 0`
    /// direkt hinter dem Fenster, das es auslöst).</summary>
    public int LastEvent;

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
                ShowText?.Invoke(a.A, a.B, a.C, a.D);
                // Der Block, der ein Ereignis in ein Fenster verwandelt, nullt
                // es danach (`mov byte [0x539930], 0` @0x49867D). Ohne das
                // feuerte dieselbe Regel in jeder Runde neu.
                LastEvent = 0;
                break;

            // ---- 11.08.2026 ------------------------------------------------
            // geld(b) += a. Der Betrag ist ein WORT und darf negativ sein.
            // Mission 1 zahlt so dreimal 50 $ für die versenkten Schiffe.
            case "money":
                AddMoney?.Invoke(a.A, a.B);
                break;
            case "sound":
                PlaySound?.Invoke(a.A);
                break;
            case "close_texts":
                CloseTexts?.Invoke();
                break;
            // bus_cmd(11, einheit, ukol, x, y) — der Befehl, mit dem eine
            // Mission ihre eigenen Einheiten losschickt. ukol 4 ist Angriff.
            // In Mission 1 sind das die Plätze 1000..1003, also die ersten vier
            // Einheiten von Spieler 1: drei Infanteristen und ein MG-Fahrzeug.
            case "order":
                OrderUnit?.Invoke(a.A, a.B, a.C, a.D);
                break;
            // add_target(spieler, art, vorrang, wort, c) @0x4CF700 — ⚠ das ist
            // KEIN Missionsziel im Panel, sondern die ZIELLISTE DES
            // COMPUTERSPIELERS. Alle Leser der Tabelle 0xBC5A78 liegen im
            // KI-Bereich (0x4BCF30, 0x4BDCC0, 0x4BECF0), keiner in der
            // Oberflaeche; der alte Name kam aus dem String »Cannot add new
            // target« und war geraten. Siehe CAMPAIGN_RE.md.
            case "add_target":
                AddTarget?.Invoke(a.A, a.B, a.C, a.D, a.E);
                break;
            case "remove_unit":
                RemoveUnit?.Invoke(a.A);
                break;
            case "sell_unit":
                SellUnit?.Invoke(a.A);
                break;
            case "change_owner":
                ChangeOwner?.Invoke(a.A, a.B);
                break;
            case "set_relation":
                SetRelation?.Invoke(a.A, a.B, a.C);
                break;
            case "stop_transport":
                StopTransport?.Invoke(a.A);
                break;
            // v[a] = find_unit(b, c) — die Mission merkt sich ihre Einheit
            case "find_unit":
                if (a.A >= 0 && a.A < _var.Length)
                    _var[a.A] = FindUnit != null ? FindUnit(a.B, a.C) : 0xFFFF;
                break;
            // v[a] = Feld c des Gebaeudesatzes b — die Marke, gegen die spaeter
            // auf Wachstum geprueft wird
            case "set_store":
                if (a.A >= 0 && a.A < _var.Length && StoreField != null)
                    _var[a.A] = StoreField(a.B, a.C);
                break;
            // space_in(a=spieler, b=x, c=y, typen) — die Verstaerkung startet
            // ausserhalb der Karte und braucht ihre Anflugzeit, wie im Original
            case "space_in":
                if (a.Types.Length == 0) break;
                if (_incoming.Count >= SpaceInSlots)
                {
                    GD.PrintErr("space_in: alle 20 Plaetze belegt " +
                                "(»More mer_ships needed«) — Verstaerkung faellt aus");
                    break;
                }
                _incoming.Add(new Incoming
                {
                    Y = a.C, Target = a.B, Player = a.A, Types = a.Types,
                });
                GD.Print($"Verstaerkung angefordert: {a.Types.Length} Einheiten " +
                         $"fuer Spieler {a.A} nach ({a.B}, {a.C})");
                break;
            // v[a] = g_robot_class_count(b, c) — die Mission MERKT sich einen
            // Bestand, statt ihn nur zu vergleichen. Mission 15 tut das im
            // ersten Takt mit `units(1, 7)`: den Bodenfahrzeugen des neutralen
            // Spielers, gegen die sie später zählt.
            case "set_units":
                if (a.A >= 0 && a.A < _var.Length)
                    _var[a.A] = UnitCount != null ? UnitCount(a.B, a.C) : 0;
                break;
            // v[a] += v[b]; v[b] = 0 — die VERBRAUCHSKORREKTUR.
            //
            // Mission 5 gewinnt, wenn zwei Teilelager über ihre Marke wachsen.
            // Damit eine GUTSCHRIFT dabei nicht als Produktion durchgeht, hebt
            // sie die Marke um genau den gutgeschriebenen Betrag: v[190..192]
            // halten die Waffen-, Fahrwerk- und Spezialteile, die @0x4B28E0
            // gerade einem Gebäude zurückgeschrieben hat — dieselbe Routine
            // erhöht dort +0x28/+0x2a/+0x2c um genau diese drei Zahlen. Auf
            // beiden GAME.EXE dieselben sechs Schreibstellen, Abstand 0xFA0.
            //
            // ⚠ Die Engine verrechnet noch keine Einheiten gegen Teile, also
            // bleiben v[190..192] hier auf 0 und die Regel feuert nie. Das ist
            // eine Lücke, keine Falle: ohne Gutschrift gibt es nichts zu
            // korrigieren.
            case "take_var":
                if (a.A >= 0 && a.A < _var.Length && a.B >= 0 && a.B < _var.Length)
                {
                    _var[a.A] += _var[a.B];
                    _var[a.B] = 0;
                }
                break;
            // v[a] = game_time() — der Zeitstempel, auf den `time_after` zeigt
            case "set_time":
                if (a.A >= 0 && a.A < _var.Length) _var[a.A] = Minutes;
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

    /// <summary>Every building record the script reads a STORE out of, as
    /// (slot, offset). Mission 5 is the whole reason this exists: it marks the
    /// two part stores of building 0 and wins when both have GROWN, so the only
    /// way to tell "the engine cannot do it" from "the engine did not do it
    /// yet" is to watch those two numbers while the mission runs.</summary>
    public List<(int Slot, int Off)> WatchedStores()
    {
        var list = new List<(int, int)>();
        void Add(int slot, int off)
        {
            if (!list.Contains((slot, off))) list.Add((slot, off));
        }
        foreach (var r in _script.Rules)
        {
            foreach (var c in r.When) if (c.Kind == "var_vs_store") Add(c.B, c.C);
            foreach (var a in r.Then) if (a.Kind == "set_store") Add(a.B, a.C);
        }
        return list;
    }

    /// <summary>What the script currently holds in the variables it filled from
    /// a store — the other half of the comparison <see cref="WatchedStores"/>
    /// shows.</summary>
    public List<(int Var, int Slot, int Off, int Value)> StoreMarks()
    {
        var list = new List<(int, int, int, int)>();
        foreach (var r in _script.Rules)
            foreach (var a in r.Then)
                if (a.Kind == "set_store" && a.A >= 0 && a.A < _var.Length)
                    list.Add((a.A, a.B, a.C, _var[a.A]));
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

    /// <summary>
    /// Everything the harness must arrange for an end rule to fire — the end
    /// conditions with their variable links RESOLVED.
    ///
    /// Since the setter rules are carried too, an end condition over a block
    /// variable is no longer a dead end: v[22] in mission 13 is raised by
    /// `objects(7, 0) != 0` ("build power generators"), so what the harness has
    /// to arrange is that, not the variable. Walking only `EndConds()` left the
    /// chain untested — the run reported "not forcible: var(22)!=0" and nothing
    /// ever fired.
    ///
    /// Variables whose writers are themselves conditioned on variables are
    /// followed too; each one only once, so a counter that raises itself
    /// (`v[1] < 40 -> inc v[1]`) cannot loop. Those resolve by time, not by the
    /// harness, and simply drop out.
    /// </summary>
    public List<Cond> ChainConds()
    {
        var list = new List<Cond>();
        var seen = new HashSet<int>();
        var queue = new Queue<Cond>();
        foreach (var c in EndConds()) queue.Enqueue(c);
        while (queue.Count > 0)
        {
            var c = queue.Dequeue();
            // `unit_field` fragt die Welt, hängt aber an der Variablen, in der
            // der Einheitenindex steht — also beides: die Bedingung sammeln UND
            // dem Erzeuger dieser Variablen nachgehen.
            if (c.Kind == "unit_field" || c.Kind == "var_vs_store")
                queue.Enqueue(new Cond { Kind = "var", A = c.A, Op = "!=", B = 0 });
            if (c.Kind != "var") { list.Add(c); continue; }
            if (!seen.Add(c.A)) continue;
            foreach (var r in _script.Rules)
            {
                bool writes = r.Once == c.A;
                if (!writes)
                    foreach (var a in r.Then)
                        if ((a.Kind == "inc" || a.Kind == "set" ||
                             a.Kind == "find_unit" || a.Kind == "set_time" ||
                             a.Kind == "set_store") && a.A == c.A)
                        { writes = true; break; }
                if (!writes) continue;
                foreach (var w in r.When) queue.Enqueue(w);
            }
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

    /// <summary>For the harness: every end rule's links with the value they
    /// have RIGHT NOW. Without this a chain that does not fire is a silence —
    /// mission 5 forced all five of its conditions and still did not end, and
    /// nothing said which link was false (it was the marking rule: the harness
    /// had raised the store BEFORE the mission noted it down, so "has grown"
    /// was false by construction).</summary>
    public string WhyNot()
    {
        var parts = new List<string>();
        foreach (var r in _script.Rules)
        {
            if (!Ends(r)) continue;
            foreach (var c in r.When)
                parts.Add($"{c.Kind}({c.A},{c.B}){c.Op}{c.C}=" + (Test(c) ? "ja" : "NEIN"));
        }
        return string.Join(" ", parts);
    }

    /// <summary>For the harness: the value of one of the block's variables.
    /// </summary>
    public int Var(int n) => n >= 0 && n < _var.Length ? _var[n] : -1;

    /// <summary>For the harness: what the script is doing right now.</summary>
    /// <summary>Die Missionsziele, wie der Block sie fuehrt.
    ///
    /// <para><c>v[101+k]</c> ist der Zustand des k-ten Ziels — <b>1 offen,
    /// 10 erfuellt</b> —, <c>v[131+k]</c> seine Textnummer aus HELPG.TXT. Das
    /// stand seit dem 09.08. im Vokabular und wurde nie angezeigt: Mission 1
    /// startet mit <c>v[101]=1, v[131]=110</c>, und #110 ist genau die
    /// Untermission mit den Schiffen. Wer sie erfuellte, bekam Geld und einen
    /// Klang, aber nirgends eine Bestaetigung — gemeldet als »die Nebenmission
    /// laesst sich nicht sauber abschliessen«.</para></summary>
    public List<(int Text, int State)> Objectives()
    {
        var list = new List<(int, int)>();
        for (int k = 0; k < 30; k++)
        {
            int st = 101 + k, tx = 131 + k;
            if (tx >= _var.Length || _var[st] == 0 || _var[tx] == 0) continue;
            list.Add((_var[tx], _var[st]));
        }
        return list;
    }

    public string Line() =>
        $"Skript M{_script.Mission} ({_script.Block}): {RulesFired} Regeln, " +
        $"{Minutes} min" + (_ended ? (Success ? ", GEWONNEN" : ", VERLOREN") : "");

    // ---- Was die Runtime von diesem Skript ueberhaupt ausfuehren kann -------
    //
    // ⚠ 11.08.2026, und der Grund dafuer ist eine alte Lehre in neuer Gestalt:
    // ein fehlender Haken macht eine Bedingung FALSCH und eine Wirkung STILL.
    // Beides sieht im Spiel aus wie »die Mission tut nichts« — nicht wie ein
    // Fehler. Nach 270 gelesenen Regeln ist die Frage »welche davon koennen
    // hier ueberhaupt feuern« darum wichtiger als jede einzelne Mission, und
    // sie muss die Engine selbst beantworten, nicht das Auge.

    /// <summary>Bedingungsarten, für die ein Haken hängt.</summary>
    private bool Hooked(Cond c) => c.Kind switch
    {
        "var" or "time_gt" or "time_after" or "event" => true,
        "obj_owner" => ObjOwner != null,
        "units" => UnitCount != null,
        "buildings" => BuildingCount != null,
        "objects" => ObjectCount != null,
        "unit_index" => FindUnit != null,
        "unit_field" or "unit_pos" => UnitField != null,
        "var_vs_store" => StoreField != null,
        "selected" => Selection != null,
        "units_mark" => MarkCount != null,
        "unit_is" => UnitHasMark != null,
        "money_of" => MoneyOf != null,
        "terrain" => TerrainAt != null,
        _ => false,
    };

    /// <summary>Wirkungsarten, für die ein Haken hängt.</summary>
    private bool Hooked(Act a) => a.Kind switch
    {
        "inc" or "set" or "set_time" or "take_var" or "end" => true,
        "text" => ShowText != null,
        "find_unit" => FindUnit != null,
        "set_store" => StoreField != null,
        "set_units" => UnitCount != null,
        "space_in" => SpaceInSpawn != null,
        "money" => AddMoney != null,
        "sound" => PlaySound != null,
        "close_texts" => CloseTexts != null,
        "order" => OrderUnit != null,
        "add_target" => AddTarget != null,
        "remove_unit" => RemoveUnit != null,
        "sell_unit" => SellUnit != null,
        "change_owner" => ChangeOwner != null,
        "set_relation" => SetRelation != null,
        "stop_transport" => StopTransport != null,
        _ => false,
    };

    /// <summary>Was dieses Skript nicht ausführen kann: je fehlender Art, wie
    /// oft sie vorkommt und in wievielen Regeln. Leer heisst: alles hängt.
    /// </summary>
    public string Coverage(out int rules, out int blocked)
    {
        var missing = new SortedDictionary<string, int>();
        rules = _script.Rules.Count;
        blocked = 0;
        foreach (var r in _script.Rules)
        {
            bool bad = false;
            foreach (var c in r.When)
                if (!Hooked(c)) { missing[c.Kind + "?"] = missing.GetValueOrDefault(c.Kind + "?") + 1; bad = true; }
            foreach (var a in r.Then)
                if (!Hooked(a)) { missing[a.Kind + "!"] = missing.GetValueOrDefault(a.Kind + "!") + 1; bad = true; }
            if (bad) blocked++;
        }
        var parts = new List<string>();
        foreach (var kv in missing) parts.Add($"{kv.Key}x{kv.Value}");
        return string.Join(" ", parts);
    }
}
