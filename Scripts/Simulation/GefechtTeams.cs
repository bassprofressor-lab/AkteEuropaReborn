using System.Collections.Generic;
using System.Linq;
using Godot;

namespace AkteEuropaReborn.Rendering;

/// <summary>
/// ⭐ 14.11.2026 — P7 (Spielerwunsch): <b>Stärke und Team je Startplatz im Gefecht.</b>
///
/// <para>Bisher hiess das Gefecht »N Gegner, eine Schwierigkeit, jeder gegen jeden«: die ersten
/// N belegten Plätze nach dem Menschen wurden Computergegner, alle mit derselben Stufe, und die
/// Bündnistafel blieb leer. Jetzt bestimmt der Gefechtsschirm je Startplatz <i>wer</i> dort
/// spielt (Du / Computer leicht, normal, schwer / leer) und <i>in welchem Team</i>.</para>
///
/// <para><b>Die Teams brauchen keine neue Mechanik.</b> Das Spiel hat aus der Kampagne die
/// Bündnistafel des Originals (<c>_allied</c>, 0x87B155 + 40·a + b), und alles fragt sie
/// schon: Zielwahl und Feuer (<c>IsHostile</c>), die KI (<c>AiHostile</c>), Minen, Ausweichen,
/// der Nebel (Verbündete teilen die Sicht) und das Ende (<c>Verdict</c>: gewonnen, wenn kein
/// nicht verbündeter Computergegner mehr etwas hat). Hier wird sie nur aus den Teams GEFÜLLT —
/// mit der Diagonale (jeder mit sich selbst), sonst wäre man sein eigener Feind.</para>
///
/// <para>⚠ Ohne Teams bleibt die Tafel unberührt (<c>_haveAllies</c> falsch), damit »jeder gegen
/// jeden« byte-gleich der alte Lauf ist. Gegenschalter <c>--gefecht-plaetze-alt</c>.</para>
/// </summary>
public partial class MapEntityLayer
{
    /// <summary>Gegner und ihre Stufen aus der Aufstellung — in der Reihenfolge von
    /// <paramref name="live"/>, wie die alte Auswahl.</summary>
    private (List<int> Foes, Dictionary<int, AiLevel> Stufen) GefechtAufstellung(List<int> live, int human)
    {
        var foes = new List<int>();
        var stufen = new Dictionary<int, AiLevel>();
        foreach (int p in live)
        {
            if (p == human || p is < 0 or > 7) continue;
            int art = UI.SkirmishSetup.PlatzArt[p];
            if (!UI.SkirmishSetup.IstKi(art)) continue;
            foes.Add(p);
            stufen[p] = UI.SkirmishSetup.StufeVon(art);
        }
        GD.Print("Gefecht-Aufstellung: " + string.Join(", ", live.Select(p =>
            $"Platz {p + 1} " + (p == human ? "Du" : stufen.TryGetValue(p, out var s) ? $"KI {s}" : "leer") +
            (UI.SkirmishSetup.PlatzTeam[p] > 0 ? $" Team {UI.SkirmishSetup.PlatzTeam[p]}" : ""))));
        return (foes, stufen);
    }

    /// <summary>Die Bündnistafel aus den Teams füllen — nur wenn es Teams gibt.</summary>
    private void GefechtTeamsSetzen(List<int> mitspieler)
    {
        if (!UI.SkirmishSetup.HatTeams()) return;
        _allied = new bool[8, 8];
        for (int a = 0; a < 8; a++)
            for (int b = 0; b < 8; b++)
                _allied[a, b] = UI.SkirmishSetup.Verbuendet(a, b);
        _haveAllies = true;
        var teams = mitspieler.Where(p => p is >= 0 and < 8).GroupBy(p => UI.SkirmishSetup.PlatzTeam[p] > 0 ? UI.SkirmishSetup.PlatzTeam[p] : 10 + p)
                              .Select(g => (g.Key < 10 ? $"Team {g.Key}" : "allein") + " [" + string.Join("+", g.Select(p => $"P{p}")) + "]");
        GD.Print("Gefecht-Teams: " + string.Join(" gegen ", teams) + " — Buendnistafel gesetzt");
    }

    /// <summary>Für den Prüfstand: ist a mit b verbündet (dieselbe Frage wie Zielwahl und KI)?</summary>
    public static bool GefechtVerbuendetFuerProbe(int a, int b) => !AiHostile(a, b);
}

public partial class MapEntityLayer
{
    /// <summary><c>--gefecht-teams-probe</c> (P7): das Ende nach Teams, ohne eine Stunde zu spielen.
    /// (1) beim Start läuft die Partie; (2) der VERBÜNDETE fällt ganz — die Partie läuft weiter,
    /// solange Feinde stehen; (3) alle Feinde fallen — gewonnen, obwohl kein Computergegner mehr
    /// steht, der verbündet wäre. Gegenprobe ohne Teams (jeder gegen jeden): Schritt (2) wäre schon
    /// ein Teil des Sieges, (3) ebenso. Setzt Dead direkt (keine Folgen, die Probe endet sofort).</summary>
    public string GefechtTeamsProbe()
    {
        var sb = new System.Text.StringBuilder("gefecht-teams-probe\n");
        int me = ViewPlayer;
        string v0 = Verdict();
        var freunde = _ai.Where(a => Allied(me, a.Player)).Select(a => a.Player).ToList();
        var feinde = _ai.Where(a => !Allied(me, a.Player)).Select(a => a.Player).ToList();
        sb.Append($"  Du P{me}, verbuendet {string.Join(",", freunde.Select(p => "P" + p))}, Feinde {string.Join(",", feinde.Select(p => "P" + p))}\n");
        sb.Append($"  (1) Start: »{v0}« (Soll leer)\n");
        void Faellt(List<int> wer) { foreach (var e in _entities) if (wer.Contains(e.Owner) && !e.IsProp) e.Dead = true; }
        Faellt(freunde);
        string v1 = Verdict();
        sb.Append($"  (2) Verbuendete gefallen: »{v1}« (Soll leer)\n");
        Faellt(feinde);
        string v2 = Verdict();
        sb.Append($"  (3) Feinde gefallen: »{v2}« (Soll MISSION ERFUELLT)\n");
        bool ok = v0 == "" && v1 == "" && v2.Contains("ERFUELLT") && freunde.Count > 0 && feinde.Count > 0;
        sb.Append(ok ? "  BESTANDEN" : "  DURCHGEFALLEN");
        return sb.ToString();
    }
}
