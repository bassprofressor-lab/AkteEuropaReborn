namespace AkteEuropaReborn.UI;

using System;
using System.Collections.Generic;
using Godot;

/// <summary>
/// Das Baumenü des Gefechts — das frei schwebende Fenster, das im Original
/// aufgeht, wenn man eine Basis anklickt.
///
/// <para><b>Gemeldet am 11.08.2026:</b> »das Bau Menu im Gefecht, das ist nicht
/// das Originale.« Es war es auch nicht: die Bauliste stand als nackte
/// Textliste im eingelassenen Kasten des Bedienfelds (siehe
/// <see cref="BuildPanel"/>) — kein Fenster, keine Reiter, keine Werte, keine
/// Knöpfe, und die Preise waren auf »20/40« abgeschnitten.</para>
///
/// <para><b>Wonach dieses Fenster gebaut ist.</b> Nach dem Bildschirmfoto des
/// Originals (akte-europa_8.png, 1280×1024) und nach den Zeichenketten und
/// Fundstellen in GAME.EXE. Von oben:</para>
/// <list type="number">
/// <item>Titelleiste mit Namen und X.</item>
/// <item><c>"Energie : "</c> (0x501790) und ein Balken.</item>
/// <item><c>"Status : aktiv"</c> (0x501a98; daneben liegen »forschen«
/// 0x501a50, »vergrössern« 0x501a68, »reparieren« 0x501a80, »angehalten«
/// 0x501bc4 — die fünf Zustände, die das Original kennt).</item>
/// <item>Vier Reiter: <c>Depot</c> (0x501a48), <c>Produktion</c> (0x501934),
/// <c>Forschung</c> (0x501a3c), <c>Reparatur</c> (0x501a30). Das Original
/// setzt sie auf x = 20 / 100 / 180 / 260 bei y = 55, gezeichnet über
/// 0x401820.</item>
/// <item>Ein schwarzer Listenkasten. Die Zeilen stehen im Original auf
/// <b>x = 32, y = 90 + 15·Zeile</b> (@0x46829F: <c>lea edx,[ebx+ebx*2+0x12]</c>
/// dann <c>lea eax,[edx+edx*4]</c>, also 15·Zeile + 90).</item>
/// <item>Darunter die drei Teilebestände des Gebäudes.</item>
/// <item>Vier Knöpfe in zwei Reihen: <c>Umbenennen</c> (0x5019dc),
/// <c>Entfernen</c> (0x5019d0), <c>Produzieren</c> (0x501780),
/// <c>Erstellen</c> (0x5019c4).</item>
/// <item>Rechts ein Vorschaufeld und die Werteliste — siehe
/// <see cref="UnitStatBook"/>, dort stehen Beschriftungen und Reihenfolge mit
/// ihren Fundstellen.</item>
/// </list>
///
/// <para><b>Die drei Sinnbilder der Rohstoffe sind keine Erfindung.</b> Das
/// Original schreibt sie als gewöhnliche Zeichen in die Zeile und lässt die
/// Schrift den Rest tun: <c>"Waffen gelagert : ]"</c> @0x501c70,
/// <c>"Fahrwerke gelagert : ["</c> @0x501c54, <c>"Spezialteile gelagert : {"</c>
/// @0x501c34, und der Preisdrucker @0x46ee01/08/0f hängt <c>" ]"</c>,
/// <c>" ["</c>, <c>" {"</c> in genau dieser Reihenfolge aneinander. In
/// FONT.CWD sind <c>]</c>, <c>[</c> und <c>{</c> keine Klammern, sondern die
/// drei Teilesymbole (und <c>$</c> der Geldsack) — nachgesehen in
/// <c>UI/akte_font.png</c>. Wir schreiben also dieselben Zeichen und bekommen
/// dieselben Bilder.</para>
///
/// <para>⚠ <b>UNSERE Setzungen, ausdrücklich:</b></para>
/// <list type="bullet">
/// <item>Die <b>Farbwerte und die Rahmenform</b>, wie schon im
/// <see cref="MissionEndWindow"/> — das Original zeichnet aus einer .PIC.</item>
/// <item>Die <b>Schriftgrösse</b>: 13 px × 2 wie überall in dieser Oberfläche,
/// also doppelt so gross wie im Original von 1997. Das Fenster ist damit
/// verhältnismässig grösser als dort.</item>
/// <item>Die <b>Titelzeile</b>: das Original schreibt den Gebäudenamen und
/// seine Nummer hinein (»Basis 2«). Die kennen wir hier nicht — das Fenster
/// bekommt von aussen nur, was <c>MapEntityLayer.BuildPanelTitle()</c> liefert,
/// und das ist das WORT des Reiters. Bis eine Auskunft über das gewählte
/// Gebäude da ist (siehe <see cref="Head"/>), steht dieses Wort in der
/// Leiste, und Energiebalken und Statuszeile bleiben weg, statt eine leere
/// Anzeige vorzutäuschen.</item>
/// <item>Die drei Reiter <b>Depot, Forschung, Reparatur</b> sind gezeichnet,
/// aber leer: was in ihnen steht, ist noch nicht gelesen bzw. nicht
/// angeschlossen. Sie sagen das selbst, statt etwas zu behaupten.</item>
/// <item><b>Umbenennen</b> und <b>Entfernen</b> sind aus demselben Grund
/// gesperrt: die Entwurfsliste liegt in <c>MapEntityLayer</c> und lässt sich
/// von aussen nicht ändern.</item>
/// <item>Dass man das Fenster an der Titelleiste <b>ziehen</b> kann. Im
/// Original schweben die Fenster frei; ob sie sich ziehen lassen, ist
/// ungelesen.</item>
/// </list>
/// </summary>
public sealed partial class BaseWindow : PanelContainer
{
    // ---- die Wörter des Originals ------------------------------------------
    private static readonly string[] TabWords =
        { "Depot", "Produktion", "Forschung", "Reparatur" };

    /// <summary>Die Sinnbilder aus FONT.CWD: Waffenteil, Fahrwerksteil,
    /// Spezialteil — in der Reihenfolge des Preisdruckers @0x46ee01.</summary>
    public const string IconW = "]", IconF = "[", IconS = "{";

    // ---- Farben (⚠ unsere Werte, nach dem Bildschirmfoto) -------------------
    private static readonly Color WinBg = new(0.30f, 0.28f, 0.25f);
    private static readonly Color WinEdge = new(0.55f, 0.51f, 0.45f);
    private static readonly Color BarBg = new(0.22f, 0.20f, 0.18f);
    private static readonly Color TitleFg = new(0.88f, 0.86f, 0.80f);
    private static readonly Color PlainFg = new(0.84f, 0.83f, 0.76f);
    private static readonly Color BoxBg = new(0.02f, 0.02f, 0.02f);
    private static readonly Color RowFg = new(0.78f, 0.82f, 0.85f);
    private static readonly Color RowSelFg = new(1f, 1f, 1f);
    private static readonly Color RowSelBg = new(0.24f, 0.30f, 0.24f);
    private static readonly Color RowDearFg = new(0.62f, 0.42f, 0.38f);
    private static readonly Color IconFg = new(0.85f, 0.20f, 0.14f);
    private static readonly Color EnergyFg = new(0.85f, 0.85f, 0.82f);
    private static readonly Color HintFg = new(0.55f, 0.54f, 0.50f);

