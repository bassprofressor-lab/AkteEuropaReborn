namespace AkteEuropaReborn.Simulation;

using System;
using System.Collections.Generic;
using Godot;

/// <summary>
/// What a unit design is worth: the arithmetic the original runs whenever a
/// design record is written, reconstructed from the routine at <b>0x4b1fb0</b>.
///
/// A sec47 record (46 bytes) carries only three chosen components — weapon at
/// +0x17, propulsion at +0x18, equipment at +0x19 — and the whole tail from
/// +0x1a is DERIVED from them. The routine looks each component up in the
/// 58-byte stats array at 0x5045a0 (indexed by the bare component number) and
/// writes fifteen fields:
///
/// <code>
///   +0x1a  = stats[weapon]  [0x20]            * f      price, weapon parts
///   +0x1b  = stats[prop]    [0x21]            * f      price, chassis parts
///   +0x1c  = (stats[equip][0x22] + stats[weapon][0x22]) * f   price, special parts
///   +0x1d  = sum of [0x10]
///   +0x1e  = sum of [0x0e]
///   +0x1f  = sum of [0x12]      — but zeroed when prop > 150 AND weapon > 49
///   +0x20  = sum of [0x13]
///   +0x22  = sum of [0x16]   (u16)
///   +0x24  = sum of [0x14]   (u16)
///   +0x26  = sum of [0x11]
///   +0x28  = sum of [0x1a]   (u16)            — hit points
///   +0x2a  = 1 when weapon == 8, else sum of [0x18]
///   +0x2b  = sum of [0x1e]
///   +0x2c  = stats[prop]  [0x0d]              — the chassis' component id
///   +0x2d  = stats[weapon][0x0d]              — the weapon's component id
/// </code>
///
/// where <c>f</c> is 2 if the weapon field is row 65 (the Teleporter) and 1
/// otherwise — <c>cmp al,0x41 / sete dl / inc dl</c> — and "sum" means the three
/// chosen components added together. Every addition is 8-bit except the three
/// u16 fields, so the wrap-around is kept here rather than tidied away.
///
/// <para><b>The three prices are proven, not inferred.</b> The production button
/// @0x44a6eb compares them one after another against three consecutive u16
/// stores (0xc0693c/3e/40) and only then sends build command 0x1F7 — the same
/// shape the ship yard uses. Their sources say which is which: +0x1a comes from
/// the weapon, +0x1b from the chassis, +0x1c from the equipment, matching the
/// three part stocks the economy already runs on.</para>
///
/// <para>Checked against the game's own data: all <b>586</b> named designs in
/// sec47 are reproduced exactly, every field, no exceptions — see
/// <c>--selftest-designs</c>.</para>
///
/// <para><b>What each field becomes.</b> The spawn routine @0x4b1b9e copies the
/// tail into the new unit, and the entity table's own fields name them:</para>
/// <code>
///   +0x1e -> entity +0x29   energie_max, the hit points
///   +0x20 -> entity +0x27   attack, what the hit routine @0x40c9a0 subtracts
///   +0x28 -> entity +0x2e AND +0x30   the fuel tank, full
///   +0x2a -> entity +0x39 AND +0x3a   the ammunition, full
///   +0x2c -> entity +0x0b, +0x2d -> entity +0x0c   the two component ids
/// </code>
/// <para>Six remain unnamed, but their destination is now known, which is where
/// the next answer will come from: +0x1d -> entity +0x20 (u16), +0x1f -> +0x26,
/// +0x22 -> +0x2a, +0x24 -> +0x2b, +0x26 -> +0x2c, +0x2b -> +0x3d. Build TIME is
/// not among them — no field of the record was shown to be one.</para>
/// </summary>
public static class DesignMath
{
    public const int Stride = 58;

