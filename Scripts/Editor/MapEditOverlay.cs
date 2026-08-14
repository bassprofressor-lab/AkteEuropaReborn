namespace AkteEuropaReborn.Editor;

using System;
using System.Collections.Generic;
using Godot;
using AkteEuropaReborn.Import;
using AkteEuropaReborn.Rendering;

/// <summary>
/// DER BEARBEITUNGSMODUS — mit der Maus auf der Karte setzen: Gebaeude
/// (militaerisch wie zivil), Gegenstaende und Gleise.
///
/// <para><b>Wie es zusammenhaengt.</b> Der Editor haelt die erzeugte Karte als
/// <see cref="CwmFile"/> im Speicher (<see cref="MapEditSession"/>), der
/// Kartenbetrachter zeigt die daraus geschriebenen Dateien. Ein Klick geht
/// beide Wege: er traegt den Satz in die <c>CwmFile</c> ein — die ist die
/// Wahrheit, die gespeichert wird — und, wo es ohne Neubacken geht, gleich in
/// die lebende Liste des Zeichners, damit man das Ergebnis sofort sieht.</para>
///
/// <para><b>Was sofort dasteht und was erst nach dem Speichern.</b> Ein Gebaeude
/// steht sofort: es ist im Kartenbild gar nicht enthalten, der Backofen laesst
/// seine Zellen aus und der Zeichner malt es aus dem Muster. Ein Gegenstand und
/// ein Gleis sind dagegen BILD und in die Karte gebacken; fuer sie zeigt dieser
/// Schirm eine Vorschau und backt beim Speichern neu. Das ist eine bewusste
/// Entscheidung gegen eine zweite Zeichenroutine neben dem Backofen — die
/// koennte mit ihm auseinanderlaufen, und dann sieht der Editor etwas anderes
/// als das Spiel. Die Vorschau sagt darum ausdruecklich, dass sie eine ist.</para>
///
/// <para><b>Die Bauplatzfrage wird gestellt, nicht uebergangen.</b> Ein Gebaeude
/// kommt nur auf eine Flaeche, die <c>can_build_here</c> @0x4203C0 auf JEDER
/// Musterzelle erfuellt — imap frei, Hangbyte 0, alle vier Ecken sec2 ≥ 2. Es
/// sind dieselben zwei Fragen, die <see cref="MapDeposits"/> und
/// <see cref="MapNeutrals"/> stellen, und sie werden von dort geholt statt
/// nachgebaut. Wo es nicht geht, sagt der Schirm WARUM, statt den Klick still zu
/// schlucken.</para>
///
/// <para>⚠ <b>UNSERE ZUTAT von A bis Z.</b> Das Spiel von 1997 hat keinen
/// Karteneditor — es gibt in beiden GAME.EXE keinen Schalter, keinen Debugstring
/// und keine Menuezeile dafuer. Gemessen ist hier nur, WAS gesetzt wird (die
/// Gebaeudearten des Kachelsatzes, die Gleisbilder aus sec22, die Objektcodes
/// der Karte); dass man es von Hand setzen kann, ist unser Zusatz.</para>
/// </summary>
public partial class MapEditOverlay : Node2D
{
    /// <summary>Was der Pinsel gerade setzt.</summary>
    public enum Brush { Gebaeude, Gegenstand, Gleis }

    private MapEntityLayer _layer = null!;
    private CwmFile _map = null!;

    private Brush _brush = Brush.Gebaeude;
    private int _typ = 1, _owner = 0, _line;
    private int _propAt;
    private readonly List<int> _types = new();
    private readonly List<int> _props = new();
    private Vector2I? _hover;
    private string _say = "";

    // Was dieser Lauf gesetzt hat — nur fuer die Vorschau und den Zaehler.
    private readonly List<(int Col, int Row, Brush What)> _mine = new();
    private int _nextSlot;

    private CanvasLayer _ui = null!;
    private Label _head = null!, _foot = null!;

