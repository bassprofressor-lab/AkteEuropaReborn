namespace AkteEuropaReborn.Simulation;

/// <summary>
/// <b>Die Schrägenrechnung des Originals</b> — <c>@0x4B5CE0</c>, die stufenlose
/// Anhebung einer Einheit auf einer Schräge.
///
/// <para>Der Kachelsatz in sec1 ist vier Byte breit: <c>word[+0]</c> die
/// Kachelnummer, <c>byte[+2]</c> die HÖHE, <c>byte[+3]</c> die
/// <b>SCHRÄGENART</b>. Feld +3 haben wir seit jeher als »flag« mitgeführt
/// (<see cref="NavGrid.FlagAt"/>) und nie gedeutet; der Kommentar dort sagte
/// »slope 0..4«. Es sind <b>19 Arten</b>, 0…18.</para>
///
/// <para>Gelesen am 27.08.2026, die ganze Kette:</para>
/// <code>
/// 0x41D110  al = byte[Kachelkarte[0x677E20] + (Zeile*Breite[0x542DC4] + Spalte)*4 + 3]
///           -&gt; die Schrägenart dieser Zelle
/// 0x430E60  der FEINVERSATZ einer Einheit in ihrer Zelle, aus Richtung
///           (Satz +4) und Fahrzähler (Satz +6). Steht sie (Richtung 0xFF,
///           @0x430FE1), ist er GESETZT auf (20, 10) — die Zellmitte.
///           Sonst je Richtung ±Zähler/4 bzw. /6 in x, ±/8 bzw. /12 in y,
///           danach +20 / +10.
/// 0x4B5CE0  bl = Versatz(Art, FeinX, FeinY)          &lt;- diese Datei
/// 0x4B611F  al = Höhe(Zelle) * 15 + bl               &lt;- Hub
/// 0x4301FF  and eax, 0xFFFF00FF                      ; NUR das niedrige Byte
/// 0x430204  sub si, ax                               ; das HEBT die Einheit an
/// </code>
///
/// <para><b>Warum das zählt:</b> 40 und 20 sind Zellbreite und -höhe, 15 ist
/// <c>MapBaker.ElevStep</c>. Das Original hebt eine Einheit auf einer Schräge
/// also <b>stufenlos</b> an, anteilig danach, wo in der Zelle sie steht. Wir
/// nahmen bis zum 27.08.2026 allein <c>Höhe*15</c> — auf der Auffahrt einer
/// Brücke sass das Fahrzeug darum zu tief und verschwand hinter dem Geländer.
/// Gemeldet mit »das muss ja was mit der hoehe zu tun haben, denn die einheiten
/// fahren ja ueber eine schraege auf die bruecke oder eben runter«.</para>
///
/// <para>⚠ Für eine STEHENDE Einheit ist der Versatz kein Nullwert: bei
/// Feinversatz (20,10) liefern die 19 Arten der Reihe nach
/// <c>0 7 8 8 7 15 15 15 15 0 0 0 0 0 1 15 14 15 16</c> — bis zu 16 Bildpunkte,
/// mehr als eine ganze Höhenstufe. Genau das ist der gemeldete Fehler.</para>
///
/// <para><b>Gegenprobe zur Lesung:</b> eine Volkszählung über Feld +3 aller
/// 33 Originalkarten liefert <b>keinen Wert über 18</b> — genau die 19 Arme der
/// Sprungtafel <c>0x4B6134</c>. Ein beliebiges Flaggenbyte hätte Werte bis 255
/// gezeigt. ⚠ Unsere EIGENEN Karten (Editor) benutzen nur 0…8; die neun
/// übrigen Arten legt der Editor nicht an.</para>
///
/// <para>Rückfallschalter: <c>--kein-hang</c> (<see cref="Aus"/>) rechnet wie
/// vor dem 27.08.2026, also allein mit <c>Höhe*15</c>. Prüfstand:
/// <c>--hang-check</c>.</para>
/// </summary>
public static class Hang
{
    /// <summary>Die Zahl der Schrägenarten — <c>cmp ecx, 0x12</c> @0x4B5CFE,
    /// dann 19 Arme über die Sprungtafel <c>0x4B6134</c>.</summary>
    public const int Arten = 19;

    /// <summary>Zellbreite und -höhe in Bildpunkten, die beiden Teiler der
    /// Rechnung. Gleich <c>MapBaker.TileW</c> / <c>TileH</c>; hier noch einmal,
    /// damit die Simulation nicht auf den Einleser zeigen muss.</summary>
    public const int Breite = 40, Hoehe = 20;

    /// <summary>Eine Höhenstufe in Bildpunkten — <c>mov cl, 0xf</c> @0x4B6129,
    /// gleich <c>MapBaker.ElevStep</c>.</summary>
    public const int Stufe = 15;

    /// <summary>Der Feinversatz einer STEHENDEN Einheit: @0x430FE1 setzt ihn
    /// auf (20, 10), die Zellmitte.</summary>
    public const int MitteX = Breite / 2, MitteY = Hoehe / 2;

    /// <summary><c>--kein-hang</c> — der Stand von vor dem 27.08.2026: der
    /// Schrägenanteil bleibt weg, die Anhebung ist allein
    /// <c>Höhe * <see cref="Stufe"/></c>. Ohne diesen Schalter liesse sich die
    /// Änderung nicht mehr gegen ihren Vorgänger halten.</summary>
    public static bool Aus;

