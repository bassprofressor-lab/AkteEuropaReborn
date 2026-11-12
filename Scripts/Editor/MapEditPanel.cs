namespace AkteEuropaReborn.Editor;

using System;
using System.Collections.Generic;
using Godot;
using AkteEuropaReborn.Import;
using AkteEuropaReborn.Rendering;

/// <summary>
/// ⭐ 11.11.2026 — bug-802: DAS BEDIENFELD DES BEARBEITUNGSMODUS.
///
/// <para><b>Die Spielermeldung</b> (11.11.2026): »wenn man karte bearbeiten
/// macht, ist das bau menu komisch, das muss einfach zu nutzen sein, aktuell
/// sieht man da nicht durch.«</para>
///
/// <para><b>Was verwirrte</b> (Bild scratchpad/editorui/vorher_pinsel.png): die
/// alte Leiste waren zwei Textzeilen oben rechts, halb ueber der Rohstoffleiste;
/// bedienbar NUR mit Tasten (1..6, +/-, Komma/Punkt), keine Knoepfe; »Gebaeude Typ
/// 1« statt eines Namens, kein Bild der Auswahl; der Besitzer nur als Wort; was
/// die rechte Taste tut, je Pinsel verschieden und nirgends gesagt; Speichern nur
/// ueber F5, kein Weg ins Gefecht; dazu die Tastenlegende des Spiels (X=stop,
/// B=bauen …), die im Editor nichts bedeutet.</para>
///
/// <para><b>Jetzt</b>: ein Feld rechts mit Werkzeuggruppen (Gelaende, Bauten &amp;
/// Truppen, Objekte, Wegnehmen) als Knoepfe samt Taste, einer Auswahlliste mit
/// Bildern (Gebaeude aus dem Muster des Kachelsatzes gesetzt, Gegenstaende aus der
/// Objektbank, Einheiten aus user://data/Units), farbigen Besitzerknoepfen, einer
/// Hilfezeile »Linksklick … / Rechtsklick …« und den Knoepfen Speichern (F5), Im
/// Gefecht spielen und Zurueck (Esc). Alle Tasten bleiben, wie sie waren.</para>
///
/// <para>⚠ UNSERE ZUTAT von A bis Z (wie der ganze Editor): Aufbau, Farben,
/// Gruppen und Texte sind unsere Setzung. Gelesen sind nur die Gebaeudenamen
/// (building_types.json, GAME.EXE 0x4fccfc) und die Parteifarben
/// (<see cref="MapEntityLayer.FactionColor"/>).</para>
/// </summary>
public partial class MapEditOverlay
{
    private Label _pTitle = null!, _pDirty = null!, _pTool = null!, _pLeft = null!, _pRight = null!,
                  _pSay = null!, _pOwnerNote = null!, _pListHead = null!;
    private ItemList _pList = null!;
    private TextureRect _pPreview = null!;
    private VBoxContainer _pOwnerBox = null!;
    private readonly Dictionary<Brush, Button> _toolButtons = new();
    private readonly Dictionary<int, Button> _pinselKnoepfe = new();   // P5
    private readonly Dictionary<int, Button> _ownerButtons = new();
    private Button _pBack = null!;
    private bool _backArmed;
    private Brush _listFor = (Brush)(-1);
    private bool _fillingList;

    /// <summary>Die Linien des Gleispinsels zur Auswahl — UNSERE SETZUNG
    /// (0..9; der Satz kennt mehr, gebraucht werden wenige).</summary>
    private const int RailLines = 10;

    // ---- ueber F5 hinweg merken (ReloadCurrentScene baut den Ueberzug neu) ---
    private static (Brush B, int Typ, int Owner, int Line, MapFactory.Ground G, int Prop, int Unit)? _merk;

    private void Merken()
    {
        _merk = (_brush, _typ, _owner, _line, _ground, _propAt, _unitAt);
        // ⭐ 12.11.2026 C1 (Fable-Pruefung): auch den BLICK merken. Vorher sprang die
        // Ansicht nach jedem F5 an den Kartenanfang, und man suchte die Stelle, an der
        // man gerade malte. Gegenschalter --editor-kamera-alt.
        if (GetViewport()?.GetCamera2D() is { } cam && !KameraAlt) _kamera = (cam.Position, cam.Zoom);
    }

    private static (Vector2 Pos, Vector2 Zoom)? _kamera;
    private static bool KameraAlt => Array.IndexOf(Core.CommandLine.Args, "--editor-kamera-alt") >= 0;

