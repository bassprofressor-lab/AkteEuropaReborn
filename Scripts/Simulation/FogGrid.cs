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
///
/// <para><b>Does high ground see further? YES — and it is a bigger radius, not
/// a line of sight.</b> Answered 11.08.2026 by reading all eleven call sites of
/// the stamp. Ten of them hand it a fixed or table radius; exactly one — the
/// <b>ground-unit</b> block of the fog round, @0x4206fa..0x4207e1 — computes it
/// from the terrain:</para>
/// <code>
///   0x4207AF  call 0x401aaf -> 0x41d0e0   ; elev(col,row), see below
///   0x4207BC  mov  cl, byte [ebp+0x6e26f4] ; entity +0x2c = "Sicht"
///   0x4207C8  add  ax, cx
///   0x4207CF  dec  ax
///   0x4207D4  call 0x401258 -> 0x4200c0   ; stamp(col, row, ax)
///
///   radius = elevation(col, row) + sight - 1
/// </code>
/// <para>The <b>same shape in the second executable</b> (F:, 1.420.800 B), where
/// the whole block sits 0xdc1 lower: @0x41F96F <c>call 0x401aaa -> 0x41c2a0</c>,
/// @0x41F97C <c>mov cl, byte [ebp+0x6e1754]</c> (the same entity +0x2c off the
/// base 0x6e1728), @0x41F988 <c>add ax, cx</c>, @0x41F98F <c>dec ax</c>,
/// @0x41F994 the stamp. The byte pattern
/// <c>81 e1 ff 00 ff ff 66 03 c1 8b 4c 24 18 66 48 50 51 53</c> occurs once in
/// each file and only there, so this is the form, not an address.</para>
///
/// <para><b>0x41d0e0 really is the elevation</b> and not some other tile byte:
/// it returns <c>byte[dword[0x677e20] + (row*width + col)*4 + 2]</c>, and byte 2
/// of the four-byte tile record is what our own importer reads as the height
/// (<c>MapBaker.cs</c>, <c>elev[i] = rec[o + 2]</c>). It is the same reader the
/// damage formula uses (GAMESTATE_RE.md §3.94,
/// <c>defence = (30 + def/5) * (attack + 2*elevation) / 50</c> @0x40cdc4).
/// CAMPAIGN_RE.md still calls it <c>terrain_at</c>; that name is wrong.</para>
///
/// <para><b>Entity +0x2c really is the sight value:</b> the design record's
/// <c>+0x24 sight</c> ("Sicht ") is copied into it (aekernel-tools/cwm_extra.py
/// §sec19), and over all 23 levels / 1115 placed units it is near-constant per
/// unit_type — 276 units of type 148 all carry 3, 153 of type 161 all carry 4 —
/// which no facing byte would be. Our <c>Entity.Sight</c> is already that byte
/// (<c>MapEntityLayer.cs</c>, <c>HexByte(raw, 0x2c)</c>).</para>
///
/// <para><b>There is no line of sight.</b> The other plausible build for a 2.5D
/// engine — high cells seeing over low ones — is not what happens. The stamp
/// @0x4200c0..0x420303 reads no elevation, no slope and no terrain at all: it
/// walks the circle and sets every cell it touches. Height only ever grows the
/// radius.</para>
///
/// <para><b>And only for ground units.</b> Buildings take a literal
/// <c>push 0xa</c> (@0x4206AB), ships their record's own <c>+0x24 - 1</c>
/// (@0x420856), and the remaining call sites 0x42060E, 0x4208D1, 0x420934,
/// 0x420998, 0x4209FE, 0x420A4B, 0x420BC0, 0x420C46 fixed radii of 1, 2, 0, 0,
/// 10, a record byte, 0 and 3. Not one of them touches 0x41d0e0.</para>
///
/// <para>See <see cref="UnitRadius"/> and the four-value
/// <see cref="Update(IEnumerable{ValueTuple{int, int, int, int}})"/>.</para>
/// </summary>
public sealed class FogGrid
{
    public const byte Unseen = 0, Seen = 1, Watched = 2;

