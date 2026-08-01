using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using AkteEuropaReborn.Core.Math;
using AkteEuropaReborn.Simulation.Components;
using AkteEuropaReborn.Simulation.ECS;
using Godot;

using Vector2I = AkteEuropaReborn.Simulation.Components.Vector2I;
using SimGridMap = AkteEuropaReborn.Simulation.GridMap;

namespace AkteEuropaReborn.Simulation;

[StructLayout(LayoutKind.Sequential)]
public struct DeterministicRng
{
    private uint _s0, _s1;
    public DeterministicRng(uint seed) { _s0 = SplitMix32(seed); _s1 = SplitMix32(_s0); }
    private static uint SplitMix32(uint x) { x += 0x9E3779B9; x = (x ^ (x >> 16)) * 0x85EBCA6B; x = (x ^ (x >> 13)) * 0xC2B2AE35; return x ^ (x >> 16); }
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public uint NextUInt() { uint s0 = _s0, s1 = _s1, result = s0 + s1; s1 ^= s0; _s0 = (s0 << 13) | (s0 >> 19) ^ s1 ^ (s1 << 5); _s1 = (s1 << 7) | (s1 >> 25); return result; }
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public int NextInt(int min, int max) => min + (int)(NextUInt() % (uint)(max - min));
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public float NextFloat() => NextUInt() * (1f / uint.MaxValue);
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public Fixed NextFixed() => Fixed.FromRaw((int)(NextUInt() >> 16));
    public bool NextBool(float probability = 0.5f) => NextFloat() < probability;
    public Vector2I NextVector2(Fixed maxLength = default) { var angle = NextFloat() * MathF.PI * 2; var len = maxLength != default ? NextFloat() * maxLength.ToFloat() : NextFloat(); return new Vector2I((int)(MathF.Cos(angle) * len), (int)(MathF.Sin(angle) * len)); }
}

public sealed class SimulationState
{
    public const int TicksPerSecond = 20;
    public static readonly Fixed TickDuration = Fixed.FromFloat(1f / TicksPerSecond);

    public int CurrentTick = 0;
    public int FrameCount = 0;
    public uint RandomSeed = 0;
    public DeterministicRng Rng;
    public GamePhase Phase = GamePhase.Playing;

    public bool IsPaused => Phase == GamePhase.Paused;

    public World World = new();
    public GridMap Grid = new();
    public PlayerState[] Players = new PlayerState[GameConfig.MaxPlayers];
    public CommandBuffer Commands = new();
    public List<ReplayFrame> ReplayFrames = new();

    public SimulationState(uint? seed = null) { RandomSeed = seed ?? (uint)DateTime.UtcNow.Ticks; Rng = new DeterministicRng(RandomSeed); for (int i = 0; i < GameConfig.MaxPlayers; i++) Players[i] = new PlayerState { Index = i, Faction = (Faction)(i + 1) }; }
}

public enum GamePhase : byte { Initializing = 0, Playing = 1, Paused = 2, Ended = 3 }

public sealed class PlayerState
{
    public int Index;
    public Faction Faction = Faction.Neutral;
    public string Name = "";
    public int Credits = 0;
    public int Energy = 0;
    public int MaxCredits = 99999;
    public int MaxEnergy = 99999;
    public int PowerOutput = 0;
    public int PowerDrain = 0;
    public bool HasLost = false;
    public bool HasWon = false;
    public Dictionary<string, int> TechLevels = new();
    public Dictionary<UnitType, int> UnitCounts = new();
    public Dictionary<string, int> BuildQueue = new();
}

public struct DiplomacyState { public int TeamId; public Faction[] Members; public DiplomaticStance[] Stances; public DiplomacyState(int maxPlayers) { TeamId = 0; Members = Array.Empty<Faction>(); Stances = new DiplomaticStance[GameConfig.MaxPlayers]; } }
public enum DiplomaticStance : byte { Neutral = 0, Allied = 1, War = 2 }
public enum TechType : ushort { None = 0, PowerPlant = 1, Refinery = 2, Barracks = 3, WarFactory = 4, TechCenter = 5, Radar = 6, Helipad = 7, Airfield = 8, MissileSilo = 9, Chronosphere = 10, IronCurtain = 11, NuclearMissile = 12 }

public sealed class GridMap
{
    public const int MapWidthTiles = 256;
    public const int MapHeightTiles = 256;
    public const int TileSize = 32;

