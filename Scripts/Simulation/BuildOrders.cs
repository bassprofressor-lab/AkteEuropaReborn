namespace AkteEuropaReborn.Rendering;

using System.Collections.Generic;
using Godot;
using AkteEuropaReborn.Simulation.Commands;

/// <summary>
/// <b>»DEPOT BAUEN«, »MINE BAUEN«, »GENERATOR BAUEN«</b> — die drei anderen
/// Bauaufträge des Originals, Antwort auf <b>C7</b>.
///
/// <para>Der Radarmast (Simulation/RadarMast.cs) war der vierte und der
/// einfachste: er wirkt sofort auf der Zelle der Einheit. Diese drei brauchen
/// einen <b>Platzierungsmodus</b> — ein Knopf schaltet den Mauszeiger um, ein
/// Klick auf die Karte wählt die Stelle, und gebaut wird erst, wenn das
/// Fahrzeug dort ANGEKOMMEN ist.</para>
///
/// <para><b>Die ganze Kette, gelesen 18.08.2026</b> (ausführlich in
/// <c>aekernel-tools/GAMESTATE_RE.md</c>, Abschnitt »The build orders«):</para>
/// <code>
///  1. Knopf 17/18/19   @0x4489ED/0x448A0C/0x448A2B
///                      dword[0x502ACC] = 5 / 6 / 7
///  2. Kartenklick      @0x437FAB — verzweigt nach RUMPF byte[Einheit+0x0E]:
///                      72  -> Kommando 20 (P4 = Modus, P5 = Vorkommen)
///                      74  -> Kommando 21 (ohne Modus)
///                      198 -> Kommando 16
///                      danach dword[0x502ACC] = 0
///  3. Kommando 20      @0x4C3241
///                      P4==5: order(Einheit, P2, P3, 0); cx = (P3&lt;&lt;8)|P2
///                      P4==6: cx = P5; zum Vorkommen fahren
///                      word[+0x40] = cx  @0x4C3320
///                      byte[+0x38] = P4  @0x4C3351
///  4. order()          @0x40B070  byte[+0x14] = 2   (fahren)
///  5. Fahrt zu Ende    @0x408ABC  byte[+0x14] = 0   (Leerlauf) = ANKUNFT
///  6. Leerlauf         @0x407F38  verteilt nach RUMPF, Tafel 0x40A16C
///  7. Rumpf 72         @0x40806A  Modus 5 -> Typ 5, Modus 6 -> Typ 15
///     Rumpf 74         @0x4082DD  -> Typ 7
/// </code>
///
/// <para><b>⚠⚠ Der Befund, der die Sache erst baubar macht: DAS FAHRZEUG IST
/// DER PREIS.</b> Auf dem ganzen Weg wird kein einziger Rohstoff abgebucht — ich
/// habe zweimal danach gesucht. Zwei Befehle vor der Erzeugung steht
/// <c>0x410E60(Einheit)</c>, und das ist das <b>Entfernen der Einheit</b> (räumt
/// das Belegungsraster 0xA0A858, setzt <c>word[+0x24] = 10000</c>). Ein
/// Baufahrzeug wird also VERBRAUCHT. Wer hier Kosten einbaute, erfände eine
/// zweite Bezahlung.</para>
///
/// <para><b>⚠ Nur drei Rümpfe von sechs</b> haben überhaupt einen eigenen
/// Leerlauf mit Bauauftrag (71, 72, 73, 74, 78, 198 haben eigene Handler; die
/// 122 anderen fallen auf »nichts«). Gebaut sind hier <b>72</b>
/// (Gebäude-Techniker: Depot und Mine) und <b>74</b> (Generatorenbauer). Die
/// Handler von 71, 73, 78 und 198 sind <b>NICHT gelesen</b> — sie bekommen
/// deshalb auch keinen Knopf, statt einen zu bekommen, der rät.</para>
///
/// <para><b>⚠ Die Versätze sind NICHT einheitlich, und das ist gelesen, nicht
/// geglättet:</b> Depot und Generator entstehen bei <c>(Spalte−1, Zeile−1)</c>
/// (@0x408126/@0x40833E, beide <c>dec</c>), die Mine bei
/// <c>(Spalte−1, Zeile−2)</c> (@0x408234 <c>dec</c> gegen @0x408228
/// <c>sub eax,2</c>). Wer daraus eine Regel machte, verschöbe die Mine um eine
/// Zeile.</para>
/// </summary>
public partial class MapEntityLayer
{
    /// <summary>Der dritte Bauteil-Rumpf mit eigenem Leerlauf, den wir NICHT
    /// gebaut haben — der Boden-Techniker. Sein Handler @0x408267 ist nicht
    /// gelesen; er steht hier nur, damit die Zahl nicht verlorengeht.</summary>
    public const int PartGroundTechUnread = PartGroundTech;

