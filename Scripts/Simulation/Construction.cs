using System.Collections.Generic;
using Godot;
using AkteEuropaReborn.Import;
using AkteEuropaReborn.Simulation;

namespace AkteEuropaReborn.Rendering;

/// <summary>
/// ERRICHTEN — putting a new building on the map.
///
/// <para><b>The original can build exactly three things</b>, and that is not a
/// simplification of ours: <c>add_building</c> @0x4C8D60 has three call sites in
/// the whole game (@0x408057, @0x40815F, @0x4082BD), and they place
/// <b>Depot (5)</b>, <b>Feld-Rohstoffmine (15)</b> and <b>Generator (7)</b>.
/// Nobody — not the player, not the AI — can raise a factory or a base; those
/// stand on the map or not at all. The mission texts say the same in the game's
/// own words: <c>OBJECTG.TXT</c> #013 "Bauen Sie Stromgeneratoren", #017 "Bauen
/// Sie Rohstoffminen", #023 "Bauen Sie fünf Rohstoffminen".</para>
///
/// <para><b>Who builds what comes from the vehicle's special part</b>
/// (<c>byte[ent+0x0e]</c>, top_special), through the jump table @0x40a094 with
/// its index bytes @0x40a0b0 — one to one, no overlap:</para>
///
/// <list type="bullet">
/// <item><b>72 Gebäude-Techniker</b> → Depot or Feld-Rohstoffmine, and
/// <c>byte[ent+0x38]</c> picks: <b>5 = Depot, 6 = Mine</b>, anything else falls
/// through.</item>
/// <item><b>73 Boden-Techniker</b> → ramps and bridges, no building.</item>
/// <item><b>74 Generatorenbauer</b> → the Generator, and it does not read
/// +0x38 at all: it has only the one. The part is named after what it
/// builds.</item>
/// </list>
///
/// <para><b>The build happens while STANDING.</b> The outer dispatch @0x407E4A
/// switches on the UKOL <c>byte[ent+0x14]</c>, and the whole build path hangs
/// off branch 0 — UKOL 0, "stays". A builder does not carry a build order as
/// its task; it drives to the spot, comes to rest, and then places. That is why
/// the game's own trace labels there read "stay A".."stay F".</para>
///
/// <para><b>The building lands offset by one</b> — every call site passes
/// <c>x−1, y−1</c>, so the unit stands in the building's corner, not on its
/// origin.</para>
///
/// <para><b>OURS, and it has to be said:</b> the fourth test below wants the
/// four corner heights of a cell. The original keeps them in a grid of its own
/// at 0xa39f10 with a 257-step, i.e. one entry per grid POINT for a 256-cell
/// map; we only keep one height per CELL. We read the four corners as the
/// heights of (x,y), (x,y+1), (x+1,y) and (x+1,y+1), which is what a 257-wide
/// point grid amounts to if the cell's height sits on its upper-left corner.
/// That is a reading, not a measurement — everything else in this file is the
/// original's.</para>
/// </summary>
public partial class MapEntityLayer
{
    /// <summary>The three buildable types, by the game's numbering.</summary>
    public const int TypeDepot = 5, TypeGenerator = 7, TypeFieldMine = 15;

    /// <summary>The special part that may build each of them.</summary>
    public const int PartBuildingTech = 72, PartGroundTech = 73, PartGeneratorTech = 74;

    /// <summary>The order byte <c>[ent+0x38]</c> for the Gebäude-Techniker.</summary>
    public const int OrderDepot = 5, OrderFieldMine = 6;

    /// <summary>The minimum corner height a build site needs — the original
    /// tests all four against 2 (@0x420360, four <c>cmp … 2 / jae</c>).</summary>
    public const int MinCornerHeight = 2;

    /// <summary>One checked cell, as the original collects them.</summary>
    public readonly struct SiteCell
    {
        public readonly int Col, Row;
        public readonly bool Ok;
        public SiteCell(int col, int row, bool ok) { Col = col; Row = row; Ok = ok; }
    }

