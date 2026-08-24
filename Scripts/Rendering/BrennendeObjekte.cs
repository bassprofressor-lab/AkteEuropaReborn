namespace AkteEuropaReborn.Rendering;

/// <summary>
/// ⭐⭐⭐ <b>DAS ZWEITE BRANDWESEN — brennende KARTENOBJEKTE.</b>
/// Gelesen am 24.08.2026, <b>noch nicht gebaut</b>, und warum nicht steht unten.
///
/// <para>Diese Datei enthaelt absichtlich keinen Code. Sie ist die vollstaendige
/// Aufnahme eines Teilsystems, das uns fehlt — damit die Lesung nicht in einem
/// Sitzungsprotokoll verschwindet und der naechste Anlauf nicht wieder bei null
/// beginnt.</para>
///
/// <para><b>Wie es gefunden wurde.</b> Nicht durch Suchen, sondern durch die
/// Aufruferliste von <c>zapal_forestA</c> @0x4CA7E0: sie hat <b>drei</b>
/// Aufrufer. Zwei gehoeren zum Waldbrand (und der zweite war der Grund, warum
/// unser Feuer sich nicht ausbreitete — siehe
/// <see cref="MapEntityLayer.BrandSchrittSekunden"/>). Der dritte, @0x4CA043,
/// laeuft ueber eine ganz andere Liste.</para>
///
/// <para><b>Die Liste</b> — 6 Byte je Eintrag ab <c>0xC03A30</c>, Anzahl in
/// <c>word[0x539D88]</c>, unmittelbar hinter der Waldbrandliste
/// (<c>0xBFF3E1</c>…<c>0xC03A31</c>, 3 Byte je Zelle, 6000 Plaetze):</para>
/// <code>
///   +0x00  u8   Spalte
///   +0x01  u8   Zeile
///   +0x02  u8   ART        -> Zeile in der Arttafel
///   +0x03  u8   Zustand    (0 = steht, sonst brennt/zerstoert)
///   +0x04  u16  ungelesen
/// </code>
///
/// <para><b>Die Arttafel</b> <c>0xBB3B60</c>, 8 Byte je Zeile:</para>
/// <code>
///   +0x00  u8   VERHALTENSKLASSE 0/1/2   (@0x4C9FEC, Sprungziele
///                0x4CA015 / 0x4CA0FC / 0x4CA17A)
///   +0x02  u16  Grundkachel              (@0x4CA593 / @0x4CA76A)
/// </code>
/// <para>Auf die Grundkachel wird gerechnet: <b>+10001 (0x2711) = brennt</b>
/// (@0x4CA59B), <b>+10002 (0x2712) = zerstoert</b> (@0x4CA772).</para>
///
/// <para><b>Die zwei Handlungen</b>, beide aus <c>Zasah</c> gerufen:</para>
/// <code>
///   0x4CA570  ANZUENDEN   (Thunk 0x401294)  — Protokollname »hori« (tsch. »brennt«)
///   0x4CA750  ZERSTOEREN  (Thunk 0x4015AA)
/// </code>
///
/// <para><b>Die Schadensbaender</b> @0x40D442…0x40D4C9 — dieselbe Bauart wie die
/// fuenf Waldbaender, die wir am 21.08. gebaut haben:</para>
/// <code>
///   wert = ((Schaden + 128) · Faktor) &gt;&gt; 7,  dann − rand()%5 + rand()%5
///   wert &gt; 80        ZERSTOEREN
///   wert 41 … 80      ANZUENDEN
///   wert 21 … 40      ANZUENDEN mit 1/3        (@0x40D4AB, rand()%3 == 0)
///   wert &lt;= 20        weiter bei 0x40D4CE      (ungelesen)
/// </code>
///
/// <para><b>Der Takt</b> @0x4C9FC0: laeuft die <c>word[0x539D88]</c> Eintraege ab,
/// ueberspringt Art 0xFF (leer), verzweigt nach der Verhaltensklasse — und
/// <b>Klasse 0 ruft @0x4CA043 den Waldbrand-Uebergriff</b>. Ein brennendes
/// Objekt steckt also den Wald an. Gezeichnet wird ab 0x42E4DC.</para>
///
/// <para><b>Was wir davon haben:</b> nur die Geschossschwelle. <c>_objSchwelle</c>
/// gibt einem Nicht-Wald-Objekt die 30 (»zerstoerbares Objekt«), damit ein
/// Geschoss daran haengenbleibt. Auf einen TREFFER reagiert es bei uns
/// ueberhaupt nicht — <see cref="MapEntityLayer.WaldTreffer"/> behandelt nur
/// Wald.</para>
///
/// <para>⚠⚠ <b>WARUM ES HEUTE NICHT GEBAUT WURDE — das Hindernis ist die
/// AUSGABE, nicht die Lesung.</b> Unser ausgegebener Objektsatz
/// (<c>map_NN.json</c>, Feld <c>objects</c>) hat diese Felder:</para>
/// <code>
///   col, row, x, y, w, h, burnt, bx, by, ash, ax, ay
/// </code>
/// <para>Lage und die drei Bildvarianten — <b>aber keine ART</b>. Ohne sie gibt
/// es weder die Verhaltensklasse noch die Grundkachel, auf die +10001/+10002
/// gerechnet wird. Der Weg ist damit vorgezeichnet und in dieser Reihenfolge
/// zu gehen:</para>
/// <list type="number">
/// <item><b>Ausgeber erweitern</b>: die Objektart je Zelle mitschreiben (und,
/// wenn sie dasteht, gleich die Grundkachel aus der Belegungskarte).</item>
/// <item><b>Karten neu backen</b> — 30 Karten, also der teure Schritt.</item>
/// <item><b>Arttafel 0xBB3B60 einlesen</b> (Klasse + Grundkachel je Art).</item>
/// <item><b>Schadensbaender</b> in <c>WaldTreffer</c>s Nachbarschaft bauen.</item>
/// <item><b>Takt und Zeichnen</b> der drei Klassen.</item>
/// </list>
///
/// <para>⚠ Punkt 1 und 2 sind der Aufwand; 3 bis 5 sind gelesen und
/// geradeaus. Nichts davon ist geraten — jede Zahl oben hat eine Adresse.</para>
/// </summary>
internal static class BrennendeObjekteOffen
{
    /// <summary>Die Liste der brennbaren Kartenobjekte im Original.</summary>
    public const uint Liste = 0xC03A30, Anzahl = 0x539D88, Arttafel = 0xBB3B60;

    /// <summary>Was auf die Grundkachel gerechnet wird.</summary>
    public const int Brennt = 10001, Zerstoert = 10002;

    /// <summary>Die Schadensschwellen aus @0x40D483…0x40D4A9.</summary>
    public const int SchwelleZerstoeren = 80, SchwelleAnzuenden = 40, SchwelleWuerfeln = 20;
}
