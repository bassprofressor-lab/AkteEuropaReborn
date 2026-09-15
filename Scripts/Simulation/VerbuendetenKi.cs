namespace AkteEuropaReborn.Rendering;

using System.Linq;
using Godot;

/// <summary>
/// <c>--verbuendeten-ki-check</c> (15.09.2026, seine Entscheidung c zu
/// <c>berichte/verbuendete-fable.md</c> §8.6): ein verbuendeter Rechnerspieler der
/// Kampagne bekommt die KI wie ein Gegner (<c>StartCampaign</c>). Kampagne 14.
///
/// <para>Gemessen nach <c>Sekunden</c> Spielzeit, fuer jeden verbuendeten Spieler:
/// steht er in der KI-Liste, hat er Denk-Zuege gemacht — und hat er dabei nichts gegen
/// den Spieler oder einen anderen Verbuendeten unternommen: keine seiner Einheiten hat
/// ein verbuendetes Ziel, kein Greifauftrag auf ein verbuendetes Gebaeude, der Riegel
/// in <c>AiSend</c> musste nichts abwehren. Die Ankunft wird mitgemessen: Einheiten und
/// Bauten des Verbuendeten vorher/nachher.</para>
///
/// <para>Nullmodell <c>--verbuendete-ohne-ki</c>: der Verbuendete steht nicht in der
/// Liste und macht keine Zuege — der Lauf MUSS durchfallen.</para>
/// </summary>
public partial class MapEntityLayer
{
    /// <summary><c>--verbuendete-ohne-ki</c> — der Stand bis 15.09.2026: ein
    /// Verbuendeter der Kampagne steht still, nur Gegner bekommen die KI.</summary>
    public static bool VerbuendeteOhneKi;

    public string VerbuendetenKiCheck(int sekunden)
    {
        var sb = new System.Text.StringBuilder("verbuendeten-ki-check\n");
        if (VerbuendeteOhneKi) sb.AppendLine("  ⚠ NULLMODELL --verbuendete-ohne-ki: hier MUSS der Lauf durchfallen");
        if (!InCampaign) return sb.Append("  keine Kampagne — ungeprueft\n  DURCHGEFALLEN").ToString();

        var verb = new System.Collections.Generic.List<int>();
        for (int p = 0; p < 8; p++)
            if (p != ViewPlayer && !IsNeutralPlayer(p) && Allied(ViewPlayer, p) && AliveAsPlayer(p)) verb.Add(p);
        if (verb.Count == 0) return sb.Append("  kein verbuendeter Spieler auf der Karte — sagt NICHTS\n  DURCHGEFALLEN").ToString();

        int abgewehrt0 = AiSendFreundAbgewehrt;
        var vorher = verb.ToDictionary(p => p, p => (E: ArmyOf(p).Count, B: _entities.Count(x => x.IsBuilding && !x.Dead && x.Owner == p)));
        var bautenVor = new System.Collections.Generic.List<(int I, int Owner, string Name)>();
        for (int i = 0; i < _entities.Count; i++)
            if (_entities[i].IsBuilding && !_entities[i].Dead && verb.Contains(_entities[i].Owner))
                bautenVor.Add((i, _entities[i].Owner, BuildingName(_entities[i])));
        var killsVor = _players.ToDictionary(x => x.Index, x => x.Kills);
        int takte = (int)(sekunden * SimHz);
        int freundZiel = 0;
        string freundBeispiel = "";
        for (int t = 0; t < takte; t++)
        {
            SimTickFuerProbe();
            if (t % 25 != 0) continue;
            foreach (var e in _entities)
            {
                if (e.Dead || e.IsProp || e.Target < 0 || e.Target >= _entities.Count || !verb.Contains(e.Owner)) continue;
                var z = _entities[e.Target];
                if (z.Owner is >= 0 and <= 7 && Allied(e.Owner, z.Owner))
                { freundZiel++; if (freundBeispiel == "") freundBeispiel = $" (Platz {e.Slot} -> Platz {z.Slot} Sp {z.Owner})"; }
            }
        }

        bool ok = true;
        foreach (int p in verb)
        {
            var a = _ai.FirstOrDefault(x => x.Player == p);
            bool greiftFreund = a != null && a.GrabTarget >= 0 && a.GrabTarget < _entities.Count
                                && Allied(_entities[a.GrabTarget].Owner, p) && !Herrenlos(_entities[a.GrabTarget].Owner);
            int eN = ArmyOf(p).Count, bN = _entities.Count(x => x.IsBuilding && !x.Dead && x.Owner == p);
            bool soll = a != null && a.Zuege > 0 && !greiftFreund;
            ok &= soll;
            sb.AppendLine($"  Spieler {p}: KI {(a != null ? "ja" : "NEIN")}, Zuege {a?.Zuege ?? 0}, gebaut {a?.Built ?? 0}, "
                        + $"Programm {(a?.Plan != null ? $"{a.FromPlan}p/{a.Plan.Count}" : "keins")}, gesperrt {AiGesperrt(p)}, "
                        + $"Greifauftrag auf Verbuendeten {greiftFreund}; Einheiten {vorher[p].E} -> {eN}, Bauten {vorher[p].B} -> {bN}  {(soll ? "ja" : "NEIN")}");
        }
        foreach (var (i, o, name) in bautenVor)
        {
            var b = _entities[i];
            if (b.Dead || b.Owner != o)
                sb.AppendLine($"    Bau von Spieler {o} weg: {name} Platz {b.Slot} auf ({b.Col},{b.Row}) — {(b.Dead ? "zerstoert" : $"jetzt Spieler {b.Owner}")}, TP {b.Hp}/{b.HpMax}");
        }
        sb.AppendLine("    Abschuesse in der Zeit: " + string.Join(", ", _players.Where(x => killsVor.ContainsKey(x.Index) && x.Kills != killsVor[x.Index])
                                                              .Select(x => $"P{x.Index} +{x.Kills - killsVor[x.Index]}")));
        int abgewehrt = AiSendFreundAbgewehrt - abgewehrt0;
        bool friedlich = freundZiel == 0 && abgewehrt == 0;
        ok &= friedlich;
        sb.AppendLine($"  nach {sekunden} s: Ziele auf Verbuendete {freundZiel}{freundBeispiel}, AiSend-Riegel {abgewehrt}  {(friedlich ? "ja" : "NEIN")}");
        sb.AppendLine($"  {AiLine()}");
        sb.AppendLine($"  {AiPlanLine()}");
        sb.AppendLine($"  --verbuendete-ohne-ki {VerbuendeteOhneKi}");
        return sb.Append(ok ? "  BESTANDEN" : "  DURCHGEFALLEN").ToString();
    }
}