    public readonly byte[] Terrain = new byte[MapWidthTiles * MapHeightTiles];
    public readonly byte[] Height = new byte[MapWidthTiles * MapHeightTiles];
    public readonly ushort[] PassableMask = new ushort[MapWidthTiles * MapHeightTiles];
    public readonly uint[] Visibility = new uint[MapWidthTiles * MapHeightTiles];
    public readonly byte[] FogOfWar = new byte[MapWidthTiles * MapHeightTiles];
    public readonly uint[] Occupancy = new uint[MapWidthTiles * MapHeightTiles];
    public readonly int[] Distances = new int[MapWidthTiles * MapHeightTiles];
    public readonly int[] Parents = new int[MapWidthTiles * MapHeightTiles];
    public readonly byte[] OpenFlags = new byte[MapWidthTiles * MapHeightTiles];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int Index(int x, int y) => y * MapWidthTiles + x;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool InBounds(int x, int y) => (uint)x < MapWidthTiles && (uint)y < MapHeightTiles;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetPassable(int x, int y, UnitType type, bool passable)
    {
        if (!InBounds(x, y)) return;
        var idx = Index(x, y);
        var bit = (ushort)(1 << (int)type);
        if (passable) PassableMask[idx] |= bit;
        else PassableMask[idx] = (ushort)(PassableMask[idx] & ~bit);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsPassable(int x, int y, UnitType type)
    {
        if (!InBounds(x, y)) return false;
        var bit = 1 << (int)type;
        return (PassableMask[Index(x, y)] & bit) != 0;
    }

    public void SetTerrain(int x, int y, byte terrainId) { if (InBounds(x, y)) Terrain[Index(x, y)] = terrainId; }
    public byte GetTerrain(int x, int y) => InBounds(x, y) ? Terrain[Index(x, y)] : (byte)0;

    public void SetHeight(int x, int y, byte height) { if (InBounds(x, y)) Height[Index(x, y)] = height; }

    public void ClearFog() { Array.Clear(Visibility, 0, Visibility.Length); Array.Clear(FogOfWar, 0, FogOfWar.Length); }
    public void SetVisible(int x, int y, int playerMask) { if (InBounds(x, y)) { var idx = Index(x, y); Visibility[idx] |= (uint)playerMask; FogOfWar[idx] = 2; } }
    public void SetExplored(int x, int y, int playerMask) { if (InBounds(x, y)) { var idx = Index(x, y); if (FogOfWar[idx] == 0) FogOfWar[idx] = 1; } }

    public Vector2 TileToWorld(int x, int y) => new Vector2((x - y) * 16f, (x + y) * 8f);
    public Vector2I WorldToTile(Vector2 worldPos) { float isoX = worldPos.X / 16f; float isoY = worldPos.Y / 8f; int x = (int)MathF.Round((isoX + isoY) / 2f); int y = (int)MathF.Round((isoY - isoX) / 2f); return new Vector2I(x, y); }
    public void ClearPathfinding() { Array.Clear(Distances, 0, Distances.Length); Array.Clear(Parents, 0, Parents.Length); Array.Clear(OpenFlags, 0, OpenFlags.Length); }
}

public static class Pathfinding
{
    private static readonly (int dx, int dy)[] Directions8 = { (1, 0), (1, 1), (0, 1), (-1, 1), (-1, 0), (-1, -1), (0, -1), (1, -1) };
    public static List<Vector2I> FindPath(GridMap grid, Vector2I start, Vector2I goal, UnitType unitType, int maxNodes = 10000)
    {
        var path = new List<Vector2I>();
        if (!grid.InBounds(start.X, start.Y) || !grid.InBounds(goal.X, goal.Y)) return path;
        if (!grid.IsPassable(goal.X, goal.Y, unitType)) return path;
        grid.ClearPathfinding();
        var openSet = new PriorityQueue<int, int>();
        int startIdx = grid.Index(start.X, start.Y);
        int goalIdx = grid.Index(goal.X, goal.Y);
        grid.Distances[startIdx] = 0;
        openSet.Enqueue(startIdx, Heuristic(start.X, start.Y, goal.X, goal.Y));
        int nodesExpanded = 0;
        while (openSet.Count > 0 && nodesExpanded < maxNodes)
        {
            int current = openSet.Dequeue();
            if (current == goalIdx) break;
            int cx = current % GridMap.MapWidthTiles;
            int cy = current / GridMap.MapWidthTiles;
            if (grid.OpenFlags[current] == 2) continue;
            grid.OpenFlags[current] = 2;
            nodesExpanded++;
            for (int i = 0; i < 8; i++)
            {
                int nx = cx + Directions8[i].dx;
                int ny = cy + Directions8[i].dy;
                if (!grid.InBounds(nx, ny)) continue;
                if (!grid.IsPassable(nx, ny, unitType)) continue;
                if (i % 2 == 1)
                {
                    var d1 = Directions8[(i + 7) % 8];
                    var d2 = Directions8[(i + 1) % 8];
                    if (!grid.IsPassable(cx + d1.dx, cy + d1.dy, unitType) || !grid.IsPassable(cx + d2.dx, cy + d2.dy, unitType))
                        continue;
                }
                int neighborIdx = grid.Index(nx, ny);
                if (grid.OpenFlags[neighborIdx] == 2) continue;
                int gCost = grid.Distances[current] + (i % 2 == 0 ? 10 : 14);
                if (grid.OpenFlags[neighborIdx] == 0 || gCost < grid.Distances[neighborIdx])
                {
                    grid.Distances[neighborIdx] = gCost;
                    grid.Parents[neighborIdx] = current;
                    int fCost = gCost + Heuristic(nx, ny, goal.X, goal.Y);
                    openSet.Enqueue(neighborIdx, fCost);
                    grid.OpenFlags[neighborIdx] = 1;
                }
            }
        }
        if (grid.Distances[goalIdx] > 0 || startIdx == goalIdx)
        {
            int current = goalIdx;
            while (current != startIdx)
            {
                int x = current % GridMap.MapWidthTiles;
                int y = current / GridMap.MapWidthTiles;
                path.Add(new Vector2I(x, y));
                current = grid.Parents[current];
                if (path.Count > 10000) break;
            }
            path.Reverse();
        }
        return path;
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)] private static int Heuristic(int x1, int y1, int x2, int y2) { int dx = Math.Abs(x1 - x2); int dy = Math.Abs(y1 - y2); return Math.Max(dx, dy) * 10 + Math.Min(dx, dy) * 4; }
}
internal sealed class PriorityQueue<T, TPriority> where TPriority : IComparable<TPriority>
{
    private readonly List<(T Item, TPriority Priority)> _heap = new();
    public int Count => _heap.Count;
    public void Enqueue(T item, TPriority priority) { _heap.Add((item, priority)); SiftUp(_heap.Count - 1); }
    public T Dequeue() { var root = _heap[0]; var last = _heap[_heap.Count - 1]; _heap.RemoveAt(_heap.Count - 1); if (_heap.Count > 0) { _heap[0] = last; SiftDown(0); } return root.Item; }
    private void SiftUp(int i) { while (i > 0) { int parent = (i - 1) / 2; if (_heap[i].Priority.CompareTo(_heap[parent].Priority) >= 0) break; (_heap[i], _heap[parent]) = (_heap[parent], _heap[i]); i = parent; } }
    private void SiftDown(int i) { while (true) { int left = 2 * i + 1; int right = 2 * i + 2; int smallest = i; if (left < _heap.Count && _heap[left].Priority.CompareTo(_heap[smallest].Priority) < 0) smallest = left; if (right < _heap.Count && _heap[right].Priority.CompareTo(_heap[smallest].Priority) < 0) smallest = right; if (smallest == i) break; (_heap[i], _heap[smallest]) = (_heap[smallest], _heap[i]); i = smallest; } }
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct ReplayFrame { public int Tick; public PlayerCommand[] Commands; public ulong Checksum; }

public sealed class CommandBuffer
{
    private readonly Queue<PlayerCommand> _pending = new();
    private readonly List<PlayerCommand> _executed = new();

