namespace AkteEuropaReborn.Rendering;

using System.Collections.Generic;
using Godot;

/// <summary>
/// ⭐⭐⭐ <b>DIE LANDMINE</b> — »Mines and traps« des Originals (24.08.2026).
///
/// <para>Gebaut auf Ansage: »bau mir die Mine für den Minenleger«. Und die
/// Rückfrage »solltest ja alle Daten vom Original haben oder?« hat eine Antwort
/// mit Adressen: <b>ja, den Kern vollständig</b>. Alles unten ist gelesen, und
/// wo es das nicht ist, steht es dabei.</para>
///
/// <para><b>Die Kette im Original, Stelle für Stelle:</b></para>
/// <list type="number">
/// <item><b>Der eigene Taktblock.</b> Die Hauptschleife druckt vor jedem Block
/// seinen Namen; bei <c>0x416506</c> steht <c>"Mines and traps"</c>
/// (@0x4F7B5C), und der Aufruf danach (<c>0x416513</c>, über den Thunk
/// <c>0x401ED8</c>) ist <b>@0x4216F0</b>. Der Block liegt zwischen zwei anderen
/// — er läuft <b>jeden Takt</b>.</item>
///
/// <item><b>Das Feld.</b> <b>500</b> Plätze (<c>cmp si, 0x1F4</c>) zu je
/// <b>6 Byte</b> ab <b>0x552E18</b>:
/// <c>+0x00 Spalte · +0x01 Zeile · +0x02 · +0x03 · +0x04 belegt ·
/// +0x05 Leger</c>.
/// ⚠⚠ <b>BERICHTIGT am 15.09.2026: +0x02 und +0x03 sind NICHT tot.</b> Die
/// Legeroutine würfelt sie (<c>rand%20+10</c>, <c>rand%10+5</c>), und der
/// Zeichenlistenbauer <c>0x42F2B0</c> liest sie über den Satzzeiger
/// (<c>[ebp−2]</c>/<c>[ebp−1]</c>, C <c>0x42F346</c>/<c>0x42F372</c>) — die Lage
/// der Mine in ihrer 40×20-Zelle. Der alte Scan sah nur die absolute Form.
/// Sie werden jetzt gewürfelt und gespeichert (<see cref="Mine.LageX"/>);
/// gezeichnet wird noch nicht. berichte/minen-opus.md §2.1.</item>
///
/// <item><b>Das Legen</b> (@0x421940, Thunk 0x401302, <b>18 Rufer</b> — der
/// Minenleger @0x408858 und 17 im SETUP der Missionen 15, 20, 25; berichtigt
/// 15.09.2026): die Zielzelle muss in der imap <c>0xFFFE</c> (leer) sein oder
/// einen Wert <c>≤ 8000</c> tragen (dort steht ein FAHRZEUG) — <b>unter ein
/// Fahrzeug darf man legen</b>, auf rauen Boden, Wasser, Gebäude und unter
/// Fussvolk (imap 10000+) nicht. Dann der erste freie der 500 Plätze; ist keiner
/// frei, druckt es <c>"Too many mines"</c> (@0x4F9198) und legt nichts.</item>
///
/// <item><b>Der Schuss des Minenlegers</b> (@0x4087D0…0x408892): Reichweite
/// prüfen, Nachladezähler <c>+0x32</c> muss 0 sein, dann <b>Mine legen</b>,
/// <b>Klang 28 oder 29</b> (<c>0x1C + rand&amp;1</c>), <c>+0x32 = +0x3D</c>
/// (Nachladezeit) und <c>--byte[+0x39]</c> (Munition). Kein Geschoss, kein
/// Schaden — der »Schuss« IST das Legen.</item>
///
/// <item><b>Das Auslösen</b> (@0x421716…0x4217B6): für jede belegte Mine —
/// <list type="bullet">
///   <item>imap der Zelle <c>&lt; 8000</c>, also <b>eine Einheit steht darauf</b>;</item>
///   <item>ihr Spieler ist <c>imap / 1000</c> (1000 Plätze je Spieler), und die
///   Diplomatietafel <c>byte[opfer + leger·40 + 0x87B155]</c> muss <b>0</b>
///   sein — <b>eigene und verbündete Einheiten lösen nicht aus</b>;</item>
///   <item>ihr Feld <c>+0x0E</c> darf nicht <c>0x44</c> (68) sein — das ist die
///   Ausnahme, und für die FALLE steht zwei Schleifen weiter <c>0x45</c> (69);</item>
///   <item>⚠ <c>word[+0x06] &lt; 0</c> — siehe <see cref="MinenTor"/>;</item>
///   <item>dann <c>0x40C9A0(40050, opfer)</c> und die Mine wird geleert.</item>
/// </list></item>
///
/// <item><b>Der Schaden ist 50, und das ist gerechnet, nicht geschätzt.</b> Die
/// Trefferroutine @0x40C9A0 nimmt als »Schützen« eine Kennung; ab
/// <c>0x9C40</c> (40000) bis <c>0xA028</c> (41000) rechnet sie @0x40CCA4
/// <c>schaden = kennung + 0x63C0</c> in 16 Bit. Die Legestelle übergibt
/// <c>0x9C72</c> = 40050, und <c>40050 + 25536 = 65586 ≡ <b>50</b></c>.
/// Die Kennung <b>40000 + D</b> heisst also »Schaden D, ohne Schützen«.</item>
/// </list>
///
/// <para>⚠⚠ <b>Was NICHT gebaut ist und warum:</b> die <b>FALLE</b> (zweites
/// Feld @0x688B58, gleiche Form, Ausnahme 0x45, Wirkung Geschwindigkeit 0) —
/// in Kampagne 15 kommt keine vor. Das ZEICHNEN (Art 15, ANIM 93/73) ist
/// gelesen, aber noch nicht gebaut. Das RÄUMEN ist seit dem 15.09.2026 gebaut
/// (<see cref="MinenRaeumen"/>). Gelesen in berichte/minen-opus.md.</para>
/// </summary>
public partial class MapEntityLayer
{
    /// <summary>Wieviele Minen gleichzeitig liegen können — <c>cmp si, 0x1F4</c>
    /// @0x4217BB und dieselbe Schranke beim Legen @0x421987.</summary>
    public const int UrMinen = 500;

