namespace AkteEuropaReborn.Import;

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

/// <summary>
/// The tables that live inside GAME.EXE itself: component stats, the buildable
/// ship designs and the aircraft templates. They are authored static data in
/// the binary's .data section, so reading them needs a virtual address turned
/// into a file offset and nothing else.
///
/// Addresses and record layouts come from the reverse engineering documented in
/// UNIT_STATS_RE.md and GAMESTATE_RE.md.
/// </summary>
public sealed class ExeTables
{
    private readonly byte[] _d;
    private readonly List<(uint Va, uint VSize, uint Raw, uint RawSize)> _sections = new();

    public const uint ImageBase = 0x400000;

    // ---- the tables, by virtual address ------------------------------------

    /// <summary>Component stats: 58-byte records indexed by unit_type; the
    /// record starts at its u16 hp_max.</summary>
    public const uint StatsRecord0 = 0x5045ba;
    public const int StatsStride = 58;

    /// <summary>The stats array the map's sec46 overwrites — eight player
    /// blocks of 200 records; the tech threshold sits at +0x24 of a record
    /// counted from here.</summary>
    public const uint StatsArrayBase = 0x5045a0;

    /// <summary>Ten buildable ship designs, 42 bytes each (SHIP_PROD).</summary>
    public const uint ShipDesigns = 0x52eda0;
    public const int ShipStride = 42;

    /// <summary>Eight aircraft templates, 48 bytes each.</summary>
    public const uint AircraftTemplates = 0x51b021;
    public const int AircraftStride = 48;

    /// <summary>The order vocabulary, 40 entries of 30 bytes. Latin-1, not
    /// cp437 — the exe mixes the two, and this table is the odd one.</summary>
    public const uint OrderVocabulary = 0x4fd660;
    public const int OrderStride = 30, OrderCount = 40;

    /// <summary>Building type names, 16 entries of 20 bytes, cp437. The map's
    /// `typ` is 1-based, so the name of type n is entry n-1 — proven by the
    /// door count: the three factories (2,3,4) all have two doors, the HQ one.
    /// </summary>
    public const uint BuildingTypeNames = 0x4fdcc4;
    public const int BuildingTypeStride = 20, BuildingTypeCount = 16;

    /// <summary>The campaign's own mission names, 37 entries of 21 bytes.
    ///
    /// This table answers the question that stood open since the campaign
    /// script was read: which state belongs to which level. The map loader
    /// indexes it with the campaign counter itself (@0x41e25e):
    ///
    ///     movsx eax, word [0x539934]            ; the counter
    ///     lea   edx, [eax + eax*4]              ; 5*eax
    ///     lea   edi, [eax + edx*4 + 0x4f81c0]   ; 21*eax + table
    ///
    /// and the entries read "Test Name", "Mission 1" … "Mission 33", then the
    /// three extra ones Sumpfschlacht, Sandfalle and Waldesrauschen. So the
    /// counter IS the mission number — and the mission number is the level file
    /// number, which the saved games confirm from the other side: `1.DM` is
    /// called "Mission 26" and its elevation grid is level 26's.
    ///
    /// Found by a raw dword scan for the counter, not by an xref walk: the
    /// counter is read as a 16-bit variable and an xref pass never saw it.</summary>
    public const uint MissionNames = 0x4f81c0;
    public const int MissionNameStride = 21, MissionNameCount = 37;

    /// <summary>The fire-sound table: 22-byte records, and the first u16 of a
    /// record is the sound number a weapon of that class fires with.
    ///
    /// Read off the shooting code @0x40c4c0, which is the only thing that needs
    /// explaining here:
    /// <code>
    ///   mov cl, byte [edx*2 + 0x5045bc]   ; edx*2 = row*58, so this is stats+0x1c
    ///   lea edx, [ecx + ecx*4]            ; 5*cl
    ///   lea ecx, [ecx + edx*2]            ; 11*cl
    ///   mov di, word [ecx*2 + 0x4f98f2]   ; table[cl], stride 22
    ///   call rand ; and eax,1 ; add di, ax
    /// </code>
    /// So a weapon component names a <b>sound class</b> in its stats record at
    /// <b>+0x1c</b>, the class picks a row of this table, and the game plays the
    /// row's base number <b>or that plus one</b>, at random — two shots that
    /// never sound quite the same.
    ///
    /// <para>The bases come out 0, 2, 4, 6, 8, 10, 12, 14, 18, 22, 24, 26, 32 —
    /// even, two apart, and the sound bank's very first block is <b>exactly 40
    /// sounds</b> (0..39, all preloaded). Twenty classes of two. That the two
    /// counts meet is what makes this certain rather than likely.</para>
    ///
    /// <para>Two call sites reach into the same table with a fixed row instead
    /// of a component's class: @0x40c522 uses 0x4f9aaa and @0x40c55f uses
    /// 0x4f9ac0, which are rows 20 and 21 (0x4f9aaa - 0x4f98f2 = 20 x 22).</para>
    /// </summary>
    public const uint FireSoundTable = 0x4f98f2;
    public const int FireSoundStride = 22, FireSoundRows = 22;

    /// <summary>Where a component's sound class sits in its stats record.</summary>
    public const int StatsSoundClass = 0x1c;

    /// <summary>The base sound number of every class, by row.</summary>
    public int[] FireSounds()
    {
        var v = new int[FireSoundRows];
        for (int i = 0; i < FireSoundRows; i++)
        {
            var r = Read((uint)(FireSoundBase + i * FireSoundStride), 2);
            v[i] = r.Length < 2 ? -1 : r[0] | (r[1] << 8);
        }
        return v;
    }

    /// <summary>Rows 1..7 of the fire-sound table run 2, 4, 6, 8, 10, 12, 14 and
    /// rows 11..13 run 22, 24, 26 with row 16 at 32 — two apart because every
    /// class owns two sounds. Row 0 is left out of the test: it is 0, and a run
    /// of zeros would match it anywhere.</summary>
    private static readonly (int Row, int Base)[] FireFingerprint =
    {
        (1, 2), (2, 4), (3, 6), (4, 8), (5, 10), (6, 12), (7, 14),
        (11, 22), (12, 24), (13, 26), (16, 32),
    };

    private bool FireSoundsLookRight(uint va)
    {
        foreach (var (row, want) in FireFingerprint)
        {
            var r = Read((uint)(va + row * FireSoundStride), 2);
            if (r.Length < 2 || (r[0] | (r[1] << 8)) != want) return false;
        }
        return true;
    }

    /// <summary>The shape the game reveals around a unit: <b>20 x 20 u16</b>,
    /// row = the sight radius, column = how far the row is from the circle's
    /// rim, value = the half-width to open there.
    ///
    /// <para>Found by following the main loop's own trace labels. One of them
    /// reads <b>"unexplored"</b> (@0x4f7a54), and the step it names runs
    /// <b>@0x4205b0 on every fifth tick</b> (`[0x4fa240] % 5 == 1`). That step
    /// clears a <b>65535-byte</b> grid at 0x678b58 — 256 x 256, recomputed from
    /// scratch each pass — unless <c>byte[0x4f8a3c]</c> says fog is off, and
    /// then stamps units through @0x4200c0, which <b>clamps the radius to 0x13 =
    /// 19</b> and reads its spans out of this table:
    /// <c>ax = word[(r*20 + d)*2 + 0x4f8a48]</c>.</para>
    ///
    /// <para>The numbers are a circle: at radius 19 the centre row opens 20 and
    /// dy = 10 opens 17, against sqrt(400-100) = 17.3. It is exported rather
    /// than recomputed, so the remake reveals exactly the cells the original
    /// reveals, rounding included.</para></summary>
    public const uint SightCircleTable = 0x4f8a48;
    public const int SightRadii = 20;

    /// <summary>The clamp @0x4200c8.</summary>
    public const int SightMax = 19;

    public int[] SightCircle()
    {
        var v = new int[SightRadii * SightRadii];
        for (int i = 0; i < v.Length; i++)
        {
            var r = Read((uint)(SightCircleBase + i * 2), 2);
            v[i] = r.Length < 2 ? 0 : r[0] | (r[1] << 8);
        }
        return v;
    }