    /// <summary>Der Setzmodus für den Generator — <c>dword[0x502ACC] = 7</c>
    /// @0x448A2B. ⚠ Er steht NICHT in <c>byte[+0x38]</c> des Originals: Rumpf 74
    /// fragt das Feld gar nicht ab. Wir tragen ihn trotzdem ein, damit die
    /// Ankunft eine einzige Weiche hat — siehe <see cref="Entity.BuildOrder"/>.
    /// </summary>
    public const int OrderGenerator = 7;

    /// <summary>Der Auftrag, dessen Handler wir gelesen haben, je Bauteil.
    /// <c>0</c> heisst »dieses Fahrzeug kann bei uns nichts setzen«.</summary>
    private static int[] BuildOrdersOfPart(int part) => part switch
    {
        PartBuildingTech  => new[] { OrderDepot, OrderFieldMine },
        PartGeneratorTech => new[] { OrderGenerator },
        _                 => System.Array.Empty<int>(),
    };

    /// <summary>Der Gebäudetyp, den ein Auftrag setzt. ⚠ <b>Modus 6 gibt 15,
    /// nicht 6</b> — @0x40821E schiebt buchstäblich <c>push 0xF</c>. Die
    /// naheliegende Regel »Modus = Typ« gilt nur für 5 und 7 und wäre hier
    /// falsch.</summary>
    public static int BuildTypeOfOrder(int order) => order switch
    {
        OrderDepot     => TypeDepot,        // push 5  @0x408124
        OrderFieldMine => TypeFieldMine,    // push 0xF @0x40821E
        OrderGenerator => TypeGenerator,    // push 7  @0x40833C
        _              => 0,
    };

    /// <summary>Der Versatz vom angeklickten Punkt zur Gebäudeecke. Siehe den
    /// Klassenkommentar: die Mine schert aus.</summary>
    public static Vector2I BuildOffsetOfOrder(int order)
        => order == OrderFieldMine ? new Vector2I(-1, -2) : new Vector2I(-1, -1);

    /// <summary>
    /// <b>Welches Sonderbauteil trägt diese Einheit?</b> 0, wenn keines.
    ///
    /// <para>⚠ <b>Unsere Anordnung, dieselbe wie beim Radarstab:</b> das Original
    /// entscheidet am <b>Rumpf</b> <c>byte[Einheit+0x0E]</c> (@0x437FAB,
    /// @0x407F49). Wir lesen das Bauteil aus dem ENTWURF, weil unsere Einheiten
    /// aus der Karte kommen und nie durch den Bauweg des Originals laufen, der
    /// <c>+0x0E</c> füllt. Dasselbe Feld trägt schon
    /// <see cref="RadarKitWeapon"/> = 75, und dort ist die Zuordnung gemessen —
    /// 72, 74 und 75 stehen in derselben Ausrüstungsliste.</para>
    ///
    /// <para>⚠ Sollte sich zeigen, dass 72/74 in unseren Entwürfen NICHT im
    /// Waffenfeld stehen, sagt <c>--bau-check</c> das in seiner ersten Zeile
    /// (»kein Entwurf mit Bauteil …«) — statt still gar nichts zu tun.</para>
    /// </summary>
    private int BuildPartOf(Entity e)
    {
        if (e.IsBuilding || e.IsProp || e.Dead || e.Mark < 0) return 0;
        LoadDesigns();
        int owner = e.Owner is >= 0 and <= 7 ? e.Owner : 0;
        var d = DesignBySlot(e.Mark + 200 * owner);
        if (d is not { } dd) return 0;
        return dd.Weapon is PartBuildingTech or PartGeneratorTech ? dd.Weapon : 0;
    }

    // ================= der Setzmodus =========================================

