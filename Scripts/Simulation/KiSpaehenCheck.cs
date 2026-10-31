namespace AkteEuropaReborn.Rendering;

using System.Linq;
using System.Text;
using Godot;

/// <summary>
/// ⭐ 30.10.2026 — <b>DER PRUEFSTAND ZU bug-729..732</b> (<c>--ki-spaehen-check[=takte]</c>),
/// die vier KI-Schwaechen aus <c>berichte/gefecht-ki-stillstand-opus.md</c> §6. Bau:
/// <c>berichte/bau-ki-spaehen-sprit.md</c>.
///
/// <list type="number">
/// <item><b>bug-729 Spaehschleife</b> — Leerfahrten: Spaehfahrten zu einer Rasterzelle, an der
///   schon ein Spaeher angekommen war und die trotzdem ungesehen blieb. Soll 0.
///   Nullmodell <c>--spaehen-sperre-alt</c>.</item>
/// <item><b>bug-730 P3 spaeht nie</b> — Gruppenlaeufe, in denen eine Gruppe einen Platz ohne
///   Bauwerk als Ziel behielt (die gemessene Ursache: die ganze Armee blieb darin verplant).
///   Soll 0. Nullmodell <c>--ki-ziel-skriptplatz-alt</c>. Dazu als Angabe: wie oft die Armee
///   ganz verplant war (frei 0), und Spaehversuche ohne Fahrt trotz fahrbereiter Einheiten
///   (die faengt bug-729 als »unerreichbar«).</item>
/// <item><b>bug-731 Sprit</b> — Einheiten ohne Sprit, die in eine Gruppe aufgenommen wurden
///   (Bilden und Auffuellen). Soll 0. Nullmodell <c>--ki-sprit-aufnahme-alt</c>.</item>
/// <item><b>bug-732 Skriptplaetze</b> — wie oft <c>AiPickTarget</c> einen Platz ohne Bauwerk
///   lieferte. Soll 0. Nullmodell <c>--ki-ziel-skriptplatz-alt</c>. In der Kampagne wird
///   gezaehlt, dass <c>AiPickTarget</c> gar nicht gerufen wird.</item>
/// </list>
/// Jeder Punkt urteilt einzeln; KEIN URTEIL, wenn der Lauf den Fall nicht herstellt.
/// </summary>
public partial class MapEntityLayer
{
    /// <summary>bug-730 Messpunkt: Spaehversuche ohne Fahrt, obwohl mehr fahrbereite
    /// Einheiten frei waren als die Wache.</summary>
    public int KiSpaehAusfallMitFahrbereiten;

