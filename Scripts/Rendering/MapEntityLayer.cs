namespace AkteEuropaReborn.Rendering;

using System.Collections.Generic;
using System.Linq;
using Godot;
using GDict = Godot.Collections.Dictionary<string, Godot.Variant>;

/// <summary>
/// Interactive overlay of a map's GAME ENTITIES (bases/units/defenses with owner
/// + hp, reverse-engineered from CWM section 5) and player-start MARKERS (sec4),
/// drawn on top of the baked map texture.
///
/// The 10000+ grid objects (trees/walls/crates) are static props baked into the
/// picture; the entities here are the ~dozens of real, owned, destroyable units.
///
/// Data source (dual, forward-compatible):
///   * PREFERRED: &lt;name&gt;.entities.json, resolved through
///     <see cref="Core.Content.Path"/> — the imported content under
///     user://data first, the development tree only as a fallback
///     (fields: col,row,owner,team,unit_type,category,hp,hp_max + markers[]).
///   * FALLBACK : the &lt;name&gt;.json 'tiles' object cells (props, owner unknown).
///
/// Efficiency: no per-object node; a flat list + one _Draw pass; hit-testing
/// iterates on click/hover only. Controls (routed from MapViewer): left-click =
/// select, hover = highlight, D = toggle dots/markers.
/// </summary>
[GlobalClass]
public partial class MapEntityLayer : Node2D
{
    public sealed class Entity
    {
        public int Slot, Col, Row, Owner, Team, UnitType, Category, Hp, HpMax, Elev;
        public int Mark = -1;      // record +0x43 — the campaign's handle on one unit
        /// <summary>Bis wann die Schusspose gezeigt wird — von Fire() gesetzt.
        /// UNSERE Zutat: das Original hat dafuer einen eigenen Zaehler, den wir
        /// nicht gelesen haben.</summary>
        public float FireUntil;
        public int Facing;         // 0-7, from the entity heading byte (+0x08)
        public int Weapon;         // weapon component id (record +0x0c); 0 = none

        /// <summary>Equipment row (record +0x10). The spawn routine @0x4b1b5c
        /// copies the design's third component (sec47 +0x19) into this byte, and
        /// the stats table names rows 65..88: Teleporter, Repair Device,
        /// Transporter, Shield, Kamikaze, Mirror, Illusion, Radar … So a unit is
        /// propulsion + weapon + EQUIPMENT — there is no body component, and the
        /// parts 70..76/100..101 once taken for bodies belong to unit_types
        /// 150..158, the ship hulls. Equipment carries no sprite; the draw code
        /// only special-cases 83 (Mirror) and 84 (Illusion) @0x4299b4, which put
        /// a second copy of the unit on the map.</summary>
        public int Equipment;

        /// <summary>Ammunition, record +0x39 / +0x3a. Max 0 = the unit carries
        /// no ammunition at all (infantry and unarmed vehicles) and fires
        /// without limit. One shot costs one round (@0x40c587) and firing is
        /// blocked at zero (@0x40bb44).</summary>
        public int Ammo, AmmoMax;

        /// <summary>Fuel tank, record +0x2e / +0x30. The move code counts it
        /// down and, at zero, stops the unit and prints "no fuel" (@0x407ab8);
        /// the Treibstoffheli and the Nachschub-Posten refill exactly this.
        /// It used to be read as hit points — hence the tank sizes of 250..1000
        /// that looked like generous health.</summary>
        public int Fuel, FuelMax;

        /// <summary>Attack (+0x27) and defence (+0x28) of this unit, the two
        /// values the hit routine @0x40c9a0 works with. Attack runs 1..15
        /// (infantry 1-2, vehicles 4..12, ships 5..11), defence is 0 on almost
        /// every placed unit.</summary>
        public int Attack, Defence;
        public Rect2 Footprint;
        public bool IsProp;        // true = generic grid prop (no owner/hp)
        public bool IsBuilding;    // true = a sec3 building (drawn into the map)
        public string Name = "";   // base name for buildings ("Bolougne")
        public int BType;          // building type (1-based into building_types.json)
        public int StockW, StockF, StockS;  // stored Waffen / Fahrwerk / Spezial parts
        public int Deposit = -1;            // Terranium left in the ground (sec28)
        public int DepositStart;            // what it held when the map was saved
        public int Grade;                   // deposit grade 0..6 (sec28 +0x0a)
        public int StockT;                  // Terranium stored in the building (+0x2e)
        public float EconTimer;             // seconds until the next economy tick
        public int ResearchTech;            // technology being researched, 0 = none
        public int ResearchDone;            // progress toward ResearchTotal

        // ---- building state (Step A) — all four from the map data ----
        public int State;                   // instance record +0x02, see StateName
        /// <summary>Record +0x0a. A freshly raised building starts at 100
        /// (<c>add_building</c> @0x4C8F4C writes 0x64); placed ones carry
        /// whatever the map gives them.</summary>
        public int Condition = 100;
        public int ProdSpeed;               // sec24 +0x05 Produktionsgeschwindigkeit
        public int Capacity;                // sec24 +0x08 Lagerplatz
        public int CostStore;               // sec24 +0x0a Lagerausbaukosten
        public int CostProd;                // sec24 +0x0c Produktionserweiterungskosten
        public int EffNum = 1, EffDen = 1;  // sec24 +0x03 / +0x04 production chance
        public int UpgradeStep;             // sec24 +0x06, counts 0..100
        public int Ticks;                   // this building's own tick counter
        public int ProdAccum;               // ticks banked toward the next part
        public List<int>? Hangar;           // sec19 slots parked here (Flughafen)
        public int HangarSize;              // sec27 +0x03, +2 per Erweiterung
        public int Shipyard = -1;           // Hafen (typ 11): the Schiffswerft
                                            // (typ 16) sec29 pairs it with
        // ---- being taken (see Simulation/Capture.cs for the whole reading) ----
        public int Built;                   // record +0x18, "is a real building"
        public int Doors;                   // record +0x34, the door count 0/1/2
        /// <summary>The doors as cell offsets, three bytes each from +0x35 in
        /// the record. Door 0 first — that is the one the original's capture
        /// block uses. See Import/CwmData.Building.DoorCells.</summary>
        public readonly List<(int Col, int Row)> DoorCells = new();
        public int DoorCol => DoorCells.Count > 0 ? DoorCells[0].Col : 0;
        public int DoorRow => DoorCells.Count > 0 ? DoorCells[0].Row : 0;
        public int CaptureTotal;            // +0x38, and it tracks the hit points
        public int CaptureProgress;         // +0x3a
        public int Intruder = -1;           // +0x3c, the player at the door
        public int ShownOwner = -1;         // +0x3d, flickers while it is taken

        public bool IsTarget;      // listed in the mission's win conditions
        public int Infantry = -1;  // ROBO.CWR infantry set, -1 = not a foot soldier

        /// <summary>Cells the unit really covers, from the imap. 1x1 for almost
        /// everything; the map_01 ships are 2x2. See BodyCenter.</summary>
        public int FootW = 1, FootH = 1;

        /// <summary>Record +0x0b, the game's own SPODEK — the chassis. The voice
        /// routine @0x429290 keys its eleven-way switch on (this &gt;&gt; 1) - 1.</summary>
        public int Chassis = -1;

        /// <summary>Record +0x0a. The same routine switches on it first: 0, 1
        /// and 3 have their own sets, everything else falls to one line.</summary>
        public int Subclass = -1;

        /// <summary>Record +0x11 — the chassis' POSE GROUP.
        ///
        /// The draw code builds the hull's frame as
        ///     base_frame[chassis] + facing + slope_block + (this &amp; 7) * 48
        /// (`imul ax, ax, 0x30` @0x429b3b, used @0x429fc3), and 0xFF means
        /// group 0 (@0x429b1b). The parts that own more than one group are the
        /// interesting ones: Läufer eight, Abwehrstellung eight, Spinne and
        /// Kugelroller three, Schwere Ketten two.
        ///
        /// ⚠ MEASURED, and it corrects the guess this session started with.
        /// The 59 Läufer on the maps are spread over ALL EIGHT groups, and the
        /// remake drew group 0 for every one of them. So the walkers were not
        /// only stiff, they were mostly the wrong picture.
        ///
        /// ⚠ CORRECTED 10.08.2026 — the field is the GAIT, and the game says so
        /// itself. Its debug dump @0x41763f prints it as <b>`ANIM_SPODEK:`</b>
        /// (string @0x4f77a8 / F: @0x4f6784) — "animation of the chassis". The
        /// pictures agree: rendered out, the eight groups of the Läufer are one
        /// stride (leg forward → planted → trailing), the three of the Spinne a
        /// leg shuffle, the two of the Schweren Ketten a tread cycle. And the
        /// creation code sets the field from the PART TABLE'S GROUP COUNT —
        /// @0x4c3584 / @0x4b1c63 / @0x4b37dc all read `byte[chassis*4 +
        /// 0x77c871]`, the high byte of the part's first u16, and write 0xFF
        /// when it is &lt;= 1 and 0 otherwise. Measured against the maps: all
        /// 907 placed units of a one-group chassis carry 0xFF, all 453 of a
        /// multi-group chassis a group its chassis really owns — 1360 of 1360,
        /// no counter-example. (That also retracts the 08.06. note "Reifen carry
        /// +0x11 = 1..7"; they carry 0xFF.)
        ///
        /// ⚠ What stays true: <b>nothing advances it while a vehicle drives.</b>
        /// The steppers all sit in "move units" @0x406cd0 behind subclass
        /// (+0x0a) == 1 — the infantry — or behind the Abwehrstellung's
        /// deploy task (@0x409855/@0x4098a4, `[+0x11] = [+0x15]` counting 0..7
        /// with the facing pinned to 7). 34 absolute and 20 register-base
        /// accesses on EACH of the two GAME.EXE, same set, no other writer. So
        /// the frames are the game's, the cadence is OURS — see
        /// <see cref="HullGaitFps"/>.</summary>
        public int Pose;

        /// <summary>Record +0x28, and it is NOT the fuel tank — that is +0x2e.
        /// This one is <b>0 on 2847 of the 2863 units the maps carry</b>, so it
        /// is a runtime field rather than a stat; the damage arithmetic
        /// @0x40cd90 reads it beside +0x27. The voice routine compares it with
        /// 50 and takes a different set above. Carried unnamed.</summary>
        public int Field28;
        public string Combo => $"{UnitType}_{Weapon}";

        // ---- live movement state (Step E) ----
        public Vector2 Pos;              // ground point in map pixels (smooth)
        public bool Mobile;              // has a propulsion that can actually drive
        /// <summary>Which branch of Can_go @0x4055D0 this unit takes — ship,
        /// hover, walker or ordinary vehicle. Picked from +0x0a and +0x0b, the
        /// way the original picks it.</summary>
        public Simulation.NavGrid.MoveClass Move;
        public List<Vector2I>? Path;     // remaining waypoint cells
        public int PathIdx;
        public Vector2I Goal;            // final target cell (for re-pathing)
        public Vector2I? Reserved;       // cell currently being driven into
        public float WaitTime;           // blocked-by-another-unit timer

        /// <summary>What this unit still has to do after the current order.
        /// OURS — the original takes one order at a time; see
        /// <see cref="MaxOrders"/>.</summary>
        public readonly List<Order> Orders = new();

        /// <summary>Weapon range in tiles — entity +0x2b, which the original's
        /// unit panel prints as "Reichw.". It is a property of the UNIT, not of
        /// its weapon: the design sums it over propulsion, weapon and equipment,
        /// so two vehicles with the same gun can reach differently far. Held
        /// against the weapon table over the shipped maps, it tracks the weapon's
        /// range_raw monotonically (50 -> 4, 70 -> 8, 80 -> 9, 90 -> 11) and
        /// spreads around it, which is the equipment's doing.
        /// 0 = not known, fall back to the weapon's own range.</summary>
        public int Range;

        /// <summary>Sight in tiles — entity +0x2c, the panel's "Sicht". Carried
        /// for the panel; there is no fog of war here to spend it on. It streams
        /// wider than the range does per weapon, which is the radar equipment
        /// adding to it.</summary>
        public int Sight;

        /// <summary>Speed — entity +0x20 (u16), which the unit panel @0x474fe0
        /// labels "Geschw.".
        ///
        /// Two independent readings agree, which is what settled it after it had
        /// been left unnamed twice: the panel's label order — checkable there,
        /// because "Munition " is followed by the known +0x39/+0x3a — and the
        /// data, where over 1863 units it tracks the chassis' own speed_raw
        /// monotonically across all 16 chassis types. The hit routine writing it
        /// to 2 now reads as what it plainly is: a unit slowed when struck.
        /// </summary>
        public int Speed;

        // ---- combat state (Step B) ----
        public int AimFacing = -1;       // turret facing (-1 = follow the hull)
        public int Target = -1;          // entity index being attacked, -1 = none
        public bool Ordered;             // true = the player ordered this attack
        public bool DugIn;               // "Eingraben" — holds position, harder to kill
        public float BuildTime;          // seconds left on the current build
        public int BuildIndex;           // design being built
        public int MenuIndex;            // pick in this factory's own menu
                                         // (auto-acquired targets are never chased)
        public float Cooldown;           // seconds until the weapon can fire again

        /// <summary>Reload time in the game's own units — entity +0x3d, which
        /// the unit panel prints as "Nachladen". Light weapons sit at 20, the
        /// Schw.Raketenwerfer at 120. 0 = not known, use the flat fallback.
        /// </summary>
        public int Reload;
        public bool Dead;
        public float DeadTime;           // seconds since destruction (wreck anim)
    }

    /// <summary>
    /// A rocket in flight. ANIM.CWA sequences 64/65 hold them in 8 facings, so a
    /// projectile just draws the sprite for its direction. Damage lands on
    /// IMPACT, not on firing — if the target dies or drives off in the meantime
    /// the rocket flies on to the point it was aimed at and detonates there.
    /// </summary>
    private struct Projectile
    {
        public Vector2 Pos;
        public Vector2 Aim;      // where it is heading (updated while the target lives)
        public int Target;       // entity index, -1 once the target is gone
        public int Shooter;
        public int Damage;
        public int Facing;
        public string Kind;      // "rocket_l" or "rocket_h"
        public float Speed;
    }

    /// <summary>One sec19 record — an AIRCRAFT. The section holds 200 x 68 of
    /// them; +0x08 is the kind and +0x3b the map's English name.
    ///
    /// CORRECTED: "Ammo" and "Fuel" are not crates lying around, they are the
    /// Munitionheli and the Treibstoffheli — they carry hp, speed, sight and an
    /// airframe like every other aircraft, and their payload component is
    /// literally "Munition" / "Treibstoff", which is what they deliver. Their
    /// German names come from the template table the spawn routine copies from:
    /// Shark = Jagdflieger, Whale = Bomber, Fight = Kampfhubschrauber.
    ///
    /// A record reads (0,0) exactly while the aircraft is parked in a hangar.</summary>
    public sealed class Special
    {
        public int Slot, Col, Row, Kind;
        public string Name = "";
        public bool Stored;
        public Rect2 Footprint;
        // instance values straight out of the record
        public int Speed, Hp, HpMax, Ammo, AmmoMax, Fuel, FuelMax;
        public int Payload, Airframe, Attack, Defence, Sight;

        /// <summary>What is left of the payload (record +0x31). A full top-up
        /// of one customer costs 50, a Nachschub-Posten refills it to 255.</summary>
        public int Cargo;

        /// <summary>The entity this supply helicopter is serving (record +0x2e,
        /// 0xFFFF = none), and the building it is flying to for a refill.</summary>
        public int Customer = -1;
        public int DepotSlot = -1;
        public float FuelFrac;       // sub-tile remainder of the fuel burn

        /// <summary>The aircraft KIND, and what each one is.
        ///
        /// <b>Settled 2026-08-01 from the template table itself.</b> The eight
        /// records at 0x51b021 carry their own name AND their kind number at
        /// <b>+0x2d</b>:
        ///
        ///     1 Jagdflieger   2 Bomber   3 Spionageflieger   10 Kampfhubschrauber
        ///     11 Mechanikerheli   12 Transport Heli   13 Treibstoffheli   14 Munitionheli
        ///
        /// The two already known from the other side — 13 tops a unit up, 14
        /// refills its ammunition — sit in that column and match, which is what
        /// makes the other six readable rather than merely ordered. So kind 11
        /// is the Mechanikerheli and kind 12 the Transport, and the earlier note
        /// that "kind 15 is the Mechanikerheli" is withdrawn: there is no kind
        /// 15 in the table at all.
        ///
        /// <para><b>Which kinds the original actually handles</b>, read off the
        /// switch @0x422f8e — note the `dec ecx` before the bounds check, so the
        /// jump index is kind−1:</para>
        /// <code>
        ///    1 Jagdflieger        -> 0x422fa7      2 Bomber          -> 0x423081
        ///   10 Kampfhubschrauber  -> 0x4230c9     13 Treibstoffheli  -> 0x42313c
        ///   14 Munitionheli       -> 0x42327f
        ///    3 Spionageflieger, 11 Mechanikerheli, 12 Transport  -> the default
        /// </code>
        /// <para>So the Kampfhubschrauber DOES have its own branch; the three
        /// without one are the scout, the mechanic and the transport. An earlier
        /// note here said only 13 and 14 were handled — that was off by the
        /// `dec` and is withdrawn.</para></summary>
        public bool IsSupply => Kind is 13 or 14;

        /// <summary>The template's own name for this kind.</summary>
        public string KindName => Kind switch
        {
            1 => "Jagdflieger", 2 => "Bomber", 3 => "Spionageflieger",
            10 => "Kampfhubschrauber", 11 => "Mechanikerheli", 12 => "Transport Heli",
            13 => "Treibstoffheli", 14 => "Munitionheli",
            _ => $"Art {Kind}",
        };

        // live flight state (ours)
        public Vector2 Pos;
        public Vector2? Goal;        // where it is flying to
        public int Facing;
        public int Owner = -1;       // the Flughafen it belongs to, once known
        public int HomeSlot = -1;    // that airport's building slot
        public int Target = -1;      // entity being attacked
        public float Cooldown;
        public bool Dead;
        public string TypeName = "";

        public bool Flying => !Stored && !Dead;
        public bool Armed => Attack > 0 && AmmoMax > 0;
    }

    /// <summary>One playing ANIM.CWA effect (muzzle flash, explosion, wreck).</summary>
    private struct Effect
    {
        public Vector2 Pos;
        public string Kind;
        public float Time;
        public float FrameTime;
        public bool Hold;                // freeze on the last frame (scorch mark)
        public int Variant;              // wreck only: which rubble picture (0..2)
    }

    /// <summary>
    /// Initial facing of a placed unit. CAUTION: record +0x08 is NOT a heading —
    /// the game's own debug dump (`nr:%d typ:%d faze:%d ukol:%d prod:%d
    /// energie:%d reload:%d spodek:%d akce:%d x:%d y:%d` @0x4f7350, printed from
    /// the entity loop @0x413759) names it **energie**. No heading field could be
    /// identified in the 78-byte record, so this byte is used only as a stable
    /// arbitrary seed to keep the starting orientations varied; once a unit
    /// moves, its facing comes from the direction of travel.
    /// </summary>
    private static int SeedFacing(int b) => Mathf.Clamp(b * 8 / 181, 0, 7);

    /// <summary>
    /// Screen movement direction -> sprite facing. Read off the chassis strips:
    /// f0/f4 show the vehicle axis vertical, f2/f6 horizontal, the rest diagonal,
    /// stepping 45 deg per index. Taking f0 = south (front toward the viewer) gives
    /// f = ((angle - 90 deg) / 45 deg) mod 8. The axis is certain; the 180 deg
    /// front/back sense is read off the sprite art, not disassembly-confirmed.
    /// </summary>
    public static int DirToFacing(Vector2 d)
    {
        if (d.LengthSquared() < 0.0001f) return DefaultFacing;
        float ang = Mathf.RadToDeg(Mathf.Atan2(d.Y, d.X));   // 0 = right, 90 = down
        int f = Mathf.RoundToInt((ang - 90f) / 45f);
        return ((f % 8) + 8) % 8;
    }

    /// <summary>Propulsion types that cannot drive (scenery + fixed defenses).</summary>
    // 148/149 used to be in here: they carry no propulsion component, so they
    // looked immobile.  They are the FOOT SOLDIERS — they walk on their own legs.
    private static readonly HashSet<int> ImmobileTypes = new() { 171, 172 };

    /// <summary>
    /// Water-borne unit types. Read off the original placements rather than the
    /// names: across all 23 maps every entity of type 150..153 sits on a class-0
    /// (water) cell — 45 of 45 — while every other type is placed on land. These
    /// are the blank-named "hull chassis" rows of the stats table, i.e. the ships.
    /// (Luftkissen/166 sounds amphibious but is only ever placed on land.)
    /// </summary>
    /// CORRECTED 2026-07-30: the set stopped at 153 because only those four
    /// occur as placed units. The SHIP_PROD table settles the range — its ten
    /// designs use chassis 150,151,152,153,154,155,156,151,157,158, so every
    /// one of 150..158 is a ship hull. 157 (Sea Cruiser / Schlachtshiff) and
    /// 158 (Battle Ship / Kreuzer) do occur on the maps: twenty of them were
    /// driving on land in our simulation. 154..156 (Submarine, Fuel Ship,
    /// Ammo Ship) appear in no map and can only ever come out of a Werft.
    private static readonly HashSet<int> NavalTypes =
        new() { 150, 151, 152, 153, 154, 155, 156, 157, 158 };

    public struct Marker
    {
        public int Col, Row, Type;
        public Rect2 Footprint;
    }

    private const int TileW = 40;
    private const int TileH = 20;

    /// <summary>How far the ground of a cell sits below the top of the tile
    /// sprite that draws it. The baker blits a tile at
    /// `origin_y + row*TileH - elev*15 - 50`, so the 20 px the cell actually
    /// occupies begin 50 px further down.</summary>
    private const int GroundLift = 50;

    // Faction colors by owner 0..7 (neutral placeholder palette, high-contrast).
    private static readonly Color[] Factions =
    {
        new(0.30f, 0.65f, 1.00f), // 0 blue
        new(1.00f, 0.35f, 0.30f), // 1 red
        new(0.40f, 0.90f, 0.45f), // 2 green
        new(1.00f, 0.85f, 0.25f), // 3 yellow
        new(0.80f, 0.45f, 1.00f), // 4 purple
        new(1.00f, 0.60f, 0.20f), // 5 orange
        new(0.30f, 0.90f, 0.90f), // 6 cyan
        new(1.00f, 0.45f, 0.80f), // 7 pink
    };
    /// <summary>The faction colours, for anything that has to agree with what is
    /// drawn on the map — the overview map above all, where a side being a
    /// different colour than on the battlefield would be worse than useless.</summary>
    public static Color FactionColor(int owner) => Factions[Mathf.PosMod(owner, Factions.Length)];

    private static readonly Color PropColor = new(0.75f, 0.75f, 0.80f, 0.8f);
    private static readonly Color MarkerColor = new(1f, 1f, 1f, 0.95f);

    // Zone (sec2) overlay colors, indexed by value 0..3 (0 = transparent).
    private static readonly Color[] ZoneColors =
    {
        new(0, 0, 0, 0),               // 0 none
        new(1f, 0.90f, 0f, 0.43f),     // 1 shore/edge (yellow)
        new(0f, 0.78f, 1f, 0.43f),     // 2 water/navigable (cyan)
        new(1f, 0.16f, 0.16f, 0.55f),  // 3 special (red)
    };

    private readonly List<Entity> _entities = new();
    private readonly List<Marker> _markers = new();
    private readonly List<Special> _special = new();
    private readonly HashSet<int> _targetSlots = new();

    // Building type names, from GAME.EXE @VA 0x4fdcc4 (16 x 20 bytes). The map's
    // `typ` is 1-based into that table — proven by the door count: the three
    // factories (typ 2,3,4) all have 2 doors, the HQ (typ 1) has 1.
    private static Dictionary<int, string>? _bldNames;

    /// <summary>
    /// Hit points of a building. NOT in the game data — sec3 carries no hp field
    /// and buildings have no entity record; the w/ch/sp fields vary per instance
    /// (stored goods), so they are not stats. These values are ours, chosen so a
    /// headquarters outlasts a radar mast.
    /// </summary>
    /// <summary>
    /// Hit points of a building type, from the game's own 10-byte stat row
    /// (<c>building_types.json</c>, field <c>hp</c>) — <b>Basis 1200,
    /// Spezial-Fabrik 800, everything else 1000</b>, and <b>700</b> for any
    /// type from 17 up, which is the value <c>add_building</c> @0x4C8F1B writes
    /// when the table has no row.
    ///
    /// <para>⚠ CORRECTED 07.08.2026. This used to be a table of OURS —
    /// 800 for the three factories, 700 for Generator and Feldbahnhof, 300 for
    /// the Radarstellung, 500 for the rest — and not one of those numbers was
    /// the game's. It only ever showed when a map record carried no hp_max of
    /// its own, which is why it survived so long. The real table is checked
    /// against sec3 on every run: 684 buildings, 0 disagreements.</para>
    /// </summary>
    private static int BuildingHp(int type)
    {
        LoadBuildingNames();
        return _bldHp != null && _bldHp.TryGetValue(type, out int hp) && hp > 0
               ? hp : Import.ExeTables.BuildingHpDefault;
    }
    private int _selected = -1;
    private int _hovered = -1;
    private bool _showDots;
    private bool _showZones;
    private bool _showBuildings;
    private string _source = "";

    // ---- Step E: navigation + commands ----
    private Simulation.NavGrid? _nav;
    private ImageTexture? _navTex;
    private Rect2 _navRect;
    private bool _showNav;
    private readonly HashSet<int> _sel = new();
    private Rect2? _band;                 // live rubber-band rectangle (map coords)
    private int _ox, _oy;                 // map pixel origin
    private Dictionary<(int, int), int> _elevLookup = new();
    private Dictionary<(int, int), int>? _flagLookup;
    // Per-unit movement speed, from the component stats table (unit_catalog.json
    // `speed_raw`, record[unit_type-1][+0x30] — the same tail off-by-one as the
    // component id). Raw values: immobile 2, drivable 4..14. The pixels-per-raw
    // conversion is ours; the ORDER between units is the game's.
    private const float PxPerSpeedUnit = 6f;
    private const float MoveSpeed = 55f;  // fallback when a type has no entry
    private string _order = "";           // last order feedback for the HUD

    /// <summary>Eine Meldung in die Statuszeile stellen. Bisher schrieb nur
    /// diese Klasse selbst hinein; der Cheat-Mode sitzt aber im MapViewer
    /// und muss sich melden koennen, damit ein Schummel nie still laeuft.</summary>
    public void Say(string text) => _order = text;

    // ---- Step B: combat ----
    // Weapon stats recovered from the GAME.EXE component table (weapons.json):
    // damage +0x04, range +0x06.
    //
    // RELOAD, corrected 2026-08-01: it IS in the data. The unit panel @0x474fe0
    // prints "Nachladen" and reads entity +0x3d right after — the same shape
    // that gives "Energie :" its +0x08 and +0x29, which are known independently,
    // so the label order is checkable there. Design +0x2b feeds it.
    //
    // The values behave exactly like a reload: 2x Maschinengewehr 20, Bordkanone
    // 20, Flak 20, L.Raketenwerfer 100, Schw.Raketenwerfer 120 — light weapons
    // fire six times as often as heavy ones, where this used to be a flat 1.1 s
    // for everything.
    //
    // What stays OURS is only the scale: how long one of those units is in
    // seconds is not known, so ReloadTick is chosen to put the common value of
    // 20 at the 1.1 s this ran on before. The RELATIVE rates are the game's.
    private const float FireInterval = 1.1f;      // fallback, units without a value
    private const float ReloadTick = 1.1f / 20f;  // seconds per reload unit — OURS

    /// <summary>Seconds between two shots for this unit: its own reload value
    /// where it has one, the old flat interval where it does not.</summary>
    private static float ReloadOf(Entity e)
        => e.Reload > 0 ? e.Reload * ReloadTick : FireInterval;
    // ⚠ There is deliberately NO default weapon component any more — see WeaponOf.
    private static Dictionary<int, (string Name, int Damage, float RangeTiles)>? _weapons;
    private readonly List<Effect> _effects = new();
    private readonly Dictionary<string, List<Texture2D>> _fx = new();
    private readonly Dictionary<string, Vector2> _fxAnchor = new();
    private readonly List<(Vector2 A, Vector2 B, float T)> _tracers = new();
    private readonly List<Projectile> _shots = new();
    private float _acquireTimer;
    private bool _showRanges = true;

    // Zone overlay: one small W×H texture stretched over the map (elevation
    // ignored — a coarse gameplay layer). Kept as a single draw call so it scales
    // to 254×254 maps without thousands of rects.
    private ImageTexture? _zoneTex;
    private Rect2 _zoneRect;

    // ---- fog of war ---------------------------------------------------------

    private Simulation.FogGrid? _fog;
    private Rect2 _fogRect;
    private ImageTexture? _fogTex;
    private int _fogDrawn = -1;
    private float _fogTick;

    /// <summary>The original runs its "unexplored" step on every fifth tick
    /// (@0x41678c: `[0x4fa240] % 5 == 1`). At the 25 ticks a second the movies
    /// run at, that is a fifth of a second — the interval is the game's, the
    /// seconds are ours.</summary>
    private const float FogEverySec = 5f / 25f;

    /// <summary>What a cell that has been seen but is not watched looks like,
    /// and what one never seen looks like. OURS: the original keeps two arrays
    /// and this keeps three states, so it can dim what a unit walked away
    /// from.</summary>
    private static readonly Color FogSeen = new(0, 0, 0, 0.45f);
    private static readonly Color FogUnseen = new(0, 0, 0, 1f);

    /// <summary>`--fog` turns the fog on for THIS run without touching the
    /// player's setting — the harness needs it on to show that the overview map
    /// obeys it, and the setting on this machine has it off.</summary>
    public static bool ForceFog;

    public bool FogActive => _fog != null && (ForceFog || UI.Settings.FogOfWar);

    /// <summary>Can the player see this cell right now?</summary>
    private bool Watched(int col, int row)
        => !FogActive || _fog!.IsWatched(col, row);

    /// <summary>Rebuilds the visibility from everything the view player owns.
    /// The original stamps from its units the same way; which entities count is
    /// ours only in that a dead one does not.</summary>
    private void UpdateFog()
    {
        if (_fog == null) return;
        // through FogActive, not the setting: otherwise a run with `--fog` would
        // filter the dots against a grid that RevealAll had just wiped
        if (!FogActive) { _fog.RevealAll(); return; }
        _fog.Update(Watchers());
    }

    /// <summary>
    /// How many cells a building type covers, from the tileset patterns.
    ///
    /// <para>Measured on tileset 11: the Basis is 7x6, the three factories 8x5,
    /// the Kraftwerk 5x6. Falls back to 5x4 when the patterns are not loaded —
    /// a size in the right order rather than the 1x1 that made a building
    /// almost unclickable.</para>
    /// </summary>
    public Vector2I BuildingFootprint(int bType)
    {
        if (_footprint.TryGetValue(bType, out var v)) return v;
        var size = new Vector2I(5, 4);
        var bt = Patterns?.GetBuildingType(bType) ?? default;
        if (Patterns != null && !bt.IsEmpty)
        {
            int w = 0, h = 0;
            for (int x = 0; x < Import.CwpFile.PatternWidth; x++)
                for (int y = 0; y < Import.CwpFile.PatternHeight; y++)
                    if (Patterns.PatternTile(bt.FirstPattern, x, y) != 0)
                    { if (x + 1 > w) w = x + 1; if (y + 1 > h) h = y + 1; }
            if (w > 0 && h > 0) size = new Vector2I(w, h);
        }
        _footprint[bType] = size;
        return size;
    }

    /// <summary>
    /// The offset from a building's corner cell to the point it watches from.
    ///
    /// <para><b>The game has a table for this</b> and the fog update looks it up
    /// per type (@0x4206AB..0x4206D6, exported as <c>sight_col</c>/<c>sight_row</c>
    /// — see <see cref="Import.ExeTables.SightCentre"/>). Half the footprint,
    /// which is what stood here since 07.08., was close but ours: the original
    /// puts the Basis at (3,3), all three factories at (3,2), the Flughafen at
    /// (4,2) and the Mine at (4,3).</para>
    ///
    /// <para>Half the footprint stays as the fallback for a catalogue that
    /// predates the table, and for a type the table does not name.</para>
    /// </summary>
    private Vector2I BuildingHalfSpan(int bType)
    {
        if (_bldSight != null && _bldSight.TryGetValue(bType, out var s)) return s;
        var f = BuildingFootprint(bType);
        return new Vector2I(f.X / 2, f.Y / 2);
    }

    private readonly Dictionary<int, Vector2I> _footprint = new();

    private IEnumerable<(int Col, int Row, int Sight)> Watchers()
    {
        foreach (var e in _entities)
        {
            if (e.Dead || e.IsProp) continue;
            if (e.Owner != ViewPlayer) continue;
            if (!e.IsBuilding)
            {
                yield return (e.Col, e.Row, e.Sight > 0 ? e.Sight : 4);
                continue;
            }

            // A BUILDING watches from a point of its own, not from the corner
            // cell its record names, and it sees TEN cells however big it is.
            //
            // Both numbers are the original's, read 08.08.2026 out of the fog
            // update @0x4205B0: the radius is a literal `push 0xa` and the point
            // comes out of a per-type table (see BuildingHalfSpan). Until then
            // this was our own half-footprint with a radius of 6, which is what
            // made "buildings seem to block the reveal" — the far side of a 7x6
            // Basis fell outside its own circle. The fog blocks nothing;
            // FogGrid.Stamp only ever opens a circle.
            var half = BuildingHalfSpan(e.BType);
            int s = BuildingSightRadius;
            yield return (e.Col + half.X, e.Row + half.Y, s);
        }
    }

    /// <summary>The fog as a W x H texture drawn over the map, the same trick
    /// the zone overlay uses — one texture instead of a rectangle per cell.
    /// </summary>
    private byte[]? _fogPixels;

    private void BuildFogTexture()
    {
        if (_fog == null) return;
        int w = _fog.Width, h = _fog.Height;
        // a byte buffer, not SetPixel: NET05 is 254 x 254 and this runs five
        // times a second — 64k interop calls per rebuild would be paid for in
        // frame time, and the picture is only three distinct colours
        _fogPixels ??= new byte[w * h * 4];
        byte seen = (byte)(FogSeen.A * 255f), unseen = (byte)(FogUnseen.A * 255f);
        for (int r = 0, i = 0; r < h; r++)
            for (int c = 0; c < w; c++, i += 4)
            {
                _fogPixels[i] = 0; _fogPixels[i + 1] = 0; _fogPixels[i + 2] = 0;
                _fogPixels[i + 3] = _fog.At(c, r) switch
                {
                    Simulation.FogGrid.Watched => (byte)0,
                    Simulation.FogGrid.Seen => seen,
                    _ => unseen,
                };
            }
        var img = Image.CreateFromData(w, h, false, Image.Format.Rgba8, _fogPixels);
        if (_fogTex == null) _fogTex = ImageTexture.CreateFromImage(img);
        else _fogTex.Update(img);
        _fogDrawn = _fog.Version;
    }

    /// <summary>
    /// The fog texture for anyone else who wants to draw it — the minimap does,
    /// so that it can show the actively watched ground brighter than the merely
    /// remembered ground (Fehlerliste Punkt 23).
    ///
    /// <para>It is the same texture the map itself uses, one pixel per cell, so
    /// the overview cannot drift out of step with the battlefield: both read the
    /// same three states out of the same grid. Returns null when the fog is off,
    /// and then nothing is dimmed at all.</para>
    /// </summary>
    public Texture2D? FogTexture()
    {
        if (!FogActive || _fog == null) return null;
        if (_fogTex == null || _fogDrawn != _fog.Version) BuildFogTexture();
        return _fogTex;
    }

    /// <summary>For a scripted run, which cannot look at the screen.</summary>
    public string FogWatchLine()
    {
        if (_fog == null) return "fog: kein Gitter";
        if (!FogActive) return "fog: abgeschaltet";
        var (u, s, w) = _fog.Counts();
        int all = u + s + w;
        return $"fog: {w} beobachtet, {s} erkundet, {u} unbekannt von {all} Feldern " +
               $"({100f * w / all:0.0}% / {100f * s / all:0.0}% / {100f * u / all:0.0}%)";
    }

    private Label _panel = null!;

    // unit_type -> (tier, name) from unit_catalog.json, loaded once. `name` is
    // populated once the GAME.EXE unit-stats table is recovered; until then the
    // panel falls back to the tier label. Reading it here needs no code change.
    private static Dictionary<int, (string Tier, string Name)>? _catalog;
    private static readonly Dictionary<int, int> _speeds = new();

    /// <summary>Movement speed in px/s for a unit type (game data x our scale).</summary>
    private static float SpeedOf(int unitType)
        => _speeds.TryGetValue(unitType, out int raw) && raw > 0
            ? raw * PxPerSpeedUnit
            : MoveSpeed;

    /// <summary>How fast this unit drives. Its OWN value wins — entity +0x20,
    /// the panel's "Geschw.", which the design sums over chassis, weapon and
    /// equipment, so two vehicles on the same chassis need not be equally fast.
    /// The per-chassis figure from the stats table stands in where a unit
    /// carries none.</summary>
    private static float SpeedOf(Entity e)
        => e.Speed > 0 ? e.Speed * PxPerSpeedUnit : SpeedOf(e.UnitType);

    // Real unit sprites (ROBO.CWR RE): unit_type -> facing -> tight-bbox size.
    // Sprites are drawn instead of owner dots when available.
    private static Dictionary<int, Dictionary<int, (int W, int H, int Yoff)>>? _unitAnchors;
    private readonly Dictionary<(int, int), Texture2D?> _unitTex = new();
    private bool _drawSprites = true;
    private const int DefaultFacing = 2;

    public override void _Ready()
    {
        ZIndex = 100;
        TextureFilter = TextureFilterEnum.Nearest;
        LoadCatalog();
        LoadUnitIndex();
        LoadWeapons();
        LoadBuildingNames();
        LoadOrders();
        LoadDesigns();
        LoadTechs();
        var layer = new CanvasLayer { Layer = 3 };   // above the panel frame (2)
        AddChild(layer);
        _panel = new Label { Position = new Vector2(12, 96), Visible = false,
                             TextureFilter = TextureFilterEnum.Nearest };
        _panel.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0));
        _panel.AddThemeConstantOverride("outline_size", 6);
        layer.AddChild(_panel);
    }

    /// <summary>The buildable types of this map's tileset, or null when the
    /// content predates the pattern export. Without it nothing can be raised —
    /// the build-site test has no footprint to walk.</summary>
    public Import.BuildingPatterns? Patterns { get; private set; }

    /// <summary>MapBaker's own OriginY for this map — the number its Blit adds
    /// before <c>row*TileH</c>. NOT <c>_oy</c>, which is the ground origin
    /// (that one carries GroundLift on top). Stamping a building with the wrong
    /// one would put it at a different height than the baked ones.</summary>
    private int _originY;

    private void LoadPatterns(GDict meta)
    {
        Patterns = null;
        _originY = meta.TryGetValue("origin_y", out var oyv) &&
                   oyv.VariantType != Variant.Type.Nil ? oyv.AsInt32() : 0;
        if (!meta.TryGetValue("tileset", out var tv)) return;
        int ts = tv.AsInt32();
        string path = Core.Content.Path($"Buildings/tileset_{ts:00}.json");
        if (!FileAccess.FileExists(path)) return;
        using var f = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        if (f == null) return;
        var json = new Json();
        if (json.Parse(f.GetAsText()) != Error.Ok ||
            json.Data.VariantType != Variant.Type.Dictionary) return;
        Patterns = Import.BuildingPatterns.FromDict(json.Data.AsGodotDictionary());

        // and the pictures, so a raised building can be stamped into the map
        string tj = Core.Content.Path($"Buildings/tileset_{ts:00}_tiles.json");
        string tp = Core.Content.Path($"Buildings/tileset_{ts:00}_tiles.png");
        if (!FileAccess.FileExists(tj) || !FileAccess.FileExists(tp)) return;
        using var tf = FileAccess.Open(tj, FileAccess.ModeFlags.Read);
        if (tf == null) return;
        var tjson = new Json();
        if (tjson.Parse(tf.GetAsText()) != Error.Ok ||
            tjson.Data.VariantType != Variant.Type.Dictionary) return;
        // NOT ResourceLoader: the atlas lives under user:// and was never
        // imported, so Load<Texture2D> returns null there. Image.LoadFromFile
        // reads it straight off disk.
        var atlas = Image.LoadFromFile(ProjectSettings.GlobalizePath(tp));
        if (atlas == null)
            GD.PrintErr($"Bau-Atlas nicht lesbar: {tp}");
        Patterns.LoadAtlas(tjson.Data.AsGodotDictionary(), atlas);
    }

    public void Load(string name, GDict meta)
    {
        // Eine neue Karte, ein neuer Satz Meldungen: was der Spieler in der
        // VORIGEN Mission weggeklickt hat, darf die naechste nicht stummschalten.
        UI.HelpWindow.Forget();
        _entities.Clear();
        _ammoCap.Clear();
        LoadPatterns(meta);
        _markers.Clear();
        _targetSlots.Clear();
        _selected = -1;
        _hovered = -1;
        _zoneTex = null;
        _sel.Clear();
        _groups.Clear();
        _orderMarks.Clear();
        _band = null;
        _order = "";

        // ⚠ The origin is taken from the TILES, not from a metadata field.
        //
        // It used to be read from an "origin" array, which is what the Python
        // baker writes — the C# importer writes "origin_y" instead, so on
        // imported content the array was missing and the origin silently fell
        // back to 0. Every entity was then drawn `origin` pixels too far north:
        // on map_01 that is 115 px, nearly six rows, which put the campaign's
        // first tank in the middle of a lake. And neither field was right on its
        // own: the tiles are blitted at `origin_y + row*20 - elev*15 - 50`, so
        // the ground origin is `origin_y - 50` and the array carried the
        // unadjusted number.
        //
        // A tile knows where it was drawn, so the tiles are asked. Measured over
        // map_01: `sy - (row*20 - elev*15)` is 115 for all 3024 of them.
        //
        // ⚠ AND THAT WAS STILL 50 px SHORT (0.4.0). `sy` is the top of the tile
        // SPRITE, which is 50 px taller than the cell it stands on: the cell's
        // ground band runs from `sy+50` to `sy+70`. The layer used `sy` as the
        // top of the cell, so every entity, every overlay and the fog sat two
        // and a half rows north of the ground they belong to.
        //
        // Measured rather than reasoned. Every cell the imap calls water was
        // sampled in the baked picture and asked whether the pixel is blue:
        //   sample at sy+10 (what the old origin implied): map_01 91 of 475
        //     wrong, map_05 133 of 2123, map_08 341 of 4092
        //   sample at sy+60 (this origin):                 map_01 0 of 475,
        //     map_05 0 of 2123, map_08 4 of 4092
        // Inland the error was invisible; at the water's edge it is the
        // difference between a ship floating and a ship standing on the beach,
        // which is how it was found.
        // Which also settles what the two written fields mean: `origin_y` and
        // the Python baker's `origin[1]` ARE this number (map_01: 165), so the
        // "-50" that 0.3.1 applied to them is withdrawn — they were right and
        // the correction was the mistake. They are taken as they stand when the
        // tiles cannot be asked.
        int ox = 0, oy = TileOrigin(meta, out bool fromTiles) + GroundLift;
        if (!fromTiles)
        {
            if (meta.TryGetValue("origin", out var origin) &&
                origin.VariantType == Variant.Type.Array)
            {
                var oa = origin.AsGodotArray();
                if (oa.Count >= 2) { ox = oa[0].AsInt32(); oy = oa[1].AsInt32(); }
            }
            else if (meta.TryGetValue("origin_y", out var oyv) &&
                     oyv.VariantType != Variant.Type.Nil)
                oy = oyv.AsInt32();
        }
        _ox = ox; _oy = oy;
        _mission = (meta.TryGetValue("mission", out var mv) ? mv.AsString() : name).ToUpper();

        // The music. It was exported and playable all along, but nothing ever
        // started it outside the sound probe — which is why the game was
        // silent. Which piece belongs to which mission is OURS; see
        // Audio.MidiMusic.StartForMission.
        Audio.MidiMusic.StartForMission(UI.SkirmishSetup.CampaignMission);

        // elevation per cell (for correct vertical placement) from the map tiles.
        var elev = BuildElevLookup(meta);
        _elevLookup = elev;
        _flagLookup = BuildFlagLookup(meta);

        // walkability grid over the same tiles (props / cliffs / water fallback);
        // the real terrain classes are layered on from the sec2 zones below
        _nav = Simulation.NavGrid.Build(meta);
        _navRect = new Rect2(ox, oy, _nav.Width * TileW, _nav.Height * TileH);

        LoadShipDesigns(name);
        _shipsBuilt = 0;
        if (!LoadEntitiesJson(name, ox, oy, elev))
            LoadPropsFallback(meta, ox, oy);

        _navTex = _nav.BuildDebugTexture();
        SeedResearch();
        InitEntityMovement();

        QueueRedraw();
        UpdatePanel();
        var census = _nav.Census();
        GD.Print($"MapEntityLayer: {_entities.Count} entities, {_markers.Count} markers, " +
                 $"{_entities.FindAll(x => x.IsBuilding).Count} buildings ({_source}); " +
                 $"nav {_nav.Width}x{_nav.Height} " +
                 $"frei {census.Free} / grob {census.Rough} / wasser {census.Water} / " +
                 $"gesperrt {census.Blocked}" +
                 (_nav.HasTerrain ? $" [imap, {_nav.Inferred} abgeleitet]" : " [Kachelcode-Notbehelf]"));
    }

    /// <summary>Where the baked picture's row 0 sits, read off the tiles: a tile
    /// carries the `sy` it was drawn at, so `sy - (row*TileH - elev*ElevStep)`
    /// is the origin and it is the same for every tile of a map. Returns 0 with
    /// <paramref name="ok"/> false when the metadata has no tiles to ask.</summary>
    private static int TileOrigin(GDict meta, out bool ok)
    {
        ok = false;
        if (!meta.TryGetValue("tiles", out var tv) || tv.VariantType != Variant.Type.Array)
            return 0;
        foreach (var item in tv.AsGodotArray())
        {
            if (item.VariantType != Variant.Type.Dictionary) continue;
            var t = item.AsGodotDictionary<string, Variant>();
            if (!t.ContainsKey("sy")) return 0;              // an older bake
            ok = true;
            return GetI(t, "sy") - (GetI(t, "row") * TileH - GetI(t, "elev", 0) * 15);
        }
        return 0;
    }

    private static Dictionary<(int, int), int> BuildElevLookup(GDict meta)
        => TileField(meta, "elev");

    /// <summary>The tile's FLAG byte per cell — the fourth byte of the map
    /// record, which the game reads through 0x41d110 as the slope class a
    /// turret is mounted by.</summary>
    private static Dictionary<(int, int), int> BuildFlagLookup(GDict meta)
        => TileField(meta, "flag");

    private static Dictionary<(int, int), int> TileField(GDict meta, string field)
    {
        var map = new Dictionary<(int, int), int>();
        if (meta.TryGetValue("tiles", out var tv) && tv.VariantType == Variant.Type.Array)
        {
            foreach (var item in tv.AsGodotArray())
            {
                if (item.VariantType != Variant.Type.Dictionary) continue;
                var t = item.AsGodotDictionary<string, Variant>();
                map[(GetI(t, "col"), GetI(t, "row"))] = GetI(t, field, 0);
            }
        }
        return map;
    }

    private bool LoadEntitiesJson(string name, int ox, int oy, Dictionary<(int, int), int> elev)
    {
        string path = Core.Content.Path($"Maps/{name}.entities.json");
        if (!FileAccess.FileExists(path)) return false;
        // which root answered — the imported content or the development tree
        GD.Print($"entities: {path}");
        using var f = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        if (f == null) return false;
        var json = new Json();
        if (json.Parse(f.GetAsText()) != Error.Ok || json.Data.VariantType != Variant.Type.Dictionary)
            return false;
        var root = json.Data.AsGodotDictionary<string, Variant>();

        if (root.TryGetValue("entities", out var ev) && ev.VariantType == Variant.Type.Array)
        {
            foreach (var item in ev.AsGodotArray())
            {
                if (item.VariantType != Variant.Type.Dictionary) continue;
                var e = item.AsGodotDictionary<string, Variant>();
                int col = GetI(e, "col"), row = GetI(e, "row");
                int el = elev.TryGetValue((col, row), out var ee) ? ee : 0;
                string raw = e.TryGetValue("raw", out var rv) ? rv.AsString() : "";
                bool haveRaw = raw.Length >= 0x3a * 2;
                // CORRECTED: +0x02 is the FACING, already 0..7 — the infantry
                // draw @0x42a89c adds it straight to `base + block*8`, and the
                // owner turned out to be the slot block (slot/1000), which both
                // hostility tests (@0x409bde, @0x40d058) index the alliance
                // matrix with. The old heading-from-+0x08 conversion is gone.
                int facing = haveRaw ? HexByte(raw, 0x02) & 7 : DefaultFacing;
                _entities.Add(new Entity
                {
                    Slot = GetI(e, "slot", -1), Col = col, Row = row,
                    Owner = GetI(e, "owner", -1), Team = GetI(e, "team", -1),
                    // +0x43 ist die MISSIONSMARKE: damit zeigt die Kampagne auf
                    // EINE bestimmte Einheit (`find_unit` @0x4D0F20). Gegenprobe
                    // an den Karten: map_03 traegt genau eine 193 und Mission 3
                    // sucht 193, map_06 eine 194 und Mission 6 sucht 194 — quer
                    // durch fuenfzehn Missionen. Liegt hinter dem Fenster, das
                    // `haveRaw` prueft, darum die eigene Laengenpruefung.
                    Mark = raw.Length >= 0x44 * 2 ? HexByte(raw, 0x43) : -1,
                    UnitType = GetI(e, "unit_type", -1), Category = GetI(e, "category", -1),
                    // CORRECTED 2026-07-26 — a unit's LIFE is `energie`
                    // (+0x08, max +0x29): the hit routine @0x40c9a0 subtracts
                    // the damage from it and destroys the unit when the hit is
                    // at least what is left. What we used to call hp
                    // (+0x2e/+0x30) is the FUEL TANK: the move code counts it
                    // down and prints "no fuel" at zero (@0x407ab8), which is
                    // also what the Treibstoffheli and the Nachschub-Posten
                    // refill. See GAMESTATE_RE.md 3.93.
                    Hp = haveRaw ? HexByte(raw, 0x08) : GetI(e, "hp", -1),
                    HpMax = haveRaw ? HexByte(raw, 0x29) : GetI(e, "hp_max", -1),
                    Fuel = haveRaw ? Hex16(raw, 0x2e) : GetI(e, "hp", 0),
                    FuelMax = haveRaw ? Hex16(raw, 0x30) : GetI(e, "hp_max", 0),
                    // A/V = attack and defence, and they are +0x26 and +0x27.
                    //
                    // This moved twice before it settled. The damage arithmetic
                    // @0x40cd90 reads +0x27 and +0x28 — but through TWO
                    // different registers, so those are one field from the
                    // shooter and one from the victim, not an A/V pair. The
                    // attack itself is read earlier in the same routine, at
                    // @0x40cb7d, and that is +0x26.
                    //
                    // Two independent witnesses agree: the unit panel @0x474fe0
                    // prints "A/V " and then reads +0x26 and +0x27 (label before
                    // field there, checkable against "Munition " -> the known
                    // +0x39/+0x3a); and +0x26 tracks the weapon's damage across
                    // every map — MG 10 -> 6, Bordkanone 18 -> 7, Flak 20 -> 14,
                    // L.Raketenwerfer 50 -> 19, Schwerer 70 -> 23,
                    // Mittelstreckenrakete 255 -> 30.
                    Attack = haveRaw ? HexByte(raw, 0x26) : 0,
                    Defence = haveRaw ? HexByte(raw, 0x27) : 0,
                    Range = haveRaw ? HexByte(raw, 0x2b) : 0,
                    Sight = haveRaw ? HexByte(raw, 0x2c) : 0,
                    Reload = haveRaw ? HexByte(raw, 0x3d) : 0,
                    Speed = haveRaw ? Hex16(raw, 0x20) : 0,
                    Elev = el,
                    Facing = facing,
                    Equipment = haveRaw ? HexByte(raw, 0x10) : 0,
                    Ammo = haveRaw ? HexByte(raw, 0x39) : 0,
                    AmmoMax = haveRaw ? HexByte(raw, 0x3a) : 0,
                    Weapon = haveRaw ? HexByte(raw, 0x0c) : 0,
                    // +0x0b is the record's `spodek` (the game's own dump name).
                    // For the size classes 148/149 it is the INFANTRY set: the
                    // maps hold 921 of them and every value is even, 0..22,
                    // which lands exactly on the 24 sets of ROBO.CWR's aux table.
                    Infantry = haveRaw && GetI(e, "unit_type", -1) is 148 or 149
                        ? HexByte(raw, 0x0b) : -1,
                    // the two fields the voice routine @0x429290 keys on, plus
                    // the tank it tests against 50 — see Audio.GameSounds.Voice
                    Chassis = haveRaw ? HexByte(raw, 0x0b) : -1,
                    // 0xFF is the game's "no group" (@0x429b1b), and the draw
                    // code masks the rest to three bits (@0x429b37).
                    Pose = haveRaw && HexByte(raw, 0x11) != 0xFF ? HexByte(raw, 0x11) & 7 : 0,
                    Subclass = haveRaw ? HexByte(raw, 0x0a) : -1,
                    Field28 = haveRaw ? HexByte(raw, 0x28) : 0,
                    AimFacing = haveRaw ? HexByte(raw, 0x03) & 7 : -1,
                    FootW = System.Math.Max(1, GetI(e, "foot_w", 1)),
                    FootH = System.Math.Max(1, GetI(e, "foot_h", 1)),
                    Footprint = CellRect(ox, oy, col, row, el), IsProp = false,
                });
                var last = _entities[^1];
                if (last.Weapon > 0 && last.AmmoMax > 0)
                    _ammoCap[last.Weapon] = last.AmmoMax;   // capacity per weapon
            }
        }

        if (root.TryGetValue("markers", out var mv) && mv.VariantType == Variant.Type.Array)
        {
            foreach (var item in mv.AsGodotArray())
            {
                if (item.VariantType != Variant.Type.Dictionary) continue;
                var mk = item.AsGodotDictionary<string, Variant>();
                int col = GetI(mk, "col"), row = GetI(mk, "row");
                int el = elev.TryGetValue((col, row), out var ee) ? ee : 0;
                _markers.Add(new Marker { Col = col, Row = row, Type = GetI(mk, "type", -1),
                                          Footprint = CellRect(ox, oy, col, row, el) });
            }
        }

        if (root.TryGetValue("buildings", out var bdv) && bdv.VariantType == Variant.Type.Array)
        {
            foreach (var item in bdv.AsGodotArray())
            {
                if (item.VariantType != Variant.Type.Dictionary) continue;
                var bd = item.AsGodotDictionary<string, Variant>();
                int col = GetI(bd, "col"), row = GetI(bd, "row");
                int el = elev.TryGetValue((col, row), out var ee) ? ee : 0;
                int owner = GetI(bd, "owner", 255);
                int btype = GetI(bd, "type", -1);
                // records with owner 255 are map-script placeholders ("InitN"),
                // not buildings — the real ones carry a player and a type 1..16
                // 0xFF marks the "InitN" script placeholders. Players are 0..7;
                // owner 11 is by far the most common (477 buildings across all
                // files) and is not a player slot — neutral/civilian structures.
                bool real = owner != 255;
                // hp and hp_max now come out of the record itself (+0x02 / +0x12,
                // proven by the repair handler @0x43e070) instead of our table
                int hpMax = GetI(bd, "hp_max", 0);
                if (hpMax <= 0) hpMax = BuildingHp(btype);
                int hp = real ? GetI(bd, "hp", hpMax) : 0;
                if (!real) hpMax = 0;
                var bld = new Entity
                {
                    Slot = GetI(bd, "slot", -1), Col = col, Row = row,
                    Owner = real ? owner : -1, Team = real ? owner : -1,
                    UnitType = -1, Category = -1, Elev = el,
                    Hp = hp, HpMax = hpMax,
                    State = GetI(bd, "state"),
                    ProdSpeed = GetI(bd, "prod_speed"),
                    Capacity = GetI(bd, "capacity"),
                    CostStore = GetI(bd, "cost_store"),
                    CostProd = GetI(bd, "cost_prod"),
                    EffNum = Mathf.Max(1, GetI(bd, "eff_num", 1)),
                    EffDen = Mathf.Max(1, GetI(bd, "eff_den", 1)),
                    UpgradeStep = GetI(bd, "upgrade_step"),
                    IsBuilding = true, BType = btype,
                    // the capture fields; content exported before 2026-08-06
                    // carries no door, and CaptureDoorsMissing counts that
                    Built = GetI(bd, "built"), Doors = GetI(bd, "doors"),
                    ShownOwner = real ? owner : -1,
                    // w/ch/sp are the stored Waffen / Fahrwerk / Spezial parts —
                    // a Waffen-Fabrik only ever fills w, a Fahrwerk-Fabrik only
                    // ch, a Spezial-Fabrik only sp, and the Basis all three
                    StockW = GetI(bd, "w"), StockF = GetI(bd, "ch"), StockS = GetI(bd, "sp"),
                    StockT = GetI(bd, "terranium"),
                    Name = bd.TryGetValue("name", out var nv) ? nv.AsString() : "",
                    HangarSize = GetI(bd, "hangar_size"),
                    Shipyard = GetI(bd, "shipyard", -1),
                    Hangar = bd.TryGetValue("hangar", out var hgv) &&
                             hgv.VariantType == Variant.Type.Array
                        ? new List<int>(System.Linq.Enumerable.Select(
                              hgv.AsGodotArray(), v => v.AsInt32()))
                        : null,
                    Footprint = CellRect(ox, oy, col, row, el),
                };
                if (bd.TryGetValue("door_cells", out var dcv) &&
                    dcv.VariantType == Variant.Type.Array)
                    foreach (var dv in dcv.AsGodotArray())
                    {
                        if (dv.VariantType != Variant.Type.Dictionary) continue;
                        var dd = dv.AsGodotDictionary<string, Variant>();
                        bld.DoorCells.Add((GetI(dd, "col"), GetI(dd, "row")));
                    }
                else if (bld.Doors > 0)
                    // content exported between the first and the second reading
                    // of the door record: one pair, no list
                    bld.DoorCells.Add((GetI(bd, "door_col"), GetI(bd, "door_row")));

                // ⚠ CORRECTED 07.08.2026 — a building used to keep the default
                // 1x1 here, and BodyRect turns FootW/FootH into the CLICK area.
                // A Basis covers 7x6 cells, so its whole body was unclickable
                // except for one tile at its corner: reported as "das Anwahlfeld
                // ist immer links von den Gebaeuden ein kleines Feld". The size
                // comes from the tileset patterns, the same place the fog and
                // the doors read it from.
                var foot = BuildingFootprint(bld.BType);
                bld.FootW = foot.X;
                bld.FootH = foot.Y;
                _entities.Add(bld);
            }
        }

        // foot soldiers: the map file carries no hp and no weapon for them, so
        // both come from the design their sprite set points at
        LoadInfantryDesigns();
        foreach (var e in _entities)
        {
            if (e.Infantry < 0 || _infDesigns == null) continue;
            // their life is `energie` out of the record like every other unit
            // (15..35 for the two size classes) — the invented 60/90 is gone
            if (e.HpMax <= 0) { e.HpMax = e.UnitType == 149 ? InfHpHeavy : InfHpLight; e.Hp = e.HpMax; }
            e.Mobile = true;
            if (_infDesigns.TryGetValue(e.Infantry, out var des) && des.Damage > 0)
                e.Weapon = InfCompBase + des.WeaponRow;
        }

        // sec53: the player table and its alliance matrix
        _players.Clear();
        _haveAllies = false;
        _allied = new bool[8, 8];
        if (root.TryGetValue("players", out var plv) && plv.VariantType == Variant.Type.Array)
            foreach (var item in plv.AsGodotArray())
            {
                if (item.VariantType != Variant.Type.Dictionary) continue;
                var pd = item.AsGodotDictionary<string, Variant>();
                int pi = GetI(pd, "player", -1);
                if (pi is < 0 or > 7) continue;
                _players.Add(new Player
                {
                    Index = pi, Flag = GetI(pd, "flag"),
                    Name = pd.TryGetValue("name", out var pn) ? pn.AsString() : "",
                    Comment = pd.TryGetValue("comment", out var pc) ? pc.AsString() : "",
                    Human = pd.TryGetValue("human", out var ph) && ph.AsBool(),
                    Beaten = pd.TryGetValue("beaten", out var pb) && pb.AsBool(),
                    Kills = GetI(pd, "kills"), Losses = GetI(pd, "losses"),
                });
                if (!pd.TryGetValue("allies", out var av) ||
                    av.VariantType != Variant.Type.Array) continue;
                foreach (var a in av.AsGodotArray())
                {
                    int q = a.AsInt32();
                    if (q is >= 0 and <= 7) { _allied[pi, q] = true; _haveAllies = true; }
                }
            }

        // the rail network: sec33 nodes point back at their building, sec34
        // lines name the two nodes they join — together that is the graph the
        // game hauls goods along
        _rail.Clear();
        _railRoutes.Clear();
        _lineRoute.Clear();
        _linePiece.Clear();
        _railLines.Clear();
        _freightWagons.Clear();
        _bldBySlot.Clear();
        _railStart.Clear();
        _hasRail = false;
        var node2bld = new Dictionary<int, int>();
        if (root.TryGetValue("rail_nodes", out var rnv) && rnv.VariantType == Variant.Type.Array)
            foreach (var item in rnv.AsGodotArray())
            {
                if (item.VariantType != Variant.Type.Dictionary) continue;
                var nd = item.AsGodotDictionary<string, Variant>();
                node2bld[GetI(nd, "node", -1)] = GetI(nd, "building", -1);
            }
        if (root.TryGetValue("links", out var lkv) && lkv.VariantType == Variant.Type.Array)
            foreach (var item in lkv.AsGodotArray())
            {
                if (item.VariantType != Variant.Type.Dictionary) continue;
                var lk = item.AsGodotDictionary<string, Variant>();
                // Which buildings a line joins. The sec33 node (bud1/bud2) says
                // so directly and is right: checked against the route end points
                // it names the same building in 164 of 164 cases where both are
                // known — the old verdict that it "never" did was drawn before
                // the buildings themselves were read correctly. It also reaches
                // much further: 542 pairs against the 164 whose track happens to
                // end on a building. So the node leads and the end point fills
                // in where the node has none.
                int a = GetI(lk, "bud1", -1), b2 = GetI(lk, "bud2", -1);
                if (a < 0) a = GetI(lk, "end1", -1);
                if (b2 < 0) b2 = GetI(lk, "end2", -1);
                // the line's own route: `delka` direction codes on a half-tile
                // lattice, exported as (col,row) points. Walking them lands on
                // the stored end point for every line in every file, so this is
                // the track the game itself draws — not a straight connection.
                if (lk.TryGetValue("route", out var rtv) && rtv.VariantType == Variant.Type.Array)
                {
                    var pts = new List<Vector2>();
                    foreach (var pv in rtv.AsGodotArray())
                    {
                        if (pv.VariantType != Variant.Type.Array) continue;
                        var p = pv.AsGodotArray();
                        if (p.Count < 2) continue;
                        pts.Add(new Vector2((float)p[0].AsDouble(), (float)p[1].AsDouble()));
                    }
                    if (pts.Count > 1) _railRoutes.Add(pts);
                    // keep the route under its line number as well — a train
                    // runs on the line that carries its own number
                    int lineNo = GetI(lk, "slot", -1);
                    if (lineNo >= 0 && pts.Count > 1)
                    {
                        _lineRoute[lineNo] = pts;
                        var pcs = new List<int>();
                        if (lk.TryGetValue("pieces", out var pcv) &&
                            pcv.VariantType == Variant.Type.Array)
                            foreach (var q in pcv.AsGodotArray()) pcs.Add(q.AsInt32());
                        _linePiece[lineNo] = pcs;
                    }
                }
                if (a < 0 || b2 < 0 || a == b2) continue;
                // die Linie selbst, für das Bahnsystem (Simulation/RailFreight.cs).
                // ⚠ Die vier Warenschalter des Satzes (+0x08..+0x0b) werden hier
                // BEWUSST nicht gelesen: der Kartenlader überschreibt sie beim
                // Laden aus der Typmatrix @0x504128 (@0x41F2A2), die Datei-Werte
                // sind also tot. RailFreight rechnet sie nach.
                AddRailLine(GetI(lk, "slot", -1), a, b2, GetI(lk, "delka", 1));
                if (!_rail.TryGetValue(a, out var la)) _rail[a] = la = new List<int>();
                if (!_rail.TryGetValue(b2, out var lb)) _rail[b2] = lb = new List<int>();
                if (!la.Contains(b2)) la.Add(b2);
                if (!lb.Contains(a)) lb.Add(a);
                _hasRail = true;
            }

        // sec120: what each player may build in the air, with the game's own
        // English names (Shark Fighter, Whale Bomber, Duck Spy, Fuel Heli …)
        _airDesigns = null;
        if (root.TryGetValue("air_designs", out var adv) && adv.VariantType == Variant.Type.Array)
        {
            _airDesigns = new List<AirDesign>();
            foreach (var item in adv.AsGodotArray())
            {
                if (item.VariantType != Variant.Type.Dictionary) continue;
                var ad = item.AsGodotDictionary<string, Variant>();
                _airDesigns.Add(new AirDesign
                {
                    Player = GetI(ad, "player", -1),
                    Enable = GetI(ad, "enable") != 0,
                    Name = ad.TryGetValue("name", out var anv) ? anv.AsString() : "",
                    Speed = GetI(ad, "speed"), Hp = GetI(ad, "hp"),
                    Payload = GetI(ad, "payload"), Airframe = GetI(ad, "airframe"),
                    Attack = GetI(ad, "attack"), Defence = GetI(ad, "defence"),
                    Sight = GetI(ad, "sight"), Ammo = GetI(ad, "ammo"),
                    Fuel = GetI(ad, "fuel"),
                    CostW = GetI(ad, "cost_w"), CostF = GetI(ad, "cost_f"),
                    CostS = GetI(ad, "cost_s"),
                });
            }
            _airSource = "sec120";
        }
        FillCampaignAirDesigns();

        // sec44 + sec121: the trains, 60 x 4 wagons on the SPOJ lines
        LoadWagons(root);

        // sec19: aircraft (Shark / Whale / Fight) and supply crates
        // (Ammo / Fuel).  A crate always lies on the map; an aircraft sits at
        // (0,0) exactly while it is parked inside a hangar.
        _special.Clear();
        if (root.TryGetValue("special", out var spv) && spv.VariantType == Variant.Type.Array)
            foreach (var item in spv.AsGodotArray())
            {
                if (item.VariantType != Variant.Type.Dictionary) continue;
                var sp2 = item.AsGodotDictionary<string, Variant>();
                int col = GetI(sp2, "col"), row = GetI(sp2, "row");
                int el = elev.TryGetValue((col, row), out var se) ? se : 0;
                var sp = new Special
                {
                    Slot = GetI(sp2, "slot", -1), Col = col, Row = row, Kind = GetI(sp2, "kind"),
                    Name = sp2.TryGetValue("name", out var sn) ? sn.AsString() : "",
                    Stored = col == 0 && row == 0,
                    Speed = GetI(sp2, "speed"),
                    Hp = GetI(sp2, "hp"), HpMax = GetI(sp2, "hp_max"),
                    Ammo = GetI(sp2, "ammo"), AmmoMax = GetI(sp2, "ammo_max"),
                    Fuel = GetI(sp2, "fuel"), FuelMax = GetI(sp2, "fuel_max"),
                    Payload = GetI(sp2, "payload"), Airframe = GetI(sp2, "airframe"),
                    Attack = GetI(sp2, "attack"), Defence = GetI(sp2, "defence"),
                    Sight = GetI(sp2, "sight"),
                    // +0x09 is the owner: the customer search reads it and then
                    // scans only that player's entity block (@0x427990). Checked
                    // against the hangar lists: 27 of 27 parked aircraft agree
                    // with their airfield's owner, none disagree.
                    Owner = GetI(sp2, "owner", -1),
                    Cargo = GetI(sp2, "cargo"),
                    Footprint = CellRect(ox, oy, col, row, el),
                };
                sp.Pos = sp.Footprint.Position + new Vector2(TileW / 2f, TileH / 2f);
                LoadAircraft();
                if (_aircraft != null && _aircraft.TryGetValue(sp.Payload, out var tp))
                    sp.TypeName = tp;
                _special.Add(sp);
            }

        // the owner now comes out of the record itself (+0x09); the hangar lists
        // add the home field and put the parked ones on their airport
        foreach (var e in _entities)
        {
            if (!e.IsBuilding || e.BType != 9 || e.Hangar == null) continue;
            foreach (int s in e.Hangar)
            {
                var a = _special.Find(x => x.Slot == s);
                if (a == null) continue;
                a.Owner = e.Owner; a.HomeSlot = e.Slot;
                a.Pos = e.Pos; a.Col = e.Col; a.Row = e.Row; a.Stored = true;
            }
        }
        // an airborne aircraft has no hangar entry — give it the nearest airport
        // as its home field (ours; only the field, the owner is data)
        foreach (var a in _special)
        {
            if (a.HomeSlot >= 0) continue;
            Entity? best = null; float bd = float.MaxValue;
            foreach (var e in _entities)
            {
                if (!e.IsBuilding || e.BType != 9 || e.Owner is < 0 or > 7) continue;
                float dd = Mathf.Abs(e.Col - a.Col) + Mathf.Abs(e.Row - a.Row);
                if (dd < bd) { bd = dd; best = e; }
            }
            if (best == null) continue;
            if (a.Owner is < 0 or > 7) a.Owner = best.Owner;
            a.HomeSlot = best.Slot;
        }

        // Terranium deposits (sec28): each record names the building that sits
        // on it and how much raw material is left
        if (root.TryGetValue("money", out var mnv) && mnv.VariantType == Variant.Type.Array)
        {
            var ma = mnv.AsGodotArray();
            for (int i = 0; i < _money.Length && i < ma.Count; i++) _money[i] = ma[i].AsInt32();
        }

        if (root.TryGetValue("deposits", out var dpv) && dpv.VariantType == Variant.Type.Array)
        {
            foreach (var item in dpv.AsGodotArray())
            {
                if (item.VariantType != Variant.Type.Dictionary) continue;
                var dp = item.AsGodotDictionary<string, Variant>();
                int slot = GetI(dp, "building", -1);
                var be = _entities.Find(x => x.IsBuilding && x.Slot == slot);
                if (be == null) continue;
                be.Deposit = GetI(dp, "terranium", -1);
                be.DepositStart = Mathf.Max(1, be.Deposit);
                be.Grade = GetI(dp, "grade", 0);
            }
        }

        // mission win conditions (sec69) — only the .DM full-state maps have them
        // sec69 holds EIGHT objective lists, one per player.  Each entry names a
        // building that decides the mission for that player, and `destroyed`
        // records that it has already fallen.
        for (int p = 0; p < 8; p++) _objectives[p].Clear();
        if (root.TryGetValue("targets", out var tgv) && tgv.VariantType == Variant.Type.Array)
        {
            foreach (var item in tgv.AsGodotArray())
            {
                if (item.VariantType != Variant.Type.Dictionary) continue;
                var tg = item.AsGodotDictionary<string, Variant>();
                int slot = GetI(tg, "building", -1);
                int pl = GetI(tg, "player", -1);
                if (pl is >= 0 and <= 7 && slot >= 0) _objectives[pl].Add(slot);
                if (!_targetSlots.Add(slot)) continue;
                var e = _entities.Find(x => x.IsBuilding && x.Slot == slot);
                if (e != null) e.IsTarget = true;
            }
        }

        // sec2 stays what it is — a zone map for the Z overlay. It is NOT the
        // passability: Can_go @0x4055D0 never reads it, and taking it for one
        // is what let land units drive into the sea (on NET07 its class 0 covers
        // 7115 cells where the water is 6452).
        if (root.TryGetValue("zones", out var zv) && zv.VariantType == Variant.Type.Dictionary)
            BuildZoneTexture(zv.AsGodotDictionary<string, Variant>(), ox, oy);

        // the map's own passability, straight off the imap (sec6)
        if (root.TryGetValue("terrain", out var tev) && tev.VariantType == Variant.Type.Dictionary)
            _nav?.ApplyTerrain(tev.AsGodotDictionary<string, Variant>());
        else
            GD.PrintErr($"{name}: der Spielstand kennt noch keine Passierbarkeit — die Karte " +
                        "laeuft auf dem alten Kachelcode-Notbehelf (Bruecken sperren, Wasser " +
                        "ungenau). Neu importieren, oder --reexport-states=<Quelle>.");

        // the fog covers the same grid the map does
        int fw = GetI(root, "width"), fh = GetI(root, "height");
        if (fw > 0 && fh > 0)
        {
            Simulation.FogGrid.Load();
            _fog = new Simulation.FogGrid(fw, fh);
            _fogRect = new Rect2(ox, oy, fw * TileW, fh * TileH);
            _fogDrawn = -1;
            _fogTick = 0;
            UpdateFog();
        }

        _source = "entities.json (RE game-state)";
        return _entities.Count > 0 || _markers.Count > 0;
    }

    private void BuildZoneTexture(GDict zones, int ox, int oy)
    {
        int w = GetI(zones, "width"), h = GetI(zones, "height");
        if (w <= 0 || h <= 0 ||
            !zones.TryGetValue("grid", out var gv) || gv.VariantType != Variant.Type.Array)
            return;
        var rows = gv.AsGodotArray();
        var img = Image.CreateEmpty(w, h, false, Image.Format.Rgba8);
        for (int r = 0; r < h && r < rows.Count; r++)
        {
            if (rows[r].VariantType != Variant.Type.Array) continue;
            var cells = rows[r].AsGodotArray();
            for (int c = 0; c < w && c < cells.Count; c++)
            {
                int z = cells[c].AsInt32();
                img.SetPixel(c, r, z >= 0 && z < ZoneColors.Length ? ZoneColors[z] : ZoneColors[0]);
            }
        }
        _zoneTex = ImageTexture.CreateFromImage(img);
        _zoneRect = new Rect2(ox, oy, w * TileW, h * TileH);
    }

    private void LoadPropsFallback(GDict meta, int ox, int oy)
    {
        if (!meta.TryGetValue("tiles", out var tv) || tv.VariantType != Variant.Type.Array)
            return;
        foreach (var item in tv.AsGodotArray())
        {
            if (item.VariantType != Variant.Type.Dictionary) continue;
            var t = item.AsGodotDictionary<string, Variant>();
            if (!(t.TryGetValue("object", out var ob) && ob.AsBool())) continue;
            int col = GetI(t, "col"), row = GetI(t, "row"), el = GetI(t, "elev", 0);
            _entities.Add(new Entity
            {
                Slot = -1, Col = col, Row = row, Owner = -1, Team = -1,
                UnitType = GetI(t, "code", 0), Category = -1, Hp = -1, HpMax = -1, Elev = el,
                Footprint = CellRect(ox, oy, col, row, el), IsProp = true,
            });
        }
        _source = "tiles (props only — entities.json missing)";
    }

    private static Rect2 CellRect(int ox, int oy, int col, int row, int elev)
        => new(ox + col * TileW, oy + row * TileH - elev * 15, TileW, TileH);

    // ================= Step E: units that can be selected and driven ===========

    private int ElevOf(int col, int row) => _elevLookup.TryGetValue((col, row), out var e) ? e : 0;

    // ---- selection and order feedback (game feel) ---------------------------
    //
    // A ring around every selected unit reads as noise once a dozen are picked,
    // so the selection is drawn as four corner brackets on the unit's cell —
    // the primary unit (the one the panel describes) gets brighter, longer
    // ones. Purely presentation; nothing here comes from the original.
    private void DrawSelectionBrackets(Vector2 c, bool primary, float hw = 13f, float hh = 8f)
    {
        var col = primary ? new Color(0.45f, 1f, 0.6f, 1f) : new Color(0.3f, 0.9f, 0.5f, 0.75f);
        float len = primary ? 6f : 4f, w = primary ? 2f : 1.5f;
        Vector2[] corners =
        {
            new(c.X - hw, c.Y - hh), new(c.X + hw, c.Y - hh),
            new(c.X - hw, c.Y + hh), new(c.X + hw, c.Y + hh),
        };
        for (int k = 0; k < 4; k++)
        {
            float sx = k % 2 == 0 ? 1 : -1, sy = k < 2 ? 1 : -1;
            DrawLine(corners[k], corners[k] + new Vector2(len * sx, 0), col, w);
            DrawLine(corners[k], corners[k] + new Vector2(0, len * sy), col, w);
        }
    }

    // ---- control groups -----------------------------------------------------
    private readonly Dictionary<int, List<int>> _groups = new();

    /// <summary>Store the current selection as group n (slots, so the group
    /// survives a re-load of the same map).</summary>
    public void StoreGroup(int n)
    {
        var g = new List<int>(_sel);
        if (g.Count == 0) { _groups.Remove(n); _order = $"Gruppe {n} geleert"; }
        else { _groups[n] = g; _order = $"Gruppe {n}: {g.Count} Einheiten"; }
        UpdatePanel();
    }

    /// <summary>Select a unit from a script, so a headless run can photograph
    /// the info panel. A click is the only other way in, and a scripted run
    /// cannot click — the same gap <see cref="PanelWatchLine"/> works around,
    /// except this one puts the real panel on screen.
    ///
    /// `which` is an index into the units that carry a weapon or a tank, so a
    /// caller does not have to know slot numbers to land on something with
    /// something to show.</summary>
    public string SelectForShot(int which)
    {
        var pick = new List<int>();
        for (int i = 0; i < _entities.Count; i++)
        {
            var e = _entities[i];
            if (e.IsBuilding || e.IsProp || e.Dead) continue;
            if (e.Weapon != 0 || e.FuelMax > 0) pick.Add(i);
        }
        if (pick.Count == 0) return "select: keine Einheit mit Waffe oder Tank";
        int idx = pick[Mathf.PosMod(which, pick.Count)];
        _sel.Clear();
        _sel.Add(idx);
        SetPrimary();
        UpdatePanel();
        QueueRedraw();
        var s = _entities[idx];
        return $"select: Platz {s.Slot} {LabelOf(s.UnitType)}, Waffe {s.Weapon} " +
               $"\"{WeaponOf(s.Weapon).Name}\", Munition {s.Ammo}/{s.AmmoMax}, " +
               $"Sprit {s.Fuel}/{s.FuelMax}  |  Panel: " +
               _panel.Text.Replace("\n", " / ");
    }

    /// <summary>Recall group n; dead members are dropped silently.</summary>
    public bool RecallGroup(int n)
    {
        if (!_groups.TryGetValue(n, out var g)) return false;
        _sel.Clear();
        foreach (int i in g)
            if (i >= 0 && i < _entities.Count && !_entities[i].Dead) _sel.Add(i);
        if (_sel.Count == 0) { _groups.Remove(n); _order = $"Gruppe {n} ist gefallen"; }
        else _order = $"Gruppe {n}: {_sel.Count} Einheiten";
        SetPrimary();
        UpdatePanel();
        QueueRedraw();
        return true;
    }

    /// <summary>Middle of the current selection, for the camera.</summary>
    public Vector2? SelectionCenter()
    {
        if (_sel.Count == 0)
            return _selected >= 0 && _selected < _entities.Count ? _entities[_selected].Pos : null;
        var sum = Vector2.Zero;
        foreach (int i in _sel) sum += _entities[i].Pos;
        return sum / _sel.Count;
    }

    /// <summary>A short-lived mark where an order was given: a shrinking ring
    /// for a move, a cross for an attack. Ours — the original's own feedback is
    /// not recoverable from the data.</summary>
    private readonly List<(Vector2 Pos, float Time, bool Attack)> _orderMarks = new();
    private const float OrderMarkSec = 0.7f;

    public void AddOrderMark(Vector2 pos, bool attack) => _orderMarks.Add((pos, 0f, attack));

    /// <summary>The things worth looking at that happened to the viewed player —
    /// a unit of his taking fire or dying, a factory of his finishing something.
    ///
    /// This used to be a single overwriting slot, which meant two incidents in
    /// the same second left only one, and Tab kept jumping to the same place.
    /// It is a short ring now: Tab walks it newest first, and the minimap marks
    /// the recent ones. Ours; the original's alert handling is not recoverable
    /// from the data.</summary>
    public readonly struct GameEvent
    {
        public GameEvent(Vector2 pos, string what, float at, bool lost)
        { Pos = pos; What = what; At = at; Lost = lost; }
        public Vector2 Pos { get; }
        public string What { get; }
        /// <summary>Simulation clock when it happened.</summary>
        public float At { get; }
        /// <summary>A loss, as opposed to something merely under fire.</summary>
        public bool Lost { get; }
    }

    private readonly List<GameEvent> _events = new();
    private int _eventCursor = -1;
    private const int EventRing = 12;

    /// <summary>The newest incident, for the status line.</summary>
    public (Vector2 Pos, string What)? LastEvent()
        => _events.Count == 0 ? null : (_events[^1].Pos, _events[^1].What);

    /// <summary>The ring, oldest first.</summary>
    public IReadOnlyList<GameEvent> Events => _events;

    /// <summary>Steps back through the incidents, newest first, and returns the
    /// one to look at. Walking off the end starts over at the newest.</summary>
    public (Vector2 Pos, string What)? StepEvent()
    {
        if (_events.Count == 0) return null;
        _eventCursor = _eventCursor <= 0 ? _events.Count - 1 : _eventCursor - 1;
        var e = _events[_eventCursor];
        return (e.Pos, e.What);
    }

    private void NoteEvent(Entity e, string what)
    {
        if (e.Owner != ViewPlayer) return;
        _events.Add(new GameEvent(e.Pos, what, (float)DebugClock, e.Dead || e.Hp <= 0));
        if (_events.Count > EventRing) _events.RemoveAt(0);
        _eventCursor = _events.Count;      // Tab starts at the newest again
    }

    // ---- what the overview map needs ---------------------------------------

    /// <summary>Every unit and building worth a dot on the overview map, with
    /// its owner. Props — the scenery baked into the picture — are left out.
    ///
    /// <b>The fog applies here too.</b> It did not, and the overview map handed
    /// the player every enemy position on a map he had not scouted — the fog
    /// over the battlefield was worth nothing while the little map beside it
    /// told all. The rule is the one the battlefield already uses: one's own
    /// things always; someone else's UNIT only while it is being watched; a
    /// BUILDING once the cell has been seen, because a base does not walk away
    /// while nobody looks.</summary>
    public List<(Vector2 Pos, int Owner, bool Building)> MinimapDots()
    {
        var list = new List<(Vector2, int, bool)>(_entities.Count);
        _dotsMine = _dotsForeign = _dotsHidden = 0;
        foreach (var e in _entities)
        {
            if (e.IsProp || e.Dead) continue;
            if (FogActive && e.Owner != ViewPlayer &&
                !(e.IsBuilding ? _fog!.IsSeen(e.Col, e.Row) : _fog!.IsWatched(e.Col, e.Row)))
            { _dotsHidden++; continue; }
            if (e.Owner == ViewPlayer) _dotsMine++; else _dotsForeign++;
            list.Add((e.Pos, e.Owner, e.IsBuilding));
        }
        return list;
    }

    // what the last MinimapDots call put on the map, for the harness line
    private int _dotsMine, _dotsForeign, _dotsHidden;

    /// <summary>Whether the overview map keeps up and whether it obeys the fog —
    /// the two changes that had never been shown to work.</summary>
    public string MinimapWatchLine(Minimap? m)
    {
        MinimapDots();
        var alive = new int[8];
        foreach (var e in _entities)
            if (!e.IsProp && !e.Dead && e.Owner is >= 0 and <= 7) alive[e.Owner]++;
        return $"minimap: {(m == null ? "-" : m.Repaints.ToString())} Neuzeichnungen bei " +
               $"{(m == null ? "-" : m.ViewMoves.ToString())} Kamerabewegungen, " +
               $"{(m == null ? "-" : m.FogDrawn.ToString())} davon mit Nebelschicht; " +
               $"Punkte {_dotsMine} eigene, {_dotsForeign} fremde sichtbar, " +
               $"{_dotsHidden} vom Nebel verdeckt (Nebel {(FogActive ? "an" : "aus")}, " +
               $"Spieler {ViewPlayer}, lebend " +
               string.Join("/", System.Array.ConvertAll(alive, x => x.ToString())) + ")";
    }

    /// <summary>The recent incidents as marks, with how old each one is.</summary>
    public List<Minimap.Alarm> MinimapAlarms()
    {
        var list = new List<Minimap.Alarm>();
        foreach (var e in _events)
        {
            float age = (float)DebugClock - e.At;
            if (age <= Minimap.AlarmSeconds) list.Add(new Minimap.Alarm(e.Pos, age, e.Lost));
        }
        return list;
    }

    /// <summary>The size of the map in the same pixels the entities live in, so
    /// the overview can scale between the two.</summary>
    public Vector2 MapPixelSize()
        => _nav == null ? Vector2.Zero
                        : new Vector2(_ox * 2 + _nav.Width * TileW, _oy * 2 + _nav.Height * TileH);

    private void UpdateOrderMarks(float dt)
    {
        for (int i = _orderMarks.Count - 1; i >= 0; i--)
        {
            var m = _orderMarks[i];
            m.Time += dt;
            if (m.Time >= OrderMarkSec) _orderMarks.RemoveAt(i);
            else _orderMarks[i] = m;
        }
    }

    private void DrawOrderMarks()
    {
        foreach (var m in _orderMarks)
        {
            float t = m.Time / OrderMarkSec;
            var col = m.Attack ? new Color(1f, 0.35f, 0.3f, 1f - t)
                               : new Color(0.4f, 1f, 0.55f, 1f - t);
            if (m.Attack)
            {
                float r = 9f + t * 4f;
                DrawLine(m.Pos + new Vector2(-r, -r * 0.5f), m.Pos + new Vector2(r, r * 0.5f), col, 2f);
                DrawLine(m.Pos + new Vector2(-r, r * 0.5f), m.Pos + new Vector2(r, -r * 0.5f), col, 2f);
            }
            else DrawArc(m.Pos, 16f * (1f - t) + 4f, 0, Mathf.Tau, 20, col, 2f);
        }
    }

    /// <summary>Map pixels for a rail route point. The track runs on a
    /// half-tile lattice, so col/row are fractional; the elevation is taken
    /// from the cell the point falls in.</summary>
    private Vector2 RailPoint(Vector2 p)
        => new(_ox + p.X * TileW + TileW / 2f,
               _oy + p.Y * TileH - ElevOf(Mathf.RoundToInt(p.X), Mathf.RoundToInt(p.Y)) * 15
                   + TileH / 2f);

    /// <summary>Ground point (map pixels) at the center of a cell.</summary>
    private Vector2 CellCenter(int col, int row)
        => new(_ox + col * TileW + TileW / 2f,
               _oy + row * TileH - ElevOf(col, row) * 15 + TileH / 2f);

    /// <summary>
    /// Where a unit's picture belongs: the middle of its BODY, not of its anchor
    /// cell. The record names one cell, but the imap stamps a unit into every
    /// cell it covers, and the importer measures that (CwmData.UnitFootprints).
    /// For the 1x1 that everything else is, this is CellCenter unchanged.
    /// </summary>
    private Vector2 BodyCenter(Entity e)
    {
        if (e.FootW <= 1 && e.FootH <= 1) return CellCenter(e.Col, e.Row);
        var a = CellCenter(e.Col, e.Row);
        var b = CellCenter(e.Col + e.FootW - 1, e.Row + e.FootH - 1);
        return (a + b) * 0.5f;
    }

    /// <summary>Current body rect of an entity — follows it while it drives, and
    /// is as big as the unit really is. A ship covers 2x2 cells (the imap says
    /// so, see CwmData.UnitFootprints); giving it a one-cell box made it hard to
    /// click and drew a bracket a quarter of its size.</summary>
    private static Rect2 BodyRect(Entity e)
    {
        var size = new Vector2(TileW * Mathf.Max(1, e.FootW), TileH * Mathf.Max(1, e.FootH));
        return new Rect2(e.Pos - size / 2f, size);
    }

    /// <summary>Place every entity on its cell and claim that cell on the grid.</summary>
    private void InitEntityMovement()
    {
        for (int i = 0; i < _entities.Count; i++)
        {
            var e = _entities[i];
            e.Pos = BodyCenter(e);
            e.Mobile = !e.IsProp && e.UnitType >= 0 && !ImmobileTypes.Contains(e.UnitType);
            // Can_go branches on +0x0a first and on the chassis +0x0b second.
            // A unit that came off a map carries both; one that was BUILT has no
            // record behind it, so the hull number decides — that is the older
            // NavalTypes reading, kept exactly where it is still the only source.
            e.Move = e.Subclass >= 0
                ? Simulation.NavGrid.ClassOf(e.Subclass, e.Chassis)
                : NavalTypes.Contains(e.UnitType)
                    ? Simulation.NavGrid.MoveClass.Ship
                    : Simulation.NavGrid.MoveClass.Vehicle;
            e.Path = null;
            e.Reserved = null;
            // A Nachschub-Posten is driven ONTO: its tick @0x43e872 looks up
            // its own cell in the spatial grid and services the unit standing
            // there, so neither the post itself nor the structure baked into
            // the map picture may block that cell.
            if (e.IsBuilding && e.BType == 14) _nav?.ClearStatic(e.Col, e.Row);
            else _nav?.SetOccupant(e.Col, e.Row, i, e.Infantry >= 0);
        }
    }

    /// <summary>Wo das Ohr steht: der Mittelpunkt des Bildes, in Zellen. Wird
    /// jede Runde nachgezogen, weil das Original seine Klangdämpfung ebenfalls
    /// gegen die aktuelle Kameramitte rechnet (@0x404926). Ausserhalb der Karte
    /// wird nicht abgeschnitten, sondern weitergerechnet — sonst spränge die
    /// Lautstärke, sobald der Rand im Bild ist.</summary>
    public void SetListener(Vector2 mapPos)
    {
        if (_nav == null || _nav.Width == 0)
        {
            Audio.SoundBankPlayer.ListenerCell = new Vector2(float.NaN, float.NaN);
            return;
        }
        Audio.SoundBankPlayer.ListenerCell = CellAt(mapPos) is { } c
            ? new Vector2(c.X, c.Y)
            : new Vector2((mapPos.X - _ox) / TileW, (mapPos.Y - _oy) / TileH);
    }

    /// <summary>Map pixel -> cell, honouring the elevation lift of the tiles.</summary>
    public Vector2I? CellAt(Vector2 mapPos)
    {
        if (_nav == null || _nav.Width == 0) return null;
        int col = Mathf.FloorToInt((mapPos.X - _ox) / TileW);
        if (col < 0 || col >= _nav.Width) return null;
        int guess = Mathf.FloorToInt((mapPos.Y - _oy) / TileH);
        // an elevated tile is drawn higher, so the matching row is at or below the
        // guess; take the frontmost (largest row) match, like the renderer does.
        for (int row = Mathf.Min(guess + 12, _nav.Height - 1); row >= Mathf.Max(guess - 1, 0); row--)
        {
            float top = _oy + row * TileH - ElevOf(col, row) * 15;
            if (mapPos.Y >= top && mapPos.Y < top + TileH) return new Vector2I(col, row);
        }
        int clamped = Mathf.Clamp(guess, 0, _nav.Height - 1);
        return new Vector2I(col, clamped);
    }

    // ---- selection ----

    public IReadOnlyCollection<int> Selection => _sel;

    private void SetPrimary()
    {
        _selected = -1;
        foreach (int i in _sel) { _selected = i; break; }
        UpdatePanel();
    }

    /// <summary>Can the player pick this entity up and give it orders? Only his
    /// own — clicking a foreign unit used to select it and let it be commanded,
    /// which in a campaign mission meant playing the other side.</summary>
    private bool Commandable(int i)
        => i >= 0 && i < _entities.Count &&
           !_entities[i].IsProp && !_entities[i].Dead &&
           _entities[i].Owner == ViewPlayer;

    /// <summary>Click select: the entity under the cursor (frontmost), else
    /// clear. A foreign unit is still shown in the panel — it just cannot be
    /// selected, so it can be looked at without being taken over.</summary>
    public void SelectAt(Vector2 mapPos, bool additive = false)
    {
        int hit = Pick(mapPos);
        bool mine = Commandable(hit);
        if (!additive) _sel.Clear();
        if (mine)
        {
            if (additive && !_sel.Add(hit)) _sel.Remove(hit);
            else _sel.Add(hit);
        }
        SetPrimary();
        if (!mine && hit >= 0) { _selected = hit; UpdatePanel(); }   // look, do not touch
        SpeakSelected(hit);
        QueueRedraw();
    }

    /// <summary>The unit answers when it is picked — sound 150..253, chosen the
    /// way @0x429290 chooses it (record +0x0a, then the chassis at +0x0b, then a
    /// throw of three). Only for the player's own units, and only for units: the
    /// original bails out at once when what is selected is not one
    /// (<c>si &gt;= 0x1f40</c>), and a building answering back would be ours.
    /// </summary>
    private void SpeakSelected(int i)
    {
        if (!UI.Settings.Announcements) return;
        if (i < 0 || i >= _entities.Count) return;
        var e = _entities[i];
        if (e.IsBuilding || e.IsProp || e.Dead || e.Chassis < 0) return;
        if (e.Owner != ViewPlayer) return;
        int s = Audio.GameSounds.Voice(e.Subclass, e.Chassis, e.Field28);
        if (s >= 0) Audio.SoundBankPlayer.Play(s);
    }

    /// <summary>When the clock may next carry a hit line. The original keeps one
    /// such gate for the whole game (0x4f5aec against the clock 0x4fa240), not
    /// one per unit, and that is what stops a battle sounding like a chorus.
    /// </summary>
    private float _hitVoiceAt;

    /// <summary>The unit that was hit says so — routine @0x4297f0, called from
    /// the hit routine itself. Mode 0 there, so it is not placed on the map;
    /// heard wherever the camera is, like a report coming in.</summary>
    private void SpeakHit(Entity victim)
    {
        if (!UI.Settings.Announcements) return;
        if (victim.IsBuilding || victim.IsProp || victim.Chassis < 0) return;
        if (victim.Owner != ViewPlayer) return;      // ours: only our own report
        float now = _clock;
        if (now < _hitVoiceAt) return;
        _hitVoiceAt = now + Audio.GameSounds.HitVoiceGapSec();
        Audio.SoundBankPlayer.Play(
            Audio.GameSounds.HitVoice(victim.Subclass, victim.Chassis, victim.Field28));
    }

    /// <summary>
    /// A vehicle rolls onto a cell a foot soldier is standing on.
    ///
    /// The original asks two questions about that cell and both of them let the
    /// vehicle through: `pratelska_infa` @0x433fe0 walks the cell's nine men and
    /// passes when every one of them is friendly (`neutok[mine][theirs] != 0`
    /// out of the alliance matrix @0x87b155), and `prejet` — the game's own word,
    /// "to run over" — @0x412980 passes when NONE of them is. So friendly
    /// infantry is driven through and hostile infantry is driven over; only the
    /// second one dies. @0x412a50 then stamps the cell back to 0xFFFE, which is
    /// what <see cref="Kill"/> does here by clearing the occupant.
    ///
    /// OURS: the original's cell holds up to nine men and ours holds one entity,
    /// so "all nine" and "none" collapse into a single test.
    /// </summary>
    private void RunOverFoot(int driverIdx, Entity driver, int footIdx)
    {
        if (footIdx < 0 || footIdx >= _entities.Count) return;
        var foot = _entities[footIdx];
        if (foot.Dead || foot.Infantry < 0) return;
        if (!IsHostile(driver, foot)) return;          // friendly: driven through
        NoteEvent(foot, "ueberfahren");
        Kill(footIdx, foot);
        _crushed++;
    }

    /// <summary>How many foot soldiers have been run over — for the report line.</summary>
    private int _crushed;

    // ---- the doors on a building --------------------------------------------
    //
    // The buildings themselves are baked into the map picture, but their DOORS
    // are not and never were: the original draws them at run time on top
    // (@0x42B25C..@0x42B394), picking an ANIM.CWA frame from the door's state
    // byte. That is why they were missing here.
    //
    // The rule for the state is the game's, @0x43D2EC: at 0 (shut) the door
    // starts opening the moment its imap cell holds something, and from open it
    // shuts again once the cell is empty.
    //
    // OURS: the speed of that swing (the original counts in its own ticks and
    // that count is not read), and the placement — we centre the picture on the
    // door cell, where the original computes an offset of its own.

    private const float DoorOpenSpeed = 6f;      // ours: phases per second
    private readonly Dictionary<int, float> _doorPhase = new();

    /// <summary>Counters for `--door-check`: why a door is or is not drawn.</summary>
    private int _doorsDrawn, _doorsNoCount, _doorsNoCells, _doorsNoPic, _doorsNoTex;

    public string DoorCheck()
    {
        _doorsDrawn = _doorsNoCount = _doorsNoCells = _doorsNoPic = _doorsNoTex = 0;
        int buildings = 0;
        foreach (var e in _entities)
        {
            if (!e.IsBuilding || e.Dead) continue;
            buildings++;
            DrawBuildingDoors(e, count: true);
        }
        return $"door-check: {buildings} Gebaeude — {_doorsDrawn} Tore gezeichnet; " +
               $"ohne Tueranzahl {_doorsNoCount}, ohne Tuerzellen {_doorsNoCells}, " +
               $"ohne Bildnummer {_doorsNoPic}, ohne Bilddatei {_doorsNoTex}";
    }

    private void DrawBuildingDoors(Entity e, bool count = false)
    {
        if (!count && (!_drawSprites || _nav == null)) return;
        if (e.Dead) return;
        if (e.Doors <= 0) { _doorsNoCount++; return; }
        var cells = BuildingDoors(e.BType);
        if (cells.Count == 0) { _doorsNoCells++; return; }

        for (int k = 0; k < cells.Count && k < e.Doors; k++)
        {
            int pic = Import.BuildingPatterns.DoorPicture(e.BType, 0, k, e.ProdSpeed);
            if (pic < 0) { _doorsNoPic++; continue; }    // no constant tile for this type
            int c = e.Col + cells[k].X, r = e.Row + cells[k].Y;
            if (_nav != null && !_nav.InBounds(c, r)) continue;

            // the game's own rule: something in the cell opens the door
            int key = e.Slot * 4 + k;
            float want = _nav != null && _nav.OccupantAt(c, r) >= 0
                       ? Import.BuildingPatterns.DoorPhases - 1 : 0;
            float have = _doorPhase.TryGetValue(key, out float p) ? p : 0f;
            have = Mathf.MoveToward(have, want, DoorOpenSpeed * (float)GetProcessDeltaTime());
            _doorPhase[key] = have;

            var tex = DoorTexture(Import.BuildingPatterns.DoorPicture(e.BType, Mathf.RoundToInt(have), k, e.ProdSpeed));
            if (tex == null) { _doorsNoTex++; continue; }
            _doorsDrawn++;
            if (count) continue;

            // The original's own placement, @0x42B34B..0x42B379: it starts from
            // the BUILDING's screen position and adds the door offset in whole
            // cells — `byte[+0x36] * 4 * 5` = row*20 = TileH and
            // `byte[+0x35] * 5 * 8` = col*40 = TileW. Note what it does NOT do:
            // it never looks up the elevation of the door cell, it uses the
            // building's own. Centring the picture in the cell (what we did
            // first) put doors on open grass.
            var at = CellRect(_ox, _oy, e.Col, e.Row, e.Elev);
            DrawTexture(tex, new Vector2(
                at.Position.X + cells[k].X * TileW,
                at.Position.Y + cells[k].Y * TileH + TileH - tex.GetHeight()));
        }
    }

    /// <summary>Buildings sorted so the ones further back are drawn first —
    /// the order pass C of the baker used, which is why they nested correctly
    /// in the baked picture. Rebuilt only when the list changes.</summary>
    private List<Entity> BuildingsBackToFront()
    {
        if (_buildingOrder == null || _buildingOrderStamp != _entities.Count)
        {
            _buildingOrder = new List<Entity>();
            foreach (var e in _entities)
                if (e.IsBuilding && !e.IsProp) _buildingOrder.Add(e);
            _buildingOrder.Sort((a, b) => a.Row != b.Row ? a.Row - b.Row : a.Col - b.Col);
            _buildingOrderStamp = _entities.Count;
        }
        return _buildingOrder;
    }

    private List<Entity>? _buildingOrder;
    private int _buildingOrderStamp = -1;

    /// <summary>The atlas as one texture, made once — a building is a few dozen
    /// tiles and every one of them is a region of this.</summary>
    private Texture2D? _patternTex;

    private Texture2D? PatternTexture()
    {
        if (_patternTex != null) return _patternTex;
        if (Patterns?.AtlasImage == null) return null;
        var img = Patterns.AtlasImage;

        // ⚠ 11.08.2026 — hier ging ein Fehler ZWEIMAL still durch, und das war
        // das eigentliche Problem. `CreateFromImage` gibt für ein Bild über der
        // Höchstgröße kein null zurück, sondern eine Textur mit einer toten RID;
        // gezeichnet wird die als WEISSE FLÄCHE. Die Engine meldete zwar
        // »Texture dimensions exceed device maximum«, aber mitten in einem Strom
        // von »Attempting to use an uninitialized RID«, einmal pro Kachel und
        // pro Bild — im Spiel sah man nur weisse Gebäude und im Log nichts, was
        // auf den Kachelsatz gezeigt hätte. Also lieber gar nicht zeichnen und
        // EINMAL sagen, welches Glied falsch ist und was dagegen hilft.
        int max = MaxTextureSize();
        if (img.GetWidth() > max || img.GetHeight() > max)
        {
            if (!_atlasTooBig)
            {
                _atlasTooBig = true;
                GD.PrintErr($"Bau-Atlas {img.GetWidth()}x{img.GetHeight()} ueberschreitet die " +
                            $"Hoechstgroesse einer Textur ({max}) — die Gebaeude bleiben " +
                            "ungezeichnet. Der Kachelsatz stammt aus einem Import vor dem " +
                            "Spaltenumbruch (BuildingPatterns.AtlasColumnHeight); " +
                            "--reexport-buildings=<Quelle> schreibt ihn neu.");
            }
            return null;
        }
        _patternTex = ImageTexture.CreateFromImage(img);
        return _patternTex;
    }

    /// <summary>Einmal gemeldet, nicht einmal je Bild.</summary>
    private bool _atlasTooBig;

    /// <summary>Was die Karte an Kantenlänge zulässt. Kopflos gibt es kein
    /// Rendergerät — dann gilt die 16.384, die Vulkan mindestens zusichert und
    /// die die hier gemessene Karte auch tatsächlich hat.</summary>
    private static int MaxTextureSize()
    {
        try
        {
            var rd = RenderingServer.GetRenderingDevice();
            if (rd != null)
            {
                ulong v = rd.LimitGet(RenderingDevice.Limit.MaxTextureSize2D);
                if (v > 0 && v < int.MaxValue) return (int)v;
            }
        }
        catch (System.Exception) { }
        return 16384;
    }

    /// <summary>
    /// A building's own body.
    ///
    /// <para>⚠ NEW 07.08.2026. Buildings used to be part of the baked map
    /// picture, because the original writes their tiles into the map grid as
    /// <c>tile + 0x2710</c> and the baker could not tell them from a tree. That
    /// made a destroyed building impossible to remove: its pixels WERE the map.
    /// The baker now leaves those cells out (MapBaker.BuildingCells) and the
    /// building is drawn here instead — which is what lets it show its ruin.</para>
    ///
    /// <para>The picture is the type's frame: <c>FirstPattern</c> while it
    /// stands, the last pattern once it has fallen. See
    /// <see cref="Import.BuildingPatterns.RuinPattern"/> for where that reading
    /// comes from. A type with only one pattern (17) has no ruin and simply keeps
    /// standing — the original has no other picture for it either.</para>
    /// </summary>
    private void DrawBuildingBody(Entity e)
    {
        if (!_drawSprites || Patterns == null || e.IsProp) return;
        var tex = PatternTexture();
        if (tex == null) return;

        var bt = Patterns.GetBuildingType(e.BType);
        int first = bt.FirstPattern;
        int stack = e.Dead ? 0 : DamageFrame(e);      // wie viele Muster übereinander
        if (e.Dead)
        {
            int ruin = Import.BuildingPatterns.RuinPattern(Patterns, e.BType);
            if (ruin < 0) return;
            first = ruin;
            stack = 1;
        }
        if (first < 0 || stack < 1) return;

        // the cells the type animates, and the tile each shows right now
        var anim = BuildingAnimCells(e);

        // MapBaker.Blit's placement, cell for cell and elevation for elevation.
        // The draw space and the baked picture share their origin (_oy is the
        // map's own origin_y), so the same arithmetic has to hold here — if it
        // did not, every building would jump the moment it stopped being baked.
        //
        // ⚠ Das Original stempelt `bild` Muster ÜBEREINANDER (@0x4C97B4, `add
        // eax, 0xb4` je Runde gegen `edx = bild`) — Muster 0 ist das ganze
        // Gebäude, 1..n-2 sind Einzelkachel-Auflagen, n-1 ist die Ruine. Genau
        // deshalb sind die mittleren Muster so klein: in 06.CWP hat Muster 0
        // 37 Kacheln, die Muster 1..19 haben 1 bis 7. Sie sind SCHADENSFLECKEN.
        for (int k = 0; k < stack; k++)
            for (int dx = 0; dx < Import.CwpFile.PatternWidth; dx++)
                for (int dy = 0; dy < Import.CwpFile.PatternHeight; dy++)
                {
                    int code = BuildingCellTile(first, k, dx, dy, anim);
                    if (code == 0 || !Patterns.TryGetTile(code, out var t)) continue;
                    int c = e.Col + dx, r = e.Row + dy;
                    float sx = _ox + c * Import.MapBaker.TileW;
                    float sy = _oy + r * Import.MapBaker.TileH
                             - ElevOf(c, r) * Import.MapBaker.ElevStep
                             + Import.MapBaker.BlitAnchor + t.YOff;
                    DrawTextureRectRegion(tex, new Rect2(sx, sy, t.W, t.H),
                                          new Rect2(t.X, t.Y, t.W, t.H));
                }
    }

    /// <summary>
    /// Die EINE Entscheidung, welche Kachel Lage <paramref name="k"/> in
    /// Musterzelle (<paramref name="dx"/>,<paramref name="dy"/>) legt — oder 0
    /// für »nichts«. <see cref="DrawBuildingBody"/> und der Prüfstand
    /// <see cref="BuildingCellsOurs"/> teilen sie sich, damit der Prüfstand
    /// nicht eine Kopie prüft.
    ///
    /// <para>⚠ <b>KORRIGIERT 10.08.2026.</b> Hier stand »die Zellanimation gilt
    /// nur dem Grundbild, eine Auflage hat ihre eigene Kachel und darf nicht
    /// ersetzt werden«. Das war GERATEN, und es ist falsch herum.</para>
    ///
    /// <para>Das Original hält keinen Stapel: es schreibt in die KARTENZELLE,
    /// und wer zuletzt schreibt, gewinnt. Die Schadensauflage wird EINMAL
    /// gestempelt (@0x4C97B4), die Zellanimation schreibt JEDES BILD
    /// (A 0x4D5A3D / 0x4D5A96). Also gewinnt die Animation — sofern ihr
    /// Wächter A 0x4D58B4 sie durchlässt, der die Kachel der Karte gegen
    /// <c>Tiles[1]</c> der ersten Zeile des Typs hält.</para>
    ///
    /// <para>An den Daten nachgerechnet (<c>aekernel-tools/banim_re.py
    /// data</c>): über alle 23 .CWP liegen <b>151</b> Zeilen unter einer
    /// Schadensauflage, und in <b>151 von 151</b> Fällen lässt der Wächter die
    /// Animation weiterlaufen — kein Gegenbeispiel. Ein beschädigtes Fließband
    /// läuft im Original also weiter, und der Fleck darüber ist im nächsten
    /// Bild wieder weg.</para>
    ///
    /// <para>Darum gehört eine Animationszelle der Animation allein. Phase 0
    /// zeigt dort die Kachel des GRUNDMUSTERS, nicht die der Auflage — das ist
    /// der Rücksetzzweig A 0x4D5A6D, der ausdrücklich
    /// <c>word[(90*first + 6*dx + dy)*2 + 0xb97b38]</c> zurückschreibt, also
    /// Muster <c>first</c>.</para>
    /// </summary>
    private int BuildingCellTile(int first, int k, int dx, int dy,
                                 Dictionary<(int, int), int>? anim)
    {
        if (Patterns == null) return 0;
        if (anim != null && anim.TryGetValue((dx, dy), out int swap))
        {
            if (k > 0) return 0;              // die Auflage kommt hier nicht zum Zug
            if (swap != 0) return swap;       // 0 = Phase 0 = Grundkachel
        }
        return Patterns.PatternTile(first + k, dx, dy);
    }

    /// <summary>
    /// Wie viele Muster ein Gebäude gerade übereinander zeigt — seine
    /// SCHADENSSTUFE.
    ///
    /// Die Formel steht @0x4CBBF0 (F: 0x4CB7C0, Rumpf identisch):
    /// <code>bild = (hp_max − hp) / (hp_max / musterzahl)</code>
    /// mit `hp` = Satzbyte +0x06 und `hp_max` = +0x16. Beide werden beim Anlegen
    /// aus derselben Typtabelle 0x539DB8 gesetzt (Typ 1 → 1200, 2/3 → 1000,
    /// 4 → 800, ab Typ 17 → 700), und **`hp_max` wird danach nie wieder
    /// geschrieben** — 2 Schreibstellen in der ganzen EXE, beide im Anlegen. Das
    /// ist der Beleg, dass es keine Bauzeit ist: eine Konstante je Typ.
    ///
    /// Gegenprobe an 36 Karten / 1451 Gebäudesätzen: `hp &lt;= hp_max` gilt
    /// 1451 mal, kein Gegenbeispiel; `hp_max` ist je Typ konstant und gleich der
    /// EXE-Tabelle; und wo die errechnete Stufe ≥ 1 ist, stimmt sie mit dem
    /// gespeicherten Bildbyte in **14 von 14** Fällen exakt überein.
    ///
    /// ⚠ Die Routine liefert bei voller Gesundheit 0, die gelieferten Karten
    /// tragen dort aber 1 (alle 915 Sätze der 23 .CWM), die Spielstände 0 (alle
    /// 526). **0 und 1 heissen beide »heil«** — darum die Untergrenze 1.
    ///
    /// ⚠ NICHT gebaut: der Bauzustand. Dasselbe Bildbyte trägt ab **100** den
    /// Baufortschritt (Anlegen setzt 100, der Ticker @0x43CA50 erhöht jeden
    /// zweiten Takt bis 250, also 300 Takte, dann auf 1), und die Anzeige wählt
    /// daraus `(bild−100)/50` ∈ {0,1,2} auf drei eigene Vollbilder. Die haben
    /// aber nur die Typen 5, 7 und 15 — die Gebäude, die der Spieler im Feld
    /// baut (383 von 410 Typzeilen führen dort 0). Solange die Engine keinen
    /// Bauzustand kennt, fehlt auch die Sperre @0x40D2A0, die ein Gebäude im Bau
    /// unverwundbar macht.
    /// </summary>
    private int DamageFrame(Entity e)
    {
        if (Patterns == null || e.HpMax <= 0 || e.Hp >= e.HpMax) return 1;
        int count = Patterns.GetBuildingType(e.BType).PatternCount;
        if (count < 2) return 1;
        int step = e.HpMax / count;
        if (step < 1) return 1;
        int frame = (e.HpMax - Mathf.Max(0, e.Hp)) / step;
        return Mathf.Clamp(frame, 1, count - 1);
    }

    /// <summary>
    /// Wie viele Phasen eine Gebäudeanimation je Sekunde weiterschaltet.
    ///
    /// <para>⚠ <b>UNSERE SETZUNG</b>, und sie bleibt es — aber jetzt aus einem
    /// gelesenen Grund und nicht aus Ratlosigkeit. Nachgeprüft am 10.08.2026 auf
    /// BEIDEN GAME.EXE (aekernel-tools/banim_re.py rate):</para>
    /// <list type="bullet">
    /// <item>Der Treiber (A 0x4D5D10 / F 0x4D58A0) hat in der ganzen .text
    /// <b>genau EINE</b> Aufrufstelle — A 0x417EC3 / F 0x417CFF, über den Sprung
    /// A 0x4022B1 / F 0x4022A7 — und die steht in der Hauptschleife
    /// (A 0x415CF0 / F 0x415B30), 0x29 Byte hinter dem Profilerabschnitt
    /// »animations of the buildings«.</item>
    /// <item>Kein Timer, kein Teiler, keine Bedingung: jede Zeile schaltet
    /// <b>eine Phase je gezeichnetem Bild</b>.</item>
    /// <item>Die Bildrate selbst ist <c>IDirectDrawSurface::Flip</c>
    /// (A 0x415CB0, erkennbar an DDERR_SURFACEBUSY 0x887601C2 und
    /// DDERR_WASSTILLDRAWING 0x8876021C) — also der Vertikalrücklauf bzw. das,
    /// was die Maschine von 1997 schaffte.</item>
    /// </list>
    /// <para>Eine Zahl steht dort somit <b>nicht</b>, und es kann auch keine
    /// geben. Sechs ist die Rate, die die Türen schon benutzen.</para>
    /// </summary>
    public const float BuildingAnimFps = 6f;

    /// <summary>
    /// Welche Musterzellen eines Gebäudes gerade eine Animationskachel zeigen —
    /// <c>null</c>, wenn der Typ keine hat.
    ///
    /// <para>Die Tabelle ist der dritte Block des .CWP-Endes; siehe
    /// <see cref="Import.CwpFile.CellAnim"/>. Der Lauf des Originals
    /// (A 0x4D5830 / F 0x4D53C0) zählt <c>byte[bld + 0x0b + zeile]</c> hoch,
    /// zeigt <c>Tiles[ph]</c> solange <c>ph &lt;= LastPhase</c>, und setzt beim
    /// Überlauf die Kachel des GRUNDMUSTERS zurück (ph = 0). Ein Umlauf ist
    /// also <c>LastPhase+1</c> Bilder lang, und Phase 0 ist das Grundbild.</para>
    ///
    /// <para>⚠ <b>KORRIGIERT 10.08.2026.</b> Hier stand, die drei Bänder einer
    /// Fabrik liefen »absichtlich auseinander«, weil jede Zeile ihren eigenen
    /// Zähler habe. Der eigene Zähler stimmt, der Schluss nicht: das Anlegen
    /// eines Gebäudes nullt die zehn Zählerbytes in einem Zug
    /// (A 0x4C94A8 <c>lea eax,[ebp+0xc0691b]</c> und drei Schreibbefehle über
    /// 4+4+2 Byte — daher auch die Obergrenze von <b>zehn</b> Zeilen je Typ),
    /// und der Treiber schaltet alle Zeilen im selben Durchlauf weiter.
    /// Zeilen gleicher Länge laufen im Original also im Gleichschritt, und
    /// verschieden lange Zeilen laufen nur deshalb auseinander, weil sie
    /// verschieden lang sind. Genau das tut die Formel unten. Nichts zu
    /// entkoppeln.</para>
    ///
    /// <para>Der Wert einer Zelle ist die Kachel oder <b>0 für »Phase 0, also
    /// die Kachel des Grundmusters«</b>. Die Zelle steht auch dann in der Liste
    /// — <see cref="DrawBuildingBody"/> braucht sie, um die Schadensauflage
    /// dort zurückzuhalten.</para>
    ///
    /// <para>Eine Ruine animiert nicht: das Bild ist ein anderes Muster, und der
    /// Wächter des Originals (A 0x4D58B4) verwirft jede Zelle, deren
    /// Kartenkachel nicht mehr zum Gebäude gehört.</para>
    /// </summary>
    private Dictionary<(int, int), int>? BuildingAnimCells(Entity e)
        => BuildingAnimCells(e, (int)(_clock * BuildingAnimFps));

    /// <summary>Dasselbe für einen vorgegebenen Takt — der Prüfstand fährt damit
    /// jede Phase ab, statt auf sie zu warten.</summary>
    private Dictionary<(int, int), int>? BuildingAnimCells(Entity e, int tick)
    {
        if (Patterns == null || e.Dead) return null;
        var bt = Patterns.GetBuildingType(e.BType);
        if (bt.AnimCount <= 0) return null;

        Dictionary<(int, int), int>? cells = null;
        for (int k = 0; k < bt.AnimCount && k < AnimRowsPerBuilding; k++)
        {
            var a = Patterns.GetAnimRow(bt.AnimFirst + k);
            if (a.LastPhase <= 0) continue;
            // ⚠ Modus (Zeilenbyte +2). Gelesen, nicht gesetzt: 1 = Dauerlauf,
            // 2 = rückwärts laufender Einmalablauf, alles andere = einmal
            // vorwärts und dann für immer aus (0xff). Umgeschaltet wird der
            // Modus von A 0x4D5BA0 — einer Funktion, die in BEIDEN GAME.EXE
            // ausser ihrem Sprung KEINE Aufrufstelle hat, also toter Code ist.
            // In allen 23 ausgelieferten .CWP tragen **207 von 207** Zeilen den
            // Modus 1 (banim_re.py data). Wir laufen deshalb nur Modus 1; eine
            // andere Zeile bliebe stehen, so wie sie es im Original täte,
            // solange niemand sie startet.
            if (a.Mode != 1) continue;
            int phase = tick % (a.LastPhase + 1);
            (cells ??= new Dictionary<(int, int), int>())[(a.Dx, a.Dy)] = a.TileAt(phase);
        }
        return cells;
    }

    /// <summary>Wie viele Animationszeilen ein Gebäudesatz überhaupt tragen
    /// kann: das Anlegen nullt <c>bld+0x0b</c> bis <c>bld+0x14</c>, also zehn
    /// Zählerbytes (A 0x4C94A8..0x4C94C1). Über alle 23 .CWP kommt kein Typ auf
    /// mehr als drei Zeilen, die Grenze wird also nie erreicht.</summary>
    public const int AnimRowsPerBuilding = 10;

    /// <summary>
    /// `--anim-check` — count the building cell animations on this map and say
    /// for each whether it can actually be drawn.
    ///
    /// <para>Three ways a row can fail, and the report separates them: the type
    /// owns no row, the row's cell sits outside the pattern, or the tile of a
    /// phase is missing from the tileset atlas. The last one is the interesting
    /// one — those tiles appear in no pattern, so the exporter had to be taught
    /// to put them in.</para>
    /// </summary>
    public string AnimCheck()
    {
        if (Patterns == null) return "anim-check: keine Muster geladen";
        int buildings = 0, animated = 0, rows = 0, offPattern = 0, noTile = 0, phases = 0;
        var perType = new SortedDictionary<int, int>();

        foreach (var e in _entities)
        {
            if (!e.IsBuilding || e.Dead) continue;
            buildings++;
            var bt = Patterns.GetBuildingType(e.BType);
            if (bt.AnimCount <= 0) continue;
            animated++;
            perType[e.BType] = perType.TryGetValue(e.BType, out int n) ? n + 1 : 1;

            for (int k = 0; k < bt.AnimCount; k++)
            {
                var a = Patterns.GetAnimRow(bt.AnimFirst + k);
                rows++;
                phases += a.LastPhase;
                if (Patterns.PatternTile(bt.FirstPattern, a.Dx, a.Dy) == 0) offPattern++;
                for (int ph = 1; ph <= a.LastPhase; ph++)
                {
                    int tile = a.TileAt(ph);
                    if (tile == 0 || !Patterns.TryGetTile(tile, out _)) noTile++;
                }
            }
        }

        var sb = new System.Text.StringBuilder();
        sb.Append($"anim-check: {buildings} Gebaeude, {animated} mit Animation, ");
        sb.Append($"{rows} Zeilen mit {phases} Bildern — ");
        sb.Append($"Zelle ausserhalb des Musters {offPattern}, Kachel nicht im Atlas {noTile}");
        foreach (var kv in perType) sb.Append($"\n   typ {kv.Key}: {kv.Value} Stueck");
        return sb.ToString();
    }

    // ================= --banim-check =========================================
    //
    /// <summary>
    /// Was eine Musterzelle bei uns WIRKLICH zeigt: die oberste Kachel, die
    /// <see cref="DrawBuildingBody"/> für sie legt. Die Zeichenschleife stapelt
    /// die Muster, also gewinnt die zuletzt gelegte Kachel — genau das bildet
    /// diese Tabelle ab.
    /// </summary>
    private Dictionary<(int, int), int> BuildingCellsOurs(Entity e, int first, int stack, int tick)
    {
        var map = new Dictionary<(int, int), int>();
        if (Patterns == null) return map;
        var anim = BuildingAnimCells(e, tick);
        for (int k = 0; k < stack; k++)
            for (int dx = 0; dx < Import.CwpFile.PatternWidth; dx++)
                for (int dy = 0; dy < Import.CwpFile.PatternHeight; dy++)
                {
                    int code = BuildingCellTile(first, k, dx, dy, anim);
                    if (code == 0) continue;
                    map[(dx, dy)] = code;
                }
        return map;
    }

    /// <summary>
    /// Was dieselbe Zelle im ORIGINAL zeigt — die Vergleichsseite des
    /// Prüfstands, aus dem Maschinencode abgeschrieben und nicht aus unserem
    /// Zeichenweg.
    ///
    /// <para>Das Original hält je Kartenzelle EINE Kachel. Gestempelt wird in
    /// dieser Reihenfolge: das Grundmuster, darüber die Schadensauflagen
    /// (@0x4C97B4, ein Muster je Schadensstufe), und danach schreibt der
    /// Animationslauf (A 0x4D5830) bei JEDEM Bild seine Zelle — er kommt also
    /// zuletzt. Beim Überlauf schreibt er die Kachel des GRUNDMUSTERS zurück
    /// (Rücksetzzweig A 0x4D5A6D), nicht die der Auflage.</para>
    ///
    /// <para>Der Wächter A 0x4D58B4 könnte ihn davon abhalten — er verlangt,
    /// dass die Kartenkachel entweder die Musterkachel ist oder mindestens
    /// <c>Tiles[1]</c> der ERSTEN Zeile des Typs. An den Daten nachgerechnet
    /// (banim_re.py data): von den 151 Stellen, an denen eine Schadensauflage
    /// auf einer Animationszelle liegt, lässt der Wächter <b>151</b> durch.
    /// Kein Gegenbeispiel, deshalb steht er hier nicht im Weg.</para>
    /// </summary>
    private Dictionary<(int, int), int> BuildingCellsOriginal(Entity e, int first, int stack, int tick)
    {
        var map = new Dictionary<(int, int), int>();
        if (Patterns == null) return map;
        for (int k = 0; k < stack; k++)
            for (int dx = 0; dx < Import.CwpFile.PatternWidth; dx++)
                for (int dy = 0; dy < Import.CwpFile.PatternHeight; dy++)
                {
                    int code = Patterns.PatternTile(first + k, dx, dy);
                    if (code != 0) map[(dx, dy)] = code;
                }
        var bt = Patterns.GetBuildingType(e.BType);
        for (int k = 0; k < bt.AnimCount && k < AnimRowsPerBuilding; k++)
        {
            var a = Patterns.GetAnimRow(bt.AnimFirst + k);
            if (a.LastPhase <= 0 || a.Mode != 1) continue;
            int ph = tick % (a.LastPhase + 1);
            int code = ph == 0 ? Patterns.PatternTile(first, a.Dx, a.Dy) : a.TileAt(ph);
            if (code != 0) map[(a.Dx, a.Dy)] = code;
        }
        return map;
    }

    private sealed class BAnimRow
    {
        public int Slot, BType, Row, Dx, Dy, LastPhase, Mode;
        public readonly HashSet<int> Tiles = new();
        public int Changes;
        public int Last = int.MinValue;
    }

    private readonly Dictionary<(int, int), BAnimRow> _bAnimSeen = new();
    private float _bAnimTime;
    private int _bAnimSamples;

    /// <summary>Eine Probe je Bild: was zeigt jede Animationszelle GERADE, im
    /// laufenden Spiel. Ein Standbild kann man behaupten, eine Zählung nicht.
    /// </summary>
    public void BAnimSample()
    {
        if (Patterns == null) return;
        _bAnimSamples++;
        _bAnimTime = _clock;
        int tick = (int)(_clock * BuildingAnimFps);
        foreach (var e in _entities)
        {
            if (!e.IsBuilding || e.IsProp || e.Dead) continue;
            var bt = Patterns.GetBuildingType(e.BType);
            if (bt.AnimCount <= 0 || bt.FirstPattern < 0) continue;
            int stack = DamageFrame(e);
            var ours = BuildingCellsOurs(e, bt.FirstPattern, stack, tick);
            for (int k = 0; k < bt.AnimCount && k < AnimRowsPerBuilding; k++)
            {
                var a = Patterns.GetAnimRow(bt.AnimFirst + k);
                if (!_bAnimSeen.TryGetValue((e.Slot, k), out var s))
                    _bAnimSeen[(e.Slot, k)] = s = new BAnimRow
                    {
                        Slot = e.Slot, BType = e.BType, Row = k,
                        Dx = a.Dx, Dy = a.Dy, LastPhase = a.LastPhase, Mode = a.Mode,
                    };
                int code = ours.TryGetValue((a.Dx, a.Dy), out int c) ? c : 0;
                s.Tiles.Add(code);
                if (s.Last != int.MinValue && s.Last != code) s.Changes++;
                s.Last = code;
            }
        }
    }

    /// <summary>`--banim-demo` — jedes animierte Gebäude auf ein Viertel seiner
    /// Trefferpunkte setzen und so STEHEN LASSEN, damit ein Bildschirmfoto die
    /// Schadensstufe UND das laufende Band zeigt. Ohne das ist der Streitfall
    /// dieser Sitzung — Auflage gegen Animation — auf keiner ausgelieferten
    /// Karte zu sehen: dort steht jedes Gebäude auf voller Gesundheit.
    /// Der Prüfstand <see cref="BAnimCheck"/> fährt die Stufen selbst durch;
    /// dies hier ist nur für das Auge.</summary>
    public void BAnimDemo()
    {
        if (Patterns == null) return;
        foreach (var e in _entities)
        {
            if (!e.IsBuilding || e.IsProp || e.Dead || e.HpMax <= 0) continue;
            if (Patterns.GetBuildingType(e.BType).AnimCount <= 0) continue;
            e.Hp = Mathf.Max(1, e.HpMax / 4);
        }
        QueueRedraw();
    }

    /// <summary>
    /// `--banim-check` — der Prüfstand für die Zellanimation der Gebäude.
    ///
    /// <para>Er beantwortet drei Fragen mit Zahlen statt mit einem Screenshot:
    /// </para>
    /// <list type="number">
    /// <item><b>Zeigen wir, was das Original zeigen würde?</b> Jede
    /// Animationszelle jedes animierten Gebäudes wird über ALLE Phasen und über
    /// fünf Schadensstufen (100/75/50/25/1 %) gegen
    /// <see cref="BuildingCellsOriginal"/> gehalten — Kachelcode gegen
    /// Kachelcode, Zelle für Zelle, auch die nicht animierten.</item>
    /// <item><b>Läuft sie im laufenden Spiel wirklich?</b> Aus den Proben von
    /// <see cref="BAnimSample"/>: wie viele verschiedene Kacheln jede Zeile
    /// gezeigt hat (Soll: <c>LastPhase+1</c>) und wie oft sie gewechselt hat.
    /// </item>
    /// <item><b>Wie schnell?</b> Wechsel je Sekunde gegen
    /// <see cref="BuildingAnimFps"/>.</item>
    /// </list>
    /// </summary>
    public string BAnimCheck()
    {
        if (Patterns == null) return "banim-check: keine Muster geladen";
        var sb = new System.Text.StringBuilder();

        int animated = 0, rows = 0, cmp = 0, bad = 0, clash = 0, clashBad = 0;
        // ⚠ Nebenbefund, hier gezählt statt behauptet: der Kachel-Atlas trägt
        // für einen NICHT baubaren Typ nur Grundbild und Ruine
        // (BuildingPatterns.WriteAtlas sammelt alle Muster nur für die drei
        // baubaren Typen). Eine Fabrik ist nicht baubar — ihre Schadensauflagen
        // fehlen also und werden STILL nicht gezeichnet.
        int ovHave = 0, ovMiss = 0;
        var ovSeen = new HashSet<int>();
        var lens = new SortedDictionary<int, int>();
        var modes = new SortedDictionary<int, int>();
        var badEx = new List<string>();
        int[] stages = { 100, 75, 50, 25, 1 };

        foreach (var e in _entities)
        {
            if (!e.IsBuilding || e.IsProp || e.Dead) continue;
            var bt = Patterns.GetBuildingType(e.BType);
            if (bt.AnimCount <= 0 || bt.FirstPattern < 0) continue;
            animated++;
            if (ovSeen.Add(e.BType))
                for (int q = 1; q < bt.PatternCount - 1; q++)
                    for (int dx = 0; dx < Import.CwpFile.PatternWidth; dx++)
                        for (int dy = 0; dy < Import.CwpFile.PatternHeight; dy++)
                        {
                            int t = Patterns.PatternTile(bt.FirstPattern + q, dx, dy);
                            if (t == 0) continue;
                            if (Patterns.TryGetTile(t, out _)) ovHave++; else ovMiss++;
                        }
            int cyc = 1;
            for (int k = 0; k < bt.AnimCount && k < AnimRowsPerBuilding; k++)
            {
                var a = Patterns.GetAnimRow(bt.AnimFirst + k);
                rows++;
                lens[a.LastPhase + 1] = lens.GetValueOrDefault(a.LastPhase + 1) + 1;
                modes[a.Mode] = modes.GetValueOrDefault(a.Mode) + 1;
                cyc = Lcm(cyc, a.LastPhase + 1);
            }

            int save = e.Hp;
            foreach (int pct in stages)
            {
                e.Hp = Mathf.Max(1, e.HpMax * pct / 100);
                int stack = DamageFrame(e);
                // Wie oft trifft eine Schadensauflage eine Animationszelle? Das
                // ist die Stelle, an der die heutige Stapelei und das Original
                // auseinanderlaufen konnten.
                for (int k = 0; k < bt.AnimCount && k < AnimRowsPerBuilding; k++)
                {
                    var a = Patterns.GetAnimRow(bt.AnimFirst + k);
                    for (int q = 1; q < stack; q++)
                        if (Patterns.PatternTile(bt.FirstPattern + q, a.Dx, a.Dy) != 0) clash++;
                }
                for (int tick = 0; tick < cyc; tick++)
                {
                    var ours = BuildingCellsOurs(e, bt.FirstPattern, stack, tick);
                    var orig = BuildingCellsOriginal(e, bt.FirstPattern, stack, tick);
                    foreach (var kv in orig)
                    {
                        cmp++;
                        int got = ours.TryGetValue(kv.Key, out int g) ? g : 0;
                        if (got == kv.Value) continue;
                        bad++;
                        bool onAnim = false;
                        for (int k = 0; k < bt.AnimCount && k < AnimRowsPerBuilding; k++)
                        {
                            var a = Patterns.GetAnimRow(bt.AnimFirst + k);
                            if (a.Dx == kv.Key.Item1 && a.Dy == kv.Key.Item2) onAnim = true;
                        }
                        if (onAnim) clashBad++;
                        if (badEx.Count < 6)
                            badEx.Add($"slot {e.Slot} typ {e.BType} hp {pct}% Stufe {stack} " +
                                      $"Takt {tick} Zelle ({kv.Key.Item1},{kv.Key.Item2}): " +
                                      $"Original {kv.Value}, wir {got}" +
                                      (onAnim ? " [Animationszelle]" : ""));
                    }
                    foreach (var kv in ours)
                        if (!orig.ContainsKey(kv.Key))
                        {
                            cmp++; bad++;
                            if (badEx.Count < 6)
                                badEx.Add($"slot {e.Slot} typ {e.BType} Takt {tick} " +
                                          $"Zelle ({kv.Key.Item1},{kv.Key.Item2}) hat bei uns " +
                                          $"Kachel {kv.Value}, im Original keine");
                        }
                }
            }
            e.Hp = save;
        }

        sb.Append($"banim-check: {animated} Gebaeude mit Animation, {rows} Zeilen ");
        sb.Append($"(Umlauflaenge {string.Join(", ", Fmt(lens))}; Modus {string.Join(", ", Fmt(modes))})");
        sb.Append($"\n   Abgleich gegen das Original ueber alle Takte x 5 Schadensstufen: ");
        sb.Append($"{cmp} Zellvergleiche, {bad} Abweichungen");
        sb.Append($"\n   Schadensauflagen animierter Typen im Kachel-Atlas: {ovHave} von {ovHave + ovMiss}");
        if (ovMiss > 0)
            sb.Append(" — ! die fehlenden werden STILL nicht gezeichnet "
                      + "(WriteAtlas sammelt alle Muster nur fuer die baubaren Typen)");
        sb.Append($"\n   Abweichungen auf Animationszellen: {clashBad} — ");
        sb.Append($"{clash} mal liegt eine Schadensauflage auf einer Animationszelle ");
        sb.Append("(dort gewinnt im Original die Animation, 151 von 151 an den .CWP nachgerechnet)");
        foreach (string x in badEx) sb.Append($"\n      ! {x}");

        if (_bAnimSamples == 0)
        {
            sb.Append("\n   live: nicht abgetastet (--banim-check braucht Laufzeit)");
            return sb.ToString();
        }
        int full = 0, frozen = 0, part = 0, changes = 0;
        foreach (var s in _bAnimSeen.Values)
        {
            changes += s.Changes;
            if (s.Tiles.Count >= s.LastPhase + 1) full++;
            else if (s.Tiles.Count <= 1) frozen++;
            else part++;
        }
        float secs = Mathf.Max(0.001f, _bAnimTime);
        sb.Append($"\n   live: {_bAnimSamples} Proben in {secs:0.0}s ueber {_bAnimSeen.Count} Zeilen — ");
        sb.Append($"{full} zeigen JEDE Phase, {part} nur einen Teil, {frozen} stehen fest");
        sb.Append($"\n   Takt: {changes} Wechsel = {changes / secs / Mathf.Max(1, _bAnimSeen.Count):0.00} ");
        sb.Append($"Phasen/s je Zeile (unsere Setzung: {BuildingAnimFps:0.#})");
        int shown = 0;
        foreach (var s in _bAnimSeen.Values)
        {
            if (shown++ >= 6) break;
            sb.Append($"\n      slot {s.Slot} typ {s.BType} Zeile {s.Row} Zelle ({s.Dx},{s.Dy}) " +
                      $"Modus {s.Mode}, {s.LastPhase + 1} Phasen: {s.Tiles.Count} verschiedene " +
                      $"Kacheln [{string.Join(",", s.Tiles)}], {s.Changes} Wechsel");
        }
        return sb.ToString();
    }

    private static int Lcm(int a, int b)
    {
        int x = a, y = b;
        while (y != 0) { int t = x % y; x = y; y = t; }
        return x == 0 ? b : a / x * b;
    }

    private static IEnumerable<string> Fmt(SortedDictionary<int, int> d)
    {
        foreach (var kv in d) yield return $"{kv.Key}x{kv.Value}";
    }

    private readonly Dictionary<int, Texture2D?> _doorTex = new();

    private Texture2D? DoorTexture(int pic)
    {
        if (pic < 0 || pic >= Import.BuildingPatterns.DoorPictureCount) return null;
        if (_doorTex.TryGetValue(pic, out var cached)) return cached;
        string path = Core.Content.Path($"Effects/door/f{pic:00}.png");
        Texture2D? tex = null;
        if (FileAccess.FileExists(path))
        {
            var img = Image.LoadFromFile(ProjectSettings.GlobalizePath(path));
            if (img != null) tex = ImageTexture.CreateFromImage(img);
        }
        _doorTex[pic] = tex;
        return tex;
    }

    /// <summary>
    /// `--group-check` — pick every mobile unit of the viewed player, order them
    /// all to one cell the way a right-click does, and report how many actually
    /// got a route. Answers "why does only part of my group drive off" with a
    /// number instead of an impression.
    /// </summary>
    public string GroupMoveCheck()
    {
        if (_nav == null) return "group-check: kein Gitter";
        _sel.Clear();
        for (int i = 0; i < _entities.Count; i++)
        {
            var e = _entities[i];
            if (e.IsBuilding || e.IsProp || e.Dead || !e.Mobile) continue;
            if (e.Owner != ViewPlayer) continue;
            _sel.Add(i);
        }
        if (_sel.Count == 0) return "group-check: nichts Bewegliches beim betrachteten Spieler";

        // a goal far enough away that everyone has to travel
        int firstIdx = -1;
        foreach (int k in _sel) { firstIdx = k; break; }
        var first = _entities[firstIdx];
        var goal = new Vector2I(Mathf.Clamp(first.Col + 20, 0, _nav.Width - 1),
                                Mathf.Clamp(first.Row + 20, 0, _nav.Height - 1));
        var sb = new System.Text.StringBuilder();
        sb.Append($"group-check: {_sel.Count} Einheiten von Spieler {ViewPlayer} " +
                  $"-> ({goal.X},{goal.Y})\n");

        // how many find a route on their own, before any goal juggling
        int solo = 0, blockedByOwn = 0;
        foreach (int i in _sel)
        {
            var e = _entities[i];
            if (_nav.FindPath(new Vector2I(e.Col, e.Row), goal, e.Move, i) != null) solo++;
            else
            {
                // is it our own group that is in the way? try again with every
                // selected unit's cell cleared
                var saved = new List<(int C, int R, int Occ)>();
                foreach (int k in _sel)
                    if (k != i)
                    {
                        var o = _entities[k];
                        saved.Add((o.Col, o.Row, _nav.OccupantAt(o.Col, o.Row)));
                        _nav.ClearOccupant(o.Col, o.Row, k);
                    }
                if (_nav.FindPath(new Vector2I(e.Col, e.Row), goal, e.Move, i) != null)
                    blockedByOwn++;
                foreach (var (c, r, occ) in saved)
                    if (occ >= 0) _nav.SetOccupant(c, r, occ, _entities[occ].Infantry >= 0);
            }
        }
        sb.Append($"   Weg allein gefunden: {solo}/{_sel.Count}; " +
                  $"davon zusaetzlich moeglich, wenn die EIGENE Gruppe nicht im Weg " +
                  $"staende: {blockedByOwn}\n");

        IssueMove(CellCenter(goal.X, goal.Y));
        int withPath = 0;
        foreach (int i in _sel) if (_entities[i].Path != null) withPath++;
        sb.Append($"   nach dem Befehl: {withPath}/{_sel.Count} haben einen Pfad — " +
                  $"Meldung: {_order}");
        return sb.ToString();
    }

    /// <summary>
    /// `--sound-check` — was ein Klang an seiner Stelle noch wiegt.
    ///
    /// Fragt nicht die Formel ab (die stünde nur zweimal da), sondern geht die
    /// Einheiten der geladenen Karte durch und meldet für jede, wie weit sie vom
    /// Bildmittelpunkt weg ist und um wieviel Dezibel ihr Schuss darum leiser
    /// wird. Die Probe ist die SPANNE: solange die weiteste Einheit genauso laut
    /// ist wie die nächste, ist die Dämpfung nicht angeschlossen.
    /// </summary>
    public string SoundDistanceCheck()
    {
        var listener = Audio.SoundBankPlayer.ListenerCell;
        var sb = new System.Text.StringBuilder();
        sb.Append($"sound-check: Ohr auf Zelle ({listener.X:0.0},{listener.Y:0.0}), " +
                  $"{Audio.SoundBankPlayer.DistanceFactor / 100f:0.00} dB je Zelle " +
                  $"(still ab {10000 / Audio.SoundBankPlayer.DistanceFactor:0} Zellen)\n");
        if (float.IsNaN(listener.X)) { sb.Append("   kein Ohr gesetzt — nichts wird gedaempft"); return sb.ToString(); }

        float near = float.MaxValue, far = -1f, nearDb = 0, farDb = 0;
        int n = 0, silent = 0;
        foreach (var e in _entities)
        {
            if (e.IsProp || e.Dead) continue;
            n++;
            float dx = e.Col - listener.X, dy = e.Row - listener.Y;
            float d = Mathf.Sqrt(dx * dx + dy * dy);
            float db = Audio.SoundBankPlayer.DistanceDb(e.Col, e.Row);
            if (db <= -100f) silent++;
            if (d < near) { near = d; nearDb = db; }
            if (d > far) { far = d; farDb = db; }
        }
        if (n == 0) return sb.Append("   keine Einheit auf der Karte").ToString();
        sb.Append($"   {n} Objekte: naechstes {near:0.0} Zellen -> {nearDb:0.0} dB, " +
                  $"weitestes {far:0.0} Zellen -> {farDb:0.0} dB, " +
                  $"{silent} davon still (>= 100 Zellen)\n");
        sb.Append($"   Spanne {(nearDb - farDb):0.0} dB — " +
                  (Mathf.Abs(nearDb - farDb) < 0.05f
                      ? "NICHT ANGESCHLOSSEN (alles gleich laut)"
                      : "Daempfung wirkt"));
        return sb.ToString();
    }

    /// <summary>
    /// `--infdeath-check` — kill one foot soldier and follow him, tick by tick:
    /// the death flag, the clock, the animation block that comes out of it and
    /// whether that block has a texture. Everything the drawing of a fallen
    /// soldier depends on, in one line each.
    /// </summary>
    public string InfantryDeathCheck()
    {
        if (_nav == null) return "infdeath-check: kein Gitter";
        int fi = -1;
        for (int i = 0; i < _entities.Count; i++)
            if (_entities[i].Infantry >= 0 && !_entities[i].Dead) { fi = i; break; }
        if (fi < 0) return "infdeath-check: keine Infanterie auf dieser Karte";

        var e = _entities[fi];
        var sb = new System.Text.StringBuilder();
        sb.Append($"infdeath-check: slot {e.Slot}, Satz {e.Infantry}, Richtung {e.Facing}, " +
                  $"Spieler {e.Owner}, HP {e.Hp}/{e.HpMax}\n");
        sb.Append($"   vor dem Tod: Dead={e.Dead}, Block {InfBlock(e)}, " +
                  $"Textur={(GetInfantryTexture(e.Infantry, e.Facing, InfBlock(e)) != null ? "ok" : "FEHLT")}\n");

        Kill(fi, e);
        sb.Append($"   nach Kill(): Dead={e.Dead}, DeadTime={e.DeadTime:0.00}, " +
                  $"Belegung frei={(_nav.OccupantAt(e.Col, e.Row) < 0 ? "ja" : "NEIN")}\n");

        // walk the clock the way _Process does for a dead entity
        for (int step = 0; step <= 5; step++)
        {
            int b = InfBlock(e);
            var tex = GetInfantryTexture(e.Infantry, e.Facing, b);
            sb.Append($"   t={e.DeadTime:0.00}s -> Block {b}, " +
                      $"Textur={(tex != null ? $"{tex.GetWidth()}x{tex.GetHeight()}" : "FEHLT")}\n");
            e.DeadTime += 0.15f;
        }

        // Der Fall, der den Fehler gemacht hat: ein Treffer auf die Leiche.
        // Vor dem 11.08.2026 lief er bis Kill() durch und setzte DeadTime auf
        // 0 -- der Tote stand auf und kippte erneut. Hier wird genau das
        // geprueft, und zwar ueber beide Wege, die den Schutz nicht hatten:
        // direkter Beschuss (ApplyHit) und Luftangriff (derselbe Aufruf).
        float was = e.DeadTime;
        ApplyHit(-1, fi, e, 50);
        sb.Append($"   Treffer auf die Leiche: DeadTime {was:0.00} -> {e.DeadTime:0.00} " +
                  $"({(Mathf.IsEqualApprox(was, e.DeadTime) ? "unveraendert, richtig" : "ZURUECKGESETZT — der Tote steht wieder auf")}), " +
                  $"HP {e.Hp}\n");

        // and the two gates the draw loop puts in front of it
        bool fogGate = FogActive && e.Owner != ViewPlayer && !e.IsBuilding && !Watched(e.Col, e.Row);
        sb.Append($"   Zeichen-Tore: _drawSprites={_drawSprites}, " +
                  $"Nebel blockt={(fogGate ? "JA — wird gar nicht gezeichnet" : "nein")}, " +
                  $"Infantry={e.Infantry} (>=0 noetig)");
        return sb.ToString();
    }

    /// <summary>
    /// `--crush-check` — why a foot soldier is or is not run over, condition by
    /// condition, on the map as loaded. Every step the move code takes is asked
    /// separately, so a failure names itself instead of having to be guessed.
    /// </summary>
    public string CrushCheck()
    {
        if (_nav == null) return "crush-check: kein Gitter";
        var sb = new System.Text.StringBuilder();

        var vehicles = new List<int>();
        var feet = new List<int>();
        for (int i = 0; i < _entities.Count; i++)
        {
            var e = _entities[i];
            if (e.IsBuilding || e.IsProp || e.Dead) continue;
            if (e.Infantry >= 0) feet.Add(i);
            else if (e.Mobile) vehicles.Add(i);
        }
        sb.Append($"crush-check: {vehicles.Count} Fahrzeuge, {feet.Count} Infanteristen\n");
        if (feet.Count == 0 || vehicles.Count == 0) return sb.ToString().TrimEnd();

        int shown = 0, hostilePairs = 0, gridOk = 0, freeOk = 0;
        foreach (int fi in feet)
        {
            var foot = _entities[fi];
            // is the cell marked as a foot soldier at all?
            bool crush = _nav.CrushableAt(foot.Col, foot.Row, -1) == fi;
            if (crush) gridOk++;
            // and does a vehicle of another owner consider him an enemy?
            int driver = -1;
            foreach (int vi in vehicles)
                if (IsHostile(_entities[vi], foot)) { driver = vi; break; }
            if (driver >= 0) hostilePairs++;
            bool free = _nav.IsFree(foot.Col, foot.Row, Simulation.NavGrid.MoveClass.Vehicle, -1);
            if (free) freeOk++;

            if (shown < 6)
            {
                shown++;
                sb.Append($"   Inf slot {foot.Slot} (Spieler {foot.Owner}) bei " +
                          $"({foot.Col},{foot.Row}): Zelle-als-Fussvolk={(crush ? "ja" : "NEIN")}, " +
                          $"befahrbar={(free ? "ja" : "NEIN")}, " +
                          $"Feind-Fahrzeug={(driver >= 0 ? $"slot {_entities[driver].Slot}" : "KEINS")}");
                if (driver >= 0)
                {
                    var v = _entities[driver];
                    var path = _nav.FindPath(new Vector2I(v.Col, v.Row),
                                             new Vector2I(foot.Col, foot.Row), v.Move, driver);
                    sb.Append($", Weg dorthin={(path == null ? "KEINER" : $"{path.Count} Schritte")}");
                }
                sb.Append('\n');
            }
        }
        sb.Append($"   zusammen: {gridOk}/{feet.Count} korrekt als Fussvolk im Gitter, " +
                  $"{freeOk}/{feet.Count} befahrbar, {hostilePairs}/{feet.Count} mit einem " +
                  $"feindlichen Fahrzeug auf der Karte");
        return sb.ToString();
    }

    /// <summary>
    /// Harness: ask the grid for a route and report it, cell by cell, together
    /// with the ground class of every step. Used to show that a vehicle really
    /// crosses a bridge and really refuses the water beside it — a script cannot
    /// click, so the route has to be quoted.
    /// </summary>
    public void NavProbe(string spec)
    {
        if (_nav == null) { GD.Print("nav-probe: kein Gitter"); return; }
        var p = spec.Split(',');
        if (p.Length < 4) { GD.Print("nav-probe: c0,r0,c1,r1[,klasse]"); return; }
        var a = new Vector2I(p[0].ToInt(), p[1].ToInt());
        var b = new Vector2I(p[2].ToInt(), p[3].ToInt());
        var mc = p.Length > 4
            ? System.Enum.Parse<Simulation.NavGrid.MoveClass>(p[4], true)
            : Simulation.NavGrid.MoveClass.Vehicle;

        string Cell(Vector2I v) => $"({v.X},{v.Y}){TerrainName(_nav.GroundAt(v.X, v.Y))[..1]}";
        var path = _nav.FindPath(a, b, mc);
        if (path == null) { GD.Print($"nav-probe {mc} {Cell(a)} -> {Cell(b)}: KEIN WEG"); return; }

        var hist = new Dictionary<Simulation.NavGrid.Ground, int>();
        foreach (var v in path)
        {
            var g = _nav.GroundAt(v.X, v.Y);
            hist[g] = hist.TryGetValue(g, out int n) ? n + 1 : 1;
        }
        var parts = new List<string>();
        foreach (var kv in hist) parts.Add($"{TerrainName(kv.Key)}={kv.Value}");
        GD.Print($"nav-probe {mc} {Cell(a)} -> {Cell(b)}: {path.Count} Schritte, " +
                 string.Join(" ", parts));
        var line = new List<string>();
        foreach (var v in path) line.Add($"{v.X},{v.Y}");
        GD.Print("   Weg: " + string.Join(" ", line));
    }

    /// <summary>
    /// Harness: hold the grid against the picture. For every cell the imap calls
    /// water, look at the pixel the layer would place a unit on and ask whether
    /// it is blue. The two are produced by different code — the baker draws the
    /// tiles, the layer places the units — so agreement between them is a real
    /// check on the drawing origin, and a disagreement is exactly the fault that
    /// put the ships of mission 1 on the beach.
    /// </summary>
    public string GroundCheck(Image img)
    {
        if (_nav == null || img == null) return "ground-check: kein Gitter";
        int water = 0, blue = 0, off = 0, outside = 0;
        for (int r = 0; r < _nav.Height; r++)
            for (int c = 0; c < _nav.Width; c++)
            {
                if (_nav.GroundAt(c, r) != Simulation.NavGrid.Ground.Water) continue;
                water++;
                var p = CellCenter(c, r);
                int x = Mathf.RoundToInt(p.X), y = Mathf.RoundToInt(p.Y);
                if (x < 0 || y < 0 || x >= img.GetWidth() || y >= img.GetHeight()) { outside++; continue; }
                var col = img.GetPixel(x, y);
                if (col.B > col.G + 0.06f && col.B > col.R + 0.06f) blue++; else off++;
            }
        return $"ground-check: {water} Wasserzellen, {blue} unter blauem Pixel, " +
               $"{off} daneben, {outside} ausserhalb des Bildes";
    }

    /// <summary>Rubber-band select: every owned, non-prop entity inside the box.</summary>
    public void BoxSelect(Rect2 mapRect, bool additive)
    {
        if (!additive) _sel.Clear();
        var norm = mapRect.Abs();
        for (int i = 0; i < _entities.Count; i++)
        {
            if (!Commandable(i)) continue;
            if (norm.Intersects(BodyRect(_entities[i]))) _sel.Add(i);
        }
        SetPrimary();
        _band = null;
        QueueRedraw();
    }

    public void SetBand(Rect2? mapRect) { _band = mapRect; QueueRedraw(); }

    public void ClearSelection() { _sel.Clear(); SetPrimary(); QueueRedraw(); }

    // ---- move orders ----

    /// <summary>
    /// Order every selected mobile unit to drive to (or next to) the clicked cell.
    /// Units get distinct goal cells so they don't pile up on one tile.
    /// </summary>
    public void IssueMove(Vector2 mapPos, bool queue = false)
    {
        if (_nav == null) return;
        var cell = CellAt(mapPos);
        if (cell == null) { _order = "outside the map"; return; }

        // Shift appends instead of replacing: a unit that is already on its way
        // keeps that order and takes this one afterwards.
        if (queue)
        {
            int q = 0;
            foreach (int i in _sel)
            {
                var e = _entities[i];
                if (!e.Mobile || e.Dead || e.DugIn) continue;
                if (e.Path == null && e.Orders.Count == 0) continue;   // idle: order it outright
                if (e.Orders.Count >= MaxOrders) continue;
                e.Orders.Add(Order.Move(cell.Value));
                q++;
            }
            if (q > 0)
            {
                AddOrderMark(CellCenter(cell.Value.X, cell.Value.Y), attack: false);
                _order = $"angereiht -> ({cell.Value.X},{cell.Value.Y}): {q} Einheit(en)";
                UpdatePanel();
                QueueRedraw();
                return;
            }
            // nobody had anything to append to — fall through and order normally
        }

        int ordered = 0, failed = 0, dry = 0;
        var taken = new HashSet<Vector2I>();
        foreach (int i in _sel)
        {
            var e = _entities[i];
            if (!e.Mobile || e.Dead || e.DugIn) continue;   // dug in = holds position
            e.Target = -1;                      // a move order cancels the attack
            if (!queue) e.Orders.Clear();       // a plain order replaces the queue

            // a dry tank goes nowhere — the original stops the unit at zero
            if (e.FuelMax > 0 && e.Fuel <= 0) { dry++; continue; }

            // pick a free goal cell near the click that no other unit in this
            // order is already heading for
            Vector2I? goal = null;
            for (int rad = 0; rad <= 8 && goal == null; rad++)
                for (int dy = -rad; dy <= rad && goal == null; dy++)
                    for (int dx = -rad; dx <= rad && goal == null; dx++)
                    {
                        if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) != rad) continue;
                        var c = new Vector2I(cell.Value.X + dx, cell.Value.Y + dy);
                        if (taken.Contains(c)) continue;
                        if (_nav.IsFree(c.X, c.Y, e.Move, i)) goal = c;
                    }
            if (goal == null) { failed++; continue; }

            var path = _nav.FindPath(new Vector2I(e.Col, e.Row), goal.Value, e.Move, i);
            if (path == null || path.Count == 0) { failed++; continue; }

            taken.Add(goal.Value);
            e.Path = path;
            e.PathIdx = 0;
            e.Goal = goal.Value;
            e.Reserved = null;
            e.WaitTime = 0;
            ordered++;
        }
        if (ordered > 0) AddOrderMark(CellCenter(cell.Value.X, cell.Value.Y), attack: false);
        _order = ordered > 0
            ? $"move -> ({cell.Value.X},{cell.Value.Y}): {ordered} unit(s)" +
              (failed > 0 ? $", {failed} no route" : "") +
              (dry > 0 ? $", {dry} ohne Sprit" : "")
            : failed > 0 ? "no route (water / blocked)"
            : dry > 0 ? "kein Sprit" : "nothing mobile selected";
        UpdatePanel();
        QueueRedraw();
    }

    public void StopSelected()
    {
        foreach (int i in _sel)
        {
            var e = _entities[i];
            if (e.Reserved is { } r) _nav?.ClearOccupant(r.X, r.Y, i);
            e.Path = null;
            e.Reserved = null;
            e.Target = -1;
            e.Orders.Clear();          // stop means stop, queue and all
        }
        _order = "stop";
        QueueRedraw();
    }

    // ---- the order queue ---------------------------------------------------
    //
    // OURS. The original takes one order at a time — there is no queue field in
    // an entity record and no code that walks one. It is added because a map
    // ten thousand pixels across makes a single order tedious, and it is kept
    // deliberately thin: a list of destination cells, taken one after another,
    // cleared by any plain order and by Stop.

    /// <summary>How many orders a unit will remember. Ours.</summary>
    public const int MaxOrders = 8;

    /// <summary>One queued order: drive somewhere, or attack something. A move
    /// carries the cell and no target; an attack carries the target's index and
    /// the cell it stood on, so the mark can be drawn where it was given.</summary>
    public readonly struct Order
    {
        private Order(Vector2I cell, int target) { Cell = cell; Target = target; }
        public static Order Move(Vector2I cell) => new(cell, -1);
        public static Order Attack(Vector2I cell, int target) => new(cell, target);
        public Vector2I Cell { get; }
        /// <summary>Entity index to attack, -1 for a plain move.</summary>
        public int Target { get; }
        public bool IsAttack => Target >= 0;
    }

    /// <summary>A unit reached its destination: start the next queued order, if
    /// there is one.</summary>
    private void NextQueued(int i, Entity e)
    {
        if (_nav == null || e.Orders.Count == 0) return;
        var next = e.Orders[0];
        e.Orders.RemoveAt(0);

        if (next.IsAttack)
        {
            // the target may have died while the unit was driving; in that case
            // skip straight on to whatever was queued behind it
            if (next.Target < _entities.Count && !_entities[next.Target].Dead &&
                CanFight(e) && IsHostile(e, _entities[next.Target]))
            {
                e.Target = next.Target;
                e.Ordered = true;
                e.Path = null;
                e.Reserved = null;
                return;
            }
            NextQueued(i, e);
            return;
        }

        var path = _nav.FindPath(new Vector2I(e.Col, e.Row), next.Cell, e.Move, i);
        if (path == null || path.Count == 0)
        {
            // unreachable now — drop it and try whatever comes after
            NextQueued(i, e);
            return;
        }
        e.Path = path;
        e.PathIdx = 0;
        e.Goal = next.Cell;
        e.Reserved = null;
        e.WaitTime = 0;
    }

    /// <summary>How many orders the given unit still has queued — for the
    /// harness, and for anything that wants to report the queue.</summary>
    public int QueuedFor(int index)
        => index >= 0 && index < _entities.Count ? _entities[index].Orders.Count : 0;

    public void ToggleNav() { _showNav = !_showNav; QueueRedraw(); }

    /// <summary>Use the original FONT.CWD typeface for the info panel.</summary>
    public void SetUiFont(Font font, int size)
    {
        _uiFont = font;
        _uiFontSize = size;
        _panel.AddThemeFontOverride("font", font);
        _panel.AddThemeFontSizeOverride("font_size", size);
        _panel.AddThemeConstantOverride("line_spacing", 0);
    }

    /// <summary>
    /// Place the info text inside the recessed display box of the original
    /// PANEL.DTA frame (screen coordinates, supplied by MapViewer).
    /// </summary>
    /// <summary>The info text and the production list share the display box, so
    /// one steps aside for the other.</summary>
    public void SetPanelTextVisible(bool on)
    {
        _panelTextOn = on;
        _panel.Visible = on;
    }

    private bool _panelTextOn = true;

    public void SetPanelBox(Rect2 box)
    {
        _compactPanel = true;
        _panel.Position = box.Position + new Vector2(4, 3);
        _panel.Size = box.Size - new Vector2(8, 6);
        _panel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _panel.ClipText = true;
        _panel.AddThemeConstantOverride("outline_size", 0);
    }

    private Font? _uiFont;
    private int _uiFontSize = 13;
    private bool _compactPanel;      // text is inside the original panel frame
    private string _mission = "";

    // ================= Step B: combat ==========================================

    private static void LoadBuildingNames()
    {
        if (_bldNames != null) return;
        _bldNames = new Dictionary<int, string>();
        string path = Core.Content.Path("Maps/building_types.json");
        if (!FileAccess.FileExists(path)) return;
        using var f = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        if (f == null) return;
        var json = new Json();
        if (json.Parse(f.GetAsText()) != Error.Ok || json.Data.VariantType != Variant.Type.Dictionary)
            return;
        var root = json.Data.AsGodotDictionary<string, Variant>();
        if (!root.TryGetValue("types", out var tv) || tv.VariantType != Variant.Type.Dictionary)
            return;
        foreach (var kv in tv.AsGodotDictionary<string, Variant>())
        {
            if (!int.TryParse(kv.Key, out int t) || kv.Value.VariantType != Variant.Type.Dictionary)
                continue;
            var d = kv.Value.AsGodotDictionary<string, Variant>();
            string nm = d.TryGetValue("name", out var nv) ? nv.AsString() : "";
            if (nm.Length > 0) _bldNames[t] = nm;
            // hit points and doors of a NEW building, straight out of the
            // exe's own 10-byte stat row (see ExeTables.BuildingStats)
            if (d.TryGetValue("hp", out var hv) && hv.AsInt32() > 0)
                _bldHp![t] = hv.AsInt32();
            if (d.TryGetValue("door_cells", out var dcv) &&
                dcv.VariantType == Variant.Type.Array)
            {
                var list = new List<Vector2I>();
                foreach (var e in dcv.AsGodotArray())
                {
                    if (e.VariantType != Variant.Type.Dictionary) continue;
                    var dd = e.AsGodotDictionary<string, Variant>();
                    list.Add(new Vector2I(
                        dd.TryGetValue("col", out var cv2) ? cv2.AsInt32() : 0,
                        dd.TryGetValue("row", out var rv2) ? rv2.AsInt32() : 0));
                }
                _bldDoors![t] = list;
            }
            // where the type watches from — a table in the exe, not a guess
            if (d.TryGetValue("sight_col", out var scv) && d.TryGetValue("sight_row", out var srv)
                && (scv.AsInt32() > 0 || srv.AsInt32() > 0))
                _bldSight![t] = new Vector2I(scv.AsInt32(), srv.AsInt32());
        }
        if (root.TryGetValue("sight_radius", out var rv3) && rv3.AsInt32() > 0)
            BuildingSightRadius = rv3.AsInt32();
    }

    private static Dictionary<int, int>? _bldHp = new();
    private static Dictionary<int, List<Vector2I>>? _bldDoors = new();
    private static Dictionary<int, Vector2I>? _bldSight = new();

    /// <summary>
    /// How far a building sees, in cells.
    ///
    /// <para><b>Measured 08.08.2026</b>, where it used to be our 6: the fog
    /// update @0x4206AB pushes a literal <c>0xa</c> in front of the stamper, the
    /// same for every type. Overwritten from <c>building_types.json</c> when the
    /// catalogue carries the value, so an older export still runs.</para>
    /// </summary>
    public static int BuildingSightRadius { get; private set; } = Import.ExeTables.BuildingSightRadius;

    /// <summary>The doors a new building of this type gets, as offsets from its
    /// origin. Empty when the catalogue predates the stat table.</summary>
    public static List<Vector2I> BuildingDoors(int type)
    {
        LoadBuildingNames();
        return _bldDoors != null && _bldDoors.TryGetValue(type, out var d)
               ? d : new List<Vector2I>();
    }

    private static string BuildingTypeName(int type)
        => _bldNames != null && _bldNames.TryGetValue(type, out var n) ? n : $"Typ {type}";

    // ---- infantry designs ----
    //
    // The 24 sprite sets are twelve pairs, and the design list holds exactly
    // twelve designs on propulsion 148/149.  `set / 2` is the design's place in
    // that list — strongly supported (7 of 9 propulsion agreements, 9 of 12
    // agreements between "this design has a damaging weapon" and "this sprite
    // set actually shows a muzzle flash"), not proven.  A design's weapon value
    // is the stats ROW directly, as with equipment, not the turrets' +20.
    private sealed class InfDesign
    {
        public string Name = "", WeaponName = "";
        public int Damage, RangeRaw, WeaponRow;
    }

    private static Dictionary<int, InfDesign>? _infDesigns;   // by set index

    // The weapons of the foot soldiers are registered under synthetic component
    // ids so the existing WeaponOf() keeps working; 21..38 are the turrets.
    private const int InfCompBase = 200;

    // The infantry rows carry ranges an order of magnitude below the vehicles'
    // (2..15 against 50..90), so the same /10 tile scaling would leave a rifle
    // at a fifth of a tile.  The floor is OURS.
    private const float InfMinRange = 2.5f;

    // Nowhere in the data: infantry hit points.  Stats rows 148/149 hold
    // hp_max 0 and every placed soldier has hp = hp_max = 0.  OURS.
    private const int InfHpLight = 60, InfHpHeavy = 90;

    private static void LoadInfantryDesigns()
    {
        if (_infDesigns != null) return;
        _infDesigns = new Dictionary<int, InfDesign>();
        string path = Core.Content.Path("Maps/infantry.json");
        if (!FileAccess.FileExists(path)) return;
        using var f = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        if (f == null) return;
        var json = new Json();
        if (json.Parse(f.GetAsText()) != Error.Ok ||
            json.Data.VariantType != Variant.Type.Dictionary) return;
        var root = json.Data.AsGodotDictionary<string, Variant>();
        if (!root.TryGetValue("designs", out var dv) ||
            dv.VariantType != Variant.Type.Array) return;
        LoadWeapons();
        foreach (var item in dv.AsGodotArray())
        {
            if (item.VariantType != Variant.Type.Dictionary) continue;
            var d = item.AsGodotDictionary<string, Variant>();
            var des = new InfDesign
            {
                Name = d.TryGetValue("name", out var nv) ? nv.AsString() : "",
                WeaponName = d.TryGetValue("weapon_name", out var wn) ? wn.AsString() : "",
                Damage = GetI(d, "damage"),
                RangeRaw = GetI(d, "range_raw"),
                WeaponRow = GetI(d, "weapon_row"),
            };
            if (des.Damage > 0 && _weapons != null)
                _weapons[InfCompBase + des.WeaponRow] =
                    (des.WeaponName.Length > 0 ? des.WeaponName : des.Name,
                     des.Damage,
                     Mathf.Max(des.RangeRaw / 10f, InfMinRange));
            if (!d.TryGetValue("sets", out var sv) || sv.VariantType != Variant.Type.Array)
                continue;
            foreach (var s in sv.AsGodotArray()) _infDesigns[s.AsInt32()] = des;
        }
    }

    private static void LoadWeapons()
    {
        if (_weapons != null) return;
        _weapons = new Dictionary<int, (string, int, float)>();
        string path = Core.Content.Path("Maps/weapons.json");
        if (!FileAccess.FileExists(path)) return;
        using var f = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        if (f == null) return;
        var json = new Json();
        if (json.Parse(f.GetAsText()) != Error.Ok || json.Data.VariantType != Variant.Type.Dictionary)
            return;
        var root = json.Data.AsGodotDictionary<string, Variant>();
        if (!root.TryGetValue("weapons", out var wv) || wv.VariantType != Variant.Type.Dictionary)
            return;
        foreach (var kv in wv.AsGodotDictionary<string, Variant>())
        {
            if (!int.TryParse(kv.Key, out int comp) ||
                kv.Value.VariantType != Variant.Type.Dictionary) continue;
            var w = kv.Value.AsGodotDictionary<string, Variant>();
            _weapons[comp] = (w.TryGetValue("name", out var nv) ? nv.AsString() : "?",
                              GetI(w, "damage", 10),
                              w.TryGetValue("range_tiles", out var rv) ? (float)rv.AsDouble() : 5f);
        }
    }

    /// <summary>Stats of a unit's mounted weapon; unknown components fall back
    /// to the basic MG (the campaign's most common weapon).</summary>
    /// <summary>How far this unit can shoot.
    ///
    /// The unit's own value wins: entity +0x2c is the range in tiles, summed by
    /// the design over propulsion, weapon and equipment, and it is what the
    /// original's panel prints as "Reichw.". The weapon table's figure — our own
    /// `range_raw / 10` — only stands in where a unit carries no such value.
    /// </summary>
    private static float RangeOf(Entity e)
        => e.Range > 0 ? e.Range : WeaponOf(e.Weapon).RangeTiles;

    /// <summary>Stats of a unit's mounted weapon.
    ///
    /// ⚠ CORRECTED 2026-08-06: this used to fall back to component 24 for
    /// anything the table did not name, and since the table only held six of
    /// the game's 50 components, 890 of the 1446 armed units on the maps (61 %)
    /// were labelled "2x Maschinengewehr". The table is read whole now, which
    /// leaves exactly one component unnamed — 61, on two units — and that one
    /// is SHOWN as a gap rather than borrowed from somebody else.</summary>
    private static (string Name, int Damage, float RangeTiles) WeaponOf(int comp)
    {
        if (_weapons != null && _weapons.TryGetValue(comp, out var w)) return w;
        return ($"BAUTEIL {comp}", 10, 5f);
    }

    // Entities with hp_max 0 (unit_types 148/149, the "Leichter"/"Schwerer" size
    // classes) are scenery markers, not combatants — they neither shoot nor can
    // be shot at.
    private static bool CanFight(Entity e)
        => !e.IsProp && !e.Dead && e.Weapon != 0 && e.HpMax > 0;

    /// <summary>Rounds left. A unit whose maximum is 0 carries no ammunition at
    /// all — infantry and the unarmed rows read 0/0 in every map file — and
    /// fires without limit. The original gates the shot itself (@0x40bb44) and
    /// takes one round off per shot (@0x40c587); a dry unit keeps its target,
    /// it just stops shooting.</summary>
    private static bool HasAmmo(Entity e) => e.AmmoMax <= 0 || e.Ammo > 0;

    /// <summary>Ammunition capacity per weapon component, learned from the map's
    /// own placements (the maximum is constant per weapon there: 21 -> 80,
    /// 24 -> 100, ...). Used to arm units the factories build, which have no
    /// record of their own to read it from.</summary>
    private readonly Dictionary<int, int> _ammoCap = new();

    /// <summary>
    /// Hostility. Only the player slots 0..7 fight each other — owner 11 marks
    /// neutral/civilian structures (477 of them across all files), which units
    /// must not pick up on their own.
    /// </summary>
    // The alliance matrix from sec53 (player record +0x15, one byte per other
    // player, 1 = allied).  The defeat check @0x4982a0 reads exactly this to
    // decide who still counts as an enemy, so hostility is data, not "different
    // owner" as we assumed before.  Player 7 is allied with everyone in every
    // file — the neutral slot.  Maps without the section fall back to the old
    // rule.
    private static bool[,] _allied = new bool[8, 8];
    private static bool _haveAllies;

    /// <summary>
    /// Sides that are only standing there. Nobody shoots at them and they shoot
    /// at nobody, until the player gives an explicit order.
    ///
    /// <para>⚠ <b>ZURÜCKGEZOGEN am 06.08.2026, und zwar der Grund, nicht die
    /// Wirkung.</b> Was hier unten steht — "die Missionsskripte sind erst halb
    /// gelesen", "die Regel ist unsere" — stimmt nicht mehr: `mission_init`
    /// @0x487c40 setzt die ganze Bündnismatrix und macht Spieler 7 neutral, in
    /// allen 33 Missionen (siehe <see cref="LoadCampaignDiplomacy"/>). Das Feld
    /// bleibt, weil es die gelesene Diplomatie ausführt und weil es der Rückfall
    /// für Karten ohne `campaign_diplomacy.json` ist; die Begründung darunter
    /// ist Geschichte.</para>
    ///
    /// ⚠ OURS, and a stand-in for something not yet read. The original's own
    /// alliance matrix is no help here: the default is written at @0x419529 —
    /// eight passes of stride 41 setting `neutok[p][p] = 1` — so out of the box
    /// every side is hostile to every other, and a campaign level carries no
    /// sec53 to change it. The mission SCRIPT changes it, and the scripts are
    /// only half read (see StartCampaign for where they live).
    ///
    /// What IS read, and what made this necessary: mission 1's script block
    /// @0x49844d contains exactly ONE write to the player table,
    /// `hrac[1].aktiv = 1` — so mission 1 puts two sides into play, 0 and 1,
    /// while its players 2 and 7 are never touched. The player reported it from
    /// the other end: "man muss die neutralen Einheiten erst anfahren, damit sie
    /// meine eigenen werden", and our AI was shooting them instead.
    ///
    /// ⚠ `aktiv` is NOT the general switch, and the numbers say so: of the 33
    /// script blocks only nine write it at all (missions 1, 11, 12, 13, 18, 20,
    /// 22, 24, 27, 28, 31). So it is not exported as a rule. The rule used here
    /// is ours: <b>a side that owns a base is playing, a side with nothing but
    /// field units is standing by</b>. Measured over the campaign levels it
    /// gives map_01 P1 as the opponent with P2 and P7 neutral — which is what
    /// the player describes — but on map_07 it leaves P2's twenty units idle,
    /// so it is a stand-in and not the answer.
    /// </summary>
    private readonly bool[] _standby = new bool[8];

    public bool IsStandby(int owner) => owner is >= 0 and <= 7 && _standby[owner];

    /// <summary>
    /// The campaign's own diplomacy, from <c>campaign_diplomacy.json</c> —
    /// which is not a table but the code of <c>mission_init</c> @0x487c40, read
    /// out by <see cref="Import.ExeTables.CampaignDiplomacy"/>.
    ///
    /// ⚠ This RETIRES the stand-in above. <see cref="_standby"/> said "a side
    /// that owns a base is playing, a side with nothing but field units is
    /// standing by", and its own note admitted the mission scripts were only
    /// half read and that the rule left map_07's player 2 idle for no reason.
    /// The scripts are read now: every mission sets the whole 8x8 matrix and
    /// makes <b>player 7 neutral</b>, all 33 of them. The old rule survives
    /// only for maps that have neither this file nor a sec53 — a .DM opened as
    /// a skirmish, say.
    /// </summary>
    private static readonly bool[] _neutralPlayers = new bool[8];
    private static bool _haveNeutral;
    private static int _diploMission = -1;

    /// <summary>Slots the mission puts out of play. Their units do not fight,
    /// nobody fights them — and they are the ones that can be driven up to and
    /// taken over (Simulation/Takeover.cs).</summary>
    public static bool IsNeutralPlayer(int p) => p is >= 0 and <= 7 && _neutralPlayers[p];
    public static bool HaveNeutralPlayers => _haveNeutral;

    /// <summary>A skirmish has no mission_init: nobody is neutral there. The
    /// list is static, so it has to be cleared when one game follows another.</summary>
    public static void ClearNeutralPlayers()
    {
        for (int p = 0; p < 8; p++) _neutralPlayers[p] = false;
        _haveNeutral = false;
        _diploMission = -1;
    }

    /// <summary>Fill <see cref="_allied"/> and the neutral list from the
    /// campaign's own diplomacy. Returns false when the file is not there, in
    /// which case nothing is touched and the old rule still applies.</summary>
    private static bool LoadCampaignDiplomacy(int mission)
    {
        if (mission < 1) return false;
        string path = Core.Content.Path("Maps/campaign_diplomacy.json");
        if (!FileAccess.FileExists(path)) return false;
        using var f = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        if (f == null) return false;
        var json = new Json();
        if (json.Parse(f.GetAsText()) != Error.Ok ||
            json.Data.VariantType != Variant.Type.Dictionary) return false;
        var root = json.Data.AsGodotDictionary<string, Variant>();
        if (!root.TryGetValue("missions", out var mv) ||
            mv.VariantType != Variant.Type.Array) return false;

        foreach (var item in mv.AsGodotArray())
        {
            if (item.VariantType != Variant.Type.Dictionary) continue;
            var d = item.AsGodotDictionary<string, Variant>();
            if (GetI(d, "mission", -1) != mission) continue;
            if (!d.TryGetValue("allied", out var av) ||
                av.VariantType != Variant.Type.Array) return false;

            var rows = av.AsGodotArray();
            if (rows.Count < 8) return false;
            var mat = new bool[8, 8];
            for (int a = 0; a < 8; a++)
            {
                var row = rows[a].AsGodotArray();
                for (int b = 0; b < 8 && b < row.Count; b++) mat[a, b] = row[b].AsInt32() != 0;
            }
            _allied = mat;
            _haveAllies = true;

            for (int p = 0; p < 8; p++) _neutralPlayers[p] = false;
            if (d.TryGetValue("neutral", out var nv) && nv.VariantType == Variant.Type.Array)
                foreach (var p in nv.AsGodotArray())
                {
                    int q = p.AsInt32();
                    if (q is >= 0 and <= 7) _neutralPlayers[q] = true;
                }
            _haveNeutral = true;
            _diploMission = mission;
            return true;
        }
        return false;
    }

    private bool IsHostile(Entity a, Entity b)
    {
        if (b.IsProp || b.Dead || b.HpMax <= 0) return false;
        if (a.Owner is < 0 or > 7 || b.Owner is < 0 or > 7) return false;
        if (_standby[a.Owner] || _standby[b.Owner]) return false;
        return _haveAllies ? !_allied[a.Owner, b.Owner] : b.Owner != a.Owner;
    }

    /// <summary>Per-player name and defeat flag from sec53.</summary>
    public sealed class Player
    {
        public int Index, Flag;
        public string Name = "", Comment = "";
        public bool Human, Beaten;

        /// <summary>Record +0x20 / +0x24: units destroyed and units lost. The
        /// kill counter is bumped wherever a unit dies (@0x40cfbc, @0x428144),
        /// the loss counter @0x40b4d7, and the statistics screen prints the
        /// latter after " / Verluste " (@0x48571b). 4.DM has player 0 on
        /// 15 kills / 1 loss and player 1 on exactly the mirror image.</summary>
        public int Kills, Losses;
    }

    private readonly List<Player> _players = new();

    /// <summary>Who is still in the game, by the original rule: a player is out
    /// once nothing of theirs is left, and the mission ends when no unallied
    /// player has anything left (@0x4982a0).</summary>
    public string StandingsLine()
    {
        if (_players.Count == 0) return "keine Spielertabelle (sec53)";
        var alive = new int[8];
        foreach (var e in _entities)
            if (!e.IsProp && !e.Dead && e.HpMax > 0 && e.Owner is >= 0 and <= 7)
                alive[e.Owner]++;
        var parts = new List<string>();
        foreach (var p in _players)
        {
            if (p.Beaten && alive[p.Index] == 0) continue;
            string nm = p.Name.Length > 0 ? p.Name : (p.Human ? "SPIELER" : "CPU");
            // the game's own score: kills and losses out of the player record
            string score = p.Kills + p.Losses > 0 ? $" {p.Kills}/{p.Losses}" : "";
            parts.Add($"P{p.Index} {nm} {alive[p.Index]}{score}{(p.Beaten ? " (raus)" : "")}");
        }
        return string.Join("  ", parts);
    }

    /// <summary>Cell distance (Chebyshev-ish, in tiles) between two entities.</summary>
    private static float CellDistance(Entity a, Entity b)
        => new Vector2(a.Col - b.Col, a.Row - b.Row).Length();

    /// <summary>
    /// Attack order: every selected unit that carries a weapon engages the
    /// entity under the cursor. Units without a weapon just drive there.
    /// </summary>
    public bool IssueAttack(Vector2 mapPos, bool queue = false)
    {
        int hit = Pick(mapPos);
        if (hit < 0) return false;
        var victim = _entities[hit];
        if (victim.IsProp || victim.Dead) return false;

        int n = 0;
        foreach (int i in _sel)
        {
            var e = _entities[i];
            if (i == hit || !CanFight(e) || !IsHostile(e, victim)) continue;

            // Shift appends: drive the route already given, then attack. A unit
            // with nothing to do falls through and attacks at once.
            if (queue && (e.Path != null || e.Orders.Count > 0))
            {
                if (e.Orders.Count >= MaxOrders) continue;
                e.Orders.Add(Order.Attack(new Vector2I(victim.Col, victim.Row), hit));
                n++;
                continue;
            }

            e.Target = hit;
            e.Ordered = true;
            e.Path = null;
            e.Reserved = null;
            e.Orders.Clear();          // a plain attack replaces the queue
            n++;
        }
        if (n == 0) return false;
        AddOrderMark(victim.Pos, attack: true);
        _order = $"attack -> slot {victim.Slot} ({LabelOf(victim.UnitType)}): {n} unit(s)";
        UpdatePanel();
        QueueRedraw();
        return true;
    }

    /// <summary>
    /// Idle armed units pick up hostiles that wander into weapon range. Run on a
    /// timer rather than every frame — a map has dozens of entities, so the full
    /// pairwise scan is cheap at 2.5 Hz but pointless at 60.
    /// </summary>
    private void AutoAcquire()
    {
        for (int i = 0; i < _entities.Count; i++)
        {
            var e = _entities[i];
            if (!CanFight(e) || e.Target >= 0 || e.Path != null) continue;
            float range = RangeOf(e);
            int best = -1;
            float bestDist = range;
            for (int j = 0; j < _entities.Count; j++)
            {
                if (i == j) continue;
                var t = _entities[j];
                if (!IsHostile(e, t)) continue;
                float d = CellDistance(e, t);
                if (d <= bestDist) { bestDist = d; best = j; }
            }
            if (best >= 0) { e.Target = best; e.Ordered = false; }
        }
    }

    /// <summary>Drive at / shoot at the current target. Called once per frame.</summary>
    private void UpdateCombat(int i, Entity e, float dt)
    {
        if (e.Cooldown > 0) e.Cooldown -= dt;
        if (e.Target < 0) { e.AimFacing = -1; return; }   // turret returns to the hull

        var t = _entities[e.Target];
        if (t.Dead || t.IsProp) { e.Target = -1; return; }

        var w = WeaponOf(e.Weapon);
        float dist = CellDistance(e, t);

        e.AimFacing = DirToFacing(t.Pos - e.Pos);   // the turret tracks its target

        if (dist <= RangeOf(e))
        {
            e.Path = null;                      // in range: hold position and fire
            // a turreted unit keeps its hull heading and only swings the weapon;
            // one without a turret has to turn its whole body
            if (e.Weapon == 0) e.Facing = e.AimFacing;
            if (e.Cooldown <= 0 && HasAmmo(e))
            {
                e.Cooldown = ReloadOf(e);
                if (e.AmmoMax > 0 && !(CheatAmmo && Cheated(e)))
                    e.Ammo--;                    // one round per shot (@0x40c587)
                DebugShots++;
                Fire(i, e, e.Target, t, w);
            }
            return;
        }

        // only a player-ordered attack chases; a target picked up automatically
        // is simply dropped when it leaves the firing envelope
        if (!e.Ordered || !e.Mobile) { e.Target = -1; return; }

        // out of range: walk towards the target, re-pathing when it moves away
        if (e.Path == null && _nav != null)
        {
            var goal = _nav.NearestFree(new Vector2I(t.Col, t.Row), e.Move, i);
            if (goal == null) { e.Target = -1; return; }
            var path = _nav.FindPath(new Vector2I(e.Col, e.Row), goal.Value, e.Move, i);
            if (path == null || path.Count == 0) { e.Target = -1; return; }
            e.Path = path;
            e.PathIdx = 0;
            e.Goal = goal.Value;
        }
    }

    /// <summary>Rocket launchers get a flying projectile; guns keep the tracer.</summary>
    private static string? RocketKind(int weaponComp) => weaponComp switch
    {
        26 => "rocket_l",              // L.Raketenwerfer
        27 or 28 => "rocket_h",        // Schw.Raketenwerfer, Mittelstreckenrakete
        _ => null,
    };

    // px/s. No projectile-speed field could be identified in the data, so this
    // is ours — like the rate of fire.
    private const float RocketSpeed = 190f;

    private void Fire(int si, Entity shooter, int vi, Entity victim,
                      (string Name, int Damage, float RangeTiles) w)
    {
        // Solange diese Frist laeuft, zeigt ein Fusssoldat seine Schusspose —
        // siehe InfBlock. Etwas laenger als eine Nachladezeit, damit die Pose
        // waehrend eines Feuerstosses nicht flackert.
        shooter.FireUntil = _clock + FirePoseSeconds;
        Vector2 dir = (victim.Pos - shooter.Pos).Normalized();
        // ⚠ 11.08.2026 — der Muendungsfeuerball (ANIM.CWA-Folge 232) wurde fuer
        // JEDE Waffe gleich gesetzt: gemeldet als »der Feuerball, der bei einer
        // richtigen Kanone stimmt, kommt auch bei Infanterie und beim
        // MG-Fahrzeug«. Ein Gewehr macht keinen Feuerball.
        //
        // Fuer die INFANTERIE ist das inzwischen belegt, und zwar an ihren
        // eigenen Bildern: ein Fusssoldatensatz hat 15 Bloecke, und die
        // Bloecke 9 und 10 -- die Schusspose -- tragen den Muendungsblitz
        // BEREITS IM SPRITE (roter Blitz vor dem Gewehr, nachgesehen an Satz 0,
        // Richtung 2). Ein zusaetzlicher Effekt war dort also doppelt gemalt.
        // Die Tuerme dagegen haben nur 8 Richtungen und ueberhaupt keine
        // Schussbilder, brauchen den Effekt also.
        //
        // ⚠ Was UNSERE SETZUNG bleibt: welcher Effekt einem Fahrzeug zusteht.
        // Das MG-Fahrzeug bekommt hier denselben Feuerball wie die Kanone, und
        // dass das im Original so ist, ist NICHT gelesen.
        //
        // Am 11.08.2026 sah es kurz so aus, als stuende die Antwort in Feld
        // +0x02 des Klangsatzes 0x4f98f2 (Werte 232, 102, 143, 0). Das ist sie
        // NICHT: 0x4048cf liest genau dieses Feld und uebergibt es der
        // Klangroutine (Entfernung zur Kamera, Panning, 399 -> rand()%6+400).
        // Es ist eine zweite KLANG-Nummer, keine ANIM.CWA-Folge -- und 143 ist
        // in ANIM.CWA leer, was die Fehldeutung auch auffliegen liess.
        if (shooter.Weapon < InfantryWeaponFirst)
            _effects.Add(new Effect { Pos = shooter.Pos + dir * 12f - new Vector2(0, 8),
                                      Kind = "muzzle", FrameTime = 0.035f });

        // the weapon's own report, out of the game's own table: a component
        // names a sound class at stats +0x1c, the class picks a row of the
        // fire-sound table @0x4f98f2, and the original plays that number or the
        // next at random (@0x40c4c0). See Audio/GameSounds.Fire.
        Audio.GameSounds.Fire(WeaponRowOf(shooter.Weapon), null, shooter.Col, shooter.Row);

        string? rocket = RocketKind(shooter.Weapon);
        if (rocket != null)
        {
            // rockets travel — the hit is resolved in UpdateProjectiles
            _shots.Add(new Projectile
            {
                Pos = shooter.Pos + dir * 12f - new Vector2(0, 8),
                Aim = victim.Pos - new Vector2(0, 6),
                Target = vi, Shooter = si, Damage = w.Damage,
                Facing = DirToFacing(dir), Kind = rocket, Speed = RocketSpeed,
            });
            return;
        }

        _tracers.Add((shooter.Pos - new Vector2(0, 8), victim.Pos - new Vector2(0, 6), 0.10f));
        ApplyHit(si, vi, victim, w.Damage);
    }

    /// <summary>Damage plus everything that follows from it (death, retaliation).</summary>
    /// <summary>Damage of one shot, as the hit routine @0x40c9a0 computes it.
    ///
    /// The stack frame is constant from the `add esp, 8` @0x40cdd8 onwards, and
    /// in it `[esp+0x24]` is the entity whose energie is written — the victim —
    /// while the pre-adjust `[esp+0x2c]` is the same slot, so the values read at
    /// 0x40cdcc/0x40cde3 are the VICTIM's. `[esp+0x18]`/`[esp+0x1a]` come from
    /// the attacker branch @0x40cb9b/@0x40cbab. That gives:
    ///
    ///     offence  = (attacker.defence + 30) * attacker.attack / 40
    ///     defence  = (30 + victim.defence / 5)
    ///              * (victim.attack + 2 * elevation(victim cell)) / 50
    ///     damage   = offence - defence + rnd(0..4) - rnd(0..4)
    ///     damage <= -2      -> 0
    ///     damage <  1       -> rnd(0..9) / 3
    ///
    /// Elevation sits inside the subtracted term: high ground protects. The
    /// constants (30, 40, 50, the /5, the doubled elevation, both die rolls and
    /// the small-value clamp) are read straight off @0x40cdc4..0x40ceaf.
    /// Buildings have no such pair of ratings, so a shot at a structure keeps
    /// using the weapon table's damage.</summary>
    private int ShotDamage(Entity? shooter, Entity victim, int weaponDamage)
    {
        if (shooter == null || victim.IsBuilding || shooter.Attack <= 0) return weaponDamage;
        int elev = ElevOf(victim.Col, victim.Row);
        int offence = (shooter.Defence + 30) * shooter.Attack / 40;
        int defence = (30 + victim.Defence / 5) * (victim.Attack + 2 * elev) / 50;
        int dmg = offence - defence + (int)(GD.Randi() % 5) - (int)(GD.Randi() % 5);
        if (dmg <= -2) return 0;
        if (dmg < 1) return (int)(GD.Randi() % 10) / 3;
        return dmg;
    }

    private void ApplyHit(int si, int vi, Entity victim, int damage)
    {
        // ⚠ 11.08.2026 — WER SCHON TOT IST, WIRD NICHT NOCHMAL GETROFFEN.
        //
        // Gemeldet als »die Infanterie stirbt, kippt kurz um und steht dann
        // wieder, aber macht nix mehr«. Genau das war es: ohne diese Zeile lief
        // ein Treffer auf eine Leiche durch bis Kill(), und Kill() setzt
        // DeadTime auf 0. Die Umfall-Bilder (Bloecke 12..14) fingen also von
        // vorn an -- der Tote richtete sich auf und kippte erneut, endlos, und
        // der Sterbeklang 131 spielte jedes Mal mit.
        //
        // Die Geschossbahn hatte den Schutz schon (`if (!t.Dead)` weiter
        // unten), der direkte Beschuss und der Luftangriff nicht. Beim
        // Luftangriff faellt es zwangslaeufig an: Kill() raeumt `Target` nur
        // bei den Eintraegen in _entities auf, ein Flugzeug haelt sein Ziel in
        // seinem eigenen Satz und haette ewig weitergeschossen.
        if (victim.Dead) return;
        var shooter = si >= 0 && si < _entities.Count ? _entities[si] : null;
        damage = ShotDamage(shooter, victim, damage);
        // the original destroys the unit outright once a hit is at least what
        // is left of its energie (@0x40cf8d), instead of letting it reach zero
        // Unverwundbar: der Treffer wird gezählt und gemeldet wie immer, nur
        // der Schaden bleibt aus — so bleiben Klang, Meldung und Zielwahl heil.
        if (CheatGodMode && Cheated(victim)) damage = 0;
        bool lethal = !victim.IsBuilding && damage >= victim.Hp && damage > 0;
        victim.Hp -= victim.DugIn ? Mathf.RoundToInt(damage * DugInDamageFactor) : damage;
        if (lethal) victim.Hp = 0;

        NoteEvent(victim, victim.Hp > 0 ? "unter Beschuss" : "verloren");
        SpeakHit(victim);

        // shoot back: an idle armed unit engages whoever hit it
        if (victim.Hp > 0)
        {
            if (victim.Target < 0 && shooter != null && CanFight(victim) &&
                IsHostile(victim, shooter))
            {
                victim.Target = si;      // return fire, but hold position
                victim.Ordered = false;
            }
            return;
        }

        Kill(vi, victim);
    }

    /// <summary>Take an entity off the board: clear its cells, drop it out of
    /// every selection and target, and leave the right remains behind.</summary>
    private void Kill(int vi, Entity victim)
    {
        victim.Hp = 0;
        victim.Dead = true;
        victim.DeadTime = 0;
        victim.Path = null;
        victim.Target = -1;
        if (vi >= 0)
        {
            _nav?.ClearOccupant(victim.Col, victim.Row, vi);
            if (victim.Reserved is { } rc) _nav?.ClearOccupant(rc.X, rc.Y, vi);
            _sel.Remove(vi);
            foreach (var other in _entities)
                if (other.Target == vi) other.Target = -1;
            if (_selected == vi) SetPrimary();
        }
        victim.Reserved = null;
        if (victim.Infantry >= 0)
        {
            // A foot soldier has its own falling-over frames (blocks 12..14 of
            // its set) and leaves a body. NOTHING else is drawn on top.
            //
            // ⚠ CORRECTED 07.08.2026. This used to add a "blast" effect here,
            // described in the comment as "just a small blast where it was hit".
            // It is nothing of the kind: `blast` is ANIM.CWA sequence 550 at
            // **60x79 pixels** — a tall FLAME, the same fire that burns on
            // trees, and nearly three times the size of the 29x29 explosion.
            // Every dying soldier briefly went up in a bonfire, which together
            // with the hit report read as "a building is under attack".
            // The falling-over frames were already running underneath it.
            //
            // the original's own sound for this: @0x40d37c, in the hit routine
            // right after it prints "Hit to exploding infantry!!!"
            Audio.GameSounds.PlayAt(Audio.GameSounds.InfantryDies, victim.Col, victim.Row);
            return;
        }
        _effects.Add(new Effect { Pos = victim.Pos - new Vector2(0, 6),
                                  Kind = "explosion", FrameTime = 0.06f });
        // the rubble variant is picked from where it fell, so two wrecks side by
        // side do not look stamped from the same mould — see DrawWreck
        _effects.Add(new Effect { Pos = victim.Pos, Kind = "wreck",
                                  FrameTime = 0.25f, Hold = true,
                                  Variant = victim.Col * 3 + victim.Row });
    }

    // ---- ANIM.CWA effect sprites ----

    private List<Texture2D> EffectFrames(string kind)
    {
        if (_fx.TryGetValue(kind, out var cached)) return cached;
        var list = new List<Texture2D>();
        for (int i = 0; i < 64; i++)
        {
            string path = Core.Content.Path($"Effects/{kind}/f{i:00}.png");
            Texture2D? tex = ResourceLoader.Exists(path) ? ResourceLoader.Load<Texture2D>(path) : null;
            if (tex == null && FileAccess.FileExists(path))
            {
                var img = Image.LoadFromFile(path);
                if (img != null) tex = ImageTexture.CreateFromImage(img);
            }
            if (tex == null) break;
            list.Add(tex);
        }
        _fx[kind] = list;
        _fxAnchor[kind] = list.Count > 0 ? list[0].GetSize() / 2f : Vector2.Zero;
        return list;
    }

    /// <summary>Fly the rockets and resolve their impacts.</summary>
    private void UpdateProjectiles(float dt)
    {
        for (int i = _shots.Count - 1; i >= 0; i--)
        {
            var p = _shots[i];

            // keep tracking a live target; otherwise fly on to the last aim point
            if (p.Target >= 0 && p.Target < _entities.Count)
            {
                var t = _entities[p.Target];
                if (t.Dead || t.IsProp) p.Target = -1;
                else p.Aim = t.Pos - new Vector2(0, 6);
            }

            Vector2 d = p.Aim - p.Pos;
            float dist = d.Length();
            float step = p.Speed * dt;
            if (dist > 0.01f) p.Facing = DirToFacing(d);

            if (dist > step)
            {
                p.Pos += d / dist * step;
                _shots[i] = p;
                continue;
            }

            // impact
            _shots.RemoveAt(i);
            _effects.Add(new Effect { Pos = p.Aim, Kind = "explosion", FrameTime = 0.06f });
            if (CellAt(p.Aim) is { } ic) Audio.GameSounds.Explosion(ic.X, ic.Y);
            else Audio.GameSounds.Explosion();
            if (p.Target >= 0 && p.Target < _entities.Count)
            {
                var t = _entities[p.Target];
                if (!t.Dead) ApplyHit(p.Shooter, p.Target, t, p.Damage);
            }
        }
    }

    /// <summary>Directional projectile sprite (Effects/rocket_*/f0..f7.png).</summary>
    private Texture2D? ProjectileTexture(string kind, int facing)
    {
        string key = $"{kind}/f{facing}";
        if (_projTex.TryGetValue(key, out var cached)) return cached;
        string path = Core.Content.Path($"Effects/{kind}/f{facing}.png");
        Texture2D? tex = ResourceLoader.Exists(path) ? ResourceLoader.Load<Texture2D>(path) : null;
        if (tex == null && FileAccess.FileExists(path))
        {
            var img = Image.LoadFromFile(path);
            if (img != null) tex = ImageTexture.CreateFromImage(img);
        }
        _projTex[key] = tex;
        return tex;
    }

    private readonly Dictionary<string, Texture2D?> _projTex = new();

    private void UpdateEffects(float dt)
    {
        for (int i = _effects.Count - 1; i >= 0; i--)
        {
            var fx = _effects[i];
            fx.Time += dt;
            var frames = EffectFrames(fx.Kind);
            if (frames.Count == 0) { _effects.RemoveAt(i); continue; }
            if (fx.Time >= frames.Count * fx.FrameTime && !fx.Hold) _effects.RemoveAt(i);
            else _effects[i] = fx;
        }
        for (int i = _tracers.Count - 1; i >= 0; i--)
        {
            var tr = _tracers[i];
            tr.T -= dt;
            if (tr.T <= 0) _tracers.RemoveAt(i); else _tracers[i] = tr;
        }
    }

    /// <param name="ground">true = the effects that stay on the ground (wrecks,
    /// scorch marks); they are drawn BEFORE the units so the living drive over
    /// them, while explosions and muzzle flashes go on top.</param>
    private void DrawEffects(bool ground)
    {
        if (!ground)
            foreach (var tr in _tracers)
                DrawLine(tr.A, tr.B, new Color(1f, 0.95f, 0.5f, Mathf.Clamp(tr.T * 8f, 0, 1)), 1.2f);

        if (!ground)
            foreach (var p in _shots)
            {
                var tex = ProjectileTexture(p.Kind, p.Facing);
                if (tex != null) DrawTexture(tex, p.Pos - tex.GetSize() / 2f);
            }

        foreach (var fx in _effects)
        {
            if (fx.Hold != ground) continue;
            var frames = EffectFrames(fx.Kind);
            if (frames.Count == 0) continue;
            if (fx.Kind == "wreck") { DrawWreck(fx, frames); continue; }
            int f = Mathf.Min((int)(fx.Time / fx.FrameTime), frames.Count - 1);
            DrawTexture(frames[f], fx.Pos - _fxAnchor[fx.Kind]);
        }
    }

    /// <summary>
    /// What a destroyed vehicle leaves behind.
    ///
    /// <para>⚠ CORRECTED 07.08.2026 — this was the reported "black patch on the
    /// ground". ANIM.CWA sequence 0 was treated as a four-frame animation held on
    /// its LAST frame forever, and that last picture is 451 pixels drawn in three
    /// near-black colours — (0,0,0), (19,19,15), (31,27,27) — pasted opaque onto
    /// the grass. Measured, not guessed: frames 0..2 are rubble at mean RGB
    /// (89,81,76), (81,74,68), (92,77,73), frame 3 has mean RGB (0,0,0).</para>
    ///
    /// <para>It is not an animation frame at all. The four pictures are three
    /// scattered-rubble variants plus one ground mark, and the rubble does not
    /// decay into it — the visible pixel counts run 273, 234, 52, 451. A picture
    /// built from three near-black shades is a DARKENING sprite: it belongs under
    /// the rubble, blended, the way a scorch mark darkens the ground it lies on.
    /// No other exported sequence has a black picture (explosion and blast are 0%
    /// near-black), so this is specific to the wreck bank, not a shadow plane the
    /// exporter should strip everywhere.</para>
    ///
    /// <para><b>OURS:</b> that the ground mark is drawn at 45% and that each wreck
    /// keeps one rubble variant for good. The original's own wreck records carry a
    /// non-zero byte at +0 (@0x77cae8, tested only for "slot in use" in the tick
    /// @0x4396c0) which would be the natural place for the variant, but nothing
    /// proves it selects a picture — so the choice here is by position, and it is
    /// ours. The pixels are all the original's.</para>
    /// </summary>
    private void DrawWreck(Effect fx, List<Texture2D> frames)
    {
        var anchor = _fxAnchor["wreck"];
        if (frames.Count > 1)
            DrawTexture(frames[^1], fx.Pos - anchor, new Color(1, 1, 1, 0.45f));
        var rubble = frames[Mathf.PosMod(fx.Variant, Mathf.Max(1, frames.Count - 1))];
        DrawTexture(rubble, fx.Pos - anchor);
    }

    /// <summary>
    /// Weapon range of the selected units. Distances are measured in CELLS, and
    /// a cell is 40x20 px, so the reachable area is an ellipse on screen, not a
    /// circle.
    /// </summary>
    private void DrawRangeRings()
    {
        if (!_showRanges) return;
        foreach (int i in _sel)
        {
            var e = _entities[i];
            if (!CanFight(e)) continue;
            float r = RangeOf(e);
            var pts = new Vector2[33];
            for (int k = 0; k <= 32; k++)
            {
                float a = k * Mathf.Tau / 32f;
                pts[k] = e.Pos + new Vector2(Mathf.Cos(a) * r * TileW, Mathf.Sin(a) * r * TileH);
            }
            DrawPolyline(pts, new Color(1f, 0.55f, 0.2f, 0.35f), 1.2f);
        }
    }

    public void ToggleRanges() { _showRanges = !_showRanges; QueueRedraw(); }

    // ---- orders (the game's own vocabulary, GAME.EXE @0x4fd660) ----

    private static string[]? _orders;

    private static void LoadOrders()
    {
        if (_orders != null) return;
        _orders = System.Array.Empty<string>();
        string path = Core.Content.Path("Maps/orders.json");
        if (!FileAccess.FileExists(path)) return;
        using var f = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        if (f == null) return;
        var json = new Json();
        if (json.Parse(f.GetAsText()) != Error.Ok || json.Data.VariantType != Variant.Type.Dictionary)
            return;
        var root = json.Data.AsGodotDictionary<string, Variant>();
        if (!root.TryGetValue("orders", out var ov) || ov.VariantType != Variant.Type.Array) return;
        var arr = ov.AsGodotArray();
        var list = new List<string>();
        foreach (var x in arr) list.Add(x.AsString());
        _orders = list.ToArray();
    }

    /// <summary>The game's own name for an order index (0 Angreifen, 1 Bewegen, …).</summary>
    private static string OrderName(int i)
        => _orders != null && i >= 0 && i < _orders.Length && _orders[i].Length > 0
            ? _orders[i] : "?";

    private const int OrdAttack = 0, OrdMove = 1, OrdGuard = 2, OrdDigIn = 7,
                      OrdDigOut = 8, OrdStop = 26;

    /// <summary>Current order of a unit, named the way the game names it.</summary>
    private string OrderOf(Entity e)
    {
        if (e.Dead) return "ZERSTOERT";
        if (e.IsBuilding) return "";
        if (e.DugIn) return OrderName(OrdDigIn).ToUpper();
        if (e.Target >= 0) return OrderName(OrdAttack).ToUpper();
        if (e.Path != null) return OrderName(OrdMove).ToUpper();
        return e.Mobile ? OrderName(OrdGuard).ToUpper() : OrderName(OrdStop).ToUpper();
    }

    /// <summary>
    /// "Eingraben" / "Ausgraben" for the selection. Digging in stops the unit and
    /// halves incoming damage — the ORDER is the game's, the damage rule is ours
    /// (no dug-in modifier could be found in the data).
    /// </summary>
    public void ToggleDigIn()
    {
        int n = 0;
        foreach (int i in _sel)
        {
            var e = _entities[i];
            if (!e.Mobile || e.Dead) continue;
            e.DugIn = !e.DugIn;
            if (e.DugIn)
            {
                if (e.Reserved is { } r) _nav?.ClearOccupant(r.X, r.Y, i);
                e.Path = null;
                e.Reserved = null;
            }
            n++;
        }
        _order = n > 0 ? $"{OrderName(OrdDigIn)} / {OrderName(OrdDigOut)}: {n}" : "";
        UpdatePanel();
        QueueRedraw();
    }

    private const float DugInDamageFactor = 0.5f;

    // ================= mission objectives (sec69) =============================
    //
    // sec69 is 8 x 100 x 6: every player owns a list of buildings that decide
    // the mission for him.  What the lists hold differs per map — in "Chanel
    // Tunnel" each player's list is mostly his own buildings while player 3's
    // points at other players', and in 2.DM all eight lists are identical — so
    // the honest reading is "these buildings matter to me", not "destroy them".
    // `imp` is a weight and does not separate own from enemy targets.
    //
    // The end condition itself is the proven one from sec53 (@0x4982a0): you are
    // out when nothing of yours is left, and the mission is over once no
    // unallied player still has anything.

    private readonly List<int>[] _objectives =
        { new(), new(), new(), new(), new(), new(), new(), new() };

    /// <summary>Which player the HUD is reporting for — the human one from
    /// sec53 if the map names one, otherwise player 0.</summary>
    /// <summary>Whose side the camera and the panel are on. By default the
    /// slot the map file marks as human (sec53); a skirmish overrides it.</summary>
    public int ViewPlayer
    {
        get
        {
            if (_viewPlayer >= 0) return _viewPlayer;
            foreach (var p in _players) if (p.Human) return p.Index;
            return 0;
        }
        set => _viewPlayer = value is >= 0 and <= 7 ? value : -1;
    }

    private int _viewPlayer = -1;

    private bool Allied(int a, int b)
        => a == b || (_haveAllies && a is >= 0 and <= 7 && b is >= 0 and <= 7 && _allied[a, b]);

    // ---- was ein Missionsskript mit der Welt tun darf   (11.08.2026) --------
    //
    // Diese vier sind die Wirkungen, die der Missionsblock ueber den Befehlsbus
    // absetzt. Sie stehen hier und nicht in `MissionScript`, weil dort keine
    // Entitaeten liegen — das Skript kennt nur Zahlen.

    /// <summary>Kontostand eines Spielers. `get_money(spieler)` @0x4CF5E0 liest
    /// `dword[0xA9C600 + 4*spieler]`; hier ist es <c>_money</c>.</summary>
    private int Money(int player) => player is >= 0 and <= 7 ? _money[player] : 0;

    private void Money(int player, int value)
    {
        if (player is >= 0 and <= 7) _money[player] = value;
    }

    /// <summary>
    /// `bus_cmd(11, einheit, ukol, x, y)` — der Befehl, mit dem eine Mission
    /// ihre eigenen Einheiten losschickt.
    ///
    /// <para>Der Einheitenplatz ist <c>1000*spieler + k</c>: Mission 1 schickt
    /// 1000..1003 los, also die ersten vier Einheiten von Spieler 1 — die drei
    /// Infanteristen und das MG-Fahrzeug, die dem Spieler auf dem Weg zur
    /// Brücke entgegenkommen.</para>
    ///
    /// <para><b>ukol 4 ist Angriff</b>, gelesen aus `order` @0x410220, das genau
    /// dieses Feld direkt schreibt (aekernel-tools/ai_units.py). ⚠ Was der
    /// Befehl bei ANDEREN ukol-Werten tut, ist ungelesen — ein unbekannter Wert
    /// wird darum gemeldet und nicht geraten.</para>
    ///
    /// <para>⚠ <b>UNSERE Setzung ist das Ziel:</b> das Original gibt (x, y)
    /// mit, wir schicken die Einheit auf den nächsten Feind zu. Mission 1 setzt
    /// dort 39/0, und 39 ist keine sinnvolle Zelle dieser 42×72-Karte für einen
    /// Angriff auf den Spieler — bis das Feld gelesen ist, ist der nächste Feind
    /// die ehrlichere Näherung als eine Zelle, die wir nicht verstehen.</para>
    /// </summary>
    private void MissionOrder(int slot, int ukol, int x, int y)
    {
        int idx = -1;
        for (int i = 0; i < _entities.Count; i++)
            if (!_entities[i].IsBuilding && !_entities[i].Dead && _entities[i].Slot == slot)
            { idx = i; break; }
        if (idx < 0)
        {
            GD.PrintErr($"Missionsbefehl: Einheitenplatz {slot} ist leer");
            return;
        }
        if (ukol != 4)
        {
            GD.PrintErr($"Missionsbefehl ukol={ukol} auf Platz {slot} — ungelesen, " +
                        "es geschieht nichts");
            return;
        }
        var me = _entities[idx];
        // ⚠ Zielwahl: das Original gibt (x, y) mit, wir suchen einen Gegner —
        // und »der naechste« nahm auch NEUTRALE und einander. Gemeldet: »1x
        // Infanterie und das MG greifen einen anderen Infanteristen an«.
        // Gemeint ist der Spieler: die vier kommen dem Spieler entgegen.
        // Darum erst unter SEINEN Einheiten suchen, und nur wenn er keine mehr
        // hat, unter den uebrigen Feinden.
        int best = -1;
        float bestD = float.MaxValue;
        for (int pass = 0; pass < 2 && best < 0; pass++)
        for (int i = 0; i < _entities.Count; i++)
        {
            var o = _entities[i];
            if (o.IsProp || o.Dead || i == idx) continue;
            if (o.IsBuilding && o.BType >= 17) continue;
            if (o.Owner is < 0 or > 7) continue;
            if (Allied(o.Owner, me.Owner)) continue;
            if (pass == 0 && o.Owner != ViewPlayer) continue;
            float d0 = me.Pos.DistanceSquaredTo(o.Pos);
            if (d0 < bestD) { bestD = d0; best = i; }
        }
        if (best < 0) { GD.Print($"Missionsbefehl: Platz {slot} findet kein Ziel"); return; }
        me.Target = best;
        me.Ordered = true;
        GD.Print($"Missionsbefehl: Einheit {slot} (Spieler {me.Owner}) greift " +
                 $"{_entities[best].Name} an  [Original: ukol 4 nach ({x},{y})]");
    }

    /// <summary>`remove_unit` @0x4D0B00 und »Robot already sold.« @0x4D0EC0.
    /// Beide nehmen eine Einheit vom Feld — verkauft wird sie mit Erlös, und
    /// ⚠ <b>wieviel das ist, ist ungelesen</b>, darum gibt es hier keinen.
    /// Verschwinden lassen ist gelesen, der Preis nicht.</summary>
    private void MissionRemove(int slot, bool sold)
    {
        for (int i = 0; i < _entities.Count; i++)
        {
            var e = _entities[i];
            if (e.IsBuilding || e.Dead || e.Slot != slot) continue;
            GD.Print($"Missionsskript: Einheit {slot} " +
                     (sold ? "verkauft (Erloes ungelesen)" : "verschwindet"));
            Kill(i, e);
            return;
        }
    }

    /// <summary>`set_relation(a, b, wert)` @0x4CF6D0 — die Bündnismatrix zur
    /// Laufzeit ändern. Sie ist <c>byte[BASE + a*40 + b]</c>, und genau so
    /// liegt sie hier.</summary>
    private void SetRelationRuntime(int a, int b, int wert)
    {
        if (a is < 0 or > 7 || b is < 0 or > 7) return;
        _allied[a, b] = wert != 0;
        _haveAllies = true;
        GD.Print($"Missionsskript: Spieler {a} und {b} sind jetzt " +
                 (wert != 0 ? "verbuendet" : "verfeindet"));
    }

    /// <summary>Units and buildings still standing for a player.</summary>
    private int AssetsOf(int player)
    {
        int n = 0;
        foreach (var e in _entities)
            if (!e.IsProp && !e.Dead && e.HpMax > 0 && e.Owner == player) n++;
        return n;
    }

    /// <summary>The mission verdict for the viewed player, by the original
    /// rule: out with nothing left, won once no unallied player has anything.
    /// Returns "" while the mission is still running.</summary>
    /// <summary>Player slot 7 is the neutral one: in every .DM that carries a
    /// sec53 player table it is allied with everybody.</summary>
    public const int NeutralSlot = 7;

    /// <summary>
    /// The mission's own script, when the campaign carries one for this
    /// mission. It replaces <see cref="Verdict"/>'s guess with the condition
    /// the original actually checks — see Campaign/MissionScript.cs.
    /// </summary>
    private Campaign.MissionScript? _mscript;
    private bool _mscriptTried;

    private void MissionScriptTick(float dt)
    {
        if (!_mscriptTried)
        {
            _mscriptTried = true;
            int m = UI.SkirmishSetup.CampaignMission;
            if (m > 0) _mscript = Campaign.MissionScript.For(m);
            if (_mscript != null)
            {
                // the four questions a rule may ask, answered from the entities
                // ⚠ 11.08.2026 — hier stand `e.Owner`, und das war der Grund,
                // warum es in Kampagne 2 kein Geld gab.
                //
                // Das Original fuehrt den Besitzer als BYTE, und herrenlos ist
                // dort 0xFF = 255. Bei uns ist es -1. Mission 2 zahlt ihre
                // 400 $ aber nur, wenn `obj_owner(0) == 255` -- und
                // Gebaeudeplatz 0 ist genau der herrenlose Nachschub-Posten.
                // Mit -1 konnte die Bedingung nie wahr werden, die Kette
                // v[70] -> v[101] -> Geld brach an der letzten Stelle ab, und
                // im Spiel sah man zwei Helis, die man nie bezahlen konnte.
                _mscript.ObjOwner = slot =>
                {
                    foreach (var e in _entities)
                        if (e.IsBuilding && e.Slot == slot)
                            return e.Dead ? 12                 // 12 = leer, wie im Original
                                 : e.Owner is >= 0 and <= 7 ? e.Owner
                                 : 255;                        // herrenlos, als Byte
                    return 12;
                };
                _mscript.UnitCount = UnitClassCount;
                _mscript.BuildingCount = BuildingClassCount;
                _mscript.ObjectCount = (type, owner) =>
                {
                    int n = 0;
                    foreach (var e in _entities)
                        if (e.IsBuilding && !e.Dead && e.BType == type && e.Owner == owner) n++;
                    return n;
                };
                // find_unit(spieler, marke) @0x4D0F20 — die erste lebende
                // Einheit dieses Spielers mit der Marke in +0x43, sonst 0xFFFF.
                // Der Index ist der Satz-Index des Originals, und der ist unser
                // `Slot`: die Sätze liegen als spieler*1000 + k, weshalb das
                // Spiel den Besitzer aus `slot/1000` ableitet.
                _mscript.FindUnit = (player, mark) =>
                {
                    foreach (var e in _entities)
                        if (!e.IsBuilding && !e.Dead && e.Owner == player && e.Mark == mark)
                            return e.Slot;
                    return 0xFFFF;
                };
                // ⚠ Nur +0x00 und +0x01 werden beantwortet — Spalte und Zeile,
                // die beiden Felder, nach denen die Kampagne fragt. Alles andere
                // gibt -1, damit eine Bedingung, die wir nicht beantworten
                // können, FALSCH ist und nicht versehentlich wahr.
                // ⚠ NUR Einheiten. Im Original sind Einheiten- und
                // Gebaeudesaetze zwei verschiedene Tabellen, und dieselbe Zahl
                // heisst in beiden etwas anderes; hier tragen beide ein `Slot`.
                // Solange die markierte Einheit von der Karte kam, fiel das
                // nicht auf — sobald `space_in` Hullman den freien Satz 0 gibt,
                // antwortete Gebaeudeplatz 0 an seiner Stelle und Mission 14
                // schloss ihre Kette nie.
                _mscript.UnitField = (index, off) =>
                {
                    foreach (var e in _entities)
                        if (!e.IsBuilding && e.Slot == index && !e.Dead)
                            return off == 0 ? e.Col : off == 1 ? e.Row : -1;
                    return -1;
                };
                // Ein Wort aus einem Gebaeudesatz. Die Karte fuehrt dieselben
                // vier Lager vier Byte weiter vorn als der Laufzeitsatz
                // (sec3 +0x2c/+0x2e/+0x30/+0x32 = Laufzeit +0x28/+0x2a/+0x2c/
                // +0x2e), darum die Zuordnung hier und nicht im Importer.
                _mscript.StoreField = (slot, off) =>
                {
                    foreach (var e in _entities)
                        if (e.IsBuilding && e.Slot == slot)
                            return off switch
                            {
                                0x28 => e.StockW, 0x2a => e.StockF,
                                0x2c => e.StockS, 0x2e => e.StockT,
                                _ => -1,
                            };
                    return -1;
                };
                // ---- 11.08.2026: der tutorialartige Ablauf ------------------
                // Das Hilfefenster an seiner Stelle im 640x480-Raster des
                // Originals. Siehe UI/HelpWindow.cs.
                _mscript.ShowText = (id, art, x, y) =>
                {
                    GD.Print($"Missionstext {id} (Art {art}) bei {x},{y}");
                    UI.HelpWindow.Show(GetTree().Root, id, x, y);
                };
                _mscript.CloseTexts = UI.HelpWindow.CloseAll;
                _mscript.PlaySound = slot => Audio.GameSounds.Play(slot);
                // geld(spieler) += betrag — Busbefehl 528. Der Betrag ist ein
                // WORT und darf negativ sein.
                _mscript.AddMoney = (betrag, spieler) =>
                {
                    Money(spieler, Money(spieler) + betrag);
                    GD.Print($"Missionsgeld: Spieler {spieler} {(betrag >= 0 ? "+" : "")}" +
                             $"{betrag} $ -> {Money(spieler)} $");
                };
                // Was ist angewaehlt? Das Original haelt es in einem Wort:
                // < 8000 eine Einheit, ab 10000 eine Gruppe, sonst nichts.
                _mscript.Selection = () =>
                    _sel.Count > 1 ? 10000 : _selected >= 0 ? _selected : 0xFFFF;
                // count_units_with_mark(marke, spieler): das Original zaehlt
                // Saetze mit `+0x43 == marke` und `ukol < 100`. Unsere Marke ist
                // die Entwurfsnummer, mit der eine Einheit angelegt wurde.
                _mscript.MarkCount = (marke, spieler) =>
                {
                    int n = 0;
                    foreach (var e in _entities)
                        if (!e.IsBuilding && !e.IsProp && !e.Dead &&
                            e.Owner == spieler && e.Mark == marke) n++;
                    return n;
                };
                _mscript.UnitHasMark = (index, marke) =>
                {
                    foreach (var e in _entities)
                        if (!e.IsBuilding && !e.Dead && e.Slot == index)
                            return e.Mark == marke;
                    return false;
                };
                _mscript.MoneyOf = Money;
                // terrain_at(x, y) — das Gelaendebyte der Zelle. Unser Gitter
                // fuehrt es als `Ground`; die Zahlen sind dieselbe Ordnung
                // (frei/grob/wasser/gesperrt), aber ⚠ ob 4 im Original genau
                // unsere 4 ist, ist UNGEPRUEFT — Mission 1 fragt `> 4`.
                _mscript.TerrainAt = (x, y) =>
                    _nav != null && _nav.InBounds(x, y) ? (int)_nav.GroundAt(x, y) : -1;
                // bus_cmd(11, einheit, ukol, x, y) — ukol 4 ist Angriff.
                _mscript.OrderUnit = MissionOrder;
                _mscript.RemoveUnit = slot => MissionRemove(slot, false);
                _mscript.SellUnit = slot => MissionRemove(slot, true);
                _mscript.ChangeOwner = (slot, spieler) =>
                {
                    foreach (var e in _entities)
                        if (!e.IsBuilding && !e.Dead && e.Slot == slot)
                        {
                            GD.Print($"Missionsskript: Einheit {slot} geht an " +
                                     $"Spieler {spieler} (vorher {e.Owner})");
                            e.Owner = spieler;
                            return;
                        }
                };
                _mscript.SetRelation = SetRelationRuntime;
                // Die Belegungskarte: Einheitenplatz, ab 8000 ein Gebaeude,
                // 0xFFFE = frei. Unser Gitter fuehrt -1 fuer frei.
                _mscript.ImapAt = (col, row) =>
                {
                    if (_nav == null || !_nav.InBounds(col, row)) return 0xFFFE;
                    int occ = _nav.OccupantAt(col, row);
                    if (occ < 0 || occ >= _entities.Count) return 0xFFFE;
                    var e = _entities[occ];
                    return e.IsBuilding ? 8000 + e.Slot : e.Slot;
                };
                // ⚠ add_target ist die ZIELLISTE DES COMPUTERSPIELERS, kein
                // Eintrag im Missionspanel — siehe SkirmishAi.AddMissionTarget.
                _mscript.AddTarget = AddMissionTarget;
                // Verstaerkung — die Mechanik, an der Mission 14 haengt
                _mscript.SpaceInSpawn = SpawnReinforcement;
                var watched = _mscript.WatchedSlots();
                if (watched.Count > 0)
                {
                    var parts = new List<string>();
                    foreach (int slot in watched)
                        parts.Add($"{slot}:{_mscript.ObjOwner!(slot)}");
                    GD.Print("Skript beobachtet Objekte " + string.Join(" ", parts) +
                             "   (12 = zerstoert/leer)");
                }
                GD.Print(_mscript.Line());
            }
        }
        _mscript?.Tick(dt);
        // Was der Takt vorgemerkt hat, jetzt wegraeumen: ein Fenster, das
        // dieselbe Regel gleich wieder oeffnet, bleibt dabei stehen.
        UI.HelpWindow.CommitClose();
    }

    /// <summary>
    /// `g_robot_class_count(klasse, spieler)` @0x4CF980 — die häufigste
    /// Siegbedingung der Kampagne (115 Aufrufstellen).
    ///
    /// ⚠ CORRECTED 10.08.2026 — bis heute zählte JEDE Klasse alle Einheiten des
    /// Spielers, weil nur Klasse 0 zugeordnet war. Der Verteiler 0x4CFA28 führt
    /// aber auf **vier eigene Zähler**, und die sind gelesen (auf beiden
    /// GAME.EXE mit `fingerprint` eindeutig wiedergefunden, je ein Treffer):
    ///
    ///   Klasse 1 @0x4CF7A0  typ (+0x0a) ∈ {0, 3}   Bodenfahrzeuge
    ///   Klasse 2 @0x4CF820  typ == 1 UND ukol (+0x14) &lt; 100   lebende Personen
    ///   Klasse 3 @0x4CF8A0  typ ∈ {4, 5}           Schiffe (leicht / schwer)
    ///   Klasse 4 @0x4CF920  sec19 (200 × 68 @0x6DDF70), kind ∉ {0, 13, 14}
    ///                       — Flugzeuge OHNE Treibstoff- und Munitionskisten
    ///   Klasse 0 @0x4CF9AC  die Summe der vier — also MIT Flugzeugen
    ///
    /// Gegenprobe an 29 Kartenexporten (1971 Sätze): typ 0 sind 1360 Sätze mit
    /// Fahrwerk 161..168, typ 1 sind 512 mit Fahrwerk 148/149 (Chaingunner,
    /// Laser Trooper, Col.Hullman, Scientist …), typ 4/5 sind 97 Schiffe. Und
    /// an den Missionszielen: Mission 1 „Neutralisieren Sie feindliche
    /// Einheiten" zählt `units(3,1)` von 3 herunter — map_01 gibt Spieler 1
    /// genau **drei** Klasse-3-Sätze.
    ///
    /// ⚠ `ukol >= 100` heisst im Original „tot, Satz noch belegt" (@0x40B5A0
    /// schreibt 0x64 ohne freizugeben). Hier steht `Dead` dafür.
    /// </summary>
    private int UnitClassCount(int cls, int player)
    {
        if (cls == 0)
            return UnitClassCount(1, player) + UnitClassCount(2, player)
                 + UnitClassCount(3, player) + UnitClassCount(4, player);
        if (cls == 4) return AircraftCount(player);
        int n = 0;
        foreach (var e in _entities)
        {
            if (e.IsBuilding || e.IsProp || e.Dead || e.Owner != player) continue;
            bool hit = cls switch
            {
                1 => e.Subclass is 0 or 3,
                2 => e.Subclass == 1,
                3 => e.Subclass is 4 or 5,
                _ => false,
            };
            if (hit) n++;
        }
        return n;
    }

    /// <summary>
    /// Klasse 4: die Flugzeuge eines Spielers.
    ///
    /// ⚠ **Eine Lücke, und sie ist benannt.** Das Original zählt sec19, ein
    /// eigenes Feld von 200 Sätzen zu 68 Byte, unabhängig von den Gebäuden. Die
    /// Engine führt Flugzeuge nur als Hangar-Einträge der Flughäfen. Solange
    /// sie nirgends sonst existieren, ist das dieselbe Zahl; sobald eines
    /// fliegt, wäre es zu wenig. Keine erfundene Zahl, aber auch nicht das
    /// Original.
    /// </summary>
    private int AircraftCount(int player)
    {
        int n = 0;
        foreach (var e in _entities)
            if (e.IsBuilding && !e.Dead && e.Owner == player && e.Hangar != null)
                n += e.Hangar.Count;
        return n;
    }

    /// <summary>
    /// `g_buildings_count(klasse, spieler)` @0x4CFB10, Verteiler 0x4CFBCC.
    /// Ebenfalls gelesen und auf beiden GAME.EXE mit denselben Sofortwerten:
    ///
    ///   0 @0x4CFAC0  jedes Gebäude des Spielers (typ != 0)
    ///   1            count_objects(2) + (3) + (4)   die drei FABRIKEN
    ///   2            count_objects(10) + (15)       Rohstoffminen
    ///   3            count_objects(6) + (12)        Bahnhöfe
    ///
    /// Die Gegenprobe steht in OBJECTG.TXT: Missionsziel #023 heisst „Bauen Sie
    /// fünf Rohstoffminen" und die Bedingung ist `buildings(2,0) == 5` —
    /// wörtlich. Und #005 „Wiederaufnahme der Produktion" prüft
    /// `buildings(1,0) > 0`, also **eine eigene Fabrik**; map_05 gibt Spieler 0
    /// keine, er muss sie erobern.
    /// </summary>
    private int BuildingClassCount(int cls, int player)
    {
        int n = 0;
        foreach (var e in _entities)
        {
            if (!e.IsBuilding || e.Dead || e.Owner != player) continue;
            bool hit = cls switch
            {
                0 => e.BType != 0,
                1 => e.BType is 2 or 3 or 4,
                2 => e.BType is 10 or 15,
                3 => e.BType is 6 or 12,
                _ => false,
            };
            if (hit) n++;
        }
        return n;
    }

    /// <summary>
    /// Satzbyte +0x0a (das spieleigene `typ`) für eine Einheit, die NICHT von
    /// der Karte kommt, sondern gebaut oder als Verstärkung eingesetzt wurde.
    ///
    /// ⚠ CORRECTED 10.08.2026 — hier stand fest `Subclass = 1`. Das ist der
    /// Wert für eine PERSON, und seit die Klassen gelesen sind, zählte damit
    /// jeder gebaute Panzer als Infanterist.
    ///
    /// Gemessen an 29 Kartenexporten: Fahrwerk 148/149 → typ 1, 161..168 →
    /// typ 0, die Schiffsrümpfe 150..153 → typ 4 und 157/158 → typ 5.
    /// ⚠ UNSERE SETZUNG sind 154..156: sie sind ebenfalls Schiffsrümpfe, kamen
    /// in keiner Karte vor, und werden hier wie die leichten geführt.
    /// </summary>
    private static int TypeOfChassis(int propulsion) => propulsion switch
    {
        148 or 149 => 1,
        >= 150 and <= 156 => 4,
        157 or 158 => 5,
        _ => 0,
    };

    /// <summary>
    /// `--damage-check`: die Schadensstufen eines Gebäudes durchfahren und
    /// zählen, was dabei wirklich gezeichnet würde.
    ///
    /// Ohne das wäre »Gebäudeschaden ist gebaut« eine Behauptung: die Formel
    /// könnte stimmen und die mittleren Muster trotzdem leer sein. Gezählt wird
    /// darum, wie viele Kacheln der Stapel bei jeder Stufe legt.
    /// </summary>
    public string DamageCheckLine()
    {
        if (Patterns == null) return "damage-check: keine Muster geladen";
        var seen = new HashSet<int>();
        var lines = new List<string>();
        foreach (var e in _entities)
        {
            if (!e.IsBuilding || e.HpMax <= 0 || !seen.Add(e.BType)) continue;
            var bt = Patterns.GetBuildingType(e.BType);
            if (bt.IsEmpty) continue;
            int save = e.Hp;
            var parts = new List<string>();
            foreach (int pct in new[] { 100, 75, 50, 25, 1 })
            {
                e.Hp = Mathf.Max(0, e.HpMax * pct / 100);
                int f = DamageFrame(e);
                int tiles = 0;
                for (int k = 0; k < f; k++)
                    for (int dx = 0; dx < Import.CwpFile.PatternWidth; dx++)
                        for (int dy = 0; dy < Import.CwpFile.PatternHeight; dy++)
                            if (Patterns.PatternTile(bt.FirstPattern + k, dx, dy) != 0) tiles++;
                parts.Add($"{pct}%:Stufe{f}/{tiles}K");
            }
            e.Hp = save;
            lines.Add($"Typ{e.BType}(hp_max {e.HpMax}, {bt.PatternCount} Muster) " +
                      string.Join(" ", parts));
        }
        return lines.Count == 0
            ? "damage-check: keine Gebaeude mit hp_max auf dieser Karte"
            : "damage-check:\n   " + string.Join("\n   ", lines);
    }

    // ================= Cheat-Mode ============================================
    //
    /// <summary>
    /// Die drei Schummelschalter. ⚠ **UNSERE ZUTAT von A bis Z** — das Original
    /// von 1997 hat keinen Cheat-Mode; es gibt in beiden GAME.EXE keine
    /// Tastenfolge und keinen Debugstring, der einen anbietet. Sie stehen
    /// darum ausdrücklich neben dem Spiel und nicht darin.
    ///
    /// Sie wirken nur auf den <see cref="ViewPlayer"/> und seine Verbündeten —
    /// ein Schalter, der auch dem Gegner hilft, wäre keiner.
    /// </summary>
    public static bool CheatGodMode, CheatAmmo, CheatFuel;

    /// <summary>Gilt der Schummel für diese Einheit? Nur eigene und verbündete
    /// — <see cref="Allied"/> zieht die Grenze, die auch der Beschuss zieht.
    /// </summary>
    private bool Cheated(Entity e)
        => ViewPlayer >= 0 && e.Owner >= 0 && Allied(e.Owner, ViewPlayer);

    /// <summary>Dasselbe für ein Flugzeug. `Special.Owner` ist der Spieler des
    /// Flughafens, dem es gehört — und er ist erst gesetzt, wenn der bekannt
    /// ist; solange bleibt das Flugzeug ungeschummelt statt versehentlich für
    /// alle.</summary>
    private bool Cheated(Special a)
        => ViewPlayer >= 0 && a.Owner >= 0 && Allied(a.Owner, ViewPlayer);

    /// <summary>Was der Cheat-Mode gerade tut — für die Statuszeile und den
    /// Prüfstand, damit ein eingeschalteter Schummel nie unbemerkt läuft.</summary>
    public static string CheatLine()
    {
        var on = new List<string>();
        if (CheatGodMode) on.Add("Unverwundbar");
        if (CheatAmmo) on.Add("Munition");
        if (CheatFuel) on.Add("Sprit");
        return on.Count == 0 ? "" : "CHEAT: " + string.Join(" + ", on);
    }

    /// <summary>
    /// `--cheat-check`: die drei Schalter wirklich ausüben statt sie zu
    /// behaupten — eine eigene Einheit beschiessen, eine schiessen lassen, eine
    /// fahren lassen, und jedes Mal vorher/nachher melden.
    ///
    /// ⚠ Gemessen wird an einer EIGENEN und einer FREMDEN Einheit. Ein
    /// God-Mode, der auch den Gegner unverwundbar macht, wäre keiner — und das
    /// sähe man einer Zeile »hp unverändert« nicht an.
    /// </summary>
    public string CheatCheckLine()
    {
        Entity? mine = null, foe = null;
        foreach (var e in _entities)
        {
            if (e.IsBuilding || e.IsProp || e.Dead || e.HpMax <= 0) continue;
            if (mine == null && Allied(e.Owner, ViewPlayer)) mine = e;
            else if (foe == null && e.Owner >= 0 && !Allied(e.Owner, ViewPlayer)) foe = e;
            if (mine != null && foe != null) break;
        }
        if (mine == null) return "cheat-check: keine eigene Einheit auf dieser Karte";

        var log = new List<string> { MapEntityLayer.CheatLine() is { Length: > 0 } c ? c : "CHEAT: alle aus" };

        int hp0 = mine.Hp;
        ApplyHit(-1, _entities.IndexOf(mine), mine, 50);
        log.Add($"eigene Einheit: 50 Schaden, hp {hp0} -> {mine.Hp}");
        if (foe != null)
        {
            int fhp = foe.Hp;
            ApplyHit(-1, _entities.IndexOf(foe), foe, 50);
            log.Add($"fremde Einheit: 50 Schaden, hp {fhp} -> {foe.Hp}");
        }
        else log.Add("fremde Einheit: keine auf der Karte");

        // Munition und Sprit an derselben Einheit, ueber die echten Zaehler
        mine.Ammo = mine.AmmoMax = 10;
        int a0 = mine.Ammo;
        if (!(CheatAmmo && Cheated(mine))) mine.Ammo--;
        log.Add($"ein Schuss: Munition {a0} -> {mine.Ammo}");

        mine.Fuel = mine.FuelMax = 10;
        int f0 = mine.Fuel;
        if (!(CheatFuel && Cheated(mine))) mine.Fuel--;
        log.Add($"ein Schritt: Sprit {f0} -> {mine.Fuel}");
        return "cheat-check: " + string.Join(" | ", log);
    }

    /// <summary>
    /// `--air-buy-check`: am Flughafen des eigenen Spielers so lange kaufen, bis
    /// es nicht mehr geht — und dabei melden, WORAN es scheitert.
    ///
    /// ⚠ Ein Prüfstand, der nur »gekauft« meldet, sieht nicht, ob die richtige
    /// Schranke greift. Das Original hat genau zwei, in dieser Reihenfolge
    /// (Hangar, dann Teile), und beide müssen vorkommen können.
    /// </summary>
    public string AirBuyCheckLine()
    {
        Entity? ap = null;
        foreach (var e in _entities)
            if (e.IsBuilding && !e.Dead && e.BType == 9 && Allied(e.Owner, ViewPlayer))
            { ap = e; break; }
        if (ap == null) return "air-buy-check: kein eigener Flughafen auf dieser Karte";

        var menu = AirMenu(ap);
        var names = new List<string>();
        foreach (var d in menu) names.Add($"{d.Name} {d.CostW}/{d.CostF}/{d.CostS}");
        int hang0 = ap.Hangar?.Count ?? 0;
        var log = new List<string>
        {
            $"Flughafen slot {ap.Slot}, Lager {ap.StockW}/{ap.StockF}/{ap.StockS}, " +
            $"Hangar {hang0}/{ap.HangarSize}, Menue {menu.Count}: {string.Join(", ", names)}",
        };
        int bought = 0;
        for (int i = 0; i < 40; i++)
        {
            ap.MenuIndex = i % Mathf.Max(1, menu.Count);
            if (!BuyAircraft(ap)) { log.Add($"Abbruch nach {bought}: {_order}"); break; }
            bought++;
        }
        log.Add($"gekauft {bought}, Lager {ap.StockW}/{ap.StockF}/{ap.StockS}, " +
                $"Hangar {ap.Hangar?.Count ?? 0}/{ap.HangarSize}, " +
                $"Flugzeuge im Feld {_special.Count}");
        return "air-buy-check: " + string.Join(" | ", log);
    }

    /// <summary>What the script says, for the harness.</summary>
    public string MissionScriptLine() => _mscript?.Line() ?? "";

    /// <summary>Die Missionsziele fuer die Statuszeile: Nummer, Zustand und
    /// die erste Zeile des zugehoerigen Hilfetexts. Ohne sie sieht der Spieler
    /// nicht, dass eine Untermission laeuft — und schon gar nicht, dass er sie
    /// erfuellt hat.</summary>
    public string MissionObjectiveLine()
    {
        if (_mscript == null) return "";
        var objs = _mscript.Objectives();
        if (objs.Count == 0) return "";
        var parts = new List<string>();
        foreach (var (text, state) in objs)
        {
            // ⚠ Nicht einfach der erste Absatz: #110 faengt mit dem blossen
            // Kopf »@Untermission« an, und der ergab nach dem Entfernen der
            // Auszeichnung eine LEERE Zeile. Genommen wird der erste Absatz,
            // der nach dem Abstreifen noch etwas sagt.
            // Die Texte sind nach demselben Muster gebaut: Kopf
            // (»@Untermission«), Vorrede, dann »@Ziel der Untermission« und
            // DANACH die eigentliche Aufgabe. Genau die wollen wir — der Kopf
            // allein sagt nichts (der erste Versuch zeigte »Untermission«).
            var paras = UI.HelpWindow.TextOf(text);
            string name = $"Ziel {text}";
            if (paras != null)
            {
                int after = -1;
                for (int q = 0; q < paras.Count; q++)
                    if (paras[q].Contains("Ziel") || paras[q].Contains("ziel"))
                    { after = q + 1; break; }
                if (after >= 0 && after < paras.Count)
                    name = paras[after].Replace("@", "").Trim();
                else
                    foreach (string q in paras)
                    {
                        string t = q.Replace("@", "").Trim();
                        if (t.Length < 16) continue;
                        name = t; break;
                    }
            }
            // ... statt … : die Originalschrift hat kein Auslassungszeichen
            if (name.Length > 46) name = name[..46].TrimEnd() + "...";
            // Die Originalschrift hat keine eckigen Klammern — Woerter statt Zeichen.
            parts.Add((state >= 10 ? "ERFUELLT: " : "OFFEN: ") + name.Trim());
        }
        return "ZIELE   " + string.Join("   ", parts);
    }

    /// <summary>
    /// `--depot-check` — das Versorgungsdepot von Anfang bis Ende.
    ///
    /// <para>Die Spielermeldung war: in Kampagne 2 lassen sich die
    /// Versorgungshelis nicht bauen, und ohne sie ist die Mission nicht
    /// durchspielbar. Der Prüfstand geht darum den ganzen Weg ab, statt eine
    /// Zahl zu setzen: Depot finden → Menü ansehen → kaufen → prüfen, dass das
    /// Geld abgezogen ist und der Heli fliegt.</para>
    ///
    /// <para>⚠ Was er NICHT beweisen kann: dass Typ 14 im Original dieses
    /// Fenster öffnet. Das ist unsere Setzung (siehe
    /// <see cref="SupplyDepotType"/>), und ein grünes Ergebnis hier heißt nur,
    /// dass unser Weg in sich stimmt.</para>
    /// </summary>
    public string DepotCheck()
    {
        var sb = new System.Text.StringBuilder();
        // ⚠ Die Flugzeugvorlagen entstehen erst im Missionsstart. Ohne diesen
        // Anstoss meldete der Pruefstand »nichts kaufbar« und sah damit genau
        // wie ein kaputter Kaufweg aus — dieselbe Falle wie beim Skript.
        if (_airDesigns == null || _airDesigns.Count == 0) FillCampaignAirDesigns();
        sb.Append($"depot-check: Flugzeugvorlagen {_airDesigns?.Count ?? 0} ({_airSource})\n");

        if (_airDesigns != null)
            for (int k = 0; k < 8 && k < _airDesigns.Count; k++)
                sb.Append($"   [{k}] {_airDesigns[k].Name} Spieler {_airDesigns[k].Player} " +
                          $"Payload {_airDesigns[k].Payload} Art {_airDesigns[k].Kind}\n");
        var depots = new List<int>();
        for (int i = 0; i < _entities.Count; i++)
            if (IsSupplyDepot(_entities[i]) && !_entities[i].Dead) depots.Add(i);
        sb.Append($"depot-check: {depots.Count} Nachschub-Posten auf der Karte");
        if (depots.Count == 0) return sb.Append(" — nichts zu pruefen").ToString();

        // ⚠ HIER STAND `depots.Find(...)`, und das war ein Fehler DES
        // PRUEFSTANDS, nicht der Engine: `List<int>.Find` liefert ohne Treffer
        // `default(int)` = 0 — also die Entitaet 0, die mit dem Depot nichts zu
        // tun hat. Der Lauf meldete daraufhin »Depot 0 auf (2,12), Spieler 0«
        // und »nichts kaufbar«, obwohl er nie ein Depot in der Hand hatte, und
        // meine eigene Fehlerzeile behauptete eine Begruendung (»Entwuerfe
        // nicht freigegeben«), die der Code gar nicht geprueft hatte. Die
        // Kruecke des Pruefstands ist selbst eine Annahme.
        int own = -1;
        foreach (int i in depots)
            if (_entities[i].Owner == ViewPlayer) { own = i; break; }
        sb.Append("\n   Besitzer der Posten:");
        foreach (int i in depots)
            sb.Append($" Platz {_entities[i].Slot}->Spieler {_entities[i].Owner}");
        // ⭐ Und DAS ist der eigentliche Ablauf von Kampagne 2: der Posten ist
        // HERRENLOS (Spieler -1). Der Spieler hat es so beschrieben — man
        // startet mit wenig Sprit und Munition, nur ein Fahrzeug schafft es bis
        // zum Depot, nimmt es EIN, und kann dann die Helis kaufen, die den Rest
        // der Truppe auffuellen. Der Pruefstand stellt die Einnahme nach, statt
        // sie vorauszusetzen: ohne sie prueft er den Kaufweg gar nicht.
        if (own < 0)
        {
            own = depots[0];
            sb.Append($"\n   herrenlos — Pruefstand nimmt Platz {_entities[own].Slot} " +
                      $"fuer Spieler {ViewPlayer} ein (im Spiel: hinfahren)");
            // ⚠ NICHT MEHR EINNEHMEN. Der Posten ist auf map_02 herrenlos und
            // bleibt es — man faehrt auf ihn drauf, man nimmt ihn nicht ein.
            // Der Pruefstand hat die Einnahme vorher stillschweigend
            // nachgestellt und damit die eigentliche Frage uebersprungen: kann
            // man am HERRENLOSEN Posten kaufen? Genau daran ist es im Spiel
            // gescheitert, und der Pruefstand hat es nicht gesehen.
        }

        var e = _entities[own];
        _selected = own;
        int who = e.Owner is >= 0 and <= 7 ? e.Owner : 0;
        var menu = AirMenu(e);
        sb.Append($"\n   Depot {e.Slot} auf ({e.Col},{e.Row}), Spieler {e.Owner}, " +
                  $"Kontostand ${_money[who]}");
        sb.Append($"\n   Menue: {menu.Count} Eintraege");
        foreach (var d in menu) sb.Append($" [{d.Name} Art {d.Kind} ${HeliPrice}]");
        sb.Append($"\n   Bau-Panel: {BuildPanelTitle()} / {BuildPanelRows().Count} Zeilen");
        if (menu.Count == 0)
            return sb.Append("\n   NICHTS KAUFBAR — die Entwuerfe 13/14 sind fuer " +
                             $"Spieler {e.Owner} nicht freigegeben").ToString();

        // ohne Geld muss es abgelehnt werden, mit Geld gehen — beide Richtungen
        // ⚠ Ueber den KLICKWEG, nicht ueber BuyAircraft direkt: genau daran
        // ist es im Spiel gescheitert, und dieser Pruefstand hat es zweimal
        // nicht gesehen, weil er die Abkuerzung nahm.
        _selected = own;
        int keep = _money[who];
        _money[who] = HeliPrice - 1;
        int hadPoor = _special.Count;
        BuildPanelPick(0);
        bool poor = _special.Count > hadPoor;
        sb.Append($"\n   mit ${HeliPrice - 1}: {(poor ? "GEKAUFT — falsch!" : "abgelehnt")}  ({_order})");

        _money[who] = keep < HeliPrice ? HeliPrice * 2 : keep;
        int before = _money[who], hadAir = _special.Count;
        BuildPanelPick(0);
        bool ok = _special.Count > hadAir;
        sb.Append($"\n   mit ${before}: {(ok ? "gekauft" : "ABGELEHNT — falsch!")}  " +
                  $"Kontostand ${_money[who]} (-{before - _money[who]}), " +
                  $"Flugzeuge {hadAir} -> {_special.Count}");
        if (_special.Count > hadAir)
        {
            var a = _special[^1];
            sb.Append($"\n   neuer Heli: {a.Name}, Art {a.Kind}, Ladung {a.Cargo}, " +
                      $"geparkt {a.Stored} (soll false sein — Art 13/14 fliegen sofort)");
        }
        return sb.ToString();
    }

    /// <summary>`--script-coverage` — was die Runtime von diesem Missionsskript
    /// ausführen kann, und was ihr dafür fehlt. Ein fehlender Haken macht eine
    /// Bedingung falsch und eine Wirkung still; beides sieht im Spiel aus wie
    /// »die Mission tut nichts«. Siehe MissionScript.Coverage.</summary>
    public string ScriptCoverage()
    {
        if (_mscript == null) MissionScriptTick(0.001f);
        if (_mscript == null) return "script-coverage: kein Skript fuer diese Mission";
        string fehlt = _mscript.Coverage(out int rules, out int blocked);
        string probe = _mscript.ImapProbe();
        if (probe.Length > 0) GD.Print("   " + probe);
        return $"script-coverage: M{_mscript.Mission} {rules} Regeln, " +
               $"{rules - blocked} ausfuehrbar, {blocked} blockiert" +
               (fehlt.Length > 0 ? "   FEHLT: " + fehlt : "   (alles verdrahtet)");
    }

    /// <summary>
    /// `--tutorial-check` — den tutorialartigen Ablauf einer Mission durchgehen,
    /// Schritt für Schritt, und sagen, welches Fenster woran hängt.
    ///
    /// <para>⚠ Ohne diesen Prüfstand ist der Ablauf gar nicht prüfbar: die
    /// erste Regel von Mission 1 wartet darauf, dass der Spieler eine Einheit
    /// ANKLICKT (<c>selected &lt; 8000</c>), und ein Lauf ohne Hand klickt nie.
    /// Ein leeres Ergebnis sähe darum genauso aus wie ein kaputter Ablauf —
    /// dieselbe Falle wie am 09.08. beim `--script-check`, der nur zerstören
    /// konnte.</para>
    ///
    /// <para>Er stellt der Reihe nach her, was die Bedingungen verlangen:
    /// eine angewählte Einheit, eine angewählte Gruppe, ein Ereignis, und die
    /// Position des eigenen Panzers. Nach jedem Schritt wird gefragt, welche
    /// Fenster aufgegangen sind.</para>
    /// </summary>
    public string TutorialCheck()
    {
        var sb = new System.Text.StringBuilder();
        // Das Skript entsteht erst im ersten Takt (samt seiner Haken) — ohne
        // diesen einen Takt meldete der Pruefstand »kein Skript« und sah damit
        // genau wie ein fehlendes aus.
        if (_mscript == null) MissionScriptTick(0.001f);
        if (_mscript == null) return "tutorial-check: kein Skript fuer diese Mission";
        var seen = new List<string>();
        _mscript.ShowText = (id, art, x, y) =>
        {
            seen.Add($"#{id}@{x},{y}");
            UI.HelpWindow.Show(GetTree().Root, id, x, y);
        };

        int own = -1;
        for (int i = 0; i < _entities.Count; i++)
            if (!_entities[i].IsBuilding && !_entities[i].IsProp && !_entities[i].Dead
                && _entities[i].Owner == ViewPlayer) { own = i; break; }
        sb.Append($"tutorial-check: Mission {_mscript.Mission}, eigene Einheit " +
                  $"{(own >= 0 ? $"Platz {_entities[own].Slot} auf ({_entities[own].Col},{_entities[own].Row})" : "KEINE")}\n");

        void Step(string was)
        {
            int before = seen.Count;
            for (int t = 0; t < 30; t++) _mscript!.Tick(0.2f);
            var neu = seen.GetRange(before, seen.Count - before);
            sb.Append($"   {was,-34} -> {(neu.Count == 0 ? "kein Fenster" : string.Join(" ", neu))}\n");
        }

        Step("nichts angewaehlt");
        if (own >= 0) { _sel.Clear(); _sel.Add(own); _selected = own; }
        Step("eine Einheit angewaehlt");
        _sel.Clear();
        for (int i = 0; i < _entities.Count && _sel.Count < 2; i++)
            if (!_entities[i].IsBuilding && !_entities[i].IsProp && !_entities[i].Dead
                && _entities[i].Owner == ViewPlayer) _sel.Add(i);
        Step($"Gruppe angewaehlt ({_sel.Count})");
        _mscript.LastEvent = 1;
        Step("Ereignis 1");
        // den eigenen Panzer nach Norden setzen — die Ortsabfragen des Blocks
        // haengen an seiner ZEILE (byte[0x6E26C9] gegen 30 und 20)
        if (own >= 0)
        {
            _entities[own].Row = 25;
            Step("Panzer auf Zeile 25 (< 30)");
            _entities[own].Row = 15;
            Step("Panzer auf Zeile 15 (< 20)");
        }
        // Und die Untermission zu Ende spielen: die Schiffe des Gegners
        // versenken. Erst daran laesst sich sehen, ob die Auszahlung ankommt —
        // ein Prüfstand, der die Zahl nur setzt, prüft die Zahl und nicht die
        // Mechanik (Regel 11).
        var ships = new List<int>();
        for (int i = 0; i < _entities.Count; i++)
        {
            var e = _entities[i];
            if (!e.IsBuilding && !e.IsProp && !e.Dead && e.Owner != ViewPlayer &&
                e.Subclass is 4 or 5) ships.Add(i);   // Klasse 3 = Schiffe
        }
        sb.Append($"   Schiffe des Gegners: {ships.Count}, Kontostand {Money(ViewPlayer)} $\n");
        foreach (int i in ships)
        {
            int before = Money(ViewPlayer);
            Kill(i, _entities[i]);
            for (int t = 0; t < 30; t++) _mscript.Tick(0.2f);
            sb.Append($"   Schiff {_entities[i].Name} versenkt -> {Money(ViewPlayer)} $ " +
                      $"({(Money(ViewPlayer) - before >= 0 ? "+" : "")}{Money(ViewPlayer) - before})\n");
        }
        sb.Append($"   zusammen {seen.Count} Fenster, {UI.HelpWindow.OpenCount} offen, " +
                  $"{_mscript.RulesFired} Regeln gefeuert, Kontostand {Money(ViewPlayer)} $");
        return sb.ToString();
    }

    /// <summary>
    /// `--produce-check`: die Produktionskette der Mission an Spieler 0 übergeben
    /// und dann LAUFEN LASSEN.
    ///
    /// ⚠ Warum es das gibt: `--script-check` hebt ein beobachtetes Lager um
    /// 1000 und meldet »erzwungen«. Damit ist Mission 5s Bedingung zwar wahr,
    /// aber über die Frage, um die es geht — kann die Engine überhaupt wieder
    /// produzieren? — sagt das GAR NICHTS. Genau die Fehlerklasse aus Regel 9:
    /// ein Prüfstand, der nur eine Richtung kann, prüft nur eine Richtung.
    ///
    /// map_05 erklärt die Mission: **alle zwölf Gebäude gehören Spieler 1**,
    /// darunter Basis 0 »Canarian« und die beiden Fabriken 1 und 2 mit je 1000
    /// Terranium. Missionsziel #005 heisst »Wiederaufnahme der Produktion«, und
    /// der Weg dahin ist Einnehmen. Der Prüfstand nimmt darum den Griff ab, den
    /// der Spieler von Hand täte, und lässt alles andere echt laufen: die
    /// Fabriken machen aus Terranium Teile, `Haul` fährt sie zur Basis, und die
    /// Mission entscheidet selbst.
    ///
    /// ⚠ UNSERE SETZUNG ist der Umfang der Übergabe: das beobachtete Gebäude
    /// und alles, was es beliefert (Fabriken und Vorkommen desselben früheren
    /// Besitzers). Das Original verlangt, sie einzeln einzunehmen.
    /// </summary>
    public string ProduceCheckLine()
    {
        if (_mscript == null) return "produce-check: kein Skript fuer diese Mission";
        var want = _mscript.WatchedStores();
        if (want.Count == 0) return "produce-check: das Skript liest kein Lager";

        var owners = new HashSet<int>();
        var slots = new HashSet<int>();
        foreach (var (slot, _) in want) slots.Add(slot);
        foreach (var e in _entities)
            if (e.IsBuilding && slots.Contains(e.Slot) && e.Owner >= 0) owners.Add(e.Owner);

        int given = 0;
        foreach (var e in _entities)
        {
            if (!e.IsBuilding || e.Dead) continue;
            bool watched = slots.Contains(e.Slot);
            bool feeder = owners.Contains(e.Owner) && (IsFactory(e) || e.Deposit > 0);
            if (!watched && !feeder) continue;
            if (e.Owner == 0) continue;
            e.Owner = 0;
            e.Team = 0;
            given++;
        }
        // ⚠ UNSERE SETZUNG, und sie ist der Zweck des Prüfstands: der frühere
        // Besitzer wird mit Spieler 0 verbündet. Ohne das nahm er die Fabrik
        // nach 35 Sekunden zurück und schoss sie danach zusammen — gemessen
        // wurde dann die Schlacht statt der Wirtschaft.
        //
        // ⚠ Der erste Anlauf setzte dafür `_standby`, und das tat NICHTS: das
        // Feld ist seit der Kampagnen-Diplomatie ein stillgelegter Notbehelf
        // und wird auf Karten mit Bündnismatrix gar nicht mehr gefragt. Der
        // Lauf sah aus wie ein Ergebnis und war keins.
        int idle = 0;
        foreach (int p in owners)
        {
            if (p is < 0 or > 7) continue;
            _allied[0, p] = _allied[p, 0] = true;
            _haveAllies = true;
            idle++;
        }

        QueueRedraw();
        return $"produce-check: {given} Gebaeude an Spieler 0 uebergeben " +
               $"(beobachtet: {string.Join(",", slots)}; Zulieferer von Spieler " +
               $"{string.Join(",", owners)}), {idle} Spieler verbuendet " +
               "— ab jetzt laeuft die Wirtschaft echt";
    }

    /// <summary>
    /// `--store-check`: the part stores the mission watches, next to the value
    /// it marked them at. Mission 5 ("Wiederaufnahme der Produktion") wins when
    /// both of building 0's stores have GROWN over the mark, and without this
    /// line there is no way to tell a mission that CANNOT be won from one that
    /// has not been won yet.
    /// </summary>
    public string StoreCheckLine()
    {
        if (_mscript == null) return "store-check: kein Skript fuer diese Mission";
        var want = _mscript.WatchedStores();
        if (want.Count == 0) return "store-check: das Skript liest kein Lager";
        var marks = new Dictionary<(int, int), int>();
        foreach (var m in _mscript.StoreMarks()) marks[(m.Slot, m.Off)] = m.Value;

        var parts = new List<string>();
        foreach (var (slot, off) in want)
        {
            Entity? b = null;
            foreach (var e in _entities) if (e.IsBuilding && e.Slot == slot) { b = e; break; }
            if (b == null) { parts.Add($"{slot}+{off:x}:kein Gebaeude"); continue; }
            int now = off switch
            {
                0x28 => b.StockW, 0x2a => b.StockF,
                0x2c => b.StockS, 0x2e => b.StockT, _ => -1,
            };
            string mark = marks.TryGetValue((slot, off), out int mv) ? mv.ToString() : "-";
            parts.Add($"{slot}+{off:x}(Typ{b.BType} P{b.Owner} St{b.State}) {mark}->{now}" +
                      (now > 0 && mark != "-" && now > mv ? " GEWACHSEN" : ""));
        }
        // Woher es kommen muesste: wer produziert ueberhaupt, und hat er Rohstoff
        int fac = 0, facT = 0, mines = 0, deposit = 0;
        foreach (var e in _entities)
        {
            if (e.Dead || e.Owner != 0) continue;
            if (IsFactory(e)) { fac++; facT += e.StockT; }
            if (e.Deposit > 0) { mines++; deposit += e.Deposit; }
        }
        // Wer produziert, und was ist aus ihm geworden — ohne das war beim
        // Messen nicht zu unterscheiden, ob eine Fabrik zerstoert,
        // zurueckerobert oder nur leer ist
        var facs = new List<string>();
        foreach (var e in _entities)
            if (e.IsBuilding && IsFactory(e))
                facs.Add($"{e.Slot}:Typ{e.BType} P{e.Owner}" +
                         (e.Dead ? " TOT" : "") + $" T{e.StockT} St{e.State}" +
                         $" W{e.StockW}/F{e.StockF}/S{e.StockS}");

        return "store-check: " + string.Join("   ", parts) +
               $"   | Spieler 0: {fac} Fabriken mit {facT} Terranium, " +
               $"{mines} Vorkommen mit {deposit}" +
               (facs.Count > 0 ? "   | Fabriken " + string.Join(" ", facs) : "");
    }

    /// <summary>
    /// Harness: knock out every building the mission script watches, so the
    /// whole chain can be checked without playing the mission — the condition
    /// reads the world, the rule latches, `end` fires, and Verdict() carries it
    /// into the campaign. Reports what it destroyed.
    /// </summary>
    public string MissionScriptForceCheck()
    {
        if (_mscript == null) return "script-check: kein Skript fuer diese Mission";

        // ⚠ ZUERST die Frage, die am 09.08. einen Tag gekostet hat: gewinnt das
        // Skript die Mission schon im Anfangszustand? Drei invertierte
        // Bedingungen taten genau das, und weil der Prueflauf erst erzwungen und
        // dann geschaut hat, sah es jedes Mal wie ein Erfolg aus. Ein Durchlauf
        // ohne dt wertet alle Regeln einmal aus, ohne die Uhr zu bewegen.
        _mscript.Tick(0.0);
        if (_mscript.Ended)
            return "script-check: ⚠ das Skript entscheidet die Mission SOFORT (" +
                   (_mscript.Success ? "gewonnen" : "verloren") +
                   "), bevor irgendetwas erzwungen wurde — eine Bedingung steht " +
                   "verkehrt herum oder ein Anfangswert fehlt";

        // Nicht `EndConds()`: seit die Setzer-Regeln mitlaufen, steht hinter
        // einer Endbedingung ueber eine Blockvariable eine WELTbedingung, und
        // die ist das, was sich erzwingen laesst.
        var conds = _mscript.ChainConds();
        // ⚠ nicht hier aussteigen, wenn nur keine obj_owner-Plaetze dabei sind —
        // die meisten Missionen enden ueber eine ZAEHLbedingung, und ein
        // vorzeitiges return hat die glatt uebersprungen
        if (conds.Count == 0)
            return "script-check: das Skript prueft nichts, was sich erzwingen liesse";

        int killed = 0, given = 0, left = 0, zoned = 0;
        var untouched = new List<string>();

        // ⚠ ZUERST die markierte Einheit, denn `unit_field` fragt nach IHRER
        // Zelle und steht in der Kette VOR der Bedingung, die sie überhaupt
        // benennt. Mission 14 erwartet Colonel Hullmann (Entwurf 191), und die
        // Karte bringt ihn nicht mit — `space_in` setzt ihn ein.
        //
        // Der Anflug dauert im Original 37 Takte, und ein Prüflauf, der nach
        // einem Takt fragt »ist Hullman da?«, bekäme NEIN aus einem Grund, der
        // mit der geprüften Bedingung nichts zu tun hat. Also erst alles landen
        // lassen, was schon unterwegs ist, und die echte Einheit nehmen; die
        // Umwidmung unten bleibt nur der Notnagel, wenn gar nichts kam.
        int landed = _mscript.FlushIncoming();
        if (landed > 0) GD.Print($"script-check: {landed} Verstaerkung(en) vorgezogen");
        var stores = new List<Campaign.MissionScript.Cond>();
        Entity? marked = null;
        foreach (var c in conds)
        {
            if (c.Kind != "unit_index") continue;
            foreach (var e in _entities)
                if (!e.IsBuilding && !e.Dead && e.Owner == c.A && e.Mark == c.B) { marked = e; break; }
            if (marked != null) continue;
            foreach (var e in _entities)
                if (!e.IsBuilding && !e.Dead)
                {
                    e.Owner = c.A;
                    e.Mark = c.B;
                    marked = e;
                    zoned++;
                    break;
                }
            if (marked == null) { left++; untouched.Add(Show(c)); }
        }

        foreach (var c in conds)
        {
            if (c.Kind == "unit_index") continue;          // oben schon erledigt
            // »v[a] < Lager« heisst: das Lager muss WACHSEN — und zwar NACHDEM
            // die Mission es sich gemerkt hat. Darum erst zurueckstellen; siehe
            // unten.
            if (c.Kind == "var_vs_store") { stores.Add(c); continue; }
            if (c.Kind == "unit_field")
            {
                if (marked == null) { left++; untouched.Add(Show(c)); continue; }
                if (c.B == 0) marked.Col = c.C;
                else if (c.B == 1) marked.Row = c.C;
                else { left++; untouched.Add(Show(c)); continue; }
                given++;
                continue;
            }
            if (c.Kind == "obj_owner")
            {
                // Ein Platz bekommt schlicht den verlangten Besitzer. 12 heisst
                // im Original leer, also zerstoert; alles andere heisst besetzt.
                int want = c.Op == "==" ? c.B : (c.B == 12 ? 0 : 12);
                if (c.Op != "==" && c.Op != "!=") { left++; untouched.Add(Show(c)); continue; }
                bool done = false;
                foreach (var e in _entities)
                    if (e.IsBuilding && e.Slot == c.A)
                    {
                        if (want == 12) { e.Dead = true; killed++; }
                        else { e.Dead = false; e.Owner = want; given++; }
                        done = true;
                    }
                if (!done) { left++; untouched.Add(Show(c)); }
                continue;
            }

            int target = Campaign.MissionScript.TargetCount(c);
            if (target < 0) { left++; untouched.Add(Show(c)); continue; }

            // ⚠ Nicht jede Endbedingung will Vernichtung. Mission 3 will einen
            // Forschungskomplex, der noch STEHT, Mission 23 genau fuenf Minen.
            // Der Prueflauf zaehlt darum auf die Zielzahl HIN — herunter, indem
            // er ausschlaegt, hinauf, indem er uebergibt.
            var mine = new List<Entity>();
            var spare = new List<Entity>();
            foreach (var e in _entities)
            {
                bool isKind = c.Kind switch
                {
                    "objects" => e.IsBuilding && e.BType == c.A,
                    "units" => !e.IsBuilding,
                    "buildings" => e.IsBuilding,
                    _ => false,
                };
                if (!isKind) continue;
                if (!e.Dead && e.Owner == c.B) mine.Add(e);
                else if (e.Owner != c.B) spare.Add(e);
            }
            while (mine.Count > target)
            {
                var e = mine[^1];
                mine.RemoveAt(mine.Count - 1);
                e.Dead = true;
                killed++;
            }
            while (mine.Count < target && spare.Count > 0)
            {
                var e = spare[^1];
                spare.RemoveAt(spare.Count - 1);
                e.Dead = false;
                e.Owner = c.B;
                mine.Add(e);
                given++;
            }
            // Verlangt die Bedingung etwas, das es auf der Karte GAR NICHT gibt,
            // dann meist deshalb, weil der Spieler es erst bauen soll — Mission
            // 13 will Stromgeneratoren, Mission 17 Rohstoffminen. Der Prueflauf
            // kann nicht bauen, also widmet er ein Gebaeude um: derselbe
            // Weltzustand ohne die Bauzeit. Zuerst zerstoerte, dann fremde, damit
            // moeglichst wenig von dem verschwindet, was andere Glieder zaehlen.
            if (c.Kind == "objects" && mine.Count < target)
                for (int pass = 0; pass < 2 && mine.Count < target; pass++)
                    foreach (var e in _entities)
                    {
                        if (mine.Count >= target) break;
                        if (!e.IsBuilding || mine.Contains(e)) continue;
                        if (pass == 0 && !e.Dead) continue;
                        e.Dead = false;
                        e.BType = c.A;
                        e.Owner = c.B;
                        mine.Add(e);
                        zoned++;
                    }
            if (mine.Count != target) { left++; untouched.Add($"{Show(c)} [nur {mine.Count}]"); }
        }
        // ⚠ Die Lager zuletzt, und erst nach einem Durchlauf. Mission 5 merkt
        // sich beim ersten eigenen Gebaeude der Klasse 1 die beiden Teilelager
        // und gewinnt, wenn BEIDE gewachsen sind. Wer das Lager vorher hebt,
        // hebt die Marke gleich mit — die Bedingung ist dann per Konstruktion
        // falsch, und der Lauf schweigt darueber. Also: erst markieren lassen,
        // dann heben.
        if (stores.Count > 0)
        {
            _mscript.Tick(0.0);
            foreach (var c in stores)
            {
                bool hit = false;
                foreach (var e in _entities)
                    if (e.IsBuilding && e.Slot == c.B)
                    {
                        int add = c.Op is "<" or "<=" ? 1000 : 0;
                        if (c.C == 0x28) e.StockW += add;
                        else if (c.C == 0x2a) e.StockF += add;
                        else if (c.C == 0x2c) e.StockS += add;
                        else if (c.C == 0x2e) e.StockT += add;
                        else break;
                        hit = true;
                        given++;
                        break;
                    }
                if (!hit) { left++; untouched.Add(Show(c)); }
            }
        }
        var unreachable = _mscript.UnreachableVars();
        // Noch ein Durchlauf, DANN erst die Glieder melden: die Setzer-Regeln
        // wirken erst im Takt nach dem Erzwingen, und eine Diagnose, die davor
        // gedruckt wird, meldet lauter »NEIN« obwohl die Kette gleich schliesst.
        _mscript.Tick(0.0);
        GD.Print("script-check Glieder: " + _mscript.WhyNot());
        return $"script-check: {conds.Count - left} von {conds.Count} Endbedingungen erzwungen " +
               $"({killed} ausgeschlagen, {given} uebergeben, {zoned} umgewidmet)" +
               (left > 0 ? $"; nicht erzwingbar: {string.Join(" ", untouched)}" : "") +
               (unreachable.Count > 0
                   ? $"; ⚠ Variablen ohne Erzeuger: v[{string.Join("] v[", unreachable)}]"
                   : "");
    }

    private static string Show(Campaign.MissionScript.Cond c) =>
        c.Kind == "obj_owner"
            ? $"obj_owner({c.A}){c.Op}{c.B}"
            : $"{c.Kind}({c.A},{c.B}){c.Op}{c.C}";

    public string Verdict()
    {
        // A mission that carries its own script is judged by it and by nothing
        // else — that script is the original's condition, the fallback below is
        // ours.
        if (_mscript != null && _mscript.Decides)
            return _mscript.Ended
                ? (_mscript.Success ? "MISSION ERFUELLT" : "MISSION GESCHEITERT")
                : "";

        // A skirmish does not need the map's own player table — the NET maps
        // carry none at all. Judge it by what is left standing instead.
        if (_aiOn)
        {
            int meS = ViewPlayer;
            if (AssetsOf(meS) == 0) return "MISSION GESCHEITERT";
            foreach (var a in _ai)
                if (!Allied(meS, a.Player) && AssetsOf(a.Player) > 0) return "";
            return "MISSION ERFUELLT";
        }
        if (_players.Count == 0)
        {
            // A campaign level carries no player table either: sec53 is runtime
            // state and only a saved game has it. Judged the same way — out when
            // nothing of yours is left, won when no other side has anything.
            //
            // OUR SETTING: every other slot counts as hostile, except 7. There
            // is no alliance matrix to read here, and slot 7 is left out because
            // in every .DM that does carry sec53 it is allied with everyone —
            // which is what makes it the neutral one.
            int meC = ViewPlayer;
            if (AssetsOf(meC) == 0) return "MISSION GESCHEITERT";
            for (int p = 0; p < 8; p++)
                if (p != meC && p != NeutralSlot && AssetsOf(p) > 0) return "";
            return "MISSION ERFUELLT";
        }
        int me = ViewPlayer;
        if (AssetsOf(me) == 0) return "MISSION GESCHEITERT";
        foreach (var p in _players)
        {
            if (Allied(me, p.Index)) continue;
            if (AssetsOf(p.Index) > 0) return "";
        }
        return "MISSION ERFUELLT";
    }

    /// <summary>How the viewed player's own objective list stands, plus what is
    /// left of the lists belonging to players he is not allied with.</summary>
    public string MissionLine()
    {
        if (_players.Count == 0) return "";
        int me = ViewPlayer;
        int mine = 0, mineLost = 0, foe = 0, foeLost = 0;
        for (int p = 0; p < 8; p++)
        {
            bool friend = Allied(me, p);
            foreach (int slot in _objectives[p])
            {
                var e = _entities.Find(x => x.IsBuilding && x.Slot == slot);
                if (e == null || e.HpMax <= 0) continue;      // InitN placeholders
                if (friend) { mine++; if (e.Dead) mineLost++; }
                else { foe++; if (e.Dead) foeLost++; }
            }
        }
        if (mine + foe == 0) return "";
        string v = Verdict();
        // both counts are "still standing / listed".  Careful with the wording:
        // a list may name buildings that belong to somebody else, so this is
        // "on my side's list", not "my buildings".
        return $"ZIELE eig.Liste {mine - mineLost}/{mine}  " +
               $"Gegnerliste {foe - foeLost}/{foe}" +
               (v.Length > 0 ? "   " + v : "");
    }

    /// <summary>
    /// Progress on the mission's win conditions, grouped by base — 57 individual
    /// targets would not fit any HUD. Returns e.g. "BOLOUGNE 2/6  ST.OMER 0/3".
    /// </summary>
    public string ObjectiveSummary()
    {
        var total = new Dictionary<string, int>();
        var done = new Dictionary<string, int>();
        foreach (var e in _entities)
        {
            if (!e.IsBuilding || !e.IsTarget || e.HpMax <= 0) continue;   // skip InitN
            string k = (e.Name.Length > 0 ? e.Name : "?").ToUpper();
            total[k] = total.GetValueOrDefault(k) + 1;
            if (e.Dead) done[k] = done.GetValueOrDefault(k) + 1;
        }
        if (total.Count == 0) return "";
        var parts = new List<string>();
        foreach (var kv in total)
            parts.Add($"{kv.Key} {done.GetValueOrDefault(kv.Key)}/{kv.Value}");
        parts.Sort();
        int allDone = 0, allTotal = 0;
        foreach (var kv in total) { allTotal += kv.Value; allDone += done.GetValueOrDefault(kv.Key); }
        return $"ZIELE {allDone}/{allTotal}   " + string.Join("   ", parts);
    }

    // ================= production from the design list (sec47) ================

    /// <summary>A buildable design: the game's own name plus its components.</summary>
    private readonly struct Design
    {
        public Design(string name, int prop, int equip, int weapon, bool available, int slot)
            : this(name, prop, equip, weapon, available, slot,
                   Simulation.DesignMath.Compute(weapon, prop, equip))
        {
        }

        public Design(string name, int prop, int equip, int weapon, bool available, int slot,
                      Simulation.DesignMath.Derived derived)
        {
            Name = name; Propulsion = prop; Equip = equip; Weapon = weapon;
            Available = available; Slot = slot; Derived = derived;
        }

        public string Name { get; }
        public int Propulsion { get; }

        /// <summary>The record's derived tail: what the design costs, what it can
        /// take. Read off the record for a design out of sec47, computed by the
        /// game's own formula for one the player drew up — the two agree, which
        /// is what <c>--selftest-designs</c> measures.</summary>
        public Simulation.DesignMath.Derived Derived { get; }

        /// <summary>Price in weapon / chassis / special parts (+0x1a..+0x1c).</summary>
        public int CostW => Derived.CostW;
        public int CostF => Derived.CostF;
        public int CostS => Derived.CostS;

        /// <summary>What the finished unit rolls out with, all from the record:
        /// hit points (+0x1e), attack value (+0x20), a full tank (+0x28) and a
        /// full magazine (+0x2a).</summary>
        public int Hp => Derived.Hp;
        public int Attack => Derived.Attack;
        public int Defence => Derived.Defence;
        public int Fuel => Derived.Fuel;
        public int Ammo => Derived.Ammo;
        public int Range => Derived.Range;
        public int Sight => Derived.Sight;
        public int Reload => Derived.Reload;
        public int Speed => Derived.Speed;

        /// <summary>The record's place in sec47. It matters because the
        /// campaign schedule unlocks designs BY SLOT: the script's `vehicle`
        /// call @0x4d04d0 writes `sec47[slot + 200*player] +0x00 = value` for
        /// all eight players, and 0x51ce20 with stride 46 is sec47 itself. So
        /// the schedule's numbers are slots within a player's block of 200.
        /// </summary>
        public int Slot { get; }

        /// <summary>Record +0x19, exported under the key "body". It is not a
        /// body: the spawn routine @0x4b1b5c writes it into entity +0x10, and
        /// the stats table names rows 65..88 as the equipment (Teleporter,
        /// Repair Device, Shield, Mirror, Illusion …).</summary>
        public int Equip { get; }
        public int Weapon { get; }
        /// <summary>Design record +0x00 — the "enable" flag (58 of 586 are set).</summary>
        public bool Available { get; }
    }

    /// <summary>
    /// Which factory may build a design. The record carries no factory field, so
    /// this follows the factory NAMES: the Waffen-Fabrik builds armed designs
    /// (weapon 1..19 = a real turret), the Spezial-Fabrik the equipment carriers
    /// (weapon 65..79: Repair, Teleporter, Radar, Building Const. …) and the
    /// Fahrwerk-Fabrik everything else, i.e. the plain chassis. Our reading.
    /// </summary>
    private static bool FitsFactory(Design d, int bType) => bType switch
    {
        2 => d.Weapon >= 1 && d.Weapon <= 19,
        4 => d.Weapon >= 65 && d.Weapon <= 79,
        _ => !(d.Weapon >= 1 && d.Weapon <= 19) && !(d.Weapon >= 65 && d.Weapon <= 79),
    };

    /// <summary>Designs a given factory can build, best first.</summary>
    private static List<int> BuildableBy(int bType)
    {
        var list = new List<int>();
        if (_designs == null) return list;
        for (int i = 0; i < _designs.Count; i++)
        {
            var d = _designs[i];
            bool unlocked = d.Available ||
                            (d.Weapon >= 65 && d.Weapon <= 88 && _researchedStatic.Contains(d.Weapon));
            if (unlocked && FitsFactory(d, bType)) list.Add(i);
        }
        if (list.Count == 0)                       // fall back to the whole roster
            for (int i = 0; i < _designs.Count; i++)
                if (FitsFactory(_designs[i], bType)) list.Add(i);
        return list;
    }

    private static List<Design>? _designs;

    /// <summary>Every sec47 row by its RAW index 0..1599, unfiltered — what
    /// `space_in` needs. See the note in <see cref="LoadDesigns"/>.</summary>
    private static readonly Dictionary<int, Design> _designBySlot = new();

    /// <summary>Which mission the list was last built for. The list is static
    /// and survives a scene change, so without this a second mission in the
    /// same session would keep the first one's unlocks.</summary>
    private static int _designsMission = -1;

    private static void LoadDesigns()
    {
        if (_designs != null && _designsMission == UI.SkirmishSetup.CampaignMission) return;
        _designsMission = UI.SkirmishSetup.CampaignMission;
        Simulation.DesignMath.Load();          // the component rows a new design is costed from
        _designs = new List<Design>();
        string path = Core.Content.Path("Maps/unit_designs.json");
        if (!FileAccess.FileExists(path)) return;
        using var f = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        if (f == null) return;
        var json = new Json();
        if (json.Parse(f.GetAsText()) != Error.Ok || json.Data.VariantType != Variant.Type.Dictionary)
            return;
        var root = json.Data.AsGodotDictionary<string, Variant>();
        if (!root.TryGetValue("designs", out var dv) || dv.VariantType != Variant.Type.Dictionary)
            return;
        var seen = new HashSet<string>();
        foreach (var kv in dv.AsGodotDictionary<string, Variant>())
        {
            if (kv.Value.VariantType != Variant.Type.Dictionary) continue;
            var d = kv.Value.AsGodotDictionary<string, Variant>();
            string nm = d.TryGetValue("name", out var nv) ? nv.AsString() : "";
            int prop = GetI(d, "propulsion", 0);
            // Only designs whose chassis we can actually draw, one per name.
            //
            // ⚠ 148 and 149 are the INFANTRY chassis and they belong here.
            // Dropping them is what "the AI only builds Transporters" (point 15
            // of the player's list) really was: the campaign's own schedule
            // unlocks Chaingunner, Laser Trooper, Radar Scout, Pioneer and
            // Lander (rows 50..54, chassis 148/149) long before it unlocks any
            // vehicle, and with those five thrown away the only buildable
            // designs left in the early missions were row 57 "Transporter" and
            // row 84 "Chaingun Tank". Measured against the recovered schedule,
            // not guessed.
            bool drawable = (prop >= 160 && prop <= 175) || prop == 148 || prop == 149;
            if (nm.Length == 0 || !drawable) continue;
            if (!seen.Add(nm)) continue;
            bool avail = false;
            if (d.TryGetValue("flags", out var fv) && fv.VariantType == Variant.Type.Array)
            {
                var fa = fv.AsGodotArray();
                avail = fa.Count > 0 && fa[0].AsInt32() != 0;
            }
            int slot = int.TryParse(kv.Key, out int sl) ? sl : -1;
            int weapon = GetI(d, "weapon", 0), equip = GetI(d, "body", 0);
            // The record carries its own derived tail; use it rather than
            // recomputing, so a design out of the game's data keeps the game's
            // own numbers even where our table is short a component row.
            string raw = d.TryGetValue("raw", out var rw) ? rw.AsString() : "";
            var derived = raw.Length >= 0x2e * 2
                ? Simulation.DesignMath.FromRecordHex(raw)
                : Simulation.DesignMath.Compute(weapon, prop, equip);
            _designs.Add(new Design(nm, prop, equip, weapon, avail, slot, derived));
        }
        // ⚠ Dieselbe Runde noch einmal, aber OHNE die beiden Filter oben.
        // `_designs` ist eine BAULISTE: ein Name kommt nur einmal vor. sec47 ist
        // aber pro Spieler abgelegt (200 Zeilen je Spieler, 1600 im ganzen), und
        // `space_in` nennt seine Einheiten als `typ + 200*spieler` — Spieler 1
        // schickt Zeile 257 »Transporter«, und die faellt der Namensprobe zum
        // Opfer, weil Zeile 57 denselben Namen traegt. Verstaerkung muss darum
        // ueber den ROHEN Index nachschlagen, nicht ueber die Bauliste.
        _designBySlot.Clear();
        foreach (var kv in dv.AsGodotDictionary<string, Variant>())
        {
            if (kv.Value.VariantType != Variant.Type.Dictionary) continue;
            if (!int.TryParse(kv.Key, out int raw)) continue;
            var d = kv.Value.AsGodotDictionary<string, Variant>();
            string nm = d.TryGetValue("name", out var nv) ? nv.AsString() : "";
            if (nm.Length == 0) continue;
            int prop = GetI(d, "propulsion", 0);
            int weapon = GetI(d, "weapon", 0), equip = GetI(d, "body", 0);
            string rawHex = d.TryGetValue("raw", out var rw) ? rw.AsString() : "";
            var der = rawHex.Length >= 0x2e * 2
                ? Simulation.DesignMath.FromRecordHex(rawHex)
                : Simulation.DesignMath.Compute(weapon, prop, equip);
            _designBySlot[raw] = new Design(nm, prop, equip, weapon, false, raw, der);
        }
        ApplyCampaignDesigns();
        LoadOwnDesigns();
    }

    /// <summary>What the campaign has unlocked by mission N, added to what the
    /// design list already offers.
    ///
    /// The MEANING of the schedule's numbers is settled — the script's
    /// `vehicle` call @0x4d04d0 writes `sec47[slot + 200*player] +0x00`, and
    /// 0x51ce20 with stride 46 IS sec47 — but what they add up to is worth
    /// saying out loud: by mission 32 the script has unlocked nine slots in
    /// all, and seven of them are infantry (Chaingunner, Laser Trooper,
    /// Pioneer …). Only 57 "Transporter" and 84 "Chaingun Tank" are vehicles.
    ///
    /// So this list is NOT a buildable roster. It is the set of ready-made
    /// designs; in the original everything else is drawn up by the player in
    /// the design screen, which is exactly what the saved games show — 4.DM
    /// carries slots 47..49 named "H-Cannon-81-165" and the like, components
    /// spelled out in the name, and no state unlocks them.
    ///
    /// The remake has no design screen. Using the schedule as a GATE would
    /// therefore leave a campaign factory with two designs and call it
    /// faithfulness; it is applied as an addition instead, so it can only ever
    /// open something up. The missing piece is named rather than papered over.
    /// </summary>
    private static void ApplyCampaignDesigns()
    {
        int mission = UI.SkirmishSetup.CampaignMission;
        if (_designs == null || mission <= 0) return;
        var u = Campaign.CampaignManager.UnlocksFor(mission);
        if (!u.Known) return;
        int added = 0, on = 0;
        for (int i = 0; i < _designs.Count; i++)
        {
            var d = _designs[i];
            bool sched = d.Slot >= 0 && u.Vehicles.Contains(d.Slot % DesignsPerPlayer);
            if (sched && !d.Available)
            {
                // keep the record's own derived tail — recomputing it here would
                // quietly swap the game's numbers for ours
                _designs[i] = new Design(d.Name, d.Propulsion, d.Equip, d.Weapon, true,
                                         d.Slot, d.Derived);
                added++;
            }
            if (sched || d.Available) on++;
        }
        GD.Print($"designs: {_designs.Count} Entwuerfe, {on} verfuegbar " +
                 $"({added} durch Fahrplan M{mission} dazu)");
    }

    /// <summary>sec47 is 1600 records — eight player blocks of 200.</summary>
    private const int DesignsPerPlayer = 200;

    // ---- the design screen ---------------------------------------------------

    public readonly DesignScreen Designer = new();

    /// <summary>Fill the screen's three part lists from the imported tables:
    /// the propulsions out of the unit catalogue (the chassis run 160..175),
    /// the weapons out of `weapons.json`'s rows 1..19, the equipment out of
    /// `research.json`'s rows 65..79 — the ones a design can actually carry,
    /// which is where the research work drew the line.</summary>
    private void LoadDesignParts()
    {
        if (Designer.Propulsions.Count > 0) return;
        LoadCatalog();
        if (_catalog != null)
            // the catalogue entry is (tier, name) — the NAME is what a player
            // picks a chassis by, not "heavy"
            foreach (var kv in _catalog)
                if (kv.Key >= 160 && kv.Key <= 175 && !string.IsNullOrEmpty(kv.Value.Item2))
                    Designer.Propulsions.Add(new DesignScreen.Part
                    { Id = kv.Key, Name = kv.Value.Item2 });
        Designer.Propulsions.Sort((a, b) => a.Id.CompareTo(b.Id));

        ReadParts("Maps/weapons.json", "types", Designer.Weapons);
        ReadParts("Maps/research.json", "technologies", Designer.Equipment, 65, 79);
        GD.Print($"designer: {Designer.Propulsions.Count} Fahrwerke, " +
                 $"{Designer.Weapons.Count} Waffen, {Designer.Equipment.Count} Ausruestungen");
    }

    private static void ReadParts(string rel, string key, List<DesignScreen.Part> into,
                                  int lo = int.MinValue, int hi = int.MaxValue)
    {
        if (into.Count > 0) return;
        string path = Core.Content.Path(rel);
        if (!FileAccess.FileExists(path)) return;
        using var f = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        if (f == null) return;
        var json = new Json();
        if (json.Parse(f.GetAsText()) != Error.Ok ||
            json.Data.VariantType != Variant.Type.Dictionary) return;
        var root = json.Data.AsGodotDictionary<string, Variant>();
        if (!root.TryGetValue(key, out var tv) || tv.VariantType != Variant.Type.Dictionary) return;
        foreach (var kv in tv.AsGodotDictionary<string, Variant>())
        {
            if (!int.TryParse(kv.Key, out int id) || id < lo || id > hi) continue;
            if (kv.Value.VariantType != Variant.Type.Dictionary) continue;
            var d = kv.Value.AsGodotDictionary<string, Variant>();
            into.Add(new DesignScreen.Part
            {
                Id = id,
                Name = d.TryGetValue("name", out var nv) ? nv.AsString() : id.ToString(),
                Damage = GetI(d, "damage"),
                Range = GetI(d, "range_raw"),
            });
        }
        into.Sort((a, b) => a.Id.CompareTo(b.Id));
    }

    public void ToggleDesigner()
    {
        LoadDesignParts();
        Designer.Toggle();
        UpdatePanel();
        QueueRedraw();
    }

    public void DesignerInput(int move, int change, bool accept)
    {
        if (!Designer.Active) return;
        if (move != 0) Designer.Move(move);
        if (change != 0) Designer.Change(change);
        if (accept) AcceptDesign();
        UpdatePanel();
        QueueRedraw();
    }

    /// <summary>Where the player's own designs live between sessions.
    ///
    /// Beside the campaign progress, and for the same reason: a design the
    /// player drew up is not content derived from the discs, so it does not
    /// belong in <c>user://data</c> with the imported tables. Same shape as
    /// <see cref="Campaign.CampaignManager.SavePath"/> — a Godot ConfigFile.
    /// </summary>
    public const string OwnDesignsPath = "user://designs.cfg";

    /// <summary>Writes out every design the player drew up. They are the ones
    /// with no slot: a record read from sec47 has its place in the file, one
    /// made on the design screen has -1.</summary>
    private static void SaveOwnDesigns()
    {
        if (_designs == null) return;
        var c = new ConfigFile();
        int n = 0;
        foreach (var d in _designs)
        {
            if (d.Slot >= 0) continue;
            c.SetValue("designs", $"d{n}",
                new Godot.Collections.Array { d.Name, d.Propulsion, d.Equip, d.Weapon });
            n++;
        }
        c.SetValue("designs", "count", n);
        c.Save(OwnDesignsPath);
    }

    /// <summary>Reads them back and appends them to the roster. Only the three
    /// components are stored — everything else about a design follows from them
    /// through the game's own arithmetic, so there is nothing else to keep.</summary>
    private static void LoadOwnDesigns()
    {
        if (_designs == null) return;
        var c = new ConfigFile();
        if (c.Load(OwnDesignsPath) != Error.Ok) return;
        int n = (int)c.GetValue("designs", "count", 0);
        var seen = new HashSet<string>();
        foreach (var d in _designs) seen.Add(d.Name);
        int back = 0;
        for (int i = 0; i < n; i++)
        {
            var v = c.GetValue("designs", $"d{i}", new Godot.Collections.Array());
            if (v.VariantType != Variant.Type.Array) continue;
            var a = v.AsGodotArray();
            if (a.Count < 4) continue;
            string nm = a[0].AsString();
            if (nm.Length == 0 || !seen.Add(nm)) continue;
            _designs.Add(new Design(nm, a[1].AsInt32(), a[2].AsInt32(), a[3].AsInt32(), true, -1));
            back++;
        }
        if (back > 0) GD.Print($"designs: {back} eigene Entwuerfe aus {OwnDesignsPath}");
    }

    /// <summary>Take the current combination into the design list, where the
    /// factories find it like any other. It gets no slot number: it is not a
    /// record read out of sec47 but one the player drew up, and marking that
    /// with -1 keeps the two apart in the schedule's eyes.</summary>
    private void AcceptDesign()
    {
        var p = Designer.CurrentPropulsion;
        var w = Designer.CurrentWeapon;
        if (p == null || w == null || _designs == null) { Designer.Say("nichts zu bauen"); return; }
        string name = Designer.ProposedName();
        foreach (var d in _designs)
            if (d.Name == name) { Designer.Say("gibt es schon"); return; }
        int equip = Designer.CurrentEquipment?.Id ?? 0;
        var made = new Design(name, p.Id, equip, w.Id, true, -1);
        _designs.Add(made);
        SaveOwnDesigns();
        Designer.Say($"uebernommen — {made.CostW}/{made.CostF}/{made.CostS} Teile, " +
                     $"{made.Hp} HP, Angriff {made.Attack}");
        GD.Print($"designer: '{name}' angelegt — Fahrwerk {p.Id}, Waffe {w.Id}, Ausruestung {equip}; " +
                 $"kostet {made.CostW}/{made.CostF}/{made.CostS} Teile, {made.Hp} HP, " +
                 $"Angriff {made.Attack}, Tank {made.Fuel}, Munition {made.Ammo}");
    }

    /// <summary>
    /// Turret component for a design's weapon field. The design list indexes
    /// weapons in its own space; the ROBO.CWR component is **weapon + 20**,
    /// confirmed by name against the stats table: design 4 "Chaingun Tank" ->
    /// comp 24 "2x Maschinengewehr", 6 "Rocket Turret" -> 26 "L.Raketenwerfer",
    /// 7 -> 27 "Schw.Raketenwerfer", 8 "LR-Missile" -> 28 "Mittelstreckenrakete",
    /// 18 "AA Turret" -> 38 "Flak-Geschütz", 1 "Cannon Jeep" -> 21 "Bordkanone".
    /// Weapons 65..79 are equipment (Repair, Transporter, Building Const. …) and
    /// 185..199 infantry arms — neither maps this way, so they get no turret.
    /// </summary>
    private static int TurretOf(int designWeapon)
        => designWeapon >= 1 && designWeapon <= 19 ? designWeapon + 20 : 0;

    /// <summary>
    /// Der Sprite-Satz und die Waffe eines FUSSSOLDATEN, den nicht die Karte
    /// mitbringt, sondern eine Fabrik oder eine Verstärkung.
    ///
    /// ⚠ CORRECTED 10.08.2026 — hier lief bisher gar nichts. Die Produktion
    /// setzte `Weapon = TurretOf(d.Weapon)`, und `TurretOf` bildet nur 1..19 ab;
    /// Infanteriewaffen sind aber die Zeilen 185..199. Jeder gebaute Soldat
    /// bekam damit `Weapon = 0`, fiel durch `CanFight`, stand nicht in `ArmyOf`
    /// — und wurde weder von der Welle noch von der Streife je angefasst.
    /// Gemessen auf Mission 10: die KI baute fünf Chaingunner, `Armee 2`,
    /// »Infanterie bewegt 0/4«. Gefunden hat es die Arbeit an den KI-Punkten.
    ///
    /// Der Kartenpfad macht es längst richtig (`e.Weapon = InfCompBase +
    /// des.WeaponRow` über `e.Infantry`); hier wird derselbe Satz über die
    /// WAFFENZEILE des Entwurfs gefunden, denn die ist es, die beide Tabellen
    /// gemeinsam haben — sec47 +weapon und `infantry.json` `weapon_row`.
    /// </summary>
    private static bool InfantryFor(int designWeapon, out int set, out int weapon)
    {
        set = -1;
        weapon = 0;
        LoadInfantryDesigns();
        if (_infDesigns == null) return false;
        foreach (var kv in _infDesigns)
            if (kv.Value.WeaponRow == designWeapon)
            {
                // der niedrigste der beiden Sätze — die Karte führt Paare
                // (0,1), (2,3), … und der erste ist der stehende
                if (set < 0 || kv.Key < set) set = kv.Key;
                weapon = InfCompBase + kv.Value.WeaponRow;
            }
        return set >= 0;
    }

    /// <summary>The other way round: from the component a unit carries back to
    /// the stats row, which is what the fire-sound class is stored in. Exactly
    /// the inverse of <see cref="TurretOf"/> and nothing more — a component
    /// outside 21..39 gets no sound rather than a guessed one.</summary>
    private static int WeaponRowOf(int comp)
        => comp >= 21 && comp <= 39 ? comp - 20 : -1;

    /// <summary>Factory building types: Waffen-, Fahrwerk- and Spezial-Fabrik.</summary>
    private static bool IsFactory(Entity e) => e.IsBuilding && (e.BType is 2 or 3 or 4);

    private const float BuildSeconds = 6f;

    /// <summary>
    /// Start / advance production in the selected factories. The design list is
    /// the game's (sec47); the build TIME is ours — no build-time field has been
    /// identified in the data.
    /// </summary>
    // ================= research (Forschung) ===================================
    //
    // What the original does, as far as it could be recovered:
    //   * technologies are rows 65..88 of the component stats table, with German
    //     names; a design's equipment value IS that row number.
    //   * money is real: CWM sec73 holds 8 per-player balances, printed as
    //     "Kontostand : $"; research is paid from it.
    //   * progress is shown as "% fertig" = done*100/total (CWM sec96).
    //   * the persistent RESULT is the design list's `enable` flag.
    // NOT recovered: the price of an individual technology. `ResearchCost` below
    // is therefore ours, and the panel does not present it as an original value.

    private static Dictionary<int, string>? _techs;
    // static so the (static) build-menu filter can consult it — there is only
    // ever one entity layer
    private static readonly HashSet<int> _researchedStatic = new();
    private readonly int[] _money = new int[8];
    private const int ResearchCost = 2000;      // ours — no per-tech price found
    private const int ResearchTotal = 5000;     // the "total" seen in CWM sec96
    private const int ResearchRate = 60;        // ours: progress per economy tick

    private static void LoadTechs()
    {
        if (_techs != null) return;
        _techs = new Dictionary<int, string>();
        string path = Core.Content.Path("Maps/research.json");
        if (!FileAccess.FileExists(path)) return;
        using var f = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        if (f == null) return;
        var json = new Json();
        if (json.Parse(f.GetAsText()) != Error.Ok || json.Data.VariantType != Variant.Type.Dictionary)
            return;
        var root = json.Data.AsGodotDictionary<string, Variant>();
        if (!root.TryGetValue("technologies", out var tv) || tv.VariantType != Variant.Type.Dictionary)
            return;
        foreach (var kv in tv.AsGodotDictionary<string, Variant>())
        {
            if (!int.TryParse(kv.Key, out int ut) || kv.Value.VariantType != Variant.Type.Dictionary)
                continue;
            var t = kv.Value.AsGodotDictionary<string, Variant>();
            _techs[ut] = t.TryGetValue("name", out var nv) ? nv.AsString() : $"Tech {ut}";
        }
    }

    /// <summary>Technologies already available at map start — the design list's
    /// own `enable` flag is the game's persistent research state.</summary>
    private void SeedResearch()
    {
        _researchedStatic.Clear();
        if (_designs == null) return;
        foreach (var d in _designs)
            if (d.Available && d.Weapon >= 65 && d.Weapon <= 88) _researchedStatic.Add(d.Weapon);
    }

    /// <summary>Next technology this player has not got yet, or -1.</summary>
    private int NextTech()
    {
        if (_techs == null) return -1;
        foreach (var kv in _techs)
            if (!_researchedStatic.Contains(kv.Key)) return kv.Key;
        return -1;
    }

    public string MoneyLine() => $"Kontostand : $ {_money[0]}";

    /// <summary>Start a research project on the selected Basis (key O).</summary>
    public void StartResearch()
    {
        foreach (int i in _sel)
        {
            var e = _entities[i];
            if (!e.IsBuilding || e.BType != 1 || e.Dead) continue;   // Basis only
            if (e.ResearchTech > 0) { _order = "forscht bereits"; continue; }
            int tech = NextTech();
            if (tech < 0) { _order = "alles erforscht"; continue; }
            int owner = Mathf.Clamp(e.Owner, 0, 7);
            if (_money[owner] < ResearchCost)
            {
                _order = "Sie haben nicht genug Geld";   // the game's own wording
                continue;
            }
            _money[owner] -= ResearchCost;
            e.ResearchTech = tech;
            e.ResearchDone = 0;
            e.State = StResearch;              // "Status : forschen"
            _order = $"Forschung: {_techs![tech]}";
        }
        UpdatePanel();
        QueueRedraw();
    }

    private void UpdateResearch(Entity e)
    {
        if (e.ResearchTech <= 0) return;
        e.ResearchDone += ResearchRate;
        if (e.ResearchDone < ResearchTotal) return;
        _researchedStatic.Add(e.ResearchTech);
        _order = $"{_techs?.GetValueOrDefault(e.ResearchTech) ?? "?"} erforscht";
        // @0x4ab41b, in the routine that prints "Nachricht des FORSCHUNGSLABORS:"
        // and "Neue Waffe erfunden"
        Audio.GameSounds.Play(Audio.GameSounds.ResearchDone);
        e.ResearchTech = 0;
        e.ResearchDone = 0;
        if (e.State == StResearch) e.State = StAktiv;
    }

    /// <summary>Send the selected buildings into repair (key K).
    /// Whether the original charges for this is not in the data, so it is free
    /// here; the +1-hp-every-4th-tick pace is the game's own.</summary>
    public void StartRepair()
    {
        foreach (int i in _sel)
        {
            var e = _entities[i];
            if (!e.IsBuilding || e.Dead || e.HpMax <= 0) continue;
            if (e.Hp >= e.HpMax)
            {
                _order = "unbeschaedigt";
                Audio.GameSounds.Play(Audio.GameSounds.Refused);
                continue;
            }
            e.State = e.BType is 2 or 3 or 4 ? FaRepair : StRepair;
            _order = "Status : reparieren";
            // NO sound here on purpose: the routine @0x43e196 plays one after
            // "mining 3", after "enlarging" and after "upgrading", but there is
            // no call between "repair" and "enlarging". Repairing is silent in
            // the original, so it is silent here.
        }
        UpdatePanel();
        QueueRedraw();
    }

    /// <summary>Lagerausbau (key V) or Produktionserweiterung (key C) on the
    /// selected factories.  Both cost what the factory's own record says and
    /// take 100 steps; afterwards the cost is multiplied by 3/2.</summary>
    public void StartUpgrade(bool storage)
    {
        foreach (int i in _sel)
        {
            var e = _entities[i];
            if (!IsFactory(e) || e.Dead) continue;
            if (e.State != StAktiv)
            {
                _order = "Gebaeude beschaeftigt";
                Audio.GameSounds.Play(Audio.GameSounds.Refused);
                continue;
            }
            int owner = Mathf.Clamp(e.Owner, 0, 7);
            int cost = storage ? e.CostStore : e.CostProd;
            if (_money[owner] < cost)
            {
                _order = "Sie haben nicht genug Geld";   // the game's own wording
                Audio.GameSounds.Play(Audio.GameSounds.Refused);
                continue;
            }
            _money[owner] -= cost;
            e.State = storage ? FaExpand : FaProdUp;
            e.UpgradeStep = 0;
            _order = storage ? $"Lagerausbau $ {cost}"
                             : $"Produktionserw. $ {cost}";
            // "enlarging" and "upgrading" — @0x43e794 and @0x43e837
            Audio.GameSounds.Play(storage ? Audio.GameSounds.Enlarging
                                          : Audio.GameSounds.Upgrading);
        }
        UpdatePanel();
        QueueRedraw();
    }

    /// <summary>"% fertig" for whatever job this building is running, or -1.</summary>
    public static int PercentDone(Entity e)
    {
        if (e.ResearchTech > 0) return e.ResearchDone * 100 / ResearchTotal;
        if (e.BType is 2 or 3 or 4 && e.State is FaExpand or FaProdUp)
            return e.UpgradeStep * 100 / UpgradeSteps;
        if (e.HpMax > 0 && e.State == (e.BType is 2 or 3 or 4 ? FaRepair : StRepair))
            return e.Hp * 100 / e.HpMax;
        return -1;
    }

    /// <summary>Step through the selected factory's build menu (key N).</summary>
    public void CycleBuildMenu()
    {
        int n = 0;
        foreach (int i in _sel)
        {
            var e = _entities[i];
            if (e.Dead) continue;
            if (e.IsBuilding && e.BType == 9)          // Flughafen: what to buy
            {
                int m = AirMenu(e).Count;
                if (m == 0) continue;
                e.MenuIndex = (e.MenuIndex + 1) % m;
                n++;
                continue;
            }
            if (IsDock(e))                             // Hafen: what to build
            {
                int m = ShipMenu(e).Count;
                if (m == 0) continue;
                e.MenuIndex = (e.MenuIndex + 1) % m;
                n++;
                continue;
            }
            if (!IsFactory(e)) continue;
            var menu = BuildableBy(e.BType);
            if (menu.Count == 0) continue;
            e.MenuIndex = (e.MenuIndex + 1) % menu.Count;
            n++;
        }
        if (n > 0) { UpdatePanel(); QueueRedraw(); }
    }

    // ---- what the production panel shows (see UI/BuildPanel.cs) -------------

    /// <summary>The selected building, if it is one the view player can build
    /// from. Only the player's own — a list over somebody else's factory would
    /// be an offer that cannot be taken.</summary>
    private Entity? Producer()
    {
        if (_selected < 0 || _selected >= _entities.Count) return null;
        var e = _entities[_selected];
        if (!e.IsBuilding || e.IsProp || e.Dead) return null;
        if (e.Owner != ViewPlayer && !IsSupplyDepot(e)) return null;
        // ⚠ 11.08.2026, zweiter Anlauf: der Nachschub-Posten gehoert auf
        // map_02 NIEMANDEM (Besitzer -1), und er wird auch nicht eingenommen —
        // man faehrt auf ihn drauf (darum raeumt Load() seine Sperre weg). Mit
        // der Besitzerpruefung war das Baupanel dort nie zu sehen, und der
        // Spieler kam an die Helis nicht heran.
        //
        // Der Dialog des Originals prueft an dieser Stelle ohnehin KEINEN
        // Besitzer: beide Zweige @0x44C2CF und @0x44C37C pruefen nur
        // `cmp dword [ecx*4 + 0xA9C600], eax` — den Kontostand.
        if (IsSupplyDepot(e)) return e;
        return IsFactory(e) || IsDock(e) || e.BType == 9 ? e : null;
    }

    /// <summary>The panel's heading: the game's own word for the tab
    /// ("Produktion", 0x501934) and what the building has to spend.</summary>
    public string BuildPanelTitle()
    {
        var e = Producer();
        if (e == null) return "";
        if (IsFactory(e))
            return $"PRODUKTION  W{e.StockW} F{e.StockF} S{e.StockS}";
        if (IsSupplyDepot(e))
            return $"VERSORGUNGSDEPOT  ${_money[ViewPlayer is >= 0 and <= 7 ? ViewPlayer : 0]}";
        int owner = e.Owner is >= 0 and <= 7 ? e.Owner : 0;
        return $"PRODUKTION  ${_money[owner]}";
    }

    public bool BuildPanelWanted => Producer() != null;

    /// <summary>Harness only: the factory <c>--demo-buildpanel</c> is waiting on.
    /// The click happens through the panel the moment a line can be paid for, so
    /// what the run proves is the panel and not a shortcut past it.</summary>
    private int _panelPending = -1;

    private void PollBuildPanelDemo()
    {
        if (_panelPending < 0 || _panelPending != _selected) return;
        var rows = BuildPanelRows();
        int pick = rows.FindIndex(r => r.Affordable);
        if (pick < 0) return;
        var b = _entities[_panelPending];
        _panelPending = -1;
        BuildPanelPick(pick);
        GD.Print($"demo-buildpanel: nach {DebugClock:0.0}s bezahlbar — Zeile {pick + 1} " +
                 $"\"{rows[pick].Name}\" ({rows[pick].Cost}) geklickt: {_order}; " +
                 $"Lager jetzt W{b.StockW} F{b.StockF} S{b.StockS} T{b.StockT}, " +
                 $"Bauzeit {b.BuildTime:0.0}s");
    }

    /// <summary>One row per thing this building can make, in the same order the
    /// N key steps through, so the two ways of choosing cannot disagree.</summary>
    public List<UI.BuildPanel.Row> BuildPanelRows()
    {
        var rows = new List<UI.BuildPanel.Row>();
        var e = Producer();
        if (e == null) return rows;

        if (IsFactory(e) && _designs != null)
        {
            var menu = BuildableBy(e.BType);
            for (int i = 0; i < menu.Count; i++)
            {
                var d = _designs[menu[i]];
                rows.Add(new UI.BuildPanel.Row(
                    d.Name, $"{d.CostW}/{d.CostF}/{d.CostS}",
                    CanAfford(e, d), i == e.MenuIndex % Mathf.Max(1, menu.Count)));
            }
            return rows;
        }
        // ⚠ Typ 14 (der Nachschub-Posten, das »Versorgungsdepot«) steht hier
        // seit dem 11.08. NEBEN dem Flughafen: der Kauf hing vorher allein an
        // Typ 9, und den gibt es auf keiner Kampagnenkarte 1..15. Siehe
        // SupplyDepotType. Am Depot kostet der Heli GELD, am Flughafen Teile.
        if (e.BType == 9 || IsSupplyDepot(e))
        {
            var menu = AirMenu(e);
            bool money = IsSupplyDepot(e);
            int owner = money ? (ViewPlayer is >= 0 and <= 7 ? ViewPlayer : 0)
                              : (e.Owner is >= 0 and <= 7 ? e.Owner : 0);
            for (int i = 0; i < menu.Count; i++)
                rows.Add(new UI.BuildPanel.Row(
                    menu[i].Name,
                    money ? $"${HeliPrice}"
                          : $"{menu[i].CostW}/{menu[i].CostF}/{menu[i].CostS}",
                    money ? _money[owner] >= HeliPrice
                          : menu[i].CostW <= e.StockW && menu[i].CostF <= e.StockF
                            && menu[i].CostS <= e.StockS,
                    i == e.MenuIndex % Mathf.Max(1, menu.Count)));
            return rows;
        }
        if (IsDock(e))
        {
            // a dock spends the linked Schiffswerft's parts, not its own and not
            // money — @0x44b253 / @0x4b2b20, and BuildShip does the same
            var menu = ShipMenu(e);
            int yi = ShipyardOf(e);
            var yard = yi >= 0 ? _entities[yi] : null;
            for (int i = 0; i < menu.Count; i++)
            {
                var d = menu[i];
                bool pay = yard != null && yard.StockW >= d.CostW &&
                           yard.StockF >= d.CostF && yard.StockS >= d.CostS;
                rows.Add(new UI.BuildPanel.Row(
                    d.Name, $"{d.CostW}/{d.CostF}/{d.CostS}", pay,
                    i == e.MenuIndex % Mathf.Max(1, menu.Count)));
            }
        }
        return rows;
    }

    /// <summary>A click on row <paramref name="i"/>: make it the pick, then
    /// order it through the same path the B key uses, so nothing about paying,
    /// queueing or refusing is duplicated here.</summary>
    public void BuildPanelPick(int i)
    {
        var e = Producer();
        if (e == null || i < 0) return;
        e.MenuIndex = i;
        _sel.Clear();
        _sel.Add(_selected);
        ProduceFromSelection();
        UpdatePanel();
        QueueRedraw();
    }

    /// <summary>
    /// <c>--demo-buildpanel</c>: select one of the view player's factories so the
    /// production list has something to show, and order the first affordable
    /// line through the panel's own click path — so what is tested is the
    /// panel, not a shortcut past it.
    /// </summary>
    public Vector2? DebugDemoBuildPanel()
    {
        int idx = -1;
        for (int i = 0; i < _entities.Count; i++)
        {
            var e = _entities[i];
            if (!e.IsBuilding || e.IsProp || e.Dead) continue;
            if (!IsFactory(e) && !IsDock(e) && e.BType != 9) continue;
            if (e.Owner is < 0 or > 7) continue;
            // the viewed player first; anybody's factory rather than none
            if (e.Owner == ViewPlayer) { idx = i; break; }
            if (idx < 0) idx = i;
        }
        if (idx < 0) { GD.Print("demo-buildpanel: keine Fabrik auf dieser Karte"); return null; }

        var b = _entities[idx];
        if (b.Owner != ViewPlayer)
        {
            GD.Print($"demo-buildpanel: keine eigene Fabrik — die Liste zeigt nur eigene, " +
                     $"also wird P{b.Owner}s {BuildingTypeName(b.BType)} uebernommen");
            ViewPlayer = b.Owner;
        }
        _selected = idx;
        _sel.Clear(); _sel.Add(idx); SetPrimary();
        UpdatePanel();

        var rows = BuildPanelRows();
        GD.Print($"demo-buildpanel: {b.Name} ({BuildingTypeName(b.BType)}) P{b.Owner}, " +
                 $"Lager W{b.StockW} F{b.StockF} S{b.StockS} T{b.StockT}; " +
                 $"{rows.Count} Zeilen: " +
                 string.Join(", ", rows.ConvertAll(r =>
                     $"{r.Name} {r.Cost}{(r.Affordable ? "" : " (zu teuer)")}")));

        int pick = rows.FindIndex(r => r.Affordable);
        if (pick < 0)
        {
            // a factory starts with Terranium and NO parts — that is the
            // original's own fill (Simulation/Resources.cs), so nothing is
            // affordable at t=0 and the click has to wait for the first parts
            _panelPending = idx;
            GD.Print("demo-buildpanel: noch nichts bezahlbar — die Fabrik hat " +
                     "Terranium, aber keine Teile; es wird gewartet");
            return b.Pos;
        }
        BuildPanelPick(pick);
        GD.Print($"demo-buildpanel: Zeile {pick + 1} \"{rows[pick].Name}\" geklickt — " +
                 $"{_order}; Lager jetzt W{b.StockW} F{b.StockF} S{b.StockS}, " +
                 $"Bauzeit {b.BuildTime:0.0}s");
        return b.Pos;
    }

    /// <summary>Name of what the selected factory would build next.</summary>
    private string MenuPick(Entity e)
    {
        var menu = BuildableBy(e.BType);
        if (_designs == null || menu.Count == 0) return "-";
        return $"{_designs[menu[e.MenuIndex % menu.Count]].Name} ({e.MenuIndex % menu.Count + 1}/{menu.Count})";
    }

    // ---- buying aircraft at the Flughafen -----------------------------------
    //
    // The game sells the two supply helicopters outright: the button tooltips
    // read "Sprithelikopter kaufen" / "Munitionshelikopter kaufen" (@0x4f19f0,
    // @0x4f1a3b), the panel prints "Kaufen" and "Kostet : $" (@0x5021c0,
    // @0x5021c8), and the price it prints comes from the globals 0x52fac0 and
    // 0x52fac4 — **150** for both. What the airfield may build at all is the
    // per-player list in sec120 with its enable flag.
    public const int HeliPrice = 150;

    /// <summary>One buildable aircraft out of sec120 — the aircraft template
    /// with the leading enable flag, one block of 20 per player.</summary>
    private sealed class AirDesign
    {
        public int Player, Speed, Hp, Payload, Airframe, Attack, Defence, Sight, Ammo, Fuel;
        /// <summary>Preis in Waffen-/Fahrwerk-/Spezialteilen (sec120
        /// +0x1F/+0x20/+0x21) — was `build_in_airport` @0x4BB3D0 prueft.</summary>
        public int CostW, CostF, CostS;
        public bool Enable;
        public string Name = "";

        public AirDesign Clone() => (AirDesign)MemberwiseClone();

        /// <summary>sec19 kind. The payload component identifies the type, and
        /// these five are the ones that occur on the maps.</summary>
        public int Kind => Payload switch
        {
            101 => 1, 105 => 2, 100 => 10, 106 => 13, 107 => 14, _ => 0,
        };
    }

    private List<AirDesign>? _airDesigns;
    private string _airSource = "";

    /// <summary>A campaign level carries no sec120 either, so its airfields had
    /// nothing to offer at all. The schedule says what the campaign has
    /// unlocked by mission N, and the exe's own template table says what the
    /// eight types are — together they make the list the map does not bring.
    ///
    /// The schedule is the campaign player's progress; applying it to every
    /// player is OUR setting. In the original the enables sit per player block
    /// in sec120, and a campaign level simply has no such block to read.</summary>
    private void FillCampaignAirDesigns()
    {
        int mission = UI.SkirmishSetup.CampaignMission;
        if (mission <= 0 || (_airDesigns != null && _airDesigns.Count > 0)) return;
        var u = Campaign.CampaignManager.UnlocksFor(mission);
        if (!u.Known) return;

        var types = LoadAircraftTemplates();
        if (types.Count == 0) return;
        _airDesigns = new List<AirDesign>();
        for (int p = 0; p < 8; p++)
            for (int i = 0; i < types.Count; i++)
            {
                var t = types[i];
                t.Player = p;
                t.Enable = u.Aircraft.Contains(i);
                _airDesigns.Add(t.Clone());
            }
        _airSource = $"Fahrplan M{mission}";
        int on = 0;
        foreach (int i in u.Aircraft) if (i < types.Count) on++;
        // ⚠ Die Preise mitmelden: ein Entwurf ohne sie faellt in der KI still auf
        // die alte Tabelle zurueck, und dann sieht ein Lauf genauso aus wie
        // einer, der sie gelesen hat.
        int priced = 0;
        foreach (var t in types) if (t.CostW + t.CostF + t.CostS > 0) priced++;
        GD.Print($"aircraft: {types.Count} Vorlagen aus {_airSource}, {on} freigegeben, " +
                 $"{priced} mit gelesenem Preis" +
                 (types.Count > 0
                     ? $" (erster: {types[0].CostW}/{types[0].CostF}/{types[0].CostS})"
                     : ""));
    }

    /// <summary>The eight aircraft templates out of the exe, as designs.</summary>
    private static List<AirDesign> LoadAircraftTemplates()
    {
        var list = new List<AirDesign>();
        string path = Core.Content.Path("Maps/aircraft.json");
        if (!FileAccess.FileExists(path)) return list;
        using var f = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        if (f == null) return list;
        var json = new Json();
        if (json.Parse(f.GetAsText()) != Error.Ok ||
            json.Data.VariantType != Variant.Type.Dictionary) return list;
        var root = json.Data.AsGodotDictionary<string, Variant>();
        if (!root.TryGetValue("types", out var tv) || tv.VariantType != Variant.Type.Array)
            return list;
        foreach (var item in tv.AsGodotArray())
        {
            if (item.VariantType != Variant.Type.Dictionary) continue;
            var t = item.AsGodotDictionary<string, Variant>();
            list.Add(new AirDesign
            {
                Name = t.TryGetValue("name", out var nv) ? nv.AsString() : "",
                Speed = GetI(t, "speed"), Hp = GetI(t, "hp"),
                Payload = GetI(t, "payload"), Airframe = GetI(t, "airframe"),
                Attack = GetI(t, "attack"), Defence = GetI(t, "defence"),
                Sight = GetI(t, "sight"), Ammo = GetI(t, "ammo"),
                Fuel = GetI(t, "fuel"),
                CostW = GetI(t, "cost_w"), CostF = GetI(t, "cost_f"),
                CostS = GetI(t, "cost_s"),
            });
        }
        return list;
    }

    /// <summary>Der Nachschub-Posten (Typ 14) — das »Versorgungsdepot«.
    ///
    /// <para>⚠ <b>Warum das hier steht, und was daran UNSERE Setzung ist</b>
    /// (11.08.2026). Der Kauf der Versorgungshelis hing bei uns am Flughafen
    /// (<c>BType == 9</c>) — und <b>keine Kampagnenkarte 1..15 trägt ein
    /// Gebäude vom Typ 9</b> (über alle 15 gezählt). In Kampagne 2 war der Kauf
    /// damit unerreichbar, und die Mission ohne ihn nicht durchspielbar: man
    /// startet mit wenig Sprit und Munition, und nur ein Fahrzeug schafft es
    /// bis zum Depot.</para>
    ///
    /// <para><b>Aus der EXE gelesen ist der KAUFWEG:</b> der Zwei-Tasten-Dialog
    /// @0x44C2B9 verzweigt auf Taste 1 → @0x44C2CF und Taste 2 → @0x44C37C, und
    /// <b>beide Zweige</b> prüfen <c>cmp dword [ecx*4 + 0xA9C600], eax</c> mit
    /// <c>jge</c> — den Kontostand gegen einen Preis. Die beiden Tasten tragen
    /// die Beschriftungen »Sprithelikopter kaufen« (0x4F19F0) und
    /// »Munitionshelikopter kaufen« (0x4F1A3B). Es wird also mit <b>Geld</b>
    /// gekauft, anders als am Flughafen, wo Flugzeuge Teile kosten.</para>
    ///
    /// <para>⚠ <b>NICHT gelesen ist, welcher Gebäudetyp dieses Fenster
    /// öffnet.</b> Drei Anläufe sind daran gescheitert: die Zuordnung steht in
    /// keiner Typ→Fensterart-Tabelle, und der Fensteröffner lädt die Feldbasis
    /// 0x8B9038 nicht über eine feste Adresse (alle 19 solchen Stellen sind
    /// Sucher oder Schließer). Dass es der <b>Typ 14</b> ist, ruht auf der
    /// Schilderung des Spielers und auf der Datenlage — map_02 trägt eine 14,
    /// und in 1..15 gibt es nirgends eine 9. <b>Das ist unsere Setzung, und sie
    /// steht hier, damit sie beim nächsten Fund sofort zu finden ist.</b></para>
    /// </summary>
    public const int SupplyDepotType = 14;

    /// <summary>Kauft dieses Gebäude Helikopter gegen Geld? Der Flughafen tut
    /// es nicht — dort kosten Flugzeuge Teile.</summary>
    private static bool IsSupplyDepot(Entity e) => e.IsBuilding && e.BType == SupplyDepotType;

    /// <summary>The supply helicopters this airfield's owner may buy.</summary>
    private List<AirDesign> AirMenu(Entity e)
    {
        var list = new List<AirDesign>();
        if (_airDesigns == null) return list;
        // Am Depot gibt es genau die zwei Nachschubhelis, die der Dialog des
        // Originals als seine beiden Tasten führt — Sprit (Art 13) und
        // Munition (Art 14). Das Freigabe-Byte gilt hier wie überall.
        if (IsSupplyDepot(e))
        {
            // ⚠ HIER WIRD NICHT auf das Freigabe-Byte gefiltert, und das ist
            // kein Versehen: der Zwei-Tasten-Dialog ist KEINE Entwurfsliste.
            // Er hat zwei feste Tasten (»Sprithelikopter kaufen« /
            // »Munitionshelikopter kaufen«) und prüft in beiden Zweigen nur
            // den Kontostand — kein Enable, keine Auswahl. Der Flughafen
            // dagegen filtert seine LISTE über das Byte, und genau daran wäre
            // das Depot sonst gescheitert: auf Kampagne 2 ist für Spieler 0
            // kein einziger Flugzeugentwurf freigegeben (»0 freigegeben«), und
            // das Menü blieb leer.
            // Die Vorlagen sind je Spieler abgelegt; am herrenlosen Posten
            // kauft der Spieler, der davorsteht.
            foreach (var d in _airDesigns)
                if (d.Player == ViewPlayer && d.Kind is 13 or 14)
                    list.Add(d);
            return list;
        }
        // ⚠ CORRECTED 10.08.2026 — hier stand zusätzlich `&& d.Kind is 13 or 14`,
        // also nur die beiden Nachschubhelis. Das Original filtert die Liste am
        // Flughafen NUR über das Freigabe-Byte, über alle 20 Entwürfe des
        // Spielers: der Zeichner @0x46670A überspringt eine Zeile genau dann,
        // wenn `Entwurf +0x00 == 0`, und die markierte Zeile schreibt ihren
        // Index nach `fenster +0x1C` — das Feld, das die Kauftaste liest.
        // Damit ist auch die Sperre erklärt: ein gesperrter Entwurf lässt sich
        // gar nicht erst markieren, während `build_in_airport` der KI das
        // Freigabe-Byte überhaupt nicht prüft.
        foreach (var d in _airDesigns)
            if (d.Player == e.Owner && d.Enable)
                list.Add(d);
        return list;
    }

    private string AirMenuPick(Entity e)
    {
        var m = AirMenu(e);
        if (m.Count == 0) return "-";
        var d = m[e.MenuIndex % m.Count];
        return $"{d.Name} ${HeliPrice} ({e.MenuIndex % m.Count + 1}/{m.Count})";
    }

    /// <summary>Buy the picked helicopter: it is paid for from the owner's
    /// Kontostand (sec73) and parked in the airfield's hangar.</summary>
    /// <summary>Setzt einen Versorgungsheli neben sein Depot und schickt ihn
    /// sofort los. Dieselben Felder wie der Flughafenweg unten — ein Heli, der
    /// hier anders entsteht als dort, wäre ein zweiter Satz Wahrheiten.
    ///
    /// <para>Art 13 und 14 parken nie: <c>spawn_aircraft</c> setzt ihnen
    /// <c>+0x31 = 0xFF</c> und sendet sie unmittelbar aus.</para></summary>
    private void SpawnSupplyHeli(Entity depot, AirDesign d, int owner)
    {
        int slot = 0;
        foreach (var s in _special) slot = Mathf.Max(slot, s.Slot + 1);
        var a = new Special
        {
            Slot = slot, Kind = d.Kind, Name = d.Name, TypeName = d.Name,
            Col = depot.Col, Row = depot.Row, Stored = false, Owner = depot.Owner,
            HomeSlot = depot.Slot, Pos = depot.Pos, Footprint = depot.Footprint,
            Speed = d.Speed, Hp = d.Hp, HpMax = d.Hp,
            Ammo = d.Ammo, AmmoMax = d.Ammo, Fuel = d.Fuel, FuelMax = d.Fuel,
            Payload = d.Payload, Airframe = d.Airframe,
            Attack = d.Attack, Defence = d.Defence, Sight = d.Sight,
            Cargo = SupplyCargoFull,
        };
        a.Owner = owner;
        _special.Add(a);
        GD.Print($"Versorgungsdepot {depot.Slot}: {d.Name} (Art {d.Kind}) gekauft, " +
                 $"fliegt sofort los");
    }

    private bool BuyAircraft(Entity e)
    {
        var menu = AirMenu(e);
        if (menu.Count == 0) { _order = "nichts kaufbar"; return false; }

        // ⚠ CORRECTED 10.08.2026 — die Taste »Produzieren« am Flughafen
        // (@0x449D14, Fensterart 5, Taste 13) prüft **kein Geld**: im ganzen
        // Pfad steht kein einziger Zugriff auf den Kontostand 0xA9C600. Sie
        // prüft ZWEI Dinge, und zwar in DIESER Reihenfolge — erst den Hangar
        // (sec27 `+0x04 belegt == +0x03 Plätze` → »Leider kein Platz im Hangar
        // vorhanden!«), dann die drei Teilelager DES FLUGHAFENGEBÄUDES
        // (`+0x3C/+0x3E/+0x40` gegen Entwurf `+0x1F/+0x20/+0x21` →
        // »Sie besitzen nicht genügend Einzelteile!«) — und setzt dann Befehl
        // 501, der in `spawn_aircraft` @0x4B1380 mündet: buchstäblich dieselbe
        // Routine, die auch die KI benutzt. Kein Bauzähler, keine Warteschlange.
        //
        // ⚠ Der $150-Preis ist NICHT falsch, er gehört nur woanders hin: in den
        // eigenen Zwei-Tasten-Dialog »Sprithelikopter kaufen« (Fensterart 31,
        // @0x44C2B9), der den Kontostand prüft. Am Flughafen kostet auch der
        // Sprit-Heli 0/30/40 Teile. Beides zu verlangen hiesse doppelt zahlen.
        var d = menu[e.MenuIndex % menu.Count];

        // ---- der Nachschub-Posten kauft mit GELD --------------------------
        // Der Zwei-Tasten-Dialog @0x44C2B9 prüft in BEIDEN Zweigen
        // `cmp dword [ecx*4 + 0xA9C600], eax` mit `jge` — den Kontostand gegen
        // einen Preis, und sonst nichts: kein Hangar, keine Teile. Der Heli
        // fliegt sofort los, wie am Flughafen auch (Art 13/14 parken nie).
        // Siehe SupplyDepotType für das, was hier UNSERE Setzung ist.
        if (IsSupplyDepot(e))
        {
            int who = ViewPlayer is >= 0 and <= 7 ? ViewPlayer : 0;
            if (_money[who] < HeliPrice)
            {
                _order = $"Sie besitzen nicht genuegend Geld! ({d.Name} kostet " +
                         $"${HeliPrice}, Kontostand ${_money[who]})";
                return false;
            }
            _money[who] -= HeliPrice;
            SpawnSupplyHeli(e, d, who);
            _order = $"{d.Name} gekauft fuer ${HeliPrice} — Kontostand ${_money[who]}";
            return true;
        }

        if (e.Hangar != null && e.Hangar.Count >= Mathf.Max(1, e.HangarSize))
        { _order = "Leider kein Platz im Hangar vorhanden!"; return false; }
        if (d.CostW > e.StockW || d.CostF > e.StockF || d.CostS > e.StockS)
        {
            _order = $"Sie besitzen nicht genuegend Einzelteile! ({d.Name} kostet " +
                     $"{d.CostW}/{d.CostF}/{d.CostS}, Flughafen hat " +
                     $"{e.StockW}/{e.StockF}/{e.StockS})";
            return false;
        }
        e.StockW -= d.CostW; e.StockF -= d.CostF; e.StockS -= d.CostS;
        int slot = 0;
        foreach (var s in _special) slot = Mathf.Max(slot, s.Slot + 1);
        var a = new Special
        {
            Slot = slot, Kind = d.Kind, Name = d.Name, TypeName = d.Name,
            Col = e.Col, Row = e.Row, Stored = true, Owner = e.Owner,
            HomeSlot = e.Slot, Pos = e.Pos, Footprint = e.Footprint,
            Speed = d.Speed, Hp = d.Hp, HpMax = d.Hp,
            Ammo = d.Ammo, AmmoMax = d.Ammo, Fuel = d.Fuel, FuelMax = d.Fuel,
            Payload = d.Payload, Airframe = d.Airframe,
            Attack = d.Attack, Defence = d.Defence, Sight = d.Sight,
            Cargo = SupplyCargoFull,
        };
        _special.Add(a);
        // ⚠ Entwurf 5 und 6 (Kind 13/14, Sprit- und Munitionsheli) PARKEN NICHT:
        // `spawn_aircraft` setzt ihnen `+0x31 = 0xFF`, sendet sie sofort aus und
        // gibt den Hangarplatz gleich wieder frei. Alle anderen bleiben stehen.
        if (d.Kind is 13 or 14)
        {
            a.Stored = false;
            a.Col = e.Col + 5;
            a.Row = e.Row + 2;
        }
        else (e.Hangar ??= new List<int>()).Add(slot);
        _order = $"{d.Name} fertig ({d.CostW}/{d.CostF}/{d.CostS} Teile)";
        return true;
    }

    // ================= the Schiffswerft ======================================
    //
    // Ships ARE buildable — the earlier note "no producer exists" came from an
    // xref tool that misses two thirds of the references.  The chain, all out
    // of GAME.EXE:
    //
    //   * a HAFEN (building typ 11) owns the panel @0x471800 that prints
    //     "Werft <name>" and lists the ten SHIP_PROD designs of the player,
    //     showing only those whose enable byte is set;
    //   * sec29 (50 x 4, dest 0x87a1f8) pairs that dock with a SCHIFFSWERFT
    //     (typ 16): +0x00 u16 backlink to the dock, +0x02 u8 the yard.  The
    //     yard holds the parts — in all ten maps that have a pair, the dock's
    //     three stores are 0 and the yard's are not;
    //   * the produce button @0x44b253 checks the design's three costs
    //     (+0x19/+0x1a/+0x1b) against that yard's Waffen- / Fahrwerk- /
    //     Spezial-Teile and otherwise prints "Sie besitzen nicht genuegend
    //     Einzelteile.";
    //   * command 0x1fc lands in @0x4b2b20, which takes a free slot in the
    //     player's 1000-entity block, deducts the parts, bumps sec128
    //     ("Gebaute Einheiten") and fills the entity from the design.  The
    //     ship appears at the DOCK, on its column and one row below it.
    //
    // OURS: the build takes ShipSeconds (no build-time field exists in the
    // data, exactly as for the land factories) and the ship is placed on the
    // nearest free naval cell if the dock's own row+1 is occupied.
    private sealed class ShipDesign
    {
        public int Player, Index, Chassis, Weapon, WeaponComp;
        public int CostW, CostF, CostS;
        public int Speed, Energie, Attack, Defence, Sight, Ammo, Fuel, Reload;
        public int Tech;                 // campaign level this design needs
        public bool Enable;
        public string Name = "";
    }

    /// <summary>Campaign technology level — the byte at 0x540eb8.
    ///
    /// It is NOT in any map file: a fresh game sets it to 1 (@0x4426f4),
    /// command 979 carries it in as a game-setup parameter along with three
    /// other options, and the campaign screen 61 raises it to 6 (@0x48849f).
    /// The bulk gate @0x419e90 then enables a ship design while both its
    /// weapon's and its chassis's stats +0x24 are &lt;= this level.
    ///
    /// That gate is not the whole story — the campaign script also unlocks
    /// single designs one by one (@0x4d0560, some thirty call sites in the
    /// state machine), which is why the saved missions show patterns no single
    /// threshold produces: on Chanel Tunnel the AA Ship and the Sea Cruiser
    /// both need level 6, yet one is enabled and the other is not.  A map that
    /// brings its own list therefore wins; only maps without one fall back to
    /// this gate, at the value the binary itself starts from.</summary>
    private const int CampaignTechLevel = 1;

    private List<ShipDesign>? _shipDesigns;   // this map's own sec119 table
    private string _shipSource = "";          // where the list came from
    private const float ShipSeconds = 8f;     // OURS

    /// <summary>Read ships.json and keep the table that belongs to this map.
    /// The 23 .CWM level files stop at sec39 and carry no table of their own;
    /// they fall back to the exe's campaign default, whose enable bytes are
    /// all zero because the tech level (0x540eb8) lives outside the map
    /// files — such a map reports "keine Schiffsliste" instead of pretending.
    /// </summary>
    private void LoadShipDesigns(string mapName)
    {
        _shipDesigns = null; _shipSource = "";
        string path = Core.Content.Path("Maps/ships.json");
        if (!FileAccess.FileExists(path)) return;
        using var f = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        if (f == null) return;
        var json = new Json();
        if (json.Parse(f.GetAsText()) != Error.Ok ||
            json.Data.VariantType != Variant.Type.Dictionary) return;
        var root = json.Data.AsGodotDictionary<string, Variant>();

        // map_DM_4 / map_4 -> "4", map_NET02 -> "NET02", map_07 -> "07"
        string stem = mapName.StartsWith("map_") ? mapName.Substring(4) : mapName;
        if (stem.StartsWith("DM_")) stem = stem.Substring(3);

        Godot.Collections.Array? arr = null;
        if (root.TryGetValue("missions", out var mv) &&
            mv.VariantType == Variant.Type.Dictionary)
        {
            var missions = mv.AsGodotDictionary<string, Variant>();
            if (missions.TryGetValue(stem, out var lv) && lv.VariantType == Variant.Type.Array)
            { arr = lv.AsGodotArray(); _shipSource = "sec119"; }
        }
        if (arr == null && root.TryGetValue("default", out var dv) &&
            dv.VariantType == Variant.Type.Array)
        { arr = dv.AsGodotArray(); _shipSource = "GAME.EXE"; }
        if (arr == null) return;

        _shipDesigns = new List<ShipDesign>();
        foreach (var item in arr)
        {
            if (item.VariantType != Variant.Type.Dictionary) continue;
            var d = item.AsGodotDictionary<string, Variant>();
            _shipDesigns.Add(new ShipDesign
            {
                Player = GetI(d, "player", 0), Index = GetI(d, "index"),
                Enable = GetI(d, "enable") != 0,
                Name = d.TryGetValue("name", out var nv) ? nv.AsString() : "",
                Chassis = GetI(d, "chassis"), Weapon = GetI(d, "weapon"),
                WeaponComp = GetI(d, "weapon_comp"),
                CostW = GetI(d, "cost_w"), CostF = GetI(d, "cost_ch"),
                CostS = GetI(d, "cost_sp"),
                Speed = GetI(d, "speed"), Energie = GetI(d, "energie"),
                Attack = GetI(d, "attack"), Defence = GetI(d, "defence"),
                Sight = GetI(d, "sight"), Ammo = GetI(d, "ammo"),
                Fuel = GetI(d, "fuel"), Reload = GetI(d, "reload"),
                Tech = GetI(d, "tech"),
            });
        }
        // A map without its own sec119 runs on the exe's default table, whose
        // enable bytes are all zero because the campaign never got that far.
        if (_shipSource == "GAME.EXE")
        {
            // In a campaign mission the schedule says it exactly: the state
            // machine unlocks designs as the campaign runs, and state N is
            // mission N (the map loader indexes the mission-name table with
            // the campaign counter, @0x41e25e). Everything states 1..N have
            // unlocked is buildable.
            var u = UI.SkirmishSetup.CampaignMission > 0
                ? Campaign.CampaignManager.UnlocksFor(UI.SkirmishSetup.CampaignMission) : null;
            if (u is { Known: true })
            {
                foreach (var d in _shipDesigns) d.Enable = u.Ships.Contains(d.Index);
                _shipSource = $"Fahrplan M{UI.SkirmishSetup.CampaignMission}";
            }
            else
            {
                // Outside the campaign there is no state to ask, so the bulk
                // gate is applied at the level a fresh game starts from — those
                // maps get the two starter ships instead of an empty yard.
                foreach (var d in _shipDesigns)
                    d.Enable = d.Tech <= CampaignTechLevel;
            }
        }
        int on = 0;
        foreach (var d in _shipDesigns) if (d.Enable) on++;
        GD.Print($"ships: {_shipDesigns.Count} Entwuerfe aus {_shipSource}, {on} freigegeben");
    }

    /// <summary>A Hafen (typ 11) is where ships are ordered.</summary>
    private static bool IsDock(Entity e) => e.IsBuilding && e.BType == 11;

    /// <summary>The Schiffswerft sec29 pairs this dock with — it holds the parts.</summary>
    private int ShipyardOf(Entity dock)
    {
        if (dock.Shipyard < 0) return -1;
        for (int i = 0; i < _entities.Count; i++)
            if (_entities[i].IsBuilding && _entities[i].Slot == dock.Shipyard) return i;
        return -1;
    }

    /// <summary>The ship types this dock's owner may order.</summary>
    private List<ShipDesign> ShipMenu(Entity dock)
    {
        var list = new List<ShipDesign>();
        if (_shipDesigns == null) return list;
        int owner = Mathf.Clamp(dock.Owner, 0, 7);
        // the exe's default table is player 0's block only — the init routine
        // @0x4b2330 copies it into the other seven, so it holds for everyone
        bool anyPlayer = _shipSource == "GAME.EXE";
        foreach (var d in _shipDesigns)
            if ((anyPlayer || d.Player == owner) && d.Enable) list.Add(d);
        return list;
    }

    /// <summary>What B would order here. The display box fits about twenty
    /// characters, so the panel gets name + cost and the order line the rest.</summary>
    private string ShipMenuPick(Entity dock, bool full = false)
    {
        var m = ShipMenu(dock);
        if (m.Count == 0) return "-";
        var d = m[dock.MenuIndex % m.Count];
        string s = $"{d.Name} {d.CostW}/{d.CostF}/{d.CostS}";
        return full ? $"{s} ({dock.MenuIndex % m.Count + 1}/{m.Count})" : s;
    }

    /// <summary>Order the picked ship: the parts come out of the linked
    /// Schiffswerft, exactly as @0x44b253 and @0x4b2b20 do it.</summary>
    private bool BuildShip(Entity dock)
    {
        var menu = ShipMenu(dock);
        if (menu.Count == 0)
        {
            _order = _shipDesigns == null ? "keine Schiffsliste in dieser Karte"
                                          : "nichts baubar";
            return false;
        }
        if (dock.BuildTime > 0f) { _order = "Werft baut schon"; return false; }
        int yi = ShipyardOf(dock);
        if (yi < 0) { _order = "keine Schiffswerft"; return false; }
        var yard = _entities[yi];
        var d = menu[dock.MenuIndex % menu.Count];
        if (yard.StockW < d.CostW || yard.StockF < d.CostF || yard.StockS < d.CostS)
        { _order = "Sie besitzen nicht genuegend Einzelteile"; return false; }

        yard.StockW -= d.CostW; yard.StockF -= d.CostF; yard.StockS -= d.CostS;
        dock.BuildIndex = d.Index;
        dock.BuildTime = ShipSeconds;
        _order = $"{d.Name} in Bau ({d.CostW}/{d.CostF}/{d.CostS} Teile)";
        return true;
    }

    /// <summary>Launch the finished ship at the dock, one row below it — the
    /// production handler writes col = dock.col and row = dock.row + 1.</summary>
    private void LaunchShip(Entity dock)
    {
        if (_nav == null || _shipDesigns == null) return;
        var menu = ShipMenu(dock);
        if (menu.Count == 0) return;
        ShipDesign? d = null;
        foreach (var x in menu) if (x.Index == dock.BuildIndex) d = x;
        d ??= menu[0];

        var want = new Vector2I(dock.Col, dock.Row + 1);
        var cell = _nav.NearestFree(want, Simulation.NavGrid.MoveClass.Ship);
        if (cell == null) { dock.BuildTime = 1f; return; }   // no water free yet
        int el = ElevOf(cell.Value.X, cell.Value.Y);
        var u = new Entity
        {
            Slot = -1, Col = cell.Value.X, Row = cell.Value.Y,
            Owner = dock.Owner, Team = dock.Team, UnitType = d.Chassis,
            Category = -1, Elev = el, Name = d.Name,
            // energie is the life, the tank and the magazine come straight
            // from the design record (@0x4b2b20 writes +0x08/+0x29, +0x2e/+0x30
            // and +0x39/+0x3a from +0x1d, +0x26 and +0x25)
            Hp = d.Energie, HpMax = d.Energie,
            Fuel = d.Fuel, FuelMax = d.Fuel,
            Ammo = d.Ammo, AmmoMax = d.Ammo,
            Attack = d.Attack, Defence = d.Defence,
            Weapon = d.WeaponComp,
            Facing = DefaultFacing, Mobile = true,
            Move = Simulation.NavGrid.MoveClass.Ship,
            Footprint = CellRect(_ox, _oy, cell.Value.X, cell.Value.Y, el),
        };
        // Ein Fussoldat traegt seine Waffe aus infantry.json, nicht aus
        // TurretOf — siehe InfantryFor. Ohne das kann er nicht kaempfen.
        if (InfantryFor(d.Weapon, out int inf, out int iw)) { u.Infantry = inf; u.Weapon = iw; }
        u.Pos = CellCenter(u.Col, u.Row);
        _entities.Add(u);
        _nav.SetOccupant(u.Col, u.Row, _entities.Count - 1);
        _shipsBuilt++;
        NoteEvent(dock, $"{d.Name} fertig");
        _order = $"{d.Name} vom Stapel gelaufen";
        QueueRedraw();
    }

    /// <summary>What the original counts in sec128, "Gebaute Einheiten".</summary>
    private int _shipsBuilt;
    public int ShipsBuilt => _shipsBuilt;

    public void ProduceFromSelection()
    {
        _order = "";
        int bought = 0;
        foreach (int i in _sel)
            // ⚠ 11.08.2026 — hier stand nur `BType == 9`. Das Baupanel des
            // Nachschub-Postens zeigte seine zwei Helis, und ein Klick darauf
            // lief hier vorbei ins Leere: nichts gebaut, kein Geld abgezogen.
            // Der Pruefstand --depot-check hat es nicht gesehen, weil er
            // BuyAircraft DIREKT aufruft statt ueber den Klickweg.
            if (_entities[i].IsBuilding && !_entities[i].Dead
                && (_entities[i].BType == 9 || IsSupplyDepot(_entities[i]))
                && BuyAircraft(_entities[i])) bought++;
        if (bought > 0) { UpdatePanel(); QueueRedraw(); return; }

        int slipped = 0;
        foreach (int i in _sel)
            if (IsDock(_entities[i]) && !_entities[i].Dead
                && BuildShip(_entities[i])) slipped++;
        if (slipped > 0) { UpdatePanel(); QueueRedraw(); return; }

        if (_designs == null || _designs.Count == 0)
        { if (_order.Length == 0) _order = "keine Designs geladen"; return; }
        int n = 0;
        foreach (int i in _sel)
        {
            var e = _entities[i];
            if (!IsFactory(e) || e.Dead) continue;
            var menu = BuildableBy(e.BType);
            if (menu.Count == 0) { _order = "nichts baubar"; continue; }
            int pick = menu[e.MenuIndex % menu.Count];
            var chosen = _designs[pick];
            if (!CanAfford(e, chosen))
            {
                _order = $"zu wenig Teile: {chosen.Name} kostet " +
                         $"{chosen.CostW}/{chosen.CostF}/{chosen.CostS}";
                continue;
            }
            PayFor(e, chosen);
            e.BuildIndex = pick;
            e.BuildTime = BuildSeconds;
            n++;
        }
        if (n > 0) _order = $"Produktion: {n} Fabrik(en)";
        else if (_order.Length == 0) _order = "keine Fabrik gewaehlt";
        UpdatePanel();
        QueueRedraw();
    }

    // ---- building states ----
    //
    // Every building record carries `cis_typ` at +0x15 — its running number
    // within its own type — and that number indexes a 50-entry instance array
    // that each building type owns as a map section of its own (Basis sec23,
    // the three factories share sec24, Bahnstation and Feldbahnhof share
    // sec30, Mine and Feld-Rohstoffmine share sec28 = the deposits).  Byte
    // +0x02 of that record is the STATE, and the tick handler @0x43dc34 picks
    // the routine from the building type.  The words below are the game's own.
    public const int StAktiv = 0;
    // Basis and friends (sec23 and the small arrays)
    public const int StRepair = 1, StExpand = 2, StResearch = 3;
    // the three factories (sec24) number theirs differently
    public const int FaHalt = 1, FaRepair = 2, FaExpand = 3, FaProdUp = 4;

    /// <summary>The state word the original panel prints for this building.</summary>
    public static string StateName(Entity e)
    {
        // The factory panel labels its two jobs "Lagerausbau" (@0x501b30) and
        // "Produktionserweiterung" (@0x501b40); the short "vergroessern" is
        // what the Basis panel prints for its own state 2.
        if (e.BType is 2 or 3 or 4)          // factories: sec24, five states
            return e.State switch
            {
                0 => "aktiv", 1 => "angehalten", 2 => "reparieren",
                3 => "lagerausbau", 4 => "prod.erw.", _ => "aktiv",
            };
        return e.State switch                 // Basis and the rest: four states
        {
            1 => "reparieren", 2 => "vergroessern", 3 => "forschen", _ => "aktiv",
        };
    }

    // ---- economy: mining and manufacturing ----
    //
    // The chain the data describes: a Mine draws raw Terranium out of its
    // deposit (sec28) into its own store (+0x2e); a factory turns stored
    // Terranium into its own part type (+0x28 Waffen / +0x2a Fahrwerk /
    // +0x2c Spezial); building a unit consumes those parts.
    //
    // MANUFACTURING now runs on the ORIGINAL rule (@0x43dec5..@0x43e027):
    // one Terranium becomes one part every ProdPeriod[speed] ticks, with
    // probability EffNum/EffDen, and only while the output store is below the
    // factory's Lagerplatz.  The period table is verbatim from VA 0x4faca0.
    //
    // What a unit COSTS is no longer ours either: a design record carries its
    // three prices at +0x1a..+0x1c, derived from its components by the routine
    // @0x4b1fb0, and the production button @0x44a6eb checks them one by one
    // against these very three stores.  See Simulation/DesignMath.cs.
    // The mining rate below is still OURS.
    private const float EconTick = 1f;       // seconds per tick — ours
    private const int MineRate = 5;          // Terranium per tick a mine digs
    private const int MineCap = 200;         // matches the deposit's +0x08

    /// <summary>Ticks between two produced parts, by Produktionsgeschwindigkeit.
    /// Verbatim from GAME.EXE VA 0x4faca0 — a clean 2/3 series, so every
    /// Produktionserweiterung makes the factory exactly 1.5x faster.</summary>
    private static readonly int[] ProdPeriod =
        { 256, 170, 114, 76, 50, 33, 22, 15, 10, 8 };

    // The upgrade jobs: 100 steps, one every 5th tick (@0x43e0a0 / @0x43e11f),
    // then Lagerplatz +10 or Produktionsgeschwindigkeit +1, and that job's
    // cost is multiplied by 3/2.  Repair adds 1 hp every 4th tick (@0x43e05c).
    private const int UpgradeSteps = 100, UpgradeTick = 5;
    private const int CapacityGain = 10, RepairTick = 4;

    private readonly RandomNumberGenerator _rng = new();

    /// <summary>A building tops up the ammunition of its owner's units standing
    /// next to it — one round per tick, exactly what the original does
    /// (@0x4127e8, walking the neighbouring cells of the spatial grid). Without
    /// it a unit that fires its magazine dry stays dry until a Munitionheli
    /// happens by.</summary>
    private void RearmNeighbours(Entity b)
    {
        if (!b.IsBuilding || b.Owner is < 0 or > 7) return;
        foreach (var e in _entities)
        {
            if (e.IsBuilding || e.IsProp || e.Dead) continue;
            if (e.Owner != b.Owner || e.AmmoMax <= 0 || e.Ammo >= e.AmmoMax) continue;
            if (Mathf.Abs(e.Col - b.Col) > 1 || Mathf.Abs(e.Row - b.Row) > 1) continue;
            e.Ammo++;
        }
    }

    /// <summary>A Nachschub-Posten services whatever stands ON it.
    ///
    /// Its tick handler @0x43e872 looks the post's own cell up in the spatial
    /// grid, and if a unit sits there it writes `hp = hp_max` (+0x2e = +0x30)
    /// and `ammo = ammo_max` (+0x39 = +0x3a) outright — the ground counterpart
    /// to the two supply helicopters. The original also requires the unit's
    /// +0x04 to read 0xFF, a state byte we do not model; everything else is
    /// exactly what the handler does. The post has no owner (all 63 of them
    /// carry owner 255), so it serves anyone.</summary>
    private void SupplyPostService(Entity post)
    {
        if (post.BType != 14 || post.Dead) return;
        foreach (var e in _entities)
        {
            if (e.IsBuilding || e.IsProp || e.Dead || e.HpMax <= 0) continue;
            if (e.Col != post.Col || e.Row != post.Row) continue;
            if (e.Fuel < e.FuelMax || (e.AmmoMax > 0 && e.Ammo < e.AmmoMax)) SupplyPostRuns++;
            e.Fuel = e.FuelMax;                       // +0x2e = +0x30
            if (e.AmmoMax > 0) e.Ammo = e.AmmoMax;    // +0x39 = +0x3a
        }
    }

    /// <summary>How often a Nachschub-Posten serviced somebody (harness).</summary>
    public int SupplyPostRuns;

    private void UpdateEconomy(int index, Entity e, float dt)
    {
        if (e.Dead) return;
        e.EconTimer -= dt;
        if (e.EconTimer > 0f) return;
        e.EconTimer = EconTick;
        e.Ticks += TickScale;      // Ticks counts ORIGINAL game ticks

        // being taken: the original does this on the building's own tick too,
        // and its duration is counted in the same original ticks (see
        // Simulation/Capture.cs)
        CaptureTick(index, e, TickScale);
        if (e.Dead) return;

        RearmNeighbours(e);
        SupplyPostService(e);

        // mining: deposit -> the mine's own store
        if (e.Deposit > 0 && e.StockT < MineCap)
        {
            int take = Mathf.Min(MineRate, Mathf.Min(e.Deposit, MineCap - e.StockT));
            e.Deposit -= take;
            e.StockT += take;
        }

        // the two state enums differ: a factory's 1 means "angehalten" and its
        // repair sits at 2, while every other building repairs on 1
        bool fact = e.BType is 2 or 3 or 4;
        int st = e.State;
        if (st == StAktiv) Produce(e);
        else if (st == (fact ? FaRepair : StRepair)) Repair(e);
        else if (fact && st == FaExpand)                     // Lagerausbau
        {
            if (Advance(e)) { e.Capacity += CapacityGain; e.CostStore = e.CostStore * 3 / 2; }
        }
        else if (fact && st == FaProdUp)                     // Produktionserweiterung
        {
            if (Advance(e)) { e.ProdSpeed++; e.CostProd = e.CostProd * 3 / 2; }
        }

        UpdateResearch(e);
        Haul(e);
    }

    /// <summary>+1 hp every 4th tick; back to "aktiv" once the building is whole.</summary>
    private static void Repair(Entity e)
    {
        e.Hp = Mathf.Min(e.Hp + TickScale / RepairTick, e.HpMax);
        if (e.Hp >= e.HpMax) e.State = StAktiv;
    }

    /// <summary>One of the 100 upgrade steps; true when the job just finished.</summary>
    private static bool Advance(Entity e)
    {
        e.UpgradeStep += TickScale / UpgradeTick;
        if (e.UpgradeStep < UpgradeSteps) return false;
        e.UpgradeStep = 0;
        e.State = StAktiv;
        return true;
    }

    /// <summary>The original manufacturing step: 1 Terranium -> 1 part.</summary>
    private void Produce(Entity e)
    {
        if (!IsFactory(e)) return;
        int period = ProdPeriod[Mathf.Clamp(e.ProdSpeed, 0, ProdPeriod.Length - 1)];
        e.ProdAccum += TickScale;
        while (e.ProdAccum >= period)
        {
            e.ProdAccum -= period;
            if (e.StockT <= 0) continue;
            int made = e.BType switch { 2 => e.StockW, 3 => e.StockF, _ => e.StockS };
            if (e.Capacity > 0 && made >= e.Capacity) continue;
            if (_rng.RandiRange(0, 99) >= e.EffNum * 100 / Mathf.Max(1, e.EffDen)) continue;
            e.StockT--;
            switch (e.BType)
            {
                case 2: e.StockW++; break;
                case 3: e.StockF++; break;
                default: e.StockS++; break;
            }
        }
    }

    // How many original ticks one of our economy ticks stands for.  The game's
    // periods (76 ticks per part at speed 3, 4 ticks per repair point, 5 ticks
    // per upgrade step) are in its own ticks and nothing in the data says how
    // long one was.  This ONE factor is ours; every ratio it scales is
    // original, so the balance between the three stays the game's.
    private const int TickScale = 16;

    // ---- transport ----
    //
    // The game's own network is now readable.  Each building carries a node
    // number (record +0x16, the debug dump's `rail`), sec33 holds those nodes —
    // `+0x00` points straight back at the building, `+0x02..+0x06` list up to
    // five attached lines — and sec34 holds the SPOJ lines themselves, whose
    // first two bytes name the nodes they join (dump @0x413411).  Checked on
    // 1.DM and 10.DM: 76 of 76 nodes point at the right building.
    //
    // So a delivery now walks the rail graph.  Campaign .CWM levels ship with
    // NO lines (the players lay them during the mission), and there we fall
    // back to the old rule — nearest own building within reach, OURS.
    //
    // ⚠ 10.08.2026: das ECHTE Bahnsystem steht jetzt in
    // Simulation/RailFreight.cs — Züge, die von allein fahren, laden und
    // entladen, mit den Warenschaltern aus der Typmatrix des Programms. Alles
    // unter dieser Zeile ist der ERSATZ für Karten OHNE Linien und tritt
    // zurück, sobald eine Linie die Ware fährt (RailCarriesFrom in Haul).
    private const int HaulAmount = 4;      // goods moved per economy tick
    private const int HaulRange = 40;      // tiles a fallback delivery may cover
    private const int HaulReserve = 30;    // Terranium a mine keeps for itself
    private const int PartReserve = 40;    // parts a factory keeps so it can build
    private const int HaulHops = 3;        // lines a delivery may travel

    /// <summary>building slot -> the slots it is joined to by SPOJ lines.</summary>
    private readonly Dictionary<int, List<int>> _rail = new();

    /// <summary>Every line's own track, in (col,row) with half-tile steps.
    /// From sec34's direction codes plus the end points in sec34 +0x02/+0x04
    /// and sec122 — see GAMESTATE_RE.md 3.805.</summary>
    private readonly List<List<Vector2>> _railRoutes = new();
    private bool _hasRail;

    // ================= the trains ===========================================
    //
    // sec44 holds 240 wagon records of 24 bytes and sec121 one y per record;
    // together they are 60 trains of 4 wagons, wagon-major (wagon w of train t
    // is record w*60 + t).  The tick splits the slot with `div 60` (@0x4c6c06):
    // the remainder is the train AND the SPOJ line it runs on, the quotient is
    // the wagon number.  The cursor +0x0a indexes that line's direction codes
    // and is counted up against its `delka` (@0x4c6c30); the wagon's position
    // is the route point at that cursor, which holds for 286 of the 309
    // wagons that have a live line, each within a single step.
    //
    // The sprite comes from the draw path @0x42b4c0: wagon 0 uses ROBO.CWR
    // part 57, wagons 1 and 2 part 58, and wagon 3 part 58 with the rail piece
    // rotated by +4 mod 8 — it is coupled facing the other way.  The frame
    // inside the part is the RAIL PIECE, not a facing.
    private sealed class Wagon
    {
        public int Line, Index, Step, Piece;
        public float Col, Row;          // Row may be a half tile
        public float Move;              // seconds until the next step — ours
        public int Dir = 1;             // +1 = along the route, -1 = against it

        /// <summary>true = dieser Waggon gehört einer Fahrt des Bahnsystems und
        /// wird von <c>RailPlaceWagons</c> gesetzt, nicht von
        /// <see cref="UpdateTrains"/>. Karten ohne Zugsätze (alle NET-Karten:
        /// 0 Züge, jede Linie faze 0) bekommen so trotzdem einen sichtbaren Zug
        /// — der Automat legt ihn beim Abfahren an.</summary>
        public bool Freight;
    }

    private readonly List<Wagon> _wagons = new();
    private readonly Dictionary<int, List<Vector2>> _lineRoute = new();
    private readonly Dictionary<int, List<int>> _linePiece = new();
    private readonly Dictionary<int, Texture2D?> _trainTex = new();

    /// <summary>Seconds a wagon takes for one route step. OURS: the tick
    /// counts a per-wagon distance down by the record's +0x0c (8 everywhere in
    /// the shipped maps) and steps when it runs out, but nothing in the data
    /// says how long a tick is.</summary>
    private const float TrainStepSeconds = 0.35f;

    private static readonly Dictionary<int, int> WagonPart =
        new() { { 0, 57 }, { 1, 58 }, { 2, 58 }, { 3, 58 } };

    private Texture2D? GetTrainTexture(int part, int piece)
    {
        int key = part * 8 + (piece & 7);
        if (_trainTex.TryGetValue(key, out var t)) return t;
        string p = Core.Content.Path($"Units/train/{part}/f{piece & 7}.png");
        t = ResourceLoader.Exists(p) ? ResourceLoader.Load<Texture2D>(p) : null;
        if (t == null && FileAccess.FileExists(p))
        {
            // imported content carries no Godot import step — read the file
            var img = Image.LoadFromFile(p);
            if (img != null) t = ImageTexture.CreateFromImage(img);
        }
        _trainTex[key] = t;
        return t;
    }

    private void LoadWagons(GDict root)
    {
        _wagons.Clear();
        if (!root.TryGetValue("trains", out var tv) || tv.VariantType != Variant.Type.Array)
            return;
        foreach (var item in tv.AsGodotArray())
        {
            if (item.VariantType != Variant.Type.Dictionary) continue;
            var w = item.AsGodotDictionary<string, Variant>();
            int col = GetI(w, "col"), yh = GetI(w, "y_half");
            if (col == 0 && yh == 0) continue;            // empty wagon slot
            _wagons.Add(new Wagon
            {
                Line = GetI(w, "line", -1), Index = GetI(w, "wagon"),
                Step = GetI(w, "step"), Piece = GetI(w, "piece"),
                Col = col, Row = yh / 2f, Move = TrainStepSeconds,
            });
        }
        // Which way a train faces is in the data, not a choice: the wagons
        // trail the one that leads, so if wagon 0 stands on a HIGHER route
        // step than the last wagon the train runs along the route, and if it
        // stands on a lower one it runs against it. Both occur — Chanel
        // Tunnel has its trains one way round, The Dam the other.
        var lead = new Dictionary<int, int>();          // line -> step of wagon 0
        var tail = new Dictionary<int, int>();          // line -> step of the last wagon
        var tailIdx = new Dictionary<int, int>();
        foreach (var w in _wagons)
        {
            if (w.Index == 0) lead[w.Line] = w.Step;
            if (!tailIdx.TryGetValue(w.Line, out var ti) || w.Index > ti)
            { tailIdx[w.Line] = w.Index; tail[w.Line] = w.Step; }
        }
        foreach (var w in _wagons)
            if (lead.TryGetValue(w.Line, out var a) && tail.TryGetValue(w.Line, out var b) && a != b)
                w.Dir = a > b ? 1 : -1;
    }

    /// <summary>Walk every wagon one route step at a time. When it reaches the
    /// end of its line it turns round — OURS: the original's end handling was
    /// not reconstructed, and holding still would freeze every train within a
    /// minute.</summary>
    private void UpdateTrains(float dt)
    {
        if (_wagons.Count == 0) return;
        bool moved = false;
        var flip = new HashSet<int>();
        foreach (var w in _wagons)
        {
            if (w.Freight) continue;      // fährt am Fahrplan, siehe RailFreight.cs
            if (!_lineRoute.TryGetValue(w.Line, out var route) || route.Count < 2) continue;
            w.Move -= dt;
            if (w.Move > 0f) continue;
            w.Move += TrainStepSeconds;
            int next = w.Step + w.Dir;
            // OURS: what the original does at the end of a line was not
            // reconstructed, so the whole train turns round rather than
            // freezing — a wagon that would run off the end marks the line
            if (next >= route.Count || next < 0) { flip.Add(w.Line); continue; }
            w.Step = next;
            var p = route[w.Step];
            w.Col = p.X; w.Row = p.Y;
            if (_linePiece.TryGetValue(w.Line, out var pcs) && w.Step < pcs.Count)
                w.Piece = pcs[w.Step];
            moved = true;
        }
        foreach (var w in _wagons)
            if (flip.Contains(w.Line)) w.Dir = -w.Dir;
        if (moved) QueueRedraw();
    }

    private void DrawTrains()
    {
        foreach (var w in _wagons)
        {
            int part = WagonPart.TryGetValue(w.Index, out var pp) ? pp : 58;
            int piece = w.Index == 3 ? (w.Piece + 4) & 7 : w.Piece;   // @0x42b52a
            var tex = GetTrainTexture(part, piece);
            var at = RailPoint(new Vector2(w.Col, w.Row));
            if (tex == null)
            {
                DrawCircle(at, 3f, new Color(0.9f, 0.6f, 0.2f));
                continue;
            }
            // the wagon frames come off the same 64x56 canvas as the unit
            // parts, so they use the same anchor
            DrawTexture(tex, at - ComposedAnchor);
        }
    }

    public int WagonCount => _wagons.Count;

    /// <summary>Buildings reachable over at most HaulHops lines, nearest first.</summary>
    private IEnumerable<Entity> RailReach(Entity from)
    {
        if (!_hasRail || !_rail.ContainsKey(from.Slot)) yield break;
        var seen = new HashSet<int> { from.Slot };
        var front = new List<int> { from.Slot };
        for (int hop = 0; hop < HaulHops && front.Count > 0; hop++)
        {
            var next = new List<int>();
            foreach (int s in front)
            {
                if (!_rail.TryGetValue(s, out var nb)) continue;
                foreach (int n in nb)
                {
                    if (!seen.Add(n)) continue;
                    next.Add(n);
                    var e = _entities.Find(x => x.IsBuilding && x.Slot == n);
                    if (e != null) yield return e;
                }
            }
            front = next;
        }
    }

    /// <summary>Pick a delivery target: over the rail network if the map has
    /// one, otherwise the nearest own building.</summary>
    private Entity? Consignee(Entity from, System.Func<Entity, bool> pick)
    {
        foreach (var x in RailReach(from))
            if (!x.Dead && x.Owner == from.Owner && pick(x)) return x;
        // ⚠ ZWEIMAL BERICHTIGT.
        //
        // (1) 10.08. früh stand hier `_hasRail ? null : …`, und das hat im
        //     Gefecht die ganze Wirtschaft stillgelegt: die Teile blieben in der
        //     Fabrik liegen, der Spieler hat es genau so gemeldet.
        //
        // (2) ⚠ Die Begründung von damals war ZU KURZ GEGRIFFEN und wird hiermit
        //     zurückgezogen. Sie lautete: »NET05 hat 35 Linien, davon Fabrik ↔
        //     Basis: 0, also gibt es keinen vorgelegten Weg«. Die Zahl stimmt,
        //     die Frage war falsch. **Das Netz läuft über BAHNSTATIONEN.** Die
        //     Typmatrix @0x504128 sagt es wörtlich: Fabrik → Bahnstation (6) /
        //     Feldbahnhof (12) ihr eigenes Bauteil, und von dort → Basis alle
        //     drei. Auf NET05 sind acht Feldbahnhöfe der Umschlagplatz, und
        //     **32 der 35 Linien tragen Ware**. Es fehlte nicht die eine Linie,
        //     es fehlte das ganze Bahnsystem — gebaut in Simulation/RailFreight.cs.
        //
        // Der Nahweg bleibt trotzdem stehen, denn es gibt Karten OHNE jede Linie
        // (NET01/06/07: 0 Linien; map_05: 9 Knoten, 0 Linien). Dort trägt er
        // allein. Wo die Bahn fährt, tritt er zurück — das entscheidet
        // RailCarriesFrom() in Haul(), nicht diese Stelle: das Netz wird weiter
        // ZUERST gefragt und behält seinen Vorrang.
        return NearestOwned(from, pick);
    }

    private void Haul(Entity e)
    {
        if (e.Owner < 0) return;

        // mine -> factory (raw Terranium)
        if (e.Deposit >= 0 && e.StockT > HaulReserve)
        {
            // Fährt die Bahn dieses Terranium schon fort? Dann nicht doppelt.
            if (RailCarriesFrom(e.Slot, GoodT)) return;
            var f = Consignee(e, x => IsFactory(x) && x.StockT < 2000);
            if (f != null)
            {
                int n = Mathf.Min(HaulAmount, e.StockT - HaulReserve);
                e.StockT -= n;
                f.StockT += n;
            }
            return;
        }

        // factory -> Basis (finished parts)
        if (IsFactory(e) && OwnParts(e) > OwnReserve(e))
        {
            // Dasselbe für das eigene Bauteil: liegt die Fabrik an einer Linie,
            // die es fortfährt (Fabrik → Bahnhof → Basis), macht der Zug es.
            int own = e.BType == 2 ? GoodW : e.BType == 3 ? GoodF : GoodS;
            if (RailCarriesFrom(e.Slot, own)) return;
            var hq = Consignee(e, x => x.BType == 1);
            if (hq == null) return;
            int n = Mathf.Min(HaulAmount, OwnParts(e) - OwnReserve(e));
            SpendParts(e, n);
            switch (e.BType)
            {
                case 2: hq.StockW += n; break;
                case 3: hq.StockF += n; break;
                default: hq.StockS += n; break;
            }
            return;
        }

        // Basis -> factory (the part types a factory does not make itself)
        //
        // The return leg, added 2026-08-01 with the real prices. A design costs
        // all three part types — that is what the original's production button
        // checks, +0x1a/+0x1b/+0x1c against three stores — but a factory only
        // ever manufactures its own type, so without this leg a Waffen-Fabrik
        // sits on 38/0/0 and can never pay for anything. Measured exactly that
        // way before it existed.
        //
        // Only foreign types travel back, and only up to what the next build
        // actually needs, so nothing loops against the outbound leg above.
        if (IsFactory(e)) SupplyFactory(e);
    }

    private void SupplyFactory(Entity f)
    {
        if (_designs == null) return;
        var menu = BuildableBy(f.BType);
        if (menu.Count == 0) return;
        var d = _designs[menu[f.MenuIndex % menu.Count]];

        int needW = f.BType == 2 ? 0 : Mathf.Max(0, d.CostW - f.StockW);
        int needF = f.BType == 3 ? 0 : Mathf.Max(0, d.CostF - f.StockF);
        int needS = f.BType is 2 or 3 ? Mathf.Max(0, d.CostS - f.StockS) : 0;
        if (needW + needF + needS == 0) return;

        var hq = Consignee(f, x => x.BType == 1);
        if (hq == null) return;

        int take = Mathf.Min(Mathf.Min(needW, hq.StockW), HaulAmount);
        hq.StockW -= take; f.StockW += take;
        take = Mathf.Min(Mathf.Min(needF, hq.StockF), HaulAmount);
        hq.StockF -= take; f.StockF += take;
        take = Mathf.Min(Mathf.Min(needS, hq.StockS), HaulAmount);
        hq.StockS -= take; f.StockS += take;
    }

    private Entity? NearestOwned(Entity from, System.Func<Entity, bool> pick)
    {
        Entity? best = null;
        float bestD = HaulRange;
        foreach (var x in _entities)
        {
            if (!x.IsBuilding || x.Dead || x.Owner != from.Owner || ReferenceEquals(x, from))
                continue;
            if (!pick(x)) continue;
            float d = CellDistance(from, x);
            if (d < bestD) { bestD = d; best = x; }
        }
        return best;
    }

    /// <summary>
    /// The game's own seven-step wording for a deposit, picked by the grade byte
    /// through a jump table @0x474528 (strings at 0x501fa4..0x501fe4).
    /// </summary>
    private static readonly string[] GradeWords =
    {
        "komplett abgebaut", "kaum mehr", "sehr gering", "gering",
        "mittel", "hoch", "sehr hoch",
    };

    /// <summary>
    /// Current grade of a deposit. The file stores a fixed grade; how it decays
    /// as the deposit empties is not in the data, so we scale it by the fraction
    /// left — that way the original wording walks down the original scale.
    /// </summary>
    private static int GradeOf(Entity e)
    {
        if (e.Deposit <= 0) return 0;
        int g = Mathf.RoundToInt(e.Grade * (float)e.Deposit / e.DepositStart);
        return Mathf.Clamp(g, 1, GradeWords.Length - 1);
    }

    /// <summary>Parts a factory has of its own product type.</summary>
    private static int OwnParts(Entity e) => e.BType switch
    {
        2 => e.StockW,
        3 => e.StockF,
        _ => e.StockS,
    };

    /// <summary>
    /// Was eine Fabrik von ihrer EIGENEN Teileart zurückhält, bevor sie
    /// abliefert: genau das, was ihr nächster Bau kostet.
    ///
    /// ⚠ CORRECTED 10.08.2026 — hier stand eine pauschale 40, frei erfunden.
    /// Gemessen an Mission 5 (»Wiederaufnahme der Produktion«) war das der
    /// Grund, warum die Mission nicht gewinnbar war: eine Fabrik macht rund ein
    /// Teil je fünf Sekunden, brauchte also über drei Minuten, bevor sie
    /// überhaupt das erste Stück abgab — und bis dahin hatte der Rückweg
    /// (`SupplyFactory`) das Lager der Basis längst unter die Marke gezogen,
    /// die sich die Mission bei der Einnahme gemerkt hatte. Gemessen: Waffen
    /// blieben über 85 Sekunden auf 0.
    ///
    /// Die neue Zahl ist **nicht** erfunden: sie kommt aus dem Entwurfssatz
    /// (+0x1a/+0x1b/+0x1c), also aus derselben Quelle, die auch die
    /// Produktionsschaltfläche des Originals prüft (@0x44A6EB).
    ///
    /// ⚠ Was daran unsere Setzung BLEIBT: dass überhaupt jemand Teile fährt.
    /// Das Original bewegt Güter über **Bahnverbindungen, die der Spieler
    /// legt** — der Produktionsschritt @0x43DFEE erhöht nur das eigene Lager
    /// der Fabrik, sonst nichts. Kampagnenkarten starten ohne Linien (map_05:
    /// 9 Knoten, 0 Verbindungen), und Linien legen kann die Engine nicht. Bis
    /// dahin steht dieser Weg dafür ein.
    /// </summary>
    private int OwnReserve(Entity f)
    {
        if (_designs == null) return PartReserve;
        var menu = BuildableBy(f.BType);
        if (menu.Count == 0) return PartReserve;
        var d = _designs[menu[f.MenuIndex % menu.Count]];
        return f.BType switch { 2 => d.CostW, 3 => d.CostF, _ => d.CostS };
    }

    private static void SpendParts(Entity e, int n)
    {
        switch (e.BType)
        {
            case 2: e.StockW = Mathf.Max(0, e.StockW - n); break;
            case 3: e.StockF = Mathf.Max(0, e.StockF - n); break;
            default: e.StockS = Mathf.Max(0, e.StockS - n); break;
        }
    }

    /// <summary>Can this factory pay for that design?
    ///
    /// The original asks exactly this, one part type after another, at the
    /// production button @0x44a6eb: design +0x1a against the first store,
    /// +0x1b against the second, +0x1c against the third, and only if all three
    /// pass does it send build command 0x1F7. The three stores are the ones the
    /// economy already keeps (+0x28 Waffen, +0x2a Fahrwerk, +0x2c Spezial), and
    /// the price fields come from the matching component — weapon, chassis,
    /// equipment — which is what settles which is which.</summary>
    private static bool CanAfford(Entity e, in Design d) =>
        d.CostW <= e.StockW && d.CostF <= e.StockF && d.CostS <= e.StockS;

    private static void PayFor(Entity e, in Design d)
    {
        e.StockW = Mathf.Max(0, e.StockW - d.CostW);
        e.StockF = Mathf.Max(0, e.StockF - d.CostF);
        e.StockS = Mathf.Max(0, e.StockS - d.CostS);
    }

    /// <summary>Harness helper: can this factory pay for what its menu points
    /// at right now?</summary>
    private static bool CanAffordMenuChoice(Entity e)
    {
        if (_designs == null) return false;
        var menu = BuildableBy(e.BType);
        if (menu.Count == 0) return false;
        return CanAfford(e, _designs[menu[e.MenuIndex % menu.Count]]);
    }

    private void UpdateProduction(int i, Entity e, float dt)
    {
        UpdateEconomy(i, e, dt);
        if (IsDock(e))                       // a Hafen launches, it does not build
        {
            if (e.BuildTime <= 0f || e.Dead) return;
            e.BuildTime -= dt;
            if (e.BuildTime <= 0f) LaunchShip(e);
            return;
        }
        if (e.BuildTime <= 0f || e.Dead || _nav == null || _designs == null) return;
        e.BuildTime -= dt;
        if (e.BuildTime > 0f) return;

        var d = _designs[e.BuildIndex % _designs.Count];
        var cell = _nav.NearestFree(new Vector2I(e.Col, e.Row), Simulation.NavGrid.MoveClass.Vehicle);
        if (cell == null) { e.BuildTime = 1f; return; }   // blocked: try again shortly
        NoteEvent(e, $"{d.Name} fertig");

        // +0x28 of the design record: the chassis' hp_max plus whatever the
        // weapon and the equipment add. HpOfType knows the chassis alone, so it
        // only stands in when the record has no tail to read.
        // Everything a new unit starts with comes out of its own design record,
        // the way the spawn routine @0x4b1b9e fills it: +0x1e -> energie_max,
        // +0x20 -> attack, +0x28 -> the fuel tank (both halves), +0x2a -> the
        // magazine (both halves). Only where a record has no tail do the older
        // stand-ins apply.
        int hp = d.Hp > 0 ? d.Hp : HpOfType(d.Propulsion);
        int ammo = d.Ammo > 0
            ? d.Ammo
            : _ammoCap.TryGetValue(TurretOf(d.Weapon), out var cap) ? cap : 0;
        var u = new Entity
        {
            Slot = -1, Col = cell.Value.X, Row = cell.Value.Y,
            Owner = e.Owner, Team = e.Team, UnitType = d.Propulsion,
            Category = -1, Hp = hp, HpMax = hp, Elev = ElevOf(cell.Value.X, cell.Value.Y),
            Name = d.Name, Equipment = d.Equip, Weapon = TurretOf(d.Weapon),
            // the spawn routine @0x4b1b9e copies design +0x2c into entity +0x0b,
            // which is the field the voice routine switches on — so a unit off
            // the line answers like the same chassis on the map does. Kind 1 is
            // what a built vehicle is; a map unit brings its own +0x0a.
            Chassis = d.Derived.ChassisComponent, Subclass = TypeOfChassis(d.Propulsion),
            Attack = d.Attack, Defence = d.Defence,
            Fuel = d.Fuel, FuelMax = d.Fuel,
            AmmoMax = ammo, Ammo = ammo,
            Range = d.Range, Sight = d.Sight, Reload = d.Reload, Speed = d.Speed,
            Facing = DefaultFacing, Mobile = true,
            // ⚠ CORRECTED 07.08.2026 — this was hard-wired to Vehicle, so a
            // unit off the line walked over the same ground as a wheeled one no
            // matter what it stood on. A LEGGED chassis (0x11) and a HOVER (7)
            // have their own class, and the map path has always asked for it
            // via ClassOf; only the production path did not.
            //
            // This is a PASSABILITY fix, not a speed fix: TerrainCost only
            // singles out Ship, so the class barely touches how fast a unit
            // moves. The report "the AI's spiders move far too fast" is NOT
            // explained by it — see the handoff.
            Move = Simulation.NavGrid.ClassOf(-1, d.Derived.ChassisComponent),
            Footprint = CellRect(_ox, _oy, cell.Value.X, cell.Value.Y, ElevOf(cell.Value.X, cell.Value.Y)),
        };
        // Ein Fussoldat traegt seine Waffe aus infantry.json, nicht aus
        // TurretOf — siehe InfantryFor. Ohne das kann er nicht kaempfen.
        if (InfantryFor(d.Weapon, out int inf, out int iw)) { u.Infantry = inf; u.Weapon = iw; }
        u.Pos = CellCenter(u.Col, u.Row);
        _entities.Add(u);
        _nav.SetOccupant(u.Col, u.Row, _entities.Count - 1);
        _order = $"{d.Name} fertig";
        QueueRedraw();
    }

    /// <summary>
    /// One unit of a reinforcement flight, put on the map — the engine's side of
    /// `space_in`.
    ///
    /// The original's chain is @0x4C0260 (the queue) -> @0x4C1600 (find a free
    /// place beside the cell, roll the two random bytes +0x02/+0x03, play effect
    /// 0x60) -> @0x4D0810 -> @0x4B34E0 `create_unit(typ, player, x, y)`. That
    /// last one is where the campaign's handle comes from: it takes the first
    /// free record in <c>player*1000 .. +999</c> and writes <b>typ into +0x43</b>
    /// (@0x4B366E). So the byte `find_unit` searches for is not a separate mark
    /// at all — it IS the design number the unit was created with, which is why
    /// mission 14 drops design 191 and then looks for 191, and why sec47 row 191
    /// is called "Col.Hullman".
    /// </summary>
    private void SpawnReinforcement(int typ, int col, int row, int player)
    {
        LoadDesigns();
        if (_nav == null) { GD.PrintErr("space_in: keine Navigationskarte"); return; }
        int raw = typ + 200 * player;
        if (!_designBySlot.TryGetValue(raw, out var d))
        {
            GD.PrintErr($"space_in: Entwurf {typ} von Spieler {player} " +
                        $"(sec47 {raw}) steht nicht in unit_designs.json");
            return;
        }
        var move = Simulation.NavGrid.ClassOf(-1, d.Derived.ChassisComponent);
        var cell = _nav.NearestFree(new Vector2I(col, row), move);
        if (cell == null)
        {
            // »Incredible error ...no free place for new robot« @0x4C1624
            GD.PrintErr($"space_in: kein freier Platz neben ({col}, {row}) fuer {d.Name}");
            return;
        }
        int hp = d.Hp > 0 ? d.Hp : HpOfType(d.Propulsion);
        int ammo = d.Ammo > 0
            ? d.Ammo
            : _ammoCap.TryGetValue(TurretOf(d.Weapon), out var cap) ? cap : 0;
        int el = ElevOf(cell.Value.X, cell.Value.Y);
        var u = new Entity
        {
            Slot = FreeRecord(player), Col = cell.Value.X, Row = cell.Value.Y,
            Owner = player, Team = player, UnitType = d.Propulsion,
            Category = -1, Hp = hp, HpMax = hp, Elev = el,
            Name = d.Name, Equipment = d.Equip, Weapon = TurretOf(d.Weapon),
            Chassis = d.Derived.ChassisComponent, Subclass = TypeOfChassis(d.Propulsion),
            Mark = typ,                       // record +0x43, written by create_unit
            Attack = d.Attack, Defence = d.Defence,
            Fuel = d.Fuel, FuelMax = d.Fuel,
            AmmoMax = ammo, Ammo = ammo,
            Range = d.Range, Sight = d.Sight, Reload = d.Reload, Speed = d.Speed,
            Facing = DefaultFacing, Mobile = true, Move = move,
            Footprint = CellRect(_ox, _oy, cell.Value.X, cell.Value.Y, el),
        };
        // Ein Fussoldat traegt seine Waffe aus infantry.json, nicht aus
        // TurretOf — siehe InfantryFor. Ohne das kann er nicht kaempfen.
        if (InfantryFor(d.Weapon, out int inf, out int iw)) { u.Infantry = inf; u.Weapon = iw; }
        u.Pos = CellCenter(u.Col, u.Row);
        _entities.Add(u);
        _nav.SetOccupant(u.Col, u.Row, _entities.Count - 1);
        GD.Print($"space_in: {d.Name} (Entwurf {typ}) fuer Spieler {player} " +
                 $"auf ({u.Col}, {u.Row}), Satz {u.Slot}");
        QueueRedraw();
    }

    /// <summary>The lowest free record index of a player. The original's tables
    /// are <c>player*1000 + k</c> — that is why it derives the owner from
    /// <c>slot/1000</c> — and @0x4B34E0 scans exactly that range for the first
    /// record whose +0x09 is 0xFF.</summary>
    private int FreeRecord(int player)
    {
        int lo = player * 1000, hi = lo + 1000;
        var used = new HashSet<int>();
        foreach (var e in _entities)
            if (!e.IsBuilding && e.Slot >= lo && e.Slot < hi) used.Add(e.Slot);
        for (int k = lo; k < hi; k++) if (!used.Contains(k)) return k;
        return -1;
    }

    /// <summary>hp_max of a propulsion type from the recovered stats table.</summary>
    private static int HpOfType(int unitType)
    {
        if (_catalogHp.TryGetValue(unitType, out int hp)) return hp;
        return 300;
    }
    private static readonly Dictionary<int, int> _catalogHp = new();

    /// <summary>
    /// Scripted demo used by the preview screenshot path: grab the mobile units
    /// around the first one found and send them ~10 cells away. Returns a point
    /// the camera should look at, or null if the map has no mobile units.
    /// </summary>
    /// <summary>
    /// Scripted combat demo for the preview screenshot: find the closest pair of
    /// hostile armed units, select the attacker's group and engage.
    /// </summary>
    /// <param name="minDist">smallest acceptable distance in tiles — pick a
    /// distant pair to watch a unit drive in while its turret already tracks.</param>
    /// <summary>
    /// Harness for the infantry rule: find a vehicle and the nearest foot
    /// soldier that is NOT its friend, and drive the vehicle straight at him.
    /// The route must go THROUGH his cell — a soldier does not block one — and
    /// he must be dead when it arrives (`prejet` @0x412980 / @0x412a50). The
    /// friendly case is reported alongside: those are driven through and live.
    /// </summary>
    public Vector2? DebugDemoCrush()
    {
        if (_nav == null) return null;
        int bestV = -1, bestF = -1;
        float best = float.MaxValue;
        for (int i = 0; i < _entities.Count; i++)
        {
            var v = _entities[i];
            if (!v.Mobile || v.Dead || v.Infantry >= 0 ||
                v.Move != Simulation.NavGrid.MoveClass.Vehicle) continue;
            for (int j = 0; j < _entities.Count; j++)
            {
                var f = _entities[j];
                if (f.Infantry < 0 || f.Dead || !IsHostile(v, f)) continue;
                float d = CellDistance(v, f);
                if (d < best) { best = d; bestV = i; bestF = j; }
            }
        }
        if (bestV < 0) { GD.Print("demo-crush: kein Fahrzeug mit feindlicher Infanterie in Sicht"); return null; }

        var veh = _entities[bestV];
        var foot = _entities[bestF];
        int friends = 0, foes = 0;
        foreach (var e in _entities)
            if (e.Infantry >= 0 && !e.Dead) { if (IsHostile(veh, e)) foes++; else friends++; }

        var cell = new Vector2I(foot.Col, foot.Row);
        bool free = _nav.IsFree(cell.X, cell.Y, veh.Move, bestV);
        var path = _nav.FindPath(new Vector2I(veh.Col, veh.Row), cell, veh.Move, bestV);
        bool onto = path != null && path.Count > 0 && path[^1] == cell;

        // The harness takes the gun off the vehicle. Otherwise it shoots the man
        // on the way in and the run proves nothing about driving over him — the
        // first attempt did exactly that and reported him dead with the crush
        // counter still at zero.
        veh.Weapon = 0;
        veh.Target = -1;

        _sel.Clear(); _sel.Add(bestV); SetPrimary();
        if (path != null) { veh.Path = path; veh.PathIdx = 0; veh.Goal = cell; veh.Ordered = true; }
        _crushWatch = bestF;
        _crushDriver = bestV;
        _order = $"crush demo: slot {veh.Slot} -> Infanterie slot {foot.Slot}";
        GD.Print($"demo-crush: Fahrzeug slot {veh.Slot} (P{veh.Owner}) bei ({veh.Col},{veh.Row}) " +
                 $"auf Infanterie slot {foot.Slot} (P{foot.Owner}) bei ({foot.Col},{foot.Row}); " +
                 $"Zelle frei fuer das Fahrzeug: {(free ? "ja" : "NEIN")}; " +
                 $"Weg endet auf der Zelle: {(onto ? "ja" : "NEIN")} " +
                 $"({(path == null ? "kein Weg" : path.Count + " Schritte")}); " +
                 $"auf der Karte {foes} feindliche und {friends} eigene Fussoldaten");
        return veh.Pos;
    }

    /// <summary>The foot soldier the crush demo is driving at, and the driver.</summary>
    private int _crushWatch = -1, _crushDriver = -1;

    /// <summary>Closing line of the crush demo, printed when the run ends.</summary>
    public string CrushReport()
    {
        if (_crushWatch < 0 || _crushWatch >= _entities.Count) return "";
        var f = _entities[_crushWatch];
        string where = _crushDriver >= 0 && _crushDriver < _entities.Count
            ? $"Fahrzeug steht auf ({_entities[_crushDriver].Col},{_entities[_crushDriver].Row}), " +
              $"Ziel war ({f.Col},{f.Row}); " : "";
        return $"crush: {where}Infanterie slot {f.Slot} " +
               $"{(f.Dead ? "liegt" : "steht noch")}; {_crushed} ueberfahren";
    }

    public Vector2? DebugDemoFight(float minDist = 0f)
    {
        int bestA = -1, bestB = -1;
        float best = float.MaxValue;
        for (int i = 0; i < _entities.Count; i++)
        {
            var a = _entities[i];
            if (!CanFight(a) || !a.Mobile) continue;
            for (int j = 0; j < _entities.Count; j++)
            {
                var b = _entities[j];
                if (i == j || !IsHostile(a, b)) continue;
                float d = CellDistance(a, b);
                if (d < minDist) continue;
                if (d < best) { best = d; bestA = i; bestB = j; }
            }
        }
        if (bestA < 0) { GD.Print("demo-fight: no hostile pair found"); return null; }

        var atk = _entities[bestA];
        var vic = _entities[bestB];
        _sel.Clear();
        for (int i = 0; i < _entities.Count; i++)
        {
            var e = _entities[i];
            if (CanFight(e) && e.Owner == atk.Owner && IsHostile(e, vic) &&
                CellDistance(e, atk) <= 10)
            { _sel.Add(i); e.Target = bestB; e.Ordered = true; }
        }
        SetPrimary();
        _order = $"attack demo: {_sel.Count} unit(s) of P{atk.Owner} vs slot {vic.Slot} " +
                 $"(P{vic.Owner}), {best:0.0} tiles apart";
        GD.Print("demo-fight: " + _order);
        UpdatePanel();
        QueueRedraw();
        return (atk.Pos + vic.Pos) / 2f;
    }

    /// <summary>Preview harness: select the first building that sits on a deposit.</summary>
    public Vector2? DebugDemoMine()
    {
        int idx = _entities.FindIndex(e => e.IsBuilding && e.Deposit >= 0);
        if (idx < 0) { GD.Print("demo-mine: no deposit on this map"); return null; }
        _sel.Clear();
        _sel.Add(idx);
        SetPrimary();
        var e = _entities[idx];
        GD.Print($"demo-mine: {e.Name} ({BuildingTypeName(e.BType)}) deposit {e.Deposit} stock {e.StockT}");
        QueueRedraw();
        return e.Pos;
    }

    /// <summary>Preview harness: select a Basis and start a research project.</summary>
    public Vector2? DebugDemoResearch()
    {
        int idx = _entities.FindIndex(e => e.IsBuilding && e.BType == 1 && !e.Dead &&
                                          e.Owner >= 0 && e.Owner < 8);
        if (idx < 0) { GD.Print("demo-research: no Basis with a player owner"); return null; }
        _sel.Clear();
        _sel.Add(idx);
        SetPrimary();
        var e2 = _entities[idx];
        int before = BuildableBy(4).Count;
        _money[Mathf.Clamp(e2.Owner, 0, 7)] += ResearchCost;   // fund the demo project
        StartResearch();
        _researchWatch = idx;
        GD.Print($"demo-research: {e2.Name} (Basis) P{e2.Owner}, Spezial-Menue vorher {before} " +
                 $"-> {_order}");
        return e2.Pos;
    }

    private int _researchWatch = -1;

    /// <summary>Preview harness for the design screen: open it, walk the three
    /// rows, take a design, and say whether a factory can actually build it —
    /// the only thing that matters about a new design.</summary>
    public Vector2? DebugDemoDesign()
    {
        ToggleDesigner();
        if (!Designer.Ready) { GD.Print("demo-design: keine Bauteildaten"); return null; }
        int before2 = BuildableBy(2).Count, before3 = BuildableBy(3).Count;

        // pick something other than the first of each, so the walk is visible
        Designer.Move(0); Designer.Change(3);          // Fahrwerk
        Designer.Move(1); Designer.Change(2);          // Waffe
        Designer.Move(1); Designer.Change(1);          // Ausruestung
        string name = Designer.ProposedName();
        DesignerInput(0, 0, true);

        var p = Designer.CurrentPropulsion;
        var w = Designer.CurrentWeapon;
        var q = Designer.CurrentEquipment;
        GD.Print($"demo-design: '{name}' = Fahrwerk {p?.Id} {p?.Name}, Waffe {w?.Id} {w?.Name}, " +
                 $"Ausruestung {(q == null ? "keine" : q.Id + " " + q.Name)}");
        GD.Print($"demo-design: Waffen-Fabrik {before2} -> {BuildableBy(2).Count}, " +
                 $"Fahrwerk-Fabrik {before3} -> {BuildableBy(3).Count}; {Designer.Message}");
        Designer.Close();

        int idx = _entities.FindIndex(e => e.IsBuilding && IsFactory(e) && !e.Dead);
        return idx >= 0 ? _entities[idx].Pos : null;
    }

    /// <summary>Preview harness: damage a factory, order the repair, and put a
    /// second factory into Lagerausbau and a third into Produktionserweiterung.</summary>
    public Vector2? DebugDemoState()
    {
        var facts = new List<int>();
        for (int i = 0; i < _entities.Count; i++)
            if (IsFactory(_entities[i]) && !_entities[i].Dead && _entities[i].Owner is >= 0 and <= 7)
                facts.Add(i);
        if (facts.Count == 0) { GD.Print("demo-state: no factory on this map"); return null; }

        var a = _entities[facts[0]];
        a.Hp = Mathf.Max(1, a.HpMax - 60);          // knock it down so repair has work
        _sel.Clear(); _sel.Add(facts[0]); SetPrimary();
        StartRepair();
        _stateWatch = new[] { facts[0], -1, -1 };

        if (facts.Count > 1)
        {
            _sel.Clear(); _sel.Add(facts[1]); SetPrimary();
            _money[Mathf.Clamp(_entities[facts[1]].Owner, 0, 7)] += _entities[facts[1]].CostStore;
            StartUpgrade(true);
            _stateWatch[1] = facts[1];
        }
        if (facts.Count > 2)
        {
            _sel.Clear(); _sel.Add(facts[2]); SetPrimary();
            _money[Mathf.Clamp(_entities[facts[2]].Owner, 0, 7)] += _entities[facts[2]].CostProd;
            StartUpgrade(false);
            _stateWatch[2] = facts[2];
        }
        _sel.Clear(); _sel.Add(facts[0]); SetPrimary();
        GD.Print($"demo-state: {a.Name} ({BuildingTypeName(a.BType)}) hp {a.Hp}/{a.HpMax} " +
                 $"-> {StateName(a)}; " +
                 (_stateWatch[1] >= 0
                      ? $"Lagerausbau auf {_entities[_stateWatch[1]].Name} " +
                        $"(Lagerplatz {_entities[_stateWatch[1]].Capacity}, " +
                        $"Kosten {_entities[_stateWatch[1]].CostStore}); " : "") +
                 (_stateWatch[2] >= 0
                      ? $"Produktionserw. auf {_entities[_stateWatch[2]].Name} " +
                        $"(V{_entities[_stateWatch[2]].ProdSpeed}, " +
                        $"Kosten {_entities[_stateWatch[2]].CostProd})" : ""));
        return a.Pos;
    }

    private int[]? _stateWatch;

    /// <summary>Preview harness: wipe out one side so the verdict can be seen.
    /// `win` clears everyone the viewed player is not allied with, otherwise it
    /// clears the viewed player himself.</summary>
    public Vector2? DebugDemoEnd(bool win)
    {
        int me = ViewPlayer;
        int before = 0, killed = 0;
        Vector2? focus = null;
        foreach (var e in _entities)
        {
            if (e.IsProp || e.Dead || e.HpMax <= 0 || e.Owner is < 0 or > 7) continue;
            before++;
            bool doomed = win ? !Allied(me, e.Owner) : e.Owner == me;
            if (!doomed) continue;
            e.Hp = 0; e.Dead = true; e.DeadTime = 0; e.Path = null; e.Target = -1;
            killed++;
            focus ??= e.Pos;
        }
        GD.Print($"demo-end({(win ? "win" : "lose")}): Spieler {me}, " +
                 $"{killed} von {before} ausgeloescht -> {MissionLine()}");
        return focus;
    }

    /// <summary>Preview harness: look at the foot soldiers on this map.</summary>
    public Vector2? DebugDemoInfantry()
    {
        var idxs = new List<int>();
        for (int i = 0; i < _entities.Count; i++)
            if (_entities[i].Infantry >= 0) idxs.Add(i);
        if (idxs.Count == 0) { GD.Print("demo-inf: keine Infanterie auf dieser Karte"); return null; }
        // pick the spot where soldiers already stand close to an enemy, so the
        // fight happens in front of the camera instead of a long march away
        int best = idxs[0], bestN = -1;
        foreach (int i in idxs)
        {
            int n = 0, foes = 0;
            foreach (int j in idxs)
                if (Mathf.Abs(_entities[i].Col - _entities[j].Col) <= 6 &&
                    Mathf.Abs(_entities[i].Row - _entities[j].Row) <= 6) n++;
            for (int j = 0; j < _entities.Count; j++)
                if (IsHostile(_entities[i], _entities[j]) &&
                    CellDistance(_entities[i], _entities[j]) <= 8f) foes++;
            int score = n + foes * 4;
            if (foes > 0 && score > bestN) { bestN = score; best = i; }
        }
        if (bestN < 0) best = idxs[0];
        var sets = new SortedSet<int>();
        foreach (int i in idxs) sets.Add(_entities[i].Infantry);
        // put every armed soldier around that spot onto the nearest enemy, so
        // the preview actually shows them shooting
        _sel.Clear();
        var e = _entities[best];
        int ordered = 0;
        foreach (int i in idxs)
        {
            var s = _entities[i];
            if (s.Weapon == 0) continue;
            if (Mathf.Abs(s.Col - e.Col) > 12 || Mathf.Abs(s.Row - e.Row) > 12) continue;
            _sel.Add(i);
            // prefer an armed enemy so the preview also shows return fire
            int tgt = -1; float bd = 40f;
            for (int pass = 0; pass < 2 && tgt < 0; pass++)
                for (int j = 0; j < _entities.Count; j++)
                {
                    if (!IsHostile(s, _entities[j])) continue;
                    if (pass == 0 && (_entities[j].Weapon == 0 || _entities[j].IsBuilding))
                        continue;
                    float dd = CellDistance(s, _entities[j]);
                    if (dd < bd) { bd = dd; tgt = j; }
                }
            if (tgt < 0) continue;
            s.Target = tgt; s.Ordered = true;
            s.Goal = new Vector2I(_entities[tgt].Col, _entities[tgt].Row);
            ordered++;
        }
        if (_sel.Count == 0) _sel.Add(best);
        SetPrimary();
        GD.Print($"demo-inf: {idxs.Count} Infanteristen, Sets {string.Join(",", sets)}; " +
                 $"Blick auf Set {e.Infantry} bei ({e.Col},{e.Row}), {bestN} im Umkreis, " +
                 $"{ordered} auf ein Ziel angesetzt");
        return e.Pos;
    }

    /// <summary>Preview harness: select a Flughafen, launch its player's whole
    /// air force and watch it go.</summary>
    public Vector2? DebugDemoAir()
    {
        int idx = _entities.FindIndex(e => e.IsBuilding && e.BType == 9 && !e.Dead &&
                                           e.Hangar != null && e.Hangar.Count > 0);
        if (idx < 0) idx = _entities.FindIndex(e => e.IsBuilding && e.BType == 9 && !e.Dead);
        var byType = new Dictionary<string, int>();
        foreach (var s in _special)
        {
            string k = s.TypeName.Length > 0 ? s.TypeName : s.Name;
            byType[k] = byType.GetValueOrDefault(k) + 1;
        }
        var parts = new List<string>();
        foreach (var kv in byType) parts.Add($"{kv.Value}x {kv.Key}");
        parts.Sort();
        GD.Print($"demo-air: {_special.Count} Flugzeuge — {string.Join(", ", parts)}");
        if (idx >= 0) LaunchAircraft(_entities[idx].Owner);
        if (idx < 0)
        {
            if (_special.Count == 0) return null;
            var c0 = _special[0];
            GD.Print($"demo-air: kein Flughafen — Blick auf {c0.Name} bei ({c0.Col},{c0.Row})");
            return c0.Pos;
        }
        _sel.Clear(); _sel.Add(idx); SetPrimary();
        var e2 = _entities[idx];
        GD.Print($"demo-air: {e2.Name} (Flughafen) P{e2.Owner} " +
                 $"Hangar {(e2.Hangar?.Count ?? 0)}/{e2.HangarSize} " +
                 $"[{HangarNames(e2)}]");
        return e2.Pos;
    }

    /// <summary>Preview harness: pick the owner with the most supply helicopters,
    /// bleed a handful of his units (hit points for the Treibstoffheli, rounds
    /// for the Munitionheli) and watch the helicopters work through them.</summary>
    public Vector2? DebugDemoSupply()
    {
        var helis = _special.FindAll(s => s.IsSupply && !s.Dead);
        if (helis.Count == 0) { GD.Print("demo-supply: keine Versorgungshelis auf dieser Karte"); return null; }

        var perOwner = new Dictionary<int, int>();
        foreach (var s in helis) perOwner[s.Owner] = perOwner.GetValueOrDefault(s.Owner) + 1;
        int owner = -1, bestN = -1;
        foreach (var kv in perOwner) if (kv.Value > bestN) { bestN = kv.Value; owner = kv.Key; }

        // wound a few of that player's units so there is something to service
        int hurt = 0, dry = 0;
        var focus = new List<Entity>();
        foreach (var e in _entities)
        {
            if (e.IsBuilding || e.IsProp || e.Dead || e.Owner != owner) continue;
            // the Treibstoffheli looks for a low TANK, the Munitionheli for
            // an empty magazine — drain one of each
            if (e.FuelMax > 0 && hurt < 3) { e.Fuel = Mathf.Max(1, e.FuelMax * 40 / 100); hurt++; focus.Add(e); }
            else if (e.AmmoMax > 0 && dry < 3) { e.Ammo = e.AmmoMax * 30 / 100; dry++; focus.Add(e); }
            if (hurt >= 3 && dry >= 3) break;
        }

        // and drain one unit that stands next to one of its owner's buildings,
        // so the building's own trickle rearm is visible too
        foreach (var e in _entities)
        {
            if (e.IsBuilding || e.IsProp || e.Dead || e.Owner != owner || e.AmmoMax <= 0) continue;
            var near = _entities.Find(b => b.IsBuilding && !b.Dead && b.Owner == owner &&
                                           Mathf.Abs(b.Col - e.Col) <= 1 && Mathf.Abs(b.Row - e.Row) <= 1);
            if (near == null) continue;
            e.Ammo = 0;
            focus.Add(e);
            GD.Print($"demo-supply: slot {e.Slot} steht an {near.Name} und wurde auf 0 Munition gesetzt");
            break;
        }

        foreach (var s in helis) if (s.Owner == owner) s.Customer = -1;
        // send one of them off nearly empty so the run to the Nachschub-Posten
        // is exercised inside a short capture
        var first = helis.Find(s => s.Owner == owner);
        if (first != null) first.Cargo = 5;
        LaunchAircraft(owner);

        var names = new List<string>();
        foreach (var s in helis)
            if (s.Owner == owner)
                names.Add($"{(s.TypeName.Length > 0 ? s.TypeName : s.Name)} nutzlast={s.Cargo}");
        // and send one damaged unit onto a Nachschub-Posten, which services
        // whatever stands on it
        bool sent = false;
        foreach (var post in _entities.FindAll(x => x.IsBuilding && x.BType == 14 && !x.Dead))
        {
            // nearest units first — most of them cannot reach a given post at
            // all (water, walls), so try until one takes the order
            var cands = new List<Entity>();
            foreach (var e in _entities)
                if (!e.IsBuilding && !e.IsProp && !e.Dead && e.Mobile && e.Owner == owner)
                    cands.Add(e);
            cands.Sort((a, b) => a.Pos.DistanceTo(post.Pos).CompareTo(b.Pos.DistanceTo(post.Pos)));
            Entity? best = null; int bestLen = int.MaxValue;
            foreach (var w in cands.GetRange(0, Mathf.Min(12, cands.Count)))
            {
                int wi = _entities.IndexOf(w);
                _sel.Clear(); _sel.Add(wi); SetPrimary();
                IssueMove(post.Pos);
                if (w.Path == null) continue;                  // unreachable
                if (w.Path.Count < bestLen) { bestLen = w.Path.Count; best = w; }
                w.Path = null;
            }
            if (best == null) continue;
            int bi = _entities.IndexOf(best);
            _sel.Clear(); _sel.Add(bi); SetPrimary();
            IssueMove(post.Pos);
            best.Fuel = Mathf.Max(1, best.FuelMax * 40 / 100);  // give it a reason to go
            if (best.AmmoMax > 0) best.Ammo = best.AmmoMax * 30 / 100;
            if (!focus.Contains(best)) focus.Add(best);
            GD.Print($"demo-supply: slot {best.Slot} (Sprit {best.Fuel}/{best.FuelMax}, " +
                     $"{best.Ammo}/{best.AmmoMax} Muni) faehrt zum Nachschub-Posten " +
                     $"{post.Name} ({post.Col},{post.Row}), " +
                     $"{best.Pos.DistanceTo(post.Pos) / TileW:0.0} Kacheln, " +
                     $"{bestLen} Schritte");
            sent = true;
            break;
        }
        if (!sent) GD.Print("demo-supply: keine Einheit kann einen Nachschub-Posten erreichen");

        if (focus.Count > 0)                 // show a customer in the panel
        {
            int fi = _entities.IndexOf(focus[0]);
            if (fi >= 0) { _sel.Clear(); _sel.Add(fi); SetPrimary(); }
        }
        int depots = _entities.FindAll(e => e.IsBuilding && e.BType == 14 && !e.Dead).Count;
        GD.Print($"demo-supply: P{owner} hat {bestN} Versorgungshelis [{string.Join(", ", names)}], " +
                 $"{depots} Nachschub-Posten auf der Karte; " +
                 $"{hurt} Einheiten auf 40% Sprit, {dry} auf 30% Munition gesetzt");
        foreach (var e in focus)
            GD.Print($"  kunde slot {e.Slot} ({e.Col},{e.Row}) hp {e.Hp}/{e.HpMax} mun {e.Ammo}/{e.AmmoMax}");
        _supplyWatch = focus;
        return focus.Count > 0 ? focus[0].Pos : helis[0].Pos;
    }

    private List<Entity>? _supplyWatch;

    /// <summary>What became of the units the supply demo bled.</summary>
    public string SupplyWatchLine()
    {
        if (_supplyWatch == null) return "";
        var parts = new List<string>();
        foreach (var e in _supplyWatch)
            parts.Add($"slot {e.Slot} energie {e.Hp}/{e.HpMax} sprit {e.Fuel}/{e.FuelMax} " +
                      $"mun {e.Ammo}/{e.AmmoMax}");
        var helis = new List<string>();
        foreach (var s in _special)
        {
            if (s.Dead || !s.IsSupply) continue;
            helis.Add($"{(s.Kind == 13 ? "T" : "M")}{s.Slot} last={s.Cargo} " +
                      $"kunde={s.Customer}{(s.DepotSlot >= 0 ? " NP" : "")}");
        }
        return $"lieferungen={SupplyRuns} posten={SupplyPostRuns}  " +
               $"kunden: {string.Join(" | ", parts)}  helis: {string.Join(" | ", helis)}";
    }

    /// <summary>Preview harness: store a control group, drop the selection and
    /// recall it — the keys themselves cannot be driven headlessly.</summary>
    public Vector2? DebugDemoGroups()
    {
        var idxs = new List<int>();
        for (int i = 0; i < _entities.Count && idxs.Count < 5; i++)
            if (!_entities[i].IsBuilding && !_entities[i].IsProp && !_entities[i].Dead
                && _entities[i].Mobile) idxs.Add(i);
        if (idxs.Count == 0) { GD.Print("demo-groups: keine mobilen Einheiten"); return null; }
        _sel.Clear(); foreach (int i in idxs) _sel.Add(i); SetPrimary();
        StoreGroup(1);
        int stored = _sel.Count;
        _sel.Clear(); SetPrimary();
        bool ok = RecallGroup(1);
        var c = SelectionCenter();
        GD.Print($"demo-groups: {stored} Einheiten in Gruppe 1 gelegt, Auswahl geleert, " +
                 $"zurueckgeholt={ok} -> {_sel.Count} Einheiten, Mitte={c}");
        return c;
    }

    /// <summary>Preview harness: buy a supply helicopter at an airfield.</summary>
    public Vector2? DebugDemoBuy()
    {
        int idx = -1;
        foreach (var (e, i) in _entities.Select((e, i) => (e, i)))
        {
            if (!e.IsBuilding || e.BType != 9 || e.Dead || e.Owner is < 0 or > 7) continue;
            if (AirMenu(e).Count == 0) continue;
            idx = i; break;
        }
        if (idx < 0) { GD.Print("demo-buy: kein Flughafen mit Kaufliste"); return null; }
        var b = _entities[idx];
        // the shipped maps hand every player a Kontostand of 0, so the demo
        // funds the purchase the same way the research demo does
        if (_money[b.Owner] < HeliPrice)
        {
            _money[b.Owner] += HeliPrice * 3;
            GD.Print($"demo-buy: Kontostand von P{b.Owner} war 0 — fuer die Demo auf " +
                     $"${_money[b.Owner]} gesetzt");
        }
        _sel.Clear(); _sel.Add(idx); SetPrimary();
        var menu = AirMenu(b);
        GD.Print($"demo-buy: {b.Name} (Flughafen) P{b.Owner}, Kontostand ${_money[b.Owner]}, " +
                 $"Hangar {(b.Hangar?.Count ?? 0)}/{b.HangarSize}, kaufbar: " +
                 string.Join(", ", menu.Select(d => d.Name)));
        int before = _money[b.Owner], air = _special.Count;
        ProduceFromSelection();
        GD.Print($"demo-buy: {_order}  -> Kontostand ${before} -> ${_money[b.Owner]}, " +
                 $"Flugzeuge {air} -> {_special.Count}, Hangar {(b.Hangar?.Count ?? 0)}");
        return b.Pos;
    }

    /// <summary>Preview harness: order a ship at a Hafen and let it run off the
    /// slipway. Prints the yard's part store before and after, which is what
    /// the original checks and deducts.</summary>
    public Vector2? DebugDemoShip()
    {
        int idx = -1;
        for (int i = 0; i < _entities.Count; i++)
        {
            var e = _entities[i];
            if (!IsDock(e) || e.Dead || e.Owner is < 0 or > 7) continue;
            if (ShipMenu(e).Count == 0) continue;
            idx = i; break;
        }
        if (idx < 0)
        {
            GD.Print($"demo-ship: kein Hafen mit Bauliste (Liste aus {(_shipSource.Length > 0 ? _shipSource : "nichts")})");
            return null;
        }
        var dock = _entities[idx];
        int yi = ShipyardOf(dock);
        var yard = yi >= 0 ? _entities[yi] : null;
        var menu = ShipMenu(dock);
        GD.Print($"demo-ship: Hafen '{dock.Name}' slot {dock.Slot} P{dock.Owner} ({dock.Col},{dock.Row}), " +
                 $"Werft slot {dock.Shipyard} '{yard?.Name}' Teile {yard?.StockW}/{yard?.StockF}/{yard?.StockS}, " +
                 $"Liste aus {_shipSource}: " +
                 string.Join(", ", menu.Select(d => $"{d.Name} {d.CostW}/{d.CostF}/{d.CostS}")));
        _sel.Clear(); _sel.Add(idx); SetPrimary();
        int shipsBefore = _entities.Count(e => !e.IsBuilding && !e.IsProp && !e.Dead
                                            && NavalTypes.Contains(e.UnitType));
        int w0 = yard?.StockW ?? 0, f0 = yard?.StockF ?? 0, s0 = yard?.StockS ?? 0;
        ProduceFromSelection();
        GD.Print($"demo-ship: {_order}  Teile {w0}/{f0}/{s0} -> " +
                 $"{yard?.StockW}/{yard?.StockF}/{yard?.StockS}, Schiffe vorher {shipsBefore}");

        // Exercise the shortage branch as well. No shipped map combines a
        // player-owned dock, a design list and a yard too poor to pay, so the
        // harness empties the store, asks again and puts it back — the same
        // kind of nudge --demo-supply uses to bleed its customers.
        if (yard != null)
        {
            int kw = yard.StockW, kf = yard.StockF, ks = yard.StockS;
            float bt = dock.BuildTime;
            string keep = _order;
            yard.StockW = yard.StockF = yard.StockS = 0;
            dock.BuildTime = 0f;
            BuildShip(dock);
            GD.Print($"demo-ship: mit leerem Lager -> \"{_order}\"");
            yard.StockW = kw; yard.StockF = kf; yard.StockS = ks;
            dock.BuildTime = bt; _order = keep;
        }
        _shipWatch = idx;
        return dock.Pos;
    }

    private int _shipWatch = -1;

    /// <summary>Status line for the ship demo, read after the sim has run.</summary>
    public string ShipWatchLine()
    {
        if (_shipWatch < 0) return "";
        var dock = _entities[_shipWatch];
        int yi = ShipyardOf(dock);
        var yard = yi >= 0 ? _entities[yi] : null;
        var ships = _entities.Where(e => !e.IsBuilding && !e.IsProp && !e.Dead
                                      && NavalTypes.Contains(e.UnitType)).ToList();
        var fresh = ships.Where(e => e.Slot < 0).ToList();
        return $"werft={dock.Name} teile={yard?.StockW}/{yard?.StockF}/{yard?.StockS} " +
               $"bau={dock.BuildTime:0.0}s gebaut={_shipsBuilt} schiffe={ships.Count} " +
               $"neu=[{string.Join(" | ", fresh.Select(e => $"{e.Name} ut{e.UnitType} ({e.Col},{e.Row}) " +
                    $"energie {e.Hp}/{e.HpMax} sprit {e.Fuel}/{e.FuelMax} mun {e.Ammo}/{e.AmmoMax}"))}]";
    }

    /// <summary>Preview harness: look at the longest train on the map.</summary>
    public Vector2? DebugDemoTrain()
    {
        if (_wagons.Count == 0) { GD.Print("demo-train: keine Waggons in dieser Karte"); return null; }
        var byLine = new Dictionary<int, List<Wagon>>();
        foreach (var w in _wagons)
        {
            if (!_lineRoute.ContainsKey(w.Line)) continue;
            if (!byLine.TryGetValue(w.Line, out var l)) byLine[w.Line] = l = new List<Wagon>();
            l.Add(w);
        }
        if (byLine.Count == 0)
        { GD.Print($"demo-train: {_wagons.Count} Waggons, aber keiner auf einer bekannten Linie"); return null; }
        int best = -1, bestLen = -1;
        foreach (var kv in byLine)
        {
            int len = _lineRoute[kv.Key].Count;
            if (kv.Value.Count * 1000 + len > bestLen) { bestLen = kv.Value.Count * 1000 + len; best = kv.Key; }
        }
        var team = byLine[best];
        team.Sort((a, b) => a.Index.CompareTo(b.Index));
        var route = _lineRoute[best];
        GD.Print($"demo-train: Linie {best}, Route {route.Count} Schritte, {team.Count} Waggons: " +
                 string.Join(" | ", team.ConvertAll(w =>
                     $"W{w.Index} Schritt {w.Step} bei ({w.Col},{w.Row}) Gleis {w.Piece} " +
                     $"Part {(WagonPart.TryGetValue(w.Index, out var p) ? p : 58)}" +
                     $"{(GetTrainTexture(WagonPart.TryGetValue(w.Index, out var p2) ? p2 : 58, w.Piece) != null ? "" : " TEXTUR FEHLT")}")));
        _trainWatch = best;
        return RailPoint(new Vector2(team[0].Col, team[0].Row));
    }

    private int _trainWatch = -1;

    /// <summary>Status line for the train demo, read after the sim has run.</summary>
    public string TrainWatchLine()
    {
        if (_trainWatch < 0) return "";
        var team = _wagons.FindAll(w => w.Line == _trainWatch);
        team.Sort((a, b) => a.Index.CompareTo(b.Index));
        int len = _lineRoute.TryGetValue(_trainWatch, out var r) ? r.Count : 0;
        return $"zug linie={_trainWatch} laenge={len} waggons={team.Count} " +
               string.Join(" ", team.ConvertAll(w => $"[W{w.Index} s{w.Step} ({w.Col},{w.Row}) g{w.Piece}]"));
    }

    /// <summary>Preview harness: select the first factory and start building.</summary>
    public Vector2? DebugDemoBuild()
    {
        int idx = _entities.FindIndex(e => IsFactory(e) && !e.Dead);
        if (idx < 0) { GD.Print("demo-build: no factory on this map"); return null; }
        _sel.Clear();
        _sel.Add(idx);
        SetPrimary();
        _buildPending = true;          // the store is empty at t=0; wait for parts
        var e2 = _entities[idx];
        // watch from the factory owner's side — otherwise the run reports on a
        // player who owns nothing on this map, and its events never fire
        if (e2.Owner is >= 0 and <= 7) ViewPlayer = e2.Owner;
        GD.Print($"demo-build: {e2.Name} ({BuildingTypeName(e2.BType)}) of P{e2.Owner} " +
                 $"at ({e2.Col},{e2.Row}) -> {_order}");
        _buildWatch = idx;
        return e2.Pos;
    }

    /// <summary>Harness for the order queue: pick one unit, give it a move and
    /// then append two more, and report what it is actually carrying. The
    /// progress is printed again when the run ends, so the queue can be seen
    /// draining rather than merely being accepted.</summary>
    public Vector2? DebugDemoQueue()
    {
        if (_nav == null) return null;
        int seed = _entities.FindIndex(e => e.Mobile && !e.Dead && e.Owner == ViewPlayer &&
                                            _nav.IsWalkable(e.Col, e.Row, e.Move));
        if (seed < 0) seed = _entities.FindIndex(e => e.Mobile && !e.Dead);
        if (seed < 0) { GD.Print("demo-queue: keine bewegliche Einheit"); return null; }
        var s = _entities[seed];
        _sel.Clear();
        _sel.Add(seed);
        SetPrimary();
        _queueWatch = seed;

        // Walkable is not the same as reachable — a cell across water passes the
        // first test and fails the second. Take only cells a route actually
        // reaches, so the harness measures the queue and not the pathfinder.
        var goals = new List<Vector2I>();
        var from = new Vector2I(s.Col, s.Row);
        for (int rad = 2; rad <= 6 && goals.Count < 3; rad++)
            foreach (var d in new[] { new Vector2I(rad, 0), new Vector2I(0, rad),
                                      new Vector2I(-rad, 0), new Vector2I(0, -rad),
                                      new Vector2I(rad, rad), new Vector2I(-rad, rad) })
            {
                if (goals.Count >= 3) break;
                var c = new Vector2I(s.Col + d.X, s.Row + d.Y);
                if (goals.Contains(c) || !_nav.IsWalkable(c.X, c.Y, s.Move)) continue;
                var probe = _nav.FindPath(from, c, s.Move, seed);
                if (probe == null || probe.Count == 0) continue;
                goals.Add(c);
            }
        if (goals.Count == 0) { GD.Print("demo-queue: kein erreichbares Ziel"); return s.Pos; }

        IssueMove(CellCenter(goals[0].X, goals[0].Y));
        GD.Print($"demo-queue: Einheit {seed} ({LabelOf(s.UnitType)}) bei ({s.Col},{s.Row}); " +
                 $"Befehl 1 -> ({goals[0].X},{goals[0].Y}): Pfad={(s.Path?.Count ?? 0)} Schritte");
        for (int k = 1; k < goals.Count; k++)
        {
            IssueMove(CellCenter(goals[k].X, goals[k].Y), queue: true);
            GD.Print($"demo-queue: Befehl {k + 1} -> ({goals[k].X},{goals[k].Y}): " +
                     $"jetzt {s.Orders.Count} angereiht");
        }

        // and an attack behind the moves, if there is anything to attack
        if (CanFight(s))
        {
            int foe = _entities.FindIndex(x => !x.IsProp && !x.Dead && x.HpMax > 0 &&
                                               IsHostile(s, x) && x.Pos.DistanceTo(s.Pos) < 40 * 40);
            if (foe >= 0)
            {
                int before = s.Orders.Count;
                IssueAttack(_entities[foe].Pos, queue: true);
                _queueFoe = foe;
                _queueFoeHp = _entities[foe].Hp;
                GD.Print($"demo-queue: Angriff auf {foe} ({LabelOf(_entities[foe].UnitType)}, " +
                         $"{_entities[foe].Hp} HP) angereiht: {before} -> {s.Orders.Count}");
            }
            else GD.Print("demo-queue: kein Gegner in Reichweite zum Anreihen");
        }
        return s.Pos;
    }

    private int _queueWatch = -1, _queueFoe = -1, _queueFoeHp = -1;

    /// <summary>What the watched unit still has to do — printed at the end of a
    /// scripted run.</summary>
    public string QueueWatchLine()
    {
        if (_queueWatch < 0 || _queueWatch >= _entities.Count) return "";
        var e = _entities[_queueWatch];
        string foe = "";
        if (_queueFoe >= 0 && _queueFoe < _entities.Count)
        {
            var f = _entities[_queueFoe];
            foe = $"; angereihtes Angriffsziel {_queueFoe}: {_queueFoeHp} -> {f.Hp} HP" +
                  (f.Dead ? " (zerstoert)" : "");
        }
        return $"queue: Einheit {_queueWatch} bei ({e.Col},{e.Row}), " +
               $"unterwegs={(e.Path != null ? "ja" : "nein")}, " +
               $"Ziel={(e.Target >= 0 ? e.Target.ToString() : "keins")}, " +
               $"noch {e.Orders.Count} angereiht" + foe;
    }

    /// <summary>The watched factory's three stores against what its menu choice
    /// costs. Since a design's price is taken from the record rather than from a
    /// flat 20, a factory needs all THREE part types — which is what the
    /// original's production button checks — so this is the line that says
    /// whether the economy actually reaches that.</summary>
    public string BuildWatchLine()
    {
        if (_buildWatch < 0 || _buildWatch >= _entities.Count) return "";
        var e = _entities[_buildWatch];
        var menu = BuildableBy(e.BType);
        string want = "keine Auswahl";
        if (menu.Count > 0 && _designs != null)
        {
            var d = _designs[menu[e.MenuIndex % menu.Count]];
            want = $"{d.Name} kostet {d.CostW}/{d.CostF}/{d.CostS}";
        }
        return $"build: Fabrik {_buildWatch} (P{e.Owner}) Lager {e.StockW}/{e.StockF}/{e.StockS}, " +
               $"{want}, Bauzeit {e.BuildTime:0.0}s";
    }

    /// <summary>What the alarm ring is holding — for a scripted run, which
    /// cannot look at the minimap.</summary>
    public string EventWatchLine()
    {
        if (_events.Count == 0) return "events: keine";
        var sb = new System.Text.StringBuilder($"events: {_events.Count} gemerkt, ");
        sb.Append($"{MinimapAlarms().Count} noch als Alarm sichtbar");
        for (int k = _events.Count - 1; k >= 0 && k > _events.Count - 4; k--)
            sb.Append($" | {_events[k].What} bei ({_events[k].Pos.X / TileW:0},{_events[k].Pos.Y / TileH:0})");
        return sb.ToString();
    }

    /// <summary>What the info panel would say about every unit on this map —
    /// the same reason as <see cref="VoiceWatchLine"/>: a scripted run cannot
    /// click, so the panel is exercised instead of eyeballed.
    ///
    /// It counts the three things that were wrong on 0.4.0: units whose weapon
    /// has no name (the panel handed out component 24's name to all of them),
    /// units with a magazine, and units with a fuel tank (which the panel never
    /// printed at all).</summary>
    public string PanelWatchLine()
    {
        LoadWeapons();
        int units = 0, unnamed = 0, withAmmo = 0, withFuel = 0;
        var names = new HashSet<string>();
        foreach (var e in _entities)
        {
            if (e.IsBuilding || e.IsProp || e.Weapon == 0 && e.FuelMax == 0) continue;
            units++;
            if (e.Weapon != 0)
            {
                string n = WeaponOf(e.Weapon).Name;
                names.Add(n);
                if (n.StartsWith("BAUTEIL ")) unnamed++;
            }
            if (e.AmmoMax > 0) withAmmo++;
            if (e.FuelMax > 0) withFuel++;
        }
        return $"panel: {units} Einheiten, {names.Count} verschiedene Waffennamen, " +
               $"{unnamed} ohne Namen, {withAmmo} mit Munition, {withFuel} mit Sprit";
    }

    /// <summary>Which pose group every unit is drawn from, and whether the
    /// export carries it. Exists because the answer used to be "group 0, all of
    /// them" without anything saying so — the 59 Läufer on the maps are spread
    /// over all eight of theirs.</summary>
    public string PoseWatchLine()
    {
        var used = new SortedDictionary<int, int>();
        int walkers = 0, missing = 0, drawn = 0, outOfRange = 0;
        foreach (var e in _entities)
        {
            if (e.IsBuilding || e.IsProp || e.Chassis < 0) continue;
            drawn++;
            int pose = PoseOf(e);
            if (pose > 0) used[pose] = used.GetValueOrDefault(pose) + 1;
            if (e.Chassis == Import.UnitsExporter.WalkerChassis) walkers++;
            // a record that names a group its chassis does not own — counted,
            // not hidden: it is the maps' own inconsistency, and it falls back
            // to group 0 rather than reaching into the next part's frames
            if (e.Pose > 0 && pose == 0) outOfRange++;
            else if (pose > 0 && LoadUnitPart("hull", $"{e.UnitType}/g{pose}", e.Facing) == null
                              && LoadUnitPart("hull", $"{e.UnitType}/g{pose}", 0) == null)
                missing++;
        }
        var parts = new List<string>();
        foreach (var kv in used) parts.Add($"g{kv.Key}:{kv.Value}");
        return $"poses: {drawn} Einheiten, {used.Count} Gruppen ausser 0 " +
               $"({(parts.Count > 0 ? string.Join(" ", parts) : "keine")}), " +
               $"{missing} ohne Bild, {outOfRange} ausserhalb der Gruppen ihres Fahrwerks; " +
               $"{walkers} Laeufer (Turm +{Import.UnitsExporter.WalkerLift} px)";
    }

    /// <summary>Runs the voice rule over every unit on this map and reports how
    /// many of them can speak and whether any of the numbers it produces is
    /// missing from the bank. A scripted run cannot click a unit, so this is how
    /// the rule stays checked: it exercises exactly what a click would.</summary>
    public string VoiceWatchLine()
    {
        Audio.SoundBankPlayer.Load();
        var have = Audio.SoundBankPlayer.Index;
        int units = 0, speak = 0, missing = 0, outside = 0;
        var seen = new HashSet<int>();
        foreach (var e in _entities)
        {
            if (e.IsBuilding || e.IsProp || e.Chassis < 0) continue;
            units++;
            // every draw the rule can make, not just one throw, and the hit line
            // beside it — both rules are checked by the same pass
            for (int t = 0; t < 24; t++)
            {
                int s = t == 0
                    ? Audio.GameSounds.HitVoice(e.Subclass, e.Chassis, e.Field28)
                    : Audio.GameSounds.Voice(e.Subclass, e.Chassis, e.Field28);
                if (s < 0) continue;
                if (t == 0) speak++;
                if (!seen.Add(s)) continue;
                if (s < Audio.GameSounds.AnnounceFirst || s > Audio.GameSounds.AnnounceLast) outside++;
                if (have.Count > 0 && !have.ContainsKey(s)) missing++;
            }
        }
        return $"voices: {speak} von {units} Einheiten sprechen, {seen.Count} verschiedene Klaenge, " +
               $"{missing} nicht in der Bank, {outside} ausserhalb 150..253";
    }

    public Vector2? DebugDemoOrder(bool naval = false)
    {
        if (_nav == null) return null;
        var want = naval ? Simulation.NavGrid.MoveClass.Ship : Simulation.NavGrid.MoveClass.Vehicle;
        // skip units that are stranded on terrain of the wrong domain (a few land
        // vehicles are placed out at sea — presumably loaded onto transports)
        int seed = _entities.FindIndex(e => e.Mobile && e.Move == want &&
                                            _nav.IsWalkable(e.Col, e.Row, want));
        if (seed < 0) return null;
        var s = _entities[seed];

        _sel.Clear();
        for (int i = 0; i < _entities.Count; i++)
        {
            var e = _entities[i];
            if (e.Mobile && e.Move == want &&
                Mathf.Abs(e.Col - s.Col) <= 8 && Mathf.Abs(e.Row - s.Row) <= 8)
                _sel.Add(i);
        }
        SetPrimary();

        var start = new Vector2I(s.Col, s.Row);
        var offsets = new List<Vector2I>();
        foreach (int dist in new[] { 10, 7, 5, 3 })
            foreach (var d in new[] { new Vector2I(0, dist), new Vector2I(dist, 0),
                                      new Vector2I(0, -dist), new Vector2I(-dist, 0),
                                      new Vector2I(dist, dist), new Vector2I(-dist, dist) })
                offsets.Add(d);
        foreach (var d in offsets)
        {
            var c = new Vector2I(start.X + d.X, start.Y + d.Y);
            if (!_nav.IsFree(c.X, c.Y, s.Move)) continue;
            if (_nav.FindPath(start, c, s.Move) == null) continue;
            IssueMove(CellCenter(c.X, c.Y));
            GD.Print($"demo: {_sel.Count} units, {start} -> {c}  [{_order}]");
            return (CellCenter(start.X, start.Y) + CellCenter(c.X, c.Y)) / 2f;
        }
        GD.Print("demo: no reachable target near the first mobile unit");
        return CellCenter(start.X, start.Y);
    }

    // ---- per-frame movement ----

    /// <summary>Accumulated simulation time and tick count (preview harness).</summary>
    public double DebugClock { get; private set; }
    public int DebugTicks { get; private set; }
    public int DebugShots { get; private set; }

    /// <summary>One-line combat state for the preview harness.</summary>
    private int _buildWatch = -1;
    private bool _buildPending;

    public string DebugCombatInfo()
    {
        var sb = new System.Text.StringBuilder();
        int inf = 0, armed = 0, engaged = 0, walking = 0, infDead = 0;
        foreach (var e in _entities)
        {
            if (e.Infantry < 0) continue;
            inf++;
            if (e.Weapon != 0) armed++;
            if (e.Target >= 0) engaged++;
            if (e.Path != null) walking++;
            if (e.Dead) infDead++;
        }
        if (inf > 0)
        {
            sb.Append($"inf {inf} (armed {armed}, engaged {engaged}, walking {walking}, " +
                      $"dead {infDead})");
            foreach (var e in _entities)
            {
                if (e.Infantry < 0 || !e.Dead) continue;
                sb.Append($" corpse[set {e.Infantry} f{e.Facing} block {InfBlock(e)} " +
                          $"t={e.DeadTime:0.0}s tex={(GetInfantryTexture(e.Infantry, e.Facing, InfBlock(e)) != null ? "ok" : "MISSING")}]");
                break;
            }
            sb.Append(" || ");
        }
        sb.Append(NetworkLine()).Append(" || ").Append(StandingsLine()).Append(" || ");
        string ml = MissionLine();
        if (ml.Length > 0) sb.Append($"P{ViewPlayer} {ml} || ");
        if (LastEvent() is { } ev)
            sb.Append($"letztes Ereignis: {ev.What} bei ({ev.Pos.X / TileW:0},{ev.Pos.Y / TileH:0})" +
                      $" [Tab, {_events.Count} gemerkt] || ");
        string sw = SupplyWatchLine();
        if (sw.Length > 0) sb.Append(sw).Append(" || ");
        string shl = ShipWatchLine();
        if (shl.Length > 0) sb.Append(shl).Append(" || ");
        string twl = TrainWatchLine();
        if (twl.Length > 0) sb.Append(twl).Append(" || ");
        string ail = AiLine();
        if (ail.Length > 0) sb.Append(ail).Append(" || ");
        if (_researchWatch >= 0 && _researchWatch < _entities.Count)
        {
            var rb = _entities[_researchWatch];
            sb.Append($"research tech={rb.ResearchTech} done={rb.ResearchDone}/{ResearchTotal} " +
                      $"known={_researchedStatic.Count} spezial-menu={BuildableBy(4).Count} " +
                      $"money={_money[Mathf.Clamp(rb.Owner, 0, 7)]} || ");
        }
        if (_stateWatch != null)
        {
            foreach (int wi in _stateWatch)
            {
                if (wi < 0 || wi >= _entities.Count) continue;
                var w = _entities[wi];
                sb.Append($"[{w.Name} {BuildingTypeName(w.BType)} status={w.State}:{StateName(w)} " +
                          $"hp={w.Hp}/{w.HpMax} step={w.UpgradeStep} cap={w.Capacity} " +
                          $"V={w.ProdSpeed} kosten={w.CostStore}/{w.CostProd} " +
                          $"T={w.StockT} parts={OwnParts(w)}] ");
            }
            sb.Append("|| ");
        }
        if (_buildWatch >= 0 && _buildWatch < _entities.Count)
        {
            var f = _entities[_buildWatch];
            sb.Append($"factory {f.Name} T={f.StockT} W={f.StockW} F={f.StockF} S={f.StockS} " +
                      $"build={f.BuildTime:0.0}s | units={_entities.Count}");
            var hq = NearestOwned(f, x => x.BType == 1);
            if (hq != null)
                sb.Append($" | HQ {hq.Name} W={hq.StockW} F={hq.StockF} S={hq.StockS} T={hq.StockT}");
            var mine = _entities.Find(x => x.IsBuilding && x.Deposit >= 0 && x.Owner == f.Owner);
            if (mine != null)
                sb.Append($" | mine dep={mine.Deposit} T={mine.StockT}");
            sb.Append(" || ");
        }
        sb.Append(PanelWatchLine()).Append(" || ");
        sb.Append($"shots={DebugShots} effects={_effects.Count} tracers={_tracers.Count} " +
                  $"rockets={_shots.Count}");
        if (_shots.Count > 0)
            sb.Append($" first@({_shots[0].Pos.X:0},{_shots[0].Pos.Y:0})->({_shots[0].Aim.X:0},{_shots[0].Aim.Y:0}) f{_shots[0].Facing}");
        foreach (int i in _sel)
        {
            var e = _entities[i];
            sb.Append($" | P{e.Owner} slot {e.Slot} hull={e.Facing} aim={e.AimFacing} " +
                      $"cd={e.Cooldown:0.00} mun={e.Ammo}/{e.AmmoMax} target={e.Target}");
            if (e.Target >= 0)
            {
                var t = _entities[e.Target];
                sb.Append($" (slot {t.Slot} hp {t.Hp}/{t.HpMax}, {CellDistance(e, t):0.0} tiles, " +
                          $"dead={t.Dead})");
            }
        }
        return sb.ToString();
    }

    public override void _Process(double delta)
    {
        if (_nav == null) return;
        float dt = (float)delta;
        _clock += dt;

        // ours: when a piece has run out, put the next one on
        _musicTick += dt;
        if (_musicTick >= 2f) { _musicTick = 0; Audio.MidiMusic.Poll(); }

        // the original's "unexplored" step, on its own slower beat
        _fogTick += dt;
        if (_fogTick >= FogEverySec)
        {
            _fogTick = 0;
            UpdateFog();
            QueueRedraw();
        }
        UpdateAircraft(dt);
        if (_orderMarks.Count > 0) { UpdateOrderMarks(dt); QueueRedraw(); }
        DebugClock += delta;
        DebugTicks++;
        bool moved = false;

        _acquireTimer -= dt;
        if (_acquireTimer <= 0f) { _acquireTimer = 0.4f; AutoAcquire(); }

        // neutral units join whoever drives up to them — see Simulation/Takeover.cs
        _takeoverTimer -= dt;
        if (_takeoverTimer <= 0f) { _takeoverTimer = TakeoverEverySec; TakeoverTick(); }

        PollBuildPanelDemo();

        // preview harness: start the scripted build as soon as the factory has
        // manufactured enough parts
        if (_buildPending && _buildWatch >= 0 && _buildWatch < _entities.Count &&
            CanAffordMenuChoice(_entities[_buildWatch]))
        {
            ProduceFromSelection();
            _buildPending = false;
        }

        for (int i = 0; i < _entities.Count; i++)
        {
            var e = _entities[i];
            if (e.IsProp) continue;
            if (e.Dead) { e.DeadTime += dt; continue; }
            if (e.IsBuilding) { UpdateProduction(i, e, dt); continue; }

            UpdateCombat(i, e, dt);

            if (e.Path == null || e.PathIdx >= e.Path.Count) continue;

            if (e.Reserved == null)
            {
                var next = e.Path[e.PathIdx];
                if (!_nav.IsFree(next.X, next.Y, e.Move, i))
                {
                    // someone is in the way: wait briefly, then look for a new route
                    e.WaitTime += dt;
                    if (e.WaitTime > 0.7f)
                    {
                        e.WaitTime = 0;
                        var repath = _nav.FindPath(new Vector2I(e.Col, e.Row), e.Goal, e.Move, i);
                        if (repath == null || repath.Count == 0) e.Path = null;
                        else { e.Path = repath; e.PathIdx = 0; }
                    }
                    continue;
                }
                // a foot soldier in the target cell does not stop the move; the
                // original either drives through him (`pratelska_infa`
                // @0x433fe0, all friendly) or over him (`prejet` @0x412980,
                // none friendly, and @0x412a50 clears the cell afterwards)
                int foot = _nav.CrushableAt(next.X, next.Y, i);
                if (foot >= 0) RunOverFoot(i, e, foot);

                _nav.SetOccupant(next.X, next.Y, i, e.Infantry >= 0);
                e.Reserved = next;
                e.WaitTime = 0;
            }

            var target = e.Reserved.Value;
            Vector2 dest = CellCenter(target.X, target.Y);
            Vector2 d = dest - e.Pos;
            float dist = d.Length();
            // same factor the path cost uses, so sand really is slower on screen
            float step = SpeedOf(e) * dt / _nav.TerrainCost(target.X, target.Y, e.Move);
            moved = true;

            if (dist > 0.01f) e.Facing = DirToFacing(d);

            if (dist <= step)
            {
                _nav.ClearOccupant(e.Col, e.Row, i);
                e.Pos = dest;
                e.Col = target.X;
                e.Row = target.Y;
                e.Elev = ElevOf(e.Col, e.Row);
                e.Footprint = CellRect(_ox, _oy, e.Col, e.Row, e.Elev);
                e.Reserved = null;
                e.PathIdx++;
                if (e.PathIdx >= e.Path.Count)
                {
                    e.Path = null;
                    NextQueued(i, e);      // one waypoint done: take the next order
                }
                // One unit of fuel per SQUARE ENTERED — confirmed: the move
                // code prints "on square" (@0x4f6ba4) and then decrements
                // +0x2e (@0x407aa7); at zero it prints "no fuel" and stops the
                // unit where it stands. The three equipment types that branch
                // off first (0x44 Mine Remover, 0x45 Trap Remover, 0xc1) rejoin
                // the same path, so they pay too.
                if (CheatFuel && Cheated(e)) { }   // Tank bleibt voll
                else if (e.FuelMax > 0 && e.Fuel > 0 && --e.Fuel == 0)
                {
                    e.Path = null;
                    e.Target = -1;
                    _order = $"slot {e.Slot}: kein Sprit";
                }
            }
            else
            {
                e.Pos += d / dist * step;
            }
        }

        UpdateProjectiles(dt);
        UpdateEffects(dt);
        UpdateFreight(dt);          // das Bahnsystem — Simulation/RailFreight.cs
        RailMoveWagons();
        UpdateTrains(dt);
        UpdateAi(dt);
        MissionScriptTick(dt);

        if (moved || _effects.Count > 0 || _tracers.Count > 0 || _shots.Count > 0)
        {
            QueueRedraw();
            if (_selected >= 0) UpdatePanel();
        }
    }

    private static int GetI(GDict d, string k, int def = 0)
        => d.TryGetValue(k, out var v) && v.VariantType != Variant.Type.Nil ? v.AsInt32() : def;

    /// <summary>Value of the byte at record offset i, read from a hex string.</summary>
    private static int HexByte(string hex, int i)
    {
        int p = i * 2;
        if (p + 2 > hex.Length) return 0;
        return System.Convert.ToInt32(hex.Substring(p, 2), 16);
    }

    /// <summary>Little-endian u16 out of the raw record.</summary>
    private static int Hex16(string hex, int i) => HexByte(hex, i) | (HexByte(hex, i + 1) << 8);

    private static void LoadCatalog()
    {
        if (_catalog != null) return;
        _catalog = new Dictionary<int, (string, string)>();
        string path = Core.Content.Path("Maps/unit_catalog.json");
        if (!FileAccess.FileExists(path)) return;
        using var f = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        if (f == null) return;
        var json = new Json();
        if (json.Parse(f.GetAsText()) != Error.Ok || json.Data.VariantType != Variant.Type.Dictionary)
            return;
        var root = json.Data.AsGodotDictionary<string, Variant>();
        if (!root.TryGetValue("units", out var uv) || uv.VariantType != Variant.Type.Dictionary)
            return;
        foreach (var kv in uv.AsGodotDictionary<string, Variant>())
        {
            if (kv.Value.VariantType != Variant.Type.Dictionary) continue;
            var u = kv.Value.AsGodotDictionary<string, Variant>();
            if (!u.TryGetValue("unit_type", out var utv)) continue;
            string tier = u.TryGetValue("tier", out var tv) ? tv.AsString() : "";
            string name = u.TryGetValue("name", out var nv) ? nv.AsString() : "";
            _catalog[utv.AsInt32()] = (tier, name);
            if (u.TryGetValue("speed_raw", out var sv))
                _speeds[utv.AsInt32()] = sv.AsInt32();
            if (u.TryGetValue("hp_max", out var hv) && hv.AsInt32() > 0)
                _catalogHp[utv.AsInt32()] = hv.AsInt32();
        }
    }

    /// <summary>Best label for a unit_type: real name if recovered, else tier.</summary>
    private static string LabelOf(int unitType)
    {
        if (_catalog != null && _catalog.TryGetValue(unitType, out var e))
            return !string.IsNullOrEmpty(e.Name) ? e.Name
                 : !string.IsNullOrEmpty(e.Tier) ? e.Tier : "?";
        return "?";
    }

    /// <summary>Human-readable imap ground class (Can_go @0x4055D0).</summary>
    private static string TerrainName(Simulation.NavGrid.Ground g) => g switch
    {
        Simulation.NavGrid.Ground.Water => "Wasser (Schiff/Hover)",
        Simulation.NavGrid.Ground.Rough => "grob (Fuss/Hover)",
        Simulation.NavGrid.Ground.Blocked => "gesperrt",
        _ => "frei",
    };

    private static Color OwnerColor(int owner)
        => owner >= 0 && owner < Factions.Length ? Factions[owner] : PropColor;

    private static void LoadUnitIndex()
    {
        if (_unitAnchors != null) return;
        _unitAnchors = new Dictionary<int, Dictionary<int, (int, int, int)>>();
        string path = Core.Content.Path("Units/units_index.json");
        if (!FileAccess.FileExists(path)) return;
        // which root the sprites come from — the imported content or the tree
        GD.Print($"units: {path}");
        using var f = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        if (f == null) return;
        var json = new Json();
        if (json.Parse(f.GetAsText()) != Error.Ok || json.Data.VariantType != Variant.Type.Dictionary)
            return;
        var root = json.Data.AsGodotDictionary<string, Variant>();
        if (!root.TryGetValue("units", out var uv) || uv.VariantType != Variant.Type.Dictionary)
            return;
        foreach (var kv in uv.AsGodotDictionary<string, Variant>())
        {
            if (!int.TryParse(kv.Key, out int ut) || kv.Value.VariantType != Variant.Type.Dictionary)
                continue;
            var u = kv.Value.AsGodotDictionary<string, Variant>();
            if (!u.TryGetValue("facings", out var fv) || fv.VariantType != Variant.Type.Dictionary)
                continue;
            var fac = new Dictionary<int, (int, int, int)>();
            foreach (var fk in fv.AsGodotDictionary<string, Variant>())
            {
                if (!int.TryParse(fk.Key, out int fa) || fk.Value.VariantType != Variant.Type.Dictionary)
                    continue;
                var fd = fk.Value.AsGodotDictionary<string, Variant>();
                fac[fa] = (GetI(fd, "w"), GetI(fd, "h"), GetI(fd, "yoff"));
            }
            _unitAnchors[ut] = fac;
        }
    }

    private readonly Dictionary<(int, int, int), Texture2D?> _infTex = new();

    // Read off the rendered blocks of a set (the side view makes it obvious):
    // 0..7 walk, 9..10 fire, 11 standing, 12..14 falling over.
    private const int InfIdleBlock = 11;
    private static readonly int[] InfFireBlocks = { 9, 10 };
    private static readonly int[] InfDeathBlocks = { 12, 13, 14 };
    private const float InfWalkFps = 9f, InfFireFps = 7f;

    /// <summary>Which animation block a foot soldier is showing right now.</summary>
    private const float InfDeathFps = 5f;

    /// <summary>
    /// `--inf-anim-check` — does a walking foot soldier actually cycle through
    /// its eight blocks?
    ///
    /// <para>The reading of the infantry blocks (0..7 walk cycle, 9..10 fire, 11
    /// stand, 12..14 fall/die/corpse) has been settled since 07.08. and
    /// <see cref="InfBlock"/> looks right on paper. What was never checked is
    /// whether it does anything <b>in a running game</b>: the walk branch hangs
    /// on <c>e.Path != null</c>, and if the path is cleared earlier than assumed
    /// the soldier stands still while it moves. That is the open half of
    /// Fehlerliste Punkt 2, and looking at it was not enough — a foot soldier is
    /// twenty pixels tall.</para>
    ///
    /// <para>So this samples instead: every call, for every foot soldier, it
    /// records the block and whether a path was set. Over a run one then reads
    /// off how many <b>distinct</b> blocks each soldier showed while walking.
    /// Eight means the cycle runs; one means it is frozen.</para>
    /// </summary>
    public void InfAnimSample()
    {
        // The check has to make them WALK itself. `--demo-inf` sends the
        // soldiers into a firefight, which is a different branch of InfBlock,
        // and the first run of this measured 27 soldiers with zero paths.
        if (!_infWalkOrdered)
        {
            _infWalkOrdered = true;
            _sel.Clear();
            for (int i = 0; i < _entities.Count; i++)
            {
                var e = _entities[i];
                if (e.Infantry < 0 || e.Dead || e.Owner != ViewPlayer) continue;
                e.Target = -1;                       // drop any fire order
                _sel.Add(i);
            }
            _infOrdered = _sel.Count;
            if (_sel.Count > 0 && _nav != null)
            {
                int fi = -1;
                foreach (int k in _sel) { fi = k; break; }
                var f = _entities[fi];
                // ⚠ Do not just pick a cell twelve away and hope: on map_05 that
                // lands in the water and every soldier reports "no route", which
                // looks exactly like a broken walk cycle. Ask the grid first and
                // take the first goal that really has a path.
                var dirs = new[] { (12, 0), (-12, 0), (0, 12), (0, -12),
                                   (8, 8), (-8, -8), (8, -8), (-8, 8) };
                foreach (var (dc, dr) in dirs)
                {
                    var goal = new Vector2I(Mathf.Clamp(f.Col + dc, 0, _nav.Width - 1),
                                            Mathf.Clamp(f.Row + dr, 0, _nav.Height - 1));
                    if (_nav.FindPath(new Vector2I(f.Col, f.Row), goal, f.Move, fi) == null) continue;
                    IssueMove(new Vector2(goal.X * Import.MapBaker.TileW,
                                          goal.Y * Import.MapBaker.TileH));
                    _infOrderNote = $"Ziel ({goal.X},{goal.Y}) — {_order}";
                    break;
                }
                if (_infOrderNote.Length == 0)
                    _infOrderNote = "kein erreichbares Ziel in acht Richtungen gefunden";
            }
            else
            {
                // say WHY nobody was ordered instead of reporting a silent zero
                var owners = new SortedDictionary<int, int>();
                foreach (var e in _entities)
                    if (e.Infantry >= 0 && !e.Dead)
                        owners[e.Owner] = owners.GetValueOrDefault(e.Owner) + 1;
                _infOrderNote = $"niemand ausgewaehlt — betrachteter Spieler {ViewPlayer}, " +
                    "Infanterie gehoert " +
                    string.Join(", ", System.Linq.Enumerable.Select(owners,
                        kv => $"{kv.Value}x Spieler {kv.Key}"));
            }
        }

        foreach (var e in _entities)
        {
            if (e.Infantry < 0 || e.Dead) continue;
            if (!_infSeen.TryGetValue(e.Slot, out var s))
                s = _infSeen[e.Slot] = new InfAnimTally();
            s.Samples++;
            if (e.Path != null)
            {
                s.Walking++;
                s.WalkBlocks.Add(InfBlock(e));
            }
            else s.Standing++;
        }
    }

    private sealed class InfAnimTally
    {
        public int Samples, Walking, Standing;
        public readonly SortedSet<int> WalkBlocks = new();
    }

    private readonly Dictionary<int, InfAnimTally> _infSeen = new();
    private bool _infWalkOrdered;
    private int _infOrdered;
    private string _infOrderNote = "";

    public string InfAnimReport()
    {
        if (_infSeen.Count == 0) return "inf-anim-check: keine Infanterie abgetastet";
        var sb = new System.Text.StringBuilder();
        int moved = 0, cycled = 0, frozen = 0;
        foreach (var kv in _infSeen)
        {
            var s = kv.Value;
            if (s.Walking == 0) continue;
            moved++;
            if (s.WalkBlocks.Count >= 8) cycled++;
            else if (s.WalkBlocks.Count <= 1) frozen++;
        }
        sb.Append($"inf-anim-check: {_infSeen.Count} Infanteristen abgetastet, ");
        sb.Append($"{_infOrdered} in Marsch gesetzt, ");
        sb.Append($"{moved} davon mit Pfad — {cycled} zeigen alle 8 Laufbilder, ");
        sb.Append($"{frozen} stehen auf einem einzigen fest");
        if (_infOrderNote.Length > 0) sb.Append($"\n   Marschbefehl: {_infOrderNote}");
        int shown = 0;
        foreach (var kv in _infSeen)
        {
            var s = kv.Value;
            if (s.Walking == 0 || shown++ >= 6) continue;
            sb.Append($"\n   slot {kv.Key}: {s.Walking} Proben mit Pfad, {s.Standing} ohne, " +
                      $"Bloecke [{string.Join(",", s.WalkBlocks)}]");
        }
        return sb.ToString();
    }

    // ---- `--veh-anim-check`: the vehicle counterpart -------------------------

    /// <summary>Does a driving Mech / Spinne really step through its groups —
    /// and does a different PICTURE come out of it?
    ///
    /// <para>Modelled on <see cref="InfAnimSample"/>, which settled the
    /// infantry half of Fehlerliste Punkt 2. Two things it must catch that a
    /// pose-only count would miss, and both are named in the report:</para>
    /// <list type="bullet">
    /// <item>the pose changes but the group's file is missing, so
    /// <see cref="GetHullTexture"/> falls back to group 0 and the unit still
    /// stands still — counted as <c>Rückfall</c>;</item>
    /// <item>the picture changes only because the unit TURNED. Distinct
    /// textures are therefore counted per facing, never across facings.</item>
    /// </list>
    /// <para>What it cannot see: whether the stride matches the ground speed.
    /// That is a matter of taste and the cadence is ours anyway.</para>
    /// </summary>
    public void VehAnimSample()
    {
        if (!_vehWalkOrdered)
        {
            _vehWalkOrdered = true;
            _sel.Clear();
            for (int i = 0; i < _entities.Count; i++)
            {
                var e = _entities[i];
                if (e.IsBuilding || e.IsProp || e.Dead || e.Infantry >= 0) continue;
                if (!e.Mobile || e.Owner != ViewPlayer) continue;
                if (GaitPhases(e.UnitType) <= 1) continue;   // no gait, nothing to see
                e.Target = -1;                               // drop any fire order
                _sel.Add(i);
            }
            _vehOrdered = _sel.Count;
            if (_sel.Count > 0 && _nav != null)
            {
                // same trap as with the soldiers: a goal "twelve cells on" can
                // sit in the water, every unit reports "no route", and a frozen
                // pose looks exactly like a broken cycle. Ask the grid first —
                // and ⚠ ask it for EVERY selected unit, not only the first: on
                // map_DM_4 the first one is boxed in and the whole run came back
                // "37 in Fahrt gesetzt, 0 davon mit Pfad".
                var dirs = new[] { (12, 0), (-12, 0), (0, 12), (0, -12),
                                   (8, 8), (-8, -8), (8, -8), (-8, 8),
                                   (5, 0), (-5, 0), (0, 5), (0, -5),
                                   (3, 3), (-3, -3), (3, -3), (-3, 3) };
                int tried = 0;
                foreach (int fi in _sel)
                {
                    var f = _entities[fi];
                    tried++;
                    foreach (var (dc, dr) in dirs)
                    {
                        var goal = new Vector2I(Mathf.Clamp(f.Col + dc, 0, _nav.Width - 1),
                                                Mathf.Clamp(f.Row + dr, 0, _nav.Height - 1));
                        if (_nav.FindPath(new Vector2I(f.Col, f.Row), goal, f.Move, fi) == null) continue;
                        // ⚠ CellCenter, not col*TileW: the map is isometric, and
                        // the naive product lands on a different cell — the
                        // first run said "Ziel gefunden" and "no route" in the
                        // same line because of it.
                        IssueMove(CellCenter(goal.X, goal.Y));
                        _vehOrderNote = $"Ziel ({goal.X},{goal.Y}) ab Einheit {tried} von " +
                                        $"{_sel.Count} — {_order}";
                        break;
                    }
                    if (_vehOrderNote.Length > 0) break;
                }
                if (_vehOrderNote.Length == 0)
                    _vehOrderNote = $"kein erreichbares Ziel — {_sel.Count} Einheiten " +
                                    "x 16 Richtungen ohne Weg";
            }
            else
            {
                var owners = new SortedDictionary<int, int>();
                foreach (var e in _entities)
                    if (!e.IsBuilding && !e.IsProp && !e.Dead && e.Infantry < 0
                        && GaitPhases(e.UnitType) > 1)
                        owners[e.Owner] = owners.GetValueOrDefault(e.Owner) + 1;
                _vehOrderNote = owners.Count == 0
                    ? "diese Karte traegt ueberhaupt kein Fahrwerk mit Gangart"
                    : $"niemand ausgewaehlt — betrachteter Spieler {ViewPlayer}, Gangart-Fahrwerke " +
                      string.Join(", ", System.Linq.Enumerable.Select(owners,
                          kv => $"{kv.Value}x Spieler {kv.Key}"));
            }
        }

        foreach (var e in _entities)
        {
            if (e.IsBuilding || e.IsProp || e.Dead || e.Infantry >= 0) continue;
            if (GaitPhases(e.UnitType) <= 1) continue;
            if (!_vehSeen.TryGetValue(e.Slot, out var s))
            {
                s = _vehSeen[e.Slot] = new VehAnimTally();
                s.UnitType = e.UnitType;
                s.Gait = GaitPhases(e.UnitType);
            }
            s.Samples++;
            if (e.Path == null) { s.Standing++; continue; }
            s.Walking++;
            int pose = PoseOf(e);
            s.Poses.Add(pose);
            // the picture the draw would really put down — and whether it is the
            // group's own or the group-0 fallback
            string dir = pose > 0 ? $"{e.UnitType}/g{pose}" : e.UnitType.ToString();
            var own = LoadUnitPart("hull", dir, e.Facing) ?? LoadUnitPart("hull", dir, 0);
            if (own == null) s.Fallback++;
            else s.TexPerFacing.Add((e.Facing, own.GetInstanceId()));
            s.Facings.Add(e.Facing);
        }
    }

    private sealed class VehAnimTally
    {
        public int Samples, Walking, Standing, Fallback, UnitType, Gait;
        public readonly SortedSet<int> Poses = new();
        public readonly SortedSet<int> Facings = new();
        public readonly HashSet<(int, ulong)> TexPerFacing = new();
        /// <summary>Distinct pictures at the busiest single facing — the number
        /// that cannot be faked by turning.</summary>
        public int PicturesAtOneFacing()
        {
            int best = 0;
            foreach (int f in Facings)
            {
                int n = 0;
                foreach (var (ff, _) in TexPerFacing) if (ff == f) n++;
                if (n > best) best = n;
            }
            return best;
        }
    }

    private readonly Dictionary<int, VehAnimTally> _vehSeen = new();
    private bool _vehWalkOrdered;
    private int _vehOrdered;
    private string _vehOrderNote = "";

    public string VehAnimReport()
    {
        var sb = new System.Text.StringBuilder();
        // say what the map even offers, so a green zero cannot be mistaken for a pass
        var onMap = new SortedDictionary<int, int>();
        foreach (var e in _entities)
            if (!e.IsBuilding && !e.IsProp && e.Infantry < 0 && GaitPhases(e.UnitType) > 1)
                onMap[e.UnitType] = onMap.GetValueOrDefault(e.UnitType) + 1;
        var kinds = new List<string>();
        foreach (var kv in onMap) kinds.Add($"{kv.Key}x{kv.Value} ({GaitPhases(kv.Key)} Phasen)");
        if (_vehSeen.Count == 0)
            return "veh-anim-check: kein Fahrzeug mit Gangart abgetastet — auf der Karte: " +
                   (kinds.Count > 0 ? string.Join(", ", kinds) : "keins") +
                   (_vehOrderNote.Length > 0 ? $"\n   Fahrbefehl: {_vehOrderNote}" : "");

        int moved = 0, cycled = 0, frozen = 0, fellBack = 0;
        foreach (var kv in _vehSeen)
        {
            var s = kv.Value;
            if (s.Walking == 0) continue;
            moved++;
            if (s.Poses.Count >= s.Gait && s.PicturesAtOneFacing() >= 2) cycled++;
            else if (s.Poses.Count <= 1) frozen++;
            if (s.Fallback > 0) fellBack++;
        }
        sb.Append($"veh-anim-check: {_vehSeen.Count} Fahrzeuge mit Gangart abgetastet, ");
        sb.Append($"{_vehOrdered} in Fahrt gesetzt, ");
        sb.Append($"{moved} davon mit Pfad — {cycled} zeigen alle Laufbilder ihres Fahrwerks, ");
        sb.Append($"{frozen} stehen auf einem einzigen fest, {fellBack} mit Rueckfall auf Gruppe 0");
        sb.Append($"\n   auf der Karte: {(kinds.Count > 0 ? string.Join(", ", kinds) : "keins")}");
        if (_vehOrderNote.Length > 0) sb.Append($"\n   Fahrbefehl: {_vehOrderNote}");
        int shown = 0;
        foreach (var kv in _vehSeen)
        {
            var s = kv.Value;
            if (s.Walking == 0 || shown++ >= 8) continue;
            sb.Append($"\n   slot {kv.Key}: Fahrwerk {s.UnitType}, {s.Gait} Phasen, " +
                      $"{s.Walking} Proben mit Pfad, {s.Standing} ohne, " +
                      $"Gruppen [{string.Join(",", s.Poses)}], " +
                      $"{s.PicturesAtOneFacing()} verschiedene Bilder bei EINER Richtung, " +
                      $"{s.Fallback} Rueckfaelle");
        }
        return sb.ToString();
    }

    private int InfBlock(Entity e)
    {
        if (e.Dead)   // fall over once, then lie there
            return InfDeathBlocks[Mathf.Min((int)(e.DeadTime * InfDeathFps),
                                            InfDeathBlocks.Length - 1)];
        // ⚠ 11.08.2026 — hier stand nur `e.Target >= 0`, und damit zeigte ein
        // Fusssoldat die SCHUSSPOSE schon auf dem ganzen Anmarsch: gemeldet als
        // »dauerhaftes Feuer-Sprite vor der Waffe, obwohl sie noch gar nicht
        // schiessen«. Ein Ziel zu HABEN heisst nicht, darauf zu feuern -- das
        // tut sie erst in Reichweite. `Fire()` setzt darum jetzt eine kurze
        // Frist, und nur solange die laeuft, wird die Pose gezeigt.
        if (e.Weapon != 0 && _clock < e.FireUntil)
            return InfFireBlocks[(int)(_clock * InfFireFps) % InfFireBlocks.Length];
        if (e.Path != null)                       // walking: the eight-step cycle
            return (int)(_clock * InfWalkFps + e.Slot) % 8;
        return InfIdleBlock;
    }

    /// <summary>Wie lange ein Schuss die Pose haelt. UNSERE Setzung.</summary>
    private const float FirePoseSeconds = 0.5f;

    /// <summary>Ab dieser Waffenzeile sind es Handwaffen (siehe TurretOf, das
    /// nur 1..19 abbildet und 185..199 als Infanteriewaffen kennt).</summary>
    private const int InfantryWeaponFirst = 185;

    private float _clock;
    private float _musicTick;

    /// <summary>One of the 24 infantry sets from ROBO.CWR's aux table.</summary>
    private Texture2D? GetInfantryTexture(int set, int facing, int block = InfIdleBlock)
    {
        var key = (set, facing, block);
        if (_infTex.TryGetValue(key, out var cached)) return cached;
        string path = Core.Content.Path($"Units/infantry/{set}/f{facing}_b{block}.png");
        Texture2D? tex = ResourceLoader.Exists(path) ? ResourceLoader.Load<Texture2D>(path) : null;
        if (tex == null && FileAccess.FileExists(path))
        {
            var img = Image.LoadFromFile(path);
            if (img != null) tex = ImageTexture.CreateFromImage(img);
        }
        _infTex[key] = tex;
        return tex;
    }

    private Texture2D? GetUnitTexture(int unitType, int facing)
    {
        var key = (unitType, facing);
        if (_unitTex.TryGetValue(key, out var cached)) return cached;
        string path = Core.Content.Path($"Units/{unitType}/f{facing}.png");
        Texture2D? tex = ResourceLoader.Exists(path) ? ResourceLoader.Load<Texture2D>(path) : null;
        if (tex == null && FileAccess.FileExists(path))
        {
            var img = Image.LoadFromFile(path);
            if (img != null) tex = ImageTexture.CreateFromImage(img);
        }
        _unitTex[key] = tex;
        return tex;
    }

    // Composed full-unit sprites (propulsion+body+weapon) on a fixed 64x56 canvas.
    // Anchor: the unit's ground-center within that canvas (tuned in the preview).
    private readonly Dictionary<(string, int), Texture2D?> _composedTex = new();
    // Measured from the composed sprites: chassis ground-center sits at canvas
    // (30, 55) across all facings (bottom-y std 0.4px) -> pin it to the cell center.
    private static readonly Vector2 ComposedAnchor = new(30, 55);

    private Texture2D? GetComposedTexture(string combo, int facing)
        => LoadUnitPart("composed", combo, facing);

    /// <summary>
    /// Hull (the propulsion alone) and turret (weapon) are exported on the
    /// same 64x56 anchor, so the turret can aim at its target while the hull
    /// faces its direction of travel — the way the original draw dispatch
    /// resolves each component's facing on its own.
    /// </summary>
    /// <summary>The hull picture for a chassis and a facing.
    ///
    /// Some chassis have only ONE direction in the sprite bank. Component 9
    /// (unit_type 168) is the clear case: every block holds a picture at
    /// `base + block*8 + 0` and 0xFFFFFFFF in the other seven slots, and the
    /// game agrees from the other side — all 40 of that type placed across the
    /// 44 maps carry facing 0 and no other. So the missing seven are not a gap
    /// in the export; the chassis simply has no directions.
    ///
    /// It matters here because a unit in this remake turns as it drives: a
    /// directionless chassis would vanish the moment it faced anything but 0.
    /// Falling back to facing 0 draws what the bank has, which is what the
    /// original draws too — it never turns these at all.</summary>
    /// <para>`pose` is the chassis' frame group out of entity +0x11 — see
    /// <see cref="Entity.Pose"/>. Group 0 keeps the old file name, the rest sit
    /// in g&lt;n&gt;/ beside it; a chassis that owns one group ignores it.</para>
    private Texture2D? GetHullTexture(int unitType, int facing, int pose = 0)
    {
        string ut = unitType.ToString();
        string dir = pose > 0 ? $"{ut}/g{pose}" : ut;
        return LoadUnitPart("hull", dir, facing)
               ?? (facing != 0 ? LoadUnitPart("hull", dir, 0) : null)
               // a pose the export does not carry falls back to group 0 rather
               // than making the unit disappear
               ?? (pose > 0 ? GetHullTexture(unitType, facing) : null);
    }

    /// <summary>Equipment rows 65..88 of the stats table, read out of a .DM's
    /// own copy (sec46). Only the ones that occur on placed units are listed;
    /// the panel shows the name so the field is readable.</summary>
    private static string EquipName(int row) => row switch
    {
        0 => "keine",
        65 => "Teleporter", 66 => "Gas Sucker", 67 => "Mobile Radar",
        68 => "Mine Remover", 69 => "Trap Remover", 70 => "Repair Device",
        71 => "Transporter", 72 => "Building Construct", 73 => "Ground Constructor",
        74 => "Power Generator", 75 => "Radar Thrower", 76 => "Antimagnetic",
        77 => "Antiradar", 78 => "Terranium Finder", 79 => "Long-Range Target",
        81 => "Shield", 82 => "Kamikaze", 83 => "Mirror", 84 => "Illusion",
        85 => "Electronics", 86 => "AutoRepair", 87 => "Gashield", 88 => "Radar",
        _ => "?",
    };

    private Texture2D? GetTurretTexture(int weapon, int facing)
        => weapon == 0 ? null : LoadUnitPart("turret", weapon.ToString(), facing);

    // ---- where the turret sits on the hull ----------------------------------

    /// <summary>unit_type -> the five slope offsets of its chassis, exported
    /// from the player's own GAME.EXE — see
    /// <see cref="Import.ExeTables.TurretMountTable"/>.</summary>
    private static Dictionary<int, Vector2I[]>? _mount;

    /// <summary>unit_type -> how many pose groups its chassis owns, out of the
    /// same file. A record can name a group its chassis does not have — the
    /// maps put 1..7 in +0x11 on wheeled units whose part owns a single group —
    /// and the game's own mask would run into the NEXT part's frames. Bounded
    /// here instead, which is OUR safeguard and says so.</summary>
    private static readonly Dictionary<int, int> _poseGroups = new();

    /// <summary>How fast a walking chassis steps through its groups.
    ///
    /// <para><b>OURS.</b> The frames are the game's and the field is the game's
    /// (<see cref="Entity.Pose"/>), but no routine in either GAME.EXE advances
    /// +0x11 while a vehicle drives, so the original never plays them. The
    /// number is picked to match the foot soldiers, whose eight walk blocks run
    /// at <see cref="InfWalkFps"/> = 9 — a Läufer with eight groups therefore
    /// takes 0.89 s per stride, a Spinne with three 0.33 s, a tread cycle
    /// 0.22 s.</para></summary>
    private const float HullGaitFps = InfWalkFps;

    /// <summary>unit_type -> how many groups of its chassis form a GAIT, 1 = it
    /// has none and keeps its map pose.
    ///
    /// <para>Not every multi-group chassis walks. Counted out of the sprite
    /// bank, the occupancy of the 48 frames of a group is decisive:</para>
    /// <code>
    /// comp  1 Spinne          3 groups   8 facings in every one   -> gait
    /// comp  6 Schwere Ketten  2 groups   8 facings in every one   -> gait
    /// comp  9 Kugelroller     3 groups   1 facing  in every one   -> gait
    /// comp 17 Läufer          8 groups   8 facings in every one   -> gait
    /// comp 14 Abwehrstellung  8 groups   8 in group 0, 2 in 1..7  -> NOT a gait
    /// </code>
    /// <para>The Abwehrstellung's groups exist only for facings 3 and 7: they
    /// are the emplacement unfolding, which is why the game drives them with a
    /// counter of their own (@0x409855 pins the facing to 7 and steps +0x11
    /// from +0x15) and why all 150 placed ones carry facing 7 AND pose 7. So
    /// the rule is read off the data, not chosen: a chassis walks when every
    /// group covers the same directions as group 0.</para>
    ///
    /// <para>⚠ Measured on the EXPORT, not on the bank, and that needed one
    /// step more. <c>copy_units.py</c> fills the empty facings of a sparse
    /// group, so every group on disk has eight files and counting FILES sees
    /// [8,8,8,…] everywhere — the first cut of this let the Abwehrstellung
    /// unfold itself while driving. Counting DISTINCT pictures brings the
    /// bank's shape back exactly:</para>
    /// <code>
    /// 160 Spinne         [8,8,8]                   uniform -> 3 phases
    /// 165 Schwere Ketten [8,8]                     uniform -> 2 phases
    /// 168 Kugelroller    [2,2,2]                   uniform -> 3 phases
    /// 174 Läufer         [8,8,8,8,8,8,8,8]         uniform -> 8 phases
    /// 171 Abwehrstellung [8,2,2,2,2,2,2,2]         NOT uniform -> no gait
    /// </code>
    /// <para>Wanted from the import side (not touched here): parts_index.json
    /// could carry the per-group facing coverage straight out of ROBO.CWR's
    /// offset table, and this would read a number instead of hashing
    /// pictures.</para></summary>
    private readonly Dictionary<int, int> _gaitPhases = new();

    /// <summary>The land chassis, unit_type 160..175. ⚠ The gait must not leave
    /// them: the group arithmetic <c>base + facing + slope + group*48</c> lives
    /// in case 0 of the draw dispatch @0x429946, and the SHIPS are case 4 with
    /// an arithmetic of their own ("Wrong chassis of ship" @0x4fa86c). Their
    /// part-table rows are spaced <b>16</b> frames apart (4040, 4056, 4072,
    /// 4088 …), not 48, so <c>parts_index.json</c> reading "groups: 3" for
    /// unit_type 153 out of the same first-u16 rule is a misreading — g1/g2 of
    /// that hull hold the NEXT SHIPS' pictures. Measured before this bound was
    /// put in: on map_01 the check drove a 153 and it changed into two other
    /// boats. All 97 placed ships carry +0x11 = 0xFF ("no group"), which is the
    /// game saying the same thing from the other side.</summary>
    private static bool IsLandChassis(int unitType) => unitType >= 160 && unitType <= 175;

    private int GaitPhases(int unitType)
    {
        if (_gaitPhases.TryGetValue(unitType, out int cached)) return cached;
        LoadMounts();
        int groups = _poseGroups.TryGetValue(unitType, out int n) ? n : 1;
        int gait = 1;
        if (groups > 1 && IsLandChassis(unitType))
        {
            int cov0 = PicturesOfGroup(unitType, 0);
            gait = groups;
            for (int g = 1; g < groups && gait > 1; g++)
                if (PicturesOfGroup(unitType, g) != cov0 || cov0 == 0) gait = 1;
        }
        return _gaitPhases[unitType] = gait;
    }

    /// <summary>How many DIFFERENT pictures the eight facings of one group hold.
    /// </summary>
    private int PicturesOfGroup(int unitType, int group)
    {
        string dir = group > 0 ? $"{unitType}/g{group}" : unitType.ToString();
        var seen = new HashSet<string>();
        for (int f = 0; f < 8; f++)
        {
            var tex = LoadUnitPart("hull", dir, f);
            var img = tex?.GetImage();
            if (img == null) continue;
            seen.Add(System.Convert.ToBase64String(
                System.Security.Cryptography.MD5.HashData(img.GetData())));
        }
        return seen.Count;
    }

    /// <summary>The pose a unit is actually drawn from.
    ///
    /// <para>Standing, it is the map's +0x11 bounded by what its chassis owns —
    /// the game's own value. Driving, and only for a chassis whose groups are a
    /// gait, it steps on from there at <see cref="HullGaitFps"/>, which is
    /// OURS. The map pose stays the starting phase, so the placed variety
    /// survives, and <c>e.Slot</c> is added so a column of walkers does not
    /// march in lockstep.</para></summary>
    private int PoseOf(Entity e)
    {
        LoadMounts();
        int g = _poseGroups.TryGetValue(e.UnitType, out int n) ? n : 1;
        int start = e.Pose > 0 && e.Pose < g ? e.Pose : 0;
        if (e.Dead || e.Path == null) return start;
        int gait = GaitPhases(e.UnitType);
        if (gait <= 1) return start;
        return (start + (int)(_clock * HullGaitFps) + e.Slot) % gait;
    }

    /// <summary>The game's own rule, @0x429CCB..0x429D1B: the turret is drawn
    /// at the hull's place plus <c>(mount[0] + mount[k]) / 2</c>, where k is the
    /// FLAG byte of the tile the unit stands on and anything above 4 counts as
    /// 0. The halving truncates toward zero, as `sar` after `sub eax,edx` does.
    /// </summary>
    private Vector2 TurretOffset(int unitType, int col, int row)
    {
        LoadMounts();
        if (_mount == null || !_mount.TryGetValue(unitType, out var m)) return Vector2.Zero;
        int k = _flagLookup != null && _flagLookup.TryGetValue((col, row), out int fl) && fl <= 4 ? fl : 0;
        if (k >= m.Length) k = 0;
        return new Vector2(Halve(m[0].X + m[k].X), Halve(m[0].Y + m[k].Y));
    }

    private static int Halve(int v) => v < 0 ? -((-v) >> 1) : v >> 1;

    private static void LoadMounts()
    {
        if (_mount != null) return;
        _mount = new Dictionary<int, Vector2I[]>();
        string path = Core.Content.Path("Units/parts_index.json");
        if (!FileAccess.FileExists(path)) return;
        try
        {
            using var f = FileAccess.Open(path, FileAccess.ModeFlags.Read);
            using var doc = System.Text.Json.JsonDocument.Parse(f.GetAsText());
            if (!doc.RootElement.TryGetProperty("hulls", out var group)) return;
            foreach (var item in group.EnumerateObject())
            {
                if (!int.TryParse(item.Name, out int ut)) continue;
                if (item.Value.TryGetProperty("groups", out var gv))
                    _poseGroups[ut] = System.Math.Max(1, gv.GetInt32());
                if (!item.Value.TryGetProperty("mount", out var arr)) continue;
                var row = new List<Vector2I>();
                foreach (var p in arr.EnumerateArray())
                {
                    if (p.ValueKind != System.Text.Json.JsonValueKind.Array) continue;
                    var xy = p.EnumerateArray().GetEnumerator();
                    if (!xy.MoveNext()) continue;
                    int x = xy.Current.GetInt32();
                    if (!xy.MoveNext()) continue;
                    row.Add(new Vector2I(x, xy.Current.GetInt32()));
                }
                if (row.Count > 0) _mount[ut] = row.ToArray();
            }
        }
        catch (System.Exception e) { GD.PrintErr("Turmsitz: parts_index.json — " + e.Message); }
    }

    private Texture2D? LoadUnitPart(string set, string key, int facing)
    {
        var k = (set + "/" + key, facing);
        if (_composedTex.TryGetValue(k, out var cached)) return cached;
        string path = Core.Content.Path($"Units/{set}/{key}/f{facing}.png");
        Texture2D? tex = ResourceLoader.Exists(path) ? ResourceLoader.Load<Texture2D>(path) : null;
        if (tex == null && FileAccess.FileExists(path))
        {
            var img = Image.LoadFromFile(path);
            if (img != null) tex = ImageTexture.CreateFromImage(img);
        }
        _composedTex[k] = tex;
        return tex;
    }

    public void ToggleSprites() { _drawSprites = !_drawSprites; QueueRedraw(); }

    // ---- hit-testing ----
    /// <summary>
    /// The entity under a point, frontmost first.
    ///
    /// <para>⚠ CORRECTED 07.08.2026 — the dead used to be picked too. They could
    /// not be COMMANDED (<see cref="Commandable"/> excludes them), but
    /// <see cref="SelectAt"/> still put them in the panel as "ZERSTOERT", they
    /// still drew a hover frame, and worst of all a corpse lying on the same tile
    /// as a live unit won the pick and swallowed the click. A body is scenery: it
    /// is drawn, and that is all.</para>
    /// </summary>
    private int Pick(Vector2 p)
    {
        int best = -1, bestRow = int.MinValue;
        for (int i = 0; i < _entities.Count; i++)
            if (!_entities[i].Dead &&
                BodyRect(_entities[i]).HasPoint(p) && _entities[i].Row > bestRow)
            { best = i; bestRow = _entities[i].Row; }
        return best;
    }

    public void HoverAt(Vector2 mapPos)
    {
        int h = Pick(mapPos);
        if (h != _hovered) { _hovered = h; QueueRedraw(); }
    }

    /// <summary>What a click at this spot would mean. OURS — the original had
    /// one pointer for everything; this is the modern convenience the player
    /// asked for, not a recovered behaviour.</summary>
    public enum Hint { Ground, Own, Enemy }

    /// <summary>Reads the cursor hint for a map position: something hostile
    /// under the pointer while one has a selection means the click attacks,
    /// one's own thing means it selects, anything else is open ground.</summary>
    public Hint CursorHintAt(Vector2 mapPos)
    {
        int i = Pick(mapPos);
        if (i < 0 || i >= _entities.Count) return Hint.Ground;
        var e = _entities[i];
        if (e.IsProp || e.Dead) return Hint.Ground;
        if (e.Owner == ViewPlayer) return Hint.Own;
        return _sel.Count > 0 ? Hint.Enemy : Hint.Ground;
    }

    public void ToggleDots() { _showDots = !_showDots; QueueRedraw(); }

    public void ToggleZones() { _showZones = !_showZones; QueueRedraw(); }

    /// <summary>Nebel an/aus (key J). The original has the same switch, one byte
    /// at 0x4f8a3c that its exploration step checks.</summary>
    public void ToggleFog()
    {
        UI.Settings.FogOfWar = !UI.Settings.FogOfWar;
        _fogTick = FogEverySec;          // take effect on the next tick, not in ten
        UpdateFog();
        _fogDrawn = -1;
        QueueRedraw();
    }

    public void ToggleBuildings() { _showBuildings = !_showBuildings; QueueRedraw(); }

    /// <summary>The names of the aircraft parked in this Flughafen.</summary>
    private string HangarNames(Entity e)
    {
        if (e.Hangar == null || e.Hangar.Count == 0) return "";
        var seen = new List<string>();
        foreach (int s in e.Hangar)
        {
            var sp = _special.Find(x => x.Slot == s);
            if (sp.Name != null && !seen.Contains(sp.Name)) seen.Add(sp.Name);
        }
        return string.Join(" ", seen).ToUpper();
    }

    // ---- aircraft ----
    //
    // The eight types come from a template table at VA 0x51b021 (8 x 48) that
    // the spawn routine @0x4b1580 copies into a sec19 record.  The payload
    // component identifies the type uniquely, so that is the key here.
    private static Dictionary<int, string>? _aircraft;

    private static void LoadAircraft()
    {
        if (_aircraft != null) return;
        _aircraft = new Dictionary<int, string>();
        string path = Core.Content.Path("Maps/aircraft.json");
        if (!FileAccess.FileExists(path)) return;
        using var f = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        if (f == null) return;
        var json = new Json();
        if (json.Parse(f.GetAsText()) != Error.Ok ||
            json.Data.VariantType != Variant.Type.Dictionary) return;
        var root = json.Data.AsGodotDictionary<string, Variant>();
        if (!root.TryGetValue("types", out var tv) || tv.VariantType != Variant.Type.Array)
            return;
        foreach (var item in tv.AsGodotArray())
        {
            if (item.VariantType != Variant.Type.Dictionary) continue;
            var t = item.AsGodotDictionary<string, Variant>();
            _aircraft[GetI(t, "payload")] =
                t.TryGetValue("name", out var nv) ? nv.AsString() : "";
        }
    }

    private readonly Dictionary<(int, int), Texture2D?> _airTex = new();

    /// <summary>Sprite of an aircraft. The part is chosen by the record's KIND,
    /// not by its airframe value: the draw path @0x42b867 switches on +0x08 and
    /// moves a literal part number in (1 -> 114, 2 -> 115, 3 -> 119, 10 -> 112,
    /// 11 -> 113, 12 -> 118, 13 -> 117, 14 -> 116). The earlier
    /// `part = airframe - 8` rule was wrong for everything except kind 10, so
    /// both supply helicopters and all three fixed-wing types used to be drawn
    /// with the wrong hull. The exporter files the frames under the kind.</summary>
    private Texture2D? GetAirframeTexture(int kind, int facing)
    {
        var key = (kind, facing);
        if (_airTex.TryGetValue(key, out var cached)) return cached;
        string path = Core.Content.Path($"Units/aircraft/{kind}/f{facing}.png");
        Texture2D? tex = ResourceLoader.Exists(path) ? ResourceLoader.Load<Texture2D>(path) : null;
        if (tex == null && FileAccess.FileExists(path))
        {
            var img = Image.LoadFromFile(path);
            if (img != null) tex = ImageTexture.CreateFromImage(img);
        }
        _airTex[key] = tex;
        return tex;
    }

    // OURS: how the record's speed byte turns into pixels per second, and how
    // fast fuel burns.  The data has speeds (Jagdflieger 25, Spion 20, Bomber
    // und Kampfhubschrauber 10, die uebrigen Helis 8) und einen Tank
    // (800..2000), aber nichts, was sagt, was davon je Sekunde gilt.  Die
    // Verhaeltnisse untereinander bleiben die des Spiels.
    //
    // ⚠ 11.08.2026 — bis heute stand die Geschwindigkeit zwar in diesem
    // Kommentar, aber in KEINER Datei: ExeTables.Aircraft las das Feld +0x21
    // gar nicht, aircraft.json trug kein "speed", und die Fluglogik rechnete
    // mit Max(1, 0). Jedes Flugzeug flog also mit 7 px/s -- langsamer als jedes
    // Fahrzeug faehrt. Gemeldet als »helikopter fliegt langsamer als unsere
    // fahrzeuge«.
    //
    // Der Faktor ist so gewaehlt, dass der LANGSAMSTE Helikopter (8) das
    // SCHNELLSTE Fahrzeug (roh 14 x PxPerSpeedUnit 6 = 84 px/s) gerade
    // einholt: 8 * 11 = 88. Ueber die beiden Tabellen hinweg gibt es dafuer
    // keine Vorlage im Original -- sie stehen in verschiedenen Saetzen mit
    // verschiedenen Massstaeben --, also ist genau dieser Faktor unsere Wahl
    // und sonst nichts.
    private const float AirPxPerSpeed = 11f;
    private const float AirFuelBurn = 12f;      // fuel per second in the air
    private const float AirReloadSec = 12f;     // seconds a full rearm takes
    private const float AirFireGap = 1.4f;      // seconds between attacks
    private const float AirShadowDrop = 26f;    // how high above the ground it flies

    /// <summary>The closest airfield still standing that this aircraft may use.</summary>
    private Entity? NearestAirfield(Special a)
    {
        Entity? best = null; float bd = float.MaxValue;
        foreach (var e in _entities)
        {
            if (!e.IsBuilding || e.BType != 9 || e.Dead || e.Owner is < 0 or > 7) continue;
            if (a.Owner is >= 0 and <= 7 && !Allied(a.Owner, e.Owner)) continue;
            float dd = a.Pos.DistanceTo(e.Pos);
            if (dd < bd) { bd = dd; best = e; }
        }
        return best;
    }

    // ---- the supply helicopters (sec19 kind 13 and 14) ---------------------
    // Everything numeric here is the game's own:
    //   * a customer qualifies below 65 % (the search starts its best-so-far at
    //     0x41 @0x427a5f / @0x427ca2) and the LOWEST percentage wins;
    //   * the delivery sets the stat straight back to its maximum
    //     (hp @0x424946, ammunition @0x424b64);
    //   * it costs 50 * missing / max of the payload (@0x424931 / @0x424b72),
    //     so a completely empty customer costs exactly 50;
    //   * a Nachschub-Posten (building typ 14) refills the payload to 255
    //     (@0x42499e / @0x424bd8).
    // Ours: the arrival tolerance in pixels and the little blast that marks the
    // hand-over.
    private const int SupplyThreshold = 65;
    private const int SupplyFullCost = 50;
    private const int SupplyCargoFull = 255;

    /// <summary>How full the customer is in percent, in the stat this
    /// helicopter deals in: hit points for the Treibstoffheli, rounds for the
    /// Munitionheli.</summary>
    private static int SupplyPercent(Special a, Entity e) => a.Kind == 13
        ? (e.FuelMax > 0 ? e.Fuel * 100 / e.FuelMax : 100)
        : (e.AmmoMax > 0 ? e.Ammo * 100 / e.AmmoMax : 100);

    private static bool SupplyEligible(Special a, Entity e) =>
        !e.Dead && !e.IsProp && !e.IsBuilding && e.Owner == a.Owner &&
        (a.Kind == 13 ? e.FuelMax > 0 : e.AmmoMax > 0);

    /// <summary>Is another helicopter of the same kind and owner already on its
    /// way to this unit? The original walks all 200 sec19 slots for exactly this
    /// check (@0x427a6d) so two helicopters never serve the same customer.</summary>
    private bool ClaimedByAnother(Special a, int idx)
    {
        foreach (var o in _special)
            if (o != a && !o.Dead && o.Kind == a.Kind && o.Owner == a.Owner && o.Customer == idx)
                return true;
        return false;
    }

    /// <summary>The nearest Nachschub-Posten to reload at.
    ///
    /// The original scans all 255 building records for `typ == 14` and takes the
    /// closest one — it does NOT test the owner (@0x427ad5..0x427b42), and the
    /// data says why: all 63 Nachschub-Posten across the 26 maps carry owner
    /// 255. They are unowned map features with a base name (Risoyhamn, Myre)
    /// and 1000 hit points, not somebody's building — so every side may use
    /// them. Our loader keeps such records as buildings with owner -1.</summary>
    private Entity? NearestDepot(Special a)
    {
        Entity? best = null; float bd = float.MaxValue;
        foreach (var e in _entities)
        {
            if (!e.IsBuilding || e.BType != 14 || e.Dead) continue;
            float dd = a.Pos.DistanceTo(e.Pos);
            if (dd < bd) { bd = dd; best = e; }
        }
        return best;
    }

    private void UpdateSupply(Special a)
    {
        a.Target = -1;                     // these two never shoot at anything
        float reach = TileW * 0.6f;

        if (a.Cargo <= 0)                  // empty: go and fetch a new load
        {
            a.Customer = -1;
            var depot = a.DepotSlot >= 0
                ? _entities.Find(x => x.IsBuilding && x.Slot == a.DepotSlot && !x.Dead)
                : null;
            depot ??= NearestDepot(a);
            if (depot == null) { a.Goal = null; a.DepotSlot = -1; return; }
            a.DepotSlot = depot.Slot;
            a.Goal = depot.Pos;
            if (a.Pos.DistanceTo(depot.Pos) < reach)
            {
                a.Cargo = SupplyCargoFull;
                a.DepotSlot = -1;
                a.Goal = null;
            }
            return;
        }
        a.DepotSlot = -1;

        // keep the current customer while it lives and still needs the goods
        if (a.Customer >= 0 && a.Customer < _entities.Count)
        {
            var cur = _entities[a.Customer];
            if (!SupplyEligible(a, cur) || SupplyPercent(a, cur) >= 100) a.Customer = -1;
        }
        else a.Customer = -1;

        if (a.Customer < 0)                // look for the neediest unit
        {
            int best = -1, bestPct = SupplyThreshold;
            for (int i = 0; i < _entities.Count; i++)
            {
                var e = _entities[i];
                if (!SupplyEligible(a, e)) continue;
                int pct = SupplyPercent(a, e);
                if (pct >= bestPct) continue;
                if (ClaimedByAnother(a, i)) continue;
                bestPct = pct; best = i;
            }
            a.Customer = best;
        }

        if (a.Customer < 0) { a.Goal = null; return; }

        var c = _entities[a.Customer];
        a.Goal = c.Pos;
        if (a.Pos.DistanceTo(c.Pos) >= reach) return;

        // arrived: fill the customer up and book the payload it took
        int missing, max;
        if (a.Kind == 13) { max = c.FuelMax; missing = c.FuelMax - c.Fuel; c.Fuel = c.FuelMax; }
        else              { max = c.AmmoMax; missing = c.AmmoMax - c.Ammo; c.Ammo = c.AmmoMax; }
        int cost = max > 0 ? SupplyFullCost * missing / max : 0;
        a.Cargo = Mathf.Max(0, a.Cargo - cost);
        SupplyRuns++;
        // ⚠ 11.08.2026 — hier stand ein "muzzle"-Aufblitzen als Quittung fuer
        // die Uebergabe. Gemeldet als »es erscheint wieder so ein treffer
        // sprite (wie bei einem kanonentreffer) wenn die versorgungshelis an
        // den panzern munition oder treibstoff abliefern«.
        //
        // Der Kommentar von damals sagte es schon selbst -- »a blast would read
        // as a hit« -- und griff dann zum Feuerball, der genauso gelesen wird.
        // Das Spiel hat fuer die Versorgung GAR KEINEN Effekt, also hat sie
        // hier auch keinen. Sichtbar ist die Uebergabe an dem, was sie
        // bewirkt: die Balken fuer Munition und Treibstoff springen hoch.
        a.Customer = -1;
        a.Goal = null;
    }

    /// <summary>Deliveries made this session — the harness reports it.</summary>
    public int SupplyRuns;

    private void UpdateAircraft(float dt)
    {
        foreach (var a in _special)
        {
            if (a.Dead) continue;
            if (a.Stored)                            // parked: rearm and refuel
            {
                a.Ammo = Mathf.Min(a.AmmoMax, a.Ammo + Mathf.CeilToInt(a.AmmoMax * dt / AirReloadSec));
                a.Fuel = Mathf.Min(a.FuelMax, a.Fuel + Mathf.CeilToInt(a.FuelMax * dt / AirReloadSec));
                continue;
            }

            if (a.IsSupply) { UpdateSupply(a); goto move; }

            // out of fuel or ammo: head home
            bool spent = a.Fuel <= 0 || (a.Armed && a.Ammo <= 0);
            if (spent)
            {
                a.Target = -1;                       // nothing left to shoot with
                var home = a.HomeSlot < 0 ? null
                    : _entities.Find(x => x.IsBuilding && x.Slot == a.HomeSlot && !x.Dead);
                // the field may have been bombed out — then look for another one
                home ??= NearestAirfield(a);
                if (home != null)
                {
                    a.HomeSlot = home.Slot;
                    a.Goal = home.Pos;
                    if (a.Pos.DistanceTo(home.Pos) < TileW * 0.6f)
                    {
                        a.Stored = true; a.Goal = null; a.Col = home.Col; a.Row = home.Row;
                        continue;
                    }
                }
                else a.Goal = null;                  // no field left: it just circles
            }

            // pick a target inside the aircraft's own sight radius
            if (a.Armed && a.Ammo > 0 && a.Target < 0)
            {
                float best = a.Sight * TileW;
                for (int j = 0; j < _entities.Count; j++)
                {
                    var e = _entities[j];
                    if (e.IsProp || e.Dead || e.HpMax <= 0) continue;
                    if (e.Owner is < 0 or > 7 || a.Owner is < 0 or > 7) continue;
                    if (Allied(a.Owner, e.Owner)) continue;
                    float dd = a.Pos.DistanceTo(e.Pos);
                    if (dd < best) { best = dd; a.Target = j; }
                }
            }
            if (a.Target >= 0)
            {
                var t = _entities[a.Target];
                if (t.Dead) a.Target = -1;
                else
                {
                    a.Goal = t.Pos;
                    a.Cooldown -= dt;
                    if (t.Dead) { a.Target = -1; }
                    else if (a.Pos.DistanceTo(t.Pos) < TileW * 1.5f && a.Cooldown <= 0f && a.Ammo > 0)
                    {
                        a.Cooldown = AirFireGap;
                        if (!(CheatAmmo && Cheated(a))) a.Ammo--;
                        _effects.Add(new Effect { Pos = t.Pos - new Vector2(0, 8),
                                                  Kind = "explosion", FrameTime = 0.05f });
                        ApplyHit(-1, a.Target, t, a.Attack);
                    }
                }
            }

            move:
            if (a.Goal is not { } g) continue;
            var delta = g - a.Pos;
            float step = Mathf.Max(1, a.Speed) * AirPxPerSpeed * dt;
            if (delta.Length() <= step) { a.Pos = g; if (a.Target < 0) a.Goal = null; }
            else
            {
                a.Pos += delta.Normalized() * step;
                a.Facing = DirToFacing(delta);
                // Fuel is spent per TILE, not per second: the move code takes one
                // unit off at every cell crossing (@0x4250ab, @0x425100), which is
                // what a tank of 800..2000 is measured in.
                a.FuelFrac += step / TileW;
                int burn = (int)a.FuelFrac;
                if (burn > 0 && !(CheatFuel && Cheated(a)))
                { a.FuelFrac -= burn; a.Fuel = Mathf.Max(0, a.Fuel - burn); }
                // kinds above 12 top their own tank up on every step (@0x42512c) —
                // the supply helicopters never run dry
                if (a.Kind > 12) a.Fuel = a.FuelMax;
            }
            a.Col = Mathf.Clamp((int)(a.Pos.X / TileW), 0, 255);
            a.Row = Mathf.Clamp((int)(a.Pos.Y / TileH), 0, 255);
        }
    }

    /// <summary>Send every airworthy aircraft of a player at the nearest enemy.</summary>
    public int LaunchAircraft(int owner)
    {
        int n = 0;
        foreach (var a in _special)
        {
            if (a.Dead || a.Owner != owner) continue;
            if (a.Stored)
            {
                var home = _entities.Find(x => x.IsBuilding && x.Slot == a.HomeSlot);
                if (home == null) continue;
                a.Stored = false;
                a.Pos = home.Pos;
                a.Col = home.Col; a.Row = home.Row;
            }
            // "Aussenden": head for the nearest enemy so the sortie has a
            // direction; once inside the aircraft's own sight it picks a target
            a.Target = -1;
            a.Goal = null;
            float bd = float.MaxValue;
            foreach (var e in _entities)
            {
                if (e.IsProp || e.Dead || e.HpMax <= 0) continue;
                if (e.Owner is < 0 or > 7 || Allied(owner, e.Owner)) continue;
                float dd = a.Pos.DistanceTo(e.Pos);
                if (dd < bd) { bd = dd; a.Goal = e.Pos; }
            }
            n++;
        }
        _order = n > 0 ? $"{n} Flugzeuge ausgesandt" : "keine Flugzeuge";
        UpdatePanel();
        QueueRedraw();
        return n;
    }

    private bool _showRail;
    public void ToggleRail() { _showRail = !_showRail; QueueRedraw(); }

    /// <summary>How the map's rail network and its sec19 objects came out.</summary>
    public string NetworkLine()
    {
        int lines = 0;
        foreach (var kv in _rail) lines += kv.Value.Count;
        int air = 0, parked = 0, flying = 0, armed = 0;
        foreach (var s in _special)
        {
            if (s.Dead) continue;
            air++;
            if (s.Stored) parked++; else flying++;
            if (s.Armed && s.Ammo > 0) armed++;
        }
        string one = "";
        for (int pass = 0; pass < 2 && one.Length == 0; pass++)
        foreach (var s in _special)
        {
            if (s.Dead || !s.Armed) continue;
            if (pass == 0 && s.Target < 0) continue;
            one = $" | {s.TypeName} P{s.Owner} mun={s.Ammo}/{s.AmmoMax} " +
                  $"sprit={s.Fuel}/{s.FuelMax} f{s.Facing} " +
                  $"{(s.Stored ? "im Hangar" : "fliegt")} ziel={s.Target} heim={s.HomeSlot}";
            break;
        }
        string sup = "";
        foreach (var s in _special)
        {
            if (s.Dead || !s.IsSupply) continue;
            sup = $" | {s.TypeName} P{s.Owner} nutzlast={s.Cargo}/{SupplyCargoFull} " +
                  $"kunde={s.Customer}" +
                  (s.Customer >= 0 && s.Customer < _entities.Count
                      ? $" ({SupplyPercent(s, _entities[s.Customer])}%)" : "") +
                  (s.DepotSlot >= 0 ? " -> Nachschub-Posten" : "") +
                  $"  lieferungen={SupplyRuns}";
            if (s.Customer >= 0 || s.DepotSlot >= 0) break;   // prefer a busy one
        }
        return $"rail {_rail.Count} nodes/{lines / 2} lines, {air} Flugzeuge " +
               $"({parked} im Hangar, {flying} in der Luft, {armed} bewaffnet){one}{sup}";
    }

    private void UpdatePanel()
    {
        if (_selected < 0)
        {
            // idle panel: mission + selection summary, so the frame is never empty
            _panel.Visible = _panelTextOn;
            _panel.Text = $"{_mission}\n{_entities.Count} EINHEITEN\n" +
                          (_order.Length > 0 ? _order.ToUpper() : "KEINE AUSWAHL");
            return;
        }
        var e = _entities[_selected];
        _panel.Visible = _panelTextOn;
        if (e.IsProp)
        {
            _panel.Text = $"PROP {e.UnitType}\nZELLE {e.Col},{e.Row}\nHOEHE {e.Elev}";
        }
        else if (e.IsBuilding)
        {
            _panel.Text =
                $"{e.Name.ToUpper()}\n" +
                $"{BuildingTypeName(e.BType).ToUpper()}\n" +
                // while it is being taken the panel shows the flickering owner
                // (+0x3d), the way the original's does
                $"{OwnerWord(e.CaptureProgress > 0 && e.ShownOwner >= 0 ? e.ShownOwner : e.Owner)}\n" +
                (e.CaptureProgress > 0 && e.CaptureTotal > 0
                    ? $"BESETZT {e.CaptureProgress * 100 / e.CaptureTotal}% P{e.Intruder}\n"
                    : $"HP {e.Hp}/{e.HpMax}\n") +
                // a deposit (sec28) beats the part stock, which beats the cell
                (e.Deposit >= 0 ? $"{GradeWords[GradeOf(e)].ToUpper()} {e.Deposit}\n"
                 // a Flughafen reports its hangar: how many aircraft of what
                 // kind are parked, against the hangar size from sec27 +0x03
                 : e.BType == 9
                     ? $"HANGAR {(e.Hangar?.Count ?? 0)}/{e.HangarSize} " +
                       $"{HangarNames(e)}\n"
                 // a factory reports its own part store against its Lagerplatz
                 // and its Produktionsgeschwindigkeit, both from the map data
                 : IsFactory(e)
                     ? $"T{e.StockT} {(e.BType == 2 ? "W" : e.BType == 3 ? "F" : "S")}" +
                       $"{(e.BType == 2 ? e.StockW : e.BType == 3 ? e.StockF : e.StockS)}" +
                       $"/{e.Capacity} V{e.ProdSpeed}\n"
                 : e.StockW + e.StockF + e.StockS > 0
                     ? $"W{e.StockW} F{e.StockF} S{e.StockS} T{e.StockT}\n"
                 : e.StockT > 0 ? $"TERRANIUM {e.StockT}\n"
                     : $"ZELLE {e.Col},{e.Row}\n") +
                // the game's own status line, and "% fertig" while a job runs
                (e.Dead ? "ZERSTOERT"
                 // the display box fits about twenty characters, so a running
                 // job drops the "Status :" label in favour of "% fertig"
                 : PercentDone(e) >= 0
                     ? $"{StateName(e).ToUpper()} {PercentDone(e)}% FERTIG"
                 : e.BuildTime > 0f && IsDock(e)
                     ? $"BAUT {ShipMenuPick(e).ToUpper()} {e.BuildTime:0}s"
                 : e.BuildTime > 0f && _designs != null && _designs.Count > 0
                     ? $"BAUT {_designs[e.BuildIndex % _designs.Count].Name} {e.BuildTime:0}s"
                 : e.State != StAktiv ? $"STATUS : {StateName(e).ToUpper()}"
                 : e.IsTarget ? "MISSIONSZIEL"
                 : IsFactory(e) ? MenuPick(e).ToUpper()
                 // the airfield sells helicopters: show what B would buy
                 : e.BType == 9 && AirMenu(e).Count > 0 ? "KAUFEN " + AirMenuPick(e).ToUpper()
                 // the dock orders ships and the Schiffswerft pays for them
                 : IsDock(e) ? (ShipMenu(e).Count > 0
                        ? ShipMenuPick(e).ToUpper()
                        : "WERFT " + (_shipDesigns == null ? "KEINE LISTE" : "GESPERRT"))
                 : "GEBAEUDE");
        }
        else if (_compactPanel)
        {
            // Six short lines is what the original panel display box fits, and
            // the words are the game's own: ENERGIE (0x5019b0), MUNITION
            // (0x5019ec) and SPRIT (0x5019f8), which the fullest of the
            // original's panel routines (0x46a837..0x46b078) prints in that
            // order together with Stärke, Nachladen, Verteidigung and Reichw.
            //
            // ⚠ SPRIT was missing until 2026-08-06. The value was read all
            // along (entity +0x2e against the tank at +0x30) but only the dead
            // debug branch below ever printed it, and PlacePanel forces this
            // branch — so the player never saw the fuel gauge the original has.
            string st = OrderOf(e);
            if (e.Target >= 0 && !e.DugIn)
                st += $" {CellDistance(e, _entities[e.Target]):0.0}";
            else if (e.Path != null) st += $" {e.Path.Count - e.PathIdx}";
            if (e.Orders.Count > 0) st += $" +{e.Orders.Count}";   // queued behind it
            var wp2 = WeaponOf(e.Weapon);
            string levels = (e.AmmoMax > 0 ? $"MUNITION {e.Ammo}/{e.AmmoMax}" : "")
                          + (e.AmmoMax > 0 && e.FuelMax > 0 ? "  " : "")
                          + (e.FuelMax > 0 ? $"SPRIT {e.Fuel}/{e.FuelMax}" : "");
            // The game stops a unit at an empty tank and says "no fuel"
            // (@0x407ab8); an empty magazine is the other thing worth shouting.
            string warn = e.AmmoMax > 0 && e.Ammo <= 0 ? "KEINE MUNITION"
                        : e.FuelMax > 0 && e.Fuel <= 0 ? "KEIN SPRIT"
                        : st;
            _panel.Text =
                $"{LabelOf(e.UnitType).ToUpper()}\n" +
                $"SPIELER {(e.Owner < 0 ? "?" : e.Owner.ToString())}  TEAM {e.Team}\n" +
                $"ENERGIE {e.Hp}/{e.HpMax}\n" +
                $"{(e.Weapon == 0 ? "UNBEWAFFNET" : wp2.Name.ToUpper())}\n" +
                (levels.Length > 0 ? levels : $"ZELLE {e.Col},{e.Row}") + "\n" +
                warn;
        }
        else
        {
            string owner = e.Owner < 0 ? "?" : $"P{e.Owner}";
            bool stranded = e.Mobile && _nav != null && !_nav.IsWalkable(e.Col, e.Row, e.Move);
            string move = e.Dead ? "DESTROYED"
                : e.Target >= 0
                    ? $"engaging slot {_entities[e.Target].Slot} " +
                      $"({CellDistance(e, _entities[e.Target]):0.0} tiles)"
                : e.Path != null
                ? $"moving -> ({e.Goal.X},{e.Goal.Y}), {e.Path.Count - e.PathIdx} steps left"
                : stranded ? "stranded (standing off its domain)"
                : e.Mobile ? "idle (right-click = move / attack)" : "immobile";
            var wp = WeaponOf(e.Weapon);
            string weapon = e.Weapon == 0 ? "unarmed"
                : $"{wp.Name} (comp {e.Weapon}, dmg {wp.Damage}, range {wp.RangeTiles:0.#})";
            string terrain = _nav == null ? "?" : TerrainName(_nav.GroundAt(e.Col, e.Row));
            string domain = e.Move switch
            {
                Simulation.NavGrid.MoveClass.Ship => "Schiff",
                Simulation.NavGrid.MoveClass.Hover => "Hover",
                Simulation.NavGrid.MoveClass.Walker => "Fuss",
                _ => "Fahrzeug",
            };
            _panel.Text =
                $"◈ ENTITY slot {e.Slot}   cell ({e.Col},{e.Row})  elev {e.Elev}\n" +
                $"   unit_type {e.UnitType} ({LabelOf(e.UnitType)})   facing {e.Facing}\n" +
                $"   equip {e.Equipment} ({EquipName(e.Equipment)})   weapon: {weapon}" +
                (e.AmmoMax > 0 ? $"   ammo {e.Ammo}/{e.AmmoMax}" : "") +
                $"   energie {e.Hp}/{e.HpMax}   sprit {e.Fuel}/{e.FuelMax}   A/V {e.Attack}/{e.Defence}\n" +
                $"   owner {owner}   team {e.Team}   hp {e.Hp}/{e.HpMax}\n" +
                $"   domain {domain}   terrain {terrain}\n" +
                $"   {move}\n" +
                $"   selected: {_sel.Count}" + (_order.Length > 0 ? $"   |   {_order}" : "") + "\n" +
                $"   source: {_source}";
        }
    }

    public override void _Draw()
    {
        // sec2 movement/zone overlay (single stretched texture, under everything)
        if (_showZones && _zoneTex != null)
            DrawTextureRect(_zoneTex, _zoneRect, false);

        // walkability debug overlay (blue = water, red = props) — key P
        if (_showNav && _navTex != null)
            DrawTextureRect(_navTex, _navRect, false);

        // the build-site preview, under the sprites so units stay readable
        DrawBuildPreview();

        // planned routes of the selected units, drawn under the sprites
        foreach (int i in _sel)
        {
            var e = _entities[i];
            if (e.Path == null || e.PathIdx >= e.Path.Count) continue;
            var pts = new List<Vector2> { e.Pos };
            for (int k = e.PathIdx; k < e.Path.Count; k++)
                pts.Add(CellCenter(e.Path[k].X, e.Path[k].Y));
            DrawPolyline(pts.ToArray(), new Color(0.4f, 1f, 0.6f, 0.75f), 1.6f);
            DrawDiamond(pts[^1], 5f, new Color(0.4f, 1f, 0.6f));
        }

        // and what is queued behind the current route: a dashed line on to each
        // further destination, numbered so the order is readable at a glance
        foreach (int i in _sel)
        {
            var e = _entities[i];
            if (e.Orders.Count == 0) continue;
            var from = e.Path != null && e.PathIdx < e.Path.Count
                ? CellCenter(e.Path[^1].X, e.Path[^1].Y) : e.Pos;
            var faded = new Color(0.4f, 1f, 0.6f, 0.35f);
            var fadedHit = new Color(1f, 0.45f, 0.35f, 0.45f);
            for (int k = 0; k < e.Orders.Count; k++)
            {
                var o = e.Orders[k];
                // an attack order follows its target, so draw it where the
                // target is now rather than where it stood when ordered
                var to = o.IsAttack && o.Target < _entities.Count && !_entities[o.Target].Dead
                    ? _entities[o.Target].Pos
                    : CellCenter(o.Cell.X, o.Cell.Y);
                var c = o.IsAttack ? fadedHit : faded;
                DrawDashedLine(from, to, c, 1.2f, 6f);
                if (o.IsAttack) DrawArc(to, 7f, 0, Mathf.Tau, 12, c, 1.5f);
                else DrawDiamond(to, 4f, c);
                if (_uiFont != null)
                    DrawString(_uiFont, to + new Vector2(8, -4), (k + 2).ToString(),
                               HorizontalAlignment.Left, -1, _uiFontSize,
                               new Color(0.85f, 0.95f, 0.85f, 0.9f));
                from = to;
            }
        }

        // Buildings first, in their own pass and back to front. They used to be
        // baked into the map picture and therefore behind everything; drawing
        // them here keeps exactly that order, so nothing about the look changes
        // except that a destroyed one can now show its ruin.
        if (_drawSprites && Patterns != null)
            foreach (var b in BuildingsBackToFront())
                DrawBuildingBody(b);

        // burnt-out wrecks stay on the ground, under everything alive
        DrawEffects(ground: true);
        DrawRangeRings();

        // entities: real unit sprites (ROBO.CWR) when available, else owner dots.
        for (int i = 0; i < _entities.Count; i++)
        {
            var e = _entities[i];
            if (e.IsProp)
            {
                if (_showDots)
                    DrawCircle(e.Pos, 2.5f,
                               new Color(PropColor.R, PropColor.G, PropColor.B, 0.85f));
                continue;
            }

            // under the fog: someone else's unit is only drawn where the player
            // is actually watching. A BUILDING that has once been seen keeps
            // standing — it is part of the baked picture anyway, and a base does
            // not walk away while nobody looks.
            if (FogActive && e.Owner != ViewPlayer && !e.IsBuilding && !Watched(e.Col, e.Row))
                continue;

            // a fallen soldier keeps his own frames (12..14) and stays lying
            // there; for vehicles the wreck effect stands in for the sprite
            if (e.Dead && e.Infantry < 0) continue;
            if (e.Dead && _drawSprites)
            {
                var body = GetInfantryTexture(e.Infantry, e.Facing, InfBlock(e));
                if (body != null) DrawTexture(body, e.Pos - ComposedAnchor);
                continue;
            }
            if (e.Dead) continue;
            // buildings are part of the baked map picture; they only get their
            // flag (key T), a health bar once damaged, and the selection box
            if (e.IsBuilding)
            {
                DrawBuildingDoors(e);
                if (_sel.Contains(i)) DrawSelectionBrackets(e.Pos, i == _selected, 15f, 10f);
                continue;
            }

            var oc = OwnerColor(e.Owner);
            var baseC = e.Pos;
            // owner ring on the ground under the unit
            DrawArc(baseC, 7f, 0, Mathf.Tau, 20, new Color(oc.R, oc.G, oc.B, 0.9f), 2f);

            // target line while engaging
            if (e.Target >= 0 && _sel.Contains(i))
                DrawLine(baseC, _entities[e.Target].Pos, new Color(1f, 0.4f, 0.3f, 0.5f), 1f);

            if (e.DugIn)   // dug in: a earth-coloured bracket under the unit
                DrawArc(baseC, 11f, Mathf.Pi * 0.15f, Mathf.Pi * 0.85f, 14,
                        new Color(0.75f, 0.55f, 0.25f, 0.95f), 3f);
            if (_sel.Contains(i)) DrawSelectionBrackets(baseC, i == _selected);

            if (_drawSprites)
            {
                int aim = e.AimFacing >= 0 ? e.AimFacing : e.Facing;
                // foot soldiers come from their own bank in ROBO.CWR and share
                // the vehicles' 64x56 canvas, so the same anchor applies
                if (e.Infantry >= 0)
                {
                    var foot = GetInfantryTexture(e.Infantry, e.Facing, InfBlock(e));
                    if (foot != null) { DrawTexture(foot, baseC - ComposedAnchor); continue; }
                }
                // hull + separately aimed turret (preferred)
                var hull = GetHullTexture(e.UnitType, e.Facing, PoseOf(e));
                if (hull != null)
                {
                    DrawTexture(hull, baseC - ComposedAnchor);
                    var turret = GetTurretTexture(e.Weapon, aim);
                    if (turret != null)
                        DrawTexture(turret, baseC - ComposedAnchor
                                            + TurretOffset(e.UnitType, e.Col, e.Row));
                    continue;
                }
                var composed = GetComposedTexture(e.Combo, e.Facing);
                if (composed != null)
                {
                    // fixed 64x56 canvas, anchored at the unit's ground-center
                    DrawTexture(composed, baseC - ComposedAnchor);
                    continue;
                }
                // bare chassis (e.g. a freshly produced design) — the turret is
                // still drawn on top, it shares the same 64x56 anchor
                var bare = GetUnitTexture(e.UnitType, e.Facing);
                if (bare != null)
                {
                    DrawTexture(bare, new Vector2(baseC.X - bare.GetWidth() / 2f,
                                                  baseC.Y - bare.GetHeight()));
                    var turret2 = GetTurretTexture(e.Weapon, aim);
                    if (turret2 != null) DrawTexture(turret2, baseC - ComposedAnchor);
                    continue;
                }
            }
            DrawCircle(baseC, 4.5f, new Color(oc.R, oc.G, oc.B, 0.85f));
            DrawArc(baseC, 5.5f, 0, Mathf.Tau, 16, new Color(0, 0, 0, 0.6f), 1.2f);
        }

        // health bars in a second pass — the unit sprites are up to 55 px tall and
        // would otherwise cover the bar of the unit standing behind them
        for (int i = 0; i < _entities.Count; i++)
        {
            var e = _entities[i];
            if (e.IsProp || e.Dead || e.HpMax <= 0) continue;
            bool sel = _sel.Contains(i);
            if (!sel && e.Hp >= e.HpMax) continue;
            var hb = e.Pos + new Vector2(-TileW / 2f, -TileH / 2f - 8);
            float fr = Mathf.Clamp((float)e.Hp / e.HpMax, 0, 1);
            DrawRect(new Rect2(hb - new Vector2(1, 1), new Vector2(TileW + 2, 6)),
                     new Color(0, 0, 0, 0.75f));
            DrawRect(new Rect2(hb, new Vector2(TileW * fr, 4)),
                     sel && fr >= 1f ? new Color(0.3f, 1f, 0.3f)
                                     : new Color(1f - fr, 0.25f + fr * 0.75f, 0.2f));
        }

        // sec19 aircraft: the airframe sprite well above its ground shadow
        foreach (var s in _special)
        {
            if (s.Stored || s.Dead) continue;          // inside a hangar
            var c = s.Pos;
            var air = c - new Vector2(0, AirShadowDrop);
            // flattened ground shadow, drawn as an explicit ellipse so no
            // canvas transform is involved
            var sh = new Vector2[12];
            for (int k = 0; k < sh.Length; k++)
            {
                float ang = Mathf.Tau * k / sh.Length;
                sh[k] = c + new Vector2(Mathf.Cos(ang) * 8f, Mathf.Sin(ang) * 3.5f);
            }
            DrawColoredPolygon(sh, new Color(0, 0, 0, 0.32f));
            var tex = GetAirframeTexture(s.Kind, s.Facing);
            if (tex != null) DrawTexture(tex, air - ComposedAnchor);
            else DrawDiamond(air, 8f, new Color(0.1f, 0.9f, 0.95f));
            if (s.Airframe == 120)                     // helicopters get a rotor
            {
                var rot = GetAirframeTexture(_clock * 20f % 2f < 1f ? 110 : 111, s.Facing);
                if (rot != null) DrawTexture(rot, air - ComposedAnchor);
            }
            // ammo left, so an emptied aircraft is visible at a glance
            if (s.AmmoMax > 0)
            {
                float fr = Mathf.Clamp((float)s.Ammo / s.AmmoMax, 0, 1);
                var bar = air - new Vector2(9, 16);
                DrawRect(new Rect2(bar - Vector2.One, new Vector2(20, 5)),
                         new Color(0, 0, 0, 0.7f));
                DrawRect(new Rect2(bar, new Vector2(18 * fr, 3)),
                         new Color(1f - fr, 0.3f + fr * 0.7f, 0.2f));
            }
        }

        // the SPOJ rail network (key L) — each line follows its own recorded
        // track; only maps without routes fall back to straight connections
        // the trains ride on the rails whether or not the overlay is on
        DrawTrains();

        if (_showRail && _railRoutes.Count > 0)
        {
            var col = new Color(0.95f, 0.85f, 0.35f, 0.85f);
            foreach (var route in _railRoutes)
            {
                for (int k = 1; k < route.Count; k++)
                    DrawLine(RailPoint(route[k - 1]), RailPoint(route[k]), col, 2f);
                DrawCircle(RailPoint(route[0]), 3f, col);
                DrawCircle(RailPoint(route[^1]), 3f, col);
            }
        }
        else if (_showRail && _hasRail)
            foreach (var kv in _rail)
            {
                var a = _entities.Find(x => x.IsBuilding && x.Slot == kv.Key);
                if (a == null) continue;
                foreach (int n in kv.Value)
                {
                    if (n <= kv.Key) continue;         // draw each line once
                    var b = _entities.Find(x => x.IsBuilding && x.Slot == n);
                    if (b == null) continue;
                    DrawLine(a.Footprint.Position + new Vector2(TileW / 2f, TileH / 2f),
                             b.Footprint.Position + new Vector2(TileW / 2f, TileH / 2f),
                             new Color(0.95f, 0.85f, 0.35f, 0.8f), 2f);
                }
            }

        // player-start markers (diamonds)
        if (_showDots || _markers.Count > 0)
            foreach (var m in _markers)
            {
                var c = m.Footprint.Position + new Vector2(TileW / 2f, TileH / 2f);
                DrawDiamond(c, 8f, MarkerColor);
            }

        // buildings (toggle T): owner-coloured flag + base name and type name;
        // mission targets get a red ring so the win conditions are visible
        if (_showBuildings)
        {
            var font = ThemeDB.FallbackFont;
            foreach (var b in _entities)
            {
                if (!b.IsBuilding) continue;
                var c = b.Pos;
                var bc = b.Owner >= 0 && b.Owner < Factions.Length
                    ? Factions[b.Owner] : new Color(0.8f, 0.8f, 0.85f);
                if (b.Dead) bc = new Color(0.35f, 0.35f, 0.35f);
                if (b.IsTarget)
                    DrawArc(c, 13f, 0, Mathf.Tau, 20, new Color(1f, 0.2f, 0.2f, 0.9f), 2f);
                DrawLine(c, c + new Vector2(0, -16), bc, 1.5f);
                DrawColoredPolygon(
                    new[] { c + new Vector2(0, -16), c + new Vector2(11, -12), c + new Vector2(0, -8) },
                    bc);
                if (font != null && !string.IsNullOrEmpty(b.Name))
                    DrawString(font, c + new Vector2(4, -17),
                               $"{b.Name}·{BuildingTypeName(b.BType)}",
                               HorizontalAlignment.Left, -1, 11, new Color(1, 1, 1));
            }
        }

        if (_hovered >= 0 && _hovered != _selected)
            DrawRect(BodyRect(_entities[_hovered]), new Color(1, 1, 1, 0.9f), false, 2f);

        if (_selected >= 0)
        {
            var e = _entities[_selected];
            var col = e.IsProp ? PropColor : OwnerColor(e.Owner);
            var rect = BodyRect(e);
            DrawRect(rect, new Color(col.R, col.G, col.B, 0.30f));
            DrawRect(rect, col, false, 2.5f);
        }

        // muzzle flashes, explosions and tracers (ANIM.CWA)
        DrawEffects(ground: false);
        DrawOrderMarks();
        DrawCaptureBars();

        // the fog goes over the battlefield and under the selection marks: what
        // is not watched is dimmed, what was never seen is black
        if (FogActive && _fog != null)
        {
            if (_fogTex == null || _fogDrawn != _fog.Version) BuildFogTexture();
            if (_fogTex != null) DrawTextureRect(_fogTex, _fogRect, false);
        }

        // rubber-band selection rectangle
        if (_band is { } band)
        {
            var b = band.Abs();
            DrawRect(b, new Color(0.3f, 1f, 0.5f, 0.12f));
            DrawRect(b, new Color(0.3f, 1f, 0.5f, 0.9f), false, 1.5f);
        }
    }

    private void DrawDiamond(Vector2 c, float r, Color col)
    {
        var pts = new[]
        {
            c + new Vector2(0, -r), c + new Vector2(r, 0),
            c + new Vector2(0, r), c + new Vector2(-r, 0),
        };
        DrawColoredPolygon(pts, new Color(col.R, col.G, col.B, 0.35f));
        DrawPolyline(new[] { pts[0], pts[1], pts[2], pts[3], pts[0] }, col, 2f);
    }
}
