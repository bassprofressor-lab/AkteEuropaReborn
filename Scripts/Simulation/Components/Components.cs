#nullable disable
namespace AkteEuropaReborn.Simulation.Components;

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using AkteEuropaReborn.Core.Math;
using AkteEuropaReborn.Simulation.ECS;
using Godot;

[StructLayout(LayoutKind.Sequential)]
public readonly struct Vector2I : IEquatable<Vector2I>
{
    public readonly int X;
    public readonly int Y;

    public Vector2I(int x, int y) { X = x; Y = y; }

    public static implicit operator Vector2(Vector2I v) => new Vector2(v.X, v.Y);
    public static implicit operator Vector2I(Vector2 v) => new Vector2I((int)v.X, (int)v.Y);

    public bool Equals(Vector2I other) => X == other.X && Y == other.Y;
    public override bool Equals(object? obj) => obj is Vector2I other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(X, Y);
    public static bool operator ==(Vector2I a, Vector2I b) => a.Equals(b);
    public static bool operator !=(Vector2I a, Vector2I b) => !a.Equals(b);
}

[StructLayout(LayoutKind.Sequential)]
public readonly struct Position : IEquatable<Position>
{
    public readonly Fixed X, Y, Z;

    public Position(Fixed x, Fixed y, Fixed z = default) { X = x; Y = y; Z = z; }
    public Position(int x, int y, int z = 0) : this(Fixed.FromInt(x), Fixed.FromInt(y), Fixed.FromInt(z)) { }

    public static Position operator +(Position a, Position b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static Position operator -(Position a, Position b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static Position operator *(Position a, Fixed s) => new(a.X * s, a.Y * s, a.Z * s);

    public bool Equals(Position other) => X == other.X && Y == other.Y && Z == other.Z;
    public override bool Equals(object? obj) => obj is Position other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(X, Y, Z);
    public static bool operator ==(Position a, Position b) => a.Equals(b);
    public static bool operator !=(Position a, Position b) => !a.Equals(b);
    public override string ToString() => $"({X.ToFloat():F2}, {Y.ToFloat():F2}, {Z.ToFloat():F2})";
}

[StructLayout(LayoutKind.Sequential)]
public readonly struct Velocity : IEquatable<Velocity>
{
    public readonly Fixed X, Y, Z;

    public Velocity(Fixed x, Fixed y, Fixed z = default) => (X, Y, Z) = (x, y, z);

    public bool Equals(Velocity other) => X == other.X && Y == other.Y && Z == other.Z;
    public override bool Equals(object? obj) => obj is Velocity other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(X, Y, Z);
}

[StructLayout(LayoutKind.Sequential)]
public readonly struct Rotation : IEquatable<Rotation>
{
    public readonly Fixed Degrees;

    public Rotation(Fixed degrees) => Degrees = degrees;

    public static implicit operator Fixed(Rotation r) => r.Degrees;
    public static implicit operator Rotation(Fixed f) => new(f);

    public Fixed Radians => Degrees * Fixed.FromFloat(MathF.PI / 180f);
    public Fixed Cos => Fixed.FromFloat(MathF.Cos((float)Radians));
    public Fixed Sin => Fixed.FromFloat(MathF.Sin((float)Radians));

    public bool Equals(Rotation other) => Degrees == other.Degrees;
    public override bool Equals(object? obj) => obj is Rotation other && Equals(other);
    public override int GetHashCode() => Degrees.GetHashCode();
}

[StructLayout(LayoutKind.Sequential)]
public struct UnitStats
{
    public string Name;
    public int MaxHealth;
    public Fixed MoveSpeed;
    public Fixed TurnRate;
    public int AttackDamage;
    public Fixed AttackRange;
    public Fixed AttackCooldown;
    public int Armor;
    public int SightRange;
    public int BuildTime;
    public int CostCredits;
    public int CostEnergy;
    public int SupplyCost;
    public UnitType Type;
    public Faction Faction;
    public WeaponType WeaponType;
    public ArmorType ArmorType;
    public byte Prerequisite;
    public byte TechLevel;
    public ushort GraphicId;
    public ushort IconId;
}

public enum WeaponType : byte { None, Cannon, MachineGun, Missile, Flame, Sniper, Bomb }
public enum ArmorType : byte { None, Light, Medium, Heavy, Concrete, Wood }

[StructLayout(LayoutKind.Sequential)]
public struct Health : IEquatable<Health>
{
    public int Current, Max;

    public float Ratio => Max > 0 ? (float)Current / Max : 0f;
    public bool IsDead => Current <= 0;
    public bool IsFull => Current >= Max;

    public bool Equals(Health other) => Current == other.Current && Max == other.Max;
    public override bool Equals(object? obj) => obj is Health other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Current, Max);
}

public enum UnitType : byte { None = 0, Infantry, LightVehicle, HeavyVehicle, Aircraft, Building, Defense, Resource, Special }

public enum Faction : byte { Neutral = 0, Player1 = 1, Player2 = 2, Player3 = 3, Player4 = 4, Player5 = 5, Player6 = 6, Player7 = 7, Player8 = 8, Gaia = 15 }

[StructLayout(LayoutKind.Sequential)]
public struct Owner : IEquatable<Owner>
{
    public Faction Faction;
    public int PlayerIndex;

    public bool Equals(Owner other) => Faction == other.Faction && PlayerIndex == other.PlayerIndex;
    public override bool Equals(object? obj) => obj is Owner other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Faction, PlayerIndex);
}

