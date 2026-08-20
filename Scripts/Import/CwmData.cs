namespace AkteEuropaReborn.Import;

using System;
using System.Collections.Generic;
using System.Text;

/// <summary>
/// The structures a map file carries, read out of the decoded sections.
///
/// Field offsets all come from the reverse engineering documented in
/// GAMESTATE_RE.md and are the corrected ones — in particular the building
/// record starts at 76*k with its position in the first four bytes, not four
/// bytes later, which is what the runtime does (base 0xc06910).
/// </summary>
public static class CwmData
{
    public const int BuildingStride = 76;
    public const int EntityStride = 78;
    public const int EntitiesPerPlayer = 1000;

    public sealed class Building
    {
        public int Slot, Type, Owner, CisTyp, Rail, Doors, Ident;
        public int Col, Row, Hp, HpMax;
        public int StockW, StockF, StockS, Terranium;
        public string Name = "";

        /// <summary>Record +0x18, and it is the flag that says "this entry is a
        /// real building". The building tick @0x43CB29 refuses to do anything
        /// without it. Measured over the 893 sec3 entries of the 23 levels: it
        /// is 1 for every one of the 684 entries of type 1..16 and 0 for every
        /// entry of type 17 and up — 0 counterexamples. The types above 16 are
        /// the map's script placeholders and scenery records.</summary>
        public int IsBuilt;

        /// <summary>The doors, as cell offsets from <see cref="Col"/>/<see
        /// cref="Row"/>. <see cref="Doors"/> of them, <b>three bytes each from
        /// +0x35</b>: column offset, row offset, and a state byte.
        ///
        /// ⚠ CORRECTED 2026-08-06 (second reading, same day). The first reading
        /// took +0x35/+0x36 for "the door" and +0x37/+0x38 for a second pair —
        /// which fitted the numbers by accident, because +0x37 is door 0's
        /// state (0 everywhere) and +0x38 is door 1's column. The building draw
        /// @0x42B274 settles the stride: <c>byte[esi + 3*i + 0xc06947]</c>, and
        /// the door tick @0x43D38C steps <c>edx</c> by 3.
        ///
        /// Constant per type over all 798 doors of the 23 levels: Basis (1) one
        /// door at (4,2) 73 of 73; the three factories (2/3/4) <b>two</b>, at
        /// (2,3) and (5,3), 99/91/68 of each; Kaserne (6) (2,3) 12 of 12;
        /// Flughafen (9) (5,4) 39 of 39; Mine (10) (5,3) 49 of 49; type 12
        /// (1,3) 36 of 36; type 15 (2,4) 10 of 10; Werft (16) (2,3) 13 of 13.
        ///
        /// A door tile is a tile OF the building — inside the footprint 121 of
        /// 121 where one is known — and the imap leaves it free (511 of 519),
        /// because it is where units come out: the production spawn writes the
        /// new unit's cell as <c>col + door_col</c>, <c>row + door_row</c>
        /// (@0x410441..0x41047C).
        ///
        /// The third byte is the door's own STATE and is <b>0 on all 798</b>,
        /// so it is a runtime field and not exported. Its machine is at
        /// @0x43D2EC: at 0 (shut) the door starts opening the moment the imap
        /// cell holds anything below 14000 — that is, a unit stands in it — and
        /// from 0x84 (open) it starts shutting again when the cell is empty.
        /// That is the behaviour the player describes: the doors are shut and
        /// open when something drives through.</summary>
        public readonly List<(int Col, int Row)> DoorCells = new();

        /// <summary>Door 0 — the one the capture block uses.</summary>
        public int DoorCol => DoorCells.Count > 0 ? DoorCells[0].Col : 0;
        public int DoorRow => DoorCells.Count > 0 ? DoorCells[0].Row : 0;

        /// <summary>The ground the building actually covers, in cells, relative
        /// to <see cref="Col"/>/<see cref="Row"/> — which is its top-left anchor.
        ///
        /// There is no size field in the 76-byte record. The footprint comes
        /// from the spatial grid (sec6) instead: every building holds a handle
        /// of its own there, and the cells carrying that handle ARE its ground.
        /// Measured that way over 345 buildings on 30 maps the size is constant
        /// per type — type 1 (Basis) 6x4 in 82 of 83 cases, types 8 and 12 3x4
        /// in 56 of 56 each, type 6 4x4, type 11 4x5, type 7 3x3 — so this is
        /// read per building rather than tabulated, and stays right wherever a
        /// map disagrees with its type.
        ///
        /// 0 = the grid had nothing to say (a record that is not really a
        /// building; the sec3 list carries 440 such entries with owner 255).
        /// </summary>
        public int FootW, FootH;