    /// <summary>Row 0 is "1 then nothing", row 19 runs out at 20, and the middle
    /// row of radius 10 opens 11 — enough to pin the table in another build.
    /// </summary>
    private bool SightCircleLooksRight(uint va)
    {
        (int At, int Want)[] fp =
        {
            (0, 1), (1, 0), (21, 2), (10 * 20 + 10, 11),
            (19 * 20 + 0, 4), (19 * 20 + 19, 20),
        };
        foreach (var (at, want) in fp)
        {
            var r = Read((uint)(va + at * 2), 2);
            if (r.Length < 2 || (r[0] | (r[1] << 8)) != want) return false;
        }
        return true;
    }

    /// <summary>Where a turret sits on its hull: <b>22 rows of 5 (x, y) pairs
    /// of i16</b>, row = the hull component (entity +0x0b), the five entries =
    /// the slope classes of the ground the unit stands on.
    ///
    /// <para>The unit draw (kind 0 of the draw list, @0x429900) blits the hull
    /// at the list entry's own x/y and then, at <b>@0x429CCB..0x429D1B</b>,
    /// moves the pen before the turret:
    /// <c>x += (t[comp][0].x + t[comp][k].x) / 2</c> and the same for y, with
    /// <c>t = word[20*comp + 4*k + 0x4fa320]</c>. <b>k is the tile's FLAG
    /// byte</b> — @0x429AD5 calls 0x41d110, which is
    /// <c>byte[map + (row*width + col)*4 + 3]</c>, and a flag above 4 is taken
    /// as 0 (@0x429AE1). So on flat ground the offset is simply row 0.</para>
    ///
    /// <para>The table is 440 bytes and ends exactly where the slope-to-frame
    /// table begins (0x4fa4d8, five u16: 0, 16, 32, 8, 24 — the block a tilted
    /// turret is drawn from). Rows 0..19 are the chassis and are plainly one
    /// table: x is always (0, -a, 0, +a, 0) and y is between -3 and -16. Rows 20
    /// and 21 do not follow that shape and no chassis in the game reaches them;
    /// they are exported as they lie, not repaired.</para>
    ///
    /// <para>⚠ This replaces the 45 % rule 0.3.2 shipped, which was measured
    /// from the art because this table had not been found.</para></summary>
    public const uint TurretMountTable = 0x4fa320;
    public const int MountRows = 22;
    public const int MountSlopes = 5;

    /// <summary>The five turret frame blocks for the slope classes, read at
    /// @0x429B05 as <c>byte[k*2 + 0x4fa4d8]</c>. Sits right behind the mount
    /// table, which is how the table's length is known.</summary>
    public int[] SlopeBlocks()
    {
        var v = new int[MountSlopes];
        for (int k = 0; k < MountSlopes; k++)
        {
            var r = Read((uint)(TurretMountBase + MountRows * 20 + k * 2), 2);
            v[k] = r.Length < 2 ? 0 : r[0] | (r[1] << 8);
        }
        return v;
    }

    /// <summary>[component][slope] -> (x, y), straight out of the executable.
    /// </summary>
    public (int X, int Y)[,] TurretMount()
    {
        var v = new (int, int)[MountRows, MountSlopes];
        for (int c = 0; c < MountRows; c++)
            for (int k = 0; k < MountSlopes; k++)
            {
                var r = Read((uint)(TurretMountBase + c * 20 + k * 4), 4);
                if (r.Length < 4) continue;
                v[c, k] = ((short)(r[0] | (r[1] << 8)), (short)(r[2] | (r[3] << 8)));
            }
        return v;
    }

    /// <summary>The shape of the first twenty rows plus the five blocks behind
    /// them — strong enough that it is unique in both builds.</summary>
    private bool TurretMountLooksRight(uint va)
    {
        for (int c = 0; c < 20; c++)
        {
            var row = new (int X, int Y)[MountSlopes];
            for (int k = 0; k < MountSlopes; k++)
            {
                var r = Read((uint)(va + c * 20 + k * 4), 4);
                if (r.Length < 4) return false;
                row[k] = ((short)(r[0] | (r[1] << 8)), (short)(r[2] | (r[3] << 8)));
            }
            if (row[0].X != 0 || row[2].X != 0 || row[4].X != 0) return false;
            if (row[1].X > 0 || row[1].X != -row[3].X || row[1].X < -12) return false;
            foreach (var p in row) if (p.Y >= 0 || p.Y < -40) return false;
        }
        int[] want = { 0, 16, 32, 8, 24 };
        for (int k = 0; k < want.Length; k++)
        {
            var r = Read((uint)(va + MountRows * 20 + k * 2), 2);
            if (r.Length < 2 || (r[0] | (r[1] << 8)) != want[k]) return false;
        }
        return true;
    }

    // ---- where the tables actually are in THIS build ------------------------

    /// <summary>The addresses in use. They start at the documented ones and are
    /// replaced by whatever <see cref="Locate"/> finds, because the constants
    /// above hold for the January 1998 executable and NOT for the one on the
    /// September 1997 CD — there the three tables sit 0xFC0 to 0x1000 lower,
    /// and by different amounts, so no single offset repairs them.</summary>
    public uint StatsBase { get; private set; } = StatsRecord0;
    public uint ShipBase { get; private set; } = ShipDesigns;
    public uint AircraftBase { get; private set; } = AircraftTemplates;
    public uint OrderBase { get; private set; } = OrderVocabulary;
    public uint BuildingNameBase { get; private set; } = BuildingTypeNames;
    public uint MissionNameBase { get; private set; } = MissionNames;
    public uint FireSoundBase { get; private set; } = FireSoundTable;
    public uint SightCircleBase { get; private set; } = SightCircleTable;
    public bool SightCircleFound { get; private set; } = true;
    public uint TurretMountBase { get; private set; } = TurretMountTable;
    public bool TurretMountFound { get; private set; } = true;

    /// <summary>False when the fire-sound table could not be found at all — the
    /// exporter then writes nothing rather than a table of nonsense. This was
    /// found the hard way: the first run took the September 1997 executable of
    /// the installation, read the January 1998 address and wrote out "16716",
    /// which is the ASCII of a name.</summary>
    public bool FireSoundsFound { get; private set; } = true;
    public bool Relocated { get; private set; }

    /// <summary>hp_max of twelve rows, read off the 1998 build and written down
    /// in GAMESTATE_RE.md (161 = HQ 440, 173 = Stahlsucher 1000 …). Twelve u16
    /// on a 58-byte grid pin the stats table exactly; the names cannot, because
    /// the table has gaps and an anchor one byte off still reads every name
    /// almost right.</summary>
    private static readonly (int Row, int Hp)[] StatsFingerprint =
    {
        (150, 500), (160, 500), (161, 440), (162, 250), (165, 250), (166, 600),
        (170, 600), (171, 40), (172, 60), (173, 1000), (174, 300), (175, 400),
    };

    private ExeTables(byte[] d) { _d = d; }

    public static ExeTables Load(string path) => FromBytes(File.ReadAllBytes(path));

    public static ExeTables FromBytes(byte[] d)
    {
        var t = new ExeTables(d);
        int e = BitConverter.ToInt32(d, 0x3c);
        if (d[e] != 'P' || d[e + 1] != 'E') throw new InvalidDataException("Keine PE-Datei");
        int nsec = BitConverter.ToUInt16(d, e + 6);
        int optsz = BitConverter.ToUInt16(d, e + 20);
        int sect = e + 24 + optsz;
        for (int i = 0; i < nsec; i++)
        {
            int s = sect + i * 40;
            t._sections.Add((
                BitConverter.ToUInt32(d, s + 12) + ImageBase,
                BitConverter.ToUInt32(d, s + 8),
                BitConverter.ToUInt32(d, s + 20),
                BitConverter.ToUInt32(d, s + 16)));
        }
        t.Locate();
        return t;
    }

