using System.Collections.Generic;
using Godot;

namespace AkteEuropaReborn.Rendering;

/// <summary>
/// <b>DIE EINFAHRT — eine Einheit stellt sich in ein Gebaeude unter.</b>
///
/// <para>Gebaut am 30.08.2026. Der Anlass war ein stummer Hilfetext: Text
/// <b>#25</b> (»Einheiten im @Basis @Depot werden automatisch aufgetankt und
/// aufmunitioniert, waehrend Basistechniker Schaeden reparieren. Basen, ebenso
/// wie @Depots, bieten auch Schutz fuer die untergestellten Einheiten.«) kann
/// bei uns nicht feuern, weil seine Bedingung <c>unit_field_any(100, 0x14,
/// == 50)</c> lautet und wir Feld +0x14 (UKOL) nicht gefuehrt haben. Die
/// Ursache war aber nicht das Feld, sondern die fehlende MECHANIK.</para>
///
/// <para><b>Alles Folgende ist gelesen</b>, im Gebaeudetakt <c>0x43CA50</c> der
/// C-Fassung. Satzbasis der Gebaeude <c>0xC06910</c> (= sec3 +0x00), Satzbasis
/// der Einheiten <c>0x6E26C8</c>, Schrittweite 78.</para>
///
/// <list type="number">
/// <item><b>Nur VIER Gebaeudearten nehmen eine Einheit auf.</b> Die Weiche
/// <c>@0x43D5BA</c> liest <c>byte[+0x04]</c> (Typ), Index ueber
/// <c>0x43ECA0</c>, Sprungtafel <c>0x43EC8C</c>. Die Indextafel lautet
/// <c>00 04 04 04 01 02 04 04 04 04 04 03</c> — <b>Typ 1 (Basis) → Arm 0,
/// Typ 5 (Depot) → Arm 1, Typ 6 (Bahnhof) → Arm 2, Typ 12 (Feldbahnhof) →
/// Arm 3</b>, alle uebrigen auf den Leerausgang <c>0x43DC01</c>. Arm 2 und
/// Arm 3 sind woertlich dieselbe Adresse. ⭐ Genau die zwei, die Text #25
/// beim Namen nennt, plus die zwei Bahnhoefe.</item>
///
/// <item><b>Das Tor ist ein Zustandsautomat</b>, drittes Byte jedes Tuersatzes
/// (<c>+0x35</c> Spalte, <c>+0x36</c> Zeile, <c>+0x37</c> Zustand, Schrittweite
/// 3, Anzahl in <c>+0x34</c>). Die Schleife <c>@0x43D2EC..0x43D39A</c> laeuft
/// ueber alle Tueren:
/// <code>
///   Zustand 1..3     -> Zustand-1        (Tafel 0x43EC08 Arm 0x43D317, SCHLIESSEN)
///   Zustand 128..131 -> Zustand+1        (Arm 0x43D31B, OEFFNEN)
///   Zustand 4..127   -> nichts           (Arm 0x43D31F, unbenutzt)
///   Zustand == 0   und Zelle unter 14000        -> Zustand := 0x81 (129)
///   Zustand == 0x84 und Zelle frei (0xFFFE)     -> Zustand := 3
///   Zustand == 0x84 und Zelle traegt eine Einheit mit UKOL == 48 -> Zustand := 3
/// </code>
/// Damit laeuft ein voller Durchgang <b>0 -> 129 -> 130 -> 131 -> 132 -> 3 -> 2
/// -> 1 -> 0</b>. Das Tor oeffnet in drei Takten, steht bei 132 offen und
/// schliesst in drei Takten.</item>
///
/// <item><b>Angemeldet wird an Tuer 0</b> (<c>@0x43D57E</c>, nach der
/// Tuerschleife und mit den Versaetzen der ERSTEN Tuer neu gerechnet):
/// steht dort eine Einheit (imap-Wert unter 8000) und ist ihr <c>UKOL == 0</c>,
/// so wird <c>UKOL := 0x30 (48)</c> und <c>AKCE (+0x15) := Gebaeudenummer</c>
/// gesetzt. ⚠ Ohne Besitzerabgleich und ohne Typtor — das steht so da.</item>
///
/// <item><b>Eingefahren wird im Typarm</b>, und zwar genau dann, wenn
/// <c>UKOL == 48</c> UND <c>Torzustand == 1</c> ist — also im LETZTEN Takt des
/// Schliessens (<c>@0x43D609</c>/<c>0x43D60E</c> fuer die Basis, gleich in den
/// anderen drei Armen). Dann:
/// <code>
///   imap[Tuerzelle] := 0xFFFE                  ; die Einheit raeumt das Feld
///   andocken(cis_typ, id)                      ; 0x43BFC0 / 0x43C630 / 0x43C370
///   UKOL := 0x32 (50) ; AKCE := Gebaeude
/// </code></item>
///
/// <item><b>Die Schlange hat SECHS Plaetze</b> (<c>0x43BFC0</c>: sechs Worte ab
/// <c>0x878E5C + 16*idx</c>, <c>0xFFFF</c> = frei; Depot <c>0x879F3A + 14*idx</c>,
/// Bahnhof <c>0x87917A + 14*idx</c>). Sie ist DIESELBE Liste, aus der
/// »Aussenden« holt und in die eine fertig produzierte Einheit faellt — siehe
/// <see cref="DepotSlots"/>.</item>
///
/// <item><b>Untergestellt heisst unsichtbar</b>: das Zeichnertor
/// <c>@0x4300E2</c> laesst UKOL 50..99 nicht durch. Deckungsgleich mit dem
/// Band, das die Schreiber der 50 setzen.</item>
///
/// <item><b>Untergestellt heisst REPARIERT — und NICHT getankt.</b> Der
/// Dienstblock <c>@0x43E9C5</c> verzweigt Typ 1 → <c>0x43E9E4</c>, Typ 5 →
/// <c>0x43EA3C</c>, Typ 9 → <c>0x43EA96</c> und fasst je Platz genau zwei
/// Felder an: <c>byte[+0x08]</c> (Leben) gegen <c>byte[+0x29]</c> (Hoechstwert),
/// <c>inc</c>. ⚠⚠ <b>Kein Sprit, keine Munition, an keiner der drei Stellen.</b>
/// Der Hilfetext verspricht beides — das Programm tut es nicht. Wir folgen dem
/// PROGRAMM und schreiben den Widerspruch hier hin, statt ihn zu bauen.</item>
/// </list>
///
/// <para><b>⚠ UNSERE SETZUNGEN, einzeln benannt:</b></para>
/// <list type="bullet">
/// <item><b>UKOL 0 leiten wir ab.</b> Das Original fuehrt ein ganzes
/// Auftragsband (Sprungtafel <c>0x40A0D8</c>/<c>0x40A130</c>, 21 belegte Werte
/// von 56 moeglichen). Wir fuehren nur 0/48/50 und lesen »UKOL 0« als
/// <i>steht still und hat keinen Befehl</i> (<c>Path == null</c> und
/// <c>Orders.Count == 0</c>) — dieselbe Bedingung, die <c>BeladeTakt</c> schon
/// benutzt. Ohne das wuerde eine Einheit, die ueber die Tuerzelle nur
/// HINDURCHFAEHRT, angemeldet und sechs Takte spaeter verschluckt; im Original
/// traegt sie dabei UKOL 2 (»fahre«, <c>0x40B070</c>) und faellt darum durch.</item>
///
/// <item><b>Der Besitzerabgleich ist UNSERER.</b> Der Arm der Basis
/// (<c>0x43D5D9..0x43D65B</c>) hat keinen — der ZWEITE Zweig derselben Funktion
/// (<c>0x43D6C0..0x43D71F</c>) prueft dagegen ausdruecklich
/// <c>byte[+0x05] == id/1000</c>. Ohne Abgleich wuerde bei uns eine FEINDLICHE
/// Einheit, die zum Besetzen an der Tuer steht, nach sechs Takten in der Basis
/// verschwinden und die Einnahme abbrechen. Bis das gelesen ist, faehrt nur
/// ein, wem das Gebaeude gehoert. <c>--keine-einfahrt</c> nimmt die ganze
/// Mechanik zurueck.</item>
///
/// <item><b>Die Ueberlaufregel bauen wir NICHT nach.</b> Sind alle sechs
/// Plaetze belegt, faellt <c>0x43BFC0</c> in den Zweig <c>@0x43BFE6</c> und ruft
/// <c>0x410E60</c> — den <b>Einheitenloescher</b> (revier1 §8, revier4). Eine
/// Einheit, die in eine volle Basis faehrt, waere damit weg. Wir lassen sie
/// stattdessen mit UKOL 48 vor der Tuer stehen und zaehlen den Fall in
/// <see cref="GarageVoll"/>. Das ist eine bewusste Abweichung zugunsten des
/// Spielers; sie ist EINE Zeile, falls sie zurueck soll.</item>
/// </list>
/// </summary>
public partial class MapEntityLayer : Node2D
{
    // ---- UKOL, Feld +0x14 ------------------------------------------------
    /// <summary>UKOL 0 — kein Auftrag.</summary>
    public const int UkolFrei = 0;
    /// <summary>UKOL 48 (0x30) — an der Tuer angemeldet, @0x43D5AF.</summary>
    public const int UkolAngemeldet = 0x30;
    /// <summary>UKOL 50 (0x32) — untergestellt, @0x43D657 und sechs weitere.</summary>
    public const int UkolUntergestellt = 0x32;
    /// <summary>
    /// <b>UKOL 51 (0x33) — verlaesst gerade das Gebaeude.</b>
    ///
    /// <para>Gelesen aus <c>0x410420</c> (»Einheit verlaesst das Gebaeude«,
    /// revier1 §): die erste Zuweisung der Routine ist
    /// <c>@0x410441 mov byte[+0x14], 0x33</c>, danach setzt sie die Zelle auf
    /// <c>(Gebaeude.Spalte + Tuerspalte, Gebaeude.Zeile + Tuerzeile)</c> — die
    /// Einheit steht also auf DERSELBEN Tuerzelle, aus der sie kam.</para>
    ///
    /// <para>⭐⭐ <b>Und genau das ist der Riegel gegen das Hin und Her.</b>
    /// Ohne ihn faehrt eine ausgesandte Einheit sofort wieder hinein, denn sie
    /// steht ja auf der Tuerzelle und haette UKOL 0. Gemessen, bevor er da war:
    /// auf Kampagne 10/23/26 in vierzig Sekunden <b>214 / 490 / 214</b>
    /// Einfahrten statt einer Handvoll. UKOL 51 hat im Auftragsband einen
    /// eigenen Arm (<c>0x409B59</c>), der die Einheit wegfaehrt und danach
    /// <c>UKOL := 0</c> setzt (<c>0x409B08</c>); bei uns steht dafuer der
    /// Ausfahrtschritt <c>StepOutOfDoor</c> und die Zuruecksetzung am Anfang
    /// des naechsten <see cref="TorTakt"/>.</para>
    ///
    /// <para>⚠ 51 liegt IM Band 50..99 und ist damit einen Takt lang
    /// unsichtbar — auch das ist das Original, das Zeichnertor @0x4300E2 lautet
    /// woertlich <c>cmp dl,0x32 / jb weiter / cmp dl,0x64 / jb ueberspringen</c>.
    /// </para></summary>
    public const int UkolVerlaesst = 0x33;

