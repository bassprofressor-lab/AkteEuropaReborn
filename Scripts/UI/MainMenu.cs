namespace AkteEuropaReborn.UI;

using System.Collections.Generic;
using Godot;
using AkteEuropaReborn.Rendering;

/// <summary>
/// The shell the game starts into. Built in code so there is no scene to keep
/// in sync — the menu is a plain Control that hands a <see cref="SkirmishSetup"/>
/// to the map scene and frees itself.
///
/// Campaign and network are listed but not wired yet; the buttons say so
/// instead of pretending.
/// </summary>
[GlobalClass]
public partial class MainMenu : Control
{
    /// <summary>The maps the player may pick, with the mission names the map
    /// files themselves carry.
    ///
    /// The three .DM entries are SAVED GAMES, not levels of their own: their
    /// elevation grids identify them as states of campaign levels 21, 25 and 26.
    /// They stay in the list because they carry things the .CWM levels do not —
    /// a filled sec47 design list above all — but they say so, so nobody
    /// wonders why "The Dam" appears twice.</summary>
    private static readonly System.Collections.Generic.List<(string File, string Title)> Maps = new()
    {
        ("map_NET07", "Gefechtsfeld (NET07)"),
        ("map_NET02", "Zwei Ufer (NET02)"),
        ("map_NET04", "Drei Haefen (NET04)"),
        ("map_NET05", "Weites Land (NET05)"),
        ("map_NET06", "Doppelhafen (NET06)"),
        ("map_DM_4", "The Dam — Spielstand zu Level 21"),
        ("map_DM_3", "Chanel Tunnel — Spielstand zu Level 25"),
        ("map_DM_1", "Scandinavia — Spielstand zu Level 26"),
        // ⚠ 11.08.2026 — hier standen map_05, map_10 und map_14 als
        // "Kampagne 05/10/14". Sie gehoeren nicht ins Gefecht: eine
        // Kampagnenkarte bringt ihr Missionsskript, ihre Diplomatie und ihren
        // Freischalt-Fahrplan mit, und im Gefecht laeuft nichts davon. Sie
        // standen aus der Zeit drin, als es die Kampagne noch nicht gab.
    };

    /// <summary>Die gewaehlte Karte, als Platz in <see cref="Maps"/>.
    ///
    /// <para>⚠ Das war bis zum 11.08.2026 <c>_map.Selected</c> eines
    /// Auswahlfelds. Seit die Karten nach Spielmodus getrennt in einer Liste
    /// stehen, ist die Zeilennummer der Liste NICHT mehr die Kartennummer —
    /// deshalb wird sie hier gefuehrt und nirgends sonst abgelesen.</para>
    /// </summary>
    private int _mapIndex;
    private ItemList _mapList = null!;
    private Label _modeHint = null!;
    private MapPreview.Shape _mode;
    private readonly List<(MapPreview.Shape S, Button B)> _modes = new();

    /// <summary>Welche Karte hinter welcher Zeile der Kartenliste steht.</summary>
    private readonly List<int> _listed = new();

    private OptionButton _slot = null!;
    private TextureRect _preview = null!;
    private Label _previewText = null!;
    private OptionButton _level = null!;
    private OptionButton? _res;

    /// <summary>»Alle Einheiten« — siehe <see cref="SkirmishSetup.AllUnits"/>.
    /// Optional, weil der Gefechtsschirm auch ohne die Zeile gebaut werden
    /// kann.</summary>
    private CheckBox? _allUnits;

    /// <summary>Die Zeile unter dem Haken, die seinen Zustand ausspricht: ohne
    /// ihn bleiben die Flughaefen der Karte ohne Angebot, und das soll dastehen,
    /// bevor der Spieler startet — nicht erst auffallen, wenn er drin ist.
    /// Siehe die Begruendung am Aufbau des Kastens.</summary>
    private Label? _allUnitsHint;

    /// <summary>Der Kasten um den Haken. Sein RAND traegt den Zustand — amber
    /// wenn aus, blau wenn an. Grund: das Haekchen-Symbol selbst ist auf dem
    /// dunklen Grund nur ein Fleck von wenigen Bildpunkten (am Bildschirmfoto
    /// vom 13.08. nachgesehen), der Rand dagegen ist ueber die ganze Breite zu
    /// sehen. Die Information darf nicht am kleinsten Element des Kastens
    /// haengen.</summary>
    private PanelContainer? _allUnitsPanel;

    private SpinBox _ai = null!;

    /// <summary>»Techstandard« 1..8 — siehe <see cref="SkirmishSetup.Techstandard"/>.
    /// Optional wie <see cref="_allUnits"/>, weil der Schirm auch ohne die Zeile
    /// gebaut werden kann.</summary>
    private SpinBox? _tech;
    private Label _hint = null!;

    /// <summary>The skirmish setup, which used to BE the menu and is now what
    /// the start menu's added "Gefecht" row leads to.</summary>
    private Control? _setup;

    /// <summary>Titel, Untertitel und Startknopf des Aufbauschirms. Sie sind
    /// Felder, weil derselbe Schirm seit dem 13.08.2026 zwei Zeilen des
    /// Startmenues bedient — »Gefecht« und »Multiplayer«. Siehe
    /// <see cref="ShowSetup"/>.</summary>
    private Label? _setupTitle;
    private Label? _setupSub;
    private Button? _startButton;
    private StartMenuPanel? _start;

    /// <summary>The start menu of 1997, rebuilt from the code that draws and
    /// hit-tests it — see <see cref="StartMenuPanel"/> for the addresses. The
    /// rows that have nowhere to go say so when clicked instead of being greyed
    /// out and silent; the one row that is ours is marked in the caption.
    /// </summary>
    private void BuildStartMenu()
    {
        var missions = Campaign.CampaignManager.Missions;

        // "Naechstes Demo" ist nur dann eine anwählbare Zeile, wenn es
        // überhaupt Demos gibt. Die Prüfung kostet nichts — sie sieht nur nach,
        // welche der dreizehn .DM der Import gebacken hat.
        bool demos = !_noBackdrop && MenuBackdrop.Available().Count > 0;

        var original = new System.Collections.Generic.List<StartMenuPanel.Row>(
            StartMenuPanel.Original(
                newGame: missions.Count > 0 ? ShowCampaign : null,
                load: () => AddChild(new LoadGameScreen()),
                // ⚠ 13.08.2026 — DIE ZEILE DES ORIGINALS FÜHRT JETZT WIRKLICH ZUM
                // NETZSPIEL. Sie war die ganze Zeit da (Platz 65, Hilfeindex 106,
                // gelesen @0x480454ff) und tat nichts; der Mehrspieler hing
                // stattdessen als Auswahlfeld im Gefechtsschirm. Gemeldet vom
                // Spieler: »du hast das netzwerkspiel unter gefecht gepackt,
                // wobei wir den Punkt im hauptmenu Netzwerkspiel haben … daher
                // das Netzwerkspiel raus aus Gefecht«.
                net: () => ShowSetup(net: true),
                settings: () => AddChild(new SettingsScreen()),
                encyclopedia: null,
                intro: null,
                credits: null,
                demo: demos ? NextDemo : null,
                quit: () => GetTree().Quit()));

        // ⚠ UNSERE SETZUNG, 11.08.2026: die erste Zeile heisst nicht mehr
        // »Neues Spiel«, sondern »Kampagne«, und sie startet nicht mehr wortlos
        // die naechste Mission, sondern oeffnet die Uebersicht ueber ALLE
        // Missionen (CampaignScreen). Gewuenscht wortwoertlich: »Anstatt Neues
        // Spiel nennen wir es Kampagne. Dort sieht man alle Missionen …«.
        //
        // ORIGINAL ist die Zeile selbst samt Platz und Hilfeindex 104 — sie
        // steht an erster Stelle und heisst dort »Neues Spiel« (gelesen
        // @0x480454ff). UNSER ist der neue Name und alles, was dahinter liegt:
        // das Original kennt keine Missionsauswahl, sein »Neues Spiel« beginnt
        // beim Stand des Kampagnenzaehlers word[0x539934].
        var renamed = StartMenuPanel.Recaption(original, "Neues Spiel", "Kampagne",
            "Alle Missionen zeigen — Uebersicht ist unsere Zutat");

        // ⚠ UNSERE SETZUNG, 13.08.2026, gewuenscht wortwoertlich: »den Punkt im
        // hauptmenu Netzwerkspiel … was du zu Multiplayer abaendern kannst und
        // dort der Multiplayer auch lebt«. Der Hilfeindex 106 bleibt stehen und
        // zeigt weiter auf »Netzwerk-Spiel einrichten« des Originals — die
        // Herkunft der Zeile geht durch die Umbeschriftung nicht verloren.
        renamed = StartMenuPanel.Recaption(renamed, "Netzwerkspiel", "Multiplayer",
            "Gegen Menschen ueber das Netz — Gastgeber oder beitreten");

        // ⚠ UNSERE SETZUNG, und die staerkere Sorte: die Zeile ist WEG, nicht nur
        // umbenannt. Gewuenscht: »Intro Ansehen kann auch aus dem Hauptmenu
        // raus.« Sie stand auf Platz 135 mit Hilfeindex 109 (»Intro-Film
        // ansehen«) und hatte keine Aktion — wir spielen die .RPL-Filme nicht ab,
        // und eine Zeile, die beim Anklicken nur sagt, dass sie nichts tut, ist
        // schlechter als keine. In StartMenuPanel.Original() bleibt sie stehen,
        // damit die neun gelesenen Zeilen dort vollstaendig bleiben.
        renamed = StartMenuPanel.Without(renamed, "Intro ansehen");

        // OURS, and the only row that is: the original had no skirmish against
        // a computer opponent, only "Netzwerkspiel" against people. It sits
        // right under that entry — the first version put it at the very end,
        // below "Beenden", and the skirmish was reported as missing from 0.3.0
        // because nobody looks there.
        // ⚠ »Multiplayer«, nicht mehr »Netzwerkspiel« — die Zeile darueber ist
        // eine Zeile weiter oben umbeschriftet worden, und InsertAfter sucht nach
        // der Beschriftung. Steht hier der alte Name, haengt »Gefecht« lautlos
        // hinten an, unter »Beenden« — genau der Fehler, wegen dem das Gefecht in
        // 0.3.0 als fehlend gemeldet wurde.
        var rows = StartMenuPanel.InsertAfter(renamed, "Multiplayer",
            new StartMenuPanel.Row(0, "Gefecht", -1,
                "Gefecht gegen den Rechner — im Original gibt es das nicht",
                () => ShowSetup()));

        // OURS, die zweite Zeile dieser Art, und aus demselben Grund an dieser
        // Stelle: der KARTENEDITOR. Es gab ihn seit dem 12.08.2026, aber nur
        // hinter zwei Schaltern der Befehlszeile (--map-new=, --map-check=) —
        // also hinter etwas, das ein Spieler nicht hat. Die Zeile sitzt direkt
        // unter »Gefecht«, weil beide UNSERE Zutaten sind und so beieinander
        // stehen; das Original verliert dadurch keine seiner neun Zeilen, es
        // rutscht nur alles ab »Einstellungen« um eine Zeilenhoehe nach unten
        // (StartMenuPanel.InsertAfter), und das Fenster waechst mit.
        //
        // Der Schirm dahinter liegt in Scripts/Editor/MapEditorScreen.cs,
        // ShowMapEditor() in Scripts/Editor/MainMenuMapEditor.cs — dort, wo
        // schon die beiden Laeufe des Editors stehen.
        rows = StartMenuPanel.InsertAfter(rows, "Gefecht",
            new StartMenuPanel.Row(0, "Karteneditor", -1,
                "Eine neue Karte erzeugen und pruefen — im Original gibt es das nicht",
                ShowMapEditor));

        _start = new StartMenuPanel
        {
            Footer = missions.Count > 0
                ? $"{missions.Count} Missionen · {Campaign.CampaignManager.Completed} geschafft"
                : "Keine Kampagne importiert",
            // ⚠ 11.08.2026 — KEIN X-Knopf mehr. Er war ohnehin unsere Deutung
            // (was das Kreuz im Original tut, ist ungelesen), und sein Rot
            // stand in keiner gelesenen Farbstelle. Gemeldet als »neben dem
            // schriftzug ist noch so ein rotes schließ kreuz, das brauchen wir
            // nicht«. Beenden steht als eigene Zeile in der Liste, der Knopf
            // war also auch doppelt. Close = null laesst ihn weg.
            Close = null,
        };
        _start.Rows.AddRange(rows);
        AddChild(_start);
    }