    /// <summary>Find the three tables by what they contain instead of where
    /// they were. The documented addresses are kept as long as they hold; only
    /// when a table is not where it used to be is the whole image searched.
    ///
    /// Each search is checked on the build whose answer is known: on the 1998
    /// executable all three return exactly <see cref="StatsRecord0"/>,
    /// <see cref="ShipDesigns"/> and <see cref="AircraftTemplates"/>, uniquely.
    /// </summary>
    private void Locate()
    {
        if (!ShipsLookRight(ShipDesigns))
        {
            uint v = Scan(p => ShipsLookRight(p));
            if (v != 0) { ShipBase = v; Relocated = true; }
        }
        if (!AircraftLookRight(AircraftTemplates))
        {
            uint v = Scan(p => AircraftLookRight(p));
            if (v != 0) { AircraftBase = v; Relocated = true; }
        }
        if (!StatsLookRight(StatsRecord0))
        {
            uint v = Scan(p => StatsLookRight(p));
            if (v != 0) { StatsBase = v; Relocated = true; }
        }
        if (!SightCircleLooksRight(SightCircleTable))
        {
            uint v = Scan(p => SightCircleLooksRight(p));
            if (v != 0) { SightCircleBase = v; Relocated = true; }
            else SightCircleFound = false;
        }
        if (!TurretMountLooksRight(TurretMountTable))
        {
            uint v = Scan(p => TurretMountLooksRight(p));
            if (v != 0) { TurretMountBase = v; Relocated = true; }
            else TurretMountFound = false;
        }
        if (!FireSoundsLookRight(FireSoundTable))
        {
            uint v = Scan(p => FireSoundsLookRight(p));
            if (v != 0) { FireSoundBase = v; Relocated = true; }
            else FireSoundsFound = false;
        }
        if (!StringTableLooksRight(OrderVocabulary, OrderCount, OrderStride, OrderCount))
        {
            uint v = ScanBest(p => StringTableLooksRight(p, OrderCount, OrderStride, OrderCount));
            if (v != 0) { OrderBase = v; Relocated = true; }
        }
        if (!StringTableLooksRight(BuildingTypeNames, BuildingTypeCount, BuildingTypeStride,
                                   BuildingTypeCount))
        {
            uint v = ScanBest(p => StringTableLooksRight(p, BuildingTypeCount,
                                                        BuildingTypeStride, BuildingTypeCount));
            if (v != 0) { BuildingNameBase = v; Relocated = true; }
        }
        if (!StringTableLooksRight(MissionNames, MissionNameCount, MissionNameStride,
                                   MissionNameCount - 1))
        {
            uint v = ScanBest(p => StringTableLooksRight(p, MissionNameCount,
                                                        MissionNameStride, MissionNameCount - 1));
            if (v != 0) { MissionNameBase = v; Relocated = true; }
        }
        // NOT Scan(): that one stops 0x4000 short of each section's raw end,
        // and this table lives inside exactly that tail — 0x538E20 in one build
        // and 0x539DB8 in the other, while .data's scanned range ends at
        // 0x537000. It cost a run to notice.
        BuildingStatBase = ScanToEnd(BuildingStatsLookRight, 20 * BuildingStatStride);

        // same tail, same reason — 0x539d90 in one build, 0x538df8 in the other
        uint sc = ScanToEnd(SightCentreLooksRight, 20 * 2);
        SightCentreFound = sc != 0;
        if (sc != 0) { if (sc != SightCentreBase) Relocated = true; SightCentreBase = sc; }
    }

    /// <summary>Like <see cref="Scan"/>, but searching right up to the last
    /// address a table of <paramref name="size"/> bytes could still start
    /// at.</summary>
    private uint ScanToEnd(Func<uint, bool> ok, int size)
    {
        foreach (var s in _sections)
            for (uint va = s.Va; va + size <= s.Va + s.RawSize; va++)
                if (ok(va)) return va;
        return 0;
    }

    // ---- the building type table -------------------------------------------
    //
    // add_building @0x4C8D60 fills a new record from a 10-byte row indexed by
    // the type: hit points into +0x06 and +0x16, the door count into +0x34 and
    // the door offsets into +0x35.. — the same fields Capture.cs reads back out
    // of sec3, which is the cross-check that this is the right table.
    //
    // Found BY SHAPE, never by address (rule 8): the two builds on this machine
    // have it at 0x538E20 and 0x539DB8, and the scan below finds exactly one
    // candidate in each — with the same sixteen door counts,
    // 1,2,2,2,1,1,0,0,1,1,0,1,0,0,1,1.

    /// <summary>Where the 10-byte building stat rows start, 0 if not found.</summary>
    public uint BuildingStatBase { get; private set; }

    public const int BuildingStatStride = 10;

    /// <summary>Hit points a freshly built building of this type starts with,
    /// and what a type with no row of its own gets — <c>add_building</c> writes
    /// <b>700</b> for every type from 17 up (@0x4C8F1B).</summary>
    public const int BuildingHpDefault = 700;

    /// <summary>One row of the table: what a new building is made of.</summary>
    public readonly struct BuildingStat
    {
        public readonly int Hp, DoorCount;
        /// <summary>Door offsets from the building's origin, <see
        /// cref="DoorCount"/> of them.</summary>
        public readonly (int Col, int Row)[] Doors;
        public BuildingStat(int hp, int doors, (int, int)[] d)
        { Hp = hp; DoorCount = doors; Doors = d; }
    }

    public BuildingStat BuildingStats(int typ)
    {
        if (BuildingStatBase == 0 || typ < 1 || typ > 16)
            return new BuildingStat(BuildingHpDefault, 0, Array.Empty<(int, int)>());
        var r = Read((uint)(BuildingStatBase + typ * BuildingStatStride), BuildingStatStride);
        if (r.Length < BuildingStatStride)
            return new BuildingStat(BuildingHpDefault, 0, Array.Empty<(int, int)>());
        int n = r[4];
        var doors = new (int, int)[n];
        for (int i = 0; i < n && i < 2; i++) doors[i] = (r[5 + i * 2], r[6 + i * 2]);
        return new BuildingStat(BitConverter.ToUInt16(r, 0), n, doors);
    }

    // ---- where a building watches from, and how far ------------------------
    //
    // Read 08.08.2026 out of the fog update @0x4205B0. Its building half runs
    // over all 255 records and, for each one that stands, belongs to somebody
    // below 8, is ALLIED with the viewing player (the diplomacy matrix at
    // 0x87b155, stride 40) and has byte[+0x18] set, calls the stamper with:
    //
    //     push 0xa                                   ; the radius, a CONSTANT
    //     al = byte[typ*2 + 0x539d91]; ax += y       ; row offset
    //     al = byte[typ*2 + 0x539d90]; ax += x       ; col offset
    //     call stamp(x, y, radius)
    //
    // So a building watches from a point the game looks up PER TYPE, and every
    // building — radar post included — sees exactly ten cells. Both numbers were
    // ours before: the offset was "half the footprint, the obvious reading, not
    // a measured one" and the radius was 6.

    /// <summary>Where a building watches from, as an offset from the corner cell
    /// its record names — one byte column, one byte row, per type.</summary>
    public const uint SightCentreTable = 0x539d90;

    /// <summary>How far a building sees. A constant in the original: <c>push
    /// 0xa</c> @0x4206AB, the same for every type.</summary>
    public const int BuildingSightRadius = 10;

    public uint SightCentreBase { get; private set; } = SightCentreTable;
    public bool SightCentreFound { get; private set; } = true;

    /// <summary>The watch point of a type, or (0,0) when the table is missing.</summary>
    public (int Col, int Row) SightCentre(int typ)
    {
        if (SightCentreBase == 0 || typ < 0 || typ > 16) return (0, 0);
        var r = Read((uint)(SightCentreBase + typ * 2), 2);
        return r.Length < 2 ? (0, 0) : (r[0], r[1]);
    }

    /// <summary>The shape, and it is sharp enough to hit exactly once in BOTH
    /// executables on this machine: the Basis sits at (3,3), the three factories
    /// all at (3,2), the Flughafen at (4,2), the Mine at (4,3), every value in
    /// 1..4, and from type 17 on the table is zero.
    ///
    /// <para>⚠ Rule 8, and this time it was earned: the table is at 0x539d90 in
    /// the build under study and at <b>0x538df8</b> in the one installed on F:.
    /// A reader with a fixed address would have handed the installation
    /// somebody else's bytes. Verified: both tables are byte-identical over
    /// their 40 bytes.</para></summary>
    private bool SightCentreLooksRight(uint va)
    {
        var r = Read(va, 20 * 2);
        if (r.Length < 20 * 2) return false;
        (int At, int Want)[] fp =
        {
            (1 * 2, 3), (1 * 2 + 1, 3),          // Basis
            (2 * 2, 3), (2 * 2 + 1, 2),          // the three factories, all alike
            (3 * 2, 3), (3 * 2 + 1, 2),
            (4 * 2, 3), (4 * 2 + 1, 2),
            (9 * 2, 4), (9 * 2 + 1, 2),          // Flughafen
            (10 * 2, 4), (10 * 2 + 1, 3),        // Mine
        };
        foreach (var (at, want) in fp) if (r[at] != want) return false;
        for (int t = 1; t <= 16; t++)
            if (r[t * 2] is < 1 or > 4 || r[t * 2 + 1] is < 1 or > 4) return false;
        for (int i = 17 * 2; i < 20 * 2; i++) if (r[i] != 0) return false;
        return true;
    }

