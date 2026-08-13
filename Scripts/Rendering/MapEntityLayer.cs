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

        /// <summary>
        /// Traegt diese Einheit ueberhaupt eine WAFFE? Satzfeld <b>+0x0d</b>.
        ///
        /// <para><see cref="Weapon"/> ist der AUFSATZ (+0x0c) und bei einem
        /// Techniker 47 oder 48 — also ungleich 0, obwohl er nichts schiessen
        /// kann. Das Spiel fuehrt "bewaffnet" darum in einem eigenen Feld:</para>
        /// <list type="bullet">
        /// <item>Aufstell-Weiche C @0x4B1B6E / F @0x4B14AA: <c>cmp cl,0x32</c>
        /// auf die Waffenzeile des Entwurfs (+0x17). Zeile &gt;= 50 -&gt;
        /// +0x0d = 0 und die Zeile wandert nach +0x0e; Zeile &lt; 50 -&gt; die
        /// Waffe steht in +0x0d. Geschuetze sind die Zeilen 1..19,
        /// AUSRUESTUNG 65..79, Handwaffen 185..199.</item>
        /// <item>Einheitentakt C @0x40DDF0..0x40DE20 / F @0x40DC20..0x40DC4A:
        /// <c>mov al,[u+0x0d]; test al,al; jne &lt;Kampfblock&gt;</c> — eine
        /// Null in +0x0d heisst: kommt gar nicht erst in den Kampf.</item>
        /// </list>
        /// <para>Gegenprobe ueber alle 586 Entwuerfe und alle 30 Karten, ohne
        /// Gegenbeispiel: 1226 Einheiten mit Aufsatz 21..39 tragen
        /// +0x0d = Waffenzeile; 218 Einheiten mit Aufsatz 40..54 tragen
        /// +0x0d = 0 und durchweg Angriff 0, Reichweite 0, Munition 0.</para>
        ///
        /// <para>⚠ Gemeldet am 11.08.2026 als »in der Kampagne 1 gibt es 3
        /// Fahrzeuge, sieht aus als haetten die einen Bauturm drauf. die fahren
        /// dann auch aggressiv auf einen zu«. Genau das taten sie: sie kamen
        /// durch <c>CanFight</c>, bekamen ein Ziel — und WeaponOf erfand ihnen
        /// dazu noch eine Waffe mit 10 Schaden.</para></summary>
        public bool Armed;

        /// <summary>
        /// Die VARIANTE eines Schiffes — dasselbe Satzfeld <b>+0x0d</b>, das bei
        /// einem Landfahrzeug nur <see cref="Armed"/> ist. Bei einem Schiff steht
        /// dort eine Zahl, und sie wird gebraucht: der Bilderzweig des Originals
        /// (0x450A97) sieht sie an, um die zwei Entwürfe zu trennen, die sich den
        /// Rumpf 151 TEILEN — siehe <see cref="UI.PortraitBank.PictureOfShip"/>.
        /// −1 heisst »kein Schiff« oder »kein Rohsatz«.
        ///
        /// <para><b>Gemessen, nicht gesetzt.</b> Der Erzeuger @0x4B2B20 schreibt
        /// das Byte <c>+0x18</c> des SHIP_PROD-Satzes nach <c>+0x0d</c>
        /// (GAMESTATE_RE.md, »The SHIP_PROD record«), und über alle 30 Karten
        /// stimmt es bei <b>97 von 97</b> gesetzten Schiffen mit der Variante des
        /// Entwurfs desselben Rumpfes zusammen, ohne ein Gegenbeispiel:
        /// Rumpf 150 → 4 (23 Stück), 151 → 6 (33), 152 → 1 (8), 153 → 0 (13),
        /// 157 → 7 (8), 158 → 8 (12) — und genau diese sechs Zahlen trägt
        /// <c>ships.json</c> in den Feldern »variant« der Entwürfe 0,1,2,3,8,9.
        /// </para></summary>
        public int ShipVariant = -1;

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

        /// <summary>
        /// Der Satz steht in der Gebaeudetabelle, aber es steht KEIN BAUWERK
        /// darauf: sein Typ liegt ausserhalb 1..16.
        ///
        /// <para>⚠ 11.08.2026 — gemeldet als »in Kampagne 2 ist INIT7 Typ 0
        /// immer noch ein Basisgebaeude in der Stadt«. Auf map_02 tragen sieben
        /// der elf Saetze Typen ausserhalb der Namentabelle (0x4FDCC4, 16
        /// Eintraege zu 20 Byte): dreimal 51 mit Terranium 10, je einmal 22, 25
        /// und 26 mit Terranium 100 — das sind VORKOMMEN — und Platz 7 mit
        /// Typ 0.
        ///
        /// <b>Typ 0 heisst im Original woertlich »kein Gebaeude«.</b>
        /// <c>obj_owner(platz)</c> @0x4D076D:</para>
        /// <code>
        ///   ecx = platz * 19
        ///   bl  = byte[ecx*4 + 0xC06914]     ; Satz +0x00
        ///   if (bl == 0) return 0x0C         ; 12 = kein Gebaeude
        ///   return byte[ecx*4 + 0xC06915]    ; +0x01 = Besitzer
        /// </code>
        /// <para>Satz +0x00 ist der TYP — dieselbe Stelle prueft
        /// <c>find_base</c> mit <c>cmp byte[76*i + 0xC06914], 1</c> auf »ist
        /// das eine Basis«. Ein Satz mit Typ 0 existiert also nicht.
        ///
        /// Die Saetze bleiben trotzdem in der Liste, denn die Missionsskripte
        /// fragen sie ab: <c>obj_owner</c> muss fuer Platz 4 weiterhin 255
        /// antworten (Typ 51, ungleich 0) und fuer Platz 7 die 12. Sie werden
        /// nur nicht mehr GEZEICHNET, nicht mehr ANGEWAEHLT und nicht mehr
        /// mitgezaehlt.</para>
        /// </summary>
        public bool NoStructure;
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
        public string KindName => AirKindName(Kind);

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

    /// <summary>Der Name eines Flugzeug-Kinds — aus der Vorlagentabelle
    /// 0x51b021, Spalte <c>+0x2d</c>, siehe <see cref="Special.IsSupply"/>.
    ///
    /// <para>⚠ <b>Zu 11 und 12 gibt es in diesem Baum ZWEI Lesungen, und sie
    /// widersprechen sich:</b> hier (und damit im Spiel) ist 11 der
    /// Mechanikerheli und 12 der Transport, in
    /// <c>Assets/Legacy/Maps/aircraft.json</c> (geschrieben von
    /// <c>aircraft_export.py</c>) ist es umgekehrt. Der Streit ist ALT und wird
    /// hier nicht entschieden. Fuer das BILD ist er auch ohne Belang: die Tafel
    /// @0x450DC8 haengt am Byte, nicht am Namen — Kind 11 bekommt ein Bild,
    /// Kind 12 keines, wie auch immer die zwei heissen. Wer den Streit beenden
    /// will, hat damit sogar die billigste Gegenprobe der Welt: im Original
    /// EINEN von beiden anwaehlen und sehen, ob im Bedienblock ein Bild
    /// steht.</para></summary>
    public static string AirKindName(int kind) => kind switch
    {
        1 => "Jagdflieger", 2 => "Bomber", 3 => "Spionageflieger",
        10 => "Kampfhubschrauber", 11 => "Mechanikerheli", 12 => "Transport Heli",
        13 => "Treibstoffheli", 14 => "Munitionheli",
        _ => $"Art {kind}",
    };

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

    /// <summary>Der angewählte FLUGZEUGPLATZ — die Zeile in <see cref="_special"/>,
    /// oder −1. Flugzeuge sind bei uns keine <see cref="Entity"/>, deshalb
    /// braucht die Auswahl ihren eigenen Zeiger; im Original ist es dieselbe
    /// Auswahl, nur mit einer Objektnummer ab 0x4E20 (20000) statt darunter,
    /// und der Bedienblock zieht daran seinen Fall 3 (0x470C09/0x470C41).
    /// Er schliesst <see cref="_sel"/> aus: entweder Einheiten oder ein
    /// Flugzeug.</summary>
    private int _selAir = -1;
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

    /// <summary>
    /// Wer den Nebel aufdeckt, und mit welchem Radius.
    ///
    /// <para>⚠ 11.08.2026 — der vierte Wert, die HOEHE, ist neu. Die Nebelrunde
    /// des Originals (@0x4205B0) rechnet fuer eine LANDEINHEIT
    /// <c>radius = elev(zelle) + sicht - 1</c>; der Block dazu steht bei
    /// 0x4206FA..0x4207E1:</para>
    /// <code>
    ///   0x4207AF  call 0x41D0E0            ; Hoehe der Zelle, byte[Kachel+2]
    ///   0x4207BC  mov cl, byte [satz+0x2c] ; der Sichtwert der Einheit
    ///   0x4207C8  add ax, cx
    ///   0x4207CF  dec ax
    ///   0x4207D4  call 0x4200C0            ; stamp(spalte, zeile, ax)
    /// </code>
    /// <para>Auf beiden Fassungen nach der FORM gefunden (Bytefolge
    /// <c>81 e1 ff 00 ff ff 66 03 c1 8b 4c 24 18 66 48 50 51 53</c>, je genau
    /// einmal: C @0x4207C2, F @0x41F982). Gelesen und belegt hat das der
    /// Nebel-Durchgang vom 11.08.2026 (FogGrid.UnitRadius, Commit 38e04ba);
    /// hier haengt nur der Anschluss.</para>
    ///
    /// <para><b>GEBAEUDE rechnen die Hoehe NICHT mit</b> und bekommen auch das
    /// <c>dec</c> nicht: ihr Radius ist ein woertliches <c>push 0xa</c>
    /// @0x4206AB. Sie kommen darum mit <c>Elev = 1</c> herein, denn
    /// <c>UnitRadius(10, 1) = 10 + 1 - 1 = 10</c> — dieselbe Zahl, ohne einen
    /// zweiten Weg durch die Nebelrunde.</para>
    /// </summary>
    private IEnumerable<(int Col, int Row, int Sight, int Elev)> Watchers()
    {
        foreach (var e in _entities)
        {
            if (e.Dead || e.IsProp) continue;
            if (e.Owner != ViewPlayer) continue;
            if (!e.IsBuilding)
            {
                yield return (e.Col, e.Row, e.Sight > 0 ? e.Sight : 4,
                              ElevOf(e.Col, e.Row));
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
            // Elev = 1 haelt UnitRadius bei genau BuildingSightRadius — siehe
            // den Kopf dieser Methode.
            yield return (e.Col + half.X, e.Row + half.Y, s, 1);
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
        _selAir = -1;
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
                 $"{_entities.FindAll(x => x.IsBuilding && !x.NoStructure).Count} Gebaeude " +
                 $"(+{_entities.FindAll(x => x.NoStructure).Count} Skriptplaetze ohne Bauwerk) ({_source}); " +
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
                    // +0x0d ist die Waffenfahne des Spiels — siehe Entity.Armed
                    Armed = haveRaw && HexByte(raw, 0x0d) != 0,
                    // ... und bei einem SCHIFF dasselbe Byte als Zahl: die
                    // Variante, die das Bild entscheidet — siehe Entity.ShipVariant
                    ShipVariant = haveRaw && NavalTypes.Contains(GetI(e, "unit_type", -1))
                        ? HexByte(raw, 0x0d) : -1,
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
                // Gueltige Gebaeudetypen sind 1..16 — soviele Eintraege hat
                // die Namentabelle 0x4FDCC4 (16 zu 20 Byte). Alles andere ist
                // ein Skriptplatz oder ein Vorkommen und kein Bauwerk.
                bld.NoStructure = bld.BType is < 1 or > 16;
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
        _lineCell.Clear();
        _lineCellFrame.Clear();
        _linePath.Clear();
        _lineCellPiece.Clear();
        _lineCellBroken.Clear();
        _railLines.Clear();
        _freightWagons.Clear();
        _bldBySlot.Clear();
        _railStart.Clear();
        _hasRail = false;
        _railCells.Clear();
        _railTiles = null;
        // sec22 — DIE STRECKE, wie die Karte sie selbst fuehrt. Muss VOR den
        // Linien gelesen werden: RailAdoptCells() setzt daraus die Zellenketten,
        // und die Schleife darunter baut sie nur noch dort, wo sec22 nichts hat.
        if (root.TryGetValue("rail_cells", out var rcv) && rcv.VariantType == Variant.Type.Array)
            foreach (var item in rcv.AsGodotArray())
            {
                if (item.VariantType != Variant.Type.Array) continue;
                var q = item.AsGodotArray();
                if (q.Count < 5) continue;
                _railCells.Add(new RailCell
                {
                    Index = q[0].AsInt32(), Col = q[1].AsInt32(), Row = q[2].AsInt32(),
                    Frame = q[3].AsInt32(), Line = q[4].AsInt32(),
                    Hp = q.Count > 5 ? q[5].AsInt32() : 150,
                });
            }
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
                        // Die Route auf halben Zeilen ist NICHT die Strecke —
                        // ein Gleisbild ist eine ganze Zelle. Siehe RailBuildCells.
                        RailBuildCells(lineNo, pts, pcs);
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

        // ⚠ 13.08.2026 — die STRECKE kommt jetzt aus sec22, nicht mehr aus
        // unserer Ableitung. RailAdoptCells() misst zuerst, wie weit die beiden
        // auseinanderliegen (die Zahl steht in --rail-check), und ersetzt dann
        // die abgeleiteten Ketten durch die der Karte. Was sec22 nicht kennt,
        // bleibt bei der Ableitung — auf den 30 Karten ist das nichts.
        RailAdoptCells();

        // Die Enden auf die Anschlusszeile der Gebaeude fuehren. Muss NACH der
        // Schleife stehen: erst dort sind alle Linien samt ihren Endgebaeuden da.
        // ⚠ Mit sec22 ist das RUECKEN gegenstandslos — die Karte legt das Ende
        // selbst dorthin, wo es hingehoert. RailSnapToDock ruehrt eine Kette aus
        // sec22 darum nicht mehr an und MISST nur noch (siehe dort).
        RailSnapToDock();

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
        FillSkirmishAirDesigns();

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
        // ⚠ 13.08.2026 — DIE WERTE WERDEN JETZT BEHALTEN, nicht nur eingefaerbt.
        // sec2 ist die Tafel, an der `corners_carry` @0x4211A0 den BAUPLATZ
        // entscheidet; solange sie nur eine Textur war, konnte die Bauplatzpruefung
        // sie nicht lesen. Siehe ZoneAt.
        _zone = new byte[h, w];
        _zoneW = w;
        _zoneH = h;
        for (int r = 0; r < h && r < rows.Count; r++)
        {
            if (rows[r].VariantType != Variant.Type.Array) continue;
            var cells = rows[r].AsGodotArray();
            for (int c = 0; c < w && c < cells.Count; c++)
            {
                int z = cells[c].AsInt32();
                _zone[r, c] = (byte)Mathf.Clamp(z, 0, 255);
                img.SetPixel(c, r, z >= 0 && z < ZoneColors.Length ? ZoneColors[z] : ZoneColors[0]);
            }
        }
        _zoneTex = ImageTexture.CreateFromImage(img);
        _zoneRect = new Rect2(ox, oy, w * TileW, h * TileH);
    }

    /// <summary>
    /// Die sec2-Tafel (0xA3AEB0 in der untersuchten Fassung, 0xA39F10 auf F:),
    /// 257x257 Byte, Index <c>Spalte*257 + Zeile</c>.
    ///
    /// <para>Das Original nennt sie nicht »Hoehe«: Klasse 0 ist unpassierbar
    /// (in allen 23 Karten jede Wasserkachel, 0 Gegenbeispiele), 1 Ufer/Sand,
    /// 2 offenes Land, 3 besonderes Land (CwmData.Zones). Genau daran entscheidet
    /// <c>corners_carry</c> den Bauplatz.</para>
    ///
    /// <para>⚠ Ausserhalb der eingespielten Breite/Hoehe gibt es <b>-1</b>. Die
    /// Datei fuehrt 257x257 Punkte, unser Ausschnitt nur Breite x Hoehe; am
    /// letzten Rand fehlt uns der vierte Eckpunkt. Ein Bauplatz dort wird darum
    /// ABGELEHNT — lieber ein Platz zu wenig als einer, der auf einem Wert
    /// steht, den wir nicht haben.</para>
    /// </summary>
    public int ZoneAt(int col, int row)
    {
        if (_zone == null || col < 0 || row < 0 || col >= _zoneW || row >= _zoneH) return -1;
        return _zone[row, col];
    }

    /// <summary>Hat diese Karte ueberhaupt eine sec2-Tafel? Ohne sie kann die
    /// Bauplatzpruefung ihre vierte Frage nicht stellen, und das muss sie sagen
    /// statt stillschweigend alles zu erlauben.</summary>
    public bool HasZones => _zone != null;

    private byte[,]? _zone;
    private int _zoneW, _zoneH;

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
    /// <param name="unitType">Wenn &gt;= 0, kommen nur Einheiten dieses
    /// <c>unit_type</c> in die Auswahl. ⚠ Dazugekommen fuer die SCHIFFE: die 14
    /// Schiffe von map_DM_3 stehen unter 101 bewaffneten Einheiten, und ohne
    /// diesen Filter waere nicht zu sagen, welches <paramref name="which"/> auf
    /// einem Rumpf landet. Ein Bildschirmfoto vom Schiffsbild braucht aber genau
    /// das — dieselbe Luecke, die beim Flugzeugplatz zu schliessen war.</param>
    /// <param name="buildings">⚠ Dazugekommen fuer die GEBÄUDE: ein
    /// Bildschirmfoto soll belegen, dass der Bedienblock fuer ein Gebaeude KEIN
    /// Bild zeigt — und das laesst sich nur photographieren, wenn eines
    /// angewaehlt werden kann. Mit <c>true</c> waehlt dieselbe Stelle das
    /// n-te Gebaeude statt der n-ten bewaffneten Einheit.</param>
    public string SelectForShot(int which, int unitType = -1, bool buildings = false)
    {
        var pick = new List<int>();
        for (int i = 0; i < _entities.Count; i++)
        {
            var e = _entities[i];
            if (e.IsProp || e.Dead) continue;
            if (buildings)
            {
                // NoStructure steht fuer die Skripte, nicht auf dem Schirm —
                // dieselbe Regel wie bei Pick.
                if (e.IsBuilding && !e.NoStructure) pick.Add(i);
                continue;
            }
            if (e.IsBuilding) continue;
            if (unitType >= 0 && e.UnitType != unitType) continue;
            if (e.Weapon != 0 || e.FuelMax > 0) pick.Add(i);
        }
        if (pick.Count == 0)
            return buildings
                ? "select: kein Gebaeude auf dieser Karte"
                : unitType >= 0
                    ? $"select: keine Einheit mit unit_type {unitType} (und Waffe oder Tank)"
                    : "select: keine Einheit mit Waffe oder Tank";
        int idx = pick[Mathf.PosMod(which, pick.Count)];
        _sel.Clear();
        _sel.Add(idx);
        SetPrimary();
        UpdatePanel();
        QueueRedraw();
        var s = _entities[idx];
        if (buildings)
            return $"select: GEBAEUDE Platz {s.Slot}, Art {s.BType}, Feld " +
                   $"{s.Col},{s.Row}, Besitzer {s.Owner}  |  Bild: " +
                   $"{PanelPortrait().Why}  |  Panel: " +
                   _panel.Text.Replace("\n", " / ");
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
        // Einheiten und Flugzeug schliessen sich aus — an EINER Stelle, damit
        // keiner der zwanzig Aufrufer daran denken muss.
        if (_sel.Count > 0) _selAir = -1;
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
        // Ein FLUGZEUG gewinnt nur, wo keine Einheit und kein Gebäude liegt: es
        // fliegt über allem, und wer auf einen Panzer klickt, meint den Panzer.
        if (hit < 0 && !additive)
        {
            int air = PickAir(mapPos);
            if (air >= 0)
            {
                _sel.Clear();
                _selAir = air;
                SetPrimary();
                QueueRedraw();
                return;
            }
        }
        _selAir = -1;
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
                if (e.IsBuilding && !e.IsProp && !e.NoStructure) _buildingOrder.Add(e);
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

    public void ClearSelection() { _sel.Clear(); _selAir = -1; SetPrimary(); QueueRedraw(); }

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

    /// <summary>
    /// Was der Bedienblock unten links als BILD zeigen soll — der Wunsch des
    /// Spielers: »kleine bilder, die man unten links im HUD gesehen hat, wenn
    /// die Einheit angewaehlt war«.
    ///
    /// <para><b>Gelesen.</b> Der Zeichner des Bedienblocks (Fenstertyp-Byte 9,
    /// 0x46FE10..0x4711C7) ruft den Bildzeichner 0x4508A0 an zwei Stellen:
    /// <c>0x4701A9</c> mit <c>(kind, entwurf, surf, 0x0B, 0x3D)</c> — also
    /// <b>(11, 61)</b> im 204x170-Block — und <c>0x470C41</c> mit kind 3 an
    /// derselben Stelle fuer den Flugzeugplatz. Welcher Fall gezogen wird,
    /// entscheidet ein Klassenbyte <c>byte[0x6E26D2 + 78*id]</c> (0..5):
    /// 0/1 -> kind 0, 2 -> KEIN Bild, 3 -> kind 4, 4/5 -> kind 1. Fall 0 nimmt
    /// den sec47-Satz des Entwurfs und blittet <c>+0x18</c> (Fahrwerk) und
    /// darueber <c>+0x17</c> (Waffe).</para>
    ///
    /// <para>⚠ <b>Bei einer GRUPPE zeigt das Original KEIN Bild.</b> Der
    /// Gruppen-Zweig 0x47067A..0x470AB1 hat keinen Aufruf von 0x4508A0, sondern
    /// sechs Textzeilen bei y = 45/58/71/84/97 mit einer zweiten Spalte ab
    /// x = 90. Dass hier bei mehreren gewaehlten Einheiten (0,0) herauskommt,
    /// ist Treue.</para>
    ///
    /// <para><b>Fall 3 — der FLUGZEUGPLATZ — ist seit dem 13.08.2026 dabei.</b>
    /// Der Bedienblock ruft ihn bei <c>0x470C41</c> fuer jedes Objekt mit einer
    /// Nummer ab <c>0x4E20</c> (20000) auf, also fuer die sec19-Plaetze; die
    /// Kette vom Typbyte zum Bild steht bei
    /// <see cref="UI.PortraitBank.PictureOfAircraft"/>. <b>Sieben der vierzehn
    /// Kinds bekommen dort KEIN Bild</b>, und das ist kein Mangel, sondern die
    /// Tafel <c>@0x450DC8</c>.</para>
    ///
    /// <para>⚠ <b>UNSERE EINSCHRAENKUNG, und sie ist Absicht:</b> von den
    /// Landeinheiten zeichnen wir nur Fall 0 und nur fuer ein LANDFAHRWERK
    /// (unit_type 160..175). Was die fuenf Klassen des Klassenbytes bedeuten, ist
    /// UNGELESEN. ⚠ Und <c>+0x0D</c> darf hier NICHT als Bildnummer durchgehen:
    /// wer das tut, bekommt die Nummern 70..76 und 100..102, und das sind in
    /// Fall-5-Zaehlung Flugzeug- und Personenbilder.</para>
    ///
    /// <para><b>Die SCHIFFE (unit_type 150..158) sind seit dem 13.08.2026
    /// dabei.</b> Fall 1 nimmt Folge 401 ueber
    /// <c>byte[0x52EDB7 + 42*(Entwurf + 10*Spieler)] − 0x96</c> und die
    /// SCHALTERTAFEL @0x450D60 — die »10er-Permutation«, die hier bis heute als
    /// ungelesen stand, ist gelesen und steht bei
    /// <see cref="UI.PortraitBank.PictureOfShip"/>. Sie ist keine Bytetafel,
    /// sondern zehn Codeadressen, und ihr zweiter Fall (der Rumpf <b>151</b>, den
    /// sich L.Kreuzer und Flak-Barkasse TEILEN) sieht noch auf die Variante.
    /// <b>Alle ZEHN Rumpffaelle bekommen ein Bild</b>, jedes genau einmal; nur
    /// der Rumpf 159 zeigt ueber das Ende der Folge hinaus und wird von keinem
    /// Entwurf benutzt.</para>
    ///
    /// <para><b>Die INFANTERIE (unit_type 148/149) ist seit dem 13.08.2026
    /// dabei.</b> Das Original nimmt Folge 403 ueber die Bytetafel @0x450CCC mit
    /// <c>entwurf − 0x32</c> und die Schaltertafel @0x450C98; die Kette steht bei
    /// <see cref="UI.PortraitBank.PictureOfInfantry"/>, der Weg vom Einheitensatz
    /// zur Entwurfsnummer bei <see cref="InfantryDesignOf"/>. <b>Alle ZWOELF
    /// Entwuerfe bekommen ein Bild</b> — die Tafel trägt fuer sie die Werte 0…11
    /// und fuer alle 133 anderen Indizes die 12, den Fehlerzweig »Wrong index of
    /// infantry«. Ein zweites Bild gibt es hier nicht: 0x450A2F blittet
    /// einmal.</para>
    /// </summary>
    /// <para><b>Und die Bildnummern stehen im Einheitensatz selbst.</b> Das ist
    /// der Fund dieses Bauabschnitts: <c>+0x0b</c> (das <c>spodek</c> des
    /// Spiels, bei uns <see cref="Entity.Chassis"/>) und <c>+0x0c</c> (der
    /// AUFSATZ, bei uns <see cref="Entity.Weapon"/>) sind BEREITS Bildnummern,
    /// nicht Bauteilzeilen. Nachgezaehlt ueber 968 Landeinheiten auf sieben
    /// Karten: <c>component_stats[unit_type][+0x0D] == raw[+0x0b]</c> in
    /// <b>956</b> Faellen; die 12 Abweichungen sind alle
    /// <c>(unit_type 161, Bild 2, raw 0)</c> und stehen samt und sonders auf
    /// map_neu01, einer selbst gebauten Karte, deren spodek-Byte nie gefuellt
    /// wurde. Und <c>+0x0c</c> nimmt ueber dieselben Karten genau die Werte
    /// 0, 21..39 und 40..52 an — das sind Zeichen fuer Zeichen die Bildnummern
    /// der Aufbauteile (Zeile 1..19 -> Bild 21..39) und der Verbesserungen
    /// (Zeile 65..79 -> Bild 40..54). Kein einziger Wert liegt daneben.
    /// Das bestaetigt die Zuordnung des Berichts aus einer dritten Richtung und
    /// heisst: fuer eine Einheit auf der Karte braucht es den Umweg ueber
    /// component_stats gar nicht.</para>
    ///
    /// <para>Der Umweg bleibt als RUECKFALL, und er wird gebraucht: wo
    /// <c>+0x0b</c> 0 ist (die zwoelf von map_neu01, und alles, was wir selbst
    /// in die Welt setzen), liefert <c>UnitStatBook.IconOf(unit_type)</c> die
    /// Nummer.</para>
    /// <returns>Die BILDNUMMERN von Fahrwerk und Aufsatz des einzeln gewaehlten
    /// Landfahrzeugs, wieviel insgesamt gewaehlt ist, und der Grund, wenn kein
    /// Bild herauskommt.</returns>
    public (int ChassisPic, int TurretPic, int Selected, string Why) PanelPortrait()
    {
        int n = _sel.Count;
        // ---- Fall 3: der FLUGZEUGPLATZ (0x470C41) ---------------------------
        // Ein Bild, kein zweites: die Folge 402 hat pro Flugzeug genau eines,
        // hier wird nichts uebereinandergelegt.
        if (_selAir >= 0 && _selAir < _special.Count)
        {
            var a = _special[_selAir];
            int pic = UI.PortraitBank.PictureOfAircraft(a.Kind);
            return pic > 0
                ? (pic, 0, 1, "")
                : (0, 0, 1, UI.PortraitBank.AirTrouble(a.Kind));
        }
        if (_selected < 0 || _selected >= _entities.Count) return (0, 0, n, "nichts gewaehlt");
        if (n > 1) return (0, 0, n, "Gruppe — das Original zeigt hier kein Bild");
        var e = _entities[_selected];
        if (e.IsProp) return (0, 0, n, "Kulisse");
        // ---- GEBÄUDE: kein Bild, und zwar ORIGINALTREU ----------------------
        // Nicht »ungelesen« (so stand es hier bis zum 13.08.2026) und auch nicht
        // das Klassenbyte 0x6E26D2: das gehört dem EINHEITENSATZ und ein Gebäude
        // hat gar keinen. Die drei Messungen, die das tragen, stehen bei
        // PortraitBank.BuildingTrouble — kurz: der Zeichner 0x4508A0 hat sechs
        // Fälle und keiner nimmt ein Gebäude, und der Anwählgriff word[0x4FA0C8]
        // kennt Gebäude überhaupt nicht.
        if (e.IsBuilding) return (0, 0, n, UI.PortraitBank.BuildingTrouble());
        if (e.UnitType is 148 or 149)
        {
            // ---- der Infanteriezweig (Fall 0 ab 0x45099B) --------------------
            // EIN Bild, kein zweites: 0x450A2F blittet einmal. Die Kette vom
            // Entwurf zum Bild steht bei PortraitBank.PictureOfInfantry, der Weg
            // vom Satz zum Entwurf bei InfantryDesignOf.
            int design = InfantryDesignOf(e.Infantry);
            if (design < 0)
                return (0, 0, n, $"Fusssoldat mit Satz {e.Infantry}: kein Entwurf " +
                                 "dazu (Maps/infantry.json oder unit_designs.json fehlt)");
            int inf = UI.PortraitBank.PictureOfInfantry(design);
            return inf > 0
                ? (inf, 0, n, "")
                : (0, 0, n, UI.PortraitBank.InfTrouble(design));
        }
        if (NavalTypes.Contains(e.UnitType))
        {
            // ---- Fall 1: der SCHIFFSPLATZ (0x470188/0x4701A9) ----------------
            // EIN Bild, kein zweites: 0x450AF8 blittet einmal. Die Kette vom
            // Rumpf zum Bild steht bei PortraitBank.PictureOfShip.
            //
            // ⚠ UNSERE Setzung, und sie ist Absicht: das Original holt Rumpf und
            // Variante nicht aus der Einheit, sondern aus dem SHIP_PROD-Satz, den
            // das Feld +0x3e der Einheit nennt. Bei einem vom Stapel gelaufenen
            // Schiff kommt dasselbe heraus — @0x4B2B20 schreibt +0x17 nach +0x0f
            // und +0x18 nach +0x0d, die beiden Bytes SIND dieselben. Bei den
            // GESETZTEN Schiffen der Karten laufen die zwei Wege aber
            // auseinander: dort ist +0x3e durchweg `Rumpf − 150` und nicht der
            // Entwurfsplatz, nachgezaehlt an den 8 Schlachtschiffen (Rumpf 157,
            // +0x3e = 7) und den 12 Kreuzern (158, +0x3e = 8) auf den 30 Karten.
            // Wer denen ueber +0x3e ein Bild gibt, gibt dem Schlachtschiff das
            // der Flak-Barkasse. Wir nehmen darum die zwei Bytes der EINHEIT —
            // das Bild passt dann zu dem Rumpf, den der Spieler vor sich sieht.
            int ship = UI.PortraitBank.PictureOfShip(e.UnitType, e.ShipVariant);
            return ship > 0
                ? (ship, 0, n, "")
                : (0, 0, n, UI.PortraitBank.ShipTrouble(e.UnitType, e.ShipVariant));
        }
        if (e.UnitType is < 160 or > 175)
            return (0, 0, n, $"unit_type {e.UnitType} ist kein Landfahrwerk (160..175)");
        // erst das eigene Byte, dann der Rueckfall über die Bauteiltabelle
        int chassis = e.Chassis is >= 1 and <= 18
            ? e.Chassis : UI.UnitStatBook.IconOf(e.UnitType);
        if (chassis <= 0) return (0, 0, n, "Fahrwerk ohne Bildnummer");
        return (chassis, e.Weapon, n, "");
    }

    /// <summary>Die Gegenprobe zu <see cref="PanelPortrait"/>: stimmt das
    /// Bildnummern-Byte des Einheitensatzes (<c>+0x0b</c>) mit der Bildnummer
    /// ueberein, die die Bauteiltabelle fuer denselben <c>unit_type</c> nennt?
    /// Zwei getrennte Quellen, und wenn sie auseinanderlaufen, ist eine von
    /// beiden falsch gelesen.</summary>
    /// <returns>gleich, abweichend, und die ersten Abweichungen als Text</returns>
    public (int Same, int Differ, string Cases) PortraitIconCrossCheck()
    {
        int same = 0, differ = 0;
        var cases = new SortedDictionary<string, int>();
        for (int i = 0; i < _entities.Count; i++)
        {
            var e = _entities[i];
            if (e.IsProp || e.IsBuilding || e.UnitType is < 160 or > 175) continue;
            int fromTable = UI.UnitStatBook.IconOf(e.UnitType);
            if (e.Chassis == fromTable) { same++; continue; }
            differ++;
            string k = $"unit_type {e.UnitType}: Tabelle {fromTable}, Satz +0x0b {e.Chassis}";
            cases.TryGetValue(k, out int c);
            cases[k] = c + 1;
        }
        var sb = new System.Text.StringBuilder();
        foreach (var kv in cases) sb.Append($"{kv.Value}x [{kv.Key}] ");
        return (same, differ, sb.ToString().TrimEnd());
    }

    /// <summary>
    /// Prüfstand für die Bauteilbilder: er ZÄHLT, statt zu behaupten.
    ///
    /// <para>Drei Abschnitte, und jeder nennt Zahlen, die man nachrechnen kann:
    /// die BANK (wieviele Bilder, wo sie liegt), die BAUTEILE (wieviele der
    /// Sätze eine Bildnummer tragen, welche auf die »?«-Tafel 56 umgelenkt
    /// werden), und die ENTWÜRFE (wieviele zwei Bilder ergeben, wieviele eines,
    /// wieviele keines — mit Namen). Dazu die EINHEITEN DIESER KARTE, gruppiert
    /// nach dem Grund, aus dem <see cref="PanelPortrait"/> ihnen ein Bild gibt
    /// oder verweigert.</para>
    ///
    /// <para>⚠ Was dieser Prüfstand NICHT sehen kann: ob das Bild an der
    /// richtigen STELLE steht und ob es die richtigen Bildpunkte trägt. Die
    /// Stelle meldet <c>MapViewer</c> dazu (Feldecke und Grösse in
    /// Bildschirmpunkten), die Bildpunkte entscheidet ein Bildschirmfoto gegen
    /// den Ausschnitt des Originals — eine Zahl ersetzt das hier nicht.</para>
    /// </summary>
    public string PortraitCheck()
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("portrait-check: ").Append(UI.PortraitBank.WatchLine()).Append('\n');

        // ---- die Bauteilsätze (component_stats, Byte +0x0D) ------------------
        int withIcon = 0, without = 0;
        var toUnknown = new List<int>();
        var beyond = new List<int>();
        for (int row = 1; row < 200; row++)
        {
            int raw = UI.UnitStatBook.IconOf(row);
            if (raw == 0) { without++; continue; }
            withIcon++;
            if (raw == UI.PortraitBank.UnknownFrom) toUnknown.Add(row);
            else if (raw >= UI.PortraitBank.Count) beyond.Add(row);
        }
        sb.Append($"   Bauteile: {withIcon} mit Bildnummer, {without} ohne (Byte +0x0D = 0)")
          .Append('\n');
        sb.Append($"   auf die »?«-Tafel {UI.PortraitBank.Unknown} umgelenkt (icon == ")
          .Append(UI.PortraitBank.UnknownFrom).Append("): ")
          .Append(toUnknown.Count == 0 ? "keines" : string.Join(", ", toUnknown))
          .Append('\n');
        // ⚠ Bildnummern jenseits der Bank sind KEIN Fehler dieses Codes: das
        // Original fängt nur die 100 ab, 101 und 102 laufen dort ins Leere. Sie
        // gehören den Schiffsrümpfen, für die +0x0D ein totes Feld ist.
        sb.Append($"   Bildnummer jenseits der Bank (>= {UI.PortraitBank.Count}): ")
          .Append(beyond.Count == 0 ? "keine" : string.Join(", ", beyond))
          .Append(" — im Original ungefangen, betrifft nur Schiffsruempfe\n");

        // ---- die Entwürfe ---------------------------------------------------
        int two = 0, one = 0, none = 0, unknownPic = 0, infOwn = 0;
        var noneNames = new List<string>();
        var unknownNames = new List<string>();
        foreach (var d in UI.UnitStatBook.All())
        {
            int ic = UI.PortraitBank.IconOfComponent(d.Propulsion);
            int iw = UI.PortraitBank.IconOfComponent(d.Weapon);
            int n = (ic > 0 && ic < UI.PortraitBank.Count ? 1 : 0)
                  + (iw > 0 && iw < UI.PortraitBank.Count ? 1 : 0);
            if (n == 2) two++;
            else if (n == 1) one++;
            // ⚠ Die zwölf Infanterieentwürfe stehen hier NICHT als Lücke: sie
            // haben kein Bauteilbild, weil das Original sie über Fall 0 gar
            // nicht malt, sondern über den Infanteriezweig und Folge 403 (siehe
            // den Abschnitt »Infanterie« weiter unten). Bis zum 13.08.2026 hat
            // dieser Zähler sie mitgezählt und deshalb »13 ohne Bild« gemeldet;
            // 13 = die 12 Fußsoldaten PLUS »Mighty Mama«, ein Satz mit Waffe 0
            // UND Fahrwerk 0, der auch im Original nichts zu malen hat.
            else if (d.Propulsion is 148 or 149) infOwn++;
            else { none++; if (noneNames.Count < 12) noneNames.Add(d.Name); }
            if (ic == UI.PortraitBank.Unknown || iw == UI.PortraitBank.Unknown)
            {
                unknownPic++;
                if (unknownNames.Count < 12) unknownNames.Add(d.Name);
            }
        }
        sb.Append($"   Entwuerfe: {two} mit zwei Bildern (Fahrwerk + Aufbauteil), ")
          .Append($"{one} mit einem, {infOwn} Fusssoldaten (eigener Zweig, Folge 403), ")
          .Append($"{none} ohne\n");
        if (none > 0)
            sb.Append("   ohne Bild: ").Append(string.Join(", ", noneNames))
              .Append(none > noneNames.Count ? ", …" : "").Append('\n');
        sb.Append($"   auf der »?«-Tafel: {unknownPic}")
          .Append(unknownPic > 0 ? " (" + string.Join(", ", unknownNames) + ")" : "")
          .Append('\n');

        // ---- die FLUGZEUGE: alle 14 Kinds, dann die dieser Karte ------------
        //
        // Die Zahl, um die es geht: 14 Kinds, 7 Bilder. Sieben Kinds gehen leer
        // aus, und das ist die Tafel @0x450DC8, nicht unsere Lücke.
        var airWith = new List<string>();
        var airWithout = new List<string>();
        for (int k = 1; k <= 14; k++)
        {
            int pic = UI.PortraitBank.PictureOfAircraft(k);
            (pic > 0 ? airWith : airWithout).Add($"{k} {AirKindName(k)}" +
                (pic > 0 ? $" -> p{pic:00}" : ""));
        }
        sb.Append($"   Flugzeuge: {airWith.Count} der 14 Arten bekommen ein Bild ")
          .Append($"(Folge {UI.PortraitBank.AirSequence} ab p")
          .Append($"{UI.PortraitBank.FirstPictureOf(UI.PortraitBank.AirSequence):00}), ")
          .Append($"{airWithout.Count} keines\n");
        sb.Append("      mit Bild: ").Append(string.Join(", ", airWith)).Append('\n');
        sb.Append("      ohne Bild (Tafel @0x450DC8 sagt 7): ")
          .Append(string.Join(", ", airWithout)).Append('\n');

        // ---- die INFANTERIE: alle zwölf Entwürfe namentlich ----------------
        //
        // Die Zahl, um die es ging: der Prüfstand meldete »13 Entwuerfe ohne
        // Bild« gegen 12 Bilder in Folge 403. Die 13 ist die Länge der
        // SCHALTERTAFEL @0x450C98 — 12 Fälle plus der Fehlerzweig 0x4509B5. Hier
        // wird nachgezählt: zwölf Entwürfe, zwölf verschiedene Bilder.
        var infWith = new List<string>();
        var infWithout = new List<string>();
        var infPics = new SortedSet<int>();
        var infDesigns = new List<int>(UI.PortraitBank.InfantryDesigns());
        infDesigns.Sort();
        foreach (int nr in infDesigns)
        {
            int pic = UI.PortraitBank.PictureOfInfantry(nr);
            string nm = _designBySlot.TryGetValue(nr, out var dd) ? dd.Name : "?";
            if (pic > 0)
            {
                infPics.Add(pic);
                infWith.Add($"{nr} {nm} -> p{pic:00}");
            }
            else infWithout.Add($"{nr} {nm}: {UI.PortraitBank.InfTrouble(nr)}");
        }
        sb.Append($"   Infanterie: {infWith.Count} Entwuerfe mit Bild auf ")
          .Append($"{infPics.Count} verschiedene Bilder (Folge ")
          .Append($"{UI.PortraitBank.InfSequence} ab p")
          .Append($"{UI.PortraitBank.FirstPictureOf(UI.PortraitBank.InfSequence):00}), ")
          .Append($"{infWithout.Count} ohne\n");
        sb.Append("      ").Append(string.Join(", ", infWith)).Append('\n');
        if (infWithout.Count > 0)
            sb.Append("      ohne Bild: ").Append(string.Join(", ", infWithout)).Append('\n');
        // Und die Gegenrechnung: wieviele der 145 Tafelplätze der Fehlerzweig
        // hält. 145 − 12 = 133, und 12 + 1 Fehlerfall = die 13 der Schaltertafel.
        int infError = 0;
        for (int nr = UI.PortraitBank.InfFirstDesign;
             nr <= UI.PortraitBank.InfFirstDesign + UI.PortraitBank.InfLastIndex; nr++)
            if (UI.PortraitBank.PictureOfInfantry(nr) == 0) infError++;
        sb.Append($"      Tafel @0x450CCC: {UI.PortraitBank.InfLastIndex + 1} Plaetze, ")
          .Append($"{infWith.Count} Infanterie, {infError} mal der Fehlerzweig ")
          .Append($"»Wrong index of infantry« — {infWith.Count} + 1 Fehlerfall = die ")
          .Append("13 Eintraege der Schaltertafel @0x450C98\n");

        // ---- die SCHIFFE: die zehn Entwuerfe namentlich ---------------------
        //
        // Die Zahl, um die es hier geht: zehn Entwuerfe, zehn Bilder — und ob
        // wirklich jedes Bild GENAU EINMAL vergeben wird. Doppelt vergeben waere
        // das Warnzeichen: es hiesse, dass der doppelte Rumpf 151 nicht getrennt
        // wird und zwei Entwuerfe dasselbe Bild tragen.
        var shipWith = new List<string>();
        var shipWithout = new List<string>();
        var shipPics = new SortedDictionary<int, List<string>>();
        if (_shipDesigns == null)
            sb.Append("   Schiffe: keine Schiffsliste geladen (Maps/ships.json)\n");
        else
        {
            foreach (var d in _shipDesigns)
            {
                if (d.Player != 0) continue;          // acht gleiche Bloecke, einer reicht
                int pic = UI.PortraitBank.PictureOfShip(d.Chassis, d.Variant);
                string nm = $"{d.Index} {d.Name} (Rumpf {d.Chassis}, Var {d.Variant})";
                if (pic > 0)
                {
                    shipWith.Add($"{nm} -> p{pic:00}");
                    if (!shipPics.TryGetValue(pic, out var who))
                        shipPics[pic] = who = new List<string>();
                    who.Add(d.Name);
                }
                else shipWithout.Add($"{nm}: {UI.PortraitBank.ShipTrouble(d.Chassis, d.Variant)}");
            }
            sb.Append($"   Schiffe: {shipWith.Count} Entwuerfe mit Bild auf ")
              .Append($"{shipPics.Count} verschiedene Bilder (Folge ")
              .Append($"{UI.PortraitBank.ShipSequence} ab p")
              .Append($"{UI.PortraitBank.FirstPictureOf(UI.PortraitBank.ShipSequence):00}, ")
              .Append($"{UI.PortraitBank.PicturesIn(UI.PortraitBank.ShipSequence)} Stueck), ")
              .Append($"{shipWithout.Count} ohne  [{_shipSource}]\n");
            sb.Append("      ").Append(string.Join(", ", shipWith)).Append('\n');
            if (shipWithout.Count > 0)
                sb.Append("      ohne Bild: ").Append(string.Join(", ", shipWithout)).Append('\n');
            // das Warnzeichen: ein Bild an zwei Entwuerfe
            var twice = new List<string>();
            foreach (var kv in shipPics)
                if (kv.Value.Count > 1)
                    twice.Add($"p{kv.Key:00} an {string.Join(" UND ", kv.Value)}");
            sb.Append("      doppelt vergeben: ")
              .Append(twice.Count == 0 ? "keines" : string.Join("; ", twice)).Append('\n');
            // und die Tafel selbst: zehn Faelle, welcher zeigt wohin
            var cs2 = new List<string>();
            foreach (int ch in UI.PortraitBank.ShipChassis())
            {
                int p6 = UI.PortraitBank.PictureOfShip(ch, UI.PortraitBank.ShipDoubleVariant);
                int p0 = UI.PortraitBank.PictureOfShip(ch, 0);
                cs2.Add(p6 == p0
                    ? (p6 > 0 ? $"{ch}->p{p6:00}" : $"{ch}->KEIN")
                    : $"{ch}->p{p6:00}/p{p0:00}");
            }
            sb.Append("      Schaltertafel @0x450D60 (Rumpf->Bild, ")
              .Append("beim doppelten Rumpf Var 6/sonst): ")
              .Append(string.Join(" ", cs2)).Append('\n');
        }

        // ---- die GEBÄUDE: die Zahl, die »kein Bild« belegt ------------------
        //
        // ⚠ Hier wird eine ABWESENHEIT gezählt, und das ist der Grund, warum
        // dieser Abschnitt nicht nur eine Zahl nennt, sondern auch die sechs
        // Fälle des Zeichners aufzählt. Eine 0 allein wäre nicht zu
        // unterscheiden von einer Lücke unseres Codes.
        // Gezählt wird über die ECHTE Auswahl, nicht über eine nachgebaute Regel
        // — dieselbe Stelle, die auch ein Mausklick benutzt. _selected/_sel
        // werden weiter unten ohnehin gesichert und wiederhergestellt.
        int bldg = 0, bldgWith = 0;
        var bldgTypes = new SortedSet<int>();
        int keepSelBldg = _selected;
        var keepListBldg = new List<int>(_sel);
        for (int i = 0; i < _entities.Count; i++)
        {
            var b = _entities[i];
            if (!b.IsBuilding || b.IsProp || b.Dead) continue;
            bldg++;
            bldgTypes.Add(b.BType);
            _sel.Clear();
            _sel.Add(i);
            SetPrimary();
            if (PanelPortrait().ChassisPic > 0) bldgWith++;
        }
        _sel.Clear();
        foreach (int k in keepListBldg) _sel.Add(k);
        _selected = keepSelBldg;
        sb.Append($"   Gebaeude: {bldgWith} von {bldg} bekommen ein Bild — ")
          .Append("das Original hat KEINES fuer ein Gebaeude\n");
        sb.Append($"      Arten auf dieser Karte: {bldgTypes.Count} verschiedene (")
          .Append(string.Join(", ", bldgTypes)).Append(")\n");
        sb.Append("      Faelle des Zeichners 0x4508A0 (Sprungtafel @0x450C80, ")
          .Append($"»cmp eax,5; ja« davor, also genau {UI.PortraitBank.DrawerCases.Length}): ")
          .Append(string.Join(" · ", UI.PortraitBank.DrawerCases)).Append('\n');
        sb.Append("      kein Fall nimmt ein Gebaeude — roher Dword-Abtast ueber ")
          .Append("0x4508A0..0x450D00 findet 0 Treffer im Bereich der Gebaeudesaetze ")
          .Append("0xC06000..0xC08000 (Tafel 0xC06914, 76 Byte, 255 Saetze)\n");
        sb.Append("      und der Anwaehlgriff word[0x4FA0C8] kennt Gebaeude nicht: ")
          .Append("roher Abtast findet 18 Schreibstellen — 0xFFFF, 0x2710, und die ")
          .Append("Anwaehlroutine 0x4331E0, die nur Landeinheit (<0x1F40) und ")
          .Append("Flugzeugplatz (>=0x4E20) schreibt\n");
        sb.Append("      Aufrufstellen des Zeichners: 14 im ganzen .text (roher ")
          .Append("E8-Abtast ueber den einzigen Stummel 0x4023E2), in 5 der 48 ")
          .Append("Fensterarten; im Bedienblock (Art 9) genau ZWEI, 0x4701A9 ")
          .Append("Einheit und 0x470C41 Flugzeugplatz\n");
        sb.Append("      und die Gegenprobe: 11 der 48 Fensterarten LESEN die ")
          .Append("Gebaeudetafel (2, 5, 6, 8, 11, 18, 20, 21, 23, 27, 46) — der ")
          .Append("Bedienblock (Art 9) ist nicht darunter, 0 Treffer in seinen ")
          .Append("5048 Byte\n");
        sb.Append("      was der Bedienblock statt eines Bildes zeigt: Zweig ")
          .Append("0x470E76 druckt »Kontostand « (0x501CF0) an (11,45) und ")
          .Append("»Sprit gesamt « (0x501CE0) an (11,61) — das Bildfeld traegt ")
          .Append("dort TEXT\n");

        // ---- die Einheiten dieser Karte, nach dem Grund gruppiert -----------
        var byReason = new SortedDictionary<string, int>();
        int shown = 0;
        int keep = _selected;
        int keepAir = _selAir;
        var keepSel = new List<int>(_sel);
        for (int i = 0; i < _entities.Count; i++)
        {
            var e = _entities[i];
            if (e.IsProp || e.Dead) continue;
            // die ECHTE Auswahlmechanik ausüben, nicht ihre Regel nachbauen:
            // dieselbe Stelle, die auch ein Mausklick benutzt
            _sel.Clear();
            _sel.Add(i);
            SetPrimary();
            var p = PanelPortrait();
            if (p.ChassisPic > 0) shown++;
            else
            {
                string why = p.Why.Length > 0 ? p.Why : "kein Grund gemeldet";
                byReason.TryGetValue(why, out int c);
                byReason[why] = c + 1;
            }
        }
        // dieselbe Übung für die Flugzeugplätze dieser Karte — auch hier über
        // die echte Auswahl, nicht über eine nachgebaute Regel
        var airReason = new SortedDictionary<string, int>();
        int airShown = 0;
        for (int i = 0; i < _special.Count; i++)
        {
            if (_special[i].Dead) continue;
            _sel.Clear();
            _selAir = i;
            var p = PanelPortrait();
            if (p.ChassisPic > 0) airShown++;
            else
            {
                string why = p.Why.Length > 0 ? p.Why : "kein Grund gemeldet";
                airReason.TryGetValue(why, out int c);
                airReason[why] = c + 1;
            }
        }

        _sel.Clear();
        foreach (int i in keepSel) _sel.Add(i);
        _selected = keep;
        _selAir = keepAir;
        UpdatePanel();
        QueueRedraw();

        sb.Append($"   Einheiten dieser Karte: {shown} bekommen ein Bild");
        foreach (var kv in byReason) sb.Append($", {kv.Value}x \"{kv.Key}\"");
        sb.Append('\n');
        sb.Append($"   Flugzeugplaetze dieser Karte: {airShown} von ")
          .Append($"{_special.Count} bekommen ein Bild");
        foreach (var kv in airReason) sb.Append($", {kv.Value}x \"{kv.Key}\"");
        sb.Append('\n');

        // ---- die Gegenprobe zweier getrennter Quellen -----------------------
        var (cs, cd, cases) = PortraitIconCrossCheck();
        sb.Append($"   Gegenprobe Bildnummer (Bauteiltabelle +0x0D gegen ")
          .Append($"Einheitensatz +0x0b): {cs} gleich, {cd} abweichend")
          .Append(cd > 0 ? "  " + cases : "");
        return sb.ToString();
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
        // ⚠ 11.08.2026 — DER EIGENTLICHE FEHLER hinter »die Baufahrzeuge
        // fahren aggressiv auf einen zu«: der Rueckfall hat fuer JEDES
        // unbekannte Bauteil eine Waffe mit 10 Schaden und 5 Feldern
        // Reichweite ERFUNDEN, also auch fuer die Ausruestung 40..54. Ueber
        // alle Karten sind das 218 Einheiten, die damit tatsaechlich
        // geschossen haben. Ausruestung bekommt jetzt 0/0.
        if (IsEquipmentMount(comp)) return ($"BAUTEIL {comp}", 0, 0f);
        if (_weapons != null && _weapons.TryGetValue(comp, out var w)) return w;
        return ($"BAUTEIL {comp}", 10, 5f);
    }

    // Entities with hp_max 0 (unit_types 148/149, the "Leichter"/"Schwerer" size
    // classes) are scenery markers, not combatants — they neither shoot nor can
    // be shot at.
    private static bool CanFight(Entity e)
        // ⚠ 11.08.2026 — hier stand nur `e.Weapon != 0`, und e.Weapon ist der
        // AUFSATZ. Ein Baufahrzeug traegt Aufsatz 47 oder 48 und kam damit
        // durch. Massgeblich ist die Waffenfahne +0x0d (siehe Entity.Armed).
        // Wo die Rohbytes fehlen (gebaute Einheiten, aeltere Ausfuhren), bleibt
        // die abgeleitete Spanne: die Aufsaetze 40..54 sind die Ausruestung.
        => !e.IsProp && !e.Dead && e.Weapon != 0 && e.HpMax > 0 &&
           (e.Armed || !IsEquipmentMount(e.Weapon));

    /// <summary>Aufsaetze 40..54 sind AUSRUESTUNG, keine Waffen — die Abbildung
    /// Entwurfswaffe 65..79 -&gt; Aufsatz 40..54 ist gelesen, siehe
    /// <see cref="TurretOf"/>.</summary>
    private static bool IsEquipmentMount(int comp) => comp is >= 40 and <= 54;

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

    // ================= die Zahlen des Abschlussfensters ======================
    //
    // Das Original fuehrt sie IM SPIELERSATZ mit: Abschuesse +0x20, Verluste
    // +0x24 (siehe Player.Kills/Losses oben — Zaehlstellen @0x40cfbc, @0x428144
    // und @0x40b4d7, gedruckt @0x48571b hinter " / Verluste "). Bei uns kamen
    // die beiden Felder BISHER NUR AUS EINEM SPIELSTAND (sec53, gelesen in
    // Load()); eine frisch gestartete Kampagnenmission hat gar keinen
    // Spielersatz, also blieben sie 0. Darum hier ein Satz Laufzeitzaehler, der
    // dieselben Ereignisse mitschreibt.
    //
    // ⚠ UNSERE SETZUNG ist die AUFTEILUNG nach Klassen. Das Original zeigt vier
    // Zeilen (Bewaffnete / Unbewaffnete / Schiffe / Flugzeuge); WELCHES Feld es
    // dafuer liest, ist nicht gelesen. Wir teilen so:
    //   Schiffe    -> NavGrid.ArtIsShip(+0x0a), also Art 4/5 (@0x406669)
    //   Flugzeuge  -> die sec27-Saetze, bei uns `Special`
    //   Bewaffnete -> Waffenbauteil +0x0c != 0, sonst Unbewaffnete
    // Gebaeude zaehlen NICHT mit — im Bildschirmfoto des Originals hat Virgil
    // 15 Bewaffnete und 6 Unbewaffnete, das ist eine Armee, keine Basis.
    private const int EndClasses = 4;
    private readonly int[,] _lostClass = new int[8, EndClasses];
    private readonly int[] _killCount = new int[8];
    private readonly int[] _lossCount = new int[8];
    private readonly int[] _builtCount = new int[8];
    private readonly int[] _missionPay = new int[8];

    /// <summary>Welche der vier Zeilen eine Einheit fuellt. Flugzeuge kommen
    /// nicht hier durch — die sind <see cref="Special"/>, kein
    /// <see cref="Entity"/>.</summary>
    private static int EndClassOf(Entity e)
        => Simulation.NavGrid.ArtIsShip(e.Subclass) ? 2 : e.Weapon != 0 ? 0 : 1;

    /// <summary>Einen Todesfall in die Statistik schreiben: eine Verlust-Kerbe
    /// beim Besitzer, eine Abschuss-Kerbe beim Schuetzen. Gebaeude bleiben
    /// draussen (siehe oben), Requisiten sowieso.</summary>
    private void NoteKill(Entity victim, int by)
    {
        if (victim.Dead || victim.IsProp || victim.IsBuilding || victim.HpMax <= 0) return;
        if (victim.Owner is >= 0 and <= 7)
        {
            _lossCount[victim.Owner]++;
            _lostClass[victim.Owner, EndClassOf(victim)]++;
        }
        // Der Schuetze zaehlt nur, wenn er nicht auf die eigenen Leute
        // geschossen hat — sonst stuende ein Eigenbeschuss zweimal drin.
        if (by is >= 0 and <= 7 && by != victim.Owner) _killCount[by]++;
    }

    /// <summary>Eine Spalte der Tabelle im Abschlussfenster.</summary>
    public sealed class EndColumn
    {
        public string Name = "";
        public bool Human;
        /// <summary>false = die Spalte bleibt "--/--" (das Original hat acht
        /// Spalten, aber nur so viele Spieler, wie die Karte hergibt).</summary>
        public bool Used;
        public readonly int[] Alive = new int[EndClasses];
        public readonly int[] Lost = new int[EndClasses];
    }

    /// <summary>Alles, was das Fenster »Mission erfolgreich beendet« anzeigt.
    /// Was wir nicht sicher wissen, kommt als -1 heraus und wird dort leer
    /// gelassen statt erfunden.</summary>
    public sealed class EndReport
    {
        public string Mission = "";
        /// <summary>Die Missionszeit in SPIELMINUTEN — dieselbe Zahl, die das
        /// Bedienfeld unten links zeigt (siehe <see cref="MissionMinutes"/>).
        /// Das Original druckt daraus Stunden und Minuten, nicht Minuten und
        /// Sekunden: die Statistikseite liest Stundenbyte 0x8154E4 (@0x485410),
        /// haengt ":" (Zeichenkette 0x501d48) an und dann Minutenbyte 0x81AA2C
        /// (@0x4854c4), beide mit fuehrender Null aus 0x4f8004.</summary>
        public int Minutes;
        public EndColumn[] Columns = System.Array.Empty<EndColumn>();
        public int Built, Kills, Losses, Pay, Balance;
        public int SubDone = -1, SubTotal = -1;
    }

    /// <summary>Das Original hat acht Spalten in der Tabelle (Bildschirmfoto:
    /// zwei gefuellt, sechs "--/--").</summary>
    public const int EndColumnCount = 8;

    public EndReport BuildEndReport()
    {
        int me = ViewPlayer is >= 0 and <= 7 ? ViewPlayer : 0;
        var r = new EndReport
        {
            Mission = _mission,
            Minutes = MissionMinutes,
            Built = _builtCount[me],
            Kills = _killCount[me],
            Losses = _lossCount[me],
            Pay = _missionPay[me],
            Balance = _money[me],
        };

        // Untermissionen: v[101+k] ist der Zustand des k-ten Ziels — 1 offen,
        // 10 erfuellt (siehe Campaign/MissionScript.Objectives). Ohne Skript
        // gibt es die Zeile nicht, dann bleibt sie leer.
        if (_mscript != null)
        {
            var objs = _mscript.Objectives();
            if (objs.Count > 0)
            {
                r.SubTotal = objs.Count;
                r.SubDone = 0;
                foreach (var (_, state) in objs) if (state >= 10) r.SubDone++;
            }
        }

        var cols = new EndColumn[EndColumnCount];
        for (int p = 0; p < EndColumnCount; p++) cols[p] = new EndColumn();

        // lebende Einheiten je Spieler und Klasse
        foreach (var e in _entities)
        {
            if (e.IsProp || e.IsBuilding || e.Dead || e.HpMax <= 0) continue;
            if (e.Owner is < 0 or > 7) continue;
            cols[e.Owner].Alive[EndClassOf(e)]++;
        }
        // ⚠ Flugzeuge: die lebenden zaehlen wir, VERLORENE nicht — ein
        // `Special` stirbt bei uns noch gar nicht (kein einziges
        // `Dead = true` auf der Klasse). Die Zeile steht also mit b=0 da, und
        // das ist eine Luecke von UNS, nicht die des Originals.
        foreach (var a in _special)
        {
            if (a.Dead || a.Owner is < 0 or > 7) continue;
            cols[a.Owner].Alive[3]++;
        }
        for (int p = 0; p < EndColumnCount; p++)
            for (int k = 0; k < EndClasses; k++)
                cols[p].Lost[k] = _lostClass[p, k];

        // Wer ueberhaupt eine Spalte bekommt: der Mensch immer, sonst jeder
        // Platz, von dem etwas lebt oder etwas gefallen ist. Der Rest bleibt
        // "--/--" — das Original zeigt sechs solche Spalten.
        for (int p = 0; p < EndColumnCount; p++)
        {
            int sum = 0;
            for (int k = 0; k < EndClasses; k++) sum += cols[p].Alive[k] + cols[p].Lost[k];
            cols[p].Used = p == me || sum > 0;
            cols[p].Human = p == me;
            // ⚠ Der Name des Menschen ist UNSER Platzhalter. Das Original zeigt
            // dort den Profilnamen ("Virgil" im Bildschirmfoto); ein Profil
            // haben wir nicht. Die Namen der uebrigen kommen aus sec53, wenn es
            // die Tabelle gibt — eine Kampagnenkarte hat sie nicht.
            var rec = _players.Find(x => x.Index == p);
            cols[p].Name = rec != null && rec.Name.Length > 0
                ? rec.Name : p == me ? "SPIELER" : "CPU";
        }
        r.Columns = cols;
        return r;
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

    /// <summary>⚠ UNSERE SETZUNG: wie weit die Muendung vor dem Turmdrehpunkt
    /// liegt. Der DREHPUNKT ist gelesen (TurretOffset, Tabelle 0x4FA320), die
    /// Rohrlaenge nicht — die stuende in SHOOT.CWT, siehe Fire(). 14 px sind
    /// etwas mehr als ein Drittel einer Zelle und decken sich mit den
    /// Turmbildern, deren Rohr rund 12 bis 16 px ueber den Turmkoerper
    /// hinausragt.</summary>
    private const float MuzzleReach = 14f;

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
        // WO das Muendungsfeuer sitzt. Gemeldet: »wenn ein Panzer schiesst,
        // erscheint das Muzzle bei ihm am Rumpf und nicht an der Rohrmuendung«.
        //
        // ⚠ 11.08.2026 — hier stand `shooter.Pos + dir*12 - (0,8)`. Die 8 nach
        // oben war frei erfunden und traf den RUMPF. Der Turm sitzt aber nicht
        // ueber der Fahrzeugmitte: das Original zeichnet ihn an
        // `(mount[0] + mount[k]) / 2` ueber dem Fahrwerk (@0x429CCB..0x429D1B,
        // Tabelle 0x4FA320, k = Flagbyte der Zelle) — dieselbe Rechnung, die
        // TurretOffset schon fuer das BILD macht. Das Feuer geht jetzt von
        // genau diesem Punkt aus, nicht mehr von der Fahrzeugmitte.
        //
        // ⚠ WAS UNSERE SETZUNG BLEIBT: die Rohrlaenge (MuzzleReach). Die
        // richtige Zahl steht in SHOOT.CWT und ist gelesen, aber noch nicht
        // eingefuehrt — die Datei traegt 2400 Saetze zu vier Punkten
        // (u8 x, u8 y, u8 flag), Satzindex `(waffe*48 + bild)*4 + punkt`,
        // Lader @0x4544F0 nach 0x87D6B0, benutzt @0x42A188 mit `x-0x19`,
        // `y-0x44` vom Zeichenpunkt des Waffenbildes. Gezaehlt: 16 der 18
        // Waffenbauteile tragen aktive Punkte, und zwar nur fuer die Bilder
        // 0,1,2,6,7 je Neigungsblock — die nach NORDEN zeigenden Richtungen
        // 3,4,5 haben keinen, dort liegt die Muendung hinter dem Turm.
        // Solange dieser Satz nicht durch den Import laeuft, steht hier eine
        // Laenge und keine Tabelle, und das ist so gekennzeichnet.
        //
        // ACHTUNG, unveraendert und belegt: INFANTERIE bekommt KEIN
        // zusaetzliches Muendungsfeuer — ihre Schusspose (Bilder 9 und 10)
        // traegt den roten Blitz schon im Sprite. Die Grenze ist
        // InfantryWeaponFirst.
        if (shooter.Weapon < InfantryWeaponFirst)
            _effects.Add(new Effect
            {
                Pos = shooter.Pos + TurretOffset(shooter.UnitType, shooter.Col, shooter.Row)
                      + dir * MuzzleReach,
                Kind = "muzzle", FrameTime = 0.035f,
            });

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
                // dieselbe Muendung wie das Feuer darueber
                Pos = shooter.Pos + TurretOffset(shooter.UnitType, shooter.Col, shooter.Row)
                      + dir * MuzzleReach,
                Aim = victim.Pos - new Vector2(0, 6),
                Target = vi, Shooter = si, Damage = w.Damage,
                Facing = DirToFacing(dir), Kind = rocket, Speed = RocketSpeed,
            });
            return;
        }

        // Die Leuchtspur beginnt an der Muendung, nicht am Rumpf.
        _tracers.Add((shooter.Pos + TurretOffset(shooter.UnitType, shooter.Col, shooter.Row)
                      + dir * MuzzleReach,
                      victim.Pos - new Vector2(0, 6), 0.10f));
        // ⚠ OFFEN, ausdruecklich: ob das Original bei DIREKTEM Beschuss einen
        // EINSCHLAG zeigt, ist nicht gelesen. Der Geschossweg fuegt bei
        // Ankunft eine "explosion" hinzu, dieser Weg nicht. Gesucht wurde die
        // Stelle, die im Original einen Effekt anlegt: der Zeichner @0x42A188
        // haengt die Punkte an das WAFFENBILD (SHOOT.CWT, siehe oben) und
        // gehoert damit zum Schuetzen, nicht zum Ziel; ein Aufrufer der
        // Trefferroutine (Zasah @0x40C9A0, GAMESTATE_RE.md 1085) liess sich
        // ueber die Rel32-Aufrufsuche nicht finden — sie wird nicht direkt
        // gerufen. Solange das nicht geklaert ist, wird hier nichts erfunden.
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
        // ⚠ 15.08.2026 — DER WUERFEL DER SIMULATION, nicht der von Godot.
        // `GD.Randi()` haengt am globalen Zustand des Motors: er wird von
        // allem mitbewegt, was sonst noch wuerfelt (Klaenge!), und laesst sich
        // von aussen nicht keimen. Zwei Maschinen bekommen damit
        // zwangslaeufig verschiedene Zahlen. Determinism.Roll ist ein eigener
        // Strom mit gesetztem Keim und einem Zaehler, den der Pruefstand liest.
        int dmg = offence - defence + Simulation.Determinism.Roll(5)
                                    - Simulation.Determinism.Roll(5);
        if (dmg <= -2) return 0;
        if (dmg < 1) return Simulation.Determinism.Roll(10) / 3;
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

        Kill(vi, victim, shooter?.Owner ?? -1);
    }

    /// <summary>Take an entity off the board: clear its cells, drop it out of
    /// every selection and target, and leave the right remains behind.</summary>
    /// <param name="by">Wer den Todesstoss gefuehrt hat, oder -1. Nur fuer die
    /// Statistik — siehe <see cref="NoteKill"/>.</param>
    private void Kill(int vi, Entity victim, int by = -1)
    {
        NoteKill(victim, by);
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
            if (CellAt(p.Aim) is { } ic)
            {
                Audio.GameSounds.Explosion(ic.X, ic.Y);
                // Ein Einschlag trifft auch das GLEIS auf dieser Zelle — die
                // Einschlagsroutine des Originals @0x40D799 laeuft ueber alle
                // 3000 Gleisplaetze und vergleicht genau diese Zelle.
                RailHit(Mathf.RoundToInt(ic.X), Mathf.RoundToInt(ic.Y), p.Damage);
            }
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

    /// <summary>Den mitgebrachten Kontostand einsetzen. Von aussen, weil nur
    /// der Kampagnenweg ihn hat — siehe Campaign.CampaignManager.Balance fuer
    /// den Beleg, dass er ueberhaupt mitgeht.</summary>
    public void SetStartMoney(int player, int value) => Money(player, value);

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

    /// <summary>
    /// `--terra-check` — die Rohstoffvorkommen dieser Mission und die Frage,
    /// ob auf ihnen eine Feld-Rohstoffmine STEHEN KANN.
    ///
    /// <para>Warum es das braucht: mit den Vorkommen aus dem Missionsaufbau
    /// meldet <c>--build-check</c> auf map_23 weiter <b>0 Bauplätze</b> für den
    /// Typ 15 — die Liste ist also nicht alles. Der Anker eines Gebäudes muss im
    /// 3x3-Fenster eines Vorkommens liegen (<c>CellOnDeposit</c>), also gibt es
    /// je Vorkommen genau NEUN Kandidaten; dieser Prüfstand geht sie ab und sagt
    /// bei jedem, WELCHE Zelle welchen der vier Tests nicht besteht (Regel 10).
    /// </para>
    ///
    /// <para>⚠ Was er NICHT sagt: wie das Original den Anker zum Vorkommen legt.
    /// Ob die 3x3-Prüfung im Original überhaupt den Anker meint (und nicht die
    /// Mitte des Bauwerks oder die Zelle des Bautechnikers), ist <b>ungelesen</b>
    /// — hier endet das Verständnis, und der Prüfstand behauptet nichts darüber.
    /// </para>
    /// </summary>
    public string TerraCheckLine()
    {
        EnsureMissionScript();
        if (_nav == null) return "terra-check: kein Gitter";
        if (_deposits.Count == 0)
            return "terra-check: diese Mission legt keine Rohstoffvorkommen an";
        if (Patterns == null || !Patterns.HasBuildings)
            return "terra-check: keine Muster fuer dieses Tileset";

        var sb = new System.Text.StringBuilder();
        int gut = 0, kandidaten = 0;
        var grund = new SortedDictionary<string, int>();
        var cells = new List<SiteCell>();
        foreach (var (dc, dr, amount) in _deposits)
        {
            int hier = 0;
            string erste = "";
            for (int dy = 0; dy < 3; dy++)
                for (int dx = 0; dx < 3; dx++)
                {
                    int c = dc + dx, r = dr + dy;
                    kandidaten++;
                    if (CanBuild(Patterns, TypeFieldMine, c, r, -1, cells)) { hier++; gut++; continue; }
                    // die erste Zelle, die durchfaellt, samt Grund
                    foreach (var s in cells)
                    {
                        if (s.Ok) continue;
                        string w = !_nav.InBounds(s.Col, s.Row) ? "ausserhalb"
                            : _nav.GroundAt(s.Col, s.Row) != Simulation.NavGrid.Ground.Free
                                ? "Grund " + _nav.GroundAt(s.Col, s.Row)
                            : _nav.OccupantAt(s.Col, s.Row) != -1 ? "belegt"
                            : _nav.FlagAt(s.Col, s.Row) != 0 ? "Hangbyte " + _nav.FlagAt(s.Col, s.Row)
                            : "Ecken zu flach";
                        grund[w] = grund.GetValueOrDefault(w) + 1;
                        if (erste.Length == 0) erste = $"({s.Col},{s.Row}) {w}";
                        break;
                    }
                }
            sb.Append($"   Vorkommen ({dc},{dr}) {amount} Einheiten: {hier} von 9 Ankern " +
                      $"tragen eine Mine{(hier == 0 ? "   erster Grund: " + erste : "")}\n");
        }
        var teile = new List<string>();
        foreach (var kv in grund) teile.Add($"{kv.Key} x{kv.Value}");

        // ⚠ UND JETZT DIE MECHANIK, nicht die Zahl: auf jedem Vorkommen, das
        // einen Anker traegt, wird eine Mine ueber den ECHTEN Bauweg gesetzt
        // (PlaceBuilding — derselbe, den der Bautechniker geht), und danach wird
        // das Skript gefragt. Mission 23 will fuenf: »buildings(Klasse 2,
        // Spieler 0) == 5«, und Klasse 2 sind die Gebaeudetypen 10 und 15.
        // Eine Umwidmung waere hier genau der Fehler — sie pruefte die Zahl und
        // nicht, ob der Spieler bauen KANN.
        int gebaut = 0;
        if (gut > 0 && Patterns != null)
            foreach (var (dc, dr, _) in _deposits)
            {
                if (gebaut >= 5) break;
                bool fertig = false;
                for (int dy = 0; dy < 3 && !fertig; dy++)
                    for (int dx = 0; dx < 3 && !fertig; dx++)
                    {
                        int c = dc + dx, r = dr + dy;
                        if (!CanBuild(Patterns, TypeFieldMine, c, r)) continue;
                        if (PlaceBuilding(Patterns, TypeFieldMine, c, r, 0) == null) continue;
                        gebaut++;
                        fertig = true;          // je Vorkommen nur eine
                    }
            }
        int klasse2 = BuildingClassCount(2, 0);
        string ende = "";
        if (_mscript != null)
        {
            _mscript.Evaluate();
            ende = _mscript.Ended
                ? (_mscript.Success ? "   -> Skript: MISSION ERFUELLT" : "   -> Skript: VERLOREN")
                : "   -> Skript: noch nicht entschieden   " + _mscript.WhyNot();
        }
        return $"terra-check: M{_mscript?.Mission ?? 0} — {_deposits.Count} Vorkommen, " +
               $"{kandidaten} Anker geprueft, {gut} tragen eine Feld-Rohstoffmine\n" +
               sb.ToString() +
               (teile.Count > 0 ? "   Gruende: " + string.Join(", ", teile) + "\n" : "") +
               $"   ueber den echten Bauweg gesetzt: {gebaut} Feld-Rohstoffminen; " +
               $"buildings(Klasse 2, Spieler 0) = {klasse2}\n" + ende;
    }

    /// <summary>Das Missionsskript samt seinen Haken JETZT anlegen, falls es
    /// noch nicht steht. Es entsteht sonst erst im ersten Takt, und ein
    /// Prüfstand, der davor läuft, sieht eine Welt ohne Mission — genau daran
    /// meldete <c>--build-check</c> auf map_23 »0 Bauplaetze« für die
    /// Feld-Rohstoffmine, obwohl die Vorkommen eingetragen waren: sie kommen mit
    /// dem Skript, und das gab es noch nicht.</summary>
    public void EnsureMissionScript()
    {
        if (_mscript == null) MissionScriptTick(0.001f);
    }

    /// <summary>
    /// Die Rohstoffvorkommen der laufenden Mission in die Bauplatzprüfung
    /// setzen. Siehe <see cref="Campaign.MissionScript.Script.Terra"/> und
    /// <c>Simulation/Construction.cs</c>, wo <c>CellOnDeposit</c> sie liest.
    /// </summary>
    private void SetTerraPlaces(System.Collections.Generic.IReadOnlyList<(int Col, int Row, int Amount)> list)
    {
        _deposits.Clear();
        foreach (var (col, row, amount) in list) _deposits.Add((col, row, amount));
        if (_deposits.Count > 0)
            GD.Print($"Vorkommen: {_deposits.Count} Rohstoffstellen aus dem " +
                     "Missionsaufbau — die Feld-Rohstoffmine hat jetzt Bauplaetze");
    }

    /// <summary>
    /// `order(einheit, cx, cy, utok_na, extra)` @0x410220 — der Befehl, mit dem
    /// Kampagne 2 ihre sieben eben gesetzten Einheiten losschickt.
    ///
    /// <para><b>Gelesen, mit den Namen des Spiels.</b> Der Debug-Dump @0x416F00
    /// beschriftet jedes Feld des Einheitensatzes selbst, und daran hängt die
    /// ganze Deutung: <c>CX:</c> ist +0x18, <c>CY:</c> +0x19, <c>UKOL:</c> +0x14,
    /// <c>AKCE:</c> +0x15, <c>UTOK_NA:</c> +0x36, <c>STRILI_NA:</c> +0x34. Die
    /// Routine schreibt <c>UKOL = 4</c> (Angriff), <c>CX/CY</c> = die
    /// mitgegebene Zelle, <c>DALSI_SMER = 0xFF</c>, <c>AKCE = 0</c> und
    /// <c>UTOK_NA</c> = das vierte Argument.</para>
    ///
    /// <para>⚠ <b>UNSERE SETZUNG ist die Umsetzung von UKOL 4, nicht die
    /// Zelle.</b> Das Original lässt die Einheit angreifen UND nennt ihr eine
    /// Zielzelle; welches Ziel <c>UTOK_NA = 60000</c> benennt, ist ungelesen
    /// (alle sieben Aufrufe geben dieselbe Zahl, es gibt kein Gegenbeispiel).
    /// Wir fahren die Einheit darum auf CX/CY und lassen sie dort auf das
    /// treffen, was in Reichweite kommt — <c>AutoAcquire</c> greift, sobald der
    /// Weg abgelaufen ist. Damit steht keine gesetzte Einheit untätig herum,
    /// und nichts wird über UTOK_NA erfunden.</para>
    ///
    /// <para>⚠ <b>Über die BEFEHLSSCHICHT, nicht in den Zustand.</b> Der Befehl
    /// wird als Satz abgesetzt und wirkt am nächsten Taktanfang
    /// (<c>ApplyMove</c>) — dort wird er auch auf die Karte geklemmt, wie das
    /// Original es im Behandler tut (@0x4C2324). Ein Skript, das mitten im Takt
    /// schreibt, wäre genau die Naht, an der der Determinismus reisst.</para>
    /// </summary>
    private void MissionOrderAt(int slot, int cx, int cy, int utokNa)
    {
        int idx = -1;
        for (int i = 0; i < _entities.Count; i++)
            if (!_entities[i].IsBuilding && !_entities[i].Dead && _entities[i].Slot == slot)
            { idx = i; break; }
        if (idx < 0)
        {
            GD.PrintErr($"Missionsbefehl (order_at): Einheitenplatz {slot} ist leer");
            return;
        }
        var e = _entities[idx];
        if (!e.Mobile || e.DugIn)
        {
            GD.Print($"Missionsbefehl (order_at): Platz {slot} kann nicht fahren");
            return;
        }
        bool ok = PostRaw(AkteEuropaReborn.Simulation.Commands.CommandRecord.Make(
            AkteEuropaReborn.Simulation.Commands.CommandOp.Move,
            (byte)Mathf.Clamp(e.Owner, 0, 7),
            (short)idx, (short)cx, (short)cy, (short)0));
        GD.Print($"Missionsbefehl (order_at): Einheit {slot} (Spieler {e.Owner}) " +
                 $"nach ({cx},{cy}){(ok ? "" : " — Ring VOLL, Befehl fiel aus")}" +
                 $"  [Original: UKOL 4, UTOK_NA {utokNa} ungelesen]");
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
                            // obj_owner @0x4D076D, woertlich: TYP null (Satz
                            // +0x00) -> 12, sonst der Besitzer (+0x01). Ein
                            // Satz ohne Bauwerk (NoStructure) antwortet also
                            // NICHT pauschal 12 — Platz 4 auf map_02 hat Typ 51
                            // und muss weiter 255 melden, nur Platz 7 mit Typ 0
                            // meldet 12.
                            return e.BType == 0 || e.Dead ? 12
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
                    // ⚠ UNSERE SETZUNG: was hier AUSGEZAHLT wird, ist das, was
                    // das Abschlussfenster "Missionsbezahlung" nennt. Der
                    // Betrag stammt aus dem Missionsblock (Busbefehl 528), die
                    // GLEICHSETZUNG mit dem Feld im Fenster ist unsere — ein
                    // eigenes Bezahlfeld haben wir in GAME.EXE nicht gefunden.
                    // Abzuege zaehlen nicht mit, sonst frisst ein Strafgeld die
                    // Belohnung wieder auf.
                    if (betrag > 0 && spieler is >= 0 and <= 7) _missionPay[spieler] += betrag;
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
                // place_unit @0x4D0810 — DASSELBE create_unit, nur ohne die
                // Warteschlange: das Original geht bei space_in
                // @0x4C0260 -> @0x4C1600 -> @0x4D0810 -> @0x4B34E0, hier faellt
                // nur der Anflug weg. Darum derselbe Rumpf, eigener Haken.
                _mscript.PlaceUnit = (entwurf, spalte, zeile, spieler) =>
                    SpawnReinforcement(entwurf, spalte, zeile, spieler);
                _mscript.OrderUnitAt = MissionOrderAt;
                // ⚠ 13.08.2026 — DIE ROHSTOFFVORKOMMEN. Sie stehen nicht auf
                // der Karte, sondern im SETUP-Block der Mission
                // (`add_terra_place(spalte, zeile, menge)`, C: 0x4D0A10,
                // F: 0x4D05C0; 50 Aufrufe in acht Missionen, beide Fassungen
                // gleich). Die Feld-Rohstoffmine darf nur auf einem Vorkommen
                // stehen, und ohne diese Liste hatte sie auf JEDER Karte
                // 0 Bauplaetze — auf map_23, deren Ziel »Bauen Sie fuenf
                // Rohstoffminen« lautet, gegen 329 fuer das Depot und 1411 fuer
                // den Generator. Die Mission war damit unloesbar.
                SetTerraPlaces(_mscript.Terra);
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
            if (IsBuildingClass(e.BType, cls)) n++;
        }
        return n;
    }

    /// <summary>Gehört ein Bauwerk dieses <c>BType</c> zur Gebäudeklasse
    /// <paramref name="cls"/>, nach der die Kampagne fragt?
    ///
    /// <para>⚠ Diese Zuordnung stand am 13.08.2026 an ZWEI Stellen — hier und in
    /// der Erzwingungsschleife von <c>MissionScriptForceCheck</c>, wo sie
    /// zunächst ganz fehlte und dadurch Mission 5 und 23 durchfallen liess. Sie
    /// steht jetzt einmal, weil zwei Kopieen derselben Liste in einer Datei
    /// dieser Grösse zuverlässig auseinanderlaufen: der Prüfstand muss dieselbe
    /// Klasse meinen wie der Zähler, sonst prüft er etwas anderes als die
    /// Bedingung.</para></summary>
    private static bool IsBuildingClass(int bType, int cls) => cls switch
    {
        0 => bType != 0,
        1 => bType is 2 or 3 or 4,      // Fabriken
        2 => bType is 10 or 15,         // Minen
        3 => bType is 6 or 12,          // Bahnhöfe
        _ => false,
    };

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
        string captured = "";
        if (ap == null)
        {
            // ⚠ 15.08.2026 — AUF DEN GEFECHTSKARTEN GEHOEREN DIE FLUGHAEFEN
            // NIEMANDEM. Nachgezaehlt ueber alle acht NET-Karten: NET01 zwei,
            // NET02 sieben, NET03 vier, NET04 acht, NET05 sieben, NET06 acht,
            // NET07 KEINEN EINZIGEN, NET08 drei — zusammen 39, alle mit
            // Besitzer 11, also neutral; erobert werden sie im Spiel.
            // ⚠ NET07 ist keine Pruefkarte fuer den Flughafen: dort ist von 300
            // Gebaeudeplaetzen keiner vom Typ 9. Hier stand »NET07 sieben«, und
            // das war falsch — wer dort prueft, prueft nichts.
            // Der Pruefstand hat deshalb bisher auf JEDER Gefechtskarte nur
            // »kein eigener Flughafen« gemeldet und nichts geprueft — ein
            // Pruefstand, der immer dasselbe sagt, prueft nichts.
            //
            // Also nimmt er sich einen neutralen und schreibt ihn sich zu, so
            // wie eine Eroberung es taete, und sagt dazu, dass er es getan hat.
            foreach (var e in _entities)
                if (e.IsBuilding && !e.Dead && e.BType == 9)
                {
                    ap = e;
                    ap.Owner = ViewPlayer is >= 0 and <= 7 ? ViewPlayer : 0;
                    captured = $"(neutraler Flughafen fuer die Probe auf Spieler {ap.Owner} " +
                               "gesetzt — im Spiel muss er erobert werden) ";
                    break;
                }
        }
        if (ap == null) return "air-buy-check: kein Flughafen auf dieser Karte";

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
        return "air-buy-check: " + captured + string.Join(" | ", log);
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

    /// <summary>
    /// `--place-check[=<sek>]` — die EINSETZUNGEN einer Mission an ihrer
    /// Messlatte.
    ///
    /// <para>Die gelesene Zahl je Mission ist die Latte: über alle 33 Blöcke
    /// zählt <c>0x4D0810</c> <b>60 Aufrufe in 14 Missionen</b> (M2 7, M3 8, M5 2,
    /// M9 8, M11 8, M13 2, M14 3, M16 4, M17 1, M18 6, M19 1, M24 3, M28 4,
    /// M29 3), und davon sind heute <b>20 in Regeln eintragbar</b> (M2 7, M3 4,
    /// M5 2, M11 1, M14 3, M18 3). Was diese Zeile prüft, ist die
    /// AUSGELÖSTE Zahl gegen die getragene: kommt Kampagne 2 auf sieben, ist das
    /// ein Beleg; kommt sie auf neunzehn, feuert etwas zu oft.</para>
    ///
    /// <para>Dazu die vier Dinge, die ohne das Original prüfbar sind, und nur
    /// die: Zelle im Kartenrahmen, Besitzer 0..7, Entwurf 50..194, und ob die
    /// Einsetzung überhaupt gelungen ist (das Original verweigert mit
    /// »WRONG ROB_PROD in PLACE!!!!«, wenn die sec47-Zeile
    /// <c>entwurf + 200*spieler</c> leer ist — bei uns fehlt der Entwurf dann in
    /// unit_designs.json).</para>
    ///
    /// <para>⚠ <b>Was er NICHT sehen kann:</b> ob eine Einsetzung zum RICHTIGEN
    /// ZEITPUNKT kommt. Ein zu früh gesetzter Panzer steht auf einer erlaubten
    /// Zelle, gehört einem erlaubten Spieler und trägt einen erlaubten Entwurf —
    /// er sieht aus wie ein richtiger. Dafür gibt es in diesem Baum keinen
    /// Prüfstand, und diese Zeile behauptet nicht, einer zu sein.</para>
    /// </summary>
    public string PlaceCheckLine()
    {
        if (_mscript == null) MissionScriptTick(0.001f);
        if (_mscript == null) return "place-check: kein Skript fuer diese Mission";
        var (tragen, befehle) = _mscript.Carried();
        var sb = new System.Text.StringBuilder();
        sb.Append($"place-check: M{_mscript.Mission} traegt {tragen} Einsetzungen " +
                  $"und {befehle} Befehle; ausgeloest {_mscript.Placements} / " +
                  $"{_mscript.OrdersGiven}");

        // Die getragenen Stellen gegen den Kartenrahmen und die beiden Bereiche.
        int drin = 0, raus = 0, sp = 0, ent = 0;
        var schlecht = new List<string>();
        foreach (var (design, col, row, player, at) in _mscript.PlaceSites())
        {
            bool inMap = _nav != null && _nav.InBounds(col, row);
            bool okP = player is >= 0 and <= 7;
            bool okD = design is >= 50 and <= 194;
            if (inMap) drin++; else { raus++; schlecht.Add($"@0x{at:X} Zelle ({col},{row})"); }
            if (!okP) { sp++; schlecht.Add($"@0x{at:X} Spieler {player}"); }
            if (!okD) { ent++; schlecht.Add($"@0x{at:X} Entwurf {design}"); }
        }
        sb.Append($"\n   getragene Stellen: {drin} im Kartenrahmen, {raus} ausserhalb, " +
                  $"{sp} mit Spieler ausser 0..7, {ent} mit Entwurf ausser 50..194");
        if (schlecht.Count > 0) sb.Append("\n   ⚠ " + string.Join("  ", schlecht));

        // Und was tatsaechlich auf der Karte steht: eine gesetzte Einheit traegt
        // ihre Entwurfsnummer in Mark (Satz +0x43), daran ist sie zu erkennen.
        var marken = new Dictionary<int, int>();
        foreach (var (design, _, _, _, _) in _mscript.PlaceSites())
            marken[design] = marken.GetValueOrDefault(design);
        foreach (var e in _entities)
            if (!e.IsBuilding && !e.IsProp && !e.Dead && marken.ContainsKey(e.Mark))
                marken[e.Mark]++;
        var teile = new List<string>();
        foreach (var kv in marken) teile.Add($"Entwurf {kv.Key}: {kv.Value}");
        if (teile.Count > 0)
            sb.Append("\n   auf der Karte mit dieser Entwurfsnummer: " +
                      string.Join(", ", teile) +
                      "   (⚠ die Karte kann eigene mitbringen — das ist eine OBERgrenze)");
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
            // 6 s Spielzeit in FESTEN Takten: 30 x 0.2 s war eine Rechnung ueber die
            // Bildrate, und mit festem Takt (50 Hz) sind es 300 Takte.
            _mscript!.Advance(6 * Campaign.MissionScript.TicksPerSecond);
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
            _mscript.Advance(6 * Campaign.MissionScript.TicksPerSecond);
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
    /// <summary>
    /// `--econ-check[=<sek>]`: der Vorrat, den der SPIELER im Bedienfeld
    /// liest, Zeile fuer Zeile — und daneben alles, was ihn bewegt.
    ///
    /// <para>Gebaut fuer die Meldung vom 11.08.2026: »der Ressourcen Wert W
    /// steigt immer auf 10 und faellt dann wieder auf 0«. Mit
    /// <c>--store-check</c> war das nicht zu messen: der liest die Lager, die
    /// ein MISSIONSSKRIPT beobachtet, und im Gefecht gibt es keins. Hier steht
    /// darum genau das, was das Bedienfeld einer Fabrik zeigt
    /// (<c>T… W…/Lagerplatz V…</c>, siehe die Panel-Zeile fuer IsFactory) plus
    /// die beiden Zahlen, die den Ausschlag geben: was die Fabrik
    /// ZURUECKHAELT (<c>OwnReserve</c>) und wohin sie abliefert.</para>
    /// </summary>
    /// <summary>Zählt dieses Gebäude in die Bestände des Spielers? Fabriken und
    /// die Basis.
    ///
    /// <para>⚠ Diese Auswahl steht seit dem 13.08.2026 EINMAL, und zwar hier.
    /// Sie wird von <see cref="EconCheckLine"/> und
    /// <see cref="PlayerStocks"/> gemeinsam benutzt — der Prüfstand und die
    /// Anzeige im Bild müssen dieselben Gebäude zählen, sonst widersprechen sie
    /// sich und man weiss nicht, welcher lügt. Genau dieselbe Überlegung wie bei
    /// <see cref="IsBuildingClass"/>.</para></summary>
    private bool CountsForStocks(Entity e, int player) =>
        e.IsBuilding && !e.Dead && e.Owner == player && (IsFactory(e) || e.BType == 1);

    /// <summary>Was der Spieler an Lagern hat, addiert über seine Gebäude — für
    /// die stehende Rohstoffleiste (<c>UI/GameHud.cs</c>).
    ///
    /// <para>⚠ <b>Die SUMME ist unsere Zutat.</b> Das Original führt keinen
    /// Spielervorrat; bezahlt wird aus dem Lager DES Gebäudes
    /// (@0x44A6D8/ED/08). Eine Leiste, die einen Gesamtvorrat zeigt, ist eine
    /// Wettkampf-Bequemlichkeit und keine gelesene Mechanik.</para>
    ///
    /// <para>Ersetzt seit dem 13.08.2026 einen Notweg: <c>MapViewer.ReadStocks</c>
    /// hat die vier Summen aus der ZEICHENKETTE von
    /// <see cref="EconCheckLine"/> zerlegt. Eine Anzeige, die eine
    /// Prüfstandszeile parst, bricht bei jeder Änderung an deren Wortlaut.</para></summary>
    public (int T, int W, int F, int S, int Money, int Buildings) PlayerStocks(int player)
    {
        int t = 0, w = 0, f = 0, s = 0, n = 0;
        foreach (var e in _entities)
        {
            if (!CountsForStocks(e, player)) continue;
            t += e.StockT; w += e.StockW; f += e.StockF; s += e.StockS;
            n++;
        }
        return (t, w, f, s, _money[Mathf.Clamp(player, 0, 7)], n);
    }

    public string EconCheckLine()
    {
        int me = ViewPlayer is >= 0 and <= 7 ? ViewPlayer : 0;
        var facs = new List<string>();
        var hqs = new List<string>();
        foreach (var e in _entities)
        {
            if (!CountsForStocks(e, me)) continue;
            if (IsFactory(e))
                facs.Add($"[{e.Slot} Typ{e.BType} T{e.StockT} " +
                         $"W{e.StockW} F{e.StockF} S{e.StockS} /Lager{e.Capacity} " +
                         $"V{e.ProdSpeed} St{e.State} haelt{OwnReserve(e)}]");
            else
                hqs.Add($"[{e.Slot} Basis W{e.StockW} F{e.StockF} S{e.StockS} " +
                        $"T{e.StockT} bezahlbar {Affordable(e)}/{BuildableBy(1).Count}]");
        }
        return $"econ-check: P{me} " +
               (facs.Count > 0 ? "Fabriken " + string.Join(" ", facs) : "keine Fabrik") +
               (hqs.Count > 0 ? "  " + string.Join(" ", hqs) : "  keine Basis") +
               $"  | gefahren W{_econMovedW} F{_econMovedF} S{_econMovedS} T{_econMovedT}";
    }

    /// <summary>Wieviele Entwuerfe dieses Gebaeude aus seinen eigenen drei
    /// Lagern bezahlen koennte — die Zahl, an der sich »ich kann nichts bauen«
    /// messen laesst.</summary>
    private int Affordable(Entity e)
    {
        if (_designs == null) return 0;
        int n = 0;
        foreach (int i in BuildableBy(e.BType)) if (CanAfford(e, _designs[i])) n++;
        return n;
    }

    /// <summary>Was der Nahweg seit Missionsbeginn fortgefahren hat, je Sorte —
    /// nur fuer <c>--econ-check</c>, damit sich ein PENDELNDER Vorrat von einem
    /// stehenden unterscheiden laesst.</summary>
    private int _econMovedW, _econMovedF, _econMovedS, _econMovedT;

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
    /// `--tick-check` — DER TAKT des Missionsskripts, und was er in den ersten
    /// Sekunden auslöst.
    ///
    /// <para>Die Spielermeldung vom 11.08.2026 war: Kampagne 2 meldet nach
    /// knapp zwei Sekunden eine erledigte Nebenmission, ohne dass der Spieler
    /// etwas getan hat. Zwei Dinge müssen dafür stimmen, und dieser Prüfstand
    /// misst beide statt sie zu behaupten:</para>
    ///
    /// <para>(1) DER TAKT. 50 Takte je Sekunde, der langsame Teil des Blocks
    /// jeden 100., das Tor aus der JSON — alles gemessen, siehe
    /// <c>aekernel-tools/mission_tick.py</c>. Weicht eine der drei Zahlen ab,
    /// steht ein ⚠ dahinter. Zusätzlich wird die Umrechnung Sekunden→Takte an
    /// einer FRISCHEN Uhr nachgerechnet, damit ein Rückfall auf »einmal je
    /// Bild« nicht unbemerkt bleibt.</para>
    ///
    /// <para>(2) WAS VON SELBST FEUERT. Der Lauf rührt nichts an — keine
    /// Auswahl, kein Befehl, kein Schuss — und zählt, was das Skript trotzdem
    /// tut. Ein <c>sound</c> oder <c>money</c> in diesem Zustand ist genau der
    /// gemeldete Fehler und wird als ⚠ ausgewiesen; Texte und Zähler sind
    /// erlaubt, denn die Missionen sprechen den Spieler von sich aus an.</para>
    /// </summary>
    public string TickCheck(double seconds = 10.0)
    {
        // Das Skript entsteht erst im ersten Takt samt seinen Haken — ohne
        // diesen Anstoss meldete der Pruefstand »kein Skript« und saehe damit
        // genau wie eine fehlende Datei aus.
        if (_mscript == null) MissionScriptTick(0.001f);
        if (_mscript == null) return "tick-check: kein Skript fuer diese Mission";

        var sb = new System.Text.StringBuilder();

        // (1a) die Umrechnung selbst, an einer zweiten Uhr: 60 Bilder zu 1/60 s
        // muessen 50 Takte ergeben, nicht 60.
        var uhr = Campaign.MissionScript.For(_mscript.Mission);
        if (uhr != null)
        {
            for (int f = 0; f < 60; f++) uhr.Tick(1.0 / 60.0);
            sb.Append($"tick-check: 60 Bilder zu 1/60 s -> {uhr.Ticks} Takte " +
                      $"(erwartet {Campaign.MissionScript.TicksPerSecond})" +
                      (uhr.Ticks == Campaign.MissionScript.TicksPerSecond ? "" : " ⚠") + "\n");
        }

        // (2) die Mission laufen lassen, ohne irgendetwas zu tun
        int sounds = 0, money = 0, texts = 0;
        var vorher = _mscript.PlaySound;
        _mscript.PlaySound = k => { sounds++; vorher?.Invoke(k); };
        var vorherGeld = _mscript.AddMoney;
        _mscript.AddMoney = (b, p) => { money++; vorherGeld?.Invoke(b, p); };
        var vorherText = _mscript.ShowText;
        _mscript.ShowText = (id, art, x, y) => { texts++; vorherText?.Invoke(id, art, x, y); };

        long vorTakt = _mscript.Ticks;
        _mscript.Advance((int)(seconds * Campaign.MissionScript.TicksPerSecond) -
                         (int)vorTakt);

        sb.Append($"tick-check: Mission {_mscript.Mission} nach {seconds:0.0} s ohne " +
                  $"Zutun — {_mscript.TickLine(seconds)}\n");
        sb.Append($"   ausgeloest: {texts} Texte, {money} Geldbuchungen, {sounds} Klaenge");
        if (sounds > 0 || money > 0)
            sb.Append("   ⚠ das Skript meldet einen Erfolg, den niemand erspielt hat");
        sb.Append('\n');
        foreach (var (t, r, was) in _mscript.Fired)
            sb.Append($"   Takt {t,5} ({t / (double)Campaign.MissionScript.TicksPerSecond,6:0.00} s) " +
                      $"Regel {r,2}: {was}\n");
        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// `--place-force` — dieselbe Erzwingungsmaschine, aber auf die Bedingungen
    /// der Regeln angesetzt, die eine EINSETZUNG oder einen BEFEHL tragen.
    ///
    /// <para>⚠ Es ist absichtlich dieselbe Maschine und keine zweite: eine
    /// Abschrift laeuft auseinander. Nur die Bedingungsliste ist eine andere —
    /// <see cref="Campaign.MissionScript.PlaceConds"/> statt
    /// <c>ChainConds()</c>. Gebraucht wird das fuer Kampagne 2, deren sieben
    /// Einsetzungen an einer Regel haengen, die mit der Endregel nichts zu tun
    /// hat: ohne diesen Einstieg loest ein Prueflauf sie nie aus, und das sieht
    /// genauso aus wie eine kaputte Einsetzung.</para>
    /// </summary>
    public string PlaceForceLine()
    {
        if (_mscript == null) MissionScriptTick(0.001f);
        if (_mscript == null) return "place-force: kein Skript fuer diese Mission";
        var conds = _mscript.PlaceConds();
        if (conds.Count == 0)
            return $"place-force: M{_mscript.Mission} — keine Regel mit Einsetzung " +
                   "oder Befehl hat eine Bedingung, die sich erzwingen liesse";
        string zeile = MissionScriptForceCheck(conds).Replace("script-check", "place-force");
        // Und jetzt laufen lassen: die Einsetzung steht hinter dem Tor, also
        // braucht sie einen Blockdurchlauf, und der Befehl wirkt erst am
        // naechsten Taktanfang (ApplyMove). Zwei Sekunden reichen fuer beides.
        _mscript.Advance(2 * Campaign.MissionScript.TicksPerSecond);
        return zeile;
    }

    /// <summary>
    /// Harness: knock out every building the mission script watches, so the
    /// whole chain can be checked without playing the mission — the condition
    /// reads the world, the rule latches, `end` fires, and Verdict() carries it
    /// into the campaign. Reports what it destroyed.
    ///
    /// <para><paramref name="only"/> setzt eine andere Bedingungsliste an die
    /// Stelle von <c>ChainConds()</c> — dafuer gibt es genau einen Anlass, siehe
    /// <see cref="PlaceForceLine"/>. Null heisst: die Endkette, wie bisher.</para>
    /// </summary>
    public string MissionScriptForceCheck(List<Campaign.MissionScript.Cond>? only = null)
    {
        if (_mscript == null) return "script-check: kein Skript fuer diese Mission";

        // ⚠ ZUERST die Frage, die am 09.08. einen Tag gekostet hat: gewinnt das
        // Skript die Mission schon im Anfangszustand? Drei invertierte
        // Bedingungen taten genau das, und weil der Prueflauf erst erzwungen und
        // dann geschaut hat, sah es jedes Mal wie ein Erfolg aus. Ein Durchlauf
        // ohne dt wertet alle Regeln einmal aus, ohne die Uhr zu bewegen.
        _mscript.Evaluate();
        if (_mscript.Ended)
            return "script-check: ⚠ das Skript entscheidet die Mission SOFORT (" +
                   (_mscript.Success ? "gewonnen" : "verloren") +
                   "), bevor irgendetwas erzwungen wurde — eine Bedingung steht " +
                   "verkehrt herum oder ein Anfangswert fehlt";

        // Nicht `EndConds()`: seit die Setzer-Regeln mitlaufen, steht hinter
        // einer Endbedingung ueber eine Blockvariable eine WELTbedingung, und
        // die ist das, was sich erzwingen laesst.
        var conds = only ?? _mscript.ChainConds();
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
            // ⚠ 11.08.2026 — die BELEGUNGSKARTE ist erzwingbar, und bis heute
            // war sie es nicht. Mission 7 hängt ihre ganze Kette daran:
            // `imap(22,14) < 1000` heisst »auf dieser Zelle steht eine Einheit
            // von Spieler 0« (die Plätze laufen als 1000·spieler + k), und
            // genau das stösst v[1] an. Solange der Prüfstand das nicht stellen
            // konnte, meldete er »nicht erzwingbar« und die Mission fiel durch —
            // aus einem Grund, der mit der geprüften Kette nichts zu tun hat.
            // Im Spiel fährt man einfach hin.
            if (c.Kind == "imap")
            {
                if (_nav == null || !_nav.InBounds(c.A, c.C))
                { left++; untouched.Add(Show(c)); continue; }
                // ⚠ 13.08.2026 — EIN FENSTER, NICHT EINE SCHRANKE. Bis heute
                // sah der Prueflauf nur `< n` und stellte immer eine Einheit des
                // SICHTSPIELERS hin. Mission 11 fragt aber ein PAAR ab:
                //     imap(55,8) > 5999  UND  imap(55,8) < 7000
                // und das heisst nach der Griffrechnung `1000*spieler + k`
                // schlicht »auf dieser Zelle steht eine Einheit von SPIELER 6«.
                // Mit nur der oberen Schranke stellte der Prueflauf eine Einheit
                // von Spieler 0 hin, machte damit das zweite Glied wahr und das
                // erste falsch — und meldete »nicht erzwingbar« fuer das erste.
                // Also: alle imap-Glieder DERSELBEN ZELLE zusammennehmen, das
                // Fenster ausrechnen und den Spieler daraus ableiten.
                int lo = 0, hi = 8000;
                foreach (var q in conds)
                {
                    if (q.Kind != "imap" || q.A != c.A || q.C != c.C) continue;
                    switch (q.Op)
                    {
                        case "<": hi = Mathf.Min(hi, q.B); break;
                        case "<=": hi = Mathf.Min(hi, q.B + 1); break;
                        case ">": lo = Mathf.Max(lo, q.B + 1); break;
                        case ">=": lo = Mathf.Max(lo, q.B); break;
                        default: hi = -1; break;               // ==/!= : ungelesen
                    }
                }
                if (hi <= lo || hi > 8000) { left++; untouched.Add(Show(c)); continue; }
                // Der Spieler, dessen Plaetze in dieses Fenster fallen. Deckt es
                // mehrere ab, bleibt es beim Sichtspieler — dann sagt die
                // Bedingung nichts ueber den Besitzer.
                int wantOwner = lo / 1000 == (hi - 1) / 1000 ? lo / 1000 : ViewPlayer;
                Entity? mover = null;
                foreach (var e in _entities)
                    if (!e.IsBuilding && !e.IsProp && !e.Dead && e != marked &&
                        e.Owner == wantOwner) { mover = e; break; }
                if (mover == null) { left++; untouched.Add(Show(c) + $" [kein Fahrzeug von Spieler {wantOwner}]"); continue; }
                _nav.ClearOccupant(mover.Col, mover.Row, _entities.IndexOf(mover));
                mover.Col = c.A;
                mover.Row = c.C;
                _nav.SetOccupant(c.A, c.C, _entities.IndexOf(mover), mover.Infantry >= 0);
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
                    // ⚠ 13.08.2026 — DIE KLASSE ZAEHLT MIT. Hier stand nur
                    // `e.IsBuilding`, und damit uebergab der Pruefstand ein
                    // BELIEBIGES Bauwerk — auf map_05 einen Skriptplatz mit
                    // BType 0, also gar keins. `BuildingClassCount(1, 0)` blieb
                    // dadurch 0, und Mission 5 wie Mission 23 fielen durch, aus
                    // einem Grund, der mit der geprueften Kette nichts zu tun
                    // hat. `--produce-check` beweist unabhaengig, dass Mission 5
                    // in 6 s gewinnt, sobald sie ihre Fabriken hat: der Defekt
                    // war ausschliesslich im Pruefstand.
                    //
                    // Wieder ein Fall von »ein Pruefstand, der eine Zahl SETZT,
                    // prueft die Zahl und nicht die Mechanik« — er hat hier
                    // sogar die falsche Zahl gesetzt.
                    "buildings" => e.IsBuilding && IsBuildingClass(e.BType, c.A),
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
            _mscript.Evaluate();
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
        _mscript.Evaluate();
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
        // ⚠ 11.08.2026 — die BASIS (Typ 1) baut ALLES. Das ist keine Setzung
        // mehr, sondern die Auskunft des Spiels selbst (HELPG.TXT 24: »Auf der
        // Produktionsliste finden Sie eine Reihe von Vordefinierten Einheiten«,
        // ohne jede Einschraenkung nach Bauteil) — siehe IsUnitPlant. Die
        // Aufteilung darunter gilt nur noch fuer die Bauprogramme der KI und
        // fuer Anzeigen, die nach der Fabrik fragen.
        1 => true,
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
            // ⚠ »Alle Einheiten« (UNSERE Option, siehe FillSkirmishAirDesigns):
            // die Freigabe-Pruefung entfaellt, die Zuordnung zur Fabrik NICHT —
            // sonst baute die Waffen-Fabrik Ausruestungstraeger und der Reiter
            // haette keine Bedeutung mehr.
            bool unlocked = UI.SkirmishSetup.AllUnits || d.Available ||
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
        // ⚠ `using`: ConfigFile ist ein RefCounted, und ein nicht freigegebenes
        // stirbt beim Herunterfahren im Finalizer. Dasselbe Muster hat am
        // 13.08.2026 in Settings.cs »Leaked unsafe reference to object:
        // <ConfigFile#…>« in Serie und danach 0xC0000005 in GC.RunFinalizers
        // erzeugt (Rueckgabewerte 139/132 statt 0, Commit 615c1c5). Hier ist es
        // je Aufruf eines statt je Bild, also genuegt `using`.
        using var c = new ConfigFile();
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
        // ⚠ `using`, und hier besonders: die Zeile darunter springt mitten aus
        // der Methode zurueck, das Objekt wurde auf dem haeufigen Weg also gerade
        // NICHT freigegeben. Siehe SaveOwnDesigns und Commit 615c1c5.
        using var c = new ConfigFile();
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
    {
        if (designWeapon is >= 1 and <= 19) return designWeapon + 20;
        // ⚠ 11.08.2026 — die AUSRUESTUNG hat auch einen Aufsatz, und der ist
        // gelesen: Entwurfswaffe 65..79 bildet auf Bauteil 40..54 ab, aber
        // NICHT der Reihe nach. Die Reihenfolge steht unten; die Turmbilder
        // dazu liegen bereits unter Units/turret/. Vorher gab TurretOf hier 0
        // zurueck — ein gebauter Ausruestungstraeger fiel dadurch zwar richtig
        // durch CanFight, trug aber KEINEN AUFSATZ IM BILD.
        int i = System.Array.IndexOf(EquipMountOrder, designWeapon);
        return i >= 0 ? 40 + i : 0;
    }

    /// <summary>Entwurfswaffe -&gt; Aufsatz 40..54, in dieser Reihenfolge:
    /// 66-&gt;40, 67-&gt;41, 68-&gt;42, 70-&gt;43, 65-&gt;44, 69-&gt;45,
    /// 71-&gt;46, 72-&gt;47, 73-&gt;48, 74-&gt;49, 75-&gt;50, 76-&gt;51,
    /// 77-&gt;52, 78-&gt;53, 79-&gt;54.</summary>
    private static readonly int[] EquipMountOrder =
        { 66, 67, 68, 70, 65, 69, 71, 72, 73, 74, 75, 76, 77, 78, 79 };

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

    /// <summary>
    /// Die ENTWURFSNUMMER eines Fußsoldaten, 0…199 innerhalb des Spielerblocks
    /// — das, was der Bildzeichner <c>0x4508A0</c> für sein Bild braucht
    /// (<see cref="UI.PortraitBank.PictureOfInfantry"/>) und was der
    /// Einheitensatz selbst nicht trägt.
    ///
    /// <para><b>Der Weg, und jedes Glied ist schon belegt.</b> Der Satz trägt
    /// das <c>spodek</c> <c>+0x0b</c>, bei uns <see cref="Entity.Infantry"/>,
    /// und die Erzeugung <c>@0x4b1abe</c> rechnet es aus der WAFFENZEILE des
    /// Entwurfs (<c>spodek = (weapon*2 − 124) &amp; 0xFF</c>, mit 185 -> 22 und
    /// 186 -> 20; siehe <c>aekernel-tools/infantry_designs.py</c>). Die Zeile
    /// zurück gibt <c>infantry.json</c> als <c>weapon_row</c>, und in
    /// <c>unit_designs.json</c> gehört jede dieser zwölf Zeilen zu GENAU EINEM
    /// Entwurf mit Fahrwerk 148/149 — deshalb ist die Nummer eindeutig und muss
    /// hier nicht geraten werden.</para>
    ///
    /// <para>Von den acht Spielerblöcken nehmen wir den niedrigsten Treffer:
    /// sec47 wiederholt die zwölf Entwürfe in jedem Block Byte für Byte, und
    /// <c>0x4508A0</c> bekommt die Nummer INNERHALB des Blocks.</para>
    /// </summary>
    /// <returns>die Entwurfsnummer, oder −1, wenn der Satz unbekannt ist</returns>
    private static int InfantryDesignOf(int set)
    {
        LoadInfantryDesigns();
        if (_infDesigns == null || !_infDesigns.TryGetValue(set, out var des)) return -1;
        LoadDesigns();
        int best = -1;
        foreach (var kv in _designBySlot)
        {
            var d = kv.Value;
            if (d.Weapon != des.WeaponRow || d.Propulsion is not (148 or 149)) continue;
            int nr = kv.Key % DesignsPerPlayer;
            if (best < 0 || nr < best) best = nr;
        }
        return best;
    }

    /// <summary>The other way round: from the component a unit carries back to
    /// the stats row, which is what the fire-sound class is stored in. Exactly
    /// the inverse of <see cref="TurretOf"/> and nothing more — a component
    /// outside 21..39 gets no sound rather than a guessed one.</summary>
    private static int WeaponRowOf(int comp)
        => comp >= 21 && comp <= 39 ? comp - 20 : -1;

    /// <summary>Factory building types: Waffen-, Fahrwerk- and Spezial-Fabrik.
    /// Eine Fabrik macht TEILE. Einheiten baut sie NICHT — siehe
    /// <see cref="IsUnitPlant"/>.</summary>
    private static bool IsFactory(Entity e) => e.IsBuilding && (e.BType is 2 or 3 or 4);

    /// <summary>
    /// Wo EINHEITEN entstehen: in der BASIS (Gebäudetyp 1), und nirgends sonst.
    ///
    /// <para>⚠ 11.08.2026 — bis heute baute bei uns die FABRIK die Einheiten,
    /// und das war der gemeldete Fehler »ich kann im Gefecht nicht wirklich
    /// Einheiten bauen, weil der Ressourcen-Wert W immer auf 10 steigt und dann
    /// wieder auf 0 fällt«. Gemessen auf map_DM_1 (--skirmish=map_DM_1
    /// --econ-check=1): der Waffenvorrat der Fabrik auf Platz 3 lief
    /// 0,1,2,3,4,5 und stand bei Sekunde 29 wieder auf 0, während die Basis
    /// unverändert W298 F360 S200 hielt. Der Zug holt die Teile ab — und das
    /// ist RICHTIG so.</para>
    ///
    /// <para><b>Das Spiel sagt es in eigenen Worten</b> (HELPG.TXT, exportiert
    /// nach <c>user://data/UI/help.json</c>):</para>
    /// <list type="bullet">
    /// <item>Nr. 24: »Im @Basis @Fenster haben Sie die Möglichkeit, neue
    /// Einheiten zu produzieren. … Der @Produktionspreis der Einheiten ist
    /// bestimmt durch @Waffen-, @Chassis- oder @Spezialteile, die <b>in jeder
    /// Basis</b> bereitgehalten werden.«</item>
    /// <item>Nr. 32: »Die @Teile, aus denen die Einheiten hergestellt werden,
    /// werden in entsprechenden @Fabriken produziert. Gibt es nicht genügend
    /// Teile <b>in der Basis</b>, sollten Sie @Transporte organisieren, die die
    /// Teile aus den Fabriken holen.«</item>
    /// <item>Nr. 34: »Eine @Waffen @Fabrik wird übernommen. Hier werden die
    /// Teile produziert, die zur Herstellung von Waffen <b>in der Basis</b>
    /// gebraucht werden.«</item>
    /// </list>
    ///
    /// <para><b>Und das Programm bestätigt es:</b> die Produktionsschaltfläche
    /// prüft die drei Lager des Gebäudes, dessen Fenster offen ist —
    /// <c>[esi+0xC0693C]</c>, <c>[esi+0xC0693E]</c>, <c>[esi+0xC06940]</c>
    /// (@0x44A6D8, @0x44A6ED, @0x44A708), also +0x28/+0x2A/+0x2C am Satz
    /// 0xC06914 — und schickt erst danach Befehl 0x1F7 (@0x44A713). Das Fenster
    /// im Originalfoto akte-europa_8.png heisst <b>»Basis 2«</b>, trägt die
    /// Reiter Depot/Produktion/Forschung/Reparatur und den Knopf »Produzieren«,
    /// und unter seiner Liste stehen die drei Lagerzahlen der Basis
    /// (315/228/0).</para>
    ///
    /// <para>Damit fällt auch die alte Setzung »welche Fabrik welchen Entwurf
    /// baut« (<c>FitsFactory</c>) weg: die Basis baut alles.</para>
    /// </summary>
    private static bool IsUnitPlant(Entity e) => e.IsBuilding && e.BType == 1;

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

    /// <summary>⚠ 13.08.2026 — hier stand fest <c>_money[0]</c>, und das ist der
    /// Kontostand von SPIELER 0, nicht der des Spielers, dem man zusieht. Auf
    /// <c>map_05</c> ist der Sichtspieler 1, die Zeile zeigte dort also ein
    /// fremdes Konto. Gefunden beim Bau der stehenden Rohstoffleiste, nicht beim
    /// Ansehen dieser Zeile — ein Fehler in Datei A fällt bei der Arbeit an
    /// Datei B auf. Dieselbe Klemmung wie überall sonst in dieser Datei.</summary>
    public string MoneyLine() =>
        $"Kontostand : $ {_money[ViewPlayer is >= 0 and <= 7 ? ViewPlayer : 0]}";

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
            if (!IsUnitPlant(e)) continue;
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
        return IsUnitPlant(e) || IsDock(e) || e.BType == 9 ? e : null;
    }

    /// <summary>The panel's heading: the game's own word for the tab
    /// ("Produktion", 0x501934) and what the building has to spend.</summary>
    public string BuildPanelTitle()
    {
        var e = Producer();
        if (e == null) return "";
        if (IsUnitPlant(e))
            return $"PRODUKTION  W{e.StockW} F{e.StockF} S{e.StockS}";
        if (IsSupplyDepot(e))
            return $"VERSORGUNGSDEPOT  ${_money[ViewPlayer is >= 0 and <= 7 ? ViewPlayer : 0]}";
        int owner = e.Owner is >= 0 and <= 7 ? e.Owner : 0;
        return $"PRODUKTION  ${_money[owner]}";
    }

    /// <summary>Name, Energie und Zustand des gewaehlten Gebaeudes — was
    /// Titelleiste, Energiebalken und Statuszeile des Basisfensters brauchen
    /// (UI/BaseWindow.cs). null, wenn nichts Bauendes gewaehlt ist.</summary>
    public (string Name, int Hp, int HpMax, string Status)? BuildPanelHead()
    {
        var e = Producer();
        if (e == null) return null;
        return (e.Name.Length > 0 ? e.Name : BuildingTypeName(e.BType),
                e.Hp, e.HpMax, e.Dead ? "zerstoert" : StateName(e));
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

        if (IsUnitPlant(e) && _designs != null)
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
            if (!IsUnitPlant(e) && !IsDock(e) && e.BType != 9) continue;
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
        /// these five are the ones that occur on the maps.
        ///
        /// <para>⚠ <b>Drei der acht Vorlagen fehlen hier, und seit dem
        /// 13.08.2026 kostet das ein BILD:</b> die Nutzlasten 102 (Radar,
        /// Spionageflieger), 103 (Robot Transporter) und 104 (Mechaniker) fallen
        /// auf 0 durch, und ein am Flughafen GEKAUFTES Flugzeug dieser drei
        /// Arten bekommt deshalb im Bedienblock kein Bild (Kind 0 liegt
        /// ausserhalb 1..14). Ein auf der Karte STEHENDES Flugzeug ist davon
        /// nicht betroffen — dessen Kind kommt aus sec19 <c>+0x08</c>.</para>
        ///
        /// <para><b>Warum es hier nicht nachgetragen ist:</b> die Vorlagen
        /// tragen ihr Kind selbst, an <c>+0x2d</c> der Tabelle 0x51b021 —
        /// aber <c>ExeTables.Aircraft</c> liest die Spalte nicht, und
        /// <c>aircraft.json</c> unter <c>user://data/Maps</c> hat sie deshalb
        /// nicht (nachgesehen 13.08.2026: 12 Felder, kein »kind«). Nach dem
        /// NAMEN zuzuordnen wäre Raten: für 103/104 sagt
        /// <c>Assets/Legacy/Maps/aircraft.json</c> 11/12 und
        /// <see cref="AirKindName"/> 12/11, und ein vertauschtes Flugzeugbild
        /// ist schlimmer als keines. Zu tun ist: <c>CostW</c>-artig ein
        /// <c>Kind = r[0x2d]</c> in <c>ExeTables.AircraftTemplate</c>, ein
        /// <c>"kind"</c> in <c>ContentBuilder.WriteAircraft</c>, dann hier
        /// dieses Byte statt der Nutzlast-Tafel.</para></summary>
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
    /// <summary>
    /// <b>Die Flugzeugliste eines GEFECHTS — jetzt gelesen statt gesetzt.</b>
    ///
    /// <para>⭐ 13.08.2026, <b>UMGESTELLT, und die alte Begründung wird
    /// ausdrücklich zurückgezogen.</b> Hier stand: »Der Anlass ist eine Lücke in
    /// den DATEN — die Gefechtskarten tragen in sec120 null Flugzeugvorlagen«,
    /// und daraus folgte eine Option von uns (<c>AllUnits</c>), die alle acht
    /// Vorlagen freigab. <b>Beide Hälften waren falsch:</b></para>
    /// <list type="bullet">
    /// <item>Die Gefechtskarten tragen sec120 nicht mit NULL Sätzen, sondern
    /// <b>gar nicht</b>: alle 23 <c>.CWM</c> enden bei sec39, ihr Kopfbyte 3 ist
    /// 1 statt 2 (Prüfung <c>cmp cl,2</c> @0x41E6A9, 23 von 23 gegen 13 von 13
    /// bei den <c>.DM</c>, kein Gegenbeispiel). Eine FEHLENDE Sektion
    /// überschreibt nichts — deshalb bleibt im Original die Tabelle der EXE
    /// stehen, und aus »die Karte bringt nichts mit« folgt gerade NICHT »es gibt
    /// nichts«.</item>
    /// <item>Es gibt also eine gelesene Regel, und sie geht <b>auf keiner Stufe
    /// leer aus</b>. Damit löste die Option ein Problem, das es nicht gab — und
    /// erzeugte ein neues, siehe unten.</item>
    /// </list>
    ///
    /// <para><b>Die Regel, ganz aus GAME.EXE</b> (Torroutine @0x419F30, beide
    /// Fassungen; Tabelle 0x51B020 = 8 Blöcke × 20 Sätze × 48 B, Freigabe an
    /// +0x00, Nutzlast an +0x24, Rumpf an +0x25):</para>
    /// <code>
    ///   Freigabe = stats[Nutzlast].+0x24 &lt;= Techstandard
    ///           &amp;&amp; stats[Rumpf].+0x24    &lt;= Techstandard
    ///           &amp;&amp; Satz != 3 &amp;&amp; Satz != 7
    /// </code>
    /// <para>Die beiden Ausnahmen sind kein Schwellenwert, sondern zwei
    /// ausdrückliche Nullungen am Ende des Tors: <b>Satz 3 (Transport Heli)</b>
    /// @0x419F98 und <b>Satz 7..19 (Mechanikerheli und die zwölf leeren
    /// Plätze)</b> @0x419F9E. Danach kopiert @0x4B2380 den Block von Spieler 0 in
    /// die anderen sieben — das Gegenstück zu @0x4B2330 bei den Schiffen; das tut
    /// die Schleife <c>for p in 0..7</c> hier unten schon.</para>
    ///
    /// <para>⚠ <b>Warum Satz 3 und 7 hier gesperrt bleiben, ist NICHT »das
    /// Original macht es so«.</b> Das Gefecht ist seit dem 13.08.2026
    /// ausdrücklich kein Treuemodus mehr, dort wäre eine Freigabe erlaubt. Sie
    /// bleiben gesperrt, weil <b>ungeprüft</b> ist, ob sie überhaupt tragen: das
    /// Original gibt sie auf keiner der 8 Stufen frei, und ob sie je erreichbar
    /// waren, ist offen (in 36 Kartendateien kommen sie 0 Mal vor). Was nie
    /// erreichbar war, kann unfertig sein — Grafik, Verhalten, Preis ungeprüft.
    /// <b>Sobald jemand nachweist, dass sie tragen, gehören sie im Gefecht
    /// freigegeben</b>, und dann fällt diese Zeile.</para>
    ///
    /// <para><b>Die Schwellen liegen im Repository</b>, kein neuer Export:
    /// <c>component_stats.json</c> +0x24 — Nutzlast 100 = 4, 101 = 6, 102 = 5,
    /// 103 = 0, 104 = 0, 105 = 6, 106 = 1, 107 = 1; Rümpfe 120..123 alle 0
    /// (nachgesehen, 12 von 12). Daraus ergibt sich:</para>
    /// <list type="table">
    /// <item><term>1–3</term><description>Treibstoffheli, Munitionheli</description></item>
    /// <item><term>4</term><description>+ Kampfhubschrauber</description></item>
    /// <item><term>5</term><description>+ Spionageflieger</description></item>
    /// <item><term>6–8</term><description>+ Jagdflieger, Bomber</description></item>
    /// </list>
    /// <para><b>Die Vorgabe ist 1.</b> Gelesen ist daran nur, dass ein frisches
    /// Original auf 1 startet (@0x4426F4). <b>Dass 1 auch für UNSER Gefecht die
    /// richtige Vorgabe ist, ist damit nicht belegt</b> — das Gefecht ist seit dem
    /// 13.08.2026 ein Wettkampfmodus und kein Treuemodus, die Wahl der Vorgabe
    /// gehört dem Spieler. Sie steht auf 1, weil noch niemand etwas anderes
    /// entschieden hat, nicht weil die Zahl beweiskräftig wäre. Wichtig ist
    /// ohnehin die andere Hälfte: auf jeder Stufe stehen mindestens zwei
    /// Versorgungshelis da, der gemeldete Fehler (»keine Flugeinheit zur
    /// Auswahl«) ist also unabhängig von der Vorgabe behoben.</para>
    ///
    /// <para><b>Warum <see cref="UI.SkirmishSetup.AllUnits"/> die Luft nicht mehr
    /// anfasst.</b> Nicht aus Treue — im Gefecht wäre eine breitere Liste
    /// erlaubt. Sondern weil die Option ein Problem löste, das es nicht gab: es
    /// gibt eine gelesene Stellschraube, die auf keiner Stufe leer ausgeht, und
    /// die ist feiner als ein Alles-oder-nichts-Schalter. Nebenbei gab die Option
    /// Satz 3 und Satz 7 frei, deren Tragfähigkeit ungeprüft ist (siehe oben). Am
    /// Boden (601 Entwürfe gegen 65) und zur See (10 gegen 2) bleibt sie
    /// unverändert, was sie war.</para>
    ///
    /// <para>⚠ Die Kampagne geht einen ANDEREN Weg (@0x4D03E0 → @0x4B23C0, das
    /// alles nullt, danach Einzelfreigaben per Skript) und wird hier nicht
    /// angefasst: die Bedingung bleibt <c>CampaignMission &lt;= 0</c>, und
    /// <see cref="FillCampaignAirDesigns"/> bleibt, wie es war.</para>
    /// </summary>
    private void FillSkirmishAirDesigns()
    {
        if (UI.SkirmishSetup.CampaignMission > 0) return;
        if (_airDesigns != null && _airDesigns.Count > 0) return;
        var types = LoadAircraftTemplates();
        if (types.Count == 0)
        {
            GD.Print("aircraft: aircraft.json hat keine Vorlagen — der Flughafen bleibt leer");
            return;
        }
        int tech = AirProbeTechstandard;
        _airDesigns = new List<AirDesign>();
        var on = new List<string>();
        for (int p = 0; p < 8; p++)
            for (int i = 0; i < types.Count; i++)
            {
                var t = types[i];
                t.Player = p;
                t.Enable = AirEnabledAt(i, t, tech);
                if (p == 0 && t.Enable) on.Add(t.Name);
                _airDesigns.Add(t.Clone());
            }
        _airSource = $"Tor @0x419F30, Techstandard {tech}";
        int priced = 0;
        foreach (var t in types) if (t.CostW + t.CostF + t.CostS > 0) priced++;
        GD.Print($"aircraft: {types.Count} Vorlagen fuer 8 Spieler aus {_airSource}, " +
                 $"{on.Count} freigegeben ({string.Join(", ", on)}), " +
                 $"{priced} mit gelesenem Preis");
    }

    /// <summary>
    /// Der Techstandard, mit dem gerechnet wird — normalerweise der des
    /// Gefechtsschirms (<see cref="UI.SkirmishSetup.Techstandard"/>), für einen
    /// Prüflauf überschreibbar mit <c>--techstandard=1..8</c>.
    ///
    /// <para>Warum der Schalter sein muss: die gelesene Regel gibt je Stufe eine
    /// ANDERE Liste, und ein Prüfstand, der nur die Vorgabe 1 sehen kann, kann
    /// nicht zeigen, dass die Stufe überhaupt wirkt — er würde auf jeder Stufe
    /// dasselbe sagen. Mit dem Schalter ist »Stufe 4 bringt den
    /// Kampfhubschrauber dazu« eine Messung und keine Behauptung.</para>
    ///
    /// <para>Ohne den Schalter ändert sich nichts: dann gilt das Feld des
    /// Gefechtsschirms, und dessen Vorgabe 1 ist die des Originals
    /// (@0x4426F4).</para></summary>
    public static int AirProbeTechstandard
    {
        get
        {
            if (_probeTech.HasValue) return _probeTech.Value;
            int v = UI.SkirmishSetup.Techstandard;
            foreach (string a in OS.GetCmdlineUserArgs())
                if (a.StartsWith("--techstandard="))
                    v = Mathf.Clamp(a["--techstandard=".Length..].ToInt(), 1, 8);
            _probeTech = v;
            return v;
        }
    }

    private static int? _probeTech;

    /// <summary>Das Tor @0x419F30 für EINEN Satz — siehe
    /// <see cref="FillSkirmishAirDesigns"/> für die Herleitung. Die zwei
    /// Satznummern sind aus dem Code, keine Auswahl von uns.</summary>
    private static bool AirEnabledAt(int index, AirDesign t, int tech)
    {
        if (index == 3 || index == 7) return false;      // @0x419F98 / @0x419F9E
        return StatsTechLevel(t.Payload) <= tech && StatsTechLevel(t.Airframe) <= tech;
    }

    /// <summary>Die Techschwelle einer Bauteilzeile — <c>component_stats.json</c>
    /// <b>+0x24</b>, dieselbe Stelle, die das Tor mit
    /// <c>byte[58·zeile + 0x5045C4]</c> liest (58 = Satzlänge, 0x5045A0 + 0x24).
    ///
    /// <para>Eine unbekannte Zeile gibt <b>0</b> zurück, also »immer frei«. Das
    /// ist die vorsichtige Seite: fehlt die Tabelle, steht am Flughafen zuviel
    /// statt gar nichts — und ein leerer Flughafen war der gemeldete Fehler. Die
    /// Zeile <c>--air-buy-check</c> sagt die Stufe mit, damit ein solcher
    /// Rückfall in der Ausgabe zu sehen ist und nicht wie ein Ergebnis
    /// aussieht.</para></summary>
    private static int StatsTechLevel(int row)
    {
        LoadStatsTech();
        return _statsTech != null && _statsTech.TryGetValue(row, out int v) ? v : 0;
    }

    private static Dictionary<int, int>? _statsTech;

    private static void LoadStatsTech()
    {
        if (_statsTech != null) return;
        _statsTech = new Dictionary<int, int>();
        string path = Core.Content.Path("Maps/component_stats.json");
        if (!FileAccess.FileExists(path)) return;
        using var f = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        if (f == null) return;
        var json = new Json();
        if (json.Parse(f.GetAsText()) != Error.Ok ||
            json.Data.VariantType != Variant.Type.Dictionary) return;
        var root = json.Data.AsGodotDictionary<string, Variant>();
        if (!root.TryGetValue("rows", out var rv) ||
            rv.VariantType != Variant.Type.Dictionary) return;
        foreach (var kv in rv.AsGodotDictionary<string, Variant>())
        {
            if (!int.TryParse(kv.Key, out int row)) continue;
            string h = kv.Value.AsString();
            // +0x24 als Hexpaar; kürzere Zeilen gibt es nicht (alle 58 Bytes)
            if (h.Length < 0x24 * 2 + 2) continue;
            if (int.TryParse(h.Substring(0x24 * 2, 2),
                             System.Globalization.NumberStyles.HexNumber,
                             System.Globalization.CultureInfo.InvariantCulture, out int b))
                _statsTech[row] = b;
        }
    }

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
        // ⭐ 13.08.2026 — DIE ALTE BEGRUENDUNG WAR FALSCH, DER RUECKFALL BLEIBT.
        // Hier stand: »Die Vorlagentabelle der EXE (@0x51b021) traegt KEINEN
        // Preis«. Das ist widerlegt — der Preis steht im EXE-Satz an
        // +0x1F/+0x20/+0x21 (Waffen-, Fahrwerk-, Spezialteile), die Kaufpruefung
        // liest genau diese Bytes (@0x449DD2 / @0x449DF7 / @0x449E18 auf
        // 0x51B03F/40/41), und gegen sec120 gehalten stimmen sie in 832 von 832
        // Saetzen byteweise.
        //
        // ⚠ ABER: der Rueckfall wird trotzdem gebraucht, aus einem ANDEREN
        // Grund, und ich habe ihn beim Herausnehmen gemessen statt vermutet.
        // Es gibt ZWEI aircraft.json, und die Engine liest die falsche:
        //
        //   Assets/Legacy/Maps/aircraft.json  (Python, aircraft_export.py)
        //       traegt cost_w/cost_f/cost_s — alle 8 Vorlagen ungleich 0/0/0
        //   user://data/Maps/aircraft.json    (C#, ContentBuilder.WriteAircraft)
        //       traegt sie NICHT, und Core.Content.Path nimmt diese
        //
        // Ohne den Rueckfall gemessen (--air-buy-check auf map_NET02): Menue
        // »Treibstoffheli 0/0/0«, »0 mit gelesenem Preis«, und nach 40 gekauften
        // Flugzeugen stand das Teilelager unveraendert auf 300/400/200 — die
        // Flugzeuge waren UMSONST. Der Rueckfall ist also keine zweite Wahrheit,
        // sondern das einzige, was den Preis derzeit ueberhaupt in die Engine
        // bringt.
        //
        // Was ihn abschafft, liegt nicht in dieser Datei: ContentBuilder.cs
        // Zeile 868 (WriteAircraft) muesste die drei Bytes mitschreiben. Bis
        // dahin greift der Rueckfall, und die Zeile unten sagt es an der Zahl
        // »n mit gelesenem Preis« — steht dort 0, ist es der Rueckfall.
        foreach (var d in list)
            if (d.CostW + d.CostF + d.CostS == 0 &&
                AirPriceByPayload.TryGetValue(d.Payload, out var p))
            { d.CostW = p.W; d.CostF = p.F; d.CostS = p.S; }
        return list;
    }

    /// <summary>
    /// <b>Was ein Flugzeug an Teilen kostet.</b>
    ///
    /// <para>Je Nutzlast EIN Preis, über die 13 Karten mit sec120 und deren je
    /// 104 Sätze gleich, <b>kein Gegenbeispiel</b>:</para>
    /// <code>
    ///     100 Kampfhubschrauber  60/40/0      104 Mechanikerheli   0/30/150
    ///     101 Jagdflieger        50/50/0      105 Bomber          80/70/10
    ///     102 Spionageflieger     0/40/30     106 Treibstoffheli   0/30/40
    ///     103 Transport Heli      0/30/50     107 Munitionheli     0/30/40
    /// </code>
    ///
    /// <para>⭐ 13.08.2026 — <b>die BEGRÜNDUNG dieser Tabelle wird
    /// zurückgezogen, die Tabelle selbst bleibt.</b> Hier stand, die
    /// Vorlagentabelle der EXE trage keinen Preis, weshalb er aus den
    /// Spielständen zurückgerechnet werden müsse. Das ist widerlegt: der Preis
    /// steht im EXE-Satz an <b>+0x1F/+0x20/+0x21</b>, die Kaufprüfung
    /// @0x449DD2/@0x449DF7/@0x449E18 liest ihn dort, und gegen sec120 gehalten
    /// stimmt er in <b>832 von 832</b> Sätzen byteweise. Die Zahlen oben sind
    /// also richtig — sie stammen nur aus der zweitbesten Quelle.</para>
    ///
    /// <para>⚠ <b>Gebraucht wird sie trotzdem, und zwar aus einem ganz anderen
    /// Grund:</b> unser eigener Importeur schreibt die drei Bytes nicht mit.
    /// <c>ContentBuilder.WriteAircraft</c> (Zeile 868) gibt nur speed, hp,
    /// payload, airframe, attack, defence, sight, ammo und fuel aus — und genau
    /// diese Datei liest die Engine über <c>Core.Content.Path</c>. Die reichere
    /// Fassung im Baum (<c>Assets/Legacy/Maps/aircraft.json</c>, aus
    /// <c>aircraft_export.py</c>) trägt die Preise, wird aber von der Kopie unter
    /// <c>user://data</c> verdeckt.</para>
    ///
    /// <para><b>Gemessen, nicht vermutet:</b> ohne diesen Rückfall meldete
    /// <c>--air-buy-check</c> auf map_NET02 »Treibstoffheli 0/0/0«, »0 mit
    /// gelesenem Preis«, und das Teilelager stand nach 40 gekauften Flugzeugen
    /// unverändert auf 300/400/200. Die Flugzeuge waren umsonst.</para>
    ///
    /// <para>Greift NUR, wenn ein Entwurf gar keinen Preis mitbringt — eine Karte
    /// oder ein Export mit Preisen behält seine eigenen Zahlen.</para></summary>
    private static readonly Dictionary<int, (int W, int F, int S)> AirPriceByPayload = new()
    {
        { 100, (60, 40, 0) }, { 101, (50, 50, 0) }, { 102, (0, 40, 30) },
        { 103, (0, 30, 50) }, { 104, (0, 30, 150) }, { 105, (80, 70, 10) },
        { 106, (0, 30, 40) }, { 107, (0, 30, 40) },
    };

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
        if (owner is >= 0 and <= 7) _builtCount[owner]++;   // "Gebaute Einheiten"
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
        if (e.Owner is >= 0 and <= 7) _builtCount[e.Owner]++;   // "Gebaute Einheiten"
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

        /// <summary>Satzfeld <c>+0x18</c> — die Variante. Sie geht nach
        /// <c>+0x0d</c> des Einheitensatzes und entscheidet beim doppelten Rumpf
        /// 151 das BILD, siehe <see cref="UI.PortraitBank.PictureOfShip"/>.
        /// </summary>
        public int Variant;

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
    /// this gate, at the value the binary itself starts from.
    ///
    /// <para>⭐ 13.08.2026 — <b>keine Konstante mehr.</b> Der Wert ist derselbe,
    /// den das Gefecht für die LUFT braucht (Tor @0x419E90 für Schiffe,
    /// @0x419F30 für Flugzeuge, beide mit demselben Argument), und er steht jetzt
    /// als <see cref="UI.SkirmishSetup.Techstandard"/> an einer Stelle. Die
    /// Vorgabe bleibt 1 — das ist der Wert eines frischen Spiels (@0x4426F4), die
    /// Zahl ändert sich also nicht, nur ihre Herkunft. Zwei Listen aus einer
    /// Quelle statt einer Konstante neben einem Feld.</para></summary>
    private static int CampaignTechLevel => AirProbeTechstandard;

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
                Variant = GetI(d, "variant"),
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
        // ⚠ »Alle Einheiten« gilt auch zur See. Ohne die Option bleiben im
        // Gefecht nur die Entwürfe bis zur Anfangs-Technikstufe frei — gemessen
        // 2 von 10. Das ist richtig für einen Kampagnenanfang, aber in einem
        // Gefecht gibt es keine Kampagne, die den Rest je freischaltet: die
        // acht anderen Schiffe wären auf Dauer unerreichbar. Siehe
        // UI.SkirmishSetup.AllUnits.
        //
        // ⚠ DIESE BEGRUENDUNG IST SEIT DEM 13.08.2026 HALB UEBERHOLT und wird
        // hier zurueckgezogen statt ueberschrieben: die »Anfangs-Technikstufe«
        // ist jetzt der Techstandard des Gefechtsschirms (1..8, gelesen —
        // CampaignTechLevel => AirProbeTechstandard). Die acht anderen Schiffe
        // sind damit ueber die Stufe erreichbar, und die »2 von 10« gelten nur
        // fuer Stufe 1. Die Option ist zur See also nicht mehr NOETIG, sondern
        // eine Abkuerzung.
        // Ob sie zur See bleibt, ist eine WETTKAMPFENTSCHEIDUNG des Spielers
        // (Gefecht darf vom Original abweichen, die Kampagne nicht) und keine
        // Frage der Treue — deshalb bleibt sie unangetastet stehen, bis er sie
        // trifft.
        if (UI.SkirmishSetup.AllUnits && UI.SkirmishSetup.CampaignMission <= 0)
        {
            foreach (var d in _shipDesigns) d.Enable = true;
            _shipSource += " + »Alle Einheiten«";
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
            // @0x4B2B20 schreibt +0x18 des Entwurfs nach +0x0d der Einheit —
            // ohne das hat ein vom Stapel gelaufenes Schiff kein Bild, und die
            // Flak-Barkasse bekaeme das des L.Kreuzers.
            ShipVariant = d.Variant,
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
            if (!IsUnitPlant(e) || e.Dead) continue;
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
        if (n > 0) _order = $"Produktion: {n} Basis(en)";
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

    // ⚠ 15.08.2026 — HIER STAND `private readonly RandomNumberGenerator _rng = new();`
    // und ist WEG. Godot keimt einen frisch angelegten RandomNumberGenerator
    // ZUFAELLIG; sein einziger Benutzer war die Produktionschance, und die
    // wuerfelte damit auf jeder Maschine anders. Der Zwillings-Pruefstand hat
    // es auf map_NET02 bei Takt 123 gefunden — 14 Zahlen, alle Lagerbestaende.
    // Wer hier wieder einen Wuerfel braucht, nimmt Simulation.Determinism.

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
            // ⚠ 15.08.2026 — HIER STAND `_rng.RandiRange`, und `_rng` ist ein
            // frisch angelegter Godot-RandomNumberGenerator: DEN KEIMT GODOT
            // ZUFAELLIG. Die Produktionschance wuerfelte damit auf jeder
            // Maschine anders — gefunden hat es der Zwillings-Pruefstand, der
            // auf map_NET02 bei Takt 123 (2,05 s) auseinanderlief, und zwar in
            // genau 14 Zahlen, alle Lagerbestaende. Auf map_NET07 faellt es
            // nicht auf, weil die Karte keine Fabriken hat.
            if (Simulation.Determinism.Roll(100) >= e.EffNum * 100 / Mathf.Max(1, e.EffDen))
                continue;
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
    // ⚠ PartReserve (40) ist am 11.08.2026 entfallen: eine Fabrik baut keine
    // Einheiten mehr und haelt darum nichts zurueck — siehe OwnReserve.
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

        /// <summary><b>Es gibt diesen Waggon gerade nicht.</b> Im Original ist
        /// ein Waggon ein eigener Satz, der bei der Abfahrt ERZEUGT und am
        /// Streckenende einzeln GELÖSCHT wird (<c>+0x00 := 0</c>, danach
        /// überspringt ihn <c>train_tick_all</c>); Waggon <c>w+1</c> entsteht
        /// erst, wenn <c>w</c> auf Streckenzeiger 1 weiterschaltet. Bei uns
        /// bleibt der Satz liegen und wird nur nicht gezeichnet — das Ergebnis
        /// ist dasselbe, und es ersetzt das alte Klemmen, das die vier Waggons
        /// an der Endstation aufeinanderstapelte.</summary>
        public bool Hidden;

        /// <summary>Die Stelle dieses Waggons auf der Kette als Bruchzahl
        /// (<c>0..letzter Schritt</c>), so wie sie zuletzt WIRKLICH gesetzt
        /// wurde — also nach der Kupplung, nicht der getaktete Rohwert. Der
        /// nachfolgende Waggon hängt sich daran; ohne diese Zahl müsste er den
        /// Rückstand des vorderen nachrechnen und würde bei jeder Verkürzung
        /// wieder aufreissen.</summary>
        public float LeadF;

        /// <summary>Dieselbe Stelle, aber VOR der Kupplung — der reine
        /// getaktete Rückstand. Der zweite Durchgang setzt darauf wieder auf,
        /// damit sich die Verkürzung nicht von Takt zu Takt aufsummiert.</summary>
        public float RawLeadF;

        /// <summary>Nur für die Fehlersuche an der Kupplung: der geforderte
        /// Abstand in Kartenpixeln und die Kettenstelle, die daraus wurde.</summary>
        public float Need, Coupled;

        /// <summary>Wo dieser Waggon im VORIGEN Bild stand, und ob es ein voriges
        /// Bild gab. Nur für die Instrumentierung (<c>RailCensusBroken</c>): der
        /// Weg je Takt lässt sich sonst nur für EINEN Probewaggon messen, und
        /// gerade der fährt meist auf einer heilen Linie.</summary>
        public float PrevCol, PrevRow;
        public bool PrevSeen;
    }

    private readonly List<Wagon> _wagons = new();
    private readonly Dictionary<int, List<Vector2>> _lineRoute = new();
    private readonly Dictionary<int, List<int>> _linePiece = new();
    private readonly Dictionary<int, Texture2D?> _trainTex = new();

    /// <summary>Die Strecke einer Linie als KARTENZELLEN — Mittelpunkt jeder
    /// Zelle, die ein Gleisstück trägt, in (Spalte, Zeile) mit GANZER Zeile.
    /// Gebaut von <see cref="RailBuildCells"/>, siehe dort für die Begründung.
    /// Gleis UND Zug laufen hierauf, deshalb liegen sie zwangsläufig
    /// aufeinander.</summary>
    private readonly Dictionary<int, List<Vector2>> _lineCell = new();

    /// <summary>Das Gleisbild (0..5) je Zelle aus <see cref="_lineCell"/>.</summary>
    private readonly Dictionary<int, List<int>> _lineCellFrame = new();

    /// <summary>Das Stück (Wagenrichtung) je Zelle — für das WAGGONbild, dessen
    /// Bildindex das Streckenstück ist (@0x4c6de9).</summary>
    private readonly Dictionary<int, List<int>> _lineCellPiece = new();

    /// <summary>Ist DIESES Kettenglied zerschossen (Bild ≥ 100)? Gebraucht wird
    /// es nicht zum Zeichnen — das macht <c>_railCells</c> — sondern für die
    /// Instrumentierung »was tut der Waggon an einem Bruch« (siehe
    /// <c>RailCensusBroken</c> in Simulation/RailFreight.cs). Ohne diese Liste
    /// müsste der Zähler die Zelle unter dem Waggon in jedem Bild suchen.</summary>
    private readonly Dictionary<int, List<bool>> _lineCellBroken = new();

    /// <summary>
    /// <b>Eine Gleiszelle, wie sie in der KARTE steht</b> — sec22, siehe
    /// <see cref="Import.CwmExtra.RailCell"/> für den Satz und die Fundstellen.
    /// </summary>
    private sealed class RailCell
    {
        public int Index, Col, Row, Frame, Line, Hp;

        /// <summary>Grundbild 0..9. Das Original rechnet genauso
        /// (@0x4B037A: <c>div 10</c>, der Rest ist das Grundbild) — die Zehner
        /// tragen die Stützenart, die Hunderter den Schaden.</summary>
        public int Base => Frame % 10;

        /// <summary>Bild ≥ 100 heißt zerschossen (<c>rail_broken</c>
        /// @0x4B0A3B: <c>cmp …, 0x64 / setae</c>).</summary>
        public bool Broken => Frame >= 100;

        /// <summary>Welche der beiden Trümmervarianten. <c>rail_hit</c>
        /// @0x4B0460 rechnet <c>bild += (10 + zufall&amp;1)·10</c>, legt also
        /// 100 oder 110 auf das Grundbild — die Zehnerstelle trägt den Wurf,
        /// und er steht dauerhaft in der Karte. <b>Gelesen, nicht neu
        /// gewürfelt</b>, sonst tanzt der Schutt bei jedem Laden.
        /// Aus 4.DM: Bilder 100..117, also beide Varianten belegt.</summary>
        public int BrokenVariant => Frame / 10 % 10;

        /// <summary><b>Die Stütze ist keine Setzung mehr.</b> Der Zeichner nimmt
        /// Teil 65 (Träger MIT Bock) genau dann, wenn der PLATZ des Stücks im
        /// Feld durch sechs teilbar ist, sonst den blanken Träger 64 —
        /// @0x42D4B1: <c>bx=6; idiv bx; cmp dx,1; mov dl,0x41; adc dl,0xff</c>,
        /// also 65 bei Rest 0 und 64 sonst. Dieselbe Zahl steht in
        /// <c>rail_pylon_pass</c> @0x4B0350, das NUR die Plätze mit
        /// <c>platz % 6 == 0</c> anfasst.</summary>
        public bool Pylon => Index % 6 == 0;

        /// <summary>Welche STÜTZENFASSUNG (Teil 65+<c>k</c>), 0..3. Wird von
        /// <see cref="RailPylonKind"/> gesetzt; siehe dort.</summary>
        public int PylonKind;
    }

    /// <summary>
    /// <b>Die Stützenfassung — vier Teile, und die Wahl ist gelesen.</b>
    ///
    /// <para><c>rail_pylon_pass</c> @0x4B0350 läuft über alle 3000 Plätze, fasst
    /// aber nur die mit <c>platz % 6 == 0</c> an und rechnet dort
    /// <c>bild := grundbild + 20·k</c>. Der Zeichner @0x42D4FE nimmt dafür
    /// <c>partBase(65) + bild</c>; da Teil 64 und 65 je genau 20 Bilder führen
    /// (nachgezählt), sind das die Teile <b>65, 66, 67 und 68</b> — und die vier
    /// sind verschieden (Bildvergleich: kein Paar gleich).</para>
    ///
    /// <para><c>k</c> kommt aus ZWEI Nachbarproben (@0x4B03A3..0x4B03ED):
    /// <c>bit 0</c> gesetzt, wenn die erste Nachbarzelle KEIN heiles Gleis
    /// trägt, <c>bit 1</c>, wenn die zweite keines trägt. Die beiden Richtungen
    /// stehen in der Tabelle @0x5043D8, acht Byte je Grundbild, und sind genau
    /// die zwei ANGESCHLOSSENEN Seiten des Stücks:</para>
    /// <code>
    ///   f0/f6/f7  rechts + links      f1/f8/f9  unten + oben
    ///   f2  rechts + unten            f3  rechts + oben
    ///   f4  unten  + links            f5  oben   + links
    /// </code>
    /// <para>Die Probe selbst (<c>rail_neighbour_ok</c> @0x4B0300) verlangt
    /// zweierlei: die Zelle steht im Liniengitter 0x542e18 mit einem Wert
    /// 1..60 (der Linie, zu der sie gehört — 60 ist die Zahl der SPOJ-Sätze),
    /// und ihr Gleis ist nicht zerschossen (<c>rail_broken</c> @0x4B0A00).
    /// <b>Die Fassung sagt also, ob die Strecke über der Stütze weiterläuft
    /// oder dort aufhört</b> — deshalb sieht Teil 68 (beide Seiten frei) wie
    /// ein abgeschlossener Kopf aus und Teil 65 wie ein Durchlauf.</para>
    /// </summary>
    private static readonly (int C1, int R1, int C2, int R2)[] RailPylonProbe =
    {
        (1, 0, -1, 0), (0, 1, 0, -1), (1, 0, 0, 1), (1, 0, 0, -1), (0, 1, -1, 0),
        (0, -1, -1, 0), (1, 0, -1, 0), (1, 0, -1, 0), (0, 1, 0, -1), (0, 1, 0, -1),
    };

    /// <summary>
    /// <c>rail_pylon_pass</c> @0x4B0350 (F: 0x4AFC80) — die Fassung JEDER Stütze
    /// aus ihren beiden Nachbarn neu bestimmen.
    ///
    /// <para>Im Original läuft dieser Durchgang nach jedem Treffer und nach
    /// jeder Reparatur, denn beide ändern, ob die Strecke über einer Stütze
    /// weiterläuft. Bei uns lief er bisher nur einmal beim Laden — richtig,
    /// solange sich nichts ändern konnte.</para></summary>
    private void RailPylonPass()
    {
        var live = new HashSet<(int, int)>();
        foreach (var c in _railCells) if (!c.Broken) live.Add((c.Col, c.Row));
        foreach (var c in _railCells)
        {
            if (!c.Pylon) continue;
            c.PylonKind = RailPylonKind(c, live);
            // ⚠ 15.08.2026 — DER DURCHGANG SCHREIBT DAS BILD ZURUECK, und das
            // hat eine Nebenwirkung, die in den Daten steht: `bild := 20*k +
            // bild%10` (@0x4B03F0) drueckt ein GEBROCHENES Mastfeld sofort
            // wieder unter 100. Ein Mastplatz kann darum gar nicht zerschossen
            // bleiben -- in 4.DM liegen 0 von 49 kaputten Zellen auf einem
            // Platz mit index%6==0, waehrend 11 von 11 Mastplaetzen heil sind.
            // Ohne diese Zeile blieben bei uns Mastfelder kaputt liegen.
            // Beobachtet, als der Prüfstand versehentlich eine Mastzelle nahm:
            // Bild 20, zwanzig Treffer, »zerschossen=False« — sie wehrt sich
            // wirklich. Das ist die Gegenprobe zu dieser Zeile.
            c.Frame = 20 * c.PylonKind + c.Frame % 10;
        }
    }

    private static int RailPylonKind(RailCell c, HashSet<(int, int)> live)
    {
        int b = c.Base;
        if (b is < 0 or > 9) return 0;
        var p = RailPylonProbe[b];
        int k = 0;
        if (!live.Contains((c.Col + p.C1, c.Row + p.R1))) k |= 1;
        if (!live.Contains((c.Col + p.C2, c.Row + p.R2))) k |= 2;
        return k;
    }

    private readonly List<RailCell> _railCells = new();

    /// <summary>Nur für den Prüfstand: Bild und Stützenfassung je Gleiszelle.</summary>
    private IEnumerable<(int F, bool P, int K)> RailCellFrames()
    {
        foreach (var c in _railCells) if (!c.Broken) yield return (c.Base, c.Pylon, c.PylonKind);
    }

    /// <summary>Wieviele Gleiszellen die Karte selbst nennt, und wieviele davon
    /// zerschossen sind — für <c>--rail-check</c>.</summary>
    public int RailCellsFromMap, RailCellsBroken;

    /// <summary>Wie weit unsere ALTE Ableitung von der Karte abweicht:
    /// Zellen, die nur wir legten, Zellen, die nur die Karte kennt, und Zellen,
    /// an denen wir ein anderes Bild gewählt hätten. <b>Die Gegenprobe zu
    /// »wir lesen etwas falsch«</b> — vor dem 13.08.2026 war die Ableitung das
    /// Einzige, was gezeichnet wurde.</summary>
    public int RailDiffOnlyOurs, RailDiffOnlyMap, RailDiffFrame, RailDiffChecked;

    /// <summary>
    /// <b>Der Takt des Originals: 50 je Sekunde.</b> <c>SetTimer(fenster, 1,
    /// 0x14, NULL)</c> @0x415BC5 — 0x14 = 20 ms, und die Zeitgebernachricht
    /// treibt <c>game_tick</c> @0x415CF0, in dem sowohl der Zug-Tick
    /// (@0x416256, JEDEN Takt) als auch der SPOJ-Automat (@0x41638D, jeden
    /// FÜNFTEN) hängen.
    /// </summary>
    public const float OriginalTicksPerSecond = 50f;

    /// <summary>
    /// <b>Wieviele Takte ein Streckenschritt kostet — gerechnet, nicht
    /// gesetzt.</b>
    ///
    /// <para>Der Waggonsatz führt bei +0x08 einen Zähler und bei +0x0c den
    /// Abzug je Takt. <c>train_tick</c> @0x4C6A5A prüft <c>zähler &lt;=
    /// abzug</c> — dann rückt der Waggon eine Stelle weiter, sonst
    /// <c>zähler -= abzug</c> (@0x4C6A62). Beim Weiterrücken wird der Zähler
    /// neu gesetzt, und zwar <b>auf 28 für ein ungerades und auf 40 für ein
    /// gerades Streckenstück</b> (@0x4C6E53: <c>mov byte …, 0x1c ; test al,1 ;
    /// jne ; mov byte …, 0x28</c>). Dieselben zwei Zahlen sind die Nenner der
    /// Zwischenrechnung, mit der das Original den Waggon innerhalb eines
    /// Schrittes bewegt (@0x4C6B42 und @0x4C6BAA).</para>
    ///
    /// <para>Der Abzug steht in der Karte: sec44 +0x0c. <b>Über alle 30 Karten
    /// und alle 1439 Waggons ist er 8</b> — nachgezählt, kein Ausreißer.</para>
    ///
    /// <para>Damit: 40 → 32 → 24 → 16 → 8, und beim fünften Takt rückt er
    /// (8 ≤ 8): <b>5 Takte je gerader Schritt</b>. 28 → 20 → 12 → 4, beim
    /// vierten Takt rückt er: <b>4 Takte je Halbschritt einer Diagonale</b>.
    /// Ein gerader Schritt ist eine ganze Zelle, ein ungerader eine halbe in
    /// beiden Richtungen (Bildtabelle @0x539400).</para>
    ///
    /// <para><b>⚠ Was hier NOCH Setzung ist, ist nicht mehr die des Zuges,
    /// sondern eine globale:</b> <see cref="TickScale"/>. Unsere Simulation
    /// lässt die Takte des Originals mit 16 je Sekunde laufen statt mit 50 —
    /// dieselbe Zahl, mit der auch Produktion, Reparatur und Übernahme
    /// rechnen. Am Original gemessen wäre ein gerader Schritt
    /// <b>5/50 = 0,10 s</b>; bei uns sind es 5/16 = 0,3125 s. Wer die Bahn auf
    /// Originalgeschwindigkeit will, dreht an TickScale, nicht am Zug.</para>
    /// </summary>
    private const int TrainStepTicksStraight = 5, TrainStepTicksDiagonal = 4;

    /// <summary>Sekunden je GERADEM Streckenschritt in unserer Zeitrechnung.</summary>
    private const float TrainStepSeconds = TrainStepTicksStraight / (float)TickScale;

    /// <summary>Sekunden je Halbschritt einer Diagonale.</summary>
    private const float TrainStepSecondsDiagonal = TrainStepTicksDiagonal / (float)TickScale;

    /// <summary>Welches Bilderband ein Waggon zeigt. ⚠ 13.08.2026 — Waggon 3
    /// stand hier auf 58 (Güterwagen) und ist in Wahrheit eine <b>zweite
    /// LOK</b>: der Zeichenverteiler @0x42B4BF (F: 0x42A6AC) schickt Waggon 0
    /// und Waggon 3 auf denselben Sprite-Grundwert <c>[0x77C956]</c>, die
    /// Waggons 1 und 2 dagegen auf <c>[0x77C95A]</c>. Waggon 3 wird nur um vier
    /// von acht Richtungen gedreht (@0x42B542: <c>add bx,4; cmp bx,7; jle;
    /// sub bx,8</c>) — Zuglok vorn, Schublok hinten, zwei Güterwagen
    /// dazwischen.</summary>
    private static readonly Dictionary<int, int> WagonPart =
        new() { { 0, 57 }, { 1, 58 }, { 2, 58 }, { 3, 57 } };

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
        // ⚠ 12.08.2026 — `step` der Kartendatei zaehlt ROUTENSCHRITTE, gefahren
        // wird aber auf ZELLEN, und davon gibt es weniger (auf einer Diagonale
        // fallen zwei Schritte auf eine Zelle). Der Waggon wird deshalb auf die
        // naechstgelegene Zelle seiner Linie gesetzt — sonst stuende er beim
        // ersten Bild irgendwo hinter dem Streckenende.
        foreach (var w in _wagons)
        {
            if (!_lineCell.TryGetValue(w.Line, out var cells) || cells.Count == 0) continue;
            int best = 0;
            float bestD = float.MaxValue;
            for (int i = 0; i < cells.Count; i++)
            {
                float d = (cells[i] - new Vector2(w.Col, w.Row)).LengthSquared();
                if (d < bestD) { bestD = d; best = i; }
            }
            w.Step = best;
            w.Col = cells[best].X; w.Row = cells[best].Y;
            if (_lineCellPiece.TryGetValue(w.Line, out var cp) && best < cp.Count)
                w.Piece = cp[best];
        }
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
            // ⚠ 12.08.2026 — der Zug laeuft auf den ZELLEN, nicht auf den
            // Routenpunkten. Ein Routenpunkt auf halber Zeile liegt 10 px
            // neben der Schiene, die dort liegt (siehe RailBuildCells); mit
            // ihm fuhr der Zug auf jeder Diagonale halb neben dem Gleis.
            if (!_lineCell.TryGetValue(w.Line, out var cells) || cells.Count < 2) continue;
            w.Move -= dt;
            if (w.Move > 0f) continue;
            w.Move += TrainStepSeconds;
            int next = w.Step + w.Dir;
            // OURS: what the original does at the end of a line was not
            // reconstructed, so the whole train turns round rather than
            // freezing — a wagon that would run off the end marks the line
            if (next >= cells.Count || next < 0) { flip.Add(w.Line); continue; }
            w.Step = next;
            var p = cells[w.Step];
            w.Col = p.X; w.Row = p.Y;
            if (_lineCellPiece.TryGetValue(w.Line, out var pcs) && w.Step < pcs.Count)
                w.Piece = pcs[w.Step];
            moved = true;
        }
        foreach (var w in _wagons)
            if (flip.Contains(w.Line)) w.Dir = -w.Dir;
        if (moved) QueueRedraw();
    }

    /// <summary>Der Gleiskörper, Teil 65: die Strecke MIT STÜTZEN.
    ///
    /// <para>⚠ 11.08.2026, nach einem Bildschirmfoto des Originals: die Bahn in
    /// Akte Europa ist eine HOCHBAHN. Die Böcke stehen überall in der
    /// Landschaft, die Schiene läuft oben darüber, und der Zug fährt auf ihr.
    /// Wir hatten zuerst Teil 64 genommen — das ist nur der blanke Träger ohne
    /// Stützen, und damit lag die Strecke flach auf dem Boden.</para>
    ///
    /// <para>Beide Teile tragen dieselben acht Richtungen und dieselbe
    /// Leinwandlage: gemessen liegt die Schiene bei 65 auf denselben Zeilen wie
    /// bei 64 (Bild 0 bei 28 gegen 29, Bild 1 bei 20 gegen 20, Bild 6 bei 14
    /// gegen 14), nur reichen die Stützen nach unten weiter (bis Zeile 86
    /// statt 41). Der Anker bleibt deshalb unverändert.</para>
    ///
    /// <para>OFFEN: ob das Original je den flachen Träger 64 benutzt — etwa wo
    /// die Strecke ein Gebäude erreicht oder über festem Boden läuft. Solange
    /// das nicht gelesen ist, bekommt jeder Schritt die Stützenfassung, weil
    /// das dem Foto entspricht.</para></summary>
    private readonly Dictionary<int, Texture2D?> _railTex = new();

    /// <summary>Stück → Bild von Teil 64. Die beiden Reihenfolgen sind NICHT
    /// dieselbe: Waggonbild 0 steht senkrecht, Schienenbild 0 liegt waagerecht.
    /// Ohne diese Tabelle lagen die Gleise kreuz und quer.
    ///
    /// <para>Gemessen statt geraten, und von zwei Seiten:</para>
    /// <list type="bullet">
    /// <item>Stück → Richtung aus ALLEN Routen aller Karten. Nicht je Schritt —
    /// eine isometrische Diagonale wird als Treppe aus (1,0) und (0,0.5)
    /// gelegt, das Stück bleibt dabei konstant. Also über den ganzen Lauf
    /// gleicher Stücke, umgerechnet in Bildschirmpixel (x·40, y·20). Je Stück
    /// 51 bis 762 Läufe.</item>
    /// <item>Bild → Richtung aus den Pixeln der acht Schienenbilder selbst,
    /// über die Hauptträgheitsachse der undurchsichtigen Punkte.</item>
    /// </list>
    ///
    /// <para>Die Paare treffen sich auf ein Grad genau: Stück 1 bei 154,9° auf
    /// f2 bei 154,4°, Stück 5 bei 158,2° auf f7 bei 158,6°, Stück 3 bei 26,4°
    /// auf f3 bei 26,5°, Stück 6 bei 1,0° auf f0 bei 0,0°. Dass 0 und 4 sowie
    /// 2 und 6 auf dasselbe Bild fallen, ist richtig: eine Schiene hat vier
    /// Achsen, aber acht Fahrtrichtungen.</para>
    ///
    /// <para>⚠ <b>ÜBERHOLT am 12.08.2026</b> und nur noch der Gegenprobe
    /// <c>--rail-lay=cols</c> vorbehalten. Die Tabelle war in einem Punkt
    /// nachweislich falsch (Stück 5 → f7; f7 ist gar kein Diagonalstück,
    /// sondern eine RAMPE, siehe <see cref="RailFrameOfPorts"/>), und sie kann
    /// im Grundsatz nicht stimmen: die vier Diagonalstücke brauchen JE ZWEI
    /// Bilder im Wechsel, ein Stück kann also nicht EIN Bild haben.</para>
    /// </summary>
    private static readonly int[] RailFrameOf = { 1, 2, 0, 3, 1, 7, 0, 4 };

    /// <summary>
    /// <b>Was ein Gleisbild ist</b> — gemessen an Teil 64, alle zehn Bilder,
    /// Spalte für Spalte (Ober- und Unterkante der undurchsichtigen Punkte).
    ///
    /// <para>Die Leinwand 64×56 trägt EINE Kartenzelle: x 10..49 sind die
    /// 40 px der Zelle, y 21..40 ihre 20 px. Jedes Bild verbindet genau zwei
    /// der vier RANDMITTEN dieser Zelle —
    /// <c>L</c>(10,31) <c>R</c>(50,31) <c>T</c>(30,21) <c>B</c>(30,41):</para>
    /// <code>
    ///   f0  x 10..49  y 29..33 durchgehend flach     L–R   waagerecht
    ///   f1  x 27..32  y 20..41                       T–B   senkrecht
    ///   f2  x 27..49  links y40  rechts y29          B–R
    ///   f3  x 27..49  links y20  rechts y29          T–R
    ///   f4  x 10..32  links y29  rechts y40          L–B
    ///   f5  x 10..32  links y29  rechts y20          L–T
    /// </code>
    /// <b>Sechs Bilder, sechs Paare</b> — das ist der vollständige Satz, und es
    /// ist der Beweis, dass ein Gleisstück eine ZELLE ist und keine Fahrtrichtung.
    /// f2 ist übrigens Punkt für Punkt f5, um (+20,+10) in der Leinwand
    /// verschoben (f2(47,30) = f5(27,20)), ebenso f3 zu f4: dieselbe Form in
    /// der linken und in der rechten Zellhälfte.
    ///
    /// <para><b>Und was die restlichen vier sind</b> (Teil 64 und 65 haben je
    /// 20 Bilder; 10..19 sind nur die Schatten von 0..9):</para>
    /// <code>
    ///   f6  x 10..49  links y14  rechts y29    waagerecht, links 15 px höher
    ///   f7  x 10..49  links y29  rechts y14    waagerecht, rechts 15 px höher
    ///   f8  x 27..32  y  5..41                 senkrecht, 15 px länger
    ///   f9  x 27..32  y 19..26                 senkrecht, 15 px kürzer
    /// </code>
    /// <b>15 px ist <see cref="Import.MapBaker.ElevStep"/></b> — die Höhe einer
    /// Geländestufe. f6..f9 sind also die RAMPEN über einen Höhensprung, nicht
    /// Diagonalen. Genau deshalb war <c>RailFrameOf[5] = 7</c> falsch: dort
    /// stand ein 40 px breites Rampenbild an einer Stelle, an der ein 20 px
    /// breites Halbstück hingehört — der vom Spieler gemeldete Knick. Dazu
    /// passt die Fehlermeldung des Originals »Wrong index of <b>slope</b> for
    /// train«. ⚠ WANN das Original eine Rampe legt, ist NICHT gelesen; wir
    /// legen keine.
    /// </summary>
    private const int PortL = 0, PortR = 1, PortT = 2, PortB = 3;

    /// <summary>Bild zu einem Paar Randmitten. Siehe die Tabelle oben.</summary>
    private static int RailFrameOfPorts(int a, int b)
    {
        int lo = Mathf.Min(a, b), hi = Mathf.Max(a, b);
        return (lo, hi) switch
        {
            (PortL, PortR) => 0,
            (PortT, PortB) => 1,
            (PortR, PortB) => 2,
            (PortR, PortT) => 3,
            (PortL, PortB) => 4,
            (PortL, PortT) => 5,
            _ => 0,
        };
    }

    private static int RailOppositePort(int p)
        => p switch { PortL => PortR, PortR => PortL, PortT => PortB, _ => PortT };

    /// <summary>Über welche Randmitte Zelle <paramref name="a"/> zur
    /// Nachbarzelle <paramref name="b"/> geht, oder -1 wenn sie nicht
    /// benachbart sind.</summary>
    private static int RailPortTo(Vector2 a, Vector2 b)
    {
        int dx = Mathf.RoundToInt(b.X - a.X), dy = Mathf.RoundToInt(b.Y - a.Y);
        if (dy == 0 && dx == 1) return PortR;
        if (dy == 0 && dx == -1) return PortL;
        if (dx == 0 && dy == 1) return PortB;
        if (dx == 0 && dy == -1) return PortT;
        return -1;
    }

    /// <summary>
    /// <b>Die Strecke als Kette von KARTENZELLEN</b> — das, was der Route
    /// fehlte, und die Antwort auf »die Bahnstrecke ist nicht sauber
    /// zusammengebaut«.
    ///
    /// <para>Die Route steht auf einem Gitter aus ganzen Spalten und HALBEN
    /// Zeilen (<c>SPOJ_STEP</c>, cwm_extra.py): ein Schritt ist (±1,0), (0,±1),
    /// (0,±0,5) oder (±1,±0,5). Ein Gleisbild ist dagegen eine ganze Zelle.
    /// Ein Routenpunkt auf GANZER Zeile ist deshalb selbst eine Zelle; ein
    /// Punkt auf HALBER Zeile liegt auf der Grenze und gehört zur Zelle
    /// darüber oder darunter. Welche von beiden, sagen die Nachbarn: ein
    /// Schritt, der die SPALTE wechselt, darf die Zellzeile nicht wechseln —
    /// sonst wären die beiden Zellen nur über eine Ecke verbunden, und dafür
    /// gibt es kein Bild.</para>
    ///
    /// <para><b>Belegt über alle 30 Karten:</b> 576 Linien, 19015 Zellen —
    /// <b>0</b> Zellenpaare, die nicht Kante an Kante liegen, und <b>0</b>
    /// Formen ausserhalb der sechs vorhandenen Bilder. Die alte Legeart (ein
    /// Bild je Routenschritt an <c>RailPoint</c>) reisst dagegen an jeder
    /// Diagonale und an jeder Ecke auf; <c>--rail-lay=cols</c> zeigt sie.</para>
    ///
    /// <para>Nebenbei fällt damit die Frage weg, welches Bild ein Stück
    /// bekommt: die FORM steht in den Nachbarzellen, nicht in einer Tabelle.
    /// Zur Gegenprobe: über alle Karten liefert Stück 6 in 4130 von 4130
    /// Fällen f0, und die Diagonalstücke liefern sauber je zwei Bilder im
    /// Wechsel (Stück 7 → f4 1672× / f3 1530×, Stück 1 → f5 1313× / f2
    /// 1036×) — genau die Abwechslung, die eine Diagonale aus Halbstücken
    /// braucht und die eine Tabelle Stück→Bild nicht abbilden kann.</para>
    /// </summary>
    private void RailBuildCells(int line, List<Vector2> route, List<int> pieces)
    {
        int n = route.Count;
        if (n < 1) return;
        var row = new int[n];
        var fixedRow = new bool[n];
        for (int i = 0; i < n; i++)
        {
            float r = route[i].Y;
            int fl = Mathf.FloorToInt(r + 0.001f);
            bool whole = Mathf.Abs(r - Mathf.Round(r)) < 0.001f;
            row[i] = whole ? Mathf.RoundToInt(r) : fl;
            fixedRow[i] = whole;
        }
        // Ein Schritt, der die Spalte wechselt, erzwingt dieselbe Zellzeile.
        // Solange weitergeben, bis nichts mehr dazukommt (die Ketten sind kurz).
        for (int pass = 0; pass < n; pass++)
        {
            bool grew = false;
            for (int i = 0; i + 1 < n; i++)
            {
                if (Mathf.RoundToInt(route[i].X) == Mathf.RoundToInt(route[i + 1].X)) continue;
                if (!fixedRow[i] && fixedRow[i + 1]) { row[i] = row[i + 1]; fixedRow[i] = true; grew = true; }
                else if (!fixedRow[i + 1] && fixedRow[i]) { row[i + 1] = row[i]; fixedRow[i + 1] = true; grew = true; }
            }
            if (!grew) break;
        }

        var cells = new List<Vector2>();
        var cellPiece = new List<int>();
        var cellOf = new int[n];
        for (int i = 0; i < n; i++)
        {
            var cell = new Vector2(Mathf.RoundToInt(route[i].X), row[i]);
            if (cells.Count == 0 || cells[^1] != cell)
            {
                cells.Add(cell);
                cellPiece.Add(i < pieces.Count ? pieces[i] : 0);
            }
            else if (i < pieces.Count) cellPiece[^1] = pieces[i];
            cellOf[i] = cells.Count - 1;
        }

        _lineCell[line] = cells;
        _lineCellFrame[line] = RailFramesOf(cells);
        _linePath.Remove(line);
        _lineCellPiece[line] = cellPiece;
    }

    /// <summary>
    /// <b>Die Karte legt die Strecke, nicht wir.</b>
    ///
    /// <para>⚠ <b>13.08.2026 — der Befund, auf den »deine bahnstrecken sehen
    /// teils crazy aus, du liest irgendwas falsch« zutraf.</b> Bis heute wurde
    /// die Strecke aus den Streckencodes der Linie NACHGEBAUT und ihre Form aus
    /// den Nachbarzellen ERSCHLOSSEN. Beides war unsere Konstruktion. Die Karte
    /// führt jede Gleiszelle einzeln mit ihrem Bild — <b>sec22</b>, 3000 Sätze
    /// zu 5 Byte nach 0xc2c220 (siehe <see cref="Import.CwmExtra.RailCell"/>).
    /// Auf NET02 sind das <b>1193</b> Zellen; unsere Ableitung legte
    /// <b>1341</b> Stücke.</para>
    ///
    /// <para>Die Karte ist dabei in sich vollständig: über NET02/03/04/05/08
    /// gemessen liegen die Zellen einer Linie im Feld <b>fortlaufend</b>
    /// (0 von 4263 Paaren nicht) und <b>Kante an Kante</b> (0 von 4263 nicht).
    /// Die Kette entsteht also durch schlichtes Sortieren nach dem Platz.</para>
    ///
    /// <para>Was diese Zeile mitbringt und die Ableitung nie konnte: die vier
    /// RAMPEN. Bild 6..9 stehen ausnahmslos auf einer Zelle, deren Geländebyte
    /// +3 die passende Stufe nennt (147/147, 170/170, 180/180, 118/118) — und
    /// NET02 hat davon 128 Stück, die wir bisher flach gelegt haben.</para>
    ///
    /// <para>Vor dem Übernehmen wird gemessen, wie weit die alte Ableitung
    /// danebenlag; die vier Zahlen gehen in <c>--rail-check</c>.</para>
    /// </summary>
    private void RailAdoptCells()
    {
        RailCellsFromMap = _railCells.Count;
        RailChainDropped = RailChainSplit = 0;
        RailChainChecked = RailChainWrongLen = RailChainWorstDelta = 0;
        RailChainOrphanLines = RailChainOrphanCells = 0;
        RailChainWorstWhere = "";
        RailCellsBroken = 0;
        RailDiffOnlyOurs = RailDiffOnlyMap = RailDiffFrame = RailDiffChecked = 0;
        if (_railCells.Count == 0) return;

        // 1) messen: unsere alte Ableitung gegen die Karte, Zelle fuer Zelle
        var mapCell = new Dictionary<(int, int), RailCell>();
        foreach (var c in _railCells)
        {
            if (c.Broken) RailCellsBroken++;
            mapCell[(c.Col, c.Row)] = c;
        }
        // rail_pylon_pass @0x4B0350 — die Fassung jeder Stuetze aus ihren beiden
        // angeschlossenen Nachbarn. Nur die Plaetze mit platz%6==0 brauchen sie.
        RailPylonPass();

        var ourCell = new Dictionary<(int, int), int>();
        foreach (var kv in _lineCell)
        {
            if (!_lineCellFrame.TryGetValue(kv.Key, out var fr)) continue;
            for (int i = 0; i < kv.Value.Count && i < fr.Count; i++)
                ourCell[(Mathf.RoundToInt(kv.Value[i].X), Mathf.RoundToInt(kv.Value[i].Y))] = fr[i];
        }
        foreach (var kv in ourCell)
        {
            if (!mapCell.TryGetValue(kv.Key, out var c)) { RailDiffOnlyOurs++; continue; }
            RailDiffChecked++;
            if (c.Base != kv.Value) RailDiffFrame++;
        }
        foreach (var kv in mapCell) if (!ourCell.ContainsKey(kv.Key)) RailDiffOnlyMap++;

        // 2) uebernehmen: je Linie die Zellen der Karte in der Reihenfolge ihres
        //    Platzes. Das ist die Fahrtrichtung — der Kartenbauer legt sie vom
        //    einen Ende zum anderen, und die Enden stimmen mit den Knoten.
        var byLine = new Dictionary<int, List<RailCell>>();
        foreach (var c in _railCells)
        {
            if (!byLine.TryGetValue(c.Line, out var l)) byLine[c.Line] = l = new List<RailCell>();
            l.Add(c);
        }
        foreach (var kv in byLine)
        {
            var l = kv.Value;
            l.Sort((x, y) => x.Index - y.Index);
            // Nur den Lauf behalten, der zu dieser Linie gehoert — siehe
            // RailOwnRun: Liniennummer 0 sammelt auf sieben Karten Fremdzellen.
            var run = RailOwnRun(kv.Key, l);
            var cells = new List<Vector2>(run.Len);
            var frames = new List<int>(run.Len);
            var broken = new List<bool>(run.Len);
            for (int i = run.Start; i < run.Start + run.Len; i++)
            {
                cells.Add(new Vector2(l[i].Col, l[i].Row));
                frames.Add(l[i].Base);
                broken.Add(l[i].Broken);
            }
            // Die Kette so drehen, dass sie an Knoten1 anfaengt: der Zug faehrt
            // Bud1 -> Bud2, und die Fahrtrichtung haengt an der Reihenfolge.
            if (RailChainFlipped(kv.Key, cells))
            { cells.Reverse(); frames.Reverse(); broken.Reverse(); }
            _lineCellBroken[kv.Key] = broken;
            _lineCell[kv.Key] = cells;
            _lineCellFrame[kv.Key] = frames;
            _linePath.Remove(kv.Key);
            // Das Waggonbild braucht weiter ein STUECK je Zelle. Es kommt jetzt
            // aus der Kette selbst (Richtung zur naechsten Zelle), nicht mehr aus
            // den Streckencodes: die Zellenzahl der Karte und die Codezahl der
            // Linie sind nicht dieselbe, ein Index waere also verschoben.
            _lineCellPiece[kv.Key] = RailPiecesOfChain(cells);
            // Die Kette gegen die MESSLATTE der Datei halten — siehe
            // RailChainMeasureLen. Das ist der einzige Zaehler, der eine falsche
            // Kettenlaenge ueberhaupt sehen kann.
            RailChainMeasureLen(kv.Key, l.Count, cells);
        }
        _railTiles = null;
    }

    /// <summary>
    /// <b>Ist die Kette, auf der der Zug fährt, so lang wie die Datei sagt?</b>
    ///
    /// <para>⚠ 13.08.2026 — <b><c>delka</c> ist NICHT die Zellenzahl.</b> Über
    /// alle 30 Karten gezählt (<c>aekernel-tools/rail_delka_count.py</c>):
    /// <c>delka − (Zellen mit dieser Liniennummer) = 4</c> in <b>369 von 371</b>
    /// Fällen. Die beiden Ausnahmen sind genau die zwei Karten, auf denen
    /// Liniennummer 0 Fremdzellen einsammelt (DM_4: 118 statt 41, DM_6: 64 statt
    /// 26) — also kein Gegenbeispiel zur Regel, sondern der Fehler, den sie
    /// sichtbar macht.</para>
    ///
    /// <para><b>Woher die 4 kommen — nachgerechnet, nicht geraten</b>
    /// (<c>aekernel-tools/rail_route_vs_cells.py</c>): richtet man die sec22-Kette
    /// auf die <c>delka+1</c> Routenpunkte der Streckencodes aus, fallen
    /// <b>immer genau 5 Punkte</b> weg — zwei an einem Ende und drei am anderen,
    /// <b>369 von 369, kein Gegenbeispiel</b>. Fünf Punkte weniger sind vier
    /// Schritte weniger, daher die 4. Das sind die Stücke, die INNERHALB der
    /// beiden Endgebäude liegen: das Gleisbild dort bringt das Gebäude selbst
    /// mit, die Karte legt keine eigene Zelle dafür. Beispiel map_DM_6 Linie 14
    /// (delka 13): Route <c>(42,64)…(52,67)</c>, sec22 <c>(44,64)…(49,67)</c> —
    /// exakt Routenpunkt 2 bis 10.</para>
    ///
    /// <para>⚠ Welches Ende die 2 und welches die 3 bekommt, ist je Linie stabil
    /// (285 mal Kopf 2, 84 mal Kopf 3; dieselben Linien 24/31/32 auf DM_3, DM_5,
    /// DM_6, DM_7 und DM_10). <b>Der Grund dafür ist NICHT gelesen</b> — die
    /// Messlatte braucht ihn nicht, sie zählt nur die Summe 5.</para>
    ///
    /// <para><b>Welche Fehlerklasse das hier sieht</b>, die kein anderer Zähler
    /// sah: eine Kette mit der falschen LÄNGE. <see cref="RailEndFar"/> fragt, ob
    /// die Enden an ihren Gebäuden liegen — das können sie auch, wenn in der
    /// Mitte ein fremder Lauf mit drinhängt. <see cref="RailChainSplit"/> zählt
    /// nur, wie oft überhaupt geschnitten wurde, nicht ob richtig. Diese Zahl
    /// kommt aus der DATEI (<c>delka</c>) und nicht aus unserer Ableitung, sie
    /// kann sich also nicht mit sich selbst vergleichen.</para>
    ///
    /// <para>Gegenprobe zum Stand vom 13.08.2026: über alle 30 Karten stimmt die
    /// gewählte Kette in <b>371 von 371</b> Fällen mit <c>delka−4</c> überein
    /// (<c>aekernel-tools/rail_own_run_check.py</c> rechnet dieselbe Wahl in
    /// Python nach). Ein Ausschlag hier heißt also: die Wahl des eigenen Laufs
    /// ist kaputtgegangen.</para>
    /// </summary>
    private void RailChainMeasureLen(int line, int rawCells, List<Vector2> cells)
    {
        RailLine? l = null;
        foreach (var x in _railLines) if (x.Slot == line) { l = x; break; }
        if (l == null || l.Steps <= 0)
        {
            // Gleiszellen ohne Linienkopf. Auf DM_3/5/7/10 sind das die 38
            // Zellen zweier Linien, die der Spielstand nicht mehr fuehrt (siehe
            // RailOwnRun) — es faehrt nichts darauf, gezeichnet werden sie.
            RailChainOrphanLines++;
            RailChainOrphanCells += rawCells;
            return;
        }
        RailChainChecked++;
        int want = l.Steps - 4;
        int delta = cells.Count - want;
        if (delta == 0) return;
        RailChainWrongLen++;
        if (Mathf.Abs(delta) <= Mathf.Abs(RailChainWorstDelta)) return;
        RailChainWorstDelta = delta;
        RailChainWorstWhere =
            $"Linie {line}: Kette {cells.Count} Zellen, delka {l.Steps} erwartet {want} " +
            $"({rawCells} Rohzellen) — von ({cells[0].X:0},{cells[0].Y:0}) " +
            $"bis ({cells[^1].X:0},{cells[^1].Y:0}), Gebaeude {l.Bud1}/{l.Bud2}";
    }

    /// <summary>Die Zähler zu <see cref="RailChainMeasureLen"/>. <c>WorstDelta</c>
    /// ist vorzeichenbehaftet: positiv = die Kette ist zu LANG (Fremdzellen
    /// hängen mit dran), negativ = zu kurz (ein Stück fehlt).</summary>
    public int RailChainChecked, RailChainWrongLen, RailChainWorstDelta;
    public string RailChainWorstWhere = "";

    /// <summary>Gleiszellen, deren Liniennummer in der Datei keinen Linienkopf
    /// hat — siehe <see cref="RailChainMeasureLen"/>.</summary>
    public int RailChainOrphanLines, RailChainOrphanCells;

    /// <summary>Liegt der ANFANG dieser Kette naeher an Knoten2 als an Knoten1?
    /// Dann gehoert sie gedreht. Ohne Endgebaeude bleibt sie, wie sie ist.</summary>
    /// <summary>
    /// <b>Nur die Zellen behalten, die wirklich zu dieser Linie gehören.</b>
    ///
    /// <para>⚠ 14.08.2026 — <b>Liniennummer 0 sammelt Fremdzellen ein.</b>
    /// Gemessen über alle 30 Karten: die Zuordnung »sec22-Linie n gehört zu
    /// links-Platz n« passt in <b>369 von 371</b> Fällen (die 1-basierte
    /// Lesart nur in 183 von 359, also auf Zufallsniveau). Auffällig ist
    /// ausschliesslich <b>Linie 0</b>, und zwar auf sieben Karten — auf fünf
    /// davon gibt es den links-Platz 0 gar nicht. Über alle Karten tragen
    /// <b>499 von 9648</b> Gleiszellen (5,2 %) die Nummer 0.</para>
    ///
    /// <para>Sichtbar wird das an <c>map_DM_4</c>: Linie 0 bekommt dort
    /// <b>118 Zellen bei delka 45</b>, mit genau ZWEI Bruchstellen — also ihre
    /// eigene Strecke plus zwei fremde Läufe, quer über die Karte. Die Waggons
    /// dieser Linie fuhren damit auf einer Route, die nicht ihre ist, und das
    /// Linienende lag 122 Zellen von seinem Gebäude.</para>
    ///
    /// <para><b>Die Regel hier ist keine Erfindung:</b> eine SPOJ-Linie läuft
    /// von einem Endgebäude zum anderen und ist Kante an Kante durchgehend —
    /// das ist über NET02/03/04/05/08 gemessen (0 von 4263 Paaren nicht). Ein
    /// Lauf, der kein Endgebäude berührt, kann darum nicht dazugehören.
    /// Behalten wird der zusammenhängende Lauf, dessen Enden den Endgebäuden am
    /// nächsten liegen; ohne auflösbare Gebäude der längste.</para>
    ///
    /// <para>⭐ 13.08.2026 — <b>WOHER die Zellen mit der 0 kommen, ist jetzt
    /// belegt.</b> Sie sind die Strecken von Linien, die der SPIELSTAND nicht
    /// mehr führt, während ihr Gleis auf der Karte liegengeblieben ist. Der
    /// Beleg ist eine Deckung, keine Vermutung: <c>map_DM_6</c> und die
    /// Kampagnenkarte <c>map_25</c> sind dieselbe Karte (beide »Chanel Tunnel«,
    /// beide 180x220). Die 38 Fremdzellen von DM_6 zerfallen in zwei Läufe, und
    /// beide liegen auf einer Strecke von map_25:</para>
    /// <list type="bullet">
    /// <item>13 Zellen <c>(115,53)…(118,44)</c> = map_25 Linie 0, <c>delka 17</c>
    /// — und <c>17−4 = 13</c>, die Zahl stimmt auf den Punkt; 10 der 13 liegen
    /// exakt auf deren Routenpunkten (die 3 Abweichler sind der bekannte
    /// Halbschritt-Versatz zwischen sec22 und den Streckencodes).</item>
    /// <item>25 Zellen <c>(52,202)…(49,185)</c> auf map_25 Linie 26,
    /// <c>delka 27</c>, mit denselben beiden Endpunkten.</item>
    /// </list>
    /// <para>Genau diese 38 Zellen tragen auf <c>DM_3</c>, <c>DM_5</c>,
    /// <c>DM_6</c>, <c>DM_7</c> und <c>DM_10</c> die Nummer 0 — auf vier davon
    /// gibt es den Linienkopf 0 überhaupt nicht mehr, auf DM_6 gehört er einer
    /// ANDEREN Linie (Gebäude 68/10, <c>delka 30</c>). Die Linien wurden beim
    /// Umbau zum Gefechtsspielstand also entfernt beziehungsweise umnummeriert,
    /// und eine Zelle ohne Linienkopf liest sich als 0 zurück.</para>
    ///
    /// <para>⚠ Was damit noch NICHT gelesen ist: ob <c>rail_add</c> @0x4AFA90
    /// die 0 aktiv hinschreibt oder ob sie nur der Nullwert eines abgeräumten
    /// Feldes ist. Für die Kur ist das gleichgültig — sie wirft nur weg, was
    /// nachweislich nicht zusammenhängt, und gezeichnet wird die Zelle
    /// weiterhin, denn das Gleis kommt aus <c>_railCells</c> und nicht aus der
    /// Kette.</para>
    ///
    /// <para>⚠ <b>Und die Kur GREIFT — die Gegenmeldung war am falschen
    /// Gegenstand gemessen.</b> »DM_6 Linie 0: 64 Zellen bei delka 30,
    /// zusammenhängend« stimmt in keinem der beiden Punkte. Die 64 sind die
    /// ROHZELLEN, nicht die Kette; und zusammenhängend sind sie nicht, sondern
    /// drei Läufe (25 + 13 + 26). Gewählt wird der 26er — und <c>delka−4 = 26</c>
    /// (siehe <see cref="RailChainMeasureLen"/>). Über alle 30 Karten stimmt die
    /// gewählte Kette in <b>371 von 371</b> Fällen mit <c>delka−4</c>.</para></summary>
    public int RailChainDropped, RailChainSplit;

    private (int Start, int Len) RailOwnRun(int line, List<RailCell> l)
    {
        if (RailProbeSkipOwnRun) return (0, l.Count);   // Gegenprobe, siehe dort
        if (l.Count < 2) return (0, l.Count);
        var runs = new List<(int Start, int Len)>();
        int s = 0;
        for (int i = 1; i <= l.Count; i++)
        {
            bool cut = i == l.Count
                    || RailPortTo(new Vector2(l[i - 1].Col, l[i - 1].Row),
                                  new Vector2(l[i].Col, l[i].Row)) < 0;
            if (cut) { runs.Add((s, i - s)); s = i; }
        }
        if (runs.Count <= 1) return (0, l.Count);
        RailChainSplit++;

        Entity? a = null, b = null;
        foreach (var x in _railLines)
        {
            if (x.Slot != line) continue;
            foreach (var e in _entities)
            {
                if (!e.IsBuilding) continue;
                if (a == null && e.Slot == x.Bud1) a = e;
                if (b == null && e.Slot == x.Bud2) b = e;
            }
            break;
        }

        static int Near(RailCell c, Entity e)
        {
            int dc = Mathf.Max(Mathf.Max(e.Col - c.Col, 0), c.Col - (e.Col + Mathf.Max(1, e.FootW) - 1));
            int dr = Mathf.Max(Mathf.Max(e.Row - c.Row, 0), c.Row - (e.Row + Mathf.Max(1, e.FootH) - 1));
            return Mathf.Max(dc, dr);
        }

        var best = runs[0];
        int bestScore = int.MaxValue;
        foreach (var r in runs)
        {
            int score;
            if (a != null || b != null)
            {
                int d = int.MaxValue;
                foreach (var c in new[] { l[r.Start], l[r.Start + r.Len - 1] })
                {
                    if (a != null) d = Mathf.Min(d, Near(c, a));
                    if (b != null) d = Mathf.Min(d, Near(c, b));
                }
                score = d * 1000 - r.Len;          // erst am Gebaeude, dann laenger
            }
            else score = -r.Len;                    // ohne Gebaeude: der laengste
            if (score < bestScore) { bestScore = score; best = r; }
        }
        RailChainDropped += l.Count - best.Len;
        return best;
    }

    private bool RailChainFlipped(int line, List<Vector2> cells)
    {
        if (cells.Count < 2) return false;
        RailLine? l = null;
        foreach (var x in _railLines) if (x.Slot == line) { l = x; break; }
        if (l == null) return false;
        Entity? a = null, b = null;
        foreach (var e in _entities)
        {
            if (!e.IsBuilding) continue;
            if (a == null && e.Slot == l.Bud1) a = e;
            if (b == null && e.Slot == l.Bud2) b = e;
        }
        if (a == null || b == null) return false;
        float d0 = Mathf.Abs(cells[0].X - a.Col) + Mathf.Abs(cells[0].Y - a.Row)
                 + Mathf.Abs(cells[^1].X - b.Col) + Mathf.Abs(cells[^1].Y - b.Row);
        float d1 = Mathf.Abs(cells[^1].X - a.Col) + Mathf.Abs(cells[^1].Y - a.Row)
                 + Mathf.Abs(cells[0].X - b.Col) + Mathf.Abs(cells[0].Y - b.Row);
        return d1 < d0;
    }

    /// <summary>Das Fahrtrichtungs-STUECK je Zelle einer Kette, in der
    /// Zaehlweise des Originals (Bildtabelle @0x539400: 0 = nach unten,
    /// 2 = nach links, 4 = nach oben, 6 = nach rechts; die ungeraden sind die
    /// Halbschritte einer Diagonale, die es auf einer Zellenkette nicht gibt).
    /// Der Waggon dreht damit an derselben Stelle wie das Gleis.</summary>
    private static List<int> RailPiecesOfChain(List<Vector2> cells)
    {
        var pcs = new List<int>(cells.Count);
        for (int i = 0; i < cells.Count; i++)
        {
            var from = cells[i];
            var to = i + 1 < cells.Count ? cells[i + 1] : cells[i];
            if (i + 1 >= cells.Count && i > 0) { from = cells[i - 1]; to = cells[i]; }
            int dx = Mathf.RoundToInt(to.X - from.X), dy = Mathf.RoundToInt(to.Y - from.Y);
            pcs.Add(dy > 0 ? 0 : dy < 0 ? 4 : dx < 0 ? 2 : 6);
        }
        return pcs;
    }

    /// <summary>Das Bild je Zelle aus den NACHBARZELLEN. Steht seit dem
    /// 12.08.2026 für sich, weil <see cref="RailSnapToDock"/> die Kette
    /// nachträglich ändert und die Bilder dann neu gerechnet werden müssen.</summary>
    private static List<int> RailFramesOf(List<Vector2> cells)
    {
        var frames = new List<int>(cells.Count);
        for (int i = 0; i < cells.Count; i++)
        {
            int a = i > 0 ? RailPortTo(cells[i], cells[i - 1]) : -1;
            int b = i + 1 < cells.Count ? RailPortTo(cells[i], cells[i + 1]) : -1;
            if (a < 0 && b < 0) { frames.Add(0); continue; }
            if (a < 0) a = RailOppositePort(b);
            if (b < 0) b = RailOppositePort(a);
            frames.Add(RailFrameOfPorts(a, b));
        }
        return frames;
    }

    /// <summary>
    /// <b>Die ANSCHLUSSZEILE eines Gebäudes</b> — die Musterzeile, in der seine
    /// Grafik ihr eigenes Gleisstück trägt, gerechnet von der Ecke des
    /// Gebäudes (<c>Entity.Row</c>) aus.
    ///
    /// <para><b>So gemessen</b> (die Zahlen stehen bei
    /// <see cref="RailSnapToDock"/>): das Muster des Gebäudes wurde mit
    /// <see cref="Import.MapBaker"/>s eigener Rechnung
    /// <c>y = zeile·20 + BlitAnchor + Kachel.YOff</c> zusammengesetzt und
    /// daneben unser Gleisbild (Teil 64, f0) mit genau der Ablage aus
    /// <see cref="DrawRailTrack"/> gelegt — <c>Anker − ComposedAnchor +
    /// RailDeckOffset</c>. Dann wurde nachgesehen, in WELCHER Zeile die
    /// Schiene des Gleisbildes auf den Träger der Gebäudegrafik trifft.
    /// Beides steht auf demselben Gitter, der Vergleich ist also ein
    /// Pixelvergleich und keine Schätzung.</para>
    ///
    /// <list type="bullet">
    /// <item><b>Feldbahnhof (12), Zeile 0.</b> Sein Stummel sitzt LINKS UND
    /// RECHTS in Musterzeile 0; darunter, in Zeile 1, stehen die
    /// Gitterträger mit dem Andreaskreuz. Der Grundriss ist 3×4, der Stummel
    /// stößt also an (Spalte−1, Zeile+0) und (Spalte+3, Zeile+0).</item>
    /// <item><b>Bahnstation (6), Zeile 1.</b> Ihr Träger läuft LINKS durch
    /// Musterzeile 1, das Andreaskreuz sitzt in Zeile 2. Im Bild aus dem
    /// laufenden Spiel (map_NET02, Bahnhof auf 191,55, Höhe 1) liegt der
    /// Träger auf Bildschirmzeile 400 bei Zoom 2 — genau die Deckhöhe von
    /// Zellzeile 56 = Zeile+1.</item>
    /// <item><b>Basis (1), Zeile 0.</b> Anbau RECHTS, Grundriss 6×4.</item>
    /// </list>
    ///
    /// <para><see cref="int.MinValue"/> heißt »nicht gemessen« — an so einem
    /// Gebäude bleibt die Strecke unangetastet. Das gilt für Fabrik (2,3,4),
    /// Flughafen (9), Mine (10) und Werft (16): dort ist im Muster kein
    /// Träger auszumachen, der sich mit unserem Gleisbild deckt, und geraten
    /// wird hier nicht.</para>
    /// </summary>
    /// <para>⚠ <b>13.08.2026 nachgemessen, und zwar an der FARBE statt am
    /// Augenmaß.</b> Die drei Zeilen standen seit dem 11.08. auf −1 / 0 / −1,
    /// »am Bild entschieden«. Jetzt ist der Anbau gezählt, nicht geschätzt:
    /// das Gleisbild Teil 64/f0 traegt seine Schiene auf fuenf Leinwandzeilen
    /// mit der Farbfolge 111,91,63 / 191,151,151 / 115,67,67 / 115,67,67 /
    /// 63,55,43, und dieselbe Folge steckt im Gebaeudemuster. Gesucht wurde
    /// sie im zusammengesetzten Muster (Kachelsatz 47) mit MapBakers eigener
    /// Rechnung <c>y = zeile·20 − 50 + Kachel.YOff</c>; gefunden wurde
    /// (y gerechnet von der Oberkante der Ankerzeile):</para>
    /// <code>
    ///   Bahnstation (86)  Musterspalte 0        y = −13 und +7   (x 5..27 / 4..34)
    ///   Feldbahnhof (156) Musterspalte 0 und 2  y = −13 und +7   (x 0..18 / 104..119)
    ///   Basis (1)         Musterspalte 5..6     die Folge fehlt ganz
    /// </code>
    /// <para><b>Beide Bahnhoefe sind also ZWEIGLEISIG</b> — zwei Decks, genau
    /// eine Zellzeile (20 px) auseinander, auf den Zeilen −1 und 0. Unsere
    /// Schienenoberkante liegt bei <see cref="RailDeckPixel"/> = 7 px unter
    /// der Oberkante ihrer Zelle; Zeile 0 trifft damit das untere Deck auf den
    /// Pixel, Zeile −1 das obere. Genommen wird <b>0</b>, das untere: es ist
    /// das Deck in der Zeile, die der Gebaeudesatz selbst nennt, und es reicht
    /// im Bild ueber die volle Breite des Stummels (x 0..18 statt 0..10).</para>
    ///
    /// <para>Damit ist auch beantwortet, warum der Spieler »teilweise stimmt
    /// es« sagte: die Bahnstation stand mit 0 auf ihrem Deck, der Feldbahnhof
    /// mit −1 eine ganze Zellzeile = 20 px darueber.</para>
    /// <para><b>⚠ 13.08.2026 — die drei Zahlen sind ÜBERHOLT, und zwar durch
    /// die Karte selbst.</b> sec22 nennt jede Gleiszelle; wo eine Linie endet,
    /// ist damit nicht mehr zu erschließen, sondern abzulesen. Über
    /// NET02/03/04/05/08 gemessen, Linienende gegen Gebäudeecke:</para>
    /// <code>
    ///   Basis (1)        Spalte +6, Zeile +1                    25 von 25
    ///   Bahnstation (6)  Spalte −1/+4, Zeile +1 (24) / +2 (11)
    ///   Feldbahnhof (12) Spalte −1/+3, Zeile +1 (65) / +2 (67)
    /// </code>
    /// <para>Das Deck liegt also auf <b>Zeile +1 und Zeile +2</b>, nicht auf
    /// −1 und 0. Die beiden Bahnhöfe sind weiter ZWEIGLEISIG (zwei Decks, genau
    /// eine Zellzeile auseinander) — die Pixelsuche im Muster hatte nur ihre
    /// Grundlinie zwei Zeilen zu hoch. Die Basis hat <b>ein</b> Deck, 25 von 25
    /// Enden auf Zeile +1; damit ist auch der alte offene Punkt »Basis 11 px,
    /// über alle Karten konstant« erledigt: es war kein halber Zeilenversatz
    /// einer Verladebühne, sondern dieselbe Verschiebung um zwei Zeilen.</para>
    private static int RailDockRow(int bType) => bType switch
    {
        1 => 1,          // Basis        gemessen an sec22, 25 von 25 Enden
        6 => 1,          // Bahnstation  oberes von zwei Decks
        12 => 1,         // Feldbahnhof  oberes von zwei Decks
        _ => int.MinValue,
    };

    /// <summary>Wieviele Decks das Gebäude übereinander hat und wie weit sie
    /// auseinanderliegen: die beiden Bahnhöfe zwei, die Basis eines. Gemessen
    /// an sec22 — siehe <see cref="RailDockRow"/>.</summary>
    private static int RailDockDecks(int bType) => bType is 6 or 12 ? 2 : 1;

    /// <summary>
    /// Wo das Schienendeck des GEBAEUDES liegt, in Pixeln unter der Oberkante
    /// seiner Ankerzeile (<c>Entity.Row</c>) — die Zahl, gegen die der
    /// Pruefstand unsere <see cref="RailDeckPixel"/> haelt.
    ///
    /// <para>Bahnstation und Feldbahnhof: <b>+7</b>, gemessen an der Farbfolge
    /// des Gleisbildes im Muster (siehe <see cref="RailDockRow"/>). Zusammen
    /// mit <c>RailDockRow = 0</c> und <c>RailDeckPixel = 7</c> geht die
    /// Rechnung auf null auf.</para>
    ///
    /// <para>⚠ <b>Basis (1) ist UNSERE SETZUNG.</b> In ihrem Muster steht die
    /// Farbfolge des Gleisbildes nirgends — das Original zeichnet an der Basis
    /// keinen Schienenstummel, sondern eine Verladebuehne. Am rechten Rand
    /// (Musterspalte 5/6, x ≈ 215..245) liegt ein Deckband mit der Folge
    /// 207,171,171 / 115,67,67 / 115,71,71 / 115,71,71 / 59,47,43 auf
    /// y = −2..+2. Seine Oberkante liegt damit auf <b>−2</b>, also auf einer
    /// HALBEN Zellzeile: Zeile −1 laesst 10 px uebrig, Zeile 0 ebenfalls 10 px
    /// in die andere Richtung. Es bleibt bei −1 — und der Pruefstand meldet
    /// die 10 px, damit die Zahl nicht in einem Kommentar versauert.</para>
    /// </summary>
    /// <para><b>⚠ 13.08.2026 nachgezogen.</b> Das Deck sitzt auf der Zeile, die
    /// <see cref="RailDockRow"/> nennt, und darin auf derselben Höhe wie unsere
    /// Schienenoberkante (<see cref="RailDeckPixel"/> = 7). Gerechnet von der
    /// Oberkante der ANKERZEILE sind das <c>20·RailDockRow + 7</c> = <b>27</b>
    /// für alle drei Arten, und das zweite Deck der Bahnhöfe liegt eine
    /// Zellzeile tiefer (47).</para>
    private static int RailDockDeckPixel(int bType)
        => RailDockRow(bType) == int.MinValue
            ? int.MinValue
            : RailDockRow(bType) * TileH + RailDeckPixel;

    /// <summary>Wieviele Linienenden NICHT auf der Anschlusszeile ihres
    /// Gebäudes lagen, und wieviele überhaupt geprüft werden konnten — der
    /// Prüfstand liest beides, sonst wäre »bündig« wieder nur behauptet.</summary>
    public int RailDockOff, RailDockChecked, RailDockMoved;

    /// <summary>
    /// <b>Die Zahl für »schwebt«</b> — der HÖHENunterschied in Bildschirmpixeln
    /// zwischen unserer letzten Gleiszelle und dem Schienendeck des Gebäudes,
    /// je Linienende, NACH dem Nachführen.
    ///
    /// <para>Gerechnet wird in derselben Rechnung, in der beide gezeichnet
    /// werden, Geländehöhe eingeschlossen:</para>
    /// <code>
    ///   Gleis:    zeile·20 − ElevOf(spalte,zeile)·15 + RailDeckPixel
    ///   Gebäude:  Ankerzeile·20 − ElevOf(Ankerspalte,Ankerzeile)·15 + RailDockDeckPixel(typ)
    /// </code>
    /// <para>Die Differenz muss 0 sein. Sie fängt eine falsche Anschlusszeile
    /// (Vielfaches von 20) und eine Geländestufe zwischen Gebäude und letzter
    /// Gleiszelle (Vielfaches von 15). Der zweite Fall ist der, für den das
    /// Original die Rampen f6..f9 hat, die wir nicht legen — er wird hier nur
    /// GEMESSEN, nicht behoben.</para>
    ///
    /// <para>⚠ <b>13.08.2026 — WAS SIE NICHT FÄNGT: den Deckversatz selbst.</b>
    /// Hier stand, sie fange „einen falschen Deckversatz (ein bis zwei Pixel)"
    /// mit. Das ist falsch, und zwar per Konstruktion:
    /// <see cref="RailDeckPixel"/> steckt in <see cref="RailDeckY"/> UND über
    /// <see cref="RailDockDeckPixel"/> in <see cref="RailDockDeckY"/> — es
    /// kürzt sich aus der Differenz heraus. Wer <c>RailDeckOffset</c> um 50
    /// verstellt, bekommt weiterhin „42 von 42 bündig, 0 px". Dieselbe Falle
    /// wie am 11.08. bei der Streckenform: ein Prüfstand, der unsere Ableitung
    /// gegen sich selbst hält.</para>
    ///
    /// <para>Die HÖHE misst darum <c>aekernel-tools/rail_deck_overlay.py</c>:
    /// es setzt das Gebäudemuster aus den exportierten Kacheln mit MapBakers
    /// eigener Rechnung zusammen und legt unser Gleisbild an die Stelle, an die
    /// die Engine es zeichnet — ein Pixelvergleich gegen ORIGINALGRAFIK.
    /// Ergebnis 13.08.: die Schienenoberkante bei Anschlusszeile +1 liegt genau
    /// auf der Oberkante des Gitterträgers des Bahnhofs (Kachelsatz 47, Muster
    /// 86), die Stücke laufen bündig hinein. <b>RailDeckOffset = 23 ist damit
    /// gegen Originalpixel belegt.</b></para>
    ///
    /// <para><c>--rail-lay=nodock</c> nimmt die Korrektur heraus, dann steigt
    /// die Zahl: das ist die Gegenprobe.</para>
    /// </summary>
    public int RailDeckOffSum, RailDeckOffMax, RailDeckOffCount, RailDeckFlush;

    /// <summary>Je Gebäudeart: wieviele Enden bündig sind und wieviele nicht,
    /// samt der schlimmsten Abweichung. Damit steht im Prüfstand, an WELCHER
    /// Gebäudeart es sitzt und an welcher nicht.</summary>
    public readonly Dictionary<int, (int Flush, int Off, int Worst)> RailDeckByType = new();

    /// <summary>Die Höhe, auf der die Schienenoberkante einer Gleiszelle liegt
    /// — Kartenpixel, Gelände eingerechnet.</summary>
    private float RailDeckY(int col, int row)
        => _oy + row * TileH - ElevOf(col, row) * 15 + RailDeckPixel;

    /// <summary>Die Höhe, auf der das Schienendeck eines Gebäudes liegt —
    /// dieselbe Rechnung, aber mit der Kachelgrafik statt dem Gleisbild.</summary>
    private float RailDockDeckY(Entity b)
        => _oy + b.Row * TileH - ElevOf(b.Col, b.Row) * 15 + RailDockDeckPixel(b.BType);

    /// <summary>Ausschalter für die Gegenprobe (<c>--rail-lay=nodock</c>):
    /// mit <c>true</c> bleibt die Strecke, wo sie war. <c>--rail-check</c>
    /// meldet dann dieselbe Zahl unbündiger Enden, aber 0 nachgeführte — und
    /// das Bild zeigt den Fehler wieder. Die Fahne wird hier selbst gelesen,
    /// weil sie zu dieser Korrektur gehört und nicht zum Betrachter.</summary>
    public static bool RailProbeSkipDock
    {
        get
        {
            if (_probeSkipDock.HasValue) return _probeSkipDock.Value;
            bool hit = false;
            foreach (string a in OS.GetCmdlineUserArgs())
                if (a.StartsWith("--rail-lay=") && a["--rail-lay=".Length..].Contains("nodock"))
                    hit = true;
            _probeSkipDock = hit;
            return hit;
        }
    }

    private static bool? _probeSkipDock;

    /// <summary>Gegenprobe zum Weg der Waggons (<c>--rail-lay=mitten</c>): mit
    /// <c>true</c> fahren sie wieder auf den ZELLMITTEN statt auf den Randmitten
    /// der gezeichneten Schiene. Damit lässt sich zeigen, was die Umstellung
    /// bewirkt — der Richtungswechsel je Takt springt von 0,7° auf das
    /// Treppenmass. Siehe <c>RailDrawnPath</c>.</summary>
    public static bool RailProbeCellCentres
    {
        get
        {
            if (_probeCellCentres.HasValue) return _probeCellCentres.Value;
            bool hit = false;
            foreach (string a in OS.GetCmdlineUserArgs())
                if (a.StartsWith("--rail-lay=") && a["--rail-lay=".Length..].Contains("mitten"))
                    hit = true;
            _probeCellCentres = hit;
            return hit;
        }
    }

    private static bool? _probeCellCentres;

    /// <summary>
    /// Gegenprobe zur Kettenlänge (<c>--rail-lay=noown</c>): mit <c>true</c>
    /// bleibt <see cref="RailOwnRun"/> aussen vor, die Kette bekommt also ALLE
    /// Zellen mit ihrer Nummer.
    ///
    /// <para>⚠ <b>Der Schalter ist der Beleg, dass der Zähler überhaupt etwas
    /// sehen kann.</b> Ein Zähler, der auf jeder Karte dasselbe sagt, prüft
    /// nichts — »24 von 24 treffen delka−4« wäre ohne diese Fahne nicht von
    /// einer Zeile zu unterscheiden, die immer grün meldet. Mit
    /// <c>--rail-lay=noown</c> muss <c>map_DM_4</c> auf <b>23 von 24</b> fallen
    /// und Linie 0 mit <b>+77</b> Zellen nennen (map_DM_3: 32 von 33, +13).</para>
    /// </summary>
    public static bool RailProbeSkipOwnRun
    {
        get
        {
            if (_probeSkipOwn.HasValue) return _probeSkipOwn.Value;
            bool hit = false;
            foreach (string a in OS.GetCmdlineUserArgs())
                if (a.StartsWith("--rail-lay=") && a["--rail-lay=".Length..].Contains("noown"))
                    hit = true;
            _probeSkipOwn = hit;
            return hit;
        }
    }

    private static bool? _probeSkipOwn;

    /// <summary>
    /// <b>Führt das Ende jeder Linie auf die Anschlusszeile ihres
    /// Endgebäudes.</b>
    ///
    /// <para>Der Befund, der das nötig macht (gemessen über alle 30 Karten,
    /// 1152 Enden mit bekanntem Endgebäude): das Ende einer Linie liegt
    /// <b>immer</b> auf einer HALBEN Zeile (1152 von 1152) und trifft, in
    /// Zellen gerechnet, je Gebäudeart einen festen Klumpen —
    /// Feldbahnhof (512 Enden) auf Zeile+1 und Zeile+2, Bahnstation
    /// (174) auf Zeile+1 und Zeile+2, Basis (76) auf Zeile+1,
    /// Fabrik (284) auf Zeile+2, Mine (67) auf Zeile+2,
    /// Flughafen (37) auf Zeile+2. Die SPALTE stimmt dabei bis aufs Feld;
    /// nur die ZEILE geht auseinander.</para>
    ///
    /// <para>Gegen die Grafik gehalten passt die Bahnstation damit schon
    /// (Träger in Zeile 1), der <b>Feldbahnhof aber nicht</b>: sein Stummel
    /// sitzt in Zeile 0, die Strecke endet ein bis zwei Zellen darunter. Genau
    /// das ist im Bild zu sehen (map_NET02, Feldbahnhof auf 168,45, Höhe 3):
    /// der Stummel liegt auf der Deckhöhe von Zellzeile 45, unsere Schiene auf
    /// der von Zeile 47 und 48 — <b>zwei Felder tiefer</b>.</para>
    ///
    /// <para>Verschoben wird nur der LETZTE GERADE LAUF der Kette: alle Zellen
    /// am Ende, die dieselbe Zeile teilen, rücken zusammen auf die
    /// Anschlusszeile, und an der Naht wird senkrecht überbrückt. Damit bleibt
    /// die Bedingung erhalten, an der die ganze Legeart hängt — jedes Paar
    /// liegt Kante an Kante (<c>--rail-check</c> zählt es weiter mit).</para>
    ///
    /// <para>⚠ <b>UNSERE SETZUNG ist, DASS überhaupt gerückt wird.</b> Wie das
    /// Original die letzten Zellen an den Bahnsteig führt, ist nicht gelesen —
    /// naheliegend ist, dass es dort die Rampen f6..f9 legt, die wir nicht
    /// legen. Die Anschlusszeile selbst ist dagegen aus der Grafik gemessen
    /// (siehe <see cref="RailDockRow"/>).</para>
    /// </summary>
    private void RailSnapToDock()
    {
        RailDockOff = RailDockChecked = RailDockMoved = 0;
        RailEndChecked = RailEndFar = RailEndWorst = 0;
        RailDeckOffSum = RailDeckOffMax = RailDeckOffCount = RailDeckFlush = 0;
        RailDeckByType.Clear();
        _railTiles = null;              // die gelegten Stuecke neu bauen lassen
        if (_railLines.Count == 0) return;
        var bySlot = new Dictionary<int, Entity>();
        foreach (var e in _entities)
            if (e.IsBuilding && !bySlot.ContainsKey(e.Slot)) bySlot[e.Slot] = e;

        foreach (var l in _railLines)
        {
            if (!_lineCell.TryGetValue(l.Slot, out var cells) || cells.Count < 2) continue;
            if (!_lineCellPiece.TryGetValue(l.Slot, out var pcs)) continue;
            // Rueckfahrkarte: geht beim Ruecken die Bedingung »Kante an Kante«
            // verloren, bleibt die Linie, wie sie war. Lieber ein unbuendiges
            // Ende als eine aufgerissene Strecke — und der Pruefstand meldet
            // das unbuendige Ende ohnehin weiter.
            var keepC = new List<Vector2>(cells);
            var keepP = new List<int>(pcs);
            int moved = 0;
            // ⚠ 13.08.2026 — eine Kette aus sec22 wird NICHT gerueckt. Das
            // Ruecken war der Ersatz dafuer, dass unsere Ableitung das Ende der
            // Strecke nicht traf; die Karte trifft es selbst. Gemessen wird
            // weiter (RailMeasureDeck unten), damit die Zahl den Beweis fuehrt.
            if (_railCells.Count > 0)
            {
                RailMeasureEnd(cells, bySlot, l.Bud2, true);
                RailMeasureEnd(cells, bySlot, l.Bud1, false);
                RailMeasureDeck(cells, bySlot, l.Bud2, true);
                RailMeasureDeck(cells, bySlot, l.Bud1, false);
                continue;
            }
            // erst das Ende (haengt am Listenende), dann der Anfang — sonst
            // verschieben sich die Indizes unter der zweiten Runde weg
            moved += RailSnapEnd(cells, pcs, bySlot, l.Bud2, true) ? 1 : 0;
            moved += RailSnapEnd(cells, pcs, bySlot, l.Bud1, false) ? 1 : 0;
            if (moved > 0 && !RailChainSound(cells))
            {
                cells.Clear(); cells.AddRange(keepC);
                pcs.Clear(); pcs.AddRange(keepP);
                moved = 0;
            }
            RailDockMoved += moved;
            _lineCellFrame[l.Slot] = RailFramesOf(cells);
            _linePath.Remove(l.Slot);
            // erst JETZT messen: nach dem Ruecken und nach einer etwaigen
            // Rueckfahrkarte steht die Kette so da, wie sie gezeichnet wird
            RailMeasureDeck(cells, bySlot, l.Bud2, true);
            RailMeasureDeck(cells, bySlot, l.Bud1, false);
        }
    }

    /// <summary>
    /// <b>Erreicht dieses Linienende sein Gebäude überhaupt?</b> — der Abstand
    /// der letzten Gleiszelle zur Grundfläche des Endgebäudes, in Zellen
    /// (Tschebyschew, also 0 = die Zelle liegt auf dem Grundriss).
    ///
    /// <para>⚠ 13.08.2026 — <b>diese Frage stellte bisher niemand.</b>
    /// <see cref="RailMeasureDeck"/> steigt bei einem Ende, das weiter als eine
    /// Spalte neben dem Gebäude liegt, wortlos aus (dieselbe Schranke wie in
    /// <see cref="RailSnapEnd"/>) — die schöne Zahl »42 von 42 Enden bündig«
    /// zählt also nur die Enden, die den Vorfilter schon bestanden haben. Ein
    /// Ende, das ganz woanders aufhört, fällt dort heraus und taucht in keiner
    /// Meldung auf. Genau die Fehlerklasse zählt jetzt hier mit.</para>
    ///
    /// <para>Und die Zeile, die dafür weichen konnte, war noch schlimmer:
    /// »Anschluss: 0 von 0 Enden lagen NICHT auf der Anschlusszeile« berichtete
    /// über das RÜCKEN — und das läuft seit sec22 gar nicht mehr
    /// (<see cref="RailSnapToDock"/> steigt vorher aus). Eine Null von Null,
    /// die sich wie ein bestandener Prüflauf liest.</para></summary>
    public int RailEndChecked, RailEndFar, RailEndWorst;

    /// <summary>Welches Ende das schlimmste war — damit die Zahl nachschlagbar
    /// ist, statt eine zweite Nachbildung zu brauchen. (Die Nachrechnung in
    /// Python kam auf eine andere Linie; ohne diese Zeile war nicht zu
    /// entscheiden, welche von beiden recht hat.)</summary>
    public string RailEndWorstWhere = "";

    /// <summary>Wieviele Trümmerfelder (Teil 69) gelegt wurden — die Zahl, an
    /// der sich sehen lässt, ob eine Karte überhaupt zerschossenes Gleis hat.
    /// map_NET02 hat keines; <c>4.DM</c> hat 49 Zellen, <c>7.DM</c> fünf.</summary>
    public int RailBrokenDrawn;

    /// <summary>Zellen, auf denen mehr als ein Gleissatz liegt — die
    /// KREUZUNGEN. Sie tragen zwei Bilder, und beide gehören gezeichnet;
    /// solange wir eines davon wegfilterten, fehlte an jeder Kreuzung ein
    /// Strang.</summary>
    public int RailCrossings;

    private void RailMeasureEnd(List<Vector2> cells, Dictionary<int, Entity> bySlot,
                                int slot, bool tail)
    {
        if (cells.Count == 0 || !bySlot.TryGetValue(slot, out var b)) return;
        var cell = cells[tail ? ^1 : 0];
        int col = Mathf.RoundToInt(cell.X), row = Mathf.RoundToInt(cell.Y);
        int dc = Mathf.Max(Mathf.Max(b.Col - col, 0), col - (b.Col + Mathf.Max(1, b.FootW) - 1));
        int dr = Mathf.Max(Mathf.Max(b.Row - row, 0), row - (b.Row + Mathf.Max(1, b.FootH) - 1));
        int d = Mathf.Max(dc, dr);
        RailEndChecked++;
        // 2 Zellen Spielraum: die Anschlusszeile liegt je nach Gebäudeart auf
        // +1 oder +2, und der Stummel steht bei manchen Arten eine Spalte
        // neben dem Grundriss (siehe RailDockRow).
        if (d > 2) RailEndFar++;
        if (d > RailEndWorst)
        {
            RailEndWorst = d;
            RailEndWorstWhere = $"Zelle ({col},{decimal.ToInt32(row)}) gegen Platz {slot} " +
                                $"Typ {b.BType} auf ({b.Col},{b.Row}) {b.FootW}x{b.FootH}";
        }
    }

    /// <summary>Miss die Höhendifferenz an EINEM Linienende und schreib sie in
    /// die Zähler, die <c>--rail-check</c> ausgibt. Siehe
    /// <see cref="RailDeckOffSum"/> für die Rechnung.</summary>
    private void RailMeasureDeck(List<Vector2> cells, Dictionary<int, Entity> bySlot,
                                 int slot, bool tail)
    {
        if (cells.Count == 0 || !bySlot.TryGetValue(slot, out var b)) return;
        if (RailDockDeckPixel(b.BType) == int.MinValue) return;
        var cell = cells[tail ? ^1 : 0];
        int col = Mathf.RoundToInt(cell.X), row = Mathf.RoundToInt(cell.Y);
        // nur ein Ende, das wirklich am Gebaeude steht — dieselbe Schranke wie
        // in RailSnapEnd, sonst zaehlt eine Linie mit, die ganz woanders endet
        if (col < b.Col - 1 || col > b.Col + Mathf.Max(1, b.FootW)) return;

        // Ein Bahnhof hat ZWEI Decks, eine Zellzeile auseinander (sec22: beide
        // kommen etwa gleich oft vor). Gemessen wird gegen das naehere.
        int a = int.MaxValue;
        for (int k = 0; k < RailDockDecks(b.BType); k++)
            a = Mathf.Min(a, Mathf.Abs(Mathf.RoundToInt(
                RailDeckY(col, row) - RailDockDeckY(b) - k * TileH)));
        RailDeckOffCount++;
        RailDeckOffSum += a;
        if (a > RailDeckOffMax) RailDeckOffMax = a;
        if (a == 0) RailDeckFlush++;
        RailDeckByType.TryGetValue(b.BType, out var t);
        RailDeckByType[b.BType] = (t.Flush + (a == 0 ? 1 : 0),
                                   t.Off + (a == 0 ? 0 : 1),
                                   Mathf.Max(t.Worst, a));
    }

    /// <summary>Liegt in dieser Kette jedes Paar Kante an Kante? Genau die
    /// Bedingung, an der die ganze Legeart haengt.</summary>
    private static bool RailChainSound(List<Vector2> cells)
    {
        for (int i = 1; i < cells.Count; i++)
            if (RailPortTo(cells[i - 1], cells[i]) < 0) return false;
        return true;
    }

    /// <summary>Ein Ende. <paramref name="tail"/> = das Listenende. Gibt
    /// zurueck, ob wirklich gerueckt wurde.</summary>
    private bool RailSnapEnd(List<Vector2> cells, List<int> pcs,
                             Dictionary<int, Entity> bySlot, int slot, bool tail)
    {
        if (!bySlot.TryGetValue(slot, out var b)) return false;
        int dock = RailDockRow(b.BType);
        if (dock == int.MinValue) return false;

        int last = tail ? cells.Count - 1 : 0;
        int col0 = Mathf.RoundToInt(cells[last].X);
        // nur ein Ende, das wirklich am Gebaeude steht — die Knotenangabe der
        // Karte trifft in ein paar Faellen ein Gebaeude am anderen Kartenende
        if (col0 < b.Col - 1 || col0 > b.Col + Mathf.Max(1, b.FootW)) return false;

        RailDockChecked++;
        int target = b.Row + dock;
        int r0 = Mathf.RoundToInt(cells[last].Y);
        if (r0 != target) RailDockOff++;
        if (r0 == target || RailProbeSkipDock) return false;
        int d = target - r0;

        // der letzte gerade Lauf: alle Zellen am Ende auf derselben Zeile
        int k = last, step = tail ? -1 : 1;
        while (true)
        {
            int nx = k + step;
            if (nx < 0 || nx >= cells.Count) break;
            if (Mathf.RoundToInt(cells[nx].Y) != r0) break;
            k = nx;
        }
        int from = Mathf.Min(k, last), to = Mathf.Max(k, last);
        for (int i = from; i <= to; i++) cells[i] = new Vector2(cells[i].X, cells[i].Y + d);

        // die Naht: die Zelle vor dem Lauf teilt mit ihm die SPALTE — sie hat
        // eine andere Zeile (sonst gehoerte sie zum Lauf), und Kante an Kante
        // mit anderer Zeile heisst gleiche Spalte. Die Luecke wird senkrecht
        // gefuellt; steht sie danach auf derselben Zelle, faellt sie weg.
        int seam = tail ? from : to;              // erste Zelle des Laufs
        int prev = tail ? from - 1 : to + 1;
        if (prev < 0 || prev >= cells.Count) return true;   // die ganze Linie war der Lauf
        int col = Mathf.RoundToInt(cells[seam].X);
        int rp = Mathf.RoundToInt(cells[prev].Y);
        if (Mathf.RoundToInt(cells[prev].X) == col && rp == target)
        {
            // Doppelzelle: die Vorgaengerin ist jetzt die Zelle selbst
            cells.RemoveAt(prev);
            if (prev < pcs.Count) pcs.RemoveAt(prev);
            return true;
        }
        if (Mathf.RoundToInt(cells[prev].X) != col) return true;  // faengt RailChainSound ab
        int dir = target > rp ? 1 : -1;
        var bridge = new List<Vector2>();
        for (int r = rp + dir; r != target; r += dir) bridge.Add(new Vector2(col, r));
        if (bridge.Count == 0) return true;
        if (!tail) bridge.Reverse();
        int at = tail ? from : to + 1;
        int piece = pcs.Count > seam ? pcs[seam] : 0;
        for (int i = bridge.Count - 1; i >= 0; i--)
        {
            cells.Insert(at, bridge[i]);
            if (at <= pcs.Count) pcs.Insert(at, piece);
        }
        return true;
    }

    /// <summary>Wie oft ein Stück eine STÜTZE bekommt. Teil 65 trägt Träger und
    /// Bock in einem Bild, also stünde bei jedem Schritt ein Bock — auf gerader
    /// Strecke alle 40 px. Gemeldet als »du nutzt viele stützen, vielleicht zu
    /// viele«, und das Foto des Originals zeigt sie deutlich weiter
    /// auseinander.
    ///
    /// <para>⚠ UNSERE SETZUNG. Was den Abstand im Original bestimmt, ist nicht
    /// gelesen — dort steckt er vermutlich im Streckencode der Zelle
    /// (Satz 0xb95f50 +0x03), den wir noch nicht auflösen. Bis dahin: jeder
    /// dritte Schritt bekommt den Bock, die anderen den blanken Träger 64.
    /// Beide Teile haben dieselbe Leinwandlage, die Schiene läuft also
    /// durch.</para></summary>
    private const int RailPylonEvery = 3;

    /// <summary>Wie weit ein Gleisstück unter dem Waggonanker liegt, je Stück.
    ///
    /// <para>Beide Bilder kommen aus derselben Leinwand, sitzen darin aber
    /// verschieden: bei Stück 6 belegt die Schiene die Zeilen 14..33, der
    /// Waggonkörper dagegen 33..54 — mit demselben Anker gezeichnet hängt der
    /// Wagen also UNTER dem Träger statt darauf zu stehen. Bei einer Hochbahn
    /// ist das ungefähr eine Bockhöhe, und es ist der Grund, warum der Zug an
    /// Gebäuden davor statt hinein zu fahren schien: die Route endet
    /// nachweislich in der Gebäudemitte (über alle 1218 Enden gemessen ist der
    /// Versatz dy = +2 = foot_h/2), nur lag das Gleis zu hoch.</para>
    ///
    /// <para>Gemessen je Stück als Differenz der UNTERKANTEN von Waggonbild und
    /// zugehörigem Gleisbild. Sie ist nicht konstant, weil jede Richtung ihre
    /// eigene Geometrie hat: 16 für die vier flachen Lagen, 21 für die
    /// waagerechten, 26 für die beiden steilen.</para></summary>
    private static readonly int[] RailYOffsetOf = { 16, 16, 21, 26, 16, 26, 21, 16 };

    /// <summary>
    /// Wie weit die STRECKE unter dem Waggonanker liegt — <b>eine</b> Zahl fuer
    /// alle acht Richtungen, und das ist keine Vereinfachung, sondern die
    /// Auskunft der Bilder selbst.
    ///
    /// <para>Gemessen an Teil 64 (blanker Traeger, 64×56): fuer jedes Bild die
    /// Hoehe der Schiene am LINKEN und am RECHTEN Rand ihres Bildes,
    /// Schienenmitte in Leinwandzeilen —</para>
    /// <code>
    ///   f0  x 10..49   links 31   rechts 31      waagerecht
    ///   f1  x 27..32   oben  22   unten  41      senkrecht
    ///   f2  x 27..49   links 40   rechts 31      halbes Stueck
    ///   f3  x 27..49   links 22   rechts 31      halbes Stueck
    ///   f4  x 10..32   links 31   rechts 40      halbes Stueck
    ///   f5  x 10..32   links 31   rechts 22      halbes Stueck
    ///   f6  x 10..49   links 16   rechts 31      ganze Diagonale
    ///   f7  x 10..49   links 31   rechts 16      ganze Diagonale
    /// </code>
    /// <para><b>Jedes Bild hat ein Ende auf Zeile 31</b>, und das senkrechte
    /// liegt mit 22..41 mittig darum. Zeile 31 ist also die Bauhoehe des
    /// ganzen Satzes: die Stuecke sind so gezeichnet, dass sie sich dort
    /// treffen. Ein Versatz JE STUECK kann sie darum nur auseinanderziehen —
    /// die alte Tabelle <see cref="RailYOffsetOf"/> hat genau das getan und
    /// jede Richtungsaenderung um 5 bis 10 px aufgerissen.</para>
    ///
    /// <para>Der Wert 21 ist der, den das waagerechte Stueck schon hatte: mit
    /// ihm steht der Waggon (Teil 58, Unterkante Leinwandzeile 54) auf der
    /// Schiene. Er bleibt damit an der Stelle, an der er im Bild vom 11.08.
    /// richtig sass, und alle anderen Richtungen ruecken auf dieselbe
    /// Hoehe.</para>
    ///
    /// <para>⚠ 12.08.2026 auf <b>24</b> gerueckt, und zwar nicht nach Gefuehl:
    /// mit 24 liegt die Schiene (Leinwandzeile 31, Anker
    /// <see cref="ComposedAnchor"/> = (30,55)) genau auf dem MITTELPUNKT ihrer
    /// Zelle — 55 − 31 = 24. Das ist die Hoehe, auf der auch der Schienenanbau
    /// der Gebaeude sitzt: am Bahnhof (Muster 86, Kachelsatz 47) laeuft der
    /// Gitterträger auf y ≈ 30 unter der Oberkante von Musterzelle
    /// (0,0) — also 10 px in Zellzeile 1 hinein, deren Mitte. Erst mit dieser
    /// Zahl steht das Gleis auf demselben Gitter wie die Zellen, aus denen es
    /// gebaut ist.</para>
    ///
    /// <para><b>13.08.2026 auf 23 nachgemessen — ein Pixel, und diesmal
    /// gezaehlt statt hergeleitet.</b> Teil 64/f0 traegt seine Schiene auf den
    /// Leinwandzeilen 29..33, und diese fuenf Zeilen haben eine
    /// unverwechselbare Farbfolge: 111,91,63 oben, dann 191/163,151,151, dann
    /// zweimal 115,67,67, unten 63,55,43. <b>Genau diese Folge steht auch im
    /// Gebaeudemuster</b> — sie wurde im zusammengesetzten Muster (Kachelsatz
    /// 47, MapBakers eigene Rechnung <c>y = zeile·20 − 50 + Kachel.YOff</c>)
    /// gesucht und gefunden: Bahnstation (86) in Musterspalte 0 auf
    /// y−Ankerzeile = <b>−13 und +7</b>, Feldbahnhof (156) in Musterspalte 0
    /// und 2 auf <b>−13 und +7</b>. Beide Bahnhoefe tragen also ein
    /// ZWEIGLEISIGES Deck, die zwei Gleise genau eine Zellzeile auseinander.
    /// Mit Versatz 24 landet unsere Oberkante auf <c>zeile·20 + 8</c>, also
    /// einen Pixel zu tief; mit 23 auf <c>zeile·20 + 7</c> und damit
    /// pixelgenau auf dem Deck des Gebaeudes. Im laufenden Bild
    /// (map_NET02, Bahnstation 191,55, Zoom 4) war derselbe Pixel zu sehen:
    /// die Deckfarbe des Gebaeudes lag auf Schirmzeile 338..340, unsere auf
    /// 341..343 — vier Schirmzeilen sind bei Zoom 4 genau ein Kartenpixel.</para>
    /// </summary>
    /// <summary>
    /// ⚠ <b>15.08.2026 von 23 auf −17 — das Gleis lag ZWEI ZELLZEILEN zu tief.</b>
    ///
    /// <para>Gelesen sind jetzt BEIDE Einreiher des Originals, und sie stehen in
    /// beiden Fassungen wörtlich gleich:</para>
    /// <code>
    ///   Kachel   y = zeile·20 − höhe·15 − 50 + yoff   (sub ebp,0x32  @0x4B42DF)
    ///   Gleis    y = zeile·20 − höhe·15 − 62 + yoff   (add ax,0x3e   @0x42E015)
    ///   Gleis    x = spalte·40 − 6                    (sub ax,6      @0x42DFEC)
    /// </code>
    /// <para>Unser <see cref="Import.MapBaker.BlitAnchor"/> ist dieselbe −50, die
    /// Umrechnung steht also. <b>Die Probe darauf ist das x</b>: unser
    /// <c>spalte·40 + 20 − 30 + CanvasXPad(4) = spalte·40 − 6</c> trifft die des
    /// Originals auf den Pixel. Nur y war um <b>40 = 2·TileH</b> daneben.</para>
    ///
    /// <para><b>Die Gegenprobe braucht kein Gebäude:</b> Teil 65 (Träger mit
    /// Bock) ist 83 Zeilen hoch, sein Fuß liegt auf Leinwandzeile 82. Mit 23
    /// landet er auf <c>−55+23+82 = +50</c>, also 40 px UNTER seiner eigenen
    /// Zelle — in der Luft. Mit −17 auf <c>−55−17+82 = +10</c>, genau der
    /// Unterkante der Zelle. Ein Bock, der auf dem Boden steht, ist der Beleg.</para>
    ///
    /// <para>⚠ <b>Und damit ist meine eigene Messung vom Vormittag widerlegt.</b>
    /// <c>aekernel-tools/rail_deck_overlay.py</c> setzte die Gebäudekachel
    /// richtig, das Gleis aber mit unserer eigenen 23 — es hielt also wieder
    /// unsere Ableitung gegen sich selbst, zum dritten Mal an einem Tag. Die
    /// „Oberkante des Gitterträgers", die es meldete, gehört zur Anschlusszeile
    /// +3.</para>
    ///
    /// <para>⚠ Die Zahl <c>--rail-check „Anschlusszeile: n von m"</c> ändert sich
    /// dadurch NICHT — <see cref="RailDeckPixel"/> kürzt sich weiter aus der
    /// Differenz heraus. <b>Die Probe ist das Bild.</b></para></summary>
    private const int RailDeckOffset = -17;

    /// <summary>
    /// Wohin der Waggon gegen sein eigenes Gleisbild rückt — <b>nirgendwohin</b>.
    ///
    /// <para>⚠ 15.08.2026 ZURÜCKGENOMMEN. Hier stand <c>(6, −5)</c>, hergeleitet
    /// aus der gelesenen Beziehung <c>Waggon − Gleis = (+6, −28)</c> der beiden
    /// Einreiher (@0x42DF40 / @0x42E100). Die Zahl ist richtig GELESEN, gilt
    /// aber im Bezugsrahmen des Originals, wo das Gleis bei <c>G−82</c> sitzt —
    /// bei uns sitzt es bei <c>Anker+23</c>. Ein Delta aus dem einen Rahmen im
    /// anderen anzuwenden ist derselbe Fehler, vor dem der 50-px-Punkt am
    /// 13.08. ausdrücklich gewarnt hat; ich bin bei der kleinen Zahl trotzdem
    /// hineingelaufen.</para>
    ///
    /// <para><b>Nachgemessen an den Bildern selbst:</b> Gleis 64/f0 trägt seine
    /// Schiene auf den Leinwandzeilen 29..33, der Waggon 58/f0 reicht bis
    /// Zeile 52. Mit der Ablage <c>+23</c> für das Gleis liegt die
    /// Schienenoberkante auf 52 — <b>genau dort, wo die Unterkante des Waggons
    /// endet</b>. Ohne Versatz sitzt der Zug also auf der Schiene; mit
    /// <c>(6,−5)</c> schwebte er 5 px darüber, und genau das war im Bild zu
    /// sehen.</para>
    ///
    /// <para>⚠ 15.08.2026, ZWEITER ANLAUF AN DERSELBEN ZAHL. Als
    /// <see cref="RailDeckOffset"/> von 23 auf −17 ging, habe ich den Versatz
    /// mit <c>−28 + RailDeckOffset</c> nachgezogen — und die <b>28 ist genau
    /// das Delta aus dem Rahmen des Originals</b>, vor dessen Übertragung der
    /// Absatz darüber warnt. Gemeldet als »es wirkt noch so, als würde die Bahn
    /// über den Gleisen fahren, anstatt darauf«, und im übereinandergelegten
    /// Bild eindeutig: unter den Waggons klaffte ein Spalt.</para>
    ///
    /// <para>Richtig ist die gemessene Zahl, und sie ist ein Verhältnis zwischen
    /// den beiden LEINWÄNDEN, kein Absolutwert: der Waggon liegt
    /// <b>23 px über der Gleisleinwand</b>, ganz gleich wo die steckt. Vier
    /// Varianten wurden nebeneinandergelegt und angesehen:</para>
    /// <code>
    ///   −28  Spalt zwischen Waggon und Traeger   (das war der Fehler)
    ///   −25  Unterkante beruehrt die Oberkante
    ///   −23  Unterkante in der Mitte des Traegers, Traeger bleibt sichtbar  ✔
    ///   −21  Raeder versinken, der Traeger verschwindet fast
    /// </code>
    ///
    /// <para>⚠ Offen bleibt das x: der Waggonkörper füllt die Spalten 13..35
    /// (Mitte 24), das Gleisbild 10..49 (Mitte 29,5). Der Waggon steht also
    /// 5,5 px links der Schienenmitte, und die 6 hier hebt das auf.</para></summary>
    private static readonly Vector2 WagonOverRail = new(6, -23 + RailDeckOffset);

    /// <summary>Wo die Schienenoberkante eines Gleisbildes INNERHALB ihrer
    /// Zelle liegt: <c>TileH/2 − ComposedAnchor.y + RailDeckOffset + 29</c>.
    /// Eine Zahl, damit der Pruefstand dieselbe rechnet wie der Zeichner.</summary>
    private const int RailDeckPixel = TileH / 2 - 55 + RailDeckOffset + 29;

    /// <summary>Abstand zweier Stuetzen, in Bildschirmpixeln gemessen statt in
    /// Stuecken gezaehlt. ⚠ UNSERE SETZUNG bleibt die Zahl; was den Abstand im
    /// Original bestimmt, ist weiter nicht gelesen. Drei Zellen sind 120 px,
    /// also derselbe Abstand, den <see cref="RailPylonEvery"/> auf gerader
    /// Strecke ergab — nur zaehlt er jetzt auch auf einer senkrechten Strecke
    /// richtig, wo die Spalte stehenbleibt.</summary>
    private const float RailPylonEveryPx = 120f;

    /// <summary>Ein Gleisbild, <paramref name="frame"/> ist der BILDINDEX
    /// (0..5, siehe <see cref="RailFrameOfPorts"/>), nicht mehr das Stück.</summary>
    private Texture2D? GetRailTexture(int frame, int part)
    {
        // ⚠ 13.08.2026 — hier stand `frame & 7`, und damit fielen die vier
        // RAMPEN (Bild 6..9) auf 6,7,0,1 zurueck: die beiden senkrechten Rampen
        // wurden als waagerechtes und senkrechtes Flachstueck gezeichnet.
        // ⚠ 14.08.2026 — Teil 69 (das zerschossene Gleis) fuehrt ZWANZIG echte
        // Bilder: zehn Formen mal zwei Zufallsvarianten. Bei 64..68 sind die
        // Bilder 10..19 dagegen einfarbige SCHATTENMASKEN und werden nie
        // gezeichnet (belegt: Bild 3930 hat genau EINEN Palettenindex, Bild
        // 4030 dagegen 15). Die Schranke haengt darum am Teil.
        int max = part == 69 ? 19 : 9;
        int f = frame >= 0 && frame <= max ? frame : 0;
        return LoadRailTex(part is >= 64 and <= 69 ? part : 64, f);
    }

    /// <summary>
    /// <b>DIE SCHATTENMASKE eines Gleisstücks</b> — Bild <c>form + 10</c>.
    ///
    /// <para>Sie liegt seit dem ersten Import mit auf der Platte und wurde nie
    /// gezeichnet. Gemeldet als »du hattest ja noch haufen Grafiken gefunden,
    /// die wir noch garnicht nutzen«.</para>
    ///
    /// <para>Nachgesehen: die Bilder 10..19 der Teile 64 bis 68 sind
    /// Silhouetten des Trägers und der Böcke, um <b>(+19,+22) px</b> nach unten
    /// rechts versetzt — dorthin, wo bei Licht von oben links der Schatten
    /// fällt. Ihre Leinwand ist gleich hoch wie die des Farbbildes (56 bzw. 83)
    /// und teilt dessen Ursprung; nur die Breite ist gelegentlich 69 statt 64,
    /// weil der Schatten weiter nach rechts reicht. Sie wird deshalb an
    /// derselben Stelle gezeichnet.</para>
    ///
    /// <para>⚠ Mein erster Prüftest (»einfarbig?«) hat die Masken der Teile 65
    /// bis 68 NICHT erkannt, weil sie ein paar Streupixel tragen. Nur das Bild
    /// hat sie gezeigt.</para></summary>
    private Texture2D? GetRailShadow(int frame, int part)
    {
        if (part is < 64 or > 68) return null;      // 69: Trümmermasken ungeklärt
        return LoadRailTex(part, Mathf.Abs(frame) % 10 + 10);
    }

    private Texture2D? LoadRailTex(int p, int f)
    {
        int k = f + p * 32;
        if (_railTex.TryGetValue(k, out var t)) return t;
        string path = Core.Content.Path($"Units/train/rail{p}/f{f}.png");
        t = ResourceLoader.Exists(path) ? ResourceLoader.Load<Texture2D>(path) : null;
        if (t == null && FileAccess.FileExists(path))
        {
            var img = Image.LoadFromFile(path);
            if (img != null) t = ImageTexture.CreateFromImage(img);
        }
        _railTex[k] = t;
        return t;
    }

    /// <summary>
    /// <b>Wie dunkel ein Schatten ist — gemessen, nicht gewählt.</b>
    ///
    /// <para>Das Original zeichnet Schatten nicht als schwarzes Sprite. Sein
    /// zweiter Blitter @0x4AC6D0 kopiert keine Bildpunkte, sondern schickt den
    /// Palettenindex des Punktes, der schon dasteht, durch eine 256-Byte-Tabelle
    /// <c>NN.CWS</c> (geladen nach 0xB135B0, <c>fread</c> @0x4b57e5). Die Maske
    /// ist nur die Schablone.</para>
    ///
    /// <para><b>Eins zu eins können wir das nicht:</b> unser Gelände ist als RGB
    /// gebacken und hat keine Palettenindizes mehr. Gemessen wurde deshalb, wie
    /// gut ein multiplikativer Faktor die Tabelle ersetzt — und zwar nur über
    /// die Farben, die im Gelände wirklich vorkommen (155 Farben, 43 260 Punkte
    /// Stichprobe; die UI-Farben am oberen Ende der Palette würden den Wert
    /// sonst verziehen). <c>aekernel-tools/re_gfx_shadow_fit.py</c>:</para>
    /// <code>
    ///   bester Faktor je Kanal   R 0,775   G 0,831   B 0,820
    ///   Restfehler (RMS)         7,8 von 255 = 3,1 %
    ///   ein einziger Faktor      0,809
    /// </code>
    /// <para>Ein CanvasItem kann in EINEM Durchgang nur einen Faktor für alle
    /// drei Kanäle anwenden (Mischblende: <c>ziel·(1−a) + farbe·a</c>, und für
    /// verschiedene Kanäle bräuchte man verschiedene <c>a</c>). Genommen wird
    /// deshalb der einzelne Faktor 0,809, also <b>Schwarz mit 19 % Deckung</b>.
    /// Der Unterschied zur kanalweisen Fassung liegt unter dem Restfehler, den
    /// die Näherung ohnehin hat.</para>
    ///
    /// <para>⚠ <b>Was dafür nötig wäre:</b> ein palettenindiziertes Gelände und
    /// ein Shader, der die echte Tabelle nachschlägt. Dann wäre es exakt. Das
    /// ist notiert, nicht getan.</para></summary>
    /// <summary>Wie schnell der Rotor eines Hubschraubers durch seine acht
    /// Phasen läuft, in Phasen je Sekunde. <b>⚠ UNSERE SETZUNG</b> — im
    /// Original ist die Drehzahl nicht gelesen. 24 ist gewählt, weil bei acht
    /// Phasen drei Umdrehungen je Sekunde als Wischen lesen, nicht als
    /// Einzelbilder.</summary>
    private const float RotorFps = 24f;

    private static Color ShadowTint => new(0f, 0f, 0f, ShadowAlpha);

    /// <summary>Die Deckung des Schattens, umstellbar über
    /// <c>--rail-shadow=&lt;wert&gt;</c>. Gegenprobe: mit <c>0</c> verschwindet er,
    /// und der Prüfstand muss dann eine andere Helligkeit messen — ein Zähler,
    /// der nicht scheitern kann, belegt nichts. Mit einem hohen Wert wird
    /// sichtbar, WO er liegt; genau so ist geprüft worden, dass die Masken
    /// stimmen (Band auf dem Boden unter dem Träger, Schrägen von den
    /// Bockfüssen, der Kurve folgend).</summary>
    private static float ShadowAlpha
    {
        get
        {
            if (_railShadowAlpha.HasValue) return _railShadowAlpha.Value;
            float a = 0.19f;
            foreach (string x in OS.GetCmdlineUserArgs())
                if (x.StartsWith("--shadow="))
                    a = Mathf.Clamp(x["--shadow=".Length..].ToFloat(), 0f, 1f);
                else if (x.StartsWith("--rail-shadow="))
                    a = Mathf.Clamp(x["--rail-shadow=".Length..].ToFloat(), 0f, 1f);
            _railShadowAlpha = a;
            return a;
        }
    }

    private static float? _railShadowAlpha;

    /// <summary>Wieviele Schattenmasken der letzte Durchgang gelegt hat — sonst
    /// wäre »die Schatten sind da« wieder nur eine Behauptung.</summary>
    public int RailShadowsDrawn;

    /// <summary>Die STRECKE — jeder Schritt jeder Linie, unabhaengig davon, ob
    /// gerade ein Zug darauf faehrt.
    ///
    /// <para>⚠ 11.08.2026. Bis heute zeichneten wir gar keine: sichtbar war nur
    /// der Zug, weil das Bild eines Waggons sein eigenes Schienenstueck
    /// mitbringt. Gemeldet wurde das mehrfach als »keine Strecke sichtbar«.
    /// Die blanke Schiene ist Teil 64 in ROBO.CWR, acht Richtungen; gefunden,
    /// indem die belegten Teile 50..65 ausgegeben und angesehen wurden.</para>
    ///
    /// <para>Das Stueck je Schritt ist dasselbe, das ein Waggon dort zeigen
    /// wuerde (<c>_linePiece</c>) — damit liegen Gleis und Zug zwangslaeufig
    /// aufeinander statt nebeneinander.</para></summary>
    /// <summary>Wie viele Gleisstuecke der letzte Durchgang gelegt hat — der
    /// Pruefstand liest es, sonst waere „die Strecke ist da" wieder nur eine
    /// Behauptung.</summary>
    public int RailTilesDrawn;

    /// <summary>Wie viele NACHBARN unter den gelegten Stuecken nicht Kante an
    /// Kante liegen — die Zahl, an der »sauber zusammengebaut« zu messen ist.
    /// Muss 0 sein; <c>--rail-lay=cols</c> treibt sie in die Hunderte.</summary>
    public int RailTilesLoose;

    /// <summary>Nur fuer den Prueflauf: die ALTE Legeart (ein Stueck je
    /// Routenschritt an RailPoint, Bild aus <see cref="RailFrameOf"/>, Hoehe
    /// aus RailYOffsetOf) gegen die neue vergleichen. Der Name bleibt, damit
    /// die Fahne <c>--rail-lay=cols</c> weiter das tut, was sie soll: den
    /// Fehler zeigen.</summary>
    public static bool RailProbeSkipCols;

    /// <summary>Ein fertig gelegtes Gleisstück: wohin, welches Bild, wie tief
    /// unter dem Waggonanker — und in welcher ZEILE es liegt, denn danach
    /// entscheidet sich, ob ein Gebäude davor oder dahinter gehört.</summary>
    /// <summary>
    /// <b>Um wieviele Zellzeilen SPÄTER als seine eigene Zelle wird ein
    /// Gleisstück gezeichnet? — <c>+2</c>, und das ist GELESEN.</b>
    ///
    /// <para>⭐ 13.08.2026. Gemeldet war »oft fehlt die letzte Strecke zur
    /// Anbindung an ein Gebäude«. Der Grund ist die Reihenfolge, nicht ein
    /// fehlendes Stück: <see cref="DrawRailUpTo"/> zeichnet alle Stücke einer
    /// Zeile VOR den Gebäuden, deren Grundriss bis in diese Zeile reicht — und
    /// die letzte Gleiszelle einer Linie liegt gemessen auf <b>Zeile +1 oder
    /// +2</b> der Gebäudeecke, also MITTEN im vier Zeilen tiefen Grundriss.
    /// Damit wurde ausgerechnet das Anschlussstück jedes Mal zuerst gezeichnet
    /// und danach vom Gebäudemuster überdeckt.</para>
    ///
    /// <para><b>Wie das Original einsortiert</b> (Zeichenlisten-Aufbau
    /// @0x42DF40, der einzige Erzeuger von Gleiseinträgen — er läuft über die
    /// 3000 Gleisplätze ab 0xC2C220 mit Schrittweite 5, <c>+0x00</c> Spalte,
    /// <c>+0x01</c> Zeile, <c>+0x02</c> Bild, <c>0xFF</c> beendet):</para>
    /// <code>
    ///   0x42DFCE  sub bx, [0x5387B0]      ; bx = zeile - kamerazeile
    ///   0x42DFE9  lea ecx, [ebx + 2]      ; ZEILENFACH = bx + 2   <-- die 2
    ///   0x42DFF0  mov [esp+0x19], cl
    ///   0x42E01E  mov al, [esp+0x11]      ; dasselbe Byte, nach add esp,8
    ///   0x42E039  ax = [fach*stride + 0xAB93F0]   ; Zähler des Fachs
    ///   0x42E068  byte [eintrag + 0xAB8068] = 0x14 ; Art 20 = Gleis
    ///   0x42E06F  word [eintrag + 0xAB806A] = platz
    /// </code>
    /// <para>Die Zeichenliste ist also nach SCHIRMZEILE gefächert (höchstens 70
    /// Fächer, je bis 499 Einträge), und ein Gleisstück landet im Fach
    /// <c>zeile + 2</c> — nicht in seinem eigenen. Genau diese 2 fehlte uns.</para>
    ///
    /// <para><b>Warum die 2 und nicht 1 oder 3:</b> sie steht als <c>lea</c>
    /// unmittelbar an der Fachnummer und an keiner anderen Stelle des Aufbaus;
    /// der Schirm-y desselben Eintrags wird getrennt gerechnet (<c>imul bx,
    /// bx, 0x14</c> danach), die 2 wirkt also NUR auf die Reihenfolge und nicht
    /// auf die Lage. Das ist der Grund, warum die Höhe schon vorher stimmte.</para>
    ///
    /// <para>⚠ Was daran NICHT gelesen ist: in welches Fach das Original ein
    /// GEBÄUDE legt. Gebäude sind dort keine Einträge dieser Liste, sondern
    /// werden im Kachel-/Zeilendurchgang @0x42C8C0 gestempelt; die Fachnummer
    /// eines Gebäudes ist damit nicht dieselbe Größe. Belegt ist nur die
    /// Verschiebung des GLEISES gegen seine eigene Zelle, und die reicht für
    /// diese Änderung: sie verschiebt Gleis gegen Gebäude um zwei Zeilen in die
    /// Richtung, in der das Anschlussstück sichtbar wird.</para>
    ///
    /// <para>Gegenprobe: <c>--rail-lay=bucket0</c> setzt die Verschiebung auf 0
    /// zurück, dann verschwindet das Anschlussstück wieder.</para></summary>
    private const int RailDrawRowBias = 2;

    private readonly struct RailTile
    {
        public readonly Vector2 At;
        public readonly int Frame, YOff, Row;

        /// <summary>Die Zeile, nach der SORTIERT und gegen die Gebäude
        /// abgewogen wird: <c>Row + RailDrawRowBias</c>. <see cref="Row"/>
        /// bleibt die Zeile der ZELLE — sie wird an anderer Stelle noch als
        /// solche gebraucht, und zwei Bedeutungen in einem Feld waren hier schon
        /// einmal teuer.</summary>
        public readonly int DrawRow;

        /// <summary>Welcher TEIL: 64 der blanke Traeger, 65..68 die vier
        /// Stuetzenfassungen (siehe <see cref="RailPylonKind"/>).</summary>
        public readonly int Part;
        public RailTile(Vector2 at, int frame, int yoff, int row, int part)
        {
            At = at; Frame = frame; YOff = yoff; Row = row; Part = part;
            DrawRow = row + (RailProbeBucket0 ? 0 : RailDrawRowBias);
        }
    }

    /// <summary>
    /// Gegenprobe zur Zeichenreihenfolge (<c>--rail-lay=bucket0</c>): mit
    /// <c>true</c> wird ein Gleisstück wieder in der Zeile seiner eigenen Zelle
    /// gezeichnet statt zwei später — siehe <see cref="RailDrawRowBias"/>.
    ///
    /// <para>⚠ Der Schalter ist der Beleg, dass die Änderung überhaupt etwas
    /// tut: er MUSS das Anschlussstück wieder verschwinden lassen, und der
    /// Zähler <see cref="RailTilesUnderBuilding"/> muss dabei steigen. Ohne ihn
    /// wäre »sieht jetzt richtig aus« nicht von »war vorher schon so« zu
    /// unterscheiden.</para></summary>
    public static bool RailProbeBucket0
    {
        get
        {
            if (_probeBucket0.HasValue) return _probeBucket0.Value;
            bool hit = false;
            foreach (string a in OS.GetCmdlineUserArgs())
                if (a.StartsWith("--rail-lay=") && a["--rail-lay=".Length..].Contains("bucket0"))
                    hit = true;
            _probeBucket0 = hit;
            return hit;
        }
    }

    private static bool? _probeBucket0;

    private List<RailTile>? _railTiles;

    /// <summary>Die ganze Strecke als eine nach ZEILE sortierte Liste. Einmal
    /// gebaut und behalten: Zellen, Bilder und Geländehöhe stehen für die
    /// Karte fest, es ändert sich nichts von Bild zu Bild.</summary>
    private List<RailTile> RailTiles()
    {
        if (_railTiles != null) return _railTiles;
        var tiles = new List<RailTile>();
        RailTilesLoose = 0;
        RailBrokenDrawn = 0;
        RailCrossings = 0;
        if (RailProbeSkipCols) RailTilesOld(tiles);
        else if (_railCells.Count > 0)
        {
            // ⚠ 13.08.2026 — DIE KARTE legt die Strecke. Bild UND Stuetze stehen
            // im Satz: Bild = frame % 10 (0..5 Kanten, 6..9 Rampen), Stuetze =
            // Platz % 6 == 0 (@0x42D4B1). Beides gelesen, nichts erschlossen.
            var seen = new HashSet<(int, int)>();
            foreach (var c in _railCells)
            {
                // ⚠ 15.08.2026 — DIE DOPPELFILTERUNG IST GANZ WEG, und das war
                // der gemeldete Fehler »es fehlen Gleisstuecke« und »Ecken und
                // Knicke schliessen nicht«.
                //
                // Wo zwei Linien dieselbe Zelle benutzen, traegt die Karte dort
                // ZWEI Saetze mit VERSCHIEDENEM Bild -- die beiden Richtungen
                // der Kreuzung. Wir haben davon nur das erste gezeichnet, also
                // fehlte an jeder solchen Stelle der zweite Strang. Gezaehlt
                // ueber alle 30 Karten: 176 Zellen, auf map_NET02 allein 24,
                // und in JEDEM dieser Faelle unterscheiden sich die Bilder.
                //
                // Das Original filtert gar nicht: die Zeichenschleife @0x42DF40
                // laeuft ueber alle 3000 Plaetze und stellt jeden ein. Zwei
                // gleiche Traeger uebereinander sind unsichtbar, zwei
                // verschiedene ergeben die Kreuzung.
                RailCrossings += seen.Add((c.Col, c.Row)) ? 0 : 1;
                var at = RailPoint(new Vector2(c.Col, c.Row));
                if (c.Broken)
                {
                    // ⚠ 14.08.2026 — hier stand `continue`: ein zerschossenes
                    // Stueck liessen wir schlicht weg, und das war UNSERE WAHL.
                    // Die LUECKE im Deck ist richtig -- der Farb-Durchgang des
                    // Originals steigt bei `bild >= 100` aus, BEVOR er Teil 64
                    // oder 65 waehlt (@0x42B743, F: @0x42A930), es gibt dort
                    // also weder Traeger noch Bock. Was fehlte, ist der SCHUTT
                    // darunter: `partBase(64) + bild` mit bild in [100,119]
                    // sind die Bilder 4020..4039, und die Teiltabelle @0x77C870
                    // gibt 69 -> 4020. Also Teil 69, Bild `form + 10*variante`.
                    //
                    // Angesehen: verstreute Splitter, rostrot mit hellgrauen
                    // Bruchstellen, 6..13 % Deckung -- kein Gleisrest, keine
                    // Stuempfe. Sie liegen auf dem BODEN (yoff ~63 gegen 29 des
                    // Traegers); dieser Versatz steckt schon in der Leinwand
                    // des exportierten Bildes, deshalb dieselbe Ablage.
                    tiles.Add(new RailTile(at, c.Base + 10 * c.BrokenVariant,
                                           RailDeckOffset, c.Row, 69));
                    RailBrokenDrawn++;
                    continue;
                }
                tiles.Add(new RailTile(at, c.Base, RailDeckOffset, c.Row,
                                       c.Pylon ? 65 + c.PylonKind : 64));
            }
            // Die Zahl »nicht Kante an Kante« bleibt der Pruefstand der KETTEN,
            // nicht der gezeichneten Stuecke: eine Karte legt ihre Zellen
            // linienweise, und zwei Linien duerfen sich beruehren oder nicht.
            foreach (var kv in _lineCell)
                for (int i = 1; i < kv.Value.Count; i++)
                    if (RailPortTo(kv.Value[i - 1], kv.Value[i]) < 0) RailTilesLoose++;
        }
        else
            foreach (var kv in _lineCell)
            {
                if (!_lineCellFrame.TryGetValue(kv.Key, out var frames)) continue;
                var cells = kv.Value;
                var lastPylon = Vector2.Zero;
                bool hasPylon = false;
                for (int i = 0; i < cells.Count && i < frames.Count; i++)
                {
                    if (i > 0 && RailPortTo(cells[i - 1], cells[i]) < 0) RailTilesLoose++;
                    var at = RailPoint(cells[i]);
                    // Die STUETZE nach dem Abstand auf dem Schirm, nicht nach der
                    // Zahl der Stuecke: sonst stehen auf einem senkrechten Stueck
                    // sechs Boecke uebereinander und auf der Geraden einer alle
                    // drei Zellen. ⚠ UNSERE SETZUNG bleibt der Abstand selbst.
                    bool pylon = !hasPylon || (at - lastPylon).Length() >= RailPylonEveryPx;
                    if (pylon) { lastPylon = at; hasPylon = true; }
                    tiles.Add(new RailTile(at, frames[i], RailDeckOffset,
                                           Mathf.RoundToInt(cells[i].Y), pylon ? 65 : 64));
                }
            }
        // Sortiert wird nach DrawRow, nicht nach Row — siehe RailDrawRowBias.
        tiles.Sort((a, b) => a.DrawRow != b.DrawRow
                                 ? a.DrawRow - b.DrawRow
                                 : a.At.X.CompareTo(b.At.X));
        _railTiles = tiles;
        RailCountTilesUnderBuildings(tiles);
        return tiles;
    }

    /// <summary>
    /// <b>Wieviele Gleisstücke werden vor einem Gebäude gezeichnet, dessen
    /// Muster ihre eigene Zelle bedeckt?</b> — die Zahl zu
    /// <see cref="RailDrawRowBias"/>.
    ///
    /// <para>Das ist die Fehlerklasse, die »die letzte Strecke fehlt« erzeugt,
    /// und keine bisherige Zahl konnte sie sehen: <c>RailEndFar</c> misst
    /// Abstände in ZELLEN mit zwei Zellen Spielraum, <c>RailDeckOffSum</c> misst
    /// Höhen und steigt bei Gebäudearten ohne gemessene Anschlusszeile wortlos
    /// aus (also bei Fabrik, Mine, Flughafen — 255 der 742 Enden). Verdeckung
    /// ist aber keine Frage des Abstands, sondern der REIHENFOLGE.</para>
    ///
    /// <para>Gezählt wird, was <see cref="DrawRailUpTo"/> tatsächlich tut: ein
    /// Stück ist verdeckungsgefährdet, wenn seine <c>DrawRow</c> kleiner oder
    /// gleich der vordersten Grundrisszeile seines Endgebäudes ist.</para>
    ///
    /// <para>⚠ <b>ERSTER ANLAUF ZÄHLTE DAS FALSCHE.</b> Er nahm JEDES Gleisstück,
    /// dessen Zelle auf der Musterfläche eines Gebäudes liegt — und meldete auf
    /// map_NET02 »83 von 86 vor ihrem Gebäude«, mit der Verschiebung 2 genauso wie
    /// ohne. Die Musterfläche ist zehn Spalten breit und sechs Zeilen hoch; die
    /// 86 waren fast alle Stücke, die weit ÜBER dem Gebäudekörper vorbeilaufen
    /// und dort nie verdeckt werden. Die Zahl bewegte sich nicht, weil sie nicht
    /// am Gegenstand der Meldung gemessen hat.</para>
    ///
    /// <para>Gezählt werden jetzt die <b>LINIENENDEN</b> — das letzte Stück einer
    /// Kette gegen das Gebäude, an dem diese Kette endet. Das ist genau, was der
    /// Spieler beschreibt (»die letzte Strecke zur Anbindung«), und die Zahl
    /// bewegt sich mit der Verschiebung.</para>
    ///
    /// <para>⚠ »Verdeckt« ist nicht »unsichtbar«: ob an der Stelle im Muster
    /// wirklich ein Bildpunkt steht, sagt erst das Bild. Die Zahl ist die
    /// obere Schranke und der Hebel, an dem sich die Änderung messen lässt —
    /// mit <c>--rail-lay=bucket0</c> muss sie steigen.</para></summary>
    public int RailTilesUnderBuilding, RailTilesUnderChecked;
    public string RailUnderWorstWhere = "";

    /// <summary>Die Kartenzellen, die das MUSTER eines Gebäudes belegt — dieselbe
    /// Entscheidung wie in <see cref="DrawBuildingBody"/> (über
    /// <see cref="BuildingCellTile"/>), damit der Zähler nicht eine Nachbildung
    /// prüft. Die Fläche ist zehn Spalten breit und sechs Zeilen hoch und reicht
    /// damit über den Grundriss hinaus; genau dort liegen die Linienenden.</summary>
    private List<(int Col, int Row)>? BuildingPatternCells(Entity e)
    {
        if (Patterns == null || e.IsProp) return null;
        var bt = Patterns.GetBuildingType(e.BType);
        int first = bt.FirstPattern;
        int stack = e.Dead ? 1 : DamageFrame(e);
        if (e.Dead)
        {
            int ruin = Import.BuildingPatterns.RuinPattern(Patterns, e.BType);
            if (ruin < 0) return null;
            first = ruin;
        }
        if (first < 0 || stack < 1) return null;
        var anim = BuildingAnimCells(e);
        var list = new List<(int, int)>();
        for (int dx = 0; dx < Import.CwpFile.PatternWidth; dx++)
            for (int dy = 0; dy < Import.CwpFile.PatternHeight; dy++)
                for (int k = 0; k < stack; k++)
                {
                    int code = BuildingCellTile(first, k, dx, dy, anim);
                    if (code == 0 || !Patterns.TryGetTile(code, out _)) continue;
                    list.Add((e.Col + dx, e.Row + dy));
                    break;
                }
        return list;
    }

    private void RailCountTilesUnderBuildings(List<RailTile> tiles)
    {
        RailTilesUnderBuilding = 0;
        RailTilesUnderChecked = 0;
        RailUnderWorstWhere = "";
        int bias = RailProbeBucket0 ? 0 : RailDrawRowBias;
        var bySlot = new Dictionary<int, Entity>();
        foreach (var e in _entities)
            if (e.IsBuilding && !e.Dead) bySlot[e.Slot] = e;
        if (bySlot.Count == 0) return;

        foreach (var l in _railLines)
        {
            if (!_lineCell.TryGetValue(l.Slot, out var cells) || cells.Count < 2) continue;
            // Die Kette laeuft Bud1 -> Bud2 (RailChainFlipped hat sie gedreht),
            // also gehoert das erste Glied zu Bud1 und das letzte zu Bud2.
            foreach (var (cell, slot) in new[]
                     { (cells[0], l.Bud1), (cells[^1], l.Bud2) })
            {
                if (!bySlot.TryGetValue(slot, out var b)) continue;
                RailTilesUnderChecked++;
                int row = Mathf.RoundToInt(cell.Y);
                int front = RailThroughRowFor(b);
                if (row + bias > front) continue;   // wird NACH dem Gebaeude gezeichnet
                RailTilesUnderBuilding++;
                if (RailUnderWorstWhere.Length == 0)
                    RailUnderWorstWhere =
                        $"Linie {l.Slot}, Zelle ({cell.X:0},{row}) Zeichenzeile {row + bias} " +
                        $"gegen Platz {slot} Typ {b.BType} auf ({b.Col},{b.Row}) " +
                        $"{b.FootW}x{b.FootH}, vorderste Zeile {front}";
            }
        }
    }

    /// <summary>Die Legeart bis zum 12.08.2026, nur noch als Gegenprobe
    /// (<c>--rail-lay=cols</c>): ein Bild je Routenschritt an RailPoint, Form
    /// aus der Tabelle Stueck→Bild, Hoehe je Stueck. Der Prueflauf soll sehen,
    /// dass sie die Strecke aufreisst — sonst beweist die neue nichts.</summary>
    private void RailTilesOld(List<RailTile> tiles)
    {
        foreach (var kv in _lineRoute)
        {
            if (!_linePiece.TryGetValue(kv.Key, out var pcs)) continue;
            var route = kv.Value;
            int n = Mathf.Min(route.Count, pcs.Count);
            int laid = 0;
            var prev = Vector2.Zero;
            for (int i = 0; i < n; i++)
            {
                var at = RailPoint(route[i]);
                if (laid > 0 && at.IsEqualApprox(prev)) continue;
                if (laid > 0 && !RailNeighbourCells(prev, at)) RailTilesLoose++;
                prev = at;
                bool pylon = laid % RailPylonEvery == 0;
                laid++;
                tiles.Add(new RailTile(at, RailFrameOf[pcs[i] & 7],
                                       RailYOffsetOf[pcs[i] & 7],
                                       Mathf.RoundToInt(route[i].Y), pylon ? 65 : 64));
            }
        }
    }

    /// <summary>
    /// <b>Die STRECKE, verzahnt mit den Gebäuden.</b> Zeichnet alle Gleisstücke
    /// bis einschließlich Zeile <paramref name="throughRow"/>; der Rest bleibt
    /// für den nächsten Aufruf liegen.
    ///
    /// <para>⚠ <b>13.08.2026 — der Fehler, den der Spieler »die schiene liegt
    /// über dem gebäude« nannte.</b> Bis heute lief <c>DrawTrains()</c> ganz am
    /// Ende des Zeichendurchgangs, LANGE nach den Gebäuden. Die Strecke lag
    /// damit als Band quer über der Fassade der Bahnstation, statt hinter ihr
    /// zu verschwinden und aus ihrem eigenen Deck wieder herauszukommen. Im
    /// Bild (map_NET02, 191,55, Zoom 4) schnitt sie das ganze Gebäude von links
    /// nach rechts durch.</para>
    ///
    /// <para>Das Original hat diese Frage gar nicht: es STEMPELT Gleis und
    /// Gebäudekachel in dieselbe Kartenzelle, und wer zuletzt schreibt, gewinnt
    /// — das Gebäude, weil es später gesetzt wird (siehe
    /// <c>BuildingCellTile</c>). Ein Gleis unter einem Gebäude gibt es dort
    /// nicht; sichtbar ist der Anbau der Gebäudegrafik, und der ist auf
    /// dieselbe Deckhöhe gezeichnet (<see cref="RailDockDeckPixel"/>).</para>
    ///
    /// <para>Nachgebaut wird das mit der Zeichenreihenfolge: Gleisstücke einer
    /// Zeile kommen VOR den Gebäuden, deren Grundriss bis in diese Zeile
    /// reicht. Ein Gleis, das vor einem Gebäude (weiter südlich) vorbeiläuft,
    /// bleibt damit sichtbar, eines im Grundriss verschwindet darunter.</para>
    /// </summary>
    /// <summary>
    /// <b>Bis zu welcher Zeichenzeile darf die Strecke VOR diesem Gebäude
    /// gezeichnet werden?</b> — <c>Zeile + 4</c>, und beide Summanden sind
    /// gelesen.
    ///
    /// <para>⭐ 13.08.2026. Hier stand <c>b.Row + Mathf.Max(1, b.FootH) − 1</c>,
    /// also die Tiefe des GRUNDRISSES. Das war UNSERE Konstruktion, und sie ist
    /// eine andere Größe als die des Originals: der Grundriss ist je Gebäude
    /// 4, 5 oder 6 Zeilen tief, das Original rechnet mit einer FESTEN Zahl.</para>
    ///
    /// <para><b>Die beiden Fächer, gelesen.</b> Zeichenliste und Fächerung sind
    /// dieselben für Gleis und Gebäude (bis 70 Fächer, je bis 499 Einträge,
    /// Zähler ab 0xAB93F0, Einträge ab 0xAB8068). Beide Aufbauroutinen hängen
    /// nebeneinander am Sammler @0x430DC0 — Gebäude @0x430E03, Gleis
    /// @0x430E08:</para>
    /// <code>
    ///   Gleis   @0x42DF40, Feld 0xC2C222:
    ///     0x42DFCE  sub bx, [0x5387B0]     ; bx = zeile - kamerazeile
    ///     0x42DFE9  lea ecx, [ebx + 2]     ; FACH = zeile + 2
    ///
    ///   Gebaeude @0x42FCD0, Feld 0xC06944:
    ///     0x42FD4D  add bx, 3              ; zeile + 3   (wenn +0x34 == 0)
    ///     0x42FD53  sonst: add bx, [+0x36]
    ///     0x42FDA6  sub bx, [0x5387B0]
    ///     0x42FDB8  lea eax, [ebx + 2]     ; FACH = zeile + 3 + 2 = zeile + 5
    /// </code>
    ///
    /// <para><b>Dass die 3 wirklich nur die REIHENFOLGE meint, steht daneben:</b>
    /// dieselbe <c>bx</c> geht danach in den Schirm-y (<c>imul bx, bx, 0x14</c>
    /// @0x42FDC4), und der zieht <c>0x3C = 60</c> ab (@0x42FDC8) — genau
    /// <c>3 · 20</c>. Die 3 hebt sich in der LAGE exakt heraus und wirkt nur im
    /// Fach. Für den Fall <c>+0x34 != 0</c> rechnet @0x42FDD5..@0x42FDE8 die
    /// Differenz <c>(3 − [+0x36]) · 20</c> wieder auf y auf — dieselbe
    /// Verrechnung, anderer Wert.</para>
    ///
    /// <para><b>Daraus die Regel:</b> ein Gleisstück in Zellzeile <c>R</c> liegt
    /// vor einem Gebäude in Zeile <c>B</c>, solange
    /// <c>R + 2 &lt; B + 5</c>, also <c>R ≤ B + 2</c>. Da
    /// <see cref="RailTile.DrawRow"/> schon <c>R + 2</c> ist, ist die Schranke
    /// <c>B + 4</c>. Das Original verdeckt also die Zeilen <c>B</c>, <c>B+1</c>
    /// und <c>B+2</c> und zeigt die Strecke ab <c>B+3</c> — unabhängig davon, wie
    /// tief der Grundriss ist.</para>
    ///
    /// <para>⚠ <b>Was sich dadurch ÄNDERT, und was nicht.</b> Bei Grundriss 5
    /// war die alte Rechnung zufällig richtig (<c>B+4</c> in beiden). Bei 4 haben
    /// wir eine Zeile zu wenig verdeckt, bei 6 eine zu viel. Der gemeldete Fall —
    /// Linienende auf <c>B+2</c> an Fabrik, Mine und Flughafen — bleibt
    /// verdeckt, in beiden Rechnungen; <b>das ist die Lage des Originals und
    /// nicht unser Fehler</b>, siehe den Bericht. Diese Änderung macht die
    /// Reihenfolge treu, sie macht das Anschlussstück nicht sichtbar.</para>
    ///
    /// <para>Gegenprobe: <c>--rail-lay=footh</c> nimmt die alte Rechnung nach
    /// dem Grundriss zurück.</para>
    ///
    /// <para>⚠⚠ <b>ENTSCHIEDEN, NICHT OFFEN — 13.08.2026.</b> Der Spieler hat den
    /// Fall gemeldet (»so fehlt oft die letzte strecke zur anbindung an ein
    /// gebäude sauber«), die Messung stand danach: <b>224 von 476 Enden (47 %)
    /// sind überdeckt, 128 vollständig</b>, und bei Fabrik, Mine und Flughafen
    /// <b>166 von 166</b>, weil ihre Enden auf <c>B+2</c> liegen. Vorgelegt wurde
    /// ihm die Wahl »sichtbar machen (unsere Abweichung)« gegen »originaltreu
    /// lassen«, und er hat <b>originaltreu</b> gewählt.</para>
    ///
    /// <para>Damit ist das <b>kein Fehler und keine offene Aufgabe</b>: was man
    /// dort sieht, ist das Bild von 1997. Wer die Enden künftig sichtbar machen
    /// will, ändert damit eine <b>entschiedene</b> Sache — dann gehört die
    /// Entscheidung neu geholt, so wie die gekuppelten Waggons eine bewusste
    /// Abweichung sind und als solche markiert bleiben. Nicht »reparieren«.</para>
    /// </summary>
    private static int RailThroughRowFor(Entity b)
        => RailProbeFootH
               ? b.Row + Mathf.Max(1, b.FootH) - 1
               : b.Row + BuildingDrawRowBias + RailDrawRowBias - 1;

    /// <summary>Um wieviele Zeilen SPÄTER als seine eigene Zeile wird ein Gebäude
    /// einsortiert — <c>3</c>, gelesen an @0x42FD4D. Siehe
    /// <see cref="RailThroughRowFor"/>.</summary>
    private const int BuildingDrawRowBias = 3;

    /// <summary>Gegenprobe (<c>--rail-lay=footh</c>): wieder nach der Tiefe des
    /// Grundrisses statt nach der festen 3 des Originals.</summary>
    public static bool RailProbeFootH
    {
        get
        {
            if (_probeFootH.HasValue) return _probeFootH.Value;
            bool hit = false;
            foreach (string a in OS.GetCmdlineUserArgs())
                if (a.StartsWith("--rail-lay=") && a["--rail-lay=".Length..].Contains("footh"))
                    hit = true;
            _probeFootH = hit;
            return hit;
        }
    }

    private static bool? _probeFootH;

    private void DrawRailUpTo(int throughRow, ref int at)
    {
        var tiles = RailTiles();
        // ⚠ ZWEI DURCHGÄNGE, Schatten zuerst. Ein Schatten liegt (+19,+22) px
        // unten rechts von seinem Stück, also im Bereich des NACHBARSTÜCKS —
        // gemischt gezeichnet würde er dessen Träger verdunkeln, und die Strecke
        // bekäme einen dunklen Streifen auf jedem zweiten Stück.
        for (int i = at; i < tiles.Count && tiles[i].DrawRow <= throughRow; i++)
        {
            var s = tiles[i];
            var m = GetRailShadow(s.Frame, s.Part);
            if (m == null) continue;
            DrawTexture(m, s.At - ComposedAnchor + new Vector2(0, s.YOff), ShadowTint);
            RailShadowsDrawn++;
        }
        for (; at < tiles.Count; at++)
        {
            var t = tiles[at];
            if (t.DrawRow > throughRow) return;
            var tex = GetRailTexture(t.Frame, t.Part);
            if (tex == null) { at = tiles.Count; return; }   // ohne Bilder gar nichts
            DrawTexture(tex, t.At - ComposedAnchor + new Vector2(0, t.YOff));
            RailTilesDrawn++;
        }
    }

    /// <summary>Die Strecke und die Gebäude in EINEM Durchgang, hintere Zeile
    /// zuerst. Ersetzt die alte Reihenfolge »erst alle Gebäude, viel später die
    /// Strecke«; siehe <see cref="DrawRailUpTo"/> für den Grund.</summary>
    private void DrawRailAndBuildings()
    {
        RailTilesDrawn = 0; RailShadowsDrawn = 0; SlopeDrawn = 0; SlopeFallback = 0;
        int at = 0;
        if (_drawSprites && Patterns != null)
            foreach (var b in BuildingsBackToFront())
            {
                // Ein Gebäude verdeckt alles bis zu seiner VORDERSTEN Zeile —
                // sein Grundriss reicht so weit nach unten, und jede Kachel
                // darin wird in ihrer eigenen Zeile gestempelt.
                DrawRailUpTo(RailThroughRowFor(b), ref at);
                DrawBuildingBody(b);
            }
        DrawRailUpTo(int.MaxValue, ref at);
    }

    /// <summary>Liegen zwei gezeichnete Stuecke Kante an Kante? Auf dem Schirm
    /// heisst das genau 40 px in x ODER 20 px in y, nichts sonst.</summary>
    private static bool RailNeighbourCells(Vector2 a, Vector2 b)
    {
        float dx = Mathf.Abs(a.X - b.X), dy = Mathf.Abs(a.Y - b.Y);
        return (dx < 0.5f && Mathf.Abs(dy - TileH) < 0.5f)
            || (dy < 0.5f && Mathf.Abs(dx - TileW) < 0.5f);
    }

    /// <summary>Nur noch die WAGGONS — die Strecke unter ihnen liegt jetzt bei
    /// den Gebäuden, siehe <see cref="DrawRailAndBuildings"/>.</summary>
    private void DrawTrains()
    {
        foreach (var w in _wagons)
        {
            // Am Streckenende loescht das Original jeden Waggon einzeln; bei
            // uns bleibt der Satz liegen und wird hier uebersprungen.
            if (w.Hidden) continue;
            int part = WagonPart.TryGetValue(w.Index, out var pp) ? pp : 58;
            int piece = w.Index == 3 ? (w.Piece + 4) & 7 : w.Piece;   // @0x42b52a
            var tex = GetTrainTexture(part, piece);
            var at = RailPoint(new Vector2(w.Col, w.Row));
            if (tex == null)
            {
                DrawCircle(at, 3f, new Color(0.9f, 0.6f, 0.2f));
                continue;
            }
            // ⚠ 13.08.2026 — der Waggon sitzt gegen SEIN GLEISBILD versetzt,
            // und der Versatz ist gelesen: das Original reiht das Gleis bei
            // `G + (-26, -82)` ein und den Waggon bei `G + (feinX-40,
            // feinY-110)` (Einreiher @0x42DF40 bzw. @0x42E100, F: 0x42D130 /
            // 0x42D2E0; die Konstanten 6/62 bzw. 20/90 stehen dort woertlich
            // und in beiden Fassungen gleich). Mit der Feinlage (20,0) einer
            // Zellmitte macht das
            //     Waggon - Gleis = (+6, -28).
            // Wir hatten (0, -23) -- der Waggon stand also 6 px zu weit links
            // und 5 px zu tief auf der Schiene.
            //
            // ⚠ NICHT uebernommen ist die zweite Haelfte desselben Berichts:
            // dass unser Deck 50 px zu tief liege. Diese Zahl ist gegen eine
            // Bodenlinie gerechnet, die aus Sprite-Unterkanten hergeleitet
            // wurde -- ihr Verhaeltnis zu unserem MapBaker (BlitAnchor = -50,
            // dieselbe Zahl) ist ausdruecklich ungelesen. Unser Deck sitzt
            // gemessen BUENDIG auf den Gleisstummeln der Originalgrafik
            // (42 von 42 Enden, 0 px, --rail-check), und das ist die einzige
            // Messung gegen echte Originalpixel, die wir haben.
            //
            // ⚠ UNSERE NAEHERUNG: die Feinlage haengt im Original an der
            // PARITAET DER HALBZEILE -- (20,0) in der Zellmitte, (0,10) auf
            // der Randmitte. Unsere Kette kennt nur ganze Zellen, also gilt
            // hier immer der gerade Fall. Die Randmitten bleiben offen.
            DrawTexture(tex, at - ComposedAnchor + WagonOverRail);
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
                _econMovedT += n;
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
                case 2: hq.StockW += n; _econMovedW += n; break;
                case 3: hq.StockF += n; _econMovedF += n; break;
                default: hq.StockS += n; _econMovedS += n; break;
            }
            return;
        }

        // ⚠ 11.08.2026 — HIER STAND EIN RUECKWEG Basis -> Fabrik, und er ist
        // ersatzlos gestrichen.
        //
        // Er war am 01.08. eingebaut worden, weil eine Waffen-Fabrik nur Waffen
        // macht und die Entwuerfe alle drei Teilearten kosten: eine Fabrik, die
        // Einheiten baut, muesste sich die fremden Teile holen. Genau diese
        // Voraussetzung ist jetzt widerlegt — Einheiten entstehen in der BASIS
        // (siehe IsUnitPlant, HELPG.TXT 24/32/34 und die Produktionsschaltflaeche
        // @0x44A6D8). Eine Fabrik braucht keine fremden Teile, und der Rueckweg
        // hat nur die Basis leergezogen, aus der der Spieler bauen soll:
        // gemessen auf map_DM_1 lief das Fahrwerklager der Basis in den ersten
        // zehn Sekunden von 400 auf 360 herunter, ohne dass irgendetwas
        // entstanden waere.
        //
        // Das Original kennt diesen Weg ohnehin nicht. Es bewegt Waren ALLEIN
        // ueber die Bahn (spoj_launch @0x4C6410 / Zug-Tick @0x4C69C0 — »nichts
        // anderes im ganzen Programm bewegt die vier Lagerfelder«, siehe
        // Simulation/RailFreight.cs), und die Typmatrix @0x504128 kennt in der
        // Zeile »1 Basis« nur Werte 0 und 2: aus der Basis faehrt nichts hinaus.
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
    /// abliefert: <b>nichts</b>.
    ///
    /// <para>⚠ 11.08.2026 — hier stand vorher »genau das, was ihr nächster Bau
    /// kostet«, und davor eine pauschale 40. Beide Zahlen hingen an der
    /// Annahme, dass die FABRIK Einheiten baut. Sie tut es nicht: Einheiten
    /// entstehen in der Basis (siehe <see cref="IsUnitPlant"/>). Eine Fabrik
    /// baut nichts, also braucht sie nichts zurückzuhalten.</para>
    ///
    /// <para>Das ist auch das Verhalten des Originals: <c>spoj_launch</c>
    /// @0x4C6410 lädt reihum eine Einheit je Ware, Budget 200 je Fahrt
    /// (<c>mov al,0xC8</c> @0x4C6652), begrenzt <b>allein</b> durch den Bestand
    /// des Abfahrtsgebäudes — es kennt keine Rücklage. Der Zug räumt eine
    /// Fabrik leer, und genau so hat der Spieler es am 11.08. gesehen
    /// (»W steigt auf 10 und fällt wieder auf 0«). Richtig daran war alles
    /// ausser der Stelle, an der er bauen sollte.</para>
    ///
    /// <para>⚠ Was UNSERE SETZUNG bleibt: dass der Nahweg überhaupt fährt.
    /// Das Original bewegt Waren nur über Bahnverbindungen, die der Spieler
    /// legt; Kampagnenkarten starten ohne Linien (map_05: 9 Knoten, 0
    /// Verbindungen) und Linien legen kann die Engine noch nicht. Bis dahin
    /// steht dieser Weg dafür ein — er fährt jetzt aber dasselbe wie die Bahn:
    /// alles.</para>
    /// </summary>
    private int OwnReserve(Entity f) => 0;

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

    /// <summary>Prüfstand für das FLUGZEUGBILD: ein einzelnes Flugzeug anwählen
    /// — dasselbe, was ein Mausklick tut — und melden, welches Bild der
    /// Bedienblock dafür zieht. Bevorzugt eines, das ein Bild hat und fliegt,
    /// damit auf dem Bildschirmfoto beides zu sehen ist: der Rumpf auf der Karte
    /// und sein Bild unten links.</summary>
    public Vector2? DebugDemoAirPortrait()
    {
        if (_special.Count == 0)
        { GD.Print("demo-airpic: diese Karte hat keine sec19-Flugzeuge"); return null; }

        // Erst starten lassen, was im Hangar steht: ein geparktes Flugzeug ist
        // nicht auf dem Bildschirm und kann nicht angeklickt werden.
        int owner = _special.Find(s => !s.Dead)?.Owner ?? -1;
        if (owner >= 0) LaunchAircraft(owner);

        // ⭐ Angewaehlt wird mit einem KLICK, nicht mit einer Zuweisung: so
        // prueft dieser Prüfstand auch PickAir mit — an der Stelle, an der auch
        // die Maus landet. Ein Flugzeug ueber einem Gebaeude verliert den Klick
        // (das Gebaeude gewinnt), deshalb wird durchprobiert.
        int pick = -1;
        int tried = 0;
        for (int pass = 0; pass < 2 && pick < 0; pass++)
            for (int i = 0; i < _special.Count; i++)
            {
                var s = _special[i];
                if (s.Dead || s.Stored) continue;
                if (pass < 1 && UI.PortraitBank.PictureOfAircraft(s.Kind) <= 0) continue;
                tried++;
                SelectAt(s.Pos - new Vector2(0, AirShadowDrop));
                if (_selAir != i) continue;              // etwas anderes lag davor
                pick = i;
                break;
            }
        if (pick < 0)
        {
            GD.Print($"demo-airpic: {tried} Flugzeuge angeklickt, keines angewaehlt " +
                     "— entweder liegt ueberall etwas davor oder PickAir trifft nicht");
            return null;
        }

        var a = _special[pick];
        var p = PanelPortrait();
        GD.Print($"demo-airpic: Klick {tried} traf Platz {a.Slot} \"{a.Name}\" Art {a.Kind} " +
                 $"({a.KindName}) P{a.Owner} {(a.Stored ? "im Hangar" : "fliegt")} " +
                 $"-> Bild {(p.ChassisPic > 0 ? "p" + p.ChassisPic.ToString("00") : "keines")}" +
                 (p.Why.Length > 0 ? $" ({p.Why})" : ""));
        return a.Pos - new Vector2(0, AirShadowDrop);
    }

    /// <summary>
    /// Derselbe Prüfstand wie <see cref="DebugDemoAirPortrait"/>, nur für einen
    /// FUSSSOLDATEN: einen anklicken und melden, welches der zwölf Bilder der
    /// Folge 403 dabei herauskommt. ⭐ Angewählt wird mit einem KLICK, damit die
    /// Stelle mitgeprüft wird, an der auch die Maus landet.
    /// </summary>
    public Vector2? DebugDemoInfPortrait()
    {
        int pick = -1, tried = 0;
        for (int i = 0; i < _entities.Count; i++)
        {
            var e = _entities[i];
            if (e.Dead || e.Infantry < 0) continue;
            tried++;
            SelectAt(e.Pos);
            // Nicht auf DIESEN Platz bestehen: der Klick trifft, was oben liegt,
            // und Fußsoldaten stehen dicht. Es reicht, dass EIN Soldat einzeln
            // angewählt ist — das ist der Fall, den der Bedienblock zeichnet.
            //
            // ⚠ <c>_sel.Count</c> ist bei einem FREMDEN Soldaten 0, nicht 1:
            // SelectAt lässt die Auswahlmenge leer und setzt nur
            // <c>_selected</c> (»look, do not touch«, 0x2118). Auch dieser Fall
            // zeichnet ein Bild, denn PanelPortrait wehrt nur <c>n > 1</c> ab.
            if (_sel.Count > 1 || _selected < 0 ||
                _entities[_selected].Infantry < 0) continue;
            pick = _selected;
            break;
        }
        if (pick < 0)
        {
            GD.Print($"demo-infpic: {tried} Fusssoldaten angeklickt, keiner einzeln " +
                     "angewaehlt — diese Karte hat keine oder es liegt etwas davor");
            return null;
        }
        var f = _entities[pick];
        int design = InfantryDesignOf(f.Infantry);
        var p = PanelPortrait();
        GD.Print($"demo-infpic: Klick {tried} traf Platz {f.Slot} Satz {f.Infantry} " +
                 $"unit_type {f.UnitType} P{f.Owner}, Entwurf {design} " +
                 $"\"{(design >= 0 && _designBySlot.TryGetValue(design, out var dq) ? dq.Name : "?")}\"" +
                 $" -> Bild {(p.ChassisPic > 0 ? "p" + p.ChassisPic.ToString("00") : "keines")}" +
                 (p.Why.Length > 0 ? $" ({p.Why})" : ""));
        return f.Pos;
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

    /// <summary>
    /// Die MISSIONSUHR in Spielminuten — die EINE Quelle fuer beide Anzeigen:
    /// das Feld unten links im Bedienfeld und die Zeile »Missionszeit« im
    /// Abschlussfenster. Vorher hatte das Abschlussfenster eine eigene
    /// Rechnung (reale Minuten:Sekunden aus <see cref="DebugClock"/>), das
    /// Bedienfeld gar keine.
    ///
    /// <para><b>Belegt:</b> Der Taktzaehler 0x81AA28 laeuft mit 50 Hz
    /// (SetTimer 0x14 @0x415BC5); bei 250 Takten (<c>cmp al,0xFA</c> @0x4160B3)
    /// steigt das MINUTENBYTE 0x81AA2C, bei 60 Minuten (@0x416135) das
    /// STUNDENBYTE 0x8154E4. Eine Spielminute sind also fuenf reale Sekunden.
    /// Beim Missionsstart setzt 0x437EFD alle drei Bytes auf 0
    /// (<c>mov [0x81AA28],bl</c> / <c>[0x81AA2C],bl</c> / <c>[0x8154E4],bl</c>),
    /// die Uhr faengt je Mission bei 00:00 an.</para>
    ///
    /// <para>Laeuft ein Missionsskript, kommt die Zahl aus dessen Taktzaehler
    /// (<c>MissionScript.Minutes</c>), damit Anzeige und Regelwerk nie
    /// auseinanderlaufen — auch nicht, wenn der Pruefstand die Uhr vorspult.
    /// Ohne Skript (Gemetzel) bleibt die reale Spielzeit geteilt durch
    /// <see cref="Campaign.MissionScript.RealSecondsPerGameMinute"/>.</para>
    /// </summary>
    public int MissionMinutes =>
        _mscript?.Minutes
        ?? (int)(DebugClock / Campaign.MissionScript.RealSecondsPerGameMinute);

    /// <summary>Die Missionsuhr, so wie das Original sie druckt: Stunden und
    /// Minuten, je zweistellig mit fuehrender Null, getrennt durch ":".
    /// Siehe <see cref="MissionMinutes"/> fuer die Fundstellen.</summary>
    public string MissionClockText
    {
        get { int m = MissionMinutes; return $"{m / 60:00}:{m % 60:00}"; }
    }

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

    /// <summary>
    /// <b>DER FESTE TAKT.</b> Die Simulation läuft nicht mehr auf der Bildzeit.
    ///
    /// <para>⚠ <b>Der Befund, der das erzwungen hat</b> (15.08.2026, gemessen
    /// mit <c>--determinism-twin</c>): gleicher Keim, gleiche Karte, gleiche
    /// SIMULIERTE Zeit von 10 s — und je nach Bildrate drei verschiedene
    /// Zustände:</para>
    /// <code>
    ///   30 Bilder/s   300 Takte   Prüfsumme 5071A756A80B2634   HP 36911
    ///   60 Bilder/s   600 Takte             8C5F21CD5F8AF503      36965
    ///  144 Bilder/s  1440 Takte             F45A1091E165730F      36911
    /// </code>
    /// <para>Ursache war diese Stelle: <c>_Process(double delta)</c> reichte die
    /// echte verstrichene Bildzeit als <c>dt</c> in die Spielwelt, an 74
    /// Stellen allein in dieser Datei. Wer schneller zeichnet, würfelt öfter,
    /// schießt öfter und fährt anders. Im Zwillingslauf mit A=1/60 und B=1/30
    /// liefen die beiden nach <b>18 Takten = 0,30 s</b> auseinander.</para>
    ///
    /// <para><b>Ohne festen Takt ist Lockstep über Netz unmöglich</b>, gleich
    /// welcher Transport — und darum ist es die erste Stufe des
    /// Mehrspieler-Plans.</para>
    ///
    /// <para>⚠ <b>UNSERE SETZUNG ist die Taktrate</b>, nicht das Verfahren.
    /// 60/s ist genommen, weil die Simulation faktisch schon damit lief (das
    /// Spiel zeichnet mit 60 Bildern/s, und jedes Bild war ein Schritt) — jede
    /// andere Zahl hätte die Balance verschoben. Das Original taktet mit 50 Hz;
    /// wer darauf umstellen will, ändert <see cref="SimHz"/> und muss
    /// TickScale und die Fahrzeiten mit prüfen.</para>
    ///
    /// <para>Der Nachlauf ist gedeckelt: bleibt der Rechner mehr als
    /// <see cref="SimMaxCatchUp"/> Takte zurück, wird der Rückstand verworfen
    /// statt aufgeholt. Sonst holt ein Ruckler die verlorene Zeit in einem
    /// Schwall nach und reisst die Bildrate weiter ein — die »Todesspirale«.
    /// Im Netzspiel gehört an diese Stelle später das Warten auf den
    /// Server.</para></summary>
    private const int SimHz = 60;
    private const float SimDt = 1f / SimHz;
    private const int SimMaxCatchUp = 5;
    private float _simAcc;

    /// <summary>Wieviele Simulationstakte der letzte Bildlauf gefahren hat, und
    /// wie oft der Deckel gegriffen hat — ohne diese zwei Zahlen ist ein
    /// Ruckler nicht von einem Rechenfehler zu unterscheiden.</summary>
    public int SimStepsLastFrame, SimCatchUpDropped;

    public override void _Process(double delta)
    {
        if (_nav == null) return;
        float dt = (float)delta;

        // Der Klang laeuft nach der Uhr des RECHNERS, nicht nach der
        // Simulation: er gehoert nicht in den Zustand und darf nicht mitzaehlen.
        _musicTick += dt;
        if (_musicTick >= 2f) { _musicTick = 0; Audio.MidiMusic.Poll(); }

        _simAcc += dt;
        int steps = 0;
        bool moved = false;
        while (_simAcc >= SimDt && steps < SimMaxCatchUp)
        {
            moved |= SimTick(SimDt);
            _simAcc -= SimDt;
            steps++;
        }
        if (steps >= SimMaxCatchUp && _simAcc >= SimDt) { _simAcc = 0f; SimCatchUpDropped++; }
        SimStepsLastFrame = steps;

        if (moved || _effects.Count > 0 || _tracers.Count > 0 || _shots.Count > 0)
        {
            QueueRedraw();
            if (_selected >= 0) UpdatePanel();
        }
    }

    /// <summary>Ein Simulationstakt — alles, was den Zustand anfasst. Gibt
    /// zurück, ob sich etwas bewegt hat (nur fürs Neuzeichnen).</summary>
    private bool SimTick(float dt)
    {
        // DER TAKTANFANG: alles, was für diesen Takt fällig ist, wirkt hier —
        // und nur hier. Siehe Simulation/Commands/CommandBridge.cs.
        //
        // ⚠ VOR _clock und vor allem anderen. Ein Befehl, der nach der Bewegung
        // wirkt, verschiebt die Einheit um einen Takt gegenüber der anderen
        // Maschine, wenn dort die Reihenfolge anders herum steht — und genau
        // solche Unterschiede sind es, die einen Lockstep-Lauf auseinander
        // laufen lassen.
        //
        // ⚠ NEBENWIRKUNG, beabsichtigt und der Kern des Umbaus: ein Klick wirkt
        // jetzt einen Takt später (16,7 ms bei SimHz = 60). Das Original kennt
        // keinen kürzeren Weg — post() @0x4C1C50 schickt selbst den EIGENEN
        // Befehl über die Leitung und führt ihn erst aus, wenn er über Receive
        // im Ring zurückkommt. Unser bisheriger Direktzugriff war also nicht
        // bloss netzuntauglich, er war nie das Original.
        CommandTick();

        _clock += dt;

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
        // ⚠ Die Messuhr zaehlt SIMULATIONStakte, nicht Bilder — seit dem festen
        // Takt ist das derselbe Wert fuer jeden Lauf, und genau das macht die
        // Zahlen der Pruefstaende vergleichbar.
        DebugClock += dt;
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
        return moved;
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
    /// <summary>
    /// <b>DIE HANGKLASSE einer Zelle</b> — das Flag-Byte der Kachel, alles über
    /// 4 zählt als flach.
    ///
    /// <para>Das ist die Zahl, mit der das Original @0x429AD5 (über 0x41d110,
    /// <c>byte[karte + (zeile·breite + spalte)·4 + 3]</c>) sowohl den Turmsitz
    /// als auch den BILDBLOCK wählt. Den Turmsitz hat
    /// <see cref="TurretOffset"/> immer schon so gerückt; das Bild nicht — der
    /// Rumpf blieb flach, während der Boden unter ihm kippte und der Turm schon
    /// zur Seite rutschte.</para>
    ///
    /// <para>Auf map_NET02 tragen 4728 von 52 900 Zellen (9 %) eine Klasse
    /// 1..4; der Rest ist eben.</para></summary>
    private int SlopeClassOf(int col, int row)
        => SlopePoses && _flagLookup != null
           && _flagLookup.TryGetValue((col, row), out int fl) && fl <= 4
           ? fl : 0;

    /// <summary>Wieviele Bilder die Hangposen im letzten Durchgang gestellt
    /// haben, und wie oft auf das flache zurückgefallen wurde.</summary>
    public int SlopeDrawn, SlopeFallback;

    /// <summary>Ausschalter für die Gegenprobe: <c>--slope=0</c> zeichnet wieder
    /// jede Einheit flach. Ein Zähler, der nicht scheitern kann, belegt
    /// nichts.</summary>
    private static bool SlopePoses
    {
        get
        {
            if (_slopePoses.HasValue) return _slopePoses.Value;
            bool on = true;
            foreach (string a in OS.GetCmdlineUserArgs())
                if (a.StartsWith("--slope=")) on = a["--slope=".Length..].ToFloat() != 0f;
            _slopePoses = on;
            return on;
        }
    }

    private static bool? _slopePoses;

    /// <summary>Was der letzte Bilddurchgang an Hangposen und Schatten gestellt
    /// hat — damit »die Hangposen sind da« nicht wieder nur eine Behauptung
    /// ist.</summary>
    public string DebugSpriteInfo()
        => $"Hangposen {SlopeDrawn} gezeichnet, {SlopeFallback} mangels Bild flach" +
           $"; Gleisschatten {RailShadowsDrawn}";

    private Texture2D? GetHullTexture(int unitType, int facing, int pose = 0, int slope = 0)
    {
        string ut = unitType.ToString();
        string dir = pose > 0 ? $"{ut}/g{pose}" : ut;
        if (slope > 0)
        {
            var s = LoadUnitPart("hull", $"{dir}/s{slope}", facing);
            if (s != null) { SlopeDrawn++; return s; }
            SlopeFallback++;
        }
        var t = LoadUnitPart("hull", dir, facing)
                ?? (facing != 0 ? LoadUnitPart("hull", dir, 0) : null)
                // a pose the export does not carry falls back to group 0 rather
                // than making the unit disappear
                ?? (pose > 0 ? GetHullTexture(unitType, facing, 0, slope) : null);
        if (t != null) return t;
        // ... und zuletzt die ganze Bank unter der Bauteilnummer, siehe
        // GetTurretTexture.
        LoadMounts();
        if (!_hullComponent.TryGetValue(unitType, out int comp) || comp <= 0) return null;
        int blk = SlopeBlock(slope);
        return (blk > 0 ? LoadUnitPart("part", $"{comp}/b{blk}", facing) : null)
               ?? LoadUnitPart("part", comp.ToString(), facing);
    }

    /// <summary>unit_type -> Bauteilnummer, aus <c>parts_index.json</c>.</summary>
    private static readonly Dictionary<int, int> _hullComponent = new();

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

    /// <summary>
    /// Das Geschütz. Gesucht wird in dieser Reihenfolge: die Hangpose des
    /// benannten Satzes, der benannte Satz selbst, dann die <b>ganze Bank</b>
    /// unter der Bauteilnummer.
    ///
    /// <para>⚠ Der Rückfall auf <c>part/</c> ist kein Beiwerk. Der Exporter hat
    /// bis zum 15.08.2026 nur Waffen geschrieben, die auf einer Karte stehen —
    /// die 601 gespeicherten Entwürfe greifen aber auch auf Bauteile, die auf
    /// keiner der 44 Karten vorkommen (15 „Minenleger", 18 „Flak-Geschütz",
    /// 77 „Antiradar" und sieben weitere). Ein Entwurf damit hatte ein
    /// unsichtbares Geschütz.</para></summary>
    private Texture2D? GetTurretTexture(int weapon, int facing, int slope = 0)
    {
        if (weapon == 0) return null;
        if (slope > 0)
        {
            var s = LoadUnitPart("turret", $"{weapon}/s{slope}", facing);
            if (s != null) { SlopeDrawn++; return s; }
        }
        var t = LoadUnitPart("turret", weapon.ToString(), facing);
        if (t != null) return t;
        int blk = SlopeBlock(slope);
        return (blk > 0 ? LoadUnitPart("part", $"{weapon}/b{blk}", facing) : null)
               ?? LoadUnitPart("part", weapon.ToString(), facing);
    }

    /// <summary>Der Bildblock zu einer Hangklasse, aus <c>parts_index.json</c>
    /// (<c>slope_blocks</c>, die Tabelle @0x4fa4d8 mit den BILDVERSÄTZEN
    /// 0/16/32/8/24). Ein Block ist acht Bilder.</summary>
    private static int SlopeBlock(int k)
    {
        LoadMounts();
        return _slopeBlockTable != null && k > 0 && k < _slopeBlockTable.Length
               ? _slopeBlockTable[k] / 8 : 0;
    }

    private static int[]? _slopeBlockTable;

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
            if (doc.RootElement.TryGetProperty("slope_blocks", out var sb)
                && sb.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                var l = new List<int>();
                foreach (var v in sb.EnumerateArray()) l.Add(v.GetInt32());
                if (l.Count > 0) _slopeBlockTable = l.ToArray();
            }
            if (!doc.RootElement.TryGetProperty("hulls", out var group)) return;
            foreach (var item in group.EnumerateObject())
            {
                if (!int.TryParse(item.Name, out int ut)) continue;
                if (item.Value.TryGetProperty("groups", out var gv))
                    _poseGroups[ut] = System.Math.Max(1, gv.GetInt32());
                // Die Bauteilnummer, damit ein Rumpf ohne eigenen Satz auf die
                // ganze Bank unter part/ zurueckfallen kann.
                if (item.Value.TryGetProperty("component", out var cv))
                    _hullComponent[ut] = cv.GetInt32();
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

    /// <summary>Die sichtbare Fläche eines Bildes — für den Prüfstand, damit
    /// »die Waffe sitzt daneben« eine ZAHL bekommt statt einer Schätzung am
    /// Bildschirm.</summary>
    private static Rect2I? UsedRect(Texture2D? tex)
    {
        var img = tex?.GetImage();
        if (img == null) return null;
        var r = img.GetUsedRect();
        return r.Size.X <= 0 || r.Size.Y <= 0 ? null : r;
    }

    /// <summary>
    /// <b>PRÜFSTAND für den WAFFENSITZ auf dem SCHIFF.</b> Gemeldet am
    /// 13.08.2026 als »beim größten schiff scheint die waffe nicht korrekt
    /// drauf zu sitzen sondern einige felder daneben«.
    ///
    /// <para>Er schreibt für jedes Schiff dieser Karte EINE Zeile mit: Rumpf und
    /// Rumpfbauteil, Variante (+0x0d), Flag-Byte der Kachel, den Montagepunkt
    /// aus <c>parts_index.json</c>, den daraus gerechneten Versatz — und
    /// daneben, GEMESSEN aus den beiden Bildern, die Mitte der sichtbaren
    /// Rumpffläche gegen die Mitte der sichtbaren Waffenfläche. Das Klaffen
    /// steht in Bildpunkten UND in Zellen (<c>{TileW}×{TileH}</c>), weil nur
    /// die Zellenzahl die Meldung des Spielers prüft: ein fehlender
    /// Montagepunkt verschiebt um weniger als eine Zelle, »einige Felder« ist
    /// etwas anderes.</para>
    ///
    /// <para>⚠ Die Mitte der sichtbaren Fläche ist NICHT der Drehpunkt der
    /// Waffe — sie ist nur ein Maß, das für beide Bilder gleich gebildet wird.
    /// Was sie belegt, ist der ABSTAND, nicht der Sollwert.</para></summary>
    public string TurretSeatCheck()
    {
        LoadMounts();
        var sb = new System.Text.StringBuilder();
        sb.Append("waffensitz-check: ");

        // ---- die Montagetabelle, Rumpf fuer Rumpf ---------------------------
        var ohne = new List<string>();
        for (int ut = 150; ut <= 158; ut++)
        {
            bool has = _mount != null && _mount.ContainsKey(ut);
            _hullComponent.TryGetValue(ut, out int comp);
            if (!has) ohne.Add($"{ut}(Bauteil {comp})");
        }
        sb.Append($"Schiffsruempfe 150..158, ohne Montagepunkt: ")
          .Append(ohne.Count == 0 ? "keiner" : string.Join(", ", ohne)).Append('\n');

        // ---- jedes Schiff dieser Karte, eine Zeile --------------------------
        int lines = 0, ships = 0;
        var seen = new HashSet<(int, int, int)>();
        for (int i = 0; i < _entities.Count; i++)
        {
            var e = _entities[i];
            if (e.IsProp || e.IsBuilding || e.UnitType < 150 || e.UnitType > 158) continue;
            ships++;
            // eine Zeile je Rumpf/Waffe/Blickrichtung reicht — 26 gleiche
            // Zeilen belegen nichts, was die erste nicht schon belegt
            if (!seen.Add((e.UnitType, e.Weapon, e.Facing))) continue;
            lines++;
            _hullComponent.TryGetValue(e.UnitType, out int comp);
            int flag = _flagLookup != null
                       && _flagLookup.TryGetValue((e.Col, e.Row), out int fl) && fl <= 4 ? fl : 0;
            string mnt = _mount != null && _mount.TryGetValue(e.UnitType, out var m)
                         ? $"({m[0].X},{m[0].Y})" : "KEINER";
            var off = TurretOffset(e.UnitType, e.Col, e.Row);
            var hull = GetHullTexture(e.UnitType, e.Facing, PoseOf(e), SlopeClassOf(e.Col, e.Row));
            var turr = GetTurretTexture(e.Weapon, e.Facing, SlopeClassOf(e.Col, e.Row));
            var hr = UsedRect(hull);
            var tr = UsedRect(turr);
            sb.Append($"   {e.UnitType}/Bauteil {comp} Var {e.ShipVariant} Waffe {e.Weapon}")
              .Append($" Blick {e.Facing} Zelle ({e.Col},{e.Row}) Flag {flag}")
              .Append($" Montage {mnt} Versatz ({off.X},{off.Y})px");
            if (hr.HasValue && tr.HasValue)
            {
                var h = hr.Value; var t = tr.Value;
                float hx = h.Position.X + h.Size.X / 2f, hy = h.Position.Y + h.Size.Y / 2f;
                float tx = t.Position.X + t.Size.X / 2f + off.X;
                float ty = t.Position.Y + t.Size.Y / 2f + off.Y;
                // ⚠ Semikolon als Trenner, nicht Komma: die Zahlen tragen im
                // deutschen Gebietsschema selbst ein Komma.
                sb.Append($" | Rumpfbild {hull!.GetWidth()}x{hull.GetHeight()}")
                  .Append($" sichtbar x{h.Position.X}..{h.Position.X + h.Size.X}")
                  .Append($" Mitte ({hx:0.0}; {hy:0.0})")
                  .Append($" Waffenmitte ({tx:0.0}; {ty:0.0})")
                  .Append($" KLAFFEN ({hx - tx:0.0}; {hy - ty:0.0})px")
                  .Append($" = ({(hx - tx) / TileW:0.00}; {(hy - ty) / TileH:0.00}) Zellen");
                // beruehren sich die Flaechen ueberhaupt?
                var tShift = new Rect2I(t.Position + new Vector2I((int)off.X, (int)off.Y), t.Size);
                sb.Append(h.Intersects(tShift) ? " ueberlappt" : " KEINE UEBERLAPPUNG");
            }
            else sb.Append(" | kein Bildpaar");
            sb.Append('\n');
        }
        sb.Append($"   {ships} Schiffe auf dieser Karte, {lines} verschiedene Faelle gemeldet");
        return sb.ToString();
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
            // NoStructure: der Satz existiert fuer die Skripte, aber nicht auf
            // dem Bildschirm — er darf sich auch nicht anwaehlen lassen.
            if (!_entities[i].Dead && !_entities[i].NoStructure &&
                BodyRect(_entities[i]).HasPoint(p) && _entities[i].Row > bestRow)
            { best = i; bestRow = _entities[i].Row; }
        return best;
    }

    /// <summary>Das Flugzeug unter einem Punkt, oder −1. Ein Flugzeug im Hangar
    /// ist nicht auf dem Bildschirm und lässt sich deshalb auch nicht anklicken
    /// — wie eine <c>NoStructure</c> bei <see cref="Pick"/>.
    ///
    /// <para>⚠ UNSERE SETZUNG ist allein die GRÖSSE des Klickfelds: eine Zelle
    /// (40×20) um den Punkt, an dem der Rumpf gezeichnet wird
    /// (<see cref="AirShadowDrop"/> über dem Bodenschatten). Das Original hat
    /// für seine Trefferprüfung eine eigene Rechnung, die nicht gelesen ist;
    /// gelesen ist nur, DASS ein Flugzeug angewählt werden kann (Objektnummer
    /// 0x4E20 + Platz, 0x470C09).</para></summary>
    private int PickAir(Vector2 p)
    {
        int best = -1; float bd = float.MaxValue;
        for (int i = 0; i < _special.Count; i++)
        {
            var s = _special[i];
            if (s.Dead || s.Stored) continue;
            if (!AirRect(s).HasPoint(p)) continue;
            float d = p.DistanceTo(s.Pos - new Vector2(0, AirShadowDrop));
            if (d < bd) { bd = d; best = i; }
        }
        return best;
    }

    private static Rect2 AirRect(Special s)
    {
        var at = s.Pos - new Vector2(0, AirShadowDrop);
        return new Rect2(at - new Vector2(TileW / 2f, TileH / 2f),
                         new Vector2(TileW, TileH));
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
        // Ein angewaehltes FLUGZEUG: Name, Art, Huelle, Munition und Sprit —
        // dieselben Groessen, die der Block auch fuer eine Einheit zeigt. Ohne
        // diesen Zweig stuende neben dem Bild "KEINE AUSWAHL".
        if (_selAir >= 0 && _selAir < _special.Count)
        {
            var a = _special[_selAir];
            _panel.Visible = _panelTextOn;
            _panel.Text =
                $"{(a.Name.Length > 0 ? a.Name : a.TypeName).ToUpper()}\n" +
                $"{a.KindName.ToUpper()}\n" +
                $"{OwnerWord(a.Owner)}\n" +
                $"HP {a.Hp}/{a.HpMax}\n" +
                (a.IsSupply ? $"NUTZLAST {a.Cargo}/{SupplyCargoFull}"
                            : $"MUN {a.Ammo}/{a.AmmoMax} SPRIT {a.Fuel}");
            return;
        }
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
                 : IsUnitPlant(e) ? MenuPick(e).ToUpper()
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
        //
        // Die STRECKE laeuft in demselben Durchgang mit, Zeile fuer Zeile: sie
        // gehoert unter die Gebaeude, nicht darueber (siehe DrawRailUpTo).
        DrawRailAndBuildings();

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
                // ⚠ Die Hangklasse gilt fuer BEIDE. Der Turmsitz wurde schon
                // immer danach gerueckt, das Bild aber nicht — der Rumpf blieb
                // flach auf kippendem Boden.
                int slope = SlopeClassOf(e.Col, e.Row);
                var hull = GetHullTexture(e.UnitType, e.Facing, PoseOf(e), slope);
                if (hull != null)
                {
                    DrawTexture(hull, baseC - ComposedAnchor);
                    var turret = GetTurretTexture(e.Weapon, aim, slope);
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
                // ⚠ 15.08.2026 — HIER WURDE EIN SCHATTEN ALS ROTOR GEZEICHNET.
                //
                // Es stand `GetAirframeTexture(_clock*20 % 2 < 1 ? 110 : 111,
                // s.Facing)`, also ein Wechsel zwischen Teil 110 und 111 als
                // zwei Rotorphasen. Teil 111 ist aber KEINE Rotorphase: seine
                // acht Bilder sind zu 100 % ein einziger Palettenindex —
                // schwarze Rotorblätter, die SCHATTENMASKE des Rotors. Der
                // Hubschrauber hat also mit 10 Hz zwischen Rotor und schwarzem
                // Kreuz geblinkt.
                //
                // Und die acht Bilder von Teil 110 sind PHASEN, keine
                // Richtungen — angesehen: die Blätter drehen sich durch die
                // Reihe. Mit `s.Facing` stand der Rotor je Kurs auf einer
                // festen Phase und drehte sich gar nicht.
                //
                // ⚠ UNSERE SETZUNG bleibt die Drehzahl; im Original ist sie
                // nicht gelesen.
                int phase = Mathf.PosMod((int)(_clock * RotorFps), 8);
                var rsh = GetAirframeTexture(111, phase);
                if (rsh != null) DrawTexture(rsh, c - ComposedAnchor, ShadowTint);
                var rot = GetAirframeTexture(110, phase);
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

        // das angewaehlte Flugzeug bekommt seinen Rahmen wie eine Einheit
        if (_selAir >= 0 && _selAir < _special.Count && !_special[_selAir].Stored)
            DrawRect(AirRect(_special[_selAir]), new Color(0.3f, 1f, 0.3f, 0.9f),
                     false, 2f);

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