    /// <summary>
    /// <b>DIE ECKENMASKE — vier Bit je Zelle, welche ihrer Ecken »hell« sind.</b>
    ///
    /// <para>Bit 0 = (Spalte, Zeile), Bit 1 = (Spalte+1, Zeile),
    /// Bit 2 = (Spalte, Zeile+1), Bit 3 = (Spalte+1, Zeile+1).</para>
    ///
    /// <para><b>Warum es sie gibt.</b> Das Original zeichnet den Nebelrand nicht
    /// an der Zellkante, sondern <b>durch die Zelle hindurch</b>. Dafür führt es
    /// ein eigenes <b>Eckengitter</b> mit 257×257 Bytes (<c>0x5739D8</c>, die
    /// Löschgrösse <c>0x4080·4+1 = 66049 = 257²</c> @0x41FD65 ist der Beleg,
    /// dass es Ecken und nicht Zellen sind). Jede beobachtete Kachel markiert
    /// ihre <b>vier</b> Ecken (@0x41FDB7 … @0x41FDC9, Versätze 0, 1, 257, 258).
    /// </para>
    ///
    /// <para>Danach setzt ein zweiter Ganzkartendurchgang (@0x41FF50) jede
    /// NICHT beobachtete Zelle, an der eine markierte Ecke hängt, auf einen
    /// eigenen dritten Zustand (<c>byte[…] = 2</c> @0x420013) — den <b>Saum</b>.
    /// Aus den vier Ecken bildet <c>0x41FB90</c> dann eine vierstellige
    /// Ziffernfolge (<c>1000·a + 100·c + 10·b + d − 1111·min</c>, nachgerechnet
    /// aus 0x41FBFD…0x41FC2D) und schlägt sie in einer <b>16-Einträge-Tafel</b>
    /// nach (<c>0x4F89F8</c>: 0000, 0101, 0011, 1010, 1100, 0001, 0010, 1000,
    /// 0100, 0111, 1011, 1110, 1101, 0110, 1001, 1111 — vollzählig, kein
    /// Füllwert). Das ist <b>Marching Squares</b>, und es ist der Grund, warum
    /// die Kante im Original weich aussieht und bei uns nach Kacheln.</para>
    ///
    /// <para>Wir führen dieselbe Maske; was daraus gezeichnet wird, steht bei
    /// <c>MapEntityLayer.BuildFogTexture</c>.</para>
    /// </summary>
    public byte CornerAt(int col, int row)
        => _corner != null && col >= 0 && row >= 0 && col < Width && row < Height
            ? _corner[row * Width + col] : (byte)0;

