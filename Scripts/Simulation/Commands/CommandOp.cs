namespace AkteEuropaReborn.Simulation.Commands;

/// <summary>
/// DIE OPCODES DES BEFEHLSBUSSES — Bereiche, Schranken und die Nummern, für die
/// eine Bedeutung belegt ist.
///
/// <para><b>Der Verteiler kennt genau drei Bereiche und zwei Sonderfälle.</b>
/// Aus dem Kopf @0x4C2262 (F:\…) bzw. @0x4C26E0 (C:\…), Befehl für Befehl:</para>
/// <code>
///   cmp eax,0x1F4   jg  →  nächster Bereich      ; 500
///   je              →  ein Sonderfall @0x4C3278
///   dec eax; cmp eax,0x1D; ja → Fehler           ; 30 Einträge
///   jmp [eax*4+0x4C4D54]                         ; Bereich A: 1..30
///   cmp eax,0x2BC   jg  →  nächster Bereich      ; 700
///   je              →  ein Sonderfall @0x4C3928
///   sub eax,0x1F5; cmp eax,0x24; ja → Fehler     ; 37 Einträge
///   jmp [eax*4+0x4C4DCC]                         ; Bereich B: 501..537
///   sub eax,0x3CD; cmp eax,0xE4; ja → Fehler     ; 229 Einträge
///   mov cl,[eax+0x4C4EEC]; jmp [ecx*4+0x4C4E60]  ; Bereich C: 973..1201
/// </code>
///
/// <para>⚠⚠ <b>20.08.2026 — ALLE VIER TAFELADRESSEN STANDEN HIER UM 0x478 ZU
/// NIEDRIG</b> (0x4C48DC / 0x4C4954 / 0x4C49E8 / 0x4C4A74). Der Aufbau des
/// Blocks war richtig gelesen, die Adressen nicht — und ein gleichmässiger
/// Versatz über alle vier heisst: nicht vier Tippfehler, sondern **eine**
/// falsche Quelle.</para>
///
/// <para>Nachgewiesen ist es zweifach. Erstens durch Suche nach dem Opcode
/// selbst (<c>FF 24 85 imm32</c>): im ganzen <c>.text</c> gibt es zwischen
/// 0x4C4000 und 0x4C5000 <b>genau zwei</b> solche Sprünge, <c>0x4C26F7</c> mit
/// <c>0x4C4D54</c> und <c>0x4C2719</c> mit <c>0x4C4DCC</c> — und beide stehen
/// hinter genau den Schranken, die oben stehen (<c>dec eax; cmp eax,0x1D</c>
/// bzw. <c>sub eax,0x1F5; cmp eax,0x24</c>). Zweitens durch den Inhalt: unter
/// 0x4C4DCC stehen acht aufsteigende Codeadressen (0x4C3726, 0x4C374B,
/// 0x4C3780 …), unter dem alten 0x4C4954 stehen Befehlsbytes.</para>
///
/// <para>⚠ Bei <c>0x4C4EEC</c> ist die Gegenprobe umgekehrt zu lesen: das ist
/// die <b>Byte</b>-Tafel für <c>mov cl,[…]</c>, dort gehören aufsteigende
/// Indizes hin (<c>02 01 22 02 | 03 04 05 06 …</c>) und eben KEINE
/// Codeadressen. Wer sie mit demselben Massstab prüft wie die drei anderen,
/// erklärt die richtige Adresse für falsch.</para>
///
/// <para><b>Zwei Schranken, die für Lockstep und Wiederholung entscheidend
/// sind</b> — und die zeigen, dass das Original seine Befehle selbst in
/// »zustandsrelevant« und »örtlich« einteilt:</para>
/// <list type="bullet">
///   <item><b>&lt; 800</b> wird in die Wiederholung geschrieben. @0x4C20A4 und
///   @0x4C215E: <c>cmp dx,0x320; jge »überspringen«</c>. Alles darunter geht
///   als 236 Byte in <c>c:\replay.mes</c> (@0x4C21A6, <c>fwrite(…,1,0xEC,f)</c>)
///   und als <c>»%d %d %d«</c> in <c>c:\replay.txt</c>.</item>
///   <item><b>&lt;= 1000</b> wird über das Netz weitergeleitet. @0x4C1FE2:
///   <c>cmp word[rec],0x3E8; jg »überspringen«</c>.</item>
/// </list>
/// <para>Damit sind die Bereiche A (1..30) und B (501..537) das, was eine
/// Partie ausmacht; 973..1000 gehen über die Leitung, aber nicht ins Protokoll;
/// 1001..1201 sind rein örtlich (Anzeige, Fehlerausgaben, Sitzungsverwaltung).
/// <b>Das ist die Einteilung, die eine Befehlsschicht braucht</b>, und sie ist
/// gelesen, nicht gesetzt.</para>
///
/// <para><b>Der Ring hat 1000 Plätze.</b> Der Weiterschalter @0x4C1B30 ist drei
/// Befehle lang: <c>inc cx; mov [eax],cx; cmp cx,0x3E8; jne; mov [eax],0</c> —
/// er wird mit der Adresse des Lese-, des Schreib- und des Sendezeigers
/// aufgerufen, alle drei laufen also modulo 1000.</para>
///
/// <para>⚠ <b>Wo das Verständnis endet.</b> 1000 × 236 = 236 000 Byte; die
/// Kladde liegt aber nur 235 520 Byte hinter der Ringbasis (in BEIDEN EXE genau
/// derselbe Abstand), und 32 Byte davor liegt ein Achtbytefeld je Spieler
/// (0xB89418, @0x4C1F15). Der Ring, den der Zeiger beschreiben KANN, ist also
/// zwei Sätze länger als der, der offenbar belegt ist. Welche der beiden Zahlen
/// die Länge des Feldes im Original war, ist NICHT gelesen — wir nehmen 1000,
/// weil das der bewiesene Zahlenraum des Zeigers ist, und schreiben die
/// Ungereimtheit hier hin statt sie glattzubügeln.</para>
/// </summary>
public static class CommandOp
{
    /// <summary>Bereich A: die Einheitenbefehle. 30 Einträge, in beiden EXE
    /// dieselben 30 mit demselben Parameter- und Feldprofil
    /// (<c>cmd_opcodes.py</c>: 30 von 30 gleich).</summary>
    public const short UnitFirst = 1, UnitLast = 30;