    /// <summary>Die Eigner zur Auswahl. 0..7 sind die Spielerplaetze, 11 ist
    /// herrenlos (der Preis einer Eroberungskarte, siehe
    /// <see cref="MapNeutrals"/>), 255 ist Kulisse ohne Eigner.</summary>
    private static readonly int[] Owners = { 0, 1, 2, 3, 4, 5, 6, 7, 11, 255 };

    /// <summary>Godot braucht fuer eine <c>partial</c>-Node einen parameterlosen
    /// Erzeuger; der hier ist der, den <see cref="MapEditSession"/> benutzt.</summary>
    public MapEditOverlay() { }

    public MapEditOverlay(MapEntityLayer layer)
    {
        _layer = layer;
        _map = MapEditSession.Map!;
    }

    public override void _Ready()
    {
        _types.AddRange(_layer.EditorBuildingTypes());
        if (_types.Count > 0) _typ = _types[0];
        CollectProps();
        _nextSlot = _layer.EditorBuildingCount;
        BuildUi();
        Refresh();
        Harness();
    }

    /// <summary>
    /// <c>--map-edit-check</c> — DER PRUEFSTAND DES BEARBEITUNGSMODUS.
    ///
    /// <para>Ein Klick laesst sich in einem Prueflauf nicht ausloesen, und
    /// »der Schirm sieht richtig aus« ist bei einem Editor kein Beleg (Regel 11:
    /// ein Prueflauf muss die MECHANIK ausueben, nicht ihr Ergebnis
    /// hinschreiben). Dieser Lauf ruft darum genau die Wege, die die Maus ruft —
    /// <see cref="Put"/> mit allen drei Pinseln — und laesst danach speichern.
    /// Er zaehlt, was DANACH in der Karte steht, nicht was er vorhatte.</para>
    ///
    /// <para>⚠ Er sucht sich seine Stellen selbst, indem er die Karte absucht,
    /// statt feste Zellen zu nehmen: eine erzeugte Karte hat bei jedem Samen ein
    /// anderes Gelaende, und ein Prueflauf, der auf einer bestimmten Zelle
    /// besteht, prueft am Ende nur den Samen.</para>
    /// </summary>
    private void Harness()
    {
        bool want = false;
        foreach (string a in Core.CommandLine.Args) if (a == "--map-edit-check") want = true;
        if (!want) return;

        int before = CountBuildings();
        GD.Print($"map-edit-check: Start — {before} Gebaeude, {CountRail()} Gleiszellen in der Karte");

        // 1. ein Gebaeude auf die erste Stelle, die can_build_here erfuellt
        int put = 0;
        for (int r = 2; r < _map.Height - 8 && put == 0; r++)
            for (int c = 2; c < _map.Width - 12 && put == 0; c++)
            {
                _say = "";
                PutBuilding(new Vector2I(c, r));
                if (_say.StartsWith("Typ ")) { put = 1; GD.Print($"map-edit-check: Gebaeude -> {_say}"); }
            }
        if (put == 0) GD.Print($"map-edit-check: ⚠ kein Bauplatz gefunden — zuletzt: {_say}");

        // 2. eine Gleisstrecke von sechs Zellen, waagerecht
        _brush = Brush.Gleis;
        int rc = 0;
        for (int k = 0; k < 6; k++)
        {
            int c = _map.Width / 2 + k, r = _map.Height / 2;
            PutRail(new Vector2I(c, r));
            if (MapFactory.HasRail(_map, c, r)) rc++;
        }
        GD.Print($"map-edit-check: Gleis -> {rc} von 6 Zellen liegen, Bilder " +
                 RailFramesLine(_map.Width / 2, _map.Height / 2, 6));

        // 3. drei Gegenstaende
        _brush = Brush.Gegenstand;
        int props = 0;
        for (int k = 0; k < 3 && _props.Count > 0; k++)
        {
            _propAt = k % _props.Count;
            int c = _map.Width / 2 + k, r = _map.Height / 2 + 3;
            int want2 = _props[_propAt];
            PutProp(new Vector2I(c, r));
            if (_map.CodeAt(c, r) == want2) props++;
        }
        GD.Print($"map-edit-check: Gegenstaende -> {props} von 3 stehen im Raster " +
                 $"({_props.Count} Arten zur Wahl)");

        // 3b. eine RAMPE: eine Zelle mit Gelaendebyte 1..4 suchen und sehen, ob
        // das abgeleitete Bild das gemessene Rampenbild ist. ⚠ Gesucht statt
        // gesetzt — welche Zelle eine Stufe traegt, entscheidet das Gelaende.
        _brush = Brush.Gleis;
        int rampen = 0, rampFalsch = 0;
        for (int r = 1; r < _map.Height - 1 && rampen < 4; r++)
            for (int c = 1; c < _map.Width - 1 && rampen < 4; c++)
            {
                int fl = _map.FlagAt(c, r);
                if (fl is < 1 or > 4) continue;
                if (MapFactory.HasRail(_map, c, r)) continue;
                // Waagerecht fuer Byte 1/3, senkrecht fuer 2/4 — sonst waere die
                // Form gar nicht die, fuer die das Bild gemessen ist.
                bool waag = fl is 1 or 3;
                var cells = waag
                    ? new[] { (c - 1, r), (c, r), (c + 1, r) }
                    : new[] { (c, r - 1), (c, r), (c, r + 1) };
                foreach (var (cc, rr) in cells) PutRail(new Vector2I(cc, rr));
                int got = FrameAt(c, r);
                int soll = fl switch { 3 => 6, 1 => 7, 4 => 8, _ => 9 };
                if (got == soll) rampen++;
                else { rampFalsch++; GD.Print($"map-edit-check: ⚠ Rampe ({c},{r}) Byte {fl}: Bild {got}, erwartet {soll}"); }
            }
        GD.Print($"map-edit-check: Rampen -> {rampen} richtig, {rampFalsch} falsch " +
                 "(gemessen: L-R Byte3=6 111/111, Byte1=7 148/150, T-B Byte4=8 136/136, Byte2=9 94/94)");

        // 3c. das eben gesetzte Gebaeude wieder WEGNEHMEN
        _brush = Brush.Gebaeude;
        int nachBau = CountBuildings();
        if (put == 1)
        {
            // dieselbe Zelle wie beim Setzen: der Eintrag steht in _mine
            var b = _mine.Find(x => x.What == Brush.Gebaeude);
            Take(new Vector2I(b.Col, b.Row));
            GD.Print($"map-edit-check: Wegnehmen -> {_say}; Gebaeude {nachBau} -> {CountBuildings()} " +
                     $"(vor dem Setzen {before})");
            // ⚠ Die zweite Haelfte, und sie ist die wichtigere: ist die
            // GRUNDFLAECHE wieder frei? Nur den Satz zu leeren waere der
            // schlimmere Fehler — das Gebaeude waere weg und sein Platz auf
            // ewig unpassierbar, ohne dass man saehe warum.
            var imap2 = _map.Sec(6); var zone2 = _map.Sec(2);
            int frei = 0, ges = 0;
            foreach (var (dx, dy) in _layer.EditorFootCells(_typ))
            {
                ges++;
                if (imap2 != null && zone2 != null &&
                    MapDeposits.Free(_map, imap2, b.Col + dx, b.Row + dy) &&
                    MapDeposits.Corners(zone2, _map.Width, _map.Height, b.Col + dx, b.Row + dy))
                    frei++;
            }
            GD.Print($"map-edit-check: Grundflaeche nach dem Wegnehmen: {frei} von {ges} " +
                     "Zellen wieder BAUBAR (muss ges sein — sonst bleibt der Platz gesperrt)");
            // und gleich wieder hinstellen, damit die gespeicherte Karte einen
            // sichtbaren Zuwachs hat und der naechste Zaehler etwas aussagt
            PutBuilding(new Vector2I(b.Col, b.Row));
        }

        // 4. speichern und nachzaehlen — was auf der Platte steht, zaehlt
        string? made = MapEditSession.Save(s => GD.Print("map-edit-check:   " + s));
        GD.Print($"map-edit-check: gespeichert als {made ?? "(nichts)"}; " +
                 $"jetzt {CountBuildings()} Gebaeude (vorher {before}), " +
                 $"{CountRail()} Gleiszellen");
        GD.Print("map-edit-check: fertig");
    }

