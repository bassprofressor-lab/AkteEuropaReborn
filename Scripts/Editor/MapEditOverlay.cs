namespace AkteEuropaReborn.Editor;

using System;
using System.Collections.Generic;
using Godot;
using AkteEuropaReborn.Import;
using AkteEuropaReborn.Rendering;

/// <summary>
/// DER BEARBEITUNGSMODUS — mit der Maus auf der Karte setzen: Gebaeude
/// (militaerisch wie zivil), Gegenstaende, Gleise, Einheiten, und das GELAENDE
/// selbst (Klasse und Hoehe).
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
/// <para><b>Das Gelaende ist der Sonderfall unter den Pinseln</b>, und zwar
/// weil eine Zelle nicht fuer sich steht: ihr Kachelcode haengt am Hangbyte,
/// an der Wassermaske der vier Nachbarn und am Wasserabstand im Umkreis von
/// drei. Ein Strich zieht darum einen ganzen Umkreis nach — <b>81 Zellen je
/// Klick</b>, gemessen — und zwar mit derselben Kachelwahl, Nahtwahl und
/// Ufer-Nachbedingung, die <see cref="MapGenerator"/> benutzt. Was dabei
/// schiefgeht, wenn man auch nur einen dieser drei Schritte auslaesst, steht
/// bei <see cref="MapEditTerrain"/>: es ist gemessen, nicht befuerchtet.</para>
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
    /// <remarks>11.11.2026 bug-802: <c>Wegnehmen</c> (Taste 7) neu — nimmt weg,
    /// was unter der Maus steht, egal welche Art.</remarks>
    public enum Brush { Gebaeude, Gegenstand, Gleis, Einheit, Gelaende, Hoehe, Wegnehmen, Vorkommen, Startplatz, Bruecke }

    private MapEntityLayer _layer = null!;
    private CwmFile _map = null!;

    private Brush _brush = Brush.Gebaeude;
    private int _typ = 1, _owner = 0, _line;
    /// <summary>Welche Gelaendeklasse der Gelaendepinsel malt.</summary>
    private MapFactory.Ground _ground = MapFactory.Ground.Free;
    private int _propAt;
    private int _unitAt;
    /// <summary>D9: Blickrichtung (+0x02/+0x03) der naechsten Einheit, 0..7.</summary>
    private int _blick = 0;
    private readonly List<int> _types = new();
    private readonly List<int> _props = new();
    private Button? _spiegelKnopf;                                          // D6
    /// <summary>C4: je Objektcode, was er auf dieser Karte ist (aus der Belegung sec6
    /// der Zellen, auf denen er steht) und wie oft er vorkommt.</summary>
    private readonly Dictionary<int, (string Art, int Anzahl)> _propInfo = new();
    private Vector2I? _hover;
    private string _say = "";

    // Was dieser Lauf gesetzt hat — nur fuer die Vorschau und den Zaehler.
    private readonly List<(int Col, int Row, Brush What)> _mine = new();
    private int _nextSlot;

    private CanvasLayer _ui = null!;

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
        Wiederherstellen();      // bug-802: Werkzeug ueber F5 hinweg behalten
        BuildUi();
        Refresh();
        Harness();
        P5Check();                // P5 (12.11.2026)
        StartCheck();             // P6/D5 (12.11.2026)
        KameraCheck();            // C1 (12.11.2026)
        MinikarteCheck();         // D10 (12.11.2026)
        SpiegelCheck();           // D6 (12.11.2026)
        FahrwerkCheck();          // B6 (12.11.2026)
        SpielenCheck();           // C9 (12.11.2026)
        WaldCheck();              // Wald brennt (12.11.2026)
        SofortCheck();            // D4 (12.11.2026)
        FussvolkCheck();          // D9 (13.11.2026)
        BrueckeCheck();           // D9 (13.11.2026)
        RampeCheck();             // D9 (13.11.2026)
        SatzCheck();              // B3 (13.11.2026)
        LagerCheck();             // D9 (13.11.2026)
        HafenCheck();             // bug-832 (14.11.2026)
    }


    // ---- 11.11.2026 bug-800: der Bildlauf -----------------------------------
    private string _uiShot = "";
    private int _uiShotIn = 90;

    private void PinselVorwahl(int n)
    {
        _brush = n switch
        {
            1 => Brush.Gebaeude, 2 => Brush.Gegenstand, 3 => Brush.Gleis,
            4 => Brush.Einheit, 5 => Brush.Gelaende, 6 => Brush.Hoehe, 8 => Brush.Vorkommen, 9 => Brush.Startplatz, 10 => Brush.Bruecke, _ => Brush.Wegnehmen,
        };
        Refresh();
    }

    public override void _Process(double delta)
    {
        if (_uiShot.Length == 0) return;
        if (_uiShotIn-- > 0) return;
        var img = GetViewport().GetTexture().GetImage();
        var err = img.SavePng(_uiShot);
        GD.Print($"editor-ui-shot: {_uiShot} {err} ({img.GetWidth()}x{img.GetHeight()})");
        _uiShot = "";
        GetTree().Quit(err == Error.Ok ? 0 : 1);
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

    /// <summary>Wie viele Zellen einen Gegenstand tragen — der Kachelcode IST
    /// der Gegenstand, es gibt keinen zweiten Ort dafuer.</summary>
    private int CountProps()
    {
        int n = 0;
        for (int c = 0; c < _map.Width; c++)
            for (int r = 0; r < _map.Height; r++)
                if (_map.CodeAt(c, r) >= CwpFile.ObjectCodeBase) n++;
        return n;
    }

    /// <summary>bug-802: belegte Saetze in sec5.</summary>
    private int CountUnits()
    {
        var s = _map.Sec(5);
        if (s == null) return 0;
        int n = 0;
        for (int o = 0; o + CwmData.EntityStride <= s.Length; o += CwmData.EntityStride)
            if (s[o + 0x09] != 0xFF) n++;
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
        var artZahl = new Dictionary<(int, string), int>();
        for (int c = 0; c < _map.Width; c++)
            for (int r = 0; r < _map.Height; r++)
            {
                int code = _map.CodeAt(c, r);
                if (code < CwpFile.ObjectCodeBase) continue;
                if (bTiles.Contains(code - CwpFile.ObjectCodeBase)) continue;
                seen[code] = seen.TryGetValue(code, out int n) ? n + 1 : 1;
                int bel = BelegungAt(c, r);
                string art = Import.MapForest.IstWald(bel) ? "Wald"
                           : bel >= Import.MapForest.ObjektVon && bel < Import.MapForest.ObjektBis ? "zerstoerbar"
                           : "Fels/Hindernis";
                artZahl[(code, art)] = artZahl.TryGetValue((code, art), out int k) ? k + 1 : 1;
            }
        var all = new List<KeyValuePair<int, int>>(seen);
        all.Sort((a, b) => b.Value != a.Value ? b.Value.CompareTo(a.Value) : a.Key.CompareTo(b.Key));
        foreach (var kv in all) _props.Add(kv.Key);
        foreach (var kv in all)
        {
            string best = "Fels/Hindernis"; int bn = -1;
            foreach (var ak in artZahl)
                if (ak.Key.Item1 == kv.Key && ak.Value > bn) { bn = ak.Value; best = ak.Key.Item2; }
            _propInfo[kv.Key] = (best, kv.Value);
        }
    }

    // ---- die Bedienleiste ---------------------------------------------------
    //
    // ⭐ 11.11.2026 — bug-802: die alte Leiste (zwei Textzeilen, nur Tasten)
    // ist ersetzt durch das Bedienfeld in MapEditPanel.cs (BuildUi/Refresh).

    private static string GroundName(MapFactory.Ground g) => g switch
    {
        MapFactory.Ground.Free => "frei",
        MapFactory.Ground.Rough => "rau",
        MapFactory.Ground.Water => "Wasser",
        _ => "gesperrt",
    };

    internal static string OwnerName(int o) => o switch
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
            if (cell != _hover) { _hover = cell; Ziehen(cell); QueueRedraw(); }   // P5: Ziehen
            return;
        }

        // P5 (12.11.2026): Druecken beginnt einen Strich (ein Rueckgaengig-Schritt),
        // Loslassen beendet ihn.
        if (e is InputEventMouseButton { Pressed: false } los
            && los.ButtonIndex is MouseButton.Left or MouseButton.Right)
        {
            StrichEnden();
            return;
        }
        if (e is InputEventMouseButton { Pressed: true } mb)
        {
            var cell = _layer.CellAt(GetLocalMousePosition());
            if (cell == null) return;
            if (mb.ButtonIndex is MouseButton.Left or MouseButton.Right)
            {
                bool links = mb.ButtonIndex == MouseButton.Left;
                StrichBeginnen(links);
                Anwenden(cell.Value, links);
                GetViewport().SetInputAsHandled();
            }
            return;
        }

        if (e is not InputEventKey { Pressed: true, Echo: false } k) return;
        bool used = true;
        switch (k.Keycode)
        {
            case Key.Key1: _brush = Brush.Gebaeude; break;
            case Key.Key2: _brush = Brush.Gegenstand; break;
            case Key.Key3: _brush = Brush.Gleis; break;
            case Key.Key4: _brush = Brush.Einheit; break;
            case Key.Key5: _brush = Brush.Gelaende; break;
            case Key.Key6: _brush = Brush.Hoehe; break;
            case Key.Key7: _brush = Brush.Wegnehmen; break;          // bug-802
            case Key.Key8: _brush = Brush.Vorkommen; break;          // P5/D7
            case Key.Key9: _brush = Brush.Startplatz; if (_owner is < 0 or > 7) _owner = 0; break;   // P6/D5
            case Key.Key0: _brush = Brush.Bruecke; break;                                            // D9 (13.11.2026)
            case Key.M:                                                      // D6: Spiegeln
                _spiegeln = !_spiegeln;
                _spiegelKnopf?.SetPressedNoSignal(_spiegeln);
                _say = _spiegeln ? "Spiegeln AN (Bodenart, Höhe, Wald/Fels, Gleise)" : "Spiegeln aus";
                Refresh(); GetViewport().SetInputAsHandled(); return;
            case Key.Bracketleft: PinselGroesse(-1); Refresh(); QueueRedraw(); GetViewport().SetInputAsHandled(); return;
            case Key.Bracketright: PinselGroesse(+1); Refresh(); QueueRedraw(); GetViewport().SetInputAsHandled(); return;
            case Key.Z when k.CtrlPressed: Rueckgaengig(); GetViewport().SetInputAsHandled(); return;
            case Key.Y when k.CtrlPressed: Wiederholen(); GetViewport().SetInputAsHandled(); return;
            case Key.Equal or Key.Plus or Key.KpAdd: Step(+1); break;
            case Key.Minus or Key.KpSubtract: Step(-1); break;
            // ⚠ DER EIGNER HAT JETZT EIGENE TASTEN, und das ist eine
            // FEHLERBEHEBUNG, keine Umgewoehnung: bis hierher stand »0..7 =
            // Eigner« in der Leiste, aber die Faelle Key1..Key4 stehen VOR dem
            // Zweig, der die Ziffer als Eigner liest — die Ziffern 1 bis 4 sind
            // nie dort angekommen. Anwaehlbar waren damit nur die Spieler 1, 6,
            // 7 und 8; die Spieler 2 bis 5 gar nicht. Mit dem fuenften und
            // sechsten Pinsel waeren noch zwei weggefallen. »,« und ».«
            // schalten den Eigner reihum durch die Tafel Owners, und dann sind
            // alle zehn erreichbar.
            //
            // ⚠ <s>Dafuer stand hier »W«, weil der Kartenbetrachter fast jeden
            // Buchstaben belegt und A, D, W frei aussahen.</s> Sie sind es
            // nicht: A/D/W sind die KAMERA (MapViewer.cs:2425-2427), und die
            // wird mit Input.IsKeyPressed GEPOLLT — dagegen hilft
            // SetInputAsHandled nicht. Jeder Eignerwechsel haette die Karte
            // mitgescrollt. Komma und Punkt sind im Betrachter nirgends belegt,
            // weder als Fall noch gepollt.
            case Key.Period: _owner = Owners[(Array.IndexOf(Owners, _owner) + 1) % Owners.Length]; break;
            case Key.Comma: _owner = Owners[(Array.IndexOf(Owners, _owner) + Owners.Length - 1) % Owners.Length]; break;
            // D9 (13.11.2026): R dreht die Blickrichtung der naechsten Einheit (8 Stufen).
            // Im Betrachter schaltet R die Reichweitenkreise — im Einheitenpinsel hat der
            // Editor Vorrang, sonst bleibt R beim Betrachter.
            case Key.R when _brush == Brush.Einheit && !k.CtrlPressed:
                _blick = (_blick + (k.ShiftPressed ? 7 : 1)) % 8; FillList(); SelectInList(); break;
            case Key.N: _owner = 11; break;
            case Key.K: _owner = 255; break;
            case Key.F5: SaveAndReload(); return;
            case Key.Escape: ZurueckZumMenue(); GetViewport().SetInputAsHandled(); return;   // bug-802
            default:
                used = false;
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
            case Brush.Einheit:
                _unitAt = (_unitAt + d + MapEditUnits.Liste.Length) % MapEditUnits.Liste.Length;
                break;
            case Brush.Gelaende:
                _ground = (MapFactory.Ground)(((int)_ground + d + 4) % 4);
                break;
            case Brush.Hoehe:
                break;                                  // links hebt, rechts senkt
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
            case Brush.Einheit: PutUnit(cell); break;
            case Brush.Gelaende: PutGround(cell, _ground); break;
            case Brush.Hoehe: PutHeight(cell, +1); break;
            case Brush.Wegnehmen: TakeAny(cell); return;              // bug-802
            case Brush.Vorkommen: PutVorkommen(cell); break;          // P5/D7
            case Brush.Startplatz: PutStartplatz(cell); break;        // P6/D5
            case Brush.Bruecke: if (_brueckeArt == 1) PutRampe(cell); else PutBruecke(cell); break;   // D9 (13.11.2026)
        }
        Refresh();
        QueueRedraw();
    }

    /// <summary>
    /// GELAENDE MALEN — und der Grund, warum hier so wenig steht: die Arbeit
    /// macht <see cref="MapEditTerrain"/>, weil sie zum groessten Teil aus dem
    /// NACHZIEHEN des Umkreises besteht und nicht aus dem Klick.
    ///
    /// <para>⚠ Der Schirm sagt beim Ablehnen den GRUND weiter, statt ihn zu
    /// schlucken. Ein Pinsel, der bei jedem dritten Klick nichts tut und nichts
    /// sagt, ist von einem kaputten Pinsel nicht zu unterscheiden.</para>
    /// </summary>
    private void PutGround(Vector2I cell, MapFactory.Ground g)
    {
        if (MapEditSession.Tiles == null && MapEditSession.Palette == null)
        { _say = "keine Kacheltabelle und kein Bodenblock — Gelaendemalen ginge nur ins Blaue"; return; }

        if (MapFactory.Belegt(_map, cell.X, cell.Y) is { } belegt) { _say = belegt; return; }   // B4
        string? why = MapEditTerrain.PaintClass(_map, MapEditSession.Tiles, MapEditSession.Palette,
                                                cell.X, cell.Y, g, MapEditSession.Seed,
                                                MapEditSession.Seams);
        if (why != null) { _say = why; return; }

        _mine.Add((cell.X, cell.Y, Brush.Gelaende));
        MapEditSession.Dirty = true;
        _say = $"({cell.X},{cell.Y}) ist jetzt {GroundName(g)} — {MapEditTerrain.LastRetiled} Zellen " +
               $"neu gekachelt, {MapEditTerrain.LastMissing} davon aus dem Bodenblock, " +
               $"{MapEditTerrain.LastShoreFixed} Uferkacheln nachgezogen" +
               (MapEditTerrain.LastShoreLeft > 0 ? $" (⚠ {MapEditTerrain.LastShoreLeft} nicht)" : "") +
               "; sichtbar nach F5";
    }

    /// <summary>Eine Zelle anheben oder absenken. Die zwei gemessenen Schranken
    /// prueft <see cref="MapEditTerrain.ChangeHeight"/>; abgelehnt wird mit
    /// Begruendung, statt Nachbarzellen mitzuveraendern.</summary>
    private void PutHeight(Vector2I cell, int delta)
    {
        string? why = MapEditTerrain.ChangeHeight(_map, MapEditSession.Tiles, MapEditSession.Palette,
                                                  cell.X, cell.Y, delta, MapEditSession.Seed,
                                                  MapEditSession.Seams);
        if (why != null) { _say = why; return; }

        _mine.Add((cell.X, cell.Y, Brush.Hoehe));
        MapEditSession.Dirty = true;
        _say = $"({cell.X},{cell.Y}) auf Höhe {_map.ElevAt(cell.X, cell.Y)} — " +
               $"{MapEditTerrain.LastRetiled} Zellen neu gekachelt; sichtbar nach F5";
    }

    private void PutBuilding(Vector2I cell)
    {
        var cwp = MapEditSession.Cwp;
        if (cwp == null) { _say = "kein Kachelsatz — kein Gebäude"; return; }
        // D9 (13.11.2026): Klick auf ein stehendes Gebaeude uebernimmt das gewaehlte Lager
        if (!LagerAus && LagerUebernehmen(cell)) return;
        var bt = cwp.GetBuildingType(_typ);
        if (bt.IsEmpty) { _say = $"Typ {_typ} gibt es in diesem Kachelsatz nicht"; return; }

        var foot = _layer.EditorFootCells(_typ);
        var (fw, fh) = _layer.EditorFootprint(_typ);
        if (cell.X + fw > _map.Width || cell.Y + fh > _map.Height)
        { _say = "das Gebäude ragt über den Kartenrand"; return; }

        // ⭐ 14.11.2026 bug-832: der Hafen steht im Wasser und gehoert zu einer Werft (MapEditHafen.cs)
        if (_typ == MapNeutrals.HafenTyp && !HafenPinselAlt) { HafenSetzen(cell, cwp, foot, fw, fh); return; }

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
        // ⭐ 12.11.2026 — A3 (Fable-Pruefung): der Platz war die ZAHL der Gebaeude.
        // Nach »wegnehmen -> F5 -> setzen« (oder auf Kopien gelieferter Karten mit
        // Luecken) ueberschrieb das neue Gebaeude den Satz eines vorhandenen. Jetzt
        // der erste FREIE Platz (+0x18 IsBuilt != 1), wie NextUnitSlot fuer Einheiten.
        _nextSlot = NextBuildingSlot();
        if (_nextSlot < 0) { _say = "kein Gebaeudeplatz mehr frei (255)"; return; }
        var lager = LagerFuer(_typ);        // D9 (13.11.2026): Lager aus dem Bedienfeld
        if (!MapFactory.PutBuilding(_map, _nextSlot, _typ, _owner, 0, cell.X, cell.Y,
                                    fw, fh, hp, "", doors, lager.W, lager.F, lager.S, lager.T,
                                    cwp: cwp))   // bug-742: Musterstempel
        { _say = "kein Gebaeudeplatz mehr frei (255)"; return; }

        _layer.EditorAddBuilding(_nextSlot, _typ, _owner, cell.X, cell.Y, fw, fh, hp);
        _mine.Add((cell.X, cell.Y, Brush.Gebaeude));
        _nextSlot++;
        MapEditSession.Dirty = true;
        string lt = LagerText(_typ, lager);
        _say = $"Typ {_typ} auf ({cell.X},{cell.Y}), {fw}x{fh}, {OwnerName(_owner)}" + (lt.Length > 0 ? $", {lt}" : "");
    }

    /// <summary>
    /// EINE EINHEIT SETZEN.
    ///
    /// <para>Der Platz einer Einheit haengt am EIGNER: sec5 ist in Bloecke zu
    /// 1000 geteilt, und <c>CwmData.Entity.Owner</c> rechnet den Eigner aus der
    /// Platznummer zurueck (<c>Slot / 1000</c>). Ein Satz im falschen Block
    /// gehoerte also dem falschen Spieler, ganz gleich was sonst darin steht.</para>
    ///
    /// <para>⚠ <b>Die Gattung entscheidet, wo sie stehen darf</b>, und der
    /// Editor fragt danach — mit derselben Tafel, die auch den Rumpf bestimmt:
    /// <c>Can_go</c> @0x4055D0 laesst Gattung 4 und 5 nur auf Wasser und den
    /// Rest nur an Land. Ein Schiff auf die Wiese zu setzen ergaebe eine
    /// Einheit, die sich nie bewegen kann; das sagt der Schirm, statt es
    /// zuzulassen. Geprueft wird der GANZE Rumpf (4 bzw. 16 Zellen), so wie das
    /// Original ihn prueft.</para>
    /// </summary>
    private void PutUnit(Vector2I cell)
    {
        var u = MapEditUnits.Liste[_unitAt];
        if (_owner is < 0 or > 7)
        { _say = $"eine Einheit braucht einen Spielerplatz 0..7, nicht {OwnerName(_owner)}"; return; }

        int side = Simulation.NavGrid.HullSide(u.Art);
        // ⭐ 12.11.2026 B6 (Fable-Pruefung): das FAHRWERK mitgeben — mit -1 galten Luftkissen
        // (Rumpf 7) und Laeufer (Rumpf 0x11) als gewoehnliche Fahrzeuge: das Luftkissen durfte
        // nicht aufs Wasser, der Laeufer nicht auf rauen Boden. Das Rumpfbyte steht im
        // gemessenen Kampfsatz (MapEditUnits.SatzOf). Gegenschalter --editor-fahrwerk-alt.
        int rumpf = Array.IndexOf(Core.CommandLine.Args, "--editor-fahrwerk-alt") < 0
                    && MapEditUnits.SatzOf(u.Type) is { } satz ? satz.Chassis : -1;
        var mc = Simulation.NavGrid.ClassOf(u.Art, rumpf);
        for (int dy = 0; dy < side; dy++)
            for (int dx = 0; dx < side; dx++)
            {
                int c = cell.X + dx, r = cell.Y + dy;
                if (c >= _map.Width || r >= _map.Height)
                { _say = "die Einheit ragt über den Kartenrand"; return; }
                if (MapFactory.Belegt(_map, c, r) is { } belegt) { _say = belegt; return; }   // B5: nicht stapeln
                if (_layer.NavGridOf() is { } nav && !nav.CanEnter(c, r, mc))
                {
                    _say = $"({c},{r}) traegt {(mc == Simulation.NavGrid.MoveClass.Ship ? "kein Wasser" : "kein Land")}" +
                           $" — {u.Name} kann dort nicht stehen";
                    return;
                }
            }

        int slot = NextUnitSlot(_owner);
        if (slot < 0) { _say = $"Spieler {_owner + 1} hat keinen freien Einheitenplatz mehr (1000)"; return; }
        // bug-740 (11.11.2026): Waffe 0 hier ist nur noch der Rueckfall — PutEntity
        // schreibt den gemessenen Kampfsatz der Bauart (MapEditUnits.SatzOf).
        // D9 (13.11.2026): mit Blickrichtung (Taste R) und, beim Fussvolk, dem Zellversatz.
        uint wurf = MapTerrain.Hash(cell.X, cell.Y, MapEditSession.Seed ^ (uint)slot ^ 0x5F17u);
        if (!MapFactory.PutEntity(_map, slot, cell.X, cell.Y, u.Type, u.Attack, 0,
                                  u.Fuel, _blick, u.Kind_, u.Art, u.Energy, wurf))
        { _say = "der Satz liess sich nicht schreiben"; return; }

        int inf = MapEditUnits.Fussvolksatz(u.Type);
        var (sx, sy) = inf >= 0 ? MapEditUnits.Streuung(wurf) : ((sbyte)0, (sbyte)0);
        _layer.EditorAddUnit(slot, u.Type, _owner, cell.X, cell.Y, u.Art, u.Energy, u.Fuel,
                             _blick, inf, sx, sy);
        _mine.Add((cell.X, cell.Y, Brush.Einheit));
        _gone.Remove((cell.X, cell.Y));            // B10: das rote Kreuz der dort weggenommenen Einheit weg
        MapEditSession.Dirty = true;
        _say = $"{u.Name} (Typ {u.Type}) auf ({cell.X},{cell.Y}), Platz {slot}, Spieler {_owner + 1}";
    }

    /// <summary>Der naechste freie Platz im Block dieses Spielers. Frei ist ein
    /// Satz, wenn <c>+0x09 == 0xFF</c> ist — derselbe Leerwert, den
    /// <see cref="CwmData.Entities"/> prueft und den
    /// <see cref="MapFactory.Empty"/> setzt.</summary>
    private int NextBuildingSlot()
    {
        var s = _map.Sec(3);
        if (s == null) return -1;
        for (int i = 0; i < 255; i++)
        {
            int o = i * CwmData.BuildingStride;
            if (o + CwmData.BuildingStride > s.Length) break;
            if (s[o + 0x18] != 1) return i;
        }
        return -1;
    }

    private int NextUnitSlot(int owner)
    {
        var s = _map.Sec(5);
        if (s == null) return -1;
        int from = owner * CwmData.EntitiesPerPlayer;
        for (int k = 0; k < CwmData.EntitiesPerPlayer; k++)
        {
            int o = (from + k) * CwmData.EntityStride;
            if (o + CwmData.EntityStride > s.Length) break;
            if (s[o + 0x09] == 0xFF) return from + k;
        }
        return -1;
    }

    private void PutProp(Vector2I cell)
    {
        if (_props.Count == 0) { _say = "diese Karte fuehrt keine Gegenstände"; return; }
        int code = _props[_propAt];
        if (MapFactory.Belegt(_map, cell.X, cell.Y) is { } belegt) { _say = belegt; return; }   // B4
        int elev = _map.ElevAt(cell.X, cell.Y);
        // ⚠ Die Gelaendeklasse bleibt, was sie war. Ein Gegenstand ist Bewuchs;
        // ihn zum Hindernis zu erklaeren waere eine Entscheidung ueber das
        // Spiel, keine ueber das Bild — und sie steht hier nicht an.
        bool warFrei = BelegungAt(cell.X, cell.Y) == MapFactory.ImapFree;
        MapFactory.Paint(_map, cell.X, cell.Y, code, elev, GroundAt(cell));
        // 12.11.2026: ein BAUM (Art »Wald« auf dieser Karte) auf freiem Boden wird echter
        // Wald — er brennt und sperrt wie die Baeume des Generators und der Originale.
        if (_propInfo.TryGetValue(code, out var pi) && pi.Art == "Wald" && warFrei)
            MapEditTerrain.WaldSetzen(_map, cell.X, cell.Y);
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
        if (MapFactory.Belegt(_map, cell.X, cell.Y) is { } belegt) { _say = belegt; return; }   // B4
        if (GroundAt(cell) == MapFactory.Ground.Water && !MapFactory.HasRail(_map, cell.X, cell.Y))
        { _say = $"({cell.X},{cell.Y}): kein Gleis auf Wasser oder gesperrten Boden"; return; }
        if (MapFactory.PutRail(_map, cell.X, cell.Y, RailFrameAt(cell.X, cell.Y, true), _line) < 0)
        { _say = "kein Gleisplatz mehr frei (3000)"; return; }
        _mine.Add((cell.X, cell.Y, Brush.Gleis));
        RefreshRailAround(cell);
        MapEditSession.Dirty = true;
        _say = $"Gleis auf ({cell.X},{cell.Y}), Zugstrecke {_line + 1} — sichtbar nach F5";
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
            case Brush.Vorkommen:                                       // P5/D7
                TakeVorkommen(cell);
                break;
            case Brush.Bruecke:                                         // D9 (13.11.2026)
                if (!BauwerkWegnehmen(cell)) _say = $"({cell.X},{cell.Y}): dort steht keine Brücke und keine Rampe";
                break;
            case Brush.Startplatz:                                      // P6/D5
                if (_owner is >= 0 and <= 7)
                {
                    int n = StartplatzWegnehmen(_owner);
                    _say = n > 0 ? $"Startplatz Spieler {_owner + 1} weg ({n} Teile)" : $"Spieler {_owner + 1} hat keinen Startplatz";
                    if (n > 0) MapEditSession.Dirty = true;
                }
                break;
            case Brush.Gebaeude:
                int i = _layer.EditorBuildingAt(cell);
                if (i < 0) { _say = "dort steht kein Gebäude"; break; }
                int slot = _layer.EditorSlotOf(i);
                string paar = HafenPaarLoesen(slot);   // bug-832: Hafen/Werft-Paar (MapEditHafen.cs)
                // Erst die Karte, dann das Bild: schlaegt das Leeren des Satzes
                // fehl, soll das Gebaeude sichtbar bleiben, statt aus der
                // Anzeige zu verschwinden und in der Datei stehenzubleiben.
                if (!MapFactory.ClearBuilding(_map, slot))
                { _say = $"Platz {slot} traegt keinen gebauten Satz — nichts weggenommen"; break; }
                _layer.EditorRemoveBuilding(slot);
                _mine.RemoveAll(x => x.What == Brush.Gebaeude && x.Col == cell.X && x.Row == cell.Y);
                MapEditSession.Dirty = true;
                _say = $"Gebäude auf Platz {slot} weg{paar} — Grundfläche wieder frei";
                break;
            case Brush.Gleis:
                if (MapFactory.PutRail(_map, cell.X, cell.Y, -1, _line) < 0)
                { _say = "dort liegt kein Gleis"; break; }
                RefreshRailAround(cell);
                MapEditSession.Dirty = true;
                _say = $"Gleis von ({cell.X},{cell.Y}) weg";
                break;
            // ⚠ Beim Gelaende gibt es kein »wegnehmen« — eine Zelle hat IMMER
            // eine Klasse. Die rechte Taste malt darum »frei«, den Grundwert
            // einer begehbaren Zelle; das ist unsere Setzung und steht in der
            // Leiste, damit niemand ein Rueckgaengig erwartet.
            case Brush.Gelaende:
                PutGround(cell, MapFactory.Ground.Free);
                break;
            case Brush.Hoehe:
                PutHeight(cell, -1);
                break;
            // ⚠ HIER STAND »Gegenstand wegnehmen: dafuer den Boden neu malen«,
            // und der Rat ging ins Leere: MapEditTerrain.PaintClass steigt aus,
            // sobald die Zelle die gewuenschte Klasse schon hat — ein Gegenstand
            // aendert die Klasse aber gar nicht, also war er unloeschbar. Der
            // Weg steht jetzt bei MapEditTerrain.RemoveProp.
            case Brush.Gegenstand:
            {
                string? why = MapEditTerrain.RemoveProp(_map, MapEditSession.Tiles,
                                                        MapEditSession.Palette, cell.X, cell.Y,
                                                        MapEditSession.Seed, MapEditSession.Seams);
                if (why != null) { _say = why; break; }
                _mine.RemoveAll(x => x.What == Brush.Gegenstand && x.Col == cell.X && x.Row == cell.Y);
                MapEditSession.Dirty = true;
                _say = $"Gegenstand von ({cell.X},{cell.Y}) weg — {MapEditTerrain.LastRetiled} Zellen " +
                       $"neu gekachelt, {MapEditTerrain.LastPropsKept} Nachbarn mit Gegenstand " +
                       "unangetastet; sichtbar nach F5";
                break;
            }
            case Brush.Wegnehmen:
                TakeAny(cell);
                return;
            // bug-802 (11.11.2026): Einheiten wegnehmen — der Satz geht aus
            // sec5, das Bild verschwindet erst nach F5 (bis dahin rotes Kreuz).
            case Brush.Einheit:
            {
                int uslot = MapFactory.ClearEntityAt(_map, cell.X, cell.Y);
                if (uslot < 0) { _say = "dort steht keine Einheit"; break; }
                _gone.Add((cell.X, cell.Y));
                _mine.RemoveAll(x => x.What == Brush.Einheit && x.Col == cell.X && x.Row == cell.Y);
                MapEditSession.Dirty = true;
                _say = $"Einheit auf Platz {uslot} (Spieler {uslot / CwmData.EntitiesPerPlayer + 1}) weg — " +
                       "verschwindet aus dem Bild nach F5";
                break;
            }
            default:
                _say = "dieser Pinsel kennt kein Wegnehmen";
                break;
        }
        Refresh();
        QueueRedraw();
    }

    /// <summary>Steht in sec5 eine Einheit, deren Rumpf diese Zelle deckt?</summary>
    private bool UnitAt(Vector2I cell)
    {
        var s = _map.Sec(5);
        if (s == null) return false;
        for (int o = 0; o + CwmData.EntityStride <= s.Length; o += CwmData.EntityStride)
        {
            if (s[o + 0x09] == 0xFF) continue;
            int side = Simulation.NavGrid.HullSide(s[o + 0x0a]);
            if (cell.X >= s[o] && cell.Y >= s[o + 1] && cell.X < s[o] + side && cell.Y < s[o + 1] + side)
                return true;
        }
        return false;
    }

    /// <summary>Weggenommene Einheiten bis zum naechsten F5 — ihr Bild steht
    /// noch in der lebenden Liste (sie umzunummern verschoebe die Belegung der
    /// Wegekarte), darum ein rotes Kreuz darueber.</summary>
    private readonly List<(int Col, int Row)> _gone = new();

    /// <summary>bug-802 (11.11.2026): WEGNEHMEN, WAS DORT STEHT — erst eine
    /// Einheit, sonst ein Gebaeude, sonst ein Gleis, sonst ein Gegenstand. Dieselben Wege wie die
    /// rechte Taste der einzelnen Pinsel (<see cref="Take"/>); die Reihenfolge
    /// ist UNSERE SETZUNG.</summary>
    private void TakeAny(Vector2I cell)
    {
        var keep = _brush;
        // Die Einheit ZUERST: sie steht oft im Ueberhang eines Gebaeudemusters
        // (Dach/Fassade decken begehbare Zellen) — der erste Prueflauf nahm sonst
        // das Gebaeude statt der Einheit weg.
        if (UnitAt(cell)) _brush = Brush.Einheit;
        else if (_layer.EditorBuildingAt(cell) >= 0) _brush = Brush.Gebaeude;
        // D9 (13.11.2026): Bruecke/Rampe VOR dem Gegenstand — ihre Kachel liegt im Objektband,
        // und als Gegenstand weggenommen blieb der sec17-Satz verwaist stehen.
        else if (MapBauwerkAbriss.ArtAn(_map, cell.X, cell.Y, out _) > 0) _brush = Brush.Bruecke;
        else if (MapFactory.HasRail(_map, cell.X, cell.Y)) _brush = Brush.Gleis;
        else if (_map.CodeAt(cell.X, cell.Y) >= CwpFile.ObjectCodeBase) _brush = Brush.Gegenstand;
        else
        {
            _say = $"({cell.X},{cell.Y}): hier steht nichts, was sich wegnehmen laesst " +
                   "(Gebäude, Einheit, Gleis oder Gegenstand)";
            Refresh();
            QueueRedraw();
            return;
        }
        Take(cell);
        _brush = keep;
        Refresh();
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
        Merken();                // bug-802: Werkzeug ueber das Neuladen retten
        GetTree().ReloadCurrentScene();
    }

    // ---- Vorschau -----------------------------------------------------------

    public override void _Draw()
    {
        VorkommenZeichnen();                                            // P5/D7
        BrueckeVorschauZeichnen();                                      // D9 (13.11.2026)
        if (_hover is { } h0)
        {
            // P5: die Pinselgroesse im Rahmen zeigen (mittig um die Maus)
            int pr = Flaechig(_brush) ? _pinsel / 2 : 0;
            var h = new Vector2I(Math.Max(0, h0.X - pr), Math.Max(0, h0.Y - pr));
            var (fw, fh) = _brush == Brush.Gebaeude ? _layer.EditorFootprint(_typ)
                         : _brush == Brush.Startplatz ? StartplatzMass()          // P6/D5
                         : (1 + 2 * pr, 1 + 2 * pr);
            var col = _brush switch
            {
                Brush.Wegnehmen => new Color(1f, 0.25f, 0.2f, 0.95f),   // bug-802
                Brush.Gebaeude => new Color(0.3f, 1f, 0.4f, 0.9f),
                Brush.Gegenstand => new Color(1f, 0.85f, 0.2f, 0.9f),
                Brush.Gelaende => GroundColor(_ground),
                Brush.Hoehe => new Color(1f, 0.5f, 0.9f, 0.9f),
                Brush.Vorkommen => new Color(1f, 0.75f, 0.1f, 0.95f),
                Brush.Startplatz => new Color(0.3f, 0.9f, 1f, 0.95f),
                Brush.Bruecke => new Color(0.4f, 1f, 0.5f, 0.95f),
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

        // bug-802: weggenommene Einheiten, bis F5 neu laedt
        foreach (var (c, r) in _gone)
        {
            var p = _layer.EditorCellTopLeft(c, r);
            var x = new Color(1f, 0.2f, 0.2f, 0.95f);
            DrawLine(p, p + new Vector2(MapBaker.TileW, MapBaker.TileH), x, 3f);
            DrawLine(p + new Vector2(MapBaker.TileW, 0), p + new Vector2(0, MapBaker.TileH), x, 3f);
        }

        // Was dieser Lauf gesetzt hat, aber noch nicht im Bild steht.
        foreach (var (c, r, what) in _mine)
        {
            if (what == Brush.Gebaeude) continue;          // das steht schon da
            var p = _layer.EditorCellTopLeft(c, r);
            var col = what switch
            {
                Brush.Gleis => new Color(0.4f, 0.8f, 1f, 0.85f),
                Brush.Gelaende => new Color(0.6f, 1f, 0.6f, 0.85f),
                Brush.Hoehe => new Color(1f, 0.5f, 0.9f, 0.85f),
                _ => new Color(1f, 0.85f, 0.2f, 0.85f),
            };
            DrawRect(new Rect2(p + new Vector2(6, 4), MapBaker.TileW - 12, MapBaker.TileH - 8), col);
        }
    }

    /// <summary>Die Vorschaufarbe der vier Gelaendeklassen — nur eine
    /// Lesehilfe: gruen frei, braun rau, blau Wasser, rot gesperrt.</summary>
    private static Color GroundColor(MapFactory.Ground g) => g switch
    {
        MapFactory.Ground.Free => new Color(0.4f, 1f, 0.4f, 0.9f),
        MapFactory.Ground.Rough => new Color(0.8f, 0.6f, 0.3f, 0.9f),
        MapFactory.Ground.Water => new Color(0.3f, 0.6f, 1f, 0.9f),
        _ => new Color(1f, 0.3f, 0.3f, 0.9f),
    };

    /// <summary>C4: die Belegung sec6 einer Zelle (u16, Index col*256+row).</summary>
    private int BelegungAt(int c, int r)
    {
        var imap = _map.Sec(6);
        int i = (c * MapFactory.ImapStride + r) * 2;
        return imap == null || i + 1 >= imap.Length ? 0 : imap[i] | (imap[i + 1] << 8);
    }

    /// <summary>C4: der Listentext eines Objekts — Art, Nummer, Haeufigkeit.</summary>
    private string PropText(int code, bool zweizeilig = false)
        => _propInfo.TryGetValue(code, out var pi)
            ? $"{pi.Art} Nr. {code - CwpFile.ObjectCodeBase}{(zweizeilig ? " · " : "  ")}{pi.Anzahl}x auf der Karte"
            : $"Objekt Nr. {code - CwpFile.ObjectCodeBase}";

    /// <summary>C4: der Listentext einer Einheit — Fahrwerk und Waffe.</summary>
    private static string UnitText(MapEditUnits.Kind u, bool zweizeilig = false)
    {
        string w = MapEditUnits.SatzOf(u.Type) is { } s ? MapEntityLayer.EditorWaffenName(s.Weapon) : "";
        string t = w.Length > 0 ? $"{u.Name} · {w}" : u.Art == 1 ? $"{u.Name} · Fußvolk" : u.Name;
        return u.Art is 4 or 5 ? $"{t}  (nur Wasser)" : t;
    }
}
