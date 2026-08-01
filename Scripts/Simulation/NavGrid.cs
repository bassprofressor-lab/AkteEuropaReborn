namespace AkteEuropaReborn.Simulation;

using System;
using System.Collections.Generic;
using Godot;
using GDict = Godot.Collections.Dictionary<string, Godot.Variant>;

/// <summary>
/// Walkability grid + A* over a legacy CWM map, built from the baked map JSON.
///
/// TERRAIN CLASS — the CWM sec2 grid, read COLUMN-major (index = col*257 + row,
/// same convention as sec6). That was the missing piece: read row-major it looks
/// like noise, read correctly it is the game's own terrain map, proven against
/// the baked pixels of all 23 maps:
///   0 = water / impassable  (every water tile — ground code 0..7 — is class 0,
///       with zero counter-examples; class 0 additionally covers the partly-wet
///       shore transition tiles)
///   1 = shore / sand        2 = open land        3 = special land
///
/// Further sources of impassability:
///   * PROPS — cells flagged `object` (CWM code >= 10000: trees, walls, rocks,
///     buildings). These are baked into the map picture and physically block.
///   * CLIFFS — a step of >= <see cref="MaxClimb"/> elevation levels between two
///     neighbouring cells; smaller steps just cost more.
///   * ENTITIES — live units/buildings occupy their cell (dynamic layer).
///
/// DOMAIN — land units may not enter class 0, naval units may not leave it.
/// Which units are naval was taken from the original placements, not from names:
/// unit_types 150..153 stand on water on every map they appear on (45/45), while
/// every other type is placed on land — see <c>MapEntityLayer.NavalTypes</c>.
/// </summary>
public sealed class NavGrid
{
    /// <summary>Movement domain of a unit.</summary>
    public enum Domain { Land = 0, Naval = 1 }

    /// <summary>Ground tile codes 0..7 are the animated water cycle (fallback
    /// terrain source when a map has no sec2 grid).</summary>
    public const int WaterCodeMax = 7;

    /// <summary>Elevation step (in levels) that a ground unit can no longer climb.</summary>
    public const int MaxClimb = 3;

    // sec2 terrain classes
    public const byte ClassWater = 0;
    public const byte ClassShore = 1;
    public const byte ClassLand = 2;
    public const byte ClassSpecial = 3;

    private const byte FreeCell = 0;
    private const byte StaticBlock = 1;

    public int Width { get; private set; }
    public int Height { get; private set; }
    public bool HasZones { get; private set; }

    private byte[] _static = Array.Empty<byte>();   // props (never changes)
    private byte[] _class = Array.Empty<byte>();    // sec2 terrain class
    private byte[] _elev = Array.Empty<byte>();
    private int[] _occupant = Array.Empty<int>();   // entity index or -1

    public bool InBounds(int c, int r) => c >= 0 && r >= 0 && c < Width && r < Height;

    private int Idx(int c, int r) => r * Width + c;

    public byte ClassAt(int c, int r) => InBounds(c, r) ? _class[Idx(c, r)] : ClassWater;

    public bool IsProp(int c, int r) => !InBounds(c, r) || _static[Idx(c, r)] == StaticBlock;

    public int ElevAt(int c, int r) => InBounds(c, r) ? _elev[Idx(c, r)] : 0;

    public int OccupantAt(int c, int r) => InBounds(c, r) ? _occupant[Idx(c, r)] : -1;

    /// <summary>Terrain the given domain can travel on, ignoring occupants.</summary>
    public bool IsWalkable(int c, int r, Domain domain = Domain.Land)
    {
        if (!InBounds(c, r)) return false;
        int i = Idx(c, r);
        if (_static[i] == StaticBlock) return false;         // props block everyone
        return domain == Domain.Naval ? _class[i] == ClassWater : _class[i] != ClassWater;
    }

    /// <summary>Free AND unoccupied (except by <paramref name="mover"/> itself).</summary>
    public bool IsFree(int c, int r, Domain domain = Domain.Land, int mover = -1)
        => IsWalkable(c, r, domain) &&
           (_occupant[Idx(c, r)] < 0 || _occupant[Idx(c, r)] == mover);

    /// <summary>
    /// Relative cost of crossing a cell: sand/shore slows a ground unit down,
    /// open water is free sailing. Also used as the inverse speed factor so the
    /// visible movement matches the path cost.
    /// </summary>
    public float TerrainCost(int c, int r, Domain domain)
    {
        if (domain == Domain.Naval) return 1f;
        return ClassAt(c, r) switch
        {
            ClassShore => 1.45f,     // beach / dunes
            ClassSpecial => 1.15f,
            _ => 1f,
        };
    }

    // ---- construction -------------------------------------------------------

