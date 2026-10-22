namespace AkteEuropaReborn.Rendering;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Godot;

/// <summary>
/// <c>--gefecht-ki-aktiv-check[=takte]</c> (23.10.2026, Paket W3, bug-632) — wird die
/// Gefechts-KI wirklich aktiv? Synchron wie <c>--ki-wegpunkte-check</c>; Aufruf
/// <c>--skirmish=map_NET07 --no-briefing --no-movie --gefecht-ki-aktiv-check=30000</c>.
///
/// <para><b>Urteil BESTANDEN</b>, wenn innerhalb der Takte</para>
/// <list type="number">
///   <item>G1 mindestens eine Gruppe mit ≥ 3 Mitgliedern gebildet wurde,</item>
///   <item>G2 jeder geschriebene Marschweg nur 4er-Nachbarschritte hat und jeder
///   Fahrbefehl der Wegpunktfahrt im Kasten Anker + [−4, +3]² liegt,</item>
///   <item>G3 das Weiterschalten genau bei <c>3·drin/2 &gt; Grösse</c> geschah (Sammeln) und
///   keine Gruppe mit Weg vor Endmarke 0 angegriffen hat,</item>
///   <item>G4 mindestens eine Gruppe mit Weg ihr Ende erreicht hat und danach ein
///   Gruppenmitglied auf eine feindliche Einheit oder ein feindliches Gebäude
///   geschossen hat.</item>
/// </list>
/// <para>Nullmodell <c>--gefecht-ki-marsch-aus</c>: keine Gruppe → DURCHGEFALLEN. Die
/// Aktivitätszahlen (Schüsse der KI, erster Schuss, Wellen/Gruppen) werden in BEIDEN
/// Läufen geschrieben — das ist der Vorher/Nachher-Vergleich.</para>
/// </summary>
public partial class MapEntityLayer
{
    public static bool GefechtKiCheckAn;

    private int _gkSchuesse, _gkSchuesseMensch, _gkSchuesseGebaeude, _gkGruppenSchuesse,
                _gkGruppenSchuesseGebaeude, _gkGruppenSchuesseEinheit;
    private int _gkErsterSchuss = -1, _gkErsterSchussMensch = -1, _gkErsterGruppenSchuss = -1;

    /// <summary>Aus <see cref="BeschussZaehlen"/>: ein wirklich gefallener Schuss.</summary>
    private void GefechtKiSchussZaehlen(Entity s, Entity o)
    {
        if (s.Owner is < 0 or > 7 || !IstKi(s.Owner) || !AiHostile(s.Owner, o.Owner)) return;
        _gkSchuesse++;
        if (_gkErsterSchuss < 0) _gkErsterSchuss = _taktNr;
        if (o.Owner == ViewPlayer) { _gkSchuesseMensch++; if (_gkErsterSchussMensch < 0) _gkErsterSchussMensch = _taktNr; }
        if (o.IsBuilding) _gkSchuesseGebaeude++;
        // ein Schuss eines Gruppenmitglieds NACH dem Marsch (Endmarke 0, losgeschickt)
        if (s.AiCpu0 != 10 || s.AiCpu1 is < 0 or > 3) return;
        var gr = _aiGruppen[s.Owner][s.AiCpu1];
        if (gr.Einheiten.Count == 0 || !gr.Losgeschickt || KiEnde(s.Owner, s.AiCpu1) != 0) return;
        _gkGruppenSchuesse++;
        if (o.IsBuilding) _gkGruppenSchuesseGebaeude++; else _gkGruppenSchuesseEinheit++;
        if (_gkErsterGruppenSchuss < 0) _gkErsterGruppenSchuss = _taktNr;
    }

    private static string Sek(int takt, int t0) => takt < 0 ? "—" : $"{(takt - t0) / (float)SimHz:0} s";

