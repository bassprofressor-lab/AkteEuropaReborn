using System.Collections.Generic;
using System.Linq;
using Godot;

namespace AkteEuropaReborn.Rendering;

/// <summary>
/// The skirmish opponent — OURS, from the ground up.
///
/// Nothing in this file is reconstructed from GAME.EXE. The original's AI was
/// never reversed (its debug strings "AI end", "AI: production base:",
/// "More mer_ships needed" are all that is known of it), so this is a plain,
/// readable opponent written for the remake: it keeps its factories busy, buys
/// what its buildings can buy, gathers an army and throws it at the nearest
/// enemy. Everything it commands goes through the same simulation rules the
/// human player is bound by — it does not cheat on resources, sight or speed.
///
/// The three difficulties differ only in how fast it thinks, how big a group
/// it gathers before attacking, and whether it keeps a home guard.
/// </summary>
public partial class MapEntityLayer : Node2D
{
    public enum AiLevel { Easy, Normal, Hard }

    private sealed class AiPlayer
    {
        public int Player;
        public AiLevel Level;
        public float Think;             // seconds until the next decision
        public float AttackTimer;       // seconds until the next wave may leave
        public readonly List<int> Wave = new();
        public int TargetIdx = -1;      // what the current wave is going for
        public int Built, Waves;        // for the status line
        public int Picks;               // build decisions, drives the mix
    }

    private readonly List<AiPlayer> _ai = new();
    private bool _aiOn;

    /// <summary>Seconds between decisions, army size a wave needs, and how many
    /// units stay home. All three are ours — tuned, not recovered.</summary>
    private static (float think, int wave, int guard) AiTuning(AiLevel l) => l switch
    {
        AiLevel.Easy => (3.0f, 8, 4),
        AiLevel.Hard => (0.8f, 4, 1),
        _ => (1.5f, 6, 2),
    };

    /// <summary>Hand the listed players over to the computer.</summary>
    public void EnableSkirmishAi(IEnumerable<int> players, AiLevel level = AiLevel.Normal)
    {
        _ai.Clear();
        foreach (int p in players)
        {
            if (p is < 0 or > 7) continue;
            _ai.Add(new AiPlayer { Player = p, Level = level, Think = 0.5f + p * 0.13f });
        }
        _aiOn = _ai.Count > 0;
        GD.Print($"KI aktiv fuer Spieler {string.Join(", ", _ai.Select(a => a.Player))} " +
                 $"({level})");
    }

    public bool SkirmishAiActive => _aiOn;

    /// <summary>Slots that own at least one building — the slots a build-up
    /// skirmish can actually be played from.</summary>
    public List<int> PlayersWithBase()
    {
        var l = new List<int>();
        foreach (var e in _entities)
        {
            if (!e.IsBuilding || e.Dead || e.IsProp) continue;
            if (e.Owner is < 0 or > 7) continue;
            if (!l.Contains(e.Owner)) l.Add(e.Owner);
        }
        l.Sort();
        return l;
    }

    /// <summary>Buildings per start slot — the thing that decides a build-up
    /// skirmish once the starting armies are gone.
    ///
    /// The player asked whether the NET maps are even. Measured across all
    /// eight: <b>NET04 and NET05 give all eight slots a base, NET02 six,
    /// NET01/03/08 four, NET07 three and NET06 exactly one</b>. Evenness is a
    /// second question, which is what this count answers.</summary>
    public int[] BasesPerSlot()
    {
        var n = new int[8];
        foreach (var e in _entities)
        {
            if (!e.IsBuilding || e.Dead || e.IsProp) continue;
            if (e.Owner is < 0 or > 7) continue;
            n[e.Owner]++;
        }
        return n;
    }

    /// <summary>How lopsided the built slots are: the largest count divided by
    /// the smallest, or 0 when fewer than two slots are built. 1.0 is even.
    /// The threshold for calling a map uneven is OURS — twice the buildings is
    /// where a start stops being a start.</summary>
    public float BaseSpread()
    {
        var n = BasesPerSlot();
        int lo = int.MaxValue, hi = 0, built = 0;
        foreach (int v in n)
            if (v > 0) { built++; lo = Mathf.Min(lo, v); hi = Mathf.Max(hi, v); }
        return built < 2 ? 0f : (float)hi / lo;
    }

    /// <summary>Player slots that own something on this map.</summary>
    public List<int> LivePlayers()
    {
        var l = new List<int>();
        for (int p = 0; p < 8; p++) if (AliveAsPlayer(p)) l.Add(p);
        return l;
    }