        // from the per-type instance record, so only where the type has one
        public int? Shipyard;             // typ 11: its Schiffswerft
        public int? State;
        public string? StateName;
        public int? EffNum, EffDen, ProdSpeed, UpgradeStep, Capacity;
        public int? CostStore, CostProd;  // both signed: the panel prints them
        public int? HangarSize;           // typ 9
        public List<int>? Hangar;
    }

    public sealed class Entity
    {
        public int Slot, PlayerBlock, Col, Row, Facing, Aim, Kind, GameUnitType;
        public int UnitType, Attack, Energie, EnergieMax;
        /// <summary>+0x24, the constant 0x2710 = the object-code base of dir2,
        /// and +0x2e/+0x30 — the FUEL TANK.
        ///
        /// <para>⚠ Bis zum 16.08.2026 hiessen die beiden hier <c>Hp</c>/
        /// <c>HpMax</c>, »unter den Namen, die die exportierten Daten immer
        /// hatten, damit die Datei vergleichbar bleibt«. Die Begruendung war
        /// falsch herum: die Vergleichbarkeit einer Datei ist nichts wert, wenn
        /// ihr Schluessel das Falsche behauptet. Die Korrektur vom 26.07.2026
        /// steht seit je im Kopf von <c>MapEntityLayer.Load</c> — das LEBEN
        /// einer Einheit ist <c>energie</c> (+0x08, max +0x29), und +0x2e/+0x30
        /// zaehlt die Bewegung herunter (»no fuel« @0x407ab8). Der alte Name hat
        /// in der Sitzung vom 15.08. eine halbe Stunde gekostet und im
        /// Rueckfall von <c>Load</c> jeder Einheit ein Leben in Hoehe ihres
        /// Tankinhalts gegeben.</para></summary>
        public int CodeBase, Fuel, FuelMax;
        public byte[] Raw = Array.Empty<byte>();

        /// <summary>How many cells the unit really covers, taken from the imap:
        /// the cells that carry its own slot number ARE its body. Most units are
        /// 1x1; the ships on map_01 are 2x2, and drawing them on the middle of
        /// their ANCHOR cell instead of the middle of their body was what put
        /// them half a tile inland. 0 when the imap holds no cell for it.</summary>
        public int FootW, FootH;

        /// <summary>The owner is the slot BLOCK, not a field: the hostility
        /// tests (@0x409bde, @0x40d058) and the sprite draw (@0x42a825) all
        /// derive the player as `index / 1000`.</summary>
        public int Owner => Slot / EntitiesPerPlayer;
    }

    private static string Name(byte[] r, int at, int len) => Cp437.GetString(r, at, len);

    /// <summary>Every building carries `cis_typ` at +0x19 — its running number
    /// within its type — and that number indexes a per-type array of 50
    /// instances, each of which is a section of its own. The dispatcher
    /// @0x43dc34 proves the mapping: `idx = idxtbl[typ-1]` (@0x43ecd8) then
    /// `jmp jmptbl[idx]` (@0x43ecac), and each handler addresses exactly one
    /// section. Common head: +0x00 u16 backlink, +0x02 status — except sec29,
    /// where +0x02 is the Schiffswerft that pays for the dock's ships.</summary>
    /// <remarks>⚠ Am 14.08.2026 von <c>private</c> auf <c>public</c> gesetzt.
    /// Der Rückweg des Karteneditors (<c>MapOpen</c>) braucht dieselbe Zuordnung,
    /// und weil sie verschlossen war, führte er sie ein zweites Mal — zwei
    /// Abschriften einer gemessenen Tafel, die stillschweigend auseinanderlaufen,
    /// sobald jemand eine davon berichtigt. Lieber öffentlich als doppelt.</remarks>
    public static readonly Dictionary<int, (int Section, int Stride)> InstanceSection = new()
    {
        { 1, (23, 16) },  { 2, (24, 14) },  { 3, (24, 14) },  { 4, (24, 14) },
        { 6, (30, 14) },  { 7, (26, 4) },   { 9, (27, 52) },  { 10, (28, 18) },
        { 11, (29, 4) },  { 12, (30, 14) }, { 13, (31, 4) },  { 15, (28, 18) },
    };

