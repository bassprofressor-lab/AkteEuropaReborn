using Godot;

namespace AkteEuropaReborn.Rendering;

/// <summary>
/// <b>»GEH MIR AUS DEM WEG« — das dritte Stück der Wegplanung.</b>
///
/// <para>Gebaut am 30.08.2026. Es fehlte seit dem 23.08., und der Einchecker
/// von damals hat es beim Namen genannt: die Suchkarte des Originals ist
/// richtig gelesen — <i>»der Nachbau ist schlechter, solange ihm das dritte
/// Stück fehlt: ein wartender Fahrer muss ausweichen oder neu planen«</i>.
/// Gemessen war das eindeutig: mit der neuen Suchkarte fuhren 96 Einheiten auf
/// map_04 in 120 s nur noch <b>260 Zellen statt 2901</b>. Sie krochen, weil
/// jeder Weg sofort durch einen Nachbarn führte und dort auf eine Einheit
/// wartete, die selbst wartete.</para>
///
/// <para><b>Die Kette des Originals, jetzt ganz gelesen:</b></para>
/// <code>
///   1. Der Fahrer trifft auf eine besetzte Zelle -> Can_go @0x4055D0 gibt 1
///   2. Can_go ruft »geh mir aus dem Weg« @0x404D20 (21 der 22 Rufer)
///   3. Der Blockierer setzt UKOL := 3, AKCE := Richtung und meldet 1
///   4. Der ukol-3-Arm @0x408E45 faehrt ihn im naechsten Takt EINEN Schritt weg
///   5. Der Fahrer kommt durch
/// </code>
///
/// <para><b>Schritt 2 bis 4 gab es bei uns nicht.</b> Wir haben gewürfelt und
/// den FAHRER neu geplant; der Blockierer wurde nie gefragt.</para>
///
/// <para><b>@0x404D20 — wer weicht aus</b> (berichte/revier1.md §6, hier nur
/// der Fahrzeugzweig):</para>
/// <code>
///   +0x0F == 0xAB          -> nein   ; dieser Antrieb weicht nie aus
///   rand() % 50 == 13      -> nein   ; 2 % blosse Verweigerung, VOR allem anderen
///   Gattung (+0x0A) > 2    -> nein   ; Schiffe weichen nicht aus
///   POHYB (+0x04) != 0xFF  -> ja     ; faehrt schon
///   OTACIM (+0x16) != 0    -> ja     ; dreht schon
///   UKOL == 4 (Angriff)    -> nein   ; bleibt stehen
///   UKOL == 2 und AKCE == 0-> ja
///   UKOL != 0              -> nein   ; (und bei UKOL 3: UKOL := 0)
///   sonst: UKOL := 3 ; AKCE := Richtung ; ja
/// </code>
///
/// <para><b>@0x408E45 — der Ausweichschritt.</b> Drei Versuche; je Versuch wird
/// aus der Tafel <c>0x4F5B10</c> (drei Byte je Richtung) ein Kandidat gezogen,
/// der Index ist <c>(rand() + versuch) % 3</c>. Die Zelle muss auf der Karte
/// liegen, ihre imap muss <c>0xFFFE</c> sein und ihr Lagenbyte ungleich 99.
/// Dann ein Schritt dorthin.</para>
///
/// <para><b>Die Tafel selbst</b>, aus der EXE gelesen — je Fahrtrichtung des
/// Fahrers die drei Ausweichrichtungen, und das Muster ist SEITWÄRTS:</para>
/// <code>
///   akce 0 (Sued)  -> 2 6 0   (West, Ost, Sued)
///   akce 1         -> 2 0 0     akce 5         -> 4 6 6
///   akce 2 (West)  -> 4 0 2   (Nord, Sued, West)
///   akce 3         -> 2 4 4     akce 6 (Ost)   -> 0 4 6
///   akce 4 (Nord)  -> 2 6 4     akce 7         -> 6 0 6
/// </code>
/// </summary>
public partial class MapEntityLayer : Node2D
{
    /// <summary>UKOL 3 — AUSWEICHEN. Der Arm dazu ist <c>0x408E45</c>;
    /// gesetzt wird er in »geh mir aus dem Weg« <c>@0x404D20</c>.</summary>
    public const int UkolAusweichen = 3;

