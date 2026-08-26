namespace AkteEuropaReborn.Import;

using System;
using System.Collections.Generic;

/// <summary>
/// <b>DER WALD — was ein Kartenobjekt ist, wann es aufragt, und was brennt.</b>
///
/// <para>⚠ 18.08.2026. Zwei Fragen, eine Antwort. Gemeldet waren sie getrennt:
/// »in Original Kampagne 1 gibt es z. B. von Haus aus ein paar brennende Bäume,
/// die haben wir garnicht« und »ab wann ragt ein Objekt auf«. Beide hängen an
/// derselben Stelle des Originals: an der BELEGUNGSKARTE, nicht an der
/// Kachel.</para>
///
/// <para><b>Die vier Zahlenräume der Belegungskarte</b> (imap, 256×256 u16,
/// <c>0xBDEA80</c>, Zellindex <c>spalte*256 + zeile</c>). Gelesen an der
/// Trefferroutine <c>Zasah</c> (C: @0x40C9A0), die den Getroffenen genau nach
/// diesen Grenzen aufteilt:</para>
/// <list type="bullet">
///   <item>&lt; 8000 — eine lebende Einheit (@0x40C9AC <c>cmp di, 0x1F40</c>)</item>
///   <item>10000..13999 — eine Infanteriezelle, bis zu 9 Mann (@0x40D017
///         <c>sub di, 0x2710</c>, Tafel <c>0x7847EC</c>, Schleife bis 9
///         @0x40D258)</item>
///   <item><b>50000..55999 — WALD</b> (@0x40D61D <c>cmp di, 0xC350</c> …
///         <c>0xDAC0</c>), Tafel <c>0xBFF3E0</c>, 6000 Einträge à 3 Byte</item>
///   <item>60000..60299 — ein Gebäude (@0x40D269), Tafel <c>0xC06910</c></item>
///   <item><b>61000..63999 — ein ZERSTÖRBARES OBJEKT</b> (@0x40D3CB
///         <c>cmp di, 0xEE48</c> … <c>0xFA00</c>), Tafel <c>0xC03A30</c>,
///         Einträge à 6 Byte</item>
///   <item>0xFFFC Wasser, 0xFFFD unwegsam, 0xFFFE leer (siehe
///         <c>CwmData.SpatialCell</c>)</item>
/// </list>
///
/// <para><b>Beide Tafeln stehen in der KARTENDATEI.</b> Der Spielstandschreiber
/// @0x41D210 legt 131 Blöcke in fester Reihenfolge ab; Block 18 ist
/// <c>0x4650</c> = 18000 Byte nach <c>0xBFF3E0</c> (= 6000 × 3, der Wald) und
/// Block 4 ist <c>0x2EE0</c> = 12000 Byte nach <c>0xC03A30</c> (die Objekte).
/// Eine <c>.CWM</c> trägt die ersten 38 Blöcke, beide sind also da — siehe
/// <see cref="WaldSektion"/> und <see cref="ObjektSektion"/>.</para>
///
/// <para><b>Waldeintrag, 3 Byte:</b> +0 Spalte, +1 Zeile, +2 Zustand. Zustand 1
/// = steht, 0 = weg, 2..254 = BRENNT (der Zähler läuft hoch). Gelesen am
/// Brandtakt @0x4CA330 und an <c>zapal</c> @0x4CAC50.</para>
///
/// <para><b>Objekteintrag, 6 Byte:</b> +0 Spalte, +1 Zeile, +2 Art (Index in
/// die Arttafel <c>0xBB3B60</c>, 8 Byte je Art), +3 Schadenszähler. Gelesen
/// @0x40D3E1 ff.</para>
///
/// <para>Was diese Zeile NICHT tut: die Kacheln zeichnen. Sie sagt nur, welche
/// Zelle im Original ins Zeilenfach kommt und welche Kachel an die Stelle eines
/// angezündeten Baums tritt. Das Zeichnen steht in <c>MapBaker</c> und
/// <c>MapObjects</c>.</para>
/// </summary>
public static class MapForest
{
    // ---- die Sektionen der Kartendatei -------------------------------------

    /// <summary>Sektion 6 — die Belegungskarte (imap), 256×256 u16 nach
    /// <c>0xBDEA80</c>. Spaltenweise: Index <c>spalte*256 + zeile</c>
    /// (@0x41F345).</summary>
    public const int ImapSektion = 6;

