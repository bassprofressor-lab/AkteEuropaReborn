namespace AkteEuropaReborn.Rendering;

using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>
/// ⭐⭐ 02.10.2026 (bug-400) — <c>--k24-flucht-check[=sekunden]</c> (Vorgabe 10 s).
/// Gemeldet (er kennt das Original): »Am Anfang von K24 müssten eigentlich
/// gegnerische Einheiten wegrennen, bei uns stehen sie nur da und werden
/// abgeschossen.« Gelesen in berichte/k24-flucht-fable.md: die drei Radardroiden
/// von P2 (Plätze 2083…2085, Marke +0x43 = 52, Fussvolk, unbewaffnet) sind im
/// Original die EINZIGEN Sätze mit faze 0 und bekommen von der Zustandsmaschine
/// (0x4BBB80, Freiliste 0x4BBE10, faze-Tor @0x4BBDEB) sofort einen Fahrbefehl;
/// die 83 Abwehrstellungen (faze 1) werden nie angefasst.
///
/// <para>Gebaut ist Vorschlag A (faze im Satz + Tor, <c>--ki-faze-alt</c>).
/// ⚠ Vorschlag B (Takt 7 jeder 50-Bild-Runde statt Denk-Takt 0,76 s / 1,5 s) ist
/// NICHT gebaut: die Droiden laufen bei uns erst beim ersten Denk-Takt los.</para>
///
/// <para>BESTANDEN (K24), wenn (1) alle drei Droiden vor ihrem Tod (oder bis zum
/// Ende) eine Zuweisung UND einen Weg hatten, (2) mindestens einer sich von
/// seiner Startzelle entfernt hat, (3) <b>0</b> Zuweisungen an Sätze mit
/// faze ≠ 0. Nullmodell <c>--ki-faze-alt</c>: die Quote geht an die Stellungen,
/// der Prüfstand fällt durch. Jede andere Mission: nur die Zählung (faze ≠ 0 je
/// Besitzer, Zuweisungen, gesperrte Sätze je Durchlauf) — Bedingung (3) gilt
/// überall.</para>
/// </summary>
public partial class MapEntityLayer
{
    public string K24FluchtCheck(int sekunden)
    {
        int m = UI.SkirmishSetup.CampaignMission;
        var sb = new System.Text.StringBuilder(
            $"k24-flucht-check K{m}, {sekunden} s, --ki-faze-alt {KiFazeAlt}\n");
        if (!InCampaign) return sb.Append("  keine Kampagne — ungeprueft\n  DURCHGEFALLEN").ToString();
        MissionScriptTick(0.001f);

        // die Zählung: faze != 0 je Besitzer
        var faze = new SortedDictionary<int, int>();
        var lebend = new SortedDictionary<int, int>();
        foreach (var e in _entities)
        {
            if (e.IsBuilding || e.IsProp || e.Dead || e.Owner < 0) continue;
            lebend[e.Owner] = lebend.GetValueOrDefault(e.Owner) + 1;
            if (e.Faze != 0) faze[e.Owner] = faze.GetValueOrDefault(e.Owner) + 1;
        }
        sb.AppendLine("  Einheiten je Besitzer (davon faze != 0): " + string.Join(" ",
            lebend.Select(kv => $"P{kv.Key}={kv.Value}({faze.GetValueOrDefault(kv.Key)})")));

        // die Droiden: P2, Marke 52
        var droiden = new List<int>();
        for (int i = 0; i < _entities.Count; i++)
        {
            var e = _entities[i];
            if (!e.IsBuilding && !e.IsProp && !e.Dead && e.Mark == 52 && e.Faze == 0) droiden.Add(i);
        }
        var start = droiden.ToDictionary(i => i, i => new Vector2I(_entities[i].Col, _entities[i].Row));
        var zugewiesen = new Dictionary<int, int>();      // Index -> Takt
        var wegTakt = new Dictionary<int, int>();
        var todTakt = new Dictionary<int, int>();
        var todWeite = new Dictionary<int, float>();
        int takt = 0, zuwGesamt = 0, zuwFaze = 0, ersterDurchlauf = -1;
        int zm0 = ZmDurchlaeufe;
        var zuwErste = new List<string>();
        KiFazeZuweisungen = 0;
        ZuweisungsHaken = idx =>
        {
            zuwGesamt++;
            var e = _entities[idx];
            if (e.Faze != 0) zuwFaze++;
            if (zuwErste.Count < 8) zuwErste.Add($"T{takt}:#{idx}({e.UnitType},faze {e.Faze})");
            if (start.ContainsKey(idx) && !zugewiesen.ContainsKey(idx)) zugewiesen[idx] = takt;
        };
        try
        {
            int takte = sekunden * SimHz;
            for (takt = 0; takt < takte; takt++)
            {
                SimTickFuerProbe();
                if (ersterDurchlauf < 0 && ZmDurchlaeufe > zm0) ersterDurchlauf = takt;
                foreach (int i in droiden)
                {
                    var e = _entities[i];
                    if (todTakt.ContainsKey(i)) continue;
                    if (e.Path != null && !wegTakt.ContainsKey(i)) wegTakt[i] = takt;
                    if (e.Dead)
                    {
                        todTakt[i] = takt;
                        todWeite[i] = new Vector2(e.Col - start[i].X, e.Row - start[i].Y).Length();
                    }
                }
            }
        }
        finally { ZuweisungsHaken = null; }

        var fehler = new List<string>();
        foreach (int i in droiden)
        {
            var e = _entities[i];
            float weite = todWeite.TryGetValue(i, out float w)
                ? w : new Vector2(e.Col - start[i].X, e.Row - start[i].Y).Length();
            sb.AppendLine($"  Droide #{i} Platz {e.Slot} Start {start[i]}: " +
                          $"Zuweisung {(zugewiesen.TryGetValue(i, out int zt) ? $"Takt {zt} ({zt / (float)SimHz:0.00} s)" : "KEINE")}, " +
                          $"Weg {(wegTakt.TryGetValue(i, out int wt) ? $"Takt {wt}" : "KEINER")}, " +
                          $"{(todTakt.TryGetValue(i, out int tt) ? $"tot in Takt {tt} ({tt / (float)SimHz:0.00} s)" : $"lebt, jetzt ({e.Col},{e.Row})")}, " +
                          $"{weite:0.0} Zellen von der Startzelle");
        }
        sb.AppendLine($"  Zuweisungen der Zustandsmaschine: {zuwGesamt}, davon an faze != 0: {zuwFaze} " +
                      $"(Zaehler {KiFazeZuweisungen}); erste: {string.Join(" ", zuwErste)}");
        foreach (var a in _ai)
            sb.AppendLine($"  P{a.Player}: {a.SmZuweisungen} zugewiesen, faze-Tor {a.SmFazeGesperrt} Satz-Durchlaeufe " +
                          $"uebersprungen, zuletzt {a.SmFazeJeDurchlauf} je Durchlauf");

        sb.AppendLine($"  erster Durchlauf der Zustandsmaschine: Takt {ersterDurchlauf} ({ersterDurchlauf / (float)SimHz:0.00} s); " +
                      "Original Takt 7 jeder 50-Bild-Runde = 0,14 s (Vorschlag B, NICHT gebaut)");
        if (zuwFaze > 0) fehler.Add($"{zuwFaze} Zuweisungen an Saetze mit faze != 0 (Original 0x4BBDEB: keine)");
        if (m == 24)
        {
            if (droiden.Count != 3) fehler.Add($"K24: {droiden.Count} Radardroiden statt 3");
            foreach (int i in droiden)
            {
                // ⚠ Wer schon VOR dem ersten Durchlauf faellt, konnte nur Takt 7
                // (Vorschlag B) retten — kein Fehler des faze-Tors, aber gemeldet.
                if (todTakt.TryGetValue(i, out int frueh) && (ersterDurchlauf < 0 || frueh < ersterDurchlauf))
                {
                    sb.AppendLine($"  ⚠ Droide #{i} fiel in Takt {frueh}, VOR dem ersten KI-Durchlauf (Takt {ersterDurchlauf}) — " +
                                  "nur Takt 7 (Vorschlag B) haette ihn vorher losgeschickt; nicht gewertet");
                    continue;
                }
                bool vorTod = zugewiesen.TryGetValue(i, out int zt)
                              && (!todTakt.TryGetValue(i, out int tt) || zt <= tt);
                if (!vorTod) fehler.Add($"Droide #{i}: keine Zuweisung vor dem Tod");
                if (!wegTakt.ContainsKey(i)) fehler.Add($"Droide #{i}: nie einen Weg");
            }
            int gewertet = droiden.Count(i => !(todTakt.TryGetValue(i, out int f) && (ersterDurchlauf < 0 || f < ersterDurchlauf)));
            if (gewertet < 2) fehler.Add($"nur {gewertet} Droiden erlebten den ersten KI-Durchlauf — zu wenig fuer eine Aussage");
            bool bewegt = droiden.Any(i => (todWeite.TryGetValue(i, out float w) ? w
                : new Vector2(_entities[i].Col - start[i].X, _entities[i].Row - start[i].Y).Length()) > 0f);
            if (!bewegt) fehler.Add("kein Droide hat seine Startzelle verlassen");
        }
        foreach (var f in fehler) sb.AppendLine("  FEHLER: " + f);
        sb.Append(fehler.Count == 0 ? "  BESTANDEN" : "  DURCHGEFALLEN");
        return sb.ToString();
    }
}