    public string GefechtKiAktivCheck(int takte)
    {
        var sb = new StringBuilder($"gefecht-ki-aktiv-check {UI.SkirmishSetup.Map}, {takte} Takte ({takte / SimHz} s), Stufe {UI.SkirmishSetup.Level}\n");
        sb.AppendLine($"  Schalter: --gefecht-ki-marsch-aus {GefechtKiMarschAus}, --gefecht-ki-originalfehler {GefechtKiOriginalfehler}; " +
                      $"KI {string.Join(",", _ai.Select(a => $"P{a.Player}"))}, Mensch P{ViewPlayer}, Nebel {FogActive}");
        if (InCampaign) return sb.Append("gefecht-ki-aktiv-check: KEIN URTEIL (Kampagne)").ToString();
        if (GefechtKiMarschAn)
        {
            KiTafelnSicher();
            sb.AppendLine($"  Sektortafeln: {KiTafelQuelle}");
            int nn = GefechtKiTafelNummer();
            if (nn >= 0) sb.Append(KiTafelVergleich(nn).Replace("  A1", "  T1").Replace("  A2", "  T2"));
        }

        KiPruefAn = true;
        GefechtKiCheckAn = true;
        _kiPruefGruppen.Clear(); _kiPruefLetzter.Clear(); KiWegLog.Clear(); GefechtGruppenLog.Clear();
        _kiPruefWeiter = _kiPruefRegelOk = _kiPruefAngriffVorEnde = _kiPruefBefehle = 0;
        _kiPruefBefehleImKasten = _kiPruefBefehleRingVoll = _kiPruefSofort = _kiPruefLeer = _kiPruefEinzelImMarsch = 0;
        int t0 = _taktNr;
        int maxGroesse = 0, maxGroesseTakt = -1;
        for (int t = 0; t < takte; t++)
        {
            SimTickFuerProbe();
            if (t % SimHz != 0) continue;
            foreach (var a in _ai)
                for (int g = 0; g < 4; g++)
                {
                    int n = _aiGruppen[a.Player][g].Einheiten.Count;
                    if (n > maxGroesse) { maxGroesse = n; maxGroesseTakt = _taktNr; }
                }
        }
        KiPruefAn = false;
        GefechtKiCheckAn = false;

        // G1
        var gross = GefechtGruppenLog.Where(x => x.Groesse >= 3).ToList();
        int ersteGruppe = gross.Count > 0 ? gross.Min(x => x.Takt) : -1;
        // G2/G3 aus dem Wegprotokoll (KiWegpunkteCheck.cs)
        var mitWeg = _kiPruefGruppen.Where(r => r.Laenge > 0).ToList();
        int schritte = mitWeg.Sum(r => r.Schritte), schraeg = mitWeg.Sum(r => r.Schraeg);
        int vorEnde = _kiPruefAngriffVorEnde + mitWeg.Count(r => r.ErsterAngriffTakt >= 0 &&
                                                                (r.Ende0Takt < 0 || r.ErsterAngriffTakt < r.Ende0Takt));
        int ende = mitWeg.Count(r => r.Ende0Takt >= 0);
        int ersterGruppenangriff = _kiPruefGruppen.Where(r => r.ErsterAngriffTakt >= 0).Select(r => r.ErsterAngriffTakt).DefaultIfEmpty(-1).Min();

        foreach (var r in _kiPruefGruppen.Take(10))
            sb.AppendLine($"  Gruppe P{r.P} g{r.G} bei {Sek(r.Takt, t0)}: Start {r.Start} Ziel {r.Ziel}, Weg " +
                          $"{(r.Weg.Count == 0 ? "—" : string.Join(" ", r.Weg))}, Ende 0 bei {Sek(r.Ende0Takt, t0)}, " +
                          $"erster Angriff {Sek(r.ErsterAngriffTakt, t0)}");
        sb.AppendLine($"  G1 Gruppen >= 3: {gross.Count} (alle gebildet {GefechtGruppenLog.Count}, Groessen " +
                      $"{string.Join(",", GefechtGruppenLog.Take(16).Select(x => x.Groesse))}), erste bei {Sek(ersteGruppe, t0)}, " +
                      $"groesste {maxGroesse} bei {Sek(maxGroesseTakt, t0)}");
        sb.AppendLine($"  G2 Wege {mitWeg.Count}, Schritte {schritte}, schraeg {schraeg}; Fahrbefehle {_kiPruefBefehle}, im Kasten {_kiPruefBefehleImKasten}" +
                      (_kiPruefBefehleRingVoll > 0 ? $", Ring voll {_kiPruefBefehleRingVoll}" : ""));
        sb.AppendLine($"  G3 Weiterschalten {_kiPruefWeiter} (Regel 3*drin/2 > Groesse ok {_kiPruefRegelOk}/{_kiPruefWeiter}), Angriff vor Ende 0: {vorEnde}");
        sb.AppendLine($"  G4 Marschende erreicht {ende}, Gruppenschuesse nach dem Marsch {_gkGruppenSchuesse} " +
                      $"(Einheiten {_gkGruppenSchuesseEinheit}, Gebaeude {_gkGruppenSchuesseGebaeude}), erster {Sek(_gkErsterGruppenSchuss, t0)}");
        sb.AppendLine($"  Aktivitaet: KI-Schuesse auf Feinde {_gkSchuesse} (auf den Menschen {_gkSchuesseMensch}, auf Gebaeude {_gkSchuesseGebaeude}), " +
                      $"erster KI-Schuss {Sek(_gkErsterSchuss, t0)}, erster auf den Menschen {Sek(_gkErsterSchussMensch, t0)}, " +
                      $"erster Gruppenangriffsbefehl {Sek(ersterGruppenangriff, t0)}, Wellen/Gruppen gesamt {_ai.Sum(a => a.Waves)}");
        sb.AppendLine($"  {GefechtKiZeile()}");
        sb.AppendLine($"  Gruende: {GefechtGruendeZeile()}");
        foreach (var a in _ai)
            if (!GefechtGruppenLog.Any(x => x.P == a.Player))
                sb.AppendLine($"  ohne Gruppe: {KiDiagnoseKeineGruppe(a.Player)}; Armee {ArmyOf(a.Player).Count}, sec110 {_aiSec110[a.Player]}");
        sb.AppendLine($"  Zaehler: Wegsuchen {KiWegsuchen}, Weiterschaltungen {KiWegWeiter}, Marschende {KiWegEnde}, Einzelbefehle an Mitglieder im Marsch {_kiPruefEinzelImMarsch}");

        bool g1 = gross.Count > 0;
        bool g2 = mitWeg.Count > 0 && schraeg == 0 && _kiPruefBefehleImKasten == _kiPruefBefehle;
        bool g3 = _kiPruefRegelOk == _kiPruefWeiter && vorEnde == 0;
        bool g4 = ende > 0 && _gkGruppenSchuesse > 0;
        string urteil = g1 && g2 && g3 && g4 ? "BESTANDEN"
                      : $"DURCHGEFALLEN ({(g1 ? "" : "G1 ")}{(g2 ? "" : "G2 ")}{(g3 ? "" : "G3 ")}{(g4 ? "" : "G4")})".Replace(" )", ")");
        sb.Append($"gefecht-ki-aktiv-check: {UI.SkirmishSetup.Map} Gruppen>=3 {gross.Count}, Wege {mitWeg.Count} (schraeg {schraeg}), " +
                  $"Weiterschalten {_kiPruefRegelOk}/{_kiPruefWeiter}, vor Ende {vorEnde}, Ende erreicht {ende}, " +
                  $"Gruppenschuesse {_gkGruppenSchuesse}, KI-Schuesse {_gkSchuesse}, erster Schuss {Sek(_gkErsterSchuss, t0)} — {urteil}");
        return sb.ToString();
    }
}