    /// <summary>Which building a special part may place, or 0 for none.
    /// <paramref name="order"/> is <c>byte[ent+0x38]</c> and only matters for
    /// the Gebäude-Techniker.</summary>
    public static int BuildableBy(int topSpecial, int order) => topSpecial switch
    {
        PartBuildingTech => order == OrderDepot ? TypeDepot
                          : order == OrderFieldMine ? TypeFieldMine : 0,
        PartGeneratorTech => TypeGenerator,
        _ => 0,
    };

    /// <summary>
    /// <c>can_build_here</c> @0x4203C0 — may a building of this type stand with
    /// its origin at (col,row)?
    ///
    /// <para>It walks the pattern's ten columns and six rows and tests every
    /// cell that carries a TILE (not the mask — the two rasters differ, and the
    /// original checks the drawn footprint, overhang included). Four tests, in
    /// the original's order:</para>
    ///
    /// <list type="number">
    /// <item>the cell is on the map (@0x41C390);</item>
    /// <item>the imap holds 0xFFFE — free — or the builder itself;</item>
    /// <item>the terrain cell's flag byte is 0 (@0x41C2D0 reads byte 3 of the
    /// 4-byte cell);</item>
    /// <item>all four corner heights are ≥ 2 (@0x420360).</item>
    /// </list>
    ///
    /// <para>A single failure makes the answer false, but the loop still runs
    /// to the end so <paramref name="collect"/> comes back complete — that is
    /// the original's behaviour, and it is what a green/red site overlay needs.
    /// </para>
    /// </summary>
    /// <summary>
    /// ⚠ <b>The Feld-Rohstoffmine does NOT use this test in the original, and
    /// ours is wrong for it.</b> Both places that ask about a mine — the task
    /// dispatch @0x408111 and the cursor preview @0x4309D1 — call a different
    /// routine, @0x4205C0, and it does not look at the terrain at all: it walks
    /// the <b>50 deposit places</b> at 0x677448 (stride 14, +0 valid, +1 col,
    /// +2 row) and asks whether the cell lies in the <b>3x3 window</b>
    /// <c>fx ≤ x &lt; fx+3</c>, <c>fy ≤ y &lt; fy+3</c> of one of them. A mine
    /// goes on a deposit, not on flat ground.
    ///
    /// <para>We have no deposit list yet — it is the "terra_places" behind
    /// "Cannot add more terra_places" @0x4D05E7 and is not exported. Until it
    /// is, <see cref="CanBuild"/> answers for type 15 with the ground test,
    /// which is TOO PERMISSIVE: it offers sites the original would refuse.
    /// See the handoff.</para>
    /// </summary>
    public const int TypeFieldMineUsesDepositTest = TypeFieldMine;

    /// <summary>The deposits a Feld-Rohstoffmine may stand on. Empty until the
    /// mission scripts are read — see <see cref="HasDeposits"/>.</summary>
    private readonly List<(int Col, int Row, int Amount)> _deposits = new();

    /// <summary>Whether we know where the deposits are at all.</summary>
    public bool HasDeposits => _deposits.Count > 0;

    /// <summary>@0x4205C0 — the cell must lie in the 3x3 window of a deposit.
    /// The deposit list itself is filled by the MISSION SCRIPT, not by the map:
    /// <c>add_terra_place(col, row, amount)</c> has its call sites in the script
    /// region 0x487000..0x492000, every one with constant arguments —
    /// (4, 55, 5000), (11, 38, 1000), (20, …, 20000) and so on. So this cannot
    /// be right before the scripts are read.
    ///
    /// <para>⚠ Korrektur 13.08.2026, zur Adresse: <b>@0x4D05C0 ist die
    /// F:-Fassung</b> (der Entwicklungsbau); in der untersuchten Fassung auf
    /// <c>C:\Program Files (x86)</c> liegt dieselbe Funktion an
    /// <b>@0x4D0A10</b>. Hier stand nur die eine Zahl ohne die Fassung dazu —
    /// und wer nach ihr in der falschen EXE sucht, findet nichts. Nach der FORM
    /// suchen, nicht nach der Adresse.</para>
    ///
    /// <para>⚠ Und ein offener Widerspruch, ausdrücklich nicht glattgebügelt:
    /// hier stand <b>56</b> Aufrufstellen, eine Messung vom 13.08. über das
    /// Profil (drei Konstanten je Aufruf) kam auf <b>50</b> Vorkommen in 8
    /// Missionen, in beiden Fassungen gleich. Welche Zählung mitzählt, was die
    /// andere weglässt, ist ungeklärt — beide Zahlen bleiben stehen, bis es
    /// jemand nachzählt.</para></summary>
    private bool CellOnDeposit(int col, int row)
    {
        foreach (var d in _deposits)
            if (col >= d.Col && col < d.Col + 3 && row >= d.Row && row < d.Row + 3)
                return true;
        return false;
    }