    /// <summary>The shape: entry 0 all zero, entries 1..16 carrying one of the
    /// three hit-point values with a 1 at +0x03 and at most two doors, and
    /// nothing at all from 17 on.</summary>
    private bool BuildingStatsLookRight(uint va)
    {
        var r = Read(va, 20 * BuildingStatStride);
        if (r.Length < 20 * BuildingStatStride) return false;
        for (int i = 0; i < BuildingStatStride; i++) if (r[i] != 0) return false;
        for (int t = 1; t <= 16; t++)
        {
            int o = t * BuildingStatStride;
            int hp = BitConverter.ToUInt16(r, o);
            if (hp is not (800 or 1000 or 1200)) return false;
            if (r[o + 3] != 1 || r[o + 4] > 2) return false;
        }
        for (int i = 17 * BuildingStatStride; i < 20 * BuildingStatStride; i++)
            if (r[i] != 0) return false;
        return true;
    }

    /// <summary>A fixed-stride table of names, every slot zero-terminated AND
    /// zero-padded to the end of its field.
    ///
    /// The padding is the whole trick. Without it the search finds dozens of
    /// places inside the game's packed debug text that fit the grid; with it,
    /// only the real tables do. `named` says how many slots must actually carry
    /// a name — demanding all of them is what separates the true base from the
    /// same table read one record late.</summary>
    private bool StringTableLooksRight(uint va, int count, int stride, int named)
    {
        var before = Read(va - 1, 1);
        if (before.Length == 1 && before[0] >= 0x20 && before[0] < 0x7f) return false;
        int have = 0;
        for (int i = 0; i < count; i++)
        {
            var r = Read((uint)(va + i * stride), stride);
            if (r.Length < stride) return false;
            int z = Array.IndexOf(r, (byte)0);
            if (z < 0) return false;
            for (int k = 0; k < z; k++) if (r[k] < 0x20 || r[k] == 0x7f) return false;
            for (int k = z; k < stride; k++) if (r[k] != 0) return false;
            if (z >= 3) have++;
        }
        return have >= named;
    }

    /// <summary>The lowest address that satisfies the test. For a string table
    /// the same run also matches one record later, so the first hit is the
    /// base.</summary>
    private uint ScanBest(Func<uint, bool> ok) => Scan(ok);

    /// <summary>Every virtual address the image maps, in order.</summary>
    private uint Scan(Func<uint, bool> ok)
    {
        foreach (var s in _sections)
            for (uint va = s.Va; va + 0x4000 < s.Va + s.RawSize; va++)
                if (ok(va)) return va;
        return 0;
    }

    /// <summary>A real, zero-terminated name in a fixed field: at least three
    /// characters and nothing but text before the terminator.
    ///
    /// <see cref="Cp437.GetString"/> is NOT good enough as a test — it skips
    /// control bytes and trims, so a field of mostly zeros with a few letters
    /// in it passes. That difference is what first sent the aircraft search to
    /// the wrong address.</summary>
    private static bool IsName(byte[] r, int at, int len, int min = 3)
    {
        int i = at;
        while (i < at + len && i < r.Length && r[i] != 0)
        {
            byte c = r[i];
            if (c < 0x20 || c == 0x7f) return false;
            i++;
        }
        return i < at + len && i - at >= min;      // terminator inside the field
    }

    /// <summary>Ten records of 42 whose hull is one of the naval types 150..158
    /// — the signature the shipyard work established — each with a name.</summary>
    private bool ShipsLookRight(uint va)
    {
        for (int i = 0; i < 10; i++)
        {
            var r = Read((uint)(va + i * ShipStride), ShipStride);
            if (r.Length < ShipStride) return false;
            if (r[0x00] > 1 || r[0x17] < 150 || r[0x17] > 158) return false;
            if (!IsName(r, 0x01, 0x15)) return false;
        }
        return true;
    }

    /// <summary>Eight records of 48 with a long and a short name and an
    /// airframe in the aircraft sprite range. The byte in front of the first
    /// name must not be text: without that the search also lands in the middle
    /// of a debug line ("…y attack H…") that happens to fit the grid.</summary>
    private bool AircraftLookRight(uint va)
    {
        var before = Read(va - 1, 1);
        if (before.Length == 1 && before[0] >= 32 && before[0] < 127) return false;
        for (int i = 0; i < 8; i++)
        {
            var r = Read((uint)(va + i * AircraftStride), AircraftStride);
            if (r.Length < AircraftStride) return false;
            if (!IsName(r, 0x00, 0x15) || !IsName(r, 0x15, 0x0c, 2)) return false;
            if (r[0x22] == 0 || r[0x24] < 100 || r[0x24] > 130) return false;
        }
        return true;
    }

    private bool StatsLookRight(uint va)
    {
        foreach (var (row, hp) in StatsFingerprint)
        {
            var r = Read((uint)(va + row * StatsStride), 2);
            if (r.Length < 2 || BitConverter.ToUInt16(r, 0) != hp) return false;
        }
        return true;
    }

    /// <summary>Virtual address to file offset, or -1 outside every section.</summary>
    public int Offset(uint va)
    {
        foreach (var s in _sections)
            if (va >= s.Va && va < s.Va + s.RawSize)
                return (int)(s.Raw + (va - s.Va));
        return -1;
    }

    private byte[] Read(uint va, int n)
    {
        int o = Offset(va);
        if (o < 0 || o + n > _d.Length) return Array.Empty<byte>();
        var b = new byte[n];
        Array.Copy(_d, o, b, 0, n);
        return b;
    }

    /// <summary>cp437 text, stopping at the first zero — the game's own
    /// encoding, and the one that keeps the umlauts intact.</summary>
    private static string Str(byte[] r, int at, int len) => Cp437.GetString(r, at, len);

    // ---- component stats ---------------------------------------------------

    public sealed class Stat
    {
        public int UnitType, HpMax, ComponentId, Tech;
        public string Name = "", SuccName = "";
        public byte[] Raw = Array.Empty<byte>();
    }

    /// <summary>One stats record. `component_id` deliberately comes from the
    /// PREDECESSOR: a record's tail already describes its successor, which is
    /// the off-by-one the sprite work proved four separate ways.</summary>
    public Stat? StatsFor(int unitType)
    {
        var r = Read((uint)(StatsBase + unitType * StatsStride), StatsStride);
        if (r.Length < StatsStride) return null;
        var prev = Read((uint)(StatsBase + (unitType - 1) * StatsStride), StatsStride);
        return new Stat
        {
            UnitType = unitType,
            HpMax = BitConverter.ToUInt16(r, 0),
            Name = Str(r, 0x0b, 0x15),
            SuccName = Str(r, 0x22, 0x0b),
            ComponentId = prev.Length >= StatsStride ? prev[0x2d] : 0,
            Tech = TechOf(unitType),
            Raw = r,
        };
    }

    /// <summary>The technology threshold of a component: `+0x24` of the record
    /// counted from the array base, which is what the enable gate @0x419e90
    /// compares against the campaign level.</summary>
    public int TechOf(int row)
    {
        // the array base sits 0x1a in front of record 0, whichever build it is
        var b = Read((uint)(StatsBase - (StatsRecord0 - StatsArrayBase) + row * StatsStride + 0x24), 1);
        return b.Length == 1 ? b[0] : 0;
    }

    /// <summary>One row of the stats array as it lies, counted from the ARRAY
    /// base rather than from record 0.
    ///
    /// This is the view the design arithmetic @0x4b1fb0 uses: it indexes the
    /// array with a bare component number — weapon, propulsion and equipment
    /// alike — and reads fields at +0x0d..+0x22 of the row. Those offsets only
    /// line up when counted from 0x5045a0, which is why this exists next to
    /// <see cref="StatsFor"/> instead of reusing it.
    ///
    /// The array holds eight player blocks of 200 rows (a technology state per
    /// player), but only block 0 is filled in the file — the others are copied
    /// into place at start-up (rep movsd @0x4b22ed). Block 0 is therefore the
    /// right row for anything read out of the binary.</summary>
    public byte[] ComponentRow(int row)
    {
        if (row < 0 || row >= 200) return Array.Empty<byte>();
        return Read((uint)(StatsBase - (StatsRecord0 - StatsArrayBase) + row * StatsStride), StatsStride);
    }

