namespace AkteEuropaReborn.Rendering;

using System.Text;
using Godot;

/// <summary>
/// ⭐ <b>DER RADARMAST IN DER ZELLFRAGE 0x421D40</b> (30.10.2026, bug-722,
/// berichte/bau-gaswolke.md §8). Der Bau steht in <see cref="MineOderFalleAufZelle"/>
/// (Fallen.cs); hier nur der Pruefstand.
///
/// <para><c>--radarmast-zelle-check</c> — ⚠ EINGRIFF: ein eigener Minenleger bekommt
/// Munition 4, einen Legerahmen 2×1 ab seiner Zelle (c, r) und einen Radarmast auf
/// (c, r). Soll wie im Original: keine Mine auf (c, r) — der Arm @0x408832 sieht den
/// Mast und faehrt zur naechsten freien Zelle des Rahmens. Nullmodell
/// <c>--radarmast-zelle-alt</c>: die Mine liegt unter dem Mast.</para>
/// </summary>
public partial class MapEntityLayer
{
    public string RadarmastZelleCheck()
    {
        var sb = new StringBuilder("radarmast-zelle-check\n");
        EnsureMissionScript();
        if (_nav == null) return sb.Append("  keine Karte\n  DURCHGEFALLEN").ToString();
        if (RadarmastZelleAlt) sb.AppendLine("  ⚠ NULLMODELL --radarmast-zelle-alt: hier MUSS der Lauf durchfallen");
        int li = -1;
        for (int i = 0; i < _entities.Count && li < 0; i++)
        {
            var e = _entities[i];
            if (!e.Dead && !e.IsProp && !e.IsBuilding && e.Owner == ViewPlayer && LegerArt(e) == ZbranMine && !Untergestellt(e)) li = i;
        }
        if (li < 0) return sb.Append("  kein eigener Minenleger auf dieser Karte (K18 hat drei) — sagt NICHTS\n  DURCHGEFALLEN").ToString();
        var l = _entities[li];
        if (!FreierStreifen(out int c, out int r)) return sb.Append("  kein freier Streifen\n  DURCHGEFALLEN").ToString();
        l.Target = -1; l.Path = null; l.Ordered = false; l.Orders.Clear();
        ProbeVersetzen(li, c, r);
        l.Ammo = System.Math.Max(l.Ammo, 4); l.Cooldown = 0;
        if (l.FuelMax > 0) l.Fuel = System.Math.Max(l.Fuel, l.FuelMax);
        bool mast = PlaceRadarMast(c, r, ViewPlayer);
        l.LegeX = c; l.LegeY = r; l.LegeB = 2; l.LegeH = 1;
        int unter = 0, daneben = 0, takte = 0;
        for (; takte < 400 && l.LegeB != 0 && !l.Dead; takte++)
        {
            SimTickFuerProbe();
            unter = MinenAufZelle(c, r); daneben = MinenAufZelle(c + 1, r);
        }
        unter = MinenAufZelle(c, r); daneben = MinenAufZelle(c + 1, r);
        bool ok = mast && unter == 0 && daneben == 1;
        sb.AppendLine($"  Leger {li} auf ({c},{r}), Mast {(mast ? "gesetzt" : "NICHT gesetzt")}, Rahmen 2x1; nach {takte} Takten: "
                    + $"Minen unter dem Mast {unter} (Soll 0), auf ({c + 1},{r}) {daneben} (Soll 1), Auftrag {(l.LegeB == 0 ? "zu Ende" : "laeuft")}");
        return sb.Append(ok ? "  BESTANDEN" : "  DURCHGEFALLEN").ToString();
    }
}
