namespace AkteEuropaReborn.UI;

using System;
using System.Collections.Generic;
using System.IO;
using Godot;
using AkteEuropaReborn.Editor;

/// <summary>
/// Der Karteneditor als Schirm — dieselben zwei Laeufe, die es seit dem
/// 12.08.2026 als Schalter <c>--map-new=</c> und <c>--map-check=</c> gibt, nur
/// ohne Befehlszeile.
///
/// <para><b>Was dieser Schirm KANN, ist genau das, was der Editor kann</b>, und
/// keine Zeile mehr. Nachgelesen in <see cref="MapGenerator"/>,
/// <see cref="MapFactory"/>, <see cref="TilePalette"/> und
/// <see cref="MapCheck"/>:</para>
/// <list type="bullet">
///   <item>Ein GELAENDE erzeugen: Wasserrand (3 Zellen), Strandring, Landesinneres
///     auf Hoehe 2, ein Berg links oben, ein See rechts unten — und zwei
///     Startmarken (Typ 0x70/0x71) in gegenueberliegenden Ecken. Die Form ist
///     UNSERE Setzung, ihre eine gemessene Bedingung steht im Kopf von
///     <see cref="MapGenerator"/>: unter Hoehe 2 gibt es keinen Bauplatz.</item>
///   <item>Die geschriebenen Dateien PRUEFEN: <see cref="MapCheck"/> liest
///     <c>map_*.json</c>, <c>map_*.entities.json</c> und <c>map_*.png</c>
///     zurueck und zaehlt Bauplaetze, Gelaende und Erreichbarkeit nach.</item>
/// </list>
///
/// <para><b>Was er NICHT kann</b> — und was dieser Schirm deshalb auch nicht
/// anbietet, statt es vorzugaukeln: keine Maus auf der Karte, kein Malen
/// einzelner Zellen, kein Setzen von Einheiten, Gebaeuden, Rohstoffen oder
/// Gleisen, kein Oeffnen einer erzeugten Karte zum Weiterbearbeiten, keine Wahl
/// der Spielerzahl (es sind immer zwei Marken). Eine erzeugte Karte ist reines
/// Gelaende: <b>0 Einheiten, 0 Gebaeude</b>.</para>
///
/// <para>⚠ <b>Darum gibt es hier keinen »Spielen«-Knopf, sondern »Ansehen«.</b>
/// Gemessen am 12.08.2026 mit <c>--skirmish=map_pruef01 --quit-after=3</c>:
/// <c>StartSkirmish</c> findet auf einer erzeugten Karte keinen besetzten Platz,
/// und die Abrechnung meldet im ersten Takt »Gemetzel entschieden: MISSION
/// GESCHEITERT«. Ein Gefecht darauf waere also verloren, bevor das Bild steht.
/// »Ansehen« zeigt die Karte im Kartenbetrachter — ohne Gefecht, ohne KI, ohne
/// Abrechnung (siehe <see cref="SkirmishSetup.ViewMap"/>).</para>
///
/// <para>Der Aufbau ist der des Gefechtsschirms und des Einstellungsschirms:
/// voller Deckel, Rollbereich, mittige Spalte — damit er auch in einem
/// 720p-Fenster nicht oben und unten abgeschnitten wird.</para>
/// </summary>
public partial class MapEditorScreen : Control
{
    /// <summary>Wohin »Ansehen« fuehrt. Setzt das Hauptmenue, weil nur es die
    /// Menuekulisse aus dem Speicher nehmen kann, bevor die naechste
    /// 20..33-Megapixel-Karte geladen wird.</summary>
    public Action<string>? OnView { get; init; }

    /// <summary>Wohin »Bearbeiten« fuehrt — derselbe Kartenbetrachter, aber mit
    /// eingeschaltetem <see cref="MapEditSession"/>. Setzt das Hauptmenue, aus
    /// demselben Grund wie <see cref="OnView"/>.</summary>
    public Action<string>? OnEdit { get; init; }

    // ---- UNSERE Setzungen fuer die Eingabefelder ----------------------------
    //
    // Die Obergrenze ist GELESEN: CwmFile.Create wirft ueber 254 ("die groesste
    // gelieferte Karte ist 254x254"). Die Untergrenze ist UNSERE: bei 3 Zellen
    // Wasserrand, einem Strandring und Startmarken 6 Zellen vom Rand bleibt
    // unter 24 Zellen kaum noch Landesinneres uebrig.
    private const int MinSize = 24, MaxSize = 254;

