using System.Collections.Generic;
using System.Linq;
using Godot;

namespace AkteEuropaReborn.Rendering;

/// <summary>
/// The skirmish opponent.
///
/// ⚠ Die Kopfzeile "Nichts in dieser Datei stammt aus GAME.EXE" ist seit dem
/// 10.08.2026 ueberholt. ZWEI Stuecke sind jetzt aus der EXE gelesen und als
/// solche gekennzeichnet (Werkzeug `aekernel-tools/ai_units.py`, auf BEIDEN
/// GAME.EXE per Fingerabdruck gegengeprueft):
///
///   * <see cref="AiFindBase"/> + <see cref="AiProducePlanStep"/> —
///     `find_base` @0x4BB0C0 / `build_in_base` @0x4BB1E0 / `ai_production`
///     @0x4BB9A0. Der Computerspieler baut in einer ZUFAELLIG gewaehlten
///     eigenen BASIS (Gebaeudetyp 1), nicht in einer Fabrik, und fragt dabei
///     KEIN Baumenue ab. Hat er keine Basis, baut er nichts.
///   * <see cref="AiSweep"/> + <see cref="AiRingTarget"/> — `ai_units`
///     @0x4BF4E0. Jede untaetige Einheit sucht sich selbst ein Ziel; es gibt
///     im Original keine Welle.
///
/// Alles andere — Wellen, Wachen, Denk-Takt, das Besetzen, die Entwurfswahl im
/// Gemetzel — ist weiterhin OURS: ein schlichter, lesbarer Gegner. Er ist an
/// dieselben Regeln gebunden wie der Mensch und schummelt nicht bei Rohstoffen,
/// Sicht oder Tempo.
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
        public int PlanMissed;                     // lines whose design we do not have
        public readonly List<string> PlanNames = new();
        public readonly HashSet<int> PlanUnmatched = new();

        // die einzelnen Ausgaenge von build_in_base @0x4BB1E0, getrennt gezaehlt
        public int PlanNoBase;                     // »AI: production base: 255«
        public int PlanBroke;                      // »Sources check« nicht bestanden
        public int PlanBusy;                       // die Basis baut schon (unsere Setzung)
        public int PlanOther;                      // Zeilenart 2 — auch im Original nichts

        // und die von build_in_airport @0x4BB3D0, genauso getrennt (11.08.2026)
        public int PlanAir;                        // »Airp build« — ein Flugzeug entstand
        public int PlanNoAirport;                  // find_airport gab 0xFF, kein Flughafen
        public int PlanAirBroke;                   // »Sources check« am Flughafen
        public int PlanHangar;                     // »Hangar check« nicht bestanden
        public int PlanAirMissed;                  // den Flugzeugentwurf gibt es nicht
        public readonly List<string> PlanAirNames = new();
        public readonly HashSet<int> PlanAirUnmatched = new();
        public string AirBroke = "";               // woran der Flughafen scheitert

        /// <summary>
        /// Der Wuerfel dieses Spielers. `find_base` und der Einheitendurchlauf
        /// greifen beide auf `rand()` zurueck; das Wuerfeln selbst ist also
        /// original, der Startwert ist UNSERE Setzung.
        ///
        /// <para>⚠ <b>Umgestellt am 12.08.2026 von <c>System.Random</c> auf den
        /// Kartenkeim</b> (Simulation/Determinism.cs). Zwei Gruende, beide aus
        /// dem Netzspiel:</para>
        /// <list type="number">
        /// <item><c>System.Random</c> gibt KEINE Zusage ueber seine Zahlenfolge
        /// zwischen .NET-Fassungen; zwei Spieler mit verschieden gepatchtem
        /// .NET wuerfeln verschieden, und das ist im Lockstep ein
        /// auseinandergelaufenes Spiel.</item>
        /// <item>Der Startwert war die feste Zahl <c>0x4BF4E0 + spieler</c> —
        /// jede Partie auf jeder Karte hatte damit DIESELBEN KI-Wuerfe. Aus dem
        /// Kartenkeim gespeist, ist die Folge weiterhin auf allen Maschinen
        /// gleich, aber nicht mehr in jeder Partie dieselbe.</item>
        /// </list>
        /// <para>Der Weg geht ueber <see cref="Determinism.Roll"/>, also ueber
        /// den EINEN Strom des Spiels. Getrennte Stroeme je Spieler waeren
        /// robuster (ein Spieler, der aussteigt, verschoebe den Strom der
        /// anderen nicht) — solange es keine Netzschicht gibt, waere das aber
        /// eine Vorkehrung gegen ein Problem, das noch niemand gemessen
        /// hat.</para>
        /// </summary>
        public int Roll(int n) => Simulation.Determinism.Roll(n);

        /// <summary>Der Einheitendurchlauf: Sekunden bis zum naechsten Block und
        /// welcher der acht Bloecke als naechstes drankommt (ai_units).</summary>
        public float Sweep;
        public int Block;
        public int Looked, Sent;                   // fuer die Statuszeile
        public int SawAny, SawFoe, SawClass;       // Belegung im Ring / feindlich / Klasse passt
        public readonly HashSet<string> ClassSeen = new();

        /// <summary>Welche INFANTERIE (typ +0x0a == 1) je einen Befehl bekommen
        /// hat — die Zahl, an der Punkt 4 der Fehlerliste haengt.</summary>
        public readonly HashSet<int> MovedInf = new();
        public string Broke = "";                  // woran »Sources check« scheitert

        public AiPlayer(int player) { Player = player; }
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
            var a = new AiPlayer(p) { Level = level, Think = 0.5f + p * 0.13f };
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
            if (!e.IsBuilding && e.Attack != 6) continue;
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
        InCampaign = true;

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
                 $" ({level}); keine Angriffswellen, kein Gebaeudegreifer " +
                 "(das Original marschiert auf einer Kampagnenkarte nicht) — " +
                 string.Join(" ", System.Array.ConvertAll(live.ToArray(),
                     p => $"P{p}:{ArmyOf(p).Count}E")) +
                 (read
                     ? $"; Diplomatie aus GAME.EXE, Mission {mission}, neutral " +
                       string.Join(",", NeutralPlayerList())
                     : "; OHNE campaign_diplomacy.json — es gilt die zurueckgezogene " +
                       "Basis-Regel"));
        UnarmedProbeSetup(human);
        return human;
    }

    // ---- Pruefstand »--unarmed-check« ---------------------------------------
    //
    // ⚠ WAS DIESER PRUEFSTAND SEHEN KANN UND WAS NICHT.
    //
    // Er kann sehen: ob ein Ausruestungstraeger in der Armee des Computers
    // steht, ob ihm der Einheitendurchlauf ein ZIEL gibt und ob er sich darauf
    // ZUBEWEGT. Genau das war die Meldung.
    //
    // Er kann NICHT von selbst zuschauen: auf map_01 stehen die drei
    // Baufahrzeuge des Spielers 1 bei (16,6), (13,8) und (15,9), der Mensch
    // hat eine einzige Einheit bei (4,39) — dreissig Felder weit weg. Ein
    // Leerlauf meldet darum »0 feindlich im Ring«, und zwar auch mit dem
    // Fehler drin. Der Pruefstand stellt deshalb EINE feindliche Einheit in
    // den Suchring des ersten Baufahrzeugs; das ist genau die Lage, die der
    // Spieler herstellt, wenn er nach Norden faehrt. Das Umsetzen ist eine
    // Handlung DES HARNISCHS und steht nur unter diesem Schalter.
    //
    // Der Ring ist der von <see cref="AiRingTarget"/>: getroffen wird, was
    // weiter weg steht als `far` und hoechstens `far + 1` — bei den drei
    // Wagen (Reichweite +0x2b = 0, Sicht +0x2c = 6) also das Band (6, 7].
    // Naeher heranstellen hilft nicht: der Durchlauf faengt erst da an, wo der
    // normale Takt aufhoert.
    private bool _unarmedCheck;
    private string _unarmedProbe = "";
    private readonly List<UnarmedWatch> _unarmedWatch = new();

    private sealed class UnarmedWatch
    {
        public int Idx;
        public int Slot, Part;
        public Vector2I Start;
        public bool GotTarget, GotPath, EverInArmy, Died;
        public float MaxDist;
    }

    /// <summary>Den Schalter lesen — <see cref="Core.CommandLine"/>, damit der
    /// Harnisch (MapViewer) dafuer nichts zu tun hat.</summary>
    private static bool WantUnarmedCheck()
    {
        foreach (string a in Core.CommandLine.Args)
            if (a == "--unarmed-check") return true;
        return false;
    }

    private void UnarmedProbeSetup(int human)
    {
        _unarmedCheck = WantUnarmedCheck();
        _unarmedWatch.Clear();
        _unarmedProbe = "";
        if (!_unarmedCheck || _nav == null) return;

        for (int i = 0; i < _entities.Count; i++)
        {
            var e = _entities[i];
            if (e.IsBuilding || e.IsProp || e.Dead || !e.Mobile) continue;
            if (e.Owner is < 0 or > 7 || e.Owner == human) continue;
            if (!AiUnarmed(e)) continue;
            _unarmedWatch.Add(new UnarmedWatch
            {
                Idx = i, Slot = e.Slot, Part = e.Weapon, Start = new Vector2I(e.Col, e.Row),
            });
        }
        if (_unarmedWatch.Count == 0) { _unarmedProbe = "keine unbewaffnete Einheit auf der Karte"; return; }

        // eine bewegliche Einheit des Menschen als Koeder
        int probe = -1;
        for (int i = 0; i < _entities.Count; i++)
        {
            var e = _entities[i];
            if (e.IsBuilding || e.IsProp || e.Dead || !e.Mobile || e.Owner != human) continue;
            probe = i; break;
        }
        if (probe < 0) { _unarmedProbe = $"kein beweglicher Koeder bei Spieler {human}"; return; }

        var w = _unarmedWatch[0];
        var t = _entities[w.Idx];
        int near = t.Range > 0 ? t.Range : Mathf.RoundToInt(RangeOf(t));
        int far = t.Sight > near ? t.Sight : near + AiSightPad;
        var p = _entities[probe];
        var from = new Vector2I(p.Col, p.Row);

        int bestC = -1, bestR = -1;
        int r = far + 1;
        for (int dy = -r; dy <= r && bestC < 0; dy++)
        for (int dx = -r; dx <= r && bestC < 0; dx++)
        {
            float d = Mathf.Sqrt(dx * dx + dy * dy);
            if (d <= far || d > far + 1) continue;      // genau das Band von AiRingTarget
            int c = t.Col + dx, ro = t.Row + dy;
            if (!_nav.InBounds(c, ro) || !_nav.IsFree(c, ro, p.Move, probe)) continue;
            // ⚠ Und das Feld muss vom Baufahrzeug aus ERREICHBAR sein. Ohne
            // diese Bedingung stellte der Harnisch den Koeder zwar in den
            // Suchring, aber hinter unwegsames Gelaende: `AiSend` fand dann
            // keinen Pfad, gab still auf, und der Pruefstand meldete »kein
            // Ziel« AUCH MIT DEM FEHLER DRIN. Er haette den Fehler nicht
            // sehen koennen. Gemessen am 11.08.2026: Koeder auf (20,1),
            // 22 Aufrufe von `AiSend`, kein einziger Pfad.
            var probePath = _nav.FindPath(new Vector2I(t.Col, t.Row), new Vector2I(c, ro),
                                          t.Move, w.Idx);
            if (probePath == null || probePath.Count == 0) continue;
            bestC = c; bestR = ro;
        }
        if (bestC < 0) { _unarmedProbe = "kein erreichbares freies Feld im Suchring"; return; }

        _nav.ClearOccupant(p.Col, p.Row, probe);
        if (p.Reserved is { } rc) _nav.ClearOccupant(rc.X, rc.Y, probe);
        p.Reserved = null;
        p.Path = null;
        p.Target = -1;
        p.Orders.Clear();
        p.Col = bestC; p.Row = bestR;
        p.Elev = ElevOf(p.Col, p.Row);
        p.Pos = CellCenter(p.Col, p.Row);
        p.Footprint = CellRect(_ox, _oy, p.Col, p.Row, p.Elev);
        _nav.SetOccupant(p.Col, p.Row, probe, p.Infantry >= 0);

        _unarmedProbe = $"Koeder: Spieler {human}, Platz {p.Slot} von ({from.X},{from.Y}) " +
                        $"nach ({bestC},{bestR}) gestellt — Ring des Platzes {w.Slot} " +
                        $"bei ({t.Col},{t.Row}), Band ({far},{far + 1}]";
        GD.Print("unarmed-check: " + _unarmedProbe);
    }

    /// <summary>Jeden Takt nachsehen, was aus den beobachteten Wagen geworden
    /// ist. Absichtlich nicht nur »hat gerade ein Ziel«, sondern »hat je eines
    /// bekommen« — ein Ziel kann in derselben Sekunde wieder verfallen.</summary>
    private void UnarmedWatchTick()
    {
        if (!_unarmedCheck) return;
        foreach (var w in _unarmedWatch)
        {
            if (w.Idx < 0 || w.Idx >= _entities.Count) continue;
            var e = _entities[w.Idx];
            if (e.Target >= 0) w.GotTarget = true;
            if (e.Path != null) w.GotPath = true;
            if (e.Dead) { w.Died = true; continue; }
            // »war je in der Armee« statt »ist es am Ende«: mit dem Fehler drin
            // faehrt der Wagen los und wird erschossen, und ein Toter steht in
            // keiner Armee mehr — der Befund waere hinterher verschwunden.
            if (!w.EverInArmy && ArmyOf(e.Owner).Contains(w.Idx)) w.EverInArmy = true;
            float d = new Vector2(e.Col - w.Start.X, e.Row - w.Start.Y).Length();
            if (d > w.MaxDist) w.MaxDist = d;
        }
    }

    /// <summary>Der Befund. Steht in der Zeile, die der Harnisch bei
    /// <c>--quit-after</c> ohnehin druckt.</summary>
    public string AiUnarmedLine()
    {
        if (!_unarmedCheck) return "";
        if (_unarmedWatch.Count == 0) return "unarmed-check: " + _unarmedProbe;
        var parts = new List<string>();
        int bad = 0;
        foreach (var w in _unarmedWatch)
        {
            bool moved = w.MaxDist > 0.01f;
            if (w.GotTarget || moved || w.EverInArmy) bad++;
            parts.Add($"Platz {w.Slot} (Aufsatz {w.Part}, {AiPartName(w.Part)}) " +
                      $"von ({w.Start.X},{w.Start.Y}): " +
                      (w.GotTarget ? "ZIEL BEKOMMEN" : "kein Ziel") + ", " +
                      (w.GotPath ? "PFAD BEKOMMEN" : "kein Pfad") + ", " +
                      $"{(moved ? "GEFAHREN" : "steht")} {w.MaxDist:0.0} Felder; " +
                      $"in der Armee: {(w.EverInArmy ? "JA" : "nein")}" +
                      (w.Died ? "; unterwegs VERLOREN" : ""));
        }
        return $"unarmed-check: {_unarmedProbe}\nunarmed-check: {_unarmedWatch.Count} " +
               $"unbewaffnete Einheiten, {bad} davon in der Armee, mit Ziel oder in Bewegung " +
               (bad == 0 ? "— in Ordnung" : "— FEHLER") + "\n  " +
               string.Join("\n  ", parts);
    }

    /// <summary>Der Name des Ausruestungsaufsatzes. Die Umrechnung ist die aus
    /// sec47 (Entwurf +0x17 -> +0x2d), oben belegt; die Namen selbst stehen in
    /// <c>component_stats.json</c>, Zeilen 65..79.</summary>
    private static string AiPartName(int part) => part switch
    {
        40 => "Luftsauger",      41 => "Radar",             42 => "Minenraeumer",
        43 => "Mechaniker",      44 => "Teleporter",        45 => "Fallenraeumer",
        46 => "Transporter",     47 => "Gebaeude-Techniker", 48 => "Boden-Techniker",
        49 => "Generatorenbauer", 50 => "Radarstab-Ausleger", 51 => "Antimagnetiker",
        52 => "Antiradar",       53 => "Terranium-Finder",  54 => "Zielfokus",
        _ => $"Aufsatz {part}",
    };

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
        // ⚠ IM NETZSPIEL SIND ALLE MENSCHENPLÄTZE TABU, nicht nur der eigene —
        // und das ist keine Feinheit, sondern die Bedingung dafür, dass ein
        // Netzspiel mit KI-Gegnern überhaupt laufen kann.
        //
        // Ohne diese Zeile rechnet jede Maschine `live minus MEIN Platz`. Auf
        // dem Gastgeber (Platz 0) wäre Platz 1 dann ein Computerspieler, auf dem
        // Mitspieler (Platz 1) wäre es Platz 0 — beide Simulationen würden also
        // eine ANDERE Armee vom Rechner steuern lassen, und sie liefen im
        // ersten Denk-Takt auseinander. Der Fehler sähe aus wie ein
        // Rechenfehler; er ist eine verschiedene Aufstellung.
        //
        // Die Plätze kommen aus der Partie des Vermittlers (NetSession.Slot),
        // sind also auf allen Maschinen dieselbe Liste. Ohne Netzspiel ist sie
        // leer und es ändert sich nichts.
        var humans = Network.NetworkManager.HumanSlots();
        var foes = new List<int>();
        foreach (int p in live)
            if (p != human && !humans.Contains(p) && foes.Count < aiCount) foes.Add(p);
        if (humans.Length > 0)
            GD.Print($"Netzspiel: Menschenplätze {string.Join(",", humans)} — " +
                     $"die KI bekommt davon keinen; Computerspieler {string.Join(",", foes)}");
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
        // B8: erst die Basen verteilen, DANN die KI einschalten — sonst plant
        // sie ihren ersten Zug auf einem Besitzstand, den es einen Takt spaeter
        // nicht mehr gibt. ⚠ Und danach ist `prize` veraltet, darum wird die
        // Zahl unten neu geholt.
        var mine = new List<int>(foes) { human };
        // ⚠ 17.08.2026 — C24: VOR dem Verteilen der Basen. Wer nicht mitspielt,
        // soll auch keine Basis zugeteilt bekommen, und seine Einheiten sollen
        // beim Verteilen nicht mehr im Weg stehen.
        int idle = NoClearIdleSlots ? 0 : ClearIdleSlots(mine);
        int gaveBases = conquest ? GrantStartingBases(mine) : 0;
        if (gaveBases > 0) prize = NeutralPrizes();
        EnableSkirmishAi(foes, level);
        var per = BasesPerSlot();
        float spread = BaseSpread();
        GD.Print($"Gemetzel: Spieler {human} (Startplatz {human + 1}) gegen {foes.Count} KI ({level}); " +
                 (conquest
                    ? $"EROBERUNGSKARTE: {prize.All} neutrale Gebaeude zu besetzen " +
                      $"({prize.Factories} Fabriken, {prize.Bases} Basen); " +
                      $"besetzte Plaetze {string.Join(",", live)}; " +
                      "die Truppen der Karte bleiben stehen — sie sind das Werkzeug; " +
                      (NoStartBase
                          ? "Startbasen NICHT zugeteilt (--no-start-base, alter Stand)"
                          : $"Startbasen zugeteilt: {gaveBases} von {mine.Count} Mitspielern " +
                            "(unsere Abweichung, B8 — nur die Basis, die uebrigen " +
                            "Gebaeude bleiben zu besetzen)")
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

    // ========================================================================
    //  B8 — jeder Mitspieler faengt mit einer Basis an
    // ========================================================================

    /// <summary>GEGENPROBE: nicht zuteilen, also der Stand vor dem 15.08.2026.
    /// Nur <c>--no-start-base</c> setzt das.</summary>
    public static bool NoStartBase;

    /// <summary>Wieviele Basen zuletzt zugeteilt wurden — fuer die Meldung und
    /// den Pruefstand.</summary>
    public int StartBasesGiven { get; private set; }

    /// <summary>Hat dieser Platz schon eine Basis (Gebaeudeart 1)?</summary>
    private bool HasBase(int p)
    {
        foreach (var e in _entities)
            if (e.IsBuilding && !e.IsProp && !e.Dead && e.Owner == p && e.BType == 1) return true;
        return false;
    }

    /// <summary>
    /// Wo dieser Platz seinen Schwerpunkt hat, IN ZELLEN.
    ///
    /// <para>⚠ Ganzzahlig, und das ist kein Geschmack: die Zuteilung unten
    /// laeuft im Lockstep-Pfad, und <c>Entity.Pos</c> ist <c>float</c>. Die
    /// Fliesskommafrage zwischen zwei verschiedenen Maschinen ist offen (siehe
    /// Handoff), also darf hier keine Fliesskommaentscheidung stehen.
    /// <see cref="PlayerHome"/> ist die Fassung fuer die Kamera, die darf.</para>
    /// </summary>
    private (int C, int R)? HomeCell(int p)
    {
        foreach (var e in _entities)
            if (e.IsBuilding && !e.IsProp && !e.Dead && e.Owner == p && e.BType == 1)
                return (e.Col, e.Row);
        long sc = 0, sr = 0; int n = 0;
        foreach (var e in _entities)
        {
            if (e.IsProp || e.Dead || e.Owner != p) continue;
            sc += e.Col; sr += e.Row; n++;
        }
        return n > 0 ? ((int)(sc / n), (int)(sr / n)) : null;
    }

    /// <summary>
    /// JEDER MITSPIELER BEKOMMT EINE BASIS — <b>UNSERE ABWEICHUNG</b>, und sie
    /// ist ausdruecklich gewollt.
    ///
    /// <para>Gewuenscht als B8: »Bei Gefecht sollte jeder Spieler, auch AI,
    /// direkt mit einer Basis starten, oder Sie einnehmen zu muessen. Nur die
    /// anderen Gebaeude muessen eingenommen werden.«</para>
    ///
    /// <para><b>Was die Karten mitbringen</b> und warum das noetig ist: die
    /// Eroberungskarten stellen 4 bis 8 Basen NEUTRAL auf (Eigner 11), und wer
    /// zuerst eine erreicht, hat sie. Das ist ein Wettlauf, kein Gefecht — und
    /// die Kampagne bleibt davon unberuehrt, weil dieser Weg nur im Gemetzel
    /// laeuft (Trennachse <c>CampaignMission &gt; 0</c>).</para>
    ///
    /// <para>⚠ <b>Ganzzahlig und in fester Reihenfolge.</b> Die Plaetze werden
    /// aufsteigend bedient, und jeder nimmt die ihm naechste noch freie Basis;
    /// gemessen wird in ZELLEN, nicht in Bildpunkten, und bei gleichem Abstand
    /// gewinnt der kleinere Satzindex. Damit haengt das Ergebnis an keiner
    /// Fliesskommazahl und an keinem Wurf — es muss auf zwei Maschinen dasselbe
    /// sein, sonst laeuft das Netzspiel im ersten Takt auseinander.</para>
    ///
    /// <para>Die uebrigen neutralen Gebaeude bleiben liegen; nur die Basis wird
    /// vorweggenommen. <c>--no-start-base</c> stellt den alten Stand her.</para>
    /// </summary>
    private int GrantStartingBases(List<int> players)
    {
        StartBasesGiven = 0;
        if (NoStartBase) return 0;
        var taken = new HashSet<int>();
        var sorted = new List<int>(players);
        sorted.Sort();
        foreach (int p in sorted)
        {
            if (p is < 0 or > 7 || HasBase(p)) continue;
            var home = HomeCell(p);
            int best = -1; long bd = long.MaxValue;
            for (int i = 0; i < _entities.Count; i++)
            {
                var e = _entities[i];
                if (!e.IsBuilding || e.IsProp || e.Dead) continue;
                if (e.Owner != NeutralOwner || e.BType != 1 || e.Doors == 0) continue;
                if (taken.Contains(i)) continue;
                long d = 0;
                if (home is { } h)
                {
                    long dc = e.Col - h.C, dr = e.Row - h.R;
                    d = dc * dc + dr * dr;
                }
                if (d < bd) { bd = d; best = i; }
            }
            if (best < 0) continue;
            Hand(_entities[best], p);
            taken.Add(best);
            StartBasesGiven++;
        }
        return StartBasesGiven;
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

    /// <summary>
    /// <b>Die Truppen der Plätze wegräumen, die gar nicht mitspielen.</b>
    ///
    /// <para>⚠ 17.08.2026, gemeldet (C24): »wenn die Karte z.B. 5 KI erlaubt und
    /// man nur 3 nutzt, dann steht an den so gesehen inaktiven Basen immer die
    /// Starteinheit dennoch da.«</para>
    ///
    /// <para><b>Und genau so ist es gebaut:</b> <c>StartSkirmish</c> füllt
    /// <c>foes</c> nur bis <c>aiCount</c> auf; alle weiteren Plätze aus
    /// <c>live</c> bekommen keine KI — ihre Einheiten aber bleiben stehen und
    /// rühren sich nie. Für den Spieler sind das Ziele ohne Gegner, und sie
    /// verfälschen jede Abrechnung, weil sie als lebende Armee zählen.</para>
    ///
    /// <para><b>Weggeräumt werden nur die MOBILEN Einheiten</b>, nicht die
    /// Gebäude. Ein Gebäude eines nicht mitspielenden Platzes ist ein PREIS —
    /// es lässt sich einnehmen, und das ist auf einer Eroberungskarte der Sinn
    /// der Sache. Eine Einheit, die niemand steuert, ist dagegen nur ein
    /// Hindernis.</para>
    ///
    /// <para>Weggeräumt wird auf demselben Weg wie in
    /// <see cref="KeepStartingTroop"/> — <c>Dead</c> mit langer Totzeit, damit
    /// kein Wrack und kein Rauch entsteht, und die Belegung freigeben. ⚠ NICHT
    /// aus der Liste löschen: die Sätze hängen an ihrem Index, und das Löschen
    /// mitten im Aufbau würde jeden Verweis darauf verschieben.</para></summary>
    /// <summary><c>--no-clear-idle</c> — die Gegenprobe zu C24: die Truppen der
    /// nicht mitspielenden Plaetze bleiben stehen, wie vor dem 17.08.2026.</summary>
    public static bool NoClearIdleSlots;

    private int ClearIdleSlots(List<int> playing)
    {
        int removed = 0;
        var per = new Dictionary<int, int>();
        for (int i = 0; i < _entities.Count; i++)
        {
            var e = _entities[i];
            if (e.IsBuilding || e.IsProp || e.Dead) continue;
            if (e.Owner is < 0 or > 7 || playing.Contains(e.Owner)) continue;
            e.Dead = true;
            e.Hp = 0;
            e.DeadTime = 999f;
            e.Path = null;
            e.Target = -1;
            e.Orders.Clear();
            _nav?.ClearOccupant(e.Col, e.Row, i);
            per[e.Owner] = per.GetValueOrDefault(e.Owner) + 1;
            removed++;
        }
        if (removed > 0)
        {
            var parts = new List<string>();
            foreach (var kv in per) parts.Add($"P{kv.Key}: {kv.Value}");
            GD.Print($"Gemetzel: {removed} Einheiten von {per.Count} Plaetzen weggeraeumt, " +
                     $"die nicht mitspielen ({string.Join(", ", parts)}) — " +
                     "ihre Gebaeude bleiben stehen und sind einnehmbar");
        }
        return removed;
    }

    public string AiLine()
    {
        if (!_aiOn) return "";
        // E units · W in the wave · b built · a attacks · g on its way to a door
        // · t taken · p builds that came out of the mission's own programme
        return "KI " + string.Join(" ", _ai.Select(a =>
            $"P{a.Player}:{ArmyOf(a.Player).Count}E/{a.Wave.Count}W/{a.Built}b/{a.Waves}a" +
            $"/{(a.Grabber >= 0 ? 1 : 0)}g/{a.Taken}t" +
            (a.Plan != null ? $"/{a.FromPlan}p" : "")))
            // ⚠ Seit dem Depot (16.08.2026) ist »gebaut« nicht mehr dasselbe wie
            // »steht auf der Karte«. Diese zwei Zahlen schliessen die Luecke.
            + $" || Depot: {AiDepotSent} ausgesandt, {AiDepotStuck}x kein Platz";
    }

    /// <summary>Was die Computerspieler aus ihrem Programm gezogen haben, mit
    /// Namen — und was sie daran gehindert hat. Die Ausgaenge sind die von
    /// `build_in_base` @0x4BB1E0, jeder einzeln gezaehlt, damit ein Ausfall
    /// nicht wieder als "uebersprungen" verschwindet.</summary>
    public string AiPlanLine()
    {
        if (!_aiOn) return "";
        var parts = new List<string>();
        foreach (var a in _ai)
        {
            if (a.Plan == null)
            {
                parts.Add($"P{a.Player} ohne Programm ({AiBaseCount(a.Player)} Basen)");
                continue;
            }
            int air = a.Plan.Count(l => l.Kind == 1);
            parts.Add($"P{a.Player} {a.FromPlan}x aus dem Programm " +
                      $"[{string.Join(", ", a.PlanNames)}]" +
                      $"; {AiBaseCount(a.Player)} Basen" +
                      $"; ohne Basis {a.PlanNoBase}x, Lager leer {a.PlanBroke}x, " +
                      $"Basis baut schon {a.PlanBusy}x, leere Zeilenart {a.PlanOther}x" +
                      (a.PlanUnmatched.Count > 0
                          ? $", Entwurf unbekannt {a.PlanMissed}x: " +
                            string.Join(",", a.PlanUnmatched)
                          : "") +
                      (a.Broke.Length > 0 ? $" [zuletzt: {a.Broke}]" : "") +
                      // Zeilenart 1 — steht nur da, wo das Programm sie hat
                      $"  |  Flugzeuge: {air} Zeilenart-1-Zeilen, {a.PlanAir}x gebaut " +
                      $"[{string.Join(", ", a.PlanAirNames)}]" +
                      $"; {AiAirportLine(a.Player)}" +
                      $"; ohne Flughafen {a.PlanNoAirport}x, Lager leer " +
                      $"{a.PlanAirBroke}x, Hangar voll {a.PlanHangar}x" +
                      (a.PlanAirUnmatched.Count > 0
                          ? $", Entwurf unbekannt {a.PlanAirMissed}x: " +
                            string.Join(",", a.PlanAirUnmatched)
                          : "") +
                      (a.AirBroke.Length > 0 ? $" [zuletzt: {a.AirBroke}]" : ""));
        }
        // Der Harnisch (MapViewer) druckt genau diese eine Zeile; die Streife
        // haengt darum hier mit dran, statt einen neuen Aufruf dort zu brauchen.
        string sweep = AiUnitLine();
        string plan = parts.Count == 0 ? "" : "KI-Bauprogramm gebaut: " + string.Join("  ", parts);
        // und aus demselben Grund haengt der Pruefstand fuer die unbewaffneten
        // Einheiten hier mit dran
        string un = AiUnarmedLine();
        var all = new List<string>();
        if (plan.Length > 0) all.Add(plan);
        if (sweep.Length > 0) all.Add(sweep);
        if (un.Length > 0) all.Add(un);
        return string.Join("\n", all);
    }

    /// <summary>Was der Einheitendurchlauf (ai_units @0x4BF4E0) getan hat —
    /// wie viele untaetige Einheiten angesehen und wie viele losgeschickt
    /// wurden. Das ist die Zahl, an der Punkt 4 haengt.</summary>
    public string AiUnitLine()
    {
        if (!_aiOn) return "";
        return "KI-Streife: " + string.Join("  ", _ai.Select(a =>
            $"P{a.Player} {a.Sent} losgeschickt, {a.Looked} untaetige angesehen " +
            $"(Armee {ArmyOf(a.Player).Count}), im Ring {a.SawAny} Einheiten, " +
            $"{a.SawFoe} feindlich, {a.SawClass} in der eigenen Klasse " +
            $"[{string.Join(" ", a.ClassSeen)}]; Infanterie bewegt " +
            $"{a.MovedInf.Count}/{InfantryOf(a.Player)}"));
    }

    // ---- the loop ---------------------------------------------------------

    private void UpdateAi(float dt)
    {
        UpdateAiInner(dt);

        // ⚠ DER EINHÄNGER DES PRÜFSTANDS (Simulation/DeterminismHarness.cs).
        //
        // Er steht hier und nicht in MapEntityLayer._Process, weil an dieser
        // Datei gerade jemand anderes arbeitet. UpdateAi ist der vorletzte
        // Aufruf des Takts (@MapEntityLayer.cs:11119, danach kommt nur noch
        // MissionScriptTick) — also der späteste Punkt, an den diese Sitzung
        // herankommt. Kostet ausserhalb eines Prüflaufs ein einziges bool.
        DeterminismTick(dt);
    }

    private void UpdateAiInner(float dt)
    {
        UnarmedWatchTick();
        if (!_aiOn) return;
        foreach (var a in _ai)
        {
            if (!AliveAsPlayer(a.Player)) continue;

            // Der Einheitendurchlauf haengt NICHT am Denk-Takt. Im Original ist
            // er ein eigener Eintrag der KI-Runde (Takte 16,20,…,44) und laeuft
            // damit auch dann, wenn gerade nichts entschieden wird.
            AiSweep(a, dt);

            // ⚠⚠ 16.08.2026 — DAS DEPOT LEEREN (Fehler F der Liste E, und es ist
            // eine REGRESSION von diesem Tag). Seit fertige Einheiten nicht mehr
            // direkt auf der Karte stehen, sondern im Depot ihres Gebäudes
            // liegen (siehe MapEntityLayer.Entity.Depot), muss sie jemand
            // herausholen. Der Mensch hat dafür den Knopf »Aussenden«; die KI
            // hatte gar nichts, und ihre Einheiten blieben liegen — gemeldet als
            // »Die AI im Gefecht scheint Ihre Einheiten nicht aus dem Depot zu
            // senden«.
            //
            // Das steht VOR dem Denk-Takt und nicht darin: das Aussenden ist
            // keine Entscheidung, sondern die Fortsetzung des Bauens. Eine KI,
            // die nur alle paar Sekunden denkt, dürfte sonst ihr Depot
            // verstopfen und danach nicht mehr produzieren (das Depot hat sechs
            // Plätze, und ist es voll, wartet die Fertigstellung).
            AiEmptyDepots(a);

            a.Think -= dt;
            a.AttackTimer -= dt;
            if (a.Think > 0f) continue;
            var (think, waveSize, guard) = AiTuning(a.Level);
            a.Think += think;

            AiProduce(a);

            // ⚠ AUF EINER KAMPAGNENKARTE MARSCHIERT NIEMAND (11.08.2026).
            //
            // Der Spieler meldete, in Mission 1 fahre alles irgendwohin, obwohl
            // sie im Original wie ein Tutorial anfängt. Gemessen nach 90 s:
            // 19 von 21 Einheiten des Gegners in einer Welle, 10 Fusssoldaten
            // unterwegs — und `shots=0`. Es marschierte nur.
            //
            // Und das Original tut das NICHT. `ai_units(spieler, block)`
            // @0x4BF4E0 (aekernel-tools/ai_units.py) nimmt nur Einheiten mit
            // `faze == 0` UND `ukol == 0` und geht dann **den eigenen Sichtring**
            // durch (Versatztabelle, begrenzt durch +0x2c, die grosse
            // Reichweite). Findet es dort ein feindliches Fahrzeug, ruft es
            // `order` @0x410220 und setzt `ukol = 4`. Findet es keines,
            // geschieht GAR NICHTS. Es gibt im ganzen Durchlauf keinen Schritt,
            // der eine Einheit auf ein Ziel ausserhalb ihrer Reichweite
            // zubewegt — die Angriffswelle und der Gebäudegreifer sind
            // vollständig UNSER Zusatz, und ihr Kopfkommentar sagt das bei
            // `AiGrab` sogar selbst (»OURS from end to end«).
            //
            // Im Gefecht bleiben beide: dort ersetzen sie das Missionsskript,
            // das es nicht gibt, und ohne sie stünde die Karte still. Auf einer
            // Kampagnenkarte richtet die Mission aus, was geschieht — bis deren
            // Block (0x498000..0x4A5600) gelesen ist, ist Stillhalten näher am
            // Original als Herumfahren.
            if (!InCampaign)
            {
                AiGrab(a);
                AiFight(a, waveSize, guard);
            }
            else
            {
                // ⚠ NACHTRAG desselben Tages, und er nimmt die Aussage oben
                // nicht zurueck, sondern vervollstaendigt sie: das Original
                // marschiert auf einer Kampagnenkarte nicht VON SELBST — aber
                // die MISSION kann ihre Computerspieler losschicken.
                // `add_target(spieler, art, vorrang, wort, c)` @0x4CF700 traegt
                // ein Ziel in die Liste 0xBC5A78 ein (100 je Spieler, 6 Byte),
                // und alle Leser dieser Liste liegen im KI-Bereich. Genau so
                // bekommt Mission 9, 13, 15, 20 und 24 ihre Angriffe.
                AiMissionAttack(a, waveSize, guard);
            }
        }
    }

    /// <summary>Läuft gerade eine Kampagnenmission? Gesetzt von
    /// <see cref="StartCampaign"/>, und der einzige Schalter, der die beiden
    /// Verhaltensweisen abschaltet, die im Original keine Entsprechung haben.</summary>
    public bool InCampaign { get; private set; }

    // ---- die Zielliste, die eine Mission ihren Computerspielern gibt --------
    //
    // `add_target(spieler, art, vorrang, wort, c)` @0x4CF700, Tabelle 0xBC5A78:
    // **100 Ziele je Spieler à 6 Byte**, der erste freie Platz gewinnt, sonst
    // meldet das Spiel »Cannot add new target«. Der Eintrag:
    //
    //     +0x00  art      1..4, Sprungtabelle @0x4BE184
    //     +0x01  vorrang  die auswaehlende Routine nimmt nur Ziele darueber
    //     +0x02  wort     das Ziel selbst
    //     +0x04  c        zweites Byte des Ziels (bei art 3 die Spalte)
    //
    // Die auswaehlende Routine @0x4BDCC0 laeuft die 100 Plaetze ab und **loescht
    // einen Eintrag, sobald sein Ziel erledigt ist** — das ist die Abbruch-
    // bedingung, und sie steht je Art woanders:
    //
    //     art 1  Gebaeudeplatz `wort`   (76*wort + 0xC06914), weg wenn typ == 0
    //     art 2  Einheitenplatz `wort`  (78*wort + 0x6E26D1), weg wenn +0x09 == 0xFF
    //     art 3  Kartenzelle `(wort<<8) + c` — die gepackte Zellnummer
    //            row*256+col — in der Belegungskarte 0xBDEA80; weg, sobald der
    //            Belegende dem Spieler SELBST gehoert (`/1000 == spieler`)
    //
    // ⚠ **Art 4 ist ungelesen** und wird darum nur vermerkt, nicht ausgefuehrt.
    private sealed class MissionTarget
    {
        public int Kind, Priority, Word, Second;
    }

    private readonly List<MissionTarget>[] _missionTargets =
        { new(), new(), new(), new(), new(), new(), new(), new() };

    /// <summary>Ein Ziel eintragen — <c>add_target</c>.</summary>
    public void AddMissionTarget(int player, int kind, int prio, int word, int second)
    {
        if (player is < 0 or > 7) return;
        var list = _missionTargets[player];
        if (list.Count >= 100)          // »Cannot add new target«
        {
            GD.PrintErr($"add_target: Spieler {player} hat schon 100 Ziele");
            return;
        }
        list.Add(new MissionTarget { Kind = kind, Priority = prio, Word = word, Second = second });
        GD.Print($"Missionsziel fuer Spieler {player}: Art {kind}, Vorrang {prio}, " +
                 $"Ziel {word}" + (kind == 3 ? $" (Zelle {second},{word})" : "") +
                 (kind is < 1 or > 3 ? "  ⚠ Art ungelesen — wird nicht ausgefuehrt" : ""));
    }

    /// <summary>Wieviele Ziele ein Spieler noch offen hat — für den Prüfstand.</summary>
    public int MissionTargetsOf(int player)
        => player is >= 0 and <= 7 ? _missionTargets[player].Count : 0;

    /// <summary>Den Eintrag auflösen: der Index der Entität, die gemeint ist,
    /// oder -1. Erledigte Ziele werden dabei gestrichen, genau wie @0x4BDCC0
    /// es tut.</summary>
    private int ResolveTarget(int player, MissionTarget t)
    {
        switch (t.Kind)
        {
            case 1:                                   // Gebaeudeplatz
                for (int i = 0; i < _entities.Count; i++)
                    if (_entities[i].IsBuilding && !_entities[i].Dead &&
                        _entities[i].Slot == t.Word) return i;
                return -1;
            case 2:                                   // Einheitenplatz
                for (int i = 0; i < _entities.Count; i++)
                    if (!_entities[i].IsBuilding && !_entities[i].Dead &&
                        _entities[i].Slot == t.Word) return i;
                return -1;
            case 3:                                   // Kartenzelle
            {
                int col = t.Second, row = t.Word;
                for (int i = 0; i < _entities.Count; i++)
                {
                    var e = _entities[i];
                    if (e.IsProp || e.Dead || e.Col != col || e.Row != row) continue;
                    // erledigt, sobald die Zelle dem Spieler selbst gehoert
                    return e.Owner == player ? -1 : i;
                }
                return -1;
            }
            default:
                return -1;                            // Art 4: ungelesen
        }
    }

    /// <summary>
    /// Die Armee eines Computerspielers auf das Ziel schicken, das ihm die
    /// Mission gegeben hat — mit dem höchsten Vorrang zuerst, wie @0x4BDCC0.
    /// Kein Ziel heisst: es geschieht nichts, und das ist der Normalfall.
    /// </summary>
    private void AiMissionAttack(AiPlayer a, int waveSize, int guard)
    {
        var list = _missionTargets[a.Player];
        if (list.Count == 0) return;

        int best = -1, bestPrio = int.MinValue;
        for (int k = list.Count - 1; k >= 0; k--)
        {
            int idx = ResolveTarget(a.Player, list[k]);
            if (idx < 0) { list.RemoveAt(k); continue; }   // erledigt — streichen
            if (list[k].Priority > bestPrio) { bestPrio = list[k].Priority; best = idx; }
        }
        if (best < 0) return;

        // ⚠ UNSERE Setzung ist die Zahl der Einheiten, die losgehen: das
        // Original waehlt sie in @0x4BECF0, das hier nicht gelesen ist. Genommen
        // wird dieselbe Wellengroesse wie im Gefecht, damit wenigstens EINE
        // Zahl im Spiel steht und nicht zwei verschiedene.
        var army = ArmyOf(a.Player);
        a.Wave.RemoveAll(i => i >= _entities.Count || _entities[i].Dead ||
                              _entities[i].Owner != a.Player);
        if (a.Wave.Count > 0 && a.TargetIdx == best) return;   // schon unterwegs
        if (army.Count <= guard) return;

        a.Wave.Clear();
        int take = Mathf.Min(waveSize, army.Count - guard);
        for (int k = 0; k < take && k < army.Count; k++)
        {
            a.Wave.Add(army[k]);
            AiSend(army[k], best);
        }
        a.TargetIdx = best;
        a.Waves++;
        GD.Print($"KI P{a.Player}: {take} Einheiten auf das Missionsziel " +
                 $"{_entities[best].Name} bei ({_entities[best].Col},{_entities[best].Row})");
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

    /// <summary>Wie viele Fusssoldaten dieser Spieler noch hat — Einheitensatz
    /// +0x0a == 1 (siehe <see cref="AiInfantryClass"/>).</summary>
    private int InfantryOf(int p)
    {
        int n = 0;
        foreach (var e in _entities)
            if (!e.IsBuilding && !e.IsProp && !e.Dead && e.Owner == p &&
                e.GameUnitType == AiInfantryClass) n++;
        return n;
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
            // ⚠ 11.08.2026 — und hier fielen die BAUFAHRZEUGE durch. `CanFight`
            // prueft `e.Weapon != 0`, und `e.Weapon` ist der AUFSATZ (+0x0c),
            // nicht die Waffe: ein Gebaeude-Techniker traegt dort die 47, ein
            // Boden-Techniker die 48 — beide ungleich 0, beide also »kampf-
            // faehig«. Damit standen sie in der Armee, der Einheitendurchlauf
            // hat sie angefasst und `AiSend` ist mit ihnen losgefahren. Das
            // Original haette sie nie angefasst: ihr +0x0d ist 0 (siehe
            // <see cref="AiUnarmed"/>), und ohne Waffe koennen sie ohnehin
            // nichts ausrichten.
            if (AiUnarmed(e)) continue;
            l.Add(i);
        }
        return l;
    }

    // ---- production -------------------------------------------------------

    private void AiProduce(AiPlayer a)
    {
        // Das Bauprogramm der Mission macht EINEN Schritt je Entscheidung, fuer
        // den SPIELER — nicht einen je Fabrik. So ist das Original gebaut:
        // `ai_production` @0x4BB9A0 fuehrt eine einzige Zeile je 50 Takte aus
        // und legt sie in eine ausgewuerfelte BASIS (siehe AiProducePlanStep).
        // Die Fabriken kommen dabei gar nicht vor; sie waehlen nur im Gemetzel,
        // wo es kein Programm gibt, und das ist unsere Zutat.
        bool planned = a.Plan is { Count: > 0 };
        if (planned) AiProducePlanStep(a);

        for (int i = 0; i < _entities.Count; i++)
        {
            var e = _entities[i];
            if (!e.IsBuilding || e.Dead || e.Owner != a.Player) continue;

            // eine BASIS, die frei ist und Teile hat, faengt die naechste
            // Einheit an — ⚠ 11.08.2026: hier stand IsFactory, und damit baute
            // der Computer dort, wo das Original nur Teile herstellt. Der
            // Kommentar ueber dieser Schleife sagte es selbst: `ai_production`
            // @0x4BB9A0 legt seine Zeile in eine ausgewuerfelte BASIS, »die
            // Fabriken kommen dabei gar nicht vor«. Siehe
            // MapEntityLayer.IsUnitPlant fuer die Belege.
            if (IsUnitPlant(e) && e.BuildTime <= 0f && _designs != null && _designs.Count > 0)
            {
                // with a programme the bases do not choose at all
                if (planned) continue;
                var menu = BuildableBy(e.BType);
                if (menu.Count > 0)
                {
                    e.MenuIndex = AiPickDesign(a, menu);
                    int pick = menu[e.MenuIndex % menu.Count];
                    var chosen = _designs[pick];
                    // the same three-store test the player's base passes
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

    /// <summary>Der Gebaeudetyp, in dem der Computerspieler produziert, und wie
    /// viele Gebaeudeplaetze <c>find_base</c> absucht — beides GELESEN:
    /// `cmp byte[76*i + 0xC06914], 1` ist Gebaeudesatz +0x04 = typ, und die
    /// Schleife laeuft <c>for (dx = 0; dx &lt; 0xFF; dx++)</c>.
    /// Auf der F:-Fassung steht dasselbe bei 0xC05974 — <c>ai_units.py</c>
    /// liest die Adresse in beiden Faellen aus dem Rumpf.</summary>
    private const int AiBaseType = 1;
    private const int AiBaseSlots = 255;

    /// <summary>
    /// <b>`find_base` @0x4BB0C0 — GELESEN, nicht erfunden.</b>
    ///
    /// <code>
    /// find_base(spieler):
    ///     n = 0
    ///     for i = 0 .. 254:
    ///         if typ[i] == 1 and eigner[i] == spieler: kand[n++] = i
    ///     if n == 0: return 0xFF
    ///     return kand[rand() % n]
    /// </code>
    ///
    /// Zwei Dinge daran sind der ganze Punkt 15 der Fehlerliste:
    /// <list type="number">
    /// <item>gebaut wird in einer <b>BASIS</b> (Gebaeudetyp 1), nicht in einer
    /// der drei Fabriken — deshalb fragt das Original auch kein Baumenue ab;</item>
    /// <item>die Basis wird <b>ausgewuerfelt</b>, es ist nicht "die erste freie".</item>
    /// </list>
    /// Wer keine Basis hat, baut nichts: `build_in_base` gibt bei 0xFF sofort 0
    /// zurueck, und die Zeile ist trotzdem verbraucht.
    /// </summary>
    private int AiFindBase(AiPlayer a)
    {
        var cand = new List<int>();
        for (int i = 0; i < _entities.Count && cand.Count < AiBaseSlots; i++)
        {
            var e = _entities[i];
            if (!e.IsBuilding || e.IsProp || e.Dead) continue;
            if (e.BType != AiBaseType || e.Owner != a.Player) continue;
            cand.Add(i);
        }
        return cand.Count == 0 ? -1 : cand[a.Roll(cand.Count)];
    }

    /// <summary>Wie viele Basen ein Spieler hat — nur fuer die Statuszeile.</summary>
    private int AiBaseCount(int p)
    {
        int n = 0;
        foreach (var e in _entities)
            if (e.IsBuilding && !e.IsProp && !e.Dead && e.BType == AiBaseType && e.Owner == p) n++;
        return n;
    }

    /// <summary>
    /// Eine Zeile des Bauprogramms — jetzt in der Form des Originals.
    ///
    /// <code>
    /// ai_production(spieler):                          @0x4BB9A0, Takt 5 der Runde
    ///     if !enabled[spieler]:   »AI: no production - no transport«;  return
    ///     pc = programmzaehler[spieler]
    ///     if pc == 0xFF:          »AI: no production - nothing to do«; return
    ///     zeile = programm[50*spieler + pc]            ; 3 Byte
    ///     switch zeile[0]: 0 -> build_in_base(zeile[1], spieler)      @0x4BB1E0
    ///                      1 -> build_in_airport(zeile[1], spieler)   @0x4BB3D0
    ///                      sonst: nichts
    ///     pc++;  if pc > 49 or naechste[0] == 0xFF: pc = 0            ; laeuft um
    ///
    /// build_in_base(entwurf, spieler):
    ///     »AI: production in base %d«
    ///     b = find_base(spieler);  »AI: production base: %d«
    ///     if b == 0xFF: return 0
    ///     »Sources check«   n = 200*spieler + entwurf
    ///     if kosten_w > lager_w(b) || kosten_f > lager_f(b) || kosten_s > lager_s(b): return 0
    ///     »Depo check«      if depotplatz(b) belegt: return 0
    ///     »Depo check ok«   spawn;  »Robot build«
    /// </code>
    ///
    /// <b>Was sich dadurch aendert und warum Punkt 15 genau das war:</b> bisher
    /// wurde die Zeile jeder untaetigen FABRIK angeboten und nur gebaut, wenn
    /// der Entwurf auf deren <c>BuildableBy</c>-Menue stand. Die Zeilen 84/85/86
    /// ("Chaingun Tank", "Light Tank", "Medium Tank") tragen in
    /// <c>unit_designs.json</c> aber <c>flags[0] == 0</c>, stehen also auf gar
    /// keinem Menue, und Zeile 53 ("Pioneer") landet ueber
    /// <c>FitsFactory</c> nur in der Fahrwerk-Fabrik. Das ist der gemeldete
    /// Befund <c>Entwuerfe ohne Menueeintrag: 85,86,84,53</c> — und warum am
    /// Ende fast nur der freigeschaltete "Transporter" gebaut wurde.
    /// Das Original kennt diese Huerde nicht: es liest die drei Kostenbytes des
    /// Entwurfs und vergleicht sie mit den drei Lagern der Basis, fertig.
    ///
    /// <b>UNSERE SETZUNGEN hier, ausdruecklich:</b>
    /// <list type="bullet">
    /// <item>der "Depo check" ist bei uns "die Basis baut gerade nichts" —
    /// das Original prueft einen Platz in einer 16-Byte-Tabelle
    /// (<c>word[16*cis_typ + 0x878E66] == 0xFFFF</c>), die wir nicht abbilden;</item>
    /// <item>der Entwurf wird ueber <c>Slot % 200</c> gesucht statt ueber
    /// <c>200*spieler + zeile</c> (@0x4BB258): <c>LoadDesigns</c> haelt je Name
    /// nur EINEN Eintrag, es ueberlebt also nur einer der acht Spielerbloecke;</item>
    /// <item>das Tempo — eine Zeile je Denk-Entscheidung statt je 50 Takte.</item>
    /// </list>
    /// </summary>
    private void AiProducePlanStep(AiPlayer a)
    {
        if (_designs == null || a.Plan == null || a.Plan.Count == 0) return;

        var (kind, what) = a.Plan[a.Pc % a.Plan.Count];
        a.Pc = (a.Pc + 1) % a.Plan.Count;   // der Zaehler laeuft weiter, egal was folgt
        // Der Verteiler von `ai_production` @0x4BBA27: `test eax,eax` -> 0,
        // `cmp eax,1` -> 1, alles andere faellt durch und tut NICHTS.
        if (kind == 1) { AiProduceAirStep(a, what); return; }
        if (kind != 0) { a.PlanOther++; return; }

        int bi = AiFindBase(a);
        if (bi < 0) { a.PlanNoBase++; return; }     // »AI: production base: 255«
        var b = _entities[bi];
        if (b.BuildTime > 0f) { a.PlanBusy++; return; }

        int pick = -1;
        for (int k = 0; k < _designs.Count; k++)
        {
            int s = _designs[k].Slot;
            if (s >= 0 && s % DesignsPerPlayer == what) { pick = k; break; }
        }
        if (pick < 0) { a.PlanMissed++; a.PlanUnmatched.Add(what); return; }

        var d = _designs[pick];
        if (!CanAfford(b, d))
        {
            a.PlanBroke++;                                 // »Sources check«
            a.Broke = $"{d.Name} kostet {d.CostW}/{d.CostF}/{d.CostS}, " +
                      $"Basis hat {b.StockW}/{b.StockF}/{b.StockS}";
            return;
        }
        PayFor(b, d);
        b.BuildIndex = pick;
        b.BuildTime = BuildSeconds;
        a.Built++;
        a.FromPlan++;
        if (a.PlanNames.Count < 12) a.PlanNames.Add(d.Name);
    }

    /// <summary>
    /// <b>Alles aussenden, was in den Depots dieses KI-Spielers liegt.</b>
    ///
    /// <para>⚠ <b>UNSERE SETZUNG, und sie ist nötig</b>, aber sie ist bewusst
    /// die einfachste: die KI sendet SOFORT und ALLES aus. Ob das Original seine
    /// KI Einheiten im Depot sammeln lässt, ist <b>nicht gelesen</b> — was
    /// gelesen ist, ist nur das Depot selbst (sechs Plätze, 0x878e5c). Bis
    /// dahin ist »sofort raus« das Verhalten, das dem Stand vor dem Depot
    /// entspricht, und damit das, was am wenigsten kaputtmacht.</para>
    ///
    /// <para>⚠ Schlägt das Aussenden fehl (kein freier Platz an der Tür), bleibt
    /// die Einheit liegen und wird beim nächsten Durchgang erneut versucht —
    /// <see cref="MapEntityLayer.SendOutOfDepot"/> gibt dafür false zurück,
    /// statt sie zu verwerfen.</para></summary>
    private void AiEmptyDepots(AiPlayer a)
    {
        // ⚠⚠ 18.08.2026 — HIER STAND EIN `foreach (var b in _entities)`, UND ES
        // WARF. `SendOutOfDepot` stellt die fertige Einheit auf die Karte und
        // HAENGT SIE DAMIT AN `_entities` AN; der naechste Schritt des
        // Aufzaehlers stirbt dann an »Collection was modified; enumeration
        // operation may not execute«.
        //
        // Gemessen: auf map_DM_1 und map_DM_4 in JEDEM Lauf, sobald ein
        // Computerspieler das erste Mal etwas fertigbaut — der Takt bricht ab,
        // die Ausnahme wiederholt sich Bild fuer Bild. Gefunden ist er beim
        // Nachschub des Ladens, nicht beim Ansehen der KI: der Prueflauf lief
        // laenger als die uebrigen und kam dadurch ueberhaupt erst so weit.
        // (Das ist Regel 14 von der anderen Seite — ein Fehler in Datei A faellt
        // bei der Arbeit an Datei B auf.)
        //
        // Ueber den Index, mit der Laenge VON VORHER: was waehrend des Laufs
        // hinten dazukommt, ist die gerade ausgesandte Einheit und hat hier
        // nichts zu suchen. Die zweite Bedingung faengt den Fall ab, dass
        // anderswo etwas entfernt wird.
        int stand = _entities.Count;
        for (int i = 0; i < stand && i < _entities.Count; i++)
        {
            var b = _entities[i];
            if (!b.IsBuilding || b.Dead || b.Owner != a.Player) continue;
            // rückwärts wäre falsch: das Original nimmt den ersten Platz, und
            // die Reihenfolge ist die der Fertigstellung.
            int wache = 0;
            while (b.Depot.Count > 0 && wache++ < DepotSlots)
            {
                if (!SendOutOfDepot(b, 0)) { AiDepotStuck++; break; }
                AiDepotSent++;
            }
        }
    }

    /// <summary>Wieviele Einheiten die KI aus Depots herausgeholt hat, und wie
    /// oft es nicht ging. ⚠ Regel 33: ohne diese zwei Zahlen ist »die KI baut«
    /// nicht von »die KI baut und alles bleibt liegen« zu unterscheiden — genau
    /// das war der gemeldete Fehler.</summary>
    public int AiDepotSent, AiDepotStuck;

    // ================= Zeilenart 1: die Flugzeuge ============================
    //
    // GELESEN am 11.08.2026, Werkzeug `aekernel-tools/ai_air.py`, auf BEIDEN
    // GAME.EXE per Fingerabdruck wiedergefunden (7 von 7 Funktionen eindeutig)
    // und dort Zahl fuer Zahl neu ausgelesen; die Instruktionsfolgen sind bis
    // auf EINEN Befehl gleich, und der unterscheidet sich nur in der
    // Operandenreihenfolge desselben Vergleichs.
    //
    // ⚠ Die im Handoff notierte Spawn-Adresse **0x4B1840 ist die falsche**,
    // und zwar nicht als Tippfehler, sondern als andere Funktion:
    //   * 0x4B1380 ist die FLUGZEUG-Spawn-Routine — `build_in_airport` ruft sie
    //     ueber den Thunk 0x401D93 (`jmp 0x4B1380`). Die frueher notierte
    //     0x4B1580 ist dieselbe Funktion, 0x200 Byte tiefer.
    //   * 0x4B1840 ist die BODEN-Spawn-Routine der Zeilenart 0: sie sucht im
    //     Einheitenfeld 0x6E26C8 (Schrittweite 78) einen freien Satz und wird
    //     unter anderem von 0x4BB31B gerufen — mitten aus `build_in_base`.

    /// <summary>Der Gebaeudetyp, in dem der Computerspieler FLUGZEUGE baut.
    /// GELESEN: `find_airport` @0x4BB150 ist Byte fuer Byte dieselbe Funktion
    /// wie `find_base` @0x4BB0C0 — dieselben 255 Plaetze, dasselbe typ@+0x14,
    /// eigner@+0x15, dasselbe Kandidatenfeld, dieselbe Zufallswahl. Der EINZIGE
    /// Unterschied ist die Typzahl: <c>cmp byte[76*i + 0xC06914], 9</c> statt
    /// <c>, 1</c>. Auf der F:-Fassung steht dasselbe bei 0xC05974.</summary>
    private const int AiAirportType = 9;

    /// <summary>
    /// Die drei Kostenbytes der acht Flugzeugvorlagen — GELESEN, aber ueber
    /// eine Kruecke im Umlauf.
    ///
    /// `build_in_airport` vergleicht <c>byte[0x51B03F + 48*(20*spieler +
    /// entwurf)]</c> und die beiden Folgebytes mit den drei Lagern des
    /// Flughafens. Das sind die Felder <b>+0x1F/+0x20/+0x21</b> des
    /// sec120-Satzes (48 Byte, 20 je Spieler) — und genau die drei liest
    /// <c>CwmExtra.AirDesigns</c> heute NICHT aus, weshalb
    /// <see cref="AirDesign"/> keine Kosten hat und <c>aircraft.json</c> auch
    /// keine enthaelt.
    ///
    /// Bis das nachgezogen ist, stehen hier die Werte der acht Standardvorlagen
    /// aus GAME.EXE, in beiden Fassungen identisch gelesen:
    /// <code>
    ///   0 Jagdflieger        50/ 50/  0    4 Kampfhubschrauber  60/ 40/  0
    ///   1 Bomber             80/ 70/ 10    5 Treibstoffheli      0/ 30/ 40
    ///   2 Spionageflieger     0/ 40/ 30    6 Munitionheli        0/ 30/ 40
    ///   3 Transport Heli      0/ 30/ 50    7 Mechanikerheli      0/ 30/150
    /// </code>
    /// <b>Was daran UNSERE Setzung ist:</b> dass fuer alle acht Spieler
    /// dieselben Kosten gelten. Im Original hat jeder Spieler seinen eigenen
    /// 20er-Block, und eine .DM-Karte kann darin andere Zahlen tragen. Sobald
    /// der Import die drei Bytes mitfuehrt, faellt diese Tabelle ersatzlos weg.
    /// </summary>
    private static readonly int[,] AiAirCost =
    {
        { 50, 50, 0 }, { 80, 70, 10 }, { 0, 40, 30 }, { 0, 30, 50 },
        { 60, 40, 0 }, { 0, 30, 40 }, { 0, 30, 40 }, { 0, 30, 150 },
    };

    /// <summary>
    /// <b>`find_airport` @0x4BB150 — GELESEN.</b>
    /// <code>
    /// find_airport(spieler):
    ///     n = 0
    ///     for i = 0 .. 254:
    ///         if typ[i] == 9 and eigner[i] == spieler: kand[n++] = i
    ///     if n == 0: return 0xFF
    ///     return kand[rand() % n]
    /// </code>
    /// Kein naechstgelegener Flughafen, kein Baumenue, keine Pruefung des
    /// ENABLE-Bytes des Entwurfs (sec120 +0x00) — der Computerspieler baut
    /// auch Flugzeuge, die dem Menschen noch gesperrt sind.
    /// </summary>
    private int AiFindAirport(AiPlayer a)
    {
        var cand = new List<int>();
        for (int i = 0; i < _entities.Count && cand.Count < AiBaseSlots; i++)
        {
            var e = _entities[i];
            if (!e.IsBuilding || e.IsProp || e.Dead) continue;
            if (e.BType != AiAirportType || e.Owner != a.Player) continue;
            cand.Add(i);
        }
        return cand.Count == 0 ? -1 : cand[a.Roll(cand.Count)];
    }

    /// <summary>Wie viele Flughaefen ein Spieler hat und wie voll deren Hangars
    /// sind — nur fuer die Statuszeile, aber es ist die Zahl, an der man sieht,
    /// dass ein gebautes Flugzeug wirklich irgendwo steht.</summary>
    private string AiAirportLine(int p)
    {
        int n = 0, belegt = 0, platz = 0;
        foreach (var e in _entities)
        {
            if (!e.IsBuilding || e.IsProp || e.Dead) continue;
            if (e.BType != AiAirportType || e.Owner != p) continue;
            n++;
            belegt += e.Hangar?.Count ?? 0;
            platz += Mathf.Max(1, e.HangarSize);
        }
        return $"{n} Flughaefen, Hangar {belegt}/{platz}";
    }

    /// <summary>
    /// Der Flugzeugentwurf, den eine Zeilenart-1-Zeile meint.
    ///
    /// Das Original rechnet <c>48*(20*spieler + entwurf)</c>: jeder Spieler hat
    /// seine eigenen ZWANZIG Flugzeugzeilen (sec120). Unsere
    /// <see cref="_airDesigns"/> traegt je nach Herkunft acht (aus der
    /// EXE-Vorlagentabelle ueber den Fahrplan) oder zwanzig (aus sec120) Saetze
    /// je Spieler; darum wird der <c>entwurf</c>-te Satz IM BLOCK DES SPIELERS
    /// genommen statt an einer festen Schrittweite. Das ist dieselbe Rechnung,
    /// nur ohne die Blockgroesse fest zu verdrahten.
    /// </summary>
    private AirDesign? AiAirDesign(int player, int what)
    {
        if (_airDesigns == null || what < 0) return null;
        int seen = 0;
        foreach (var d in _airDesigns)
        {
            if (d.Player != player) continue;
            if (seen++ == what) return d;
        }
        return null;
    }

    /// <summary>
    /// Eine Zeile der Zeilenart 1 — »Build in airp«.
    ///
    /// <code>
    /// build_in_airport(entwurf, spieler):              @0x4BB3D0
    ///     »Build in airp«
    ///     f = find_airport(spieler)                    @0x4BB150
    ///     if f == 0xFF: return 0                       ; kein Flughafen
    ///     »Sources check«
    ///     k = entwurf[20*spieler + entwurf]            ; 48 Byte, 0x51B020
    ///     if k+0x1F > lager_w(f) || k+0x20 > lager_f(f) || k+0x21 > lager_s(f):
    ///         return 0
    ///     »Hangar check«
    ///     c = gebaeude[f]+0x29                         ; die sec27-Nummer
    ///     if sec27[c]+0x03 &lt;= sec27[c]+0x04: return 0   ; Plaetze &lt;= belegt
    ///     »Hangar check ok«
    ///     spawn_aircraft(entwurf, spieler, c)          @0x4B1380
    ///     »Airp build«
    ///
    /// spawn_aircraft(entwurf, spieler, cis):           @0x4B1380
    ///     slot = erster sec19-Satz mit +0x08 == 0      ; 200 Saetze zu 68 Byte
    ///     die Hangar- UND die Lagerpruefung NOCH EINMAL
    ///     lager -= kosten ; belegt++ ; sec19[slot] aus dem Entwurf fuellen
    ///     +0x09 = spieler, +0x28 = cis, +0x32 = entwurf, +0x08 = vorlage+0x2E
    ///     entwurf 0/1/4 -> +0x2C = 0x31/0x2F/0x30 und +0x2A = 3/6/10
    ///     entwurf 5/6   -> +0x31 = 0xFF, sofort aussenden und belegt--
    /// </code>
    ///
    /// <b>Der Punkt, an dem sich das von unserem <c>BuyAircraft</c> trennt:</b>
    /// der Computerspieler zahlt ein Flugzeug NICHT mit Geld, sondern mit den
    /// drei Lagern des Flughafens — genau wie die Basis eine Bodeneinheit
    /// bezahlt. Der Preis von $150 gilt nur fuer den Menschen, der am Flughafen
    /// "Kaufen" drueckt (0x52FAC0/0x52FAC4).
    ///
    /// <b>UNSERE SETZUNGEN hier, ausdruecklich:</b>
    /// <list type="bullet">
    /// <item>die Kosten kommen aus <see cref="AiAirCost"/> statt aus dem
    /// Entwurf, weil der Import die drei Bytes noch nicht mitfuehrt;</item>
    /// <item>»Hangar check« wird an <c>Hangar.Count &lt; HangarSize</c>
    /// gemessen; das Original zaehlt einen eigenen Belegtzaehler (sec27 +0x04),
    /// den es beim Aussenden NICHT herunterzaehlt — nur die beiden
    /// Nachschubhelikopter geben ihren Platz sofort wieder frei;</item>
    /// <item>ein Nachschubhelikopter (Entwurf 5/6) wird bei uns geparkt statt
    /// sofort ausgesandt; das Aussenden haengt am Kundenlauf und gehoert nicht
    /// in diese Datei;</item>
    /// <item>das Tempo — eine Zeile je Denk-Entscheidung statt je 50 Takte.</item>
    /// </list>
    /// </summary>
    private void AiProduceAirStep(AiPlayer a, int what)
    {
        int ai = AiFindAirport(a);
        if (ai < 0) { a.PlanNoAirport++; return; }       // find_airport == 0xFF
        var ap = _entities[ai];

        var d = AiAirDesign(a.Player, what);
        if (d == null || what >= AiAirCost.GetLength(0))
        {
            a.PlanAirMissed++;
            a.PlanAirUnmatched.Add(what);
            return;
        }

        // ⚠ BERICHTIGT 10.08.2026 — der Preis kommt jetzt aus dem ENTWURF
        // (sec120 +0x1F/+0x20/+0x21), also von dort, wo `build_in_airport`
        // @0x4BB3D0 ihn holt (`0x51B03F/40/41` gegen die Basis 0x51B020). Die
        // Tabelle darunter war eine Setzung: sie gab allen acht Spielern
        // dieselben Kosten, obwohl jeder seinen eigenen 20er-Block hat. Sie
        // bleibt als Rückfall für Karten, deren Export die drei Bytes noch
        // nicht mitführt — ein Entwurf ohne Preis wäre sonst umsonst.
        int cw = d.CostW, cf = d.CostF, cs = d.CostS;
        if (cw + cf + cs == 0 && what >= 0 && what < AiAirCost.GetLength(0))
        {
            cw = AiAirCost[what, 0];
            cf = AiAirCost[what, 1];
            cs = AiAirCost[what, 2];
        }
        if (cw > ap.StockW || cf > ap.StockF || cs > ap.StockS)   // »Sources check«
        {
            a.PlanAirBroke++;
            a.AirBroke = $"{d.Name} kostet {cw}/{cf}/{cs}, " +
                         $"Flughafen hat {ap.StockW}/{ap.StockF}/{ap.StockS}";
            return;
        }

        int platz = Mathf.Max(1, ap.HangarSize);
        if ((ap.Hangar?.Count ?? 0) >= platz)                     // »Hangar check«
        {
            a.PlanHangar++;
            a.AirBroke = $"Hangar von {ap.Name} voll ({ap.Hangar?.Count ?? 0}/{platz})";
            return;
        }

        // »Hangar check ok« — ab hier ist es spawn_aircraft @0x4B1380
        ap.StockW = Mathf.Max(0, ap.StockW - cw);
        ap.StockF = Mathf.Max(0, ap.StockF - cf);
        ap.StockS = Mathf.Max(0, ap.StockS - cs);

        int slot = 0;
        foreach (var s in _special) slot = Mathf.Max(slot, s.Slot + 1);
        _special.Add(new Special
        {
            Slot = slot, Kind = d.Kind, Name = d.Name, TypeName = d.Name,
            Col = ap.Col, Row = ap.Row, Stored = true,
            Owner = a.Player, HomeSlot = ap.Slot, Pos = ap.Pos,
            Footprint = ap.Footprint,
            Speed = d.Speed, Hp = d.Hp, HpMax = d.Hp,
            Ammo = d.Ammo, AmmoMax = d.Ammo, Fuel = d.Fuel, FuelMax = d.Fuel,
            Payload = d.Payload, Airframe = d.Airframe,
            Attack = d.Attack, Defence = d.Defence, Sight = d.Sight,
            Cargo = SupplyCargoFull,
        });
        (ap.Hangar ??= new List<int>()).Add(slot);

        a.PlanAir++;
        a.Built++;
        if (a.PlanAirNames.Count < 12) a.PlanAirNames.Add(d.Name);
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

    // ---- der Einheitendurchlauf (ai_units @0x4BF4E0) -----------------------

    /// <summary>Wie lange ein voller Durchlauf durch die eigenen Einheiten
    /// dauert. Das Original braucht dafuer 50 Takte — acht Aufrufe in den
    /// Takten 16, 20, 24, … 44 einer 50-Takt-Runde. Die UMRECHNUNG in Sekunden
    /// ist UNSERE Setzung; die Aufteilung in acht Bloecke ist gelesen.</summary>
    private const float AiSweepSeconds = 2f;

    /// <summary>Die Klassenschwelle aus `ai_units`: <c>cmp byte[u+0x0a], 3</c>
    /// mit <c>seta</c> auf beiden Seiten — eine Einheit sucht sich nur ein Ziel
    /// derselben Seite der Schwelle. +0x0a ist das Feld, das der Debugausdruck
    /// des Spiels <c>typ</c> nennt (GAMESTATE_RE 3.9), bei uns
    /// <c>Entity.GameUnitType</c>. GELESEN, auf beiden EXE gleich.</summary>
    private const int AiClassSplit = 3;

    /// <summary>Womit die Sicht ersetzt wird, wenn eine Einheit keine mitbringt
    /// (+0x2c == 0). UNSERE Setzung.</summary>
    private const int AiSightPad = 4;

    /// <summary>Was +0x0a bei INFANTERIE steht. Aus den Karten ausgezaehlt, nicht
    /// gesetzt: ueber alle 30 map_*.entities.json tragen die 2112 Fahrzeuge
    /// (Fahrwerk 160..175) durchweg 0, die <b>601 Infanteristen (Fahrwerk 148 und
    /// 149) durchweg 1</b>, die 122 + 22 Schiffe (150..158) 4 bzw. 5, und sechs
    /// Einheiten des Typs 138 tragen 3 — kein Gegenbeispiel. Die Schwelle
    /// <see cref="AiClassSplit"/> = 3 trennt damit <b>Land von See</b>: eine
    /// Landeinheit sucht sich nie ein Schiff und umgekehrt.</summary>
    private const int AiInfantryClass = 1;

    // ---- »hat Waffe / hat keine« --------------------------------------------
    //
    // ⚠ 11.08.2026, gemeldet als »in der Kampagne 1 gibt es 3 Fahrzeuge, sieht
    // aus als haetten die einen Bauturm drauf. Die fahren dann auch aggressiv
    // auf einen zu als wuerden sie einen angreifen«. Es sind auf map_01 die
    // Plaetze 1019/1021 (Aufsatz 47) und 1020 (Aufsatz 48) des Spielers 1.
    //
    // <b>Das Spiel unterscheidet »bewaffnet« selbst, und zwar an +0x0d.</b>
    // Zwei Fundstellen, auf BEIDEN Fassungen nach der Form wiedergefunden
    // (Werkzeug: `aekernel-tools/ai_unarmed.py`, das alles hier Behauptete
    // noch einmal aus den EXE und den Karten nachrechnet):
    //
    //   * die WEICHE beim Aufstellen, @0x4B1B6E (C:) / @0x4B14AA (F:):
    //         cmp cl, 0x32          ; cl = Entwurf +0x17, die WAFFENZEILE
    //         jb  <unten>
    //         mov byte [u+0x0d], 0  ; Zeile >= 50  -> KEINE Waffe,
    //         mov byte [u+0x0e], cl ;                die Zeile geht nach +0x0e
    //       <unten>
    //         mov byte [u+0x0d], al ; Zeile <  50  -> die Waffe steht in +0x0d
    //         mov byte [u+0x0e], 0
    //     Die Waffenzeilen 1..19 sind die Geschuetze, 65..79 die AUSRUESTUNG
    //     (Teleporter, Transport, Radar, Mechaniker, G-/B-Techniker …) und
    //     185..199 die Handwaffen — nur die erste Gruppe liegt unter 50.
    //
    //   * der EINHEITENTAKT @0x40DDF0..0x40DE20 (C:) / @0x40DC20..0x40DC4A (F:):
    //         mov al, [u+0x0d]  ;  cmp al, 8 ; je …
    //         mov cl, [u+0x0c]  ;  cmp cl, 0x26 ; jne …   (Flak-Sonderfall)
    //         mov [esp+0x12], al ; test al, al ; jne <KAMPFBLOCK>
    //     Wer in +0x0d eine Null stehen hat, kommt gar nicht erst in den
    //     Kampfblock. +0x0d IST die Fahne »bewaffnet«.
    //
    // <b>Warum wir sie am AUFSATZ +0x0c ablesen und nicht an +0x0d:</b> unser
    // <c>Entity</c> traegt +0x0d nicht (der Kartenleser in MapEntityLayer liest
    // die Zeile nicht mit). Die Umrechnung ist aber vollstaendig und belegt —
    // sec47, Entwurf +0x2d ist der Aufsatz, den das Aufstellen nach +0x0c
    // schreibt (@0x4B1B38/@0x4B1B50), und ueber alle 586 Entwuerfe gilt ohne
    // Ausnahme:
    //         Waffenzeile   1..19  ->  Aufsatz 21..39   (Zeile + 20)
    //         Ausruestung  65..79  ->  Aufsatz 40..54
    //         Handwaffe   185..199 ->  Aufsatz  0
    // Gegenprobe an den Karten selbst, alle 30 map_*.entities.json, kein
    // Gegenbeispiel: jede Einheit mit Aufsatz 21..39 traegt +0x0d = Waffenzeile
    // und +0x0e = 0; jede mit Aufsatz 40..52 traegt <b>+0x0d = 0</b> und
    // +0x0e = 66..77. Dieselben 218 Einheiten haben Angriff (+0x26) = 0,
    // Reichweite (+0x2b) = 0 und Munition 0/0 — auch daran ist keine bewaffnet.
    // Aufsatz 61 (2 Einheiten, +0x0d = 139) liegt bewusst AUSSERHALB des
    // Bandes: der ist bewaffnet (Angriff 30, Reichweite 15).
    private const int AiEquipPartFirst = 40;   // Ausruestung 65 -> Aufsatz 44 … Band 40..54
    private const int AiEquipPartLast = 54;

    /// <summary>Eine Einheit, die einen AUSRUESTUNGSAUFSATZ traegt statt einer
    /// Waffe — im Original die mit <c>+0x0d == 0</c>. Siehe den Block ueber
    /// <see cref="AiEquipPartFirst"/> fuer die beiden Fundstellen.</summary>
    private static bool AiUnarmed(Entity e)
        => e.Weapon >= AiEquipPartFirst && e.Weapon <= AiEquipPartLast;

    /// <summary>
    /// <b>`ai_units(spieler, block)` @0x4BF4E0 — GELESEN.</b>
    ///
    /// <code>
    /// for i = 1000*spieler + block;  i &lt; 1000*spieler + 1000;  i += 8
    ///     u = einheit[i]
    ///     if faze(u+0x09) != 0: weiter          ; nur lebende
    ///     if ukol(u+0x14) != 0: weiter          ; nur UNTAETIGE
    ///     c = u[+0x2c] (Sicht) ; b = u[+0x2b] (Reichweite)
    ///     lim = T[c+1] ;  k = (rand()%3 != 0) ? c : b
    ///     for s = T[k] .. lim-1:                ; T = Praefixzaehler ueber eine
    ///         x = u.x + OFF[s].dx               ;     entfernungssortierte
    ///         y = u.y + OFF[s].dy               ;     Versatzliste
    ///         v = belegung[(x&lt;&lt;8)|y] ; if v &gt;= 8000: weiter   ; kein Fahrzeug
    ///         if diplomatie[40*spieler + v/1000] != 0: weiter    ; verbuendet
    ///         if (v[+0x0a] &gt; 3) != (u[+0x0a] &gt; 3): weiter        ; andere Klasse
    ///         order(i, x, y, v, 0) ; break
    /// </code>
    ///
    /// <b>Das ist Punkt 4 der Fehlerliste.</b> Das Original kennt keine Welle:
    /// JEDE untaetige Einheit sieht selbst nach, ob in ihrem Ring ein Feind
    /// steht, und geht hin. Unser <see cref="AiFight"/> dagegen ruehrt sich erst,
    /// wenn <c>free.Count &gt;= waveSize + guard</c> beisammen ist, nimmt dann
    /// hoechstens die Haelfte und sortiert nach Entfernung zum Ziel — Infanterie
    /// ist langsam, steht darum weit hinten und kam nie an die Reihe. Sie blieb
    /// stehen. Der Durchlauf hier ersetzt die Welle nicht, er ergaenzt sie: er
    /// fasst nur an, was gerade nichts tut.
    ///
    /// <b>Was das Original NICHT ueber den Befehlsbus schickt:</b> `order`
    /// @0x410220 schreibt <c>ukol = 4</c>, das Zielfeld (+0x18/+0x19) und die
    /// Zieleinheit (+0x36) direkt in den Einheitensatz. Das deckt sich mit der
    /// Messung „0 von 140 Setzstellen des Busses liegen im KI-Bereich" — die KI
    /// benutzt den Bus nicht. Unser <see cref="AiSend"/> tut genau dasselbe:
    /// Pfad setzen, Ziel setzen, <c>Ordered</c> setzen.
    ///
    /// <b>UNSERE SETZUNGEN:</b> die Blockeinteilung laeuft ueber die Stelle in
    /// <see cref="ArmyOf"/> statt ueber den Einheitenplatz (wir haben keine
    /// 1000er-Bloecke je Spieler); der Sekundentakt oben; und die Drosselung
    /// <c>if (mission == 14) nur jeder 5. Aufruf</c>, die im Rumpf steht
    /// (<c>word[0x539934] == 14</c>), ist bewusst NICHT uebernommen — sie gilt
    /// nur einer einzigen Mission und wir kennen ihren Grund nicht.
    /// </summary>
    private void AiSweep(AiPlayer a, float dt)
    {
        a.Sweep -= dt;
        if (a.Sweep > 0f) return;
        a.Sweep += AiSweepSeconds / 8f;
        int block = a.Block;
        a.Block = (a.Block + 1) & 7;
        if (_nav == null) return;

        var army = ArmyOf(a.Player);
        for (int k = block; k < army.Count; k += 8)
        {
            int ui = army[k];
            if (ui == a.Grabber) continue;              // der geht zu einer Tuer
            var e = _entities[ui];
            if (e.DugIn) continue;
            // `ukol == 0`: nichts vor, nichts unterwegs, kein Ziel
            if (e.Path != null || e.Target >= 0 || e.Orders.Count > 0) continue;
            if (e.FuelMax > 0 && e.Fuel <= 0) continue;
            a.Looked++;
            int t = AiRingTarget(a, ui, e);
            if (t < 0) continue;
            AiSend(ui, t);
            a.Sent++;
            if (e.GameUnitType == AiInfantryClass) a.MovedInf.Add(ui);
        }
    }

    /// <summary>
    /// Der Ring, in dem eine untaetige Einheit nach einem Gegner sieht.
    ///
    /// Die Versatzliste selbst (0x79A008) und ihre Praefixtabelle (0x834A80)
    /// stehen in .bss, werden also zur Laufzeit gebaut — GELESEN ist, WIE sie
    /// benutzt werden: der normale Einheitentakt @0x40DDB0 scannt
    /// <c>OFF[0 .. T[+0x2b]-1]</c>, also alles innerhalb der WAFFENREICHWEITE;
    /// dieser KI-Durchlauf scannt <c>OFF[T[k] .. T[+0x2c+1]-1]</c> und faengt
    /// damit erst an, wo der normale Takt aufhoert. Zu zwei Dritteln ist
    /// <c>k = +0x2c</c> (nur der aeusserste Ring), zu einem Drittel
    /// <c>k = +0x2b</c> (das ganze Band von der Reichweite bis zur Sicht).
    /// Die Arbeitsteilung ist damit klar: der Takt schiesst auf alles in
    /// Reichweite, die KI holt heran, was zwischen Reichweite und Sicht steht.
    ///
    /// <b>UNSERE SETZUNG:</b> die Metrik. Wonach die Versatzliste sortiert ist,
    /// steht in keiner Datei — wir nehmen den euklidischen Abstand. Und weil das
    /// Original den ERSTEN Treffer der entfernungssortierten Liste nimmt, nehmen
    /// wir den naechstgelegenen.
    /// </summary>
    private int AiRingTarget(AiPlayer a, int ui, Entity e)
    {
        int near = e.Range > 0 ? e.Range : Mathf.RoundToInt(RangeOf(e));
        int far = e.Sight > near ? e.Sight : near + AiSightPad;
        bool wide = a.Roll(3) == 0;                 // rand() % 3 == 0
        float lo = wide ? near : far;
        float hi = far + 1;
        bool high = e.GameUnitType > AiClassSplit;

        int best = -1;
        float bestD = float.MaxValue;
        int r = Mathf.CeilToInt(hi);
        for (int dy = -r; dy <= r; dy++)
        for (int dx = -r; dx <= r; dx++)
        {
            float d = Mathf.Sqrt(dx * dx + dy * dy);
            if (d <= lo || d > hi || d >= bestD) continue;
            int c = e.Col + dx, w = e.Row + dy;
            if (!_nav!.InBounds(c, w)) continue;
            int oi = _nav.OccupantAt(c, w);
            if (oi < 0 || oi == ui || oi >= _entities.Count) continue;
            var o = _entities[oi];
            // `v >= 8000` heisst im Original "kein Eintrag der Einheitentabelle"
            if (o.Dead || o.IsProp || o.IsBuilding) continue;
            a.SawAny++;
            if (!AiHostile(a.Player, o.Owner)) continue;
            a.SawFoe++;
            if (a.ClassSeen.Count < 8) a.ClassSeen.Add($"{e.GameUnitType}->{o.GameUnitType}");
            if ((o.GameUnitType > AiClassSplit) != high) continue;
            a.SawClass++;
            bestD = d; best = oi;
        }
        return best;
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
    /// <summary>Herrenlos: die Kampagne setzt Spieler 7 in allen 33
    /// Matrizen neutral; die NET-Karten führen ihre neutralen Gebäude unter
    /// Eigner 11. Beide sind »herrenlos« und keine Verbündeten.</summary>
    private static bool Herrenlos(int owner) => owner == NeutralSlot || owner == NeutralOwner;

    private void AiGrab(AiPlayer a)
    {
        if (_nav == null) return;

        // is the standing job still a job?
        if (a.GrabTarget >= 0 && a.GrabTarget < _entities.Count)
        {
            var b = _entities[a.GrabTarget];
            // ebenso hier: fällt das Ziel an einen Verbündeten, ist der
            // Auftrag erledigt — sonst hängt die Einheit an einer Tür, die
            // sie nach der Regel oben gar nicht mehr ansteuern dürfte
            bool mine = Allied(b.Owner, a.Player);
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
            // ⚠ UNSERE SETZUNG (10.08.2026), und sie widerspricht nichts
            // Gelesenem: das EINNEHMEN selbst fragt im Original keine
            // Diplomatie — die Bündnismatrix wird EXE-weit 207 mal gelesen
            // (F: 205), davon 0 mal in der Einnahme-Routine, und der
            // Eignertest dort ist blanke Gleichheit @0x43CC21. Ein
            // Verbündeter NIMMT also ein, und das bleibt so.
            //
            // Aber `AiGrab` bildet nichts nach: die KI-Runde des Originals
            // hat 21 Teilaufgaben und **keine** heisst nach dem Besetzen; von
            // 26 Lesestellen der Türfelder +0x35/+0x36 liegen 2 im KI-Bereich
            // (0x4BD2ED, 0x4BDF18), und der einzige Matrixzugriff dort
            // (@0x4BD1B2) ist ein TON-Tor für den lokalen Spieler (Klang 122,
            // Spielerplatte 40 Byte) — keine Zielwahl. Wohin diese Routine
            // eine Einheit schickt, ist darum unsere Entscheidung, und sie
            // lautet: **einem Verbündeten nimmt man nichts weg.** Ohne das
            // fuhr der Computerspieler im Prüflauf zur Tür des Verbündeten
            // und holte sich dessen Fabrik zurück.
            if (b.Doors == 0 || b.Built == 0 || b.Owner == a.Player) continue;
            // ⚠ ABER NICHT den Neutralen: er ist in allen 33 Matrizen mit
            // JEDEM verbündet, und die neutralen Fabriken sind auf einer
            // Eroberungskarte der ganze Zweck dieser Routine. Ohne diese
            // Ausnahme hätte die Zeile darüber die KI stillgelegt, statt sie
            // höflich zu machen.
            if (Allied(b.Owner, a.Player) && !Herrenlos(b.Owner)) continue;
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
        // ⚠ OURS, und die zweite Haelfte von Punkt 4 der Fehlerliste. Hier stand
        // `Mathf.Max(waveSize, free.Count / 2)`: die Welle nahm hoechstens die
        // HAELFTE der freien Einheiten, und weil eine Zeile darueber nach
        // Entfernung zum Ziel sortiert wird, war es immer dieselbe Haelfte —
        // die hintere. Infanterie ist langsam, bleibt darum zurueck, steht beim
        // naechsten Aufruf wieder hinten und wurde nie mitgenommen. Sie blieb
        // buchstaeblich stehen. Es geht jetzt alles mit, was nicht Wache ist.
        int take = free.Count - guard;
        for (int k = 0; k < take; k++)
        {
            a.Wave.Add(free[k]);
            AiSend(free[k], target);
            if (_entities[free[k]].GameUnitType == AiInfantryClass) a.MovedInf.Add(free[k]);
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
        // Zweiter Riegel, unabhaengig von <see cref="ArmyOf"/>: ein
        // Ausruestungstraeger bekommt ueberhaupt kein Angriffsziel. `AiSend`
        // wird auch aus <see cref="AiMissionAttack"/> und <see cref="AiFight"/>
        // heraus gerufen; steht die Regel nur in `ArmyOf`, faellt sie beim
        // naechsten neuen Aufrufer wieder auf.
        if (AiUnarmed(e)) return;

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
    /// <summary>
    /// <b>Der Pruefstand fuer die Zeilenart 1 — und wofuer er blind ist.</b>
    ///
    /// Auf KEINER installierten Karte laeuft dieser Zweig von selbst, und zwar
    /// aus zwei voneinander unabhaengigen Gruenden, beide gezaehlt:
    /// <list type="number">
    /// <item>von den 572 Zeilen aller Baupläne in <c>mission_plans.json</c>
    /// sind 46 von der Zeilenart 1 — und alle 46 gehoeren den Missionen
    /// 17, 19, 22, 23, 24, 25, 27 und 34. Die installierten Missionen 1..15
    /// haben davon <b>null</b>;</item>
    /// <item>keine der Karten map_01..map_15 traegt ueberhaupt ein Gebaeude vom
    /// Typ 9. Flughaefen gibt es nur auf den beiden Gefechtskarten (1.DM: je
    /// einer fuer P0 und P1; 3.DM: P0, P2, P4) und auf den NET-Karten, dort
    /// samt und sonders neutral (Eigner 11).</item>
    /// </list>
    ///
    /// Darum bekommt <c>--demo-ai</c> auf einer Karte OHNE Bauprogramm hier ein
    /// Pruefprogramm gesetzt: je eine Zeilenart-1-Zeile mit genau den drei
    /// Entwuerfen, die in allen 46 echten Zeilen vorkommen (0 Jagdflieger,
    /// 1 Bomber, 4 Kampfhubschrauber). <b>Das Programm ist UNSERES</b>, die
    /// Zeilenform und die drei Zahlen sind es nicht.
    ///
    /// ⚠ <b>Was dieser Pruefstand sehen kann und was nicht.</b> Er sieht: den
    /// Verteiler nach Zeilenart, die Flughafensuche samt Zufallswahl, den
    /// Sources check gegen die drei Lager, den Hangar check, das Entstehen des
    /// sec19-Satzes und alle vier Ausgangszaehler. Er sieht NICHT: ob die
    /// Kosten je Spieler stimmen (sie kommen aus <see cref="AiAirCost"/>, nicht
    /// aus sec120), ob die Reihenfolge im echten Bauprogramm dieselbe Wirkung
    /// hat (kein installiertes Programm hat Zeilenart 1) und ob ein
    /// Nachschubhelikopter richtig sofort ausgesandt wuerde.
    /// </summary>
    private void AiAirHarness()
    {
        if (!_aiOn) return;
        bool arg = false;
        // siehe Core/CommandLine.cs — ohne »--« kommt sonst nichts an
        foreach (string s in Core.CommandLine.Args)
            if (s == "--demo-ai") arg = true;
        if (!arg) return;

        int n = 0;
        foreach (var a in _ai)
        {
            if (a.Plan is { Count: > 0 }) continue;      // ein echtes Programm bleibt
            a.Plan = new List<(int, int)> { (1, 0), (1, 1), (1, 4) };
            a.Pc = 0;
            n++;
        }
        if (n > 0)
            GD.Print($"demo-ai: PRUEFPROGRAMM (unseres) fuer {n} Spieler — " +
                     "je 3 Zeilen der Zeilenart 1 mit Entwurf 0/1/4, weil keine " +
                     "installierte Mission eine solche Zeile hat");
    }

    public Vector2? DebugDemoAi()
    {
        var players = new List<int>();
        for (int p = 0; p < 8; p++)
            if (AliveAsPlayer(p)) players.Add(p);
        if (players.Count < 2) { GD.Print("demo-ai: weniger als zwei Spieler auf der Karte"); return null; }
        EnableSkirmishAi(players, AiLevel.Hard);
        AiAirHarness();
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
