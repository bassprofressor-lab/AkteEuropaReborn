namespace AkteEuropaReborn.UI;

using System;
using System.Collections.Generic;
using Godot;
using AkteEuropaReborn.Rendering;

/// <summary>
/// Das Fenster »Erstellung« — der Entwurfsdialog, in dem sich der Spieler eine
/// eigene Einheit aus Fahrwerk, Aufbauteil und Verbesserung zusammensetzt.
///
/// <para><b>Wonach es gebaut ist.</b> Nach dem Bildschirmfoto des Originals
/// (akte-europa_8.png) und nach GAME.EXE. Der Fenstername steht auf 0x501afc,
/// die drei Listenüberschriften auf <c>"Fahrwerk"</c> (0x4f1505),
/// <c>"Verbesserung"</c> (0x501aec) und <c>"Aufbauteil"</c> (0x501adc), unten
/// <c>"Kosten : ]"</c> (0x501acc — das <c>]</c> gehört schon zur Zeile),
/// <c>"Name : "</c> (0x501abc) und die beiden Knöpfe <c>"Erstellen"</c>
/// (0x5019c4) und <c>"Abbruch"</c> (0x501aac).</para>
///
/// <para><b>Die Einträge und ihre Preise sind gelesen.</b> Jede Zeile ist der
/// LANGE Bauteilname (Satz +0x25) mit der Zahl aus +0x01 in Klammern —
/// @0x46cc53 und @0x46d050 hängen sie ohne Bedingung an, siehe
/// <see cref="UnitStatBook"/>. Rechts steht der Preis des Bauteils, und das
/// ist genau das Byte, aus dem die Preisformel @0x4b1fb0 den Entwurfspreis
/// baut: Aufbauteil +0x20, Fahrwerk +0x21, Verbesserung +0x22. Auf dem
/// Bildschirmfoto steht »Leichte Bordkanone (0) ]15« (Zeile 1, +0x20 = 15),
/// »Maschinengewehr (0) ]10« (Zeile 4, +0x20 = 10) und »Reifen (0) [10«
/// (Zeile 161, +0x21 = 10) — dieselben Zahlen, die die Tabelle trägt, und
/// zusammen genau das »Kosten : ]15 [10 {0« der Fusszeile.</para>
///
/// <para><b>Die Werteliste rechts</b> hat die Reihenfolge des
/// Erstellungsfensters, nicht die des Basisfensters: die Verweise auf die
/// Beschriftungen stehen dort der Reihe nach bei 0x46d845 (Energie), 0x46d95d
/// (A/V), 0x46dbf3 (Nachladen), 0x46dd24 (Geschw.), 0x46de33 (Sicht), 0x46df4b
/// (Reichw.), 0x46e1e0 (Sprit), 0x46e2ef (Munition) — ohne Bauteilnamen, denn
/// die stehen ja in den Listen.</para>
///
/// <para><b>Das Fenster rechnet nichts selbst.</b> Es bedient
/// <see cref="DesignScreen"/> — dieselbe Stelle, die die Tasten M / Pfeile /
/// Enter schon bedienen. Ein Klick in eine Liste wird in die Schritte
/// umgerechnet, die <c>MapEntityLayer.DesignerInput</c> annimmt, und
/// »Erstellen« ist <c>DesignerInput(0, 0, true)</c>. Damit kann das Fenster
/// nicht auseinanderlaufen mit dem, was die Tastatur tut.</para>
///
/// <para><b>Das Vorschaufeld zeigt die fertige Einheit</b> — seit dem
/// 13.08.2026, und es ist der Kern dessen, was der Spieler an diesem Fenster
/// vermisst hat (»im Modularen Bau System hatte man die Grafiken der einzelteile
/// gesehen und wie die einheit am ende aussah«). Das Original malt dort ZWEI
/// Bilder übereinander am gleichen Ursprung: <c>0x46D5DF</c> und
/// <c>0x46D63A</c> rufen beide <c>0x4508A0(5, …, 520, 40)</c>, unten das
/// Fahrwerk, darüber das Aufbauteil, in einem Kasten
/// <c>0x456A50(520, 40, 3, 3)</c> = 60×60. Woher die Bilder kommen und warum
/// diese Reihenfolge, steht in <see cref="PortraitBank"/>.
/// ⚠ Die <b>Verbesserung</b> hat ein Bild, wird vom Original aber NICHT in die
/// Vorschau gezeichnet — wir zeichnen sie dort auch nicht.</para>
///
/// <para>⚠ <b>UNSERE Setzungen:</b> Farben und Rahmen wie im
/// <see cref="BaseWindow"/>. Das Feld <b>»Name : «</b> zeigt den Namen, den
/// <see cref="DesignScreen.ProposedName"/> vorschlägt — im Original tippt man
/// ihn selbst; eine Texteingabe gibt es hier noch nicht, und das steht schon
/// im Kopf von DesignScreen. Und die <b>drei kleinen Einzelfelder</b> unter der
/// Vorschau: das Original zeigt die Bilder der gewählten Teile nirgends
/// einzeln, es hat nur den einen 60×60-Kasten. Sie stehen da, weil der Spieler
/// ausdrücklich »die Grafiken der einzelteile« genannt hat, und sie sind als
/// Zutat gekennzeichnet, nicht als Fund.</para>
/// </summary>
public sealed partial class DesignWindow : PanelContainer
{
    private static readonly Color WinBg = new(0.30f, 0.28f, 0.25f);
    private static readonly Color WinEdge = new(0.55f, 0.51f, 0.45f);
    private static readonly Color BarBg = new(0.22f, 0.20f, 0.18f);
    private static readonly Color TitleFg = new(0.88f, 0.86f, 0.80f);
    private static readonly Color PlainFg = new(0.84f, 0.83f, 0.76f);
    private static readonly Color BoxBg = new(0.02f, 0.02f, 0.02f);
    private static readonly Color RowFg = new(0.78f, 0.82f, 0.85f);
    private static readonly Color RowSelFg = new(1f, 1f, 1f);
    private static readonly Color RowSelBg = new(0.24f, 0.30f, 0.24f);
    private static readonly Color IconFg = new(0.85f, 0.20f, 0.14f);