    public string KiSpaehenCheck(int takte)
    {
        int m = UI.SkirmishSetup.CampaignMission;
        string ort = InCampaign ? $"K{m}" : $"Gefecht {UI.SkirmishSetup.Map}";
        var sb = new StringBuilder($"ki-spaehen-check {ort}, {takte} Takte ({takte / SimHz} s)\n");
        sb.AppendLine($"  Schalter: --spaehen-sperre-alt {SpaehenSperreAlt}, " +
                      $"--ki-sprit-aufnahme-alt {KiSpritAufnahmeAlt}, --ki-ziel-skriptplatz-alt {KiZielSkriptplatzAlt}; " +
                      $"KI {string.Join(",", _ai.Select(a => $"P{a.Player}"))}, Nebel {FogActive}, Gruppen-KI {GefechtKiMarschAn}");

        int skript0 = _entities.Count(e => e.NoStructure && !e.Dead);
        int trockenMax = 0;
        MissionScriptTick(0.001f);
        for (int t = 0; t < takte; t++)
        {
            SimTickFuerProbe();
            if (t % SimHz != 0) continue;
            int tr = 0;
            foreach (var a in _ai)
                foreach (var e in _entities)
                    if (!e.Dead && !e.IsBuilding && !e.IsProp && e.Owner == a.Player && e.Mobile && SpritLeer(e)) tr++;
            if (tr > trockenMax) trockenMax = tr;
        }
        int skript1 = _entities.Count(e => e.NoStructure && !e.Dead);

        // Nebenbefund Kampagne: Auftraege Art 1, die auf einen Platz ohne Bauwerk zeigen
        // (das Original loescht sie in target 0x4BEE13, wir loesen sie auf).
        int auftraegeSkript = 0, auftraege = 0;
        for (int p = 0; p < 8; p++)
            foreach (var t in _missionTargets[p])
            {
                if (t.Kind != 1) continue;
                auftraege++;
                int i = ResolveTarget(p, t);
                if (i >= 0 && _entities[i].NoStructure) auftraegeSkript++;
            }

        sb.AppendLine($"  {GefechtKiZeile()}");
        sb.AppendLine($"  Gruende: {GefechtGruendeZeile()}");
        sb.AppendLine($"  {KiSpaehZeile()}");

        // 1 — bug-729
        int fahrten = _kiSpaehFahrtenJe.Sum();
        sb.Append($"  1 bug-729 Spaehschleife: Rasterfahrten {fahrten}, Leerfahrten {KiSpaehLeerfahrten}, Leerwahlen {KiSpaehLeerwahlen}, gesperrt {KiSpaehGesperrt} — ");
        sb.AppendLine(InCampaign || fahrten == 0 ? "KEIN URTEIL (keine Spaehfahrt zu einer Rasterzelle)"
                    : KiSpaehLeerfahrten + KiSpaehLeerwahlen == 0 ? "BESTANDEN" : "DURCHGEFALLEN");

        // 2 — bug-730
        int keinZiel = _gefGruende.Where(kv => kv.Key.EndsWith(" kein bekanntes Ziel")).Sum(kv => kv.Value);
        int leerArmee = _gefGruende.Where(kv => kv.Key.Contains(" Spaehen: frei 0 <= Wache")).Sum(kv => kv.Value);
        sb.Append($"  2 bug-730 Spaehausfall: »kein bekanntes Ziel« {keinZiel}x, davon Armee ganz verplant (frei 0) {leerArmee}x, " +
                  $"Gruppenlaeufe mit gehaltenem Platz ohne Bauwerk {KiGruppeAufSkriptplatz}, Ausfaelle mit fahrbereiten Einheiten {KiSpaehAusfallMitFahrbereiten}, " +
                  $"Spaehfahrten je KI {string.Join("/", _ai.Select(a => $"P{a.Player} {KiBuch(a).Spaehfahrten}"))} — ");
        sb.AppendLine(InCampaign || keinZiel == 0 ? "KEIN URTEIL (die KI kannte immer ein Ziel)"
                    : KiGruppeAufSkriptplatz == 0 ? "BESTANDEN" : "DURCHGEFALLEN");

        // 3 — bug-731
        sb.Append($"  3 bug-731 Sprit: hoechstens {trockenMax} KI-Einheiten zugleich ohne Sprit, davon in Gruppen aufgenommen {KiSpritleerAufgenommen}, " +
                  $"Nachzuegler {GefechtNachzuegler} — ");
        sb.AppendLine(InCampaign ? $"KEIN URTEIL (Kampagne: die Regel gilt nur im Gefecht; aufgenommen {KiSpritleerAufgenommen} ist der Originalstand)"
                    : trockenMax == 0 ? "KEIN URTEIL (keine KI-Einheit ohne Sprit)"
                    : KiSpritleerAufgenommen == 0 ? "BESTANDEN" : "DURCHGEFALLEN");

        // 4 — bug-732
        sb.Append($"  4 bug-732 Skriptplaetze: AiPickTarget Gefecht {KiPickGefecht}x / Kampagne {KiPickKampagne}x, davon Platz ohne Bauwerk {KiPickSkriptplatz}, " +
                  $"Plaetze ohne Bauwerk lebend {skript0} -> {skript1}; Nebenbefund Auftraege Art 1 {auftraege}, davon auf Platz ohne Bauwerk {auftraegeSkript} — ");
        if (InCampaign)
            sb.AppendLine(KiPickKampagne == 0 ? "KAMPAGNE UNBERUEHRT (AiPickTarget nie gerufen)" : "DURCHGEFALLEN (AiPickTarget in der Kampagne gerufen)");
        else
            sb.AppendLine(KiPickGefecht == 0 ? "KEIN URTEIL (AiPickTarget nie gerufen)"
                        : KiPickSkriptplatz == 0 ? "BESTANDEN" : "DURCHGEFALLEN");
        return sb.ToString();
    }
}
