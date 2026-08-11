namespace AkteEuropaReborn.UI;

using System;
using Godot;
using AkteEuropaReborn.Rendering;

/// <summary>
/// Das Fenster »MISSION ERFOLGREICH BEENDET« — die Abrechnung am Ende einer
/// Mission.
///
/// <para><b>Woher der Aufbau kommt:</b> abgeschrieben vom Bildschirmfoto des
/// Originals (akte-europa_5.jpg, 1280×1024). Der Reihe nach von oben:
/// Titelleiste mit dem Fensternamen und einem X rechts · der Missionsname
/// mittig in ROT · eine Tabelle ueber ACHT Spalten in einem schwarzen,
/// eingelassenen Kasten (erste Spalte der Spielername in Blau, zweite »CPU« in
/// Gruen, die restlichen sechs leer als »--/--«) mit den vier Zeilen
/// Bewaffnete · Unbewaffnete · Schiffe · Flugzeuge, jeder Wert im Format
/// <c>a/b</c> · darunter mittig untereinander »Missionszeit : 00:29«,
/// »Gebaute Einheiten 0«, »Ausgeschaltete 21 / Verluste 2«,
/// »Untermissionen 1 / 1« · dann in ORANGE »Missionsbezahlung $320« und
/// darunter »Kontostand $470« · ganz unten ein Knopf ueber die volle Breite:
/// »Weiter«.</para>
///
/// <para>Zwei Beschriftungen sind aus dem Programm belegt und nicht nur vom
/// Bild abgelesen: die Statistikseite druckt die Verluste hinter
/// <c>" / Verluste "</c> @0x48571b, und die beiden Zaehler stehen im
/// Spielersatz auf +0x20 (Abschuesse) und +0x24 (Verluste) — siehe
/// <c>Rendering/MapEntityLayer.Player</c>.</para>
///
/// <para>⚠ <b>UNSERE SETZUNGEN, ausdruecklich:</b></para>
/// <list type="bullet">
/// <item>Die <b>Farbwerte</b> und die Rahmenform. Das Original zeichnet mit
/// eigener Grafik aus einer .PIC; hier stehen Godot-Stilkaesten, die nach dem
/// Bild eingestellt sind. Die FARBROLLEN dagegen sind die des Originals
/// (Mensch blau, CPU gruen, Missionsname rot, Bezahlung orange).</item>
/// <item>Der <b>Name des Menschen</b>: das Original zeigt den Profilnamen
/// (»Virgil« im Bild), ein Profil haben wir nicht — es steht »SPIELER«.</item>
/// <item>Der <b>Titel bei einer verlorenen Mission</b>. Vom Original liegt nur
/// das Erfolgsbild vor.</item>
/// <item>Dass <b>»Weiter« bei fehlender Folgemission ins Menue</b> geht. Im
/// Original ist der Knopf immer »Weiter«; wohin er am Kampagnenende fuehrt,
/// ist ungelesen.</item>
/// </list>
///
/// <para><b>Was das Fenster nebenbei erledigt</b> (gemeldet 11.08.2026): es gab
/// keinen Weg von einer beendeten Mission zur naechsten — der Spieler musste
/// ins Hauptmenue und »Neues Spiel« druecken. Der Grund stand in
/// <c>MapViewer.StartNextMission</c>: der Knopf existierte, aber
/// <c>_nextMission</c> wurde NIE gesetzt (blieb -1), also lief er in
/// <c>ByIndex(-1) == null</c> und damit ins Menue — und sichtbar gemacht wurde
/// er ueberhaupt nicht. »Weiter« startet jetzt die naechste Mission aus
/// <c>user://data/Maps/campaign.json</c>.</para>
/// </summary>
public sealed partial class MissionEndWindow : PanelContainer
{
    // Die vier Zeilen der Tabelle, in der Reihenfolge des Bildschirmfotos.
    private static readonly string[] RowNames =
        { "Bewaffnete", "Unbewaffnete", "Schiffe", "Flugzeuge" };

    // --- Farben, nach dem Bildschirmfoto eingestellt (⚠ unsere Werte) -------
    private static readonly Color WinBg = new(0.30f, 0.28f, 0.25f);
    private static readonly Color WinEdge = new(0.55f, 0.51f, 0.45f);
    private static readonly Color BarBg = new(0.22f, 0.20f, 0.18f);
    private static readonly Color TitleFg = new(0.85f, 0.62f, 0.22f);
    private static readonly Color MissionFg = new(0.85f, 0.18f, 0.14f);
    private static readonly Color TableBg = new(0.02f, 0.02f, 0.02f);
    private static readonly Color HumanFg = new(0.30f, 0.45f, 0.98f);
    private static readonly Color CpuFg = new(0.20f, 0.85f, 0.25f);
    private static readonly Color EmptyFg = new(0.42f, 0.42f, 0.40f);
    private static readonly Color PlainFg = new(0.82f, 0.82f, 0.76f);
    private static readonly Color PayFg = new(0.88f, 0.62f, 0.18f);