    /// <summary>Die Entwurfsstelle, die auch die Tasten bedienen.</summary>
    public DesignScreen? Screen;

    /// <summary>Was <c>MapEntityLayer.DesignerInput(move, change, accept)</c>
    /// tut — Zeile wechseln, Teil wechseln, übernehmen.</summary>
    public Action<int, int, bool>? Input;

    /// <summary>Das X und »Abbruch«.</summary>
    public Action? OnClose;

    private readonly Label _title = new();
    private readonly PartList _prop = new(), _equip = new(), _weapon = new();
    private readonly Label _cost = new(), _name = new(), _stats = new(), _message = new();
    private readonly Preview _preview = new();

    /// <summary>Die drei Einzelbilder in der Reihenfolge der Listen des
    /// Originals: Fahrwerk, Aufbauteil, Verbesserung (⚠ unsere Zutat, siehe
    /// Klassenkopf).</summary>
    private readonly PartIcon _iconProp = new(), _iconWeapon = new(), _iconEquip = new();

    private readonly Button _make = new(), _cancel = new();

    public DesignWindow()
    {
        MouseFilter = MouseFilterEnum.Stop;
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

        // ---- Titelleiste ----------------------------------------------------
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
        _title.Text = "Erstellung";
        _title.AddThemeColorOverride("font_color", TitleFg);
        _title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _title.MouseFilter = MouseFilterEnum.Ignore;
        barRow.AddChild(_title);
        var x = new Button { Text = "X", FocusMode = FocusModeEnum.None };
        x.CustomMinimumSize = new Vector2(22, 18);
        x.AddThemeColorOverride("font_color", new Color(1f, 0.72f, 0.68f));
        x.Pressed += () => OnClose?.Invoke();
        barRow.AddChild(x);

        var pad = new MarginContainer();
        pad.AddThemeConstantOverride("margin_left", 8);
        pad.AddThemeConstantOverride("margin_right", 8);
        pad.AddThemeConstantOverride("margin_top", 6);
        pad.AddThemeConstantOverride("margin_bottom", 6);
        col.AddChild(pad);
        var body = new VBoxContainer();
        body.AddThemeConstantOverride("separation", 6);
        pad.AddChild(body);

        // ---- die drei Listen -------------------------------------------------
        var mid = new HBoxContainer();
        mid.AddThemeConstantOverride("separation", 10);
        body.AddChild(mid);

        var left = new VBoxContainer();
        left.AddThemeConstantOverride("separation", 2);
        mid.AddChild(left);
        left.AddChild(Head("Fahrwerk"));
        left.AddChild(Box(_prop));
        left.AddChild(Head("Verbesserung"));
        left.AddChild(Box(_equip));

        var centre = new VBoxContainer();
        centre.AddThemeConstantOverride("separation", 2);
        mid.AddChild(centre);
        centre.AddChild(Head("Aufbauteil"));
        centre.AddChild(Box(_weapon));

        var right = new VBoxContainer();
        right.AddThemeConstantOverride("separation", 4);
        mid.AddChild(right);
        right.AddChild(_preview);

        // die drei Einzelbilder darunter, in der Reihenfolge der Listen
        // (⚠ unsere Zutat — das Original hat nur den einen 60×60-Kasten)
        var icons = new HBoxContainer();
        icons.AddThemeConstantOverride("separation", 3);
        right.AddChild(icons);
        icons.AddChild(_iconProp);
        icons.AddChild(_iconWeapon);
        icons.AddChild(_iconEquip);
        _iconProp.TooltipText = "Fahrwerk";
        _iconWeapon.TooltipText = "Aufbauteil";
        _iconEquip.TooltipText = "Verbesserung (das Original zeichnet sie NICHT " +
                                 "in die Vorschau)";

        _stats.AddThemeColorOverride("font_color", PlainFg);
        _stats.HorizontalAlignment = HorizontalAlignment.Right;
        _stats.SizeFlagsVertical = SizeFlags.ExpandFill;
        right.AddChild(_stats);
        _make.Text = "Erstellen";
        _make.FocusMode = FocusModeEnum.None;
        _make.Pressed += () => { Input?.Invoke(0, 0, true); Refresh(); };
        right.AddChild(_make);
        _cancel.Text = "Abbruch";
        _cancel.FocusMode = FocusModeEnum.None;
        _cancel.Pressed += () => OnClose?.Invoke();
        right.AddChild(_cancel);

        // ---- Fusszeile --------------------------------------------------------
        var foot = new HBoxContainer();
        foot.AddThemeConstantOverride("separation", 16);
        body.AddChild(foot);
        _cost.AddThemeColorOverride("font_color", PlainFg);
        foot.AddChild(_cost);
        _name.AddThemeColorOverride("font_color", PlainFg);
        _name.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _name.HorizontalAlignment = HorizontalAlignment.Center;
        foot.AddChild(_name);
        _message.AddThemeColorOverride("font_color", new Color(0.85f, 0.62f, 0.22f));
        foot.AddChild(_message);

        // die Auswahl aus den Listen zurück in die Entwurfsstelle
        _prop.OnPick = i => Pick(DesignScreen.Row.Propulsion, i);
        _equip.OnPick = i => Pick(DesignScreen.Row.Equipment, i);
        _weapon.OnPick = i => Pick(DesignScreen.Row.Weapon, i);
    }

