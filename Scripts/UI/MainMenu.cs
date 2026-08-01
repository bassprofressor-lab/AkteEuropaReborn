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
    private static readonly (string File, string Title)[] Maps =
    {
        ("map_NET07", "Gefechtsfeld (NET07)"),
        ("map_NET02", "Zwei Ufer (NET02)"),
        ("map_NET04", "Drei Haefen (NET04)"),
        ("map_NET05", "Weites Land (NET05)"),
        ("map_NET06", "Doppelhafen (NET06)"),
        ("map_DM_4", "The Dam — Spielstand zu Level 21"),
        ("map_DM_3", "Chanel Tunnel — Spielstand zu Level 25"),
        ("map_DM_1", "Scandinavia — Spielstand zu Level 26"),
        ("map_05", "Kampagne 05"),
        ("map_10", "Kampagne 10"),
        ("map_14", "Kampagne 14"),
    };

    private OptionButton _map = null!;
    private OptionButton _level = null!;
    private SpinBox _ai = null!;
    private Label _hint = null!;

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(new ColorRect
        {
            Color = new Color(0.05f, 0.06f, 0.08f),
            AnchorRight = 1, AnchorBottom = 1,
        });

        var box = new VBoxContainer
        {
            AnchorLeft = 0.5f, AnchorRight = 0.5f, AnchorTop = 0.5f, AnchorBottom = 0.5f,
            GrowHorizontal = GrowDirection.Both, GrowVertical = GrowDirection.Both,
            CustomMinimumSize = new Vector2(460, 0),
        };
        box.AddThemeConstantOverride("separation", 10);
        AddChild(box);

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

        box.AddChild(Row("Gegner", _ai = new SpinBox { MinValue = 1, MaxValue = 7, Value = 3 }));

        box.AddChild(Row("Schwierigkeit", _level = new OptionButton()));
        _level.AddItem("Leicht");
        _level.AddItem("Normal");
        _level.AddItem("Schwer");
        _level.Selected = 1;

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
        var net = new Button { Text = "Netzwerk — noch nicht spielbar", Disabled = true };
        box.AddChild(net);

        var quit = new Button { Text = "Beenden" };
        quit.Pressed += () => GetTree().Quit();
        box.AddChild(quit);

        _hint = new Label
        {
            Text = "Links waehlen/ziehen = Auswahl · Rechts = Befehl · B bauen · N Auswahl · Esc zurueck",
            HorizontalAlignment = HorizontalAlignment.Center,
            Modulate = new Color(0.55f, 0.6f, 0.66f),
        };
        box.AddChild(_hint);

        // the porting harness: decode with the new C# reader and compare
        // against the Python reference before anything else happens
        foreach (string a in OS.GetCmdlineUserArgs())
            if (a.StartsWith("--import="))
            {
                var b = new Import.ContentBuilder(a["--import=".Length..]);
                bool ok = b.Run();
                GetTree().Quit(ok ? 0 : 1);
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
            else if (a.StartsWith("--selftest-cwp="))
            {
                string dir = a["--selftest-cwp=".Length..];
                int rc = Import.ImportSelfTest.RunCwp(dir);
                rc |= Import.ImportSelfTest.RunCwpSweep(dir);
                rc |= Import.ImportSelfTest.RunCwm(dir);
                rc |= Import.ImportSelfTest.RunEntities(dir);
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
            else if (a == "--selftest-designs")
            {
                GetTree().Quit(Import.ImportSelfTest.RunDesigns());
                return;
            }
            else if (a == "--selftest-briefings")
            {
                GetTree().Quit(Import.ImportSelfTest.RunBriefings());
                return;
            }

        if (!Core.Content.Ready) { ShowImportScreen(); return; }
        AutoStart();
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

        // The briefing goes here rather than in the map scene, because this is
        // the one place all three ways in meet: the menu button, the mission
        // picker and --campaign. No text (a skirmish, or content imported before
        // briefings.json existed) simply means straight on, as before.
        var br = _skipBriefing ? null : BriefingScreen.For(m.Index);
        if (br == null) { GetTree().ChangeSceneToFile(SkirmishSetup.GameScene); return; }
        GD.Print($"Briefing: \"{br.Value.Title}\", {br.Value.Paragraphs.Count} Absaetze");
        AddChild(new BriefingScreen(m.Label, br.Value.Paragraphs,
            () => GetTree().ChangeSceneToFile(SkirmishSetup.GameScene)));
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
    /// and for a shortcut that drops straight into a game.</summary>
    private void AutoStart()
    {
        foreach (string a in OS.GetCmdlineUserArgs())
            if (a == "--no-briefing") _skipBriefing = true;

        foreach (string a in OS.GetCmdlineUserArgs())
        {
            if (a.StartsWith("--briefing="))
            {
                int no = a["--briefing=".Length..].ToInt();
                var b = BriefingScreen.For(no);
                if (b == null) { GD.PrintErr($"briefing: kein Text fuer Mission {no}"); GetTree().Quit(2); return; }
                GD.Print($"briefing {no}: \"{b.Value.Title}\", {b.Value.Paragraphs.Count} Absaetze");
                foreach (string p in b.Value.Paragraphs) GD.Print("  " + p);
                GetTree().Quit();
                return;
            }
            if (a.StartsWith("--campaign"))
            {
                string arg = a.Contains('=') ? a[(a.IndexOf('=') + 1)..] : "";
                var m = int.TryParse(arg, out int no)
                    ? Campaign.CampaignManager.ByIndex(no) : Campaign.CampaignManager.Next();
                if (m == null) { GD.PrintErr("campaign: keine solche Mission"); GetTree().Quit(2); return; }
                CallDeferred(nameof(StartMissionByIndex), m.Index);
                return;
            }
            if (!a.StartsWith("--skirmish")) continue;
            string rest = a.Contains('=') ? a[(a.IndexOf('=') + 1)..] : "";
            var parts = rest.Split(',', System.StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > 0)
            {
                int mi = System.Array.FindIndex(Maps, m => m.File == parts[0].Trim());
                if (mi >= 0) _map.Selected = mi;
            }
            if (parts.Length > 1 && int.TryParse(parts[1], out int n)) _ai.Value = n;
            if (parts.Length > 2)
                _level.Selected = parts[2].Trim().ToLower() switch
                {
                    "easy" or "leicht" => 0, "hard" or "schwer" => 2, _ => 1,
                };
            CallDeferred(nameof(OnStart));
            return;
        }
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
        SkirmishSetup.Map = Maps[Mathf.Clamp(_map.Selected, 0, Maps.Length - 1)].File;
        SkirmishSetup.AiCount = (int)_ai.Value;
        SkirmishSetup.Level = _level.Selected switch
        {
            0 => MapEntityLayer.AiLevel.Easy,
            2 => MapEntityLayer.AiLevel.Hard,
            _ => MapEntityLayer.AiLevel.Normal,
        };
        SkirmishSetup.CampaignMission = 0;      // a skirmish records nothing
        SkirmishSetup.Active = true;
        GetTree().ChangeSceneToFile(SkirmishSetup.GameScene);
    }
}
