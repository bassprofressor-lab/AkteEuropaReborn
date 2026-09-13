using System.Collections.Generic;
using Godot;

namespace AkteEuropaReborn.Rendering;

/// <summary>
/// <b>DIE MITNAHME ZUR NÄCHSTEN MISSION — die Spielseite der Fensterart 38</b>
/// (13.09.2026). Zeichner und Klicks stehen in <see cref="UI.MitnahmeView"/>.
/// Lesung: <c>berichte/mitnahmefenster-fable.md</c>.
///
/// <list type="bullet">
/// <item><b>Linke Liste</b> (Filter <c>0x482436…0x4824CD</c>): je Satz des
/// 1000er-Blocks, Platz aufsteigend — lebt, Klassenbyte +0x0A == 0 (Fahrzeug),
/// +0x0D ≠ 0 (bewaffnet), +0x0D ≠ 8 (»MS-Rakete«), +0x0F ≠ 0xAB (»A-W-Stell.«),
/// nicht schon in der Mitnahmetafel.</item>
/// <item><b>Auszahlung</b> <c>0x4C1720</c>: Σ ⌊3·Wert/10⌋ über ALLE lebenden
/// Klasse-0-Einheiten, die nicht in der Tafel stehen — ohne Waffen-/Antriebsfilter.
/// Gebucht erst beim Start (Zustand 200, <c>0x416B05</c>).</item>
/// <item><b>Aufstellen</b> <c>place_carry</c> <c>0x43B190</c>: der ganze Satz kommt
/// mit — Rang/Erfahrung bleiben —, aber <b>Trefferpunkte, Munition und Sprit
/// werden auf das Maximum gesetzt</b> (<c>0x43B275…0x43B298</c>).</item>
/// </list>
///
/// <para>⚠ UNSERE Setzung bleibt der Weg: das Original kopiert 78 Byte in den
/// Stapel <c>0x81A410</c>; wir merken uns Entwurf, Name und Rang
/// (<see cref="Campaign.CampaignManager.CarriedUnit"/>) und bauen den Satz aus
/// dem Entwurf neu. Ein selbst gezeichneter Entwurf ohne sec47-Platz wird über
/// seinen Namen gefunden.</para>
///
/// <para>Gegenschalter <c>--mitnahme-alt</c>: unser altes CarryWindow, Schaden
/// reist mit. <c>--marke-alt</c>: gebaute Einheiten ohne Marke/+0x0D/+0x0F.</para>
/// </summary>
public partial class MapEntityLayer
{
    public static bool MitnahmeAlt, MarkeAlt;

    /// <summary>Waffe 8 »MS-Rakete« und Antrieb 0xAB »A-W-Stell.« — @0x48247C / @0x482473.</summary>
    private const int MitnahmeOhneWaffe = 8, MitnahmeOhneAntrieb = 0xAB;

    /// <summary>Lebt und ist Fahrzeug (Klasse 0) — die Grundmenge von Liste und
    /// Auszahlung. ⚠ Eine untergestellte Einheit ist im Original weiter ein
    /// lebender Satz im 1000er-Block; ob sie dort erscheint, ist nicht im Spiel
    /// gesehen (Lesung §7.2) — sie zählt hier mit.</summary>
    private bool MitnahmeKlasse0(Entity e, int player)
        => !e.IsBuilding && !e.IsProp && !e.Dead && e.Owner == player && e.GameUnitType == 0;

    /// <summary>Die linke Liste ohne die Tafel — Griffe (Entitätsindizes), nach
    /// Platznummer aufsteigend.</summary>
    public List<int> MitnahmeLinks(int player, ICollection<int> tafel)
    {
        var l = new List<int>();
        for (int i = 0; i < _entities.Count; i++)
        {
            var e = _entities[i];
            if (!MitnahmeKlasse0(e, player)) continue;
            if (e.Comp0D == 0 || e.Comp0D == MitnahmeOhneWaffe) continue;
            if (e.Comp0F == MitnahmeOhneAntrieb) continue;
            if (tafel.Contains(i)) continue;
            l.Add(i);
        }
        l.Sort((a, b) => _entities[a].Slot.CompareTo(_entities[b].Slot));
        return l;
    }

    /// <summary><c>0x4C1720</c> — was Berkewitz Corp. für den Rest zahlt.</summary>
    public int MitnahmeAuszahlung(int player, ICollection<int> tafel)
    {
        int summe = 0;
        for (int i = 0; i < _entities.Count; i++)
        {
            if (!MitnahmeKlasse0(_entities[i], player) || tafel.Contains(i)) continue;
            summe += Campaign.CampaignManager.SellPrice(UnitValueOf(i));
        }
        return summe;
    }