    public void QueueCommand(PlayerCommand cmd) => _pending.Enqueue(cmd);

    public PlayerCommand[] GetCommandsForTick(int tick)
    {
        var list = new List<PlayerCommand>();
        while (_pending.Count > 0 && _pending.Peek().Tick <= tick)
            list.Add(_pending.Dequeue());
        return list.ToArray();
    }

    public void MarkExecuted(PlayerCommand cmd) => _executed.Add(cmd);
    public IReadOnlyList<PlayerCommand> ExecutedCommands => _executed;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct PlayerCommand
{
    public int Tick;
    public byte PlayerIndex;
    public Command Command;
    public Entity[] Entities;
    public Vector2I TargetPosition;
    public Entity TargetEntity;
    public byte[] Payload;

    public CommandType Type => Command.Type;
    public Entity[] SelectedEntities => Entities;
}

public struct Command
{
    public CommandType Type;
    public Entity[] SelectedEntities;
    public Vector2I TargetPosition;
    public Entity TargetEntity;
}

public static class GameConfig
{
    public const int MaxPlayers = 8;
    public const int MapWidthTiles = 256;
    public const int MapHeightTiles = 256;
    public const int MaxEntities = 65535;
    public const int MaxSelectedUnits = 255;
    public const int ViewDistance = 12;
    public const int FogUpdateInterval = 4;
    public const int StartingCredits = 5000;
    public const int StartingEnergy = 100;
    public const int HarvesterCapacity = 700;
    public const int OreValuePerLoad = 700;
    public const int CreditTickIncome = 0;
    public const float MinDamageVariance = 0.85f;
    public const float MaxDamageVariance = 1.15f;
    public const int MaxAttackRange = 10;
    public const float SelectionBoxMinSize = 4f;
    public const float ScrollEdgeMargin = 16f;
    public const float ScrollSpeed = 800f;
    public static Fixed TickDuration => Fixed.FromFloat(1f / SimulationState.TicksPerSecond);
}