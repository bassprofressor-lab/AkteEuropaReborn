namespace AkteEuropaReborn.Simulation;

using System.Collections.Generic;
using Godot;

/// <summary>
/// The fog of war, built the way the original builds it.
///
/// <para><b>How the original does it</b>, found by following the main loop's own
/// trace labels — one of them is literally <c>"unexplored"</c> (@0x4f7a54):</para>
/// <list type="bullet">
/// <item>the step it names runs <b>@0x4205b0 on every fifth tick</b>
///   (<c>[0x4fa240] % 5 == 1</c>), not every frame;</item>
/// <item>it <b>clears the whole visibility grid first</b> — a 65535-byte block at
///   0x678b58, so 256 x 256 — and rebuilds it from scratch. Visibility is
///   therefore never remembered, only ever recomputed;</item>
/// <item>what IS remembered is separate: the fog-off branch @0x420646 fills a
///   second, smaller array with 1s, which is the "has been seen" state;</item>
/// <item>a switch at <c>byte[0x4f8a3c]</c> turns the whole thing off;</item>
/// <item>the stamp @0x4200c0 <b>clamps the radius to 0x13 = 19</b> and takes its
///   row spans from a 20 x 20 table of u16 — see
///   <see cref="Import.ExeTables.SightCircleTable"/>. That table is exported
///   from the player's own executable, so the shape here is the game's own,
///   rounding included.</item>
/// </list>
///
/// <para><b>Ours:</b> three states rather than the original's two arrays —
/// unseen, seen-but-not-watched, watched — because a remake that only knows
/// "seen" cannot dim what a unit has walked away from. And the cadence is kept
/// (every fifth tick) because recomputing a 250 x 250 grid every frame would be
/// waste, not fidelity.</para>
/// </summary>
public sealed class FogGrid
{
    public const byte Unseen = 0, Seen = 1, Watched = 2;

    /// <summary>The original's own clamp; overridden by the exported table.</summary>
    public const int MaxRadius = 19;

    public readonly int Width, Height;
    private readonly byte[] _cells;

    /// <summary>`Span[r * Radii + d]` — the half-width to open on the row that
    /// is d steps in from the rim of a circle of radius r. Straight out of the
    /// executable; null until <see cref="Load"/> finds it.</summary>
    private static int[]? _span;
    private static int _radii;

    public FogGrid(int w, int h)
    {
        Width = w; Height = h;
        _cells = new byte[w * h];
    }

    public byte At(int col, int row)
        => col < 0 || row < 0 || col >= Width || row >= Height ? Unseen : _cells[row * Width + col];

    public bool IsWatched(int col, int row) => At(col, row) == Watched;
    public bool IsSeen(int col, int row) => At(col, row) != Unseen;

    /// <summary>Everything visible — for a map with no fog, and for the
    /// "Nebel aus" setting.</summary>
    public void RevealAll()
    {
        for (int i = 0; i < _cells.Length; i++) _cells[i] = Watched;
        Version++;
    }

    /// <summary>Bumped whenever the grid changes, so the drawing side knows when
    /// to rebuild its texture instead of doing it every frame.</summary>
    public int Version { get; private set; }

    /// <summary>One pass, the way @0x4205b0 does it: drop every "watched" back
    /// to "seen" and stamp it again from the watchers. What was once seen stays
    /// seen.</summary>
    public void Update(IEnumerable<(int Col, int Row, int Sight)> watchers)
    {
        for (int i = 0; i < _cells.Length; i++)
            if (_cells[i] == Watched) _cells[i] = Seen;

        foreach (var (col, row, sight) in watchers) Stamp(col, row, sight);
        Version++;
    }

    /// <summary>@0x4200c0: clamp the radius, then open each row by the span the
    /// table gives. `d` counts in from the rim, which is how the table is
    /// indexed — the centre row is the widest.</summary>
    private void Stamp(int col, int row, int sight)
    {
        int max = _radii > 0 ? _radii - 1 : MaxRadius;
        int r = Mathf.Clamp(sight, 0, max);

        for (int dy = -r; dy <= r; dy++)
        {
            int y = row + dy;
            if (y < 0 || y >= Height) continue;
            int half = Span(r, r - Mathf.Abs(dy));
            if (half <= 0) continue;
            int x0 = Mathf.Max(0, col - half + 1), x1 = Mathf.Min(Width - 1, col + half - 1);
            int at = y * Width;
            for (int x = x0; x <= x1; x++) _cells[at + x] = Watched;
        }
    }

    private static int Span(int r, int d)
    {
        if (_span == null || _radii <= 0) return d + 1;      // no table: a diamond
        if (r < 0 || r >= _radii || d < 0 || d >= _radii) return 0;
        return _span[r * _radii + d];
    }

    /// <summary>Reads the exported circle table. Without it the stamp still
    /// works — it opens a diamond — but the shape is then ours, not the
    /// game's, and that is said out loud in the log.</summary>
    public static void Load()
    {
        if (_span != null) return;
        string path = Core.Content.Path("Maps/sight_circle.json");
        if (!FileAccess.FileExists(path))
        {
            GD.Print("Nebel: sight_circle.json fehlt — Sichtkreis ist eine Raute, nicht der des Spiels");
            return;
        }
        try
        {
            using var f = FileAccess.Open(path, FileAccess.ModeFlags.Read);
            using var doc = System.Text.Json.JsonDocument.Parse(f.GetAsText());
            _radii = doc.RootElement.GetProperty("radii").GetInt32();
            var list = new List<int>();
            foreach (var e in doc.RootElement.GetProperty("span").EnumerateArray())
                list.Add(e.GetInt32());
            if (list.Count == _radii * _radii) _span = list.ToArray();
        }
        catch (System.Exception e) { GD.PrintErr("Nebel: sight_circle.json — " + e.Message); }
    }

    /// <summary>How many cells are in each state — for the scripted checks.</summary>
    public (int Unseen, int Seen, int Watched) Counts()
    {
        int u = 0, s = 0, w = 0;
        foreach (byte b in _cells)
        {
            if (b == Unseen) u++;
            else if (b == Seen) s++;
            else w++;
        }
        return (u, s, w);
    }
}