    // ---- der Torzustand, Tuersatz +0x02 ----------------------------------
    public const int TorZu = 0;
    /// <summary>Der Anfangswert des Oeffnens, @0x43D35A <c>mov byte[edx], 0x81</c>.</summary>
    public const int TorOeffnetVon = 0x81;
    /// <summary>Offen — der Endwert des Zaehlens, @0x43D34A <c>cmp eax, 0x84</c>.</summary>
    public const int TorOffen = 0x84;
    /// <summary>Der Anfangswert des Schliessens, @0x43D389 <c>mov byte[edx], 3</c>.</summary>
    public const int TorSchliesstVon = 3;
    /// <summary>Der Takt, in dem eingefahren wird — @0x43D60E <c>cmp al, 1</c>.</summary>
    public const int TorEinfahrt = 1;

    /// <summary><c>--keine-einfahrt</c> — der Stand von vor dem 30.08.2026.</summary>
    public static bool KeineEinfahrt;

    /// <summary>Nimmt dieses Gebaeude eine Einheit auf? Die vier Arme der
    /// Indextafel <c>0x43ECA0</c> — Basis, Depot, Bahnhof, Feldbahnhof.</summary>
    public static bool GarageTyp(int bType) => bType is 1 or 5 or 6 or 12;

    /// <summary>Steht diese Einheit in einem Gebaeude? Das Zeichnertor des
    /// Originals @0x4300E2 laesst UKOL 50..99 nicht durch — dasselbe Band, das
    /// die Schreiber der 50 setzen.</summary>
    /// ⚠⚠ <b>UNSERE Einengung, und sie ist gemessen.</b> Das Tor des Originals
    /// lautet woertlich <c>0x32 &lt;= UKOL &lt; 0x64</c>, also 50..99 — und
    /// <b>51 liegt darin</b>. Bei uns darf es das nicht: 51 heisst »verlaesst
    /// gerade das Gebaeude«, und wo das Original diesen Zustand in ein, zwei
    /// Takten durch seinen Auftragsarm <c>0x409B59</c> wieder aufloest, kann er
    /// bei uns stehenbleiben, wenn die Tuer zugestellt ist. Eine Einheit, die
    /// unsichtbar und unanklickbar vor einer verstopften Tuer steht, waere ein
    /// echter Verlust — die Bandgrenze ist darum auf den EINEN Wert eingeengt,
    /// den wir wirklich setzen.
    public static bool Untergestellt(Entity e) => e.Ukol == UkolUntergestellt;