    private readonly Label _title = new();
    private readonly Label _mission = new();
    private readonly GridContainer _grid = new();
    private readonly Label _timeL = new(), _builtL = new(), _scoreL = new(), _subL = new();
    private readonly Label _payL = new(), _balL = new();
    private readonly Button _go = new();
    private readonly Button _x = new();

    /// <summary>Was »Weiter« tut. Wird von aussen gesetzt, damit das Fenster
    /// nichts ueber Kampagne und Szenenwechsel wissen muss.</summary>
    public Action? OnContinue;

    /// <summary>Was das X tut — im Original schliesst es das Fenster; bei uns
    /// heisst das »weiter zusehen«.</summary>
    public Action? OnClose;

    public MissionEndWindow()
    {
        MouseFilter = MouseFilterEnum.Stop;
        // ⚠ Always: das Fenster kommt haeufig bei angehaltenem Baum hoch —
        // dieselbe Falle, die den SettingsScreen am 10.08. gekostet hat.
        ProcessMode = ProcessModeEnum.Always;

        var frame = new StyleBoxFlat
        {
            BgColor = WinBg, BorderColor = WinEdge,
            ContentMarginLeft = 3, ContentMarginRight = 3,
            ContentMarginTop = 3, ContentMarginBottom = 3,
        };
        frame.SetBorderWidthAll(2);
        AddThemeStyleboxOverride("panel", frame);

        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 0);
        AddChild(col);

        // ---- Titelleiste ---------------------------------------------------
        var bar = new PanelContainer();
        var barBox = new StyleBoxFlat
        {
            BgColor = BarBg,
            ContentMarginLeft = 6, ContentMarginRight = 4,
            ContentMarginTop = 2, ContentMarginBottom = 2,
        };
        bar.AddThemeStyleboxOverride("panel", barBox);
        col.AddChild(bar);

        var barRow = new HBoxContainer();
        bar.AddChild(barRow);
        _title.Text = "MISSION ERFOLGREICH BEENDET";
        _title.AddThemeColorOverride("font_color", TitleFg);
        _title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        barRow.AddChild(_title);
        _x.Text = "X";
        _x.CustomMinimumSize = new Vector2(22, 18);
        _x.FocusMode = FocusModeEnum.None;
        _x.AddThemeColorOverride("font_color", new Color(1f, 0.85f, 0.85f));
        _x.Pressed += () => OnClose?.Invoke();
        barRow.AddChild(_x);

        // ---- Inhalt --------------------------------------------------------
        var pad = new MarginContainer();
        pad.AddThemeConstantOverride("margin_left", 14);
        pad.AddThemeConstantOverride("margin_right", 14);
        pad.AddThemeConstantOverride("margin_top", 10);
        pad.AddThemeConstantOverride("margin_bottom", 8);
        col.AddChild(pad);

        var body = new VBoxContainer();
        body.AddThemeConstantOverride("separation", 8);
        pad.AddChild(body);

        _mission.HorizontalAlignment = HorizontalAlignment.Center;
        _mission.AddThemeColorOverride("font_color", MissionFg);
        _mission.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        body.AddChild(_mission);

        // Die Tabelle sitzt im Original in einem schwarzen, eingelassenen
        // Kasten — nicht auf dem Fensterhintergrund.
        var tableBox = new PanelContainer();
        var tb = new StyleBoxFlat
        {
            BgColor = TableBg, BorderColor = new Color(0.16f, 0.16f, 0.15f),
            ContentMarginLeft = 8, ContentMarginRight = 8,
            ContentMarginTop = 4, ContentMarginBottom = 4,
        };
        tb.SetBorderWidthAll(1);
        tableBox.AddThemeStyleboxOverride("panel", tb);
        body.AddChild(tableBox);

        // Spalte 0 sind die Zeilennamen, danach die acht Spielerspalten.
        _grid.Columns = 1 + MapEntityLayer.EndColumnCount;
        _grid.AddThemeConstantOverride("h_separation", 14);
        _grid.AddThemeConstantOverride("v_separation", 0);
        tableBox.AddChild(_grid);

        foreach (var l in new[] { _timeL, _builtL, _scoreL, _subL })
        {
            l.HorizontalAlignment = HorizontalAlignment.Center;
            l.AddThemeColorOverride("font_color", PlainFg);
            l.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        }
        var mid = new VBoxContainer();
        mid.AddThemeConstantOverride("separation", 0);
        foreach (var l in new[] { _timeL, _builtL, _scoreL, _subL }) mid.AddChild(l);
        body.AddChild(mid);

        _payL.HorizontalAlignment = HorizontalAlignment.Center;
        _payL.AddThemeColorOverride("font_color", PayFg);
        _payL.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        body.AddChild(_payL);

