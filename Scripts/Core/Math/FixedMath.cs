#nullable disable
namespace AkteEuropaReborn.Core.Math;

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

[StructLayout(LayoutKind.Sequential)]
public readonly struct Fixed : IEquatable<Fixed>, IComparable<Fixed>
{
    public const int FractionalBits = 16;
    public const int OneRaw = 1 << 16;
    public const int HalfRaw = 1 << 15;
    public const int Mask = (1 << 16) - 1;

    public readonly int Raw;

    public Fixed(int raw) => Raw = raw;

    public static Fixed Zero => new Fixed(0);
    public static Fixed One => new Fixed(65536);
    public static Fixed Half => new Fixed(32768);

    public static Fixed FromRaw(int raw) => new Fixed(raw);
    public static Fixed FromFloat(float f) => new Fixed((int)Math.Round(f * 65536f));
    public static Fixed FromInt(int i) => new Fixed(i * 65536);

    public float ToFloat() => Raw / 65536f;
    public int ToInt() => Raw >> 16;

    public static implicit operator Fixed(int i) => FromInt(i);
    public static implicit operator Fixed(float f) => FromFloat(f);
    public static explicit operator float(Fixed f) => f.ToFloat();
    public static explicit operator int(Fixed f) => f.ToInt();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed operator +(Fixed a, Fixed b) => new Fixed(a.Raw + b.Raw);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed operator -(Fixed a, Fixed b) => new Fixed(a.Raw - b.Raw);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed operator -(Fixed a) => new Fixed(-a.Raw);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed operator *(Fixed a, Fixed b) => new Fixed((int)(((long)a.Raw * b.Raw) >> 16));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed operator /(Fixed a, Fixed b) => new Fixed((int)(((long)a.Raw << 16) / b.Raw));

    public static bool operator ==(Fixed a, Fixed b) => a.Raw == b.Raw;
    public static bool operator !=(Fixed a, Fixed b) => a.Raw != b.Raw;
    public static bool operator <(Fixed a, Fixed b) => a.Raw < b.Raw;
    public static bool operator >(Fixed a, Fixed b) => a.Raw > b.Raw;
    public static bool operator <=(Fixed a, Fixed b) => a.Raw <= b.Raw;
    public static bool operator >=(Fixed a, Fixed b) => a.Raw >= b.Raw;

    public bool Equals(Fixed other) => Raw == other.Raw;
    public override bool Equals(object? obj) => obj is Fixed other && Equals(other);
    public override int GetHashCode() => Raw.GetHashCode();
    public int CompareTo(Fixed other) => Raw.CompareTo(other.Raw);
    public override string ToString() => ToFloat().ToString("F4");

    public static Fixed Abs(Fixed x) => x.Raw < 0 ? new Fixed(-x.Raw) : x;
    public static Fixed Min(Fixed a, Fixed b) => a < b ? a : b;
    public static Fixed Max(Fixed a, Fixed b) => a > b ? a : b;
    public static Fixed Clamp(Fixed x, Fixed min, Fixed max) => x < min ? min : (x > max ? max : x);
    public static Fixed Lerp(Fixed a, Fixed b, Fixed t) => a + (b - a) * t;

    public static Fixed Sqrt(Fixed x)
    {
        if (x.Raw <= 0) return Zero;
        int r = x.Raw;
        for (int i = 0; i < 8; i++)
            r = (r + (x.Raw * 65536) / r) >> 1;
        return new Fixed(r);
    }
}

