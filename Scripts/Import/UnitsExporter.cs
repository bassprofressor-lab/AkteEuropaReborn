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
/// The canvas starts at 64x56 (<see cref="CwrFile.CanvasW"/>) and grows to the
/// right and downwards for the frames that need more — the ships do. The ANCHOR
/// does not move with it, which is what still lets the layers line up.
///
/// The per-unit_type set is the odd one out and deliberately so — it is cropped,
/// and units_index.json carries the offsets the renderer needs to place it.
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

    public int Frames, Hulls, Turrets, Combos, InfantrySets, Aircraft, Wagons, Rails, RailFrames, Chassis;

    /// <summary>Wieviele Hangpose-Bilder geschrieben wurden — bis zum
    /// 15.08.2026 waren es null, siehe <see cref="StackAt"/>.</summary>
    public int SlopePoses;

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

    /// <summary>Der GLEISKÖRPER selbst — Teil 64 flach, Teil 65 auf Stützen.
    ///
    /// <para>Gefunden, nachdem eine erste Lesung danebenlag: die Zeichenroutine
    /// rechnet für eine Kartenzelle zwar mit Bildern 40..47 von Teil 57, aber
    /// das sind Wagenkörper. Also wurden die belegten Teile 50..65 der Reihe
    /// nach ausgegeben und angesehen — und 64 ist genau das Gesuchte: acht
    /// dünne Schienenstücke in den acht Richtungen, ohne Fahrzeug darauf. 65
    /// ist dieselbe Strecke auf Stützen.</para>
    ///
    /// <para>Warum das so lange fehlte: das Bild eines WAGGONS trägt sein
    /// eigenes Schienenstück mit, also sah man Gleis nur unter dem Zug — und
    /// deshalb wurde nie danach gesucht, ob es die Schiene auch für sich
    /// gibt.</para></summary>
    /// <para>⚠ 13.08.2026 — es sind FÜNF, nicht zwei. <c>rail_pylon_pass</c>
    /// @0x4B0350 rechnet für jedes Stück mit <c>platz % 6 == 0</c> ein Bild
    /// <c>grundbild + 20·k</c> mit k = 0..3, und der Zeichner @0x42D4FE nimmt
    /// dafür <c>partBase(65) + bild</c>. Da Teil 64 und 65 je genau 20 Bilder
    /// führen (gemessen), zeigt <c>partBase(65) + 20·k</c> auf die Teile
    /// <b>65, 66, 67, 68</b> — vier STÜTZENLÄNGEN, ausgewählt aus dem Gelände
    /// neben dem Stück.</para></summary>
    /// <para>⚠ 14.08.2026 — <b>Teil 69 dazu: das ZERSCHOSSENE Gleis.</b> Der
    /// Farb-Durchgang steigt bei <c>bild >= 100</c> aus, bevor er 64 oder 65
    /// wählt (<c>cmp bl,0x64 / jae</c> @0x42B743, F: @0x42A930) — er zeichnet
    /// dort also weder Träger noch Bock, sondern <c>partBase(64) + bild</c>,
    /// und mit <c>bild ∈ [100,119]</c> sind das die Bilder 4020..4039. Die
    /// Teiltabelle @0x77C870 gibt 64→3920, 65→3940, 66→3960, 67→3980,
    /// 68→4000, <b>69→4020</b> — also exakt und vollständig Teil 69.</para>
    ///
    /// <para>Anders als bei 64..68 sind dessen Bilder 10..19 <b>keine
    /// Schattenmasken</b>, sondern die zweite Zufallsvariante: gemessen hat
    /// Bild 3930 (Teil 64 +10) genau EINEN Palettenindex, Bild 4030 dagegen
    /// fünfzehn. <c>rail_hit</c> @0x4B0460 würfelt sie einmal beim Treffer
    /// (<c>bild += (10 + zufall&amp;1)·10</c>) und sie bleibt im Bildwert
    /// stehen — beim Laden also LESEN, nicht neu würfeln.</para></summary>
    public static readonly int[] RailParts = { 64, 65, 66, 67, 68, 69 };

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
        // ⚠ Die Hangtabelle MUSS vor der Schleife stehen. Sie wurde bisher erst
        // in Mount() nachgeladen, und Mount() läuft erst beim Index ganz am
        // Ende — die Hangposen hätten dann alle den Rückfallwert benutzt.
        if (_exe != null && _exe.TurretMountFound) _slopeBlocks = _exe.SlopeBlocks();
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
            var flat = FlatOffset(ut);
            bool newHull = !hulls.ContainsKey(ut);
            bool newTurret = weap > 0 && !turrets.Contains(weap);
            for (int f = 0; f < CwrFile.Facings; f++)
            {
                var hull = Stack(f, prop);
                Image? turret = weap > 0 ? Stack(f, weap) : null;
                Save($"composed/{key}/f{f}.png", Compose(hull, turret, flat));
                if (newHull) Save($"hull/{ut}/f{f}.png", hull);
                if (newTurret && turret != null) Save($"turret/{weap}/f{f}.png", turret);
            }
            // The other pose groups of the chassis, where it owns any. Group 0
            // keeps its old file name so nothing that already reads these has
            // to change; 1..n-1 go into g<n>/ beside it.
            if (newHull)
                for (int g = 1; g < _cwr.PartGroups(prop); g++)
                    for (int f = 0; f < CwrFile.Facings; f++)
                        Save($"hull/{ut}/g{g}/f{f}.png", StackPose(f, g, prop));

            // ⚠ 15.08.2026 — DIE HANGPOSEN, bis heute nicht exportiert.
            //
            // Klasse 0 ist das flache Bild und steht schon oben; 1..4 kommen in
            // s<k>/ daneben, unter der jeweiligen Gruppe. Der Name ist so
            // gewählt, dass nichts, was die alten Dateien liest, sich ändern
            // muss — eine Einheit ohne Hangbild fällt einfach auf das flache
            // zurück.
            if (newHull)
                for (int k = 1; k < SlopeClasses; k++)
                {
                    int blk = SlopeBlockOf(k);
                    if (blk == 0) continue;               // dieselbe Pose wie flach
                    for (int g = 0; g < _cwr.PartGroups(prop); g++)
                        for (int f = 0; f < CwrFile.Facings; f++)
                            Save(g == 0 ? $"hull/{ut}/s{k}/f{f}.png"
                                        : $"hull/{ut}/g{g}/s{k}/f{f}.png",
                                 StackAt(f, blk, g, prop));
                    SlopePoses += CwrFile.Facings * _cwr.PartGroups(prop);
                }
            if (newTurret && weap > 0)
                for (int k = 1; k < SlopeClasses; k++)
                {
                    int blk = SlopeBlockOf(k);
                    if (blk == 0) continue;
                    for (int f = 0; f < CwrFile.Facings; f++)
                        Save($"turret/{weap}/s{k}/f{f}.png", StackAt(f, blk, 0, weap));
                    SlopePoses += CwrFile.Facings;
                }
            composed.Add((key, ut, weap, name));
            Combos++;

            if (newHull) { hulls[ut] = name; Hulls++; }
            if (newTurret) { turrets.Add(weap); Turrets++; }
        }
        say?.Invoke($"Rumpf/Turm: {Hulls} Fahrwerke, {Turrets} Waffen, {Combos} Kombinationen" +
                    $", {SlopePoses} Hangpose-Bilder (Bloecke " +
                    string.Join("/", _slopeBlocks) + " aus @0x4fa4d8)");

        // The chassis set covers every unit type that appears, not only those
        // that came with a weapon — a scenery piece has a sprite too.
        //
        // And it runs past the highest one in use: a factory can build a
        // chassis that stands on no map (175 "Schweber" is on none of the 44),
        // and without its pictures the unit would come out of the works
        // invisible. The stats table is walked upward while a row still has a
        // name and a component of its own, which is where the chassis run ends
        // — row 176 has neither.
        // ⚠ 15.08.2026 — DIE GANZE TABELLE, nicht nur was oberhalb liegt.
        //
        // Hier stand ein Lauf von `top + 1` aufwärts, der beim ersten leeren
        // Eintrag ABBRACH. Damit fehlten alle Typen UNTERHALB des höchsten
        // platzierten, die auf keiner Karte stehen: die 601 gespeicherten
        // Entwürfe benutzen unter anderem die Fahrwerke 148 und 149, und die
        // hatten überhaupt keine Bilder — weder unter hull/ noch sonstwo.
        //
        // Die Schranke bleibt streng (ein Name UND ein Bauteil, das Bilder
        // besitzt), nur der Abbruch wird zum Überspringen. Ein leerer Eintrag
        // mitten in der Tabelle ist ein Loch, kein Ende.
        for (int ut = 0; ut < 256; ut++)
        {
            var st = _exe?.StatsFor(ut);
            if (st == null || st.Name.Length == 0 || st.ComponentId <= 0) continue;
            if (_cwr.PartBase(st.ComponentId) < 0) continue;
            unitTypes.Add(ut);
        }
        // Jeder Fahrwerkstyp bekommt seinen hull/-Satz samt Hangposen und einen
        // Eintrag im Index — auch die, die mit keiner Waffe zusammen auf einer
        // Karte stehen. Sonst findet der Zeichner den Turmsitz und die
        // Posengruppen nicht, und die Einheit fiele auf den nackten
        // Fahrgestellsatz zurueck.
        foreach (int ut in unitTypes)
        {
            if (hulls.ContainsKey(ut)) continue;
            int prop = ComponentOf(ut);
            if (prop <= 0 || _cwr.PartBase(prop) < 0) continue;
            for (int g = 0; g < _cwr.PartGroups(prop); g++)
                for (int f = 0; f < CwrFile.Facings; f++)
                    Save(g == 0 ? $"hull/{ut}/f{f}.png" : $"hull/{ut}/g{g}/f{f}.png",
                         StackPose(f, g, prop));
            for (int k = 1; k < SlopeClasses; k++)
            {
                int blk = SlopeBlockOf(k);
                if (blk == 0) continue;
                for (int g = 0; g < _cwr.PartGroups(prop); g++)
                    for (int f = 0; f < CwrFile.Facings; f++)
                        Save(g == 0 ? $"hull/{ut}/s{k}/f{f}.png"
                                    : $"hull/{ut}/g{g}/s{k}/f{f}.png",
                             StackAt(f, blk, g, prop));
                SlopePoses += CwrFile.Facings * _cwr.PartGroups(prop);
            }
            hulls[ut] = NameOf(ut);
            Hulls++;
        }
        say?.Invoke($"Fahrwerke gesamt: {Hulls} (auch die, die auf keiner Karte stehen), " +
                    $"{SlopePoses} Hangpose-Bilder");

        WriteAllParts(say);
        WriteChassis(unitTypes, say);
        WriteInfantry(say);
        WriteAircraft(say);
        WriteTrains(say);
        WritePartsIndex(hulls, turrets);
        WriteComposedIndex(composed);
    }

    /// <summary>
    /// <b>DIE GANZE BANK, unter der Bauteilnummer.</b>
    ///
    /// <para>⚠ Bis zum 15.08.2026 hat der Exporter nur geschrieben, was auf
    /// einer Karte STEHT oder gebaut werden kann. Das reicht nicht: die
    /// gespeicherten Entwürfe (601 Stück in <c>unit_designs.json</c>) greifen auf
    /// die Waffen 0..19 und 65..79, und dazu gehören Bauteile, die auf keiner
    /// der 44 Karten vorkommen — <b>15 „Minenleger", 18 „Flak-Geschütz", 77
    /// „Antiradar"</b> und sieben weitere. Wer so etwas konstruiert, bekam ein
    /// unsichtbares Geschütz.</para>
    ///
    /// <para>Geschrieben wird deshalb JEDES belegte Teil unter seiner eigenen
    /// Nummer, mit allen Blöcken und Gruppen, die es besitzt. Das sind zehn
    /// Teile mehr als bisher und kostet ein paar hundert Bilder; dafür fehlt
    /// nichts mehr. Die benannten Sätze (hull/, turret/, aircraft/, train/)
    /// bleiben unverändert daneben stehen — sie sind der Weg, den der Zeichner
    /// zuerst geht, <c>part/</c> ist der Rückfall.</para></summary>
    private void WriteAllParts(Action<string>? say)
    {
        int parts = 0, frames = 0;
        foreach (var p in _cwr.PopulatedParts())
        {
            int groups = _cwr.PartGroups(p.Component);
            int blocks = System.Math.Max(1, System.Math.Min(p.Frames, CwrFile.GroupFrames)
                                            / CwrFile.Facings);
            for (int g = 0; g < groups; g++)
                for (int blk = 0; blk < blocks; blk++)
                    for (int f = 0; f < CwrFile.Facings; f++)
                    {
                        int fr = _cwr.PartFrame(p.Component, f, blk, g);
                        if (fr < 0 || _cwr.DecodeFrame(fr) == null) continue;
                        string dir = $"part/{p.Component}";
                        if (g > 0) dir += $"/g{g}";
                        if (blk > 0) dir += $"/b{blk}";
                        Save($"{dir}/f{f}.png", _cwr.PartImage(p.Component, f, _pal, blk, g));
                        frames++;
                    }
            parts++;
        }
        AllParts = parts; AllPartFrames = frames;
        say?.Invoke($"Bank: {parts} Teile vollstaendig unter part/, {frames} Bilder");
    }

    /// <summary>Wieviele Teile und Bilder <see cref="WriteAllParts"/> gelegt
    /// hat.</summary>
    public int AllParts, AllPartFrames;

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
        sb.Append("},\"rail_part\":{");
        // der blanke Gleiskoerper: 64 flach, 65 auf Stuetzen
        bool f2 = true;
        foreach (int part in RailParts)
        {
            int b = _cwr.PartBase(part);
            if (!f2) sb.Append(',');
            f2 = false;
            sb.Append($"\"{part}\":{(b < 0 ? "null" : b.ToString())}");
            if (b < 0) continue;
            // ⚠ 13.08.2026 — es waren nur ACHT Bilder exportiert, und damit
            // fehlten genau die RAMPEN. Teil 64 und 65 fuehren je 20 Bilder:
            // 0..5 die sechs Kantenformen, 6..9 die vier Rampen (Bild 6 und 7
            // waagerecht, 8 und 9 senkrecht), 10..19 die Schatten dazu. Teil 65
            // traegt darueber hinaus weitere Bloecke zu 20 — die Stuetzenarten,
            // die das Original in @0x4B0350 als `grundbild + 20*k` waehlt.
            // Exportiert wird jetzt, was der Teil wirklich hat.
            int cnt = System.Math.Min(_cwr.PartFrameCount(part), 80);
            for (int f = 0; f < cnt; f++)
                Save($"train/rail{part}/f{f}.png",
                     _cwr.PartImage(part, f % CwrFile.Facings, _pal, f / CwrFile.Facings));
            RailFrames += cnt;
            Rails++;
        }
        sb.Append("},\"_rail\":\"train/rail64 ist das flache Gleis, train/rail65 ");
        sb.Append("dasselbe auf Stuetzen -- acht Richtungen wie beim Waggon. Der ");
        sb.Append("Bildindex eines WAGGONS traegt sein Schienenstueck mit, deshalb ");
        sb.Append("sah man Gleis lange nur unter dem Zug\"}");
        Directory.CreateDirectory(_dst + "/train");
        File.WriteAllText(_dst + "/train/train.json", sb.ToString(), new UTF8Encoding(false));
        say?.Invoke($"Zuege: {Wagons} Wagentypen, {Rails} Gleisarten mit {RailFrames} Bildern");
    }

    // ---- the two indices ----------------------------------------------------

    private void WritePartsIndex(SortedDictionary<int, string> hulls, SortedSet<int> turrets)
    {
        var sb = new StringBuilder();
        sb.Append("{\"_note\":\"hull (the propulsion alone) and turret (weapon) drawn separately ");
        sb.Append("so the weapon can aim independently; same 64x56 anchor\",");
        sb.Append("\"_mount\":\"a hull carries the five turret offsets of its chassis, read out ");
        sb.Append("of the executable (GAME.EXE table at 0x4fa320): on flat ground the draw code ");
        sb.Append("takes mount[k] outright (@0x42a099), on the tilted path the average ");
        sb.Append("(mount[0]+mount[k])/2 (@0x429CCB); k = the tile's flag byte, 0 above 4. ");
        sb.Append("`slope_blocks` is the frame block a tilted unit is drawn from (@0x429B05). ");
        sb.Append("Since 15.08.2026 the exporter writes them: class 0 is the flat picture and ");
        sb.Append("keeps its old name, classes 1..4 sit in s<k>/ beside it (and under g<n>/ for ");
        sb.Append("the other pose groups). The class is the tile's FLAG byte, >4 counts as 0\",");
        // ⚠ The walker is not in that table's world at all — see WALKER_LIFT.
        sb.Append("\"_walker\":\"chassis 0x11 (Laeufer) does NOT use the mount table: @0x42a027 ");
        sb.Append("jumps past it to @0x42a0e8, whose first instruction is `sub bp, 0x1b` - a flat ");
        sb.Append("27 px lift and no x offset. Its hull is 40 px tall against 20..28 for every ");
        sb.Append("other land chassis, which is why the table's -8 put the turret at its feet\",");
        sb.Append($"\"walker_chassis\":{WalkerChassis},\"walker_lift\":{WalkerLift},");
        sb.Append("\"_groups\":\"pose groups of 48 frames; frame = base + group*48 + block*8 + ");
        sb.Append("facing (@0x429fa6), the group out of entity +0x11 masked to 3 bits ");
        sb.Append("(`imul ax,ax,0x30` @0x429b3b). Group 0 is hull/<ut>/f<n>.png, the rest ");
        sb.Append("hull/<ut>/g<g>/f<n>.png\",");
        // the minimum, not the size: a frame wider or taller gets its own
        sb.Append($"\"canvas_min\":[{CwrFile.CanvasW},{CwrFile.CanvasH}],");
        sb.Append($"\"canvas\":[{CwrFile.CanvasW},{CwrFile.CanvasH}],\"slope_blocks\":[");
        for (int k = 0; k < _slopeBlocks.Length; k++)
            sb.Append(k > 0 ? "," : "").Append(_slopeBlocks[k]);
        sb.Append("],\"hulls\":{");
        bool first = true;
        foreach (var kv in hulls)
        {
            if (!first) sb.Append(',');
            first = false;
            sb.Append($"\"{kv.Key}\":{{\"unit_type\":{kv.Key},\"name\":\"{Esc(kv.Value)}\"");
            int hc = ComponentOf(kv.Key);
            sb.Append($",\"component\":{hc},\"groups\":{_cwr.PartGroups(hc)}");
            var m = Mount(kv.Key);
            if (m != null)
            {
                sb.Append(",\"mount\":[");
                for (int k = 0; k < m.Length; k++)
                    sb.Append(k > 0 ? "," : "").Append($"[{m[k].X},{m[k].Y}]");
                sb.Append(']');
            }
            sb.Append('}');
        }
        sb.Append("},\"turrets\":{");
        first = true;
        foreach (int t in turrets)
        {
            if (!first) sb.Append(',');
            first = false;
            sb.Append($"\"{t}\":{{\"component\":{t}}}");
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
        => StackPose(facing, 0, components);

    /// <summary>As above, for one pose group — see <see cref="CwrFile.PartGroups"/>.
    /// A part that owns a single group ignores the number.
    ///
    /// ⚠ Deliberately NOT an overload of <c>Stack</c>. It was one for an hour,
    /// and `Stack(f, prop)` then bound to it with `prop` as the GROUP and an
    /// empty component list, so every composed and turret picture came out
    /// blank and was silently skipped. Two int parameters and a params array
    /// are a trap; the name keeps them apart.</summary>
    private Image StackPose(int facing, int group, params int[] components)
        => StackAt(facing, 0, group, components);

    /// <summary>
    /// Wie <see cref="StackPose"/>, aber mit dem <b>BLOCK</b> — der fünften
    /// Grösse in der Formel des Zeichenpfads
    /// <c>bild = basis + gruppe·48 + block·8 + richtung</c> (@0x429fa6).
    ///
    /// <para>Der Block ist die <b>Hangpose</b>: die Neigung, mit der eine
    /// Einheit auf schrägem Gelände steht. Welcher Block zu welcher Hangklasse
    /// gehört, sagt die Tabelle @0x4fa4d8 (<c>0, 16, 32, 8, 24</c> als
    /// Bildversätze, also die Blöcke 0, 2, 4, 1, 3), und die Klasse selbst ist
    /// das <b>Flag-Byte</b> der Kachel, auf der die Einheit steht
    /// (@0x429AD5 → 0x41d110, alles über 4 zählt als 0).</para>
    ///
    /// <para>⚠ Bis zum 15.08.2026 hat der Exporter <b>nur Block 0</b>
    /// geschrieben. Eine Einheit am Hang zeigte damit den flachen Rumpf,
    /// während der Boden unter ihr kippte — und der Turmsitz wurde bereits nach
    /// der Hangklasse verschoben, das Bild aber nicht. Der Hinweis stand seit
    /// Monaten im eigenen Index (»the exporter writes block 0 only«).</para>
    /// </summary>
    private Image StackAt(int facing, int block, int group, params int[] components)
    {
        // the canvas has to hold the WIDEST layer, or a ship's hull loses its
        // bow to a turret-sized picture — see CwrFile.CanvasFor
        int cw = CwrFile.CanvasW, ch = CwrFile.CanvasH;
        foreach (int c in components)
        {
            if (c <= 0 || c >= CwrFile.PartCount || _cwr.PartBase(c) < 0) continue;
            int fr = _cwr.PartFrame(c, facing, block, group);
            if (fr < 0) continue;
            var (w, h) = _cwr.CanvasFor(fr);
            cw = System.Math.Max(cw, w);
            ch = System.Math.Max(ch, h);
        }

        var canvas = Image.CreateEmpty(cw, ch, false, Image.Format.Rgba8);
        canvas.Fill(new Color(0, 0, 0, 0));
        foreach (int c in components)
        {
            if (c <= 0 || c >= CwrFile.PartCount) continue;
            if (_cwr.PartBase(c) < 0) continue;
            int fr = _cwr.PartFrame(c, facing, block, group);
            if (fr < 0) continue;
            var layer = _cwr.FacingImage(fr, _pal, cw, ch);
            canvas.BlendRect(layer, new Rect2I(0, 0, cw, ch), Vector2I.Zero);
        }
        return canvas;
    }

    /// <summary>Wieviele Hangklassen es gibt — fünf, die Länge der Tabelle
    /// @0x4fa4d8. Klasse 0 ist flacher Boden.</summary>
    public const int SlopeClasses = ExeTables.MountSlopes;

    /// <summary>Der Bild-BLOCK zu einer Hangklasse. Die Tabelle führt
    /// BILDVERSÄTZE (0, 16, 32, 8, 24); ein Block ist acht Bilder, also
    /// Block = Versatz/8 — die Klassen 0..4 greifen damit auf die Blöcke
    /// 0, 2, 4, 1, 3. Block 5 kommt in der Tabelle nicht vor und wird nicht
    /// geschrieben; wofür er da ist, ist offen.</summary>
    private int SlopeBlockOf(int k)
    {
        if (k <= 0 || k >= _slopeBlocks.Length) return 0;
        return _slopeBlocks[k] / CwrFile.Facings;
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
    // READ OUT OF THE GAME, 03.08.2026 — this replaces the 45 % rule 0.3.2
    // shipped, which was measured from the art because the table had not been
    // found yet.
    //
    // The ordinary unit is drawn by case 0 of the draw list's switch
    // (@0x429900). The hull goes down at the list entry's own x/y, and then
    // @0x429CCB..0x429D1B moves the pen before the turret:
    //     x += (t[comp][0].x + t[comp][k].x) / 2
    //     y += (t[comp][0].y + t[comp][k].y) / 2      t = ExeTables.TurretMount
    // comp is the HULL component (entity +0x0b) and k the tile's flag byte,
    // taken as 0 above 4 — so on flat ground the offset is row 0 of the table.
    // See ExeTables.TurretMountTable for the addresses.
    //
    // The composed pictures below are built for FLAT ground (k = 0), which is
    // what a picture without a map under it can mean; the map layer applies the
    // slope entry per unit.

    private readonly Dictionary<int, (int X, int Y)[]> _mount = new();
    /// <summary>Filled from the executable in <see cref="Mount"/>; the values
    /// here are what the January 1998 build holds, kept only so the file has a
    /// sane line when the table cannot be located.</summary>
    private int[] _slopeBlocks = { 0, 16, 32, 8, 24 };

    /// <summary>The five slope offsets for a unit type's hull, or null when the
    /// executable's table could not be read.</summary>
    /// <summary>Ships are drawn by their own case of the switch (kind 4, whose
    /// error path says "Wrong chassis of ship"): the turret goes to
    /// <c>x + 0x10</c>, <c>y + mount - 0x0c</c> with these three mounts, and no
    /// slope enters it (@0x42ADB7..0x42AE31). Chassis 73, 100 and 101 fall into
    /// that error path in the original — it prints and then uses whatever was
    /// on the stack — so there is no number to copy and they get none.</summary>
    private static readonly Dictionary<int, int> ShipMount = new() { { 70, 15 }, { 71, 2 }, { 72, 12 } };

    /// <summary>The Läufer's chassis component, and the lift it gets instead of
    /// the mount table.
    ///
    /// ⚠ FOUND 2026-08-06, and it explains a complaint that survived three
    /// releases. The draw code tests the chassis against 0x11 twice: @0x429af4
    /// denies the walker a slope block, and @0x42a025 jumps clean past the
    /// mount-table block at @0x42a08e to @0x42a0e8, whose first instruction is
    ///     sub bp, 0x1b
    /// — a flat 27 px lift with no x offset at all. The table row the remake
    /// used instead says (0,-8), and the walker's hull is 40 px tall where
    /// every other land chassis is 20..28, so the turret sat at its feet.</summary>
    public const int WalkerChassis = 0x11;
    public const int WalkerLift = 0x1b;

    private (int X, int Y)[]? Mount(int unitType)
    {
        if (_mount.TryGetValue(unitType, out var m)) return m;
        var t = _exe;
        if (t == null || !t.TurretMountFound) return null;
        int comp = ComponentOf(unitType);
        if (comp == WalkerChassis)
        {
            var walk = new (int X, int Y)[ExeTables.MountSlopes];
            for (int k = 0; k < walk.Length; k++) walk[k] = (0, -WalkerLift);
            _mount[unitType] = walk;
            return walk;
        }
        if (ShipMount.TryGetValue(comp, out int sm))
        {
            var ship = new (int X, int Y)[ExeTables.MountSlopes];
            for (int k = 0; k < ship.Length; k++) ship[k] = (0x10, sm - 0x0c);
            _mount[unitType] = ship;
            return ship;
        }
        if (comp < 0 || comp >= ExeTables.MountRows) return null;
        var all = t.TurretMount();
        var row = new (int X, int Y)[ExeTables.MountSlopes];
        for (int k = 0; k < row.Length; k++) row[k] = all[comp, k];
        _mount[unitType] = row;
        _slopeBlocks = t.SlopeBlocks();
        return row;
    }

    /// <summary>The offset the turret is drawn at on flat ground.</summary>
    private Vector2I FlatOffset(int unitType)
    {
        var m = Mount(unitType);
        return m == null ? Vector2I.Zero : new Vector2I(m[0].X, m[0].Y);
    }

    /// <summary>Hull with the turret at the offset the game gives it on flat
    /// ground — the same offset the map layer applies when it draws the two
    /// separately, so the composed picture and the live unit agree.</summary>
    private static Image Compose(Image hull, Image? turret, Vector2I offset)
    {
        // as wide and as tall as the hull, the turret and the turret's offset
        // together need — a 64x56 box would clip a ship back off again
        int cw = System.Math.Max(CwrFile.CanvasW, hull.GetWidth());
        int ch = System.Math.Max(CwrFile.CanvasH, hull.GetHeight());
        if (turret != null)
        {
            cw = System.Math.Max(cw, turret.GetWidth() + System.Math.Max(0, offset.X));
            ch = System.Math.Max(ch, turret.GetHeight() + System.Math.Max(0, offset.Y));
        }

        var canvas = Image.CreateEmpty(cw, ch, false, Image.Format.Rgba8);
        canvas.Fill(new Color(0, 0, 0, 0));
        canvas.BlendRect(hull, new Rect2I(0, 0, hull.GetWidth(), hull.GetHeight()), Vector2I.Zero);
        if (turret != null) BlendAt(canvas, turret, offset);
        return canvas;
    }

    /// <summary>Blend a layer at an offset that may be NEGATIVE, clipping the
    /// SOURCE rather than the destination.
    ///
    /// ⚠ Found 2026-08-06 while the walker's 27 px lift went in. Handing
    /// <c>BlendRect</c> a negative destination does not clip the way the Python
    /// reference's <c>Image.paste</c> does: at (0,-8) the two agree to the
    /// pixel, at (0,-27) the C# side lost 186 of the turret's 257 pixels. The
    /// small offsets of the mount table hid it for as long as they were all the
    /// remake used.</summary>
    private static void BlendAt(Image canvas, Image layer, Vector2I at)
    {
        int sx = System.Math.Max(0, -at.X), sy = System.Math.Max(0, -at.Y);
        int w = layer.GetWidth() - sx, h = layer.GetHeight() - sy;
        if (w <= 0 || h <= 0) return;
        canvas.BlendRect(layer, new Rect2I(sx, sy, w, h),
                         new Vector2I(System.Math.Max(0, at.X), System.Math.Max(0, at.Y)));
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