    /// <summary>Das Bild, das jetzt in sec22 auf dieser Zelle steht, oder −1.</summary>
    private int FrameAt(int c, int r)
    {
        var s = _map.Sec(22);
        if (s == null) return -1;
        for (int i = 0; i < MapFactory.RailSlots && i * MapFactory.RailStride + 4 < s.Length; i++)
        {
            int o = i * MapFactory.RailStride;
            if (s[o + 2] != MapFactory.RailEmpty && s[o] == c && s[o + 1] == r) return s[o + 2];
        }
        return -1;
    }

    private string RailFramesLine(int c0, int r, int n)
    {
        var s = _map.Sec(22);
        if (s == null) return "(kein sec22)";
        var outp = new List<string>();
        for (int k = 0; k < n; k++)
        {
            int c = c0 + k, frame = -1;
            for (int i = 0; i < MapFactory.RailSlots && i * MapFactory.RailStride + 4 < s.Length; i++)
            {
                int o = i * MapFactory.RailStride;
                if (s[o + 2] != MapFactory.RailEmpty && s[o] == c && s[o + 1] == r) { frame = s[o + 2]; break; }
            }
            outp.Add(frame < 0 ? "-" : frame.ToString());
        }
        return string.Join(",", outp);
    }

    private int CountBuildings()
    {
        var s = _map.Sec(3);
        if (s == null) return 0;
        int n = 0;
        for (int i = 0; i * CwmData.BuildingStride + 0x18 < s.Length && i < 255; i++)
            if (s[i * CwmData.BuildingStride + 0x18] == 1) n++;
        return n;
    }

