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
        public int Slot, PlayerBlock, Col, Row, Facing, Aim, Kind, Subclass;
        public int UnitType, Category, Energie, EnergieMax;
        /// <summary>+0x24, the constant 0x2710 = the object-code base of dir2,
        /// and +0x2e/+0x30, which the fuel correction of 2026-07-26 showed to be
        /// the TANK rather than health — kept under the names the exported data
        /// has always used so the file stays comparable.</summary>
        public int CodeBase, Hp, HpMax;
        public byte[] Raw = Array.Empty<byte>();

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
    private static readonly Dictionary<int, (int Section, int Stride)> InstanceSection = new()
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
            if (s3[k + 0x04] == 0 && nm.Length == 0) continue;
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
                Doors = s3[k + 0x34],
                Ident = s3[k + 0x41],
            };
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
                var words = fact ? StatusFactory : StatusBase;
                b.StateName = st < words.Length ? words[st] : st.ToString();
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
    /// col*256 + row (@0x41f345). A cell uses three handle spaces: below 8000 a
    /// live entity slot (stamped @0x41f347), 10000..11999 an inline object code
    /// (dir2 frame = v-10000, cf. sub 0x2710 @0x411133), from 50000 a static
    /// object handle. 0xFFFC is empty, 0xFFFD..0xFFFF are sentinels.</summary>
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
                Subclass = raw[0x0a],
                UnitType = raw[0x0f],
                Category = raw[0x26],
                Energie = raw[0x08],
                EnergieMax = raw[0x29],
                CodeBase = BitConverter.ToUInt16(raw, 0x24),
                Hp = BitConverter.ToUInt16(raw, 0x2e),
                HpMax = BitConverter.ToUInt16(raw, 0x30),
                Raw = raw,
            });
        }
        return list;
    }
}