    public static NavGrid Build(GDict meta)
    {
        var g = new NavGrid
        {
            Width = GetI(meta, "width"),
            Height = GetI(meta, "height"),
        };
        if (g.Width <= 0 || g.Height <= 0) { g.Width = g.Height = 0; return g; }

        int n = g.Width * g.Height;
        g._static = new byte[n];
        g._class = new byte[n];
        g._elev = new byte[n];
        g._occupant = new int[n];
        Array.Fill(g._occupant, -1);
        Array.Fill(g._class, ClassLand);

        if (!meta.TryGetValue("tiles", out var tv) || tv.VariantType != Variant.Type.Array)
            return g;

        foreach (var item in tv.AsGodotArray())
        {
            if (item.VariantType != Variant.Type.Dictionary) continue;
            var t = item.AsGodotDictionary<string, Variant>();
            int c = GetI(t, "col"), r = GetI(t, "row");
            if (!g.InBounds(c, r)) continue;
            int i = g.Idx(c, r);
            g._elev[i] = (byte)Mathf.Clamp(GetI(t, "elev"), 0, 255);

            bool isObject = t.TryGetValue("object", out var ob) && ob.AsBool();
            if (isObject) g._static[i] = StaticBlock;
            else if (GetI(t, "code", 9999) <= WaterCodeMax) g._class[i] = ClassWater;
        }
        return g;
    }

    /// <summary>
    /// Overlay the real terrain classes from the map's sec2 grid (entities.json
    /// `zones`), which is authoritative — it also marks the half-wet shore tiles
    /// and rock faces that the tile-code rule alone misses.
    /// </summary>
    public void ApplyZones(GDict zones)
    {
        int w = GetI(zones, "width"), h = GetI(zones, "height");
        if (w <= 0 || h <= 0 ||
            !zones.TryGetValue("grid", out var gv) || gv.VariantType != Variant.Type.Array)
            return;
        var rows = gv.AsGodotArray();
        for (int r = 0; r < Height && r < rows.Count; r++)
        {
            if (rows[r].VariantType != Variant.Type.Array) continue;
            var cells = rows[r].AsGodotArray();
            for (int c = 0; c < Width && c < cells.Count; c++)
                _class[Idx(c, r)] = (byte)Mathf.Clamp(cells[c].AsInt32(), 0, 3);
        }
        HasZones = true;
    }

    private static int GetI(GDict d, string k, int def = 0)
        => d.TryGetValue(k, out var v) && v.VariantType != Variant.Type.Nil ? v.AsInt32() : def;

    // ---- dynamic occupancy --------------------------------------------------

    public void ClearOccupants() { if (_occupant.Length > 0) Array.Fill(_occupant, -1); }

    /// <summary>Take the static block off one cell. Used for the Nachschub-Posten:
    /// its tick handler @0x43e872 services the unit standing ON the post, so the
    /// structure baked into the map picture must not block that cell.</summary>
    public void ClearStatic(int c, int r)
    {
        if (InBounds(c, r)) _static[Idx(c, r)] = FreeCell;
    }

    public void SetOccupant(int c, int r, int entity)
    {
        if (InBounds(c, r)) _occupant[Idx(c, r)] = entity;
    }

    public void ClearOccupant(int c, int r, int entity)
    {
        if (InBounds(c, r) && _occupant[Idx(c, r)] == entity) _occupant[Idx(c, r)] = -1;
    }

    /// <summary>Cell counts per terrain class + props, for the HUD / debug overlay.</summary>
    public (int Water, int Shore, int Land, int Props) Census()
    {
        int w = 0, s = 0, l = 0, p = 0;
        for (int i = 0; i < _static.Length; i++)
        {
            if (_static[i] == StaticBlock) { p++; continue; }
            switch (_class[i])
            {
                case ClassWater: w++; break;
                case ClassShore: s++; break;
                default: l++; break;
            }
        }
        return (w, s, l, p);
    }

    /// <summary>Coarse debug texture: terrain classes tinted, props red.</summary>
    public ImageTexture? BuildDebugTexture()
    {
        if (Width <= 0 || Height <= 0) return null;
        var img = Image.CreateEmpty(Width, Height, false, Image.Format.Rgba8);
        var prop = new Color(1f, 0.25f, 0.2f, 0.40f);
        var byClass = new[]
        {
            new Color(0.15f, 0.45f, 1f, 0.45f),   // 0 water
            new Color(1f, 0.85f, 0.25f, 0.35f),   // 1 shore / sand
            new Color(0.25f, 0.9f, 0.4f, 0.22f),  // 2 open land
            new Color(0.7f, 0.5f, 1f, 0.30f),     // 3 special land
        };
        for (int r = 0; r < Height; r++)
            for (int c = 0; c < Width; c++)
            {
                int i = Idx(c, r);
                img.SetPixel(c, r, _static[i] == StaticBlock ? prop : byClass[_class[i]]);
            }
        return ImageTexture.CreateFromImage(img);
    }