    private Label Head(string text)
    {
        var l = new Label { Text = text };
        l.AddThemeColorOverride("font_color", PlainFg);
        _heads.Add(l);
        return l;
    }

    private readonly List<Label> _heads = new();

    private PanelContainer Box(PartList list)
    {
        var p = new PanelContainer();
        var sb = new StyleBoxFlat
        {
            BgColor = BoxBg, BorderColor = new Color(0.16f, 0.16f, 0.15f),
            ContentMarginLeft = 2, ContentMarginRight = 2,
            ContentMarginTop = 2, ContentMarginBottom = 2,
        };
        sb.SetBorderWidthAll(1);
        p.AddThemeStyleboxOverride("panel", sb);
        p.AddChild(list);
        return p;
    }

    // ---- Bedienung -----------------------------------------------------------

    /// <summary>Ein Klick in eine Liste. Die Entwurfsstelle kennt nur »eine
    /// Zeile weiter« und »ein Teil weiter«, also wird der Klick in genau diese
    /// Schritte umgerechnet — dann geht die Tastatur denselben Weg wie die
    /// Maus.</summary>
    private void Pick(DesignScreen.Row row, int index)
    {
        if (Screen == null || Input == null) return;
        int move = (int)row - (int)Screen.Cursor;
        if (move != 0) Input(move, 0, false);
        int at = CurrentIndex(row);
        if (at >= 0 && index != at) Input(0, index - at, false);
        Refresh();
    }