    // ---- was das Fenster von aussen braucht ---------------------------------

    /// <summary>Die Bauliste — <c>MapEntityLayer.BuildPanelRows</c>.</summary>
    public Func<List<BuildPanel.Row>>? Rows;

    /// <summary>Die Kopfzeile, die <c>MapEntityLayer.BuildPanelTitle()</c>
    /// liefert. Daraus kommen die drei Teilebestände bzw. der Kontostand.
    /// </summary>
    public Func<string>? TitleLine;

    /// <summary>Eine Zeile bestellen — <c>MapEntityLayer.BuildPanelPick</c>.
    /// </summary>
    public Action<int>? Produce;

    /// <summary>Der Knopf »Erstellen«.</summary>
    public Action? OnDesign;

    /// <summary>Reiter »Forschung« — was das gewählte Gebäude gerade erforscht,
    /// und der Knopf, der es anstösst
    /// (<c>MapEntityLayer.ResearchNote</c>/<c>StartResearch</c>). ⚠ Bis zum
    /// 17.08.2026 war dieser Reiter leer und sagte »noch nicht angeschlossen«,
    /// obwohl die Mechanik seit langem auf Taste O lag — siehe Fehler C2.</summary>
    public Func<string>? ResearchNote;
    public Action? OnResearch;

    /// <summary>Reiter »Reparatur«, dasselbe für <c>StartRepair</c> (Taste K).
    /// ⚠ Es gibt dafür KEINE Einheit und braucht auch keine: das Gebäude
    /// repariert sich selbst, +1 Trefferpunkt je viertem Takt. Fehler C6.</summary>
    public Func<string>? RepairNote;
    public Action? OnRepair;

    /// <summary>Was im Depot dieses Gebäudes liegt — eine Zeile je Einheit.
    /// ⚠ Bis zum 16.08.2026 sagte der Reiter wörtlich »noch nicht
    /// angeschlossen«, und der Spieler kam an seine fertigen Einheiten nicht
    /// heran. Siehe <c>MapEntityLayer.Entity.Depot</c>.</summary>
    public Func<List<BuildPanel.Row>>? DepotRows;

    /// <summary>»Aussenden« — Zeile <c>k</c> aus dem Depot auf die Karte
    /// stellen. Der Knopf des Originals sitzt unten links (20,230).</summary>
    public Action<int>? OnSendOut;

    /// <summary>»Recycle« — Zeile <c>k</c> aus dem Depot verwerten. Der Knopf
    /// des Originals sitzt unten rechts (140,255) und schickt Befehl <b>506</b>
    /// an <c>0x4B28E0</c>. ⚠ Er zahlt in <b>Teilen</b> in die drei Lager DIESES
    /// Gebäudes, nicht in Geld — siehe
    /// <c>MapEntityLayer.RecycleFromPanel</c>.</summary>
    public Action<int>? OnRecycle;

    /// <summary>»Transportieren« — Zeile <c>k</c> aus dem Depot ueber die Bahn
    /// in ein anderes Gebaeude verlegen. Der Knopf des Originals sitzt unten
    /// links (20,255). ⚠ Er ist KEIN Befehl: er verlangt erst ein Ziel, dann
    /// prueft er die Verbindung (0x4497D0) und schickt Befehl 0x206.</summary>
    public Action<int>? OnTransport;

    /// <summary>Wartet gerade ein Transport auf sein Ziel?</summary>
    public Func<bool>? TransportArmed;

    /// <summary>Ist das gewählte Gebäude ein MARKT? Dann heisst der Knopf
    /// »Bestellen« und die Liste ist die Ware, nicht die Bauliste.</summary>
    public Func<bool>? IsMarket;

    /// <summary>Das X.</summary>
    public Action? OnClose;

    /// <summary>Name, Energie und Zustand des gewählten Gebäudes. Noch von
    /// niemandem gesetzt — solange bleibt der Kopf des Fensters weg, siehe
    /// Klassenkopf. Der Bauplatz dafür ist Absicht: sobald
    /// <c>MapEntityLayer</c> die Auskunft herausgibt, ist das Fenster mit einer
    /// Zeile fertig angeschlossen.</summary>
    public Func<(string Name, int Hp, int HpMax, string Status)?>? Head;

    /// <summary>Was die zwei Ausbauten gerade kosten und ob sie überhaupt gehen
    /// — <c>null</c> blendet beide Knöpfe aus (das gewählte Gebäude ist keine
    /// Fabrik). ⚠ Der Preis steht IM Knopf, wie beim Verkauf: er wächst mit
    /// jedem Ausbau um die Hälfte, und das soll man sehen, bevor man drückt.
    /// </summary>
    public Func<(int CostStore, int CostProd, bool Ready)?>? UpgradeChoice;

    /// <summary>Ausbau anwerfen — <c>true</c> Lagerausbau, <c>false</c>
    /// Produktionserweiterung.</summary>
    public System.Action<bool>? OnUpgrade;

    /// <summary>Was gerade abgebrochen werden könnte — <c>null</c> blendet den
    /// Knopf aus. ⚠ <b>Bis zum 18.08.2026 lag das nur auf Umschalt+B</b>, und
    /// ein Abbruch, den niemand findet, ist keiner. Der Preis wird
    /// zurückgezahlt, deshalb steht er im Knopf.</summary>
    public Func<string?>? CancelChoice;

    /// <summary>Den laufenden Bau bzw. den letzten Eintrag der Warteschlange
    /// abbrechen.</summary>
    public System.Action? OnCancelBuild;

    /// <summary>Wieviele Flugzeuge im Hangar stehen — <c>null</c> blendet den
    /// Knopf aus. ⚠ Auch das lag nur auf einer Taste; die Bestandszeile des
    /// Flughafens sagte dem Spieler wörtlich »(Y startet)«, und genau das ist
    /// das Eingeständnis, dass der Knopf fehlte.</summary>
    public Func<int?>? HangarCount;

    /// <summary>Alles starten, was im Hangar steht.</summary>
    public System.Action? OnLaunch;

    // ---- Bauteile des Fensters ----------------------------------------------
    private readonly Label _title = new();
    private readonly Button _x = new();
    private readonly HBoxContainer _energyRow = new();
    private readonly Label _energyLabel = new();
    private readonly EnergyBar _energy = new();
    private readonly Label _status = new();
    private readonly Button[] _tabs = new Button[TabWords.Length];
    private readonly Sheet _sheet = new();
    private readonly Label _stock = new();
    private readonly Button _rename = new(), _remove = new(), _make = new(), _design = new();