    public bool CanBuild(IBuildingPatterns cwp, int typ, int col, int row, int builder = -1,
                         List<SiteCell>? collect = null)
    {
        collect?.Clear();
        if (_nav == null || cwp == null || !cwp.HasBuildings) return false;

        // The mine asks a different question, and we cannot answer it yet.
        // Saying "yes" from the ground test offered 1247 sites on map_25 where
        // the original would allow a handful — so we say NO until the deposits
        // are in. A missing feature beats a wrong one.
        if (typ == TypeFieldMine)
        {
            if (!HasDeposits) return false;
            if (!CellOnDeposit(col, row)) return false;
        }

        var bt = cwp.GetBuildingType(typ);
        // OURS: the original does not look at the pattern count here — it takes
        // `word[0xbb3202 + typ*10]` and walks, so a type with no pattern would
        // run the loop over an empty raster, test nothing, and come back TRUE.
        // We refuse instead. It matters: not every tileset carries every type
        // (14.CWP has count 0 for the Feld-Rohstoffmine), and "yes" there would
        // mean placing a building with no footprint at all.
        if (bt.IsEmpty) return false;

        bool ok = true;
        for (int dy = 0; dy < CwpFile.PatternHeight; dy++)
        {
            for (int dx = 0; dx < CwpFile.PatternWidth; dx++)
            {
                if (cwp.PatternTile(bt.FirstPattern, dx, dy) == 0) continue;

                int c = col + dx, r = row + dy;
                bool cellOk = CellTakesBuilding(c, r, builder);
                if (!cellOk) ok = false;
                collect?.Add(new SiteCell(c, r, cellOk));
            }
        }
        return ok;
    }

    /// <summary>The four per-cell tests, in the original's order.</summary>
    private bool CellTakesBuilding(int c, int r, int builder)
    {
        if (_nav == null) return false;

        if (!_nav.InBounds(c, r)) return false;

        // the imap must read "free", or hold the builder itself
        if (_nav.GroundAt(c, r) != NavGrid.Ground.Free) return false;
        int occ = _nav.OccupantAt(c, r);
        if (occ != -1 && occ != builder) return false;

        if (_nav.FlagAt(c, r) != 0) return false;

        return CornersCarry(c, r);
    }

    /// <summary>All four corners of the cell at or above <see
    /// cref="MinCornerHeight"/>. See the class remark — the corner grid is
    /// OURS, read off the per-cell heights.</summary>
    private bool CornersCarry(int c, int r)
    {
        if (_nav == null) return false;
        return _nav.ElevAt(c, r) >= MinCornerHeight
            && _nav.ElevAt(c, r + 1) >= MinCornerHeight
            && _nav.ElevAt(c + 1, r) >= MinCornerHeight
            && _nav.ElevAt(c + 1, r + 1) >= MinCornerHeight;
    }

