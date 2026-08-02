namespace AkteEuropaReborn.Import;

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Godot;

/// <summary>
/// Writes the unit sprites out of ROBO.CWR into <c>user://data/Units</c> — the
/// piece that decides whether a freshly imported game shows vehicles or
/// coloured dots.
///
/// Six sets, exactly the ones the renderer asks for
/// (<c>MapEntityLayer</c> lines 2913, 4053, 4113, 4128, 4181, 4272):
///
///   <c>{unit_type}/fN.png</c>        the chassis alone, TIGHT, with its size
///                                    and y offset recorded in units_index.json
///   <c>hull/{unit_type}/fN.png</c>   the propulsion on the shared canvas
///   <c>turret/{weapon}/fN.png</c>    the weapon on the same canvas, so it can
///                                    aim independently of the hull
///   <c>composed/{ut}_{weapon}/fN.png</c>  both, in draw order
///   <c>infantry/{set}/fN_bB.png</c>  fifteen blocks per foot soldier
///   <c>aircraft/{kind}/fN.png</c>, <c>train/{part}/fN.png</c>
///
/// Everything on the canvas is 64x56 (<see cref="CwrFile.CanvasW"/>): one shared
/// anchor is what lets the layers line up. The per-unit_type set is the odd one
/// out and deliberately so — it is cropped, and units_index.json carries the
/// offsets the renderer needs to place it.
///
/// Ported from compose_units.py, copy_units.py, infantry_export.py,
/// aircraft_export.py and train_export.py; every address in the comments is
/// theirs.
/// </summary>
public sealed class UnitsExporter
{
    private readonly CwrFile _cwr;
    private readonly PalFile _pal;
    private readonly ExeTables? _exe;
    private readonly string _dst;

    public int Frames, Hulls, Turrets, Combos, InfantrySets, Aircraft, Wagons, Chassis;

    public UnitsExporter(CwrFile cwr, PalFile pal, ExeTables? exe, string unitsDir)
    {
        _cwr = cwr; _pal = pal; _exe = exe;
        _dst = unitsDir.TrimEnd('/', '\\');
    }

    /// <summary>sec19 KIND to sprite part, read straight off the draw path
    /// @0x42b867 (an index table, not the old `airframe - 8` arithmetic, which
    /// was right for exactly one of the eight by luck).</summary>
    public static readonly (int Kind, int Part)[] AircraftParts =
    {
        (1, 114), (2, 115), (3, 119), (10, 112), (11, 113), (12, 118),
        (13, 117), (14, 116),
    };

    /// <summary>The rotor, drawn on top of a helicopter and shared by all of
    /// them, so it gets a folder of its own named by part.</summary>
    public static readonly int[] RotorParts = { 110, 111 };

    /// <summary>The rail cars: the wagon draw path @0x42b4c0 picks part 57 for
    /// the one that leads and 58 for the rest.</summary>
    public static readonly int[] TrainParts = { 57, 58 };

    /// <summary>Read off the rendered blocks of infantry set 0: 0..7 walk,
    /// 9..10 fire, 11 standing, 12..14 falling. Said as observed, because it
    /// comes from looking at the frames and not from code.</summary>
    public const int InfantryBlocks = 15, InfantryIdleBlock = 11;

    // ---- the whole job ------------------------------------------------------