    /// <summary>Unser <c>dword[0x502ACC]</c>: 0 = aus, sonst 5/6/7.</summary>
    public int PlacementMode { get; private set; }

    /// <summary>Die Einheit, die im Setzmodus wartet. ⚠ Das Original braucht sie
    /// nicht (es nimmt beim Klick die gewählte Einheit aus <c>0x4FA0C8</c>) —
    /// wir merken sie uns, damit ein Auswahlwechsel den Modus nicht auf ein
    /// fremdes Fahrzeug umlenkt.</summary>
    public int PlacementUnit { get; private set; } = -1;

    /// <summary>Was zuletzt bei einem Bauauftrag geschah — für die Leiste.</summary>
    public string BuildOrderNote = "";

    /// <summary>Wieviel gesetzt, wieviel abgelehnt, wieviel wirklich gebaut.
    /// ⚠ Regel 33: ohne die drei Zahlen nebeneinander ist »kein Gebäude« nicht
    /// von »der Befehl kam nie an« zu unterscheiden.</summary>
    public int BuildOrdersPosted, BuildOrdersRefused, BuildingsRaised;

    /// <summary>Den Setzmodus anwerfen — der Knopf.</summary>
    public bool BeginPlacement(int idx, int order)
    {
        if (idx < 0 || idx >= _entities.Count) return false;
        if (BuildTypeOfOrder(order) == 0) return false;
        var e = _entities[idx];
        if (e.Owner != ViewPlayer || e.Dead) return false;
        PlacementMode = order;
        PlacementUnit = idx;
        BuildOrderNote = $"{BuildOrderWord(order)}: Stelle anklicken (Esc bricht ab)";
        return true;
    }

    /// <summary>Den Setzmodus abbrechen. ⚠ Das Original setzt
    /// <c>dword[0x502ACC] = 0</c> unmittelbar nach dem Absenden (@0x438017,
    /// @0x438051, @0x438096) — ein Klick, ein Auftrag.</summary>
    public void CancelPlacement()
    {
        if (PlacementMode == 0) return;
        PlacementMode = 0;
        PlacementUnit = -1;
        ClearBuildPreview();
        QueueRedraw();
    }

    /// <summary>Die Vorschau dem Mauszeiger nachführen, solange der Modus läuft.
    ///
    /// <para>Das ist das, was <c>0x421200</c> mit <c>Merken = 1</c> tut: sie
    /// schreibt jede geprüfte Zelle mit ihrem Ja/Nein nach <c>0xA32188</c>, die
    /// Anzahl nach <c>word[0x502AD0]</c>. Unsere Vorschau
    /// (<see cref="SetBuildPreview"/>) sammelt dieselbe Liste über
    /// <see cref="CanBuild"/> — sie war schon da, sie hatte nur nie einen
    /// Bediener.</para></summary>
    public void PlacementHover(int col, int row)
    {
        if (PlacementMode == 0) return;
        var off = BuildOffsetOfOrder(PlacementMode);
        if (PlacementMode == OrderFieldMine)
        {
            // ⚠ Die Mine hängt nicht am Zeiger, sondern am VORKOMMEN: der Klick
            // wählt eines aus (Kommando 20 schickt seine Nummer, nicht die
            // Zelle). Steht der Zeiger auf keinem, gibt es auch nichts zu
            // zeigen — das ist die ehrlichere Auskunft als ein rotes Rechteck
            // an einer Stelle, die ohnehin nie in Frage kam.
            int k = DepositIndexAt(col, row);
            if (k < 0) { ClearBuildPreview(); return; }
            var d = _deposits[k];
            SetBuildPreview(TypeFieldMine, d.Col + off.X, d.Row + off.Y, skipDeposit: true);
            return;
        }
        SetBuildPreview(BuildTypeOfOrder(PlacementMode), col + off.X, row + off.Y);
    }