[StructLayout(LayoutKind.Sequential)]
public readonly struct FixedVector2 : IEquatable<FixedVector2>
{
    public readonly Fixed X, Y;

    public FixedVector2(Fixed x, Fixed y) { X = x; Y = y; }
    public FixedVector2(int x, int y) { X = Fixed.FromInt(x); Y = Fixed.FromInt(y); }

    public static FixedVector2 Zero => new FixedVector2(Fixed.Zero, Fixed.Zero);
    public static FixedVector2 One => new FixedVector2(Fixed.One, Fixed.One);
    public static FixedVector2 Up => new FixedVector2(Fixed.Zero, -Fixed.One);
    public static FixedVector2 Down => new FixedVector2(Fixed.Zero, Fixed.One);
    public static FixedVector2 Left => new FixedVector2(-Fixed.One, Fixed.Zero);
    public static FixedVector2 Right => new FixedVector2(Fixed.One, Fixed.Zero);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static FixedVector2 operator +(FixedVector2 a, FixedVector2 b) => new FixedVector2(a.X + b.X, a.Y + b.Y);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static FixedVector2 operator -(FixedVector2 a, FixedVector2 b) => new FixedVector2(a.X - b.X, a.Y - b.Y);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static FixedVector2 operator *(FixedVector2 a, Fixed s) => new FixedVector2(a.X * s, a.Y * s);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static FixedVector2 operator /(FixedVector2 a, Fixed s) => new FixedVector2(a.X / s, a.Y / s);

    public Fixed LengthSq => X * X + Y * Y;
    public Fixed Length => Fixed.Sqrt(LengthSq);

    public FixedVector2 Normalized() => Length > Fixed.Zero ? this / Length : Zero;

    public Fixed DistanceTo(FixedVector2 other) => (other - this).Length;

    public bool Equals(FixedVector2 other) => X == other.X && Y == other.Y;
    public override bool Equals(object? obj) => obj is FixedVector2 other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(X, Y);
    public override string ToString() => $"({X}, {Y})";
}

/// <summary>
/// Der gekeimte Würfel des Spiels — xoroshiro64**, 32 Bit Ausgabe,
/// 64 Bit Zustand. Zwei Maschinen mit demselben Keim werfen dieselbe Folge,
/// unabhängig von Betriebssystem, .NET-Fassung und Fliesskomma-Einstellung:
/// es sind ausschliesslich ganzzahlige Verschiebungen und Multiplikationen.
///
/// <para>⚠ <b>Der Vorgänger hatte eine Periode von 718 535 — GEMESSEN am
/// 12.08.2026</b> mit <c>--determinism-rng-check</c> (Brents Zyklussuche über
/// den 64-Bit-Zustand, Keim 12345). Nach 718 535 Zügen wiederholte er sich
/// Zahl für Zahl. Das ist keine theoretische Grenze: eine lange Partie mit
/// Kampf würfelt zwei- bis dreimal je Takt, also ist die Marke in der
/// Grössenordnung von hunderttausend Takten erreichbar — und ein
/// Netzspiel, in dem der Würfel umläuft, ist ein Netzspiel mit einem
/// Muster.</para>
///
/// <para><b>Woran es lag.</b> Es war <b>xoroshiro falsch abgeschrieben</b>.
/// Der Schritt lautete
/// <c>_s0 = (s0 &lt;&lt; 13) | (s0 &gt;&gt; 19) ^ s1 ^ (s1 &lt;&lt; 5)</c>, und
/// weil <c>^</c> in C# STÄRKER bindet als <c>|</c>, rechnete der Übersetzer
/// <c>(s0&lt;&lt;13) | (((s0&gt;&gt;19) ^ s1) ^ (s1&lt;&lt;5))</c> — ein
/// <b>ODER</b> statt der gemeinten Rotation-und-XOR. Ein ODER kann Bits nur
/// setzen, nie löschen, und genau das zeigt die zweite Messung: der Zustand
/// trug im Mittel <b>37,51 von 64</b> gesetzten Bits statt der erwarteten 32.
/// Die neue Fassung misst 32,18.</para>
///
/// <para>Auffällig war er dabei NICHT: über 4 Millionen Züge verteilte er 16
/// Fächer auf 247 376..251 964 (erwartet je 250 000), also unauffällig gleich.
/// Ein Würfel, der gut aussieht und nach 700 000 Zügen von vorn anfängt, ist
/// genau die Sorte Fehler, die man nicht durch Hinsehen findet.</para>
/// </summary>
public struct DeterministicRng
{
    private uint _s0, _s1;