    /// <summary>`combos` are the (unit_type, weapon component) pairs actually
    /// placed on the maps, plus what the shipyard can build.</summary>
    public void Run(IEnumerable<(int UnitType, int Weapon)> combos, Action<string>? say = null)
    {
        Directory.CreateDirectory(_dst);
        var seen = new SortedSet<(int, int)>();
        var unitTypes = new SortedSet<int>();
        foreach (var c in combos) { seen.Add(c); unitTypes.Add(c.UnitType); }

        var hulls = new SortedDictionary<int, string>();
        var turrets = new SortedSet<int>();
        var composed = new List<(string Key, int Ut, int Weapon, string Name)>();

        foreach (var (ut, weap) in seen)
        {
            int prop = ComponentOf(ut);
            if (prop <= 0 && weap == 0) continue;          // pure scenery
            string name = NameOf(ut);

            // composed: propulsion first, then the weapon on its deck
            string key = $"{ut}_{weap}";
            bool newHull = !hulls.ContainsKey(ut);
            bool newTurret = weap > 0 && !turrets.Contains(weap);
            for (int f = 0; f < CwrFile.Facings; f++)
            {
                // A facing the bank has no frame for stays out of the mount
                // table too — the map layer falls back to facing 0 there, and
                // the point it draws with has to fall back with it.
                var hull = Stack(f, prop);
                if (!_deck.ContainsKey((ut, f)) && !IsBlank(hull)) _deck[(ut, f)] = DeckPoint(hull);
                Image? turret = null;
                if (weap > 0)
                {
                    turret = Stack(f, weap);
                    if (!_foot.ContainsKey((weap, f)) && !IsBlank(turret)) _foot[(weap, f)] = FootPoint(turret);
                }
                Save($"composed/{key}/f{f}.png",
                     Mount(hull, turret, Deck(ut, f), weap > 0 ? Foot(weap, f) : Vector2I.Zero));
                if (newHull) Save($"hull/{ut}/f{f}.png", hull);
                if (newTurret && turret != null) Save($"turret/{weap}/f{f}.png", turret);
            }
            composed.Add((key, ut, weap, name));
            Combos++;

            if (newHull) { hulls[ut] = name; Hulls++; }
            if (newTurret) { turrets.Add(weap); Turrets++; }
        }
        say?.Invoke($"Rumpf/Turm: {Hulls} Fahrwerke, {Turrets} Waffen, {Combos} Kombinationen");

        // The chassis set covers every unit type that appears, not only those
        // that came with a weapon — a scenery piece has a sprite too.
        //
        // And it runs past the highest one in use: a factory can build a
        // chassis that stands on no map (175 "Schweber" is on none of the 44),
        // and without its pictures the unit would come out of the works
        // invisible. The stats table is walked upward while a row still has a
        // name and a component of its own, which is where the chassis run ends
        // — row 176 has neither.
        int top = 0;
        foreach (int ut in unitTypes) top = Math.Max(top, ut);
        for (int ut = top + 1; ut < 256; ut++)
        {
            var st = _exe?.StatsFor(ut);
            if (st == null || st.Name.Length == 0 || st.ComponentId <= 0) break;
            if (_cwr.PartBase(st.ComponentId) < 0) break;
            unitTypes.Add(ut);
        }
        WriteChassis(unitTypes, say);
        WriteInfantry(say);
        WriteAircraft(say);
        WriteTrains(say);
        WritePartsIndex(hulls, turrets);
        WriteComposedIndex(composed);
    }

    // ---- the cropped per-unit_type set + units_index.json -------------------

    /// <summary>The chassis on its own, cropped, together with the size and y
    /// offset of every facing, which is what the renderer places it by.
    ///
    /// ⚠ ONE KNOWN DIFFERENCE from the older Python export, stated rather than
    /// papered over. The frame of a facing is taken by the game's own rule,
    /// `base + block*8 + facing` (@0x429c80). Component 9 (unit_type 168,
    /// Kugelroller) has only frame `base+0` populated and seven holes after it,
    /// so seven facings come out empty here. `copy_units.py` filled them by
    /// walking the *populated* frames in order — 528, 536, 544 … — which gives
    /// eight pictures but is a numbering the draw code does not use, and the
    /// composed set it wrote alongside is blank in exactly those facings.
    /// Which of the two the original really does is unresolved; the formula the
    /// disassembly shows is what is followed here.</summary>
    private void WriteChassis(IEnumerable<int> unitTypes, Action<string>? say)
    {
        var sb = new StringBuilder();
        sb.Append("{\"_note\":\"Block-0 chassis sprites, 8 facings, from ROBO.CWR RE\",\"units\":{");
        bool first = true;
        foreach (int ut in unitTypes)
        {
            int comp = ComponentOf(ut);
            int b = comp > 0 ? _cwr.PartBase(comp) : -1;
            if (b < 0) continue;

            var facings = new StringBuilder();
            bool ff = true, any = false;
            for (int f = 0; f < CwrFile.Facings; f++)
            {
                var fr = _cwr.DecodeFrame(b + f);
                if (fr == null) continue;
                Save($"{ut}/f{f}.png", CwpFile.ToImage(fr, _pal));
                if (!ff) facings.Append(',');
                ff = false;
                any = true;
                facings.Append($"\"{f}\":{{\"w\":{fr.Width},\"h\":{fr.Height},\"yoff\":{fr.YOffset}}}");
            }
            if (!any) continue;
            Chassis++;
            if (!first) sb.Append(',');
            first = false;
            sb.Append($"\"{ut}\":{{\"name\":\"{Esc(NameOf(ut))}\",\"category\":\"{Esc(CategoryOf(ut))}\",");
            sb.Append("\"facings\":{").Append(facings).Append("}}");
        }
        sb.Append("}}");
        File.WriteAllText(_dst + "/units_index.json", sb.ToString(), new UTF8Encoding(false));
        say?.Invoke($"Fahrgestelle: {Chassis} Typen mit units_index.json");
    }

