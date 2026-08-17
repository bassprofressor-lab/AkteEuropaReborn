namespace AkteEuropaReborn.Simulation;

/// <summary>
/// <b>DER WERT EINER EINHEIT</b> — <c>@0x450F30</c>, Befehl für Befehl gelesen und
/// ohne die EXE nachrechenbar. Er ist die Grundlage von <b>beiden</b> Preisen am
/// Geschäftszentrum: was der Laden verlangt und was ein Verkäufer bekommt.
///
/// <para><b>Die Rechnung, so wie sie in der EXE steht</b> (18.08.2026 an der
/// Disassemblierung nachgezählt, nicht aus dem Handoff übernommen):</para>
/// <code>
///   0x450F54  div 1000        -> ax = Einheitsnummer / 1000 = der SPIELER
///   0x450F5D  al  = ent[+0x43]                 (die Entwurfsnummer der Einheit)
///   0x450F69  ecx = al + 200*Spieler           (der PLATZ in sec47)
///   0x450F70..76  ecx *= 46                    (sec47, Schrittweite 46)
///   0x450F7A  edx = d[+0x1b] + d[+0x1a] + d[+0x1c]      die drei Kosten
///   0x450F94  al  = ent[+0x08]                 die HUELLE JETZT
///   0x450F9A  edx = edx * al
///   0x450F9D..A2  eax = 30 * edx               (edx+edx, dann *3, dann *5)
///   0x450FA8  bl  = d[+0x1e]                   die HUELLE VOLL
///   0x450FAE  idiv ebx                         -> edi
///   0x450FB2  al  = ent[+0x28]                 die ERFAHRUNG
///   0x450FB9  call 0x43AAC0                    -> die Stufe 0..7
///   0x450FC5  eax = Faktor[Stufe] * edi
///   0x450FD7  idiv 100                         -> der Wert
/// </code>
///
/// <para><b>⚠ Damit ist <c>Entity.Field28</c> benannt.</b> Es hiess bisher
/// »Satz +0x28, als Laufzeitfeld getragen, unbenannt«. Es ist die
/// <b>ERFAHRUNG</b>, und drei unabhängige Lesungen sagen dasselbe: die
/// Wertfunktion schickt es hier durch eine Stufentafel mit den Schwellen
/// 5/20/40/75/110/170/254/255; die Schadensrechnung @0x40cd90 liest es neben
/// +0x27; und die Sprachausgabe vergleicht es mit 50 und nimmt darüber einen
/// anderen Satz. Dazu die Zahl, die schon im alten Kommentar stand:
/// <b>0 auf 2847 von 2863</b> Einheiten der Karten — genau das, was man von
/// frischen Einheiten erwartet.</para>
///
/// <para><b>⚠ Zwei Feldbreiten, und sie sind Teil der Lesung</b> (Regel 18):
/// Hülle und Erfahrung werden als <c>byte</c> geholt (<c>mov al, byte ptr …</c>),
/// die drei Kosten ebenfalls. Ein Wert über 255 kann in dieser Rechnung also gar
/// nicht auftreten; wer hier mit grösseren Zahlen rechnet, rechnet etwas
/// anderes als das Original.</para>
///
/// <para><b>⚠ Das Original teilt ohne Netz</b>: <c>idiv ebx</c> @0x450FAE
/// prüft den Teiler nicht. Ein Entwurf mit <c>+0x1e == 0</c> stürzt das
/// Original ab. Wir geben stattdessen 0 zurück — <b>unsere Abweichung</b>, und
/// zwar eine, die keine Spielregel berührt: sie ersetzt einen Absturz, kein
/// Verhalten.</para>
/// </summary>
public static class UnitValue
{
    /// <summary>
    /// Die Erfahrungsstufen, <c>0x4FA0E0</c>. In der EXE stehen dort
    /// <b>Paare</b> (Untergrenze, Obergrenze); die Stufenfunktion @0x43AAC0
    /// liest nur die Obergrenze — <c>byte[0x4FA0E1 + 2·Stufe] &gt;= Erfahrung</c>,
    /// erste Übereinstimmung gewinnt.
    ///
    /// <para>Die Rohbytes, damit die Herkunft nachprüfbar bleibt:
    /// <c>00 05 · 06 14 · 15 28 · 29 4b · 4c 6e · 6e aa · ab fe · ff ff</c>.
    /// ⚠ Paar 4 endet auf 110 und Paar 5 fängt auf 110 an — die 110 gehört zu
    /// Stufe 4, weil die erste Übereinstimmung gewinnt. Das ist eine
    /// Ungereimtheit DES ORIGINALS, hier stehengelassen statt geglättet.</para>
    /// </summary>
    public static readonly int[] LevelMax = { 5, 20, 40, 75, 110, 170, 254, 255 };