    /// <summary>Die Ausweichtafel <c>0x4F5B10</c>, drei Byte je Richtung —
    /// Zeile für Zeile aus der EXE gelesen, nicht gerechnet.</summary>
    private static readonly int[,] AusweichTafel =
    {
        { 2, 6, 0 }, { 2, 0, 0 }, { 4, 0, 2 }, { 2, 4, 4 },
        { 2, 6, 4 }, { 4, 6, 6 }, { 0, 4, 6 }, { 6, 0, 6 },
    };

    /// <summary>Die 2-%-Verweigerung: <c>rand() % 50 == 13</c> @0x404D20.
    /// ⚠ Sie sitzt VOR allem anderen — auch eine ausweichbereite Einheit sagt
    /// in einer von fünfzig Anfragen nein.</summary>
    private const int AusweichVerweigerung = 50;

    /// <summary>
    /// <c>--kein-ausweichen</c> — die Gegenprobe. Der Bau selbst ist seit dem
    /// 31.08.2026 <b>an</b>.
    ///
    /// <para><b>Die Geschichte in drei Messungen</b>, alle auf map_DM_4, 60 s,
    /// auf dem trockengelegten Pruefstand:</para>
    /// <code>
    ///   30.08.  ein Lauf              19 gegen 21 ans Ziel   -> "schadet"
    ///   31.08.  vier Keime            19,25 gegen 19,25 ans Ziel,
    ///                                 aber 1697 gegen 1539 gefahren, 4 von 4
    ///                                 gleichgerichtet         -> AUS
    ///   31.08.  nach dem Fix unten,
    ///           elf Keime             19,82 gegen 20,82 ans Ziel (+1,0),
    ///                                 1708 gegen 1717 gefahren -> NEUTRAL
    /// </code>
    ///
    /// <para>⚠ <b>Die mittlere Zeile war der Fehler, und er lag NICHT in der
    /// Lesung.</b> Der Aufrufer <c>@0x408B88</c> hat zwei Ausstiege, und beide
    /// stehen bei uns seit dem 15.08. korrekt gebaut da (<c>GiveWayOdds = 60</c>
    /// und <c>BlockRearm = 40 / Spread = 20</c>). Der Einbau vom 30.08. hat den
    /// ersten davon <b>kurzgeschlossen</b>: bei einer Zusage sprang er mit einem
    /// blanken <c>return</c> heraus und uebersprang den 1/60-Wurf. Aus 24340
    /// Zusagen wurden 24340 Dauerblockaden. Im Original ERZEUGT die Antwort den
    /// Rueckgabewert (<c>@0x405BB0</c>: <c>cmp eax,1 / sbb eax,eax / inc eax</c>)
    /// und WAEHLT damit zwischen den beiden Ausstiegen — sie ersetzt keinen.</para>
    ///
    /// <para>⚠⚠ <b>Warum es trotzdem an ist, obwohl die Zahl nichts sagt.</b>
    /// Über elf Keime sind es +1,0 Einheiten ans Ziel bei einer Streuung der
    /// Differenzen von ±2,9 — also <b>nicht gesichert</b> (sechs besser, zwei
    /// gleich, drei schlechter). Die ersten vier Keime hatten +2,25 gezeigt; das
    /// war Ueberanpassung an genau die Keime, an denen gebaut wurde. <b>Es ist
    /// also nicht der Nutzen, der entscheidet, sondern die Treue:</b> die
    /// Mechanik ist gelesen (<c>0x404D20</c>, Tafel <c>0x4F5B10</c>), sie ist
    /// nachweislich nicht mehr schaedlich, und die Kampagne soll das Original
    /// sein. Waere sie messbar schlechter, bliebe sie aus.</para>
    ///
    /// <para>⚠ <b>Ungemessen bleibt die Buendnispruefung</b>
    /// (<see cref="NeutokErlaubtFragen"/>, aus <c>0x4054D0</c>): auf map_DM_4
    /// meldete sie in allen elf Laeufen <c>feind 0</c> — es kam nie ein Gegner
    /// in den Weg. Sie ist gebaut und belegt, aber <b>nicht erprobt</b>. Eine
    /// Karte, auf der sich die Fronten mischen, muesste das nachholen.</para></summary>
    public static bool AusweichenAn = true;

