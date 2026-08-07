namespace AkteEuropaReborn.Simulation;

using System;
using System.Collections.Generic;
using Godot;
using GDict = Godot.Collections.Dictionary<string, Godot.Variant>;

/// <summary>
/// Walkability grid + A* over a legacy CWM map.
///
/// THE TERRAIN IS THE GAME'S OWN. It comes out of <c>Can_go</c> @0x4055D0 — the
/// routine every movement question goes through, named by its own debug line
/// "can go, number:" @0x4f6754. It takes a unit slot and a direction (the 8-way
/// table @0x4f5af0), and returns <b>0 no, 1 yes but someone has to give way,
/// 2 free</b>.
///
/// It asks exactly one map, the <b>imap</b> at 0xbdea80 (the game's own name,
/// `imap[rx+px][ry+py]:` @0x4f66b4) = sec6 of the map file, 256x256 u16 column
/// major. Three of its values are ground:
///
///   0xFFFE  free      — everyone
///   0xFFFD  rough     — foot yes, hover only where the tile's flag byte is 0,
///                       vehicles never
///   0xFFFC  water     — ships and hover only
///   0xFFFF, >= 14000  — the static object handles; they walk into the routine's
///                       own `nothing` path @0x405586 and block everyone
///
/// It branches on entity <b>+0x0a</b> (jump table @0x40678c, six cases): case 0
/// is the ordinary unit and splits again on the chassis <b>+0x0b</b> — 7 hover
/// (@0x4056b9), 0x11 walker (@0x405973), anything else "normal chassis"
/// (@0x405bd7) — and <b>case 4 is the ship</b> (@0x406669), whose only terrain
/// test is 0xFFFC.
///
/// ⚠ TWO EARLIER READINGS WITHDRAWN.
/// (1) "sec2 is the passability". It is not: Can_go never touches sec2. The
///     zones stay zones (the Z overlay), the passability comes from the imap.
///     On NET07 sec2 class 0 covers 7115 cells where the real water is 6452 —
///     that difference is why land units used to drive into the sea.
/// (2) "a cell with a prop on it blocks". It does not. Most object cells are
///     0xFFFE, that is free (map_02: 498, map_14: 512, NET07: 312) — bridges
///     and roads among them. That is why bridges used to be impassable.
///
/// OURS, and marked as such where it shows: <see cref="TerrainCost"/> (the
/// original has no per-cell movement cost in this routine), the climb limit
/// <see cref="MaxClimb"/>, and the ground under an occupied cell, which the imap
/// cannot say — see CwmData.Terrain.
/// </summary>
public sealed class NavGrid
{
    /// <summary>How a unit moves. The numbers are ours; what selects them is
    /// the game's: entity +0x0a == 4 or 5 is a ship, otherwise the chassis at
    /// +0x0b picks hover (7), walker (0x11) or the ordinary vehicle.</summary>
    public enum MoveClass { Vehicle = 0, Walker = 1, Hover = 2, Ship = 3 }

    /// <summary>The four ground classes Can_go distinguishes.</summary>
    public enum Ground : byte { Free = 0, Rough = 1, Water = 2, Blocked = 3 }

    /// <summary>Chassis (+0x0b) that Can_go singles out by name.</summary>
    public const int ChassisHover = 7;
    public const int ChassisWalker = 0x11;

    /// <summary>Entity +0x0a values that take the ship branch (@0x406669,
    /// @0x40671b) — both test the imap for 0xFFFC and nothing else.</summary>
    public static bool ArtIsShip(int art) => art is 4 or 5;

    /// <summary>The move class of a unit, the way Can_go picks its branch.</summary>
    public static MoveClass ClassOf(int art, int chassis)
        => ArtIsShip(art) ? MoveClass.Ship
         : chassis == ChassisHover ? MoveClass.Hover
         : chassis == ChassisWalker ? MoveClass.Walker
         : MoveClass.Vehicle;

    /// <summary>Ground tile codes 0..7 are the animated water cycle. Only used
    /// when a map was exported before the terrain block existed.</summary>
    public const int WaterCodeMax = 7;