    /// <summary>Der Schaden einer Mine. Gerechnet aus der Kennung 0x9C72, die
    /// die Legestelle @0x42179A übergibt: <c>40050 + 0x63C0 ≡ 50</c>.</summary>
    public const int MinenSchaden = 50;

    /// <summary>Das Bauteil des Minenlegers. Entwurfswaffe <b>15</b> bildet über
    /// <c>TurretOf</c> auf Bauteil <b>35</b> ab; die Bauteiltafel führt dort
    /// »Minenleger«.</summary>
    public const int MinenLegerTeil = 35;

    /// <summary>Der Fallenleger — Tafelzeile 16, Bauteil 36. Er laeuft durch
    /// DENSELBEN Code wie der Minenleger (Zweig @0x4088DC gegen @0x408790)
    /// und benutzt dieselben Felder +0x40/+0x48/+0x49.</summary>
    public const int FallenLegerTeil = 36;

    /// <summary>Der Gaswerfer — Tafelzeile 9, Bauteil 29. Gaswolken sind bei
    /// uns nicht gebaut (das dritte Feld @0x677F30 mit 200 Plaetzen).</summary>
    public const int GaswerferTeil = 29;

    /// <summary>Das Feld <c>+0x0E</c>, mit dem eine Einheit MINEN nicht auslöst
    /// (@0x421786 <c>cmp dl, 0x44</c>). Die FALLE prüft zwei Schleifen weiter
    /// gegen <c>0x45</c> — zwei Werte, zwei Fallenarten, das passt
    /// zusammen.</summary>
    public const int MinenImmun = 0x44;

    /// <summary>Die zwei Klangnummern beim Legen: <c>0x1C + (rand &amp; 1)</c>
    /// @0x408876.</summary>
    public const int MinenKlang = 28;

    /// <summary>Ein Minenplatz, Satz von 6 Byte (sec84). Die zwei Zufallsbytes
    /// <c>+0x02</c>/<c>+0x03</c> sind die Lage in der Zelle — siehe Klassenkopf.
    /// </summary>
    public struct Mine
    {
        public int Col, Row, Player;
        public bool Aktiv;
        /// <summary>+0x02 / +0x03: die Lage in der Zelle in Bildpunkten,
        /// <c>rand%20+10</c> und <c>rand%10+5</c> (@0x421940). Liest der
        /// Zeichenlistenbauer 0x42F2B0.</summary>
        public int LageX, LageY;
    }

