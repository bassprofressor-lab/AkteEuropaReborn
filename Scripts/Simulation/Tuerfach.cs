namespace AkteEuropaReborn.Rendering;

using System.Collections.Generic;
using Godot;

/// <summary>
/// ⭐⭐ <b>DAS FACH DER TORE</b> (15.09.2026, seine Meldung aus K16: »von den Jungle-Basen
/// liegt die Tür über den Einheiten, das Tor verdeckt die davorstehende Einheit«).
/// Lesung berichte/tueren-zeichnen-einfahrt-opus.md §A.
///
/// <para><b>Original:</b> das Gebäude (Energiebalken und Tore) kommt in den Korb
/// <c>Zeile + Tür0.Zeile + 2</c> (C 0x42FD47..0x42FDB8 / F 0x42EECD), ein Fahrzeug in
/// <c>Satzzeile + 2</c> (C 0x4301B3); im Korb stehen die Einheiten vor den Kacheln.
/// Eine Einheit auf der Zelle VOR der Tür liegt damit einen Korb später — über dem
/// Tor. Eingereiht wird nach Platznummer (0xC06910 aufwärts).</para>
///
/// <para><b>Bei uns</b> stimmte die Rechnung (<see cref="BuildingDrawRowFor"/>), aber
/// die Zeichenschleife lief als <c>while</c> über <c>BuildingsBackToFront()</c>, das
/// nach ZEILE sortiert ist, nicht nach FACH: ein früheres Gebäude mit größerem Fach
/// (Fabrik/Mine/türlos +3, Flughafen +4) hielt alle folgenden auf. Nachgerechnet:
/// 27 von 1024 Gebäuden mit Tür zeichneten ihr Tor zu spät, 18 davon Basen; auf K16
/// Basis Platz 7. Kein Dschungel-Sonderfall — es trifft, wo eine Fabrik weiter oben
/// steht. Gegenschalter <c>--tuerfach-alt</c>.</para>
/// </summary>
public partial class MapEntityLayer
{
    /// <summary><c>--tuerfach-alt</c> — der Stand bis 15.09.2026: die <c>while</c>-Schleife
    /// über die nach Zeile sortierten Gebäude.</summary>
    public static bool TuerfachAlt;

    /// <summary>Je Zeile die Gebäude, deren Fach (Körper im Blockmodus, Tore) in dieser
    /// Zeile gezeichnet wird. Dieselbe Rechnung für Zeichner und Prüfstand.</summary>
    private List<Entity>[] FachKoerbe(List<Entity> gebaeude, int letzteZeile)
    {
        var korb = new List<Entity>[letzteZeile + 2];
        if (TuerfachAlt)
        {
            int gi = 0;
            for (int r = 0; r <= letzteZeile; r++)
                while (gi < gebaeude.Count && gebaeude[gi].Row + BuildingDrawRowFor(gebaeude[gi]) <= r)
                    (korb[r] ??= new List<Entity>()).Add(gebaeude[gi++]);
            return korb;
        }
        var nachPlatz = new List<Entity>(gebaeude);
        nachPlatz.Sort((a, b) => a.Slot.CompareTo(b.Slot));        // Einreiher 0xC06910 aufwärts
        foreach (var b in nachPlatz)
        {
            int r = Mathf.Clamp(b.Row + BuildingDrawRowFor(b), 0, letzteZeile);
            (korb[r] ??= new List<Entity>()).Add(b);
        }
        return korb;
    }

    /// <summary>
    /// <c>--tuerfach-check</c>: für jedes Gebäude mit Tür die Zeile, in der die Zeichenschleife
    /// sein Fach wirklich zeichnet, gegen <c>Row + BuildingDrawRowFor</c>.
    /// Soll K16: 0 Abweichungen. Nullmodell <c>--tuerfach-alt</c>: 1 (Platz 7, +1); K17: 1.
    /// </summary>
    public string TuerfachCheck()
    {
        var sb = new System.Text.StringBuilder("tuerfach-check\n");
        if (TuerfachAlt) sb.AppendLine("  ⚠ NULLMODELL --tuerfach-alt: hier MUSS es Abweichungen geben");
        int letzteZeile = _nav != null ? _nav.Height : 0;
        foreach (var b in BuildingsBackToFront())
            letzteZeile = Mathf.Max(letzteZeile, b.Row + BuildingDrawRowFor(b));
        var gebaeude = BuildingsBackToFront();
        var korb = FachKoerbe(gebaeude, letzteZeile);
        int mitTuer = 0, falsch = 0;
        var wo = new Dictionary<Entity, int>();
        for (int r = 0; r < korb.Length; r++)
            if (korb[r] != null) foreach (var b in korb[r]) wo[b] = r;
        foreach (var b in gebaeude)
        {
            if (b.Dead || b.IsProp || b.Doors <= 0) continue;
            mitTuer++;
            int soll = b.Row + BuildingDrawRowFor(b);
            int ist = wo.TryGetValue(b, out int z) ? z : -1;
            if (ist != soll)
            {
                falsch++;
                sb.AppendLine($"    {BuildingName(b)} Platz {b.Slot} Spieler {b.Owner} auf ({b.Col},{b.Row}): Fach {soll}, gezeichnet in Zeile {ist} ({ist - soll:+#;-#;0})");
            }
        }
        sb.AppendLine($"  {mitTuer} Gebaeude mit Tuer, {falsch} im falschen Fach (Soll 0); --tuerfach-alt {TuerfachAlt}");
        return sb.Append(falsch == 0 && mitTuer > 0 ? "  BESTANDEN" : "  DURCHGEFALLEN").ToString();
    }
}