    /// <summary>Wieviele Einfahrten es gab, wieviele an einer vollen Schlange
    /// scheiterten, und wie oft ein Tor aufgegangen ist. ⚠ Ohne diese drei
    /// Zahlen ist »der Bau tut nichts« nicht von »es hat niemand angehalten«
    /// zu unterscheiden.</summary>
    public int Eingefahren, GarageVoll, ToreGeoeffnet;

    /// <summary>Wieviele Plaetze dieses Gebaeude belegt hat — die PRODUZIERTEN
    /// (<see cref="Entity.Depot"/>, Entwurfsnummern) und die EINGEFAHRENEN
    /// (<see cref="Entity.Garage"/>, echte Saetze) zusammen. Im Original ist es
    /// eine einzige Liste von Einheitennummern, darum ist auch der Deckel
    /// einer.</summary>
    public static int GarageBelegt(Entity b) => b.Depot.Count + b.Garage.Count;

    /// <summary>Die Meldezeile eines gewoehnlichen Laufs. Leer, solange nichts
    /// geschehen ist — eine Zeile »0 Einfahrten« in jedem Protokoll waere
    /// Laerm. Zaehlt auch, wer WO steht, damit ein unerwartetes Verschwinden
    /// nicht erst im Spiel auffaellt.</summary>
    public string EinfahrtLine()
    {
        if (Eingefahren == 0 && GarageVoll == 0) return "";
        var wo = new List<string>();
        foreach (var b in _entities)
            if (b.IsBuilding && b.Garage.Count > 0)
                wo.Add($"{BuildingTypeName(b.BType)} {b.Slot} (P{b.Owner}): {b.Garage.Count}");
        return $"einfahrt: {Eingefahren} eingefahren, {GarageVoll} an voller Schlange "
             + $"abgewiesen, {ToreGeoeffnet} Toroeffnungen"
             + (wo.Count > 0 ? "  |  drin: " + string.Join(" · ", wo) : "");
    }