    /// <summary>The status words as the game prints them. The Basis panel maps
    /// 0..3 through the jump table @0x46b60c; the factory tick @0x43deaf takes
    /// 0..4 and its table @0x43ecec sends 1 to nothing ("Status : angehalten",
    /// @0x501bc4), 2 to repair, 3 to Lagerausbau, 4 to Produktionserweiterung.
    /// </summary>
    private static readonly string[] StatusBase = { "aktiv", "reparieren", "vergroessern", "forschen" };
    private static readonly string[] StatusFactory =
        { "aktiv", "angehalten", "reparieren", "vergroessern", "produktionserweiterung" };

    /// <summary>The per-type instance record of a building, or null.</summary>
    private static byte[]? Instance(CwmFile m, int typ, int cis)
    {
        if (cis > 49 || !InstanceSection.TryGetValue(typ, out var e)) return null;
        var s = m.Sec(e.Section);
        if (s == null || (cis + 1) * e.Stride > s.Length) return null;
        return CwmExtra.Slice(s, cis * e.Stride, e.Stride);
    }

    /// <summary>sec3 — 300 records of 76 bytes.</summary>
    public static List<Building> Buildings(CwmFile m)
    {
        var list = new List<Building>();
        var s3 = m.Sec(3);
        if (s3 == null) return list;
        for (int k = 0; k + BuildingStride <= s3.Length; k += BuildingStride)
        {
            string nm = Name(s3, k + 0x1b, 0x11);
            // ⚠⚠ 20.08.2026 — GEFUNDEN VOM PRUEFSTAND `game.007`, und ohne den
            // waere es nie aufgefallen.
            //
            // Hier stand nur `typ == 0 && Name leer`. Auf einer KARTE reicht
            // das: ein leerer Satz traegt dort weder Typ noch Namen. Ein
            // SPIELSTAND des Originals schreibt seinen leeren Saetzen aber
            // einen Namen — die Platznummer als Text (»5 «, »6 «, »7 « …).
            // Damit kamen aus `game.007` **252 leere Gebaeude** durch: 256
            // statt 4, und 252 davon mit Typ 0 und 0 Trefferpunkten.
            //
            // GEMESSEN ueber alle Karten, alle .DM und game.007:
            //   heute durch und echt      3764
            //   heute durch, aber LEER     252   <- alle aus game.007
            //   heute abgewiesen, echt      70   <- Typ 0, kein Name, aber TP
            //   heute abgewiesen und leer 18714
            //
            // ⚠ Deshalb wird die Pruefung STRENGER und nicht ANDERS: haenge man
            // sie allein an `hp_max`, kaemen die 70 neu herein, und ob die echt
            // sind, ist ungelesen — das Original nennt einen Satz mit Typ 0
            // woertlich »kein Gebaeude« (`obj_owner` @0x4D076D gibt dafuer 12
            // zurueck). Ein Satz ohne Typ UND ohne Trefferpunkte ist dagegen
            // unter jeder Lesart leer.
            if (s3[k + 0x04] == 0 &&
                (nm.Length == 0 || BitConverter.ToUInt16(s3, k + 0x16) == 0)) continue;
            var b = new Building
            {
                Slot = k / BuildingStride,
                Col = BitConverter.ToUInt16(s3, k + 0x00),
                Row = BitConverter.ToUInt16(s3, k + 0x02),
                Type = s3[k + 0x04],
                Owner = s3[k + 0x05],
                Hp = BitConverter.ToUInt16(s3, k + 0x06),
                HpMax = BitConverter.ToUInt16(s3, k + 0x16),
                CisTyp = s3[k + 0x19],
                Rail = s3[k + 0x1a],
                Name = nm,
                StockW = BitConverter.ToUInt16(s3, k + 0x2c),
                StockF = BitConverter.ToUInt16(s3, k + 0x2e),
                StockS = BitConverter.ToUInt16(s3, k + 0x30),
                Terranium = BitConverter.ToUInt16(s3, k + 0x32),
                // +0x34 doors: the second gate of the capture block, and a
                // COUNT — 240 buildings carry 1 and 279 carry 2, constant per
                // type. 0 = cannot be taken: types 8, 11 (Hafen), 13, 14 and
                // everything from 17 up. The doors themselves follow at +0x35,
                // three bytes each; see Building.DoorCells.
                Doors = s3[k + 0x34],
                IsBuilt = s3[k + 0x18],
                Ident = s3[k + 0x41],
            };
            // the doors, three bytes each from +0x35 (see Building.DoorCells)
            for (int d = 0; d < b.Doors && k + 0x35 + 3 * d + 2 < s3.Length; d++)
                b.DoorCells.Add((s3[k + 0x35 + 3 * d], s3[k + 0x36 + 3 * d]));

            Footprint(m, b);

            var inst = Instance(m, b.Type, b.CisTyp);
            if (inst != null && b.Type == 11)
            {
                // sec29: +0x00 backlink to this dock, +0x02 the Schiffswerft
                // (typ 16) that pays for and enables ship production
                b.Shipyard = inst[0x02];
            }
            else if (inst != null)
            {
                int st = inst[0x02];
                b.State = st;
                bool fact = b.Type is 2 or 3 or 4;
                bool mine = b.Type is 10 or 15;
                var words = fact ? StatusFactory : StatusBase;
                b.StateName = st < words.Length ? words[st] : st.ToString();
                if (mine)
                {
                    // sec28, 18 Byte je Mine, im Spiel die Platte 0x878AD0.
                    // Dieselbe Kopfform wie sec24: +0x03/+0x04 Zaehler und
                    // Nenner der Foerderchance (@0x43E57F), +0x05 die
                    // AUSBAUSTUFE — Index in die Periodentabelle 0x4FACB8
                    // (@0x43E5D4) und zugleich die TORKACHEL (@0x42B31F),
                    // +0x06 der 0..100-Fortschritt einer Erweiterung,
                    // +0x08 der Lagerplatz.
                    b.EffNum = inst[0x03];
                    b.EffDen = inst[0x04];
                    b.ProdSpeed = inst[0x05];
                    b.UpgradeStep = inst[0x06];
                    b.Capacity = BitConverter.ToUInt16(inst, 0x08);
                    // spiegelbildlich zur Fabrik, nur zwei Felder weiter: der
                    // Missionsvorlauf @0x440887 setzt bei der Fabrik +0x0a/+0x0c
                    // und bei der Mine +0x0e/+0x10 mit denselben zwei Zahlen.
                    b.CostStore = BitConverter.ToInt16(inst, 0x0e);
                    b.CostProd = BitConverter.ToInt16(inst, 0x10);
                }
                if (fact)
                {
                    // sec24: +0x03/+0x04 production chance numerator and
                    // denominator (@0x43df33), +0x05 the Produktionsgeschwindig-
                    // keit the panel prints (@0x46f748), +0x06 the 0..100
                    // upgrade progress, +0x08 Lagerplatz (+10 per Lagerausbau,
                    // @0x43e0f1), +0x0a Lagerausbaukosten and +0x0c Produktions-
                    // erweiterungskosten, both *3/2 per upgrade.
                    b.EffNum = inst[0x03];
                    b.EffDen = inst[0x04];
                    b.ProdSpeed = inst[0x05];
                    b.UpgradeStep = inst[0x06];
                    b.Capacity = BitConverter.ToUInt16(inst, 0x08);
                    b.CostStore = BitConverter.ToInt16(inst, 0x0a);
                    b.CostProd = BitConverter.ToInt16(inst, 0x0c);
                }
            }
            list.Add(b);
        }
        Hangars(m, list);
        return list;
    }

