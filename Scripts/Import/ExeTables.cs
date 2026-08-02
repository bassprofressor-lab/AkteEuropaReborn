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
}