    private byte[]? _corner;

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
        MarkCorners();
        Version++;
    }

    /// <summary>Bumped whenever the grid changes, so the drawing side knows when
    /// to rebuild its texture instead of doing it every frame.</summary>
    public int Version { get; private set; }

    // ---- raw access, for saving and loading a game --------------------------
    //
    // The fog is part of a save: reloading a mission with the map fully lit
    // again would give away every enemy the player had not found yet.

    public int CellCount => _cells.Length;
    public int CellAt(int i) => i >= 0 && i < _cells.Length ? _cells[i] : Unseen;

    public void SetCellAt(int i, int v)
    {
        if (i >= 0 && i < _cells.Length) _cells[i] = (byte)Mathf.Clamp(v, 0, Watched);
    }

    /// <summary>Bumped by a load, so the overlay redraws itself.</summary>
    public void MarkChanged() => Version++;

    /// <summary>One pass, the way @0x4205b0 does it: drop every "watched" back
    /// to "seen" and stamp it again from the watchers. What was once seen stays
    /// seen.</summary>
    public void Update(IEnumerable<(int Col, int Row, int Sight)> watchers)
    {
        for (int i = 0; i < _cells.Length; i++)
            if (_cells[i] == Watched) _cells[i] = Seen;

        foreach (var (col, row, sight) in watchers) Stamp(col, row, sight);
        MarkCorners();
        Version++;
    }

    /// <summary>
    /// Der zweite und dritte Ganzkartendurchgang des Originals, zusammengezogen:
    /// erst die Ecken jeder beobachteten Kachel markieren (@0x41FD60), dann
    /// jeder Zelle ihre vier Ecken als Maske geben (@0x41FF50). Siehe
    /// <see cref="CornerAt"/>.
    ///
    /// <para>⚠ Das Original braucht dafür ZWEI volle Durchgänge über die Karte
    /// und ein eigenes 257×257-Feld. Wir kommen mit einem Feld und zwei
    /// Schleifen aus, weil wir die Maske ohnehin je Zelle brauchen und nicht je
    /// Ecke — das Ergebnis ist dasselbe, der Weg ist kürzer.</para>
    /// </summary>
    private void MarkCorners()
    {
        int w = Width, h = Height;
        _ecken ??= new byte[(w + 1) * (h + 1)];
        _corner ??= new byte[w * h];
        System.Array.Clear(_ecken, 0, _ecken.Length);
        for (int r = 0, i = 0; r < h; r++)
            for (int c = 0; c < w; c++, i++)
            {
                if (_cells[i] != Watched) continue;
                int k = r * (w + 1) + c;
                _ecken[k] = 1;
                _ecken[k + 1] = 1;
                _ecken[k + w + 1] = 1;
                _ecken[k + w + 2] = 1;
            }
        for (int r = 0, i = 0; r < h; r++)
            for (int c = 0; c < w; c++, i++)
            {
                int k = r * (w + 1) + c;
                _corner[i] = (byte)(_ecken[k]
                                    | (_ecken[k + 1] << 1)
                                    | (_ecken[k + w + 1] << 2)
                                    | (_ecken[k + w + 2] << 3));
            }
    }

    private byte[]? _ecken;

    /// <summary>The ground-unit call site's own arithmetic, @0x4207C8/@0x4207CF
    /// (F: @0x41F988/@0x41F98F): <c>elevation + sight - 1</c>. A unit two levels
    /// up therefore opens two more rings than the same unit in the valley, and
    /// on flat ground it opens one LESS than its bare sight value — the
    /// <c>dec ax</c> is not optional, the game has always subtracted it.
    ///
    /// <para>Buildings do not go through here: they are a literal 10
    /// (@0x4206AB), height and all.</para></summary>
    public static int UnitRadius(int sight, int elev) => sight + elev - 1;

    /// <summary>The same round as
    /// <see cref="Update(IEnumerable{ValueTuple{int, int, int}})"/>, but for
    /// watchers that know the height of the cell they stand on — which is what
    /// the original's ground units are. Each one is stamped with
    /// <see cref="UnitRadius"/>.
    ///
    /// <para>⚠ Nothing calls this yet. <c>MapEntityLayer.Watchers()</c> yields
    /// three values and its <c>ElevOf(col, row)</c> is private, so the height
    /// cannot reach the fog without a change in a file this pass does not own.
    /// The overload is here so that change is a one-line one, and so the reading
    /// above does not have to be found again. Until then the fog keeps using the
    /// bare sight value: one ring too many at sea level, and no reward for the
    /// hill.</para></summary>
    public void Update(IEnumerable<(int Col, int Row, int Sight, int Elev)> watchers)
    {
        for (int i = 0; i < _cells.Length; i++)
            if (_cells[i] == Watched) _cells[i] = Seen;

        foreach (var (col, row, sight, elev) in watchers)
            Stamp(col, row, UnitRadius(sight, elev));
        MarkCorners();
        Version++;
    }

    /// <summary>@0x4200c0: clamp the radius, then open each row by the span the
    /// table gives. `d` counts in from the rim, which is how the table is
    /// indexed — the centre row is the widest.</summary>
    private void Stamp(int col, int row, int sight)
    {
        int max = _radii > 0 ? _radii - 1 : MaxRadius;
        // The original clamps on the HIGH side only (@0x4200c8, `cmp si,0x13`).
        // A negative radius is not clamped to zero there — it falls out at
        // @0x420138, where the first row bound is already past the last, and
        // stamps nothing at all. Reachable now that UnitRadius subtracts one:
        // a sight of 0 at elevation 0 gives -1, and that unit sees nothing,
        // not one cell.
        if (sight < 0) return;
        int r = Mathf.Min(sight, max);

        for (int dy = -r; dy <= r; dy++)
        {
            int y = row + dy;
            if (y < 0 || y >= Height) continue;
            int half = Span(r, r - Mathf.Abs(dy));
            if (half <= 0) continue;
            // ⚠⚠ 19.08.2026 — JEDE ZEILE WAR ZWEI ZELLEN ZU SCHMAL. Hier stand
            // `col - half + 1 .. col + half - 1`, also die Breite `2t-1`. Das
            // Original nimmt `col - t .. col + t` EINSCHLIESSLICH, also `2t+1`:
            //
            //     0x4201A0  mov ax, word[t*2 + 0x4F8A48]   ; der Tafelwert
            //     0x4201A8  mov cx, di / sub cx, ax        ; links  = Spalte - t
            //     0x4201B4  add ax, di                     ; rechts = Spalte + t
            //
            // ⚠ AUSSER IN DER MITTELZEILE. Dort rechnet das Original mit dem
            // RADIUS statt mit dem Tafelwert (0x4201B9: `mov cx,di / lea
            // eax,[edi+esi] / sub cx,si`), und weil `t[r][r] == r+1` ist, kommt
            // dort `2r+1 = 2t-1` heraus — genau unsere alte Formel. Unsere
            // Mittelzeile stimmte also zufaellig, und das hat den Fehler in
            // allen anderen Zeilen verdeckt.
            //
            // GEMESSEN an der Tafel (beide GAME.EXE byteweise gleich):
            //     r= 6   Original 145   vorher 121   (-24)
            //     r=10   Original 373   vorher 333   (-40)
            //     r=19   Original 1229  vorher 1153  (-76)
            // Jede Einheit sah also spuerbar weniger, als sie soll.
            int arm = dy == 0 ? half - 1 : half;
            int x0 = Mathf.Max(0, col - arm), x1 = Mathf.Min(Width - 1, col + arm);
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