    /// <summary>Die zwei Fabrikausbauten. ⚠ <b>Sie lagen bis zum 18.08.2026 NUR
    /// auf den Tasten V und C</b> — und damit war die ganze Mechanik für den
    /// Spieler nicht vorhanden. Das ist derselbe Fehler wie bei Forschung,
    /// Reparatur, Depot, Hangar und dem Verkauf; er ist damit der sechste
    /// Fall, siehe UI/UnitOrderBar.cs.
    ///
    /// <para>⚠ <b>Die WÖRTER sind die des Originals</b> (0x501B30 und
    /// 0x501B40 — dieselben, die das Fabrikfenster als Zustand ausgibt, siehe
    /// <c>MapEntityLayer.StateName</c>). Unser ist nur die STELLE: ein fünfter
    /// Reiter wäre eine Erfindung gewesen, denn die vier Reiterwörter sind
    /// gelesen und ein fünftes gibt es nicht.</para></summary>
    private readonly Button _expand = new(), _prodUp = new();

    /// <summary>»Abbrechen« und »Starten«. ⚠ Derselbe Fall wie die zwei
    /// Ausbauten: beide Mechaniken waren fertig und lagen nur auf Umschalt+B
    /// bzw. Y. Siehe <see cref="CancelChoice"/> und
    /// <see cref="HangarCount"/>.</summary>
    private readonly Button _cancel = new(), _launch = new(), _recycle = new(), _transport = new();
    private readonly Preview _preview = new();
    private readonly Label _stats = new();

    private int _tab = 1;                       // »Produktion« ist der brauchbare

    public BaseWindow()
    {
        MouseFilter = MouseFilterEnum.Stop;
        // ⚠ Always: dasselbe wie beim Abschlussfenster — das Baumenü muss auch
        // bei angehaltenem Baum bedienbar bleiben.
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
        bar.MouseFilter = MouseFilterEnum.Stop;
        bar.GuiInput += DragBar;
        col.AddChild(bar);

        var barRow = new HBoxContainer();
        bar.AddChild(barRow);
        _title.Text = "Basis";
        _title.AddThemeColorOverride("font_color", TitleFg);
        _title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _title.MouseFilter = MouseFilterEnum.Ignore;
        barRow.AddChild(_title);
        _x.Text = "X";
        _x.CustomMinimumSize = new Vector2(22, 18);
        _x.FocusMode = FocusModeEnum.None;
        _x.AddThemeColorOverride("font_color", new Color(1f, 0.72f, 0.68f));
        _x.Pressed += () => OnClose?.Invoke();
        barRow.AddChild(_x);

        // ---- Inhalt --------------------------------------------------------
        var pad = new MarginContainer();
        pad.AddThemeConstantOverride("margin_left", 8);
        pad.AddThemeConstantOverride("margin_right", 8);
        pad.AddThemeConstantOverride("margin_top", 6);
        pad.AddThemeConstantOverride("margin_bottom", 6);
        col.AddChild(pad);

        var body = new VBoxContainer();
        body.AddThemeConstantOverride("separation", 4);
        pad.AddChild(body);

        // »Energie : « und der Balken (bleiben weg, solange niemand die Werte
        // liefert — siehe Head)
        _energyLabel.Text = "Energie :";
        _energyLabel.AddThemeColorOverride("font_color", PlainFg);
        _energyRow.AddThemeConstantOverride("separation", 8);
        _energyRow.AddChild(_energyLabel);
        _energy.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _energy.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        _energyRow.AddChild(_energy);
        _energyRow.Visible = false;
        body.AddChild(_energyRow);

        _status.Text = "Status : aktiv";
        _status.AddThemeColorOverride("font_color", PlainFg);
        _status.Visible = false;
        body.AddChild(_status);

        // ---- die vier Reiter ------------------------------------------------
        var tabRow = new HBoxContainer();
        tabRow.AddThemeConstantOverride("separation", 4);
        body.AddChild(tabRow);
        for (int i = 0; i < TabWords.Length; i++)
        {
            int which = i;
            var b = new Button
            {
                Text = TabWords[i],
                ToggleMode = true,
                FocusMode = FocusModeEnum.None,
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
            };
            b.Pressed += () => SetTab(which);
            tabRow.AddChild(b);
            _tabs[i] = b;
        }

        // ---- Liste links, Vorschau und Werte rechts --------------------------
        var mid = new HBoxContainer();
        mid.AddThemeConstantOverride("separation", 8);
        body.AddChild(mid);

        var left = new VBoxContainer();
        left.AddThemeConstantOverride("separation", 4);
        left.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        mid.AddChild(left);

        var listBox = new PanelContainer();
        var lb = new StyleBoxFlat
        {
            BgColor = BoxBg, BorderColor = new Color(0.16f, 0.16f, 0.15f),
            ContentMarginLeft = 2, ContentMarginRight = 2,
            ContentMarginTop = 2, ContentMarginBottom = 2,
        };
        lb.SetBorderWidthAll(1);
        listBox.AddThemeStyleboxOverride("panel", lb);
        listBox.SizeFlagsVertical = SizeFlags.ExpandFill;
        left.AddChild(listBox);
        _sheet.OnPick = i => { _sheet.Selected = i; Refresh(); };
        _sheet.OnConfirm = i => { Produce?.Invoke(i); Refresh(); };
        listBox.AddChild(_sheet);

        // die Teilebestände des Gebäudes, in derselben Schreibweise wie die
        // Preise darüber
        _stock.AddThemeColorOverride("font_color", PlainFg);
        _stock.HorizontalAlignment = HorizontalAlignment.Center;
        left.AddChild(_stock);

        var keys = new GridContainer { Columns = 2 };
        keys.AddThemeConstantOverride("h_separation", 6);
        keys.AddThemeConstantOverride("v_separation", 4);
        left.AddChild(keys);
        foreach (var (b, text) in new[]
                 { (_rename, "Umbenennen"), (_remove, "Entfernen"),
                   (_make, "Produzieren"), (_design, "Erstellen"),
                   (_expand, "Lagerausbau"), (_prodUp, "Produktionserw."),
                   (_cancel, "Abbrechen"), (_launch, "Starten"),
                   (_recycle, "Recycle"), (_transport, "Transportieren") })
        {
            b.Text = text;
            b.FocusMode = FocusModeEnum.None;
            b.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            keys.AddChild(b);
        }
        // Was wir wirklich können, kann man drücken; der Rest sagt, warum nicht.
        _rename.Disabled = true;
        _rename.TooltipText = "noch nicht angeschlossen — die Entwurfsliste liegt " +
                              "in MapEntityLayer und ist von aussen nicht änderbar";
        _remove.Disabled = true;
        _remove.TooltipText = _rename.TooltipText;
        // ⚠ 17.08.2026 — DER KNOPF GEHÖRT JETZT DEM REITER (Fehler C2 und C6).
        // Er hiess fest »Produzieren« und rief fest die Produktion; die Reiter
        // »Forschung« und »Reparatur« waren gezeichnet und tot. Beide Mechaniken
        // sind seit langem GEBAUT (MapEntityLayer.StartResearch bzw.
        // StartRepair, Tasten O und K) und waren nur über die Oberfläche nicht
        // erreichbar — der Spieler hat genau danach gefragt: »Wo kann ich denn
        // Forschen?« und »Beschädigte Gebäude reparieren wäre gut. Welche
        // Einheit bietet sich da an?«.
        //
        // ⚠ Die Antwort auf die zweite Frage ist ausdrücklich KEINE Einheit: das
        // Gebäude repariert sich selbst. Genau deshalb muss der Knopf am
        // Gebäudefenster sitzen und nicht an einer Einheit.
        _make.Pressed += () =>
        {
            switch (_tab)
            {
                case 0: OnSendOut?.Invoke(_sheet.Selected); break;
                case 2: OnResearch?.Invoke(); break;
                case 3: OnRepair?.Invoke(); break;
                default:
                    if (_sheet.Selected >= 0) Produce?.Invoke(_sheet.Selected);
                    break;
            }
            Refresh();
        };
        _design.Pressed += () => OnDesign?.Invoke();
        _expand.Pressed += () => { OnUpgrade?.Invoke(true); Refresh(); };
        _prodUp.Pressed += () => { OnUpgrade?.Invoke(false); Refresh(); };
        _cancel.Pressed += () => { OnCancelBuild?.Invoke(); Refresh(); };
        _launch.Pressed += () => { OnLaunch?.Invoke(); Refresh(); };
        _recycle.Pressed += () => { OnRecycle?.Invoke(_sheet.Selected); Refresh(); };
        _transport.Pressed += () => { OnTransport?.Invoke(_sheet.Selected); Refresh(); };

        var right = new VBoxContainer();
        right.AddThemeConstantOverride("separation", 4);
        mid.AddChild(right);
        right.AddChild(_preview);
        _stats.AddThemeColorOverride("font_color", PlainFg);
        _stats.HorizontalAlignment = HorizontalAlignment.Right;
        _stats.SizeFlagsVertical = SizeFlags.ExpandFill;
        right.AddChild(_stats);

        SetTab(1);
    }