    /// <summary>Den gemerkten Blick zuruecksetzen — zwei Bilder spaeter, damit der
    /// Kartenbetrachter seine eigene Startsicht schon gesetzt hat.</summary>
    private async void KameraZurueck()
    {
        if (_kamera is not { } k) return;
        _kamera = null;
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (GetViewport()?.GetCamera2D() is { } cam)
        {
            cam.Zoom = k.Zoom; cam.Position = k.Pos;
            GD.Print($"map-editor: Blick nach F5 zurück auf ({k.Pos.X:0},{k.Pos.Y:0}), Zoom {k.Zoom.X:0.##}");
        }
    }

    private void Wiederherstellen()
    {
        KameraZurueck();
        if (_merk is not { } m) return;
        _merk = null;
        _brush = m.B; _owner = m.Owner; _line = m.Line; _ground = m.G;
        if (_types.Contains(m.Typ)) _typ = m.Typ;
        if (m.Prop < _props.Count) _propAt = m.Prop;
        if (m.Unit < MapEditUnits.All.Length) _unitAt = m.Unit;
    }

    // ---- Aufbau --------------------------------------------------------------

    private void BuildUi()
    {
        _ui = new CanvasLayer { Layer = 90 };
        AddChild(_ui);

        // Rechts, ueber der Minikarte (die steht unten rechts ab ~y 830 bei 1080).
        var panel = new PanelContainer
        {
            AnchorLeft = 1, AnchorRight = 1, AnchorTop = 0, AnchorBottom = 1,
            OffsetLeft = -396, OffsetRight = -4, OffsetTop = 6, OffsetBottom = -262,
            GrowHorizontal = Control.GrowDirection.Begin,
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        var ps = new StyleBoxFlat
        {
            BgColor = new Color(0.06f, 0.07f, 0.09f, 0.94f),
            BorderColor = new Color(0.35f, 0.40f, 0.48f),
            ContentMarginLeft = 10, ContentMarginRight = 10, ContentMarginTop = 8, ContentMarginBottom = 8,
        };
        ps.SetBorderWidthAll(1);
        ps.SetCornerRadiusAll(4);
        panel.AddThemeStyleboxOverride("panel", ps);
        _ui.AddChild(panel);

        // Kein Rollbereich: die Liste nimmt, was uebrig ist, damit Hilfe und
        // Knoepfe unten immer sichtbar bleiben (erster Bildlauf: sie lagen
        // unterhalb des Randes).
        var v = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        v.AddThemeConstantOverride("separation", 5);
        // ⭐ 12.11.2026 D10/bug-823: in einem Fenster unter 1080 Zeilen war der Inhalt
        // hoeher als der Platz, das Feld wuchs nach UNTEN ueber die Minikarte (1600x900:
        // bis y ~800, Minikarte ab 696) und schluckte dort jeden Klick — nur ihr unterer
        // Streifen war anklickbar. Jetzt rollt das Feld, wenn der Platz nicht reicht, und
        // bleibt sonst wie bisher (die Liste nimmt, was uebrig ist). Gegenschalter
        // --editor-feld-alt.
        // Die Knoepfe (Speichern/Spielen/Zurueck) und die Tastenzeile stehen AUSSERHALB
        // des Rollbereichs, fest unten — sie muessen immer zu sehen sein.
        VBoxContainer unten = v;
        if (Array.IndexOf(Core.CommandLine.Args, "--editor-feld-alt") < 0)
        {
            var aussen = new VBoxContainer();
            aussen.AddThemeConstantOverride("separation", 5);
            panel.AddChild(aussen);
            v.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
            var roll = new ScrollContainer
            {
                HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
                SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            };
            aussen.AddChild(roll);
            roll.AddChild(v);
            unten = new VBoxContainer();
            unten.AddThemeConstantOverride("separation", 5);
            aussen.AddChild(unten);
        }
        else panel.AddChild(v);

        // Kopf
        _pTitle = new Label { Text = "KARTE BEARBEITEN" };
        _pTitle.AddThemeFontSizeOverride("font_size", 22);
        v.AddChild(_pTitle);
        _pDirty = new Label();
        v.AddChild(_pDirty);

        // Werkzeuge in drei Gruppen nebeneinander, Wegnehmen darunter
        var tools = new GridContainer { Columns = 3 };
        tools.AddThemeConstantOverride("h_separation", 4);
        tools.AddThemeConstantOverride("v_separation", 4);
        v.AddChild(tools);
        foreach (string head in new[] { "Gelände", "Bauten/Truppen", "Objekte" })
            tools.AddChild(new Label { Text = head, Modulate = new Color(0.62f, 0.72f, 0.85f) });
        ToolButton(tools, Brush.Gelaende, "5 Bodenart");
        ToolButton(tools, Brush.Gebaeude, "1 Gebäude");
        ToolButton(tools, Brush.Gegenstand, "2 Wald/Fels");
        ToolButton(tools, Brush.Hoehe, "6 Höhe");
        ToolButton(tools, Brush.Einheit, "4 Einheiten");
        ToolButton(tools, Brush.Gleis, "3 Gleise");
        // P5 (12.11.2026): Wegnehmen und Vorkommen in einer Zeile, darunter Pinsel und Rueckgaengig
        var zeile7 = new HBoxContainer();
        zeile7.AddThemeConstantOverride("separation", 4);
        v.AddChild(zeile7);
        ToolButton(zeile7, Brush.Wegnehmen, "7 Wegnehmen");
        ToolButton(zeile7, Brush.Vorkommen, "8 Vorkommen");
        ToolButton(zeile7, Brush.Startplatz, "9 Startplatz");
        var pinselZeile = new HBoxContainer();
        pinselZeile.AddThemeConstantOverride("separation", 4);
        v.AddChild(pinselZeile);
        pinselZeile.AddChild(new Label { Text = "Pinsel [ ]" });
        foreach (int pg in new[] { 1, 3, 5 })
        {
            int gg = pg;
            var b = new Button
            {
                Text = $"{pg}x{pg}", ToggleMode = true, FocusMode = Control.FocusModeEnum.None,
                CustomMinimumSize = new Vector2(52, 30),
                TooltipText = "Pinselgröße für Bodenart, Höhe, Wald/Fels und Wegnehmen. Mit gedrückter Maustaste ziehen malt.",
            };
            b.Pressed += () => { _pinsel = gg; _say = ""; Refresh(); QueueRedraw(); };
            _pinselKnoepfe[pg] = b;
            pinselZeile.AddChild(b);
        }
        // D6 (12.11.2026): Spiegeln beim Malen
        _spiegelKnopf = new Button
        {
            Text = "⇅ Spiegeln", ToggleMode = true, FocusMode = Control.FocusModeEnum.None,
            CustomMinimumSize = new Vector2(0, 30),
            TooltipText = "Taste M: jeder Strich wird zugleich um die Kartenmitte gedreht gemalt (180°, wie bei "
                        + "symmetrisch erzeugten Karten) — für Bodenart, Höhe, Wald/Fels und Gleise.",
        };
        _spiegelKnopf.Toggled += an => { _spiegeln = an; _say = an ? "Spiegeln AN (Bodenart, Höhe, Wald/Fels, Gleise)" : "Spiegeln aus"; Refresh(); };
        pinselZeile.AddChild(_spiegelKnopf);
        pinselZeile.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        var zur = new Button { Text = "↶ zurück", TooltipText = "Rückgängig (Strg+Z)", FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(0, 30) };
        zur.Pressed += Rueckgaengig;
        var vor = new Button { Text = "↷ vor", TooltipText = "Wiederholen (Strg+Y)", FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(0, 30) };
        vor.Pressed += Wiederholen;
        pinselZeile.AddChild(zur);
        pinselZeile.AddChild(vor);
        _pTool = new Label { Visible = false };
        v.AddChild(_pTool);

        // Auswahl
        // C4 (12.11.2026): das Vorschaubild sitzt in der Kopfzeile, damit die Liste die
        // volle Breite hat — »Schwere Ketten · Schwere Bordkanone« passte sonst nicht.
        var kopf = new HBoxContainer();
        kopf.AddThemeConstantOverride("separation", 6);
        v.AddChild(kopf);
        _pListHead = Head("Auswahl");
        _pListHead.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _pListHead.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        kopf.AddChild(_pListHead);
        var sel = new HBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        sel.AddThemeConstantOverride("separation", 6);
        v.AddChild(sel);
        _pList = new ItemList
        {
            CustomMinimumSize = new Vector2(0, 132),   // P3/C5 (12.11.2026): vorher 90 = 2½ Zeilen; 132 = 3 volle, Minikarte bleibt frei
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            FixedIconSize = new Vector2I(48, 32),
            IconMode = ItemList.IconModeEnum.Left,
            FocusMode = Control.FocusModeEnum.None,
            TooltipText = "Auswahl für das Werkzeug — oder +/- blättern.",
        };
        _pList.ItemSelected += OnListPick;
        sel.AddChild(_pList);
        _pPreview = new TextureRect
        {
            CustomMinimumSize = new Vector2(72, 56),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            SizeFlagsVertical = Control.SizeFlags.ShrinkBegin,
        };
        kopf.AddChild(_pPreview);

        // Besitzer
        _pOwnerBox = new VBoxContainer();
        _pOwnerBox.AddThemeConstantOverride("separation", 4);
        v.AddChild(_pOwnerBox);
        _pOwnerBox.AddChild(Head("Besitzer  (Tasten , und .)"));
        var g = new GridContainer { Columns = 5 };
        g.AddThemeConstantOverride("h_separation", 4);
        g.AddThemeConstantOverride("v_separation", 4);
        _pOwnerBox.AddChild(g);
        foreach (int o in Owners)
        {
            int oo = o;
            var b = new Button
            {
                Text = o switch { 11 => "herrenl.", 255 => "Kulisse", _ => $"Sp {o + 1}" },
                ToggleMode = true, FocusMode = Control.FocusModeEnum.None,
                CustomMinimumSize = new Vector2(66, 30),
                TooltipText = o switch
                {
                    11 => "herrenlos (Taste N) — ein neutrales Gebäude, das man einnehmen kann",
                    255 => "Kulisse (Taste K) — gehört niemandem",
                    _ => $"Spieler {o + 1}",
                },
            };
            var col = o is >= 0 and <= 7 ? MapEntityLayer.FactionColor(o) : new Color(0.6f, 0.6f, 0.6f);
            b.AddThemeStyleboxOverride("normal", Chip(col, 0.35f, 1));
            b.AddThemeStyleboxOverride("hover", Chip(col, 0.55f, 1));
            b.AddThemeStyleboxOverride("pressed", Chip(col, 0.95f, 3));
            b.AddThemeStyleboxOverride("hover_pressed", Chip(col, 0.95f, 3));
            b.AddThemeColorOverride("font_pressed_color", Colors.Black);
            b.AddThemeColorOverride("font_hover_pressed_color", Colors.Black);
            b.Pressed += () => { _owner = oo; _say = ""; Refresh(); };
            _ownerButtons[o] = b;
            g.AddChild(b);
        }
        _pOwnerNote = Small("");
        _pOwnerNote.Visible = false;   // Platz: steht im Tooltip der Knoepfe
        _pOwnerBox.AddChild(_pOwnerNote);

        // Was ein Klick tut
        _pLeft = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _pRight = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _pLeft.AddThemeFontSizeOverride("font_size", 15);
        _pRight.AddThemeFontSizeOverride("font_size", 15);
        v.AddChild(_pLeft);
        v.AddChild(_pRight);
        _pSay = Small("");
        _pSay.Modulate = new Color(0.75f, 0.95f, 0.75f);
        v.AddChild(_pSay);

        // Knoepfe
        unten.AddChild(new HSeparator());
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 4);
        unten.AddChild(row);
        var save = new Button
        {
            Text = "Speichern (F5)", FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(0, 34),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            TooltipText = "Schreibt die Karte und lädt sie neu — erst danach sind Boden, Objekte und Gleise im Bild.",
        };
        save.Pressed += SaveAndReload;
        row.AddChild(save);
        var play = new Button
        {
            Text = "Spielen", FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(0, 34),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            TooltipText = "Im Gefecht spielen: speichert (falls nötig), du Platz 1, die anderen Plätze die KI.",
        };
        play.Pressed += PlayNow;
        row.AddChild(play);
        _pBack = new Button
        {
            Text = "Zurück (Esc)", FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(0, 34),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            TooltipText = "Zurück ins Menü. Ungespeichertes erst beim zweiten Druck verwerfen.",
        };
        _pBack.Pressed += ZurueckZumMenue;
        row.AddChild(_pBack);
        var keys = Small("Karte schieben: W A S D / mittlere Maustaste · Mausrad: Zoom · " +
                         "+/− blättert · N herrenlos · K Kulisse");
        keys.AddThemeFontSizeOverride("font_size", 13);
        unten.AddChild(keys);
    }

