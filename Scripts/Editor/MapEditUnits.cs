namespace AkteEuropaReborn.Editor;

/// <summary>
/// DIE EINHEITEN, DIE DER EDITOR SETZEN KANN — und woher ihre Zahlen kommen.
///
/// <para><b>Gemessen an den gelieferten Karten</b>, und zwar an den ROHSÄTZEN,
/// nicht an den ausgeschriebenen Feldern. Für jede Bauart steht hier der Satz,
/// der auf den 29 gelieferten Karten am häufigsten vorkommt, zusammen mit dem
/// Anteil — eine Bauart, die dort in mehreren Ausführungen auftritt, sagt das
/// damit selbst. (Der Anteil ist oft unter der Hälfte, weil auf einer Karte
/// auch angeschlagene Einheiten stehen; der häufigste Satz ist der heile.)</para>
///
/// <para>⚠ <b>Und die Rohsätze, weil die Namen in die Irre führen.</b> Der erste
/// Anlauf las <c>hp_max</c> aus der <c>.entities.json</c> und setzte damit
/// »Energie 440« für einen Reifen — herausgekommen ist eine Einheit mit
/// 74 Energie. Der Grund: der Exporteur trägt dort noch die alten Namen. Es
/// gilt, was der Zeichner liest: <b>energie +0x08 / +0x29 ist ein BYTE</b> und
/// das LEBEN (13..178 über alle Bauarten), <b>sprit +0x2e / +0x30 ist ein WORT</b>
/// und der TANK (0..1000). Was hier als 440 stand, war der Tank.</para>
///
/// <para>⚠ <b>Warum NICHT aus <c>unit_catalog.json</c>.</b> Dessen
/// <c>class2_off0a</c> ist die Vorgabe der Statustafel, nicht die Gattung des
/// Kartensatzes — und die beiden gehen auseinander: der Katalog gibt
/// <c>171 Abwehrstellung</c> die Gattung 4 und <c>165 Schwere Ketten</c> die 5,
/// also ausgerechnet die zwei Nummern, auf denen <c>Can_go</c> @0x4055D0 einen
/// SCHIFFSRUMPF prüft. Nachgezählt tragen die Kartensätze das nicht: Gattung 4
/// haben nur die Rümpfe <b>150, 151, 152, 153</b> und Gattung 5 nur
/// <b>157, 158</b> — kein einziger Landtyp. Wer den Katalogwert nähme, setzte
/// Abwehrstellungen, die nur auf Wasser stehen können.</para>
///
/// <para>⚠ <b>Fußsoldaten fehlen absichtlich.</b> 148 »Leichter« und 149
/// »Schwerer« sind mit 617 und 447 Stück die zweit- und dritthäufigsten
/// Einträge, aber sie tragen <c>hp_max 0</c> und kommen aus einer eigenen
/// Bilderbank; ihr Satz hat ein Feld mehr, das hier nicht gelesen ist
/// (<c>Entity.Infantry</c>). Sie zu setzen wäre geraten, und dann stünde eine
/// Einheit ohne Bild und ohne Leben auf der Karte. Lieber eine ehrliche Lücke.</para>
/// </summary>
public static class MapEditUnits
{
    /// <summary>Eine Bauart, wie die Karten sie führen.</summary>
    /// <param name="Type">Satzbyte +0x0F.</param>
    /// <param name="Kind">+0x09.</param>
    /// <param name="Art">+0x0A — die Zeile, auf die <c>Can_go</c> verteilt.</param>
    /// <param name="Attack">+0x26.</param>
    /// <param name="Energy">+0x08 / +0x29 — das Leben.</param>
    /// <param name="Fuel">+0x2e / +0x30 — der Tank.</param>
    /// <param name="Share">Wie oft dieser Satz vorkommt, von wie vielen dieser
    /// Bauart. Steht dabei, damit sichtbar bleibt, wie einheitlich die Karten
    /// selbst sind.</param>
    public readonly record struct Kind(string Name, int Type, int Kind_, int Art,
                                       int Attack, int Energy, int Fuel, string Share);

    /// <summary>
    /// Die Auswahl, nach Häufigkeit auf den gelieferten Karten. ⚠ ALLE Zahlen
    /// kommen aus den Rohsätzen der Karten — auch die Tankgröße; die frühere
    /// Fassung nahm sie aus <c>unit_catalog.json</c> und lag damit daneben.
    /// Nur die NAMEN stammen aus dem Katalog.
    /// </summary>
    public static readonly Kind[] All =
    {
        //   Name              Typ kind gatt angr energie sprit   Anteil
        new("Reifen",          161, 0, 0,  6,  50,  440, "193/402"),
        new("6x6 Reifen",      163, 0, 0,  7,  63,  400, "104/453"),
        new("Ketten",          164, 0, 0,  7,  83,  300, "151/480"),
        new("Schwere Ketten",  165, 0, 0, 11, 106,  250,  "70/220"),
        new("Panzerreifen",    162, 0, 0,  9, 114,  250,  "78/145"),
        new("Spinne",          160, 0, 0,  4,  55,  500,  "51/100"),
        new("Luftkissen",      166, 0, 0, 10,  47,  600,   "50/74"),
        new("Schneegleiter",   167, 0, 0,  9,  46,  800,   "11/33"),
        new("Kugelroller",     168, 0, 0, 23,  59,  300,    "5/43"),
        new("Stahlsucher",     173, 0, 0, 20,  13, 1000,   "22/32"),
        new("Laeufer",         174, 0, 0, 20,  80,  300,  "29/221"),
        new("Abwehrstellung",  171, 1, 0, 13, 178,   40, "390/982"),
        // ⚠ Die Schiffsruempfe. Ihre Gattung 4/5 ist das, worauf Can_go
        // @0x4055D0 den 2x2- bzw. 4x4-Rumpf prueft — wer eines davon auf Land
        // setzt, bekommt eine Einheit, die nirgends stehen darf. Der Editor
        // prueft das beim Setzen und weist es ab.
        new("Schiff klein",    151, 0, 4, 18, 125,  500,  "96/117"),
        new("Schiff mittel",   150, 0, 4,  6,  90,  500,   "25/29"),
        new("Schiff ohne Waffe",153,0, 4,  0, 120,  500,   "14/17"),
        new("Schiff gross",    157, 0, 5, 30, 175,  500,    "6/26"),
    };
}