    /// <summary>
    /// <b>Der Klick auf die Karte</b> — @0x437FAB, und er verzweigt nach dem
    /// Bauteil des Fahrzeugs auf Kommando 20 oder 21.
    ///
    /// <para>⚠ <b>Für die Mine wird die Vorkommensnummer mitgeschickt</b>, nicht
    /// die Zelle: das Original nimmt sie aus <c>byte[0x81A3A4] − 1</c> und der
    /// Behandler holt sich Spalte und Zeile aus der Vorkommenstafel
    /// (@0x4C32BE). Der Klick wählt also ein VORKOMMEN aus, keine Zelle — wir
    /// suchen dasjenige, in dessen 3×3-Fenster der Klick fällt.</para></summary>
    public bool PlacementClick(int col, int row)
    {
        if (PlacementMode == 0) return false;
        int order = PlacementMode, idx = PlacementUnit;
        // ⚠ Der Modus geht AUS, gleich ob der Befehl durchgeht oder nicht —
        // genau wie im Original, das ihn unmittelbar nach `post` nullt.
        CancelPlacement();

        if (idx < 0 || idx >= _entities.Count)
        { BuildOrderNote = "die Einheit ist weg"; return false; }

        int vorkommen = -1;
        if (order == OrderFieldMine)
        {
            vorkommen = DepositIndexAt(col, row);
            if (vorkommen < 0)
            {
                BuildOrderNote = "dort liegt kein Vorkommen — eine Mine geht nur auf eines";
                BuildOrdersRefused++;
                return false;
            }
        }
        return PostPlaceBuilding(idx, col, row, order, vorkommen);
    }

    /// <summary>Die Nummer des Vorkommens, in dessen 3×3-Fenster diese Zelle
    /// liegt — <c>−1</c>, wenn keines. Dasselbe Fenster wie
    /// <c>CellOnDeposit</c> (@0x4205C0).</summary>
    public int DepositIndexAt(int col, int row)
    {
        var ds = _deposits;
        for (int i = 0; i < ds.Count; i++)
            if (col >= ds[i].Col && col < ds[i].Col + 3 &&
                row >= ds[i].Row && row < ds[i].Row + 3) return i;
        return -1;
    }

    // ================= der Befehl ============================================

    /// <summary>
    /// Kommando <b>20</b> bzw. <b>21</b> absetzen. P1 = Einheit, P2/P3 = Zelle,
    /// P4 = Modus, P5 = Vorkommen.
    ///
    /// <para>⚠ <b>Die Zelle geht EINSBASIG nicht mit.</b> Das Original schiebt
    /// <c>dword[0x5387D0] + 1</c> und zieht bei der Erzeugung wieder ab
    /// (@0x437FD9 gegen @0x408126). Zwei Umrechnungen, die sich aufheben — wir
    /// lassen beide weg und rechnen durchgehend nullbasig. <b>Das ist eine
    /// Abweichung im Zahlenweg, keine im Verhalten</b>, und sie steht hier,
    /// damit niemand die fehlende +1 für einen Fehler hält.</para></summary>
    public bool PostPlaceBuilding(int idx, int col, int row, int order, int deposit)
    {
        if (idx < 0 || idx >= _entities.Count)
        { BuildOrderNote = "keine Einheit gewaehlt"; return false; }
        var e = _entities[idx];
        int part = BuildPartOf(e);
        if (BuildOrdersOfPart(part).Length == 0)
        { BuildOrderNote = "das Fahrzeug kann nichts bauen"; return false; }
        if (e.Owner != ViewPlayer)
        { BuildOrderNote = "das ist nicht Ihre Einheit"; return false; }
        if (System.Array.IndexOf(BuildOrdersOfPart(part), order) < 0)
        { BuildOrderNote = "dieses Fahrzeug baut das nicht"; return false; }

        short op = part == PartGeneratorTech ? CommandOp.PlaceGenerator
                                             : CommandOp.PlaceBuilding;
        if (!Emit(CommandRecord.Make(op, (byte)ViewPlayer, (short)idx,
                                     (short)col, (short)row,
                                     (short)order, (short)deposit)))
        { BuildOrderNote = "der Befehl liess sich nicht absetzen"; return false; }
        BuildOrdersPosted++;
        return true;
    }