    /// <summary>
    /// <b>DER TORTAKT</b> — einmal je Spieltakt, wie der Gebaeudetakt des
    /// Originals. Er gehoert in <c>SimTick</c> und nicht in die Wirtschaft:
    /// das Tor zaehlt in ORIGINALTAKTEN (drei zum Oeffnen, drei zum
    /// Schliessen), und bei einem Wirtschaftstakt von einer Sekunde stuende
    /// eine Tuer neun Sekunden offen statt einer Zehntelsekunde.
    /// </summary>
    private void TorTakt()
    {
        if (KeineEinfahrt || _nav == null) return;
        // ⭐ ZUERST: wer im vorigen Takt ausgefahren ist, ist fertig damit.
        // Das ist der Arm 0x409B59 des Auftragsbands in einer Zeile — er faehrt
        // die Einheit weg und setzt danach UKOL 0 (@0x409B08). Bei uns tut das
        // Wegfahren `StepOutOfDoor`, und hier faellt die 51 auf 0 zurueck.
        // ⚠ Die Reihenfolge traegt: TorTakt laeuft in SimTick VOR der KI, ein
        // Aussenden der KI wird also fruehestens im naechsten Takt zurueck-
        // gesetzt — genau einen Takt Schutz, und der reicht, weil die Einfahrt
        // sechs braucht.
        foreach (var e in _entities)
        {
            if (e.Ukol != UkolVerlaesst) continue;
            var her = e.InGebaeude;
            // ⚠⚠ 30.08.2026 — HIER STAND `e.Ukol = UkolFrei` OHNE Bedingung,
            // also genau EIN Takt Schutz. Das reicht nicht, und die Messung hat
            // es gnadenlos gezeigt: Kampagne 10/23/26 meldeten mit und ohne den
            // Riegel Zeichen fuer Zeichen dieselben 214 / 490 / 214 Einfahrten
            // in vierzig Sekunden. Ein Takt Schutz gegen eine Einfahrt, die
            // SECHS Takte braucht, aendert nichts — die Einheit stand nach dem
            // Aussenden weiter auf der Tuerzelle und ging wieder hinein.
            // ⭐ Der Zustand endet nicht nach einer Zeit, sondern an einem ORT:
            // erst wenn sie die Tuerzelle verlassen hat. Das ist, was der
            // Auftragsarm 0x409B59 tut — wegfahren, dann UKOL 0.
            if (her != null && e.Col == her.Col + her.DoorCol
                            && e.Row == her.Row + her.DoorRow) continue;
            e.Ukol = UkolFrei;
            e.InGebaeude = null;
        }

        for (int bi = 0; bi < _entities.Count; bi++)
        {
            var b = _entities[bi];
            if (!b.IsBuilding || b.IsProp || b.Dead) continue;
            if (b.Built == 0 || b.DoorCells.Count == 0) continue;

            // Die Zustaende wachsen mit der Tuerzahl mit — der Satz des
            // Originals hat sie schon, unserer bekommt sie beim ersten Takt.
            while (b.TorZustand.Count < b.DoorCells.Count) b.TorZustand.Add(TorZu);

            for (int d = 0; d < b.DoorCells.Count; d++)
            {
                int z = b.TorZustand[d];
                // ---- die Animation, @0x43D2EC..0x43D31D --------------------
                if (z is >= 1 and <= 3) z--;
                else if (z is >= 128 and <= 131) z++;

                int dc = b.Col + b.DoorCells[d].Col, dr = b.Row + b.DoorCells[d].Row;
                int occ = _nav.OccupantAt(dc, dr);
                var auf = occ >= 0 && occ < _entities.Count ? _entities[occ] : null;
                bool einheit = auf != null && !auf.IsBuilding && !auf.IsProp && !auf.Dead;

                // ---- die zwei Umschaltungen, @0x43D344..0x43D389 -----------
                if (z == TorZu)
                {
                    if (occ >= 0) { z = TorOeffnetVon; ToreGeoeffnet++; }
                }
                else if (z == TorOffen)
                {
                    if (occ < 0) z = TorSchliesstVon;
                    else if (einheit && auf!.Ukol == UkolAngemeldet) z = TorSchliesstVon;
                }
                b.TorZustand[d] = z;

                // ---- ab hier nur TUER 0, wie im Original -------------------
                if (d != 0 || !einheit) continue;
                var u = auf!;

                // ⚠ UNSERE Ableitung von UKOL 0 — siehe Kopfkommentar.
                if (u.Ukol == UkolAngemeldet && (u.Path != null || u.Orders.Count > 0))
                    u.Ukol = UkolFrei;                       // sie faehrt wieder
                if (u.Ukol == UkolFrei && u.Mobile
                    && u.Path == null && u.Orders.Count == 0)
                    u.Ukol = UkolAngemeldet;                 // @0x43D5AF

                if (!GarageTyp(b.BType)) continue;
                if (u.Ukol != UkolAngemeldet || z != TorEinfahrt) continue;
                if (u.Owner != b.Owner) continue;            // ⚠ UNSER Tor
                Einfahren(b, occ, u);
            }
        }
    }

