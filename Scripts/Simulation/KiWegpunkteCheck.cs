namespace AkteEuropaReborn.Rendering;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Godot;

/// <summary>
/// <c>--ki-wegpunkte-check[=takte]</c> (22.10.2026, Paket W, bug-621) — der Prüfstand
/// zu den Marschwegen der KI (Simulation/KiMarschwege.cs; Bericht
/// berichte/gruppen-wegpunkte-sec108-opus.md §6).
///
/// <para><b>Wo:</b> Kampagne 25 (»Chanel Tunnel«) ist die Karte von <c>3/5/6/7/10.DM</c>
/// — DM_6 und DM_10 sind Entwicklerspielstände DIESER Mission (Kopf +26 = 25). Ein
/// Gefecht hat keine Gruppen (die Gefechts-KI ist unsere eigene, AiFight), darum läuft
/// der Prüfstand in der Kampagne: <c>-- --campaign=25 --no-briefing --no-movie
/// --ki-wegpunkte-check</c>.</para>
///
/// <para><b>Teil A — die Tafeln gegen das Original.</b> Unsere zur Laufzeit gerechneten
/// Anker/Verbindungen gegen <c>AIxx.CWI</c> (falls vorhanden), und die Original-Marschwege
/// aus den <c>.DM</c> dieser Mission Schritt für Schritt gegen unsere Verbindungstafel
/// (der unabhängige Sollwert, Regel N: das Original selbst spricht mit).</para>
///
/// <para><b>Teil B — der Lauf.</b> P1 Form (nur (1,0)/(0,1), Platz 29 = Zielsektor),
/// P2 kein Angriffsbefehl solange Ende ≠ 0, P3 Weiterschalten genau bei 3·drin/2 &gt;
/// Grösse, P4 mindestens eine Gruppe mit ≥ 2 Wegpunkten erreicht Ende 0 (sonst KEIN
/// URTEIL), P5 Fahrziel jedes Wegpunktbefehls in Anker + [−4, +3]².</para>
///
/// <para>Nullmodelle: <c>--ki-wegpunkte-aus</c> (Angriff im Takt der Bildung, kein Weg →
/// DURCHGEFALLEN), <c>--ki-sektorweg-alt</c> (8 Nachbarn → schräge Schritte).</para>
/// </summary>
public partial class MapEntityLayer
{
    public static bool KiPruefAn;

    private sealed class KiPruefGruppe
    {
        public int P, G, Takt, Index, Laenge, Ende0Takt = -1, ErsterAngriffTakt = -1;
        public (int x, int y) Start, Ziel, Gewaehlt;
        public List<(int x, int y)> Weg = new();
        public int Schraeg, Schritte;
        public bool Platz29IstZiel, IndexNebenStart, Leer;
    }

    private readonly List<KiPruefGruppe> _kiPruefGruppen = new();
    private readonly Dictionary<(int gi, int index), bool> _kiPruefLetzter = new();
    private int _kiPruefWeiter, _kiPruefRegelOk, _kiPruefAngriffVorEnde, _kiPruefBefehle,
                _kiPruefBefehleImKasten, _kiPruefBefehleRingVoll, _kiPruefSofort, _kiPruefLeer, _kiPruefEinzelImMarsch;

    private KiPruefGruppe? KiPruefAktuell(int p, int g)
    {
        for (int i = _kiPruefGruppen.Count - 1; i >= 0; i--)
            if (_kiPruefGruppen[i].P == p && _kiPruefGruppen[i].G == g) return _kiPruefGruppen[i];
        return null;
    }