    /// <summary>Bereich B: Bau-, Kauf- und Entwurfsbefehle.</summary>
    public const short BuildFirst = 501, BuildLast = 537;

    /// <summary>Bereich C: Anzeige, Sitzung, Fehlerausgabe.</summary>
    public const short SystemFirst = 973, SystemLast = 1201;

    /// <summary>Die beiden Sonderfälle, die der Verteiler mit <c>je</c>
    /// abfängt, ohne Sprungtabelle.</summary>
    public const short Special500 = 500, Special700 = 700;

    /// <summary>Alles darunter geht in die Wiederholung (@0x4C20A4).</summary>
    public const short ReplayBelow = 800;

    /// <summary>Bis hierher wird weitergeleitet (@0x4C1FE2).</summary>
    public const short RelayUpTo = 1000;

    /// <summary>So viele Plätze hat der Ring (@0x4C1B3C).</summary>
    public const int RingSlots = 1000;

    // ---- die Nummern, für die eine Bedeutung BELEGT ist ---------------------

    /// <summary>
    /// <b>3 = BEWEGEN.</b> P1 = Einheitsnummer, P2/P3 = Zielzelle.
    ///
    /// <para>Von zwei Seiten belegt. <b>Der Absender</b> @0x4342E9..0x43433E
    /// baut den Satz so: <c>P1 = word[0x8320F8]</c> (die Auswahlliste, je
    /// Eintrag ein Wort) nur wenn <c>&lt; 0x1F40 = 8000</c> — das ist genau die
    /// Länge der Einheitentafel (8000 Sätze von 78 Byte @0x6E1728);
    /// <c>P2 = ent[P1].x - Mittel + Klick.x</c>, <c>P3 = ent[P1].y - Mittel +
    /// Klick.y</c>, wobei »Mittel« aus einer <c>idiv</c>-Summe der Auswahl
    /// stammt. Ist die Nummer &gt;= 8000, wird stattdessen Opcode 6
    /// geschrieben (@0x434346).</para>
    /// <para><b>Der Behandler</b> @0x4C2324 klemmt genau P2 und P3 auf die
    /// Karte: <c>if (P2 == 0 || P2 &gt; 60000) P2 = 1; if (P2 &gt;=
    /// [0x541E24]-1) P2 = [0x541E24]-2</c>, dasselbe für P3 gegen
    /// [0x541E58]. Zwei unabhängige Seiten, dieselbe Deutung.</para>
    /// <para>⚠ Bemerkenswert und für uns verbindlich: <b>geprüft wird im
    /// Behandler</b>, nicht beim Absenden. Ein Befehl darf also unsinnige Werte
    /// tragen; die Simulation muss ihn auf jeder Maschine gleich zurechtbiegen.
    /// Genau so ist es hier gebaut.</para>
    /// </summary>
    public const short Move = 3;