    /// <summary>Set up a skirmish: the human keeps one slot, the computer takes
    /// up to `aiCount` of the others and the rest are left as neutral bystanders
    /// so the map still looks like itself.</summary>
    public int StartSkirmish(int human, int aiCount, AiLevel level)
    {
        // Since the starting armies go (see ClearStartingArmies), a slot is only
        // playable if it owns a BUILDING — on map_NET07 that is three of the
        // eight, the rest bringing nothing but troops. Taking the wider list
        // would seat a player on an empty slot and lose him the same second.
        var built = PlayersWithBase();
        bool buildUp = built.Count > 0;
        var live = buildUp ? built : LivePlayers();
        if (live.Count == 0) return -1;
        if (!live.Contains(human)) human = live[0];
        var foes = new List<int>();
        foreach (int p in live)
            if (p != human && foes.Count < aiCount) foes.Add(p);
        if (foes.Count < aiCount)
            GD.Print($"Gemetzel: die Karte hat nur {live.Count} " +
                     $"{(buildUp ? "bebaute" : "besetzte")} Startplaetze, " +
                     $"also {foes.Count} statt {aiCount} Gegner");
        ViewPlayer = human;

        // ⚠ Measured across the eight NET maps: only THREE of them give any slot
        // a building — NET01 (four slots, one each), NET06 (one slot with four)
        // and NET07 (three slots with 1, 6 and 9). On NET02, NET03, NET04, NET05
        // and NET08 nobody owns a structure at all. Taking their armies away
        // there leaves the player with literally nothing, which is why a
        // skirmish on NET02 used to end in MISSION GESCHEITERT the same second
        // it started. So the armies only go where there is a base to build from;
        // the other maps stay as they were drawn, and the line below says which
        // of the two happened.
        int cleared = buildUp ? ClearStartingArmies() : 0;
        EnableSkirmishAi(foes, level);
        var per = BasesPerSlot();
        float spread = BaseSpread();
        GD.Print($"Gemetzel: Spieler {human} (Startplatz {human + 1}) gegen {foes.Count} KI ({level}); " +
                 (buildUp
                    ? $"bebaute Plaetze {string.Join(",", live)}; " +
                      $"Gebaeude je Platz {string.Join("/", System.Array.ConvertAll(live.ToArray(), p => per[p]))}; " +
                      $"Verhaeltnis {spread:0.0}:1{(spread >= 2f ? " — UNAUSGEGLICHEN" : "")}; " +
                      $"{cleared} mitgebrachte Einheiten entfernt"
                    : $"besetzte Plaetze {string.Join(",", live)}; " +
                      "KEIN Platz hat ein Gebaeude — die Karte wird gespielt, wie sie gezeichnet ist, " +
                      "mit ihren Truppen"));
        return human;
    }

    /// <summary>Takes the armies the map brings with it off the board.
    ///
    /// The NET maps were drawn for the original's own multiplayer and come
    /// stocked: hundreds of finished units on both sides, which meet within
    /// seconds. What a skirmish wants instead is the usual build-up — a base,
    /// an economy, and an army one has to earn. So every mobile unit is removed
    /// at the start and only the structures stay.
    ///
    /// OURS, and knowingly so: this is not what the original did with these
    /// maps. Buildings, deposits, rail lines and the map itself are untouched,
    /// so what is removed is exactly the head start.</summary>
    private int ClearStartingArmies()
    {
        int n = 0;
        for (int i = 0; i < _entities.Count; i++)
        {
            var e = _entities[i];
            if (e.IsProp || e.IsBuilding || e.Dead) continue;
            e.Dead = true;
            e.Hp = 0;
            e.DeadTime = 999f;              // long dead: no wreck or smoke at t=0
            e.Path = null;
            e.Target = -1;
            e.Orders.Clear();
            _nav?.ClearOccupant(e.Col, e.Row, i);
            n++;
        }
        _sel.Clear();
        return n;
    }

    public string AiLine()
    {
        if (!_aiOn) return "";
        return "KI " + string.Join(" ", _ai.Select(a =>
            $"P{a.Player}:{ArmyOf(a.Player).Count}E/{a.Wave.Count}W/{a.Built}b/{a.Waves}a"));
    }

    // ---- the loop ---------------------------------------------------------

    private void UpdateAi(float dt)
    {
        if (!_aiOn) return;
        foreach (var a in _ai)
        {
            a.Think -= dt;
            a.AttackTimer -= dt;
            if (a.Think > 0f) continue;
            var (think, waveSize, guard) = AiTuning(a.Level);
            a.Think += think;

            if (!AliveAsPlayer(a.Player)) continue;
            AiProduce(a);
            AiFight(a, waveSize, guard);
        }
    }

    private bool AliveAsPlayer(int p)
    {
        foreach (var e in _entities)
            if (!e.IsProp && !e.Dead && e.Owner == p) return true;
        return false;
    }

