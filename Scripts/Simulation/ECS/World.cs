#nullable disable
namespace AkteEuropaReborn.Simulation.ECS;

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using AkteEuropaReborn.Core.Math;
using AkteEuropaReborn.Simulation.Components;

[System.Diagnostics.DebuggerDisplay("Entity {Index}.{Generation}")]
public readonly struct Entity : IEquatable<Entity>
{
    public readonly uint Index;
    public readonly ushort Generation;

    public Entity(uint index, ushort generation) { Index = index; Generation = generation; }
    public static readonly Entity Null = new(0xFFFFFFFF, 0);
    public bool IsNull => Index == 0xFFFFFFFF;
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Equals(Entity other) => Index == other.Index && Generation == other.Generation;
    public override bool Equals(object obj) => obj is Entity other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Index, Generation);
    public static bool operator ==(Entity a, Entity b) => a.Equals(b);
    public static bool operator !=(Entity a, Entity b) => !a.Equals(b);
    public override string ToString() => IsNull ? "Entity.Null" : $"Entity[{Index}.{Generation}]";
}

public readonly struct ComponentType : IEquatable<ComponentType>
{
    public readonly int Id, Size;
    public readonly string Name;
    private ComponentType(int id, int size, string name) => (Id, Size, Name) = (id, size, name);
    private static int _nextId = 0;
    private static readonly Dictionary<Type, ComponentType> _cache = new();
    public static ComponentType Get<T>() where T : struct
    {
        var type = typeof(T);
        if (_cache.TryGetValue(type, out var ct)) return ct;
        ct = new ComponentType(_nextId++, System.Runtime.InteropServices.Marshal.SizeOf<T>(), type.Name);
        _cache[type] = ct;
        return ct;
    }
    public bool Equals(ComponentType other) => Id == other.Id;
    public override bool Equals(object obj) => obj is ComponentType ct && Equals(ct);
    public override int GetHashCode() => Id;
    public static bool operator ==(ComponentType a, ComponentType b) => a.Id == b.Id;
    public static bool operator !=(ComponentType a, ComponentType b) => a.Id != b.Id;
}

public interface IComponentPool
{
    void Move(Entity entity, Archetype fromArchetype, int fromIndex, Archetype toArchetype, int toIndex);
    void Remove(Entity entity);
    void Clear();
}

public sealed class ComponentPool<T> : IComponentPool where T : struct
{
    private readonly ComponentType _type = ComponentType.Get<T>();
    private readonly List<Archetype> _archetypes = new();
    private readonly Dictionary<Archetype, T[]> _data = new();
    private readonly Dictionary<Entity, (Archetype Archetype, int Index)> _entityLocation = new();
    public ComponentType Type => _type;
    public bool Has(Entity entity) => _entityLocation.ContainsKey(entity);
    public ref T Get(Entity entity) { var (archetype, index) = _entityLocation[entity]; return ref _data[archetype][index]; }
    public bool TryGet(Entity entity, out T component) { if (_entityLocation.TryGetValue(entity, out var loc)) { component = _data[loc.Archetype][loc.Index]; return true; } component = default; return false; }
    public ref T Add(Entity entity, Archetype archetype, int index) { if (!_data.TryGetValue(archetype, out var array)) { array = new T[1024]; _data[archetype] = array; } if (index >= array.Length) Array.Resize(ref array, array.Length * 2); _entityLocation[entity] = (archetype, index); return ref array[index]; }
    public void Remove(Entity entity) { if (_entityLocation.Remove(entity, out var loc)) { var array = _data[loc.Archetype]; var lastIndex = loc.Archetype.EntityCount - 1; if (loc.Index != lastIndex) { array[loc.Index] = array[lastIndex]; var movedEntity = loc.Archetype.Entities[lastIndex]; _entityLocation[movedEntity] = (loc.Archetype, loc.Index); } } }
    public void Move(Entity entity, Archetype fromArchetype, int fromIndex, Archetype toArchetype, int toIndex) { var value = _data[fromArchetype][fromIndex]; ref var dest = ref Add(entity, toArchetype, toIndex); dest = value; _entityLocation.Remove(entity); }
    public IEnumerable<(Entity Entity, T Component)> Iterate(Archetype archetype) { if (!_data.TryGetValue(archetype, out var array)) yield break; for (int i = 0; i < archetype.EntityCount; i++) yield return (archetype.Entities[i], array[i]); }
    public void Clear() { _data.Clear(); _entityLocation.Clear(); }
}