    /// <summary>Elevation step (in levels) a ground unit can no longer climb.
    /// OURS — the original has no such test in Can_go.</summary>
    public const int MaxClimb = 3;

    public int Width { get; private set; }
    public int Height { get; private set; }

    /// <summary>True once the real imap terrain has been laid over the fallback.</summary>
    public bool HasTerrain { get; private set; }

    /// <summary>Cells whose ground the importer had to infer from their occupant.</summary>
    public int Inferred { get; private set; }

    private byte[] _ground = Array.Empty<byte>();     // <see cref="Ground"/>
    private byte[] _flag = Array.Empty<byte>();       // tile flag byte (slope 0..4)
    private byte[] _elev = Array.Empty<byte>();
    private int[] _occupant = Array.Empty<int>();     // entity index or -1
    private bool[] _crushable = Array.Empty<bool>();  // occupant is a foot soldier

    public bool InBounds(int c, int r) => c >= 0 && r >= 0 && c < Width && r < Height;

    private int Idx(int c, int r) => r * Width + c;

    public Ground GroundAt(int c, int r) => InBounds(c, r) ? (Ground)_ground[Idx(c, r)] : Ground.Blocked;

    public int FlagAt(int c, int r) => InBounds(c, r) ? _flag[Idx(c, r)] : 0;

    public int ElevAt(int c, int r) => InBounds(c, r) ? _elev[Idx(c, r)] : 0;

    public int OccupantAt(int c, int r) => InBounds(c, r) ? _occupant[Idx(c, r)] : -1;

    /// <summary>Terrain a move class may stand on, ignoring occupants. This IS
    /// the table Can_go implements; the hover line's flag test is its own
    /// (@0x4057b5 calls the tile-flag accessor @0x41d110 and lets it pass only
    /// when the byte is 0).</summary>
    public bool CanEnter(int c, int r, MoveClass mc)
    {
        if (!InBounds(c, r)) return false;
        return (Ground)_ground[Idx(c, r)] switch
        {
            Ground.Free => mc != MoveClass.Ship,
            Ground.Rough => mc == MoveClass.Walker ||
                            (mc == MoveClass.Hover && _flag[Idx(c, r)] == 0),
            Ground.Water => mc is MoveClass.Ship or MoveClass.Hover,
            _ => false,
        };
    }

    /// <summary>Kept under its old name because half the caller set asks it this
    /// way; it is <see cref="CanEnter"/>.</summary>
    public bool IsWalkable(int c, int r, MoveClass mc = MoveClass.Vehicle) => CanEnter(c, r, mc);

    /// <summary>
    /// Free to move into: the terrain allows it and nothing blocks the cell.
    ///
    /// A foot soldier does NOT block a cell. The original says so twice over,
    /// and in both directions: `pratelska_infa` @0x433fe0 lets a unit through an
    /// infantry cell when all nine of its men are friendly, and `prejet` — the
    /// game's own word for running over — @0x412980 lets it through when none of
    /// them is, killing them on the way (@0x412a50 then stamps the cell 0xFFFE).
    /// Either way the cell is passable, which is why infantry could always be
    /// driven over and why it must not stop a tank here.
    /// </summary>
    public bool IsFree(int c, int r, MoveClass mc = MoveClass.Vehicle, int mover = -1)
    {
        if (!CanEnter(c, r, mc)) return false;
        int i = Idx(c, r);
        if (_occupant[i] < 0 || _occupant[i] == mover) return true;
        return _crushable[i];
    }

    /// <summary>The foot soldier standing in the way, or -1. The caller decides
    /// what happens to them — driven through if friendly, run over if not.</summary>
    public int CrushableAt(int c, int r, int mover = -1)
    {
        if (!InBounds(c, r)) return -1;
        int i = Idx(c, r);
        return _occupant[i] >= 0 && _occupant[i] != mover && _crushable[i] ? _occupant[i] : -1;
    }

    /// <summary>
    /// Relative cost of crossing a cell. OURS — Can_go answers yes or no and
    /// says nothing about speed; rough ground costing more is our reading of
    /// what it looks like on screen.
    /// </summary>
    public float TerrainCost(int c, int r, MoveClass mc)
    {
        if (mc == MoveClass.Ship) return 1f;
        return GroundAt(c, r) == Ground.Rough ? 1.45f : 1f;
    }

