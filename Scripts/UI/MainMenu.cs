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

    private OptionButton _map = null!;
    private OptionButton _slot = null!;
    private TextureRect _preview = null!;
    private Label _previewText = null!;
    private OptionButton _level = null!;
    private OptionButton? _res;
    private SpinBox _ai = null!;
    private Label _hint = null!;

    /// <summary>The skirmish setup, which used to BE the menu and is now what
    /// the start menu's added "Gefecht" row leads to.</summary>
    private Control? _setup;
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
                newGame: missions.Count > 0 ? StartCampaign : null,
                load: () => AddChild(new LoadGameScreen()),
                net: null,
                settings: () => AddChild(new SettingsScreen()),
                encyclopedia: null,
                intro: null,
                credits: null,
                demo: demos ? NextDemo : null,
                quit: () => GetTree().Quit()));

        // OURS, and the only row that is: the original had no skirmish against
        // a computer opponent, only "Netzwerkspiel" against people. It sits
        // right under that entry — the first version put it at the very end,
        // below "Beenden", and the skirmish was reported as missing from 0.3.0
        // because nobody looks there.
        var rows = StartMenuPanel.InsertAfter(original, "Netzwerkspiel",
            new StartMenuPanel.Row(0, "Gefecht", -1,
                "Gefecht gegen den Rechner — im Original gibt es das nicht", ShowSetup));

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

    private void StartCampaign()
    {
        var m = Campaign.CampaignManager.Next();
        if (m == null) { Campaign.CampaignManager.Reset(); m = Campaign.CampaignManager.Next(); }
        if (m != null) StartMission(m);
    }

    private void ShowSetup()
    {
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

        var box = new VBoxContainer { CustomMinimumSize = new Vector2(460, 0) };
        box.AddThemeConstantOverride("separation", 10);
        middle.AddChild(box);

        var title = new Label
        {
            Text = "AKTE EUROPA — REBORN",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        title.AddThemeFontSizeOverride("font_size", 34);
        box.AddChild(title);
        var sub = new Label
        {
            Text = "Rekonstruktion des RTS von 1997",
            HorizontalAlignment = HorizontalAlignment.Center,
            Modulate = new Color(0.7f, 0.75f, 0.8f),
        };
        box.AddChild(sub);
        box.AddChild(new HSeparator());

        box.AddChild(Row("Karte", _map = new OptionButton()));
        foreach (var (_, t) in Maps) _map.AddItem(t);
        _map.Selected = 0;
        _map.ItemSelected += _ => ShowPreview();

        // the picture of the chosen map, with its original name underneath
        _preview = new TextureRect
        {
            CustomMinimumSize = new Vector2(0, 150),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            TextureFilter = TextureFilterEnum.Nearest,
        };
        box.AddChild(_preview);
        _previewText = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            Modulate = new Color(0.7f, 0.75f, 0.8f),
        };
        box.AddChild(_previewText);

        box.AddChild(Row("Gegner", _ai = new SpinBox { MinValue = 1, MaxValue = 7, Value = 3 }));

        // Which of the map's eight slots the human takes. The slots are the
        // original's own — a NET map fills some and leaves others empty — so
        // "Automatisch" means the first one this map actually uses, and a
        // chosen slot that the map leaves empty falls back to the same.
        box.AddChild(Row("Startplatz", _slot = new OptionButton()));
        _slot.AddItem("Automatisch");
        for (int p = 0; p < 8; p++) _slot.AddItem($"Spieler {p + 1}");
        _slot.Selected = 0;

        box.AddChild(Row("Schwierigkeit", _level = new OptionButton()));
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
            box.AddChild(Row("Rohstoffe", _res = new OptionButton()));
            foreach (string n in resNames) _res.AddItem(n);
            _res.Selected = Mathf.Clamp(SkirmishSetup.Resources, 0, resNames.Count - 1);
        }

        box.AddChild(new HSeparator());

        var start = new Button { Text = "GEMETZEL STARTEN", CustomMinimumSize = new Vector2(0, 44) };
        start.Pressed += OnStart;
        box.AddChild(start);

        // ---- the campaign ----------------------------------------------------
        var missions = Campaign.CampaignManager.Missions;
        var next = Campaign.CampaignManager.Next();
        var camp = new Button
        {
            Text = missions.Count == 0 ? "Kampagne — keine Missionen importiert"
                 : next == null ? "Kampagne — durchgespielt (neu beginnen)"
                 : $"KAMPAGNE — {next.Label}",
            CustomMinimumSize = new Vector2(0, 44),
            Disabled = missions.Count == 0,
        };
        camp.Pressed += () =>
        {
            var m = Campaign.CampaignManager.Next();
            if (m == null) { Campaign.CampaignManager.Reset(); m = Campaign.CampaignManager.Next(); }
            if (m == null) return;
            StartMission(m);
        };
        box.AddChild(camp);
        if (missions.Count > 0)
        {
            var pick = new OptionButton();
            foreach (var m in missions) pick.AddItem(m.Label);
            pick.Selected = System.Math.Max(0, next != null
                ? System.Array.IndexOf(Indices(missions), next.Index) : 0);
            pick.ItemSelected += i => StartMission(missions[(int)i]);
            box.AddChild(Row("Mission waehlen", pick));
            box.AddChild(Hint($"{missions.Count} Missionen · " +
                              $"{Campaign.CampaignManager.Completed} geschafft"));
        }
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
        ShowPreview();
        ReadShotArgs();
        // --setup opens the skirmish panel straight away, so it can be
        // photographed without a click
        foreach (string a in OS.GetCmdlineUserArgs())
            if (a == "--setup") { ShowSetup(); break; }

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
        bg.SetAnchorsPreset(LayoutPreset.FullRect);
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

    private static int[] Indices(System.Collections.Generic.IReadOnlyList<Campaign.CampaignManager.Mission> ms)
    {
        var a = new int[ms.Count];
        for (int i = 0; i < ms.Count; i++) a[i] = ms[i].Index;
        return a;
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
                    _map.AddItem(want);
                    mi = Maps.Count - 1;
                    GD.Print($"skirmish: {want} steht nicht in der Auswahl, aber die Daten sind da — aufgenommen");
                }
                if (mi < 0)
                {
                    GD.PrintErr($"skirmish: die Karte {want} gibt es nicht");
                    GetTree().Quit(2);
                    return true;
                }
                _map.Selected = mi;
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
            CallDeferred(nameof(OnStart));
            return true;
        }
        return false;
    }

    /// <summary>Puts the chosen map's thumbnail on screen. The first call for
    /// a map shrinks the baked PNG and caches it, so the menu does not carry a
    /// 10160-pixel-wide picture around.</summary>
    private void ShowPreview()
    {
        string file = Maps[Mathf.Clamp(_map.Selected, 0, Maps.Count - 1)].File;
        _preview.Texture = MapPreview.Of(file);
        _previewText.Text = MapPreview.Caption(file);
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

    private void OnStart()
    {
        SkirmishSetup.Map = Maps[Mathf.Clamp(_map.Selected, 0, Maps.Count - 1)].File;
        SkirmishSetup.Human = _slot.Selected - 1;   // -1 = automatisch
        SkirmishSetup.AiCount = (int)_ai.Value;
        SkirmishSetup.Level = _level.Selected switch
        {
            0 => MapEntityLayer.AiLevel.Easy,
            2 => MapEntityLayer.AiLevel.Hard,
            _ => MapEntityLayer.AiLevel.Normal,
        };
        if (_res != null) SkirmishSetup.Resources = _res.Selected;
        SkirmishSetup.CampaignMission = 0;      // a skirmish records nothing
        SkirmishSetup.Active = true;
        StopBackdrop();
        GetTree().ChangeSceneToFile(SkirmishSetup.GameScene);
    }
}