    /// <summary>Die Einfahrt selbst — <c>@0x43D61A..0x43D65B</c>.</summary>
    private void Einfahren(Entity b, int ui, Entity u)
    {
        if (GarageBelegt(b) >= DepotSlots)
        {
            // ⚠ Das Original loescht hier die Einheit (@0x43BFE6 -> 0x410E60).
            // Wir lassen sie stehen — siehe Kopfkommentar.
            GarageVoll++;
            return;
        }
        // imap[Tuerzelle] := 0xFFFE. Bei uns ist das die Belegung des
        // Wegegitters, und sie muss BEIDE Zellen loswerden: die eigene und die
        // vorgemerkte, genauso wie beim Tod einer Einheit.
        _nav?.ClearOccupant(u.Col, u.Row, ui);
        if (u.Reserved is { } rc) _nav?.ClearOccupant(rc.X, rc.Y, ui);
        u.Reserved = null;
        u.Path = null;
        u.Orders.Clear();
        u.Target = -1;

        u.Ukol = UkolUntergestellt;      // @0x43D657
        u.InGebaeude = b;
        b.Garage.Add(u);
        Eingefahren++;
        NoteEvent(b, $"{EinheitenWort(u)} untergestellt");
        if (b.Owner == ViewPlayer)
            _order = $"{EinheitenWort(u)} steht jetzt in {BuildingTypeName(b.BType)} "
                   + $"({GarageBelegt(b)}/{DepotSlots})";
        UpdatePanel();
        QueueRedraw();
    }

