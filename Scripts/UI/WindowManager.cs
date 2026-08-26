namespace AkteEuropaReborn.UI;

using System.Collections.Generic;
using Godot;

/// <summary>
/// <b>Die Fensterverwaltung des Originals</b> — die Schicht zwischen dem
/// Zeichnen und der Klickauswertung: <b>öffnen, schliessen, nach vorn holen,
/// altern lassen</b>.
///
/// <para>⭐⭐ Gelesen am 22.08.2026, <c>OFFENE_FRAGEN.md</c> Abschnitt <b>BM</b>
/// (Revier 5, <c>0x442FB0…0x4505F0</c>, 52 Funktionen). Bis dahin hatten wir
/// gar keine Verwaltung: jedes Fenster war ein einzelner Godot-Knoten, den
/// irgendwer anlegte und irgendwer wieder wegnahm. Sechs Regeln des Originals
/// hatten dadurch keinen Ort, an dem sie hätten stehen können.</para>
///
/// <para><b>Was hier steht, und woher es kommt:</b></para>
/// <list type="number">
///   <item><b>Die Doppelöffnungssperre</b> (BM.2). Ein Fenster gibt es je
///   <see cref="Art"/> einmal — Objektfenster je <see cref="Kennung"/> einmal.
///   ⭐ Das ist nicht geraten: von den 31 Öffnern mit Artwache prüfen <b>30</b>
///   genau die Art, die sie danach anlegen. Bei freier Wahl unter 48 Arten
///   wären 0,6 Treffer zu erwarten.</item>
///   <item><b>Die Reihenfolge</b> (BM.4). <b>Platz 0 ist oben.</b> Das steht
///   nirgends, es folgt aus zwei Suchläufen: die Maus (<c>0x446DE0</c>) und die
///   Tastatur (<c>0x413EC4</c>) gehen die Liste <b>von 0 aufwärts</b> und
///   brechen beim ersten Treffer ab — läge 0 unten, träfe man durch verdeckte
///   Fenster hindurch.</item>
///   <item><b>Neu kommt HINTEN hinein, dann nach vorn</b> — zwei getrennte
///   Schritte (<c>0x441270</c>, dann <c>0x44FC20</c>). ⭐ Genau darum können
///   vier Öffner den zweiten weglassen: <see cref="BleibtHinten"/>.</item>
///   <item><b>Die Blende</b> (BM.10): <b>4 Bilder auf</b> mit Klang 307,
///   <b>6 Bilder zu</b>. Das Original klappt den eigenen Punktpuffer zur
///   Mittelzeile zusammen und füllt den Rand mit <c>0xFF</c> = durchsichtig.</item>
///   <item><b>Die Lebensdauer</b> (BM.11): <c>+0xAD22</c> wird <b>alle 20
///   Takte</b> um eins heruntergezählt; bei 0 schliesst sich das Fenster.
///   <c>0x441270</c> setzt sie auf <b>0 = niemals</b>, und nur das
///   Meldungsfenster (Art 13, <c>0x4469A0</c>, 44 Rufstellen) bekommt sie als
///   Argument. <b>Meldungen verschwinden also von allein, und der Rufer
///   bestimmt, wie lange sie stehen.</b></item>
///   <item><b>Zwanzig Plätze.</b> ⭐ Zehn Fundstellen im Code nennen dieselben
///   zwei Rohzahlen: Schrittweite <c>0xAD24</c> = 44 324 und die Endadresse
///   <c>0x99C42A</c> = <c>0x8C3D5A + 20·44324</c>, auf das Byte genau.</item>
/// </list>
///
/// <para>⚠ <b>Was hier NICHT steht:</b> das Zeichnen (Abschnitt BA) und die
/// Klickauswertung (<c>ui_action</c>). Die Verwaltung kennt ihre Fenster nur
/// als Knoten mit Art und Kennung — sie malt keines.</para>
///
/// <para>⚠ <b>Unsere Abweichung, und sie ist bewusst:</b> das Original arbeitet
/// die Blende im eigenen <b>Punktpuffer</b> ab. Wir haben keine Punktpuffer je
/// Fenster, sondern Godot-Knoten; die Blende ist darum eine <b>Skalierung um
/// die Mittelachse</b>. Das Ergebnis sieht gleich aus (das Fenster klappt zur
/// Mittellinie zusammen), der Weg ist ein anderer. Die BILDZAHLEN 4 und 6 sind
/// dagegen die des Originals, und sie sind das, was man sieht.</para>
/// </summary>
public static class WindowManager
{
    /// <summary>⭐ Zwanzig Plätze — zehn Fundstellen, dieselbe Rohzahl.</summary>
    public const int MaxFenster = 20;

    // ---- die Fensterarten -------------------------------------------------
    //
    // Die Nummern des Originals, soweit belegt (BM.1a). Hier stehen nur die,
    // die wir schon haben oder gleich brauchen; die Tafel ist vollstaendig in
    // OFFENE_FRAGEN BM.1a.

    /// <summary>Art 13 — das MELDUNGSFENSTER, 44 Rufstellen. ⭐ Das einzige, das
    /// eine Standzeit mitbekommt (<c>0x4469A0</c>).</summary>
    public const int ArtMeldung = 13;

    /// <summary>Art 44 — die Statuszeile. Bleibt hinten.</summary>
    public const int ArtStatuszeile = 44;

    /// <summary>Art 45 — der Ladebalken »Laden…«.</summary>
    public const int ArtLaden = 45;

    /// <summary>
    /// ⚠ <b>UNSERE Nummern, oberhalb der 48 des Originals.</b> Dieselbe Regel
    /// wie bei <see cref="Simulation.Commands.CommandOp.OursFirst"/>: Fenster,
    /// die wir haben und für die im Original keine Art nachgewiesen ist,
    /// bekommen eine Nummer, die mit keiner gelesenen kollidieren kann.
    ///
    /// <para>Das Gruppen- und das Merkpunktfenster stehen NICHT in der
    /// Öffnertafel BM.1a. Ihre Öffner sind zwar gelesen (<c>0x442C70</c> bzw.
    /// <c>0x442D40</c>), ihre Fensterart aber nicht — und eine Zahl zu raten,
    /// nur damit sie »original« aussieht, wäre schlimmer als eine ehrlich
    /// eigene.</para>
    /// </summary>
    public const int UnsereErste = 100;

    /// <summary>
    /// ⭐⭐ <b>25.08.2026 — DIESE ZWEI SIND KEINE EIGENEN NUMMERN MEHR.</b>
    ///
    /// <para>Hier stand <c>ArtGruppen = 100, ArtMerkpunkte = 101</c> mit der
    /// Begründung, ihre Fensterart sei ungelesen. Das ist seit dem 20.08.2026
    /// überholt: <c>aekernel-tools/FENSTER_RE.md</c> führt den <b>ganzen</b>
    /// Fensterverteiler <c>0x487630</c> mit allen 48 Armen, und dort stehen
    /// <b>24 = Lokator</b> (<c>0x47A740</c>, 736 B, »Lokator - Lokalisieren -
    /// Sichern«) und <b>25 = Gruppieren</b> (<c>0x47AAE0</c>, 740 B,
    /// »Gruppieren - Gruppe speichern«). Die Klassenköpfe von
    /// <see cref="LocatorWindow"/> und <see cref="GroupWindow"/> nennen genau
    /// diese zwei Zahlen seit demselben Tag — nur die Verwaltung wusste noch
    /// nichts davon. Die Tastentafel <c>0x487A10</c> (Revier 6, §3.1) nennt sie
    /// ein zweites Mal und unabhängig: 24 und 25 sind zwei der sechs
    /// Fensterarten mit EINGABEFELD, und beide Fenster haben bei uns genau
    /// so eines (das Namensfeld).</para>
    ///
    /// <para>⚠ <b>Warum das jetzt trägt und vorher nicht:</b> seit
    /// <see cref="Oeffnen"/> das Ereignisbyte setzt, ist die Fensterart eine
    /// ZAHL, die in die Missionsskripte geht. Mit 100/101 hätte das
    /// Ereignisbyte Werte angenommen, die das Original nie erzeugt — und die
    /// Regeln, die auf <c>event == 24</c> (Missionen 1, 2, 3) und
    /// <c>event == 25</c> (Missionen 1, 2) warten, hätten weiter gewartet.</para>
    ///
    /// <para>⭐ <b>Das Nullmodell steht in unseren eigenen Daten.</b>
    /// <c>Data/mission_scripts.json</c> führt über alle 33 Missionen
    /// <b>elf verschiedene</b> Ereigniswerte: 1, 6, 7, 8, 11, 16, 24, 25, 29,
    /// 31, 33. <b>Alle elf sind Fensterarten aus der Tafel des Verteilers,
    /// keine liegt über 48.</b> Das Ereignisbyte ist ein Byte (0…255); wären
    /// die Werte freie Nummern, wären elf Treffer im Bereich 1…48 mit
    /// <c>(48/256)^11 ≈ 3·10⁻⁹</c> zu erwarten.</para>
    /// </summary>
    public const int ArtGruppen = 25, ArtMerkpunkte = 24;

