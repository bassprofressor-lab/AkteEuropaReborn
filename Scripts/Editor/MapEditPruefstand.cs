namespace AkteEuropaReborn.Editor;

using System;
using System.Collections.Generic;
using Godot;
using AkteEuropaReborn.Import;
using AkteEuropaReborn.Rendering;

/// <summary>
/// ⭐ 13.11.2026 — F (Fable-Pruefung, Codequalitaet): DER PRUEFSTAND DES BEARBEITUNGSMODUS,
/// aus MapEditOverlay.cs herausgezogen (dort standen rund 400 der 1436 Zeilen im
/// Produktivknoten). Reine Verschiebung, kein Verhalten geaendert: <c>--map-edit-check</c>
/// (Harness) und der Gelaendepinsel-Lauf (TerrainHarness) samt ihren map-check-Zeilen.
/// </summary>
public partial class MapEditOverlay
{
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
        int wahl = -1;
        foreach (string a in Core.CommandLine.Args)
        {
            if (a == "--map-edit-check") want = true;
            // 11.11.2026 bug-800: Bildlauf des Bearbeitungsmodus.
            //   --editor-ui-shot=<png>[,<bilder>]  Bild nach n Bildern, dann Ende
            //   --editor-ui-pinsel=<1..7>           diesen Pinsel vorwaehlen
            else if (a.StartsWith("--editor-ui-shot="))
            {
                var q = a["--editor-ui-shot=".Length..].Split(',');
                _uiShot = q[0];
                if (q.Length > 1 && int.TryParse(q[1], out int n)) _uiShotIn = n;
            }
            else if (a.StartsWith("--editor-ui-pinsel=") &&
                     int.TryParse(a["--editor-ui-pinsel=".Length..], out int b) && b >= 1 && b <= 9)
                PinselVorwahl(b);
            //   --editor-ui-wahl=<n>   den n-ten Eintrag der Auswahlliste waehlen
            else if (a.StartsWith("--editor-ui-wahl=") &&
                     int.TryParse(a["--editor-ui-wahl=".Length..], out int w)) wahl = w;
            //   --editor-ui-besitzer=<0..7|11|255>
            else if (a.StartsWith("--editor-ui-besitzer=") &&
                     int.TryParse(a["--editor-ui-besitzer=".Length..], out int ow)) { _owner = ow; Refresh(); }
        }
        if (wahl >= 0) { OnListPick(wahl); }
        if (!want) return;

        int before = CountBuildings();
        GD.Print($"map-edit-check: Start — {before} Gebäude, {CountRail()} Gleiszellen in der Karte");

