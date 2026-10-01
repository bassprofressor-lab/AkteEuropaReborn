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

    /// <summary><c>--ausdock-tempo-alt</c>: wer aus Basis oder Depot ausfaehrt, behaelt sein
    /// Tempo — eine vom Plasma gelaehmte Einheit bleibt gelaehmt (Stand vor bug-371).</summary>
    public static bool AusdockTempoAlt;

    /// <summary><c>--depotheilung-alt</c>: das Gebaeude heilt seine Insassen wieder jeden
    /// Originaltakt statt nur bei <c>Takt % 40 == 0</c> (Stand vor bug-371).</summary>
    public static bool DepotheilungAlt;

    /// <summary><c>--basis-verlegung-alt</c>: das Basisfenster verlegt eingefahrene
    /// Einheiten NICHT per Bahn, und <c>TransportierenAusBahnhof</c> nimmt nur
    /// Bahnhof/Feldbahnhof als Quelle (Stand vor bug-372).</summary>
    public static bool BasisVerlegungAlt;

    /// <summary><c>--basis-ohne-wahl-alt</c>: »Transportieren« im Basisfenster ist ohne
    /// markierte Zeile wieder ausgegraut, statt wie im Original die (falsche) Meldung
    /// »Es besteht keine Verbindung zu diesem Gebäude« zu zeigen (Stand vor 01.10.2026).</summary>
    public static bool BasisOhneWahlAlt;

    /// <summary><c>--basis-verlegung-check</c>: gelaehmte Einheit aus der Basis per
    /// Bahn verschicken, Tempo bei Abfahrt und Ankunft messen (bug-372).</summary>
    public static bool BasisVerlegungCheckAn;
}