    /// <summary>⚠ Fensterart <b>3</b> ist die KARTE — die einzige Art, die das
    /// Ereignisbyte NICHT setzt. Siehe <see cref="Oeffnen"/>.</summary>
    public const int ArtKarte = 3;

    /// <summary>⭐ Vier Bilder auf (BM.10, <c>word[0x87B054] &lt; 4</c>).</summary>
    public const int BilderAuf = 4;

    /// <summary>⭐ Sechs Bilder zu (<c>byte[0x87ADFC] &lt; 6</c>).</summary>
    public const int BilderZu = 6;

    /// <summary>⭐ Klang 307 = <c>0x133</c>, beim Aufgehen über
    /// <c>0x4047E0</c>.</summary>
    public const int KlangAuf = 307;

    /// <summary>⭐ Die Lebensdauer wird alle 20 Takte um eins gezählt
    /// (<c>word[0x4FA248] % 20 == 0</c>).</summary>
    public const int StandzeitTakte = 20;

    // ---- ⭐⭐ DAS EREIGNISBYTE byte[C 0x539930] -------------------------------
    //
    // Einziger Setzer im ganzen Programm ist 0x4412C2
    // (`mov byte ptr [0x539930], al`), und er sitzt in 0x441270 — genau der
    // Funktion, die ein geöffnetes Fenster hinten in die Reihenfolgeliste
    // einträgt, also in Oeffnen() hier unten. Der Wert ist die FENSTERART
    // (`al = byte[44324·Fenster + 0x8B9038]`).
    //
    // ⭐ Vollerhebung über die Relokationstafel: 69 Verweise auf 0x539930,
    // 0 unklar; die F-Fassung ist befehlsgleich.
    //
    // ⚠ DIE EINE AUSNAHME IST DIE KARTE: `cmp al,3 / je` @0x4412C0 springt
    // über den Schreiber hinweg. Wer sie mitsetzte, machte aus jedem Blick auf
    // die Karte ein Ereignis — und die Karte geht in der Kampagne ständig auf.
    //
    // ⚠ Es ist ein EREIGNIS, kein Zustand: der Block, der es liest, nullt es
    // (`mov byte [0x539930], 0` @0x49867D). Hier wird es darum nur GESETZT;
    // das Zurücksetzen bleibt bei den Lesern.
    //
    // ⚠ ZWEI LESER, EIN BYTE — und das ist unsere Abweichung, sie steht hier,
    // damit sie zu sehen ist. Bei uns ist das eine Byte des Originals auf zwei
    // Felder aufgeteilt: Campaign.CampaignHints.Ereignis für den
    // Kampagnenvorspann und Campaign.MissionScript.LastEvent für die
    // Missionsskripte. Im Original verbraucht der ERSTE Leser das Byte dem
    // zweiten vor der Nase weg; bei uns können beide feuern. Solange beide
    // dieselbe Zahl vom selben Setzer bekommen, ist das der ganze Unterschied.

    /// <summary>Der Haken, über den das Ereignisbyte an das laufende
    /// Missionsskript geht. <c>Rendering.MapEntityLayer</c> hängt ihn ein,
    /// sobald das Skript steht — die Fensterverwaltung kennt das Skript nicht
    /// und soll es auch nicht kennen. <see cref="Leeren"/> nimmt ihn wieder
    /// weg, sonst hält ein statisches Feld die alte Karte über den
    /// Szenenwechsel hinweg fest.</summary>
    public static System.Action<int>? Ereignismelder;

    /// <summary>Wie oft das Ereignisbyte gesetzt wurde und was zuletzt darin
    /// stand. ⚠ Ohne den ZÄHLER ist »das Fenster hat nichts gesetzt« nicht von
    /// »es hat 0 gesetzt« zu unterscheiden — und genau diese zwei Fälle sind
    /// beim Ereignisbyte der Unterschied zwischen einer stummen Regel und einer
    /// richtig wartenden.</summary>
    public static int EreignisGesetzt, EreignisZuletzt;

    /// <summary>Das Ereignis absetzen. Beide Empfänger bekommen dieselbe Zahl
    /// aus derselben Hand — siehe den Block darüber.</summary>
    private static void Ereignis(int art)
    {
        EreignisGesetzt++;
        EreignisZuletzt = art;
        Campaign.CampaignHints.Ereignis = art;
        Ereignismelder?.Invoke(art);
    }

    /// <summary>
    /// Die vier Arten, die <c>0x44FC20</c> NICHT rufen und darum liegen
    /// bleiben, wo <c>0x441270</c> sie hingelegt hat — hinten.
    ///
    /// <para>Art 1 = Befehlsmenü (<c>0x444490</c>), Art 2 =
    /// <c>0x4445D0</c>/<c>0x444680</c>, Art 44 = Statuszeile (<c>0x444300</c>).
    /// ⚠ Für die Statuszeile ist das offensichtlich richtig; wer alles nach
    /// vorn holt, bekommt sie über dem Bauschirm.</para>
    /// </summary>
    public static bool BleibtHinten(int art) => art is 1 or 2 or 44;

    /// <summary>
    /// ⭐⭐ <b>Ein Fenster in den Schirm zwingen</b> — <c>0x441190</c>
    /// (OFFENE_FRAGEN <b>BL.8.2</b>), <b>52 Rufstellen</b>.
    ///
    /// <code>
    /// wenn art in {9, 0x23, 0x30}:  nichts tun
    /// wenn x &lt; 0:            x = 0
    /// wenn x + b &gt;= breite:  x = breite − b − 1
    /// wenn y &lt; 0:            y = 0
    /// wenn y + h &gt;= hoehe:   y = hoehe − h − 1
    /// </code>
    ///
    /// <para>⚠ Die drei Ausnahmen sind <b>9</b> (Bedienfeld), <b>35</b>
    /// (Hauptmenü) und <b>48</b> (zweites Hauptmenü). Wer sie mitzwingt,
    /// verschiebt Panel und Hauptmenü; wer den Zwang ganz weglässt, hat Fenster,
    /// die halb aus dem Bild ragen.</para>
    ///
    /// <para>⭐⭐ <b>Und hier sitzt der zehnte Auslieferungsunterschied.</b>
    /// C prüft <c>cmp al,0x30</c> (@0x4411BE), F nicht — <c>0x30 = 48</c> ist
    /// genau die Fensterart, die es in F nicht gibt. Das ist die <b>fünfte</b>
    /// unabhängige Zählung dafür, neben Anlegerzahl, Rahmenzeichner-Rufern,
    /// Knopfroutine-Rufern und der Sprungtafel.</para>
    ///
    /// <para>⚠ <b>Methodische Warnung, hier gelernt:</b> <c>cfind.py --diff</c>
    /// meldet an dieser Stelle »delete <c>cmp al,9</c>« — weil <c>difflib</c>
    /// bei drei gleichgeformten Paaren das <b>erste</b> wegwirft. Die Konstanten
    /// sagen etwas anderes: <c>9</c> und <c>0x23</c> stehen in beiden,
    /// <c>0x30</c> nur in C. <b>Die Blockstelle eines Textvergleichs ist keine
    /// inhaltliche Aussage.</b></para>
    /// </summary>
    public static bool ZwangAusgenommen(int art) => art is 9 or 35 or 48;