    // ---- das laufende Spiel hinter dem Menü ---------------------------------
    //
    // Was dort läuft und warum es die dreizehn .DM sind, steht vollständig in
    // MenuBackdrop — mit den Fundstellen in GAME.EXE. Hier steht nur, wann es
    // anfängt und wann es aufhört.

    private MenuBackdrop? _backdrop;
    private ColorRect? _empty;      // der schwarze Grund, wenn kein Demo läuft
    private bool _noBackdrop;       // --no-backdrop, für Prüfläufe

    /// <summary>Den Hintergrund anwerfen. Erst hier, ganz am Ende von
    /// <c>_Ready</c>: das Menü steht damit sofort, und die 20 bis 33 Megapixel
    /// der Karte werden nebenher von einem eigenen Faden gelesen (siehe
    /// MenuBackdrop.Next). Ein Lauf, der gleich in ein Gefecht startet, kommt
    /// hier gar nicht an und lädt deshalb auch nichts.</summary>
    private void StartBackdrop()
    {
        if (_noBackdrop || _backdrop != null) return;
        if (MenuBackdrop.Available().Count == 0) return;
        _backdrop = new MenuBackdrop();
        AddChild(_backdrop);
        // der schwarze Grund liegt im Standardlayer und würde die Ebene
        // darunter zudecken
        if (_empty != null) _empty.Visible = false;
    }

    /// <summary>Die Menüzeile »Naechstes Demo«: eins weiter, hinter dem letzten
    /// wieder von vorn — wie <c>dword[0x540740] = 2</c> im Original.</summary>
    private void NextDemo()
    {
        StartBackdrop();            // falls die Zeile vor dem ersten Bild kommt
        _backdrop?.Next();
    }

    /// <summary>Vor jedem Szenenwechsel: die Karte des Demos aus dem Speicher
    /// nehmen, bevor der MapViewer seine eigene lädt. QueueFree allein täte es
    /// erst nach dem Bild, und dann lägen zwei 30-MB-Texturen gleichzeitig da.
    /// </summary>
    private void StopBackdrop()
    {
        if (_backdrop == null) return;
        var b = _backdrop;
        _backdrop = null;
        b.Stop();
        b.QueueFree();
    }

    /// <summary>Die Menuezeile »Kampagne«: die Missionsuebersicht aufschlagen.
    ///
    /// <para>⚠ Sie ersetzt seit dem 11.08.2026 das fruehere StartCampaign(),
    /// das die naechste Mission ohne Rueckfrage startete. Wer nur weiterspielen
    /// will, klickt auf der Uebersicht die eine hell umrahmte Kachel — das ist
    /// ein Klick mehr, dafuer sieht man vorher, wo man steht.</para></summary>
    private void ShowCampaign() => AddChild(new CampaignScreen(StartMission));

    /// <summary>Den Aufbauschirm zeigen — als GEFECHT gegen den Rechner oder als
    /// MULTIPLAYER gegen Menschen.
    ///
    /// <para>⚠ 13.08.2026, gemeldet vom Spieler: der Mehrspieler hing als
    /// Auswahlfeld im Gefechtsschirm, waehrend das Startmenue eine eigene Zeile
    /// dafuer hat (die des Originals, Platz 65). Jetzt fuehrt die Zeile hierher,
    /// und im Gefecht ist von Netz nichts mehr zu sehen.</para>
    ///
    /// <para>⚠ Der Netzkasten wird trotzdem IMMER gebaut und nur versteckt: er
    /// haelt den Zustand der Verbindung, und ein Kasten, der beim Umschalten neu
    /// entsteht, verliert ihn. Im Gefecht steht sein Modus zwingend auf »Aus« —
    /// ein verstecktes Auswahlfeld, das noch »Gastgeber« sagt, waere ein
    /// Netzspiel, von dem der Spieler nichts weiss.</para></summary>
    private void ShowSetup(bool net = false)
    {
        _netEntry = net;
        if (_setupTitle != null) _setupTitle.Text = net ? "MULTIPLAYER" : "GEFECHT";
        if (_setupSub != null)
            _setupSub.Text = net
                ? "Gegen Menschen ueber das Netz — Gastgeber oder beitreten"
                : "Gegen den Rechner — im Original gibt es das nicht";
        if (_startButton != null)
            _startButton.Text = net ? "PARTIE STARTEN" : "GEMETZEL STARTEN";
        ApplyNetEntry(net);

        if (_start != null) _start.Visible = false;
        if (_setup != null) _setup.Visible = true;
    }

    private void ShowStartMenu()
    {
        if (_setup != null) _setup.Visible = false;
        if (_start != null) _start.Visible = true;
    }

    // ---- the harness: photograph the menu ------------------------------------
    //
    // MapViewer has had --shot since the map work; the menu had not, so every
    // change to it was checked by eye and described from memory. Same flags:
    //   Godot --path <proj> -- --shot=<datei.png> --shot-delay=<bilder>
    // A headless run has no viewport to read, so this needs a window.

    private string? _shotPath;
    private int _shotDelay = 30;

    private void ReadShotArgs()
    {
        foreach (string a in OS.GetCmdlineUserArgs())
        {
            if (a.StartsWith("--shot=")) _shotPath = a[7..];
            else if (a.StartsWith("--shot-delay=")) _shotDelay = a[13..].ToInt();
            else if (a.StartsWith("--menu-click=")) _clickRow = a[13..];
        }
    }

    /// <summary>`--menu-click=<caption>`: press a start-menu row through the
    /// real input path after a few frames, so a scripted run checks that the
    /// row WORKS and not merely that it is drawn.</summary>
    private string? _clickRow;
    private int _clickIn = 20;

    public override void _Process(double delta)
    {
        if (_clickRow != null && _start != null && _clickIn-- <= 0)
        {
            string want = _clickRow;
            _clickRow = null;
            GD.Print("menu: Eintraege " + string.Join(" · ", _start.Captions()));
            if (!_start.RowCentre(want, out var at))
            { GD.PrintErr($"menu: keinen Eintrag \"{want}\" gefunden"); GetTree().Quit(2); return; }
            foreach (bool down in new[] { true, false })
                Input.ParseInputEvent(new InputEventMouseButton
                {
                    ButtonIndex = MouseButton.Left, Pressed = down,
                    Position = at, GlobalPosition = at,
                });
            GD.Print($"menu: \"{want}\" bei ({at.X:0},{at.Y:0}) geklickt");
        }

        if (_shotPath == null) return;
        if (_shotDelay-- > 0) return;
        var img = GetViewport().GetTexture().GetImage();
        var err = img.SavePng(_shotPath);
        GD.Print(err == Error.Ok
            ? $"menu: Bild nach {_shotPath} ({img.GetWidth()}x{img.GetHeight()})"
            : $"menu: Bild konnte nicht geschrieben werden ({err})");
        _shotPath = null;
        GetTree().Quit(err == Error.Ok ? 0 : 1);
    }