    // ---- the two string tables ---------------------------------------------

    /// <summary>The order vocabulary the unit panel speaks in. Latin-1: this
    /// one table is not cp437, which is why "Beschützen" comes out mangled if
    /// it is read like the rest.</summary>
    public List<string> Orders()
    {
        var list = new List<string>();
        for (int i = 0; i < OrderCount; i++)
        {
            var r = Read((uint)(OrderBase + i * OrderStride), OrderStride);
            list.Add(r.Length < OrderStride ? "" : Latin1(r));
        }
        return list;
    }

    /// <summary>Building type names; entry n-1 belongs to map type n.</summary>
    public List<string> BuildingNames()
    {
        var list = new List<string>();
        for (int i = 0; i < BuildingTypeCount; i++)
        {
            var r = Read((uint)(BuildingNameBase + i * BuildingTypeStride), BuildingTypeStride);
            list.Add(r.Length < BuildingTypeStride ? "" : Str(r, 0, BuildingTypeStride));
        }
        return list;
    }

    /// <summary>The campaign's mission names, indexed by the campaign counter.
    /// Entry 0 is a test name, 1..33 the missions, 34..36 three extra ones.
    /// </summary>
    public List<string> MissionNameList()
    {
        var list = new List<string>();
        for (int i = 0; i < MissionNameCount; i++)
        {
            var r = Read((uint)(MissionNameBase + i * MissionNameStride), MissionNameStride);
            list.Add(r.Length < MissionNameStride ? "" : Str(r, 0, MissionNameStride));
        }
        return list;
    }

    private static string Latin1(byte[] r)
    {
        var sb = new StringBuilder(r.Length);
        foreach (byte b in r)
        {
            if (b == 0) break;
            sb.Append((char)b);
        }
        return sb.ToString().Trim();
    }

    // ---- ship designs ------------------------------------------------------

    public sealed class ShipDesign
    {
        public int Index, Enable, Weapon, Chassis, Variant;
        public int CostW, CostF, CostS;
        public int Speed, Energie, Attack, Defence, Range1, Range2;
        public int Sight, Ammo, Fuel, Reload, Tech;
        public string Name = "";
    }

    /// <summary>Player 0's block of the SHIP_PROD table — the campaign default.
    /// The init routine @0x4b2330 copies it into the other seven.</summary>
    public List<ShipDesign> Ships()
    {
        var list = new List<ShipDesign>();
        for (int i = 0; i < 10; i++)
        {
            var r = Read((uint)(ShipBase + i * ShipStride), ShipStride);
            if (r.Length < ShipStride) break;
            var d = new ShipDesign
            {
                Index = i,
                Enable = r[0x00],
                Name = Str(r, 0x01, 0x15),
                Weapon = r[0x16],
                Chassis = r[0x17],
                Variant = r[0x18],
                CostW = r[0x19], CostF = r[0x1a], CostS = r[0x1b],
                Speed = r[0x1c],
                Energie = r[0x1d],
                Attack = r[0x1e], Defence = r[0x1f],
                Range1 = BitConverter.ToUInt16(r, 0x20),
                Range2 = BitConverter.ToInt16(r, 0x22),
                Sight = r[0x24],
                Ammo = r[0x25],
                Fuel = BitConverter.ToUInt16(r, 0x26),
                Reload = r[0x28],
            };
            d.Tech = Math.Max(d.Weapon != 0 ? TechOf(d.Weapon) : 0, TechOf(d.Chassis));
            list.Add(d);
        }
        return list;
    }

    // ---- aircraft templates ------------------------------------------------

    public sealed class AircraftTemplate
    {
        public int Index, Hp, Payload, Airframe, Attack, Defence, Sight, Ammo, Fuel;
        public string Name = "", Short = "";
    }

    /// <summary>The eight templates the spawn routine @0x4b1580 copies from,
    /// carrying the game's own German names.</summary>
    public List<AircraftTemplate> Aircraft()
    {
        var list = new List<AircraftTemplate>();
        for (int i = 0; i < 8; i++)
        {
            var r = Read((uint)(AircraftBase + i * AircraftStride), AircraftStride);
            if (r.Length < AircraftStride) break;
            list.Add(new AircraftTemplate
            {
                Index = i,
                Name = Str(r, 0x00, 0x15),
                Short = Str(r, 0x15, 0x0c),
                Hp = r[0x22],
                Payload = r[0x23],
                Airframe = r[0x24],
                Attack = r[0x25],
                Defence = r[0x26],
                Sight = r[0x27],
                Ammo = r[0x28],
                Fuel = BitConverter.ToUInt16(r, 0x2b),
            });
        }
        return list;
    }

    // ---- the campaign's diplomacy ------------------------------------------

    /// <summary>
    /// Who is allied with whom, and who is neutral, in each of the 33 campaign
    /// missions — <b>and it lives in the code, not in a table</b>.
    ///
    /// <para>This answers the question the handoff ended on: the campaign and
    /// NET levels stop at section 38 and carry no sec106, so where does mission
    /// 1 get its neutral player from? From the executable. The map loader calls
    /// <c>mission_init</c> @0x487c40 (@0x41F1E6, thunk 0x401C2B) on every start,
    /// and that routine sets the whole thing up:</para>
    ///
    /// <code>
    ///   0x487C53  for a in 0..7: for b in 0..7:  set_relation(a, b, a==b)
    ///   0x487C75  movsx eax, word [0x539934]     ; the mission number, 1..0x78
    ///             jmp  dword [ecx*4 + 0x49417c]  ; ecx = byte [eax-1 + 0x4941fc]
    ///   &lt;branch&gt;  set_relation(a, b, 1) …        ; THIS mission's alliances
    ///   0x48827B  for p in 0..6:  set_relation(p, 7, 1)
    ///   0x4883E7  for p in 0..7:  set_neutral(p, 0)
    ///   0x4883F8                  set_neutral(7, 1)
    /// </code>
    ///
    /// <para><c>set_relation</c> @0x4cf6d0 writes SYMMETRICALLY —
    /// <c>byte[0x87b155 + a*40 + b]</c> and the mirror @0x4cf6ee — which is the
    /// player record's +0x15, the same alliance row <see cref="CwmExtra"/> reads
    /// out of sec53. <c>set_neutral</c> @0x4d09f0 writes
    /// <c>byte[0xb38d38 + player]</c>, the field the loader fills from sec106 in
    /// the .DM files (@0x41EC0A) and the takeover scan tests (@0x407275). That
    /// address sits past the written part of .data (raw ends at VA 0x53c000), so
    /// it starts at zero without anybody's help.</para>
    ///
    /// <para><b>Player 7 is the neutral one, in every mission</b> — and here it
    /// is measured rather than assumed (see <see cref="LocateDiplomacy"/>): he
    /// is the only slot allied with everybody in all 33 matrices. A raw byte
    /// scan for every call of <c>set_neutral</c> in .text finds fourteen sites
    /// and confirms it from the other side: <b>thirteen push player 7</b>, the
    /// fourteenth is the loop that clears all eight. No other player is ever
    /// made neutral, and there is no pointer to the routine anywhere in the
    /// image, so the fourteen are all of them.</para>
    ///
    /// <para>The branches are straight-line code — nothing but
    /// <c>push imm8</c>, <c>call</c>, <c>jmp</c> and <c>add esp, imm8</c> — so
    /// this reads them by byte pattern rather than by disassembling. Two of the
    /// branches use a short <c>EB</c> jump, and the compiler tail-merges: a
    /// branch may jump INTO the argument list of the shared tail's call, which
    /// is why the pending pushes have to survive a jump. Checked against
    /// <c>aekernel/campaign_diplomacy.py</c>, which decodes the same code with
    /// Capstone: <b>33 of 33 missions identical</b>.</para>
    /// </summary>
    public sealed class Diplomacy
    {
        public int Mission;
        /// <summary>Symmetric, and every player is allied with himself.</summary>
        public bool[,] Allied = new bool[8, 8];
        /// <summary>Slots the mission puts out of play — always just 7.</summary>
        public bool[] Neutral = new bool[8];