    /// <summary>
    /// <b>Der Knoten eines Fensters — aber nur, wenn er noch lebt.</b>
    ///
    /// <para>⚠⚠ GEMELDET AM 23.08.2026: »wenn ich ein Gefecht verlasse, bleibt
    /// der Bildschirm schwarz und ich komme nicht mehr ins Hauptmenue«.
    /// Die Ursache sass hier. <c>ChangeSceneToFile</c> gibt die alte Szene
    /// frei — samt aller Fensterknoten. Erst DANACH laeuft
    /// <c>MainMenu._Ready</c>, und dessen erste Anweisung ist
    /// <c>LeaveToMenu.Tidy</c> → <see cref="Leeren"/> → <see cref="Fertig"/>.
    /// Dort stand <c>if (f.Knoten is not Control c) return;</c> — und genau
    /// das greift bei einem freigegebenen Godot-Objekt NICHT: der C#-Umschlag
    /// behaelt seinen Typ, der Mustervergleich gelingt, und der Zugriff auf
    /// <c>c.Visible</c> wirft. <c>_Ready</c> starb in Zeile eins, das Menue
    /// wurde nie gebaut.</para>
    ///
    /// <para>⚠ »<c>is not Control</c>« sieht wie eine Pruefung aus und ist
    /// keine. Wer einen Godot-Knoten ueber einen Szenenwechsel hinweg
    /// aufbewahrt, braucht <c>IsInstanceValid</c> — der Typ sagt nichts
    /// darueber, ob das Objekt dahinter noch da ist.</para>
    ///
    /// <para>⚠ Warum <c>--fenster-check</c> mit seinen 18 Messungen gruen war:
    /// er lief nie mit OFFENEN Fenstern durch einen Szenenwechsel. Leere
    /// Liste, <see cref="Leeren"/> tut nichts. Messung 19 schliesst das.</para>
    /// </summary>
    private static Control? Lebend(Fenster? f)
        => f?.Knoten is Control c && GodotObject.IsInstanceValid(c) ? c : null;

    /// <summary>
    /// ⭐⭐ <b>WO EIN FENSTER AUFGEHT: AN DER MAUS, DREI PUNKTE NACH LINKS OBEN.</b>
    /// Gelesen am 26.08.2026 auf die Meldung »unseres öffnet oben links, im
    /// Original neben dem Gebäude«.
    ///
    /// <para><b>Die Kette, ganz durchgelesen</b> — am Beispiel des
    /// Nachschubpostens:</para>
    /// <code>
    /// 0x437315  Arm 14 des Klickverteilers 0x4379F0
    ///           mov  word[0x502AD8], bx          ; die angeklickte Kennung
    ///           mov  eax, [0x502AAC] ; sub ax,3  ; MAUS-Y − 3   -> push
    ///           mov  eax, [0x502AA8] ; sub ax,3  ; MAUS-X − 3   -> push
    ///           call 0x443090                    ; Oeffner der Fensterart 31
    /// 0x4430D7  der Oeffner reicht x/y durch  ->  0x45ABE0 (Anleger)
    /// 0x45AC5C  word[+0x02] = x ; word[+0x04] = y
    /// 0x443134  danach:  0x441190  =  InDenSchirm
    /// </code>
    ///
    /// <para><b>Dass 0x502AA8/0x502AAC die MAUS sind, ist belegt und nicht
    /// vermutet:</b> geschrieben werden sie in der Fensterbotschaft
    /// <c>@0x414021</c> aus <c>LOWORD/HIWORD(lParam)</c> — die Form eines
    /// <c>WM_MOUSEMOVE</c> — und <c>@0x414397</c> setzt sie neben einem Ruf über
    /// <c>[0xC657FC]</c> auf <c>(300, 200)</c> zurück. Die Relokationstafel
    /// führt für das Paar <b>239 Verweise, 0 unklar</b>.</para>
    ///
    /// <para>⭐ <b>Und es ist die Regel des ganzen Programms, keine Einzelstelle:</b>
    /// die Bytefolge <c>A1 &lt;Maus-X&gt; / 66 2D 03 00 / 50</c> (»lies Maus-X,
    /// ziehe 3 ab, schiebe auf den Stapel«) steht <b>60 mal</b> im <c>.text</c> —
    /// und in der F-Fassung, auf deren eigener Maus-Adresse
    /// <c>0x501AE8</c>, <b>ebenfalls genau 60 mal</b>. Zwei Bauten, dieselbe
    /// Zahl.</para>
    ///
    /// <para>⚠ <b>Die Ausnahmen sind vollständig aufgezählt</b>, nicht
    /// gemutmasst: neun Öffner überschreiben die übergebene Lage gleich wieder
    /// mit einer festen (mittig, <c>Höhe/5</c>, <c>Höhe−200</c>, <c>x=25</c>).
    /// Sie stehen in <see cref="FesteLage"/> und sind über die Schreiber auf
    /// <c>word[0x8B903A]</c> ausserhalb der 44 Anleger gefunden — eine
    /// Vollerhebung über die Relokationstafel, kein Abtast.</para>
    ///
    /// <para>⚠ <b>UNSERE SETZUNG sind die drei Punkte.</b> Sie stehen im
    /// Original in dessen eigenen Bildpunkten (640 x 480); wir rechnen sie
    /// NICHT auf unseren Schirm hoch. Der Versatz entscheidet nur, ob der
    /// Zeiger knapp INNERHALB der ersten Rahmenkachel sitzt, und das tut er
    /// bei uns auch ungerechnet. Wer ihn skaliert, ändert am Bild nichts und
    /// hätte eine Zahl mehr zu begründen.</para>
    ///
    /// <para>⚠ <b>Erst rufen, wenn die GRÖSSE steht.</b> Der Zwang in den
    /// Schirm rechnet mit Breite und Höhe; ein Godot-Knoten, der gerade erst
    /// sichtbar wurde, trägt die noch nicht. Darum ruft
    /// <c>MapViewer.OnBuildingWindow</c> diese Methode NACH
    /// <c>BuildingWindow.Open</c> und nicht davor.</para>
    /// </summary>
    /// <returns>false, wenn das Fenster eine feste Lage hat oder tot ist —
    /// dann wurde nichts angefasst.</returns>
    public static bool AnDieMaus(Fenster? f)
    {
        if (Lebend(f) is not Control c) return false;
        var schirm = Schirmmass();
        if (schirm.X <= 0) return false;
        // Eine feste Lage schlägt die Maus — im Original überschreibt der
        // Öffner die übergebene Lage, bei uns tut es dieselbe Tafel.
        if (FesteLage(f, schirm)) { InDenSchirm(f, schirm); return false; }
        // ⚠ `GetGlobalMousePosition` und NICHT `GetViewport().GetMousePosition()`:
        //   das Spiel laeuft im Streckmodus »CanvasItems« (Settings.cs meldet
        //   ihn beim Start), und dort sind Fensterpunkte und Leinwandpunkte
        //   nicht dasselbe. `Control.Position` zaehlt in der Leinwand seiner
        //   CanvasLayer -- die Maus muss in derselben Rechnung stehen, sonst
        //   sitzt das Fenster auf einem gestreckten Schirm daneben, und zwar
        //   umso weiter, je weiter rechts unten geklickt wurde.
        var maus = Mausquelle?.Invoke() ?? c.GetGlobalMousePosition();
        c.Position = maus - new Vector2(MausVersatz, MausVersatz);
        InDenSchirm(f, schirm);
        return true;
    }