    /// <summary>
    /// <b>508 = SCHIFF BAUEN.</b> P1 = Spieler, P2 = Entwurfsnummer, P3 =
    /// <c>cis_typ</c> des Hafens. Der Knopf @0x44A35C schreibt den Satz, der
    /// Behandler @0x4B2B20 sucht einen freien Platz und füllt die Einheit
    /// (GAMESTATE_RE.md 3.86). Steht hier als <b>Gegenprobe unserer
    /// Parameterzählung</b>: der Vorbefund nennt (Spieler, Entwurf, Dock) —
    /// unser Profil sagt für 508 »P1, P2, P3«. Es passt.</summary>
    public const short BuildShip = 508;

    /// <summary>
    /// <b>529 = EINE EINHEIT VERKAUFEN.</b> P1 = Einheitsnummer, P2 = Preis.
    ///
    /// <para>Von beiden Seiten gelesen (18.08.2026). <b>Der Absender</b> ist die
    /// Ja-Schaltfläche des Verkaufsfensters @0x44B138: <c>word[Kladde] = 0x211</c>,
    /// <c>P1 = word[Satz+0x8C3CDA]</c> (die Einheit), <c>P2 = word[Satz+0x8C3CDC]</c>
    /// (der Preis, den der Dialog vorher selbst gerechnet hat). <b>Der
    /// Behandler</b> @0x4BFFF0 löscht den laufenden Auftrag der Einheit
    /// (<c>byte[ent+0x14] = 0</c>) und trägt sie in die Angebotstafel
    /// <c>0xB4A0D0</c> ein — 1000 Sätze zu 6 Byte, <c>{u16 Einheit, u16 Preis,
    /// u8 Zustand}</c> — auf dem ersten Platz, dessen Einheitsfeld
    /// <c>0xFFFF</c> ist, mit <b>Zustand 0xFF</b>.</para>
    ///
    /// <para>Unser Profil sagt für 529 »P1, P2« (<c>cmd_opcodes.py</c>), und der
    /// Behandler holt genau <c>word[Ring+0xE0]</c> und <c>word[Ring+0xE2]</c>.
    /// Zwei Seiten, dieselbe Zählung.</para>
    ///
    /// <para>⚠ <b>Der Doppelverkauf wird NICHT verhindert.</b> Der Behandler
    /// durchsucht die Tafel vorher nach derselben Einheit und schreibt nur eine
    /// Zeile ins Protokoll (<c>»Robot already sold.«</c>, 0x539050) — dann trägt
    /// er sie ein zweites Mal ein.</para>
    ///
    /// <para>⚠ <b>Kein Markt weit und breit.</b> Weder der Dialog noch dieser
    /// Behandler prüfen ein Gebäude, eine Zelle oder eine Entfernung; beide
    /// Funktionen sind ganz gelesen. Verkaufen ist im Original ein
    /// <b>Einheitenbefehl</b> (Eintrag 4 der Befehlsliste 0x4FD660), kein
    /// Knopf im Fenster des Geschäftszentrums.</para>
    /// </summary>
    public const short Sell = 529;

