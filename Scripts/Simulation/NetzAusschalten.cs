using Godot;

namespace AkteEuropaReborn.Rendering;

/// <summary>
/// ⭐ 11.11.2026 (Online 2b) — <b>EINEN SPIELER AUS DER NETZPARTIE NEHMEN</b>:
/// die Wirkung von <c>0x41B0D0(p)</c> des Originals, gerufen von Befehl 973 (Ctrl-K),
/// 977 und 995 (Gast verlässt Spiel) — <c>berichte/mehrspieler-original-fable.md</c> §3.
///
/// <para><b>Gelesen:</b> alle Einheiten des Platzes gelöscht (<c>0x402130</c>), seine
/// Gebäude bekommen Besitzer <b>11</b> (<c>0x41B194</c>, +0x05 und +0x3D).
/// <b>UNSERE SETZUNG:</b> die Einheiten gehen über den Weg von <c>remove_unit</c>
/// (<see cref="MissionRemove"/>, also mit Ausdocken aus Garagen), die Gebäude über
/// <see cref="Hand"/> wie bei einer Einnahme, und ihre Routen werden gestrichen.
/// Nicht nachgebaut: Markt bereinigen (<c>0x6DDF78</c>), Flughäfen zurücksetzen.</para>
///
/// <para>⚠ Läuft im TAKT, auf allen Maschinen im selben (<c>NetLink.Cut</c>), und
/// würfelt nicht. Gegenschalter <c>--netz-ausschalten-aus</c>: Einheiten und Gebäude
/// bleiben stehen (der Platz wird nur nicht mehr abgewartet).</para>
/// </summary>
public partial class MapEntityLayer
{
    public static bool NetzAusschaltenAus;

    /// <summary>Gibt zurück, wie viele Einheiten/Gebäude betroffen waren.</summary>
    public int NetzSpielerAusschalten(int owner)
    {
        if (NetzAusschaltenAus || owner is < 0 or > 7) return 0;
        int einheiten = 0, gebaeude = 0;
        // Einheiten: rückwärts über eine Schnappliste der Plätze — Kill verändert die Liste.
        var plaetze = new System.Collections.Generic.List<int>();
        foreach (var e in _entities)
            if (!e.Dead && !e.IsBuilding && !e.IsProp && e.Owner == owner) plaetze.Add(e.Slot);
        foreach (int slot in plaetze) { MissionRemove(slot, false); einheiten++; }

        foreach (var b in _entities)
        {
            if (b.Dead || !b.IsBuilding || b.IsProp || b.Owner != owner) continue;
            RouteGebaeudeStreichen(b.Slot);
            Hand(b, NeutralOwner);
            gebaeude++;
        }
        GD.Print($"netz: Platz {owner} aus der Partie genommen — {einheiten} Einheiten entfernt, " +
                 $"{gebaeude} Gebäude an Besitzer {NeutralOwner}");
        QueueRedraw();
        return einheiten + gebaeude;
    }
}