    /// <summary>GEGENPROBE <c>--ausweichen-springt</c>: der Stand bis zum
    /// 03.09.2026 — der Ausweichschritt SETZT die Einheit um, statt sie zu
    /// fahren. Das war der gemeldete »Teleport«; siehe den Rechenort und den
    /// Sprungwaechter in MapEntityLayer.</summary>
    public static bool AusweichenSpringt;

    /// <summary>Bequemlichkeit: der alte Name, damit die Abfragen unten lesbar
    /// bleiben.</summary>
    private static bool KeinAusweichen => !AusweichenAn;

    /// <summary>Wie oft gefragt, wie oft zugesagt, wie oft ein Schritt wirklich
    /// getan wurde. ⚠ Ohne die drei Zahlen ist »das Ausweichen tut nichts«
    /// nicht von »es wurde nie gefragt« zu unterscheiden.</summary>
    public int AusweichGefragt, AusweichZugesagt, AusweichSchritte, AusweichEng;

    /// <summary>Wie oft gar nicht erst gefragt wurde, weil der Blockierer kein
    /// Verbuendeter ist — der Nachbau von <c>0x4054D0</c>. ⚠ Ohne diese Zahl
    /// ist »die Buendnispruefung greift« nicht von »es gab nie einen Feind im
    /// Weg« zu unterscheiden.</summary>
    public int AusweichFeind;

    /// <summary>
    /// <b>Der Nachbau von <c>neutok[a][b] != 0</c></b> — der Buendnismatrix
    /// <c>0x87B155 + a*40 + b</c>, die <c>set_relation</c> @0x4CF6D0 symmetrisch
    /// beschreibt. Ab Werk steht dort nur die Diagonale: jeder ist jedem
    /// feindlich, bis ein Missionsskript Frieden macht.
    ///
    /// <para>⚠ UNSERE Entsprechung ist <c>_allied</c>; sie kommt aus derselben
    /// Sektion (sec53) und ist damit dieselbe Tafel. Der Unterschied zu
    /// <see cref="IsHostile"/> ist Absicht: dort haengen noch Sonderfaelle mit
    /// dran (Untergestellte, Standby), die an DIESER Stelle im Original nicht
    /// gefragt werden.</para></summary>
    private bool NeutokErlaubtFragen(Entity fahrer, Entity block)
    {
        if (fahrer.Owner is < 0 or > 7 || block.Owner is < 0 or > 7) return false;
        return _haveAllies ? _allied[fahrer.Owner, block.Owner]
                           : block.Owner == fahrer.Owner;
    }