    /// <summary>
    /// <b>27 = RADAR SETZEN.</b> P1 = die Einheit, sonst nichts.
    ///
    /// <para>Der Absender ist der Befehlsmenü-Eintrag <b>20 »Radar setzen«</b>
    /// @0x448A4A: <c>word[Kladde] = 0x1B</c>, <c>P1 = word[0x4FA0C8]</c> (die
    /// gewählte Einheit). ⚠ <b>Er ist der einzige der vier Bauaufträge ohne
    /// Platzierungsmodus</b> — 17/18/19 (»Depot/Mine/Generator bauen«) setzen
    /// <c>dword[0x502ACC]</c> und warten auf einen Klick, dieser hier wirkt
    /// sofort auf der Zelle der Einheit.</para>
    ///
    /// <para>Der Behandler @0x422180 nimmt einen Mast vom Vorrat
    /// (<c>byte[+0x45]</c>), legt ihn über <c>place_radar</c> @0x421B40 an und
    /// setzt <c>word[+0x40] = 10</c>. Unser Profil sagt für 27 »P1« — und der
    /// Behandler liest genau <c>word[Ring+0xE0]</c>. Zwei Seiten, dieselbe
    /// Zählung.</para>
    ///
    /// <para>⚠ Ein Radar ist <b>kein Gebäude</b>, sondern ein Satz in einer
    /// eigenen Tafel — siehe Simulation/RadarMast.cs.</para></summary>
    public const short PlaceRadar = 27;

    /// <summary>
    /// <b>20 = BAUPLATZ SETZEN (Gebäude-Techniker).</b> P1 = die Einheit,
    /// P2/P3 = die geklickte Zelle, P4 = der Modus, P5 = die Vorkommensnummer.
    ///
    /// <para><b>Der Absender</b> ist der Kartenklick @0x437FCA, und er verzweigt
    /// vorher nach dem <b>Rumpf</b> der Einheit (<c>byte[+0x0E]</c>, @0x437FAB):
    /// <c>0x48 = 72</c> → dieser Befehl, <c>0x4A = 74</c> → Befehl 21,
    /// <c>0xC6 = 198</c> → Befehl 16. Er schreibt <c>word[0xB8A3D8] = 0x14</c>,
    /// P2/P3 als <b>Zelle + 1</b>, P4 aus <c>dword[0x502ACC]</c> und
    /// P5 aus <c>byte[0x81A3A4] − 1</c>; danach setzt er den Modus auf 0
    /// zurück.</para>
    ///
    /// <para><b>Der Behandler</b> @0x4C3241 trennt nach P4:</para>
    /// <code>
    ///   P4 == 5 (Depot):  order(Einheit, P2, P3, 0)
    ///                     cx = (P3 &lt;&lt; 8) | P2            ; gepackte Zelle
    ///   P4 == 6 (Mine):   cx = P5                         ; Vorkommensnummer
    ///                     Spalte = byte[0x6783E9 + 14·P5]
    ///                     Zeile  = byte[0x6783EA + 14·P5]
    ///                     order(Einheit, Spalte, Zeile, 0)
    ///   word[Einheit + 0x40] = cx        @0x4C3320
    ///   byte[Einheit + 0x38] = P4        @0x4C3351
    /// </code>
    ///
    /// <para>⚠ <b>Der Befehl BAUT nichts</b> — er merkt nur vor und schickt die
    /// Einheit los. Gebaut wird bei der Ankunft, im Leerlauf (Auftrag 0), und
    /// zwar vom Rumpf-Handler @0x40806A. Wer hier baute, käme ohne Fahrt aus und
    /// hätte eine andere Mechanik.</para>
    ///
    /// <para>⚠ Die Nummer 20 kollidiert nicht mit »Radar setzen«: das ist
    /// <b>Eintrag 20 der Befehlsliste</b> und <b>Kommando 27</b>. Zwei
    /// Zählungen, die man leicht verwechselt.</para></summary>
    public const short PlaceBuilding = 20;