    /// <summary>Resolve every Flughafen's hangar list (sec27 +0x0b, its length
    /// at +0x04) into sec19 slots. Only aircraft count — the game filters the
    /// same way (@0x4289d8).</summary>
    private static void Hangars(CwmFile m, List<Building> list)
    {
        var s27 = m.Sec(27);
        var s19 = m.Sec(19);
        if (s27 == null || s19 == null) return;
        foreach (var d in list)
        {
            if (d.Type != 9 || d.CisTyp > 49) continue;
            int a = d.CisTyp * 52;
            if (a + 52 > s27.Length) continue;
            d.HangarSize = s27[a + 0x03];        // +2 per Flughafen-Erweiterung
            d.Hangar = new List<int>();
            int n = Math.Min((int)s27[a + 0x04], 52 - 0x0b);
            for (int k = 0; k < n; k++)
            {
                int v = s27[a + 0x0b + k];
                if (v == 0xFF) continue;
                int o = v * CwmExtra.SpecialStride;
                if (o + CwmExtra.SpecialStride > s19.Length) continue;
                if (CwmExtra.AllZero(s19, o, CwmExtra.SpecialStride)) continue;
                int kind = s19[o + 0x08];
                if (kind != 1 && kind != 2 && kind != 10) continue;
                d.Hangar.Add(v);
            }
        }
    }