    /// <summary>
    /// <b>Den Blockierer fragen</b> — der Nachbau von <c>0x404D20</c>.
    /// </summary>
    /// <param name="blockIdx">Wer im Weg steht.</param>
    /// <param name="richtung">Die Fahrtrichtung des Fahrers, 0…7 nach der
    /// Richtungstafel <c>0x4F5AF0</c>. Sie wird zu <c>AKCE</c>.</param>
    /// <returns>true, wenn der Blockierer ausweicht oder ohnehin gleich
    /// weiterfährt.</returns>
    private bool AusweichenAnfragen(int fahrerIdx, int blockIdx, int richtung)
    {
        if (KeinAusweichen) return false;
        if (blockIdx < 0 || blockIdx >= _entities.Count) return false;
        if (fahrerIdx < 0 || fahrerIdx >= _entities.Count) return false;
        var b = _entities[blockIdx];
        if (b.Dead || b.IsBuilding || b.IsProp) return false;

        // ⭐⭐⭐ 31.08.2026 — MAN BITTET NUR VERBUENDETE. Gelesen in
        // 0x4054D0, dem Test, den Can_go VOR der Anfrage macht:
        //
        //   imap < 8000     -> neutok[fahrer/1000][ziel/1000]   (Buendnismatrix)
        //   imap < 14000    -> pratelska_infa 0x433FE0          (Infanteriezelle)
        //   sonst           -> 0                                (nie fragen)
        //
        // Gibt dieser Test 0, kehrt Can_go mit 0 zurueck, OHNE 0x404D20 auch
        // nur zu rufen — und beim Aufrufer greift dann der Geduldszweig statt
        // des 1/60-Wurfs. Einen Feind bittet man nicht zur Seite, man plant um
        // ihn herum. ⚠ Das fehlte uns: wir haben JEDEN gefragt.
        if (!NeutokErlaubtFragen(_entities[fahrerIdx], b)) { AusweichFeind++; return false; }

        AusweichGefragt++;

        // ⚠ Die Verweigerung steht VOR allem anderen — genau wie im Original.
        if (Simulation.Determinism.Roll(AusweichVerweigerung) == 13) return false;

        // Gattung > 2 weicht nie aus (Schiffe). ⚠ Unsere Entsprechung ist
        // GameUnitType; die Gattung 3 kommt auf allen 54 Karten zehnmal vor.
        if (b.GameUnitType > 2) return false;
        if (!b.Mobile || b.DugIn) return false;

        // »faehrt schon« / »dreht schon« -> der Fahrer soll einfach warten.
        if (b.Path is { Count: > 0 } || b.Orders.Count > 0) { AusweichZugesagt++; return true; }

        if (b.Target >= 0) return false;                 // UKOL 4: greift an
        if (b.Ukol == UkolAusweichen) { b.Ukol = UkolFrei; return false; }
        if (b.Ukol != UkolFrei) return false;

        b.Ukol = UkolAusweichen;
        b.AusweichRichtung = richtung;
        AusweichZugesagt++;
        return true;
    }