    private int CountRail()
    {
        var s = _map.Sec(22);
        if (s == null) return 0;
        int n = 0;
        for (int i = 0; i < MapFactory.RailSlots && i * MapFactory.RailStride + 2 < s.Length; i++)
            if (s[i * MapFactory.RailStride + 2] != MapFactory.RailEmpty) n++;
        return n;
    }

    /// <summary>
    /// DIE GEGENSTAENDE, DIE DIESE KARTE SELBST BENUTZT.
    ///
    /// <para>Angeboten wird nicht »alle Objektcodes des Kachelsatzes«, sondern
    /// die, die im Raster dieser Karte vorkommen — also die, von denen belegt
    /// ist, dass sie als Bewuchs taugen. ⚠ <b>Gebaeudekacheln bleiben
    /// draussen</b>, und das ist derselbe Befund vom 14.08.2026, der die
    /// »zerstueckelten Gebaeude« erklaert hat: eine Gebaeudekachel ist Teil
    /// eines Gebaeudes und kein Gegenstand. Sie hier anzubieten hiesse, den
    /// behobenen Fehler als Werkzeug wieder einzufuehren.</para>
    /// </summary>
    private void CollectProps()
    {
        var bTiles = new HashSet<int>();
        var cwp = MapEditSession.Cwp;
        if (cwp != null && cwp.HasBuildings)
            for (int t = 0; t < CwpFile.BuildingTypeCount; t++)
            {
                var bt = cwp.GetBuildingType(t);
                if (bt.IsEmpty) continue;
                for (int k = 0; k < bt.PatternCount; k++)
                    for (int x = 0; x < CwpFile.PatternWidth; x++)
                        for (int y = 0; y < CwpFile.PatternHeight; y++)
                        {
                            int tile = cwp.PatternTile(bt.FirstPattern + k, x, y);
                            if (tile != 0) bTiles.Add(tile);
                        }
            }

        var seen = new Dictionary<int, int>();
        for (int c = 0; c < _map.Width; c++)
            for (int r = 0; r < _map.Height; r++)
            {
                int code = _map.CodeAt(c, r);
                if (code < CwpFile.ObjectCodeBase) continue;
                if (bTiles.Contains(code - CwpFile.ObjectCodeBase)) continue;
                seen[code] = seen.TryGetValue(code, out int n) ? n + 1 : 1;
            }
        var all = new List<KeyValuePair<int, int>>(seen);
        all.Sort((a, b) => b.Value != a.Value ? b.Value.CompareTo(a.Value) : a.Key.CompareTo(b.Key));
        foreach (var kv in all) _props.Add(kv.Key);
    }