    // ---- sec4: the map markers ---------------------------------------------

    /// <summary>sec4 — 2000 slots of 6 (dest 0xc03a30). The loader counts the
    /// non-empty ones (+2 != 0xFF) into 0x539d88 (@0x41f2d2) and stamps the
    /// cell into the sec6 grid as col*256+row (@0x40d583), exactly like an
    /// entity. Types 0x70..0x74 are the per-player start markers.</summary>
    public sealed class Marker
    {
        public int Slot, Col, Row, Type;
        public int[] Extra = Array.Empty<int>();
        public byte[] Raw = Array.Empty<byte>();
    }

    public const int MarkerStride = 6;

    public static List<Marker> Markers(CwmFile m)
    {
        var list = new List<Marker>();
        var s = m.Sec(4);
        if (s == null) return list;
        for (int o = 0; o + MarkerStride <= s.Length; o += MarkerStride)
        {
            if (s[o + 2] == 0xFF) continue;
            list.Add(new Marker
            {
                Slot = o / MarkerStride, Col = s[o], Row = s[o + 1], Type = s[o + 2],
                Extra = new[] { (int)s[o + 3], s[o + 4], s[o + 5] },
                Raw = CwmExtra.Slice(s, o, MarkerStride),
            });
        }
        return list;
    }

    // ---- sec2: the movement / zone grid -------------------------------------

    /// <summary>sec2 — 257x257 bytes (dest 0xa3aeb0), indexed with a 257-wide
    /// stride that runs along the COLUMN (accessor @0x4211a0: idx = c*256 + c).
    ///
    /// Class 0 is impassable — every water tile in all 23 maps, 0
    /// counter-examples — 1 shore/sand, 2 open land, 3 special land. Read
    /// row-major (the earlier bug) the classes shuffle into noise, which is
    /// what made the section look meaningless before.</summary>
    public const int ZoneStride = 257;

    public sealed class ZoneGrid
    {
        public int Stride = ZoneStride, Width, Height;
        /// <summary>Value and count, in order of first encounter.</summary>
        public List<(int Value, int Count)> Histogram = new();
        public byte[][] Grid = Array.Empty<byte[]>();     // [row][col]
    }

    public static ZoneGrid Zones(CwmFile m)
    {
        var z = new ZoneGrid { Width = m.Width, Height = m.Height };
        var s = m.Sec(2);
        if (s == null) return z;
        z.Grid = new byte[m.Height][];
        var seen = new Dictionary<int, int>();
        var order = new List<int>();
        for (int r = 0; r < m.Height; r++)
        {
            var row = new byte[m.Width];
            for (int c = 0; c < m.Width; c++)
            {
                int i = c * ZoneStride + r;
                byte v = i < s.Length ? s[i] : (byte)0;
                row[c] = v;
                if (!seen.ContainsKey(v)) { seen[v] = 0; order.Add(v); }
                seen[v]++;
            }
            z.Grid[r] = row;
        }
        foreach (int v in order) z.Histogram.Add((v, seen[v]));
        return z;
    }

    // ---- sec6: the spatial handle grid --------------------------------------