    // ---- infantry -----------------------------------------------------------

    private void WriteInfantry(Action<string>? say)
    {
        var aux = _cwr.Aux;
        var sb = new StringBuilder();
        sb.Append("{\"_source\":\"ROBO.CWR aux table @0x7a3c48, 30 x 34 bytes\",");
        sb.Append("\"_formula\":\"frame = base + block*8 + facing (draw code @0x42a89c)\",");
        sb.Append("\"walk\":[0,1,2,3,4,5,6,7],\"fire\":[9,10],\"idle\":11,\"death\":[12,13,14],");
        sb.Append($"\"n_blocks\":{InfantryBlocks},\"n_facings\":{CwrFile.Facings},\"sets\":[");
        bool first = true;

        for (int i = 0; i + CwrFile.InfantryStride <= aux.Length; i += CwrFile.InfantryStride)
        {
            int b = BitConverter.ToUInt16(aux, i);
            int n = BitConverter.ToUInt16(aux, i + 2);
            if (b == 0 && n == 0) continue;
            int set = i / CwrFile.InfantryStride;

            for (int blk = 0; blk < InfantryBlocks; blk++)
                for (int f = 0; f < CwrFile.Facings; f++)
                {
                    int idx = b + blk * CwrFile.Facings + f;
                    if (_cwr.DecodeFrame(idx) == null) continue;
                    var img = _cwr.FacingImage(idx, _pal);
                    Save($"infantry/{set}/f{f}_b{blk}.png", img);
                    if (blk == InfantryIdleBlock) Save($"infantry/{set}/f{f}.png", img);
                }

            if (!first) sb.Append(',');
            first = false;
            sb.Append($"{{\"index\":{set},\"base\":{b},\"n_anim\":{n},\"anims\":[");
            for (int k = 0; k < n && k < 10; k++)
            {
                if (k > 0) sb.Append(',');
                sb.Append($"{{\"start\":{BitConverter.ToUInt16(aux, i + 4 + k * 2)},");
                sb.Append($"\"length\":{aux[i + 0x18 + k]}}}");
            }
            sb.Append("]}");
            InfantrySets++;
        }
        sb.Append("]}");
        Directory.CreateDirectory(_dst + "/infantry");
        File.WriteAllText(_dst + "/infantry/infantry_index.json", sb.ToString(), new UTF8Encoding(false));
        say?.Invoke($"Infanterie: {InfantrySets} Saetze");
    }

    // ---- aircraft and rail cars --------------------------------------------

    private void WriteAircraft(Action<string>? say)
    {
        foreach (var (kind, part) in AircraftParts)
        {
            int b = _cwr.PartBase(part);
            if (b < 0) continue;
            for (int f = 0; f < CwrFile.Facings; f++)
                Save($"aircraft/{kind}/f{f}.png", _cwr.PartImage(part, f, _pal));
            Aircraft++;
        }
        // the hulls again under their part number, plus the shared rotor
        foreach (int part in Concat(PartsOf(AircraftParts), RotorParts))
        {
            if (_cwr.PartBase(part) < 0) continue;
            for (int f = 0; f < CwrFile.Facings; f++)
                Save($"aircraft/{part}/f{f}.png", _cwr.PartImage(part, f, _pal));
        }
        say?.Invoke($"Flugzeuge: {Aircraft} Arten");
    }