    private readonly Mine[] _minen = new Mine[UrMinen];

    /// <summary>Wieviele Minen gelegt wurden, wie oft kein Platz mehr war
    /// (»Too many mines«), und wieviele hochgegangen sind.</summary>
    public int MinenGelegt, MinenKeinPlatz, MinenAusgeloest;

    /// <summary>Wie oft eine Mine NICHT auslöste, weil die Einheit befreundet
    /// war, weil sie die Ausnahme trägt, oder weil das Tor
    /// (<see cref="MinenTor"/>) zu war. Getrennt gezählt — »die Mine ging nicht
    /// hoch« hat drei Gründe, und eine Summe verwischt sie.</summary>
    public int MinenFreund, MinenImmunFall, MinenTorZu;

    /// <summary>Wie oft Fussvolk auf einer Mine stand und NICHT ausloeste — der
    /// Takt verlangt imap &lt; 8000, Fussvolkzellen tragen 10000..13999.</summary>
    public int MinenFussvolk;

    /// <summary>Wen die ausgeloesten Minen getroffen haben (Einheitenindex) — fuer die
    /// Pruefstaende, die sonst fremden Beschuss fuer Minenschaden halten.</summary>
    public readonly List<int> MinenOpfer = new();

    /// <summary>Wieviele Minen ein Minenraeumer beim Ueberfahren entfernt hat
    /// (0x421E10, Klang 40), und wieviele beim Legen unter einem Fahrzeug
    /// landeten.</summary>
    public int MinenGeraeumt, MinenUnterFahrzeug;

    /// <summary><c>--minen-ausnahme-alt</c> — der Stand bis 15.09.2026: die
    /// Raeumer-Ausnahme liest <c>Equipment</c> (+0x10) statt <c>Part</c>
    /// (+0x0E); der Minenraeumer von Kampagne 15 loest dann selbst aus.</summary>
    public static bool MinenAusnahmeAlt;

    /// <summary><c>--minen-legetor-alt</c> — der Stand bis 15.09.2026: gelegt
    /// wird auch auf rauen Boden, Wasser und unter Fussvolk.</summary>
    public static bool MinenLegetorAlt;

    /// <summary><c>--missionsminen-aus</c> — der Stand bis 15.09.2026: die
    /// Minenfelder des SETUP-Blocks werden nicht gelegt.</summary>
    public static bool MissionsminenAus;

    /// <summary><c>--minenraeumen-aus</c> — der Stand bis 15.09.2026: der
    /// Minenraeumer entfernt nichts.</summary>
    public static bool MinenraeumenAus;

    /// <summary><c>--minen-tor-alt</c> — der Stand bis 15.09.2026: ausgeloest
    /// wird von jeder FAHRENDEN Einheit auf der Zelle (<c>Path != null</c>),
    /// auch von Fussvolk, auch beim Anfahren und Wegfahren.</summary>
    public static bool MinenTorAlt;

    /// <summary>
    /// ⚠⚠ <b>DAS EINE, WAS ICH NICHT VERSTANDEN HABE.</b>
    ///
    /// <para>Der Auslöser verlangt @0x42178B <c>word[einheit + 0x06] &lt; 0</c>,
    /// und die FALLE verlangt dasselbe. <c>+0x06</c> heisst laut dem eigenen
    /// Auszug des Spiels <c>KOLIK</c> — »wieviel«, der Fortschritt in der Zelle,
    /// der beim Fahren hochzählt. Warum er NEGATIV sein muss, ist nicht
    /// gelesen.</para>
    ///
    /// <para>⚠ Ein roher Scan findet für <c>+0x06</c> genau EINE schreibende
    /// Stelle (@0x410496, die Aufstellroutine, setzt <b>0</b>) — aber der Scan
    /// sieht nur die Form <c>[reg + 0x6E26CE]</c> und wäre blind für ein
    /// <c>[esi + 6]</c> auf einen schon berechneten Satzzeiger. <b>Er ist eine
    /// Untergrenze, kein Beweis</b> (Arbeitsweise, Regel 7).</para>
    ///
    /// <para><b>Was hier gebaut ist und dass es UNSERE Deutung ist:</b> das Tor
    /// steht auf »die Einheit ist in Bewegung«. Das passt zu einem Fortschritt,
    /// der zwischen zwei Zellen läuft, und es passt zum Spielgefühl — man fährt
    /// auf eine Mine. ⚠ Es ist erschlossen. <c>--minen-ohne-tor</c> nimmt es
    /// weg, und <see cref="MinenTorZu"/> zählt, wie oft es gegriffen hat: eine
    /// Deutung, die nie etwas ändert, ist keine.</para>
    /// </summary>
    public static bool MinenTor = true;