    /// <summary>sec6 — 256x256 u16, column major (dest 0xbdea80), linear index
    /// col*256 + row (@0x41f345). The game calls it <b>imap</b> (its own debug
    /// line `imap[rx+px][ry+py]:` @0x4f66b4) and it is the map every movement
    /// question is asked of — see <see cref="Terrain"/> for the reading.
    ///
    /// A cell uses four handle spaces: below 8000 a live entity slot (stamped
    /// @0x41f347), 10000..13999 an infantry CELL (index into the 4000 x 22
    /// records at 0x7847e8, cf. `sub di,0x2710` @0x4129a8), from 50000 a static
    /// object handle, and the three top values as terrain.
    ///
    /// ⚠ The older note here — "0xFFFC is empty, 0xFFFD..0xFFFF are sentinels" —
    /// was wrong end about. <b>0xFFFE is the empty cell</b>; 0xFFFC is water and
    /// 0xFFFD is rough ground. Read out of Can_go @0x4055D0, see
    /// <see cref="Simulation.NavGrid"/>.</summary>
    public sealed class SpatialCell
    {
        public int Col, Row, Value, Ref;
        public string RefKind = "other";
    }

    public sealed class SpatialGrid
    {
        public int Stride = 256;
        public string Order = "col-major";
        public List<SpatialCell> NonEmpty = new();
        public List<(int Value, int Count)> TopValues = new();
    }

    /// <summary>Fills a building's footprint from the spatial grid: find the
    /// handle sitting on its anchor cell, then take the extent of every cell
    /// carrying that handle. Buildings hold handles from 50000 up; anything
    /// else on the anchor means this record is not a building on the ground.
    /// </summary>
    private static void Footprint(CwmFile m, Building b)
    {
        var s = m.Sec(6);
        if (s == null) return;
        int n = s.Length / 2;
        int at = b.Col * 256 + b.Row;
        if (at < 0 || at >= n) return;
        int handle = BitConverter.ToUInt16(s, at * 2);
        if (handle < 50000 || handle >= 0xFFFC) return;

        int x0 = int.MaxValue, y0 = int.MaxValue, x1 = int.MinValue, y1 = int.MinValue;
        for (int col = 0; col < m.Width; col++)
            for (int row = 0; row < m.Height; row++)
            {
                int i = col * 256 + row;
                if (i >= n || BitConverter.ToUInt16(s, i * 2) != handle) continue;
                if (col < x0) x0 = col;
                if (col > x1) x1 = col;
                if (row < y0) y0 = row;
                if (row > y1) y1 = row;
            }
        if (x1 < x0) return;
        b.FootW = x1 - x0 + 1;
        b.FootH = y1 - y0 + 1;
    }

    public static SpatialGrid Spatial(CwmFile m)
    {
        var g = new SpatialGrid();
        var s = m.Sec(6);
        if (s == null) return g;
        int n = s.Length / 2;
        for (int col = 0; col < m.Width; col++)
            for (int row = 0; row < m.Height; row++)
            {
                int i = col * 256 + row;
                if (i >= n) continue;
                int v = BitConverter.ToUInt16(s, i * 2);
                if (v >= 0xFFFC) continue;                 // empty and sentinels
                var (kind, r) = v < 8000 ? ("entity", v)
                    : v is >= 10000 and < 12000 ? ("object", v - 10000)
                    : v >= 50000 ? ("static", v - 50000) : ("other", v);
                g.NonEmpty.Add(new SpatialCell { Col = col, Row = row, Value = v, RefKind = kind, Ref = r });
            }

        // the six most common values over the whole grid, ties by first
        // encounter — the same order Python's Counter.most_common gives
        var count = new Dictionary<int, int>();
        var order = new List<int>();
        for (int i = 0; i < n; i++)
        {
            int v = BitConverter.ToUInt16(s, i * 2);
            if (!count.ContainsKey(v)) { count[v] = 0; order.Add(v); }
            count[v]++;
        }
        var idx = new Dictionary<int, int>();
        for (int i = 0; i < order.Count; i++) idx[order[i]] = i;
        order.Sort((a, b) => count[b] != count[a] ? count[b] - count[a] : idx[a] - idx[b]);
        for (int i = 0; i < order.Count && i < 6; i++) g.TopValues.Add((order[i], count[order[i]]));
        return g;
    }

    // ---- the terrain the game itself moves on -------------------------------

    /// <summary>The four terrain classes Can_go @0x4055D0 distinguishes, in the
    /// order the numbers are written to the export.</summary>
    public enum Ground : byte { Free = 0, Rough = 1, Water = 2, Blocked = 3 }

