namespace AkteEuropaReborn.Rendering;

/// <summary>
/// ⭐ 01.10.2026 — die Gegenschalter zu bug-377 (»Züge halten vor Gebäuden,
/// falsche Farbe, Abschnitte überlappen, Zug neben den Schienen«, Hauptmenü-
/// Demos), Bauvorlage berichte/zuege-demo-fable.md §5 V1–V6. Jeder Schalter
/// stellt den Stand VOR dem Umbau wieder her. Ausgewertet in MapViewer.cs,
/// gebaut in Simulation/ZugDemo.cs, geprüft mit <c>--zug-demo-check</c>.
/// </summary>
public partial class MapEntityLayer
{
    /// <summary>V1 <c>--geisterwaggons</c>: Waggonsätze mit Lebensbyte +0x00 == 0
    /// werden wieder geladen und gezeichnet. Das Original prüft das Byte im Takt
    /// (@0x4C768F) und im Einreiher (@0x42E113) und löscht beim Streckenende NUR
    /// dieses Byte (@0x4C6C47) — die Koordinaten bleiben als Leiche liegen.</summary>
    public static bool Geisterwaggons;

    /// <summary>V2 <c>--zugfach-alt</c>: die Waggons wieder nach ALLEM anderen in
    /// Listenreihenfolge zeichnen statt im Zeilenfach (Einreiher @0x42E100,
    /// Fach = Zeile + yoff + 2) mit Bild-Y-Sortierung (@0x430C50).</summary>
    public static bool ZugfachAlt;

    /// <summary>V3 <c>--zug-steht-am-bahnsteig</c>: von der Karte übernommene
    /// Waggons bleiben in der Standzeit wieder sichtbar am Kettenende stehen, und
    /// cursor 0 / Bandende zählen wieder als sichtbar.</summary>
    public static bool ZugStehtAmBahnsteig;

    /// <summary>V4 <c>--zug-pendelt</c>: Waggons von Linien, die nie abfahren
    /// (faze 3/4), pendeln wieder im Zellensprung durch <c>UpdateTrains</c>,
    /// ohne Ausmittelung und ohne <c>Lift</c>.</summary>
    public static bool ZugPendelt;

    /// <summary>V5 <c>--zugfarbe-roh</c>: ein Waggon, dessen Linie keinen
    /// Besitzer liefert (<c>ZugBesitzerFarbe == −1</c>), wird wieder ROH
    /// gezeichnet (blau+grün gemischt) statt in der neutralen Gruppe 10.</summary>
    public static bool ZugfarbeRoh;

    /// <summary>V6 <c>--zug-gekuppelt</c>: die Kupplung <c>RailCouple</c> wieder an
    /// (der Stand bis 01.10.2026, eine bewusste Abweichung). ⭐ ENTSCHEIDUNG DES
    /// SPIELERS 01.10.2026: »Wie im Original« — standardmäßig AUS, die Wagen fahren
    /// mit den Rückständen 0/4/7/11 Takte einzeln (Startzähler 20/40/25/40,
    /// @0x4C687C/@0x4C688C), ohne Abstandsregel.</summary>
    public static bool ZugGekuppelt;

    /// <summary><c>--zug-demo-check</c>: der Prüfstand zu bug-377,
    /// Simulation/ZugDemo.cs.</summary>
    public static bool ZugDemoCheckAn;

    // ---- bug-382, die Zug-Einfahrt (01.10.2026, berichte/zug-einfahrt-fable.md §7) ----

    /// <summary>⭐ <c>--zug-einfahrt-alt</c> (bug-382): der Stand bug-377 —
    /// E1 Waggonfach gegen die Kachelzeile <c>r</c> (zwei Zeilen zu spät), E2 Fahrweg
    /// endet in der Mitte der letzten Gleiszelle, E3 alle vier Waggons verschwinden
    /// im Ankunftstakt (<c>Hidden = !Rollt ‖ …</c>). Gebaut in Simulation/ZugEinfahrt.cs
    /// und RailFreight.cs <c>RailPlaceWagons</c>.</summary>
    public static bool ZugEinfahrtAlt;