    /// <summary>Where a player's things are, in map pixels — his Basis if he
    /// has one, otherwise the middle of everything he owns. Used to put the
    /// camera on the player's own base at the start of a skirmish.</summary>
    public Vector2? PlayerHome(int p)
    {
        foreach (var e in _entities)
            if (e.IsBuilding && !e.Dead && e.Owner == p && e.BType == 1)
                return CellCenter(e.Col, e.Row);
        var sum = Vector2.Zero; int n = 0;
        foreach (var e in _entities)
        {
            if (e.IsProp || e.Dead || e.Owner != p) continue;
            sum += CellCenter(e.Col, e.Row); n++;
        }
        return n > 0 ? sum / n : null;
    }

    /// <summary>Everything of this player that can move and shoot.</summary>
    private List<int> ArmyOf(int p)
    {
        var l = new List<int>();
        for (int i = 0; i < _entities.Count; i++)
        {
            var e = _entities[i];
            if (e.IsBuilding || e.IsProp || e.Dead) continue;
            if (e.Owner != p || !e.Mobile) continue;
            if (!CanFight(e)) continue;
            l.Add(i);
        }
        return l;
    }

    // ---- production -------------------------------------------------------

    private void AiProduce(AiPlayer a)
    {
        for (int i = 0; i < _entities.Count; i++)
        {
            var e = _entities[i];
            if (!e.IsBuilding || e.Dead || e.Owner != a.Player) continue;

            // a factory that is idle and has parts starts the next unit
            if (IsFactory(e) && e.BuildTime <= 0f && _designs != null && _designs.Count > 0)
            {
                var menu = BuildableBy(e.BType);
                if (menu.Count > 0)
                {
                    e.MenuIndex = AiPickDesign(a, menu);
                    int pick = menu[e.MenuIndex % menu.Count];
                    var chosen = _designs[pick];
                    // the same three-store test the player's factory passes
                    if (CanAfford(e, chosen))
                    {
                        PayFor(e, chosen);
                        e.BuildIndex = pick;
                        e.BuildTime = BuildSeconds;
                        a.Built++;
                    }
                }
                continue;
            }

            // a dock builds ships out of its Schiffswerft
            if (IsDock(e) && e.BuildTime <= 0f && ShipMenu(e).Count > 0)
            {
                e.MenuIndex = (e.MenuIndex + 1) % ShipMenu(e).Count;
                if (BuildShip(e)) a.Built++;
                continue;
            }

            // an airfield buys a supply helicopter when it can afford one
            if (e.BType == 9 && AirMenu(e).Count > 0 &&
                _money[Mathf.Clamp(e.Owner, 0, 7)] >= HeliPrice &&
                (e.Hangar?.Count ?? 0) < Mathf.Max(1, e.HangarSize))
            {
                if (BuyAircraft(e)) a.Built++;
            }
        }
    }

    /// <summary>Pick what to build. OURS: prefer something that can actually
    /// shoot — a vehicle turret (weapon 1..19) or an infantry arm (185..199) —
    /// and take an unarmed design only when nothing else is on offer. Every
    /// fourth unit is deliberately a different one so the army is mixed rather
    /// than nine copies of the same tank.
    ///
    /// Note: there is no "build a defence" here because nothing in the remake
    /// can raise a building yet — neither the player nor the computer. When
    /// that exists, this is where it goes.</summary>
    private int AiPickDesign(AiPlayer a, List<int> menu)
    {
        if (_designs == null || menu.Count == 0) return 0;
        var armed = new List<int>();
        for (int k = 0; k < menu.Count; k++)
        {
            int w = _designs[menu[k]].Weapon;
            if (w is (>= 1 and <= 19) or (>= 185 and <= 199)) armed.Add(k);
        }
        var pool = armed.Count > 0 ? armed : null;
        a.Picks++;
        if (pool == null) return (a.Picks + a.Player) % menu.Count;
        // every fourth build steps one along the pool, the rest stay on a
        // proven pick — keeps the army mixed without dithering
        int idx = (a.Picks / 4 + a.Player) % pool.Count;
        return pool[idx];
    }

    // ---- fighting ---------------------------------------------------------