    /// <summary>The first free building slot, or −1 when all are taken.
    ///
    /// <para><c>find_free_building_slot</c> @0x4D5290 walks the 255 records at
    /// 0xc05970 (stride 76) and takes the first whose <b>type byte at +0x04 is
    /// zero</b> — the type doubles as the occupied flag. It returns 0xfe when
    /// the table is full, and the caller places nothing.</para>
    /// </summary>
    public int FreeBuildingSlot()
    {
        var taken = new HashSet<int>();
        foreach (var e in _entities)
            if (e.IsBuilding && e.Slot >= 0) taken.Add(e.Slot);
        for (int s = 0; s < MaxBuildings; s++)
            if (!taken.Contains(s)) return s;
        return -1;
    }

    /// <summary>255 records, and the original stops at 0xfe with
    /// "Too many buildings..." @0x5392DC.</summary>
    public const int MaxBuildings = 255;

    /// <summary>
    /// <c>add_building</c> @0x4C8D60 — put a new building at (col,row).
    ///
    /// <para>The caller has already checked with <see cref="CanBuild"/>; every
    /// one of the original's three call sites does exactly that, and passes
    /// <c>x−1, y−1</c>, so a builder standing at (x,y) raises the building one
    /// cell up and left of itself.</para>
    ///
    /// <para>What the original writes, and this writes with it: position and
    /// type, the owner (0xff for types 13 and 14, which belong to nobody), hit
    /// points from the stat table into both +0x06 and +0x16, <b>condition
    /// 100</b> at +0x0a, the door count and offsets, and the name — literally
    /// <c>"Built"</c> with the slot number after it (@0x538674).</para>
    ///
    /// <para><b>The imap is stamped from the MASK, not from the tiles.</b> That
    /// is the whole point of the two rasters: a building draws about four times
    /// as many cells as it blocks, so roof and facade hang over ground that
    /// stays walkable. Cells the mask leaves out are set FREE (0xFFFE), which
    /// is what the original does at @0x4C8E3C.</para>
    ///
    /// <para>The door is stamped last, as <b>0xFFFF</b> (@0x4C90A2).</para>
    ///
    /// <para>✔ RESOLVED 07.08.2026 — the picture. This used to say "a raised
    /// building has no sprite yet, it is simply not drawn", because object tiles
    /// were only decoded while baking. Since the atlas carries every type's
    /// standing and ruined pattern and the baker no longer burns buildings into
    /// the map, a raised building is drawn like any other — see
    /// <see cref="Rendering.MapEntityLayer.DrawBuildingBody"/>. The
    /// <paramref name="into"/> image stays for `--build-check`, which wants a
    /// still picture it can save.</para>
    /// </summary>
    public Entity? PlaceBuilding(IBuildingPatterns patterns, int typ, int col, int row,
                                 int owner, Image? into = null)
    {
        if (_nav == null || patterns == null) return null;
        var bt = patterns.GetBuildingType(typ);
        if (bt.IsEmpty) return null;

        int slot = FreeBuildingSlot();
        if (slot < 0) return null;                       // "Too many buildings..."

        // types 13 and 14 belong to nobody (@0x4C8F3D writes 0xff)
        int realOwner = typ is 13 or 14 ? -1 : owner;
        int hp = BuildingHp(typ);

        var bld = new Entity
        {
            Slot = slot, Col = col, Row = row,
            Owner = realOwner, Team = realOwner, ShownOwner = realOwner,
            UnitType = -1, Category = -1, Chassis = -1,
            Hp = hp, HpMax = hp,
            Condition = FreshCondition,
            IsBuilding = true, BType = typ, Built = 1,
            Elev = _nav.ElevAt(col, row),
            Name = $"Built{slot}",
            Footprint = CellRect(_ox, _oy, col, row, _nav.ElevAt(col, row)),
        };

        var doors = BuildingDoors(typ);
        bld.Doors = doors.Count;
        _entities.Add(bld);
        int index = _entities.Count - 1;

        // the imap, from the mask
        for (int dx = 0; dx < CwpFile.PatternWidth; dx++)
            for (int dy = 0; dy < CwpFile.PatternHeight; dy++)
            {
                int c = col + dx, r = row + dy;
                if (!_nav.InBounds(c, r)) continue;
                if (patterns.PatternBlocks(bt.FirstPattern, dx, dy))
                    _nav.SetOccupant(c, r, index);
                else if (patterns.PatternTile(bt.FirstPattern, dx, dy) != 0)
                    _nav.ClearOccupant(c, r, index);     // drawn, but walkable
            }

        // The doors last, as the original does (@0x4C90A2) — and they BLOCK.
        //
        // ⚠ It writes 0xFFFF there, not 0xFFFE. 0xFFFF is a static object
        // handle and stops everyone (NavGrid's legend, @0x405586); it is not
        // "free". A door is the cell you drive UP TO, not onto — which is
        // exactly how Capture.cs takes a building. Getting this backwards left
        // one cell walkable and showed up as "imap 0 -> 18" where the mask had
        // blocked 19.
        foreach (var d in doors)
        {
            int c = col + d.X, r = row + d.Y;
            if (_nav.InBounds(c, r)) _nav.SetOccupant(c, r, index);
        }

        if (into != null) StampBuilding(into, patterns, bt.FirstPattern, col, row);
        return bld;
    }