    /// <summary>Aus <see cref="KiWegSchreiben"/>: der geschriebene Weg (vom Ziel
    /// rückwärts) und was gewählt war.</summary>
    private void KiWegProtokollieren(int p, int g, int sx, int sy, int zx, int zy, List<(int, int)> rueckwaerts)
    {
        int b = 62 * (4 * p + g);
        string text = $"Weg P{p} g{g}: Start ({sx},{sy}) Ziel ({zx},{zy}) gewaehlt {_kiZielBest} -> " +
                      $"Index {_kiSec108[b]}, Ende {_kiSec108[b + 1]}, " +
                      (rueckwaerts.Count == 0 ? "kein Wegpunkt" : string.Join(" ", Enumerable.Reverse(rueckwaerts).Select(w => $"({w.Item1},{w.Item2})")));
        if (KiWegLog.Count < 200) KiWegLog.Add($"Takt {_taktNr}: {text}");
        if (!KiPruefAn) return;
        // Paket W3: ein neuer Weg im selben Gruppenplatz beginnt ohne Vorgeschichte —
        // sonst zaehlte P3 ein Weiterschalten gegen den Stand der VORIGEN Gruppe.
        foreach (var key in _kiPruefLetzter.Keys.Where(x => x.gi == 4 * p + g).ToList()) _kiPruefLetzter.Remove(key);
        var r = new KiPruefGruppe
        {
            P = p, G = g, Takt = _taktNr, Index = _kiSec108[b], Start = (sx, sy), Ziel = (zx, zy),
            Gewaehlt = _kiZielBest, Leer = _aiGruppen[p][g].Einheiten.Count == 0,
        };
        // der Weg in Fahrtrichtung: Platz Index .. 29
        if (_kiSec108[b + 1] != 0)
            for (int k = _kiSec108[b]; k <= 29; k++) r.Weg.Add(KiWegpunkt(p, g, k));
        r.Laenge = r.Weg.Count;
        for (int i = 0; i + 1 < r.Weg.Count; i++)
        {
            int dx = Math.Abs(r.Weg[i + 1].x - r.Weg[i].x), dy = Math.Abs(r.Weg[i + 1].y - r.Weg[i].y);
            r.Schritte++;
            if (dx + dy != 1) r.Schraeg++;
        }
        if (r.Weg.Count > 0)
        {
            r.Platz29IstZiel = r.Weg[^1] == (zx, zy);
            var a = r.Weg[0];
            r.IndexNebenStart = Math.Abs(a.x - sx) + Math.Abs(a.y - sy) == 1;
        }
        if (r.Leer)                                // Weg einer leer gebliebenen Gruppe: nur gezaehlt
        {
            _kiPruefLeer++;
            if (KiLeereWege.Count < 400) KiLeereWege.Add((p, sx, sy, string.Join(" ", r.Weg.Select(w => $"({w.x},{w.y})"))));
            return;
        }
        _kiPruefGruppen.Add(r);
    }

    /// <summary>Paket W2 (<c>--k25-ki-check</c>): die Wege leer gebliebener Gruppen —
    /// Spieler, Startsektor, Weg in Fahrtrichtung.</summary>
    public static readonly List<(int p, int sx, int sy, string weg)> KiLeereWege = new();

    /// <summary>Protokoll der geschriebenen Wege (alle Läufe, für die Kopfzeile).</summary>
    public static readonly List<string> KiWegLog = new();

    private void KiPruefBefehl(int p, int g, int idx, int wx, int wy, int zx, int zy, bool ok)
    {
        _kiPruefBefehle++;
        if (!ok) _kiPruefBefehleRingVoll++;
        int ai = 2 * (11 * wx + wy);
        int axx = KiAnkerSpeicher(ai), ayy = KiAnkerSpeicher(ai + 1);
        int dx = (zx - axx + 256) % 256, dy = (zy - ayy + 256) % 256;   // Bytearithmetik
        bool im = (dx >= 252 || dx <= 3) && (dy >= 252 || dy <= 3);
        if (im) _kiPruefBefehleImKasten++;
    }

    private void KiPruefLauf(int p, int g, int index, int drin, int groesse, bool weiter)
    {
        var key = (4 * p + g, index);
        if (weiter)
        {
            _kiPruefWeiter++;
            bool regel = 3 * drin / 2 > groesse;
            bool vorher = _kiPruefLetzter.TryGetValue(key, out bool v) ? v : false;
            if (regel && !vorher) _kiPruefRegelOk++;
        }
        _kiPruefLetzter[key] = 3 * drin / 2 > groesse;
    }

    private void KiPruefEnde(int p, int g)
    {
        var r = KiPruefAktuell(p, g);
        if (r != null && r.Ende0Takt < 0) r.Ende0Takt = _taktNr;
    }