    private void ToolButton(Container h, Brush b, string text)
    {
        {
            var btn = new Button
            {
                Text = text, ToggleMode = true, FocusMode = Control.FocusModeEnum.None,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 32),
                Alignment = HorizontalAlignment.Left,
            };
            var col = b == Brush.Wegnehmen ? new Color(1f, 0.35f, 0.3f) : new Color(0.95f, 0.8f, 0.4f);
            btn.AddThemeStyleboxOverride("normal", Chip(new Color(0.5f, 0.55f, 0.62f), 0.18f, 1));
            btn.AddThemeStyleboxOverride("hover", Chip(new Color(0.6f, 0.68f, 0.78f), 0.30f, 1));
            btn.AddThemeStyleboxOverride("pressed", Chip(col, 0.40f, 2));
            btn.AddThemeStyleboxOverride("hover_pressed", Chip(col, 0.48f, 2));
            var bb = b;
            btn.Pressed += () =>
            {
                _brush = bb; _say = "";
                // P3/C12 (12.11.2026): Einheiten brauchen einen Spieler — »herrenlos«
                // oder »Kulisse« vom Gebaeudepinsel springt auf Spieler 1 zurueck.
                if (bb == Brush.Einheit && _owner is < 0 or > 7) _owner = 0;
                Refresh(); QueueRedraw();
            };
            _toolButtons[b] = btn;
            h.AddChild(btn);
        }
    }

    private static StyleBoxFlat Chip(Color c, float fill, int border)
    {
        var s = new StyleBoxFlat
        {
            BgColor = new Color(c.R * fill, c.G * fill, c.B * fill, 0.95f),
            BorderColor = c,
            ContentMarginLeft = 6, ContentMarginRight = 6, ContentMarginTop = 3, ContentMarginBottom = 3,
        };
        s.SetBorderWidthAll(border);
        s.SetCornerRadiusAll(3);
        return s;
    }

    private static Label Head(string t)
    {
        var l = new Label { Text = t, Modulate = new Color(0.62f, 0.72f, 0.85f) };
        l.AddThemeFontSizeOverride("font_size", 17);
        return l;
    }

    private static Label Small(string t)
    {
        var l = new Label { Text = t, AutowrapMode = TextServer.AutowrapMode.WordSmart, Modulate = new Color(0.65f, 0.7f, 0.77f) };
        l.AddThemeFontSizeOverride("font_size", 14);
        return l;
    }

    // ---- Auffrischen -----------------------------------------------------------

    private void Refresh()
    {
        if (_pTitle == null) return;
        _pTitle.Text = $"KARTE BEARBEITEN — {MapEditSession.Name.Replace("map_", "")}";
        _pDirty.Text = MapEditSession.Dirty
            ? $"● ungespeichert ({_mine.Count} Änderungen) — F5 speichert"
            : "gespeichert";
        _pDirty.Modulate = MapEditSession.Dirty ? new Color(1f, 0.6f, 0.4f) : new Color(0.6f, 0.85f, 0.6f);
        foreach (var (b, btn) in _toolButtons) btn.SetPressedNoSignal(b == _brush);
        foreach (var (g, btn) in _pinselKnoepfe) { btn.SetPressedNoSignal(g == _pinsel); btn.Disabled = !Flaechig(_brush); }

        bool owned = _brush is Brush.Gebaeude or Brush.Einheit or Brush.Startplatz;
        _pOwnerBox.Visible = owned;
        foreach (var (o, btn) in _ownerButtons)
        {
            btn.SetPressedNoSignal(o == _owner);
            btn.Disabled = (_brush is Brush.Einheit or Brush.Startplatz) && o is < 0 or > 7;
        }
        _pOwnerNote.Text = _brush == Brush.Einheit
            ? "Einheiten gehoeren immer einem Spieler (Sp 1..8)."
            : "herrenlos = einnehmbares Gebäude; Kulisse = gehört niemandem.";

        if (_listFor != _brush) FillList();
        SelectInList();

        string sel = Selection();
        _pTool.Text = sel;
        if (_pList.Visible) _pListHead.Text = $"{ListTitle()}: {sel}   (+/−)";
        else if (_brush == Brush.Startplatz) _pListHead.Text = SpielerZeile();   // D5: Spielerzahl nach jedem Klick neu
        (string left, string right) = _brush switch
        {
            Brush.Gebaeude => ($"setzt {sel} für {OwnerName(_owner)} (gruener Rahmen = Grundfläche)",
                               "nimmt das Gebäude unter der Maus weg"),
            Brush.Gegenstand => ("setzt das gewählte Objekt (Baum, Fels …) — sichtbar nach F5",
                                 "nimmt das Objekt unter der Maus weg"),
            Brush.Gleis => ($"legt ein Gleisstueck ({sel}; Gleise mit derselben Nummer bilden eine Strecke, auf der ein Zug fährt); Ecken und Rampen wählt der Editor — sichtbar nach F5",
                            "nimmt das Gleisstueck weg"),
            Brush.Einheit => ($"stellt {sel} für {OwnerName(_owner)} auf",
                              "nimmt die Einheit unter der Maus weg (verschwindet nach F5)"),
            Brush.Gelaende => ($"malt »{GroundName(_ground)}« (zieht 9x9 Zellen Uebergaenge nach) — sichtbar nach F5",
                               "malt »frei« (es gibt keine leere Zelle)"),
            Brush.Hoehe => ("hebt die Zelle um eine Stufe (hoechstens 1 Stufe Unterschied zum Nachbarn)",
                            "senkt die Zelle um eine Stufe"),
            Brush.Startplatz => ($"setzt den Startplatz für {OwnerName(_owner)}: Basis, Werk, Truppe — ein vorhandener wird verschoben",
                                 "nimmt den Startplatz dieses Spielers weg"),
            Brush.Vorkommen => ($"setzt ein Terranium-Vorkommen ({MapDeposits.Amount}) — gelbe Raute, mind. 8 Zellen Abstand",
                                "nimmt das nächste Vorkommen weg"),
            _ => ("nimmt weg, was dort steht: Einheit, sonst Gebäude, sonst Gleis, sonst Objekt",
                  "ebenso"),
        };
        _pLeft.Text = "Linksklick: " + left;
        _pRight.Text = "Rechtsklick: " + right;
        _pSay.Text = _say;
        if (_backArmed && !MapEditSession.Dirty) _backArmed = false;
        _pBack.Text = _backArmed ? "Verwerfen?" : "Zurück (Esc)";
    }

    private string ListTitle() => _brush switch
    {
        Brush.Gebaeude => "Gebäude", Brush.Gegenstand => "Wald/Fels", Brush.Gleis => "Gleis",
        Brush.Einheit => "Einheit", Brush.Gelaende => "Bodenart", _ => "",
    };

    private string Selection() => _brush switch
    {
        Brush.Gebaeude => _types.Count > 0 ? $"»{KachelsatzInfo.GebaeudeName(_typ)}«" : "kein Gebäude im Kachelsatz",
        Brush.Gegenstand => _props.Count > 0 ? PropText(_props[_propAt]) : "diese Karte hat keine Objekte",
        Brush.Gleis => $"Zugstrecke {_line + 1}",
        Brush.Einheit => $"»{UnitText(MapEditUnits.All[_unitAt])}«",
        Brush.Gelaende => $"»{GroundName(_ground)}«" + (MapEditSession.Tiles == null ? " ⚠ ohne Kacheltabelle" : ""),
        Brush.Hoehe => "Höhe ändern",
        Brush.Vorkommen => "Vorkommen",
        Brush.Startplatz => "Startplatz",
        _ => "Wegnehmen",
    };

    // ---- die Liste -------------------------------------------------------------

    private void FillList()
    {
        _fillingList = true;
        _listFor = _brush;
        _pList.Clear();
        _pList.Visible = true;
        _pPreview.Visible = true;
        var cwp = MapEditSession.Cwp;
        var pal = MapEditSession.Pal;
        switch (_brush)
        {
            case Brush.Gebaeude:
                _pListHead.Text = "Gebäude";
                foreach (int t in _types)
                {
                    var (fw, fh) = _layer.EditorFootprint(t);
                    _pList.AddItem($"{KachelsatzInfo.GebaeudeName(t)}  ({fw}×{fh})",
                                   cwp != null && pal != null ? MapEditIcons.Building(cwp, pal, t) : null);
                }
                break;
            case Brush.Gegenstand:
                _pListHead.Text = "Wald/Fels (wie auf dieser Karte)";
                for (int i = 0; i < _props.Count; i++)
                    _pList.AddItem(PropText(_props[i], true),
                                   cwp != null && pal != null ? MapEditIcons.Prop(cwp, pal, _props[i]) : null);
                break;
            case Brush.Gleis:
                // C4: »Linie 0..9« sagte nicht, was eine Linie ist — es ist die Zugstrecke,
                // zu der ein Gleisstueck gehoert (sec22 +4; ein Zug faehrt auf seiner Strecke).
                _pListHead.Text = "Zugstrecke (Gleise mit derselben Nummer bilden eine Strecke)";
                for (int i = 0; i < RailLines; i++) _pList.AddItem($"Zugstrecke {i + 1}");
                break;
            case Brush.Einheit:
                _pListHead.Text = "Einheit";
                foreach (var u in MapEditUnits.All)
                    _pList.AddItem(UnitText(u, true), MapEditIcons.Unit(u.Type));
                break;
            case Brush.Gelaende:
                _pListHead.Text = "Bodenart";
                foreach (var gr in new[] { MapFactory.Ground.Free, MapFactory.Ground.Rough,
                                           MapFactory.Ground.Water, MapFactory.Ground.Blocked })
                {
                    var (kurz, lang) = gr switch
                    {
                        MapFactory.Ground.Free => ("frei", "befahrbar; bebaubar ab Höhe 2"),
                        MapFactory.Ground.Rough => ("rau", "Geröll — befahrbar, nicht bebaubar"),
                        MapFactory.Ground.Water => ("Wasser", "nur auf der Höhe des Wasserspiegels"),
                        _ => ("gesperrt", "unpassierbar"),
                    };
                    int at = _pList.AddItem($"{kurz} — {lang}", MapEditIcons.Swatch(GroundColor(gr)));
                    _pList.SetItemTooltip(at, $"{kurz}: {lang}");
                }
                break;
            default:
                _pListHead.Text = _brush switch
                {
                    Brush.Hoehe => "Höhe: links anheben, rechts absenken",
                    Brush.Vorkommen => "Vorkommen: gelbe Rauten auf der Karte",
                    Brush.Startplatz => SpielerZeile(),
                    _ => "Wegnehmen: keine Auswahl nötig",
                };
                _pList.Visible = false;
                _pPreview.Visible = false;
                break;
        }
        if (_brush is Brush.Einheit or Brush.Gegenstand or Brush.Gebaeude)          // C4: voller Name als Tooltip
            for (int i = 0; i < _pList.ItemCount; i++) _pList.SetItemTooltip(i, _pList.GetItemText(i));
        _fillingList = false;
    }

    private int ListIndex() => _brush switch
    {
        Brush.Gebaeude => _types.IndexOf(_typ),
        Brush.Gegenstand => _propAt,
        Brush.Gleis => Math.Min(_line, RailLines - 1),
        Brush.Einheit => _unitAt,
        Brush.Gelaende => (int)_ground,
        _ => -1,
    };

    private void SelectInList()
    {
        int i = ListIndex();
        _fillingList = true;
        if (i >= 0 && i < _pList.ItemCount) { _pList.Select(i); _pList.EnsureCurrentIsVisible(); }
        else _pList.DeselectAll();
        _fillingList = false;
        _pPreview.Texture = i >= 0 && i < _pList.ItemCount ? _pList.GetItemIcon(i) : null;
    }

    private void OnListPick(long index)
    {
        if (_fillingList) return;
        int i = (int)index;
        switch (_brush)
        {
            case Brush.Gebaeude when i < _types.Count: _typ = _types[i]; break;
            case Brush.Gegenstand when i < _props.Count: _propAt = i; break;
            case Brush.Gleis: _line = i; break;
            case Brush.Einheit when i < MapEditUnits.All.Length: _unitAt = i; break;
            case Brush.Gelaende: _ground = (MapFactory.Ground)i; break;
        }
        _say = "";
        Refresh();
        QueueRedraw();
    }

    // ---- die Wege hinaus ----------------------------------------------------------

    /// <summary>Speichern (falls noetig) und ins Gefecht — derselbe Weg wie
    /// »Im Gefecht spielen« im Editorschirm (MainMenu.PlayMade, bug-743).</summary>
    private void PlayNow()
    {
        string? made = MapEditSession.Name;
        if (MapEditSession.Dirty)
        {
            made = MapEditSession.Save(s => GD.Print("map-editor: " + s));
            if (made == null) { _say = "Speichern ging nicht — kein Gefecht"; Refresh(); return; }
        }
        MapEditSession.Leave();
        UI.SkirmishSetup.ViewMap = "";
        UI.SkirmishSetup.Map = made;
        UI.SkirmishSetup.Human = 0;
        UI.SkirmishSetup.AiCount = 7;
        UI.SkirmishSetup.CampaignMission = 0;
        UI.SkirmishSetup.Active = true;
        GD.Print($"map-editor: spielen: {made} im Gefecht (aus dem Bearbeitungsmodus)");
        GetTree().ChangeSceneToFile(UI.SkirmishSetup.GameScene);
    }

    /// <summary>Zurueck ins Menue; mit ungespeicherten Aenderungen erst beim
    /// zweiten Druck (UNSERE SETZUNG).</summary>
    private void ZurueckZumMenue()
    {
        if (MapEditSession.Dirty && !_backArmed)
        {
            _backArmed = true;
            _say = "Ungespeicherte Änderungen! F5 speichert — noch einmal Esc/Zurück verwirft sie.";
            Refresh();
            return;
        }
        UI.MapEditorScreen.ZurueckMit = MapEditSession.Name;   // P3/C2: zurueck in den Editorschirm
        MapEditSession.Leave();
        UI.SkirmishSetup.Active = false;
        UI.SkirmishSetup.ViewMap = "";
        GetTree().ChangeSceneToFile(UI.SkirmishSetup.MenuScene);
    }
}