    /// <summary>Kachelsatz 47 zuerst — ⚠ unsere Wahl, und nur, weil er der
    /// heute durchgemessene ist. Fehlt er, wird der erste gefundene
    /// genommen.</summary>
    private const int PreferredTileset = 47;

    private LineEdit _name = null!;
    private SpinBox _w = null!, _h = null!, _pick = null!;
    private OptionButton _tileset = null!;
    private CheckBox _flat = null!;
    private LineEdit _from = null!;
    private Button _create = null!, _check = null!, _view = null!, _edit = null!;
    private RichTextLabel _log = null!;
    private Label _status = null!;

    /// <summary>Die Kachelsatznummern hinter den Zeilen des Auswahlfelds.</summary>
    private readonly List<int> _tilesets = new();

    /// <summary>Die zuletzt erfolgreich geschriebene Karte, mit <c>map_</c>
    /// davor. Leer, solange nichts erzeugt wurde — daran haengen die Knoepfe
    /// »Pruefen« und »Ansehen«.</summary>
    private string _made = "";

    /// <summary>Was der Harnisch nach dem Erzeugen noch druecken soll —
    /// »pruefen«, »ansehen« oder nichts. Siehe das Ende von <c>_Ready</c>.</summary>
    private string _autoRun = "";

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;

        var backdrop = new ColorRect { Color = new Color(0.04f, 0.05f, 0.07f, 0.96f) };
        backdrop.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(backdrop);

        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        scroll.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(scroll);

        var middle = new CenterContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        scroll.AddChild(middle);

        var box = new VBoxContainer { CustomMinimumSize = new Vector2(1000, 0) };
        box.AddThemeConstantOverride("separation", 10);
        middle.AddChild(box);

        var title = new Label { Text = "KARTENEDITOR", HorizontalAlignment = HorizontalAlignment.Center };
        title.AddThemeFontSizeOverride("font_size", 34);
        box.AddChild(title);
        box.AddChild(new Label
        {
            // Sagt in einer Zeile, was herauskommt — und was nicht. Beides ist
            // aus MapGenerator abgelesen und nicht geschaetzt.
            Text = "Erzeugt Gelaende: Wasserrand, Strandring, Berg, See, zwei Startmarken. "
                 + "Einheiten, Gebaeude und Gleise setzt er nicht.",
            HorizontalAlignment = HorizontalAlignment.Center,
            Modulate = new Color(0.7f, 0.75f, 0.8f),
        });
        box.AddChild(new HSeparator());

        // ---- links die Karte, rechts der Bericht ------------------------------
        //
        // Nebeneinander und nicht untereinander: untereinander stand der Knopf
        // »KARTE ERZEUGEN« im ersten Bild (1280x720) unterhalb des Fensterrands
        // und war nur ueber den Rollbalken zu erreichen. Dieselbe Aufteilung
        // benutzt der Gefechtsschirm.
        var lower = new HBoxContainer();
        lower.AddThemeConstantOverride("separation", 12);
        box.AddChild(lower);

        var form = new VBoxContainer();
        form.AddThemeConstantOverride("separation", 6);
        var formGroup = Group("Neue Karte", form);
        formGroup.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        lower.AddChild(formGroup);

        _name = new LineEdit
        {
            Text = "eigen01",
            PlaceholderText = "eigen01",
            TooltipText = "Die Dateien heissen danach map_<name>.png, .json und .entities.json in "
                        + Core.Content.UserDir + "Maps.",
        };
        form.AddChild(Row("Name", _name));

        string sizeHint = $"{MinSize} bis {MaxSize} Zellen. Die {MaxSize} sind gelesen — so gross "
                        + "ist die groesste gelieferte Karte, darueber weist CwmFile.Create ab. Die "
                        + $"{MinSize} sind UNSERE Setzung: darunter bleibt nach Wasserrand (3), "
                        + "Strandring und den 6 Zellen bis zur Startmarke kaum Landesinneres uebrig.";
        _w = new SpinBox { MinValue = MinSize, MaxValue = MaxSize, Value = 64, TooltipText = sizeHint };
        _h = new SpinBox { MinValue = MinSize, MaxValue = MaxSize, Value = 64, TooltipText = sizeHint };
        var size = new HBoxContainer();
        size.AddThemeConstantOverride("separation", 8);
        _w.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _h.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        size.AddChild(_w);
        size.AddChild(new Label { Text = "x" });
        size.AddChild(_h);
        form.AddChild(Row($"Groesse ({MinSize}..{MaxSize})", size));