    // ---- Schrift ------------------------------------------------------------

    /// <summary>Die Schrift des Spiels auf das ganze Fenster legen. Ohne sie
    /// stünden die drei Teilesymbole als Klammern da — sie SIND Zeichen dieser
    /// Schrift, siehe Klassenkopf.</summary>
    public void SetFont(Font font, int size)
    {
        _font = font; _fontSize = size;
        foreach (var n in Walk(this))
        {
            n.AddThemeFontOverride("font", font);
            n.AddThemeFontSizeOverride("font_size", size);
        }
        _sheet.SetFont(font, size);
        _energy.CustomMinimumSize = new Vector2(size * 8, Mathf.Max(6, size / 3));
        // Das Vorschaufeld ist im Original 60×60 Punkte (der Kasten
        // 0x456A50(…,3,3) = 3 Zellen von 20 px) und damit genau so gross wie
        // ein Bild der Bank — mit der Schrift mitskaliert, aber quadratisch.
        float side = PortraitBank.Box * (size / 13f);
        _preview.CustomMinimumSize = new Vector2(side, side);
        // Der Listenkasten des Originals fasst zwölf Zeilen; die Zeilenhöhe ist
        // seine eigene (15 px), hier mit der Schrift mitskaliert.
        _sheet.CustomMinimumSize = new Vector2(size * 14f, _sheet.Step * 12f);
        Refresh();
    }

    private Font? _font;
    private int _fontSize = 13;

    private static IEnumerable<Control> Walk(Node n)
    {
        foreach (var c in n.GetChildren())
        {
            if (c is Label or Button) yield return (Control)c;
            foreach (var d in Walk(c)) yield return d;
        }
    }

    // ---- Reiter -------------------------------------------------------------

    /// <summary>
    /// <b>⚠⚠ DAS REITERBAND SCHALTET ZUGLEICH DEN GEBÄUDEZUSTAND</b> — gelesen
    /// am 20.08.2026 aus dem Klickblock der Fensterart 6 (C <c>0x44A097</c>,
    /// Tafel <c>0x44DE98</c>) und Zeile für Zeile nachgemessen:
    ///
    /// <code>
    ///   cmp bp, 4
    ///   jne 0x44A167
    ///     dl = byte[16·Lagerplatz + 0x878E5A]
    ///     if (dl == 1) -> nichts               ; repariert schon
    ///     word[0xB8A3D8] = 0x209               ; Befehl 521 — REPARIEREN
    ///   0x44A167:                              ; Elemente 1, 2, 3
    ///     word[0xB8A3D8] = 0x20D               ; Befehl 525 — Status 0
    /// </code>
    ///
    /// <para><b>»Reparatur« ist kein Reiter.</b> Der vierte Knopf hat keinen
    /// eigenen Inhalt — der Zeichner fragt Merker #4 nirgends ab (nur #1
    /// @0x467F7D, #2 @0x468EE0, #3 @0x46A273). Er stösst die Reparatur an und
    /// sonst nichts; angezeigt bleibt, was vorher zu sehen war.</para>
    ///
    /// <para><b>Und die anderen drei brechen sie ab.</b> Befehl 525 setzt
    /// <c>byte[0x878E5A + 16·L] = 0</c> (Behandler <c>0x43FF10</c>), Befehl 521
    /// setzt ihn auf 1 (<c>0x43FD10</c>) — letzteres nur, wenn die Energie
    /// unter dem Höchstwert liegt. Ein Reiterwechsel während einer laufenden
    /// Reparatur hält sie also an. Das ist im Original so, und es ist keine
    /// Nebenwirkung, sondern derselbe Klickpfad.</para>
    ///
    /// <para>⚠ Bei uns war »Reparatur« bis heute ein vierter Reiter mit
    /// eigenem Inhalt und einem Knopf darin — man musste zweimal klicken, und
    /// ein Reiterwechsel brach nichts ab. Beides ist jetzt richtig herum.</para>
    /// </summary>
    private void SetTab(int which)
    {
        // Der vierte »Reiter« ist der Reparaturknopf: anstossen und den
        // ANGEZEIGTEN Reiter lassen, wo er war.
        if (which == RepairTab)
        {
            _tabs[RepairTab].ButtonPressed = false;
            for (int i = 0; i < _tabs.Length; i++) _tabs[i].ButtonPressed = i == _tab;
            OnRepair?.Invoke();
            Refresh();
            return;
        }
        // Jeder andere Reiter haelt eine laufende Reparatur an — Befehl 525.
        if (which != _tab) OnRepairStop?.Invoke();
        _tab = which;
        for (int i = 0; i < _tabs.Length; i++) _tabs[i].ButtonPressed = i == which;
        Refresh();
    }