    /// <summary>Wie eine untergestellte Einheit in der Depotliste heisst.</summary>
    private string EinheitenWort(Entity u)
        => u.Name.Length > 0 ? u.Name : LabelOf(u.UnitType);

    /// <summary>
    /// <b>»Aussenden« fuer eine EINGEFAHRENE Einheit</b> — sie kommt mit ihrem
    /// eigenen Satz zurueck, nicht als Neubau. Das ist der Unterschied zu
    /// <see cref="SendOutOfDepot"/>, wo im Platz nur eine Entwurfsnummer liegt
    /// und die Einheit erst entsteht.
    /// </summary>
    /// <returns>false, wenn an der Tuer kein Platz frei ist.</returns>
    public bool AusfahrenAusGarage(Entity b, int k)
    {
        if (_nav == null || k < 0 || k >= b.Garage.Count) return false;
        var u = b.Garage[k];
        var cell = SpawnCellFor(b);
        if (cell == null)
        {
            _order = $"{EinheitenWort(u)} kann nicht heraus — kein freier Platz an der Tuer";
            return false;
        }
        b.Garage.RemoveAt(k);
        // ⚠ NICHT auf 0, sondern auf 51 — @0x410441. Sonst steht sie auf der
        // Tuerzelle mit UKOL 0 und faehrt im selben Atemzug wieder hinein.
        // Siehe UkolVerlaesst.
        u.Ukol = UkolVerlaesst;
        u.Col = cell.Value.X; u.Row = cell.Value.Y;
        u.Elev = ElevOf(u.Col, u.Row);
        u.Pos = BodyCenterAt(u, u.Col, u.Row);
        u.Footprint = CellRect(_ox, _oy, u.Col, u.Row, u.Elev);
        u.Target = -1; u.Path = null; u.Goal = new Vector2I(u.Col, u.Row);
        int ui = _entities.IndexOf(u);
        if (ui >= 0)
        {
            _nav.SetHull(ui, Simulation.NavGrid.HullSide(u.GameUnitType));
            _nav.SetOccupant(u.Col, u.Row, ui, u.Infantry >= 0);
            StepOutOfDoor(ui, u, b);      // dieselbe Ausfahrt wie beim Neubau
        }
        NoteEvent(b, $"{EinheitenWort(u)} ausgesandt");
        _order = $"{EinheitenWort(u)} ausgesandt ({GarageBelegt(b)}/{DepotSlots} belegt)";
        UpdatePanel();
        QueueRedraw();
        return true;
    }

    // ======================================================================
    //  --einfahrt-check — DER PRUEFSTAND
    // ======================================================================
    //
    // Was er misst: dass eine stehende eigene Einheit auf der Tuerzelle einer
    // eigenen Basis (oder eines Depots/Bahnhofs) nach der GELESENEN Zahl von
    // Takten drin ist, und dass sie danach unsichtbar, unanklickbar und aus dem
    // Wegegitter heraus ist.
    //
    // NULLMODELL: die Zahl der Takte steht VORHER fest und wird nicht aus dem
    // Lauf genommen. Sie folgt aus der BEFEHLSFOLGE des Originals, und die
    // Reihenfolge innerhalb eines Taktes entscheidet:
    //
    //   1. cl = [Zustand] ; 1..3 -> dec ; 128..131 -> inc ; mov [Zustand], cl
    //   2. cx = imap[Tuerzelle] ; al = [Zustand]        <- der NEUE Wert
    //   3. al == 0    und cx < 14000                    -> [Zustand] := 0x81
    //   4. al == 0x84 und (Zelle frei oder UKOL == 48)  -> [Zustand] := 3
    //
    // Takt 1: 0 -> 129        (Schritt 3; UKOL 0 -> 48 kommt danach, @0x43D5AF)
    // Takt 2: 129 -> 130
    // Takt 3: 130 -> 131
    // Takt 4: 131 -> 132, und im SELBEN Takt (Schritt 4) sofort -> 3
    // Takt 5: 3 -> 2
    // Takt 6: 2 -> 1          -> der Typarm sieht die 1 und faehrt ein
    //
    // ⚠⚠ 30.08.2026 BERICHTIGT: hier stand 7. Ich hatte einen Takt gezaehlt, in
    // dem das Tor auf 132 STEHENBLEIBT — den gibt es nicht. Schritt 1 und
    // Schritt 4 liegen im selben Durchgang, und Schritt 4 liest den Wert, den
    // Schritt 1 gerade geschrieben hat. Solange eine Einheit mit UKOL 48 davor
    // steht, ist die 132 in keinem Takt sichtbar. Die 6 ist aus dieser Folge
    // neu abgeleitet, NICHT aus dem Messlauf uebernommen — der bestaetigt sie
    // nur (»Tor 129/130/131/3/2/1«, die 132 taucht auch dort nicht auf).