        _tileset = new OptionButton();
        form.AddChild(Row("Kachelsatz", _tileset));

        _pick = new SpinBox
        {
            MinValue = 0, MaxValue = 7, Value = 0,
            TooltipText = "Welcher der gefundenen Bodenlaeufe die Wiese wird. Welche es in diesem "
                        + "Kachelsatz gibt, steht im Bericht (»volle Bodenlaeufe ab 8«) — die "
                        + "Kachel selbst sagt es nicht.",
        };
        form.AddChild(Row("Bodenblock", _pick));

        _from = new LineEdit { PlaceholderText = @"nur noetig, wenn kein Kachelsatz gefunden wird" };
        var fromRow = new HBoxContainer();
        fromRow.AddThemeConstantOverride("separation", 8);
        _from.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        fromRow.AddChild(_from);
        var browse = new Button { Text = "Durchsuchen …" };
        fromRow.AddChild(browse);
        form.AddChild(Row("Ordner mit NN.CWP", fromRow));
        form.AddChild(Hint("Sonst wird gesucht: Entwicklungsbaum, user://data/DATA, eingelegte CD."));

        _flat = new CheckBox
        {
            Text = "flach (alles auf Hoehe 0)",
            TooltipText = "Die Gegenprobe des Editors, nicht zum Spielen: eine flache Karte hat "
                        + "NULL Bauplaetze, und die Pruefung muss danach genau das beanstanden.",
        };
        form.AddChild(Row("Gegenprobe", _flat));
        form.AddChild(Hint("⚠ Flach heisst NULL Bauplaetze (can_build_here: vier Eckhoehen ≥ 2)."));

        // ---- rechts der Bericht ----------------------------------------------
        //
        // Wort fuer Wort dieselben Zeilen, die der kopflose Lauf auf die Konsole
        // schreibt. Sie stehen hier, weil sonst niemand sieht, welcher
        // Bodenblock genommen wurde, wie viele Bauplaetze herauskamen und woran
        // ein Lauf gescheitert ist.
        _log = new RichTextLabel
        {
            CustomMinimumSize = new Vector2(420, 300),
            BbcodeEnabled = false,
            ScrollFollowing = true,
            SelectionEnabled = true,
            Text = "Noch nichts erzeugt.",
        };
        lower.AddChild(Group("Bericht", _log));

        // ---- die Knoepfe -----------------------------------------------------
        box.AddChild(new HSeparator());

        _create = new Button { Text = "KARTE ERZEUGEN", CustomMinimumSize = new Vector2(0, 44) };
        _create.Pressed += OnCreate;
        box.AddChild(_create);

        var after = new HBoxContainer();
        after.AddThemeConstantOverride("separation", 8);
        box.AddChild(after);

        _check = new Button
        {
            Text = "Karte pruefen",
            Disabled = true,
            CustomMinimumSize = new Vector2(0, 36),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            TooltipText = "Liest die geschriebenen Dateien zurueck: Raster, Gelaende, Bauplaetze, "
                        + "Erreichbarkeit von den Startmarken und das Bild.",
        };
        _check.Pressed += OnCheck;
        after.AddChild(_check);

        _view = new Button
        {
            Text = "Karte ansehen",
            Disabled = true,
            CustomMinimumSize = new Vector2(0, 36),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            TooltipText = "Oeffnet die Karte im Kartenbetrachter — kein Gefecht: sie traegt keine "
                        + "Einheiten und keine Gebaeude. ESC fuehrt zurueck.",
        };
        _view.Pressed += OnViewPressed;
        after.AddChild(_view);