    /// <summary>Die drei Punkte aus <c>sub ax, 3</c> — siehe
    /// <see cref="AnDieMaus"/>.</summary>
    public const int MausVersatz = 3;

    /// <summary>
    /// Woher <see cref="AnDieMaus"/> den Zeigerstand nimmt. Im Spiel null —
    /// dann fragt sie den Sichtbereich.
    ///
    /// <para>⚠ Diese Naht gibt es allein für den Prüfstand, und sie hat einen
    /// Grund: einen Mauszeiger kann ein kopfloser Lauf nicht setzen
    /// (<c>Input.WarpMouse</c> braucht ein Fenster). Ohne sie müsste der
    /// Prüfstand die gemessene Lage selbst hinschreiben — und prüfte damit
    /// seine eigene Rechnung statt der von <see cref="AnDieMaus"/>.</para>
    /// </summary>
    public static System.Func<Vector2>? Mausquelle;

    public static void InDenSchirm(Fenster? f, Vector2 schirm)
    {
        if (Lebend(f) is not Control c) return;
        if (ZwangAusgenommen(f!.Art)) return;
        var p = c.Position;
        // ⚠ Ein frisch sichtbar gemachter Knoten trägt seine Größe noch nicht
        // in `Size`, sondern erst in `CustomMinimumSize`. Mit `Size` allein
        // zwänge der Zwang gegen eine Null-Größe und liesse das Fenster stehen,
        // wo es steht — der Fehler wäre nur bei kleinen Schirmen zu sehen.
        var g = new Vector2(Mathf.Max(c.Size.X, c.CustomMinimumSize.X),
                            Mathf.Max(c.Size.Y, c.CustomMinimumSize.Y));
        if (p.X < 0) p.X = 0;
        if (p.X + g.X >= schirm.X) p.X = schirm.X - g.X - 1;
        if (p.Y < 0) p.Y = 0;
        if (p.Y + g.Y >= schirm.Y) p.Y = schirm.Y - g.Y - 1;
        c.Position = p;
    }

    /// <summary>
    /// ⭐ <b>Die festen Lagen</b> (OFFENE_FRAGEN <b>BL</b>, Anlegertafel).
    /// Nicht jedes Fenster darf hin, wo es will:
    /// <list type="bullet">
    ///   <item><b>35</b> Hauptmenü: <c>x = 25</c> fest, <c>y = Höhe − h − 20</c>
    ///   — links UNTEN.</item>
    ///   <item><b>40</b> »Pause«, <b>42</b> »Warten auf…«, <b>34</b>
    ///   Einstellungen: x mittig, <c>y = Höhe / 5</c>.</item>
    ///   <item><b>37</b> Gefechtsvorbereitung: x und y mittig.</item>
    ///   <item><b>46</b>: fest auf <c>(0,0)</c>.</item>
    /// </list>
    /// <para>⚠ <c>y = Höhe/5</c> ist keine Mitte und sieht auf einem hohen
    /// Schirm auch nicht danach aus. Es steht so da, dreimal.</para>
    /// </summary>
    /// <returns>false, wenn diese Art keine feste Lage hat.</returns>
    public static bool FesteLage(Fenster? f, Vector2 schirm)
    {
        if (Lebend(f) is not Control c) return false;
        var g = c.Size;
        switch (f!.Art)
        {
            case 35: c.Position = new Vector2(25, schirm.Y - g.Y - 20); return true;
            case 34:
            case 40:
            case 42: c.Position = new Vector2((schirm.X - g.X) / 2f, schirm.Y / 5f); return true;
            case 37: c.Position = (schirm - g) / 2f; return true;
            case 46: c.Position = Vector2.Zero; return true;
            default: return false;
        }
    }

    public sealed class Fenster
    {
        /// <summary>Die Fensterart, <c>byte[+0x00]</c> im Original.</summary>
        public int Art;

        /// <summary>Die Objektkennung, <c>word[+0x0C]</c> — bei Gebäudefenstern
        /// der Platz. <c>-1</c> = Einzelstück, es gibt es nur einmal.</summary>
        public int Kennung = -1;

        /// <summary>Der Knoten, den wir statt eines Punktpuffers haben.</summary>
        public CanvasItem? Knoten;

        /// <summary><c>+0xAD22</c>. 0 = niemals von selbst schliessen.</summary>
        public int Standzeit;

        /// <summary>Bildzähler der Blende. <c>&gt;= 0</c> heisst »geht gerade
        /// auf«, <c>&lt; 0</c> heisst »offen«.</summary>
        public int AufBild = 0;

        /// <summary>Bildzähler des Zugehens, <c>-1</c> = geht nicht zu.</summary>
        public int ZuBild = -1;
    }

    /// <summary>
    /// Die Reihenfolgeliste <c>0x87AFF8</c>. <b>Platz 0 ist oben.</b>
    /// </summary>
    private static readonly List<Fenster> _liste = new();

    /// <summary>Wie viele Fenster offen sind (<c>byte[0x4FD64C]</c>).</summary>
    public static int Anzahl => _liste.Count;

    /// <summary>Die Liste von oben nach unten — für Prüfstände.</summary>
    public static IReadOnlyList<Fenster> Liste => _liste;

    /// <summary>Wie oft ein Öffnen an der Doppelöffnungssperre gescheitert ist,
    /// und wie viele Fenster von selbst zugegangen sind.</summary>
    public static int Abgewiesen, VonSelbstZu;

    /// <summary>Alles vergessen — beim Kartenwechsel.</summary>
    public static void Leeren()
    {
        foreach (var f in _liste) Fertig(f);
        _liste.Clear();
        Abgewiesen = 0;
        VonSelbstZu = 0;
        EreignisGesetzt = 0;
        EreignisZuletzt = 0;
        _takt = 0;
        // ⚠ Der Haken geht MIT. Er hält eine Methode der alten Kartenebene
        // fest; bliebe er stehen, zeigte ein statisches Feld über den
        // Szenenwechsel hinweg auf eine freigegebene Szene — dieselbe Falle wie
        // bei den Knoten, siehe <see cref="Lebend"/>.
        Ereignismelder = null;
    }

    /// <summary>Ist ein Fenster dieser Art (und Kennung) schon offen?
    /// ⭐ Die Wache aus BM.2 — bei <paramref name="kennung"/> = -1 zählt allein
    /// die Art.</summary>
    public static Fenster? Offen(int art, int kennung = -1)
    {
        foreach (var f in _liste)
            if (f.Art == art && (kennung < 0 || f.Kennung == kennung)) return f;
        return null;
    }