    private void AiFight(AiPlayer a, int waveSize, int guard)
    {
        var army = ArmyOf(a.Player);
        // drop the dead and anything that changed hands
        a.Wave.RemoveAll(i => i >= _entities.Count || _entities[i].Dead ||
                              _entities[i].Owner != a.Player);

        // a wave that has a live target keeps going
        if (a.Wave.Count > 0 && a.TargetIdx >= 0 && a.TargetIdx < _entities.Count &&
            !_entities[a.TargetIdx].Dead)
        {
            foreach (int i in a.Wave)
            {
                var e = _entities[i];
                if (e.Target < 0 && e.Path == null) AiSend(i, a.TargetIdx);
            }
            return;
        }

        a.Wave.Clear();
        a.TargetIdx = -1;
        if (army.Count <= guard) return;               // too few to leave home
        if (a.AttackTimer > 0f) return;

        // gather the free ones, keeping the guard at home
        var free = army.Where(i => !_entities[i].DugIn).ToList();
        if (free.Count < waveSize + guard) return;

        var center = AiCenter(a.Player);
        int target = AiPickTarget(a.Player, center);
        if (target < 0) return;

        // the units closest to the enemy go first
        free.Sort((x, y) => CellDistance(_entities[x], _entities[target])
                     .CompareTo(CellDistance(_entities[y], _entities[target])));
        int take = Mathf.Min(free.Count - guard, Mathf.Max(waveSize, free.Count / 2));
        for (int k = 0; k < take; k++)
        {
            a.Wave.Add(free[k]);
            AiSend(free[k], target);
        }
        a.TargetIdx = target;
        a.AttackTimer = 20f;                            // ours: pause between waves
        a.Waves++;
        NoteEvent(_entities[target], $"KI P{a.Player} greift an");
    }

    /// <summary>The average cell of everything this player owns.</summary>
    private Vector2 AiCenter(int p)
    {
        var sum = Vector2.Zero;
        int n = 0;
        foreach (var e in _entities)
        {
            if (e.IsProp || e.Dead || e.Owner != p) continue;
            sum += new Vector2(e.Col, e.Row);
            n++;
        }
        return n > 0 ? sum / n : Vector2.Zero;
    }

    /// <summary>Nearest hostile thing, buildings counting double so a wave goes
    /// for the base rather than chasing a scout across the map.</summary>
    private int AiPickTarget(int p, Vector2 from)
    {
        int best = -1;
        float bestScore = float.MaxValue;
        for (int i = 0; i < _entities.Count; i++)
        {
            var e = _entities[i];
            if (e.IsProp || e.Dead || e.Owner is < 0 or > 7 || e.Owner == p) continue;
            if (!AiHostile(p, e.Owner)) continue;
            float d = from.DistanceTo(new Vector2(e.Col, e.Row));
            float score = e.IsBuilding ? d * 0.5f : d;
            if (score < bestScore) { bestScore = score; best = i; }
        }
        return best;
    }

    /// <summary>Hostility by player number — the same alliance matrix the
    /// entity test uses, without needing an entity of one's own.</summary>
    private static bool AiHostile(int me, int other)
    {
        if (me is < 0 or > 7 || other is < 0 or > 7) return false;
        return _haveAllies ? !_allied[me, other] : other != me;
    }

    /// <summary>Send one unit at a target: attack if it can reach, else drive
    /// toward it. Uses the same paths and rules a clicked order would.</summary>
    private void AiSend(int idx, int target)
    {
        if (_nav == null || idx < 0 || idx >= _entities.Count) return;
        var e = _entities[idx];
        var t = _entities[target];
        if (e.Dead || !e.Mobile || e.DugIn) return;
        if (e.FuelMax > 0 && e.Fuel <= 0) return;       // dry, same as for the player

        if (CanFight(e) && CellDistance(e, t) <= WeaponOf(e.Weapon).RangeTiles)
        {
            e.Target = target;
            e.Ordered = true;
            e.Path = null;
            return;
        }

        var goal = _nav.NearestFree(new Vector2I(t.Col, t.Row), e.Domain, idx);
        if (goal == null) return;
        var path = _nav.FindPath(new Vector2I(e.Col, e.Row), goal.Value, e.Domain, idx);
        if (path == null || path.Count == 0) return;
        e.Path = path;
        e.PathIdx = 0;
        e.Goal = goal.Value;
        e.Reserved = null;
        e.WaitTime = 0;
        e.Target = target;
        e.Ordered = true;
    }

    /// <summary>Preview harness: let the computer play both sides for a while
    /// and report what it did.</summary>
    public Vector2? DebugDemoAi()
    {
        var players = new List<int>();
        for (int p = 0; p < 8; p++)
            if (AliveAsPlayer(p)) players.Add(p);
        if (players.Count < 2) { GD.Print("demo-ai: weniger als zwei Spieler auf der Karte"); return null; }
        EnableSkirmishAi(players, AiLevel.Hard);
        GD.Print($"demo-ai: {players.Count} Spieler, Armeen " +
                 string.Join(" ", players.Select(p => $"P{p}:{ArmyOf(p).Count}")));
        foreach (int p in players)
        {
            var army = ArmyOf(p);
            if (army.Count > 0) return _entities[army[0]].Pos;
        }
        return null;
    }
}