    // ---- pathfinding --------------------------------------------------------

    private static readonly Vector2I[] Dirs =
    {
        new(1, 0), new(-1, 0), new(0, 1), new(0, -1),
        new(1, 1), new(1, -1), new(-1, 1), new(-1, -1),
    };

    /// <summary>Can <paramref name="mover"/> step from a to b (adjacent cells)?</summary>
    private bool CanStep(Vector2I a, Vector2I b, Domain domain, int mover)
    {
        if (!IsFree(b.X, b.Y, domain, mover)) return false;
        if (domain == Domain.Land &&
            Math.Abs(ElevAt(b.X, b.Y) - ElevAt(a.X, a.Y)) >= MaxClimb) return false;
        // no cutting a corner between two blocked cells
        if (a.X != b.X && a.Y != b.Y &&
            !(IsWalkable(b.X, a.Y, domain) && IsWalkable(a.X, b.Y, domain))) return false;
        return true;
    }

    /// <summary>
    /// A* from <paramref name="start"/> to <paramref name="goal"/> (8-way).
    /// Returns the waypoint cells WITHOUT the start cell, or null if unreachable.
    /// If the goal itself is blocked/occupied, the nearest free cell is used.
    /// </summary>
    public List<Vector2I>? FindPath(Vector2I start, Vector2I goal, Domain domain = Domain.Land,
                                    int mover = -1, int maxNodes = 60000)
    {
        if (!InBounds(start.X, start.Y) || !InBounds(goal.X, goal.Y)) return null;
        if (!IsFree(goal.X, goal.Y, domain, mover))
        {
            var alt = NearestFree(goal, domain, mover);
            if (alt == null) return null;
            goal = alt.Value;
        }
        if (start == goal) return new List<Vector2I>();

        var came = new Dictionary<Vector2I, Vector2I>();
        var gScore = new Dictionary<Vector2I, float> { [start] = 0f };
        var open = new PriorityQueue<Vector2I, float>();
        open.Enqueue(start, Heuristic(start, goal));
        var closed = new HashSet<Vector2I>();
        int expanded = 0;

        while (open.Count > 0 && expanded < maxNodes)
        {
            var cur = open.Dequeue();
            if (!closed.Add(cur)) continue;
            expanded++;
            if (cur == goal) return Reconstruct(came, cur);

            foreach (var d in Dirs)
            {
                var nb = new Vector2I(cur.X + d.X, cur.Y + d.Y);
                if (closed.Contains(nb) || !CanStep(cur, nb, domain, mover)) continue;

                float step = (d.X != 0 && d.Y != 0) ? 1.4142f : 1f;
                step *= TerrainCost(nb.X, nb.Y, domain);                            // sand is slow
                step += Math.Abs(ElevAt(nb.X, nb.Y) - ElevAt(cur.X, cur.Y)) * 0.5f; // climbing costs
                float tentative = gScore[cur] + step;
                if (gScore.TryGetValue(nb, out float known) && tentative >= known) continue;
                gScore[nb] = tentative;
                came[nb] = cur;
                open.Enqueue(nb, tentative + Heuristic(nb, goal));
            }
        }
        return null;
    }

    private static float Heuristic(Vector2I a, Vector2I b)
    {
        int dx = Math.Abs(a.X - b.X), dy = Math.Abs(a.Y - b.Y);
        return (dx + dy) + (1.4142f - 2f) * Math.Min(dx, dy); // octile
    }

    private static List<Vector2I> Reconstruct(Dictionary<Vector2I, Vector2I> came, Vector2I cur)
    {
        var path = new List<Vector2I> { cur };
        while (came.TryGetValue(cur, out var prev)) { cur = prev; path.Add(cur); }
        path.RemoveAt(path.Count - 1);   // drop the start cell
        path.Reverse();
        return path;
    }

    /// <summary>Closest free cell to <paramref name="around"/> (spiral search).</summary>
    public Vector2I? NearestFree(Vector2I around, Domain domain = Domain.Land,
                                 int mover = -1, int maxRadius = 12)
    {
        if (IsFree(around.X, around.Y, domain, mover)) return around;
        for (int rad = 1; rad <= maxRadius; rad++)
            for (int dy = -rad; dy <= rad; dy++)
                for (int dx = -rad; dx <= rad; dx++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != rad) continue;
                    int c = around.X + dx, r = around.Y + dy;
                    if (IsFree(c, r, domain, mover)) return new Vector2I(c, r);
                }
        return null;
    }
}