    /// <summary>
    /// <b>21 = BAUPLATZ SETZEN (Generatorenbauer).</b> P1 = die Einheit,
    /// P2/P3 = die geklickte Zelle. <b>Kein P4</b> — @0x438023 schreibt
    /// <c>word[0xB8A3D8] = 0x15</c> und danach nur P1..P3.
    ///
    /// <para>Das ist folgerichtig: der Rumpf 74 kann genau ein Gebäude, und sein
    /// Leerlauf @0x4082DD fragt <c>byte[+0x38]</c> gar nicht ab, sondern geht
    /// unmittelbar von <c>word[+0x40]</c> zum <c>push 7</c>.</para></summary>
    public const short PlaceGenerator = 21;

    /// <summary>
    /// <b>DIE FÜNFZEHN GEBÄUDEBEFEHLE — vier Tafeln, eine je Gebäudeart.</b>
    /// Gelesen am 21.08.2026, und die Zuordnung schliesst von beiden Seiten.
    ///
    /// <para><b>Was jeder Behandler tut.</b> Alle fünfzehn sind derselbe
    /// Dreizeiler: Satzindex aus P1, Eigentümer gegen <c>byte[Gebäude+0x05]</c>
    /// aus P2, dann <c>byte[Tafel + Satz·Länge + 0x02] = ZUSTAND</c>. Die zwei
    /// bezahlten setzen zusätzlich <c>+0x06 = 0</c> (den Fortschritt) und
    /// ziehen den Preis von <c>dword[0xA9C600 + Spieler·4]</c> ab — dem
    /// Kontostand, also sec73.</para>
    ///
    /// <code>
    ///   Gebäudeart      Abschnitt  Tafel (C)  Satz   Befehle (→ Zustand)
    ///   Fabrik 2,3,4    sec24      0x87A2C0   50x14  509→3 510→4 519→2 511→0
    ///   Mine 10,15      sec28      0x878AD0   50x18  515→3 516→4 522→2 517→0
    ///   Flughafen       sec27      0x879438   50x52  536→2 520→1 524→0
    ///   Basis           sec23      0x878E58   50x16  521→1 525→0
    /// </code>
    ///
    /// <para>⭐ <b>Die Zuordnung Tafel→Abschnitt stand schon in unserem Baum</b>
    /// und musste nicht geraten werden: <c>Import/BuildingPatterns.cs</c> hat
    /// »der Lader legt sec24 (0x2bc = 50x14) nach 0x87A2C0 und sec28 (0x384 =
    /// 50x18) nach 0x878AD0«, <c>GAMESTATE_RE.md</c> hat sec27 → 0x879438
    /// (50x52) und sec23 → Basis (16 Byte). Neu ist nur, welcher BEFEHL welche
    /// Zahl hineinschreibt.</para>
    ///
    /// <para>⭐ <b>Und die Zustandszahlen sind unsere eigenen.</b> 3 und 4 sind
    /// <c>FaExpand</c>/<c>FaProdUp</c>, 2 ist <c>FaRepair</c>, 1 ist
    /// <c>StRepair</c>, 0 ist <c>StAktiv</c> — alle vier standen seit Wochen
    /// so im Baum, aus einer ganz anderen Quelle gelesen (den vier Zeichnern).
    /// Zwei unabhängige Lesungen, dasselbe Ergebnis.</para>
    ///
    /// <para><b>Welcher Ausbau welcher ist</b>, entscheidet der PREIS und nicht
    /// eine Vermutung: 509 nimmt <c>word[Satz+0x0A]</c>, 510 nimmt
    /// <c>word[Satz+0x0C]</c> (@0x44AD8A gegen @0x44AE9F) — und +0x0A/+0x0C
    /// heissen bei uns seit dem 18.08. <c>CostStore</c> und <c>CostProd</c>.
    /// Jeder Ausbau vervielfacht nur seinen EIGENEN Preis mit 3/2.</para>
    ///
    /// <para>⚠ <b>523 und 526 sind TOT.</b> Beide sind ein zweiter »zurück auf
    /// 0« für Fabrik bzw. Mine, beide haben einen vollständigen Behandler — und
    /// im ganzen Programm keinen einzigen Absender (Bytesuche nach
    /// <c>mov word[0xB8A3D8], imm16</c>: 35 der 37 Nummern aus Bereich B haben
    /// einen, diese zwei nicht). Dieselbe Sorte Fund wie die vier toten
    /// Mauszeiger.</para>
    ///
    /// <para>⚠ <b>Der Absender prüft, der Behandler auch.</b> @0x44AD6D fragt
    /// <c>cmp cl,3; je</c> — wer schon ausbaut, sendet gar nicht erst; und
    /// @0x44AD91 vergleicht den Preis mit dem Konto, bevor gesendet wird. Der
    /// Behandler tut beides noch einmal. Wir halten es genauso: geprüft wird in
    /// <c>MapEntityLayer.GiveBuildingJob</c>, also im Behandler.</para>
    /// </summary>
    public const short FactoryExpandStore = 509, FactoryExpandProd = 510,
                       FactoryIdle = 511, FactoryRepair = 519;
    public const short MineExpandStore = 515, MineExpandProd = 516,
                       MineIdle = 517, MineRepair = 522;
    public const short AirportHalt = 520, AirportIdle = 524, AirportExpand = 536;
    public const short BaseRepair = 521, BaseIdle = 525;