    /// <summary>
    /// <b>Ein Fenster öffnen</b> — die Schablone aus BM.2, in ihrer
    /// Reihenfolge.
    /// </summary>
    /// <param name="standzeit">Wie viele Zwanzig-Takt-Schritte es steht.
    /// <b>0 = niemals von selbst zu</b>, wie <c>0x441270</c> es setzt. Nur das
    /// Meldungsfenster bekommt hier etwas anderes.</param>
    /// <returns>Das Fenster, oder <c>null</c>, wenn es schon offen war.</returns>
    public static Fenster? Oeffnen(int art, CanvasItem? knoten, int kennung = -1,
                                   int standzeit = 0)
    {
        // 1. Die Doppelöffnungssperre. ⚠ Zuerst, nicht zuletzt — das Original
        //    prüft VOR dem Anlegen, sonst hinge ein zweiter Anleger in der Luft.
        if (Offen(art, kennung) != null) { Abgewiesen++; return null; }

        // ⚠ Zwanzig Plätze. Das Original prüft das an dieser Stelle nicht — es
        // hat 20 feste Plätze und läuft in den einundzwanzigsten hinein. Wir
        // weisen ab und sagen es, statt still zu überschreiben.
        if (_liste.Count >= MaxFenster)
        {
            GD.Print($"fenster: Art {art} nicht geoeffnet — alle {MaxFenster} Plaetze belegt");
            Abgewiesen++;
            return null;
        }

        var f = new Fenster
        {
            Art = art, Kennung = kennung, Knoten = knoten,
            Standzeit = standzeit, AufBild = 0, ZuBild = -1,
        };

        // 2. HINTEN in die Liste (0x441270).
        _liste.Add(f);

        // 2b. ⭐⭐ UND DAS EREIGNISBYTE — DIESELBE FUNKTION, 0x4412C2.
        //     Es steht hier und nicht bei den Zeichnern, weil es im Original
        //     auch hier steht: EIN Setzer für alle 69 Fundstellen. Und es steht
        //     hinter der Doppelöffnungssperre, weil der Setzer im Original
        //     hinter dem Eintragen sitzt: ein Fenster, das schon offen ist,
        //     wird gar nicht erst eingetragen und setzt darum auch nichts.
        //     Die Ausnahme ist die Karte (Art 3, `cmp al,3 / je` @0x4412C0).
        if (art != ArtKarte) Ereignis(art);

        // 3. Lage: erst die feste, wenn die Art eine hat, dann in den Schirm
        //    zwingen (0x441190). ⚠ Die Reihenfolge ist die des Originals --
        //    der Anleger setzt die Lage, DANACH kommt der Zwang. Andersherum
        //    wuerde eine feste Lage den Zwang wieder aufheben.
        var schirm = Schirmmass();
        if (schirm.X > 0)
        {
            FesteLage(f, schirm);
            InDenSchirm(f, schirm);
        }

        // 4. Und nach VORN holen — ausser den vier Arten, die es nicht tun.
        if (!BleibtHinten(art)) NachVorn(f);

        Blende(f);
        return f;
    }

    /// <summary><c>0x44FC20</c> — auf Platz 0, alle anderen eins nach hinten.</summary>
    public static void NachVorn(Fenster? f)
    {
        if (f == null || _liste.Count == 0 || _liste[0] == f) return;
        if (!_liste.Remove(f)) return;
        _liste.Insert(0, f);
        Zeichenfolge();
    }

    /// <summary><c>0x446DE0</c> — der Maustreffer holt sein Fenster nach vorn.
    /// Sucht von 0 aufwärts und bricht beim ERSTEN Treffer ab; das ist genau
    /// der Suchlauf, aus dem folgt, dass 0 oben ist.</summary>
    public static Fenster? Treffer(Vector2 punkt)
    {
        foreach (var f in _liste)
        {
            if (Lebend(f) is not Control c || !c.Visible) continue;
            if (!c.GetGlobalRect().HasPoint(punkt)) continue;
            NachVorn(f);
            return f;
        }
        return null;
    }

    /// <summary><b>Schliessen anstossen</b> — das Fenster geht über
    /// <see cref="BilderZu"/> Bilder zu und verschwindet dann.</summary>
    public static void Schliessen(int art, int kennung = -1)
        => Schliessen(Offen(art, kennung));

    public static void Schliessen(Fenster? f)
    {
        if (f == null || f.ZuBild >= 0) return;
        f.ZuBild = 0;
    }

    /// <summary>Sofort weg, ohne Blende — für den Kartenwechsel.</summary>
    public static void Wegnehmen(Fenster? f)
    {
        if (f == null) return;
        Fertig(f);
        _liste.Remove(f);
        Zeichenfolge();
    }

    /// <summary>Der Knoten ist durch: unsichtbar, und die Blendenskalierung
    /// zurueck. ⚠ Ohne das Zuruecksetzen stuende ein wieder geoeffnetes
    /// Fenster als flacher Strich da — die Skalierung ueberlebt das Verstecken.</summary>
    private static void Fertig(Fenster f)
    {
        if (Lebend(f) is not Control c) return;
        c.Visible = false;
        c.Scale = Vector2.One;
    }

    /// <summary>Das Schirmmass. ⚠ Im Original sind es ZWEI Globale mit
    /// demselben Wert: die Fensterschicht liest <c>dword[0xB136B0]</c>, die
    /// Zeichenschicht <c>dword[0x5387C8]</c> — beide schreibt <c>0x4B6B1C</c>
    /// unmittelbar hinter <c>SetDisplayMode</c>. Bei uns ist es der
    /// Sichtbereich.</summary>
    /// <summary>⚠ Öffentlich, damit der Prüfstand gegen DIESELBE Zahl rechnet,
    /// mit der <see cref="AnDieMaus"/> zwingt. Ein Prüfstand, der sich sein
    /// eigenes Schirmmass setzt, misst seine eigene Annahme.</summary>
    public static Vector2 Schirmmass()
    {
        var baum = (SceneTree?)Engine.GetMainLoop();
        var sicht = baum?.Root?.GetViewport();
        // ⚠⚠ 26.08.2026, GEMESSEN statt geglaubt: ein KOPFLOSER Lauf meldet
        // hier NICHT (0,0), sondern ein QUADRAT — Sonde: sichtrect=(1600,1600),
        // waehrend project.godot 1600x900 sagt und Settings.cs »Schirm 0x0«
        // meldet. Drei Stellen, drei Zahlen.
        // ⭐ Fuer den Nachbau heisst das: jede Lage, die ein kopfloser
        // Pruefstand misst, haengt an DIESER Zahl und nicht an der des
        // Spielers. Darum nennt --fenster-check sie in seiner Zeile mit; eine
        // Lage ohne ihr Schirmmass ist eine Zahl ohne Herkunft.
        // Ein Rueckfall auf project.godot stand hier kurz und ist wieder weg:
        // er waere nie gelaufen, und ein Zweig, den nichts betritt, ist eine
        // Behauptung.
        return sicht == null ? Vector2.Zero : sicht.GetVisibleRect().Size;
    }

    private static int _takt;

    /// <summary>
    /// <b>Der Fenstertakt</b> — <c>0x4505F0</c> und <c>0x44FB10</c> in einem.
    ///
    /// <para>⚠ Er gehört an den SIMULATIONSTAKT, nicht an die Bildrate: die
    /// Lebensdauer zählt in Takten, und ein Meldungsfenster darf nicht auf einem
    /// schnellen Rechner kürzer stehen als auf einem langsamen.</para>
    /// </summary>
    public static void Takt()
    {
        _takt++;

        // ---- die Blenden, je Takt ein Bild ---------------------------------
        for (int i = _liste.Count - 1; i >= 0; i--)
        {
            var f = _liste[i];
            if (f.ZuBild >= 0)
            {
                if (f.ZuBild < BilderZu) { f.ZuBild++; Blende(f); }
                else { Fertig(f); _liste.RemoveAt(i); Zeichenfolge(); }
                continue;
            }
            if (f.AufBild >= 0 && f.AufBild < BilderAuf) { f.AufBild++; Blende(f); }
        }

        // ---- die Lebensdauer, alle 20 Takte --------------------------------
        if (_takt % StandzeitTakte != 0) return;
        foreach (var f in _liste)
        {
            if (f.Standzeit <= 0 || f.ZuBild >= 0) continue;   // 0 = niemals
            if (--f.Standzeit > 0) continue;
            Schliessen(f);
            VonSelbstZu++;
        }
    }

