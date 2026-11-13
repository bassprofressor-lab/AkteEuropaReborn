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
/// <para><s>Fußsoldaten fehlen absichtlich</s> — ⭐ 13.11.2026 (D9): sie sind DA.
/// Das Feld, das hier als »nicht gelesen« stand, ist seit langem gelesen:
/// <c>+0x0B</c> ist der Fußvolksatz (gerade, 0..22 → ROBO.CWR, Bilder
/// <c>Units/infantry/&lt;satz&gt;/</c>). Geschrieben wird der GANZE häufigste
/// Rohsatz der Originalkarten (<see cref="Vorlage"/>), nicht ein Feld nach dem
/// anderen.</para>
///
/// <para>⚠ <b>Flugzeuge gibt es in den Kartensätzen nicht.</b> Gezählt über alle
/// 55 gelieferten Karten (13.11.2026): die Gattungen sind 0 (Land), 1 (Fußvolk),
/// 3 (nur Typ 138 »Dicke Bertha«, 10 Stück), 4 und 5 (Schiffe) — kein einziger
/// Flieger. Flugzeuge liegen im Original in einer eigenen Flugzeugtafel
/// (20000+i), nicht in sec5. Ein Editorflieger hiesse ein Format erfinden;
/// darum keiner.</para>
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
        new("Läufer",         174, 0, 0, 20,  80,  300,  "29/221"),
        new("Abwehrstellung",  171, 1, 0, 13, 178,   40, "390/982"),
        // ⭐ 13.11.2026 D9: das Fußvolk — Zahlen aus der Vorlage unten (+0x26, +0x29)
        new("Leichter Soldat", 148, 0, 1,  2,  20,    0, "266/617"),
        new("Schwerer Soldat", 149, 0, 1,  5,  35,    0, "156/447"),
        // ⚠ Die Schiffsruempfe. Ihre Gattung 4/5 ist das, worauf Can_go
        // @0x4055D0 den 2x2- bzw. 4x4-Rumpf prueft — wer eines davon auf Land
        // setzt, bekommt eine Einheit, die nirgends stehen darf. Der Editor
        // prueft das beim Setzen und weist es ab.
        new("Schiff klein",    151, 0, 4, 18, 125,  500,  "96/117"),
        new("Schiff mittel",   150, 0, 4,  6,  90,  500,   "25/29"),
        new("Schiff ohne Waffe",153,0, 4,  0, 120,  500,   "14/17"),
        new("Schiff groß",    157, 0, 5, 30, 175,  500,    "6/26"),
    };

    // ========================================================================
    // ⭐ 11.11.2026 — bug-740 (Paket E2, M2): DER KAMPFSATZ.
    // ========================================================================
    //
    // Bis heute schrieb der Editor (und der Generator) Waffe +0x0C = 0,
    // Waffenfahne +0x0D = 0, Munition +0x39/+0x3A = 0 — und dazu Tempo +0x20,
    // Reichweite +0x2A/+0x2B, Sicht +0x2C, Verteidigung +0x27, Rumpfbyte +0x0B
    // und Ausruestung +0x10 auf 0. Der Lader (MapEntityLayer, Rohsatz) liest
    // genau diese Bytes; CanFight verlangt Weapon != 0 → jede Editoreinheit war
    // »UNBEWAFFNET«, und mit Tempo 0 / Reichweite 0 / Sicht 0 dazu.
    //
    // GEMESSEN am 11.11.2026 an den Rohsaetzen aller 54 gelieferten Karten
    // (user://data/Maps: map_01..33, NET01..08, DM_1..13): je Bauart der
    // HAEUFIGSTE Satz ueber das Tupel (+0x09 +0x0A +0x0B +0x0C +0x0D +0x0E
    // +0x10 +0x20 +0x26 +0x27 +0x28 +0x29 +0x2A +0x2B +0x2C +0x2E +0x3A +0x3D).
    // Angriff, Leben und Tank dieses Satzes stimmen bei 15 von 16 Bauarten mit
    // der Tafel oben ueberein (Kugelroller: Tank 288 statt 300, Satz nur 3/43 —
    // schwach belegt). Nebenbefund: +0x0D ist auf allen Saetzen = Waffe − 20
    // (24→4, 21→1, 39→19), bei Schiffen zugleich die Bildvariante.
    // Die Munition +0x39 wird auf den Hoechstwert +0x3A gesetzt (frische
    // Einheit) — UNSERE SETZUNG; auf den Karten ist +0x39 == +0x3A der
    // haeufigste Fall (Reifen 253 von 262 bewaffneten).
    // Gegenschalter --editor-einheit-alt (alles wie bis 0.6.13: Waffe 0).

    /// <summary>bug-740: der gemessene Kampfsatz einer Bauart.</summary>
    /// <param name="Chassis">+0x0B (Rumpfbyte; Stimme/Bild).</param>
    /// <param name="Weapon">+0x0C, der AUFSATZ.</param>
    /// <param name="Flag0D">+0x0D, die Waffenfahne (bei Schiffen die Bildvariante).</param>
    /// <param name="Equip">+0x10, die dritte Komponente.</param>
    /// <param name="Speed">+0x20 (Wort), das Tempo.</param>
    /// <param name="Defense">+0x27.</param>
    /// <param name="RangeMin">+0x2A.</param>
    /// <param name="Range">+0x2B.</param>
    /// <param name="Sight">+0x2C.</param>
    /// <param name="Ammo">+0x3A (Hoechstwert; +0x39 bekommt denselben).</param>
    /// <param name="Reload">+0x3D.</param>
    /// <param name="Share">wie oft das ganze Tupel vorkommt, von wie vielen dieser Bauart.</param>
    public readonly record struct Satz(int Chassis, int Weapon, int Flag0D, int Equip,
                                       int Speed, int Defense, int RangeMin, int Range,
                                       int Sight, int Ammo, int Reload, string Share);

    private static readonly System.Collections.Generic.Dictionary<int, Satz> Saetze = new()
    {
        //          rumpf waffe 0D ausr tempo vert rmin rw sicht mun lade  Anteil
        [161] = new(  2,  24,  4,  0,  10,  4, 0,  4,  4, 100,   0, "130/402"),
        [163] = new(  4,  25,  5,  0,   8,  5, 0,  4,  5,  90,   0,  "77/453"),
        [164] = new(  5,  21,  1,  0,  10,  6, 0,  4,  4,  80,   0, "117/480"),
        [165] = new(  6,  22,  2,  0,  11,  9, 0,  4,  5,  70,   0,  "50/220"),
        [162] = new(  3,  22,  2, 81,   5, 12, 0,  4,  4,  70,   0,  "63/145"),
        [160] = new(  1,  32, 12,  0,  12,  7, 0,  4,  6,  80,   0,  "39/100"),
        [166] = new(  7,  30, 10, 82,  17,  3, 0,  5,  8, 100,   0,   "39/74"),
        [167] = new(  8,  25,  5, 82,  12,  2, 0,  4,  5,  90,  25,    "9/33"),
        [168] = new(  9,  27,  7, 81,   9, 11, 5, 10, 11,  25, 120,    "3/43"),
        [173] = new( 16,  39, 19,  0,  16,  5, 0,  0,  4,   1,   0,   "22/32"),
        [174] = new( 17,  27,  7, 81,  13,  7, 5, 10, 14,  25,   0,  "14/221"),
        [171] = new( 14,  31, 11,  0,   4, 12, 0, 10, 13, 170,   0, "365/982"),
        [151] = new( 71,  26,  6,  0,   7,  6, 3,  8, 17,  60,   0,  "50/117"),
        [150] = new( 70,  24,  4,  0,  10,  6, 0,  4, 10, 200,   0,   "18/29"),
        [153] = new( 73,   0,  0,  0,   6,  6, 0,  0,  6,   0,  20,    "9/17"),
        [157] = new(100,  27,  7,  0,   8, 11, 4, 10, 18,  50,   0,    "6/26"),
    };

    /// <summary>bug-740: <c>--editor-einheit-alt</c> — Editor- und
    /// Generatoreinheiten wieder ohne Kampfsatz (Waffe 0, Tempo 0, …).</summary>
    public static bool EinheitAlt
        => System.Array.IndexOf(Core.CommandLine.Args, "--editor-einheit-alt") >= 0;

    /// <summary>bug-740: der Kampfsatz einer Bauart, oder null (unbekannte
    /// Bauart oder Gegenschalter).</summary>
    public static Satz? SatzOf(int type)
        => !EinheitAlt && Saetze.TryGetValue(type, out var s) ? s : null;

    // ========================================================================
    // ⭐ 13.11.2026 — D9: DIE VORLAGE (der ganze Rohsatz)
    // ========================================================================
    //
    // GEMESSEN an allen 55 gelieferten Karten (scratchpad inf.py): je Bauart der
    // haeufigste GANZE Satz (78 Byte), ohne die Felder, die je Einheit schwanken —
    // Lage +0x00/+0x01, Richtung +0x02/+0x03 und der Zellversatz +0x1D/+0x1E (der
    // streut bei JEDEM Soldaten: −18..18 / −7..7, nie 0/0). 148: 266 von 617
    // gleich, 149: 156 von 447. Der Rest sind angeschlagene oder besondere Saetze
    // (+0x0B = 16/20/22 bzw. 10: andere Fussvolksaetze, Missionsfiguren).
    // Gegenschalter --editor-fussvolk-aus (Fussvolk nicht in der Liste).

    private static readonly System.Collections.Generic.Dictionary<int, string> Vorlagen = new()
    {
        [148] = "00000000ff0000001400010000be009400000000000000ff0000ff00000000000400000010270201001400020300000000000000ffff000000000000001432000000003200ff0000000000000000",
        [149] = "00000000ff0000002300010200bf009500000000000000ff0000ff00000000000600000010270502002300040600000000000000ffff000000000000001433000000003300ff0000000000000000",
    };

    public static bool FussvolkAus
        => System.Array.IndexOf(Core.CommandLine.Args, "--editor-fussvolk-aus") >= 0;

    /// <summary>D9: der gemessene ganze Rohsatz einer Bauart, oder null.</summary>
    public static byte[]? Vorlage(int type)
        => !FussvolkAus && Vorlagen.TryGetValue(type, out var h) ? System.Convert.FromHexString(h) : null;

    /// <summary>D9: der Fussvolksatz (+0x0B) einer Bauart, −1 wenn keine Fussvolkbauart.</summary>
    public static int Fussvolksatz(int type)
        => Vorlage(type) is { } v && v[0x0A] == 1 ? v[0x0B] : -1;

    /// <summary>D9: die Auswahl, wie das Bedienfeld sie zeigt (mit Gegenschalter ohne Fussvolk).</summary>
    public static Kind[] Liste
        => FussvolkAus ? System.Array.FindAll(All, k => k.Art != 1) : All;

    // ========================================================================
    // ⭐ 13.11.2026 — B3 (Fable-Pruefung): DIE UEBRIGEN SATZFELDER DER FAHRZEUGE
    // ========================================================================
    //
    // Bis heute schrieb der Editor ausser Kampfsatz, Leben, Tank und Gattung NUR
    // Nullen. Die Originalsaetze tragen dort je Bauart einheitlich FF an +0x04,
    // +0x11, +0x17, +0x1A, +0x34/35, +0x45 (Leerwerte, »kein …«), Schiffe +0x13 = 2,
    // Luftkissen/Kugelroller +0x22 = 1 — und an +0x3E/+0x43 den ENTWURF, nach dem das
    // Infofenster die Einheit benennt (0x4701CF) und den Verkaufswert rechnet; mit 0
    // hiess jede Editoreinheit wie Entwurf 0 des Spielers.
    //
    // GEMESSEN an allen Rohsaetzen der 55 gelieferten Karten (scratchpad b3.py): je
    // Bauart und Byte der Mehrheitswert, nur wenn er bei >= 60 % der Saetze steht
    // (Anteile hinter jeder Zeile). Der ENTWURF dagegen nicht nach Mehrheit, sondern
    // aus der globalen Entwurfstafel (unit_designs.json, GAME.EXE): der eine Entwurf
    // mit Fahrwerk = Bauart, Waffe = Kampfsatz-Waffe − 20 und Ausruestung = Kampfsatz —
    // 11 von 12 Landbauarten haben genau einen, und er ist auch der haeufigste auf den
    // Karten bei dieser Waffe (z. B. 161 → 84 »Maschinengewehr« 262/262). Kugelroller
    // 168: kein passender Eintrag in der Tafel, gemessen 49 (35/43 bei Waffe 27).
    // Schiffe: Schiffstafel, gemessener Wert. Gegenschalter --editor-satzfelder-alt.

    private static readonly System.Collections.Generic.Dictionary<int, (int Off, byte Wert)[]> SatzFelder = new()
    {
        [161] = new (int, byte)[] { (0x04, 255), (0x11, 255), (0x17, 255), (0x34, 255), (0x35, 255), (0x3E, 84), (0x43, 84), (0x45, 255) },   // Mehrheit je Byte    286/426, 402/426, 402/426, 398/426, 398/426, 262/426, 262/426, 402/426; Entwurf 84
        [163] = new (int, byte)[] { (0x04, 255), (0x11, 255), (0x17, 255), (0x1A, 255), (0x34, 255), (0x35, 255), (0x3E, 85), (0x43, 85), (0x45, 255) },   // Mehrheit je Byte    416/453, 453/453, 452/453, 389/453, 438/453, 438/453, 453/453; Entwurf 85
        [164] = new (int, byte)[] { (0x04, 255), (0x11, 255), (0x17, 255), (0x1A, 255), (0x34, 255), (0x35, 255), (0x3E, 86), (0x43, 86), (0x45, 255) },   // Mehrheit je Byte    436/480, 480/480, 479/480, 373/480, 441/480, 440/480, 480/480; Entwurf 86
        [165] = new (int, byte)[] { (0x04, 255), (0x17, 255), (0x1A, 255), (0x34, 255), (0x35, 255), (0x3E, 92), (0x43, 92), (0x45, 255) },   // Mehrheit je Byte    176/220, 218/220, 137/220, 184/220, 184/220, 220/220; Entwurf 92
        [162] = new (int, byte)[] { (0x04, 255), (0x11, 255), (0x17, 255), (0x1A, 255), (0x34, 255), (0x35, 255), (0x3E, 93), (0x43, 93), (0x45, 255) },   // Mehrheit je Byte    122/145, 145/145, 143/145, 103/145, 142/145, 142/145, 97/145, 97/145, 145/145; Entwurf 93
        [160] = new (int, byte)[] { (0x04, 255), (0x17, 255), (0x1A, 255), (0x34, 255), (0x35, 255), (0x3E, 97), (0x43, 97), (0x45, 255) },   // Mehrheit je Byte    90/100, 100/100, 75/100, 99/100, 99/100, 83/100, 83/100, 100/100; Entwurf 97
        [166] = new (int, byte)[] { (0x04, 255), (0x11, 255), (0x17, 255), (0x1A, 255), (0x22, 1), (0x34, 255), (0x35, 255), (0x3E, 89), (0x43, 89), (0x45, 255) },   // Mehrheit je Byte    71/74, 74/74, 74/74, 55/74, 74/74, 68/74, 68/74, 73/74, 73/74, 74/74; Entwurf 89
        [167] = new (int, byte)[] { (0x04, 255), (0x11, 255), (0x15, 1), (0x17, 255), (0x34, 255), (0x35, 255), (0x3E, 88), (0x43, 88), (0x45, 255) },   // Mehrheit je Byte    21/33, 33/33, 26/33, 32/33, 32/33, 32/33, 27/33, 27/33, 33/33; Entwurf 88
        [168] = new (int, byte)[] { (0x04, 255), (0x17, 255), (0x22, 1), (0x34, 255), (0x35, 255), (0x36, 255), (0x37, 255), (0x3E, 49), (0x43, 49), (0x45, 255) },   // Mehrheit je Byte    34/43, 43/43, 43/43, 31/43, 31/43, 26/43, 26/43, 35/43, 35/43, 43/43; Entwurf 49
        [173] = new (int, byte)[] { (0x04, 255), (0x11, 255), (0x17, 255), (0x1A, 255), (0x34, 255), (0x35, 255), (0x3E, 81), (0x43, 81), (0x45, 255) },   // Mehrheit je Byte    29/32, 32/32, 32/32, 20/32, 32/32, 32/32, 32/32, 32/32, 32/32; Entwurf 81
        [174] = new (int, byte)[] { (0x04, 255), (0x11, 1), (0x17, 255), (0x34, 255), (0x35, 255), (0x3E, 112), (0x43, 112), (0x45, 255) },   // Mehrheit je Byte    170/221, 144/221, 220/221, 188/221, 188/221, 221/221; Entwurf 112
        [171] = new (int, byte)[] { (0x04, 255), (0x11, 7), (0x17, 255), (0x1A, 255), (0x34, 255), (0x35, 255), (0x3E, 104), (0x43, 104), (0x45, 255) },   // Mehrheit je Byte    981/982, 980/982, 982/982, 975/982, 980/982, 980/982, 982/982; Entwurf 104
        [151] = new (int, byte)[] { (0x04, 255), (0x11, 255), (0x13, 2), (0x17, 255), (0x1A, 255), (0x34, 255), (0x35, 255), (0x3E, 1), (0x43, 1) },   // Mehrheit je Byte    101/117, 117/117, 117/117, 117/117, 100/117, 115/117, 115/117, 116/117, 91/117; Entwurf 1
        [150] = new (int, byte)[] { (0x04, 255), (0x11, 255), (0x13, 2), (0x17, 255), (0x18, 20), (0x19, 20), (0x1A, 255), (0x34, 255), (0x35, 255), (0x3E, 0), (0x43, 0), (0x45, 255) },   // Mehrheit je Byte    25/29, 29/29, 29/29, 29/29, 25/29, 25/29, 25/29, 29/29, 29/29, 20/29; Entwurf 0
        [153] = new (int, byte)[] { (0x04, 255), (0x11, 255), (0x13, 2), (0x17, 255), (0x18, 20), (0x19, 20), (0x1A, 255), (0x34, 255), (0x35, 255), (0x3E, 3), (0x43, 3), (0x45, 255) },   // Mehrheit je Byte    17/17, 17/17, 17/17, 17/17, 17/17, 17/17, 17/17, 17/17, 17/17, 17/17, 16/17, 12/17; Entwurf 3
        [157] = new (int, byte)[] { (0x04, 255), (0x11, 255), (0x13, 2), (0x15, 1), (0x17, 255), (0x1A, 255), (0x34, 255), (0x35, 255), (0x3E, 7), (0x43, 7) },   // Mehrheit je Byte    24/26, 26/26, 26/26, 16/26, 26/26, 17/26, 26/26, 26/26, 26/26, 26/26; Entwurf 7
    };

    public static bool SatzfelderAlt
        => System.Array.IndexOf(Core.CommandLine.Args, "--editor-satzfelder-alt") >= 0;

    /// <summary>B3: die gemessenen uebrigen Satzfelder einer Bauart (leer mit Gegenschalter).</summary>
    public static (int Off, byte Wert)[] SatzFelderOf(int type)
        => !SatzfelderAlt && !EinheitAlt && SatzFelder.TryGetValue(type, out var f) ? f : System.Array.Empty<(int, byte)>();

    /// <summary>D9: der Zellversatz nach der Formel des Originals (create_unit
    /// 0x4B38BA/0x4B38D1: dx = 18 − rand%37, dy = 7 − rand%15). UNSERE SETZUNG: der
    /// Wurf kommt aus einem festen Hash (Karte ist eine Datei, kein Spielverlauf).</summary>
    public static (sbyte dx, sbyte dy) Streuung(uint wurf)
        => ((sbyte)(18 - (int)(wurf % 37)), (sbyte)(7 - (int)((wurf / 37) % 15)));

    /// <summary>bug-740: die Bauart zur Typnummer — der Generator nimmt seinen
    /// Reifen 161 von hier (Leben 50 gemessen, bis heute 100 als Vorgabe).</summary>
    public static Kind? ByType(int type)
    {
        foreach (var k in All) if (k.Type == type) return k;
        return null;
    }
}