        _balL.HorizontalAlignment = HorizontalAlignment.Center;
        _balL.AddThemeColorOverride("font_color", PlainFg);
        _balL.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        body.AddChild(_balL);

        _go.Text = "Weiter";
        _go.CustomMinimumSize = new Vector2(0, 26);
        _go.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _go.Pressed += () => OnContinue?.Invoke();
        body.AddChild(_go);
    }

    /// <summary>Die Schrift des Spiels (FONT.CWD als BMFont) auf das ganze
    /// Fenster legen. Ohne sie steht die Abrechnung in Godots Standardschrift,
    /// und das Fenster gehoert zu den wenigen, die der Spieler in Ruhe
    /// liest.</summary>
    public void SetFont(Font font, int size)
    {
        _font = font; _fontSize = size;
        foreach (var n in Walk(this))
        {
            n.AddThemeFontOverride("font", font);
            n.AddThemeFontSizeOverride("font_size", size);
        }
    }

    // ⚠ Gemerkt, nicht nur gesetzt: die Tabellenfelder entstehen erst in
    // Fill(), also NACH SetFont(). Der erste Anlauf legte die Schrift nur auf
    // die schon vorhandenen Beschriftungen — die Tabelle stand danach als
    // einziger Teil des Fensters in Godots Standardschrift.
    private Font? _font;
    private int _fontSize;

    private static System.Collections.Generic.IEnumerable<Control> Walk(Node n)
    {
        foreach (var c in n.GetChildren())
        {
            if (c is Label or Button) yield return (Control)c;
            foreach (var d in Walk(c)) yield return d;
        }
    }

    /// <summary>Fenster fuellen. <paramref name="continueLabel"/> ist die
    /// Beschriftung des unteren Knopfs (im Original immer »Weiter«),
    /// <paramref name="continueHint"/> das Zeigefaehnchen, das sagt, wohin er
    /// fuehrt — das ist UNSERE Zutat und steht bewusst nicht auf dem Knopf.
    /// </summary>
    public void Fill(MapEntityLayer.EndReport r, bool won, string missionName,
                     string continueLabel, string continueHint = "")
    {
        // ⚠ Der Titel bei Misserfolg ist unserer (siehe Klassenkopf).
        _title.Text = won ? "MISSION ERFOLGREICH BEENDET" : "MISSION GESCHEITERT";
        _mission.Text = missionName;
        _mission.AddThemeColorOverride("font_color", MissionFg);

        foreach (var c in _grid.GetChildren()) c.QueueFree();

        // Kopfzeile: leere Ecke, dann je Spalte der Name. Belegte Spalten
        // tragen ihre Farbe (Mensch blau, CPU gruen), leere bleiben leer —
        // im Original steht ueber den sechs "--/--"-Spalten nichts.
        _grid.AddChild(Cell("", PlainFg, HorizontalAlignment.Left));
        foreach (var c in r.Columns)
            _grid.AddChild(Cell(c.Used ? c.Name.ToUpper() : "",
                                c.Human ? HumanFg : CpuFg));

        for (int k = 0; k < RowNames.Length; k++)
        {
            _grid.AddChild(Cell(RowNames[k], PlainFg, HorizontalAlignment.Left));
            foreach (var c in r.Columns)
                _grid.AddChild(c.Used
                    ? Cell($"{c.Alive[k]}/{c.Lost[k]}", c.Human ? HumanFg : CpuFg)
                    // Eine Spalte ohne Spieler bleibt "--/--" — das ist die
                    // Schreibweise des Originals fuer »hier steht nichts«, und
                    // sie ist besser als eine erfundene Null.
                    : Cell("--/--", EmptyFg));
        }

        int secs = Mathf.Max(0, (int)r.Seconds);
        _timeL.Text = $"Missionszeit : {secs / 60:00}:{secs % 60:00}";
        _builtL.Text = $"Gebaute Einheiten {r.Built}";
        _scoreL.Text = $"Ausgeschaltete {r.Kills} / Verluste {r.Losses}";
        // Ohne Missionsskript gibt es keine Untermissionen — dann faellt die
        // Zeile weg, statt "0 / 0" zu behaupten.
        _subL.Visible = r.SubTotal >= 0;
        if (r.SubTotal >= 0) _subL.Text = $"Untermissionen {r.SubDone} / {r.SubTotal}";
        _payL.Text = $"Missionsbezahlung ${r.Pay}";
        _balL.Text = $"Kontostand ${r.Balance}";
        _go.Text = continueLabel;
        _go.TooltipText = continueHint;
    }

    private Label Cell(string text, Color fg,
                       HorizontalAlignment align = HorizontalAlignment.Center)
    {
        var l = new Label { Text = text, HorizontalAlignment = align };
        l.AddThemeColorOverride("font_color", fg);
        if (_font != null)
        {
            l.AddThemeFontOverride("font", _font);
            l.AddThemeFontSizeOverride("font_size", _fontSize);
        }
        return l;
    }
}