    /// <summary>
    /// Kommando 20/21, der Behandler — @0x4C3241.
    ///
    /// <para>Er BAUT nichts. Er merkt den Auftrag in zwei Feldern der Einheit vor
    /// und schickt sie los; gebaut wird bei der Ankunft. Genau diese Trennung ist
    /// der Grund, warum ein Bauauftrag im Original abbrechen kann, ohne dass
    /// etwas entsteht (@0x4080BA: steht die Einheit woanders, wird verworfen).</para>
    ///
    /// <para>⚠ <b>Kommando 21 trägt keinen Modus</b> (@0x438023 schreibt nur
    /// P1..P3). Steht keiner im Satz, ist es der Generator — das ist die
    /// Zuordnung des Absenders, und sie wird hier nachgezogen, damit ein Satz
    /// aus dem Netz oder aus einer Wiederholung dasselbe bedeutet.</para></summary>
    private bool ApplyPlaceBuilding(in CommandRecord c)
    {
        if (_nav == null) return false;
        int i = c.P1;
        if (i < 0 || i >= _entities.Count) return false;
        var e = _entities[i];
        if (e.IsBuilding || e.IsProp || e.Dead || !e.Mobile) return false;

        int order = c.Op == CommandOp.PlaceGenerator ? OrderGenerator : c.P4;
        if (BuildTypeOfOrder(order) == 0) return false;
        if (System.Array.IndexOf(BuildOrdersOfPart(BuildPartOf(e)), order) < 0) return false;

        int ziel, fahrCol, fahrRow;
        if (order == OrderFieldMine)
        {
            // @0x4C32A9 ff: die Nutzlast ist die VORKOMMENSNUMMER, und gefahren
            // wird zu der Zelle, die in der Tafel steht.
            var ds = _deposits;
            int k = c.P5;
            if (k < 0 || k >= ds.Count) return false;
            ziel = k;
            fahrCol = ds[k].Col; fahrRow = ds[k].Row;
        }
        else
        {
            // @0x4C3289: (Zeile << 8) | Spalte. ⚠ Damit passen nur Karten bis
            // 256 Spalten — dieselbe Schranke wie im Original, das dieselbe
            // Packung im Kartenraster 0xBDEA80 benutzt.
            fahrCol = Mathf.Clamp(c.P2, 0, _nav.Width - 1);
            fahrRow = Mathf.Clamp(c.P3, 0, _nav.Height - 1);
            ziel = (fahrRow << 8) | fahrCol;
        }

        // order(Einheit, Spalte, Zeile, 0) — der Fahrauftrag, und er läuft über
        // denselben Weg wie ein Bewegenbefehl. Kommt keine Strecke zustande,
        // wird auch nichts vorgemerkt: ein Fahrzeug, das nie ankommt, hätte
        // sonst einen Auftrag, den niemand mehr sieht.
        var path = _nav.FindPath(new Vector2I(e.Col, e.Row),
                                 new Vector2I(fahrCol, fahrRow), e.Move, i);
        if (path == null || path.Count == 0)
        {
            BuildOrdersRefused++;
            BuildOrderNote = "dorthin fuehrt kein Weg";
            return true;
        }
        e.Target = -1;
        e.Orders.Clear();
        e.Path = path; e.PathIdx = 0;
        e.Goal = new Vector2I(fahrCol, fahrRow);
        e.Reserved = null; e.WaitTime = 0;

        e.BuildTarget = ziel;                              // @0x4C3320
        e.BuildOrder = order;                              // @0x4C3351
        BuildOrderNote = $"{BuildOrderWord(order)}: unterwegs";
        return true;
    }

    // ================= die ANKUNFT ===========================================