/// <summary>
/// bug-802 (11.11.2026): die Bildchen der Auswahlliste. Alles aus den Daten des
/// Spielers gesetzt, nichts mitgeliefert; zwischengespeichert je Kachelsatz.
/// </summary>
public static class MapEditIcons
{
    private static readonly Dictionary<string, Texture2D?> _cache = new();

    /// <summary>Ein Gebaeude aus seinem Kachelmuster gesetzt — dieselbe Lage wie
    /// im Backofen (<c>row*20 − 50 + YOff</c>, MapBaker.Blit). Nur ein Bildchen:
    /// Tueren, Animationen und Schatten fehlen.</summary>
    public static Texture2D? Building(CwpFile cwp, PalFile pal, int typ)
    {
        string key = $"b{cwp.GetHashCode()}:{typ}";
        if (_cache.TryGetValue(key, out var t)) return t;
        t = null;
        try
        {
            var bt = cwp.GetBuildingType(typ);
            if (!bt.IsEmpty)
            {
                // FirstPattern = das stehende Gebaeude, wie MapEntityLayer es
                // zeichnet; TilePattern ist das Baugeruest (erster Bildlauf
                // zeigte damit bei jeder Fabrik dasselbe Bild).
                int pat = bt.FirstPattern;
                const int ox = 40, oy = 260;
                int cw = CwpFile.PatternWidth * MapBaker.TileW + 2 * ox;
                int ch = CwpFile.PatternHeight * MapBaker.TileH + oy + 60;
                var img = Image.CreateEmpty(cw, ch, false, Image.Format.Rgba8);
                img.Fill(new Color(0, 0, 0, 0));
                for (int y = 0; y < CwpFile.PatternHeight; y++)
                    for (int x = 0; x < CwpFile.PatternWidth; x++)
                    {
                        int tile = cwp.PatternTile(pat, x, y);
                        if (tile == 0) continue;
                        CwpFile.Frame f;
                        try { f = cwp.DecodeObject(CwpFile.ObjectCodeBase + tile); }
                        catch (ArgumentOutOfRangeException) { continue; }
                        if (f.IsEmpty) continue;
                        int sx = ox + x * MapBaker.TileW;
                        int sy = oy + y * MapBaker.TileH + MapBaker.BlitAnchor + f.YOffset;
                        Blit(img, f, pal, sx, sy);
                    }
                var used = img.GetUsedRect();
                if (used.Size.X > 0 && used.Size.Y > 0) t = ImageTexture.CreateFromImage(img.GetRegion(used));
            }
        }
        catch (Exception e) { GD.Print($"map-editor: Gebaeudebild {typ}: {e.Message}"); }
        _cache[key] = t;
        return t;
    }