    /// <summary>Die Stelle des Reparaturknopfes im Reiterband — er ist der
    /// vierte, aber kein Reiter. Siehe <see cref="SetTab"/>.</summary>
    private const int RepairTab = 3;

    /// <summary>Befehl <b>525</b>: die laufende Reparatur anhalten. Wird von
    /// jedem Klick auf Depot, Produktion oder Forschung ausgelöst.</summary>
    public Action? OnRepairStop;

    // ---- Füllen -------------------------------------------------------------

    /// <summary>Fenster nachziehen. Läuft jeden Takt, solange es offen ist —
    /// Bestände und Bezahlbarkeit ändern sich von selbst.</summary>
    public void Refresh()
    {
        string head = TitleLine?.Invoke() ?? "";
        // Die Kopfzeile ist "PRODUKTION  W300 F400 S200" bzw.
        // "VERSORGUNGSDEPOT  $1200" — das erste Wort ist alles, was wir über das
        // Gebäude wissen (⚠ siehe Klassenkopf).
        int sp = head.IndexOf("  ", StringComparison.Ordinal);
        string word = sp > 0 ? head[..sp] : head;
        string tail = sp > 0 ? head[(sp + 2)..] : "";

        if (Head?.Invoke() is { } h)
        {
            _title.Text = h.Name;
            _energyRow.Visible = true;
            _energy.Set(h.Hp, h.HpMax);
            _status.Visible = true;
            _status.Text = "Status : " + h.Status;
        }
        else if (word.Length > 0)
        {
            _title.Text = word;
        }

        _stock.Text = StockLine(tail);

        var rows = _tab switch
        {
            1 => Rows?.Invoke() ?? new List<BuildPanel.Row>(),
            0 => DepotRows?.Invoke() ?? new List<BuildPanel.Row>(),
            _ => new List<BuildPanel.Row>(),
        };
        _sheet.Note = _tab switch
        {
            0 => DepotRows == null ? "Depot — noch nicht angeschlossen."
                 : rows.Count == 0 ? "Das Depot ist leer." : "",
            2 => ResearchNote?.Invoke() ?? "Forschung — noch nicht angeschlossen.",
            3 => RepairNote?.Invoke() ?? "Reparatur — noch nicht angeschlossen.",
            // ⚠ Eine FABRIK hat hier nichts stehen, und das ist kein Mangel:
            // sie stellt Teile her (W/F/S), keine Entwürfe. »Nichts zu bauen«
            // wäre an dieser Stelle eine Fehlauskunft — sie baut die ganze Zeit.
            _ => rows.Count == 0
                 ? (UpgradeChoice?.Invoke() != null
                    ? "Eine Fabrik stellt TEILE her,\nkeine Entwuerfe.\n\n" +
                      "Ihr Lager steht oben,\nausbauen kann man sie unten."
                    : "Nichts zu bauen.")
                 : "",
        };
        _sheet.Fill(rows);
        // ⚠ 17.08.2026 — die Beschriftung folgt dem Reiter. Ein Knopf, der auf
        // dem Forschungsreiter »Produzieren« heisst und forscht, wäre schlimmer
        // als ein toter Reiter.
        // ⚠ Am MARKT heisst der Knopf »Bestellen« — das Wort des Originals
        // (0x50223c), und er kauft statt zu bauen. Die uebrigen Reiter sind
        // dort ohne Sinn: ein Markt hat kein Depot, forscht nicht und
        // repariert nicht.
        bool markt = IsMarket?.Invoke() ?? false;
        _make.Text = markt ? "Bestellen" : _tab switch
        { 0 => "Aussenden", 2 => "Forschen", 3 => "Reparieren", _ => "Produzieren" };
        _make.Disabled = _tab switch
        {
            2 => OnResearch == null,
            3 => OnRepair == null,
            // Im Depot gibt es nichts zu bezahlen — nur etwas zu waehlen.
            0 => OnSendOut == null || _sheet.Selected < 0 ||
                 _sheet.Selected >= rows.Count,
            1 => _sheet.Selected < 0 || _sheet.Selected >= rows.Count ||
                 !rows[_sheet.Selected].Affordable,
            // (der Markt laeuft ueber denselben Reiter 1; die Zeile darueber
            //  prueft schon »bezahlbar«, und das ist am Markt der Kontostand)
            _ => true,
        };
        _design.Disabled = _tab != 1;

        // Die zwei Ausbauten. ⚠ Sie stehen NUR bei einer Fabrik da — ein Knopf,
        // der bei jedem Gebaeude erscheint und bei den meisten nichts tut, ist
        // schlimmer als keiner. `Ready` ist falsch, solange das Gebaeude schon
        // etwas anderes tut (Reparatur, ein laufender Ausbau); dann sind sie
        // sichtbar, aber gesperrt, damit man sieht, DASS es sie gibt.
        var up = UpgradeChoice?.Invoke();
        _expand.Visible = _prodUp.Visible = up != null;
        if (up != null)
        {
            _expand.Text = $"Lagerausbau (${up.Value.CostStore})";
            _prodUp.Text = $"Produktionserw. (${up.Value.CostProd})";
            _expand.Disabled = _prodUp.Disabled = !up.Value.Ready;
        }

        // »Abbrechen« steht nur da, wenn es etwas abzubrechen GIBT — die Zeile
        // sagt gleich, was. Ein immer sichtbarer Abbruchknopf, der meist
        // »nichts abzubrechen« antwortet, waere schlimmer als keiner.
        string? cancel = CancelChoice?.Invoke();
        _cancel.Visible = cancel != null;
        if (cancel != null) _cancel.Text = $"Abbrechen ({cancel})";

        // »Starten« nur am Flughafen, und nur wenn etwas im Hangar steht.
        // ⚠ »ALLE starten« sagt, was der Knopf tut, und grenzt ihn vom Reiter
        // »Depot« ab: dort wird EIN Flugzeug ausgesandt (»Aussenden«), hier
        // gehen alle auf einmal los. Zwei verschiedene Handlungen — ein Knopf,
        // der beides »Starten« hiesse, waere eine Falle.
        int? hangar = HangarCount?.Invoke();
        // »Recycle« steht NUR am Depotreiter — das Original hat ihn genau
        // dort, neben »Aussenden«. ⚠ Und nicht am Flughafen: dessen Liste ist
        // der Hangar, und fuer den kennt das Original kein Verwerten.
        _recycle.Visible = _tab == 0 && !markt && hangar is not > 0;
        _recycle.Disabled = OnRecycle == null || _sheet.Selected < 0 ||
                            _sheet.Selected >= rows.Count;

        // »Transportieren« steht neben »Recycle«, unter denselben Bedingungen.
        // ⚠ Wartet gerade eine Zielwahl, sagt die Beschriftung das — sonst
        // steckt der Spieler in einem Modus, ohne es zu wissen.
        _transport.Visible = _recycle.Visible;
        _transport.Disabled = OnTransport == null || _sheet.Selected < 0 ||
                              _sheet.Selected >= rows.Count;
        _transport.Text = TransportArmed?.Invoke() == true
                          ? "Ziel anklicken…" : "Transportieren";

        _launch.Visible = hangar is > 0;
        if (hangar is > 0) _launch.Text = $"Alle starten ({hangar})";

        string name = _sheet.Selected >= 0 && _sheet.Selected < rows.Count
            ? rows[_sheet.Selected].Name : "";
        _stats.Text = StatsFor(name);
        // Das Bild des markierten Entwurfs. ⚠ Der Zeichner des Originals hat
        // SECHS Fälle, und nur Fall 0 setzt sich aus Fahrwerk (sec47 +0x18) und
        // Aufbauteil (+0x17) zusammen. Ein Flugzeug und ein Fußsoldat bringen
        // je EIN fertiges Bild mit; die Zeile trägt es in `Pic`, weil nur sie
        // weiß, aus welchem Zweig sie kommt. Bis zum 16.08.2026 fragte diese
        // Stelle ausschließlich UnitStatBook — und das kennt weder Flugzeuge
        // noch Fußsoldaten, weshalb der Kasten dort leer blieb.
        int pic = _sheet.Selected >= 0 && _sheet.Selected < rows.Count
                ? rows[_sheet.Selected].Pic : 0;
        var teile = _sheet.Selected >= 0 && _sheet.Selected < rows.Count
                  ? (rows[_sheet.Selected].Chassis, rows[_sheet.Selected].Weapon) : (0, 0);
        if (pic > 0)
            _preview.SetPicture(pic);
        // ⚠ Die Teile der Zeile SCHLAGEN den Namensnachschlag: wer die Zeile
        // gebaut hat, kennt sie sicher, waehrend UnitStatBook einen Namen
        // braucht, den ein Marktsatz oft gar nicht hat.
        else if (teile.Item1 > 0 || teile.Item2 > 0)
            _preview.Set(teile.Item1, teile.Item2);
        else if (name.Length > 0 && UnitStatBook.TryGet(name, out var pe))
            _preview.Set(pe.Propulsion, pe.Weapon);
        else
            _preview.Set(0, 0);
        _preview.Visible = _tab == 1;

        PlaceTopRight();
    }