    /// <summary>Sektion 18 — die Waldtafel, 6000 × 3 Byte nach
    /// <c>0xBFF3E0</c>. Länge <c>0x4650</c>, Block 18 des Spielstandschreibers
    /// @0x41D210.</summary>
    public const int WaldSektion = 18;

    /// <summary>Sektion 4 — die Objekttafel, 2000 × 6 Byte nach
    /// <c>0xC03A30</c>. Länge <c>0x2EE0</c>, Block 4 desselben Schreibers.
    /// ⚠ Die Grenze <c>0xFA00</c> im Code ließe 3000 Einträge zu, die Tafel hat
    /// aber nur bis <c>0xC06910</c> (= die Gebäudetafel) Platz, also 2000. Wir
    /// lesen die Sektionslänge, nicht die Grenze.</summary>
    public const int ObjektSektion = 4;

    // ---- die Zahlenräume ----------------------------------------------------

    public const int WaldVon = 50000, WaldBis = 56000;      // @0x40D61D/0x40D628
    public const int GebaeudeVon = 60000, GebaeudeBis = 60300;   // @0x40D269/0x40D274
    public const int ObjektVon = 61000, ObjektBis = 64000;  // @0x40D3CB/0x40D3D6

    public const int WaldStride = 3, ObjektStride = 6;

    // ---- die Kachelfamilie des Waldes --------------------------------------

    /// <summary>Der Fuss der Waldkachelfamilie: <c>0x288D</c> = 10381. Steht
    /// dreimal im Original, jedesmal als <c>sub ax, 0x288D</c> vor derselben
    /// Rechnung — im Brandtakt @0x4CA3F6, in <c>zapal</c> @0x4CACB9 und in
    /// <c>zrus</c> @0x4CAD92.</summary>
    public const int BodenBasis = 10381;

    /// <summary>Der Fuss der VERKOHLTEN Familie: <c>0x29AA</c> = 10666, genau
    /// einmal im ganzen Programm (@0x4CACE0, in <c>zapal</c>). Abstand zur
    /// Bodenfamilie: 285.</summary>
    public const int KohleBasis = 10666;

    /// <summary>Wie breit eine Sippe ist: <c>0x39</c> = 57 (@0x4CACC0). Der
    /// Rest modulo 57 sagt, WELCHE Baumart auf der Zelle steht.</summary>
    public const int Sippe = 57;

    /// <summary>Wie viele Geländespielarten eine Sippe hat: <c>0x13</c> = 19
    /// (@0x4CACC7). 57 = 3 × 19 — drei Baumarten mal neunzehn Bodenvarianten.
    /// </summary>
    public const int Spielart = 19;

    /// <summary>
    /// <b>WELCHE KACHEL AN DIE STELLE DES BAUMS TRITT, WENN ER BRENNT.</b>
    ///
    /// <para>Wortwörtlich <c>zapal</c> @0x4CACB4..0x4CACE5 (tschechisch
    /// »zapálit« = anzünden; die Zeichenketten heissen »zapal A/B/C« und stehen
    /// bei @0x4F6D20/14/08):</para>
    /// <code>
    ///   ax = GetTile(spalte, zeile)      ; @0x4018C0 -> 0x41D090, word[zelle]
    ///   ax -= 0x288D                     ; 10381
    ///   ax %= 0x39                       ; 57   -> Platz in der Sippe
    ///   ax  = (ax / 0x13) * 0x13         ; 19   -> auf die Baumart abrunden
    ///   ax += bl                         ; die Geländeart der Zelle,
    ///                                    ;   @0x401CBC -> 0x41D110: byte[zelle+3]
    ///   ax += 0x29AA                     ; 10666
    ///   SetTile(spalte, zeile, ax)       ; @0x401285 -> 0x41D140
    /// </code>
    ///
    /// <para><b>Nachgesehen, nicht geraten:</b> auf Kachelsatz 01 sind
    /// 10666/10686/10704 verkohlte, blattlose Bäume in Baumhöhe (40×63 bis
    /// 40×67 px) — dieselbe Silhouette wie die grünen 10495/10515/10647, nur
    /// schwarz. Die Flamme selbst ist eine eigene Sache, siehe
    /// <see cref="FlammenFolge"/>.</para>
    ///
    /// <para>⚠ Die Geländeart ist das VIERTE Byte des Zellsatzes, das
    /// <c>MapBaker.Bake</c> <c>flag</c> nennt. Das ist dieselbe Zahl.</para>
    /// </summary>
    /// <param name="code">Die Kachel, die jetzt auf der Zelle steht.</param>
    /// <param name="flag">Byte +3 des Zellsatzes (die Geländeart).</param>
    public static int Verkohlt(int code, int flag)
        => KohleBasis + Sippenplatz(code) + flag;

