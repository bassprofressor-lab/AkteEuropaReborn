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
    public enum Brush { Gebaeude, Gegenstand, Gleis, Einheit, Gelaende, Hoehe }

    private MapEntityLayer _layer = null!;
    private CwmFile _map = null!;

    private Brush _brush = Brush.Gebaeude;
    private int _typ = 1, _owner = 0, _line;
    /// <summary>Welche Gelaendeklasse der Gelaendepinsel malt.</summary>
    private MapFactory.Ground _ground = MapFactory.Ground.Free;
    private int _propAt;
    private int _unitAt;
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

        // 3a. EINHEITEN — je eine Landeinheit für zwei verschiedene Spieler,
        // und die GEGENPROBE: ein Schiff an Land muss abgewiesen werden.
        _brush = Brush.Einheit;
        int unitsPut = 0, unitFail = 0;
        for (int k = 0; k < 2; k++)
        {
            _owner = k;
            _unitAt = 0;                                  // »Reifen«, Gattung 0
            for (int r = 3; r < _map.Height - 3; r++)
            {
                int c = 4 + k * 3;
                if (c >= _map.Width - 3) break;
                _say = "";
                PutUnit(new Vector2I(c, r));
                if (_say.Contains("Platz ")) { unitsPut++; break; }
            }
        }
        // dieselbe Stelle, aber ein Schiff: das darf NICHT gehen
        _owner = 0;
        for (int i2 = 0; i2 < MapEditUnits.All.Length; i2++)
            if (MapEditUnits.All[i2].Art == 5) { _unitAt = i2; break; }
        int shipRefused = 0, shipPut = 0;
        for (int r = 3; r < _map.Height - 6 && shipRefused + shipPut < 6; r++)
        {
            _say = "";
            PutUnit(new Vector2I(10, r));
            if (_say.Contains("Platz ")) shipPut++;
            else if (_say.Contains("kann dort nicht stehen")) shipRefused++;
        }
        GD.Print($"map-edit-check: Einheiten -> {unitsPut} von 2 gesetzt (zwei Spieler), " +
                 $"{unitFail} abgewiesen; Schiff an Land: {shipRefused} abgewiesen, " +
                 $"{shipPut} durchgelassen (durchgelassen muss 0 sein, wenn kein Wasser da ist)");
        _owner = 0;

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

        // 5. DIE MESSLATTE VOR DEM GELAENDEPINSEL. ⚠ Erst jetzt, nach dem
        // Speichern: map-check liest die geschriebenen Dateien, nicht die Karte
        // im Speicher.
        var vorher = RunMapCheck(made, "vorher");

        TerrainHarness();

        made = MapEditSession.Save(s => GD.Print("map-edit-check:   " + s));
        var nachher = RunMapCheck(made, "nachher");

        // ⚠ DAS EIGENTLICHE URTEIL UEBER DEN PINSEL. Ein Pinsel, der die Karte
        // verschlechtert, muss HIER auffallen und nicht daran, dass ein Bild
        // komisch aussieht: dieselben Zahlen, einmal vor und einmal nach dem
        // Malen, nebeneinander.
        GD.Print("map-edit-check: --- map-check vorher | nachher -------------");
        foreach (string k in MapCheckLines)
        {
            vorher.TryGetValue(k, out string? a);
            nachher.TryGetValue(k, out string? b);
            if (a == null && b == null) continue;
            string mark = a == b ? "  =" : " ->";
            GD.Print($"map-edit-check: {k}\n                vorher : {a ?? "(fehlt)"}" +
                     $"\n               {mark} nachher: {b ?? "(fehlt)"}");
        }
        GD.Print("map-edit-check: fertig");
    }

    /// <summary>Die Zeilen von <see cref="MapCheck"/>, auf die es beim
    /// Gelaendepinsel ankommt — jede misst genau eine Sache, die ein falsch
    /// nachgezogener Umkreis kaputtmacht.</summary>
    private static readonly string[] MapCheckLines =
    {
        "Gelaende:",                      // die Klassenverteilung
        "Landzellen am Wasser:",          // der Uferuebergang — die schaerfste
        "Nachbarpaare:",                  // Hoehenspruenge > 1
        "nicht darstellbare Hangformen",  // die zweite Schranke
        "Zellen mit hoeherem Nachbarn:",  // Hangbytes gesetzt?
        "Naehte zwischen Bodenkacheln:",  // passen die Kacheln zueinander?
        "verschiedene Bodencodes:",
        "Bauplatzzellen",
        "map-check:",                     // das Urteil
    };

    /// <summary><see cref="MapCheck"/> ueber die gespeicherte Karte laufen
    /// lassen und die Zeilen einsammeln, auf die es ankommt.</summary>
    private static Dictionary<string, string> RunMapCheck(string? name, string wann)
    {
        var got = new Dictionary<string, string>();
        if (name == null) { GD.Print($"map-edit-check: {wann}: nichts gespeichert, kein map-check"); return got; }
        int bad = MapCheck.Run(name, line =>
        {
            string t = line.Trim();
            foreach (string k in MapCheckLines)
                if (t.StartsWith(k) && !got.ContainsKey(k)) got[k] = t;
        });
        // ⚠ MapCheck.Run gibt 0 oder 1 zurueck, NICHT die Zahl der
        // Beanstandungen — die steht in seiner letzten Zeile, und die steht
        // unten im Vergleich.
        GD.Print($"map-edit-check: map-check {wann}: Urteil {bad} (0 = OK)");
        return got;
    }

    /// <summary>
    /// DER GELAENDEPINSEL IM PRUEFSTAND — und er uebt beide Richtungen aus:
    /// was gehen muss, und was ABGEWIESEN werden muss.
    ///
    /// <para>⚠ Die zweite Haelfte ist die wichtigere. Dass ein Pinsel malt,
    /// sieht man; dass er eine Hoehe ablehnt, die eine Nachbarzelle unmoeglich
    /// machen wuerde, sieht man nur an einem Zaehler. Beide Gegenproben sind so
    /// gebaut, dass ihr Ausgang FESTSTEHT: die zweite Anhebung derselben Zelle
    /// muss am Sprung scheitern, weil die Nachbarn danach zwei Stufen tiefer
    /// laegen.</para>
    /// </summary>
    private void TerrainHarness()
    {
        if (MapEditSession.Tiles == null)
            GD.Print("map-edit-check: ⚠ keine Kacheltabelle in der Sitzung — der Pinsel " +
                     "faellt auf den Bodenblock zurueck, und die Ufer-Zeile unten sagt es");

        // ---- 5a. rau malen: eine Reihe freier Zellen ------------------------
        _brush = Brush.Gelaende;
        _ground = MapFactory.Ground.Rough;
        int rauSoll = 0, rauIst = 0, rauAbgewiesen = 0;
        for (int r = 4; r < _map.Height - 4 && rauSoll < 8; r++)
            for (int c = 4; c < _map.Width - 4 && rauSoll < 8; c++)
            {
                if (MapEditTerrain.ClassAt(_map, c, r) != 0) continue;
                if (MapFactory.HasRail(_map, c, r)) continue;
                _say = "";
                PutGround(new Vector2I(c, r), MapFactory.Ground.Rough);
                rauSoll++;
                if (MapEditTerrain.ClassAt(_map, c, r) == 1) rauIst++;
                else { rauAbgewiesen++; GD.Print($"map-edit-check: ⚠ rau ({c},{r}): {_say}"); }
            }
        GD.Print($"map-edit-check: Gelaende rau -> {rauIst} von {rauSoll} Zellen sind jetzt rau, " +
                 $"{rauAbgewiesen} abgewiesen; letzter Strich {MapEditTerrain.LastRetiled} Zellen " +
                 $"neu gekachelt, {MapEditTerrain.LastMissing} davon aus dem Bodenblock");

        // ---- 5b. GEGENPROBE: Wasser auf falscher Hoehe muss abgewiesen werden
        int wasserHoehe = -1;
        for (int r = 0; r < _map.Height && wasserHoehe < 0; r++)
            for (int c = 0; c < _map.Width && wasserHoehe < 0; c++)
                if (MapEditTerrain.ClassAt(_map, c, r) == 2) wasserHoehe = _map.ElevAt(c, r);

        if (wasserHoehe < 0)
            GD.Print("map-edit-check: Wasser -> diese Karte hat keine einzige Wasserzelle, " +
                     "die Ufer-Gegenprobe entfaellt");
        else
        {
            int hochAbgewiesen = 0, hochDurch = 0;
            for (int r = 2; r < _map.Height - 2 && hochAbgewiesen + hochDurch < 6; r++)
                for (int c = 2; c < _map.Width - 2 && hochAbgewiesen + hochDurch < 6; c++)
                {
                    if (MapEditTerrain.ClassAt(_map, c, r) is 2 or 3) continue;
                    if (_map.ElevAt(c, r) == wasserHoehe) continue;      // die duerfen ja
                    string? why = MapEditTerrain.PaintClass(_map, MapEditSession.Tiles,
                        MapEditSession.Palette, c, r, MapFactory.Ground.Water, MapEditSession.Seed,
                        MapEditSession.Seams);
                    if (why != null) hochAbgewiesen++;
                    else { hochDurch++; GD.Print($"map-edit-check: ⚠ Wasser auf ({c},{r}) Hoehe " +
                                                 $"{_map.ElevAt(c, r)} DURCHGELASSEN — Wasserspiegel ist {wasserHoehe}"); }
                }
            GD.Print($"map-edit-check: Wasser auf falscher Hoehe -> {hochAbgewiesen} abgewiesen, " +
                     $"{hochDurch} durchgelassen (durchgelassen muss 0 sein; Wasserspiegel {wasserHoehe})");

            // ---- 5c. Wasser AM Wasser, auf richtiger Hoehe: das muss gehen --
            int seeSoll = 0, seeIst = 0;
            for (int r = 2; r < _map.Height - 2 && seeSoll < 4; r++)
                for (int c = 2; c < _map.Width - 2 && seeSoll < 4; c++)
                {
                    if (MapEditTerrain.ClassAt(_map, c, r) != 0) continue;
                    if (_map.ElevAt(c, r) != wasserHoehe) continue;
                    bool amWasser = MapEditTerrain.ClassAt(_map, c - 1, r) == 2 ||
                                    MapEditTerrain.ClassAt(_map, c + 1, r) == 2 ||
                                    MapEditTerrain.ClassAt(_map, c, r - 1) == 2 ||
                                    MapEditTerrain.ClassAt(_map, c, r + 1) == 2;
                    if (!amWasser) continue;
                    seeSoll++;
                    _say = "";
                    PutGround(new Vector2I(c, r), MapFactory.Ground.Water);
                    if (MapEditTerrain.ClassAt(_map, c, r) == 2) seeIst++;
                    else GD.Print($"map-edit-check: ⚠ Wasser ({c},{r}): {_say}");
                }
            GD.Print($"map-edit-check: Wasser ans Ufer -> {seeIst} von {seeSoll} gesetzt " +
                     "(die Zeile »Landzellen am Wasser« unten muss danach 0 Innenland-Codes zeigen)");
        }

        // ---- 6. die Hoehe, mit der Gegenprobe -------------------------------
        _brush = Brush.Hoehe;
        int hebenIst = 0, hebenAbgelehnt = 0, zweitAbgelehnt = 0, zweitDurch = 0;
        for (int r = 3; r < _map.Height - 3 && hebenIst < 3; r++)
            for (int c = 3; c < _map.Width - 3 && hebenIst < 3; c++)
            {
                if (MapEditTerrain.ClassAt(_map, c, r) is 2 or 3) continue;
                // eine EBENE Stelle: alle acht Nachbarn gleich hoch. Nur dort
                // steht der Ausgang beider Proben fest.
                int e = _map.ElevAt(c, r);
                bool eben = true;
                for (int dy = -1; dy <= 1 && eben; dy++)
                    for (int dx = -1; dx <= 1 && eben; dx++)
                        if (_map.ElevAt(c + dx, r + dy) != e) eben = false;
                if (!eben || e + 1 > MapEditTerrain.MaxElev) continue;

                _say = "";
                PutHeight(new Vector2I(c, r), +1);
                if (_map.ElevAt(c, r) != e + 1)
                { hebenAbgelehnt++; GD.Print($"map-edit-check: ⚠ anheben ({c},{r}): {_say}"); continue; }
                hebenIst++;

                // DIE GEGENPROBE: noch einmal anheben. Die Nachbarn liegen jetzt
                // eine Stufe tiefer, der Sprung waere 2 — das MUSS scheitern.
                _say = "";
                PutHeight(new Vector2I(c, r), +1);
                if (_map.ElevAt(c, r) == e + 1) zweitAbgelehnt++;
                else { zweitDurch++; GD.Print($"map-edit-check: ⚠ ({c},{r}) liess sich auf " +
                                              $"{_map.ElevAt(c, r)} anheben — Sprung 2 zu den Nachbarn auf {e}"); }
            }
        GD.Print($"map-edit-check: Hoehe -> {hebenIst} von 3 angehoben ({hebenAbgelehnt} abgelehnt); " +
                 $"zweite Anhebung: {zweitAbgelehnt} abgelehnt, {zweitDurch} durchgelassen " +
                 $"(durchgelassen muss 0 sein — MaxStep {MapTerrain.MaxStep}, gemessen 111 " +
                 "Ausnahmen in 1.202.757 Paaren)");

        _brush = Brush.Gebaeude;
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
            Brush.Gleis => $"Gleis, Linie {_line}",
            Brush.Gelaende => $"Gelaende: {GroundName(_ground)}" +
                              (MapEditSession.Tiles == null ? "  ⚠ ohne Kacheltabelle" : ""),
            Brush.Hoehe => "Hoehe: LINKS anheben, RECHTS absenken",
            _ => $"Einheit {MapEditUnits.All[_unitAt].Name} " +
                 $"(Typ {MapEditUnits.All[_unitAt].Type}, {OwnerName(_owner)})",
        };
        _head.Text = $"EDITOR {MapEditSession.Name}  —  {what}" +
                     (MapEditSession.Dirty ? "  *" : "");
        _foot.Text =
            "1..6 = Gebaeude/Gegenstand/Gleis/Einheit/Gelaende/Hoehe · +/- = Art\n" +
            ",/. = Eigner (N herrenlos, K Kulisse) · LINKS setzen, RECHTS weg\n" +
            "F5 = speichern und neu laden   ·   " + _mine.Count + " gesetzt\n" +
            _say;
    }

    private static string GroundName(MapFactory.Ground g) => g switch
    {
        MapFactory.Ground.Free => "frei",
        MapFactory.Ground.Rough => "rau",
        MapFactory.Ground.Water => "Wasser",
        _ => "gesperrt",
    };

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
            case Key.Key4: _brush = Brush.Einheit; break;
            case Key.Key5: _brush = Brush.Gelaende; break;
            case Key.Key6: _brush = Brush.Hoehe; break;
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
            case Key.N: _owner = 11; break;
            case Key.K: _owner = 255; break;
            case Key.F5: SaveAndReload(); return;
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
                _unitAt = (_unitAt + d + MapEditUnits.All.Length) % MapEditUnits.All.Length;
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
        _say = $"({cell.X},{cell.Y}) auf Hoehe {_map.ElevAt(cell.X, cell.Y)} — " +
               $"{MapEditTerrain.LastRetiled} Zellen neu gekachelt; sichtbar nach F5";
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
        var u = MapEditUnits.All[_unitAt];
        if (_owner is < 0 or > 7)
        { _say = $"eine Einheit braucht einen Spielerplatz 0..7, nicht {OwnerName(_owner)}"; return; }

        int side = Simulation.NavGrid.HullSide(u.Art);
        var mc = Simulation.NavGrid.ClassOf(u.Art, -1);
        for (int dy = 0; dy < side; dy++)
            for (int dx = 0; dx < side; dx++)
            {
                int c = cell.X + dx, r = cell.Y + dy;
                if (c >= _map.Width || r >= _map.Height)
                { _say = "die Einheit ragt ueber den Kartenrand"; return; }
                if (_layer.NavGridOf() is { } nav && !nav.CanEnter(c, r, mc))
                {
                    _say = $"({c},{r}) traegt {(mc == Simulation.NavGrid.MoveClass.Ship ? "kein Wasser" : "kein Land")}" +
                           $" — {u.Name} kann dort nicht stehen";
                    return;
                }
            }

        int slot = NextUnitSlot(_owner);
        if (slot < 0) { _say = $"Spieler {_owner + 1} hat keinen freien Einheitenplatz mehr (1000)"; return; }
        if (!MapFactory.PutEntity(_map, slot, cell.X, cell.Y, u.Type, u.Attack, 0,
                                  u.Fuel, 0, u.Kind_, u.Art, u.Energy))
        { _say = "der Satz liess sich nicht schreiben"; return; }

        _layer.EditorAddUnit(slot, u.Type, _owner, cell.X, cell.Y, u.Art, u.Energy, u.Fuel);
        _mine.Add((cell.X, cell.Y, Brush.Einheit));
        MapEditSession.Dirty = true;
        _say = $"{u.Name} (Typ {u.Type}) auf ({cell.X},{cell.Y}), Platz {slot}, Spieler {_owner + 1}";
    }

    /// <summary>Der naechste freie Platz im Block dieses Spielers. Frei ist ein
    /// Satz, wenn <c>+0x09 == 0xFF</c> ist — derselbe Leerwert, den
    /// <see cref="CwmData.Entities"/> prueft und den
    /// <see cref="MapFactory.Empty"/> setzt.</summary>
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
                Brush.Gelaende => GroundColor(_ground),
                Brush.Hoehe => new Color(1f, 0.5f, 0.9f, 0.9f),
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
}