    // ---- die Bedienleiste ---------------------------------------------------

    private void BuildUi()
    {
        _ui = new CanvasLayer { Layer = 90 };
        AddChild(_ui);
        var panel = new PanelContainer
        {
            AnchorLeft = 1, AnchorRight = 1, AnchorTop = 0, AnchorBottom = 0,
            OffsetLeft = -330, OffsetTop = 110, OffsetRight = -12, OffsetBottom = 250,
            GrowHorizontal = Control.GrowDirection.Begin,
        };
        _ui.AddChild(panel);
        var box = new VBoxContainer();
        panel.AddChild(box);
        _head = new Label { Text = "EDITOR" };
        _foot = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        box.AddChild(_head);
        box.AddChild(_foot);
    }

    private void Refresh()
    {
        string what = _brush switch
        {
            Brush.Gebaeude => $"Gebaeude Typ {_typ} ({OwnerName(_owner)})",
            Brush.Gegenstand => _props.Count > 0
                ? $"Gegenstand {_props[_propAt]} ({_propAt + 1}/{_props.Count})"
                : "Gegenstand — diese Karte hat keine",
            _ => $"Gleis, Linie {_line}",
        };
        _head.Text = $"EDITOR {MapEditSession.Name}  —  {what}" +
                     (MapEditSession.Dirty ? "  *" : "");
        _foot.Text =
            "1/2/3 = Gebaeude / Gegenstand / Gleis   ·   +/- = Art waehlen\n" +
            "0..7,N,K = Eigner   ·   LINKS setzen, RECHTS wegnehmen\n" +
            "F5 = speichern und neu laden   ·   " + _mine.Count + " gesetzt\n" +
            _say;
    }

    private static string OwnerName(int o) => o switch
    {
        11 => "herrenlos",
        255 => "Kulisse",
        _ => $"Spieler {o + 1}",
    };

    // ---- Eingabe ------------------------------------------------------------

    public override void _UnhandledInput(InputEvent e)
    {
        if (!MapEditSession.Active) return;

        if (e is InputEventMouseMotion)
        {
            var cell = _layer.CellAt(GetLocalMousePosition());
            if (cell != _hover) { _hover = cell; QueueRedraw(); }
            return;
        }

        if (e is InputEventMouseButton { Pressed: true } mb)
        {
            var cell = _layer.CellAt(GetLocalMousePosition());
            if (cell == null) return;
            if (mb.ButtonIndex == MouseButton.Left) { Put(cell.Value); GetViewport().SetInputAsHandled(); }
            else if (mb.ButtonIndex == MouseButton.Right) { Take(cell.Value); GetViewport().SetInputAsHandled(); }
            return;
        }

        if (e is not InputEventKey { Pressed: true, Echo: false } k) return;
        bool used = true;
        switch (k.Keycode)
        {
            case Key.Key1: _brush = Brush.Gebaeude; break;
            case Key.Key2: _brush = Brush.Gegenstand; break;
            case Key.Key3: _brush = Brush.Gleis; break;
            case Key.Equal or Key.Plus or Key.KpAdd: Step(+1); break;
            case Key.Minus or Key.KpSubtract: Step(-1); break;
            case Key.N: _owner = 11; break;
            case Key.K: _owner = 255; break;
            case Key.F5: SaveAndReload(); return;
            default:
                if (k.Keycode >= Key.Key0 && k.Keycode <= Key.Key7 && _brush != Brush.Gebaeude)
                    used = false;
                else if (k.Keycode >= Key.Key0 && k.Keycode <= Key.Key7)
                    _owner = (int)(k.Keycode - Key.Key0);
                else used = false;
                break;
        }
        if (!used) return;
        _say = "";
        Refresh();
        QueueRedraw();
        GetViewport().SetInputAsHandled();
    }