        public bool IsAllied(int a, int b)
            => a is >= 0 and < 8 && b is >= 0 and < 8 && Allied[a, b];
        public bool IsNeutral(int p) => p is >= 0 and < 8 && Neutral[p];
    }

    // The addresses of the build the reading was done on. They are kept as
    // DOCUMENTATION only — nothing below indexes by them, because this machine
    // holds two different builds (see LocateDiplomacy).
    public const uint MissionInit = 0x487c40;         // the routine
    public const uint MissionInitDispatch = 0x487c86; // xor ecx,ecx; mov cl,…
    public const uint MissionCaseIndex = 0x4941fc;    // one byte per mission
    public const uint MissionCaseTable = 0x49417c;    // 32 branch targets
    public const uint SetRelation = 0x4cf6d0;
    public const uint SetNeutral = 0x4d09f0;          // -> byte[0xb38d38 + p]

    public const int MissionCaseCount = 32;
    /// <summary>The campaign has 33 missions; the jump table is sized 0x78.</summary>
    public const int CampaignMissions = 33;

    /// <summary>Where the jump table and its index bytes were found in THIS
    /// executable, and which slot the missions leave out of play. All three are
    /// searched for, not assumed — see <see cref="LocateDiplomacy"/>.</summary>
    public uint DiplomacyDispatch { get; private set; }
    public uint DiplomacyIndex { get; private set; }
    public uint DiplomacyTable { get; private set; }
    public int NeutralPlayer { get; private set; } = -1;
    /// <summary>The confirming evidence: where <c>set_neutral</c> was found,
    /// what it writes, and how many of its call sites push
    /// <see cref="NeutralPlayer"/>.</summary>
    public uint SetNeutralAt { get; private set; }
    public uint NeutralField { get; private set; }
    public int NeutralSites { get; private set; }
    public int NeutralSitesConst { get; private set; }

    private bool _diploTried;

    public bool HasCampaignDiplomacy
    {
        get { LocateDiplomacy(); return DiplomacyTable != 0 && NeutralPlayer >= 0; }
    }

    /// <summary>
    /// Find <c>mission_init</c> in whatever build this is.
    ///
    /// <para>⚠ This is not caution for its own sake. There are <b>two different
    /// builds of GAME.EXE on this machine</b> — 1.421.824 bytes in the working
    /// folder and under Program Files, and 1.420.800 bytes in the installation
    /// on F: — and in the second one everything has moved: the dispatch sits at
    /// 0x486346 instead of 0x487c86, and <c>set_relation</c> was compiled with
    /// different registers (<c>88 94 c3</c> for <c>88 8c c3</c>). Reading by
    /// address gave the F: install no diplomacy at all.</para>
    ///
    /// <para>So everything here is found by shape:</para>
    /// <list type="number">
    /// <item>a dispatch <c>33 C9 8A 88 idx FF 24 8D tab</c> whose table holds
    /// exactly 32 targets (<c>idx - tab == 0x80</c>);</item>
    /// <item>all 33 branches must walk cleanly to their end;</item>
    /// <item>and the 33 matrices must leave <b>exactly one</b> player allied
    /// with everybody in every single mission. That player is the neutral one —
    /// <b>measured, not assumed</b>. The shared tail @0x48827B is what puts him
    /// there and no mission takes it back.</item>
    /// </list>
    ///
    /// <para>The result is then confirmed from the other side: the function
    /// whose body is <c>33 C9 8A 44 24 04 8A 4C 24 08 88 81 imm32 C3</c> —
    /// <c>set_neutral</c> — has its call sites counted, and in both builds
    /// <b>13 of 14 push exactly that player</b>, the fourteenth being the loop
    /// that clears all eight. (There is a second function of the same shape
    /// with 119 sites spread over all eight players; the three-quarter majority
    /// is what tells them apart.)</para>
    ///
    /// <para>Checked against <c>aekernel/diplo_relocate.py</c>, which does the
    /// same search with Capstone: the two builds agree on <b>33 of 33
    /// missions</b>, and <c>campaign_diplomacy.py</c> agrees field for field.
    /// The diplomacy is a property of the game, not of one binary.</para>
    /// </summary>
    private void LocateDiplomacy()
    {
        if (_diploTried) return;
        _diploTried = true;
        var (tva, text) = TextSection();
        if (text.Length == 0) return;

        uint bestIdx = 0, bestTab = 0, bestAt = 0;
        int neutral = -1, found = 0;
        for (int i = 0; i + 16 < text.Length; i++)
        {
            if (text[i] != 0x33 || text[i + 1] != 0xC9 ||
                text[i + 2] != 0x8A || text[i + 3] != 0x88) continue;
            if (text[i + 8] != 0xFF || text[i + 9] != 0x24 || text[i + 10] != 0x8D) continue;
            uint idx = BitConverter.ToUInt32(text, i + 4);
            uint tab = BitConverter.ToUInt32(text, i + 11);
            if (idx - tab != 4 * MissionCaseCount) continue;

            var mats = AllMatrices(idx, tab);
            if (mats == null) continue;
            int only = -1;
            for (int q = 0; q < 8; q++)
            {
                bool all = true;
                foreach (var m in mats)
                    for (int p = 0; p < 8 && all; p++) if (!m[p, q]) all = false;
                if (!all) continue;
                if (only >= 0) { only = -1; break; }   // more than one: no verdict
                only = q;
            }
            if (only < 0) continue;
            found++;
            bestIdx = idx; bestTab = tab; bestAt = (uint)(tva + i); neutral = only;
        }
        if (found != 1) return;                        // ambiguous is not found

        DiplomacyDispatch = bestAt;
        DiplomacyIndex = bestIdx;
        DiplomacyTable = bestTab;
        NeutralPlayer = neutral;
        ConfirmNeutral(neutral);
    }

    /// <summary>The <c>set_neutral</c> whose sites push <paramref name="who"/>,
    /// as the second, independent witness. Nothing depends on it — it only
    /// supplies the numbers the note above quotes.</summary>
    private void ConfirmNeutral(int who)
    {
        var (tva, text) = TextSection();
        for (int i = 0; i + 17 < text.Length; i++)
        {
            if (text[i] != 0x33 || text[i + 1] != 0xC9 || text[i + 2] != 0x8A ||
                text[i + 3] != 0x44 || text[i + 4] != 0x24 || text[i + 5] != 0x04 ||
                text[i + 6] != 0x8A || text[i + 7] != 0x4C || text[i + 8] != 0x24 ||
                text[i + 9] != 0x08 || text[i + 10] != 0x88 || text[i + 11] != 0x81 ||
                text[i + 16] != 0xC3) continue;

            uint va = (uint)(tva + i);
            var sites = CallSites(va);
            int hit = 0;
            foreach (uint s in sites)
            {
                var p = PushesBefore(s, 12);
                if (p.Count >= 2 && p[^2] == who) hit++;
            }
            if (sites.Count == 0 || hit * 4 < sites.Count * 3) continue;
            if (hit <= NeutralSitesConst) continue;
            SetNeutralAt = va;
            NeutralField = BitConverter.ToUInt32(text, i + 12);
            NeutralSites = sites.Count;
            NeutralSitesConst = hit;
        }
    }

    private uint _textVa;
    private byte[]? _text;

    /// <summary>The code section — the first one in both builds, at RVA 0x1000.
    /// Cached, because the searches above sweep it several times.</summary>
    private (uint Va, byte[] Data) TextSection()
    {
        if (_text != null) return (_textVa, _text);
        if (_sections.Count == 0) return (0, Array.Empty<byte>());
        var s = _sections[0];
        int o = (int)s.Raw, n = (int)s.RawSize;
        if (o < 0 || n < 0 || o + n > _d.Length) return (0, Array.Empty<byte>());
        _text = new byte[n];
        Array.Copy(_d, o, _text, 0, n);
        _textVa = s.Va;
        return (_textVa, _text);
    }

    /// <summary>Follow one <c>jmp rel32</c> thunk, which is how every call in
    /// these branches reaches its function.</summary>
    private uint Resolve(uint va)
    {
        var b = Read(va, 5);
        return b.Length == 5 && b[0] == 0xE9 ? (uint)(va + 5 + BitConverter.ToInt32(b, 1)) : va;
    }