    /// <summary>Alles vergessen — gehört zum Kartenwechsel wie
    /// <c>NavGrid.ClearOccupants</c>.</summary>
    public void MinenLeeren()
    {
        for (int i = 0; i < _minen.Length; i++) _minen[i] = default;
        MinenGelegt = MinenKeinPlatz = MinenAusgeloest = 0;
        MinenFreund = MinenImmunFall = MinenTorZu = 0;
        MinenFussvolk = MinenGeraeumt = MinenUnterFahrzeug = 0;
        MinenOpfer.Clear();
    }

    /// <summary>
    /// <b>Eine Mine legen</b> — @0x421940.
    ///
    /// <para>⚠ Die Vorbedingung ist die des Originals: die Zelle muss LEER sein
    /// oder eine EINHEIT tragen. Auf ein Gebäude (imap ≥ 14000) oder auf rauen
    /// Boden (0xFFFD) legt es nicht.</para>
    /// </summary>
    public bool MineLegen(int col, int row, int player)
    {
        if (_nav == null || !_nav.InBounds(col, row)) return false;
        // Unsere imap-Entsprechung: frei, oder es steht eine bewegliche Einheit
        // darauf. Festes (Gebäude) schliesst das Original aus.
        int drauf = _nav.BesetztVon(col, row);
        var ding = drauf >= 0 && drauf < _entities.Count ? _entities[drauf] : null;
        bool unterFahrzeug = false;
        if (MinenLegetorAlt)
        {
            bool fest = ding != null && ding.IsBuilding;
            if (fest) return false;
            if (_nav.GroundWord(col, row) is "gesperrt" or "ausserhalb") return false;
        }
        else
        {
            // ⭐ 15.09.2026 — DAS TOR DES ORIGINALS (@0x421940): imap == 0xFFFE
            // (freie Zelle: kein rauer Boden 0xFFFD, kein Wasser 0xFFFC, kein
            // Gebaeude, kein Wald, kein Objekt) ODER imap <= 8000 (ein FAHRZEUG
            // steht darauf, auf welchem Boden auch immer). Fussvolk (10000+) sperrt.
            // M15: 837 Zellen -> 451 frei + 7 unter Fahrzeugen = 458.
            unterFahrzeug = ding != null && !ding.IsBuilding && !ding.IsProp && !ding.Dead
                            && ding.Infantry < 0;
            bool frei = ding == null && _nav.GroundAt(col, row) == Simulation.NavGrid.Ground.Free
                        && GebaeudeAufZelle(col, row) < 0;
            if (!frei && !unterFahrzeug) return false;
        }

        for (int i = 0; i < _minen.Length; i++)
        {
            if (_minen[i].Aktiv) continue;
            // +0x02 = rand%20+10, +0x03 = rand%10+5 (@0x421940)
            int lx = Simulation.Determinism.Roll(20) + 10;
            int ly = Simulation.Determinism.Roll(10) + 5;
            _minen[i] = new Mine { Col = col, Row = row, Player = player, Aktiv = true, LageX = lx, LageY = ly };
            MinenGelegt++;
            if (unterFahrzeug) MinenUnterFahrzeug++;
            return true;
        }
        // @0x42198D: das Original druckt hier "Too many mines" und legt nichts.
        MinenKeinPlatz++;
        return false;
    }

