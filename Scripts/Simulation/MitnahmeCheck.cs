using System.Collections.Generic;
using Godot;

namespace AkteEuropaReborn.Rendering;

/// <summary>Die Eingriffe des <c>--mitnahme-check</c> (13.09.2026), siehe
/// Rendering/MitnahmeLauf.cs.</summary>
public partial class MapEntityLayer
{
    /// <summary>EINGRIFF: vier bewaffnete Fahrzeuge aus dem Depot der Basis
    /// aussenden (wie gebaut), das erste angeschlagen und mit Rang.</summary>
    public string MitnahmeProbeBauen()
    {
        LoadDesigns();
        int basis = -1, entwurf = -1;
        for (int i = 0; i < _entities.Count; i++)
            if (_entities[i].IsBuilding && !_entities[i].Dead && _entities[i].Owner == ViewPlayer && _entities[i].BType == 1) { basis = i; break; }
        if (_designs != null)
            for (int k = 0; k < _designs.Count; k++)
                if (_designs[k].Weapon is > 0 and < 50 && _designs[k].Weapon != MitnahmeOhneWaffe && _designs[k].Slot >= 0
                    && TypeOfChassis(_designs[k].Propulsion) == 0 && _designs[k].Propulsion != MitnahmeOhneAntrieb) { entwurf = k; break; }
        if (basis < 0 || entwurf < 0) return $"keine Basis ({basis}) oder kein Entwurf ({entwurf})";
        int gebaut = 0;
        for (int z = 0; z < 4; z++)
        {
            _entities[basis].Depot.Add(entwurf);
            if (!SendOutOfDepot(_entities[basis], _entities[basis].Depot.Count - 1)) break;
            var u = _entities[^1];
            u.Ukol = 0;
            if (z == 0) { u.Hp = System.Math.Max(1, u.HpMax / 3); u.Rating28 = 42; }
            gebaut++;
        }
        return $"{gebaut} x \"{_designs![entwurf].Name}\" gebaut (Marke {_designs[entwurf].Slot % 200}); die erste mit 1/3 Energie und Rang 42";
    }

    public int MitnahmeRang(int i) => i >= 0 && i < _entities.Count ? _entities[i].Rating28 : -1;

    public string MitnahmeFilterZeile()
    {
        int alle = 0, links = MitnahmeLinks(ViewPlayer, new HashSet<int>()).Count;
        foreach (var e in _entities) if (!e.IsBuilding && !e.IsProp && !e.Dead && e.Owner == ViewPlayer && e.Mobile) alle++;
        return $"{alle} eigene bewegliche Einheiten, davon {links} nach dem Filter 0x482436";
    }

    public (bool Alle, bool Voll, string Zeile) MitnahmeProbeAufstellen(
        List<Campaign.CampaignManager.CarriedUnit> mit, List<int> tafel)
    {
        var sb = new System.Text.StringBuilder();
        bool alle = true, voll = true;
        for (int k = 0; k < mit.Count && k < tafel.Count; k++)
        {
            var alt = _entities[tafel[k]];
            var cell = _nav?.NearestFree(new Vector2I(alt.Col + 3, alt.Row + 3));
            int at = cell == null ? -1 : MitnahmeAufstellen(mit[k], cell.Value.X, cell.Value.Y, ViewPlayer);
            if (at < 0) { alle = false; sb.Append($"  FEHLT: {mit[k].Name} (Marke {mit[k].Design})\n"); continue; }
            var u = _entities[at];
            bool ok = u.Hp == u.HpMax && u.Rating28 == alt.Rating28 && u.Mark == alt.Mark && MitnahmeName(u) == MitnahmeName(alt);
            if (MitnahmeAlt) ok = u.Rating28 == alt.Rating28;
            if (!ok) voll = false;
            sb.Append($"  ({u.Name}: Marke {mit[k].Design}, Satz {alt.Slot} Energie vorher {alt.Hp}/{alt.HpMax} -> {u.Hp}/{u.HpMax}, Rang {alt.Rating28} -> {u.Rating28}, Name \"{MitnahmeName(alt)}\" -> \"{MitnahmeName(u)}\" (gemerkt \"{mit[k].Name}\", Satzname \"{alt.Name}\"), ok {ok})\n");
        }
        return (alle, voll, sb.ToString());
    }
}