    /// <summary>
    /// Die Blende auf den Knoten bringen.
    ///
    /// <para>⚠ UNSERE Umsetzung: das Original klappt den Punktpuffer zur
    /// Mittelzeile zusammen (<c>0x44F8B0</c>), wir skalieren um die Mittelachse.
    /// Gleicher Anblick, anderer Weg — siehe den Klassenkopf.</para>
    /// </summary>
    private static void Blende(Fenster f)
    {
        if (Lebend(f) is not Control c) return;
        float anteil = f.ZuBild >= 0
            ? 1f - Mathf.Clamp(f.ZuBild / (float)BilderZu, 0f, 1f)
            : Mathf.Clamp(f.AufBild / (float)BilderAuf, 0f, 1f);

        c.PivotOffset = c.Size * 0.5f;         // um die MITTELLINIE, nicht die Ecke
        c.Scale = new Vector2(1f, Mathf.Max(anteil, 0.001f));
        c.Visible = anteil > 0.001f;

        // ⭐ Der Klang kommt beim AUFGEHEN, und zwar wenn die Blende fertig ist
        // (0x44FC90 malt erst bei >= 4 und spielt dann 0x133).
        if (f.ZuBild < 0 && f.AufBild == BilderAuf)
        {
            f.AufBild = -1;                    // offen, nicht mehr am Aufgehen
            Audio.SoundBankPlayer.Play(KlangAuf);
        }
    }

    /// <summary>Platz 0 oben — in Godot zeichnet das SPÄTERE Kind oben, also
    /// wird die Liste rückwärts auf die Knotenreihenfolge gelegt.</summary>
    private static void Zeichenfolge()
    {
        for (int i = _liste.Count - 1, k = 0; i >= 0; i--, k++)
        {
            if (_liste[i].Knoten is not Node n || !GodotObject.IsInstanceValid(n)) continue;
            var eltern = n.GetParent();
            if (eltern != null && eltern.GetChildCount() > k) eltern.MoveChild(n, k);
        }
    }
}