    public static Texture2D? Prop(CwpFile cwp, PalFile pal, int code)
    {
        string key = $"p{cwp.GetHashCode()}:{code}";
        if (_cache.TryGetValue(key, out var t)) return t;
        t = null;
        try
        {
            var f = cwp.DecodeObject(code);
            if (!f.IsEmpty) t = ImageTexture.CreateFromImage(CwpFile.ToImage(f, pal));
        }
        catch (Exception) { }
        _cache[key] = t;
        return t;
    }

    /// <summary>Der Rumpf aus <c>user://data/Units/&lt;typ&gt;/f2.png</c> — ohne
    /// Aufsatz (die Waffe ist ein eigenes Bild).</summary>
    public static Texture2D? Unit(int type)
    {
        string key = $"u{type}";
        if (_cache.TryGetValue(key, out var t)) return t;
        t = null;
        string p = ProjectSettings.GlobalizePath(Core.Content.UserRoot) + $"Units/{type}/f2.png";
        if (System.IO.File.Exists(p))
        {
            var img = Image.LoadFromFile(p);
            if (img != null && !img.IsEmpty()) t = ImageTexture.CreateFromImage(img);
        }
        _cache[key] = t;
        return t;
    }

    public static Texture2D Swatch(Color c)
    {
        var img = Image.CreateEmpty(48, 32, false, Image.Format.Rgba8);
        img.Fill(new Color(c.R, c.G, c.B, 1f));
        return ImageTexture.CreateFromImage(img);
    }

    private static void Blit(Image img, CwpFile.Frame f, PalFile pal, int sx, int sy)
    {
        for (int y = 0; y < f.Height; y++)
        {
            int dy = sy + y;
            if (dy < 0 || dy >= img.GetHeight()) continue;
            for (int x = 0; x < f.Width; x++)
            {
                int o = y * f.Width + x;
                if (!f.Opaque[o]) continue;
                int dx = sx + x;
                if (dx < 0 || dx >= img.GetWidth()) continue;
                byte i = f.Pixels[o];
                img.SetPixel(dx, dy, Color.Color8(pal.R[i], pal.G[i], pal.B[i], 255));
            }
        }
    }
}