    private void WriteTrains(Action<string>? say)
    {
        var sb = new StringBuilder();
        sb.Append("{\"_source\":\"draw path @0x42b4c0, part table 0x77c872, piece table 0x5393f0\",");
        sb.Append("\"_note\":\"the frame index is the RAIL PIECE (wagon +0x0b), not a facing\",");
        sb.Append("\"wagon_part\":{\"0\":57,\"1\":58,\"2\":58,\"3\":58},");
        sb.Append("\"wagon_rotate\":{\"3\":4},\"base_frame\":{");
        bool first = true;
        foreach (int part in TrainParts)
        {
            int b = _cwr.PartBase(part);
            if (!first) sb.Append(',');
            first = false;
            sb.Append($"\"{part}\":{(b < 0 ? "null" : b.ToString())}");
            if (b < 0) continue;
            for (int f = 0; f < CwrFile.Facings; f++)
                Save($"train/{part}/f{f}.png", _cwr.PartImage(part, f, _pal));
            Wagons++;
        }
        sb.Append("}}");
        Directory.CreateDirectory(_dst + "/train");
        File.WriteAllText(_dst + "/train/train.json", sb.ToString(), new UTF8Encoding(false));
        say?.Invoke($"Zuege: {Wagons} Wagentypen");
    }

    // ---- the two indices ----------------------------------------------------

    private void WritePartsIndex(SortedDictionary<int, string> hulls, SortedSet<int> turrets)
    {
        var sb = new StringBuilder();
        sb.Append("{\"_note\":\"hull (the propulsion alone) and turret (weapon) drawn separately ");
        sb.Append("so the weapon can aim independently; same 64x56 anchor\",");
        sb.Append("\"_mount\":\"a hull carries a `deck` point per facing and a turret a `foot` ");
        sb.Append("point; the turret is drawn with foot on deck. OURS, measured from the art at ");
        sb.Append($"{DeckPercent}% of the hull's height - the game has its own rule (the vehicle ");
        sb.Append("draw offsets the turret by +0x10 in x and mount-0x0c in y, per chassis) but ");
        sb.Append("that branch does not fit these frames. Stacking both at the same anchor puts ");
        sb.Append("every turret on the tracks\",");
        sb.Append($"\"canvas\":[{CwrFile.CanvasW},{CwrFile.CanvasH}],\"hulls\":{{");
        bool first = true;
        foreach (var kv in hulls)
        {
            if (!first) sb.Append(',');
            first = false;
            sb.Append($"\"{kv.Key}\":{{\"unit_type\":{kv.Key},\"name\":\"{Esc(kv.Value)}\",\"deck\":[");
            for (int f = 0; f < CwrFile.Facings; f++)
            {
                sb.Append(f > 0 ? "," : "");
                if (_deck.TryGetValue((kv.Key, f), out var p)) sb.Append($"[{p.X},{p.Y}]");
                else sb.Append("null");            // facing without a frame
            }
            sb.Append("]}");
        }
        sb.Append("},\"turrets\":{");
        first = true;
        foreach (int t in turrets)
        {
            if (!first) sb.Append(',');
            first = false;
            sb.Append($"\"{t}\":{{\"component\":{t},\"foot\":[");
            for (int f = 0; f < CwrFile.Facings; f++)
            {
                sb.Append(f > 0 ? "," : "");
                if (_foot.TryGetValue((t, f), out var p)) sb.Append($"[{p.X},{p.Y}]");
                else sb.Append("null");            // facing without a frame
            }
            sb.Append("]}");
        }
        sb.Append("}}");
        File.WriteAllText(_dst + "/parts_index.json", sb.ToString(), new UTF8Encoding(false));
    }