    /// <summary>Die Bestandszeile unter der Liste. Aus der Kopfzeile
    /// "W300 F400 S200" werden die drei Sinnbilder des Originals; ein
    /// Kontostand ("$1200") bleibt, wie er ist — das Geldzeichen ist in dieser
    /// Schrift ebenfalls ein Bild.</summary>
    private static string StockLine(string tail)
    {
        if (tail.Length == 0) return "";
        if (tail.StartsWith("$", StringComparison.Ordinal)) return tail;
        int w = Num(tail, 'W'), f = Num(tail, 'F'), s = Num(tail, 'S');
        if (w < 0 && f < 0 && s < 0) return tail;
        // ⚠ Was hinter »Platz« steht, bleibt STEHEN. Die drei Sinnbilder sind
        // die Form des Originals, aber die Fabrik bringt zwei Zahlen mit, die
        // es dort nicht gibt und die man braucht: der Lagerausbau hebt den
        // Platz, die Produktionserweiterung das Tempo — ohne sie sähe man nicht,
        // wofür man bezahlt hat. Ohne diese Zeile fielen sie hier heraus.
        int extra = tail.IndexOf("Platz", StringComparison.Ordinal);
        return $"{IconW}{Mathf.Max(0, w)} {IconF}{Mathf.Max(0, f)} {IconS}{Mathf.Max(0, s)}"
             + (extra >= 0 ? "  " + tail[extra..] : "");
    }

    private static int Num(string s, char key)
    {
        int i = s.IndexOf(key);
        if (i < 0 || i + 1 >= s.Length || !char.IsDigit(s[i + 1])) return -1;
        int v = 0;
        for (int k = i + 1; k < s.Length && char.IsDigit(s[k]); k++) v = v * 10 + (s[k] - '0');
        return v;
    }

    /// <summary>Die Werteliste rechts. Beschriftungen und Reihenfolge sind die
    /// des Originals, siehe <see cref="UnitStatBook"/>.</summary>
    private static string StatsFor(string design)
    {
        if (design.Length == 0) return "";
        if (!UnitStatBook.TryGet(design, out var e))
            // Schiffe und Hubschrauber stehen nicht in sec47 — dann steht hier
            // nichts, statt fremde Zahlen zu zeigen.
            return "";
        var sb = new System.Text.StringBuilder();
        sb.Append("Energie : ").Append(e.Hp).Append('\n');
        string w = UnitStatBook.ComponentLabel(e.Weapon);
        if (w.Length > 0) sb.Append(w).Append('\n');
        sb.Append("Nachladen ").Append(e.Reload).Append('\n');
        string p = UnitStatBook.ComponentLabel(e.Propulsion);
        if (p.Length > 0) sb.Append(p).Append('\n');
        sb.Append("A/V ").Append(e.Attack).Append('/').Append(e.Defence).Append('\n');
        sb.Append("Geschw. ").Append(e.Speed).Append('\n');
        sb.Append("Sicht ").Append(e.Sight).Append('\n');
        sb.Append("Reichw. ").Append(e.Range).Append('/').Append(e.MinRange).Append('\n');
        sb.Append("Sprit ").Append(e.Fuel).Append('\n');
        sb.Append("Munition ").Append(e.Ammo);
        return sb.ToString();
    }

    // ---- Ziehen an der Titelleiste (⚠ unsere Zutat) --------------------------

    private bool _dragging;
    private Vector2 _grab;

    private void DragBar(InputEvent e)
    {
        if (e is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left)
        {
            _dragging = mb.Pressed;
            _grab = GetGlobalMousePosition() - GlobalPosition;
        }
        else if (e is InputEventMouseMotion && _dragging)
        {
            var vp = GetViewportRect().Size;
            var p = GetGlobalMousePosition() - _grab;
            GlobalPosition = new Vector2(
                Mathf.Clamp(p.X, 0f, Mathf.Max(0f, vp.X - Size.X)),
                Mathf.Clamp(p.Y, 0f, Mathf.Max(0f, vp.Y - Size.Y)));
            _placed = true;
        }
    }

    private bool _placed;