    private void KiPruefAngriff(int p, int g, int idx)
    {
        var r = KiPruefAktuell(p, g);
        if (r != null && r.ErsterAngriffTakt < 0)
        {
            r.ErsterAngriffTakt = _taktNr;
            if (r.Takt == _taktNr) _kiPruefSofort++;        // je Gruppe: Angriff im Takt der Bildung
        }
        if (KiEnde(p, g) != 0) _kiPruefAngriffVorEnde++;
    }

    private void KiPruefSend(int idx)
    {
        if (idx < 0 || idx >= _entities.Count) return;
        var e = _entities[idx];
        if (e.AiCpu0 != 10 || e.Owner is < 0 or > 7 || e.AiCpu1 is < 0 or > 3) return;
        if (!_aiGruppen[e.Owner][e.AiCpu1].Einheiten.Contains(idx)) return;
        // ⚠ Kein Gruppenangriff: das sind Einzelbefehle (ai_units 0x4BF4E0, AiSweep —
        // der Sichtring einer STEHENDEN Einheit), die das Original auch im Marsch gibt.
        if (KiEnde(e.Owner, e.AiCpu1) != 0) _kiPruefEinzelImMarsch++;
    }

    /// <summary>Die Original-Marschwege aus den <c>.DM</c> (sec108, gemessen 22.10.2026
    /// mit cwm_sections), je Mission — nur die, deren Schritte in der <c>AIxx.CWI</c>
    /// DERSELBEN Mission alle offen sind (36/36); die übrigen sind Reste anderer
    /// Missionen (sec108 wird beim Missionswechsel nicht geleert).</summary>
    private static readonly Dictionary<int, (string dm, (int x, int y)[] weg)[]> KiDmWege = new()
    {
        [25] = new[]
        {
            ("3/6/7.DM P2 g0", new[] { (4, 3), (5, 3), (5, 4), (5, 5), (5, 6), (5, 7), (4, 7) }),
            ("5.DM P2 g0", new[] { (3, 4), (3, 5), (2, 5), (2, 6), (2, 7), (3, 7), (4, 7) }),
            ("10.DM P2 g0", new[] { (3, 2), (4, 2), (5, 2), (5, 3), (5, 4), (5, 5), (5, 6), (5, 7), (4, 7) }),
        },
        [26] = new[] { ("1.DM P3 g0", new[] { (3, 1), (2, 1), (1, 1) }) },
        [21] = new[] { ("4.DM P1 g0", new[] { (1, 1), (0, 1), (0, 0) }) },
    };

    /// <summary>Die Originaltafeln <c>AIxx.CWI</c> (0x7D0 Brücken, 0x1E4 Anker,
    /// 0x1E4 Verbindungen; Lader C @0x41CCD7…0x41CD06) — oder null.</summary>
    private static byte[]? KiCwiLesen(int nummer)
    {
        string name = $"AI{nummer:00}.CWI";
        foreach (string ordner in new[]
                 {
                     ProjectSettings.GlobalizePath("res://Assets/Legacy/DATA"),
                     "C:/Program Files (x86)/Akte Europa/Data", "D:/DATA", "E:/DATA", "F:/Akte Europa/DATA",
                 })
        {
            try
            {
                string p = Path.Combine(ordner, name);
                if (File.Exists(p))
                {
                    var b = File.ReadAllBytes(p);
                    if (b.Length == 0x7D0 + 0x1E4 + 0x1E4) return b;
                }
            }
            catch { }
        }
        return null;
    }