    private void WriteComposedIndex(List<(string Key, int Ut, int Weapon, string Name)> composed)
    {
        var sb = new StringBuilder();
        sb.Append("{\"_note\":\"composed unit sprites (propulsion+weapon), 8 facings; ");
        sb.Append("there is no body layer\",");
        sb.Append($"\"canvas\":[{CwrFile.CanvasW},{CwrFile.CanvasH}],\"combos\":{{");
        bool first = true;
        foreach (var c in composed)
        {
            if (!first) sb.Append(',');
            first = false;
            sb.Append($"\"{c.Key}\":{{\"unit_type\":{c.Ut},\"weapon\":{c.Weapon},");
            sb.Append($"\"name\":\"{Esc(c.Name)}\"}}");
        }
        sb.Append("}}");
        Directory.CreateDirectory(_dst + "/composed");
        File.WriteAllText(_dst + "/composed/composed_index.json", sb.ToString(), new UTF8Encoding(false));
    }

    // ---- helpers ------------------------------------------------------------

    /// <summary>The components of one facing, drawn in the order the game draws
    /// them. A component of 0 or one the bank has no frames for is simply not
    /// there, which is a normal case, not an error.</summary>
    private Image Stack(int facing, params int[] components)
    {
        var canvas = Image.CreateEmpty(CwrFile.CanvasW, CwrFile.CanvasH, false, Image.Format.Rgba8);
        canvas.Fill(new Color(0, 0, 0, 0));
        foreach (int c in components)
        {
            if (c <= 0 || c >= CwrFile.PartCount) continue;
            if (_cwr.PartBase(c) < 0) continue;
            var layer = _cwr.PartImage(c, facing, _pal);
            canvas.BlendRect(layer, new Rect2I(0, 0, CwrFile.CanvasW, CwrFile.CanvasH), Vector2I.Zero);
        }
        return canvas;
    }

    private static IEnumerable<int> PartsOf((int Kind, int Part)[] t)
    {
        foreach (var (_, p) in t) yield return p;
    }

    private static IEnumerable<int> Concat(IEnumerable<int> a, IEnumerable<int> b)
    {
        foreach (int x in a) yield return x;
        foreach (int x in b) yield return x;
    }

    private int ComponentOf(int unitType) => _exe?.StatsFor(unitType)?.ComponentId ?? 0;

    private string NameOf(int unitType)
    {
        string n = _exe?.StatsFor(unitType)?.Name ?? "";
        return n.Length > 0 ? n : unitType.ToString();
    }

    private string CategoryOf(int unitType) => NameOf(unitType);

    /// <summary>Writes a picture — unless there is nothing on it.
    ///
    /// A chassis without directions leaves seven of its eight facings empty:
    /// component 9 (unit_type 168) has a frame at `base + block*8 + 0` and
    /// 0xFFFFFFFF in the other seven, and all 40 of that type placed across the
    /// 44 maps carry facing 0 and nothing else. Writing those seven out as blank
    /// 64x56 images made them look present to everything downstream, and a unit
    /// that turns while driving then vanished instead of falling back. Leaving
    /// the file out says what is true: that facing does not exist.</summary>
    private void Save(string rel, Image img)
    {
        if (IsBlank(img)) { Blank++; return; }
        string p = _dst + "/" + rel;
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        img.SavePng(p);
        Frames++;
    }

    /// <summary>How many frames were empty and therefore not written.</summary>
    public int Blank;

    // ---- where a turret sits on its hull ------------------------------------
    //
    // ⚠ OURS, and only because the original's own rule could not be read out.
    //
    // What IS read: the vehicle draw offsets the turret — @0x42adb7..0x42ae31
    // puts it at `hull_x + 0x10`, `hull_y + mount - 0x0c` with a per-chassis
    // mount (70 -> 15, 71 -> 2, 72 -> 12), and a weapon component <= 0x14 gets
    // no turret at all. But that branch does not fit the frames this exporter
    // writes, and the ordinary unit takes a different case of the draw's switch
    // on entity +0x47 (jump table @0x42bcd8) whose frame source is not settled.
    // Stacking both layers at the same anchor — which this exporter and
    // compose_units.py both did, and which UNIT_SPRITES_RE.md called "visually
    // correct" without checking — puts every turret down on the tracks.
    //
    // So the mount is MEASURED FROM THE ART instead: the turret's bottom centre
    // is placed on the hull's deck, and the deck is taken as the middle of the
    // hull's own outline at 45% of its height. The factor was picked by laying
    // 0.25 / 0.35 / 0.45 / 0.55 side by side over the six commonest chassis and
    // weapons; at 0.45 the turret ring sits on the mounting plate.
    //
    // It is a stand-in for a rule the game has and we have not found. When the
    // real one turns up this whole block goes.

