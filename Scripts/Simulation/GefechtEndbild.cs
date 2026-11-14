using System.Collections.Generic;
using System.Linq;
using Godot;

namespace AkteEuropaReborn.Rendering;

/// <summary>
/// ⭐ 14.11.2026 — Fable D5: <b>das Endbild eines Gefechts und einer Netzpartie.</b>
///
/// <para>Das Abschlussfenster ist das der Kampagne (UI/MissionEndWindow.cs, nach dem Bildschirmfoto
/// des Originals): acht Spalten »SPIELER / CPU / --/--«, Missionsbezahlung, Kontostand. Im Gefecht und
/// im Netz sagte es nicht, WER gewonnen hat, die Mitspieler hießen »CPU«, und die Bezahlung stand mit
/// $0 da. UNSERE ZUTAT, nur im Gefecht (die Kampagne bleibt, wie sie ist):</para>
/// <list type="bullet">
/// <item>die Spalten tragen Namen: »DU«, die Namen der Mitspieler, »CPU LEICHT/NORMAL/SCHWER«;</item>
/// <item>eine Zeile »Team«, wenn es Teams gibt;</item>
/// <item>unter dem Titel das Ergebnis: »Sieger: Team 1 — Du + CPU schwer« oder, wenn noch gekämpft
/// wird, wer noch steht;</item>
/// <item>online meldet das Spiel dem Server den Sieger (<c>match_end</c> mit dem Team), sobald nur
/// noch eine Seite steht — bisher immer 0.</item>
/// </list>
/// <para>Gegenschalter <c>--endbild-alt</c>.</para>
/// </summary>
public partial class MapEntityLayer
{
    public static bool EndbildAlt => System.Array.IndexOf(Core.CommandLine.Args, "--endbild-alt") >= 0;

    /// <summary>Wer an diesem Gefecht teilnimmt: der eigene Platz, die Computergegner, die
    /// Menschen der Netzpartie.</summary>
    private List<int> GefechtTeilnehmer()
    {
        var l = new List<int>();
        void Dazu(int p) { if (p is >= 0 and <= 7 && !l.Contains(p)) l.Add(p); }
        Dazu(ViewPlayer);
        foreach (var a in _ai) Dazu(a.Player);
        foreach (int h in Network.NetworkManager.HumanSlots()) Dazu(h);
        // ein ausgeschiedener Computergegner steht nicht mehr in _ai — die Aufstellung kennt ihn noch
        if (UI.SkirmishSetup.PlatzListe)
            for (int p = 0; p < 8; p++) if (UI.SkirmishSetup.PlatzArt[p] != UI.SkirmishSetup.ArtLeer) Dazu(p);
        l.RemoveAll(UI.SkirmishSetup.IstZuschauer);   // D10: Zuschauer stehen nicht in der Abrechnung
        l.Sort();
        return l;
    }

    public string GefechtName(int p)
    {
        if (p == ViewPlayer) return "Du";
        int nr = System.Array.IndexOf(Network.NetworkManager.HumanSlots(), p);
        if (nr >= 0) return Network.NetworkManager.PlayerName(nr).Replace(" (Gastgeber)", "");
        var ki = _ai.FirstOrDefault(a => a.Player == p);
        AiLevel? stufe = ki?.Level ?? (UI.SkirmishSetup.PlatzListe && UI.SkirmishSetup.IstKi(UI.SkirmishSetup.PlatzArt[p])
                                         ? UI.SkirmishSetup.StufeVon(UI.SkirmishSetup.PlatzArt[p]) : null);
        if (stufe is { } s)
            return "CPU " + (s switch { AiLevel.Easy => "leicht", AiLevel.Hard => "schwer", _ => "normal" });
        return $"Platz {p + 1}";
    }

    private int GefechtTeamVon(int p) => UI.SkirmishSetup.HatTeams() ? UI.SkirmishSetup.PlatzTeam[p] : 0;

    /// <summary>Die Seiten, die noch etwas haben: ein Team ist eine Seite, wer ohne Team spielt,
    /// ist seine eigene. <c>Entschieden</c>, wenn nur noch eine steht.</summary>
    public (bool Entschieden, string Text, int SiegerKennung) GefechtErgebnis()
    {
        var seiten = GefechtTeilnehmer().Where(p => AssetsOf(p) > 0)
            .GroupBy(p => GefechtTeamVon(p) > 0 ? GefechtTeamVon(p) : 100 + p).ToList();
        string Seite(IGrouping<int, int> g)
            => (g.Key < 100 ? $"Team {g.Key} — " : "") + string.Join(" + ", g.Select(GefechtName));
        if (seiten.Count == 1)
        {
            var g = seiten[0];
            // Kennung fuer den Server (match_end.winner_team): das Team, ohne Teams der Startplatz
            // (+1). Auf allen Maschinen dieselbe Rechnung — der Server will die Mehrheit gleich.
            return (true, "Sieger: " + Seite(g), g.Key < 100 ? g.Key : g.Key - 100 + 1);
        }
        if (seiten.Count == 0) return (true, "Niemand ist übrig.", 0);
        return (false, "Es kämpfen noch: " + string.Join("  gegen  ", seiten.Select(Seite)), 0);
    }

    /// <summary>Prüfweg <c>--endbild-probe</c>: alles Feindliche fällt (Dead, ohne Folgen).</summary>
    public int EndbildProbeFeindeWeg()
    {
        int n = 0;
        foreach (var e in _entities)
            if (!e.IsProp && !e.Dead && e.Owner is >= 0 and <= 7 && !Allied(ViewPlayer, e.Owner)) { e.Dead = true; n++; }
        return n;
    }

    /// <summary>Den Bericht für das Gefecht ergänzen (aus BuildEndReport).</summary>
    private void GefechtEndbildFuellen(EndReport r)
    {
        if (EndbildAlt || UI.SkirmishSetup.CampaignMission > 0 || _mscript is { Decides: true }) return;
        r.Gefecht = true;
        bool teams = UI.SkirmishSetup.HatTeams();
        var dabei = GefechtTeilnehmer();
        var menschen = Network.NetworkManager.HumanSlots();
        r.Teams = teams ? new string[r.Columns.Length] : null;
        for (int p = 0; p < r.Columns.Length; p++)
        {
            var c = r.Columns[p];
            if (!dabei.Contains(p)) continue;
            c.Used = true;
            c.Name = GefechtName(p);
            c.Human = p == ViewPlayer || System.Array.IndexOf(menschen, p) >= 0;
            if (r.Teams != null) r.Teams[p] = GefechtTeamVon(p) > 0 ? GefechtTeamVon(p).ToString() : "–";
        }
        r.Ergebnis = GefechtErgebnis().Text;
    }
}