    private int CurrentIndex(DesignScreen.Row row)
    {
        if (Screen == null) return -1;
        return row switch
        {
            DesignScreen.Row.Propulsion =>
                Screen.CurrentPropulsion is { } p ? Screen.Propulsions.IndexOf(p) : -1,
            DesignScreen.Row.Weapon =>
                Screen.CurrentWeapon is { } w ? Screen.Weapons.IndexOf(w) : -1,
            // 0 ist »(nichts)«, danach kommt die Liste
            _ => Screen.CurrentEquipment is { } e ? Screen.Equipment.IndexOf(e) + 1 : 0,
        };
    }

    // ---- Füllen ---------------------------------------------------------------

    public void Refresh()
    {
        if (Screen == null) return;

        var props = new List<PartList.Line>();
        foreach (var p in Screen.Propulsions)
            props.Add(new PartList.Line(Name(p), BaseWindow.IconF,
                                        UnitStatBook.ChassisPrice(p.Id)));
        _prop.Fill(props, CurrentIndex(DesignScreen.Row.Propulsion),
                   Screen.Cursor == DesignScreen.Row.Propulsion);

        // »(nichts)« zuerst — das Original lässt einen Entwurf ohne
        // Verbesserung zu, und die meisten fertigen haben keine.
        var eq = new List<PartList.Line> { new("(nichts)", "", -1) };
        foreach (var e in Screen.Equipment)
            eq.Add(new PartList.Line(Name(e), BaseWindow.IconS,
                                     UnitStatBook.EquipPrice(e.Id)));
        _equip.Fill(eq, CurrentIndex(DesignScreen.Row.Equipment),
                    Screen.Cursor == DesignScreen.Row.Equipment);

        var wp = new List<PartList.Line>();
        foreach (var w in Screen.Weapons)
            wp.Add(new PartList.Line(Name(w), BaseWindow.IconW,
                                     UnitStatBook.WeaponPrice(w.Id)));
        _weapon.Fill(wp, CurrentIndex(DesignScreen.Row.Weapon),
                     Screen.Cursor == DesignScreen.Row.Weapon);

        int weapon = Screen.CurrentWeapon?.Id ?? 0;
        int prop = Screen.CurrentPropulsion?.Id ?? 0;
        int equip = Screen.CurrentEquipment?.Id ?? 0;
        var d = Simulation.DesignMath.Compute(weapon, prop, equip);

        // Die Vorschau: Fahrwerk unten, Aufbauteil oben, gleicher Ursprung —
        // die Reihenfolge des Originals, siehe PortraitBank.
        _preview.Set(prop, weapon);
        _iconProp.Component = prop;
        _iconWeapon.Component = weapon;
        _iconEquip.Component = equip;

        _cost.Text = $"Kosten : {BaseWindow.IconW}{d.CostW} " +
                     $"{BaseWindow.IconF}{d.CostF} {BaseWindow.IconS}{d.CostS}";
        _name.Text = "Name : " + Screen.ProposedName();
        _message.Text = Screen.Message;

        int min = d.Tail is { Length: > 0x23 - 0x1a }
            ? d.Tail[0x22 - 0x1a] | (d.Tail[0x23 - 0x1a] << 8) : 0;
        _stats.Text =
            $"Energie {d.Hp}\n" +
            $"A/V {d.Attack}/{d.Defence}\n" +
            $"Nachladen {d.Reload}\n" +
            $"Geschw. {d.Speed}\n" +
            $"Sicht {d.Sight}\n" +
            $"Reichw. {d.Range}/{min}\n" +
            $"Sprit {d.Fuel}\n" +
            $"Munition {d.Ammo}";

        _make.Disabled = !Screen.Ready;
        PlaceOnce();
    }

    /// <summary>Der lange Bauteilname mit der Klammer des Originals; ist er
    /// nicht in der Bauteiltabelle zu finden, bleibt der Name, unter dem die
    /// Entwurfsstelle das Teil führt.</summary>
    private static string Name(DesignScreen.Part p)
    {
        string s = UnitStatBook.ComponentLongLabel(p.Id);
        return s.Length > 0 ? s : $"{p.Name} ({p.Id})";
    }

    // ---- Schrift und Stelle ----------------------------------------------------