    private void Step(int d)
    {
        switch (_brush)
        {
            case Brush.Gebaeude when _types.Count > 0:
                int i = _types.IndexOf(_typ);
                _typ = _types[((i < 0 ? 0 : i) + d + _types.Count) % _types.Count];
                break;
            case Brush.Gegenstand when _props.Count > 0:
                _propAt = (_propAt + d + _props.Count) % _props.Count;
                break;
            case Brush.Gleis:
                _line = Math.Max(0, _line + d);
                break;
        }
    }

    // ---- setzen und wegnehmen ----------------------------------------------

    private void Put(Vector2I cell)
    {
        switch (_brush)
        {
            case Brush.Gebaeude: PutBuilding(cell); break;
            case Brush.Gegenstand: PutProp(cell); break;
            case Brush.Gleis: PutRail(cell); break;
        }
        Refresh();
        QueueRedraw();
    }

    private void PutBuilding(Vector2I cell)
    {
        var cwp = MapEditSession.Cwp;
        if (cwp == null) { _say = "kein Kachelsatz — kein Gebaeude"; return; }
        var bt = cwp.GetBuildingType(_typ);
        if (bt.IsEmpty) { _say = $"Typ {_typ} gibt es in diesem Kachelsatz nicht"; return; }

        var foot = _layer.EditorFootCells(_typ);
        var (fw, fh) = _layer.EditorFootprint(_typ);
        if (cell.X + fw > _map.Width || cell.Y + fh > _map.Height)
        { _say = "das Gebaeude ragt ueber den Kartenrand"; return; }

        // can_build_here @0x4203C0, Zelle fuer Zelle — dieselbe Pruefung wie bei
        // den Vorkommen und den neutralen Gebaeuden, von dort geholt.
        var imap = _map.Sec(6);
        var zone = _map.Sec(2);
        if (imap == null || zone == null) { _say = "die Karte hat kein sec2/sec6"; return; }
        foreach (var (dx, dy) in foot)
        {
            int c = cell.X + dx, r = cell.Y + dy;
            if (!MapDeposits.Free(_map, imap, c, r))
            { _say = $"({c},{r}) ist belegt oder ein Hang — kein Bauplatz"; return; }
            if (!MapDeposits.Corners(zone, _map.Width, _map.Height, c, r))
            { _say = $"({c},{r}): sec2 unter 2 — kein Bauplatz"; return; }
        }

        int hp = MapNeutrals.HpOf(_typ);
        var doors = MapNeutrals.DoorsOf(_typ);
        if (!MapFactory.PutBuilding(_map, _nextSlot, _typ, _owner, 0, cell.X, cell.Y,
                                    fw, fh, hp, "", doors))
        { _say = "kein Gebaeudeplatz mehr frei (255)"; return; }

        _layer.EditorAddBuilding(_nextSlot, _typ, _owner, cell.X, cell.Y, fw, fh, hp);
        _mine.Add((cell.X, cell.Y, Brush.Gebaeude));
        _nextSlot++;
        MapEditSession.Dirty = true;
        _say = $"Typ {_typ} auf ({cell.X},{cell.Y}), {fw}x{fh}, {OwnerName(_owner)}";
    }

