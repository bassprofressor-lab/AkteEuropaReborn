namespace AkteEuropaReborn.Simulation;

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using AkteEuropaReborn.Core;
using AkteEuropaReborn.Core.Math;
using AkteEuropaReborn.Simulation.Components;
using AkteEuropaReborn.Simulation.ECS;
using Godot;

public static class Simulation
{
    public static void Step(SimulationState state)
    {
        if (state.IsPaused || state.Phase != GamePhase.Playing) return;
        ProcessCommands(state);
        Systems.ResourceSystem.Update(state);
        Systems.ProductionSystem.Update(state);
        Systems.MovementSystem.Update(state);
        Systems.CombatSystem.Update(state);
        Systems.AiSystem.Update(state);
        Systems.FogOfWarSystem.Update(state);
        Systems.PowerSystem.Update(state);
        Systems.VictorySystem.Update(state);
        state.CurrentTick++;
        state.FrameCount++;
    }

    private static void ProcessCommands(SimulationState state)
    {
        var commands = state.Commands.GetCommandsForTick(state.CurrentTick);
        foreach (var cmd in commands) { ExecuteCommand(state, cmd); state.Commands.MarkExecuted(cmd); }
    }

    private static void ExecuteCommand(SimulationState state, PlayerCommand cmd)
    {
        var world = state.World;
        foreach (var entity in cmd.Command.SelectedEntities)
        {
            if (!world.Alive(entity)) continue;
            switch (cmd.Command.Type)
            {
                case CommandType.Move:
                    if (world.HasComponent<Position>(entity))
                    {
                        var pos = world.GetComponent<Position>(entity);
                        var dir = new FixedVector2(cmd.Command.TargetPosition.X - pos.X, cmd.Command.TargetPosition.Y - pos.Y);
                        world.SetComponent(entity, new Velocity(dir.X, dir.Y));
                    }
                    break;
                case CommandType.Attack: break;
                case CommandType.Stop: world.RemoveComponent<Velocity>(entity); break;
            }
        }
    }
}

public static class Systems
{
    public static class ResourceSystem
    {
        public static void Update(SimulationState state)
        {
            foreach (var (entity, storage, owner) in state.World.Query<ResourceStorage, Owner>().Iterate())
            {
                if (owner.Faction != Faction.Neutral && owner.Faction != Faction.Gaia)
                {
                    ref var player = ref state.Players[owner.PlayerIndex];
                    player.Credits = storage.Credits;
                    player.Energy = storage.Energy;
                }
            }
        }
    }

    public static class ProductionSystem
    {
        public static void Update(SimulationState state)
        {
            foreach (var (entity, queue, owner, pos) in state.World.Query<BuildQueue, Owner, Position>().Iterate())
            {
                if (queue.CurrentUnit == 0) continue;
                var q = queue;
                q.CurrentProgress += (int)GameConfig.TickDuration.Raw;
                if (q.CurrentProgress >= q.TotalTime)
                {
                    q.CurrentProgress = 0;
                    q.CurrentUnit = 0;
                }
                state.World.SetComponent(entity, q);
            }
        }
    }

    public static class MovementSystem
    {
        public static void Update(SimulationState state)
        {
            var world = state.World;
            foreach (var (entity, pos, vel, rot, stats) in world.Query<Position, Velocity, Rotation, UnitStats>().Iterate())
            {
                if (vel.X.Raw == 0 && vel.Y.Raw == 0) continue;
                var speed = stats.MoveSpeed * GameConfig.TickDuration;
                var dx = vel.X * GameConfig.TickDuration;
                var dy = vel.Y * GameConfig.TickDuration;
                var lenSq = dx * dx + dy * dy;
                var maxSq = speed * speed;
                if (lenSq > maxSq)
                {
                    var factor = Fixed.FromFloat(MathF.Sqrt((float)(maxSq / lenSq)));
                    dx *= factor;
                    dy *= factor;
                }
                world.SetComponent(entity, new Position(pos.X + dx, pos.Y + dy, pos.Z));
                if (dx.Raw != 0 || dy.Raw != 0)
                {
                    var angle = Fixed.FromFloat(MathF.Atan2((float)dy, (float)dx) * 180f / MathF.PI);
                    world.SetComponent(entity, new Rotation(angle));
                }
            }
        }
    }