    public void SetFont(Font font, int size)
    {
        foreach (var n in Walk(this))
        {
            n.AddThemeFontOverride("font", font);
            n.AddThemeFontSizeOverride("font_size", size);
        }
        _prop.SetFont(font, size);
        _equip.SetFont(font, size);
        _weapon.SetFont(font, size);
        // Die beiden linken Kästen fassen zusammen so viel wie der rechte —
        // dieselbe Aufteilung wie im Bildschirmfoto des Originals.
        _prop.CustomMinimumSize = new Vector2(size * 13f, _prop.Step * 5f);
        _equip.CustomMinimumSize = new Vector2(size * 13f, _equip.Step * 5f);
        _weapon.CustomMinimumSize = new Vector2(size * 13f, _weapon.Step * 12f);
        // Der Kasten des Originals ist 60×60 Punkte bei 13 px Schrift; er wird
        // mit der Schrift mitskaliert und BLEIBT QUADRATISCH, weil die Bilder es
        // sind (siehe PortraitBank.Box).
        float side = PortraitBank.Box * (size / 13f);
        _preview.CustomMinimumSize = new Vector2(side, side);
        float small = side / 3f;
        foreach (var ic in new[] { _iconProp, _iconWeapon, _iconEquip })
            ic.CustomMinimumSize = new Vector2(small, small);
        Refresh();
    }

    private static IEnumerable<Control> Walk(Node n)
    {
        foreach (var c in n.GetChildren())
        {
            if (c is Label or Button) yield return (Control)c;
            foreach (var d in Walk(c)) yield return d;
        }
    }

    private bool _placed, _dragging;
    private Vector2 _grab;

    /// <summary>Wie im Bildschirmfoto: unter dem Basisfenster, mittig-rechts.
    /// Siehe <c>BaseWindow.PlaceTopRight</c> für den Grund, warum das mehrfach
    /// laufen muss.</summary>
    private void PlaceOnce()
    {
        // ⚠ nur im Baum — GetViewportRect() wirft sonst, siehe BaseWindow.
        if (_placed || !IsInsideTree()) return;
        var vp = GetViewportRect().Size;
        Size = GetCombinedMinimumSize();
        Position = new Vector2(Mathf.Max(0f, (vp.X - Size.X) * 0.5f),
                               Mathf.Max(0f, vp.Y - Size.Y - 24f));
    }

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

    /// <summary>Für einen Prüflauf, der nicht auf den Bildschirm sehen kann.
    /// </summary>
    public string WatchLine()
        => Screen == null
            ? "erstellung: keine Entwurfsstelle"
            : $"erstellung: {(Visible ? "offen" : "zu")}, " +
              $"{Screen.Propulsions.Count} Fahrwerke / {Screen.Weapons.Count} Aufbauteile / " +
              $"{Screen.Equipment.Count} Verbesserungen, Zeile {Screen.Cursor}, " +
              $"\"{Screen.ProposedName()}\", {_cost.Text}, Vorschau: {_preview.WatchLine()}";

    // =========================================================================

    /// <summary>Der Vorschaukasten des Originals, <c>0x456A50(520, 40, 3, 3)</c>
    /// = 60×60, gefüllt mit Palettenindex 0x2F, darin Fahrwerk und darüber
    /// Aufbauteil am gleichen Ursprung.</summary>
    private sealed partial class Preview : Control
    {
        private int _chassis, _weapon;

        /// <summary>Wieviele Bilder der letzte Zeichenlauf wirklich gemalt hat —
        /// 2 bei einer vollständigen Einheit. ⚠ Ein Prüfstand, der nur »Bank da«
        /// sagt, prüft nichts; das hier ist die Zahl, die die Mechanik ausübt.
        /// </summary>
        public int Drawn { get; private set; } = -1;

        public Preview()
        {
            // ⚠ ohne Nearest zerfliesst ein 60×60-Bild beim Hochskalieren
            TextureFilter = TextureFilterEnum.Nearest;
        }

        public void Set(int chassis, int weapon)
        {
            if (chassis == _chassis && weapon == _weapon) return;
            _chassis = chassis; _weapon = weapon;
            QueueRedraw();
        }

        public override void _Draw()
        {
            var box = new Rect2(Vector2.Zero, Size);
            if (PortraitBank.Ready) Drawn = PortraitBank.DrawUnit(this, box, _chassis, _weapon);
            else { DrawRect(box, BoxBg); Drawn = 0; }
            DrawRect(box, new Color(0.16f, 0.16f, 0.15f), false, 1f);
        }

        public string WatchLine()
            => $"Fahrwerk {_chassis} (Bild {PortraitBank.IconOfComponent(_chassis)}) + " +
               $"Aufbauteil {_weapon} (Bild {PortraitBank.IconOfComponent(_weapon)}) " +
               $"= {Drawn} Bilder auf {Size.X:0}x{Size.Y:0} an {GlobalPosition.X:0},{GlobalPosition.Y:0}";
    }