    /// <summary>
    /// <b>Der Ausweichschritt</b> — der Nachbau des ukol-3-Arms
    /// <c>@0x408E45</c>. Einmal je Spieltakt, wie der Einheitentakt des
    /// Originals.
    /// </summary>
    private void AusweichTakt()
    {
        if (KeinAusweichen || _nav == null) return;
        for (int i = 0; i < _entities.Count; i++)
        {
            var e = _entities[i];
            if (e.Ukol != UkolAusweichen) continue;
            if (e.Dead || !e.Mobile)
            {
                e.Ukol = UkolFrei;
                continue;
            }
            // Wer inzwischen wieder einen Befehl hat, weicht nicht mehr aus.
            if (e.Path is { Count: > 0 } || e.Orders.Count > 0)
            {
                e.Ukol = UkolFrei;
                continue;
            }

            int akce = e.AusweichRichtung & 7;
            bool getan = false;
            for (int versuch = 0; versuch < 3 && !getan; versuch++)
            {
                int r = (Simulation.Determinism.Roll(3) + versuch) % 3;
                int idx = AusweichTafel[akce, r] & 7;
                var d = Simulation.NavGrid.UrDirs[idx];
                int c = e.Col + d.X, w = e.Row + d.Y;
                if (!_nav.InBounds(c, w)) continue;
                // ⚠ Das Original prueft hier die imap auf 0xFFFE und das
                // Lagenbyte auf != 99 — also FREI und keine Bruecke. Unsere
                // Entsprechung ist `IsFree` fuer die eigene Bewegungsklasse;
                // sie nimmt zusaetzlich den Rumpf mit, was das Original an
                // dieser Stelle nicht tut (es kennt hier nur die eine Zelle).
                if (!_nav.IsFree(c, w, e.Move, i)) continue;

                if (e.Reserved is { } rc) _nav.ClearOccupant(rc.X, rc.Y, i);
                e.Reserved = null;

                if (AusweichenSpringt)
                {
                    // GEGENPROBE --ausweichen-springt: der Stand bis zum
                    // 03.09.2026 — die Einheit wird UMGESETZT.
                    _nav.ClearOccupant(e.Col, e.Row, i);
                    e.Col = c; e.Row = w;
                    e.Elev = ElevOf(c, w);
                    e.Pos = BodyCenterAt(e, c, w);
                    e.Footprint = CellRect(_ox, _oy, c, w, e.Elev);
                    e.Facing = idx;
                    _nav.SetOccupant(c, w, i, e.Infantry >= 0);
                }
                else
                {
                    // ⭐⭐ 03.09.2026 — DER AUSWEICHSCHRITT WIRD GEFAHREN, NICHT
                    // GESPRUNGEN. Gemeldet (01.09. und wieder am 03.09.):
                    // »Einheiten teleportieren«. Der Sprungwaechter hat es
                    // gezeigt: eine ganze Zelle in EINEM Takt, und dabei
                    // »Weg keiner, Reserviert -, Schritt 0/0« — also niemand,
                    // der faehrt. Das war genau diese Stelle.
                    //
                    // Das Original @0x408E45 macht es anders, und die drei
                    // Zeilen am Ende des Armes sagen es:
                    //
                    //   0x408F22  call 0x4013C5 -> 0x404E80   erst DREHEN; ist
                    //                                         er nicht fertig,
                    //                                         geschieht in
                    //                                         diesem Takt gar
                    //                                         nichts
                    //   0x408F42  mov byte [edi+4], al        POHYB := Richtung
                    //   0x408F46  call 0x401235 -> 0x4052D0   stempelt die
                    //                                         ZIELZELLE in die
                    //                                         imap (@0x405355
                    //                                         mov word[edx], di)
                    //
                    // ⭐ @0x4052D0 fasst +0x00 und +0x01 — Spalte und Zeile —
                    // NICHT an. Die Einheit steht also weiter auf ihrer Zelle
                    // und wird vom gewoehnlichen Fahrer hinuebergefahren, mit
                    // Drehung und mit `kolik`. Bei uns ist das ein Weg von
                    // GENAU EINEM Schritt; Vormerkung, Drehung und Fortschritt
                    // macht dann der Fahrer, wie bei jedem anderen Schritt auch.
                    e.Path = new System.Collections.Generic.List<Vector2I> { new(c, w) };
                    e.PathIdx = 0;
                    e.Goal = new Vector2I(c, w);
                    e.StepCost = 0;          // frischer Anlauf, kein geerbter Rest
                }
                AusweichSchritte++;
                getan = true;
            }
            if (!getan) AusweichEng++;      // alle drei Kandidaten zu
            e.Ukol = UkolFrei;              // der Auftrag ist damit erledigt
        }
    }

    /// <summary>Die Meldezeile. Leer, solange niemand gefragt hat.</summary>
    public string AusweichLine()
        => AusweichGefragt == 0 && AusweichFeind == 0 ? ""
         : $"ausweichen: {AusweichGefragt} gefragt, {AusweichZugesagt} zugesagt, "
         + $"{AusweichSchritte} Schritte getan, {AusweichEng}x war es rundum zu, "
         + $"{AusweichFeind}x GAR NICHT gefragt (kein Verbuendeter); "
         + $"{GiveWayGewartet}x vor einer besetzten Zelle GEWARTET statt neu geplant";
}
