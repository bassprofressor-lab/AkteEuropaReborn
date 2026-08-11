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

    /// <summary>Das ZWEITE Wort jeder Zeile des Klangsatzes (+0x02).
    ///
    /// <para>Es ist ebenfalls eine Klang-Nummer, keine Bildfolge. Belegt
    /// @0x4048cf: das ist <b>Modus 3</b> der Klangroutine
    /// (0x4047e0, Sprungtabelle 0x404a18): <c>mov cl,[eax+0x884736]</c> holt die
    /// Klangklasse aus dem GESCHOSS-Satz (Array 0x884730, Schrittweite 32), <c>ecx = cl*11</c>, dann <c>mov si,[ecx*2+0x4f98f4]</c> —
    /// also genau dieses Feld. Danach folgt die Klangroutine: <c>cmp si,0x18f</c>
    /// und bei Gleichheit <c>rand()%6 + 0x190</c>, anschliessend Abstand zur
    /// Kamera (0x5387c0/0x5387c4), <c>fsqrt</c>, Panning und die Bereiche
    /// <c>0x78 / 0x12c / 0x1f4 / 0x7d0</c>.</para>
    ///
    /// <para>⚠ Am 11.08.2026 stand hier kurz „ANIM.CWA-Folge des
    /// Muendungsfeuers". Das war falsch. Die Werte (232, 102, 143, 0) sahen wie
    /// Folgen aus, aber <b>143 ist in ANIM.CWA leer</b> — daran fiel es auf,
    /// bevor die Fehldeutung in einer Auslieferung landete. Was im Original ein
    /// Muendungsfeuer bekommt, ist damit weiterhin ungelesen.</para>
    ///
    /// <para>Noch nicht verdrahtet: welcher Satz bei 0x884730 steht (Geschoss?
    /// Einheit?) ist offen, und ohne das waere jedes Abspielen geraten.</para>
    /// </summary>
    public int[] SecondSounds()
    {
        var list = new int[FireSoundRows];
        for (int r = 0; r < FireSoundRows; r++)
        {
            var b = Read((uint)(FireSoundBase + r * FireSoundStride + 2), 2);
            list[r] = b.Length < 2 ? 0 : b[0] | (b[1] << 8);
        }
        return list;
    }

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
        public int Index, Speed, Hp, Payload, Airframe, Attack, Defence, Sight, Ammo, Fuel;
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
                // ⚠ 11.08.2026 nachgetragen: +0x21 ist die GESCHWINDIGKEIT, und
                // sie fehlte hier. aircraft.json trug deshalb gar kein "speed",
                // die Fluglogik las eine 0 und flog mit Max(1,0) -- jedes
                // Flugzeug war langsamer als ein Fahrzeug.
                //
                // Belegt von der anderen Seite: sec120 einer Karte traegt
                // dieselben Saetze mit einem fuehrenden Freigabe-Byte, und
                // CwmExtra.AirDesigns liest Speed dort an +0x22, Hp an +0x23 --
                // also genau um eins versetzt zu hier. Die Werte passen zum
                // Spiel: Jagdflieger 25, Spion 20, Bomber 10, Helis 8..10.
                Speed = r[0x21],
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

    // ---- the computer players' build programmes ("vyroba") -----------------

    /// <summary>One line of a build programme: three bytes, as the game stores
    /// them. <c>Kind</c> 0 builds a unit design in a base, 1 buys an aircraft at
    /// an airport; a 2 also occurs and <c>ai_production</c> ignores it, so it is
    /// carried through rather than dropped. <c>Third</c> is never read by the
    /// interpreter and is kept only so a comparison can be byte-exact.</summary>
    public readonly record struct BuildStep(int Kind, int What, int Third);

    /// <summary>Where the campaign's build programmes come from.
    ///
    /// The computer player produces by walking a table of up to 50 lines per
    /// player and executing one line every 50 ticks (`ai_production` @0x4BB9A0,
    /// buffer 0xbc51d0). ⚠ That table is section 63 of a full-state file, and a
    /// campaign level carries **no such section**: the loader only reads
    /// sections 39..131 when the header byte at +3 is 2, and every .CWM has 1
    /// there while every .DM has 2. For a level the buffer is set to 0xFF —
    /// empty — and then filled **by code**.
    ///
    /// That code is dispatched on the mission number:
    /// <code>
    ///   eax = word[0x539934]              ; the mission
    ///   if eax > 99: nothing
    ///   cl  = byte[eax + caseIndex]       ; 100 entries
    ///   jmp [ecx*4 + caseTable]           ; one straight-line block per mission
    /// </code>
    /// and each block is a run of <c>add_vyroba(player, kind, what, third)</c>
    /// calls with all four arguments pushed as constants.
    ///
    /// Found by shape, not by address (rule 8): the adder names itself with
    /// »Cannot add new 'vyroba'«, and among the several <c>cmp eax,0x63</c> in
    /// the binary the mission one is the only whose blocks call it.
    ///
    /// Checked against the game's own saved states: 26 of the 27 programmes
    /// stored in the 13 .DM appear verbatim in their mission's block. The one
    /// that does not is 1.DM's player 1 — its sequence occurs nowhere in the
    /// binary, and the open lead for it is the block of 140 adder calls at
    /// 0x41A2xx whose arguments come out of registers instead of constants.
    /// </summary>
    public uint VyrobaMissionVar, VyrobaCaseIndex, VyrobaCaseTable, AddVyroba;

    public const int MissionSlots = 100;

    private bool _missionTried;

    /// <summary>mission -> player -> its lines, in order.</summary>
    private readonly Dictionary<int, Dictionary<int, List<BuildStep>>> _missionPlans = new();

    public bool HasMissionPlans { get { LocateMissionPlans(); return _missionPlans.Count > 0; } }

    /// <summary>The programmes of one mission, or null if it sets none up.</summary>
    public IReadOnlyDictionary<int, List<BuildStep>>? MissionPlan(int mission)
    {
        LocateMissionPlans();
        return _missionPlans.TryGetValue(mission, out var p) ? p : null;
    }

    public IReadOnlyDictionary<int, Dictionary<int, List<BuildStep>>> MissionPlans
    {
        get { LocateMissionPlans(); return _missionPlans; }
    }

    /// <summary>Raw file offset to virtual address, or 0 outside every
    /// section — the inverse of <see cref="Offset"/>, needed to turn the
    /// position of a string into something the code can point at.</summary>
    private uint VaOf(int raw)
    {
        foreach (var s in _sections)
            if (raw >= s.Raw && raw < s.Raw + s.RawSize)
                return (uint)(s.Va + (raw - s.Raw));
        return 0;
    }

    private void LocateMissionPlans()
    {
        if (_missionTried) return;
        _missionTried = true;
        var (tva, text) = TextSection();
        if (text.Length == 0) return;

        // 1. the adder, by the message only it prints
        uint adder = FindAddVyroba(tva, text);
        if (adder == 0) return;
        AddVyroba = adder;

        // 2. every thunk that jumps to it — this build is linked incrementally,
        //    so most calls go through one
        var targets = new HashSet<uint> { adder };
        for (int i = 0; i + 5 <= text.Length; i++)
            if (text[i] == 0xE9 &&
                (uint)(tva + i + 5 + BitConverter.ToInt32(text, i + 1)) == adder)
                targets.Add((uint)(tva + i));

        // 3. every call to it whose four arguments are constants, with the
        //    pushes read backwards: the one nearest the call is the player
        var sites = new List<(uint At, int Player, int Kind, int What, int Third)>();
        for (int i = 0; i + 5 <= text.Length; i++)
        {
            if (text[i] != 0xE8) continue;
            if (!targets.Contains((uint)(tva + i + 5 + BitConverter.ToInt32(text, i + 1)))) continue;
            int k = i;
            var c = new int[4];
            bool ok = true;
            for (int n = 0; n < 4; n++)
            {
                if (k >= 2 && text[k - 2] == 0x6A) { c[n] = text[k - 1]; k -= 2; }
                else if (k >= 5 && text[k - 5] == 0x68)
                { c[n] = BitConverter.ToInt32(text, k - 4); k -= 5; }
                else { ok = false; break; }
            }
            if (ok) sites.Add(((uint)(tva + i), c[0], c[1], c[2], c[3]));
        }
        if (sites.Count == 0) return;
        sites.Sort((a, b) => a.At.CompareTo(b.At));

        // 4. the dispatch, identified by what its blocks do
        if (!FindMissionDispatch(tva, text, sites)) return;

        // 5. every mission's block, cut at the next block start. The blocks lie
        //    one after another, so an address range is enough — checked to give
        //    the same 26 of 27 as walking the code with a disassembler does.
        var idx = Read(VyrobaCaseIndex, MissionSlots);
        if (idx.Length < MissionSlots) return;
        int cases = 0;
        foreach (var b in idx) cases = Math.Max(cases, b + 1);
        var tab = Read(VyrobaCaseTable, cases * 4);
        if (tab.Length < cases * 4) return;

        var blocks = new uint[cases];
        for (int i = 0; i < cases; i++) blocks[i] = BitConverter.ToUInt32(tab, i * 4);
        var starts = new List<uint>(new HashSet<uint>(blocks));
        starts.Sort();

        // ⚠ The value register is not always set inside the block. The
        // dispatcher's own prologue loads it before the indexed jump, and it
        // holds for every mission — mission 19 reads its `push ebx` from there
        // and nowhere else. Collect it once and hand it to each block.
        var prologue = new Dictionary<int, int>();
        for (int i = 0; i + 7 < text.Length; i++)
        {
            if (text[i] != 0xFF || text[i + 1] != 0x24 || text[i + 2] != 0x8D) continue;
            if (BitConverter.ToUInt32(text, i + 3) != VyrobaCaseTable) continue;
            var pre = PrevPushes(text, i + 7, 0x400);
            if (pre != null)
                foreach (var kv in pre.Value.Consts) prologue.TryAdd(kv.Key, kv.Value);
            break;
        }

        for (int m = 0; m < MissionSlots; m++)
        {
            uint start = blocks[idx[m]];
            uint end = uint.MaxValue;
            foreach (var s in starts) if (s > start) { end = s; break; }

            var per = new Dictionary<int, List<BuildStep>>();
            foreach (var (at, player, kind, what, third) in sites)
            {
                if (at < start) continue;
                if (at >= end) break;
                if (!per.TryGetValue(player, out var list))
                    per[player] = list = new List<BuildStep>();
                list.Add(new BuildStep(kind, what, third));
            }
            if (per.Count > 0) _missionPlans[m] = per;
        }
    }

    // ---- what a mission SWITCHES ON ----------------------------------------

    /// <summary>A run of rows a mission turns on or off. The schedule is written
    /// as loops, so a single call site usually covers a whole range.
    ///
    /// <para><c>Player</c> is -1 for the three setters that write the row for
    /// ALL EIGHT players at once (they loop `xor cl,cl … cmp cl,8; jb` inside
    /// the setter). Only <c>part</c> names a player, and it is the busiest of
    /// the four by a factor of four.</para></summary>
    public readonly record struct UnlockRange(string Kind, int Player, int From, int To, int Value);

    private bool _unlockTried;
    private readonly Dictionary<int, List<UnlockRange>> _missionUnlocks = new();

    /// <summary>
    /// The per-mission unlock schedule — the thing campaign.json has carried an
    /// `_open` note about: "not derived here, it is a run of call sites in the
    /// exe, not a table".
    ///
    /// The same mission blocks that hold the build programmes also call four
    /// tiny setters, each of which writes byte +0 of a record:
    ///
    ///     design   into the 46-byte design table, for all eight players
    ///     ship     into the 42-byte ship table
    ///     aircraft into the aircraft templates
    ///     stat     into a 58-byte stats record of ONE player
    ///
    /// They are recognised by what they write into — three of the four
    /// destinations are tables this class already knows by name, and the fourth
    /// is the design table.
    ///
    /// ⚠ The arguments are usually NOT constants. The schedule is written as
    /// loops, and the value is often held in a register across a long run of
    /// calls. Both forms are matched here on raw bytes:
    ///
    ///     6A vv  5x  FE Cx  E8 rel32  83 C4 08  80 Fx ee   -> a loop, rows
    ///            |   |                          |             vv..ee-1 := value
    ///            |   the running index          its end
    ///            the value
    ///
    ///     6A vv  6A ii  E8 rel32                          -> one row
    ///     5x     6A ii  E8 rel32                          -> one row, value in
    ///                                                        a register
    ///
    /// Checked against `aekernel-tools/mission_unlocks.py`, which reads the same
    /// thing with a real disassembler.
    /// </summary>
    public IReadOnlyDictionary<int, List<UnlockRange>> MissionUnlocks
    {
        get { LocateMissionUnlocks(); return _missionUnlocks; }
    }

    /// <summary>`push r32` opcode to the register number its 8-bit low half
    /// uses in `inc r8`, `mov r8,imm8` and `cmp r8,imm8`.</summary>
    private static int PushReg(byte op) => op is >= 0x50 and <= 0x57 ? op - 0x50 : -1;

    /// <summary>One pushed argument: either an immediate, or a register whose
    /// constant has to be looked up.</summary>
    private readonly record struct Arg(int Imm, int Reg);

    /// <summary>
    /// The last <paramref name="want"/> arguments pushed in front of a call,
    /// walking BACKWARDS. <c>found[0]</c> is the argument NEAREST the call —
    /// with cdecl that is the FIRST parameter, so for `set_part(player, part,
    /// value)` it is the player, `found[1]` the part and `found[2]` the value.
    ///
    /// ⚠ The pushes are not always adjacent — a block happily puts
    /// `xor ebx,ebx` or `inc bl` between them — so matching a fixed byte
    /// sequence in front of the call misses them. Reading backwards needs
    /// instruction lengths, and rather than a full decoder this steps over the
    /// small set of forms these blocks actually contain. Anything else stops
    /// the walk, which is the safe direction: a missed row shows up in
    /// `--selftest-exe`, a wrongly guessed one might not.
    ///
    /// ⚠ 10.08.2026: `want` used to be hard-wired to two, and `set_part` — 1037
    /// of the 1533 setter calls in the mission blocks — takes THREE. Read with
    /// the two-argument rule it came out as (index = the player, value = the
    /// part number), which is not a near miss but noise.
    /// </summary>
    private static (List<Arg> Args, Dictionary<int, int> Consts)?
        PrevPushes(byte[] text, int call, int back = 0x200, int want = 2)
    {
        var found = new List<Arg>();
        var consts = new Dictionary<int, int>();
        // ⚠ `xor ebx,ebx / inc ebx` is how a 1 gets into a register here.
        // Walking backwards we meet the `inc` FIRST, so the steps are counted
        // and added when the setter finally turns up. Missing `inc r32` (0x40+r)
        // entirely was what made mission 19 read the wrong value: the walk
        // stopped on it and the raw fallback then found a byte inside an
        // address and called it `mov ebx, 2214590308`.
        // ⚠ Only steps that stand BEFORE the pushes in program order count —
        // walking backwards, that means only after both pushes have been seen.
        // The `inc bl` inside a loop body sits between the push and the call and
        // must NOT be counted: the loop pushes the value it had before it.
        var adj = new Dictionary<int, int>();
        void Bump(int r, int by) { if (found.Count >= want) adj[r] = (adj.TryGetValue(r, out int a) ? a : 0) + by; }
        // ⚠ A constant out of 0..255 is not one. Every argument in this
        // schedule is a byte — a player, a row, a 0/1 flag — so a `mov r32,
        // imm32` carrying 0x84022BE4 is the backward walk having stepped onto a
        // byte inside an address, not a value. Mission 19 shipped exactly that
        // as component number -2080376988 the first time `set_part` was read:
        // the same `push ebx` that had always been the harmless VALUE (which
        // Flag() rounded back to 1) is its INDEX as well.
        void Set(int r, int v)
        {
            v += adj.TryGetValue(r, out int a) ? a : 0;
            if (v is >= 0 and <= 255) consts.TryAdd(r, v);
        }
        int p = call;                                  // p = end of the previous instruction
        int limit = Math.Max(1, call - back);
        while (p > limit)
        {
            if (p >= 2 && text[p - 2] == 0x6A)                       // push imm8
            { if (found.Count < want) found.Add(new Arg(text[p - 1], -1)); p -= 2; continue; }
            if (p >= 5 && text[p - 5] == 0x68)                       // push imm32
            { if (found.Count < want) found.Add(new Arg(BitConverter.ToInt32(text, p - 4), -1)); p -= 5; continue; }
            if (p >= 1 && PushReg(text[p - 1]) >= 0)                 // push r32
            { if (found.Count < want) found.Add(new Arg(0, PushReg(text[p - 1]))); p -= 1; continue; }
            if (p >= 2 && (text[p - 2] == 0x32 || text[p - 2] == 0x33) &&
                (text[p - 1] & 0xC0) == 0xC0 && ((text[p - 1] >> 3) & 7) == (text[p - 1] & 7))
            { Set(text[p - 1] & 7, 0); p -= 2; continue; }                          // xor r,r
            if (p >= 2 && text[p - 2] is >= 0xB0 and <= 0xB7)                      // mov r8,imm8
            { Set(text[p - 2] - 0xB0, text[p - 1]); p -= 2; continue; }
            if (p >= 5 && text[p - 5] is >= 0xB8 and <= 0xBF)                      // mov r32,imm32
            { Set(text[p - 5] - 0xB8, BitConverter.ToInt32(text, p - 4)); p -= 5; continue; }
            if (p >= 2 && text[p - 2] == 0xFE && (text[p - 1] & 0xF8) == 0xC0)       // inc r8
            { Bump(text[p - 1] & 7, 1); p -= 2; continue; }
            if (p >= 2 && text[p - 2] == 0xFE) { p -= 2; continue; }                 // dec r8 etc.
            if (p >= 1 && text[p - 1] is >= 0x40 and <= 0x47)                        // inc r32
            { Bump(text[p - 1] - 0x40, 1); p -= 1; continue; }
            if (p >= 1 && text[p - 1] is >= 0x48 and <= 0x4F)                        // dec r32
            { Bump(text[p - 1] - 0x48, -1); p -= 1; continue; }
            // 83 /r imm8 — add/sub/cmp on a register with a small immediate;
            // `add esp,8` and `cmp eax,0x63` are both of this shape
            if (p >= 3 && text[p - 3] == 0x83 && (text[p - 2] & 0xC0) == 0xC0) { p -= 3; continue; }
            if (p >= 7 && text[p - 7] == 0x0F && text[p - 6] == 0xBF &&
                text[p - 5] == 0x05) { p -= 7; continue; }                        // movsx r32,[imm32]
            if (p >= 6 && text[p - 6] == 0x0F && (text[p - 5] & 0xF0) == 0x80) { p -= 6; continue; } // jcc rel32
            if (p >= 6 && text[p - 6] == 0x8A && (text[p - 5] & 0xC7) == 0x80) { p -= 6; continue; } // mov r8,[r32+imm32]
            if (p >= 5 && text[p - 5] == 0xE8) { p -= 5; continue; }                 // call rel32
            // the blocks set their own flags between the pushes
            if (p >= 6 && text[p - 6] == 0x88 && (text[p - 5] & 0xC7) == 0x05) { p -= 6; continue; }
            if (p >= 5 && text[p - 5] == 0xA2) { p -= 5; continue; }
            if (p >= 7 && text[p - 7] == 0xC6 && text[p - 6] == 0x05) { p -= 7; continue; }
            // ⚠ …and their own WORDS. Without these three the walk stopped
            // dead in mission 8 at `mov word ptr [0xc06bec], 0x5f` and never
            // reached the `xor ebx,ebx` that sets the player register at the
            // top of the block — the whole mission came back with no component
            // rows at all, and nothing else would have said so.
            if (p >= 10 && text[p - 10] == 0xC7 && text[p - 9] == 0x05) { p -= 10; continue; }
            if (p >= 9 && text[p - 9] == 0x66 && text[p - 8] == 0xC7 &&
                text[p - 7] == 0x05) { p -= 9; continue; }
            if (p >= 6 && text[p - 6] == 0x66 &&
                (text[p - 5] == 0xA3 || text[p - 5] == 0xA1)) { p -= 6; continue; }
            break;
        }
        return found.Count == want ? (found, consts) : null;
    }

    /// <summary>
    /// The constant a register was last loaded with, scanning back over a
    /// bounded window.
    ///
    /// Three forms, and all three are needed:
    ///   B0+r imm8    `mov r8, imm8`   — the loop counters, bl and cl
    ///   B8+r imm32   `mov r32, imm32` — ⚠ how esi and edi get theirs. In 32-bit
    ///                encoding index 6 and 7 have no own low byte (B6 is `mov
    ///                dh`), so the value register the blocks hold across a run
    ///                of calls is ALWAYS set in the 32-bit form. Leaving this
    ///                out made every mission that uses esi come back empty.
    ///   3x C0+r*9    `xor r,r`
    ///
    /// ⚠ The scan runs back to the START OF THE BLOCK, not over a fixed window.
    /// A block sets its value register once at the top and then holds it across
    /// every call it makes; a 0x120-byte window found it in some missions and
    /// not in others, which read as "this mission unlocks nothing".
    /// </summary>
    /// <remarks>
    /// ⚠ A hit whose value is outside 0..255 is SKIPPED, not returned. Every
    /// argument in this schedule is a byte, so `mov ebx, 0x83FFF764` is the raw
    /// scan having landed on a byte inside a call offset — and returning at the
    /// first hit meant mission 19 cached that number for the rest of its block.
    /// It stayed invisible for as long as ebx was only ever the VALUE, because
    /// <see cref="Flag"/> rounded anything outside 0..1 back to 1; the moment
    /// `set_part` made the same register the INDEX, out came component
    /// -2080376988. Carrying on down the block finds the real `mov ebx, 1`
    /// 0x3DE bytes further back.
    /// </remarks>
    private static int? RegConst(byte[] text, int at, int reg, int floor = 0,
                                 bool[]? inside = null, int origin = 0)
    {
        for (int i = at - 1; i >= Math.Max(0, floor); i--)
        {
            // ⚠ …and a hit INSIDE a call offset is not one either. `call rel32`
            // is `E8 b3 95 f7 ff`, and its second byte reads as `mov bl, 0x95`.
            // That is where the other GAME.EXE handed mission 13 component 149
            // instead of component 1: the false `mov` stands 0x2C3 bytes closer
            // to the call site than the real one at the top of the block.
            if (inside != null)
            {
                int k = i - origin;
                if (k >= 0 && k < inside.Length && inside[k]) continue;
            }
            if (text[i] == 0xB0 + reg && i + 1 < text.Length) return text[i + 1];
            if (text[i] == 0xB8 + reg && i + 4 < text.Length)
            {
                int v = BitConverter.ToInt32(text, i + 1);
                if (v is >= 0 and <= 255) return v;
                continue;
            }
            if ((text[i] == 0x32 || text[i] == 0x33) &&
                i + 1 < text.Length && text[i + 1] == 0xC0 + reg * 9) return 0;
        }
        return null;
    }

    /// <summary>
    /// The bytes of a block that sit INSIDE the 4-byte displacement of a
    /// `call`/`jmp rel32` — the ones the raw register search must not read an
    /// instruction out of.
    ///
    /// <para>A stray 0xE8 in data almost never has a displacement that lands
    /// back inside .text (four random bytes reach ±2 GB), so »the target is a
    /// text address« is a sharp test for a real call.</para>
    /// </summary>
    private static bool[] InsideCallOffsets(byte[] text, uint tva, int from, int to)
    {
        var inside = new bool[Math.Max(0, to - from)];
        for (int q = from; q + 4 < to && q + 4 < text.Length; q++)
        {
            if (text[q] != 0xE8 && text[q] != 0xE9) continue;
            long target = (long)tva + q + 5 + BitConverter.ToInt32(text, q + 1);
            if (target < tva || target >= tva + text.Length) continue;
            for (int k = 1; k <= 4 && q + k - from < inside.Length; k++)
                inside[q + k - from] = true;
        }
        return inside;
    }

    /// <summary>
    /// The constant a register holds at a call.
    ///
    /// First choice is what the backward instruction walk carried in — that one
    /// cannot mistake a byte inside another instruction for a setter. Only when
    /// the walk stopped early (an opcode it does not know) does this fall back
    /// to the raw byte search, which is why the fallback is still here: without
    /// it three missions read nothing at all, with it they read correctly.
    /// </summary>
    /// <summary>
    /// The value a schedule row carries.
    ///
    /// ⚠ MEASURED, not assumed: every one of the 551 rows in this binary
    /// carries 1 — the campaign schedule only ever switches things ON, no row
    /// takes anything away. So where the value register cannot be resolved (it
    /// is loaded in the dispatcher's prologue, behind instruction forms the
    /// backward walk does not know), 1 is the norm rather than a guess, and
    /// anything outside 0..1 is a false positive of the raw byte search.
    ///
    /// If a disc ever turns up with an "off" row, this is where it goes wrong,
    /// and `--selftest-exe` is what will say so.
    /// </summary>
    private static int Flag(int? v) => v is 0 or 1 ? v.Value : 1;

    private static int? Held(byte[] text, int at, int reg, int floor,
                             Dictionary<int, int> held,
                             bool[]? inside = null, int origin = 0)
    {
        if (held.TryGetValue(reg, out int known)) return known;
        int? v = RegConst(text, at, reg, floor, inside, origin);
        if (v != null) held[reg] = v.Value;
        return v;
    }

    private void LocateMissionUnlocks()
    {
        if (_unlockTried) return;
        _unlockTried = true;
        LocateMissionPlans();
        if (VyrobaCaseTable == 0) return;
        var (tva, text) = TextSection();
        if (text.Length == 0) return;

        var idx = Read(VyrobaCaseIndex, MissionSlots);
        if (idx.Length < MissionSlots) return;
        int cases = 0;
        foreach (var b in idx) cases = Math.Max(cases, b + 1);
        var tab = Read(VyrobaCaseTable, cases * 4);
        if (tab.Length < cases * 4) return;
        var blocks = new uint[cases];
        for (int i = 0; i < cases; i++) blocks[i] = BitConverter.ToUInt32(tab, i * 4);
        var starts = new List<uint>(new HashSet<uint>(blocks));
        starts.Sort();

        // which functions the blocks call, and what each writes into
        uint lo = starts[0], hi = starts[^1] + 0x1000;
        var kindOf = new Dictionary<uint, string>();
        for (int i = (int)(lo - tva); i < Math.Min(hi - tva, text.Length - 5); i++)
        {
            if (text[i] != 0xE8) continue;
            uint t = Resolve((uint)(tva + i + 5 + BitConverter.ToInt32(text, i + 1)));
            if (kindOf.ContainsKey(t)) continue;
            if (SetterDest(tva, text, t) != 0) kindOf[t] = "";      // a setter, role later
        }
        if (kindOf.Count == 0) return;

        // Which of the four is which.
        //
        // The strongest discriminator is build-independent and needs no table
        // name at all: `set_part` is the ONLY one of the four that takes three
        // arguments, and a cdecl call site says so out loud — `add esp, 0xc`
        // behind the call instead of `add esp, 8`. Measured over every call
        // site: 1035 of 1037 say three on this build, 1091 of 1093 on the other
        // (the two stragglers each fold two cleanups into one `add esp`).
        //
        // The remaining three then follow from their ORDER. The four sit next
        // to each other in the same sequence in both builds (0x4D04D0/0520/
        // 0560/05A0 here, 0x4D0080/00D0/0110/0150 on the other), and thanks to
        // the SIB test and the address window in <see cref="SetterDest"/> they
        // are the only setters in this list.
        //
        // ⚠ The old first choice — recognising three of the four by the table
        // addresses this class knows by name — is kept as the fallback. Those
        // names are addresses of ONE build, so on the other GAME.EXE they
        // identify nothing and every setter reads as "design".
        var byAddr = new List<uint>(kindOf.Keys);
        byAddr.Sort();
        string[] roles = { "design", "part", "ship", "aircraft" };
        var arity = new Dictionary<uint, int>();
        foreach (var fn in byAddr) arity[fn] = ArityOf(tva, text, lo, hi, fn);

        int threes = 0;
        foreach (var fn in byAddr) if (arity[fn] == 3) threes++;
        if (byAddr.Count == roles.Length && threes == 1 && arity[byAddr[1]] == 3)
        {
            for (int r = 0; r < byAddr.Count; r++) kindOf[byAddr[r]] = roles[r];
        }
        else
        {
            bool named = false;
            foreach (var fn in byAddr)
            {
                uint dest = SetterDest(tva, text, fn);
                if (dest == StatsArrayBase) { kindOf[fn] = "part"; named = true; }
                else if (dest == ShipDesigns) { kindOf[fn] = "ship"; named = true; }
                else if (dest == AircraftTemplates - 1) { kindOf[fn] = "aircraft"; named = true; }
            }
            if (named)
            {
                foreach (var fn in byAddr) if (kindOf[fn].Length == 0) kindOf[fn] = "design";
            }
            else
            {
                for (int r = 0; r < byAddr.Count; r++)
                    kindOf[byAddr[r]] = r < roles.Length ? roles[r] : "design";
            }
            // whatever the names said, three arguments means `set_part`
            foreach (var fn in byAddr) if (arity[fn] == 3) kindOf[fn] = "part";
        }

        // ⚠ The value register is not always set inside the block. The
        // dispatcher's own prologue loads it before the indexed jump, and it
        // holds for every mission — mission 19 reads its `push ebx` from there
        // and nowhere else. Collect it once and hand it to each block.
        var prologue = new Dictionary<int, int>();
        for (int i = 0; i + 7 < text.Length; i++)
        {
            if (text[i] != 0xFF || text[i + 1] != 0x24 || text[i + 2] != 0x8D) continue;
            if (BitConverter.ToUInt32(text, i + 3) != VyrobaCaseTable) continue;
            var pre = PrevPushes(text, i + 7, 0x400);
            if (pre != null)
                foreach (var kv in pre.Value.Consts) prologue.TryAdd(kv.Key, kv.Value);
            break;
        }

        for (int m = 0; m < MissionSlots; m++)
        {
            uint start = blocks[idx[m]];
            uint end = uint.MaxValue;
            foreach (var s in starts) if (s > start) { end = s; break; }

            int from0 = (int)(start - tva);
            int to0 = Math.Min(end == uint.MaxValue ? text.Length : (int)(end - tva),
                               text.Length - 12);

            // ⚠ Stop the LAST block at its `add esp,imm8; ret`. Every mission
            // block ends in a `jmp` to the shared tail and is bounded by the
            // next block, so this touches none of them — but the tail itself is
            // the last block, its end is the end of .text, and the padding
            // behind its `ret` decodes into plausible-looking calls. That was
            // the only place the two GAME.EXE disagreed: the other build read
            // two more component rows out of the rubbish, in 64 of 99 mission
            // slots, and every one of them was a phantom.
            if (end == uint.MaxValue)
                for (int q = from0; q + 3 < to0; q++)
                    if (text[q] == 0x83 && text[q + 1] == 0xC4 && text[q + 3] == 0xC3)
                    { to0 = q + 4; break; }

            var inside = InsideCallOffsets(text, tva, from0, to0);

            var rows = new List<UnlockRange>();
            // ⚠ A register is resolved only over the stretch SINCE THE LAST
            // setter call, and otherwise carried forward. Searching back to the
            // block start instead hits a byte inside some address or offset
            // that looks like `mov ebx, imm` and returns a wrong value — the
            // last six lists that would not line up were all of that kind.
            var held = new Dictionary<int, int>(prologue);
            int floor = from0;

            // The counting loops of this block: `cmp r8, imm8` closed by a SHORT
            // conditional jump BACKWARDS. Everything between the jump target and
            // the jump runs once per round.
            //
            // ⚠ This replaces the old rule »the index register is `inc`ed right
            // in front of the call«, which is not the shape of a three-argument
            // call — the player is pushed in between — and which misses the
            // first of two calls sharing one loop body. Mission 0 is exactly
            // that: `L: set_part(0,bl,1); set_part(1,bl,1); inc bl; cmp bl,0xc8;
            // jb L`. The old rule read player 1 as parts 0..199 and player 0 as
            // part 0 alone.
            var loops = new List<(int From, int To, int Reg, int Limit)>();
            for (int q = from0; q + 4 < to0; q++)
            {
                if (text[q] != 0x80 || (text[q + 1] & 0xF8) != 0xF8) continue;   // cmp r8,imm8
                if (text[q + 3] is < 0x70 or > 0x7F) continue;                   // jcc rel8
                int rel = (sbyte)text[q + 4];
                if (rel >= 0) continue;
                int target = q + 5 + rel;
                if (target < from0) continue;
                loops.Add((target, q, text[q + 1] & 7, text[q + 2]));
            }

            // ⚠ Two passes. A block sets its value register once and holds it
            // over every call, but the backward walk from the FIRST call often
            // stops at an opcode it does not know and never reaches the setter.
            // Walking every call first and pooling what those walks did see
            // fills the register in from a later call — which is sound, because
            // it is the same held value. Without this pass mission 19 fell back
            // to the raw byte search and read the wrong number.
            for (int q = from0; q < to0; q++)
            {
                if (text[q] != 0xE8) continue;
                uint tq = Resolve((uint)(tva + q + 5 + BitConverter.ToInt32(text, q + 1)));
                if (!kindOf.ContainsKey(tq)) continue;
                var pre = PrevPushes(text, q, 0x200, arity.TryGetValue(tq, out int aq) ? aq : 2);
                if (pre == null) continue;
                foreach (var kv in pre.Value.Consts) held.TryAdd(kv.Key, kv.Value);
            }

            for (int i = from0; i < to0; i++)
            {
                if (text[i] != 0xE8) continue;
                uint t = Resolve((uint)(tva + i + 5 + BitConverter.ToInt32(text, i + 1)));
                if (!kindOf.TryGetValue(t, out string? kind)) continue;
                int want = arity.TryGetValue(t, out int a3) && a3 == 3 ? 3 : 2;

                // the arguments, whatever stands between them; args[0] is the
                // one nearest the call, which with cdecl is the FIRST parameter
                var args = PrevPushes(text, i, 0x200, want);
                if (args == null) continue;
                var list = args.Value.Args;
                foreach (var kv in args.Value.Consts) held[kv.Key] = kv.Value;   // fresher wins

                Arg index = list[want == 3 ? 1 : 0];
                Arg value = list[want == 3 ? 2 : 1];
                int player = -1;
                if (want == 3)
                {
                    int? p = list[0].Reg >= 0
                        ? Held(text, i, list[0].Reg, floor, held, inside, from0)
                        : list[0].Imm;
                    if (p is null or < 0 or > 7) { floor = i + 5; continue; }
                    player = p.Value;
                }

                int? one = index.Reg >= 0
                    ? Held(text, i, index.Reg, floor, held, inside, from0) : index.Imm;
                int val = Flag(value.Reg >= 0
                    ? Held(text, i, value.Reg, floor, held, inside, from0) : value.Imm);
                if (one == null) { floor = i + 5; continue; }

                int last = one.Value;
                if (index.Reg >= 0)
                    foreach (var lp in loops)
                        if (lp.Reg == index.Reg && lp.From <= i && i <= lp.To)
                        { last = lp.Limit - 1; break; }
                if (last >= one.Value)
                    rows.Add(new UnlockRange(kind, player, one.Value, last, val));
                floor = i + 5;                       // next search starts after this call
            }
            if (rows.Count > 0) _missionUnlocks[m] = rows;
        }
    }

    /// <summary>
    /// Is this one of the four setters, and which?
    ///
    /// A setter ends in `mov byte ptr [&lt;scaled index&gt; + imm32], r8`, and the
    /// imm32 names the table. The scaling is what makes this reliable: a setter
    /// steps through records of 46, 42, 58 or 3 bytes, so its write always
    /// carries a **SIB byte**. The mission blocks also call byte setters that
    /// write one flag per player — `mov byte ptr [ecx + imm32], al`, no SIB —
    /// and two of those (the AI level at 0x538bd8 and its neighbour) land in the
    /// same address range.
    ///
    /// ⚠ That is exactly the bug this replaced: without the SIB test they were
    /// read as design unlocks, and every mission came out with one vehicle row
    /// too many. `--selftest-exe` caught it; nothing else would have.
    ///
    /// ⚠ Remaining limit: three of the four are told apart by an address this
    /// class hard-codes, so a differently built exe falls back to "design" for
    /// all of them. The build-independent discriminator is the record stride out
    /// of the lea chain, and reading that needs a decoder this class has not got.
    /// </summary>
    /// <summary>
    /// How many arguments a cdecl function takes, counted at its call sites:
    /// `add esp, N` right behind the call means N/4 arguments. The vote is
    /// taken over every site in the mission blocks, so the handful that fold
    /// two cleanups into one `add esp` cannot change the answer (2 of 1037 on
    /// this build, 2 of 1093 on the other).
    ///
    /// <para>This is what tells `set_part(player, part, value)` from the three
    /// two-argument setters without knowing a single address.</para>
    /// </summary>
    private int ArityOf(uint tva, byte[] text, uint lo, uint hi, uint fn)
    {
        var votes = new int[8];
        for (int i = (int)(lo - tva); i < Math.Min(hi - tva, text.Length - 9); i++)
        {
            if (text[i] != 0xE8) continue;
            if (Resolve((uint)(tva + i + 5 + BitConverter.ToInt32(text, i + 1))) != fn) continue;
            if (text[i + 5] != 0x83 || text[i + 6] != 0xC4) continue;
            int n = text[i + 7] / 4;
            if (n is > 0 and < 8) votes[n]++;
        }
        int best = 0;
        for (int n = 1; n < votes.Length; n++) if (votes[n] > votes[best]) best = n;
        return best;
    }

    private uint SetterDest(uint tva, byte[] text, uint fn)
    {
        int o = (int)(fn - tva);
        if (o < 0 || o + 0x60 > text.Length) return 0;
        for (int i = o; i < o + 0x60 && i + 8 < text.Length; i++)
        {
            if (text[i] == 0xC3) break;                       // ret
            if (text[i] != 0x88) continue;                    // mov r/m8, r8
            byte modrm = text[i + 1];
            if ((modrm & 0x07) != 0x04) continue;             // needs a SIB byte
            int mod = modrm >> 6;
            if (mod != 0 && mod != 2) continue;               // disp32 forms only
            uint dest = BitConverter.ToUInt32(text, i + 3);
            if (dest < 0x500000 || dest > 0x560000) continue;
            return dest;
        }
        return 0;
    }

    private static readonly byte[] VyrobaMessage =
        Encoding.ASCII.GetBytes("Cannot add new 'vyroba'");

    /// <summary>The adder is the only function that pushes »Cannot add new
    /// 'vyroba'«; from that one reference, walk back to the int3 padding in
    /// front of the function.</summary>
    private uint FindAddVyroba(uint tva, byte[] text)
    {
        int at = IndexOf(_d, VyrobaMessage);
        if (at < 0) return 0;
        uint sva = VaOf(at);
        if (sva == 0) return 0;

        var needle = BitConverter.GetBytes(sva);
        int j = IndexOf(text, needle);
        if (j < 0) return 0;
        while (j > 2 && !(text[j - 1] == 0xCC && text[j - 2] == 0xCC)) j--;
        return (uint)(tva + j);
    }

    /// <summary>The mission dispatch: <c>cmp eax,0x63</c> followed by
    /// <c>mov cl,[eax+idx]</c> and <c>jmp [ecx*4+tab]</c>.
    ///
    /// ⚠ The compare alone is not enough — the binary holds several, and the
    /// first one found belongs to something else entirely. The mission one is
    /// the one at least one of whose blocks contains a call to the adder, and
    /// that test is what makes this work on both builds.</summary>
    private bool FindMissionDispatch(uint tva, byte[] text,
                                     List<(uint At, int Player, int Kind, int What, int Third)> sites)
    {
        for (int i = 0; i + 0x20 < text.Length; i++)
        {
            if (text[i] != 0x83 || text[i + 1] != 0xF8 || text[i + 2] != 0x63) continue;

            uint idxTab = 0, jmpTab = 0, missionVar = 0;
            for (int k = Math.Max(0, i - 0x10); k < i + 0x20 && k + 7 < text.Length; k++)
            {
                // movsx eax, word ptr [imm32]
                if (missionVar == 0 && text[k] == 0x0F && text[k + 1] == 0xBF && text[k + 2] == 0x05)
                    missionVar = BitConverter.ToUInt32(text, k + 3);
                // mov cl, byte ptr [eax + imm32]
                if (idxTab == 0 && text[k] == 0x8A && text[k + 1] == 0x88)
                    idxTab = BitConverter.ToUInt32(text, k + 2);
                // jmp dword ptr [ecx*4 + imm32]
                if (text[k] == 0xFF && text[k + 1] == 0x24 && text[k + 2] == 0x8D)
                { jmpTab = BitConverter.ToUInt32(text, k + 3); break; }
            }
            if (idxTab == 0 || jmpTab == 0) continue;

            var idx = Read(idxTab, MissionSlots);
            if (idx.Length < MissionSlots) continue;
            int cases = 0;
            foreach (var b in idx) cases = Math.Max(cases, b + 1);
            var tab = Read(jmpTab, cases * 4);
            if (tab.Length < cases * 4) continue;

            // the deciding test: does any block hold one of the adder's callers?
            bool builds = false;
            for (int c = 0; c < cases && !builds; c++)
            {
                uint start = BitConverter.ToUInt32(tab, c * 4);
                foreach (var s in sites)
                    if (s.At >= start && s.At < start + 0x400) { builds = true; break; }
            }
            if (!builds) continue;

            VyrobaMissionVar = missionVar;
            VyrobaCaseIndex = idxTab;
            VyrobaCaseTable = jmpTab;
            return true;
        }
        return false;
    }

    private static int IndexOf(byte[] hay, byte[] needle, int from = 0)
    {
        for (int i = from; i + needle.Length <= hay.Length; i++)
        {
            int k = 0;
            while (k < needle.Length && hay[i + k] == needle[k]) k++;
            if (k == needle.Length) return i;
        }
        return -1;
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

    // ---- die Ausbaustufe, mit der eine Mission anfaengt --------------------

    private bool _startLevelTried;

    /// <summary>Wo die Tabelle steht, mit der eine Mission ihre Fabriken und
    /// Minen einstellt — 0, wenn nicht gefunden.</summary>
    public uint MissionSetupTable { get; private set; }

    /// <summary>Der Vorlauf, der die Tabelle liest.</summary>
    public uint MissionSetupRoutine { get; private set; }

    /// <summary>Wie viele Missionen die Tabelle fuehrt.</summary>
    public const int MissionSetupCount = 35;

    /// <summary>
    /// Der Missionsvorlauf @0x4407C0 (F: 0x43F8xx), den der Kartenlader am Ende
    /// unbedingt ruft (@0x41F1FD, Thunk 0x401F41 → <c>jmp 0x4407C0</c>; kein
    /// Sprung im ganzen .text landet zwischen 0x41F1FA und 0x41F212, die Stelle
    /// wird also nur durchfallend erreicht).
    ///
    /// <para>Er laeuft ueber alle 255 Gebaeudesaetze und setzt bei jeder Fabrik
    /// und jeder Mine dieselben vier Dinge neu — <b>nachdem</b> die Abschnitte
    /// 24 und 28 geladen sind, der Kartenwert wird also ueberschrieben:</para>
    /// <list type="bullet">
    ///   <item><b>Ausbaustufe</b> <c>+0x05 = einstellung &gt;&gt; 1</c>
    ///     (@0x4407FB <c>shr dl, 1</c>) — und das ist die TORKACHEL,</item>
    ///   <item>Lagerplatz <c>= (einstellung + 4) * 10</c>,</item>
    ///   <item>Lagerausbaukosten 20, Produktionserweiterungskosten 50.</item>
    /// </list>
    ///
    /// <para>Die <c>einstellung</c> kommt aus zwei Quellen: im Gefecht aus
    /// <c>byte[0x540eb8]</c>, dem Aufbaustand einer frischen Partie (1), in der
    /// Kampagne aus dieser Tabelle, mit der Missionsnummer indiziert. Sie hat
    /// 35 Eintraege zu 2 Byte, 1..9, und steigt in Dreierstufen — also genau
    /// die 35 Kampagnenzustaende. Netzkarten tragen 51..58 und laegen ausserhalb;
    /// sie nehmen darum den anderen Zweig.</para>
    ///
    /// <para>⚠ Was hier NICHT gemacht wird: der Vorlauf wird nicht ausgefuehrt.
    /// Er ruehrt auch Lagerplatz und Ausbaukosten an, das ist Wirtschaft und
    /// nicht Import. Gelesen und angeboten, angewandt wird er anderswo.</para>
    /// </summary>
    private void LocateMissionStartLevel()
    {
        if (_startLevelTried) return;
        _startLevelTried = true;
        var (tva, text) = TextSection();
        if (text.Length == 0) return;

        // movsx eax, word ptr [imm32]   0f bf 05 ....
        // mov   al,  byte ptr [eax*2 + imm32]   8a 04 45 ....
        for (int i = 0; i + 14 < text.Length; i++)
        {
            if (text[i] != 0x0F || text[i + 1] != 0xBF || text[i + 2] != 0x05) continue;
            if (text[i + 7] != 0x8A || text[i + 8] != 0x04 || text[i + 9] != 0x45) continue;
            uint tab = BitConverter.ToUInt32(text, i + 10);
            if (Offset(tab) < 0) continue;

            // die Tabelle selbst beweist sich: 35 Woerter, jedes 1..9, nie
            // fallend, und dahinter eine 0. Das trifft keine andere Stelle.
            var raw = Read(tab, MissionSetupCount * 2 + 2);
            if (raw.Length < MissionSetupCount * 2 + 2) continue;
            bool ok = true;
            int prev = 0;
            for (int k = 0; k < MissionSetupCount && ok; k++)
            {
                int v = BitConverter.ToUInt16(raw, k * 2);
                if (v < 1 || v > 9 || v < prev) ok = false;
                prev = v;
            }
            if (!ok || BitConverter.ToUInt16(raw, MissionSetupCount * 2) != 0) continue;

            // und die Routine muss die Zahl auch halbieren
            bool shr = false;
            for (int k = i; k < i + 0x40 && k + 1 < text.Length; k++)
                if (text[k] == 0xD0 && text[k + 1] == 0xEA) { shr = true; break; }
            if (!shr) continue;

            MissionSetupTable = tab;
            MissionSetupRoutine = (uint)(tva + i);
            break;
        }
    }

    public bool HasMissionStartLevel
    {
        get { LocateMissionStartLevel(); return MissionSetupTable != 0; }
    }

    /// <summary>Die Einstellung dieser Mission, oder −1 ausserhalb der Tabelle
    /// (Netzkarten). Das ist die Rohzahl 1..9, nicht die Stufe.</summary>
    public int MissionSetupValue(int mission)
    {
        if (!HasMissionStartLevel) return -1;
        if (mission < 0 || mission >= MissionSetupCount) return -1;
        var r = Read((uint)(MissionSetupTable + 2 * mission), 2);
        return r.Length < 2 ? -1 : BitConverter.ToUInt16(r, 0);
    }

    /// <summary>Die Ausbaustufe, mit der die Fabriken und Minen dieser Mission
    /// anfangen — <c>einstellung &gt;&gt; 1</c>, und damit zugleich die
    /// Torkachel, die ihre Tuer 0 am Anfang zeigt. −1, wo die Tabelle nichts
    /// sagt.</summary>
    public int MissionStartLevel(int mission)
    {
        int v = MissionSetupValue(mission);
        return v < 0 ? -1 : v >> 1;
    }

    /// <summary>Welche Ausbaustufe eine frische Gefechtspartie setzt: der
    /// Aufbaustand <c>byte[0x540eb8]</c> ist dort 1, also Stufe 0.</summary>
    public const int SkirmishStartLevel = 0;

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
