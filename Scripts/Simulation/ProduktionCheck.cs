namespace AkteEuropaReborn.Rendering;

using System.Text;

/// <summary>
/// <b><c>--produktion-check</c> — bug-462 (KayelGee R4, Meldung 23).</b> Eine
/// eigene Basis (K21) bekommt genug Teile und eine Bestellung ueber denselben Weg
/// wie der Knopf »Produzieren« (<see cref="BuildPanelPick"/>). Gezaehlt werden
/// die Takte bis die neue Einheit SICHTBAR auf der Karte steht.
///
/// <para>Soll (Original 0x4B1840 → 0x43BFC0 → 0x410420, dann der Auslass): ein
/// Satz liegt sofort in der Liste, kein Eintrag in <c>e.Depot</c>, und binnen
/// weniger Takte steht er auf der Tuer — ohne zweiten Knopf. Nullmodell
/// <c>--bauzeit-alt</c>: 6 s Bauzeit, danach eine Entwurfsnummer im Depot, die
/// auf »Aussenden« wartet; in 400 Takten keine Einheit auf der Karte.</para>
/// </summary>
public partial class MapEntityLayer
{
    public string ProduktionCheckLine()
    {
        var sb = new StringBuilder("produktion-check\n");
        int bi = ProbeBasisfensterWaehlen();
        if (bi < 0) return sb.Append("  kein eigenes Basisfenster — NICHT GEMESSEN").ToString();
        var b = _entities[bi];
        if (!IsUnitPlant(b)) return sb.Append($"  Gebaeude Art {b.BType} baut keine Einheiten — NICHT GEMESSEN").ToString();
        var menu = BuildableFor(b.BType, b.Owner);
        if (menu.Count == 0) return sb.Append("  nichts baubar — NICHT GEMESSEN").ToString();
        b.StockW += 2000; b.StockF += 2000; b.StockS += 2000;
        // Platz in der Liste schaffen
        while (b.Garage.Count > 0 && GarageBelegt(b) >= DepotSlots) b.Garage.RemoveAt(b.Garage.Count - 1);
        int vorEnt = _entities.Count, vorDepot = b.Depot.Count, vorListe = b.Garage.Count;
        // ein FAHRZEUG bestellen (Fussvolk kommt ohnehin immer heraus, 0x433A50)
        int zeile = 0;
        for (int k = 0; k < menu.Count; k++)
            if (TypeOfChassis(_designs![menu[k]].Propulsion) == 0) { zeile = k; break; }
        BuildPanelPick(zeile);
        bool sofortInListe = _entities.Count > vorEnt && b.Garage.Count > vorListe;
        var neu = _entities.Count > vorEnt ? _entities[^1] : null;
        int sichtbarNach = -1;
        for (int t = 1; t <= 400 && sichtbarNach < 0; t++)
        {
            SimTickFuerProbe();
            if (neu == null && _entities.Count > vorEnt) neu = _entities[^1];
            if (neu != null && !neu.Dead && !Untergestellt(neu) && !b.Garage.Contains(neu)) sichtbarNach = t;
        }
        bool ok = sofortInListe && b.Depot.Count == vorDepot && sichtbarNach is > 0 and <= 40;
        sb.Append($"  Basis Platz {b.Slot}: Bestellung »{_designs![menu[zeile]].Name}«, sofort in der Liste {sofortInListe}, "
                + $"Entwurfsnummern im Depot {vorDepot} -> {b.Depot.Count}, sichtbar auf der Karte nach "
                + $"{(sichtbarNach < 0 ? "— (nicht in 400 Takten)" : sichtbarNach + " Takten")}"
                + (neu != null ? $" bei ({neu.Col},{neu.Row})" : "")
                + (BauzeitAlt ? " (Nullmodell --bauzeit-alt)" : "") + "\n");
        sb.Append(ok ? "produktion-check: BESTANDEN" : "produktion-check: DURCHGEFALLEN");
        return sb.ToString();
    }
}