    /// <summary>Teil A: unsere Tafeln gegen <c>AIxx.CWI</c>.</summary>
    public string KiTafelVergleich(int nummer)
    {
        KiTafelnSicher();
        var sb = new StringBuilder();
        var cwi = KiCwiLesen(nummer);
        if (cwi == null) return $"  A1 Tafeln: keine AI{nummer:00}.CWI — das Original rechnet diese Karte auch zur Laufzeit (kein Vergleich)\n";
        int ankGleich = 0, ankNah = 0, ankBeide = 0, nurUns = 0, nurOrig = 0;
        var ankAbw = new List<string>();
        for (int s = 0; s < SektorZahl; s++)
        {
            int ox = cwi[0x7D0 + 2 * s], oy = cwi[0x7D0 + 2 * s + 1];
            int ux = _kiAnker[2 * s], uy = _kiAnker[2 * s + 1];
            if (ox == 0xFF && ux == 0xFF) { ankGleich++; continue; }
            if (ox == 0xFF) { nurUns++; continue; }
            if (ux == 0xFF) { nurOrig++; continue; }
            ankBeide++;
            if (ox == ux && oy == uy) ankGleich++;
            else
            {
                if (Math.Max(Math.Abs(ox - ux), Math.Abs(oy - uy)) <= 3) ankNah++;
                if (ankAbw.Count < 12) ankAbw.Add($"({s / 11},{s % 11}) orig ({ox},{oy}) uns ({ux},{uy})");
            }
        }
        int vGleich = 0, vNurOrig = 0, vNurUns = 0, vOffenOrig = 0;
        var abw = new List<string>();
        for (int i = 0; i < 0xF2; i++)
        {
            int o = cwi[0x7D0 + 0x1E4 + i], u = _kiVerb[i];
            if (o != 0) vOffenOrig++;
            if ((o != 0) == (u != 0)) vGleich++;
            else
            {
                if (o != 0) vNurOrig++; else vNurUns++;
                if (abw.Count < 12) abw.Add($"({i / 2 / 11},{i / 2 % 11}){(i % 2 == 0 ? "->x+1" : "->y+1")} orig {o} uns {u}");
            }
        }
        int bruecken = 0;
        for (int i = 0; i < 400; i++) if (cwi[5 * i] != 0) bruecken++;
        sb.Append($"  A1 Anker gegen AI{nummer:00}.CWI: {ankGleich}/121 gleich, {ankNah} innerhalb 3 Zellen, " +
                  $"nur bei uns {nurUns}, nur im Original {nurOrig}\n");
        sb.Append($"  A2 Verbindungen: {vGleich}/242 gleich (Original offen {vOffenOrig}), nur Original offen {vNurOrig}, " +
                  $"nur bei uns offen {vNurUns}; Brueckensaetze im Original {bruecken} (bei uns nicht gefuehrt)\n");
        if (ankAbw.Count > 0) sb.Append($"     Anker anders: {string.Join("; ", ankAbw)}\n");
        if (abw.Count > 0) sb.Append($"     Abweichungen: {string.Join("; ", abw)}\n");
        return sb.ToString();
    }