    public sealed class TerrainGrid
    {
        public int Width, Height;
        public byte[] Cells = Array.Empty<byte>();      // row major, <see cref="Ground"/>
        public int[] Histogram = new int[4];
        /// <summary>Cells whose imap value was a stamped occupant, so the ground
        /// under them had to be inferred — see <see cref="Terrain"/>.</summary>
        public int Inferred;
        /// <summary>imap values that fell through every case, if any.</summary>
        public List<int> Unknown = new();
    }

    /// <summary>
    /// The passability map, straight out of the imap (sec6) and the rules
    /// <c>Can_go</c> @0x4055D0 applies to it. Three of its values are terrain:
    /// <c>0xFFFE</c> free, <c>0xFFFD</c> rough, <c>0xFFFC</c> water. Everything
    /// from 14000 up (the static object handles, 50000+) and 0xFFFF walk into
    /// the routine's own `nothing` path @0x405586 and block.
    ///
    /// Proven against the file rather than argued: over maps 01, 02, 05, 14 and
    /// NET07, <b>0xFFFC sits on a water tile (ground code &lt;= 7) every single
    /// time and on no land tile at all</b> — 463, 519, 2056, 7190 and 6452 cells,
    /// no counter-example. And the objects that are baked into the map picture
    /// are mostly <b>0xFFFE, that is free</b> (02: 498 cells, 14: 512, NET07:
    /// 312) — bridges and roads among them; the ones that really block carry a
    /// static handle instead. Blocking a cell because a prop is drawn on it, the
    /// way this project used to, is what made bridges impassable.
    ///
    /// OURS, and only this: a cell that already carries an occupant (a unit slot
    /// below 8000, or an infantry cell 10000..13999) does not say what is under
    /// it. The occupant does — a ship stands on water, everything else on free
    /// ground. It is a handful of cells per map (map_01: 30 of 3024) and the
    /// count is reported as <see cref="TerrainGrid.Inferred"/>.
    /// </summary>
    public static TerrainGrid Terrain(CwmFile m, List<Entity> entities)
    {
        var g = new TerrainGrid { Width = m.Width, Height = m.Height };
        var s = m.Sec(6);
        if (s == null || m.Width <= 0 || m.Height <= 0) return g;
        int n = s.Length / 2;

        // which slots are naval — the original asks entity +0x0a (our GameUnitType),
        // case 4 of the jump table @0x40678c, whose only terrain test is 0xFFFC
        var naval = new HashSet<int>();
        foreach (var e in entities) if (e.GameUnitType is 4 or 5) naval.Add(e.Slot);

        g.Cells = new byte[m.Width * m.Height];
        var unknown = new HashSet<int>();
        for (int row = 0; row < m.Height; row++)
            for (int col = 0; col < m.Width; col++)
            {
                int i = col * 256 + row;
                int v = i < n ? BitConverter.ToUInt16(s, i * 2) : 0xFFFF;
                Ground gr;
                if (v == 0xFFFE) gr = Ground.Free;
                else if (v == 0xFFFD) gr = Ground.Rough;
                else if (v == 0xFFFC) gr = Ground.Water;
                else if (v < 8000) { gr = naval.Contains(v) ? Ground.Water : Ground.Free; g.Inferred++; }
                else if (v is >= 10000 and < 14000) { gr = Ground.Free; g.Inferred++; }
                else { gr = Ground.Blocked; if (v is not 0xFFFF && v < 50000) unknown.Add(v); }
                g.Cells[row * m.Width + col] = (byte)gr;
                g.Histogram[(int)gr]++;
            }
        g.Unknown.AddRange(unknown);
        g.Unknown.Sort();
        return g;
    }

    /// <summary>sec16 — the infantry cells the imap points at: 4000 records of
    /// 22 (dest 0x7847e8), `col, row, then nine u16 unit slots`. The stride and
    /// the count come from the two routines that walk them: `pratelska_infa`
    /// @0x433fe0 (`lea ebx,[eax*2 + 0x7847ec]` over 11*index, nine slots) and
    /// `prejet` @0x412980. Only the cells the imap names are live, which is a
    /// few dozen per map.</summary>
    public sealed class InfantryCell
    {
        public int Index, Col, Row;
        public List<int> Slots = new();
    }