        // 1. ein Gebaeude auf die erste Stelle, die can_build_here erfuellt
        int put = 0;
        for (int r = 2; r < _map.Height - 8 && put == 0; r++)
            for (int c = 2; c < _map.Width - 12 && put == 0; c++)
            {
                _say = "";
                PutBuilding(new Vector2I(c, r));
                if (_say.StartsWith("Typ ")) { put = 1; GD.Print($"map-edit-check: Gebäude -> {_say}"); }
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
        GD.Print($"map-edit-check: Gegenstände -> {props} von 3 stehen im Raster " +
                 $"({_props.Count} Arten zur Wahl)");

        // 3b. DIE GEGENPROBE ZUM SETZEN: dieselben Zellen wieder freiraeumen,
        // und dazu eine Zelle OHNE Gegenstand, die abgewiesen werden muss.
        // ⚠ Der Zaehler unterscheidet: auf einer ERZEUGTEN Karte gibt es keine
        // Gegenstaende, dann steht hier »0 von 0« und die Zeile sagt es. Wer den
        // Griff wirklich pruefen will, macht eine gelieferte Karte auf
        // (--map-editor-open=map_01, 644 Gegenstandszellen).
        int gone = 0, wanted = 0, refused = 0;
        for (int k = 0; k < 3; k++)
        {
            int c = _map.Width / 2 + k, r = _map.Height / 2 + 3;
            if (_map.CodeAt(c, r) < CwpFile.ObjectCodeBase) continue;
            wanted++;
            Take(new Vector2I(c, r));
            if (_map.CodeAt(c, r) < CwpFile.ObjectCodeBase) gone++;
            else GD.Print($"map-edit-check: ⚠ Gegenstand ({c},{r}) blieb: {_say}");
        }
        // die Gegenprobe: eine Zelle ohne Gegenstand darf NICHT angefasst werden
        for (int r = 1; r < _map.Height - 1 && refused == 0; r++)
            for (int c = 1; c < _map.Width - 1 && refused == 0; c++)
            {
                if (_map.CodeAt(c, r) >= CwpFile.ObjectCodeBase) continue;
                int had = _map.CodeAt(c, r);
                Take(new Vector2I(c, r));
                if (_map.CodeAt(c, r) == had && _say.Contains("kein Gegenstand")) refused = 1;
                else GD.Print($"map-edit-check: ⚠ Bodenzelle ({c},{r}) wurde angefasst: {_say}");
            }
        GD.Print($"map-edit-check: Gegenstand wegnehmen -> {gone} von {wanted} weg, " +
                 $"Bodenzelle abgewiesen {refused} von 1 (muss 1 sein), " +
                 $"{MapEditTerrain.Letzter.PropsKept} Nachbarn mit Gegenstand unangetastet");

        // 3c. UND DIE ZWEITE GEGENPROBE, die erst durch das Oeffnen vorhandener
        // Karten noetig wurde: ein GELAENDESTRICH darf keinen fremden Gegenstand
        // wegradieren. Gezaehlt wird, wie viele Gegenstandszellen die Karte vor
        // und nach dem Strich traegt — vor dem 15.08.2026 nahm der Umkreislauf
        // sie mit.
        //
        // ⚠ NICHT die ANZAHL vorher gegen nachher: der Gelaendepinsel zieht
        // seine Kachel aus derselben gemessenen Tabelle wie der Generator, und
        // die enthaelt auch Bewuchs — ein Strich PFLANZT also mitunter einen
        // Baum. Der erste Anlauf dieses Zaehlers meldete »644 vorher / 646
        // nachher« und haette damit einen Verlust verdeckt, den zwei neue
        // Baeume aufgewogen haetten. Gezaehlt wird darum, wie viele der ZUVOR
        // vorhandenen Zellen ihren Gegenstand behalten haben.
        var propCells = new List<(int C, int R)>();
        for (int c = 0; c < _map.Width; c++)
            for (int r = 0; r < _map.Height; r++)
                if (_map.CodeAt(c, r) >= CwpFile.ObjectCodeBase) propCells.Add((c, r));
        int propsBefore = propCells.Count;
        _brush = Brush.Gelaende;
        int strokes = 0;
        for (int r = 2; r < _map.Height - 2 && strokes < 8; r++)
            for (int c = 2; c < _map.Width - 2 && strokes < 8; c++)
            {
                if (_map.CodeAt(c, r) >= CwpFile.ObjectCodeBase) continue;
                if (MapEditTerrain.ClassAt(_map, c, r) != 0) continue;
                bool nearProp = false;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                        if (_map.CodeAt(c + dx, r + dy) >= CwpFile.ObjectCodeBase) nearProp = true;
                if (!nearProp) continue;
                PutGround(new Vector2I(c, r), MapFactory.Ground.Rough);
                strokes++;
            }
        int kept = 0;
        foreach (var (c, r) in propCells) if (_map.CodeAt(c, r) >= CwpFile.ObjectCodeBase) kept++;
        GD.Print($"map-edit-check: Gelaendestrich neben Gegenständen -> {strokes} Striche, " +
                 $"{kept} von {propsBefore} alten Gegenstandszellen behalten " +
                 $"(muss gleich sein), Gegenstandszellen jetzt {CountProps()} " +
                 "(darf STEIGEN — die gemessene Tabelle pflanzt selbst Bewuchs)");

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
        for (int i2 = 0; i2 < MapEditUnits.Liste.Length; i2++)
            if (MapEditUnits.Liste[i2].Art == 5) { _unitAt = i2; break; }
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

        // 3a2. bug-802 (11.11.2026): EINHEIT WEGNEHMEN und das Werkzeug
        // »Wegnehmen« — die eben gesetzte erste Einheit wieder weg, dann
        // dieselbe Zelle noch einmal (muss »nichts« melden).
        {
            var u0 = _mine.Find(x => x.What == Brush.Einheit);
            int vorE = CountUnits();
            if (unitsPut > 0)
            {
                _brush = Brush.Wegnehmen;
                Take(new Vector2I(u0.Col, u0.Row));
                int nachE = CountUnits();
                // der zweite Klick mit dem EINHEITENpinsel (rechte Taste): dort
                // steht keine mehr — er muss abweisen. (Mit Werkzeug 7 naehme er
                // das Gebaeude, in dessen Ueberhang die Einheit stand.)
                _brush = Brush.Einheit;
                _say = "";
                Take(new Vector2I(u0.Col, u0.Row));
                GD.Print($"map-edit-check: Einheit wegnehmen (Werkzeug 7) -> {vorE} -> {nachE} Einheiten " +
                         $"(muss eins weniger sein); zweiter Klick: {(_say.Contains("keine Einheit") ? "abgewiesen" : "⚠ " + _say)}");
                // und wieder hinstellen, damit die Zaehler unten unveraendert bleiben
                _brush = Brush.Einheit; _unitAt = 0; _owner = 0;
                PutUnit(new Vector2I(u0.Col, u0.Row));
                _gone.Clear();
            }
        }

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
            GD.Print($"map-edit-check: Wegnehmen -> {_say}; Gebäude {nachBau} -> {CountBuildings()} " +
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
                else if (imap2 != null && zone2 != null)
                {
                    int cc = b.Col + dx, rr = b.Row + dy, ii = (cc * MapFactory.ImapStride + rr) * 2;
                    GD.Print($"map-edit-check:   gesperrt ({cc},{rr}): imap {(imap2[ii] | (imap2[ii + 1] << 8)):X4}, " +
                             $"Hang {_map.FlagAt(cc, rr)}, sec2 {MapDeposits.Zone(zone2, _map.Width, _map.Height, cc, rr)}/" +
                             $"{MapDeposits.Zone(zone2, _map.Width, _map.Height, cc + 1, rr)}/" +
                             $"{MapDeposits.Zone(zone2, _map.Width, _map.Height, cc, rr + 1)}/" +
                             $"{MapDeposits.Zone(zone2, _map.Width, _map.Height, cc + 1, rr + 1)}, " +
                             $"Gleis {MapFactory.HasRail(_map, cc, rr)}, Code {_map.CodeAt(cc, rr)}");
                }
            }
            GD.Print($"map-edit-check: Grundfläche nach dem Wegnehmen: {frei} von {ges} " +
                     "Zellen wieder BAUBAR (muss ges sein — sonst bleibt der Platz gesperrt)");
            // und gleich wieder hinstellen, damit die gespeicherte Karte einen
            // sichtbaren Zuwachs hat und der naechste Zaehler etwas aussagt
            PutBuilding(new Vector2I(b.Col, b.Row));
        }