[StructLayout(LayoutKind.Sequential)]
public struct Selected : IEquatable<Selected>
{
    public bool Value;

    public bool Equals(Selected other) => Value == other.Value;
    public override bool Equals(object? obj) => obj is Selected other && Equals(other);
    public override int GetHashCode() => Value.GetHashCode();
}

[StructLayout(LayoutKind.Sequential)]
public struct BuildQueue : IEquatable<BuildQueue>
{
    public int CurrentProgress;
    public int TotalTime;
    public uint CurrentUnit;
    public uint NextUnit;

    public bool Equals(BuildQueue other) => CurrentProgress == other.CurrentProgress && TotalTime == other.TotalTime && CurrentUnit == other.CurrentUnit && NextUnit == other.NextUnit;
    public override bool Equals(object? obj) => obj is BuildQueue other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(CurrentProgress, TotalTime, CurrentUnit, NextUnit);
}

[StructLayout(LayoutKind.Sequential)]
public struct ResourceStorage : IEquatable<ResourceStorage>
{
    public int Credits, Energy, MaxCredits, MaxEnergy;

    public bool Equals(ResourceStorage other) => Credits == other.Credits && Energy == other.Energy && MaxCredits == other.MaxCredits && MaxEnergy == other.MaxEnergy;
    public override bool Equals(object? obj) => obj is ResourceStorage other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Credits, Energy, MaxCredits, MaxEnergy);
}

[StructLayout(LayoutKind.Sequential)]
public struct HarvesterState : IEquatable<HarvesterState>
{
    public uint TargetResource;
    public uint TargetRefinery;
    public int CarriedAmount;
    public int Capacity;
    public HarvesterPhase Phase;

    public bool Equals(HarvesterState other) => TargetResource == other.TargetResource && TargetRefinery == other.TargetRefinery && CarriedAmount == other.CarriedAmount && Capacity == other.Capacity && Phase == other.Phase;
    public override bool Equals(object? obj) => obj is HarvesterState other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(TargetResource, TargetRefinery, CarriedAmount, Capacity, Phase);
}

public enum HarvesterPhase : byte { Idle, MovingToResource, Harvesting, MovingToRefinery, Unloading }

[StructLayout(LayoutKind.Sequential)]
public struct AiState : IEquatable<AiState>
{
    public AiBehavior Behavior;
    public uint TargetEntity;
    public Position TargetPosition;
    public int ThinkTimer;
    public int AttackCooldown;
    public byte AggressionLevel;

    public bool Equals(AiState other) => Behavior == other.Behavior && TargetEntity == other.TargetEntity && TargetPosition == other.TargetPosition && ThinkTimer == other.ThinkTimer && AttackCooldown == other.AttackCooldown && AggressionLevel == other.AggressionLevel;
    public override bool Equals(object? obj) => obj is AiState other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Behavior, TargetEntity, TargetPosition, ThinkTimer, AttackCooldown, AggressionLevel);
}

public enum AiBehavior : byte { Guard, Patrol, AttackMove, Hunt, Retreat, Build, Harvest, Repair }

[StructLayout(LayoutKind.Sequential)]
public struct Visibility : IEquatable<Visibility>
{
    public uint VisibleMask;
    public uint ExploredMask;
    public byte VisibilityLevel;

    public bool Equals(Visibility other) => VisibleMask == other.VisibleMask && ExploredMask == other.ExploredMask && VisibilityLevel == other.VisibilityLevel;
    public override bool Equals(object? obj) => obj is Visibility other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(VisibleMask, ExploredMask, VisibilityLevel);
}

[StructLayout(LayoutKind.Sequential)]
public struct Command : IEquatable<Command>
{
    public CommandType Type;
    public uint TargetEntity;
    public Position TargetPosition;
    public int QueueIndex;
    public int IssuedTick;

    public bool Equals(Command other) => Type == other.Type && TargetEntity == other.TargetEntity && TargetPosition == other.TargetPosition && QueueIndex == other.QueueIndex && IssuedTick == other.IssuedTick;
    public override bool Equals(object? obj) => obj is Command other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Type, TargetEntity, TargetPosition, QueueIndex, IssuedTick);
}

public enum CommandType : byte { None = 0, Move, Attack, Stop, HoldPosition, Patrol, Build, Repair, Harvest, Return, Load, Unload, Sell, Deploy, Undeploy, UseAbility, SetRallyPoint }

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct PlayerCommand
{
    public int Tick;
    public byte PlayerIndex;
    public CommandType Type;
    public uint[] Entities;
    public Vector2I TargetPosition;
    public uint TargetEntity;
    public byte[] Payload;
}