    /// <summary>Sechs — siehe die Ableitung ueber dieser Zeile.</summary>
    public const int EinfahrtTakte = 6;

    private int _einfahrtCheck = -1, _einfahrtBau = -1, _einfahrtEinheit = -1;
    private int _einfahrtTakt0, _einfahrtVorher;
    private readonly List<string> _einfahrtLog = new();

    /// <summary><c>--einfahrt-check</c> starten.</summary>
    public void EinfahrtCheckStart() { _einfahrtCheck = 0; }

    private void PollEinfahrt()
    {
        if (_einfahrtCheck < 0 || _nav == null) return;
        switch (_einfahrtCheck)
        {
            case 0:
            {
                for (int i = 0; i < _entities.Count; i++)
                {
                    var b = _entities[i];
                    if (!b.IsBuilding || b.IsProp || b.Dead) continue;
                    if (!GarageTyp(b.BType) || b.Built == 0 || b.DoorCells.Count == 0) continue;
                    _einfahrtBau = i; break;
                }
                if (_einfahrtBau < 0)
                {
                    GD.Print("einfahrt-check: auf dieser Karte gibt es weder Basis, "
                           + "Depot, Bahnhof noch Feldbahnhof");
                    _einfahrtCheck = -1; return;
                }
                var geb = _entities[_einfahrtBau];
                geb.Owner = geb.Team = ViewPlayer;
                for (int i = 0; i < _entities.Count; i++)
                {
                    var u = _entities[i];
                    if (u.IsBuilding || u.IsProp || u.Dead || !u.Mobile) continue;
                    _einfahrtEinheit = i; u.Owner = u.Team = ViewPlayer; break;
                }
                if (_einfahrtEinheit < 0)
                { GD.Print("einfahrt-check: keine fahrende Einheit"); _einfahrtCheck = -1; return; }

                var e = _entities[_einfahrtEinheit];
                var tuer = new Vector2I(geb.Col + geb.DoorCol, geb.Row + geb.DoorRow);
                _nav.ClearOccupant(e.Col, e.Row, _einfahrtEinheit);
                if (e.Reserved is { } r0) _nav.ClearOccupant(r0.X, r0.Y, _einfahrtEinheit);
                e.Reserved = null; e.Path = null; e.Orders.Clear(); e.Target = -1;
                e.Col = tuer.X; e.Row = tuer.Y;
                e.Pos = BodyCenterAt(e, e.Col, e.Row);
                e.Footprint = CellRect(_ox, _oy, e.Col, e.Row, ElevOf(e.Col, e.Row));
                e.Elev = ElevOf(e.Col, e.Row);
                _nav.SetOccupant(e.Col, e.Row, _einfahrtEinheit, e.Infantry >= 0);

                _einfahrtVorher = Eingefahren;
                _einfahrtTakt0 = _taktNr;
                _einfahrtLog.Clear();
                GD.Print($"einfahrt-check: {BuildingTypeName(geb.BType)} Platz {geb.Slot} "
                       + $"auf ({geb.Col},{geb.Row}), Tuer ({tuer.X},{tuer.Y}); "
                       + $"{EinheitenWort(e)} daraufgestellt. Erwartet: nach "
                       + $"{EinfahrtTakte} Takten UKOL {UkolUntergestellt}.");
                _einfahrtCheck = 1;
                return;
            }

            case 1:
            {
                var geb = _entities[_einfahrtBau];
                var e = _entities[_einfahrtEinheit];
                int dt = _taktNr - _einfahrtTakt0;
                _einfahrtLog.Add($"   Takt {dt,2}: Tor "
                               + $"{(geb.TorZustand.Count > 0 ? geb.TorZustand[0] : -1),3}"
                               + $"   UKOL {e.Ukol,3}   Schlange {GarageBelegt(geb)}");
                if (Eingefahren > _einfahrtVorher)
                {
                    // ⚠ Die Zeichenfolge ist ein ZWISCHENSPEICHER, den _Draw je
                    // Bild neu aufbaut. Ein kopfloser Lauf zeichnet nicht
                    // zuverlaessig, und der erste Messlauf meldete darum
                    // faelschlich »gezeichnet True« — er las die Liste von
                    // VOR der Einfahrt. Hier wird sie darum ausdruecklich neu
                    // gebaut: gefragt ist das Tor, nicht das Alter des Puffers.
                    BuildUnitDrawOrder();
                    bool gezeichnet = _unitDraw.Contains(_einfahrtEinheit);
                    bool anklickbar = Pick(e.Pos) == _einfahrtEinheit;
                    bool imGitter = _nav.OccupantAt(geb.Col + geb.DoorCol,
                                                    geb.Row + geb.DoorRow) == _einfahrtEinheit;
                    bool takte = dt == EinfahrtTakte;
                    // ⭐ Und die eigentliche Frage: SIEHT ES DIE MISSION? Genau
                    // der Suchlauf des Originals, M2 R29 @0x498C95 —
                    // unit_field_any(100, 0x14, == 50) ueber die Plaetze 0..99
                    // des Spielers. Wenn hier 0 steht, ist das Feld verdrahtet,
                    // aber die Regel findet trotzdem nichts.
                    int gefunden = 0;
                    if (_mscript?.UnitField is { } feld)
                        for (int s = 0; s < 100; s++)
                            if (feld(s, 0x14) == UkolUntergestellt) gefunden++;
                    GD.Print(string.Join("\n", _einfahrtLog));
                    GD.Print($"einfahrt-check: DRIN nach {dt} Takten "
                           + $"(erwartet {EinfahrtTakte}) {(takte ? "OK" : "ABWEICHUNG")}\n"
                           + $"   UKOL {e.Ukol} (erwartet {UkolUntergestellt}), "
                           + $"Schlange {GarageBelegt(geb)}/{DepotSlots}\n"
                           + $"   Suchlauf der Mission (Plaetze 0..99, Feld 0x14 == 50): "
                           + $"{gefunden} — das ist die Bedingung von Hilfetext 25\n"
                           + $"   gezeichnet {gezeichnet} (erwartet False), "
                           + $"anklickbar {anklickbar} (erwartet False), "
                           + $"im Wegegitter {imGitter} (erwartet False)\n"
                           + $"   {(takte && !gezeichnet && !anklickbar && !imGitter ? "GRUEN" : "ROT")}");
                    _einfahrtCheck = 2;
                    return;
                }
                if (dt > EinfahrtTakte + 20)
                {
                    GD.Print(string.Join("\n", _einfahrtLog));
                    GD.Print($"einfahrt-check: ROT — nach {dt} Takten immer noch draussen "
                           + $"(UKOL {e.Ukol}, Tor "
                           + $"{(geb.TorZustand.Count > 0 ? geb.TorZustand[0] : -1)})");
                    _einfahrtCheck = -1;
                }
                return;
            }

            case 2:
            {
                var geb = _entities[_einfahrtBau];
                var e = _entities[_einfahrtEinheit];
                int hp = e.Hp;
                bool raus = AusfahrenAusGarage(geb, 0);
                GD.Print($"einfahrt-check: Aussenden {(raus ? "OK" : "GESCHEITERT")} — "
                       + $"derselbe Satz {(_entities[_einfahrtEinheit] == e ? "JA" : "NEIN")}, "
                       + $"Leben {hp} -> {e.Hp}, UKOL {e.Ukol} "
                       + $"(erwartet {UkolVerlaesst} = verlaesst gerade), "
                       + $"Schlange {GarageBelegt(geb)}/{DepotSlots}");
                _einfahrtCheck = -1;
                return;
            }
        }
    }
}