    /// <summary>Oben rechts, wie im Bildschirmfoto des Originals — bis der
    /// Spieler es woanders hinzieht.
    ///
    /// <para>⚠ Es reicht NICHT, das einmal beim Aufgehen zu tun: ein Control,
    /// das an einer Leinwand und nicht an einem Container hängt, kennt seine
    /// Mindestgrösse erst, wenn der Inhalt einmal durchgerechnet ist. Der erste
    /// Lauf setzte die Ecke nach einer zu kleinen Breite — das Fenster stand
    /// halb ausserhalb des Bildes, und die Werteliste rechts war
    /// abgeschnitten. Darum wird die Stelle so lange nachgezogen, bis gezogen
    /// wird.</para></summary>
    public void PlaceTopRight()
    {
        // ⚠ GetViewportRect() braucht einen Knoten IM Baum — der Aufbau im
        // Konstruktor kommt vorher (dieselbe Falle wie im HelpWindow).
        if (_placed || !IsInsideTree()) return;
        var vp = GetViewportRect().Size;
        Size = GetCombinedMinimumSize();
        Position = new Vector2(Mathf.Max(0f, vp.X - Size.X - 12f),
                               Mathf.Max(0f, Mathf.Min(12f, vp.Y - Size.Y)));
    }

    /// <summary>Für einen Prüflauf, der nicht auf den Bildschirm sehen kann.
    /// </summary>
    public string WatchLine()
    {
        var rows = Rows?.Invoke() ?? new List<BuildPanel.Row>();
        int aff = 0;
        foreach (var r in rows) if (r.Affordable) aff++;
        return $"basis-fenster: {(Visible ? "offen" : "zu")}, Reiter \"{TabWords[_tab]}\", " +
               $"{rows.Count} Zeilen ({aff} bezahlbar), Auswahl " +
               (_sheet.Selected < 0 || _sheet.Selected >= rows.Count
                   ? "-" : $"{_sheet.Selected + 1} \"{rows[_sheet.Selected].Name}\"") +
               $", Bestand \"{_stock.Text}\", Titel \"{_title.Text}\"" +
               // ⚠ Die zwei Ausbauknoepfe stehen mit in dieser Zeile, weil ein
               // kopfloser Lauf sonst nicht sagen kann, OB es sie gibt — und
               // genau das war der Fehler, den sie beheben.
               ", Ausbau: " + (_expand.Visible
                   ? $"\"{_expand.Text}\" / \"{_prodUp.Text}\"" +
                     (_expand.Disabled ? " (gesperrt)" : " (bedienbar)")
                   : "keine Fabrik gewaehlt") +
               ", Abbrechen: " + (_cancel.Visible ? $"\"{_cancel.Text}\"" : "nichts") +
               ", Starten: " + (_launch.Visible ? $"\"{_launch.Text}\"" : "kein Hangar") +
               $", Vorschau: {_preview.WatchLine()}";
    }

    // =========================================================================

    /// <summary>Der Energiebalken über den Reitern.</summary>
    private sealed partial class EnergyBar : Control
    {
        private float _fill;
        public void Set(int hp, int max)
        {
            _fill = max > 0 ? Mathf.Clamp(hp / (float)max, 0f, 1f) : 0f;
            QueueRedraw();
        }
        public override void _Draw()
        {
            DrawRect(new Rect2(Vector2.Zero, Size), BoxBg);
            DrawRect(new Rect2(Vector2.Zero, new Vector2(Size.X * _fill, Size.Y)), EnergyFg);
        }
    }

    /// <summary>
    /// Das Vorschaufeld: das Bild des markierten Entwurfs, Fahrwerk unten und
    /// Aufbauteil darüber.
    ///
    /// <para>Das Original zeichnet es im Basisfenster (Fenstertyp-Byte 6,
    /// <c>0x467C60…0x46B61B</c>) an fensterrelativ <b>(280, 80)</b>, und zwar an
    /// drei Stellen: <c>0x46840A</c> für einen Entwurf aus dem Depot
    /// (<c>word[0x878E5C + 2·Platz]</c>), <c>0x4691D5</c> für die markierte
    /// Produktionszeile — das ist diese hier — und <c>0x46A50B</c> mit
    /// <c>icon = 0x64</c>, also der »?«-Tafel. Alle drei rufen denselben
    /// Zeichner <c>0x4508A0</c>; sein Fall 0 nimmt <c>sec47 +0x18</c> als
    /// Fahrwerk und <c>sec47 +0x17</c> als Waffe. Siehe
    /// <see cref="PortraitBank"/>.</para>
    ///
    /// <para>⚠ <b>Hier stand »noch leer: welches Bild zu einem Entwurf gehört,
    /// hängt an den Sprites, und die liegen in MapEntityLayer«.</b> Das war der
    /// falsche Weg: die Vorschau kommt NICHT aus ROBO.CWR (den Kartensprites),
    /// sondern aus ANIM.CWA — ein roher Dword-Abtast nach der ROBO-Rahmentafel
    /// (0x7A4FF0, 50 Treffer), der Teiletafel (0x77C870/72) und den drei
    /// Blitter-Sprungbrettern liegt RESTLOS im Kartenzeichner
    /// 0x429000…0x42E000, keine einzige Fundstelle in der Fensteroberfläche.
    /// Das war die entscheidende Weggabelung.</para>
    ///
    /// <para>⚠ Der Entwurf wird über seinen NAMEN nachgeschlagen — dieselbe
    /// Auskunft, aus der auch die Werteliste rechts kommt
    /// (<see cref="UnitStatBook"/>). Steht er dort nicht (Schiffe und
    /// Hubschrauber stehen nicht in sec47), bleibt der Kasten leer, statt ein
    /// fremdes Bild zu zeigen.</para>
    /// </summary>
    private sealed partial class Preview : Control
    {
        private int _chassis, _weapon;

        /// <summary>Ein FERTIGES Bild statt Fahrwerk + Aufbauteil — die Fälle des
        /// Zeichners, die nur eines haben (Flugzeug, Fußsoldat). 0 = keins.</summary>
        private int _pic;

        /// <summary>Wieviele Bilder der letzte Zeichenlauf gemalt hat.</summary>
        public int Drawn { get; private set; } = -1;

        public Preview() { TextureFilter = TextureFilterEnum.Nearest; }

        public void Set(int chassis, int weapon)
        {
            if (chassis == _chassis && weapon == _weapon && _pic == 0) return;
            _chassis = chassis; _weapon = weapon; _pic = 0;
            QueueRedraw();
        }

        /// <summary>Ein Entwurf mit EINEM eigenen Bild — Flugzeug oder
        /// Fußsoldat. Setzt Fahrwerk und Aufbauteil zurück, damit die beiden
        /// Wege sich nicht überlagern.</summary>
        public void SetPicture(int pic)
        {
            if (pic == _pic) return;
            _pic = pic; _chassis = _weapon = 0;
            QueueRedraw();
        }