    /// <summary>Die Kachel, die bleibt, wenn das Feuer AUS ist oder der Baum
    /// gefällt wird (<c>zrus</c> @0x4CAD8B..0x4CADBE, tschechisch »zrušit« =
    /// beseitigen; und der Brandtakt @0x4CA3F1..0x4CA424). Dieselbe Rechnung,
    /// nur mit <see cref="BodenBasis"/> statt <see cref="KohleBasis"/> — auf
    /// Kachelsatz 01 ein Stumpf bzw. flacher Boden (40×20 bis 40×27 px).
    /// </summary>
    public static int Abgebrannt(int code, int flag)
        => BodenBasis + Sippenplatz(code) + flag;

    /// <summary>Der gemeinsame Kern beider Rechnungen: welche BAUMART steht
    /// hier. Ergibt 0, 19 oder 38.</summary>
    public static int Sippenplatz(int code)
    {
        // ⚠ Der Rest muss nicht-negativ sein: das Original rechnet mit einer
        // 16-Bit-Division ohne Vorzeichen (`div cx`), und alle Waldkacheln
        // liegen ueber 10381. Eine Kachel darunter waere gar kein Wald; dann
        // faellt die Rechnung auf Sippe 0 zurueck statt ins Negative.
        int r = (code - BodenBasis) % Sippe;
        if (r < 0) r += Sippe;
        return r / Spielart * Spielart;
    }

    /// <summary>
    /// <b>Die Flamme obendrauf</b> — ANIM.CWA-Folge <b>550</b>, sieben Bilder.
    ///
    /// <para>Gelesen am Zeichner des Zeilenfachs, Art 0x0C @0x42B422:
    /// <c>edi = (index &amp; 1) * 2 + 0x226</c> → Folge 550 oder 552,
    /// <c>phase = (bildzähler/2 + index) mod 7</c>,
    /// <c>bild = [0x815580 + (word[0x7A404A + folge*4] + phase) * 4]</c>.
    /// <c>0x7A4048</c> ist die Folgentafel aus ANIM.CWA (Lader @0x435710),
    /// Feld +2 ist das erste Bild — genau das, was
    /// <c>AnimFile.Sequence(id).Start</c> liefert.</para>
    ///
    /// <para>Folge 550 ist bei uns schon als Effekt <c>blast</c> eingespielt
    /// (60×79 px, 7 Bilder, <c>InterfaceExporter.Picked</c>). ⚠ Die zweite
    /// Fassung 552 — im Original für Wälder mit UNGERADEM Index — haben wir
    /// nicht ausgespielt; wir nehmen für alle 550. <b>⚠ UNSERE SETZUNG</b>,
    /// und sie kostet nur die Abwechslung zwischen zwei fast gleichen
    /// Flammen.</para>
    /// </summary>
    public const int FlammenFolge = 550;

    /// <summary>Wie viele Bilder die Flamme hat (<c>mov edi, 7</c>
    /// @0x42B430), und wie oft sie weiterspringt: alle ZWEI Bilder des Spiels
    /// (<c>sar eax, 1</c> @0x42B438).</summary>
    public const int FlammenBilder = 7, FlammenTakt = 2;

    /// <summary>Wie lange ein Brand höchstens dauert. <c>zapal</c> setzt den
    /// Zustand auf <c>rand() % 0x96 + 2</c> = 2..151 (@0x4CAC9F/0x4CACAB), der
    /// Brandtakt zählt jeden VIERTEN Spielschritt um eins hoch (@0x4CA340
    /// <c>and eax, 3</c>, @0x4CA395 <c>inc al</c>) und hört bei 255 auf
    /// (@0x4CA39A). Also 4·(255−151) = 416 bis 4·(255−2) = 1012 Spielschritte.
    /// </summary>
    public const int BrandZustandVon = 2, BrandZustandBis = 151,
                     BrandEnde = 255, BrandTakt = 4;

    // ---- Zugriff auf die Kartendatei ---------------------------------------

    /// <summary>Die Belegung einer Zelle, oder <c>-1</c>, wenn die Karte die
    /// Sektion nicht trägt (eine vom Editor erzeugte Karte).</summary>
    public static int Imap(CwmFile m, int col, int row)
    {
        var s = m.Sec(ImapSektion);
        if (s == null) return -1;
        int i = col * 256 + row;
        return i < 0 || i * 2 + 1 >= s.Length ? -1 : BitConverter.ToUInt16(s, i * 2);
    }