    /// <summary>
    /// The building's tiles into the baked map picture — the same thing the
    /// original does when it writes <c>tile + 0x2710</c> into the map
    /// (@0x4C8E2D), only that our map is already a picture.
    ///
    /// <para>The placement is <see cref="Import.MapBaker"/>'s, cell for cell:
    /// <c>sx = col*TileW</c>, <c>sy = originY + row*TileH − elev*ElevStep − 50
    /// + yoff</c>. If it disagreed with the baker, a raised building would sit
    /// at a different height than the ones the map came with.</para>
    /// </summary>
    private void StampBuilding(Image img, IBuildingPatterns patterns, int pattern,
                               int col, int row)
    {
        if (_nav == null || patterns is not Import.BuildingPatterns bp
            || bp.AtlasImage == null) return;

        for (int dx = 0; dx < CwpFile.PatternWidth; dx++)
            for (int dy = 0; dy < CwpFile.PatternHeight; dy++)
            {
                int code = patterns.PatternTile(pattern, dx, dy);
                if (code == 0 || !bp.TryGetTile(code, out var t)) continue;
                int c = col + dx, r = row + dy;
                if (!_nav.InBounds(c, r)) continue;

                int sx = c * Import.MapBaker.TileW;
                int sy = _originY + r * Import.MapBaker.TileH
                         - _nav.ElevAt(c, r) * Import.MapBaker.ElevStep
                         + Import.MapBaker.BlitAnchor + t.YOff;
                for (int y = 0; y < t.H; y++)
                {
                    int py = sy + y;
                    if (py < 0 || py >= img.GetHeight()) continue;
                    for (int x = 0; x < t.W; x++)
                    {
                        int px = sx + x;
                        if (px < 0 || px >= img.GetWidth()) continue;
                        var col4 = bp.AtlasImage.GetPixel(t.X + x, t.Y + y);
                        if (col4.A <= 0f) continue;
                        img.SetPixel(px, py, col4);
                    }
                }
            }
    }



    /// <summary>What a freshly raised building starts at — <c>byte[+0x0a] =
    /// 0x64</c> @0x4C8F4C.</summary>
    public const int FreshCondition = 100;

    // ---- the site preview --------------------------------------------------
    //
    // The original HAS one, and this is where it comes from: @0x430967 calls
    // can_build_here with the collect flag SET —
    //     push 1          ; collect
    //     push 5          ; typ 5, the Depot
    //     push [0x537814] ; row  (the cursor cell)
    //     push [0x537810] ; col
    // — and the routine then writes every checked cell as three bytes (col,
    // row, ok) to 0xa311e8 with the count in word[0x501b10]. So the list, its
    // contents and the fact that it is built per cursor cell are the game's.
    //
    // ⚠ OURS is only how it LOOKS. The drawing side could not be read with
    // confidence: the routine @0x4B3F0D that reads the same buffer picks
    // between tile 0x90 and 0x9c by the ok byte, but both decode to plain grass
    // in 25.CWP, so that is either not the preview or the indices mean
    // something else there. Rather than invent a reading, the colours below are
    // declared ours. The SHAPE — one marker per checked cell, two states — is
    // the original's.

