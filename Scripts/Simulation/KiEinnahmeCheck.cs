namespace AkteEuropaReborn.Rendering;

using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>
/// <c>--ki-einnahme-check[=sekunden]</c> (02.10.2026, bug-384) — der Prüfstand zu
/// den drei Bauten aus berichte/ki-tuergriff-selbstverteidiger-fable.md §9:
/// <b>A</b> Türgriff (Art 1, <c>c == 0</c> = Einnahmeziel), <b>B</b> Betriebsart 5
/// (<c>0x4BDCC0</c>) samt dem Setzer <c>ai_mode</c> @0x4D1050, <b>C</b> die
/// Türmeidung im Modus-1-Arm — und <b>D</b>, UNSERE Setzung für Mission 26.
///
/// <para>Er MISST MIT ANKUNFT (Arbeitsweise: wer eine Wirkung misst, misst die
/// Ankunft mit): nicht »ein Befehl wurde gegeben«, sondern Gruppe gebildet →
/// Einheit STEHT auf der Einnahmezelle → Einnahme läuft mit dem richtigen
/// Eindringling → Besitzer gewechselt → (K26) die Siegregel feuert.</para>
///
/// <list type="bullet">
///   <item><b>K26</b>: P3 nimmt Myre (Platz 13) ein. Soll: Fahrt auf (12,53)
///   ≤ 10 s, Ankunft ≤ 120 s, Einnahme mit Eindringling 3, <c>obj_owner(13) == 3</c>
///   ≤ 200 s, dann Siegregel. Nullmodelle <c>--m26-tuergriff-aus</c> (kein Ziel)
///   und <c>--ki-tuergriff-aus</c> (Angriff statt Einnahme) MÜSSEN durchfallen.</item>
///   <item><b>K13</b>: P4 nimmt ein statt anzugreifen. ⚠ EINGRIFF: die fünf Ziele
///   der Regel 0x49CDCC hängen an <c>v8 == 2 &amp;&amp; obj_owner(0) == 4</c>; kommt
///   die Regel binnen 5 s nicht, trägt der Prüfstand sie so ein, wie sie es täte.</item>
///   <item><b>K20</b>: Betriebsart 5. ⚠ EINGRIFF: <c>v50 := 2</c> (sonst erst nach
///   gut zehn Minuten), dann feuert Regel 0x49F484 selbst: vier Angriffsziele und
///   <c>ai_mode(6, 5)</c>. Soll: Betriebsart 5, alle P6-Fahrzeuge Modus 10,
///   Befehle &gt; 0. Nullmodell <c>--ki-betriebsart5-aus</c>.</item>
///   <item>Jede Mission: die Türmeidung wird gezählt; mit
///   <c>--ki-tuermeidung-aus</c> muss die Zahl 0 sein.</item>
/// </list>
/// </summary>
public partial class MapEntityLayer
{
    /// <summary><c>--ki-einnahme-ohne-schutz</c> — der K26-Lauf ohne den
    /// Eingriff »der Mensch hält die Tür frei«.</summary>
    public static bool KiEinnahmeOhneSchutz;

