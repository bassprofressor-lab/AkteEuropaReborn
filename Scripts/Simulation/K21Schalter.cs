namespace AkteEuropaReborn.Rendering;

/// <summary>
/// ⭐ 23.09.2026 — die Gegenschalter zu den K21-Meldungen an einer Stelle,
/// damit die drei Bauten (Gleisbruch, Heli-Tempo, Plasmawerfer) nicht
/// gegenseitig auf fehlende Felder bauen. Ausgewertet in MapViewer.cs.
/// </summary>
public partial class MapEntityLayer
{
    /// <summary><c>--zug-faehrt-durch</c>: ein Zug faehrt wieder ueber zerstoertes Gleis.</summary>
    public static bool ZugFaehrtDurch;

    /// <summary><c>--gleisbruch-zug-check</c>: der Pruefstand zum Gleisbruch unter fahrendem Zug.</summary>
    public static bool GleisbruchZugCheckAn;

    /// <summary><c>--heli-stufe-alt</c>: Helis wieder im Flugzeug-Zweig (Stand 20.09., bug-348).</summary>
    public static bool HeliStufeAlt;

    /// <summary><c>--plasma-drehen-aus</c>: die Plasmakugel bleibt auf Bild 0.</summary>
    public static bool PlasmaDrehenAus;

    /// <summary><c>--plasma-zielwahl-alt</c>: der Plasmawerfer waehlt wieder wie jede Waffe.</summary>
    public static bool PlasmaZielwahlAlt;

    /// <summary><c>--stumpf-haelt-geschoss</c>: ein abgebrannter Baum behaelt seine
    /// Geschossschwelle 40 (Stand vor bug-367).</summary>
    public static bool StumpfHaeltGeschoss;

    /// <summary><c>--stumpf-geschoss-probe</c>: der Pruefstand zu bug-367,
    /// Simulation/StumpfGeschossProbe.cs.</summary>
    public static bool StumpfGeschossProbeAn;

    /// <summary><c>--reparaturzeiger-aus</c>: kein Schraubenschluessel (Zeigerart 22,
    /// Bild 17) und kein Befehl 29 — der Boden-Techniker repariert wieder nur,
    /// wenn er zufaellig auf dem Bruch stehen bleibt (Stand vor bug-368).</summary>
    public static bool ReparaturzeigerAus;

    /// <summary><c>--gleisreparatur-check</c>: der Pruefstand zu bug-368,
    /// Simulation/Gleisreparatur.cs.</summary>
    public static bool GleisreparaturCheckAn;

    /// <summary><c>--gleistechniker-feld-alt</c>: der Boden-Techniker wird wieder an
    /// <c>Equipment</c> (+0x10) erkannt statt an <c>Part</c> (+0x0E) — Stand vor
    /// bug-370. Dort traegt ihn keine Einheit, also repariert niemand.</summary>
    public static bool GleistechnikerFeldAlt;
}