    private int _previewType = -1;
    private int _previewCol = -1, _previewRow = -1;
    private readonly List<SiteCell> _previewCells = new();
    private bool _previewOk;

    /// <summary>Show (or move) the build-site preview. Type 0 clears it.</summary>
    public void SetBuildPreview(int typ, int col, int row)
    {
        if (typ <= 0 || Patterns == null) { ClearBuildPreview(); return; }
        if (typ == _previewType && col == _previewCol && row == _previewRow) return;
        _previewType = typ; _previewCol = col; _previewRow = row;
        _previewOk = CanBuild(Patterns, typ, col, row, -1, _previewCells);
        QueueRedraw();
    }

    public void ClearBuildPreview()
    {
        if (_previewType < 0) return;
        _previewType = -1; _previewCol = _previewRow = -1;
        _previewCells.Clear();
        QueueRedraw();
    }

    /// <summary>Whether the current preview would go through.</summary>
    public bool BuildPreviewOk => _previewType > 0 && _previewOk;

    /// <summary>`--build-preview=&lt;typ&gt;` — park the preview on the first site
    /// that takes the building and centre the view on it, so the run leaves a
    /// picture. Purely a test harness.</summary>
    public string DemoBuildPreview(int typ)
    {
        if (_nav == null || Patterns == null || !Patterns.HasBuildings)
            return "build-preview: keine Muster";
        // A half-blocked site says more than a clear one: it shows both states
        // at once. Look for the most evenly split place, fall back to any.
        int bestCol = -1, bestRow = -1, bestScore = -1;
        var cells = new List<SiteCell>();
        for (int r = 0; r < _nav.Height; r++)
            for (int c = 0; c < _nav.Width; c++)
            {
                CanBuild(Patterns, typ, c, r, -1, cells);
                if (cells.Count == 0) continue;
                int ok = 0;
                foreach (var s in cells) if (s.Ok) ok++;
                int score = System.Math.Min(ok, cells.Count - ok);   // 0 when uniform
                if (score > bestScore) { bestScore = score; bestCol = c; bestRow = r; }
            }
        if (bestCol < 0) { GD.Print($"build-preview: {BuildingTypeName(typ)} passt nirgends"); return ""; }

        SetBuildPreview(typ, bestCol, bestRow);
        int good = 0;
        foreach (var s in _previewCells) if (s.Ok) good++;
        GD.Print($"build-preview: {BuildingTypeName(typ)} bei ({bestCol},{bestRow}), " +
                 $"{_previewCells.Count} Zellen geprueft — {good} frei, " +
                 $"{_previewCells.Count - good} gesperrt, Bauplatz {(_previewOk ? "JA" : "NEIN")}");
        return "";
    }

    /// <summary>Where the preview sits, in map pixels — for the harness to
    /// point the camera at.</summary>
    public Vector2? BuildPreviewCentre
    {
        get
        {
            if (_previewType <= 0 || _previewCells.Count == 0) return null;
            float x = 0, y = 0;
            foreach (var c in _previewCells)
            {
                var r = CellRect(_ox, _oy, c.Col, c.Row, ElevOf(c.Col, c.Row));
                x += r.Position.X + r.Size.X / 2; y += r.Position.Y + r.Size.Y / 2;
            }
            return new Vector2(x / _previewCells.Count, y / _previewCells.Count);
        }
    }

    private void DrawBuildPreview()
    {
        if (_previewType <= 0 || _previewCells.Count == 0) return;
        foreach (var c in _previewCells)
        {
            var r = CellRect(_ox, _oy, c.Col, c.Row, ElevOf(c.Col, c.Row));
            var col = c.Ok ? new Color(0.25f, 1f, 0.35f, 0.30f)
                           : new Color(1f, 0.2f, 0.2f, 0.35f);
            DrawRect(r, col, true);
            DrawRect(r, col with { A = 0.85f }, false, 1f);
        }
    }