    /// <summary>Die zwei Nummern aus Bereich B, die niemand sendet — sie stehen
    /// hier, damit niemand sie für eine Lücke hält. Siehe oben.</summary>
    public const short DeadFactoryIdle2 = 523, DeadMineIdle2 = 526;

    /// <summary>⚠ <b>1001 trägt den ZUFALLSKEIM.</b> @0x419512..0x419525:
    /// <c>call rand; mov word[0xB4FA20],ax; mov word[Kladde+0x08],ax; mov
    /// word[Kladde+0x00],0x3E9</c> — der Keim der Partie wird als P1 eines
    /// Befehls verteilt. Nicht heute gebaut (Keimverteilung ist nicht
    /// Gegenstand dieses Auftrags), aber hier festgehalten, weil es die Stelle
    /// ist, an die er gehört.</summary>
    public const short Seed = 1001;

    // ---- unsere eigenen Nummern --------------------------------------------

    /// <summary>
    /// ⚠ <b>UNSERE SETZUNG.</b> Ab hier stehen Befehle, für die wir im Original
    /// KEINE Nummer belegt haben. Sie liegen mit Absicht über
    /// <see cref="SystemLast"/> = 1201, damit sie mit keiner gelesenen Nummer
    /// kollidieren; sollte später eine echte Zuordnung gefunden werden, wird
    /// hier umgenummert und nichts anderes.
    ///
    /// <para>Warum überhaupt eigene Nummern: unser Angriffsbefehl trägt eine
    /// Einheitennummer als ZIEL (unser <c>Entity</c>-Index), und für einen
    /// solchen Befehl haben wir im Original keinen Behandler nachgewiesen.
    /// Opcode 1 sieht danach aus (P1 = Einheit, P2 = Richtung aus der Tafel
    /// 0x4F4AF0), ist aber ein RICHTUNGSBEFEHL — das wäre eine Deutung ohne
    /// Zahl, und die wird nicht eingebaut (Regel 6).</para>
    /// </summary>
    public const short OursFirst = 2000;

    /// <summary>⚠ UNSERE SETZUNG: P1 = Einheit, P2 = Zielnummer, P3 = 1 wenn
    /// angereiht, P4/P5 = Zelle des Ziels beim Anreihen.</summary>
    public const short OursAttack = 2001;