    /// <summary>⭐ <c>--gleisfach-alt</c> (bug-382, E4): das Gleis wieder zwei Zeilen
    /// SPÄTER als seine Zelle (<c>RailDrawRowBias = 2</c>, Stand 13.08.2026). Der
    /// Kartenzeichner zeichnet Fach <c>i+2</c> VOR den Kacheln der Zeile <c>i</c>
    /// (@0x4B43F3); die 2 von @0x42DFE9 ist der Feldversatz der zwei Vorzeilen,
    /// kein Tiefenversatz. ⚠ Zusammen mit E1 liegt das Gleis dann ÜBER dem
    /// Waggon.</summary>
    public static bool GleisfachAlt;

    /// <summary><c>--zug-einfahrt-check[=N]</c>: der Prüfstand zu bug-382.</summary>
    public static bool ZugEinfahrtCheckAn;

    // ---- bug-395, die Feinlage des Waggons (02.10.2026, berichte/zug-feinlage-fable.md §5) ----

    /// <summary>⭐ F0 <c>--zug-bogen-schirm</c> (bug-395): die Bogenlänge des Waggonwegs
    /// wieder in SCHIRMPIXELN MIT HÖHE (<c>RailLifted(…).DistanceTo(…)</c>, Stand
    /// 15.08.–02.10.2026). Dann zählt ein Rampenglied 35 px bergauf bzw. 5 px bergab
    /// statt 20, der Waggon kriecht dort auf 57 % bzw. springt (Bericht §4.2,
    /// <c>--zug-demo-check</c> 2,0 px kleinster Abstand). Das Original nimmt Δ aus der
    /// Tafel 0x539400 in der EBENE und legt die Rampe als getrennte ±15-Korrektur in
    /// denselben Takt (Tafel 0x4C73C8 @0x4C6AE6…0x4C6B13, @0x4C6ED9).</summary>
    public static bool ZugBogenSchirm;

    /// <summary>⭐ F1 <c>--zug-feinlage-alt</c> (bug-395): <c>WagonOverRail</c> wieder
    /// −23 (am 15.08.2026 nach Augenmaß gewählt) statt der gelesenen −18 — der Waggon
    /// liegt dann 5 px zu hoch (Einreiher @0x42E214…0x42E24A: Blitziel
    /// <c>y = Zeile·20 − Höhe·15 − 90 + feinY</c>, Bericht §1.4).</summary>
    public static bool ZugFeinlageAlt;

    /// <summary>⭐ F2 <c>--zug-ohne-sichtprobe</c> (bug-395): Waggons wieder auch in
    /// gerade nicht beobachteten Zellen zeichnen. Das Original prüft im Einreiher
    /// @0x42E197 die sec50-Tafel JEDES Waggons, ohne Besitzer-Ausnahme.</summary>
    public static bool ZugOhneSichtprobe;

    // ---- bug-397, F7: das Fahrmodell je Gleisschritt (02.10.2026, Simulation/ZugFahrmodell.cs) ----

    /// <summary>⭐ <c>--zug-fahrmodell-alt</c> (bug-397): der Stand bug-395 — der Fortschritt
    /// läuft gleichmäßig über die BOGENLÄNGE der Gleiskette (sec22), die Fahrzeit ist die
    /// Summe der Schrittpreise 5/4 über alle delka Schritte. Dann fährt der Zug auf
    /// senkrechtem Gleis langsamer als die Route (DM_4 Linie 6: 2,71 statt 4 px je Takt),
    /// kleinster Waggonabstand 9,5 px statt 12. Das Original rechnet je Routenschritt mit
    /// Zähler/Preis (0x4C69C0, Bericht zug-feinlage-fable.md §2).</summary>
    public static bool ZugFahrmodellAlt;
}