public sealed class Archetype
{
    public readonly ComponentType[] ComponentTypes;
    public readonly int[] ComponentOffsets;
    public readonly int EntitySize;
    public readonly List<Entity> Entities = new();
    public readonly Dictionary<Entity, int> EntityIndex = new();
    public ulong Signature { get; }
    public int EntityCount { get; internal set; } = 0;

    private Archetype(ComponentType[] types)
    {
        ComponentTypes = types;
        ComponentOffsets = new int[types.Length];
        int offset = 4;
        for (int i = 0; i < types.Length; i++) { ComponentOffsets[i] = offset; offset += types[i].Size; }
        EntitySize = offset;
        ulong sig = 0; foreach (var t in types) sig |= 1UL << t.Id; Signature = sig;
    }

    public static Archetype Create(params ComponentType[] types) { Array.Sort(types, (a, b) => a.Id - b.Id); return new Archetype(types); }
    public bool HasComponent(ComponentType type) { foreach (var t in ComponentTypes) if (t.Id == type.Id) return true; return false; }
    public int GetComponentIndex(ComponentType type) { for (int i = 0; i < ComponentTypes.Length; i++) if (ComponentTypes[i].Id == type.Id) return i; return -1; }
}
public sealed class World
{
    private uint _nextEntityIndex = 1;
    internal readonly Dictionary<Entity, Archetype> _entityArchetype = new();
    private readonly List<Archetype> _archetypes = new();
    internal readonly Dictionary<ComponentType, IComponentPool> _pools = new();
    private readonly List<Entity> _toDestroy = new();
    private readonly Dictionary<Type, object> _singletons = new();

    public int EntityCount => _entityArchetype.Count;
    public int ArchetypeCount => _archetypes.Count;

    public Entity Create()
    {
        var entity = new Entity(_nextEntityIndex++, 0);
        var archetype = GetOrCreateArchetype(Array.Empty<ComponentType>());
        archetype.Entities.Add(entity);
        archetype.EntityIndex[entity] = archetype.EntityCount++;
        _entityArchetype[entity] = archetype;
        return entity;
    }

    public Entity Create(params ComponentType[] componentTypes)
    {
        var entity = new Entity(_nextEntityIndex++, 0);
        var archetype = GetOrCreateArchetype(componentTypes);
        archetype.Entities.Add(entity);
        archetype.EntityIndex[entity] = archetype.EntityCount++;
        _entityArchetype[entity] = archetype;
        return entity;
    }

    public void Destroy(Entity entity) => _toDestroy.Add(entity);