    private void PutProp(Vector2I cell)
    {
        if (_props.Count == 0) { _say = "diese Karte fuehrt keine Gegenstaende"; return; }
        int code = _props[_propAt];
        int elev = _map.ElevAt(cell.X, cell.Y);
        // ⚠ Die Gelaendeklasse bleibt, was sie war. Ein Gegenstand ist Bewuchs;
        // ihn zum Hindernis zu erklaeren waere eine Entscheidung ueber das
        // Spiel, keine ueber das Bild — und sie steht hier nicht an.
        MapFactory.Paint(_map, cell.X, cell.Y, code, elev, GroundAt(cell));
        _mine.Add((cell.X, cell.Y, Brush.Gegenstand));
        MapEditSession.Dirty = true;
        _say = $"Gegenstand {code} auf ({cell.X},{cell.Y}) — sichtbar nach F5";
    }

    private MapFactory.Ground GroundAt(Vector2I cell)
    {
        var zone = _map.Sec(2);
        int z = zone == null ? 2 : MapDeposits.Zone(zone, _map.Width, _map.Height, cell.X, cell.Y);
        return z switch
        {
            MapFactory.ZoneImpassable => MapFactory.Ground.Water,
            MapFactory.ZoneShore => MapFactory.Ground.Rough,
            _ => MapFactory.Ground.Free,
        };
    }

    /// <summary>Ein Gleisstueck legen — und die vier Nachbarn gleich mit
    /// nachziehen, denn ihr Bild haengt daran, ob hier jetzt Gleis liegt.
    /// Ohne das Nachziehen bliebe ein gerades Stueck gerade, wo eine Ecke
    /// entstanden ist.</summary>
    private void PutRail(Vector2I cell)
    {
        if (MapFactory.PutRail(_map, cell.X, cell.Y, RailFrameAt(cell.X, cell.Y, true), _line) < 0)
        { _say = "kein Gleisplatz mehr frei (3000)"; return; }
        _mine.Add((cell.X, cell.Y, Brush.Gleis));
        RefreshRailAround(cell);
        MapEditSession.Dirty = true;
        _say = $"Gleis auf ({cell.X},{cell.Y}), Linie {_line} — sichtbar nach F5";
    }

    /// <summary>Das Bild einer Gleiszelle aus ihren Nachbarn UND ihrem
    /// Gelaendebyte — letzteres entscheidet ueber die vier Rampenbilder, und
    /// zwar gemessen in beide Richtungen (siehe
    /// <see cref="MapFactory.RailFrame"/>).</summary>
    private int RailFrameAt(int c, int r, bool self)
        => MapFactory.RailFrame(
            MapFactory.HasRail(_map, c - 1, r), MapFactory.HasRail(_map, c + 1, r),
            MapFactory.HasRail(_map, c, r - 1), MapFactory.HasRail(_map, c, r + 1),
            _map.FlagAt(c, r));

    private void RefreshRailAround(Vector2I cell)
    {
        foreach (var (dc, dr) in new[] { (0, 0), (1, 0), (-1, 0), (0, 1), (0, -1) })
        {
            int c = cell.X + dc, r = cell.Y + dr;
            if (!MapFactory.HasRail(_map, c, r)) continue;
            MapFactory.PutRail(_map, c, r, RailFrameAt(c, r, false), _line);
        }
    }

    private void Take(Vector2I cell)
    {
        switch (_brush)
        {
            case Brush.Gebaeude:
                int i = _layer.EditorBuildingAt(cell);
                if (i < 0) { _say = "dort steht kein Gebaeude"; break; }
                int slot = _layer.EditorSlotOf(i);
                // Erst die Karte, dann das Bild: schlaegt das Leeren des Satzes
                // fehl, soll das Gebaeude sichtbar bleiben, statt aus der
                // Anzeige zu verschwinden und in der Datei stehenzubleiben.
                if (!MapFactory.ClearBuilding(_map, slot))
                { _say = $"Platz {slot} traegt keinen gebauten Satz — nichts weggenommen"; break; }
                _layer.EditorRemoveBuilding(slot);
                _mine.RemoveAll(x => x.What == Brush.Gebaeude && x.Col == cell.X && x.Row == cell.Y);
                MapEditSession.Dirty = true;
                _say = $"Gebaeude auf Platz {slot} weg — Grundflaeche wieder frei";
                break;
            case Brush.Gleis:
                if (MapFactory.PutRail(_map, cell.X, cell.Y, -1, _line) < 0)
                { _say = "dort liegt kein Gleis"; break; }
                RefreshRailAround(cell);
                MapEditSession.Dirty = true;
                _say = $"Gleis von ({cell.X},{cell.Y}) weg";
                break;
            default:
                _say = "Gegenstand wegnehmen: dafuer den Boden neu malen";
                break;
        }
        Refresh();
        QueueRedraw();
    }