    /// <summary>
    /// <b>»Mines and traps«</b> — @0x4216F0, jeden Takt.
    ///
    /// <para>⚠ Die Reihenfolge im Original ist gelesen: der Block steht in der
    /// Hauptschleife VOR »Buildings« (@0x416690) und »Movement« (@0x4166BB).
    /// Bei uns wird er darum vor dem Einheitentakt gerufen.</para>
    /// </summary>
    public void MinenTakt()
    {
        if (_nav == null) return;
        for (int i = 0; i < _minen.Length; i++)
        {
            ref var m = ref _minen[i];
            if (!m.Aktiv) continue;

            // imap < 8000 -> eine EINHEIT steht auf der Zelle
            int vi = _nav.BesetztVon(m.Col, m.Row);
            if (vi < 0 || vi >= _entities.Count) continue;
            var opfer = _entities[vi];
            if (opfer.Dead || opfer.IsProp || opfer.IsBuilding) continue;

            // ⭐ 15.09.2026 — imap < 8000: nur FAHRZEUGE. Fussvolk steht in der imap
            // als 10000..13999 und loest nie aus (berichte/minen-opus.md §3.1).
            if (!MinenTorAlt && opfer.Infantry >= 0) { MinenFussvolk++; continue; }

            // Diplomatietafel 0x87B155: eigene und verbuendete loesen nicht aus
            if (!MineFeindlich(m.Player, opfer.Owner)) { MinenFreund++; continue; }

            // +0x0E == 0x44: die Ausnahme. ⚠ 15.09.2026 berichtigt: +0x0E ist bei
            // uns `Part`, `Equipment` ist +0x10 (MapEntityLayer: HexByte(raw, 0x0e)
            // bzw. 0x10). Gegenschalter --minen-ausnahme-alt.
            if ((MinenAusnahmeAlt ? opfer.Equipment : opfer.Part) == MinenImmun) { MinenImmunFall++; continue; }

            // word[+0x06] < 0 — siehe MinenTor und MinenEinfahrt
            if (MinenTor && !(MinenTorAlt ? opfer.Path != null : MinenEinfahrt(opfer, m.Col, m.Row)))
            { MinenTorZu++; continue; }

            MinenOpfer.Add(vi);
            ApplyHit(-1, vi, opfer, MinenSchaden, $"MINE von Spieler {m.Player} auf ({m.Col},{m.Row})");
            MinenAusgeloest++;
            m.Aktiv = false;
        }
    }

    /// <summary>Dieselbe Frage wie <c>IsHostile</c>, aber auf SPIELERnummern —
    /// eine Mine merkt sich ihren Leger als Zahl, nicht als Einheit.</summary>
    private bool MineFeindlich(int leger, int opfer)
    {
        if (leger is < 0 or > 7 || opfer is < 0 or > 7) return false;
        if (_standby[leger] || _standby[opfer]) return false;
        return _haveAllies ? !_allied[leger, opfer] : leger != opfer;
    }