    /// <summary>Die ZEICHENLAGE dieser Zelle aus Sektion 20 — 0, wenn die
    /// Sektion fehlt. Derselbe Index wie die Belegungskarte
    /// (<c>spalte·256 + zeile</c>), im Original <c>byte[0x542E18 + i]</c>.
    /// </summary>
    public static int Lage(CwmFile m, int col, int row)
    {
        var s = m.Sec(LagenSektion);
        if (s == null) return 0;
        int i = col * 256 + row;
        return i < 0 || i >= s.Length ? 0 : s[i];
    }

    /// <summary>
    /// <b>KOMMT DIESE ZELLE INS ZEILENFACH?</b> — die Frage, für die bisher
    /// eine geratene Pixelschwelle dastand.
    ///
    /// <para><b>Gelesen am Zeichner des Originals</b> (@0x4B4150). Er malt die
    /// Karte in DREI Durchgängen, und zwei davon teilen sich die Zellen nach
    /// genau dieser Grenze:</para>
    /// <list type="number">
    ///   <item>@0x4B41EB — ein flacher Durchgang über alle sichtbaren Zellen.
    ///     Er überspringt (@0x4B4262) jede Zelle mit einer Belegung ab 14000,
    ///     ausser 0xFFFC..0xFFFE. Was er zeichnet, liegt UNTER allem
    ///     Folgenden.</item>
    ///   <item>@0x4B4342 — das Zeilenfach, ein Durchgang für die Schatten.</item>
    ///   <item>@0x4B43BB — der VERZAHNTE Durchgang: je Zeile erst die Einträge
    ///     des Zeilenfachs, dann die Zellen dieser Zeile — und zwar
    ///     ausdrücklich nur die mit Belegung <b>50000..63999</b> oder 0xFFFF
    ///     (@0x4B446C). Hier verdecken Bäume Einheiten und Einheiten Bäume,
    ///     je nach Zeile.</item>
    /// </list>
    ///
    /// <para><b>Damit gibt es keine Höhenschwelle.</b> Das Original fragt nie,
    /// wie hoch ein Bild ist; es fragt, ob die Zelle in der Wald- oder
    /// Objekttafel steht. Gemessen über 23 mitgelieferte <c>.CWM</c>: von
    /// 68.391 Objektzellen tragen 37.231 einen Wald- und 1.075 einen
    /// Objekteintrag (die kommen ins Fach), 15.366 ein Gebäude (die zeichnet
    /// der Gebäudeweg) und 14.710 gar keinen (die bleiben im Boden). Von den
    /// 6.601 vorkommenden Objektkacheln stehen 2.930 nur je im Fach und 3.666
    /// nur je im Boden — <b>5</b> in beidem. Die Karte selbst trennt also
    /// sauber, und nicht die Bildhöhe.</para>
    ///
    /// <para>⚠ Gebäude (60000..60299) sind hier <b>nicht</b> dabei: die hat
    /// <c>MapBaker.BuildingCells</c> längst ausgenommen, und der lebende
    /// Zeichner setzt sie selbst ins Fach.</para>
    /// </summary>
    /// <summary>Die ZEICHENLAGE je Zelle — <c>.CWM</c>-Sektion 20, im Original
    /// nach <c>0x542E18</c> geladen (F <c>0x541E78</c>), 256×256 Byte.</summary>
    public const int LagenSektion = 20;