    public static class CombatSystem
    {
        public static void Update(SimulationState state)
        {
            var world = state.World;
            foreach (var (entity, health, stats, owner, pos) in world.Query<Health, UnitStats, Owner, Position>().Iterate())
            {
                if (health.IsDead) { world.Destroy(entity); continue; }
            }
        }
    }

    public static class AiSystem
    {
        public static void Update(SimulationState state)
        {
            var world = state.World;
            foreach (var (entity, ai, pos, stats, owner) in world.Query<AiState, Position, UnitStats, Owner>().Iterate())
            {
                if (owner.Faction < Faction.Player1) continue;
                var a = ai;
                a.ThinkTimer--;
                if (a.ThinkTimer <= 0)
                {
                    a.ThinkTimer = 20 * state.Rng.NextInt(1, 4);
                    switch (a.Behavior)
                    {
                        case AiBehavior.AttackMove: break;
                        case AiBehavior.Harvest: break;
                    }
                }
                world.SetComponent(entity, a);
            }
        }
    }

    public static class FogOfWarSystem
    {
        public static void Update(SimulationState state)
        {
            Array.Clear(state.Grid.Visibility, 0, state.Grid.Visibility.Length);
            foreach (var (entity, pos, stats, owner) in state.World.Query<Position, UnitStats, Owner>().Iterate())
            {
                if (owner.Faction == Faction.Neutral) continue;
                int sight = stats.SightRange;
                int cx = pos.X.ToInt(), cy = pos.Y.ToInt();
                for (int dy = -sight; dy <= sight; dy++)
                for (int dx = -sight; dx <= sight; dx++)
                {
                    int nx = cx + dx, ny = cy + dy;
                    if (!state.Grid.InBounds(nx, ny)) continue;
                    int distSq = dx * dx + dy * dy;
                    if (distSq <= sight * sight)
                    {
                        int idx = state.Grid.Index(nx, ny);
                        state.Grid.Visibility[idx] |= 1u << owner.PlayerIndex;
                        state.Grid.FogOfWar[idx] = 2;
                    }
                }
            }
            for (int i = 0; i < state.Grid.Visibility.Length; i++)
                if (state.Grid.Visibility[i] != 0)
                    state.Grid.FogOfWar[i] = (byte)Math.Max((int)state.Grid.FogOfWar[i], 1);
        }
    }

    public static class PowerSystem
    {
        public static void Update(SimulationState state)
        {
            foreach (var (entity, storage, owner) in state.World.Query<ResourceStorage, Owner>().Iterate())
            {
                if (owner.Faction >= Faction.Player1)
                {
                    ref var player = ref state.Players[owner.PlayerIndex];
                    player.PowerOutput = 0;
                    player.PowerDrain = 0;
                }
            }
        }
    }

    public static class VictorySystem
    {
        public static void Update(SimulationState state)
        {
            int alivePlayers = 0;
            Faction lastAlive = Faction.Neutral;
            for (int i = 0; i < GameConfig.MaxPlayers; i++)
            {
                var player = state.Players[i];
                if (!player.HasLost && player.Faction != Faction.Neutral)
                {
                    alivePlayers++;
                    lastAlive = player.Faction;
                }
            }
            if (alivePlayers <= 1)
            {
                state.Phase = GamePhase.Ended;
                if (alivePlayers == 1)
                    for (int i = 0; i < GameConfig.MaxPlayers; i++)
                        if (state.Players[i].Faction == lastAlive)
                            state.Players[i].HasWon = true;
            }
        }
    }
}