    /// <summary>The derived tail of a design record.</summary>
    public readonly struct Derived
    {
        public Derived(byte[] tail) { Tail = tail; }

        /// <summary>Bytes +0x1a..+0x2d of the record, index 0 = +0x1a.</summary>
        public byte[] Tail { get; }

        private byte B(int off) => Tail.Length > off - 0x1a ? Tail[off - 0x1a] : (byte)0;
        private int W(int off) => Tail.Length > off - 0x1a + 1
            ? Tail[off - 0x1a] | (Tail[off - 0x1a + 1] << 8) : 0;

        /// <summary>Price in weapon parts (+0x1a).</summary>
        public int CostW => B(0x1a);
        /// <summary>Price in chassis parts (+0x1b).</summary>
        public int CostF => B(0x1b);
        /// <summary>Price in special parts (+0x1c).</summary>
        public int CostS => B(0x1c);

        /// <summary>Hit points — design +0x1e, which the spawn routine writes
        /// into entity +0x29, `energie_max`.
        ///
        /// <b>CORRECTED.</b> This was read off +0x28 at first, because +0x28 is
        /// the sum of the stats field that had been called hp_max. It is not the
        /// hit points: the spawn routine copies +0x28 into entity +0x2e AND
        /// +0x30, the fuel tank. The data says the same thing from the other
        /// side — the Chaingunner, an infantry design, has +0x1e = 20 and
        /// +0x28 = 0: twenty hit points and no tank, which is exactly what a
        /// foot soldier has in every map.</summary>
        public int Hp => B(0x1e);

        /// <summary>Attack rating — design <b>+0x1f</b>, written to entity
        /// +0x26, which the panel labels "A/V " together with +0x27.
        ///
        /// Settled after two reversals. The damage arithmetic @0x40cd90 reads
        /// +0x27 and +0x28, which looked decisive until the registers were read
        /// too: those are one field from the shooter and one from the victim.
        /// The attack is read earlier in the same routine (@0x40cb7d) and that
        /// is +0x26 — and +0x26 is what tracks the weapon's damage across every
        /// map, closely, where +0x27 rises only weakly.</summary>
        public int Attack => B(0x1f);

        /// <summary>Defence rating — design +0x20, written to entity +0x27,
        /// the other half of the panel's "A/V ".</summary>
        public int Defence => B(0x20);

        /// <summary>Fuel tank, full — design +0x28, written to entity +0x2e and
        /// +0x30 together.</summary>
        public int Fuel => W(0x28);

        /// <summary>Ammunition, full — design +0x2a, written to entity +0x39 and
        /// +0x3a together.</summary>
        public int Ammo => B(0x2a);

        /// <summary>Weapon range in tiles — design <b>+0x24</b>, written to
        /// entity +0x2b, which the panel @0x474fe0 labels "Reichw.".
        ///
        /// <b>CORRECTED.</b> Taken off +0x26 -> entity +0x2c at first, from the
        /// other panel where the label order cannot be checked. In @0x474fe0 it
        /// can: "Munition " is followed by +0x39 and +0x3a, which are known to
        /// be the magazine, so the label comes BEFORE its field there. And the
        /// data agrees — +0x2b is uniform per weapon (190 units of 2x
        /// Maschinengewehr all carry 4) and follows range_raw at 50->4, 60->4,
        /// 80->8, 90->10, while +0x2c scatters, which is the radar equipment
        /// adding to the SIGHT.</summary>
        public int Range => B(0x24);

        /// <summary>Sight in tiles — design +0x26, written to entity +0x2c,
        /// the panel's "Sicht".</summary>
        public int Sight => B(0x26);

        /// <summary>Speed — design +0x1d, written to entity +0x20 (u16), which
        /// the panel labels "Geschw.".
        ///
        /// Two independent readings agree here, which is why this one is named
        /// where the others were not: the panel's label order (verified against
        /// "Munition"), and the data — across 1863 units it tracks the chassis'
        /// speed_raw monotonically over all 16 chassis types. The hit routine
        /// writing it to 2 now reads as what it is: a unit slowed when struck.
        /// </summary>
        public int Speed => W(0x1d);

        /// <summary>Reload time — design +0x2b, written to entity +0x3d, which
        /// the panel @0x474fe0 labels "Nachladen". Light weapons 20, the
        /// Schw.Raketenwerfer 120.</summary>
        public int Reload => B(0x2b);

        /// <summary>Superseded by <see cref="Speed"/>; kept so older callers
        /// still build. <b>Was:</b> deliberately unnamed.
        ///
        /// It looked like the sight: the unit panel @0x468ba6 reads entity +0x20
        /// and prints "Sicht" nearby. But the panel loads a label for the NEXT
        /// value it builds, so "nearby" does not settle which label belongs to
        /// which field — and the hit routine @0x40c9a0 does not just read +0x20,
        /// it WRITES it, setting it to 2 on a hit. A value a hit assigns is not
        /// a sight radius. The reading is withdrawn rather than kept as a guess;
        /// the number is carried and used for nothing.</summary>
        public int Unknown1d => W(0x1d);

        /// <summary>The chassis' component id (+0x2c).</summary>
        public int ChassisComponent => B(0x2c);
        /// <summary>The weapon's component id (+0x2d).</summary>
        public int WeaponComponent => B(0x2d);
    }

    // ---- the component table ------------------------------------------------

    private static Dictionary<int, byte[]>? _rows;

    /// <summary>True once the component table is available.</summary>
    public static bool Ready => _rows is { Count: > 0 };