    // ---- construction -------------------------------------------------------

    /// <summary>Sizes the grid and takes elevation and the tile flag byte off
    /// the baked map. The ground starts as a fallback read of the tile codes and
    /// is replaced by <see cref="ApplyTerrain"/> as soon as the map's own
    /// terrain block is there.</summary>
    public static NavGrid Build(GDict meta)
    {
        var g = new NavGrid
        {
            Width = GetI(meta, "width"),
            Height = GetI(meta, "height"),
        };
        if (g.Width <= 0 || g.Height <= 0) { g.Width = g.Height = 0; return g; }

        int n = g.Width * g.Height;
        g._ground = new byte[n];
        g._flag = new byte[n];
        g._elev = new byte[n];
        g._occupant = new int[n];
        g._crushable = new bool[n];
        Array.Fill(g._occupant, -1);

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
            g._flag[i] = (byte)Mathf.Clamp(GetI(t, "flag"), 0, 255);

            // fallback only, for content imported before the terrain block: the
            // tile code says water, an object cell is assumed to block. Both
            // are superseded the moment ApplyTerrain runs — and the second of
            // them is exactly the assumption that made bridges impassable.
            bool isObject = t.TryGetValue("object", out var ob) && ob.AsBool();
            g._ground[i] = isObject ? (byte)Ground.Blocked
                         : GetI(t, "code", 9999) <= WaterCodeMax ? (byte)Ground.Water
                         : (byte)Ground.Free;
        }
        return g;
    }

    /// <summary>Lay the map's own passability (entities.json `terrain`, run
    /// length encoded row major) over the fallback. This is the authority.</summary>
    public void ApplyTerrain(GDict terrain)
    {
        int w = GetI(terrain, "width"), h = GetI(terrain, "height");
        if (w <= 0 || h <= 0 ||
            !terrain.TryGetValue("rle", out var rv) || rv.VariantType != Variant.Type.Array)
            return;

        int at = 0, total = w * h;
        foreach (var pair in rv.AsGodotArray())
        {
            if (pair.VariantType != Variant.Type.Array) continue;
            var p = pair.AsGodotArray();
            if (p.Count < 2) continue;
            byte v = (byte)Mathf.Clamp(p[0].AsInt32(), 0, 3);
            int run = p[1].AsInt32();
            for (int k = 0; k < run && at < total; k++, at++)
            {
                int col = at % w, row = at / w;
                if (InBounds(col, row)) _ground[Idx(col, row)] = v;
            }
        }
        Inferred = GetI(terrain, "inferred");
        HasTerrain = at >= total;
    }

    private static int GetI(GDict d, string k, int def = 0)
        => d.TryGetValue(k, out var v) && v.VariantType != Variant.Type.Nil ? v.AsInt32() : def;

    // ---- dynamic occupancy --------------------------------------------------

    public void ClearOccupants()
    {
        if (_occupant.Length > 0) Array.Fill(_occupant, -1);
        if (_crushable.Length > 0) Array.Fill(_crushable, false);
    }

    /// <summary>Take one cell out of the blocked class. Used for the
    /// Nachschub-Posten: its tick handler @0x43e872 services the unit standing
    /// ON the post, so that cell must not block.</summary>
    public void ClearStatic(int c, int r)
    {
        if (InBounds(c, r) && (Ground)_ground[Idx(c, r)] == Ground.Blocked)
            _ground[Idx(c, r)] = (byte)Ground.Free;
    }

    /// <summary><paramref name="crushable"/> marks a foot soldier: they are
    /// driven through or run over, never blocked against (see
    /// <see cref="IsFree"/>).</summary>
    public void SetOccupant(int c, int r, int entity, bool crushable = false)
    {
        if (!InBounds(c, r)) return;
        int i = Idx(c, r);
        _occupant[i] = entity;
        _crushable[i] = crushable;
    }

    public void ClearOccupant(int c, int r, int entity)
    {
        if (InBounds(c, r) && _occupant[Idx(c, r)] == entity)
        {
            _occupant[Idx(c, r)] = -1;
            _crushable[Idx(c, r)] = false;
        }
    }

    /// <summary>Cell counts per ground class, for the HUD / debug overlay.</summary>
    public (int Free, int Rough, int Water, int Blocked) Census()
    {
        int f = 0, ro = 0, wa = 0, bl = 0;
        foreach (byte v in _ground)
            switch ((Ground)v)
            {
                case Ground.Free: f++; break;
                case Ground.Rough: ro++; break;
                case Ground.Water: wa++; break;
                default: bl++; break;
            }
        return (f, ro, wa, bl);
    }

    /// <summary>Coarse debug texture: the four ground classes tinted.</summary>
    public ImageTexture? BuildDebugTexture()
    {
        if (Width <= 0 || Height <= 0) return null;
        var img = Image.CreateEmpty(Width, Height, false, Image.Format.Rgba8);
        var byGround = new[]
        {
            new Color(0.25f, 0.9f, 0.4f, 0.20f),   // 0 free
            new Color(1f, 0.85f, 0.25f, 0.35f),    // 1 rough
            new Color(0.15f, 0.45f, 1f, 0.45f),    // 2 water
            new Color(1f, 0.25f, 0.2f, 0.40f),     // 3 blocked
        };
        for (int r = 0; r < Height; r++)
            for (int c = 0; c < Width; c++)
                img.SetPixel(c, r, byGround[_ground[Idx(c, r)] & 3]);
        return ImageTexture.CreateFromImage(img);
    }

    // ---- pathfinding --------------------------------------------------------

    private static readonly Vector2I[] Dirs =
    {
        new(1, 0), new(-1, 0), new(0, 1), new(0, -1),
        new(1, 1), new(1, -1), new(-1, 1), new(-1, -1),
    };

    /// <summary>Can <paramref name="mover"/> step from a to b (adjacent cells)?</summary>
    private bool CanStep(Vector2I a, Vector2I b, MoveClass mc, int mover)
    {
        if (!IsFree(b.X, b.Y, mc, mover)) return false;
        if (mc != MoveClass.Ship &&
            Math.Abs(ElevAt(b.X, b.Y) - ElevAt(a.X, a.Y)) >= MaxClimb) return false;
        // no cutting a corner between two blocked cells
        if (a.X != b.X && a.Y != b.Y &&
            !(CanEnter(b.X, a.Y, mc) && CanEnter(a.X, b.Y, mc))) return false;
        return true;
    }

    /// <summary>
    /// A* from <paramref name="start"/> to <paramref name="goal"/> (8-way).
    /// Returns the waypoint cells WITHOUT the start cell, or null if unreachable.
    /// If the goal itself is blocked/occupied, the nearest free cell is used.
    /// </summary>
    public List<Vector2I>? FindPath(Vector2I start, Vector2I goal, MoveClass mc = MoveClass.Vehicle,
                                    int mover = -1, int maxNodes = 60000)
    {
        if (!InBounds(start.X, start.Y) || !InBounds(goal.X, goal.Y)) return null;
        if (!IsFree(goal.X, goal.Y, mc, mover))
        {
            var alt = NearestFree(goal, mc, mover);
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
                if (closed.Contains(nb) || !CanStep(cur, nb, mc, mover)) continue;

                float step = (d.X != 0 && d.Y != 0) ? 1.4142f : 1f;
                step *= TerrainCost(nb.X, nb.Y, mc);                                // rough is slow
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
    public Vector2I? NearestFree(Vector2I around, MoveClass mc = MoveClass.Vehicle,
                                 int mover = -1, int maxRadius = 12)
    {
        if (IsFree(around.X, around.Y, mc, mover)) return around;
        for (int rad = 1; rad <= maxRadius; rad++)
            for (int dy = -rad; dy <= rad; dy++)
                for (int dx = -rad; dx <= rad; dx++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != rad) continue;
                    int c = around.X + dx, r = around.Y + dy;
                    if (IsFree(c, r, mc, mover)) return new Vector2I(c, r);
                }
        return null;
    }
}