        public override void _Draw()
        {
            var box = new Rect2(Vector2.Zero, Size);
            if (!PortraitBank.Ready) { DrawRect(box, BoxBg); Drawn = 0; }
            else if (_pic > 0) Drawn = PortraitBank.DrawPictures(this, box, _pic, 0);
            else Drawn = PortraitBank.DrawUnit(this, box, _chassis, _weapon);
            DrawRect(box, new Color(0.16f, 0.16f, 0.15f), false, 1f);
        }

        public string WatchLine()
            => _pic > 0
               ? $"Einzelbild {_pic} = {Drawn} Bilder auf {Size.X:0}x{Size.Y:0} " +
                 $"an {GlobalPosition.X:0},{GlobalPosition.Y:0}"
               : $"Fahrwerk {_chassis} (Bild {PortraitBank.IconOfComponent(_chassis)}) + " +
                 $"Aufbauteil {_weapon} (Bild {PortraitBank.IconOfComponent(_weapon)}) " +
                 $"= {Drawn} Bilder auf {Size.X:0}x{Size.Y:0} an {GlobalPosition.X:0},{GlobalPosition.Y:0}";
    }

    /// <summary>Der schwarze Listenkasten: Name links, Preis rechts, eine Zeile
    /// je Entwurf.</summary>
    private sealed partial class Sheet : Control
    {
        /// <summary>Die Zeilenhöhe des Originals (15 px @0x46829F), mit der
        /// Schrift mitskaliert.</summary>
        public const int OrigStep = 15;

        private readonly List<BuildPanel.Row> _rows = new();
        private Font? _font;
        private int _size = 13;
        private int _top;
        private int _hover = -1;

        public int Selected { get; set; } = 0;
        public string Note = "";
        public Action<int>? OnPick;
        public Action<int>? OnConfirm;

        public float Step => Mathf.Max(10f, OrigStep * (_size / 13f));

        public Sheet()
        {
            MouseFilter = MouseFilterEnum.Stop;
            SizeFlagsHorizontal = SizeFlags.ExpandFill;
            SizeFlagsVertical = SizeFlags.ExpandFill;
        }

        public void SetFont(Font f, int size) { _font = f; _size = size; QueueRedraw(); }

        public void Fill(List<BuildPanel.Row> rows)
        {
            _rows.Clear();
            _rows.AddRange(rows);
            if (Selected >= _rows.Count) Selected = _rows.Count - 1;
            if (Selected < 0 && _rows.Count > 0) Selected = 0;
            QueueRedraw();
        }

        private int Fits => Mathf.Max(1, (int)(Size.Y / Step));

        public override void _Draw()
        {
            if (_font == null) return;
            if (_rows.Count == 0)
            {
                float y = Step;
                foreach (string line in Note.Split('\n'))
                {
                    DrawString(_font, new Vector2(6, y), line,
                               HorizontalAlignment.Left, Size.X - 8, _size, HintFg);
                    y += Step;
                }
                return;
            }
            _top = Mathf.Clamp(_top, 0, Mathf.Max(0, _rows.Count - Fits));
            int n = Mathf.Min(_rows.Count - _top, Fits);
            for (int i = 0; i < n; i++)
            {
                int idx = _top + i;
                var r = _rows[idx];
                float top = i * Step;
                if (idx == Selected) DrawRect(new Rect2(0, top, Size.X, Step), RowSelBg);
                else if (idx == _hover)
                    DrawRect(new Rect2(0, top, Size.X, Step), new Color(1, 1, 1, 0.07f));
                var fg = !r.Affordable ? RowDearFg : idx == Selected ? RowSelFg : RowFg;
                float baseline = top + Step - Mathf.Max(2f, Step * 0.2f);
                float costW = DrawCost(r.Cost, Size.X - 4, baseline, fg, measure: true);
                DrawString(_font, new Vector2(4, baseline), r.Name,
                           HorizontalAlignment.Left, Size.X - 10 - costW, _size, fg);
                DrawCost(r.Cost, Size.X - 4, baseline, fg, measure: false);
            }
        }

        /// <summary>Den Preis rechtsbündig zeichnen: Sinnbild in Rot, Zahl in
        /// der Farbe der Zeile — so steht es im Bildschirmfoto. »20/40/0« aus
        /// der Bauliste wird zu »]20 [40 {0«.</summary>
        private float DrawCost(string cost, float right, float baseline, Color fg, bool measure)
        {
            if (_font == null || cost.Length == 0) return 0f;
            var bits = new List<(string Icon, string Text)>();
            var parts = cost.Split('/');
            if (parts.Length == 3)
            {
                bits.Add((IconW, parts[0]));
                bits.Add((IconF, parts[1]));
                bits.Add((IconS, parts[2]));
            }
            else bits.Add(("", cost));

            float total = 0f;
            foreach (var (icon, text) in bits)
                total += W(icon) + W(text) + W(" ");
            total -= W(" ");
            if (measure) return total;

            float x = right - total;
            foreach (var (icon, text) in bits)
            {
                if (icon.Length > 0)
                {
                    DrawString(_font, new Vector2(x, baseline), icon,
                               HorizontalAlignment.Left, -1, _size, IconFg);
                    x += W(icon);
                }
                DrawString(_font, new Vector2(x, baseline), text,
                           HorizontalAlignment.Left, -1, _size, fg);
                x += W(text) + W(" ");
            }
            return total;
        }

        private float W(string s) =>
            _font == null ? 0f : _font.GetStringSize(s, HorizontalAlignment.Left, -1, _size).X;

        private int RowAt(Vector2 p)
        {
            int i = _top + (int)(p.Y / Step);
            return i >= 0 && i < _rows.Count ? i : -1;
        }

        public override void _GuiInput(InputEvent e)
        {
            if (e is InputEventMouseMotion mm)
            {
                int h = RowAt(mm.Position);
                if (h != _hover) { _hover = h; QueueRedraw(); }
                return;
            }
            if (e is InputEventMouseButton { Pressed: true } mb)
            {
                if (mb.ButtonIndex == MouseButton.WheelUp) { _top--; QueueRedraw(); AcceptEvent(); return; }
                if (mb.ButtonIndex == MouseButton.WheelDown) { _top++; QueueRedraw(); AcceptEvent(); return; }
                if (mb.ButtonIndex != MouseButton.Left) return;
                int i = RowAt(mb.Position);
                if (i >= 0)
                {
                    // Ein Klick wählt, ein Doppelklick bestellt — das Original
                    // hat für das Bestellen den Knopf »Produzieren«, und der ist
                    // hier der Weg. Der Doppelklick ist UNSERE Abkürzung.
                    if (mb.DoubleClick) OnConfirm?.Invoke(i);
                    else OnPick?.Invoke(i);
                }
                AcceptEvent();
            }
        }

        public override void _Notification(int what)
        {
            if (what == NotificationMouseExit && _hover != -1) { _hover = -1; QueueRedraw(); }
        }
    }
}
