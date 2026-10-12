namespace AkteEuropaReborn.Rendering;

using System.Linq;

/// <summary>
/// <b>--flughafen-einnahme-check — gehen die Flugzeuge mit dem Flughafen?</b>
/// (12.10.2026, bug-497, gemeldet in K27). Siehe
/// Simulation/EinnahmeAbschluss.cs, <c>EinnahmeFlugzeugeUmschreiben</c>.
///
/// <para>Jeder fremde Flughafen (Art 9) wird direkt fuer den Spieler eingenommen
/// (<c>CaptureDone</c>), sobald dort ein Flugzeug beheimatet ist (die KI baut
/// sie erst; Vorgabe hoechstens 600 s, <c>=sekunden</c>), danach laufen 5 s Simulation. Soll: alle Flugzeuge der
/// Typen 1..12 mit dieser Heimat gehoeren dem Spieler, die Versorgungshelis
/// 13/14 behalten ihren Besitzer, und es gab mindestens ein Flugzeug zu pruefen.
/// Nullmodell <c>--flughafen-einnahme-alt</c>: FAELLT DURCH.</para>
///
/// <para>⚠ EINGRIFF: die Einnahme wird ohne Belagerer abgeschlossen.</para>
/// </summary>
public partial class MapEntityLayer
{
    public string FlughafenEinnahmeCheck(int warteSekunden)
    {
        var sb = new System.Text.StringBuilder(
            $"flughafen-einnahme-check K{UI.SkirmishSetup.CampaignMission}, Spieler {ViewPlayer}, " +
            $"--flughafen-einnahme-alt {FlughafenEinnahmeAlt}\n");
        // Zu Missionsbeginn hat K27 KEIN Flugzeug — die KI baut sie erst. Also
        // laufen lassen, bis an einem fremden Flughafen eines beheimatet ist.
        int w = 0;
        for (; w < warteSekunden * SimHz; w++)
        {
            if (_special.Any(a => a.Kind is >= 1 and <= 12 && _entities.Any(e =>
                    e.IsBuilding && !e.Dead && e.BType == 9 && e.Owner != ViewPlayer && e.Slot == a.HomeSlot)))
                break;
            SimTickFuerProbe();
        }
        sb.AppendLine($"  gewartet {w / SimHz} s (hoechstens {warteSekunden} s)");
        int geprueft = 0, falsch = 0, versorgerFalsch = 0;
        foreach (var e in _entities.Where(e => e.IsBuilding && !e.IsProp && !e.Dead && e.BType == 9))
            sb.AppendLine($"  Flughafen Platz {e.Slot} Spieler {e.Owner}, Hangarliste {(e.Hangar == null ? "-" : string.Join(",", e.Hangar))}");
        foreach (var a in _special)
            sb.AppendLine($"  Flugzeug Platz {a.Slot} Typ {a.Kind} Spieler {a.Owner} Heimat {a.HomeSlot} {(a.Stored ? "im Hangar" : "draussen")} Hp {a.Hp}");
        for (int i = 0; i < _entities.Count; i++)
        {
            var b = _entities[i];
            if (!b.IsBuilding || b.IsProp || b.Dead || b.BType != 9 || b.Owner == ViewPlayer) continue;
            var heim = _special.Where(a => a.HomeSlot == b.Slot && a.Kind >= 1).ToList();
            var vorher = heim.ToDictionary(a => a, a => a.Owner);
            int alt = b.Owner;
            CaptureDone(i, b, ViewPlayer);
            int kampf = 0, kampfOk = 0, versorger = 0;
            foreach (var a in heim)
            {
                if (a.Kind <= 12) { kampf++; if (a.Owner == ViewPlayer) kampfOk++; }
                else { versorger++; if (a.Owner != vorher[a]) versorgerFalsch++; }
            }
            geprueft += kampf;
            falsch += kampf - kampfOk;
            sb.AppendLine($"  Flughafen Platz {b.Slot} (Spieler {alt} -> {ViewPlayer}): Flugzeuge 1..12 {kampfOk}/{kampf} " +
                          $"umgeschrieben (davon im Hangar {heim.Count(a => a.Kind <= 12 && a.Stored)}), " +
                          $"Versorgungshelis {versorger}, umgeschrieben laut Zaehler {FlugzeugeUmgeschrieben}");
        }
        for (int t = 0; t < 5 * SimHz; t++) SimTickFuerProbe();
        sb.AppendLine($"  5 s weitergelaufen, Takt {_taktNr}");
        bool ok = geprueft > 0 && falsch == 0 && versorgerFalsch == 0;
        if (geprueft == 0) sb.AppendLine("  kein fremdes Flugzeug mit Heimat an einem fremden Flughafen — ungeprueft");
        sb.Append(ok ? "  BESTANDEN" : $"  DURCHGEFALLEN ({falsch} Flugzeuge beim alten Besitzer, {versorgerFalsch} Versorger umgeschrieben)");
        return sb.ToString();
    }
}