    /// <summary>
    /// ⚠⚠⚠ <b>DAS TOR DES ORIGINALS — UND WARUM ES NIE AUFGEHT.</b>
    ///
    /// <para>Hier stand ein »Schuss des Minenlegers«, der ans GEFECHT hing:
    /// wer ein Ziel in Reichweite hatte, legte eine Mine. <b>Das war eine
    /// Abweichung.</b> Das Original hängt das Legen an einen
    /// <b>Minenfeld-Auftrag</b> (@0x4087D0…0x408892):</para>
    /// <code>
    ///   byte[+0x0D] == 15          die Waffe ist der Minenleger
    ///   byte[+0x48] != 0           EIN AUFTRAG LIEGT VOR   &lt;-- das Tor
    ///   byte[+0x39] != 0           Munition
    ///   eigene Zelle liegt im Rechteck:
    ///        low(+0x40) &lt;= Spalte &lt; low(+0x40) + (+0x48)
    ///       high(+0x40) &lt;= Zeile  &lt; high(+0x40) + (+0x49)
    ///   0x421D40(Zelle) == 0       dort liegt noch nichts
    ///   word[+0x32] == 0           nachgeladen
    ///   -> Mine UNTER SICH legen, Klang 28/29, +0x32 = +0x3D, --[+0x39]
    ///   ausserhalb des Rechtecks: 0x40257C sucht den naechsten Punkt,
    ///   0x4021A8 faehrt hin; findet sich keiner -> +0x48 = 0, Auftrag zu Ende
    /// </code>
    ///
    /// <para>⚠⚠⚠ <b>BERICHTIGT am 15.09.2026 — das Tor GEHT auf.</b> Bus-Befehl
    /// <b>22</b> (Absender 0x43A110, Behandler C 0x4C33D0) schreibt
    /// <c>byte[+0x48] = Breite</c>, <c>byte[+0x49] = Höhe</c> (C 0x4C3419/0x4C341F)
    /// in der absoluten Form <c>[eax+0x6E2710]</c>, die der Scan unten nicht sah;
    /// HELPG #052 beschreibt den Rahmen. Nicht gebaut, weil Kampagne 15 keinen
    /// Minenleger hat (berichte/minen-opus.md §4.4). Der Absatz darunter ist
    /// Geschichte.</para>
    ///
    /// <para>⭐⭐ <b>Und dieses Tor geht im Original NIE auf.</b> Gemessen, mit
    /// 16 Ausrichtungen und auf BEIDEN Auslieferungen:</para>
    /// <list type="bullet">
    /// <item><b>13 Befehle</b> im ganzen Programm fassen <c>+0x48</c>/<c>+0x49</c>
    /// über einen Satzzeiger an. Davon <b>schreiben vier</b>: zwei setzen
    /// <b>0</b> (Auftrag beendet), zwei stehen im <b>Streckensystem</b>
    /// (@0x409A42/@0x409A8F; ihre Helfer liegen in 0x4B0xxx und der Bereich
    /// zeigt auf <c>set spoj:</c>, <c>bud1:</c>,
    /// <c>Cannon build more 'rail-possible' buildings</c>).</item>
    /// <item><b>Kein einziges <c>lea</c></b> zeigt auf <c>+0x48</c> — es gibt
    /// also auch keinen Schreibzugriff über einen vorberechneten Zeiger, die
    /// einzige Lücke, die ein solcher Scan sonst hätte.</item>
    /// <item><c>ZBRAN == 15</c> wird im ganzen Programm <b>einmal</b> geprüft:
    /// im Takt selbst.</item>
    /// <item>Der Knopfname »Mine legen« (Tafel 0x4FD660, Schritt 30) wird an
    /// <b>einer</b> Stelle benutzt — als <b>Tooltip</b> (@0x447A63). Die
    /// Knopfnummern eines Fensters (0x8B9044) haben fünf Schreibstellen, und
    /// <b>alle schreiben ≥ 1000</b>.</item>
    /// <item><b>Und die Kartendaten sagen dasselbe:</b> über alle 44 Karten
    /// tragen <b>0 von 4833</b> Einheitensätzen ein <c>+0x48 != 0</c> — obwohl
    /// <b>22</b> Minen- und Fallenleger platziert sind.</item>
    /// </list>
    ///
    /// <para><b>Entscheidung des Spielers am 24.08.2026: »lassen wie im
    /// Original«.</b> Das Legen bleibt deshalb ohne Auslöser — genau wie dort.
    /// Der Rest der Mechanik (Feld, Takt, 50 Schaden, Freundprüfung, die 500er
    /// Schranke) ist gelesen und steht, damit aus der Entscheidung jederzeit
    /// eine andere werden kann, ohne noch einmal lesen zu müssen.</para>
    /// </summary>
    private const int MinenTorImOriginalNieOffen = 0;