    /// <summary>
    /// <b>Auftrag 0, der Leerlauf</b> — @0x407F38 verteilt nach Rumpf, und
    /// @0x40806A / @0x4082DD holen dort den vorgemerkten Bauauftrag ab.
    ///
    /// <para><b>Die Prüfung, die man nicht weglassen darf</b> (@0x4080B6): die
    /// gepackte Zelle im Auftrag muss mit der Zelle übereinstimmen, auf der die
    /// Einheit gerade STEHT. Tut sie es nicht, wird der Auftrag verworfen
    /// (<c>+0x40 = 0</c>, <c>+0x38 = 0</c>) und nichts gebaut. Das ist die
    /// Stelle, an der ein umgelenktes oder abgedrängtes Fahrzeug seinen Auftrag
    /// verliert — ohne sie bauten Fahrzeuge irgendwo.</para>
    ///
    /// <para>Bei der Mine steht statt dessen ein <b>Band</b> (@0x40817C ff):
    /// <c>Vorkommen.Spalte − 2 ≤ Spalte ≤ Vorkommen.Spalte + 4</c>, dasselbe für
    /// die Zeile. Vier Vergleiche, zwei je Achse.</para>
    ///
    /// <para>Und dann, der Reihe nach wie im Original: Platz prüfen → Auswahl
    /// lösen → <b>Fahrzeug entfernen</b> → Gebäude setzen.</para></summary>
    private void BuildArrivalTick()
    {
        if (_nav == null) return;
        // ⚠ Indexschleife über eine SNAPSHOT-Länge: PlaceBuilding hängt dem
        // Gebäude an _entities an. Ein foreach warf hier »Collection was
        // modified« — derselbe Fehler wie in AiEmptyDepots am 18.08.
        int n = _entities.Count;
        for (int i = 0; i < n && i < _entities.Count; i++)
        {
            var e = _entities[i];
            if (e.BuildOrder == 0) continue;
            if (e.Dead) { e.BuildOrder = 0; e.BuildTarget = 0; continue; }
            // Noch unterwegs — der Leerlauf ist noch nicht dran (Auftrag 2).
            if (e.Path != null || e.Orders.Count > 0) continue;

            int order = e.BuildOrder;
            int typ = BuildTypeOfOrder(order);
            var off = BuildOffsetOfOrder(order);
            int col, row;
            bool mine = order == OrderFieldMine;
            int vork = -1;

            if (mine)
            {
                var ds = _deposits;
                int k = e.BuildTarget;
                vork = k;
                if (k < 0 || k >= ds.Count) { e.BuildOrder = 0; e.BuildTarget = 0; continue; }
                // das Band, @0x40817C..0x4081B4: vier Vergleiche, zwei je Achse
                if (e.Col < ds[k].Col - 2 || e.Col > ds[k].Col + 4 ||
                    e.Row < ds[k].Row - 2 || e.Row > ds[k].Row + 4)
                { e.BuildOrder = 0; e.BuildTarget = 0; BuildOrdersRefused++; continue; }
                col = ds[k].Col + off.X; row = ds[k].Row + off.Y;
            }
            else
            {
                // die gepackte Zelle gegen die Standzelle, @0x4080B6
                if (((e.Row << 8) | e.Col) != e.BuildTarget)
                { e.BuildOrder = 0; e.BuildTarget = 0; BuildOrdersRefused++; continue; }
                col = e.Col + off.X; row = e.Row + off.Y;
            }

            e.BuildOrder = 0; e.BuildTarget = 0;

            // ⚠⚠ ZWEI VERSCHIEDENE ZELLEN, und sie zu verwechseln kostet die
            // Mine: das Original prüft den Platz an der VORKOMMENSZELLE
            // (@0x4081E8) und setzt das Gebäude an der ECKE (@0x408236). Für
            // Depot und Generator fallen beide zusammen, für die Mine nicht —
            // sie liegt um (−1,−2) daneben.
            //
            // ⚠ Der Grundrisstest an der Ecke ist UNSERE Zutat: das Original
            // sieht sich für eine Mine das Gelände gar nicht an (0x4205C0 fragt
            // nur die Vorkommenstafel). Wir prüfen ihn trotzdem, weil unser
            // Belegungsraster sonst ein Gebäude über etwas anderes stempelte —
            // eine benannte Abweichung, keine stille.
            if (Patterns == null || !CanBuild(Patterns, typ, col, row, i,
                                              null, skipDeposit: mine))
            {
                BuildOrdersRefused++;
                BuildOrderNote = $"{BuildOrderWord(order)}: die Stelle traegt nichts";
                continue;
            }

            // ⚠⚠ ERST das Fahrzeug weg, DANN das Gebäude — @0x408208 (Auswahl),
            // @0x408211 (entfernen), @0x40822E (setzen). Die Reihenfolge ist
            // nicht gleichgültig: das Fahrzeug steht auf einer der Zellen, die
            // das Gebäude gleich belegt. Bliebe es stehen, prägte der
            // Rumpfabdruck sich in ein besetztes Feld.
            int owner = e.Owner;
            _sel.Remove(i);
            if (_selected == i) _selected = -1;
            _nav.ClearOccupant(e.Col, e.Row, i);
            e.Dead = true;                                  // 0x410E60
            e.Path = null; e.Orders.Clear();

            var bld = PlaceBuilding(Patterns, typ, col, row, owner);
            if (bld == null)
            {
                BuildOrdersRefused++;
                BuildOrderNote = $"{BuildOrderWord(order)}: kein Gebaeudeplatz mehr frei";
                continue;
            }
            // ⚠⚠ DIE MENGE KOMMT AUS DEM VORKOMMENSSATZ, nicht aus der Zelle
            // unter der Gebaeudeecke — @0x408241 liest `word[0x6783EC + 14·v]`.
            // Der Unterschied ist nicht kosmetisch: die Ecke liegt um (−1,−2)
            // NEBEN dem 3x3-Fenster des Vorkommens, `DepositAmountAt` in
            // PlaceBuilding findet dort also nichts, und die Mine bliebe auf
            // ihrem Anfangswert −1 stehen — eine baubare Mine, die nie etwas
            // foerdert. Gemessen hat das --bau-check=mine mit »im Boden -1«.
            if (mine && vork >= 0 && vork < _deposits.Count)
            {
                int menge = _deposits[vork].Amount;
                if (menge > 0) { bld.Deposit = menge; bld.DepositStart = menge; }
            }

            BuildingsRaised++;
            BuildOrderNote = $"{BuildingTypeName(typ)} auf ({col},{row}) gebaut " +
                             "— das Fahrzeug ist dabei aufgegangen";
            UpdateFog();
            QueueRedraw();
        }
    }