    /// <summary>How far down the hull's outline the deck sits, in percent.
    /// Whole numbers on purpose: the Python reference has to land on the same
    /// pixel, and 0.45f times a height is not the same double as 0.45.</summary>
    public const int DeckPercent = 45;

    private readonly Dictionary<(int Ut, int F), Vector2I> _deck = new();
    private readonly Dictionary<(int W, int F), Vector2I> _foot = new();

    private Vector2I Deck(int ut, int f)
        => _deck.TryGetValue((ut, f), out var v) ? v
           : _deck.TryGetValue((ut, 0), out var z) ? z
           : new Vector2I(CwrFile.CanvasW / 2, CwrFile.CanvasH / 2);

    private Vector2I Foot(int w, int f)
        => _foot.TryGetValue((w, f), out var v) ? v
           : _foot.TryGetValue((w, 0), out var z) ? z
           : new Vector2I(CwrFile.CanvasW / 2, CwrFile.CanvasH - 1);

    private static (int X0, int Y0, int X1, int Y1)? Bounds(Image img)
    {
        int x0 = int.MaxValue, y0 = int.MaxValue, x1 = -1, y1 = -1;
        for (int y = 0; y < img.GetHeight(); y++)
            for (int x = 0; x < img.GetWidth(); x++)
                if (img.GetPixel(x, y).A > 0.01f)
                {
                    if (x < x0) x0 = x;
                    if (y < y0) y0 = y;
                    if (x > x1) x1 = x;
                    if (y > y1) y1 = y;
                }
        return x1 < 0 ? null : (x0, y0, x1, y1);
    }

    /// <summary>The point on the hull a turret is mounted at.</summary>
    private static Vector2I DeckPoint(Image img)
    {
        if (Bounds(img) is not { } b) return new Vector2I(CwrFile.CanvasW / 2, CwrFile.CanvasH / 2);
        return new Vector2I((b.X0 + b.X1) / 2,
                            b.Y0 + (DeckPercent * (b.Y1 - b.Y0) + 50) / 100);
    }

    /// <summary>The point on the turret that stands on the deck.</summary>
    private static Vector2I FootPoint(Image img)
    {
        if (Bounds(img) is not { } b) return new Vector2I(CwrFile.CanvasW / 2, CwrFile.CanvasH - 1);
        return new Vector2I((b.X0 + b.X1) / 2, b.Y1);
    }

    /// <summary>Hull with the turret set down on its deck — the same offset the
    /// map layer applies when it draws the two separately, so the composed
    /// picture and the live unit agree.</summary>
    private static Image Mount(Image hull, Image? turret, Vector2I deck, Vector2I foot)
    {
        var canvas = Image.CreateEmpty(CwrFile.CanvasW, CwrFile.CanvasH, false, Image.Format.Rgba8);
        canvas.Fill(new Color(0, 0, 0, 0));
        canvas.BlendRect(hull, new Rect2I(0, 0, CwrFile.CanvasW, CwrFile.CanvasH), Vector2I.Zero);
        if (turret != null)
            canvas.BlendRect(turret, new Rect2I(0, 0, CwrFile.CanvasW, CwrFile.CanvasH), deck - foot);
        return canvas;
    }

    private static bool IsBlank(Image img)
    {
        for (int y = 0; y < img.GetHeight(); y++)
            for (int x = 0; x < img.GetWidth(); x++)
                if (img.GetPixel(x, y).A > 0f) return false;
        return true;
    }

    private static string Esc(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