    public static List<InfantryCell> InfantryCells(CwmFile m)
    {
        var list = new List<InfantryCell>();
        var imap = m.Sec(6);
        var inf = m.Sec(16);
        if (imap == null || inf == null) return list;
        int n = imap.Length / 2;

        var wanted = new SortedSet<int>();
        for (int col = 0; col < m.Width; col++)
            for (int row = 0; row < m.Height; row++)
            {
                int i = col * 256 + row;
                if (i >= n) continue;
                int v = BitConverter.ToUInt16(imap, i * 2);
                if (v is >= 10000 and < 14000) wanted.Add(v - 10000);
            }

        foreach (int idx in wanted)
        {
            int o = idx * 22;
            if (o + 22 > inf.Length) continue;
            var c = new InfantryCell { Index = idx, Col = inf[o], Row = inf[o + 1] };
            for (int k = 0; k < 9; k++)
            {
                int slot = BitConverter.ToUInt16(inf, o + 2 + k * 2);
                if (slot < 8000) c.Slots.Add(slot);       // the game's own test @0x4129cd
            }
            list.Add(c);
        }
        return list;
    }

    /// <summary>sec5 — 8000 records of 78, eight blocks of 1000, one per
    /// player: the block a slot sits in IS its owner (`slot / 1000`).</summary>
    public static List<Entity> Entities(CwmFile m)
    {
        var list = new List<Entity>();
        var s5 = m.Sec(5);
        if (s5 == null) return list;
        int n = s5.Length / EntityStride;
        for (int i = 0; i < n; i++)
        {
            int o = i * EntityStride;
            if (s5[o + 0x09] == 0xFF) continue;          // free slot
            var raw = new byte[EntityStride];
            Array.Copy(s5, o, raw, 0, EntityStride);
            list.Add(new Entity
            {
                Slot = i,
                PlayerBlock = i / EntitiesPerPlayer,
                Col = raw[0x00],
                Row = raw[0x01],
                // the file's own bytes; the &7 the drawing code applies
                // (@0x42a89c) belongs to the consumer, not to the decode
                Facing = raw[0x02],
                Aim = raw[0x03],
                Kind = raw[0x09],
                GameUnitType = raw[0x0a],
                UnitType = raw[0x0f],
                Attack = raw[0x26],
                Energie = raw[0x08],
                EnergieMax = raw[0x29],
                CodeBase = BitConverter.ToUInt16(raw, 0x24),
                Fuel = BitConverter.ToUInt16(raw, 0x2e),
                FuelMax = BitConverter.ToUInt16(raw, 0x30),
                Raw = raw,
            });
        }
        UnitFootprints(m, list);
        return list;
    }

    /// <summary>
    /// The body of every placed unit, read off the imap: a unit is stamped into
    /// it under its own slot number (@0x41f347 writes `col*256 + row`), and a
    /// unit that covers more than one cell is stamped into all of them. So the
    /// cells carrying slot N ARE unit N.
    ///
    /// The three ships of map_01 are the case that made this necessary: slots
    /// 1004, 1005 and 1006 each hold FOUR cells — 1004 is (14,17) (14,18)
    /// (15,17) (15,18) — while the record names only the first. Drawn on the
    /// middle of that one cell a ship sits half a tile up and to the left of
    /// where it belongs, and at the water's edge that is the difference between
    /// floating and standing on the beach.
    /// </summary>
    private static void UnitFootprints(CwmFile m, List<Entity> list)
    {
        var s = m.Sec(6);
        if (s == null || list.Count == 0) return;
        int n = s.Length / 2;

        var bySlot = new Dictionary<int, (int X0, int Y0, int X1, int Y1)>();
        for (int col = 0; col < m.Width; col++)
            for (int row = 0; row < m.Height; row++)
            {
                int i = col * 256 + row;
                if (i >= n) continue;
                int v = BitConverter.ToUInt16(s, i * 2);
                if (v >= 8000) continue;                       // not a unit slot
                if (bySlot.TryGetValue(v, out var b))
                    bySlot[v] = (Math.Min(b.X0, col), Math.Min(b.Y0, row),
                                 Math.Max(b.X1, col), Math.Max(b.Y1, row));
                else bySlot[v] = (col, row, col, row);
            }

        foreach (var e in list)
            if (bySlot.TryGetValue(e.Slot, out var b))
            {
                e.FootW = b.X1 - b.X0 + 1;
                e.FootH = b.Y1 - b.Y0 + 1;
            }
    }
}