    /// <summary>Two <c>mov [reg + reg*8 + imm32], reg8</c> on the SAME imm32 —
    /// the symmetric write that makes this <c>set_relation</c> (@0x4cf6d0 in one
    /// build, @0x4cf270 in the other, with different registers in each).</summary>
    private bool LooksLikeSetRelation(uint va)
    {
        var b = Read(va, 48);
        uint first = 0;
        bool have = false;
        for (int i = 0; i + 7 <= b.Length; i++)
        {
            if (b[i] != 0x88) continue;
            if ((b[i + 1] & 0xC7) != 0x84) continue;      // mod=10, rm=100 (SIB)
            if ((b[i + 2] & 0xC0) != 0xC0) continue;      // SIB scale = 8
            uint a = BitConverter.ToUInt32(b, i + 3);
            if (have && a == first) return true;
            if (!have) { first = a; have = true; }
        }
        return false;
    }

    private List<uint> CallSites(uint target)
    {
        var (tva, text) = TextSection();
        var wanted = new HashSet<uint> { target };
        for (int i = 0; i + 5 <= text.Length; i++)
            if (text[i] == 0xE9 &&
                (uint)(tva + i + 5 + BitConverter.ToInt32(text, i + 1)) == target)
                wanted.Add((uint)(tva + i));
        var outp = new List<uint>();
        for (int i = 0; i + 5 <= text.Length; i++)
            if (text[i] == 0xE8 &&
                wanted.Contains((uint)(tva + i + 5 + BitConverter.ToInt32(text, i + 1))))
                outp.Add((uint)(tva + i));
        return outp;
    }

    /// <summary>The push arguments immediately before a call. Register pushes
    /// come back as -1, which is how the clearing loop is told from the rest.</summary>
    private List<int> PushesBefore(uint va, int span)
    {
        var b = Read(va - (uint)span, span);
        var outp = new List<int>();
        for (int i = 0; i < b.Length;)
        {
            if (b[i] == 0x6A && i + 1 < b.Length) { outp.Add(b[i + 1]); i += 2; }
            else if (b[i] >= 0x50 && b[i] <= 0x57) { outp.Add(-1); i++; }
            else i++;
        }
        return outp;
    }

    /// <summary>Walk one mission's branch. It ends where the code stops being
    /// pushes and calls — in the 1997 build that is the <c>push 0x5029a8</c> at
    /// 0x4882DD, which no longer belongs to the diplomacy.</summary>
    private bool[,]? Matrix(uint start)
    {
        var m = new bool[8, 8];
        for (int p = 0; p < 8; p++) m[p, p] = true;        // the 8x8 init loop
        uint at = start;
        var pend = new List<int>();
        for (int guard = 0; guard < 4000; guard++)
        {
            var b = Read(at, 8);
            if (b.Length < 8) return null;
            if (b[0] == 0x6A) { pend.Add(b[1]); at += 2; }              // push imm8
            else if (b[0] == 0xE8)                                      // call rel32
            {
                uint t = Resolve((uint)(at + 5 + BitConverter.ToInt32(b, 1)));
                if (LooksLikeSetRelation(t))
                {
                    if (pend.Count < 3) return null;
                    // cdecl: the last push is the first argument
                    int v = pend[^3], q = pend[^2], p = pend[^1];
                    if (p is < 0 or > 7 || q is < 0 or > 7) return null;
                    m[p, q] = v != 0;
                    m[q, p] = v != 0;                                   // @0x4cf6ee
                }
                pend.Clear();
                at += 5;
            }
            else if (b[0] == 0xE9) at = (uint)(at + 5 + BitConverter.ToInt32(b, 1));
            else if (b[0] == 0xEB) at = (uint)(at + 2 + (sbyte)b[1]);
            else if (b[0] == 0x83 && b[1] == 0xC4) at += 3;             // add esp, imm8
            else return m;                                              // the end
        }
        return null;                                                    // runaway
    }

    private List<bool[,]>? AllMatrices(uint idx, uint tab)
    {
        var list = new List<bool[,]>();
        for (int mission = 1; mission <= CampaignMissions; mission++)
        {
            var c = Read((uint)(idx + mission - 1), 1);
            if (c.Length < 1 || c[0] >= MissionCaseCount) return null;
            var t = Read((uint)(tab + 4 * c[0]), 4);
            if (t.Length < 4) return null;
            var m = Matrix(BitConverter.ToUInt32(t, 0));
            if (m == null) return null;
            list.Add(m);
        }
        return list;
    }

    /// <summary>The diplomacy of one campaign mission, 1-based, or null when
    /// this executable does not carry a mission_init this can find.</summary>
    public Diplomacy? CampaignDiplomacy(int mission)
    {
        if (mission < 1 || mission > CampaignMissions) return null;
        if (!HasCampaignDiplomacy) return null;

        var c = Read((uint)(DiplomacyIndex + mission - 1), 1);
        if (c.Length < 1 || c[0] >= MissionCaseCount) return null;
        var t = Read((uint)(DiplomacyTable + 4 * c[0]), 4);
        if (t.Length < 4) return null;
        var m = Matrix(BitConverter.ToUInt32(t, 0));
        if (m == null) return null;

        var d = new Diplomacy { Mission = mission, Allied = m };
        d.Neutral[NeutralPlayer] = true;
        return d;
    }

    /// <summary>All 33, for the self-test.</summary>
    public List<Diplomacy> CampaignDiplomacyAll()
    {
        var list = new List<Diplomacy>();
        for (int m = 1; m <= CampaignMissions; m++)
        {
            var d = CampaignDiplomacy(m);
            if (d != null) list.Add(d);
        }
        return list;
    }

    // ---- "Rohstoffe: keine / wenige / normal / viele" -----------------------

    /// <summary>What one setting of the skirmish's resource option puts into the
    /// buildings. The game's own word for it is in <see cref="Name"/>.</summary>
    public sealed class ResourceLevel
    {
        public int Level;
        public string Name = "";
        /// <summary>Basis, Flughafen and Werft-Station: the three parts stores
        /// Waffen / Fahrwerk / Spezial.</summary>
        public int Weapons, Chassis, Special;
        /// <summary>The three factories: parts stores emptied, this much
        /// Terranium.</summary>
        public int Terranium;
        /// <summary>Mine and Feld-Rohstoffmine: what is left in the ground.</summary>
        public int Deposit;
    }

    /// <summary>How a building type is filled. Read off the routine's own jump
    /// table, not assigned by hand.</summary>
    public enum ResourceFill { None = 0, Stores = 1, Factory = 2, Mine = 3, Clear = 4 }

    /// <summary>The four settings, level 0..3.</summary>
    public uint ResourceStoreTable { get; private set; }
    public uint ResourceFactoryTable { get; private set; }
    public uint ResourceMineTable { get; private set; }
    public uint ResourceCaseIndex { get; private set; }
    public uint ResourceCaseTable { get; private set; }
    public uint ResourceLabelTable { get; private set; }
    /// <summary>Where the option itself lives — 0x54079c in the 1997 build.</summary>
    public uint ResourceOptionVar { get; private set; }
    public uint ResourceDispatch { get; private set; }

    private bool _resTried;
    public const int ResourceLevelCount = 4;
    /// <summary>The routine walks 255 building slots, not the full 300
    /// (@0x41A0C0: <c>inc cl</c> against 0xFF).</summary>
    public const int ResourceSlotsScanned = 255;

    public bool HasResourceTables
    {
        get { LocateResources(); return ResourceStoreTable != 0 && ResourceLabelTable != 0; }
    }