public static partial class WindowManagerCheck
{
    /// <summary>
    /// <c>--fenster-check</c> — <b>die sechs Regeln der Fensterverwaltung</b>,
    /// jede mit ihrer Gegenprobe (22.08.2026, OFFENE_FRAGEN <b>BM</b>).
    ///
    /// <para>⚠ Gemessen wird ohne Godot-Knoten: die Verwaltung führt eine
    /// Liste, und genau die wird geprüft. Ein Lauf, der erst ein Fenster malen
    /// müsste, hinge an der Oberfläche statt an der Regel.</para>
    /// </summary>
    public static string Lauf()
    {
        var sb = new System.Text.StringBuilder("fenster-check\n");
        // ⚠ Der Lauf ÖFFNET Fenster, und Oeffnen setzt seit dem 25.08.2026 das
        // Ereignisbyte. Ein Prüfstand, der dabei den Spielzustand verändert,
        // misst beim nächsten Mal sich selbst — darum wird beides gesichert und
        // am Ende zurückgelegt.
        var merkeMelder = WindowManager.Ereignismelder;
        int merkeEreignis = Campaign.CampaignHints.Ereignis;
        WindowManager.Leeren();
        bool alles = true;
        void Sag(string was, bool ok)
        {
            sb.Append($"  {was}: {(ok ? "richtig" : "FALSCH")}\n");
            alles &= ok;
        }

        // 1. Doppelöffnungssperre: dieselbe Art nur einmal
        var a = WindowManager.Oeffnen(19, null);
        var b = WindowManager.Oeffnen(19, null);
        Sag($"Art 19 zweimal geoeffnet -> {(b == null ? "abgewiesen" : "ZWEI")}, "
            + $"offen {WindowManager.Anzahl} (erwartet 1)",
            a != null && b == null && WindowManager.Anzahl == 1);

        // 1b. Gegenprobe: EINE ANDERE Art darf sehr wohl dazu
        var c = WindowManager.Oeffnen(20, null);
        Sag($"Art 20 dazu -> offen {WindowManager.Anzahl} (erwartet 2)",
            c != null && WindowManager.Anzahl == 2);

        // 1c. Objektfenster: je Kennung eines
        var d1 = WindowManager.Oeffnen(23, null, kennung: 7);
        var d2 = WindowManager.Oeffnen(23, null, kennung: 7);
        var d3 = WindowManager.Oeffnen(23, null, kennung: 8);
        Sag($"Art 23 Kennung 7 zweimal -> {(d2 == null ? "abgewiesen" : "ZWEI")}, "
            + "Kennung 8 dazu -> " + (d3 != null ? "geoeffnet" : "ABGEWIESEN"),
            d1 != null && d2 == null && d3 != null);

        // 2. Platz 0 ist oben: das zuletzt geöffnete steht vorn
        Sag($"zuletzt geoeffnet (Art {WindowManager.Liste[0].Art}) steht auf Platz 0",
            WindowManager.Liste[0].Art == 23 && WindowManager.Liste[0].Kennung == 8);

        // 3. Nach vorn holen
        WindowManager.NachVorn(a);
        Sag($"Art 19 nach vorn geholt -> Platz 0 ist Art {WindowManager.Liste[0].Art}",
            WindowManager.Liste[0] == a);

        // 4. Die vier Arten, die HINTEN bleiben
        var st = WindowManager.Oeffnen(WindowManager.ArtStatuszeile, null);
        Sag($"Statuszeile (Art 44) geoeffnet -> Platz 0 ist Art {WindowManager.Liste[0].Art}, "
            + $"sie selbst auf Platz {WindowManager.Liste.Count - 1}",
            WindowManager.Liste[0] == a && WindowManager.Liste[^1] == st);
        // Gegenprobe: eine gewoehnliche Art kommt sehr wohl nach vorn
        var gew = WindowManager.Oeffnen(29, null);
        Sag("Gegenprobe: Art 29 kommt nach vorn", WindowManager.Liste[0] == gew);

        // 5. Die Lebensdauer: 0 = niemals, n = nach 20*n Takten
        WindowManager.Leeren();
        var ewig = WindowManager.Oeffnen(19, null, standzeit: 0);
        var kurz = WindowManager.Oeffnen(WindowManager.ArtMeldung, null, standzeit: 2);
        for (int t = 0; t < 20 * 2 - 1; t++) WindowManager.Takt();
        bool nochDa = WindowManager.Offen(WindowManager.ArtMeldung) != null;
        for (int t = 0; t < 1 + WindowManager.BilderZu + 1; t++) WindowManager.Takt();
        bool jetztWeg = WindowManager.Offen(WindowManager.ArtMeldung) == null;
        bool ewigDa = WindowManager.Offen(19) != null;
        Sag($"Standzeit 2: nach 39 Takten {(nochDa ? "noch da" : "SCHON WEG")}, "
            + $"nach 40 + Zublende {(jetztWeg ? "weg" : "NOCH DA")}; "
            + $"Standzeit 0 {(ewigDa ? "bleibt" : "IST WEG")}",
            ewig != null && kurz != null && nochDa && jetztWeg && ewigDa);

        // 6. Zwanzig Plaetze
        WindowManager.Leeren();
        for (int i = 0; i < 25; i++) WindowManager.Oeffnen(200 + i, null);
        Sag($"25 Fenster geoeffnet -> {WindowManager.Anzahl} offen (erwartet "
            + $"{WindowManager.MaxFenster})",
            WindowManager.Anzahl == WindowManager.MaxFenster);

        // 7. ⭐ DER BILDSCHIRMZWANG und seine drei Ausnahmen (BL.8.2).
        WindowManager.Leeren();
        var schirm = new Godot.Vector2(640, 480);
        // ⚠ Die Knoten dieses Laufs gehoeren freigegeben — sonst meldet Godot
        // beim Beenden "RIDs of type CanvasItem were leaked", und eine solche
        // Zeile im Protokoll gewoehnt man sich an, bis sie einmal echt ist.
        var muell = new List<Godot.Control>();
        Godot.Control Kn(float x, float y, float b, float h)
        {
            var c = new Godot.Control { Position = new Godot.Vector2(x, y) };
            c.Size = new Godot.Vector2(b, h);
            muell.Add(c);
            return c;
        }

        var raus = WindowManager.Oeffnen(19, Kn(600, 460, 200, 100));
        WindowManager.InDenSchirm(raus, schirm);
        var pos = ((Godot.Control)raus!.Knoten!).Position;
        Sag($"Art 19 bei (600,460) mit 200x100 -> ({pos.X:0},{pos.Y:0}), "
            + "erwartet (439,379)",
            Mathf.Abs(pos.X - 439) < 0.5f && Mathf.Abs(pos.Y - 379) < 0.5f);

        foreach (int art in new[] { 9, 35, 48 })
        {
            WindowManager.Leeren();
            var frei = WindowManager.Oeffnen(art, Kn(600, 460, 200, 100));
            ((Godot.Control)frei!.Knoten!).Position = new Godot.Vector2(600, 460);
            WindowManager.InDenSchirm(frei, schirm);
            var q = ((Godot.Control)frei.Knoten!).Position;
            Sag($"Art {art} ist ausgenommen -> bleibt bei ({q.X:0},{q.Y:0})",
                Mathf.Abs(q.X - 600) < 0.5f && Mathf.Abs(q.Y - 460) < 0.5f);
        }

        // 7b. ⭐⭐ WO EIN FENSTER AUFGEHT: MAUS-3 (26.08.2026, siehe AnDieMaus).
        //     Gemessen wird die Rechnung der Verwaltung, nicht eine eigene:
        //     die Maus kommt aus der Naht `Mausquelle`, die Lage aus dem
        //     Knoten, den `AnDieMaus` angefasst hat.
        WindowManager.Leeren();
        var merkeMaus = WindowManager.Mausquelle;
        WindowManager.Mausquelle = () => new Godot.Vector2(200, 150);
        var beiMaus = WindowManager.Oeffnen(31, Kn(0, 0, 260, 100));
        bool angefasstMaus = WindowManager.AnDieMaus(beiMaus);
        var mp = ((Godot.Control)beiMaus!.Knoten!).Position;
        Sag($"Art 31, Maus (200,150) -> ({mp.X:0},{mp.Y:0}), erwartet (197,147)",
            angefasstMaus && Mathf.Abs(mp.X - 197) < 0.5f && Mathf.Abs(mp.Y - 147) < 0.5f);

        // Gegenprobe A — das NULLMODELL: ohne AnDieMaus bleibt das Fenster
        // liegen, wo der Knoten steht. Genau das war der gemeldete Fehler
        // (»oeffnet oben links«), und ohne diese Messung wuerde die obere
        // durchgehen, selbst wenn AnDieMaus gar nichts taete.
        WindowManager.Leeren();
        var ohne = WindowManager.Oeffnen(31, Kn(0, 0, 260, 100));
        var op = ((Godot.Control)ohne!.Knoten!).Position;
        Sag($"Nullmodell: ohne AnDieMaus bleibt Art 31 bei ({op.X:0},{op.Y:0}), "
            + "erwartet (0,0)",
            Mathf.Abs(op.X) < 0.5f && Mathf.Abs(op.Y) < 0.5f);

        // Gegenprobe B — der Zwang greift auch hier: eine Maus am rechten
        // unteren Rand darf das Fenster nicht aus dem Bild schieben.
        // ⚠ Die Maus muss an den ECHTEN Rand, nicht an den von (640,480):
        //   AnDieMaus zwingt gegen Schirmmass(), und im ersten Anlauf stand
        //   hier (630,470). Auf einem 1600x900-Schirm ragt dort nichts heraus,
        //   der Zwang hatte nichts zu tun, und die Messung prueft nichts.
        var sm = WindowManager.Schirmmass();
        WindowManager.Leeren();
        WindowManager.Mausquelle = () => sm - new Godot.Vector2(10, 10);
        var eck = WindowManager.Oeffnen(31, Kn(0, 0, 260, 100));
        WindowManager.AnDieMaus(eck);
        var ep = ((Godot.Control)eck!.Knoten!).Position;
        bool eckOk = Mathf.Abs(ep.X - (sm.X - 260 - 1)) < 0.5f
                  && Mathf.Abs(ep.Y - (sm.Y - 100 - 1)) < 0.5f;
        Sag($"Art 31, Maus ({sm.X - 10:0},{sm.Y - 10:0}) auf Schirm {sm.X:0}x{sm.Y:0} "
            + $"-> ({ep.X:0},{ep.Y:0}), erwartet ({sm.X - 261:0},{sm.Y - 101:0}) "
            + "— in den Schirm gezwungen",
            eckOk);

        // Gegenprobe C — eine Art mit FESTER Lage laesst sich von der Maus
        // nicht bewegen. Art 37 ist mittig (0x44276C), Maus hin oder her.
        // ⚠ Das Mass des Knotens steht MIT in der Zeile: die Lage rechnet sich
        //   aus ihm, und eine Lage ohne ihr Mass ist eine Zahl ohne Herkunft.
        WindowManager.Leeren();
        WindowManager.Mausquelle = () => new Godot.Vector2(10, 10);
        var festKn = Kn(0, 0, 200, 100);
        var fest = WindowManager.Oeffnen(37, festKn);
        bool angefasstFest = WindowManager.AnDieMaus(fest);
        var festP = festKn.Position;
        var festG = new Godot.Vector2(Mathf.Max(festKn.Size.X, festKn.CustomMinimumSize.X),
                                      Mathf.Max(festKn.Size.Y, festKn.CustomMinimumSize.Y));
        var festSoll = (sm - festG) / 2f;
        Sag($"Art 37 (Mass {festG.X:0}x{festG.Y:0}) hat feste Lage -> "
            + $"({festP.X:0},{festP.Y:0}) statt (7,7), erwartet mittig "
            + $"({festSoll.X:0},{festSoll.Y:0}); AnDieMaus meldet "
            + (angefasstFest ? "ANGEFASST" : "nicht angefasst"),
            !angefasstFest && Mathf.Abs(festP.X - festSoll.X) < 0.5f
                           && Mathf.Abs(festP.Y - festSoll.Y) < 0.5f);
        WindowManager.Mausquelle = merkeMaus;

        // 8. ⭐ DIE FESTEN LAGEN
        WindowManager.Leeren();
        var haupt = WindowManager.Oeffnen(35, Kn(0, 0, 200, 240));
        WindowManager.FesteLage(haupt, schirm);
        var hp = ((Godot.Control)haupt!.Knoten!).Position;
        Sag($"Hauptmenue (35) -> ({hp.X:0},{hp.Y:0}), erwartet (25,220) "
            + "= x fest 25, y = 480-240-20",
            Mathf.Abs(hp.X - 25) < 0.5f && Mathf.Abs(hp.Y - 220) < 0.5f);

        WindowManager.Leeren();
        var pause = WindowManager.Oeffnen(40, Kn(0, 0, 180, 60));
        WindowManager.FesteLage(pause, schirm);
        var pp = ((Godot.Control)pause!.Knoten!).Position;
        Sag($"Pause (40) -> ({pp.X:0},{pp.Y:0}), erwartet (230,96) "
            + "= x mittig, y = Hoehe/5",
            Mathf.Abs(pp.X - 230) < 0.5f && Mathf.Abs(pp.Y - 96) < 0.5f);

        // Gegenprobe: eine Art OHNE feste Lage wird nicht angefasst
        WindowManager.Leeren();
        var frei2 = WindowManager.Oeffnen(19, Kn(111, 222, 50, 50));
        ((Godot.Control)frei2!.Knoten!).Position = new Godot.Vector2(111, 222);
        bool angefasst = WindowManager.FesteLage(frei2, schirm);
        var fp = ((Godot.Control)frei2.Knoten!).Position;
        Sag($"Gegenprobe: Art 19 hat keine feste Lage -> bleibt ({fp.X:0},{fp.Y:0})",
            !angefasst && Mathf.Abs(fp.X - 111) < 0.5f && Mathf.Abs(fp.Y - 222) < 0.5f);

        // 9. ⭐⭐ DIE TAFEL »GEBAEUDEART -> FENSTERART« (BM.3), alle 17 Zeilen.
        // ⚠ Geprueft wird die GANZE Tafel, nicht die drei Zeilen, fuer die wir
        // ein Fenster haben — eine Tafel, die nur die gebauten Zeilen fuehrt,
        // sieht vollstaendig aus und ist es nicht.
        (int Bau, int Fenster)[] tafel =
        {
            (1, 6), (2, 8), (3, 8), (4, 8), (5, 23), (6, 2), (7, 20), (8, 0),
            (9, 5), (10, 18), (11, 11), (12, 2), (13, 21), (14, 31), (15, 18),
            (16, 0), (17, 0),
        };
        int falsch = 0;
        foreach (var (bau, soll) in tafel)
            if (Rendering.MapEntityLayer.OriginalFensterArt(bau) != soll) falsch++;
        Sag($"Tafel Gebaeudeart -> Fensterart: {tafel.Length - falsch} von "
            + $"{tafel.Length} Zeilen treffen", falsch == 0);

        // Gegenprobe: die zwei Leerarme geben WIRKLICH nichts, und die drei
        // Paare teilen sich WIRKLICH ein Fenster.
        bool paare = Rendering.MapEntityLayer.OriginalFensterArt(2)
                  == Rendering.MapEntityLayer.OriginalFensterArt(4)
                  && Rendering.MapEntityLayer.OriginalFensterArt(6)
                  == Rendering.MapEntityLayer.OriginalFensterArt(12)
                  && Rendering.MapEntityLayer.OriginalFensterArt(10)
                  == Rendering.MapEntityLayer.OriginalFensterArt(15);
        Sag("die drei Paare teilen sich je ein Fenster (2/3/4, 6/12, 10/15)", paare);

        // 10. ⭐⭐ MESSUNG 19 — DER SZENENWECHSEL MIT OFFENEN FENSTERN
        //
        // ⚠⚠ Gemeldet am 23.08.2026: »wenn ich ein Gefecht verlasse, bleibt der
        // Bildschirm schwarz und ich komme nicht mehr ins Hauptmenue«.
        // ChangeSceneToFile gibt die alte Szene frei; MainMenu._Ready ruft
        // danach als ERSTE Anweisung ueber LeaveToMenu.Tidy die Leeren() hier.
        // Die Fensterknoten sind zu diesem Zeitpunkt tot. Die alte Fassung warf,
        // _Ready starb in Zeile eins, das Menue wurde nie gebaut.
        //
        // ⚠ Die 18 Messungen davor waren gruen und BLIEBEN es — sie laufen alle
        // mit lebenden Knoten. Eine Verwaltung, die nur im Normalfall geprueft
        // wird, ist im Ausnahmefall ungeprueft.
        WindowManager.Leeren();
        var tot1 = new Godot.Control();
        var tot2 = new Godot.Control();
        WindowManager.Oeffnen(19, tot1);
        WindowManager.Oeffnen(23, tot2);
        int vorher = WindowManager.Anzahl;
        var t1 = WindowManager.Offen(19);
        tot1.Free();
        tot2.Free();

        // ⭐ DAS NULLMODELL, und es ist die eigentliche Lehre: der
        // Mustervergleich »is Control« gelingt beim freigegebenen Knoten
        // WEITER — der C#-Umschlag behaelt seinen Typ. Nur IsInstanceValid
        // sieht den Unterschied. Genau darauf ist die alte Fassung
        // hereingefallen: sie sah wie eine Pruefung aus und war keine.
        bool typPasstNoch = t1!.Knoten is Godot.Control;
        bool lebtNoch = Godot.GodotObject.IsInstanceValid(t1.Knoten);
        Sag($"Nullmodell: freigegebener Knoten -> »is Control« sagt {typPasstNoch}, "
            + $"IsInstanceValid sagt {lebtNoch} (erwartet true / false)",
            typPasstNoch && !lebtNoch);

        bool geworfen = false;
        try { WindowManager.Leeren(); }
        catch (System.Exception e)
        {
            geworfen = true;
            sb.AppendLine($"     ⚠ geworfen: {e.GetType().Name}");
        }
        Sag($"Leeren() mit {vorher} offenen, freigegebenen Fenstern -> "
            + $"{(geworfen ? "GEWORFEN — das ist der schwarze Bildschirm" : "ueberlebt")}, "
            + $"offen danach {WindowManager.Anzahl}",
            !geworfen && WindowManager.Anzahl == 0);

        // 11. ⭐⭐ DAS EREIGNISBYTE (0x441270 / 0x4412C2) UND SEINE AUSNAHME.
        //
        // ⚠ Zwei Messungen, weil eine allein nichts trennt: dass eine Zahl
        // ankommt, sagt noch nicht, dass sie von DIESEM Fenster kommt — und
        // dass die Karte nichts setzt, sagt nichts, wenn nie etwas gesetzt
        // wird. Erst zusammen zeigen sie den `cmp al,3 / je` @0x4412C0.
        WindowManager.Leeren();
        int gesehen = -1;
        WindowManager.Ereignismelder = a => gesehen = a;
        Campaign.CampaignHints.Ereignis = 0;

        WindowManager.Oeffnen(31, null);
        Sag($"Art 31 geoeffnet -> Ereignisbyte {WindowManager.EreignisZuletzt}, "
            + $"Kontexthilfe {Campaign.CampaignHints.Ereignis}, Skript {gesehen} "
            + "(erwartet 31/31/31)",
            WindowManager.EreignisZuletzt == 31
            && Campaign.CampaignHints.Ereignis == 31 && gesehen == 31);

        // Die KARTE setzt nichts — alle drei Werte müssen auf 31 stehenbleiben.
        int vorKarte = WindowManager.EreignisGesetzt;
        WindowManager.Oeffnen(WindowManager.ArtKarte, null);
        Sag($"Karte (Art 3) dazu -> Ereignisbyte bleibt {WindowManager.EreignisZuletzt}, "
            + $"Setzungen {vorKarte} -> {WindowManager.EreignisGesetzt} (erwartet 31 "
            + "und keine neue Setzung)",
            WindowManager.EreignisZuletzt == 31 && gesehen == 31
            && WindowManager.EreignisGesetzt == vorKarte
            && WindowManager.Offen(WindowManager.ArtKarte) != null);

        // Und die Doppelöffnungssperre setzt auch nichts: ein Fenster, das
        // schon offen ist, wird gar nicht erst eingetragen.
        int vorZweit = WindowManager.EreignisGesetzt;
        WindowManager.Oeffnen(31, null);
        Sag($"Art 31 ein zweites Mal -> Setzungen {vorZweit} -> "
            + $"{WindowManager.EreignisGesetzt} (erwartet unveraendert)",
            WindowManager.EreignisGesetzt == vorZweit);

        // 12. ⭐ DIE ZWEI FENSTERARTEN, DIE AM 25.08.2026 RICHTIGGESTELLT WURDEN.
        // ⚠ Geprüft wird die SCHRANKE, nicht die Zahl allein: eine Fensterart
        // des Originals liegt in 1..48. Mit den alten 100/101 hätte das
        // Ereignisbyte Werte angenommen, die es im Original nicht gibt.
        Sag($"Gruppieren = Art {WindowManager.ArtGruppen}, Lokator = Art "
            + $"{WindowManager.ArtMerkpunkte} (erwartet 25/24, beide in 1..48)",
            WindowManager.ArtGruppen == 25 && WindowManager.ArtMerkpunkte == 24
            && WindowManager.ArtGruppen is > 0 and <= 48
            && WindowManager.ArtMerkpunkte is > 0 and <= 48);

        WindowManager.Leeren();
        foreach (var knoten in muell) knoten.Free();
        // Zurücklegen, was der Lauf sich geliehen hat.
        WindowManager.Ereignismelder = merkeMelder;
        Campaign.CampaignHints.Ereignis = merkeEreignis;
        sb.Append(alles ? "  BESTANDEN" : "  DURCHGEFALLEN");
        return sb.ToString();
    }
}