    public void FlushDestroy()
    {
        foreach (var entity in _toDestroy)
        {
            if (!_entityArchetype.TryGetValue(entity, out var archetype)) continue;
            foreach (var pool in _pools.Values) pool.Remove(entity);
            var index = archetype.EntityIndex[entity];
            var lastEntity = archetype.Entities[archetype.EntityCount - 1];
            archetype.Entities[index] = lastEntity;
            archetype.EntityIndex[lastEntity] = index;
            archetype.Entities.RemoveAt(archetype.EntityCount - 1);
            archetype.EntityIndex.Remove(entity);
            archetype.EntityCount--;
            _entityArchetype.Remove(entity);
        }
        _toDestroy.Clear();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref T AddComponent<T>(Entity entity) where T : struct
    {
        var type = ComponentType.Get<T>();
        var pool = GetOrCreatePool<T>();
        var oldArchetype = _entityArchetype[entity];
        var newTypes = new List<ComponentType>(oldArchetype.ComponentTypes) { type }.ToArray();
        var newArchetype = GetOrCreateArchetype(newTypes);
        int oldIndex = oldArchetype.EntityIndex[entity];
        int newIndex = newArchetype.EntityCount;
        oldArchetype.Entities[oldIndex] = oldArchetype.Entities[oldArchetype.EntityCount - 1];
        oldArchetype.EntityIndex[oldArchetype.Entities[oldIndex]] = oldIndex;
        oldArchetype.Entities.RemoveAt(oldArchetype.EntityCount - 1);
        oldArchetype.EntityCount--;
        newArchetype.Entities.Add(entity);
        newArchetype.EntityIndex[entity] = newIndex;
        newArchetype.EntityCount++;
        _entityArchetype[entity] = newArchetype;
        foreach (var oldType in oldArchetype.ComponentTypes) { var oldPool = _pools[oldType]; var newPool = _pools[oldType]; oldPool.Move(entity, oldArchetype, oldIndex, newArchetype, newIndex); }
        return ref pool.Add(entity, newArchetype, newIndex);
    }

    public void RemoveComponent<T>(Entity entity) where T : struct
    {
        var type = ComponentType.Get<T>();
        var oldArchetype = _entityArchetype[entity];
        if (!oldArchetype.HasComponent(type)) return;
        var newTypes = new List<ComponentType>(oldArchetype.ComponentTypes);
        newTypes.Remove(type);
        var newArchetype = GetOrCreateArchetype(newTypes.ToArray());
        int oldIndex = oldArchetype.EntityIndex[entity];
        int newIndex = newArchetype.EntityCount;
        oldArchetype.Entities[oldIndex] = oldArchetype.Entities[oldArchetype.EntityCount - 1];
        oldArchetype.EntityIndex[oldArchetype.Entities[oldIndex]] = oldIndex;
        oldArchetype.Entities.RemoveAt(oldArchetype.EntityCount - 1);
        oldArchetype.EntityCount--;
        newArchetype.Entities.Add(entity);
        newArchetype.EntityIndex[entity] = newIndex;
        newArchetype.EntityCount++;
        _entityArchetype[entity] = newArchetype;
        foreach (var oldType in oldArchetype.ComponentTypes) { if (oldType.Id == type.Id) continue; var oldPool = _pools[oldType]; var newPool = _pools[oldType]; oldPool.Move(entity, oldArchetype, oldIndex, newArchetype, newIndex); }
        _pools[type].Remove(entity);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool HasComponent<T>(Entity entity) where T : struct
    {
        var type = ComponentType.Get<T>();
        return _entityArchetype.TryGetValue(entity, out var archetype) && archetype.HasComponent(type);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref T GetComponent<T>(Entity entity) where T : struct => ref GetOrCreatePool<T>().Get(entity);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetComponent<T>(Entity entity, out T component) where T : struct => GetOrCreatePool<T>().TryGet(entity, out component);

    public Query<T> Query<T>() where T : struct { var type = ComponentType.Get<T>(); var mask = 1UL << type.Id; return new Query<T>(this, mask); }
    public Query<T1, T2> Query<T1, T2>() where T1 : struct where T2 : struct { var id1 = ComponentType.Get<T1>().Id; var id2 = ComponentType.Get<T2>().Id; var mask = (1UL << id1) | (1UL << id2); return new Query<T1, T2>(this, mask); }
    public Query<T1, T2, T3> Query<T1, T2, T3>() where T1 : struct where T2 : struct where T3 : struct { var id1 = ComponentType.Get<T1>().Id; var id2 = ComponentType.Get<T2>().Id; var id3 = ComponentType.Get<T3>().Id; var mask = (1UL << id1) | (1UL << id2) | (1UL << id3); return new Query<T1, T2, T3>(this, mask); }
    public Query<T1, T2, T3, T4> Query<T1, T2, T3, T4>() where T1 : struct where T2 : struct where T3 : struct where T4 : struct { var id1 = ComponentType.Get<T1>().Id; var id2 = ComponentType.Get<T2>().Id; var id3 = ComponentType.Get<T3>().Id; var id4 = ComponentType.Get<T4>().Id; var mask = (1UL << id1) | (1UL << id2) | (1UL << id3) | (1UL << id4); return new Query<T1, T2, T3, T4>(this, mask); }
    public Query<T1, T2> Query2<T1, T2>() where T1 : struct where T2 : struct => Query<T1, T2>();
    public Query<T1, T2, T3> Query3<T1, T2, T3>() where T1 : struct where T2 : struct where T3 : struct => Query<T1, T2, T3>();
    public Query<T1, T2, T3, T4> Query4<T1, T2, T3, T4>() where T1 : struct where T2 : struct where T3 : struct where T4 : struct => Query<T1, T2, T3, T4>();

    private Archetype GetOrCreateArchetype(ComponentType[] types)
    {
        foreach (var a in _archetypes) { if (a.ComponentTypes.Length != types.Length) continue; bool match = true; for (int i = 0; i < types.Length; i++) if (a.ComponentTypes[i].Id != types[i].Id) { match = false; break; } if (match) return a; }
        var newArchetype = Archetype.Create(types);
        _archetypes.Add(newArchetype);
        return newArchetype;
    }

    internal ComponentPool<T> GetOrCreatePool<T>() where T : struct
    {
        var type = ComponentType.Get<T>();
        if (_pools.TryGetValue(type, out var pool)) return (ComponentPool<T>)pool;
        var newPool = new ComponentPool<T>();
        _pools[type] = newPool;
        return newPool;
    }

    public ref T AddComponent<T>(Entity entity, T value) where T : struct
    {
        ref var c = ref AddComponent<T>(entity);
        c = value;
        return ref c;
    }

    public void SetComponent<T>(Entity entity, T value) where T : struct
    {
        ref var c = ref GetComponent<T>(entity);
        c = value;
    }

    public bool Alive(Entity entity) => _entityArchetype.ContainsKey(entity);
}

public readonly struct Query<T> where T : struct
{
    private readonly World _world;
    private readonly ulong _mask;
    public Query(World world, ulong mask) { _world = world; _mask = mask; }
    public IEnumerable<Entity> Entities() { foreach (var (entity, arch) in _world._entityArchetype) if ((arch.Signature & _mask) != 0) yield return entity; }
}

public readonly struct Query<T1, T2> where T1 : struct where T2 : struct
{
    private readonly World _world;
    private readonly ulong _mask;
    public Query(World world, ulong mask) { _world = world; _mask = mask; }
    public IEnumerable<(Entity Entity, T1 C1, T2 C2)> Iterate()
    {
        foreach (var (entity, arch) in _world._entityArchetype)
        {
            if ((arch.Signature & _mask) == _mask)
            {
                var pool1 = _world.GetOrCreatePool<T1>();
                var pool2 = _world.GetOrCreatePool<T2>();
                for (int i = 0; i < arch.EntityCount; i++)
                {
                    var e = arch.Entities[i];
                    if (!pool1.Has(e) || !pool2.Has(e)) continue;
                    yield return (e, pool1.Get(e), pool2.Get(e));
                }
            }
        }
    }
    public IEnumerable<Entity> Entities()
    {
        foreach (var (entity, arch) in _world._entityArchetype)
            if ((arch.Signature & _mask) == _mask) yield return entity;
    }
}

public readonly struct Query<T1, T2, T3> where T1 : struct where T2 : struct where T3 : struct
{
    private readonly World _world;
    private readonly ulong _mask;
    public Query(World world, ulong mask) { _world = world; _mask = mask; }
    public IEnumerable<(Entity Entity, T1 C1, T2 C2, T3 C3)> Iterate()
    {
        foreach (var (entity, arch) in _world._entityArchetype)
        {
            if ((arch.Signature & _mask) == _mask)
            {
                var p1 = _world.GetOrCreatePool<T1>();
                var p2 = _world.GetOrCreatePool<T2>();
                var p3 = _world.GetOrCreatePool<T3>();
                for (int i = 0; i < arch.EntityCount; i++)
                {
                    var e = arch.Entities[i];
                    if (!p1.Has(e) || !p2.Has(e) || !p3.Has(e)) continue;
                    yield return (e, p1.Get(e), p2.Get(e), p3.Get(e));
                }
            }
        }
    }
}

public readonly struct Query<T1, T2, T3, T4> where T1 : struct where T2 : struct where T3 : struct where T4 : struct
{
    private readonly World _world;
    private readonly ulong _mask;
    public Query(World world, ulong mask) { _world = world; _mask = mask; }
    public IEnumerable<(Entity Entity, T1 C1, T2 C2, T3 C3, T4 C4)> Iterate()
    {
        foreach (var (entity, arch) in _world._entityArchetype)
        {
            if ((arch.Signature & _mask) == _mask)
            {
                var p1 = _world.GetOrCreatePool<T1>();
                var p2 = _world.GetOrCreatePool<T2>();
                var p3 = _world.GetOrCreatePool<T3>();
                var p4 = _world.GetOrCreatePool<T4>();
                for (int i = 0; i < arch.EntityCount; i++)
                {
                    var e = arch.Entities[i];
                    if (!p1.Has(e) || !p2.Has(e) || !p3.Has(e) || !p4.Has(e)) continue;
                    yield return (e, p1.Get(e), p2.Get(e), p3.Get(e), p4.Get(e));
                }
            }
        }
    }
}