    /// <summary>⚠ UNSERE SETZUNG: P1 = Einheit. »Anhalten« für eine Einheit.
    /// Das Original hat »Anhalten« in seiner Befehlsliste (0x4FC698 Eintrag 27,
    /// stride 30) — welcher Opcode ihn ausführt, ist NICHT gelesen.</summary>
    public const short OursStop = 2002;

    /// <summary>
    /// ⚠⚠ <b>UNSERE SETZUNG, und eine BEWUSSTE ABWEICHUNG</b>: ein Flugbefehl
    /// des Spielers. P1 = Steckplatz des Flugzeugs (nicht Einheitennummer!),
    /// P2/P3 = Zielzelle.
    ///
    /// <para><b>Das Original hat diesen Befehl nicht.</b> Gesucht wurde nach
    /// der FORM: das Zielfeld eines Flugzeugs liegt auf <c>+0x14/+0x15</c>
    /// (0x6DDF84/85), und geschrieben wird es an 20 Stellen — alle im
    /// Flugtakt selbst (0x423xxx–0x426xxx), <b>keine</b> in einem
    /// Befehlsbehandler (0x43xxxx–0x45xxxx). Flugzeuge handeln im Original
    /// selbständig; der Spieler kauft sie und schickt sie los, mehr nicht.</para>
    ///
    /// <para>Die MECHANIK dagegen ist gelesen und wird nur ausgelöst, nicht
    /// erfunden: Auftrag <b>1</b> heißt »flieg nach (x,y)«, und
    /// <c>air_back_to_airport</c> @0x42646D setzt genau diese drei Bytes
    /// (Zielspalte, Zielzeile, Auftrag 1). Wir setzen dieselben.</para>
    ///
    /// <para>⚠ Der Befehl wird NUR außerhalb der Kampagne angenommen — siehe
    /// <c>CommandBridge.ApplyAirMove</c>. Die Kampagne bleibt originaltreu,
    /// das Gefecht darf abweichen, und die Abweichung steht hier.</para></summary>
    public const short OursAirMove = 2003;

    /// <summary>Geht dieser Befehl in die Wiederholung? (Schranke des
    /// Originals, @0x4C20A4.) Unsere eigenen Nummern liegen darüber und werden
    /// deshalb wie örtliche behandelt — ⚠ das ist eine Folge unserer
    /// Nummernwahl, keine Aussage über das Original.</summary>
    public static bool GoesToReplay(short op) => op > 0 && op < ReplayBelow;

    /// <summary>Geht dieser Befehl über die Leitung? (@0x4C1FE2.)</summary>
    public static bool IsRelayed(short op) => op > 0 && op <= RelayUpTo;

    /// <summary>Kennt der Verteiler diese Nummer überhaupt? Genau die drei
    /// Bereiche und die zwei Sonderfälle.</summary>
    public static bool IsOriginal(short op)
        => (op >= UnitFirst && op <= UnitLast)
        || op == Special500 || (op >= BuildFirst && op <= BuildLast)
        || op == Special700 || (op >= SystemFirst && op <= SystemLast);

    public static string NameOf(short op) => op switch
    {
        Move => "Bewegen",
        OursAirMove => "Flugziel (unsere Abweichung)",
        BuildShip => "Schiff bauen",
        Sell => "Verkaufen",
        PlaceRadar => "Radar setzen",
        PlaceBuilding => "Bauplatz setzen (Gebaeude-Techniker)",
        PlaceGenerator => "Bauplatz setzen (Generatorenbauer)",
        Seed => "Zufallskeim",
        OursAttack => "Angreifen (unsere Setzung)",
        OursStop => "Anhalten (unsere Setzung)",
        _ when op >= UnitFirst && op <= UnitLast => "Einheitenbefehl (Bereich A, unbenannt)",
        _ when op >= BuildFirst && op <= BuildLast => "Bau/Kauf (Bereich B, unbenannt)",
        _ when op >= SystemFirst && op <= SystemLast => "System (Bereich C, unbenannt)",
        _ => "unbekannt",
    };
}
