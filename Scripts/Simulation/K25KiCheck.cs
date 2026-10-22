namespace AkteEuropaReborn.Rendering;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Godot;

/// <summary>
/// <c>--k25-ki-check</c> (22.10.2026, Paket W2, bug-628..630) — der Prüfstand aus
/// berichte/k25-gruppenbildung-opus.md §6: Kampagne 25, Spieler 2, bis Originaltakt
/// 13 300, Momentaufnahmen bei den Takten der Originalstände 5.DM (1475), 10.DM (10113),
/// 3.DM (11041) und 7.DM (13299), gezählt mit <c>_origTicks</c> (sec54).
///
/// <para>Sollwerte allein aus den Originalständen (Regel N): Freigabe sec59 von P2 = 0
/// und Zähler sec58 = 0 in 4/4, 0 Programmeinheiten, 2..6 Transporter (Original
/// 3/4/4/5; nur gemeldet), nie eine P2-Gruppe mit Mitglied, mindestens ein leerer Weg ab
/// (3,3), sobald Bertha in Modus 2 auf (3,3) stand, und kein KI-Transporter je in
/// Modus 1/2.</para>
///
/// <para>Nullmodelle (müssen ROT werden): <c>--ki-produktionsfreigabe-aus</c> (der
/// Zähler läuft), <c>--ki-modus20-aus</c> (ein stehender Transporter wird Kandidat →
/// Modus 1/2; tritt das nicht ein: KEIN URTEIL P6), und der Prüfschalter
/// <c>--ki-aufnahme-standort</c> (Bertha wird aufgenommen → Gruppe mit Mitglied).</para>
/// </summary>
public partial class MapEntityLayer
{
    private static readonly int[] K25Takte = { 1475, 10113, 11041, 13299 };

    private string K25TransporterKosten()
    {
        if (_designs == null) return "—";
        for (int k = 0; k < _designs.Count; k++)
            if (_designs[k].Weapon == TransporterZeile)
                return $"{_designs[k].Name} {_designs[k].CostW}/{_designs[k].CostF}/{_designs[k].CostS}";
        return "keiner";
    }