    // ---- speichern ----------------------------------------------------------

    /// <summary>Speichern heisst: die <c>CwmFile</c> mit dem Weg des Generators
    /// zurueckschreiben — samt Neubacken des Bildes — und die Szene neu laden,
    /// damit Gegenstaende und Gleise aus dem frischen Bild kommen. Der Umweg
    /// ueber die Szene spart eine zweite Ladeschiene im Kartenbetrachter.</summary>
    private void SaveAndReload()
    {
        _say = "speichere …";
        Refresh();
        string? made = MapEditSession.Save(s => GD.Print("map-editor: " + s));
        if (made == null) { _say = "Speichern ging nicht"; Refresh(); return; }
        UI.SkirmishSetup.ViewMap = made;
        MapEditSession.Active = true;
        GetTree().ReloadCurrentScene();
    }

    // ---- Vorschau -----------------------------------------------------------

    public override void _Draw()
    {
        if (_hover is { } h)
        {
            var (fw, fh) = _brush == Brush.Gebaeude ? _layer.EditorFootprint(_typ) : (1, 1);
            var col = _brush switch
            {
                Brush.Gebaeude => new Color(0.3f, 1f, 0.4f, 0.9f),
                Brush.Gegenstand => new Color(1f, 0.85f, 0.2f, 0.9f),
                _ => new Color(0.4f, 0.8f, 1f, 0.9f),
            };
            // Zelle fuer Zelle gefuellt statt als ein Rechteck: die Kacheln
            // liegen je nach Hoehe verschieden hoch, ein einziges Rechteck
            // laege bei jeder Stufe daneben. Die Fuellung ist duenn, damit man
            // den Boden darunter noch sieht.
            var fill = new Color(col.R, col.G, col.B, 0.22f);
            for (int dy = 0; dy < fh; dy++)
                for (int dx = 0; dx < fw; dx++)
                {
                    if (h.X + dx >= _map.Width || h.Y + dy >= _map.Height) continue;
                    var p = _layer.EditorCellTopLeft(h.X + dx, h.Y + dy);
                    var rect = new Rect2(p, MapBaker.TileW, MapBaker.TileH);
                    DrawRect(rect, fill);
                    // nur die AUSSENkanten nachziehen, sonst wird der Grundriss
                    // zum Streifenmuster
                    if (dx == 0) DrawLine(p, p + new Vector2(0, MapBaker.TileH), col, 2f);
                    if (dx == fw - 1) DrawLine(p + new Vector2(MapBaker.TileW, 0),
                                               p + new Vector2(MapBaker.TileW, MapBaker.TileH), col, 2f);
                    if (dy == 0) DrawLine(p, p + new Vector2(MapBaker.TileW, 0), col, 2f);
                    if (dy == fh - 1) DrawLine(p + new Vector2(0, MapBaker.TileH),
                                               p + new Vector2(MapBaker.TileW, MapBaker.TileH), col, 2f);
                }
        }

        // Was dieser Lauf gesetzt hat, aber noch nicht im Bild steht.
        foreach (var (c, r, what) in _mine)
        {
            if (what == Brush.Gebaeude) continue;          // das steht schon da
            var p = _layer.EditorCellTopLeft(c, r);
            var col = what == Brush.Gleis
                ? new Color(0.4f, 0.8f, 1f, 0.85f)
                : new Color(1f, 0.85f, 0.2f, 0.85f);
            DrawRect(new Rect2(p + new Vector2(6, 4), MapBaker.TileW - 12, MapBaker.TileH - 8), col);
        }
    }
}
