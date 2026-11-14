namespace AkteEuropaReborn.UI;

using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>
/// ⭐ 14.11.2026 — P7 (Spielerwunsch): <b>die Platzliste des Gefechtsschirms.</b>
///
/// <para>»wenn man die schwierigkeit auswählt, richtet sich danach der computergegner. ich kann
/// also nicht beliebig sagen, ich nehme ein computergegner leicht, einen normal und einen
/// schweren … und team bildung wäre gut«. Statt der drei Zeilen Gegner / Startplatz /
/// Schwierigkeit steht im Gefecht jetzt eine Zeile JE STARTPLATZ der Karte, wie in klassischen
/// Echtzeitstrategiespielen:</para>
/// <list type="bullet">
/// <item><b>Spieler:</b> Du · Computer leicht · Computer normal · Computer schwer · leer</item>
/// <item><b>Team:</b> – (jeder gegen jeden) · Team 1..4 — gleiches Team = verbündet</item>
/// </list>
/// <para>Damit ist der Startplatz zugleich gewählt (die Zeile, auf der »Du« steht). »Du« zieht
/// um, indem man eine andere Zeile auf »Du« stellt; die alte Zeile bekommt, was dort stand.</para>
///
/// <para>Im Mehrspielerschirm bleiben die alten drei Zeilen (das Netz kommt im nächsten Schritt).
/// Die Befehlszeile <c>--skirmish=karte,n,stufe,platz</c> bleibt beim Alten; die neue Liste
/// setzt <c>--gefecht-plaetze=du:1,schwer:1,leicht:2,normal:2</c> (in der Reihenfolge der
/// Startplätze der Karte). Gegenschalter <c>--gefecht-plaetze-alt</c>.</para>
/// </summary>
public partial class MainMenu
{
    private static readonly string[] ArtNamen = { "leer", "Du", "Computer leicht", "Computer normal", "Computer schwer" };
    /// <summary>Reihenfolge im Auswahlfeld (Art-Werte aus SkirmishSetup).</summary>
    private static readonly int[] ArtReihe =
    {
        SkirmishSetup.ArtMensch, SkirmishSetup.ArtLeicht, SkirmishSetup.ArtNormal,
        SkirmishSetup.ArtSchwer, SkirmishSetup.ArtLeer,
    };

    private PanelContainer? _plaetzeRahmen;
    private VBoxContainer? _plaetzeZeilen;
    private Label? _plaetzeInfo;
    private readonly List<(int Platz, OptionButton Art, OptionButton Team)> _zeilen = new();
    private readonly int[] _art = Enumerable.Repeat(-1, 8).ToArray();   // -1 = noch nie gesetzt
    private readonly int[] _team = new int[8];
    private List<int> _plaetzeKarte = new();
    private bool _plaetzeStill;
    /// <summary>--skirmish ohne --gefecht-plaetze: der alte Weg (aiCount/Stufe/Platz).</summary>
    private bool _plaetzeBefehlszeileAlt;

    private bool PlaetzeAn => !SkirmishSetup.PlaetzeAlt && !_netEntry && !_plaetzeBefehlszeileAlt;