    /// <summary>Loads the component rows written by the importer. Without them
    /// nothing can be derived and the callers keep their own fallbacks.</summary>
    public static void Load()
    {
        if (_rows != null) return;
        _rows = new Dictionary<int, byte[]>();
        string path = Core.Content.Path("Maps/component_stats.json");
        if (!FileAccess.FileExists(path)) return;
        using var f = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        if (f == null) return;
        var json = new Json();
        if (json.Parse(f.GetAsText()) != Error.Ok || json.Data.VariantType != Variant.Type.Dictionary)
            return;
        var root = json.Data.AsGodotDictionary<string, Variant>();
        if (!root.TryGetValue("rows", out var rv) || rv.VariantType != Variant.Type.Dictionary) return;
        foreach (var kv in rv.AsGodotDictionary<string, Variant>())
        {
            if (!int.TryParse(kv.Key, out int row)) continue;
            byte[] b = FromHex(kv.Value.AsString());
            if (b.Length == Stride) _rows[row] = b;
        }
    }

    /// <summary>Feeds the table straight in — used by the importer's self test,
    /// which has the rows in hand and no user:// copy to read.</summary>
    public static void Use(Dictionary<int, byte[]> rows) => _rows = rows;

    private static byte[] FromHex(string s)
    {
        if (s.Length % 2 != 0) return Array.Empty<byte>();
        var b = new byte[s.Length / 2];
        for (int i = 0; i < b.Length; i++)
            b[i] = Convert.ToByte(s.Substring(i * 2, 2), 16);
        return b;
    }

    private static int B(int row, int off)
    {
        if (row <= 0 || _rows == null || !_rows.TryGetValue(row, out var r)) return 0;
        return off < r.Length ? r[off] : 0;
    }

    private static int U16(int row, int off)
    {
        if (row <= 0 || _rows == null || !_rows.TryGetValue(row, out var r)) return 0;
        return off + 1 < r.Length ? r[off] | (r[off + 1] << 8) : 0;
    }

    // ---- the routine --------------------------------------------------------

    /// <summary>The tail of a design record, exactly as @0x4b1fb0 writes it.</summary>
    public static Derived Compute(int weapon, int propulsion, int equipment)
    {
        var t = new byte[0x2e - 0x1a];          // +0x1a .. +0x2d
        void Put(int off, int v) => t[off - 0x1a] = (byte)(v & 0xff);
        void PutW(int off, int v)
        {
            t[off - 0x1a] = (byte)(v & 0xff);
            t[off - 0x1a + 1] = (byte)((v >> 8) & 0xff);
        }

        int f = weapon == 0x41 ? 2 : 1;          // the Teleporter costs double
        int Sum(int off) => B(propulsion, off) + B(equipment, off) + B(weapon, off);

        Put(0x1a, B(weapon, 0x20) * f);
        Put(0x1b, B(propulsion, 0x21) * f);
        Put(0x1c, ((B(equipment, 0x22) + B(weapon, 0x22)) & 0xff) * f);
        Put(0x1d, Sum(0x10));
        Put(0x1e, Sum(0x0e));
        Put(0x1f, Sum(0x12));
        Put(0x20, Sum(0x13));
        if (propulsion > 0x96 && weapon > 0x31) Put(0x1f, 0);
        PutW(0x22, U16(equipment, 0x16) + U16(propulsion, 0x16) + U16(weapon, 0x16));
        PutW(0x24, U16(equipment, 0x14) + U16(propulsion, 0x14) + U16(weapon, 0x14));
        Put(0x26, Sum(0x11));
        PutW(0x28, U16(equipment, 0x1a) + U16(propulsion, 0x1a) + U16(weapon, 0x1a));
        Put(0x2a, weapon == 8 ? 1 : Sum(0x18));
        Put(0x2b, Sum(0x1e));
        Put(0x2c, B(propulsion, 0x0d));
        Put(0x2d, B(weapon, 0x0d));
        return new Derived(t);
    }

    /// <summary>The tail of an existing record, read rather than computed.</summary>
    public static Derived FromRecord(byte[] record)
    {
        var t = new byte[0x2e - 0x1a];
        for (int i = 0; i < t.Length && 0x1a + i < record.Length; i++) t[i] = record[0x1a + i];
        return new Derived(t);
    }

    /// <summary>The same, from the hex string the exported tables carry — the
    /// shape every other `raw` field is read in.</summary>
    public static Derived FromRecordHex(string hex)
    {
        var t = new byte[0x2e - 0x1a];
        for (int i = 0; i < t.Length; i++)
        {
            int at = (0x1a + i) * 2;
            if (at + 2 > hex.Length) break;
            t[i] = Convert.ToByte(hex.Substring(at, 2), 16);
        }
        return new Derived(t);
    }
}