    /// <summary>Die Faktoren, <c>word[0x4FA0F0 + 2·Stufe]</c>, in Hundertsteln:
    /// 0,10 · 0,20 · 0,50 · 1,00 · 2,00 · 4,00 · 7,00 · 10,00.
    /// <b>Eine Einheit der höchsten Stufe ist das Hundertfache einer frischen
    /// wert.</b></summary>
    public static readonly int[] LevelFactor = { 10, 20, 50, 100, 200, 400, 700, 1000 };

    /// <summary>Die Stufe zu einer Erfahrung — @0x43AAC0. Findet die Schleife
    /// nichts, gibt das Original nach einer Fehlerzeile <b>0</b> zurück; das ist
    /// hier nachgebaut, kann aber nicht eintreten, weil die letzte Schwelle 255
    /// ist und die Erfahrung ein Byte.</summary>
    public static int LevelOf(int experience)
    {
        int x = experience & 0xFF;
        for (int i = 0; i < LevelMax.Length; i++)
            if (LevelMax[i] >= x) return i;
        return 0;
    }

    /// <summary>
    /// Der Wert einer Einheit. Alle Eingaben sind die BYTES, die das Original
    /// liest — siehe Klassenkopf.
    /// </summary>
    /// <param name="costW">Entwurf +0x1a, Waffenteile.</param>
    /// <param name="costF">Entwurf +0x1b, Fahrwerksteile.</param>
    /// <param name="costS">Entwurf +0x1c, Spezialteile.</param>
    /// <param name="hull">Einheit +0x08, die Hülle JETZT.</param>
    /// <param name="hullMax">Entwurf +0x1e, die Hülle voll.</param>
    /// <param name="experience">Einheit +0x28.</param>
    public static int Of(int costW, int costF, int costS, int hull, int hullMax, int experience)
    {
        // ⚠ Der Teiler-0-Fall des Originals ist ein Absturz, keine Regel.
        if ((hullMax & 0xFF) == 0) return 0;
        int sum = (costW & 0xFF) + (costF & 0xFF) + (costS & 0xFF);
        // Die Reihenfolge ist die der EXE: erst mal Hülle, dann mal 30, DANN
        // teilen. Wer früher teilt, rundet an einer anderen Stelle ab und
        // bekommt für kleine Einheiten andere Preise.
        int v = sum * (hull & 0xFF) * 30 / (hullMax & 0xFF);
        return LevelFactor[LevelOf(experience)] * v / 100;
    }

    /// <summary>
    /// <b>Was ein Mensch beim Verkauf bekommt: 30 % des Werts.</b> Der
    /// Verkaufsdialog @0x446470 rechnet ihn selbst — <c>lea eax,[eax+eax*2]</c>,
    /// <c>idiv 10</c> @0x4464D7..E5 — und zeigt ihn nur an. <b>Es gibt kein
    /// Eingabefeld und keine Plus/Minus-Tasten:</b> der Preis ist keine
    /// Verhandlung, sondern eine Ansage des Spiels.
    /// </summary>
    public static int SellPrice(int value) => 3 * value / 10;

    /// <summary>
    /// <b>Was der Laden verlangt: 250 % des Werts.</b> Damit steht die Spanne des
    /// Geschäftszentrums fest — <b>8 : 1</b> zwischen Ankauf und Verkauf. Diese
    /// Zahl ist doppelt belegt: sie steht so in der Nachschubrechnung, und sie
    /// erzwingt Ladenpreise <c>≡ 0 oder 2 (mod 5)</c>, was alle 21 belegten
    /// Preise der echten <c>4.DM</c> erfüllen (zufällig 2·10⁻⁹).
    /// </summary>
    public static int ShopPrice(int value) => 25 * value / 10;
}
