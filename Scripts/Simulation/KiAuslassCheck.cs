namespace AkteEuropaReborn.Rendering;

using System.Collections.Generic;
using Godot;

/// <summary>
/// <b>--ki-einnahme-auslass-check</b> (29.10.2026, bug-647, KayelGee R8 K5) — siehe
/// <c>SkirmishAi.AiEmptyDepots</c> und <c>EinnahmeAbschluss.EinnahmeInsassenLoeschen</c>.
///
/// <para>Aufbau (Gefecht oder Kampagne mit KI-Basis): ein eigenes Fahrzeug steht auf
/// der Einnahmezelle (Tuer 0 + 1 Zeile) einer KI-Basis; drei Neubauten liegen in
/// deren Liste (<c>NeubauInListe</c>, UKOL 0x33). Dann laufen Takte bis zum
/// Besitzerwechsel. Gezaehlt: wie viele der drei je ausserhalb der Liste standen
/// (»neben der Basis erschienen«), und wie viele bei Abschluss geloescht wurden;
/// dazu, ob ein laufender Bau der KI die Einnahme ueberlebt.</para>
/// <para><b>Soll:</b> 0 daneben, 3 bis zum Abschluss in der Liste, danach 3 geloescht,
/// kein laufender Bau beim neuen Besitzer. <b>Nullmodell</b>
/// <c>--ki-auslass-rueckfall-alt</c>: die drei erscheinen neben der Basis.</para>
/// <para>⚠ EINGRIFFE: Besitzer/Lage/TP des Einnehmers (TP 30000, damit die KI ihn
/// nicht wegschiesst), drei Neubauten, ein laufender Bau (BuildTime 999 s) der Basis.</para>
/// </summary>
public partial class MapEntityLayer
{
    /// <summary><c>--ki-auslass-rueckfall-alt</c> — bug-647: die KI holt jedes Bild alle
    /// Saetze mit dem Rueckfall NearestFree aus der Garage (Stand 0.6.11).</summary>
    public static bool KiAuslassRueckfallAlt;

    public static bool KiEinnahmeAuslassCheckAn;

    public string KiEinnahmeAuslassCheck()
    {
        var sb = new System.Text.StringBuilder("ki-einnahme-auslass-check"
            + (KiAuslassRueckfallAlt ? " — NULLMODELL --ki-auslass-rueckfall-alt" : "")
            + (EinnahmeBauWeiter ? " (--einnahme-bau-weiter)" : "") + "\n");
        if (_nav == null || _designs == null || _designs.Count == 0) return sb.Append("  KEIN URTEIL: keine Karte/Entwuerfe").ToString();
        var kiSpieler = new HashSet<int>();
        foreach (var a in _ai) kiSpieler.Add(a.Player);
        int bi = -1;
        for (int i = 0; i < _entities.Count; i++)
        {
            var b = _entities[i];
            if (b.IsBuilding && !b.IsProp && !b.Dead && b.BType == 1 && b.DoorCells.Count > 0
                && kiSpieler.Contains(b.Owner) && Capturable(b)) { bi = i; break; }
        }
        if (bi < 0)
        {
            var basen = new List<string>();
            foreach (var b in _entities)
                if (b.IsBuilding && !b.IsProp && !b.Dead && b.BType == 1)
                    basen.Add($"Platz {b.Slot} Sp{b.Owner} Tueren {b.DoorCells.Count} einnehmbar {Capturable(b)}");
            return sb.Append($"  KEIN URTEIL: keine einnehmbare KI-Basis (KI {string.Join(",", kiSpieler)}; Basen: {string.Join("; ", basen)})").ToString();
        }
        var basis = _entities[bi];
        int alterBesitzer = basis.Owner;
        int ui = -1;
        for (int i = 0; i < _entities.Count; i++)
        {
            var e = _entities[i];
            if (e.IsBuilding || e.IsProp || e.Dead || e.Owner != ViewPlayer || e.Infantry >= 0
                || !e.Mobile || e.GameUnitType != 0 || Untergestellt(e)) continue;
            ui = i; break;
        }
        if (ui < 0) return sb.Append("  KEIN URTEIL: kein eigenes Fahrzeug").ToString();
        var (tuer, front) = CaptureCells(basis);
        var u = _entities[ui];
        u.HpMax = u.Hp = 30000;
        ProbeVersetzen(ui, front.X, front.Y);
        sb.AppendLine($"  EINGRIFF: KI-Basis Platz {basis.Slot} (Spieler {alterBesitzer}, TP {basis.Hp}) Tuer ({tuer.X},{tuer.Y}), "
                    + $"Einnehmer Platz {u.Slot} auf ({u.Col},{u.Row}) (Soll {front.X},{front.Y})");
        int design = 0;
        for (int k = 0; k < _designs.Count; k++)
            if (_designs[k].Slot >= 0 && _designs[k].Slot / DesignsPerPlayer == alterBesitzer) { design = k; break; }
        var neu = new List<Entity>();
        for (int k = 0; k < 3 && GarageBelegt(basis) < DepotSlots; k++)
            if (NeubauInListe(basis, design, merker: false) is { } nb) neu.Add(nb);
        basis.BuildIndex = design;
        basis.BuildTime = 999f;                      // ein laufender Bau ueber die Einnahme
        sb.AppendLine($"  EINGRIFF: {neu.Count} Neubauten in der Liste (Entwurf {design}), laufender Bau 999 s");
        int daneben = 0, maxTakte = 6000, takte = 0;
        var gesehen = new HashSet<Entity>();
        for (; takte < maxTakte; takte++)
        {
            SimTickFuerProbe();
            foreach (var n in neu)
                if (!n.Dead && !basis.Garage.Contains(n) && gesehen.Add(n))
                {
                    daneben++;
                    sb.AppendLine($"    Takt {takte}: Neubau Platz {n.Slot} steht DRAUSSEN auf ({n.Col},{n.Row}) "
                                + $"(Tuer {tuer.X},{tuer.Y})");
                }
            if (basis.Owner != alterBesitzer) break;
        }
        bool eingenommen = basis.Owner == ViewPlayer;
        int geloescht = neu.FindAll(n => n.Dead).Count;
        sb.AppendLine($"  nach {takte} Takten: Basis Besitzer {alterBesitzer} -> {basis.Owner} (eingenommen {eingenommen}), "
                    + $"Einnehmer auf ({u.Col},{u.Row}), Liste {basis.Garage.Count}, laufender Bau {basis.BuildTime:0.0} s");
        sb.AppendLine($"  Neubauten draussen erschienen: {daneben} (Soll 0), bei Abschluss geloescht: {geloescht} (Soll {neu.Count})");
        bool bauWeg = basis.BuildTime <= 0f;
        // die Erwartungen bleiben unter beiden Gegenschaltern stehen — ein Nullmodell MUSS fallen
        bool ok = eingenommen && neu.Count == 3 && daneben == 0 && geloescht == 3 && bauWeg;
        sb.AppendLine($"  laufender Bau geloescht: {bauWeg} (Soll True)");
        if (!eingenommen) sb.AppendLine("  ⚠ die Einnahme kam nicht zustande — das Urteil sagt nichts");
        sb.Append(ok ? "  BESTANDEN" : "  DURCHGEFALLEN");
        return sb.ToString();
    }
}