    // ================= was die Oberfläche davon braucht =======================

    /// <summary>Das Wort des SPIELS für einen Bauauftrag — Einträge 17, 18 und
    /// 19 der Befehlsliste 0x4FD660. ⚠ Kein selbst erfundenes Wort: der Spieler
    /// soll dasselbe lesen wie im Original.</summary>
    public static string BuildOrderWord(int order) => order switch
    {
        OrderDepot     => OrderWord(17),
        OrderFieldMine => OrderWord(18),
        OrderGenerator => OrderWord(19),
        _              => "",
    };

    /// <summary>Ein Bauauftrag, den die gewählte Einheit anbieten kann.</summary>
    public readonly struct BuildChoice
    {
        public BuildChoice(int index, int order, string word)
        { Index = index; Order = order; Word = word; }
        public int Index { get; }
        public int Order { get; }
        public string Word { get; }
    }

    /// <summary>Die Bauaufträge, die die Auswahl gerade anbietet — leer, wenn
    /// keine Einheit eines baufähigen Bauteils gewählt ist.
    ///
    /// <para>⚠ <b>Das Menü ist das Tor</b>, genau wie beim Radarmast: nur wer das
    /// Bauteil trägt, bekommt den Eintrag überhaupt. Der Behandler prüft es
    /// zusätzlich noch einmal — im Netzspiel ist ein Satz von aussen keine
    /// Aussage über die Wahrheit.</para></summary>
    public List<BuildChoice> BuildChoicesOfSelection()
    {
        var outp = new List<BuildChoice>();
        foreach (int i in _sel)
        {
            if (i < 0 || i >= _entities.Count) continue;
            var e = _entities[i];
            if (e.IsBuilding || e.IsProp || e.Dead || e.Owner != ViewPlayer) continue;
            var orders = BuildOrdersOfPart(BuildPartOf(e));
            if (orders.Length == 0) continue;
            foreach (int o in orders)
                outp.Add(new BuildChoice(i, o, BuildOrderWord(o)));
            break;                       // eine Einheit, eine Leiste
        }
        return outp;
    }

    /// <summary>Einen Bauauftrag aus der Befehlsleiste anwerfen.</summary>
    public bool BeginPlacementFromPanel(int order)
    {
        foreach (var w in BuildChoicesOfSelection())
            if (w.Order == order) return BeginPlacement(w.Index, order);
        BuildOrderNote = "keine baufaehige Einheit gewaehlt";
        return false;
    }

    /// <summary>Eine Zeile über die Bauaufträge — für Prüfstand und Protokoll.</summary>
    public string BuildOrderLine()
        => $"bauauftrag: {BuildOrdersPosted} abgesetzt, {BuildOrdersRefused} abgelehnt, " +
           $"{BuildingsRaised} gebaut" +
           (PlacementMode != 0 ? $"; Setzmodus {PlacementMode} laeuft" : "") +
           (BuildOrderNote.Length > 0 ? $"; {BuildOrderNote}" : "");
}