    /// <summary>Zeile und Werteblock einer Einheit — derselbe Textbaustein wie
    /// Depot und Markt.</summary>
    public UI.BuildingWindow.DepotZeile MitnahmeZeile(int i)
    {
        var u = _entities[i];
        var bild = BildDerEinheit(u);
        bool waffe = u.Weapon != 0 && !IsEquipmentMount(u.Weapon);
        return new UI.BuildingWindow.DepotZeile
        {
            Griff = i,
            Rang = u.Rating28,
            Name = MitnahmeName(u),
            Hp = u.Hp, HpMax = u.HpMax,
            ChassisPic = bild.ChassisPic, TurretPic = bild.TurretPic,
            HatWaffe = waffe,
            Waffe = waffe ? InfoBauteil(WeaponRowOf(u.Weapon)) : "",
            Nachladen = InfoNachladen(u),
            Verbesserung = u.Equipment > 0 ? InfoBauteil(u.Equipment) : "",
            Antrieb = u.Comp0F > 0 ? InfoBauteil(u.Comp0F) : "",
            Zwilling = InfoZwilling(u),
            Angriff = u.Attack, Verteidigung = u.Defence,
            Geschw = u.Speed, Sicht = u.Sight,
            Reichw = u.Range, MinReichw = u.RangeMin,
        };
    }

    /// <summary>Der Name nach 0x4829FC: Entwurf <c>+0x3E</c> &lt; 200 → sec47
    /// <c>typ + 200·Betrachter</c>. Bei uns steht die Entwurfsnummer in der Marke
    /// (+0x43; auf allen 225 Kartenangeboten gleich +0x3E, beim Fabrik-Aufsteller
    /// beide gleich); ohne Marke der Name des Satzes.</summary>
    private string MitnahmeName(Entity u)
    {
        if (u.Name.Length > 0) return u.Name;
        int owner = u.Owner is >= 0 and <= 7 ? u.Owner : 0;
        if (u.Mark is >= 0 and < 200 && DesignBySlot(u.Mark + 200 * owner) is { Name.Length: > 0 } d)
            return d.Name;
        return LabelOf(u);
    }

    /// <summary>Was vom Satz mitreist (unser Weg für die Stapelkopie 0x43B350).</summary>
    public Campaign.CampaignManager.CarriedUnit MitnahmeSatz(int i)
    {
        var e = _entities[i];
        int pro = e.HpMax > 0 ? Mathf.Clamp(100 * e.Hp / e.HpMax, 1, 100) : 100;
        return new Campaign.CampaignManager.CarriedUnit
        {
            Design = e.Mark, Energie = pro, Name = e.Name, Rang = e.Rating28,
        };
    }

    /// <summary>Eine mitgenommene Einheit aufstellen — <c>place_carry</c>.
    /// Gibt den Entitätsindex oder −1.</summary>
    public int MitnahmeAufstellen(Campaign.CampaignManager.CarriedUnit c, int col, int row, int player)
    {
        LoadDesigns();
        int at = -1;
        if (c.Design >= 0 && _designBySlot.ContainsKey(c.Design + 200 * player))
            at = SpawnReinforcement(c.Design, col, row, player);
        else if (_designs != null && c.Name.Length > 0)
        {
            // ⚠ UNSER Weg: ein selbst gezeichneter Entwurf hat keinen sec47-Platz.
            foreach (var d in _designs)
                if (d.Name == c.Name) { at = SpawnAusEntwurf(d, -1, col, row, player); break; }
        }
        if (at < 0) return -1;
        var e = _entities[^1];
        if (MitnahmeAlt)
        {
            if (c.Energie is > 0 and < 100 && e.HpMax > 0)
                e.Hp = Mathf.Max(1, e.HpMax * c.Energie / 100);
        }
        else
        {
            e.Hp = e.HpMax;                          // @0x43B275: +0x08 := +0x29
            e.Ammo = e.AmmoMax;                      // @0x43B288
            e.Fuel = e.FuelMax;                      // @0x43B298
            e.Rating28 = c.Rang;                     // +0x28 reist im Satz mit
        }
        if (c.Name.Length > 0) e.Name = c.Name;
        return _entities.Count - 1;
    }
}