    /// <summary>Den Rahmen bauen (einmal, im Einstellungsblock unter »Schwierigkeit«).</summary>
    private void PlaetzeBauen(VBoxContainer right)
    {
        if (SkirmishSetup.PlaetzeAlt) return;
        var v = new VBoxContainer();
        v.AddThemeConstantOverride("separation", 3);
        var kopf = new HBoxContainer();
        kopf.AddThemeConstantOverride("separation", 8);
        kopf.AddChild(new Label { Text = "Startplatz", CustomMinimumSize = new Vector2(84, 0), Modulate = new Color(0.62f, 0.72f, 0.85f) });
        kopf.AddChild(new Label { Text = "Spieler", SizeFlagsHorizontal = SizeFlags.ExpandFill, Modulate = new Color(0.62f, 0.72f, 0.85f) });
        kopf.AddChild(new Label { Text = "Team", CustomMinimumSize = new Vector2(110, 0), Modulate = new Color(0.62f, 0.72f, 0.85f) });
        v.AddChild(kopf);
        _plaetzeZeilen = new VBoxContainer();
        _plaetzeZeilen.AddThemeConstantOverride("separation", 3);
        v.AddChild(_plaetzeZeilen);
        _plaetzeInfo = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, Modulate = new Color(0.75f, 0.8f, 0.85f) };
        _plaetzeInfo.AddThemeFontSizeOverride("font_size", 14);
        v.AddChild(_plaetzeInfo);
        _plaetzeRahmen = Accent("", v);
        right.AddChild(_plaetzeRahmen);
    }

    /// <summary>Gefecht: Platzliste zeigen, die alten drei Zeilen verstecken; Netz: umgekehrt.</summary>
    private void PlaetzeSichtbar()
    {
        bool an = PlaetzeAn && _plaetzeRahmen != null;
        if (_plaetzeRahmen != null) _plaetzeRahmen.Visible = an;
        foreach (Control? c in new Control?[] { _ai, _slot, _level })
            if (c?.GetParent() is Control zeile) zeile.Visible = !an;
        if (!an && _startButton != null && !_netWaiting) _startButton.Disabled = false;
        if (an) PlaetzePruefen();
    }

    /// <summary>Die alten drei Felder als Aufstellung: Du auf dem gewählten (sonst ersten) Platz,
    /// die nächsten N als Computer mit der einen Stufe, der Rest leer — genau die alte Regel.</summary>
    private void PlaetzeAusAltenFeldern(List<int> pl)
    {
        if (pl.Count == 0) return;
        int du = GewaehlterPlatz();
        if (!pl.Contains(du)) du = pl[0];
        int n = (int)_ai.Value;
        int ki = SkirmishSetup.ArtLeicht + Mathf.Clamp(_level.Selected, 0, 2);
        foreach (int p in pl)
        {
            _team[p] = 0;
            if (p == du) _art[p] = SkirmishSetup.ArtMensch;
            else if (n > 0) { _art[p] = ki; n--; }
            else _art[p] = SkirmishSetup.ArtLeer;
        }
    }

    /// <summary>Die Zeilen für die Startplätze der gewählten Karte (aus PlaetzeAusKarte).</summary>
    private void PlaetzeFuellen(List<int> pl)
    {
        _plaetzeKarte = new List<int>(pl);
        if (_plaetzeZeilen == null || pl.Count == 0) return;
        // noch nie etwas gesetzt: aus den alten Feldern (Gegnerzahl und Stufe von früher)
        if (pl.All(p => _art[p] < 0)) PlaetzeAusAltenFeldern(pl);
        foreach (int p in pl) if (_art[p] < 0) _art[p] = SkirmishSetup.ArtLeer;
        // genau ein »Du« auf dieser Karte
        var du = pl.Where(p => _art[p] == SkirmishSetup.ArtMensch).ToList();
        if (du.Count == 0) _art[pl[0]] = SkirmishSetup.ArtMensch;
        foreach (int p in du.Skip(1)) _art[p] = SkirmishSetup.ArtNormal;

        _plaetzeStill = true;
        foreach (var z in _zeilen) z.Art.GetParent()?.QueueFree();
        _zeilen.Clear();
        foreach (int p in pl)
        {
            var h = new HBoxContainer();
            h.AddThemeConstantOverride("separation", 8);
            h.AddChild(new Label { Text = $"Platz {p + 1}", CustomMinimumSize = new Vector2(84, 0) });
            var art = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            foreach (int a in ArtReihe) art.AddItem(ArtNamen[a]);
            art.Select(System.Array.IndexOf(ArtReihe, _art[p]));
            art.TooltipText = "Wer auf diesem Startplatz spielt. »Du« ziehst du um, indem du eine andere " +
                              "Zeile auf »Du« stellst. »leer«: der Platz bleibt frei, seine Truppen verschwinden.";
            var team = new OptionButton { CustomMinimumSize = new Vector2(110, 0) };
            team.AddItem("–");
            for (int t = 1; t <= 4; t++) team.AddItem($"Team {t}");
            team.Select(Mathf.Clamp(_team[p], 0, 4));
            team.TooltipText = "Gleiches Team = verbündet: sie greifen sich nicht an und sehen, was der " +
                               "andere sieht. »–« = jeder gegen jeden.";
            int platz = p;
            art.ItemSelected += i => ArtGewaehlt(platz, (int)i);
            team.ItemSelected += i => { if (!_plaetzeStill) { _team[platz] = (int)i; PlaetzePruefen(); } };
            h.AddChild(art);
            h.AddChild(team);
            _plaetzeZeilen.AddChild(h);
            _zeilen.Add((p, art, team));
        }
        _plaetzeStill = false;
        PlaetzePruefen();
    }

    private void ArtGewaehlt(int platz, int zeile)
    {
        if (_plaetzeStill) return;
        int neu = ArtReihe[Mathf.Clamp(zeile, 0, ArtReihe.Length - 1)];
        int alt = _art[platz];
        if (alt == SkirmishSetup.ArtMensch && neu != SkirmishSetup.ArtMensch)
        {
            // ohne »Du« keine Partie: zurück, mit Hinweis
            if (_plaetzeInfo != null) _plaetzeInfo.Text = "Du bleibst auf diesem Platz, bis du eine andere Zeile auf »Du« stellst.";
            ZeileSetzen(platz);
            return;
        }
        if (neu == SkirmishSetup.ArtMensch)
            foreach (int p in _plaetzeKarte)
                if (p != platz && _art[p] == SkirmishSetup.ArtMensch) { _art[p] = alt; ZeileSetzen(p); }
        _art[platz] = neu;
        PlaetzePruefen();
    }

    private void ZeileSetzen(int platz)
    {
        _plaetzeStill = true;
        foreach (var z in _zeilen)
            if (z.Platz == platz) z.Art.Select(System.Array.IndexOf(ArtReihe, _art[platz]));
        _plaetzeStill = false;
    }

    /// <summary>Die Aufstellung in einem Satz, und Start nur, wenn es einen Gegner gibt.</summary>
    private void PlaetzePruefen()
    {
        if (_plaetzeInfo == null || !PlaetzeAn) return;
        var dabei = _plaetzeKarte.Where(p => _art[p] is > SkirmishSetup.ArtLeer).ToList();
        int du = dabei.FirstOrDefault(p => _art[p] == SkirmishSetup.ArtMensch, -1);
        string Name(int p) => _art[p] == SkirmishSetup.ArtMensch ? "Du" : ArtNamen[_art[p]].Replace("Computer ", "");
        // Seiten: ein Team ist eine Seite, jeder ohne Team ist seine eigene
        var seiten = dabei.GroupBy(p => _team[p] > 0 ? _team[p] : 10 + p).ToList();
        bool feind = du >= 0 && dabei.Any(p => p != du && !(_team[du] > 0 && _team[p] == _team[du]));
        string text;
        if (!feind)
            text = "⚠ Kein Gegner: mindestens ein Computergegner muss in einem anderen Team stehen als du.";
        else if (seiten.All(g => g.Count() == 1))
            text = $"Jeder gegen jeden — {dabei.Count} Parteien.";
        else
            text = string.Join("  gegen  ", seiten.Select(g =>
                g.Key < 10 ? $"Team {g.Key}: " + string.Join(" + ", g.Select(Name))
                           : $"Platz {g.First() + 1}: {Name(g.First())}"));
        _plaetzeInfo.Text = text;
        _plaetzeInfo.Modulate = feind ? new Color(0.75f, 0.8f, 0.85f) : new Color(0.95f, 0.65f, 0.4f);
        if (_startButton != null && !_netWaiting) _startButton.Disabled = !feind;
    }

    /// <summary>Die Liste in SkirmishSetup tragen (aus ApplySetupFields). Gegnerzahl, Stufe und
    /// Startplatz der alten Felder werden mitgeführt, damit Anzeigen und Protokolle stimmen.</summary>
    private void PlaetzeUebernehmen()
    {
        SkirmishSetup.PlatzListe = PlaetzeAn && _plaetzeKarte.Count > 0;
        if (!SkirmishSetup.PlatzListe) return;
        for (int p = 0; p < 8; p++)
        {
            bool aufKarte = _plaetzeKarte.Contains(p);
            SkirmishSetup.PlatzArt[p] = aufKarte ? Mathf.Max(0, _art[p]) : SkirmishSetup.ArtLeer;
            SkirmishSetup.PlatzTeam[p] = aufKarte ? _team[p] : 0;
        }
        int du = _plaetzeKarte.FirstOrDefault(p => _art[p] == SkirmishSetup.ArtMensch, _plaetzeKarte[0]);
        var ki = _plaetzeKarte.Where(p => SkirmishSetup.IstKi(_art[p])).ToList();
        SkirmishSetup.Human = du;
        SkirmishSetup.AiCount = ki.Count;
        if (ki.Count > 0) SkirmishSetup.Level = SkirmishSetup.StufeVon(_art[ki[0]]);
        GD.Print($"gefecht: Aufstellung {SkirmishSetup.PlaetzeText()} (Du Platz {du + 1}, {ki.Count} Computergegner" +
                 (SkirmishSetup.HatTeams() ? ", mit Teams)" : ", jeder gegen jeden)"));
    }

    private void PlaetzeLaden()
    {
        if (!SkirmishSetup.PlaetzeAusText(Settings.SkirmishPlaetze, _art, _team))
            System.Array.Fill(_art, -1);   // nichts gemerkt: aus Gegnerzahl und Stufe von eben
        if (_plaetzeKarte.Count > 0) PlaetzeFuellen(_plaetzeKarte);
    }

    private void PlaetzeMerken()
    {
        if (!PlaetzeAn) return;
        var a = new int[8]; var t = new int[8];
        for (int p = 0; p < 8; p++) { a[p] = Mathf.Max(0, _art[p]); t[p] = _team[p]; }
        var sb = new System.Text.StringBuilder();
        for (int p = 0; p < 8; p++) sb.Append(p > 0 ? "," : "").Append(a[p]).Append(':').Append(t[p]);
        Settings.SkirmishPlaetze = sb.ToString();
    }

    /// <summary><c>--gefecht-plaetze=du:1,schwer:1,leicht:2,normal:2</c> — in der Reihenfolge der
    /// Startplätze der Karte; ohne »:team« kein Team. Fehlende Plätze werden leer.</summary>
    private bool PlaetzeAusBefehlszeile()
    {
        string? arg = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--gefecht-plaetze="));
        if (arg == null || SkirmishSetup.PlaetzeAlt) return false;
        var teile = arg["--gefecht-plaetze=".Length..].Split(',');
        for (int i = 0; i < _plaetzeKarte.Count; i++)
        {
            int p = _plaetzeKarte[i];
            var at = i < teile.Length ? teile[i].Split(':') : new[] { "leer" };
            _art[p] = at[0].Trim().ToLower() switch
            {
                "du" or "mensch" => SkirmishSetup.ArtMensch,
                "leicht" or "easy" => SkirmishSetup.ArtLeicht,
                "normal" => SkirmishSetup.ArtNormal,
                "schwer" or "hard" => SkirmishSetup.ArtSchwer,
                _ => SkirmishSetup.ArtLeer,
            };
            _team[p] = at.Length > 1 && int.TryParse(at[1], out int t) ? Mathf.Clamp(t, 0, 4) : 0;
        }
        PlaetzeFuellen(_plaetzeKarte);
        GD.Print($"gefecht: --gefecht-plaetze -> {string.Join(", ", _plaetzeKarte.Select(p => $"Platz {p + 1} {ArtNamen[_art[p]]}" + (_team[p] > 0 ? $" T{_team[p]}" : "")))}");
        return true;
    }
}