    public override void _Ready()
    {
        // ⚠ WER HIER STEHT, HAT DIE SPIELWELT VERLASSEN — egal durch welche
        // Tuer (Pausenmenue, Abschlussfenster, ToMenu, Editor). Das ist der EINE
        // Eingang, und darum raeumt er die Helfer ab, die absichtlich an
        // SceneTree.Root parken und jeden Szenenwechsel ueberleben. Warum die
        // Kur hier sitzt und nicht an den neun Ausgaengen, steht samt Befund in
        // Core/LeaveToMenu.cs — B9 (Editorfeld im Gefecht) und B10 (Popups im
        // Hauptmenue) sind zwei Gesichter derselben fehlenden Gegenstelle.
        Core.LeaveToMenu.Tidy();
        if (Core.LeaveToMenu.Report)
        {
            Core.LeaveToMenu.Report = false;
            int rc = Core.LeaveToMenu.Count(GetTree(), out string report);
            GD.Print(report);
            // ⚠ VOR StartBackdrop() aussteigen, wie jeder andere kopflose
            // Schalter — die Kulisse laedt 20..33 Megapixel auf einem
            // Nebenlaeufer, und der greift beim Herunterfahren ins Leere.
            GetTree().Quit(rc);
            return;
        }
        SetAnchorsPreset(LayoutPreset.FullRect);
        foreach (string a in OS.GetCmdlineUserArgs())
            if (a == "--no-backdrop") _noBackdrop = true;
        _empty = new ColorRect
        {
            Color = new Color(0.05f, 0.06f, 0.08f),
            AnchorRight = 1, AnchorBottom = 1,
        };
        AddChild(_empty);

        // The setup is taller than a 720p window, and anchored to the middle it
        // simply lost its title at the top and its hint line at the bottom. It
        // lives in a scroller now, so a small window costs a scrollbar and not
        // the content.
        var scroll = new ScrollContainer
        {
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            Visible = false,          // shown from the start menu's "Gefecht"
        };
        scroll.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _setup = scroll;
        AddChild(scroll);

        var middle = new CenterContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        scroll.AddChild(middle);

        var box = new VBoxContainer { CustomMinimumSize = new Vector2(880, 0) };
        box.AddThemeConstantOverride("separation", 10);
        middle.AddChild(box);

        // ⚠ Titel und Untertitel sind seit dem 13.08.2026 FELDER, weil dieser
        // eine Schirm jetzt zwei Eintraege des Startmenues bedient: »Gefecht«
        // (gegen den Rechner) und »Multiplayer« (gegen Menschen). ShowSetup(net)
        // schreibt sie um. Es ist ausdruecklich EIN Schirm geblieben — Karte,
        // Startplatz, Techstandard, Rohstoffe und »alle Einheiten« gelten fuer
        // beides, und ein zweiter Schirm daneben waere ein zweiter Ort fuer
        // dieselben Einstellungen und damit ein zweiter Ort, an dem sie
        // auseinanderlaufen. Das Original macht es genauso: sein Aufbaubild ist
        // eines, Kommando 979 traegt die Einstellungen ein, 981 startet.
        _setupTitle = new Label
        {
            Text = "GEFECHT",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        _setupTitle.AddThemeFontSizeOverride("font_size", 34);
        box.AddChild(_setupTitle);
        var sub = _setupSub = new Label
        {
            // ⚠ Die Untertitelzeile sagt jetzt, was diese Seite ist. Vorher
            // stand hier »AKTE EUROPA — REBORN / Rekonstruktion des RTS von
            // 1997«, was aus der Zeit stammt, als DIESER Schirm das ganze
            // Hauptmenue war. Seit es das Startmenue von 1997 gibt, ist das
            // hier nur noch einer von dessen Eintraegen — und ein Eintrag, den
            // das Original nicht kennt.
            Text = "Gegen den Rechner — im Original gibt es das nicht",
            HorizontalAlignment = HorizontalAlignment.Center,
            Modulate = new Color(0.7f, 0.75f, 0.8f),
        };
        box.AddChild(sub);
        box.AddChild(new HSeparator());

        // ---- oben: der Spielmodus ---------------------------------------------
        //
        // ⚠ UNSERE ZUTAT, aber KEINE erfundene Liste. Die drei Formen sind die,
        // die Simulation/SkirmishAi.StartSkirmish tatsaechlich unterscheidet —
        // Eroberung (neutrale Gebaeude zum Besetzen), Aufbau (ein Platz hat eine
        // Fabrik) und »wie gezeichnet« (weder noch). Welche davon greift, haengt
        // an der KARTE und nicht an einem Schalter; siehe MapPreview.Shape.
        // Deshalb waehlt der Modus hier keine Regel aus, sondern zeigt darunter
        // die Karten, die die Maschine wirklich so spielt. Formen, zu denen
        // keine eingelesene Karte gehoert, erscheinen gar nicht erst.
        var modeBox = new VBoxContainer();
        modeBox.AddThemeConstantOverride("separation", 4);
        box.AddChild(Group("Spielmodus", modeBox));

        var modeRow = new HBoxContainer();
        modeRow.AddThemeConstantOverride("separation", 8);
        modeBox.AddChild(modeRow);
        foreach (var s in new[] { MapPreview.Shape.Conquest, MapPreview.Shape.BuildUp,
                                  MapPreview.Shape.AsDrawn })
        {
            int n = 0;
            foreach (var (file, _) in Maps) if (MapPreview.ShapeOf(file) == s) n++;
            if (n == 0) continue;
            var b = new Button
            {
                Text = $"{MapPreview.ShapeName(s)}  ({n} {(n == 1 ? "Karte" : "Karten")})",
                ToggleMode = true,
                CustomMinimumSize = new Vector2(0, 40),
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                TooltipText = MapPreview.ShapeHint(s),
                FocusMode = FocusModeEnum.None,
            };
            // Godots dunkles Standardaussehen unterscheidet »gedrueckt« kaum von
            // »nicht gedrueckt« — im ersten Bild sah der NICHT gewaehlte Modus
            // wie blosser Text aus. Der gewaehlte bekommt deshalb einen eigenen
            // Grund und einen hellen Rahmen.
            foreach (var (state, bg, edge, w) in new (string, Color, Color, int)[]
            {
                ("normal",        new(0.14f, 0.15f, 0.18f), new(0.28f, 0.31f, 0.36f), 1),
                ("hover",         new(0.19f, 0.21f, 0.25f), new(0.40f, 0.45f, 0.52f), 1),
                ("pressed",       new(0.17f, 0.27f, 0.39f), new(0.55f, 0.72f, 0.90f), 2),
                ("hover_pressed", new(0.21f, 0.32f, 0.45f), new(0.65f, 0.82f, 1.00f), 2),
            })
            {
                var sb = new StyleBoxFlat { BgColor = bg, BorderColor = edge };
                sb.SetBorderWidthAll(w);
                b.AddThemeStyleboxOverride(state, sb);
            }
            var pick = s;
            b.Pressed += () => SelectMode(pick);
            _modes.Add((s, b));
            modeRow.AddChild(b);
        }
        _modeHint = Hint("");
        _modeHint.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        modeBox.AddChild(_modeHint);

        // ---- darunter: links die Karten dieses Modus, rechts die Einstellungen -
        var lower = new HBoxContainer();
        lower.AddThemeConstantOverride("separation", 12);
        box.AddChild(lower);

        _mapList = new ItemList
        {
            // ⚠ 160 statt 250 seit dem 13.08.2026, und die Zahl ist am Bild
            // gemessen. Seit der Netzkasten UNTER dieser Liste steht, ist die
            // linke Spalte »Liste + Kasten« — mit einer Mindesthoehe von 250
            // wurde sie zum Hoehentreiber des ganzen Schirms und schob im
            // Multiplayer den Startknopf aus dem Fenster (795 statt 690 px).
            // Die Liste WAECHST ohnehin mit (SizeFlagsVertical = ExpandFill), sie
            // braucht die Mindesthoehe also nicht: mit 160 gibt die rechte Spalte
            // die Hoehe vor, die Liste faellt auf ~255 px und zeigt ihre sieben
            // Eintraege weiter vollstaendig.
            CustomMinimumSize = new Vector2(400, 160),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            AllowReselect = true,
        };
        // Die Titel samt ihrer Hinweise sind die der Kartenliste oben und
        // bleiben Wort fuer Wort stehen — vor allem der Zusatz »Spielstand zu
        // Level 21/25/26« bei den drei .DM, der sagt, dass das keine eigenen
        // Karten sind.
        _mapList.ItemSelected += i => PickFromList((int)i);
        var left = Group("Karten", _mapList);
        left.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        left.SizeFlagsVertical = SizeFlags.ExpandFill;

        // ⚠ 13.08.2026 — DER NETZKASTEN STEHT LINKS UNTER DER KARTENLISTE, und
        // das ist der dritte Anlauf und am BILD entschieden:
        //   1. in voller Breite unter dem Haken »Luftwaffe …« → »GEMETZEL
        //      STARTEN« war nur noch zur Haelfte zu sehen;
        //   2. in der Einstellungsspalte rechts → im Multiplayer-Schirm war der
        //      Startknopf ganz aus dem Fenster geschoben (die drei Netzzeilen und
        //      ihre Statuszeile kosten rund 180 px, und rechts stehen schon
        //      Vorschau, Kartenname, Gegner, Startplatz, Schwierigkeit,
        //      Rohstoffe und Techstandard);
        //   3. LINKS, wo die Kartenliste mit ihren sieben Eintraegen rund 200 px
        //      Luft laesst. Dort kostet der Kasten den Schirm KEINE Hoehe.
        // Ein Startknopf, den man erst herunterrollen muss, ist schlechter als
        // einer, den man sieht — der ScrollContainer rettet die Bedienbarkeit,
        // nicht die Gestaltung.
        // ⚠ KEIN ExpandFill vertikal, und das ist ausprobiert und wieder
        // zurueckgenommen: der ganze Schirm sitzt in einem ScrollContainer, und
        // dort bestimmt der INHALT die Hoehe, nicht die Hoehe den Inhalt. Es gibt
        // also nichts zu verteilen — das Attribut sah nach einer Loesung aus und
        // hat im Bild nichts geaendert. Ein wirkungsloses Attribut stehenzulassen
        // waere schlimmer, als es nicht zu setzen: der Naechste glaubt, es tue was.
        var leftCol = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        leftCol.AddThemeConstantOverride("separation", 10);
        leftCol.AddChild(left);
        leftCol.AddChild(_netFrame = Accent("Netzwerk", BuildNetRow()));
        lower.AddChild(leftCol);

        var right = new VBoxContainer { CustomMinimumSize = new Vector2(440, 0) };
        right.AddThemeConstantOverride("separation", 6);
        lower.AddChild(Group("Einstellungen", right));

        // the picture of the chosen map, with its original name underneath
        _preview = new TextureRect
        {
            // ⚠ 96 statt 120 seit dem 13.08.2026: die Zeile »Techstandard« hat
            // die Einstellungsspalte hoeher gemacht als die Kartenliste, und
            // damit rutschte die Bedienhilfe unten aus dem Fenster — am Bild
            // gesehen, nicht gerechnet. Die Vorschau vertraegt die 24 px, eine
            // fehlende Zeile am unteren Rand nicht.
            CustomMinimumSize = new Vector2(0, 96),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            TextureFilter = TextureFilterEnum.Nearest,
        };
        right.AddChild(_preview);
        _previewText = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            Modulate = new Color(0.7f, 0.75f, 0.8f),
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        right.AddChild(_previewText);
        right.AddChild(new HSeparator());

        right.AddChild(Row("Gegner", _ai = new SpinBox { MinValue = 1, MaxValue = 7, Value = 3 }));

        // Which of the map's eight slots the human takes. The slots are the
        // original's own — a NET map fills some and leaves others empty — so
        // "Automatisch" means the first one this map actually uses, and a
        // chosen slot that the map leaves empty falls back to the same.
        right.AddChild(Row("Startplatz", _slot = new OptionButton()));
        _slot.AddItem("Automatisch");
        for (int p = 0; p < 8; p++) _slot.AddItem($"Spieler {p + 1}");
        _slot.Selected = 0;

        right.AddChild(Row("Schwierigkeit", _level = new OptionButton()));
        _level.AddItem("Leicht");
        _level.AddItem("Normal");
        _level.AddItem("Schwer");
        _level.Selected = 1;

        // "Rohstoffe" is the original's own option, down to the four words and
        // their order — they come out of the menu code that switches the same
        // setting (Import/ExeTables.ResourceLevels). Where the table cannot be
        // read the row is left out rather than filled with invented names.
        var resNames = MapEntityLayer.ResourceLevelNames();
        if (resNames.Count > 0)
        {
            right.AddChild(Row("Rohstoffe", _res = new OptionButton()));
            foreach (string n in resNames) _res.AddItem(n);
            _res.Selected = Mathf.Clamp(SkirmishSetup.Resources, 0, resNames.Count - 1);
        }

        // »Techstandard« ist die Einstellung des ORIGINALS, nicht unsere: der
        // Bereich 1..8 ist der des Knopfs @0x44D3BC, die Beschriftung steht als
        // `Techstandard ` bei 0x502630, und der Gastgeber traegt sie mit Kommando
        // 979 ein. Sie entscheidet ueber die Freigaben der Flugzeug- UND
        // Schiffsliste (Tor @0x419F30, dann @0x4B2380) — siehe
        // SkirmishSetup.Techstandard und MapEntityLayer.AirProbeTechstandard.
        //
        // ⚠ SIE STEHT ERST HIER, SEIT SIE WIRKT. Vor der Engine-Seite waere es
        // ein Schalter ohne Wirkung gewesen, und genau daran ist der Spieler
        // heute morgen schon einmal haengengeblieben.
        SkirmishSetup.Techstandard = Settings.SkirmishTechstandard;
        right.AddChild(Row("Techstandard", _tech = new SpinBox
        {
            MinValue = 1, MaxValue = 8, Value = SkirmishSetup.Techstandard,
            TooltipText = "Technikstufe, mit der das Gefecht anfaengt — die "
                        + "Einstellung des Originals (1..8).\n"
                        + "Gemessen gibt sie am Flughafen frei:\n"
                        + "1-3  Treibstoffheli, Munitionheli\n"
                        + "4    dazu Kampfhubschrauber\n"
                        + "5    dazu Spionageflieger\n"
                        + "6-8  dazu Jagdflieger und Bomber\n"
                        + "Gilt ebenso fuer die Schiffsliste.",
        }));

        // ⚠ 17.08.2026 — FEHLER C16: »Techstandard -> woher weiss ich wieviele
        // Techstandards es gibt, und was sie bewirken?« Die Antwort stand bis
        // heute AUSSCHLIESSLICH im TooltipText darueber, und ein Hinweis, den man
        // nur sieht, wenn man schon ahnt, dass dort einer steht, beantwortet die
        // Frage nicht. Deshalb eine Zeile UNTER dem Regler, die bei jeder
        // Aenderung sagt, was diese Stufe freigibt.
        //
        // ⚠ Der Text kommt aus TechstandardEffect() und damit aus DERSELBEN
        // Quelle wie die Wirkung: den Techschwellen in component_stats.json,
        // gelesen vom Tor @0x419F30. Eine zweite, von Hand gepflegte Liste
        // haette genau so lange gestimmt, bis jemand eine Schwelle anfasst.
        var techNote = new Label
        {
            Modulate = new Color(0.70f, 0.75f, 0.80f),
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            Text = MapEntityLayer.TechstandardEffect((int)_tech.Value),
        };
        right.AddChild(techNote);
        _tech.ValueChanged += v => techNote.Text = MapEntityLayer.TechstandardEffect((int)v);

        // ⚠ <s>DIE LOBBY steht IN der Einstellungsspalte.</s> Seit dem 13.08.2026
        // steht sie LINKS unter der Kartenliste — die Begruendung samt der drei
        // Anlaeufe steht dort, an `leftCol`. Kurz: in dieser Spalte hier hat sie
        // im Multiplayer-Schirm den Startknopf ganz aus dem Fenster geschoben.

        // ⚠ UNSERE OPTION, siehe SkirmishSetup.AllUnits. Der Anlass ist eine
        // Luecke in den Daten: die Gefechtskarten tragen in sec120 NULL
        // Flugzeugvorlagen (nachgezaehlt auf NET02, NET05, NET07), der Flughafen
        // hat dort also nichts anzubieten. Mit dem Haken bekommen alle acht
        // Spieler die acht Vorlagen aus aircraft.json, und am Boden entfaellt
        // die Freigabe-Pruefung.
        //
        // ⚠ 13.08.2026 — HERAUSGEHOBEN UND GESPEICHERT, nach einem Spielerbefund
        // am echten Spielweg: »ich hatte im Gefecht am Flughafen keine
        // Flugeinheiten zur Auswahl«, das Gebaeude war eingenommen und der
        // Installer war der neue. Der Haken war die Ursache, und der Spieler
        // sagt dazu: »der ist aber schwer wahrzunehmen an der Stelle«. Er stand
        // als vierte Row() unter Gegner/Startplatz/Schwierigkeit/Rohstoffe, in
        // der 440 px schmalen Einstellungsspalte — und sah damit aus wie eine
        // Einstellung unter vielen, waehrend er in Wahrheit entscheidet, ob die
        // Flughaefen der Karte ueberhaupt etwas tun (NET02 sieben, NET04 acht).
        // Deshalb: eigener Kasten mit Akzentrand ueber die ganze Breite, direkt
        // ueber dem Startknopf, und eine Zeile darunter, die den Zustand
        // AUSSPRICHT statt ihn nur zu setzen.
        //
        // ⚠ Die Vorgabe bleibt AUS. Sie zu drehen waere eine stille Setzung,
        // solange ungelesen ist, woher das Original im Netzwerkspiel seine
        // Flugzeugvorlagen nimmt — bei den Schiffen kopiert @0x4b2330 den Block
        // von Spieler 0 in die anderen sieben, fuer die Flugzeuge ist das
        // Gegenstueck noch nicht gefunden. Gespeichert wird sie trotzdem
        // (Settings.SkirmishAllUnits): sie war ein fluechtiges static bool, ein
        // gesetzter Haken war beim naechsten Start wieder weg.
        SkirmishSetup.AllUnits = Settings.SkirmishAllUnits;
        _allUnits = new CheckBox
        {
            Text = "Luftwaffe und ganze Entwurfsliste kaufbar",
            ButtonPressed = SkirmishSetup.AllUnits,
            TooltipText = "Gefechtskarten bringen keine Flugzeugvorlagen mit. " +
                          "Mit dieser Option bekommt jeder Spieler alle acht — " +
                          "die Gegner ebenso.",
        };
        _allUnits.AddThemeFontSizeOverride("font_size", 20);
        _allUnitsHint = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _allUnitsHint.AddThemeFontSizeOverride("font_size", 14);
        _allUnits.Toggled += _ => UpdateAllUnitsHint();

        box.AddChild(new HSeparator());

        var allUnitsBox = new VBoxContainer();
        allUnitsBox.AddThemeConstantOverride("separation", 2);
        allUnitsBox.AddChild(_allUnits);
        allUnitsBox.AddChild(_allUnitsHint);
        // ⚠ OHNE Titelzeile, und das ist am Bild entschieden, nicht geschaetzt.
        // Der erste Anlauf hatte eine (»Luftwaffe und ganze Entwurfsliste«) —
        // im Bildschirmfoto vom 13.08. doppelte sie den Text des Hakens direkt
        // darunter UND machte den Schirm eine Zeile zu hoch: »Zurueck zum
        // Menue« stand halb ausserhalb des Fensters. Der Haken IST die Zeile.
        _allUnitsPanel = Accent("", allUnitsBox);
        box.AddChild(_allUnitsPanel);
        UpdateAllUnitsHint();

        var start = _startButton = new Button
        {
            // ⚠ Der Text wechselt mit dem Einstieg (ShowSetup): »GEMETZEL
            // STARTEN« im Gefecht, »PARTIE STARTEN« im Multiplayer. »Gemetzel«
            // passt nicht zu einem Knopf, der erst auf Mitspieler wartet.
            Text = "GEMETZEL STARTEN", CustomMinimumSize = new Vector2(0, 44),
        };
        // ⚠ StartWhenNetIsReady statt OnStart — ohne Netzschalter derselbe
        // Aufruf, mit Netzschalter das Warten auf die Partie (UI/MainMenuNet.cs).
        start.Pressed += StartWhenNetIsReady;
        box.AddChild(start);

        // ⚠ 11.08.2026 — HIER STAND DIE KAMPAGNE, und sie ist raus. Gemeldet
        // als »unter Gefecht sieht man immer noch Kampagne unten drunter, das
        // muss raus«. Es waren drei Sachen: der Knopf »KAMPAGNE — 02 — Hidden
        // Bases«, die Auswahl »Mission waehlen« mit allen 33 Missionen und die
        // Zeile »33 Missionen · N geschafft«.
        //
        // Es war ein Rest, genau wie die Kampagnenkarten map_05/10/14 in der
        // Liste oben (am selben Tag entfernt): dieser Schirm WAR einmal das
        // ganze Hauptmenue, und damals musste die Kampagne irgendwo stehen.
        // Seit das Startmenue von 1997 da ist, gehoert sie in dessen Zeile
        // »Kampagne« — und seit heute in die Missionsuebersicht dahinter
        // (CampaignScreen). Ein Gefechtsschirm, der nebenbei die Kampagne
        // startet, hat zwei Wege in dieselbe Sache und keinen davon dort, wo
        // man ihn sucht.
        var back = new Button { Text = "Zurueck zum Menue" };
        back.Pressed += ShowStartMenu;
        box.AddChild(back);

        _hint = new Label
        {
            Text = "Links waehlen/ziehen · Rechts klicken = Befehl, Rechts ziehen = Karte schieben · B bauen · Esc zurueck",
            HorizontalAlignment = HorizontalAlignment.Center,
            Modulate = new Color(0.55f, 0.6f, 0.66f),
        };
        box.AddChild(_hint);

        BuildStartMenu();

        Settings.Apply();
        // ⚠ UNSERE SETZUNG, und sie aendert eine Voreinstellung: der Schirm geht
        // in dem Modus auf, zu dem die MEISTEN Karten gehoeren, und waehlt
        // dessen erste. Bis zum 11.08.2026 war die Voreinstellung starr
        // map_NET07 — und ausgerechnet NET07 ist die einzige Karte in der Form
        // »Wie gezeichnet«. Der Gefechtsschirm waere also mit einer Liste von
        // genau einem Eintrag aufgegangen, was das Gegenteil von
        // uebersichtlich ist. NET07 bleibt einen Klick entfernt.
        SelectMode(BusiestMode());
        ReadShotArgs();
        // --setup opens the skirmish panel straight away, so it can be
        // photographed without a click
        foreach (string a in OS.GetCmdlineUserArgs())
            // `--setup` zeigt das Gefecht, `--setup=net` den Multiplayer — sonst
            // liesse sich der zweite Einstieg nicht photographieren, und ein
            // Schirm, der nur im Kopf richtig aussieht, ist nichts wert.
            if (a == "--setup" || a.StartsWith("--setup="))
            {
                ShowSetup(net: a.EndsWith("=net"));
                break;
            }

        // the porting harness: decode with the new C# reader and compare
        // against the Python reference before anything else happens
        foreach (string a in OS.GetCmdlineUserArgs())
            if (a.StartsWith("--import="))
            {
                // several folders separated by ';': an installation is not
                // always complete on its own — see ContentSources.FromFolders
                string[] dirs = a["--import=".Length..].Split(';', System.StringSplitOptions.RemoveEmptyEntries);
                var src = Core.ContentSources.FromFolders(dirs);
                if (src == null) { GD.PrintErr("import: keiner der Ordner existiert"); GetTree().Quit(2); return; }
                bool ok = new Import.ContentBuilder(src).Run();
                GetTree().Quit(ok ? 0 : 1);
                return;
            }
            else if (a.StartsWith("--reexport-states="))
            {
                // only the game-state files, the pictures stay as they are
                string[] dirs = a["--reexport-states=".Length..]
                    .Split(';', System.StringSplitOptions.RemoveEmptyEntries);
                var src = Core.ContentSources.FromFolders(dirs);
                if (src == null) { GD.PrintErr("reexport: keiner der Ordner existiert"); GetTree().Quit(2); return; }
                GetTree().Quit(new Import.ContentBuilder(src).ReexportStates() ? 0 : 1);
                return;
            }
            // the mission list on its own. Reads the maps ALREADY imported, so
            // the source folder is only good for the slot names out of GAME.EXE
            // — `--reexport-campaign=` with no folder is a fair way to ask for it.
            else if (a.StartsWith("--reexport-campaign"))
            {
                string rest = a["--reexport-campaign".Length..].TrimStart('=');
                string[] dirs = rest.Split(';', System.StringSplitOptions.RemoveEmptyEntries);
                var src = (dirs.Length > 0 ? Core.ContentSources.FromFolders(dirs) : null)
                          ?? Core.ContentSources.Discs()
                          ?? new Core.ContentSources.Source { Label = "(ohne Quelle)" };
                GetTree().Quit(new Import.ContentBuilder(src).ReexportCampaign() ? 0 : 1);
                return;
            }
            else if (a.StartsWith("--reexport-help="))
            {
                string[] dirs = a["--reexport-help=".Length..]
                    .Split(';', System.StringSplitOptions.RemoveEmptyEntries);
                var src = Core.ContentSources.FromFolders(dirs);
                if (src == null) { GD.PrintErr("reexport: keiner der Ordner existiert"); GetTree().Quit(2); return; }
                GetTree().Quit(new Import.ContentBuilder(src).ReexportHelp() ? 0 : 1);
                return;
            }
            else if (a.StartsWith("--reexport-tables="))
            {
                // only the tables read out of GAME.EXE — costs a second
                string[] dirs = a["--reexport-tables=".Length..]
                    .Split(';', System.StringSplitOptions.RemoveEmptyEntries);
                var src = Core.ContentSources.FromFolders(dirs);
                if (src == null) { GD.PrintErr("reexport: keiner der Ordner existiert"); GetTree().Quit(2); return; }
                GetTree().Quit(new Import.ContentBuilder(src).ReexportTables() ? 0 : 1);
                return;
            }
            // the tileset building files on their own — patterns, cell
            // animations and the tile atlas. No map is baked.
            else if (a.StartsWith("--reexport-buildings="))
            {
                string[] dirs = a["--reexport-buildings=".Length..]
                    .Split(';', System.StringSplitOptions.RemoveEmptyEntries);
                var src = Core.ContentSources.FromFolders(dirs);
                if (src == null) { GD.PrintErr("reexport: keiner der Ordner existiert"); GetTree().Quit(2); return; }
                GetTree().Quit(new Import.ContentBuilder(src).ReexportBuildings() ? 0 : 1);
                return;
            }
            // nur die Effektbilder aus ANIM.CWA
            else if (a.StartsWith("--reexport-effects="))
            {
                string[] dirs = a["--reexport-effects=".Length..]
                    .Split(';', System.StringSplitOptions.RemoveEmptyEntries);
                var src = Core.ContentSources.FromFolders(dirs);
                if (src == null) { GD.PrintErr("reexport: keiner der Ordner existiert"); GetTree().Quit(2); return; }
                GetTree().Quit(new Import.ContentBuilder(src).ReexportEffects() ? 0 : 1);
                return;
            }
            else if (a.StartsWith("--wiki-export="))
            {
                // Das Wiki fuer die Webseite — Frontmatter und Fliesstext je
                // Einheit, Gebaeude und Mission, samt Bildern. Laeuft auf dem
                // EINGESPIELTEN Inhalt, braucht also keine CD.
                Export.WikiExporter.Run(a["--wiki-export=".Length..], GD.Print);
                GetTree().Quit(0);
                return;
            }
            else if (a.StartsWith("--reexport-units="))
            {
                string[] dirs = a["--reexport-units=".Length..]
                    .Split(';', System.StringSplitOptions.RemoveEmptyEntries);
                var src = Core.ContentSources.FromFolders(dirs);
                if (src == null) { GD.PrintErr("reexport: keiner der Ordner existiert"); GetTree().Quit(2); return; }
                GetTree().Quit(new Import.ContentBuilder(src).ReexportUnits() ? 0 : 1);
                return;
            }
            else if (a == "--import-cd")
            {
                var discs = Core.ContentSources.Discs();
                if (discs == null) { GD.PrintErr("import: keine Spiel-CD im Laufwerk"); GetTree().Quit(2); return; }
                bool ok = new Import.ContentBuilder(discs).Run();
                GetTree().Quit(ok ? 0 : 1);
                return;
            }
            // Die zwei Schalter des Karteneditors. ⚠ Sie MUESSEN hier stehen und
            // nicht in Scripts/Editor/MainMenuMapEditor.cs, wo sie zuerst lagen:
            // dort hingen sie an `_Notification(NotificationEnterTree)`, also VOR
            // `_Ready` — und `_Ready` lief danach trotzdem ganz durch und startete
            // die Menuekulisse, die ihr 20..33 Megapixel grosses Bild auf einem
            // Nebenlaeufer laedt. Waehrend die Engine schon herunterfuhr, griff
            // der ins Leere: Zugriffsfehler in Image.LoadFromFile, Rueckgabewert
            // 139. Der Ausweg dort war eine Wartezeit von vier Sekunden vor dem
            // Quit. Hier braucht es sie nicht, weil dieser Zweig VOR
            // StartBackdrop() aussteigt — wie jeder andere kopflose Schalter.
            else if (a.StartsWith("--map-new="))
            {
                GetTree().Quit(MapNew(a["--map-new=".Length..]));
                return;
            }
            else if (a.StartsWith("--map-check="))
            {
                GetTree().Quit(Editor.MapCheck.Run(a["--map-check=".Length..], Say));
                return;
            }
            else if (a.StartsWith("--selftest-rail="))
            {
                GetTree().Quit(Import.ImportSelfTest.RunRail(a["--selftest-rail=".Length..]));
                return;
            }
            else if (a.StartsWith("--selftest-cwp="))
            {
                string dir = a["--selftest-cwp=".Length..];
                int rc = Import.ImportSelfTest.RunCwp(dir);
                rc |= Import.ImportSelfTest.RunCwpSweep(dir);
                rc |= Import.ImportSelfTest.RunBuildPatterns(dir);
                rc |= Import.ImportSelfTest.RunCwm(dir);
                rc |= Import.ImportSelfTest.RunEntities(dir);
                rc |= Import.ImportSelfTest.RunTerrain(dir);
                foreach (string cab in Core.ContentSources.Cabinets())
                    rc |= Import.ImportSelfTest.RunIsc(dir, cab);
                rc |= Import.ImportSelfTest.RunUnits();
                rc |= Import.ImportSelfTest.RunInterface();
                rc |= Import.ImportSelfTest.RunCwr(dir);
                rc |= Import.ImportSelfTest.RunExe(dir);
                rc |= Import.ImportSelfTest.RunBake(dir, new[] { "01", "05", "10", "NET02" });
                rc |= Import.ImportSelfTest.RunDesigns();
                rc |= Import.ImportSelfTest.RunBriefings();
                GetTree().Quit(rc);
                return;
            }
            // the baker on its own — it is the slowest single test and the one
            // that changes whenever a picture-side reading moves
            else if (a.StartsWith("--selftest-bake="))
            {
                GetTree().Quit(Import.ImportSelfTest.RunBake(
                    a["--selftest-bake=".Length..], new[] { "01", "05", "10", "NET02" }));
                return;
            }
            else if (a.StartsWith("--selftest-terrain="))
            {
                GetTree().Quit(Import.ImportSelfTest.RunTerrain(a["--selftest-terrain=".Length..]));
                return;
            }
            // the entity/building comparison on its own — the whole battery
            // behind --selftest-cwp takes minutes, and a change to one record
            // field only needs this one
            else if (a.StartsWith("--selftest-ent="))
            {
                GetTree().Quit(Import.ImportSelfTest.RunEntities(a["--selftest-ent=".Length..]));
                return;
            }
            // the same for the GAME.EXE tables, which now include the campaign's
            // diplomacy — that one is read out of CODE, so it earns a quick lane
            else if (a.StartsWith("--selftest-exe="))
            {
                GetTree().Quit(Import.ImportSelfTest.RunExe(a["--selftest-exe=".Length..]));
                return;
            }
            else if (a == "--selftest-designs")
            {
                GetTree().Quit(Import.ImportSelfTest.RunDesigns());
                return;
            }
            else if (a == "--selftest-weapons")
            {
                GetTree().Quit(Import.ImportSelfTest.RunWeapons());
                return;
            }
            else if (a == "--selftest-briefings")
            {
                GetTree().Quit(Import.ImportSelfTest.RunBriefings());
                return;
            }
            else if (a == "--sound-probe")
            {
                foreach (var c in GetChildren()) (c as Node)?.QueueFree();
                AddChild(new SoundProbe());
                return;
            }
            else if (a.StartsWith("--selftest-sounds="))
            {
                // --selftest-sounds=<refdir>[,<Installation oder SOUNDS.CWN>]
                string[] p = a["--selftest-sounds=".Length..].Split(',', 2);
                GetTree().Quit(Import.ImportSelfTest.RunSounds(p[0], p.Length > 1 ? p[1] : null));
                return;
            }

        if (!Core.Content.Ready) { ShowImportScreen(); return; }
        if (AutoStart()) return;      // ein Lauf, der gleich losspielt, braucht keine Kulisse
        StartBackdrop();
    }

    // ---- first start: where the content comes from --------------------------

    /// <summary>Shown when no content has been imported yet. The build we hand
    /// out contains none of the 1997 game — it is derived here, on the player's
    /// machine, from the player's own copy.</summary>
    private void ShowImportScreen()
    {
        foreach (var c in GetChildren()) (c as Node)?.QueueFree();

        var bg = new ColorRect { Color = new Color(0.05f, 0.06f, 0.08f) };
        // ⚠ 17.08.2026 — siehe LoadGameScreen._Ready (Fehler C20): nur Anker,
        // keine Raender, also ein Rechteck der Groesse null.
        bg.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(bg);

        var box = new VBoxContainer
        {
            AnchorLeft = 0.5f, AnchorRight = 0.5f, AnchorTop = 0.5f, AnchorBottom = 0.5f,
            GrowHorizontal = GrowDirection.Both, GrowVertical = GrowDirection.Both,
            CustomMinimumSize = new Vector2(680, 0),
        };
        box.AddThemeConstantOverride("separation", 10);
        AddChild(box);

        var t = new Label { Text = "Spieldaten einrichten", HorizontalAlignment = HorizontalAlignment.Center };
        t.AddThemeFontSizeOverride("font_size", 30);
        box.AddChild(t);
        box.AddChild(new Label
        {
            Text = "Akte Europa Reborn liefert nur die Engine aus. Gelaende, Einheiten und\n" +
                   "Karten werden hier aus deiner eigenen Spielkopie erzeugt und liegen\n" +
                   "danach unter:\n" + Core.Content.UserDir,
            HorizontalAlignment = HorizontalAlignment.Center,
            Modulate = new Color(0.75f, 0.8f, 0.85f),
        });
        box.AddChild(new HSeparator());

        // ---- the two ways OpenRA offers, in the same order --------------------
        var status = new Label { HorizontalAlignment = HorizontalAlignment.Center };

        var discs = Core.ContentSources.Discs();
        var fromCd = new Button
        {
            Text = discs != null ? $"Von CD installieren — {discs.Label}" : "Von CD installieren",
            CustomMinimumSize = new Vector2(0, 40),
            Disabled = discs == null,
            TooltipText = discs?.Describe() ?? "Keine Akte-Europa-CD im Laufwerk.",
        };
        box.AddChild(fromCd);
        if (discs == null)
            box.AddChild(Hint("Keine Spiel-CD gefunden. Leg CD 1 ein — beide Discs zusammen\n" +
                              "bringen die vollstaendige Kampagne."));
        else if (discs.Cabinet == null)
            box.AddChild(Hint("Nur CD 2 gefunden. GAME.EXE liegt auf CD 1 — ohne sie fehlen\n" +
                              "die Tabellen und die Einheitengrafiken."));

        var dl = new Button
        {
            Text = "Freeware-Dateien herunterladen",
            CustomMinimumSize = new Vector2(0, 40),
            Disabled = !Core.ContentSources.Download.Configured,
            TooltipText = Core.ContentSources.Download.Explain(),
        };
        box.AddChild(dl);
        if (!Core.ContentSources.Download.Configured)
            box.AddChild(Hint("Kein Download hinterlegt: fuer dieses Spiel ist uns keine legitime\n" +
                              "Bezugsquelle bekannt. Traegst du in ContentSources.Download eine\n" +
                              "Adresse samt SHA-256 ein, schaltet sich der Knopf frei."));

        box.AddChild(new HSeparator());
        box.AddChild(Hint("Oder einen Ordner waehlen — eine Installation oder ein fertiger Datenordner:"));

        var pathEdit = new LineEdit { PlaceholderText = @"z. B. C:\Spiele\AkteEuropa" };
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        pathEdit.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        row.AddChild(pathEdit);
        var browse = new Button { Text = "Durchsuchen …" };
        row.AddChild(browse);
        box.AddChild(row);

        var go = new Button { Text = "Einrichten", CustomMinimumSize = new Vector2(0, 40) };
        box.AddChild(go);
        box.AddChild(status);

        fromCd.Pressed += () =>
        {
            if (discs == null) return;
            status.Text = discs.Describe() + " — wird eingelesen …";
            fromCd.Disabled = go.Disabled = true;
            CallDeferred(nameof(RunImport), discs.Roots.ToArray(), discs.Cabinet ?? "",
                         status, go);
        };

        var dlg = new FileDialog
        {
            FileMode = FileDialog.FileModeEnum.OpenDir,
            Access = FileDialog.AccessEnum.Filesystem,
            Size = new Vector2I(820, 560),
            Title = "Ordner waehlen",
        };
        AddChild(dlg);
        browse.Pressed += () => dlg.PopupCentered();
        dlg.DirSelected += d => { pathEdit.Text = d; status.Text = Describe(d); };
        pathEdit.TextChanged += s => status.Text = Describe(s);

        go.Pressed += () =>
        {
            string dir = pathEdit.Text.Trim();
            switch (Core.ContentImport.Classify(dir))
            {
                case Core.ContentImport.SourceKind.Derived:
                    int n = Core.ContentImport.ImportDerived(dir, out string msg);
                    status.Text = msg;
                    if (n > 0) GetTree().ChangeSceneToFile(Core.Content.MenuSceneSafe);
                    break;
                case Core.ContentImport.SourceKind.Original:
                    status.Text = "Originalspiel erkannt — wird eingelesen …";
                    go.Disabled = true;
                    CallDeferred(nameof(RunImport), new[] { dir },
                                 Core.ContentSources.CabinetIn(dir) ?? "", status, go);
                    break;
                default:
                    status.Text = "Hier ist weder ein Originalspiel noch ein fertiger Datenordner.";
                    break;
            }
        };
    }

    /// <summary>`--campaign` starts the next mission, `--campaign=7` a given
    /// one — the handle the harness needs.</summary>
    private void StartMissionByIndex(int index)
    {
        var m = Campaign.CampaignManager.ByIndex(index);
        if (m != null) StartMission(m);
    }

    /// <summary>Start a campaign mission. The human is player 0 and the other
    /// sides are whatever the map places — a campaign level brings its own
    /// opposition, so no AI count is set up here.</summary>
    private void StartMission(Campaign.CampaignManager.Mission m)
    {
        SkirmishSetup.Map = m.Map;
        SkirmishSetup.Human = 0;
        SkirmishSetup.AiCount = 0;
        SkirmishSetup.CampaignMission = m.Index;
        SkirmishSetup.Active = true;
        GD.Print($"Kampagne: starte {m.Label} ({m.Map})");
        // die Kulisse geht VOR dem Briefing weg: sonst rechnet sie hinter dem
        // Text weiter und hält ihre 30-MB-Textur fest, bis der Spieler klickt
        StopBackdrop();

        // The briefing goes here rather than in the map scene, because this is
        // the one place all three ways in meet: the menu button, the mission
        // picker and --campaign. No text (a skirmish, or content imported before
        // briefings.json existed) simply means straight on, as before.
        var br = _skipBriefing ? null : BriefingScreen.For(m.Index);
        if (br == null) { GetTree().ChangeSceneToFile(SkirmishSetup.GameScene); return; }
        GD.Print($"Briefing: \"{br.Value.Title}\", {br.Value.Paragraphs.Count} Absaetze");
        AddChild(new BriefingScreen(m.Label, br.Value.Paragraphs,
            () => GetTree().ChangeSceneToFile(SkirmishSetup.GameScene), m.Index));
    }

    /// <summary>Set by `--no-briefing`, so a scripted run reaches the map without
    /// a keypress.</summary>
    private bool _skipBriefing;

    private static Label Hint(string text) => new()
    {
        Text = text,
        HorizontalAlignment = HorizontalAlignment.Center,
        Modulate = new Color(0.6f, 0.65f, 0.72f),
    };

    /// <summary>Read the player's own copy into user://data. One or more source
    /// folders — the two discs are handed over together — plus, where the
    /// executable is not lying about, the cabinet to take it out of. What the
    /// builder cannot produce yet is listed rather than glossed over.</summary>
    private void RunImport(string[] roots, string cabinet, Label status, Button go)
    {
        var src = new Core.ContentSources.Source
        {
            Kind = cabinet.Length > 0 && roots.Length > 1
                ? Core.ContentSources.Kind.Disc : Core.ContentSources.Kind.Installation,
            Label = string.Join(", ", roots),
            Cabinet = cabinet.Length > 0 ? cabinet : null,
        };
        foreach (string r in roots)
        {
            src.Roots.Add(r);
            src.Exe ??= Core.ContentSources.ExeIn(r);
        }

        var b = new Import.ContentBuilder(src);
        bool ok = b.Run(line => status.Text = line);
        var tail = new List<string>
        {
            ok ? $"{b.MapsBaked} Karten, {b.EntitiesWritten} Spielstaende und " +
                 $"{b.TablesWritten} Tabellen erzeugt."
               : "Es wurde nichts erzeugt.",
            "Noch nicht dabei: " + string.Join("; ", Import.ContentBuilder.Missing()),
        };
        status.Text = string.Join("\n", tail);
        go.Disabled = false;
        if (ok && Core.Content.Ready) GetTree().ChangeSceneToFile(Core.Content.MenuSceneSafe);
    }

    private static string Describe(string dir) => Core.ContentImport.Classify(dir) switch
    {
        Core.ContentImport.SourceKind.Derived => "Fertiger Datenordner erkannt.",
        Core.ContentImport.SourceKind.Original => "Originalspiel erkannt (GAME.EXE gefunden).",
        _ => "Noch nichts Brauchbares gefunden.",
    };

    /// <summary>`--skirmish=map_NET07,3,hard` skips the menu. Handy for testing
    /// and for a shortcut that drops straight into a game.
    ///
    /// <para>Gibt true zurück, wenn dieser Lauf das Menü verlässt — dann wird
    /// die Kulisse hinter dem Menü gar nicht erst geladen.</para></summary>
    private bool AutoStart()
    {
        foreach (string a in OS.GetCmdlineUserArgs())
            if (a == "--no-briefing") _skipBriefing = true;

        foreach (string a in OS.GetCmdlineUserArgs())
        {
            if (a.StartsWith("--briefing="))
            {
                int no = a["--briefing=".Length..].ToInt();
                var b = BriefingScreen.For(no);
                if (b == null) { GD.PrintErr($"briefing: kein Text fuer Mission {no}"); GetTree().Quit(2); return true; }
                GD.Print($"briefing {no}: \"{b.Value.Title}\", {b.Value.Paragraphs.Count} Absaetze");
                foreach (string p in b.Value.Paragraphs) GD.Print("  " + p);
                GetTree().Quit();
                return true;
            }
            if (a.StartsWith("--campaign"))
            {
                string arg = a.Contains('=') ? a[(a.IndexOf('=') + 1)..] : "";
                var m = int.TryParse(arg, out int no)
                    ? Campaign.CampaignManager.ByIndex(no) : Campaign.CampaignManager.Next();
                if (m == null) { GD.PrintErr("campaign: keine solche Mission"); GetTree().Quit(2); return true; }
                CallDeferred(nameof(StartMissionByIndex), m.Index);
                return true;
            }
            if (!a.StartsWith("--skirmish")) continue;
            string rest = a.Contains('=') ? a[(a.IndexOf('=') + 1)..] : "";
            var parts = rest.Split(',', System.StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > 0)
            {
                // A name that is not in the picker used to leave the selection on
                // entry 0 and start THAT map instead — silently. Four NET maps
                // are absent from the list, so every scripted check on them was
                // really a check on map_NET07, with its numbers. Now an imported
                // map that is missing from the list is added to it, and a name
                // that exists nowhere stops the run instead of substituting one.
                string want = parts[0].Trim();
                int mi = Maps.FindIndex(m => m.File == want);
                if (mi < 0 && FileAccess.FileExists(Core.Content.Path($"Maps/{want}.entities.json")))
                {
                    Maps.Add((want, want));
                    mi = Maps.Count - 1;
                    GD.Print($"skirmish: {want} steht nicht in der Auswahl, aber die Daten sind da — aufgenommen");
                }
                if (mi < 0)
                {
                    GD.PrintErr($"skirmish: die Karte {want} gibt es nicht");
                    GetTree().Quit(2);
                    return true;
                }
                // schaltet noetigenfalls den Modus um — eine per Befehlszeile
                // genannte Karte darf nicht daran scheitern, dass gerade eine
                // andere Spielform angezeigt wird
                SelectMap(mi);
            }
            if (parts.Length > 1 && int.TryParse(parts[1], out int n)) _ai.Value = n;
            if (parts.Length > 2)
                _level.Selected = parts[2].Trim().ToLower() switch
                {
                    "easy" or "leicht" => 0, "hard" or "schwer" => 2, _ => 1,
                };
            // fourth field: the start slot, 1..8, for the harness
            if (parts.Length > 3 && int.TryParse(parts[3], out int sl))
                _slot.Selected = Mathf.Clamp(sl, 0, 8);
            // fifth: "Rohstoffe", by the game's own word or by its number
            if (parts.Length > 4)
            {
                string w = parts[4].Trim().ToLower();
                var names = MapEntityLayer.ResourceLevelNames();
                int r = names.FindIndex(x => x.ToLower() == w);
                if (r < 0 && int.TryParse(w, out int rn)) r = rn;
                if (r >= 0)
                {
                    SkirmishSetup.Resources = r;
                    if (_res != null) _res.Selected = Mathf.Clamp(r, 0, _res.ItemCount - 1);
                }
                else GD.PrintErr($"skirmish: \"{parts[4]}\" ist keine Rohstoffstufe " +
                                 $"({string.Join("/", names)})");
            }
            // ⚠ NICHT mehr OnStart, sondern StartWhenNetIsReady (UI/MainMenuNet.cs).
            // Ohne Netzschalter ist das dasselbe, nur einen Bildlauf später; mit
            // Netzschalter wartet es auf die Partie, denn der Keim des
            // Vermittlers muss vor dem Kartenladen stehen.
            CallDeferred(nameof(StartWhenNetIsReady));
            return true;
        }
        return false;
    }

    /// <summary>Puts the chosen map's thumbnail on screen. The first call for
    /// a map shrinks the baked PNG and caches it, so the menu does not carry a
    /// 10160-pixel-wide picture around.</summary>
    private void ShowPreview()
    {
        string file = Maps[Mathf.Clamp(_mapIndex, 0, Maps.Count - 1)].File;
        _preview.Texture = MapPreview.Of(file);
        _previewText.Text = MapPreview.Caption(file);
    }

    // ---- Modus und Kartenliste ----------------------------------------------

    /// <summary>Den Spielmodus umschalten: die Knoepfe nachziehen, die
    /// Erklaerung darunter setzen und die Kartenliste neu fuellen. Steht in der
    /// Liste die bisher gewaehlte Karte nicht mehr, wird die erste dieses Modus
    /// genommen — es soll nie eine Karte gestartet werden, die man nicht mehr
    /// sieht.</summary>
    private void SelectMode(MapPreview.Shape mode)
    {
        _mode = mode;
        foreach (var (s, b) in _modes) b.ButtonPressed = s == mode;
        _modeHint.Text = MapPreview.ShapeHint(mode);
        FillMapList();
    }

    /// <summary>Die Spielform, zu der die meisten eingelesenen Karten gehoeren.
    /// Bei Gleichstand gewinnt die erste in der Knopfreihe.</summary>
    private MapPreview.Shape BusiestMode()
    {
        var best = _modes.Count > 0 ? _modes[0].S : MapPreview.Shape.AsDrawn;
        int bestN = -1;
        foreach (var (s, _) in _modes)
        {
            int n = 0;
            foreach (var (file, _) in Maps) if (MapPreview.ShapeOf(file) == s) n++;
            if (n > bestN) { bestN = n; best = s; }
        }
        return best;
    }

    private void FillMapList()
    {
        _mapList.Clear();
        _listed.Clear();
        for (int i = 0; i < Maps.Count; i++)
        {
            if (MapPreview.ShapeOf(Maps[i].File) != _mode) continue;
            _mapList.AddItem(Maps[i].Title);
            _mapList.SetItemTooltip(_mapList.ItemCount - 1, MapPreview.Caption(Maps[i].File));
            _listed.Add(i);
        }
        if (_listed.Count == 0) return;
        int at = _listed.IndexOf(_mapIndex);
        if (at < 0) { at = 0; _mapIndex = _listed[0]; }
        _mapList.Select(at);
        ShowPreview();
    }

    private void PickFromList(int row)
    {
        if (row < 0 || row >= _listed.Count) return;
        _mapIndex = _listed[row];
        ShowPreview();
    }

    /// <summary>Eine Karte ueber ihren Platz in <see cref="Maps"/> waehlen, auch
    /// wenn gerade ein anderer Modus zu sehen ist — der Modus springt dann mit.
    /// Das braucht der Harnisch: <c>--skirmish=map_NET07</c> nennt eine Karte
    /// und keinen Modus.</summary>
    private void SelectMap(int index)
    {
        _mapIndex = Mathf.Clamp(index, 0, Maps.Count - 1);
        SelectMode(MapPreview.ShapeOf(Maps[_mapIndex].File));
    }

    /// <summary>Ein umrandeter Abschnitt mit Ueberschrift. ⚠ Unsere Zutat wie
    /// der ganze Gefechtsschirm; sie ist der Grund, warum die Seite jetzt aus
    /// drei Bloecken besteht statt aus einer Reihe gleich aussehender
    /// Auswahlfelder.</summary>
    /// <summary>Sagt aus, was der Haken »Alle Einheiten« gerade bedeutet.
    ///
    /// <para>⚠ Der Sinn dieser Zeile ist ein Spielerbefund vom 13.08.2026: der
    /// Haken war aus, am eingenommenen Flughafen stand keine Flugeinheit zur
    /// Auswahl, und das war von aussen nicht von einem Fehler zu unterscheiden.
    /// Ein Schalter, dessen Folge man erst im Spiel merkt, gehoert beschriftet.
    /// Die Zahlen sind die gemessenen (Boden 601 gegen 65 Entwuerfe, Luft 8
    /// gegen 0, See 10 gegen 2) — siehe SkirmishSetup.AllUnits.</para></summary>
    private void UpdateAllUnitsHint()
    {
        if (_allUnitsHint == null) return;
        bool on = _allUnits?.ButtonPressed ?? false;
        // Einzeilig halten: die zweizeilige Fassung hat den Schirm zu hoch
        // gemacht. Die Zahlen sind gemessen, keine Schaetzung.
        // ⚠ 13.08.2026, zweite Fassung: der erste Text sagte »Flughaefen bleiben
        // ohne Angebot« und »zur See 2 von 10«. Beides gilt nicht mehr, seit die
        // Luft am Techstandard haengt (Tor @0x419F30) und die Schiffsliste am
        // selben Wert — die Zahlen richten sich jetzt nach der Stufe. Ein
        // Hinweistext, der eine ueberholte Zahl nennt, ist schlimmer als keiner.
        _allUnitsHint.Text = on
            ? "AN — die ganze Entwurfsliste am Boden (601) und alle 10 Schiffe. "
              + "Die Luft richtet sich nach dem Techstandard. Die Gegner ebenso."
            : "AUS — am Boden 65 von 601 Entwuerfen. Luft und See richten sich "
              + "nach dem Techstandard darueber.";
        var akzent = on
            ? new Color(0.45f, 0.60f, 0.78f)
            : new Color(0.92f, 0.72f, 0.42f);
        _allUnitsHint.Modulate = akzent;

        // ⚠ Der Rand traegt den Zustand mit, siehe _allUnitsPanel: das
        // Haekchen-Symbol allein ist zu klein, um die Frage zu beantworten.
        if (_allUnitsPanel?.GetThemeStylebox("panel") is StyleBoxFlat sb)
        {
            sb.BorderColor = akzent;
            _allUnitsPanel.AddThemeStyleboxOverride("panel", sb);
        }
    }

    /// <summary>Wie <see cref="Group"/>, aber mit Akzentrand — fuer die eine
    /// Einstellung, die man nicht uebersehen darf. Bewusst dieselbe Bauform, damit
    /// der Schirm nicht auseinanderfaellt; es unterscheidet sie nur der Rand.
    ///
    /// <para>Ein LEERER Titel laesst die Titelzeile weg. Das ist kein Sonderfall
    /// aus Bequemlichkeit: wo der Inhalt selbst schon eine Beschriftung traegt,
    /// doppelt ein Titel sie nur und kostet eine Zeile Hoehe, die der Schirm
    /// nicht hat.</para></summary>
    private static PanelContainer Accent(string title, Control content)
    {
        var p = new PanelContainer();
        var style = new StyleBoxFlat
        {
            BgColor = new Color(0.11f, 0.13f, 0.17f),
            BorderColor = new Color(0.45f, 0.60f, 0.78f),
            ContentMarginLeft = 12, ContentMarginRight = 12,
            ContentMarginTop = 8, ContentMarginBottom = 10,
        };
        style.SetBorderWidthAll(2);
        p.AddThemeStyleboxOverride("panel", style);

        var v = new VBoxContainer();
        v.AddThemeConstantOverride("separation", 4);
        p.AddChild(v);

        if (title.Length > 0)
        {
            var h = new Label { Text = title, Modulate = new Color(0.72f, 0.84f, 1.00f) };
            h.AddThemeFontSizeOverride("font_size", 18);
            v.AddChild(h);
        }
        v.AddChild(content);
        return p;
    }

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

    private static HBoxContainer Row(string label, Control field)
    {
        var h = new HBoxContainer();
        h.AddThemeConstantOverride("separation", 12);
        var l = new Label { Text = label, CustomMinimumSize = new Vector2(150, 0) };
        h.AddChild(l);
        field.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        h.AddChild(field);
        return h;
    }

    /// <summary>Die Auswahlfelder in <see cref="SkirmishSetup"/> tragen — und
    /// nichts weiter. ⚠ Herausgezogen aus <see cref="OnStart"/>, weil der
    /// Vermittler eines Netzspiels diese Werte SCHON BRAUCHT, um die Partie
    /// anbieten zu können: was er verteilt, ist genau das, was hier steht
    /// (UI/MainMenuNet.cs).</summary>
    private void ApplySetupFields()
    {
        SkirmishSetup.Map = Maps[Mathf.Clamp(_mapIndex, 0, Maps.Count - 1)].File;
        SkirmishSetup.Human = _slot.Selected - 1;   // -1 = automatisch
        SkirmishSetup.AiCount = (int)_ai.Value;
        SkirmishSetup.Level = _level.Selected switch
        {
            0 => MapEntityLayer.AiLevel.Easy,
            2 => MapEntityLayer.AiLevel.Hard,
            _ => MapEntityLayer.AiLevel.Normal,
        };
        if (_res != null) SkirmishSetup.Resources = _res.Selected;
        // ⚠ Beides, und in dieser Reihenfolge: der Lauf bekommt den Wert, und
        // die Einstellung merkt ihn sich fuer den naechsten Programmstart. Ohne
        // die zweite Zeile war der Haken ein fluechtiges static bool — siehe
        // Settings.SkirmishAllUnits.
        if (_allUnits != null)
        {
            SkirmishSetup.AllUnits = _allUnits.ButtonPressed;
            Settings.SkirmishAllUnits = _allUnits.ButtonPressed;
        }
        if (_tech != null)
        {
            SkirmishSetup.Techstandard = Mathf.Clamp((int)_tech.Value, 1, 8);
            Settings.SkirmishTechstandard = SkirmishSetup.Techstandard;
        }
        SkirmishSetup.CampaignMission = 0;      // a skirmish records nothing
    }

    private void OnStart()
    {
        ApplySetupFields();

        // ⚠ IM NETZSPIEL GEWINNT DIE PARTIE, NICHT DAS AUSWAHLFELD. Karte, Platz,
        // Techstandard, Rohstoffe und »alle Einheiten« kommen dann vom
        // Vermittler — sonst spielte jeder die Karte und den Platz, die BEI IHM
        // im Menü standen, und das ist kein Auseinanderlaufen, sondern zwei
        // verschiedene Partien. Ohne Netzschalter tut die Zeile nichts.
        Network.NetworkManager.OverrideSetup();
        SkirmishSetup.Active = true;
        StopBackdrop();
        GetTree().ChangeSceneToFile(SkirmishSetup.GameScene);
    }
}