    public string KiEinnahmeCheck(int sekunden)
    {
        int m = UI.SkirmishSetup.CampaignMission;
        var sb = new System.Text.StringBuilder($"ki-einnahme-check K{m}, {sekunden} s\n");
        sb.AppendLine($"  Schalter: --ki-tuergriff-aus {TuergriffAus}, --ki-betriebsart5-aus {Betriebsart5Aus}, " +
                      $"--ki-tuermeidung-aus {TuermeidungAus}, " +
                      $"--m26-tuergriff-aus {Campaign.MissionScript.SetzungAus.Contains("--m26-tuergriff-aus")}, " +
                      $"--ki-sec110-alt {Sec110Alt}, --ki-gruppengroesse-alt {GruppengroesseAlt}, " +
                      $"--ki-zielgruppe-alt {ZielgruppeAlt}, --ki-einnahme-ohne-schutz {KiEinnahmeOhneSchutz}");
        GruppenProtokoll.Clear();
        FreieZweigB = ZielMitGruppeUebersprungen = 0;
        if (!InCampaign) return sb.Append("  keine Kampagne — ungeprueft\n  DURCHGEFALLEN").ToString();

        // das Skript anlaufen lassen (Setup, Betriebsart, erste Regeln)
        MissionScriptTick(0.001f);
        var bas = string.Join(" ", Enumerable.Range(0, 8).Select(p => $"P{p}={AiBetriebsart(p)}"));
        sb.AppendLine($"  Betriebsart nach dem Setup: {bas}");

        int takte = (int)(sekunden * SimHz);
        int eingriffTakt = 5 * SimHz;
        string eingriff = "";
        // je (Spieler, Gebaeudeindex) eines Tuergriff-Ziels die Zeitpunkte
        var tFahrt = new Dictionary<(int, int), float>();
        var nFahrt = new Dictionary<(int, int), int>();
        var tAnkunft = new Dictionary<(int, int), float>();
        var tLaeuft = new Dictionary<(int, int), float>();
        var tBesitz = new Dictionary<(int, int), float>();
        var angriffe = new Dictionary<(int, int), int>();
        float tSieg = -1;
        int ba5Max = 0, ba5Spieler = -1, ba5Einheiten = 0;
        var startOwner = new Dictionary<int, int>();
        var gesehen = new HashSet<(int, int)>();
        int schutzEntfernt = 0;
        bool k13Ki = false;
        int ba5Soll = m == 20 ? 6 : -1;

        for (int t = 0; t < takte; t++)
        {
            SimTickFuerProbe();
            float s = (float)t / SimHz;

            if (t == eingriffTakt && _mscript != null)
            {
                if (m == 13)
                {
                    // ⚠ P4 kommt in M13 erst spaet: Regel 0x49CCFB (v101 == 10 und
                    // zehn Minuten nach v7) setzt 15 Einheiten per space_in auf
                    // (19,115) ab — das ist die Einnahmezelle von Figueres Platz 0.
                    // Steht P4 dort, nimmt er ein, 0x49CD9E/0x49CDCC tragen dann die
                    // fuenf Tuergriff-Ziele ein. Der Pruefstand zieht nur den
                    // Ausloeser vor — und traegt P4 in die KI-Liste nach, weil
                    // StartCampaign nur die Spieler nimmt, die beim START da sind
                    // (Nebenbefund, NICHT behoben).
                    _mscript.SetVarFuerProbe(101, 10);
                    _mscript.SetVarFuerProbe(7, -100);
                    // P4 bekommt die KI ERST, wenn die Ziele da sind — sonst holt die
                    // Zuweisung den Transporter (Platz 4000) sofort von der
                    // Einnahmezelle, und die Tuermeidung tut ein Uebriges.
                    // ... und der Mensch ist nicht da: seine Einheiten um die
                    // Landestelle (19,115) wuerden die Verstaerkung sonst in
                    // Sekunden zusammenschiessen (gemessen: 15 von 15 tot).
                    // ⚠ Ganz ohne Einheiten verliert P0 sofort (Endregel 0x49D1AD
                    // units(0) == 0) und das Skript steht — darum zuerst ein
                    // Platzhalter weit weg in der Kartenecke.
                    int platzhalter = SpawnReinforcement(95, 2, 2, 0);
                    int weg13 = 0;
                    for (int i = 0; i < _entities.Count; i++)
                    {
                        var e = _entities[i];
                        if (e.IsBuilding || e.IsProp || e.Dead || e.Owner != 0) continue;
                        if (System.Math.Max(System.Math.Abs(e.Col - 19), System.Math.Abs(e.Row - 115)) > 25) continue;
                        if (i == platzhalter) continue;
                        Kill(i, e, -1, "Pruefstand: der Mensch ist nicht da");
                        weg13++;
                    }
                    eingriff = "EINGRIFF: v101 := 10, v7 := -100 (Regel 0x49CCFB feuert jetzt statt " +
                               "nach zehn Minuten)" + 
                               $", {weg13} Einheiten des Spielers 0 im Umkreis 25 von (19,115) entfernt, " +
                               $"Platzhalter Platz {(platzhalter >= 0 ? _entities[platzhalter].Slot : -1)} bei (2,2)";
                }
                if (m == 20 && _mscript.Var(50) < 2)
                {
                    _mscript.SetVarFuerProbe(50, 2);
                    eingriff = "EINGRIFF: v50 := 2 (Regel 0x49F484 feuert dann selbst)";
                }
                if (m is 18 or 28)
                {
                    // ⚠ Die Skriptziele der M28 gelten P1, und P1 ist dort kein
                    // Rechnerspieler; M18 hat gar kein add_target. Beide stellen aber
                    // Spieler schon im SETUP auf Betriebsart 5 (M18 @0x48BC49..61:
                    // P1/P2/P4, M28 @0x490D68..8C: P1/P4/P6). EINGRIFF: der erste
                    // Rechnerspieler in Betriebsart 5 mit Fahrzeugen bekommt ein
                    // Tuergriff-Ziel auf das naechste feindliche Gebaeude mit Tuer —
                    // dann muss 0x4BDCC0 ALLE seine Fahrzeuge auf Modus 10 setzen und
                    // zur Einnahmezelle fahren.
                    eingriff = "EINGRIFF nicht moeglich: kein Rechnerspieler in Betriebsart 5 mit Fahrzeugen und Ziel";
                    foreach (var ap in _ai)
                    {
                        int q = ap.Player;
                        if (AiBetriebsart(q) != 5) continue;
                        var eig = _entities.Where(e => !e.IsBuilding && !e.IsProp && !e.Dead && e.Owner == q && e.Infantry < 0).ToList();
                        if (eig.Count == 0) continue;
                        int best = -1; float bd = float.MaxValue;
                        for (int i = 0; i < _entities.Count; i++)
                        {
                            var b = _entities[i];
                            if (!b.IsBuilding || b.Dead || b.IsProp || b.NoStructure || b.Doors == 0 || b.Built == 0) continue;
                            if (b.Owner is < 0 or > 7 || !AiHostile(q, b.Owner)) continue;
                            float d = eig.Min(u => new Vector2(u.Col - b.Col, u.Row - b.Row).Length());
                            if (d < bd) { bd = d; best = i; }
                        }
                        if (best < 0) continue;
                        AddMissionTarget(q, 1, 9, _entities[best].Slot, 0);
                        ba5Soll = q;
                        eingriff = $"EINGRIFF: add_target({q}, 1, 9, {_entities[best].Slot}, 0) auf " +
                                   $"{_entities[best].Name} (Spieler {_entities[best].Owner}), {eig.Count} Fahrzeuge, " +
                                   $"P{q} in Betriebsart {AiBetriebsart(q)}";
                        break;
                    }
                }
            }

            if (_mscript != null && tSieg < 0 && (_mscript.Grace >= 0 || (_mscript.Ended && _mscript.Success)))
                tSieg = s;

            if (t % 25 != 0) continue;
            if (m == 13 && t % (20 * SimHz) == 0 && _mscript != null)
                GD.Print($"k13-spur {s:0} s: v8={_mscript.Var(8)} obj_owner(0)={_mscript.ObjOwner?.Invoke(0)} " +
                         $"Ziele P4={MissionTargetsOf(4)} Regeln={_mscript.RulesFired} Ende={_mscript.Ended}");
            if (m == 13 && t > eingriffTakt && !k13Ki && MissionTargetsOf(4) > 0)
            {
                k13Ki = KiSpielerNachtragenFuerProbe(4);
                eingriff += $"; P4 bei {s:0.0} s (Ziele da) in die KI-Liste nachgetragen";
            }

            // ⚠ EINGRIFF K26 (Schalter --ki-einnahme-ohne-schutz nimmt ihn zurueck):
            // sobald die Einnahme laeuft, feuert R3 und der Feind bekommt 3×17
            // Einheiten an Myre (R8/R9, space_in). Ohne den Menschen, der die Tuer
            // frei haelt, toetet das jeden Belagerer (gemessen: 39 tote P3-Einheiten).
            // Der Pruefstand spielt den Menschen: Einheiten des Spielers 1 im
            // Umkreis 12 der Einnahmezelle werden entfernt.
            if (m == 26 && !KiEinnahmeOhneSchutz && tLaeuft.Keys.Any(x => x.Item1 == 3))
            {
                var myre = _entities[tLaeuft.Keys.First(x => x.Item1 == 3).Item2];
                var fz = CaptureCells(myre).Front;
                for (int i = 0; i < _entities.Count; i++)
                {
                    var e = _entities[i];
                    if (e.IsBuilding || e.IsProp || e.Dead || e.Owner != 1) continue;
                    if (System.Math.Max(System.Math.Abs(e.Col - fz.X), System.Math.Abs(e.Row - fz.Y)) > 12) continue;
                    Kill(i, e, -1, "Pruefstand: der Mensch haelt die Tuer frei");
                    schutzEntfernt++;
                }
            }
            for (int p = 0; p < 8; p++)
            {
                if (AiBetriebsart(p) == 5 && Ba5Modus10 > ba5Max) { ba5Max = Ba5Modus10; ba5Spieler = p; }
                foreach (var ziel in _missionTargets[p])
                {
                    if (ziel.Kind != 1 || ziel.Second != 0) continue;
                    int bi = ResolveTarget(p, ziel);
                    if (bi < 0) continue;
                    var b = _entities[bi];
                    var key = (p, bi);
                    gesehen.Add(key);
                    if (!startOwner.ContainsKey(bi)) startOwner[bi] = b.Owner;
                    var front = CaptureCells(b).Front;
                    int fahren = 0, angriff = 0;
                    bool steht = false;
                    foreach (var e in _entities)
                    {
                        if (e.IsBuilding || e.IsProp || e.Dead || e.Owner != p) continue;
                        if (e.Path != null && System.Math.Abs(e.Goal.X - front.X) <= 2 &&
                            System.Math.Abs(e.Goal.Y - front.Y) <= 2) fahren++;
                        if ((e.Col, e.Row) == (front.X, front.Y)) steht = true;
                        if (e.Target == bi) angriff++;
                    }
                    if (fahren > 0 && !tFahrt.ContainsKey(key)) { tFahrt[key] = s; nFahrt[key] = fahren; }
                    if (fahren > nFahrt.GetValueOrDefault(key)) nFahrt[key] = fahren;
                    if (steht && !tAnkunft.ContainsKey(key)) tAnkunft[key] = s;
                    if (b.CaptureProgress > 0 && b.Intruder == p && !tLaeuft.ContainsKey(key)) tLaeuft[key] = s;
                    if (b.Owner == p && startOwner[bi] != p && !tBesitz.ContainsKey(key)) tBesitz[key] = s;
                    angriffe[key] = System.Math.Max(angriffe.GetValueOrDefault(key), angriff);
                }
            }
        }
        int ba5Jetzt10 = 0;
        if (ba5Spieler >= 0)
        {
            ba5Einheiten = _entities.Count(e => !e.IsBuilding && !e.IsProp && !e.Dead &&
                                                 e.Owner == ba5Spieler && e.Infantry < 0);
            ba5Jetzt10 = _entities.Count(e => !e.IsBuilding && !e.IsProp && !e.Dead &&
                                               e.Owner == ba5Spieler && e.Infantry < 0 && e.AiCpu0 == 10);
        }

        if (eingriff.Length > 0) sb.AppendLine("  ⚠ " + eingriff);
        if (m == 26)
            sb.AppendLine(KiEinnahmeOhneSchutz
                ? "  --ki-einnahme-ohne-schutz: kein Eingriff, P3 steht allein gegen die Verstaerkung"
                : $"  ⚠ EINGRIFF: der Pruefstand haelt die Tuer frei — {schutzEntfernt} Einheiten des Spielers 1 " +
                  "im Umkreis 12 der Einnahmezelle entfernt, sobald die Einnahme lief");
        var keys = gesehen.ToList();
        foreach (var key in keys.OrderBy(k => tFahrt.GetValueOrDefault(k, 9999)))
        {
            var b = _entities[key.Item2];
            string T(Dictionary<(int, int), float> d) => d.TryGetValue(key, out var v) ? $"{v:0.0} s" : "nie";
            sb.AppendLine($"  P{key.Item1} -> {b.Name} Platz {b.Slot} ({b.Col},{b.Row}), Einnahmezelle " +
                          $"{CaptureCells(b).Front}, Besitzer {startOwner.GetValueOrDefault(key.Item2)} -> {b.Owner}: " +
                          $"Fahrt {T(tFahrt)} ({nFahrt.GetValueOrDefault(key)} Einh.), Ankunft {T(tAnkunft)}, " +
                          $"Einnahme laeuft {T(tLaeuft)}, Besitz {T(tBesitz)}, " +
                          $"Angreifer hoechstens {angriffe.GetValueOrDefault(key)}");
        }
        if (keys.Count == 0) sb.AppendLine("  kein Tuergriff-Ziel (Art 1, c == 0) auf der Karte");

        sb.AppendLine($"  Zaehler: Tuergriff-Gruppen {TuergriffGruppen}, Fahrten {TuergriffFahrten}, " +
                      $"erledigt {TuergriffErledigt}, Zielwahl eigen/verbuendet uebersprungen {ZielwahlVerbuendetUebersprungen}");
        sb.AppendLine($"  Betriebsart 5: Rufe ai_mode {BetriebsartRufe}, Runden {Ba5Runden}, Befehle {Ba5Befehle}, " +
                      $"zuletzt Modus 10 {Ba5Modus10}" + (ba5Spieler >= 0 ? $" (P{ba5Spieler}, {ba5Einheiten} Fahrzeuge)" : ""));
        sb.AppendLine($"  Ausstiege AiMissionAttack: {AmGruendeZeile()}");
        sb.AppendLine($"  Tuermeidung: {TuermeidungTreffer} Treffer im 4x4-Fenster, {TuermeidungFahrten} weggeschickt");
        sb.AppendLine($"  Setzungsregeln: {Campaign.MissionScript.SetzungenGeladen} geladen, " +
                      $"{Campaign.MissionScript.SetzungenAusgelassen} ausgelassen; Sieg {(tSieg >= 0 ? $"{tSieg:0.0} s" : "nie")}");
        if (TuermeidungAus && TuermeidungTreffer != 0) sb.AppendLine("  ⚠ Tuermeidung trotz Schalter gezaehlt");
        // ⭐ 02.10.2026 (bug-405) — Takt 8: Gruppengroessen (Soll aus 0x4BC920, Ist =
        // aufgenommene Mitglieder), Zweig B von 0x4BE790, Marken 0x4BED60.
        sb.AppendLine($"  Takt 8 (bug-405): Gruppen gebildet {GruppenProtokoll.Count}" +
                      (GruppenProtokoll.Count > 0 ? $" [{string.Join("; ", GruppenProtokoll.Take(12))}" +
                                                    (GruppenProtokoll.Count > 12 ? "; …]" : "]") : "") +
                      $", Zweig B (sec110 == 0) lieferte {FreieZweigB}×, Ziel mit Gruppe uebersprungen {ZielMitGruppeUebersprungen}×");

        bool ok;
        switch (m)
        {
            case 26:
            {
                var k = keys.FirstOrDefault(x => x.Item1 == 3 && _entities[x.Item2].Slot == 13);
                bool da = keys.Contains(k) && k != default;
                // ⚠ Die Sollzeiten des Berichts (Fahrt <= 10 s) setzten freie
                // Angreifer voraus. Gemessen: in Betriebsart 2 (Setup @0x48FFA9)
                // hat P3 rund 300 s lang KEINEN freien Angreifer (DEF_robots =
                // 2·imp je Sektor), erst dann bildet 0x4BECF0 eine Gruppe — das
                // ist das Tor des Originals. Soll darum relativ zur Fahrt:
                // 108 Zellen <= 120 s, 1200 TP = 24 s Einnahme (+ Puffer 36 s).
                float tf = da ? tFahrt.GetValueOrDefault(k, -1) : -1;
                bool f = tf >= 0;
                bool an = f && tAnkunft.TryGetValue(k, out var ta) && ta - tf <= 120;
                bool la = da && tLaeuft.ContainsKey(k);
                bool be = an && tBesitz.TryGetValue(k, out var tb) && tb - tAnkunft[k] <= 60;
                bool si = tSieg >= 0 && be && tSieg >= tBesitz[k];
                ok = f && an && la && be && si;
                // ⭐ bug-405: gehalten = Myre gehoert am Ende des Laufs noch P3 (die
                // 3×17-Verstaerkung R8/R9 kommt mit der Einnahme). Nur Anzeige.
                if (da)
                {
                    var my = _entities[k.Item2];
                    sb.AppendLine($"  K26-Ende: Myre Besitzer {my.Owner} (gehalten {my.Owner == 3 && be}), " +
                                  $"P3 Einheiten lebend {_entities.Count(e => !e.Dead && e.Owner == 3 && !e.IsBuilding && !e.IsProp)}, " +
                                  $"tot {_entities.Count(e => e.Dead && e.Owner == 3 && !e.IsBuilding)}, " +
                                  $"P1 Einheiten lebend {_entities.Count(e => !e.Dead && e.Owner == 1 && !e.IsBuilding && !e.IsProp)}");
                }
                sb.AppendLine($"  K26-Soll: Fahrt {f}{(f ? $" ({tf:0} s)" : "")}, Ankunft <=120 s danach {an}, " +
                              $"Einnahme mit Eindringling 3 {la}, obj_owner(13)==3 <=60 s nach Ankunft {be}, " +
                              $"Siegregel danach {si}");
                if (da && !be)
                {
                    var tote = _entities.Count(e => e.Dead && e.Owner == 3 && !e.IsBuilding);
                    sb.AppendLine($"  nicht hergestellt? P3 tote Einheiten {tote}, Myre TP {_entities[k.Item2].Hp}/{_entities[k.Item2].HpMax}, " +
                                  $"Fortschritt {_entities[k.Item2].CaptureProgress}/{_entities[k.Item2].CaptureTotal}");
                }
                break;
            }
            case 13:
            {
                var b0 = _entities.FirstOrDefault(x => x.IsBuilding && x.Slot == 0 && !x.Dead);
                var u0 = _entities.FirstOrDefault(x => !x.IsBuilding && x.Slot == 4000);
                if (b0 != null)
                    sb.AppendLine($"  Platz 0 {b0.Name}: Besitzer {b0.Owner}, Einnahme {b0.CaptureProgress}/{b0.CaptureTotal} " +
                                  $"Eindringling {b0.Intruder}, Tuer {CaptureCells(b0).Door} Einnahmezelle {CaptureCells(b0).Front}; " +
                                  (u0 == null ? "Platz 4000 fehlt" :
                                   $"Platz 4000 {u0.Name} auf ({u0.Col},{u0.Row}) tot={u0.Dead} Ukol={u0.Ukol} CPU0={u0.AiCpu0}") +
                                  $"; v8={_mscript?.Var(8)}");
                if (_mscript != null && MissionTargetsOf(4) == 0)
                    foreach (var z in _mscript.RuleCheck().Split((char)10))
                        if (z.Contains("49CDCC") || z.Contains("49CD9E")) sb.AppendLine("  Regel: " + z.Trim());
                bool la = keys.Any(x => x.Item1 == 4 && (tLaeuft.ContainsKey(x) || tBesitz.ContainsKey(x)));
                bool an = keys.Any(x => x.Item1 == 4 && tAnkunft.ContainsKey(x));
                ok = TuergriffGruppen > 0 && an && la;
                sb.AppendLine($"  K13-Soll: Tuergriff-Gruppe {TuergriffGruppen > 0}, Ankunft {an}, Einnahme laeuft/fertig {la}");
                break;
            }
            case 18:
            case 20:
            case 28:
            {
                // »alle Plaetze«: hoechstens zwei, die seit der letzten KI-Runde
                // gebaut wurden, duerfen noch fehlen
                int soll = ba5Soll;
                ok = ba5Spieler == soll && Ba5Runden > 0 && Ba5Befehle > 0 && Ba5Modus10 > 0 &&
                     ba5Jetzt10 >= ba5Einheiten - 2;
                sb.AppendLine($"  K{m}-Soll: P{soll} Betriebsart {AiBetriebsart(soll)}, BA5-Runden {Ba5Runden}, " +
                              $"Modus 10 jetzt {ba5Jetzt10}/{ba5Einheiten} Fahrzeuge (letzte Runde {Ba5Modus10}), " +
                              $"Befehle {Ba5Befehle}");
                break;
            }
            default:
                ok = !TuermeidungAus || TuermeidungTreffer == 0;
                sb.AppendLine("  (ohne Sollwerte fuer diese Mission — nur Zaehler)");
                break;
        }
        sb.AppendLine($"  {AiLine()}");
        return sb.Append(ok ? "  BESTANDEN" : "  DURCHGEFALLEN").ToString();
    }
}