    /// <summary>
    /// <b>Welche Zelle in den VERZAHNTEN Durchgang gehört</b> — die Regel des
    /// Originals, jetzt vollständig.
    ///
    /// <para>⚠ 19.08.2026. Hier standen ZWEI Bereiche (50000…55999 Wald und
    /// 61000…63999 Objekt) und sonst nichts. Nachgelesen bei <c>0x4B446C</c> bis
    /// <c>0x4B4491</c> sind es aber ein Bereich und ein Sonderfall, und darüber
    /// eine zweite Tafel:</para>
    ///
    /// <code>
    ///   cmp si, 0xC350 (50000)  jb  -> weiter zum 0xFFFF-Test
    ///   cmp si, 0xFA00 (64000)  jb  -> aufnehmen, Lagenbyte pruefen
    ///   cmp si, 0xFFFF          jne -> UEBERSPRINGEN
    ///   al = byte[zelle + 0x542E18]        ; die Zeichenlage
    ///     al == 0         -> zeichnen
    ///     al &lt; 0x64 (100) -> ueberspringen
    ///     sonst           -> zeichnen
    /// </code>
    ///
    /// <para><b>Was uns das gekostet hat, gemessen:</b> über alle Karten
    /// <b>578 Zellen</b> mit <c>imap == 0xFFFF</c> und einem Lagenbyte von 0
    /// oder ≥ 100 — die nimmt das Original auf, wir liessen sie durchfallen.
    /// Auf <c>map_01</c> sind es 10 Zellen (558 statt 568). Ihre Kachelcodes
    /// (10001…10071, 40×38 bis 40×48 px) und die Bytegruppen 100…108 sprechen
    /// für <b>Brücken und Rampen</b>.</para>
    ///
    /// <para>Der Bereich 56000…60999 fällt nebenbei mit hinein; dort liegt auf
    /// keiner Karte eine Zelle, aber die FORM ist jetzt die des Originals und
    /// nicht mehr zwei Bereiche, die zufällig dasselbe leisten.</para>
    ///
    /// <para>⚠ <paramref name="lage"/> ist das Byte aus Sektion 20. Fehlt die
    /// Sektion, kommt 0 herein — dann gilt für den Bereich die alte Antwort und
    /// der 0xFFFF-Fall bleibt aus. Eine Karte ohne Sektion 20 sieht damit aus
    /// wie bisher, statt still anders zu werden.</para>
    /// </summary>
    /// <summary>
    /// <b>ZEICHNET DER FLACHE DURCHGANG DIESE ZELLE?</b> - @0x4B4262, Befehl
    /// fuer Befehl gelesen (26.08.2026):
    ///
    /// <code>
    ///   cmp ax, 0x36B0 (14000)   jb  -> zeichnen
    ///   cmp ax, 0xFFFC           jb  -> Lagenbyte pruefen
    ///   cmp ax, 0xFFFE           jbe -> zeichnen
    ///   al = byte[zelle + 0x542E18]        ; Lagenbyte
    ///     al == 0                    -> UEBERSPRINGEN   (@0x4B427C)
    ///     al >= 0x63 (99)            -> UEBERSPRINGEN   (@0x4B4280)
    ///     sonst                      -> zeichnen
    /// </code>
    ///
    /// <para>⭐⭐ <b>Zusammen mit <see cref="ImZeilenfach"/> ist das eine
    /// UEBERSCHNEIDUNGSFREIE Teilung, und zwar an der BELEGUNG:</b></para>
    /// <list type="bullet">
    ///   <item><b>Fahrbahn einer Bruecke</b> = <c>0xFFFE</c> -> hier gezeichnet
    ///     (Boden), vom verzahnten Durchgang uebersprungen.</item>
    ///   <item><b>Gelaender</b> = <c>0xFFFF</c> mit Lagenbyte >= 100 -> hier
    ///     uebersprungen, vom verzahnten Durchgang in SEINER ZEILE gezeichnet
    ///     und darf damit verdecken.</item>
    /// </list>
    ///
    /// <para>⚠⚠ <b>Warum es diese Methode gibt.</b> Am 25.08.2026 wurde
    /// gemeldet, die Bruecke ueberdecke Einheiten und habe keinen Fluss
    /// darunter. Die Behebung malte daraufhin ALLES mit Lagenbyte >= 100 flach
    /// - Fahrbahn UND Gelaender - mit der Begruendung, ein zweites Mal
    /// aufragend waere dieselbe Kachel doppelt. <b>Die Sorge war unbegruendet:
    /// das Original schliesst die Dopplung ueber die BELEGUNG aus, nicht ueber
    /// einen Verzicht.</b> Ergebnis war ein Gelaender, das nicht mehr aufragt -
    /// von ihm am 26.08. gefunden, mit dem entscheidenden Hinweis: <i>"existiert
    /// ja nur bei der waagerechten Bruecke, nicht bei den senkrechten"</i>.
    /// Genau so muss es aussehen: bei der waagerechten liegen die Gelaender
    /// eine Zeile ueber und unter der Fahrbahn, bei der senkrechten links und
    /// rechts in DENSELBEN Zeilen - dort kann eine Zeilensortierung nichts
    /// aendern.</para>
    ///
    /// <para>Belegt an map_02: Bruecke Lagenbyte 101, Fahrbahn Zeile 23
    /// (Belegung 0xFFFE), Gelaender Zeilen 22 und 24 (Belegung 0xFFFF).</para>
    /// </summary>
    public static bool ImFlachenDurchgang(int imap, int lage)
    {
        if (imap < 0) return true;              // keine Belegungskarte: wie bisher
        if (imap < 14000) return true;
        if (imap >= 0xFFFC && imap <= 0xFFFE) return true;
        if (lage == 0) return false;
        return lage < 99;                       // @0x4B4280: >= 99 uebersprungen
    }