    public DeterministicRng(uint seed)
    {
        _s0 = SplitMix32(seed);
        _s1 = SplitMix32(_s0);
        // Der Nullzustand ist der einzige Fixpunkt von xoroshiro; SplitMix32
        // trifft ihn praktisch nie, aber "praktisch nie" ist bei einem
        // Netzspiel kein Argument.
        if ((_s0 | _s1) == 0) { _s0 = 0x9E3779B9; _s1 = 0x85EBCA6B; }
    }

    private static uint SplitMix32(uint x)
    {
        x += 0x9E3779B9;
        x = (x ^ (x >> 16)) * 0x85EBCA6B;
        x = (x ^ (x >> 13)) * 0xC2B2AE35;
        return x ^ (x >> 16);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint Rotl(uint x, int k) => (x << k) | (x >> (32 - k));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public uint NextUInt()
    {
        uint s0 = _s0;
        uint s1 = _s1;
        uint result = Rotl(s0 * 0x9E3779BBu, 5) * 5u;

        s1 ^= s0;
        _s0 = Rotl(s0, 26) ^ s1 ^ (s1 << 9);
        _s1 = Rotl(s1, 13);

        return result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int NextInt(int min, int max) => min + (int)(NextUInt() % (uint)(max - min));

    /// <summary>⚠ NICHT für die Spielwelt. Fliesskomma gehört nicht in einen
    /// Zustand, über den eine Prüfsumme läuft — für die Welt sind
    /// <see cref="NextInt"/> und <see cref="NextFixed"/> da.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float NextFloat() => NextUInt() * (1f / uint.MaxValue);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Fixed NextFixed() => new Fixed((int)(NextUInt() >> 16));

    /// <summary>⚠ NICHT für die Spielwelt — siehe <see cref="NextFloat"/>.</summary>
    public bool NextBool(float probability = 0.5f) => NextFloat() < probability;

    /// <summary>Trifft mit <paramref name="num"/>/<paramref name="den"/>
    /// Wahrscheinlichkeit, ganzzahlig. DAS ist die Form für die Spielwelt.</summary>
    public bool NextChance(int num, int den) => den > 0 && (int)(NextUInt() % (uint)den) < num;

    /// <summary>⚠ NICHT für die Spielwelt — siehe <see cref="NextFloat"/>.</summary>
    public System.Numerics.Vector2 NextVector2(float maxLength = 1f)
    {
        var angle = NextFloat() * MathF.PI * 2;
        var len = NextFloat() * maxLength;
        return new System.Numerics.Vector2(MathF.Cos(angle) * len, MathF.Sin(angle) * len);
    }
}

public static class MathUtils
{
    public const float PI = MathF.PI;
    public const float PI2 = MathF.PI * 2;
    public const float HALF_PI = MathF.PI / 2;
    public const float DEG2RAD = MathF.PI / 180f;
    public const float RAD2DEG = 180f / MathF.PI;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int NextPowerOfTwo(int x)
    {
        x--;
        x |= x >> 1;
        x |= x >> 2;
        x |= x >> 4;
        x |= x >> 8;
        x |= x >> 16;
        return x + 1;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsPowerOfTwo(int x) => x > 0 && (x & (x - 1)) == 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Clamp(int x, int min, int max) => x < min ? min : (x > max ? max : x);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Clamp(float x, float min, float max) => x < min ? min : (x > max ? max : x);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Lerp(float a, float b, float t) => a + (b - a) * t;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed Lerp(Fixed a, Fixed b, Fixed t) => a + (b - a) * t;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float SmoothStep(float a, float b, float t) { t = Clamp((t - a) / (b - a), 0, 1); return t * t * (3 - 2 * t); }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int ManhattanDistance(int x1, int y1, int x2, int y2) => Math.Abs(x1 - x2) + Math.Abs(y1 - y2);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int ChebyshevDistance(int x1, int y1, int x2, int y2) => Math.Max(Math.Abs(x1 - x2), Math.Abs(y1 - y2));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float EuclideanDistance(float x1, float y1, float x2, float y2)
    {
        var dx = x2 - x1;
        var dy = y2 - y1;
        return MathF.Sqrt(dx * dx + dy * dy);
    }
}