        _edit = new Button
        {
            Text = "Karte bearbeiten",
            Disabled = true,
            CustomMinimumSize = new Vector2(0, 36),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            TooltipText = "Oeffnet die Karte mit dem Pinsel: 1/2/3 waehlt Gebaeude, Gegenstand "
                        + "oder Gleis, +/- die Art, LINKS setzt, RECHTS nimmt weg, F5 speichert. "
                        + "ESC fuehrt zurueck.",
        };
        _edit.Pressed += OnEditPressed;
        after.AddChild(_edit);

        _status = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            Modulate = new Color(0.75f, 0.8f, 0.86f),
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        box.AddChild(_status);

        box.AddChild(new HSeparator());
        var back = new Button { Text = "ZURUECK", CustomMinimumSize = new Vector2(0, 40) };
        back.Pressed += Close;
        box.AddChild(back);

        // ---- der Ordnerwaehler ----------------------------------------------
        var dlg = new FileDialog
        {
            FileMode = FileDialog.FileModeEnum.OpenDir,
            Access = FileDialog.AccessEnum.Filesystem,
            Size = new Vector2I(820, 560),
            Title = "Ordner mit den Kachelsaetzen waehlen",
        };
        AddChild(dlg);
        browse.Pressed += () => dlg.PopupCentered();
        dlg.DirSelected += d => { _from.Text = d; FillTilesets(); };
        // Bei Eingabetaste UND beim Verlassen des Feldes neu suchen — nicht bei
        // jedem Tastendruck: eine Suche sind bis zu 64 x 12 File.Exists.
        _from.TextSubmitted += _ => FillTilesets();
        _from.FocusExited += FillTilesets;

        FillTilesets();