    /// <summary>
    /// <c>--hang-mal=&lt;n&gt;</c> — <b>ein Diagnoseschalter, kein Spielwert.</b>
    /// Er vervielfacht den Schrägenanteil.
    ///
    /// <para>⚠ 27.08.2026 gebaut, nachdem der Spieler den neuen und den alten
    /// Stand nebeneinander laufen liess und meldete: »beide identisch«. Meine
    /// eigene Messung sagte, auf derselben Zelle bewegt sich die Einheit um
    /// 7 Bildpunkte. Eine der beiden Aussagen ist falsch, und 7 Bildpunkte sind
    /// zu wenig, um das mit blossem Auge zu entscheiden.</para>
    ///
    /// <para><b>Wozu:</b> mit <c>--hang-mal=6</c> wären es 42 statt 7. Bleibt das
    /// Bild dann immer noch stehen, ist die Rechnung nicht das Problem, sondern
    /// der Weg von der Rechnung zum Bildschirm — und das ist eine ganz andere
    /// Suche. Ein Unterschied, den man nicht sehen kann, taugt nicht als
    /// Gegenprobe; er muss laut genug sein, dass sein AUSBLEIBEN etwas
    /// beweist.</para>
    /// </summary>
    public static int Faktor = 1;

    /// <summary>
    /// <b>Die Anhebung einer Einheit in Bildpunkten</b> — <c>@0x4B611F…0x4B6133</c>
    /// samt der Maske des Rufers.
    ///
    /// <para>⚠ Das <c>and eax, 0xFFFF00FF</c> @0x4301FF ist KEIN Beiwerk: das
    /// Original rechnet <c>ax = Höhe*15</c> (16 Bit), addiert <c>bl</c> nur auf
    /// <c>al</c> (der Übertrag geht NICHT nach <c>ah</c>) und der Rufer wirft
    /// <c>ah</c> danach ohnehin weg. Die Anhebung ist also
    /// <c>(Höhe*15 + Versatz) &amp; 0xFF</c> und läuft ab Höhe 17 über. Keine
    /// gelieferte Karte kommt dort hin — nachgebaut gehört es trotzdem, sonst
    /// steht hier eine stillschweigend andere Rechnung.</para>
    /// </summary>
    public static int Hub(int hoehe, int art, int feinX, int feinY)
        => Aus ? hoehe * Stufe & 0xFF
               : hoehe * Stufe + Versatz(art, feinX, feinY) * Faktor & 0xFF;

    /// <summary>Die Anhebung einer STEHENDEN Einheit — Feinversatz (20, 10).</summary>
    public static int HubStehend(int hoehe, int art) => Hub(hoehe, art, MitteX, MitteY);

    /// <summary>
    /// <b>Der Schrägenanteil <c>bl</c></b> — die 19 Arme von <c>@0x4B5CE0</c>,
    /// Arm für Arm, mit der Adresse jedes Arms daneben.
    ///
    /// <para>⚠ <paramref name="feinX"/> und <paramref name="feinY"/> liest das
    /// Original als <b>vorzeichenlose Bytes</b> (<c>xor eax,eax; mov al,…</c>) —
    /// darum die Maske. Das ist kein Schönheitsfehler: fährt eine Einheit nach
    /// links oder oben aus ihrer Zelle, wird der Feinversatz negativ und das
    /// Original rechnet mit 236…255 weiter.</para>
    ///
    /// <para>⚠ Die Teilungen müssen ZUR NULL HIN abschneiden — <c>idiv</c> tut
    /// das, C# tut es auch. Ein Abrunden (Bitverschiebung) wäre bei den
    /// negativen Teilern −20 und −40 etwas anderes.</para>
    /// </summary>
    public static int Versatz(int art, int feinX, int feinY)
    {
        if (art < 0 || art >= Arten) return 0;      // ja @0x4B5D01 -> bl bleibt 0
        int x = feinX & 0xFF, y = feinY & 0xFF;
        int ax = x * Stufe, ay = y * Stufe;         // lea eax,[e+e*2]; lea eax,[e+e*4]
        return art switch
        {
            0 => 0,                                                     // 0x4B5D0E eben
            1 => ax / 40,                                               // 0x4B5D15
            2 => ay / -20 + 15,                                         // 0x4B5D30
            3 => ax / -40 + 15,                                         // 0x4B5D4C
            4 => ay / 20,                                               // 0x4B5D68
            5 => 2 * y <= x ? 15 : ay / -20 + ax / 40 + 15,             // 0x4B5D83
            6 => x + 2 * y <= 40 ? 15 : ay / -20 - ax / 40 + 30,        // 0x4B5DC8
            7 => 2 * y >= x ? 15 : ay / 20 - ax / 40 + 15,              // 0x4B5E06
            8 => x + 2 * y >= 40 ? 15 : ay / 20 + ax / 40,              // 0x4B5E4C
            9 => 2 * y >= x ? 0 : ay / -20 + ax / 40,                   // 0x4B5E8C
            10 => x + 2 * y >= 40 ? 0 : ay / -20 - ax / 40 + 15,        // 0x4B5ECF
            11 => 2 * y <= x ? 0 : ay / 20 - ax / 40,                   // 0x4B5F12
            12 => x + 2 * y <= 41 ? 0 : ay / 20 + ax / 40 - 15,         // 0x4B5F55
            13 => 2 * y >= x ? ay / 20 - ax / 40                        // 0x4B5F97 Grat
                             : ay / -20 + ax / 40,
            14 => x + 2 * y == 41 ? 0                                   // 0x4B5FF3 Kehle
                : x + 2 * y > 40 ? ay / 20 + ax / 40 - 15
                                 : ay / -20 - ax / 40 + 15,
            15 => ax / -40 + ay / 20 + 15,                              // 0x4B605C
            16 => ay / 20 + ax / 40,                                    // 0x4B608F
            17 => ax / 40 - ay / 20 + 15,                               // 0x4B60BF
            18 => ax / -40 - ay / 20 + 30,                              // 0x4B60F0
            _ => 0,
        };
    }
}