    /// <summary>
    /// Find <c>fill_resources</c> and read its three tables out of its own
    /// instructions.
    ///
    /// <para>The routine is @0x419fe0 in the 1997 build and is called from
    /// exactly ONE place — @0x41ACA0, inside the game-start message handler
    /// @0x4c2280, right after <c>byte[0x54079c]</c> has been set from the
    /// options packet (@0x4C40F8). So the option belongs to a NETWORK or
    /// SKIRMISH game and never runs in a campaign mission, which keeps its
    /// stores from the level file. One caller is a fact, not an impression.</para>
    ///
    /// <para>It walks the building records (base 0xc06910, stride 76, type at
    /// +0x04) and switches on the type through an index byte and a jump table:
    /// <list type="bullet">
    /// <item><b>Basis (1), Flughafen (9), Werft-Station (16)</b> get three u16
    /// from the first table into +0x2c/+0x2e/+0x30 — Waffen, Fahrwerk,
    /// Spezial;</item>
    /// <item><b>the three factories (2, 3, 4)</b> get those three set to ZERO
    /// and Terranium at +0x32 from the second table;</item>
    /// <item><b>Mine (10) and Feld-Rohstoffmine (15)</b> get the deposit table's
    /// value written into the deposit array (0x878adc, stride 18, index from
    /// record +0x19);</item>
    /// <item>everything else from 1 to 16 is emptied, and type 0 is skipped.
    /// </item>
    /// </list>
    /// None of that is assigned here — the mapping is read back out of the jump
    /// table, and a branch is identified by which of the three table reads falls
    /// inside it.</para>
    ///
    /// <para>The numbers are a clean doubling ladder, which is what makes the
    /// reading safe: <b>0/0/0 · 150/200/100 · 300/400/200 · 600/800/400</b>,
    /// Terranium <b>0 · 500 · 1000 · 2000</b>, deposits <b>0 · 1000 · 3000 ·
    /// 9000</b>. The four names are the game's own — <c>keine</c>,
    /// <c>wenige</c>, <c>normal</c>, <c>viele</c> — taken from the menu code
    /// @0x480B5D, which switches the same variable over four string pointers, so
    /// the order of the names comes from the game and not from the ladder.</para>
    ///
    /// <para>Found by shape in both builds on this machine: tables at 0x4f7f70 /
    /// 0x4f7f88 / 0x4f7f90 in one and 0x4f6f50 / 0x4f6f68 / 0x4f6f70 in the
    /// other, and the same twelve numbers in each.</para>
    /// </summary>
    private void LocateResources()
    {
        if (_resTried) return;
        _resTried = true;
        var (tva, text) = TextSection();
        if (text.Length == 0) return;

        // the dispatch: cmp eax,0x10 / ja / xor ebx,ebx / mov bl,[eax+idx] /
        //               jmp dword [ebx*4 + tab]
        for (int i = 0; i + 20 < text.Length; i++)
        {
            if (text[i] != 0x83 || text[i + 1] != 0xF8 || text[i + 2] != 0x10 ||
                text[i + 3] != 0x77) continue;
            if (text[i + 5] != 0x33 || text[i + 6] != 0xDB ||
                text[i + 7] != 0x8A || text[i + 8] != 0x98) continue;
            if (text[i + 13] != 0xFF || text[i + 14] != 0x24 || text[i + 15] != 0x9D) continue;

            uint idx = BitConverter.ToUInt32(text, i + 9);
            uint tab = BitConverter.ToUInt32(text, i + 16);

            // the three table reads, inside the branches that follow
            uint store = 0, fact = 0, mine = 0;
            uint storeAt = 0, factAt = 0, mineAt = 0;
            for (int k = i; k < i + 0x100 && k + 8 < text.Length; k++)
            {
                if (text[k] != 0x66 || text[k + 1] != 0x8B) continue;
                uint at = (uint)(tva + k);
                // mov ax, [ebx*2 + imm32] / mov ax, [eax*2 + imm32] /
                // mov di, [eax*2 + imm32]
                if (text[k + 2] == 0x04 && text[k + 3] == 0x5D && store == 0)
                { store = BitConverter.ToUInt32(text, k + 4); storeAt = at; }
                else if (text[k + 2] == 0x04 && text[k + 3] == 0x45 && fact == 0)
                { fact = BitConverter.ToUInt32(text, k + 4); factAt = at; }
                else if (text[k + 2] == 0x3C && text[k + 3] == 0x45 && mine == 0)
                { mine = BitConverter.ToUInt32(text, k + 4); mineAt = at; }
            }
            if (store == 0 || fact == 0 || mine == 0) continue;
            if (Offset(store) < 0 || Offset(fact) < 0 || Offset(mine) < 0) continue;

            ResourceDispatch = (uint)(tva + i);
            ResourceCaseIndex = idx;
            ResourceCaseTable = tab;
            ResourceStoreTable = store;
            ResourceFactoryTable = fact;
            ResourceMineTable = mine;
            _resStoreAt = storeAt; _resFactAt = factAt; _resMineAt = mineAt;
            break;
        }
        if (ResourceStoreTable == 0) return;

        // the menu: mov al,[var] / cmp eax,3 / ja / jmp [eax*4 + tab], then four
        // `mov edi, <string>` — that is where the four names come from, in the
        // order the option counts them
        for (int i = 0; i + 32 < text.Length; i++)
        {
            if (text[i] != 0xA0 || text[i + 5] != 0x83 || text[i + 6] != 0xF8 ||
                text[i + 7] != 0x03 || text[i + 8] != 0x77) continue;
            if (text[i + 10] != 0xFF || text[i + 11] != 0x24 || text[i + 12] != 0x85) continue;
            ResourceOptionVar = BitConverter.ToUInt32(text, i + 1);
            ResourceLabelTable = BitConverter.ToUInt32(text, i + 13);
            break;
        }
    }

    private uint _resStoreAt, _resFactAt, _resMineAt;

    /// <summary>Which fill a building type gets. The branch a type jumps to is
    /// identified by which table read falls inside it — the branches lie one
    /// after another, so a branch runs to the start of the next one.</summary>
    public ResourceFill ResourceFillOf(int buildingType)
    {
        if (!HasResourceTables) return ResourceFill.None;
        if (buildingType is < 0 or > 16) return ResourceFill.Clear;   // @0x41A016
        var c = Read((uint)(ResourceCaseIndex + buildingType), 1);
        if (c.Length < 1) return ResourceFill.None;

        // every distinct branch start, sorted, so each one's extent is known
        var starts = new List<uint>();
        for (int t = 0; t <= 16; t++)
        {
            var b = Read((uint)(ResourceCaseIndex + t), 1);
            if (b.Length < 1) continue;
            var tg = Read((uint)(ResourceCaseTable + 4 * b[0]), 4);
            if (tg.Length < 4) continue;
            uint v = BitConverter.ToUInt32(tg, 0);
            if (!starts.Contains(v)) starts.Add(v);
        }
        starts.Sort();

        var mine2 = Read((uint)(ResourceCaseTable + 4 * c[0]), 4);
        if (mine2.Length < 4) return ResourceFill.None;
        uint start = BitConverter.ToUInt32(mine2, 0);
        uint end = uint.MaxValue;
        foreach (uint s in starts) if (s > start && s < end) end = s;

        if (_resStoreAt >= start && _resStoreAt < end) return ResourceFill.Stores;
        if (_resFactAt >= start && _resFactAt < end) return ResourceFill.Factory;
        if (_resMineAt >= start && _resMineAt < end) return ResourceFill.Mine;
        // the highest branch is the loop's own end — type 0 falls straight there
        return start >= end || starts.Count == 0 || start == starts[^1]
            ? ResourceFill.None : ResourceFill.Clear;
    }

    /// <summary>The four settings with the game's own names.</summary>
    public List<ResourceLevel> ResourceLevels()
    {
        var list = new List<ResourceLevel>();
        if (!HasResourceTables) return list;
        for (int l = 0; l < ResourceLevelCount; l++)
        {
            var s = Read((uint)(ResourceStoreTable + l * 6), 6);
            var f = Read((uint)(ResourceFactoryTable + l * 2), 2);
            var m = Read((uint)(ResourceMineTable + l * 2), 2);
            if (s.Length < 6 || f.Length < 2 || m.Length < 2) break;
            string name = "";
            var p = Read((uint)(ResourceLabelTable + l * 4), 4);
            if (p.Length == 4)
            {
                // the jump table holds CODE addresses; the string pointer is the
                // `mov edi, imm32` at the start of that little branch
                var br = Read(BitConverter.ToUInt32(p, 0), 5);
                if (br.Length == 5 && br[0] == 0xBF)
                {
                    var txt = Read(BitConverter.ToUInt32(br, 1), 16);
                    int n = Array.IndexOf(txt, (byte)0);
                    if (n > 0) name = Str(txt, 0, n);
                }
            }
            list.Add(new ResourceLevel
            {
                Level = l,
                Name = name,
                Weapons = BitConverter.ToUInt16(s, 0),
                Chassis = BitConverter.ToUInt16(s, 2),
                Special = BitConverter.ToUInt16(s, 4),
                Terranium = BitConverter.ToUInt16(f, 0),
                Deposit = BitConverter.ToUInt16(m, 0),
            });
        }
        return list;
    }
}