    /// <summary>
    /// Der Prüfstand. Synchron wie <c>--ki-einnahme-check</c>.
    /// </summary>
    public string KiWegpunkteCheck(int takte)
    {
        int m = UI.SkirmishSetup.CampaignMission;
        var sb = new StringBuilder($"ki-wegpunkte-check K{m}, {takte} Takte\n");
        sb.AppendLine($"  Schalter: --ki-wegpunkte-aus {KiWegpunkteAus}, --ki-sektorweg-alt {KiSektorwegAlt}, " +
                      $"--ki-sektoranker-alt {KiSektorankerAlt}, --ki-wegziel-bester {KiWegzielBester}, " +
                      $"--ki-wegkasten-berichtigt {KiWegkastenBerichtigt}, --ki-wegende-berichtigt {KiWegendeBerichtigt}");
        if (!InCampaign)
            return sb.Append("  kein Kampagnenlauf — das Gefecht hat keine Gruppen (AiFight ist unsere KI)\n" +
                             "ki-wegpunkte-check: KEIN URTEIL (Gefecht)").ToString();

        KiPruefAn = true;
        _kiPruefGruppen.Clear(); _kiPruefLetzter.Clear(); KiWegLog.Clear();
        _kiPruefWeiter = _kiPruefRegelOk = _kiPruefAngriffVorEnde = _kiPruefBefehle = 0;
        _kiPruefBefehleImKasten = _kiPruefBefehleRingVoll = _kiPruefSofort = _kiPruefLeer = _kiPruefEinzelImMarsch = 0;

        // ---- Teil A ----
        sb.AppendLine($"  Tafeln: {KiTafelSuchen} Suchen in {KiTafelMs} ms");
        sb.Append(KiTafelVergleich(m));
        int dmSchritte = 0, dmOffen = 0;
        if (KiDmWege.TryGetValue(m, out var dmw))
            foreach (var (dm, weg) in dmw)
            {
                int o = 0;
                for (int i = 0; i + 1 < weg.Length; i++) if (KiSchrittOffen(weg[i], weg[i + 1])) o++;
                dmSchritte += weg.Length - 1; dmOffen += o;
                sb.AppendLine($"  A3 Originalweg {dm}: {o}/{weg.Length - 1} Schritte bei uns offen");
            }
        else sb.AppendLine("  A3 Originalwege: fuer diese Mission keine .DM mit eigenem Weg (nur 21, 25, 26)");

        // ---- Teil B ----
        MissionScriptTick(0.001f);
        string eingriff = "";
        int eingriffTakt = 5 * SimHz;
        for (int t = 0; t < takte; t++)
        {
            SimTickFuerProbe();
            if (t == eingriffTakt && _kiPruefGruppen.Count == 0 && GruppenProtokoll.Count == 0)
                eingriff = KiPruefEingriff();
        }
        KiPruefAn = false;

        int n = _kiPruefGruppen.Count;
        int mitWeg = _kiPruefGruppen.Count(r => r.Laenge > 0);
        int schritte = _kiPruefGruppen.Sum(r => r.Schritte), schraeg = _kiPruefGruppen.Sum(r => r.Schraeg);
        int p29 = _kiPruefGruppen.Count(r => r.Laenge > 0 && r.Platz29IstZiel);
        int neben = _kiPruefGruppen.Count(r => r.Laenge > 0 && r.IndexNebenStart);
        int ende = _kiPruefGruppen.Count(r => r.Laenge >= 2 && r.Ende0Takt >= 0);
        int a = _kiPruefAngriffVorEnde + _kiPruefSofort;
        if (eingriff.Length > 0) sb.AppendLine($"  {eingriff}");
        foreach (var r in _kiPruefGruppen.Take(12))
            sb.AppendLine($"  Gruppe P{r.P} g{r.G} Takt {r.Takt}: Start {r.Start} Zielsektor {r.Ziel} (gewaehlt {r.Gewaehlt}), " +
                          $"Index {r.Index}, Weg {(r.Weg.Count == 0 ? "—" : string.Join(" ", r.Weg))}, " +
                          $"Ende 0 bei {(r.Ende0Takt < 0 ? "—" : r.Ende0Takt.ToString())}, erster Angriff " +
                          $"{(r.ErsterAngriffTakt < 0 ? "—" : r.ErsterAngriffTakt.ToString())}");
        sb.AppendLine($"  P1 Form: Platz 29 = Zielsektor {p29}/{mitWeg}, Platz Index neben Start {neben}/{mitWeg}");
        sb.AppendLine($"  P5 Fahrbefehle {_kiPruefBefehle}, davon im Kasten Anker+[-4,+3] {_kiPruefBefehleImKasten}" +
                      (_kiPruefBefehleRingVoll > 0 ? $", Ring voll {_kiPruefBefehleRingVoll}" : ""));
        sb.AppendLine($"  Zaehler: Wegsuchen {KiWegsuchen}, Weiterschaltungen {KiWegWeiter}, Marschende {KiWegEnde}");
        sb.AppendLine($"  Auftragswahl: {AmGruendeZeile()}; Wege leer gebliebener Gruppen {_kiPruefLeer}; Einzelbefehle (Sichtring, AiSweep) an Mitglieder im Marsch {_kiPruefEinzelImMarsch}");
        if (GruppenProtokoll.Count > 0)
            sb.AppendLine($"  Gruppen gebildet: {string.Join("; ", GruppenProtokoll.Take(10))}");

        string urteil;
        bool form = schraeg == 0 && p29 == mitWeg && _kiPruefBefehleImKasten == _kiPruefBefehle;
        bool tafel = dmOffen == dmSchritte;
        if (n == 0) urteil = "KEIN URTEIL (keine Gruppe gebildet)";
        else if (!form || a > 0 || _kiPruefRegelOk != _kiPruefWeiter || !tafel) urteil = "DURCHGEFALLEN";
        else if (ende == 0) urteil = "KEIN URTEIL (keine Gruppe mit >= 2 Wegpunkten hat ihr Ende erreicht)";
        else urteil = "BESTANDEN";
        if (mitWeg == 0 && n > 0) urteil = "DURCHGEFALLEN (keine Gruppe mit Weg)";
        sb.Append($"ki-wegpunkte-check: Gruppen {n}, mit Weg {mitWeg} (Schritte {schritte}, schraeg {schraeg}), " +
                  $"Angriff vor Ende {a}, Weiterschalten {_kiPruefWeiter} (Regel ok {_kiPruefRegelOk}/{_kiPruefWeiter}), " +
                  $"Ende erreicht {ende}, Originalwege {dmOffen}/{dmSchritte} offen — {urteil}");
        return sb.ToString();
    }