    /// <summary>
    /// `--build-check` — the build machinery on a real, loaded map.
    ///
    /// <para>Sweeps every cell for each of the three buildable types, counts
    /// where they would go, then actually raises one at the first site that
    /// takes it and reports what changed in the imap. Numbers, not a
    /// screenshot: how many sites, how many cells the mask blocked, how many
    /// the tiles drew over without blocking.</para>
    /// </summary>
    public string BuildCheck(Image? into = null)
    {
        if (_nav == null) return "build-check: kein Gitter";
        if (Patterns == null || !Patterns.HasBuildings)
            return "build-check: keine Muster fuer dieses Tileset";

        var sb = new System.Text.StringBuilder();
        int[] types = { TypeDepot, TypeGenerator, TypeFieldMine };
        foreach (int typ in types)
        {
            var bt = Patterns.GetBuildingType(typ);
            if (bt.IsEmpty)
            {
                sb.Append($"build-check: {BuildingTypeName(typ)} (typ {typ}) — " +
                          $"dieses Tileset kennt ihn nicht\n");
                continue;
            }

            // what the imap should gain: the mask, plus any door cell the mask
            // does not already cover (a door blocks too — it is written 0xFFFF)
            int tiles = 0, blocks = 0;
            var willBlock = new HashSet<(int, int)>();
            for (int x = 0; x < CwpFile.PatternWidth; x++)
                for (int y = 0; y < CwpFile.PatternHeight; y++)
                {
                    if (Patterns.PatternTile(bt.FirstPattern, x, y) != 0) tiles++;
                    if (Patterns.PatternBlocks(bt.FirstPattern, x, y))
                    { blocks++; willBlock.Add((x, y)); }
                }
            foreach (var d in BuildingDoors(typ)) willBlock.Add((d.X, d.Y));
            int expect = willBlock.Count;

            int sites = 0;
            var first = new Vector2I(-1, -1);
            for (int r = 0; r < _nav.Height; r++)
                for (int c = 0; c < _nav.Width; c++)
                    if (CanBuild(Patterns, typ, c, r))
                    {
                        sites++;
                        if (first.X < 0) first = new Vector2I(c, r);
                    }

            sb.Append($"build-check: {BuildingTypeName(typ)} (typ {typ}) — Muster " +
                      $"{bt.FirstPattern} von {bt.PatternCount}, {tiles} Kachelzellen, " +
                      $"{blocks} davon sperrend; {sites} Bauplaetze auf der Karte");

            if (first.X >= 0)
            {
                int before = 0;
                for (int x = 0; x < CwpFile.PatternWidth; x++)
                    for (int y = 0; y < CwpFile.PatternHeight; y++)
                        if (_nav.OccupantAt(first.X + x, first.Y + y) >= 0) before++;

                var bld = PlaceBuilding(Patterns, typ, first.X, first.Y, 0, into);
                int after = 0;
                for (int x = 0; x < CwpFile.PatternWidth; x++)
                    for (int y = 0; y < CwpFile.PatternHeight; y++)
                        if (_nav.OccupantAt(first.X + x, first.Y + y) >= 0) after++;

                sb.Append(bld == null
                    ? $"; SETZEN FEHLGESCHLAGEN bei ({first.X},{first.Y})"
                    : $"; gesetzt bei ({first.X},{first.Y}) als Platz {bld.Slot} " +
                      $"\"{bld.Name}\", {bld.Hp} TP, Zustand {bld.Condition}, " +
                      $"{bld.Doors} Tuer(en); imap belegt {before} -> {after} " +
                      $"(erwartet +{expect})" +
                      (after - before == expect ? "" : "  ⚠ WEICHT AB"));
                // and the site must be refused now
                if (bld != null && CanBuild(Patterns, typ, first.X, first.Y))
                    sb.Append("; ⚠ Platz ist danach IMMER NOCH frei");
            }
            sb.Append('\n');
        }
        return sb.ToString().TrimEnd();
    }
}