    /// <summary>
    /// <c>--minen-check</c> — <b>legt der Minenleger, und geht die Mine hoch?</b>
    ///
    /// <para>⚠ Der Prüfstand baut die Lage selbst und misst danach — in dieser
    /// Reihenfolge, und die ist heute teuer gelernt: was die Karte hergibt, wird
    /// gefragt, solange nichts darauf steht.</para>
    ///
    /// <para>Gemessen werden vier Dinge, und jedes hat sein eigenes Nullmodell:
    /// die Mine wird gelegt · eine FEINDLICHE Einheit löst sie aus und verliert
    /// genau <b>50</b> Trefferpunkte · eine EIGENE Einheit löst sie NICHT aus ·
    /// und über <see cref="UrMinen"/> hinaus wird keine mehr gelegt.</para>
    /// </summary>
    public string MinenCheck()
    {
        if (_nav == null) return "minen-check: kein Gitter";
        var sb = new System.Text.StringBuilder("minen-check\n");
        MinenLeeren();

        // Eine Wasser- oder Landzelle, die eine Einheit tragen kann.
        Entity? opfer = null; int oi = -1;
        for (int i = 0; i < _entities.Count; i++)
        {
            var e = _entities[i];
            if (e.IsBuilding || e.IsProp || e.Dead || !e.Mobile || e.HpMax <= 0) continue;
            // ⭐ 15.09.2026 — ein FAHRZEUG ohne Minenraeumer: Fussvolk loest nie aus,
            // der Raeumer ist gefeit. Beides wird unten eigens geprueft.
            if (e.Infantry >= 0 || e.Part == MinenImmun) continue;
            opfer = e; oi = i; break;
        }
        if (opfer == null) return sb.Append("  keine bewegliche Einheit — nicht gemessen").ToString();

        int fremd = opfer.Owner == 0 ? 1 : 0;
        sb.Append($"  Opfer: Nr. {oi} Rumpf {opfer.UnitType} P{opfer.Owner} "
                + $"auf ({opfer.Col},{opfer.Row}), {opfer.Hp}/{opfer.HpMax} TP\n");

        // 1. legen
        bool gelegt = MineLegen(opfer.Col, opfer.Row, fremd);
        sb.Append($"  1. Mine unter die Einheit gelegt (Leger P{fremd}): "
                + (gelegt ? "ja" : "⚠⚠ NEIN") + "\n");

        // 2. eine STEHENDE Einheit — das Tor muss halten
        int hp0 = opfer.Hp;
        MinenTakt();
        sb.Append($"  2. Einheit STEHT (Tor {(MinenTor ? "an" : "AUS")}): "
                + $"{hp0 - opfer.Hp} Schaden, Tor griff {MinenTorZu}x"
                + (MinenTor && MinenTorZu > 0 ? "  ✔ so gewollt" : "") + "\n");

        // ⭐ 15.09.2026 — die EINFAHRT (word[+0x06] < 0): Ziel des Schrittes ist die
        // Minenzelle, Fortschritt knapp unter bzw. genau bei der halben Strecke.
        // Mit --minen-tor-alt genuegt ein Weg (der Stand davor).
        void Einfahrt(bool halb)
        {
            opfer.Path = new List<Vector2I> { new(opfer.Col, opfer.Row) };
            opfer.Reserved = new Vector2I(opfer.Col, opfer.Row);
            opfer.StepCost = 160_000;
            long voll = (long)opfer.StepCost * SimHz;
            opfer.Progress = (int)(halb ? voll / 2 : voll / 2 - 1);
        }
        void Stand() { opfer.Path = null; opfer.Reserved = null; opfer.StepCost = 0; opfer.Progress = 0; }

        // 2b. erste Haelfte der Einfahrt — das Tor muss noch halten
        Einfahrt(halb: false);
        hp0 = opfer.Hp;
        int torVor = MinenTorZu;
        MinenTakt();
        int frueh = hp0 - opfer.Hp;
        // ⚠ Nicht umgedreht: mit --minen-tor-alt MUSS dieser Schritt durchfallen.
        bool fruehOk = frueh == 0 && MinenTorZu > torVor;
        sb.Append($"  2b. Einheit rollt in die Zelle, knapp VOR der halben Strecke: {frueh} Schaden "
                + (fruehOk ? "✔ Tor haelt" : $"⚠⚠ erwartet 0 (--minen-tor-alt {MinenTorAlt})") + "\n");
        if (frueh > 0) { opfer.Hp = opfer.HpMax; MinenLeeren(); MineLegen(opfer.Col, opfer.Row, fremd); }

        // 3. dieselbe Einheit ab der halben Strecke
        Einfahrt(halb: true);
        hp0 = opfer.Hp;
        MinenTakt();
        int schaden = hp0 - opfer.Hp;
        sb.Append($"  3. Einheit rollt in die Zelle, AB der halben Strecke: {schaden} Schaden "
                + (schaden == MinenSchaden ? $"✔ genau die {MinenSchaden} aus 0x9C72"
                   : $"⚠⚠ erwartet {MinenSchaden}") + "\n");
        Stand();

        // ⚠⚠ 24.08.2026 — DIESE ZWEI ZEILEN HABEN GEFEHLT, und der Pruefstand
        // hat es mir sofort um die Ohren gehauen: die 50 Schaden aus Schritt 3
        // TOETEN das Opfer (50 von 50 TP), und Schritt 4 mass danach eine
        // Leiche — »0 Schaden« stimmte, sagte aber nichts, denn der Takt steigt
        // bei `opfer.Dead` sowieso aus.
        // ⭐ Der Beleg stand in der Ausgabe daneben: »0x als Freund erkannt«.
        // Eine Nebenzahl, die der Hauptzahl widerspricht, ist das Warnzeichen.
        opfer.Hp = opfer.HpMax;
        opfer.Dead = false;
        // ⚠ Und der STEMPEL gehoert mit zurueck: `Kill()` raeumt die Belegung,
        // und der Minentakt findet sein Opfer ueber genau diese Belegung. Ohne
        // diese Zeile stand auf der Zelle nichts mehr, der Takt stieg vorher
        // aus, und Schritt 4 meldete »0 Schaden« fuer eine leere Zelle.
        // ⭐ Zum zweiten Mal an demselben Pruefstand: die 0 war richtig und
        // bedeutungslos. Der Freundzaehler daneben hat es beide Male verraten.
        _nav.SetHull(oi, Simulation.NavGrid.HullSide(opfer.GameUnitType));
        _nav.SetOccupant(opfer.Col, opfer.Row, oi, opfer.Infantry >= 0);

        // 4. die eigene Einheit
        MinenLeeren();
        MineLegen(opfer.Col, opfer.Row, opfer.Owner);
        Einfahrt(halb: true);
        hp0 = opfer.Hp;
        MinenTakt();
        int eigen = hp0 - opfer.Hp;
        // ⚠ Die Messlatte ist NICHT nur »0 Schaden«: eine tote oder gar nicht
        // erkannte Einheit nimmt auch 0. Der Freundzaehler MUSS gegriffen haben.
        bool eigenOk = eigen == 0 && MinenFreund > 0;
        sb.Append($"  4. EIGENE Mine unter der eigenen Einheit: {eigen} Schaden, "
                + $"{MinenFreund}x als Freund erkannt "
                + (eigenOk ? "✔" : "⚠⚠ (0 Schaden allein beweist nichts)") + "\n");
        Stand();

        // 4b. ⭐ 15.09.2026 — der MINENRAEUMER (+0x0E = Part = 0x44) auf feindlicher Mine
        MinenLeeren();
        MineLegen(opfer.Col, opfer.Row, fremd);
        int partVor = opfer.Part;
        opfer.Part = MinenImmun;
        Einfahrt(halb: true);
        hp0 = opfer.Hp;
        int immunVor = MinenImmunFall;
        MinenTakt();
        int raeumerSchaden = hp0 - opfer.Hp;
        bool raeumerOk = raeumerSchaden == 0 && MinenImmunFall > immunVor;
        sb.Append($"  4b. MINENRAEUMER (Part 0x44) faehrt ein: {raeumerSchaden} Schaden, Ausnahme {MinenImmunFall - immunVor}x "
                + (raeumerOk ? "✔" : $"⚠⚠ erwartet 0 (Nullmodell --minen-ausnahme-alt: {MinenAusnahmeAlt})") + "\n");
        opfer.Part = partVor;
        opfer.Hp = opfer.HpMax; opfer.Dead = false;
        Stand();

        // 5. die Schranke
        MinenLeeren();
        int konnte = 0;
        for (int k = 0; k < UrMinen + 20; k++)
            if (MineLegen(opfer.Col, opfer.Row, fremd)) konnte++;
        sb.Append($"  5. Schranke: {konnte} Minen gelegt, dann {MinenKeinPlatz}x kein Platz "
                + (konnte == UrMinen ? $"✔ genau die {UrMinen} aus 0x4217BB" : "⚠⚠") + "\n");
        MinenLeeren();

        bool ok = gelegt && fruehOk && schaden == MinenSchaden && eigenOk && raeumerOk && konnte == UrMinen;
        sb.Append(ok ? "  BESTANDEN" : "  DURCHGEFALLEN");
        return sb.ToString();
    }
}