    /// <summary>
    /// Für <c>--spielstand-check</c> (Paket W): eine Gruppe MITTEN IM MARSCH hinlegen,
    /// wenn keine da ist — sonst hätte der KI-Teil des Fingerabdrucks nichts zu
    /// verlieren. Erster Rechnerspieler mit ≥ 2 bewaffneten Einheiten: Ziel = das
    /// fernste feindliche Gebäude (add_target Art 1, Vorrang 9, c = 1), Gruppe aus bis
    /// zu drei Einheiten, Weg über <see cref="KiSektorWegsuche"/>/<see cref="KiWegSchreiben"/>.
    /// </summary>
    public string KiProbeMarschLegen()
    {
        for (int p = 0; p < 8; p++)
            for (int g = 0; g < 4; g++)
                if (_aiGruppen[p][g].Einheiten.Count > 0 && KiEnde(p, g) != 0)
                    return $"KI: Gruppe P{p} g{g} marschiert schon (Index {KiIndex(p, g)})";
        if (KiWegpunkteAus) return "KI: --ki-wegpunkte-aus, keine Marschgruppe gelegt";
        KiTafelnSicher();
        foreach (var ap in _ai)
        {
            int q = ap.Player;
            var eig = new List<int>();
            for (int i = 0; i < _entities.Count && eig.Count < 3; i++)
            {
                var e = _entities[i];
                if (e.IsBuilding || e.IsProp || e.Dead || e.Owner != q || !e.Mobile || !CanFight(e)) continue;
                eig.Add(i);
            }
            if (eig.Count < 2) continue;
            var u0 = _entities[eig[0]];
            var (sx, sy) = AiSektorVon(u0.Col, u0.Row);
            AiStaerkeraster(q);
            KiSektorWegsuche(q, sx, sy);
            int best = -1, bd = -1;
            for (int i = 0; i < _entities.Count; i++)
            {
                var b = _entities[i];
                if (!b.IsBuilding || b.Dead || b.IsProp || b.NoStructure || b.Slot < 0) continue;
                if (b.Owner is < 0 or > 7 || !AiHostile(q, b.Owner)) continue;
                var (bx, by) = AiSektorVon(b.Col, b.Row);
                if (_kiKosten[11 * bx + by] >= 999999) continue;      // ueber die Verbindungen unerreichbar
                int d = Math.Abs(bx - sx) + Math.Abs(by - sy);
                if (d > bd) { bd = d; best = i; }
            }
            if (best < 0 || bd < 2) continue;
            int g = -1;
            for (int k = 0; k < 4; k++) if (_aiGruppen[q][k].Einheiten.Count == 0) { g = k; break; }
            if (g < 0) continue;
            AddMissionTarget(q, 1, 9, _entities[best].Slot, 1);
            var gr = _aiGruppen[q][g];
            gr.Auftrag = _missionTargets[q][^1];
            foreach (int i in eig) { gr.Einheiten.Add(i); _entities[i].AiCpu0 = 10; _entities[i].AiCpu1 = g; }
            var (tx, ty) = AiSektorVon(_entities[best].Col, _entities[best].Row);
            KiWegSchreiben(q, g, sx, sy, tx, ty);
            gr.Losgeschickt = KiEnde(q, g) == 0;
            return $"KI: Gruppe P{q} g{g} mit {eig.Count} Einheiten gelegt, Weg ({sx},{sy}) -> ({tx},{ty}), " +
                   $"Index {KiIndex(q, g)}, Ende {KiEnde(q, g)}";
        }
        return "KI: keine Marschgruppe moeglich (kein Rechnerspieler mit Einheiten und fernem Ziel)";
    }