    /// <summary>Ein einzelnes Bauteilbild — Fall 5 des Zeichners. ⚠ Unsere
    /// Zutat, siehe Klassenkopf.</summary>
    private sealed partial class PartIcon : Control
    {
        private int _component;

        public int Component
        {
            get => _component;
            set { if (value != _component) { _component = value; QueueRedraw(); } }
        }

        public PartIcon() { TextureFilter = TextureFilterEnum.Nearest; }

        public override void _Draw()
        {
            var box = new Rect2(Vector2.Zero, Size);
            if (PortraitBank.Ready) PortraitBank.DrawComponent(this, box, _component);
            else DrawRect(box, BoxBg);
            DrawRect(box, new Color(0.16f, 0.16f, 0.15f), false, 1f);
        }
    }

    /// <summary>Eine der drei Bauteillisten: Name links, Sinnbild und Preis
    /// rechts.</summary>
    private sealed partial class PartList : Control
    {
        public readonly record struct Line(string Text, string Icon, int Price);

        private readonly List<Line> _lines = new();
        private Font? _font;
        private int _size = 13;
        private int _top, _sel = -1, _hover = -1;
        private bool _active;

        public Action<int>? OnPick;

        public float Step => Mathf.Max(10f, 15 * (_size / 13f));

        public PartList()
        {
            MouseFilter = MouseFilterEnum.Stop;
            SizeFlagsHorizontal = SizeFlags.ExpandFill;
            SizeFlagsVertical = SizeFlags.ExpandFill;
        }

        public void SetFont(Font f, int size) { _font = f; _size = size; QueueRedraw(); }

        public void Fill(List<Line> lines, int selected, bool active)
        {
            _lines.Clear();
            _lines.AddRange(lines);
            _sel = selected;
            _active = active;
            // die Auswahl im Bild halten
            int fits = Mathf.Max(1, (int)(Size.Y / Step));
            if (_sel >= 0 && _sel < _top) _top = _sel;
            if (_sel >= _top + fits) _top = _sel - fits + 1;
            QueueRedraw();
        }

        public override void _Draw()
        {
            if (_font == null) return;
            int fits = Mathf.Max(1, (int)(Size.Y / Step));
            _top = Mathf.Clamp(_top, 0, Mathf.Max(0, _lines.Count - fits));
            int n = Mathf.Min(_lines.Count - _top, fits);
            for (int i = 0; i < n; i++)
            {
                int idx = _top + i;
                var l = _lines[idx];
                float top = i * Step;
                if (idx == _sel)
                    DrawRect(new Rect2(0, top, Size.X, Step),
                             _active ? RowSelBg : new Color(RowSelBg, 0.55f));
                else if (idx == _hover)
                    DrawRect(new Rect2(0, top, Size.X, Step), new Color(1, 1, 1, 0.07f));
                var fg = idx == _sel ? RowSelFg : RowFg;
                float baseline = top + Step - Mathf.Max(2f, Step * 0.2f);
                float cw = 0f;
                if (l.Price >= 0 && l.Icon.Length > 0)
                {
                    string num = l.Price.ToString();
                    cw = W(l.Icon) + W(num);
                    float x = Size.X - 4 - cw;
                    DrawString(_font, new Vector2(x, baseline), l.Icon,
                               HorizontalAlignment.Left, -1, _size, IconFg);
                    DrawString(_font, new Vector2(x + W(l.Icon), baseline), num,
                               HorizontalAlignment.Left, -1, _size, fg);
                }
                DrawString(_font, new Vector2(4, baseline), l.Text,
                           HorizontalAlignment.Left, Size.X - 10 - cw, _size, fg);
            }
        }

        private float W(string s) =>
            _font == null ? 0f : _font.GetStringSize(s, HorizontalAlignment.Left, -1, _size).X;

        private int RowAt(Vector2 p)
        {
            int i = _top + (int)(p.Y / Step);
            return i >= 0 && i < _lines.Count ? i : -1;
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
                if (i >= 0) OnPick?.Invoke(i);
                AcceptEvent();
            }
        }

        public override void _Notification(int what)
        {
            if (what == NotificationMouseExit && _hover != -1) { _hover = -1; QueueRedraw(); }
        }
    }
}