        // 4. speichern und nachzaehlen — was auf der Platte steht, zaehlt
        string? made = MapEditSession.Save(s => GD.Print("map-edit-check:   " + s));
        GD.Print($"map-edit-check: gespeichert als {made ?? "(nichts)"}; " +
                 $"jetzt {CountBuildings()} Gebäude (vorher {before}), " +
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
        "Gelaende:",                      // die Klassenverteilung (Wortlaut von MapCheck, NICHT umlauten)
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
                     "fällt auf den Bodenblock zurück, und die Ufer-Zeile unten sagt es");

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
        GD.Print($"map-edit-check: Gelände rau -> {rauIst} von {rauSoll} Zellen sind jetzt rau, " +
                 $"{rauAbgewiesen} abgewiesen; letzter Strich {MapEditTerrain.Letzter.Retiled} Zellen " +
                 $"neu gekachelt, {MapEditTerrain.Letzter.Missing} davon aus dem Bodenblock");

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
                    else { hochDurch++; GD.Print($"map-edit-check: ⚠ Wasser auf ({c},{r}) Höhe " +
                                                 $"{_map.ElevAt(c, r)} DURCHGELASSEN — Wasserspiegel ist {wasserHoehe}"); }
                }
            GD.Print($"map-edit-check: Wasser auf falscher Höhe -> {hochAbgewiesen} abgewiesen, " +
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
        GD.Print($"map-edit-check: Höhe -> {hebenIst} von 3 angehoben ({hebenAbgelehnt} abgelehnt); " +
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
}