    /// <summary>Für den Prüfstand: warum eine Gruppe leer blieb (einmal je Spieler).
    /// Zählt die Einheiten nach CPU0 und, für CPU0 ∈ {1,2}, ob ihr CPU1-Sektor die
    /// Aufnahmeregel <c>DEF(+7) &lt; belegt(+0xA)</c> (C @0x4BCB25) erfüllt.</summary>
    private string KiDiagnoseKeineGruppe(int p)
    {
        var r = _aiRaster[p];
        var cpu = new SortedDictionary<int, int>();
        int kandidat = 0, waffe = 0, sektorOk = 0;
        var sek = new SortedDictionary<string, int>();
        foreach (var e in _entities)
        {
            if (e.IsBuilding || e.IsProp || e.Dead || e.Owner != p || !e.Mobile) continue;
            cpu[e.AiCpu0] = cpu.GetValueOrDefault(e.AiCpu0) + 1;
            if (e.AiCpu0 is not (1 or 2)) continue;
            kandidat++;
            if (CanFight(e)) waffe++;
            int s = AiSektorAus(e.AiCpu1);
            if (r[s].Def < r[s].Belegt) sektorOk++;
            string k = $"({s / 11},{s % 11}) def {r[s].Def} belegt {r[s].Belegt}";
            sek[k] = sek.GetValueOrDefault(k) + 1;
        }
        return $"P{p} keine Gruppe: CPU0 {string.Join(" ", cpu.Select(x => $"{x.Key}:{x.Value}"))}; " +
               $"Modus 1/2 {kandidat}, davon bewaffnet {waffe}, Sektor DEF<belegt {sektorOk}; Sektoren " +
               string.Join("; ", sek.Select(x => $"{x.Key} x{x.Value}"));
    }

    private readonly HashSet<int> _kiDiagnoseGedruckt = new();

    /// <summary>⚠ EINGRIFF (nur wenn binnen 5 s keine Gruppe entstand): jeder
    /// Rechnerspieler mit mindestens drei bewaffneten Einheiten bekommt bis zu fünf
    /// Angriffsziele (<c>add_target(p, 1, 9, Platz, 1)</c>) auf feindliche Gebäude, das
    /// FERNSTE zuerst (bei gleichem po gewinnt im Original der erste Platz).</summary>
    private string KiPruefEingriff()
    {
        var teile = new List<string>();
        foreach (var ap in _ai)
        {
            int q = ap.Player;
            if (MissionTargetsOf(q) > 0) continue;
            var eig = _entities.Where(e => !e.IsBuilding && !e.IsProp && !e.Dead && e.Owner == q && CanFight(e)).ToList();
            if (eig.Count < 3) continue;
            float mx = (float)eig.Average(e => e.Col), my = (float)eig.Average(e => e.Row);
            var ziele = new List<(int i, float d)>();
            for (int i = 0; i < _entities.Count; i++)
            {
                var b = _entities[i];
                if (!b.IsBuilding || b.Dead || b.IsProp || b.NoStructure || b.Slot < 0) continue;
                if (b.Owner is < 0 or > 7 || !AiHostile(q, b.Owner)) continue;
                ziele.Add((i, new Vector2(b.Col - mx, b.Row - my).Length()));
            }
            foreach (var (i, d) in ziele.OrderByDescending(z => z.d).Take(5))
                AddMissionTarget(q, 1, 9, _entities[i].Slot, 1);
            // ⚠ Und Betriebsart 4 (pro_style 400): mit 2/3 haelt ein Gebaeude (imp 6)
            // 12 bzw. 6 Verteidiger im Sektor, und ohne Ueberschuss gibt es keine
            // freien Angreifer (»Not free attacker«) — die Gruppe entstuende nie.
            int alt = AiBetriebsart(q);
            if (alt is 1 or 2 or 3) SetAiBetriebsart(q, 4);
            if (ziele.Count > 0) teile.Add($"P{q}: {Math.Min(5, ziele.Count)} Ziele ({eig.Count} bewaffnete), Betriebsart {alt} -> {AiBetriebsart(q)}");
        }
        return teile.Count == 0 ? "EINGRIFF nicht moeglich (kein Rechnerspieler mit Einheiten und Ziel)"
                                : "EINGRIFF nach 5 s ohne Gruppe: add_target(p, 1, 9, Platz, 1) — " + string.Join(", ", teile);
    }
}