    public static bool ImZeilenfach(int imap, int lage = 0)
    {
        // ⚠⚠ 19.08.2026, BERICHTIGUNG NOCH AM SELBEN ABEND. Der erste Anlauf
        // fasste 50000…63999 zu EINEM Bereich zusammen — richtig gelesen, aber
        // für unseren Backofen falsch: darin liegen auch die GEBÄUDE
        // (60000…60299), und die zeichnet das Original in einem eigenen Zweig
        // aus seiner Gebäudetafel, nicht als Kachel. Wir zeichnen sie LEBEND,
        // damit eine zerstörte Ruine zeigen kann.
        //
        // Aufgefallen an der Zahl: map_01 sprang von 558 auf 628 Objekte, wo
        // 568 zu erwarten waren — **70 statt 10**. Ohne diese Gegenrechnung
        // wären sechzig Gebäudekacheln fest ins Bild gebacken worden und hätten
        // sich nie mehr entfernen lassen.
        if (imap >= GebaeudeVon && imap < GebaeudeBis) return false;

        bool imBereich = imap >= WaldVon && imap < ObjektBis;
        if (!imBereich)
        {
            if (imap != 0xFFFF) return false;
            // ⚠⚠ 26.08.2026 - ZURUECKGENOMMEN. Hier stand seit dem 25.08.
            // `return false;` mit der Begruendung, die Zelle werde ja flach
            // gemalt und waere sonst doppelt. Das Original macht es anders:
            // der flache Durchgang UEBERSPRINGT 0xFFFF mit Lagenbyte >= 99
            // (@0x4B4280), der verzahnte NIMMT es (@0x4B447A). Die Teilung ist
            // ueberschneidungsfrei, und zwar an der Belegung - siehe
            // ImFlachenDurchgang. Mit `false` ragte das GELAENDER nicht mehr
            // auf; gefunden hat es der Spieler daran, dass es nur die
            // waagerechte Bruecke betrifft.
            return lage == 0 || lage >= 100;
        }
        return lage == 0 || lage >= 100;
    }

    /// <summary>Ist die Belegung ein Waldeintrag? Dann kann die Zelle
    /// BRENNEN.</summary>
    public static bool IstWald(int imap) => imap >= WaldVon && imap < WaldBis;

    /// <summary>Ein Waldeintrag, so wie er in der Kartendatei steht.</summary>
    public readonly record struct Wald(int Index, int Col, int Row, int Zustand);

    /// <summary>Alle Waldeinträge einer Karte, die etwas tragen (Zustand != 0).
    /// Die Reihenfolge ist die der Tafel — sie ist zugleich der Index, unter
    /// dem die Belegungskarte auf den Eintrag zeigt (<c>50000 + Index</c>).
    /// </summary>
    public static List<Wald> Waldliste(CwmFile m)
    {
        var raus = new List<Wald>();
        var s = m.Sec(WaldSektion);
        if (s == null) return raus;
        int n = Math.Min(s.Length / WaldStride, WaldBis - WaldVon);
        for (int i = 0; i < n; i++)
        {
            int z = s[i * WaldStride + 2];
            if (z == 0) continue;
            raus.Add(new Wald(i, s[i * WaldStride], s[i * WaldStride + 1], z));
        }
        return raus;
    }

    /// <summary>Der Waldeintrag EINER Zelle, oder <c>null</c>. Geht über die
    /// Belegungskarte, nicht über die Tafel — das ist der Weg, den auch
    /// <c>Zasah</c> nimmt.</summary>
    public static Wald? WaldAuf(CwmFile m, int col, int row)
    {
        int o = Imap(m, col, row);
        if (!IstWald(o)) return null;
        var s = m.Sec(WaldSektion);
        int i = o - WaldVon;
        if (s == null || (i + 1) * WaldStride > s.Length) return null;
        return new Wald(i, s[i * WaldStride], s[i * WaldStride + 1], s[i * WaldStride + 2]);
    }
}
