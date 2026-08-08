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
        public int Grabber = -1;        // the unit sent to take a building
        public int GrabTarget = -1;     // the building it is going for
        public int Taken;               // buildings this side has taken

        /// <summary>The mission's own build programme, when there is one, and
        /// where in it this player stands. RECOVERED, unlike the rest of this
        /// file: see <see cref="AiLoadPlan"/>.</summary>
        public List<(int Kind, int What)>? Plan;
        public int Pc;
        public int FromPlan;                       // builds the programme drove
        public int PlanMissed;                     // lines no factory could serve
        public readonly List<string> PlanNames = new();
        public readonly HashSet<int> PlanUnmatched = new();
    }

    /// <summary>
    /// The build programme the original gives this player in this mission.
    ///
    /// The computer player of "Akte Europa" does not choose what to build. It
    /// runs a programme of up to 50 lines, one line every 50 ticks, and starts
    /// over when it runs off the end (`ai_production` @0x4BB9A0). The programme
    /// is not in the map — a campaign level stops at section 38 — but in the
    /// exe, as straight-line code picked by the mission number; the importer
    /// writes it out as <c>Maps/mission_plans.json</c>.
    ///
    /// So for a campaign mission the computer now builds what the original
    /// built, in the original's order. A skirmish has no mission number and
    /// keeps <see cref="AiPickDesign"/>, which is ours.
    /// </summary>
    private static Godot.Collections.Dictionary<string, Variant>? _missionPlans;
    private static int _missionPlansFor = -1;

    private static void AiLoadPlan(AiPlayer a)
    {
        int mission = UI.SkirmishSetup.CampaignMission;
        if (_missionPlansFor != mission)
        {
            _missionPlansFor = mission;
            _missionPlans = null;
            string path = Core.Content.Path("Maps/mission_plans.json");
            if (mission > 0 && FileAccess.FileExists(path))
            {
                using var f = FileAccess.Open(path, FileAccess.ModeFlags.Read);
                var json = new Json();
                if (f != null && json.Parse(f.GetAsText()) == Error.Ok &&
                    json.Data.VariantType == Variant.Type.Dictionary)
                {
                    var root = json.Data.AsGodotDictionary<string, Variant>();
                    if (root.TryGetValue("missions", out var mv) &&
                        mv.VariantType == Variant.Type.Dictionary)
                    {
                        var all = mv.AsGodotDictionary<string, Variant>();
                        if (all.TryGetValue(mission.ToString(), out var pv) &&
                            pv.VariantType == Variant.Type.Dictionary)
                            _missionPlans = pv.AsGodotDictionary<string, Variant>();
                    }
                }
            }
        }
        if (_missionPlans == null) return;
        if (!_missionPlans.TryGetValue(a.Player.ToString(), out var rv) ||
            rv.VariantType != Variant.Type.Array) return;

        var rows = rv.AsGodotArray();
        var plan = new List<(int, int)>();
        foreach (var r in rows)
        {
            var line = r.AsGodotArray();
            if (line.Count >= 2) plan.Add((line[0].AsInt32(), line[1].AsInt32()));
        }
        if (plan.Count > 0)
        {
            a.Plan = plan;
            GD.Print($"KI Spieler {a.Player}: Bauprogramm aus Mission " +
                     $"{UI.SkirmishSetup.CampaignMission}, {plan.Count} Schritte");
        }
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
            var a = new AiPlayer { Player = p, Level = level, Think = 0.5f + p * 0.13f };
            AiLoadPlan(a);
            _ai.Add(a);
        }
        _aiOn = _ai.Count > 0;
        GD.Print($"KI aktiv fuer Spieler {string.Join(", ", _ai.Select(a => a.Player))} " +
                 $"({level})");
    }

    public bool SkirmishAiActive => _aiOn;

    /// <summary>
    /// Slots that own a FACTORY — the Waffen-, Fahrwerk- or Spezial-Fabrik, the
    /// only buildings that make units. Owning a base is not the same thing, and
    /// the difference is the whole of the skirmish question.
    ///
    /// ⚠ <b>WITHDRAWN 2026-08-06, and it was wrong in the way that matters.</b>
    /// This note used to read: "not one of the eight NET maps gives any slot a
    /// factory — the NET maps are battle maps, two sides meet with what the map
    /// puts down and nobody builds anything." The measurement behind it asked
    /// the exported building records for the key <c>typ</c>, which does not
    /// exist — the key is <c>type</c> — so it counted zero everywhere and the
    /// zero was read as a finding.
    ///
    /// Counted properly the NET maps carry <b>12 to 42 factories and 4 to 8
    /// bases, all of them owner 11 = neutral</b> (NET01 18 factories, NET05 41,
    /// NET06 42, NET08 31). The one exception is NET07, which has no neutral
    /// structures at all — and NET07 is the skirmish menu's default map, which
    /// is how the false zero came to look plausible.
    ///
    /// So the question this function asks is still the right one — it asks
    /// which PLAYER SLOTS own a factory, and on a NET map the honest answer is
    /// none. What was wrong was the conclusion drawn from it. Those maps are
    /// <b>conquest maps</b>: the factories are there to be TAKEN, and the units
    /// the map hands you are the tool for taking them (Simulation/Capture.cs).
    /// See <see cref="NeutralPrizes"/> for the count that decides this now.
    /// </summary>
    public List<int> PlayersWithFactory()
    {
        var l = new List<int>();
        foreach (var e in _entities)
        {
            if (!e.IsBuilding || e.Dead || e.IsProp) continue;
            if (e.BType is not (2 or 3 or 4)) continue;
            if (e.Owner is < 0 or > 7) continue;
            if (!l.Contains(e.Owner)) l.Add(e.Owner);
        }
        l.Sort();
        return l;
    }

    /// <summary>
    /// What is standing on the map to be TAKEN: neutral (owner 11) factories
    /// and bases that carry a door. This is what makes a map a conquest map,
    /// and it is the count that replaces the withdrawn "no NET map has a
    /// factory" (see <see cref="PlayersWithFactory"/>).
    /// </summary>
    public (int Factories, int Bases, int All) NeutralPrizes()
    {
        int f = 0, b = 0, all = 0;
        foreach (var e in _entities)
        {
            if (!e.IsBuilding || e.IsProp || e.Dead) continue;
            if (e.Owner != NeutralOwner || e.Doors == 0) continue;
            all++;
            if (e.BType is 2 or 3 or 4) f++;
            else if (e.BType == 1) b++;
        }
        return (f, b, all);
    }

    /// <summary>The owner byte the map files use for civilian structures — not
    /// a player slot. 477 buildings across all files carry it.</summary>
    public const int NeutralOwner = 11;

    /// <summary>
    /// Sides that own something built — a structure from the building list OR an
    /// entity whose category (+0x26) is 6, the game's own building/HQ class.
    ///
    /// The second half is the point: on map_01 the buildings list gives nobody a
    /// structure, but player 1's slot 1001 is a category-6 record with 440 hit
    /// points sitting in the ENTITY table. Asking only the buildings list made
    /// every side on that map look like a bystander.
    /// </summary>
    public List<int> PlayersWithHeadquarters()
    {
        var l = new List<int>();
        foreach (var e in _entities)
        {
            if (e.Dead || e.IsProp) continue;
            if (!e.IsBuilding && e.Category != 6) continue;
            if (e.Owner is < 0 or > 7) continue;
            if (!l.Contains(e.Owner)) l.Add(e.Owner);
        }
        l.Sort();
        return l;
    }

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

    /// <summary>
    /// Start a CAMPAIGN mission: the map is played exactly as it is drawn — no
    /// army is taken off it, nothing is balanced — and every side that is not
    /// the player is handed to the computer.
    ///
    /// ⚠ OURS, and knowingly a stand-in. The original does not run a skirmish AI
    /// in its campaign; it runs a script per mission, and those scripts are in
    /// GAME.EXE at <b>0x498000..0x4a5600</b> — 138 int3-separated blocks over a
    /// vocabulary of about 22 primitives the game names itself: add production
    /// ("Cannot add new 'vyroba'" @0x4cf570), show a help text (@0x443490,
    /// @0x4432e0), g_robot_class_count (@0x4cf980), g_buildings_count
    /// (@0x4cfa70), reinforcements ("Too many units in 'Space in'" @0x4c17c0),
    /// add an objective ("Cannot add new target" @0x4cf700), build a bridge
    /// ("Can't add built bridge" @0x4d0f20), finish a sub-mission ("SUB:"
    /// @0x4cfc10) and make a unit disappear (@0x4d0b00). Until those are read,
    /// a campaign mission had NO opposition at all — the enemy stood still,
    /// which is what was reported. This gives it something to do; it does not
    /// give it the mission's script.
    ///
    /// Until the real thing exists, this is a scripted mission played by a
    /// skirmish opponent, and the line it prints says so.
    /// </summary>
    public int StartCampaign(int human, AiLevel level = AiLevel.Normal)
    {
        var live = LivePlayers();
        if (live.Count == 0) return -1;
        ViewPlayer = human;

        // Who fights whom is the mission's own business, and it is no longer a
        // guess: mission_init @0x487c40 sets the full 8x8 matrix on every start
        // and makes player 7 neutral. See MapEntityLayer.LoadCampaignDiplomacy.
        int mission = UI.SkirmishSetup.CampaignMission;
        bool read = LoadCampaignDiplomacy(mission);

        var foes = new List<int>();
        var standby = new List<int>();
        for (int p = 0; p < 8; p++) _standby[p] = false;

        if (read)
        {
            foreach (int p in live)
            {
                if (p == human) continue;
                if (IsNeutralPlayer(p) || Allied(human, p)) { standby.Add(p); _standby[p] = true; }
                else foes.Add(p);
            }
        }
        else
        {
            // ⚠ the WITHDRAWN rule, kept only for a map the diplomacy does not
            // cover (a .DM opened as a mission, or content imported before the
            // file existed): a side with a base is an opponent, a side with
            // nothing but field units stands by.
            var withBase = PlayersWithHeadquarters();
            foreach (int p in live)
            {
                if (p == human) continue;
                if (withBase.Contains(p)) foes.Add(p);
                else { standby.Add(p); _standby[p] = true; }
            }
        }
        EnableSkirmishAi(foes, level);

        var mates = new List<int>();
        if (read)
            for (int p = 0; p < 8; p++)
                if (p != human && !IsNeutralPlayer(p) && Allied(human, p)) mates.Add(p);

        GD.Print($"Kampagne: Spieler {human}; Gegner {(foes.Count == 0 ? "keine" : string.Join(",", foes))}" +
                 (mates.Count > 0 ? $"; verbuendet {string.Join(",", mates)}" : "") +
                 (standby.Count > 0 ? $"; unbeteiligt {string.Join(",", standby)}" : "") +
                 $" ({level}); Armeen bleiben stehen — " +
                 string.Join(" ", System.Array.ConvertAll(live.ToArray(),
                     p => $"P{p}:{ArmyOf(p).Count}E")) +
                 (read
                     ? $"; Diplomatie aus GAME.EXE, Mission {mission}, neutral " +
                       string.Join(",", NeutralPlayerList())
                     : "; OHNE campaign_diplomacy.json — es gilt die zurueckgezogene " +
                       "Basis-Regel"));
        return human;
    }

    /// <summary>The slots this mission put out of play, for the status line.</summary>
    public static List<int> NeutralPlayerList()
    {
        var l = new List<int>();
        for (int p = 0; p < 8; p++) if (IsNeutralPlayer(p)) l.Add(p);
        return l;
    }

    /// <summary>Set up a skirmish: the human keeps one slot, the computer takes
    /// up to `aiCount` of the others and the rest are left as neutral bystanders
    /// so the map still looks like itself.</summary>
    public int StartSkirmish(int human, int aiCount, AiLevel level)
    {
        // ⚠ CORRECTED TWICE, and the second correction is the one that counts.
        //
        // (0.4.0) It used to ask which slots own a BUILDING and, where any did,
        // sweep every mobile unit off the map so the player would build his army
        // instead. On NET07 that left the human with one building — not a
        // factory — and nothing else. Reported as "ich habe gar keine Einheiten
        // um zu bauen oder zu starten", and correctly.
        //
        // (2026-08-06) The replacement then said: a map where no SLOT owns a
        // factory is a battle map, play it as drawn. That is half right and it
        // hid the real shape of the NET maps. They carry 12 to 42 factories and
        // 4 to 8 bases standing NEUTRAL, waiting to be taken — they are
        // CONQUEST maps. The troops are not leftovers, they are the tool, so
        // they must not be thinned; and the report has to name the prize
        // instead of announcing "KEIN Platz hat eine Fabrik", which told the
        // player the opposite of what the map wanted from him.
        var producers = PlayersWithFactory();
        var prize = NeutralPrizes();
        bool conquest = prize.All > 0;
        // a build-up is only a build-up when somebody OWNS a factory and there
        // is nothing to conquer; otherwise the map is played as it is drawn
        bool buildUp = producers.Count > 0 && !conquest;
        var live = buildUp ? producers : LivePlayers();
        if (live.Count == 0) return -1;
        if (!live.Contains(human)) human = live[0];
        var foes = new List<int>();
        foreach (int p in live)
            if (p != human && foes.Count < aiCount) foes.Add(p);
        if (foes.Count < aiCount)
            GD.Print($"Gemetzel: die Karte hat nur {live.Count} " +
                     $"{(buildUp ? "Plaetze mit Fabrik" : "besetzte Plaetze")}, " +
                     $"also {foes.Count} statt {aiCount} Gegner");
        ViewPlayer = human;

        // a skirmish has no mission_init and no sec106, so no slot is neutral —
        // the static list is cleared in case a campaign ran before this one
        ClearNeutralPlayers();

        // "Rohstoffe" — the original's own option, and the original applies it
        // exactly here: its routine has one caller, in the game-start message.
        // A campaign mission must NOT come through this (Simulation/Resources.cs).
        bool res = ApplyResources(UI.SkirmishSetup.Resources);

        int cleared = buildUp ? KeepStartingTroop(StarterTroop) : 0;
        EnableSkirmishAi(foes, level);
        var per = BasesPerSlot();
        float spread = BaseSpread();
        GD.Print($"Gemetzel: Spieler {human} (Startplatz {human + 1}) gegen {foes.Count} KI ({level}); " +
                 (conquest
                    ? $"EROBERUNGSKARTE: {prize.All} neutrale Gebaeude zu besetzen " +
                      $"({prize.Factories} Fabriken, {prize.Bases} Basen); " +
                      $"besetzte Plaetze {string.Join(",", live)}; " +
                      "die Truppen der Karte bleiben stehen — sie sind das Werkzeug"
                  : buildUp
                    ? $"Plaetze mit Fabrik {string.Join(",", live)}; " +
                      $"Gebaeude je Platz {string.Join("/", System.Array.ConvertAll(live.ToArray(), p => per[p]))}; " +
                      $"Verhaeltnis {spread:0.0}:1{(spread >= 2f ? " — UNAUSGEGLICHEN" : "")}; " +
                      $"je Platz bleiben {StarterTroop} Einheiten, {cleared} entfernt"
                    : $"besetzte Plaetze {string.Join(",", live)}; " +
                      "nichts Neutrales zu besetzen und kein Platz mit Fabrik — die Karte " +
                      "wird gespielt, wie sie gezeichnet ist, mit ihren Truppen"));
        if (res) GD.Print(ResourceWatchLine());
        return human;
    }

    /// <summary>How many units a slot keeps when the armies are thinned. OURS —
    /// small enough that the game is still a build-up, large enough that the
    /// player can act in the first minute. Equal for every slot, so the
    /// intervention cannot become an imbalance of its own.</summary>
    public const int StarterTroop = 6;

    /// <summary>
    /// Thin the armies the map brings down to <paramref name="keep"/> units per
    /// slot instead of removing them outright.
    ///
    /// The kept ones are the lowest slot numbers, which is the order the map
    /// file itself lists them in, so which units survive is the map's choice
    /// and not a taste of ours.
    ///
    /// OURS, and knowingly so: the original does not thin these armies at all.
    /// Buildings, deposits, rail lines and the map are untouched.
    /// </summary>
    private int KeepStartingTroop(int keep)
    {
        var order = new List<int>();
        for (int i = 0; i < _entities.Count; i++)
        {
            var e = _entities[i];
            if (e.IsProp || e.IsBuilding || e.Dead) continue;
            order.Add(i);
        }
        order.Sort((a, b) => _entities[a].Slot.CompareTo(_entities[b].Slot));

        var kept = new int[8];
        int removed = 0;
        foreach (int i in order)
        {
            var e = _entities[i];
            int p = e.Owner;
            if (p is >= 0 and <= 7 && kept[p] < keep) { kept[p]++; continue; }
            e.Dead = true;
            e.Hp = 0;
            e.DeadTime = 999f;              // long dead: no wreck or smoke at t=0
            e.Path = null;
            e.Target = -1;
            e.Orders.Clear();
            _nav?.ClearOccupant(e.Col, e.Row, i);
            removed++;
        }
        _sel.Clear();
        return removed;
    }

    // ClearStartingArmies used to sit here and take EVERY mobile unit off the
    // board. It is replaced by KeepStartingTroop, which leaves an equal handful
    // per slot — see the note there and in StartSkirmish for why sweeping the
    // map clean was the wrong answer on a map with no factory to build from.

    public string AiLine()
    {
        if (!_aiOn) return "";
        // E units · W in the wave · b built · a attacks · g on its way to a door
        // · t taken · p builds that came out of the mission's own programme
        return "KI " + string.Join(" ", _ai.Select(a =>
            $"P{a.Player}:{ArmyOf(a.Player).Count}E/{a.Wave.Count}W/{a.Built}b/{a.Waves}a" +
            $"/{(a.Grabber >= 0 ? 1 : 0)}g/{a.Taken}t" +
            (a.Plan != null ? $"/{a.FromPlan}p" : "")));
    }

    /// <summary>What the computer players took out of their programme, by name
    /// — the check that the recovered lines really reach a factory.</summary>
    public string AiPlanLine()
    {
        if (!_aiOn) return "";
        var parts = new List<string>();
        foreach (var a in _ai)
        {
            if (a.Plan == null) continue;
            parts.Add($"P{a.Player} {a.FromPlan}x aus dem Programm " +
                      $"[{string.Join(", ", a.PlanNames)}]" +
                      (a.PlanUnmatched.Count > 0
                          ? $"; {a.PlanMissed}x uebersprungen, Entwuerfe ohne Menueeintrag: " +
                            string.Join(",", a.PlanUnmatched)
                          : ""));
        }
        return parts.Count == 0 ? "" : "KI-Bauprogramm gebaut: " + string.Join("  ", parts);
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
            AiGrab(a);
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
            // NOTE (07.08.2026): a foot soldier's weapon does NOT come from the
            // entity's +0x0c — that byte is 0 for all of them. It is filled in
            // at load time from the design its sprite set points at, and only
            // when that design does damage (MapEntityLayer, "foot soldiers:
            // the map file carries no hp and no weapon for them"). So armed
            // infantry passes CanFight and unarmed infantry — the Scientist and
            // the Civilian — correctly does not. Do not "fix" this by letting
            // every soldier through; that drags the civilians into the army.
            if (!CanFight(e)) continue;
            l.Add(i);
        }
        return l;
    }

    // ---- production -------------------------------------------------------

    private void AiProduce(AiPlayer a)
    {
        // A campaign mission's own programme is stepped ONCE per decision, for
        // the player — not once per factory. That is the shape of the original:
        // `ai_production` runs a single line every 50 ticks and looks for a
        // place to put it, so a line meant for the Fahrwerk-Fabrik is not
        // quietly handed to the Waffen-Fabrik and dropped.
        bool planned = a.Plan is { Count: > 0 };
        if (planned) AiProducePlanStep(a);

        for (int i = 0; i < _entities.Count; i++)
        {
            var e = _entities[i];
            if (!e.IsBuilding || e.Dead || e.Owner != a.Player) continue;

            // a factory that is idle and has parts starts the next unit
            if (IsFactory(e) && e.BuildTime <= 0f && _designs != null && _designs.Count > 0)
            {
                // with a programme the factories do not choose at all
                if (planned) continue;
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

    /// <summary>
    /// One line of the mission's build programme.
    ///
    /// Faithful to `ai_production` @0x4BB9A0 in the two things that matter: the
    /// counter moves on whether or not the line could be built, and a line is
    /// offered to a place that can actually make it. Where a line names a design
    /// none of this player's idle factories has on its menu, nothing is built
    /// this time — the original behaves the same way, it simply finds no base
    /// (»AI: production base:«) and returns.
    ///
    /// ⚠ Our pace is still ours: the original takes one step every 50 ticks,
    /// this takes one per think-timer decision.
    /// </summary>
    private void AiProducePlanStep(AiPlayer a)
    {
        if (_designs == null || a.Plan == null || a.Plan.Count == 0) return;

        var (kind, what) = a.Plan[a.Pc % a.Plan.Count];
        a.Pc = (a.Pc + 1) % a.Plan.Count;
        if (kind != 0) return;              // 1 = aircraft, 2 = ignored by the original too

        for (int i = 0; i < _entities.Count; i++)
        {
            var e = _entities[i];
            if (!e.IsBuilding || e.Dead || e.Owner != a.Player) continue;
            if (!IsFactory(e) || e.BuildTime > 0f) continue;

            var menu = BuildableBy(e.BType);
            for (int k = 0; k < menu.Count; k++)
            {
                var d = _designs[menu[k]];
                // ⚠ `Slot % 200`, not `200*player + what` as the original does
                // (@0x4BB258): LoadDesigns keeps one entry per distinct name, so
                // only one of the eight player blocks survives in our table.
                if (d.Slot < 0 || d.Slot % DesignsPerPlayer != what) continue;
                if (!CanAfford(e, d)) return;      // the line stands; wait for parts
                PayFor(e, d);
                e.MenuIndex = k;
                e.BuildIndex = menu[k];
                e.BuildTime = BuildSeconds;
                a.Built++;
                a.FromPlan++;
                if (a.PlanNames.Count < 12) a.PlanNames.Add(d.Name);
                return;
            }
        }
        a.PlanMissed++;
        a.PlanUnmatched.Add(what);
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

    /// <summary>
    /// Go and take something. OURS from end to end — the original's AI was never
    /// reversed — but without it a conquest map is not a game: the human walks
    /// from one neutral factory to the next while the computer watches.
    ///
    /// Deliberately small: <b>one</b> unit at a time, the one nearest the
    /// nearest prize, and it keeps that job until the building changes hands or
    /// the unit dies. Everything else stays in the wave that
    /// <see cref="AiFight"/> commands, so taking buildings never eats the army.
    /// The unit is sent to the cell in FRONT of the door, which is the cell the
    /// original's capture block looks at (Simulation/Capture.cs).
    /// </summary>
    private void AiGrab(AiPlayer a)
    {
        if (_nav == null) return;

        // is the standing job still a job?
        if (a.GrabTarget >= 0 && a.GrabTarget < _entities.Count)
        {
            var b = _entities[a.GrabTarget];
            bool mine = b.Owner == a.Player;
            if (mine) a.Taken++;
            if (mine || b.Dead || b.Doors == 0) { a.Grabber = -1; a.GrabTarget = -1; }
        }
        else a.GrabTarget = -1;

        if (a.Grabber >= 0 && a.Grabber < _entities.Count)
        {
            var u = _entities[a.Grabber];
            if (u.Dead || u.Owner != a.Player) a.Grabber = -1;
        }
        else a.Grabber = -1;

        if (a.GrabTarget >= 0 && a.Grabber >= 0)
        {
            // still on the way: only nudge it when it has stopped short
            var u = _entities[a.Grabber];
            var b = _entities[a.GrabTarget];
            var (door, front) = CaptureCells(b);
            if (u.Path == null && (u.Col, u.Row) != (front.X, front.Y) &&
                (u.Col, u.Row) != (door.X, door.Y))
                AiWalkTo(a.Grabber, front);
            return;
        }

        // pick the nearest prize: something with a door that is not ours
        var army = ArmyOf(a.Player);
        if (army.Count == 0) return;
        int bestB = -1, bestU = -1;
        float bestD = float.MaxValue;
        for (int bi = 0; bi < _entities.Count; bi++)
        {
            var b = _entities[bi];
            if (!b.IsBuilding || b.IsProp || b.Dead) continue;
            if (b.Doors == 0 || b.Built == 0 || b.Owner == a.Player) continue;
            foreach (int ui in army)
            {
                var u = _entities[ui];
                if (u.DugIn) continue;
                float d = new Vector2(b.Col - u.Col, b.Row - u.Row).Length();
                if (d >= bestD) continue;
                bestD = d; bestB = bi; bestU = ui;
            }
        }
        if (bestB < 0) return;

        var (_, cell) = CaptureCells(_entities[bestB]);
        if (!AiWalkTo(bestU, cell)) return;
        a.Grabber = bestU;
        a.GrabTarget = bestB;
    }

    /// <summary>Send a unit to a cell without giving it a target to shoot at —
    /// <see cref="AiSend"/> always aims at something, and a captor must walk up
    /// to the door rather than stop at weapon range.</summary>
    private bool AiWalkTo(int idx, Vector2I cell)
    {
        if (_nav == null || idx < 0 || idx >= _entities.Count) return false;
        var e = _entities[idx];
        if (e.Dead || !e.Mobile || e.DugIn) return false;
        if (e.FuelMax > 0 && e.Fuel <= 0) return false;
        var goal = _nav.NearestFree(cell, e.Move, idx);
        if (goal == null) return false;
        var path = _nav.FindPath(new Vector2I(e.Col, e.Row), goal.Value, e.Move, idx);
        if (path == null || path.Count == 0) return false;
        e.Path = path;
        e.PathIdx = 0;
        e.Goal = goal.Value;
        e.Reserved = null;
        e.WaitTime = 0;
        e.Target = -1;
        e.Ordered = true;
        return true;
    }

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

        // gather the free ones, keeping the guard at home and leaving the unit
        // that is on its way to a door alone (AiGrab)
        var free = army.Where(i => !_entities[i].DugIn && i != a.Grabber).ToList();
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

        var goal = _nav.NearestFree(new Vector2I(t.Col, t.Row), e.Move, idx);
        if (goal == null) return;
        var path = _nav.FindPath(new Vector2I(e.Col, e.Row), goal.Value, e.Move, idx);
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