        // ---- der Harnisch ----------------------------------------------------
        //
        // `--map-editor-run[=pruefen|=ansehen]` drueckt »KARTE ERZEUGEN« von
        // selbst, sobald der Schirm steht, und danach wahlweise noch einen der
        // beiden Folgeknoepfe. Gebraucht wird das, weil ein Prueflauf nicht
        // klicken kann und ein Menue, das nur RICHTIG AUSSIEHT, nichts wert ist
        // — genau die Begruendung, aus der MainMenu schon `--menu-click=` hat.
        // Zusammen ergibt das eine Zeile, die den ganzen Weg durchmisst:
        //   … --menu-click=Karteneditor --map-editor-run=pruefen --shot=<datei> --shot-delay=<bilder>
        // Der dritte Wert ist `bearbeiten`; zusammen mit `--map-edit-check` ist
        // das der Prueflauf des Bearbeitungsmodus, und der laeuft kopflos:
        //   … --headless --path . -- --menu-click=Karteneditor \
        //       --map-editor-run=bearbeiten --map-editor-name=pinsel01 \
        //       --map-edit-check --quit-after=8
        // ⚠ Braucht ein FENSTER (kein --headless), sonst gibt es nichts zu
        // photographieren.
        // ⚠ `--map-editor-name=` gehoert dazu und ist kein Beiwerk: das
        // Benutzerverzeichnis (%APPDATA%/Godot/app_userdata/…/data) ist EINS,
        // und mehrere Laeufe nebeneinander schreiben sonst auf dieselbe
        // map_eigen01.*. Wer seinen Lauf benennt, tritt keinem anderen auf die
        // Datei. Ohne den Schalter bleibt es bei der Vorgabe des Feldes.
        bool run = false;
        foreach (string a in OS.GetCmdlineUserArgs())
        {
            if (a.StartsWith("--map-editor-name=")) _name.Text = a["--map-editor-name=".Length..];
            else if (a == "--map-editor-run" || a.StartsWith("--map-editor-run="))
            {
                _autoRun = a.Contains('=') ? a[(a.IndexOf('=') + 1)..] : "";
                run = true;
            }
        }
        if (run) CallDeferred(nameof(OnCreate));
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is InputEventKey { Pressed: true, Keycode: Key.Escape })
        {
            Close();
            GetViewport().SetInputAsHandled();
        }
    }

    private void Close() => QueueFree();

    // ---- die Kachelsaetze ---------------------------------------------------

    /// <summary>
    /// Das Auswahlfeld mit den Kachelsaetzen fuellen, die JETZT auffindbar sind
    /// — gefragt wird <see cref="MapGenerator.FindTileset"/>, also genau die
    /// Stelle, an der der Lauf selbst sucht.
    ///
    /// <para>⚠ Das ist keine Zierde. Der Einspieler legt <b>kein DATA-Verzeichnis</b>
    /// unter <c>user://data</c> an (nachgesehen am 12.08.2026: dort liegen
    /// Buildings, Effects, Maps, Sound, UI, Units — und sonst nichts), und die
    /// Engine oeffnet im Spiel nie eine <c>.CWP</c>. Auf einem Rechner ohne
    /// Entwicklungsbaum und ohne eingelegte CD findet der Editor also gar
    /// keinen Kachelsatz. Dann ist die Liste leer, der Knopf gesperrt und der
    /// Grund steht da — statt dass ein Lauf mit »Kachelsatz 47 nicht gefunden«
    /// endet.</para>
    /// </summary>
    private void FillTilesets()
    {
        string? hint = _from.Text.Trim().Length > 0 ? _from.Text.Trim() : null;
        _tilesets.Clear();
        _tileset.Clear();
        // 0..63: die gelieferten Saetze reichen von 01 bis 47, die Grenze ist
        // gross genug fuer einen nachgelegten und billig — je Nummer sind es
        // zwei File.Exists je Ordner.
        for (int t = 0; t <= 63; t++)
            if (MapGenerator.FindTileset(t, hint) != null) _tilesets.Add(t);

        foreach (int t in _tilesets) _tileset.AddItem($"{t:00}");
        int at = _tilesets.IndexOf(PreferredTileset);
        _tileset.Selected = at >= 0 ? at : 0;

        bool any = _tilesets.Count > 0;
        _tileset.Disabled = !any;
        _create.Disabled = !any;
        _status.Text = any
            ? $"{_tilesets.Count} Kachelsaetze gefunden: {string.Join(", ", _tilesets)}"
            : "Kein Kachelsatz gefunden — weder im Entwicklungsbaum (Assets/Legacy/DATA) noch "
            + "unter user://data/DATA noch auf einer eingelegten Spiel-CD. Der Einspieler legt "
            + "die NN.CWP/NN.PAL nicht ab, weil das Spiel sie nicht braucht; leg die CD ein "
            + "oder waehle oben den DATA-Ordner deiner Spielkopie.";
    }

    // ---- erzeugen -----------------------------------------------------------

    /// <summary>
    /// Der Knopf. Er baut nur die Zeichenkette der Befehlszeile zusammen und
    /// reicht sie an <c>MainMenu.MapNew</c> weiter — es gibt keinen zweiten
    /// Schreibweg, den man getrennt pruefen muesste.
    ///
    /// <para><c>async</c>, damit »arbeitet …« auch wirklich im Bild steht: der
    /// Lauf haelt den Faden fest (er backt das ganze Kartenbild), und ohne das
    /// eine gewartete Einzelbild wuerde die Zeile erst NACH dem Lauf
    /// erscheinen.</para></summary>
    private async void OnCreate()
    {
        string name = Sanitise(_name.Text);
        if (name.Length == 0) { _status.Text = "Die Karte braucht einen Namen."; return; }
        if (_tileset.Selected < 0 || _tileset.Selected >= _tilesets.Count)
        { _status.Text = "Kein Kachelsatz gewaehlt."; return; }
        int ts = _tilesets[_tileset.Selected];

        string outName = name.StartsWith("map_") ? name : "map_" + name;
        var lines = new List<string>();
        string maps = ProjectSettings.GlobalizePath(Core.Content.UserRoot).TrimEnd('/', '\\') + "/Maps";
        if (File.Exists($"{maps}/{outName}.json"))
            lines.Add($"⚠ {outName} gibt es schon — die Dateien werden ueberschrieben.");

        // ⚠ Der Ordner geht als »from=« mit, und der Trenner der Zeichenkette
        // ist das Komma: ein Pfad MIT Komma darin kaeme falsch an. Das ist die
        // eine Stelle, an der die Oberflaeche mehr weiss als der Schalter, also
        // sagt sie es hier, statt es krumm ankommen zu lassen.
        string from = _from.Text.Trim();
        if (from.Contains(','))
        {
            _status.Text = "Der Ordner darf kein Komma enthalten — daran trennt der Lauf seine Werte.";
            return;
        }

        string arg = $"{name},{(int)_w.Value},{(int)_h.Value},{ts}"
                   + (_flat.ButtonPressed ? ",flach" : "")
                   + $",boden={(int)_pick.Value}"
                   + (from.Length > 0 ? $",from={from}" : "");

        _create.Disabled = _check.Disabled = _view.Disabled = _edit.Disabled = true;
        _status.Text = $"arbeitet … ({(int)_w.Value}x{(int)_h.Value}, Kachelsatz {ts:00})";
        _log.Text = string.Join("\n", lines);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

        int rc = MainMenu.MapNew(arg, s => lines.Add(s));
        _log.Text = string.Join("\n", lines);
        _create.Disabled = false;

        if (rc == 0)
        {
            _made = outName;
            _check.Disabled = _view.Disabled = _edit.Disabled = false;
            _status.Text = $"{outName} geschrieben. Jetzt pruefen oder ansehen.";
            if (_autoRun == "pruefen") OnCheck();
            else if (_autoRun == "ansehen") OnViewPressed();
            else if (_autoRun == "bearbeiten") OnEditPressed();
        }
        else
        {
            _made = "";
            _status.Text = $"Fehlgeschlagen (Rueckgabe {rc}) — siehe Bericht.";
        }
    }

    /// <summary>Aus dem Bericht wird ein Dateiname; alles, was dort nichts zu
    /// suchen hat, faellt weg. Das Komma zuerst — daran trennt der Lauf seine
    /// Werte.</summary>
    private static string Sanitise(string s)
    {
        var sb = new System.Text.StringBuilder();
        foreach (char c in s.Trim())
            if (char.IsLetterOrDigit(c) || c == '_' || c == '-') sb.Append(c);
        return sb.ToString();
    }

    // ---- pruefen und ansehen ------------------------------------------------

    private void OnCheck()
    {
        if (_made.Length == 0) return;
        var lines = new List<string>();
        int rc = MapCheck.Run(_made, s => lines.Add(s));
        _log.Text = string.Join("\n", lines);
        _status.Text = rc == 0
            ? $"{_made}: Pruefung ohne Beanstandung."
            : $"{_made}: die Pruefung hat etwas gefunden (Rueckgabe {rc}) — siehe Bericht.";
    }

    private void OnViewPressed()
    {
        if (_made.Length == 0 || OnView == null) return;
        OnView(_made);
    }

    private void OnEditPressed()
    {
        if (_made.Length == 0 || OnEdit == null) return;
        OnEdit(_made);
    }

    // ---- Kleinteile, gebaut wie im Gefechtsschirm ---------------------------

    private static Label Hint(string text) => new()
    {
        Text = text,
        Modulate = new Color(0.6f, 0.65f, 0.72f),
        AutowrapMode = TextServer.AutowrapMode.WordSmart,
    };

    private static HBoxContainer Row(string label, Control field)
    {
        var h = new HBoxContainer();
        h.AddThemeConstantOverride("separation", 12);
        h.AddChild(new Label { Text = label, CustomMinimumSize = new Vector2(200, 0) });
        field.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        h.AddChild(field);
        return h;
    }

    /// <summary>Derselbe umrandete Abschnitt wie im Gefechtsaufbau
    /// (<c>MainMenu.Group</c>) — hier noch einmal, weil der dort privat und
    /// statisch ist und diese Datei ihn nur liest.</summary>
    private static PanelContainer Group(string title, Control content)
    {
        var p = new PanelContainer();
        var style = new StyleBoxFlat
        {
            BgColor = new Color(0.09f, 0.10f, 0.12f),
            BorderColor = new Color(0.24f, 0.27f, 0.31f),
            ContentMarginLeft = 12, ContentMarginRight = 12,
            ContentMarginTop = 8, ContentMarginBottom = 10,
        };
        style.SetBorderWidthAll(1);
        p.AddThemeStyleboxOverride("panel", style);

        var v = new VBoxContainer();
        v.AddThemeConstantOverride("separation", 6);
        p.AddChild(v);

        var h = new Label { Text = title, Modulate = new Color(0.62f, 0.72f, 0.85f) };
        h.AddThemeFontSizeOverride("font_size", 20);
        v.AddChild(h);
        content.SizeFlagsVertical = SizeFlags.ExpandFill;
        v.AddChild(content);
        return p;
    }
}