    public string K25KiCheck()
    {
        int m = UI.SkirmishSetup.CampaignMission;
        const int p = 2;
        var sb = new StringBuilder($"k25-ki-check K{m}, Spieler {p}\n");
        sb.AppendLine($"  Schalter: --ki-produktionsfreigabe-aus {KiProduktionsfreigabeAus}, --ki-modus20-aus {KiModus20Aus}, " +
                      $"--ki-aufnahme-standort {KiAufnahmeStandort}, --ki-routenfilter-aus {KiRoutenfilterAus}, " +
                      $"--ki-cpu1-nullen-alt {KiCpu1NullenAlt}, --ki-tafeln-gerechnet {KiTafelnGerechnet}");
        if (!InCampaign || m != 25)
            return sb.Append("k25-ki-check: KEIN URTEIL (nur fuer -- --campaign=25)").ToString();

        // ⚠ EINGRIFF DES PRUEFSTANDS, aus dem Original: unser M25-Skript traegt KEIN
        // add_target (Data/mission_scripts.json, 0 Treffer), im Original stehen fuer P2
        // in 5/10/3/7.DM dieselben 20 Auftraege (sec69: Art 1, Vorrang 6, Plaetze
        // 11..17, 19..23, 38, 65..67, 1, 2, 3, 35; Herkunft ungelesen). Ohne sie gibt es
        // keine Auftragswahl, keinen (leeren) Weg und keine Gruppe — P5 und die
        // Gegenprobe --ki-aufnahme-standort waeren blind. Darum legt der Pruefstand
        // genau diese Liste an, wenn P2 keine hat.
        string eingriff = "";
        if (MissionTargetsOf(p) == 0)
        {
            int[] orig = { 11, 12, 13, 14, 15, 16, 17, 19, 20, 21, 22, 23, 38, 65, 66, 67, 1, 2, 3, 35 };
            foreach (int w in orig) AddMissionTarget(p, 1, 6, w, 0);
            eingriff = $"EINGRIFF: P2 hatte keine Auftraege — die {orig.Length} aus sec69 der Originalstaende angelegt";
        }
        KiPruefAn = true;
        KiLeereWege.Clear();
        _kiPruefGruppen.Clear(); _kiPruefLeer = 0;
        MissionScriptTick(0.001f);

        int abwehr0 = 0, bertha0 = 0;
        // Transporter, die schon auf der Karte stehen, sind NICHT aus build_in_base
        // (CPU0 aus der Karte; 25.CWM endet vor sec60, also 0) — P6 zaehlt nur gebaute.
        var kartenWagen = new HashSet<int>();
        foreach (var e in _entities)
            if (!e.IsBuilding && !e.IsProp && IstTransporter(e)) kartenWagen.Add(e.Owner * 10000 + e.Slot);
        var qKarte = new HashSet<int>();
        foreach (var e in _entities)
        {
            if (e.IsBuilding || e.IsProp || e.Dead || e.Owner != p) continue;
            if (e.Faze != 0) abwehr0++;
            else if (e.GameUnitType == 3) bertha0++;
        }

        int freiNull = 0, pcNull = 0, programmMax = 0, mitglied = 0, mitgliedTakt = -1, qTakt = -1;
        var qEinheiten = new HashSet<int>();
        int mechanikFehler = 0;
        bool berthaAuf33Modus2 = false;
        int berthaAuf33Takt = -1;
        var transporter = new List<int>();
        var aufnahmen = new List<string>();
        int snap = 0, wache = 0, grenze = K25Takte[^1] + 10;
        var uhr = System.Diagnostics.Stopwatch.StartNew();
        while (snap < K25Takte.Length && wache++ < 2 * grenze)
        {
            SimTickFuerProbe();

            // fortlaufend: Gruppe mit Mitglied (P2), KI-Transporter in Modus 1/2, Bertha (3,3)
            for (int g = 0; g < 4; g++)
                if (_aiGruppen[p][g].Einheiten.Count > 0)
                {
                    if (mitgliedTakt < 0)
                    {
                        mitgliedTakt = _origTicks;
                        aufnahmen.Add($"Takt {_origTicks}: P2 g{g} mit {_aiGruppen[p][g].Einheiten.Count} Mitglied(ern) " +
                                      string.Join(",", _aiGruppen[p][g].Einheiten.Select(i => _entities[i].Name)));
                    }
                    mitglied = Math.Max(mitglied, _aiGruppen[p][g].Einheiten.Count);
                }
            for (int i = 0; i < _entities.Count; i++)
            {
                var e = _entities[i];
                if (e.Dead || e.IsBuilding || e.IsProp || e.Owner is < 0 or > 7 || e.Owner == ViewPlayer) continue;
                if (IstTransporter(e) && e.AiCpu0 is 1 or 2)
                {
                    if (kartenWagen.Contains(e.Owner * 10000 + e.Slot)) { qKarte.Add(e.Owner * 10000 + e.Slot); continue; }
                    qEinheiten.Add(e.Owner * 10000 + e.Slot);
                    if (qTakt < 0) { qTakt = _origTicks; aufnahmen.Add($"Takt {_origTicks}: Transporter {e.Name} P{e.Owner} Platz {e.Slot} in Modus {e.AiCpu0}"); }
                }
                if (e.Owner == p && e.GameUnitType == 3 && e.AiCpu0 == 2 && AiSektorVon(e.Col, e.Row) == (3, 3) && !berthaAuf33Modus2)
                { berthaAuf33Modus2 = true; berthaAuf33Takt = _origTicks; }
            }

            if (_origTicks < K25Takte[snap]) continue;
            // ---- Momentaufnahme ----
            int abw = 0, ber = 0, tr = 0, prog = 0;
            string berthaText = "—";
            var progNamen = new SortedDictionary<string, int>();
            foreach (var e in _entities)
            {
                if (e.IsBuilding || e.IsProp || e.Dead || e.Owner != p) continue;
                if (e.Faze != 0) { abw++; continue; }
                if (e.GameUnitType == 3)
                {
                    ber++;
                    var (bx, by) = AiSektorVon(e.Col, e.Row);
                    berthaText = $"({e.Col},{e.Row}) Sektor ({bx},{by}) CPU0 {e.AiCpu0} CPU1 ({e.AiCpu1 & 0xF},{(e.AiCpu1 >> 4) & 0xF})";
                    continue;
                }
                if (IstTransporter(e)) { tr++; continue; }
                prog++;
                progNamen[e.Name] = progNamen.GetValueOrDefault(e.Name) + 1;
            }
            bool frei = KiProduktionFrei(p);
            int pc = KiProgrammZaehler(p);
            if (!frei) freiNull++;
            if (pc == 0) pcNull++;
            programmMax = Math.Max(programmMax, prog);
            transporter.Add(tr);
            int trModus20 = _entities.Count(e => !e.Dead && e.Owner == p && IstTransporter(e) && e.AiCpu0 == 20);
            string lager = string.Join(" ", _entities.Where(b => b.IsBuilding && !b.Dead && b.Owner == p && b.BType is 1 or 9)
                                                    .Select(b => $"Platz {b.Slot} Art {b.BType} {b.StockW}/{b.StockF}/{b.StockS}"));
            // die Mechanik selbst: fehlt einer Fabrik (Filter bestanden) eine Route, muss
            // die Freigabe 0 sein — sonst 1 (C 0x4BB7D0). Zaehlt wie das Original.
            int fabriken = 0, gefiltert = 0, fehlend = 0;
            foreach (var g in _entities)
            {
                if (!g.IsBuilding || g.Dead || g.Owner != p || g.BType is not (2 or 3 or 4)) continue;
                fabriken++;
                if (!KiRoutenfilterAus && !KiRoutenFilter(p, g)) { gefiltert++; continue; }
                if (!KiRouteMitQuelle(p, g.Slot)) fehlend++;
                if (!KiRouteMitZiel(p, g.Slot) && KiRouteMine(p) >= 0) fehlend++;
            }
            bool mechanikOk = (fehlend > 0) == !frei || KiProduktionsfreigabeAus;
            if (!mechanikOk) mechanikFehler++;
            sb.AppendLine($"    Lager P2: {lager}; Transporterentwurf {K25TransporterKosten()}; Fabriken {fabriken} " +
                          $"(gefiltert {gefiltert}), fehlende Routen {fehlend} -> Freigabe soll {(fehlend > 0 ? 0 : 1)}, ist {(frei ? 1 : 0)}" +
                          (mechanikOk ? "" : "  ⚠ MECHANIK"));
            sb.AppendLine($"  Takt {_origTicks} (Soll-Stand {K25Takte[snap]}): Abwehr {abw}, Bertha {ber} {berthaText}, " +
                          $"Transporter {tr} (Modus 20: {trModus20}), Programm {prog}" +
                          (progNamen.Count > 0 ? $" [{string.Join(", ", progNamen.Select(x => $"{x.Key} x{x.Value}"))}]" : "") +
                          $"; Freigabe {(frei ? 1 : 0)}, Zaehler {pc}, Bausperre {KiBausperre(p)}; Gruppen mit Mitglied " +
                          $"{Enumerable.Range(0, 4).Count(g => _aiGruppen[p][g].Einheiten.Count > 0)}");
            snap++;
        }
        KiPruefAn = false;

        var leer2 = KiLeereWege.Where(w => w.p == p).ToList();
        int ab33 = leer2.Count(w => w.sx == 3 && w.sy == 3);
        var beispiel = leer2.FirstOrDefault(w => w.sx == 3 && w.sy == 3);
        if (eingriff.Length > 0) sb.AppendLine($"  {eingriff}");
        sb.AppendLine($"  Lauf: {uhr.ElapsedMilliseconds / 1000} s, Takt {_origTicks}; Abwehr/Bertha am Anfang {abwehr0}/{bertha0}");
        sb.AppendLine($"  Transportlogik: Freigabe ohne Ziel {KiFreigabeOhneZiel}, Rueckkehr wegen Bausperre {KiFreigabeSperrzeit}, " +
                      $"gesperrt mangels Wagen {KiFreigabeGesperrt}, Filter abgelehnt {KiRoutenfilterAbgelehnt}; " +
                      $"Wagen bestellt {RouteWagenBestellt}, zu arm {RouteZuArm}, Bausperre {RouteBauGesperrt}; Modus 20 gesetzt {KiModus20Gesetzt}");
        sb.AppendLine($"  Leere Wege P2: {leer2.Count}, davon ab (3,3) {ab33}" +
                      (ab33 > 0 ? $", z. B. {beispiel.weg}" : "") +
                      $"; Bertha in Modus 2 auf (3,3): {(berthaAuf33Modus2 ? $"ja, ab Takt {berthaAuf33Takt}" : "nie")}");
        foreach (var a in aufnahmen.Take(6)) sb.AppendLine($"  {a}");
        sb.AppendLine($"  Leere Wege je Spieler: {string.Join(" ", KiLeereWege.GroupBy(w => w.p).Select(g => $"P{g.Key}:{g.Count()}"))}; " +
                      $"Gruppen gebildet {GruppenProtokoll.Count}; Auftragswahl {AmGruendeZeile()}");
        sb.AppendLine($"  Kartentransporter (nicht aus build_in_base) {kartenWagen.Count}, davon je in Modus 1/2 {qKarte.Count} (nicht gewertet)");

        int n = transporter.Count;
        bool p2 = freiNull == 4 && pcNull == 4;
        bool p3 = programmMax == 0;
        bool p4 = transporter.All(t => t is >= 2 and <= 6);
        bool p5mit = mitglied == 0;
        bool? p5weg = berthaAuf33Modus2 ? ab33 > 0 : null;
        int q = qEinheiten.Count;
        bool p6 = q == 0;
        string urteil;
        if (n < 4) urteil = $"KEIN URTEIL (nur {n}/4 Momentaufnahmen)";
        else if (!p2 || !p3 || !p5mit || p5weg == false || !p6)
            urteil = "DURCHGEFALLEN" + (mechanikFehler == 0 && p5mit && p5weg != false && p6 && transporter.Any(t => t > 6)
                ? " (P2/P3: Freigabe folgt der Mechanik in 4/4, aber P2 hat mehr Wagen als im Original)" : "");
        else if (p5weg == null) urteil = "KEIN URTEIL P5 (Bertha stand nie in Modus 2 auf (3,3))";
        else urteil = "BESTANDEN";
        if (KiModus20Aus && p6 && urteil == "BESTANDEN") urteil = "KEIN URTEIL P6 (kein KI-Transporter wurde Kandidat)";
        sb.Append($"k25-ki-check: P2 Abwehr {abwehr0}/14 Bertha {bertha0}/1, frei 0 in {freiNull}/4, pc0 in {pcNull}/4, " +
                  $"Programm {programmMax}, Transporter [{string.Join(",", transporter)}]{(p4 ? "" : " (ausserhalb 2..6, nur gemeldet)")}, " +
                  $"Gruppen mit Mitglied {mitglied}{(mitgliedTakt >= 0 ? $" (ab Takt {mitgliedTakt})" : "")}, " +
                  $"leere Wege {leer2.Count} (Start (3,3) {ab33}), Freigabe gegen Mechanik {4 - mechanikFehler}/4, Transporter in Modus 1/2 {q}{(qTakt >= 0 ? $" (ab Takt {qTakt})" : "")} — {urteil}");
        return sb.ToString();
    }
}
