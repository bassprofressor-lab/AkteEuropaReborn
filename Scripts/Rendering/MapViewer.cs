namespace AkteEuropaReborn.Rendering;

using Godot;
using GDict = Godot.Collections.Dictionary<string, Godot.Variant>;

/// <summary>
/// Standalone viewer for the baked legacy campaign maps (see Assets/Legacy/Maps).
/// Loads a pre-rendered map PNG, shows it with a pannable / zoomable camera.
/// Not part of the simulation — a tool to verify the CWM reverse-engineering.
///
/// Controls (RTS style since Step E):
///   Left click / left drag    – select unit / rubber-band select (Shift = add)
///   Right click               – move order for the selected units
///   X                         – stop the selected units
///   Middle drag, WASD/arrows  – pan
///   Mouse wheel               – zoom toward cursor
///   [ ]                       – previous / next map
///   F                         – fill the window with the map (the furthest out
///                               the view goes; beyond it only black would show)
///   U / R / P / Z / T / G     – sprites / ranges / walkability / zones / buildings / dots
///   Ctrl+1..9 / 1..9          – store / recall a control group (twice = centre on it)
///   Space / Tab               – centre on the selection / on the last event
///   Esc                       – quit
/// </summary>
[GlobalClass]
public partial class MapViewer : Node2D
{
    /// <summary>The maps that are actually there, not a list written by hand.
    ///
    /// It has to be found rather than fixed since the importer reads the
    /// original CDs: disc 2 carries the campaign levels 16 to 33, which no
    /// single installation had and which an earlier hard-coded list of 26
    /// therefore could not name. Order: campaign by number, then the network
    /// maps, then the saved missions.
    ///
    /// The fallback is the set that shipped with the development tree, so the
    /// viewer still works if the folder cannot be listed.</summary>
    private static string[]? _mapNames;

    private static string[] MapNames => _mapNames ??= DiscoverMaps();

    private static readonly string[] FallbackMaps =
    {
        "map_01", "map_02", "map_03", "map_04", "map_05", "map_06", "map_07",
        "map_08", "map_09", "map_10", "map_11", "map_12", "map_13", "map_14",
        "map_15", "map_NET01", "map_NET02", "map_NET03", "map_NET04",
        "map_NET05", "map_NET06", "map_NET07", "map_NET08",
        "map_DM_1", "map_DM_3", "map_DM_4",
    };

    private static string[] DiscoverMaps()
    {
        var found = new System.Collections.Generic.List<string>();
        foreach (string root in new[] { Core.Content.UserRoot + "Maps/", Core.Content.DevRoot + "Maps/" })
        {
            using var d = DirAccess.Open(root);
            if (d == null) continue;
            foreach (string f in d.GetFiles())
                if (f.StartsWith("map_") && f.EndsWith(".json") && !f.EndsWith(".entities.json"))
                {
                    string n = f[..^5];
                    if (!found.Contains(n)) found.Add(n);
                }
            if (found.Count > 0) break;          // the imported content wins
        }
        if (found.Count == 0) return FallbackMaps;

        found.Sort((a, b) => Rank(a).CompareTo(Rank(b)));
        return found.ToArray();
    }

    /// <summary>Campaign levels first, in number order; then NET; then the
    /// saved missions.</summary>
    private static (int Group, int Num, string Name) Rank(string n)
    {
        string s = n["map_".Length..];
        if (int.TryParse(s, out int num)) return (0, num, n);
        if (s.StartsWith("NET")) return (1, int.TryParse(s[3..], out int k) ? k : 0, n);
        return (2, 0, n);
    }

    /// <summary>Which campaign level a saved game is a state of. Proven by the
    /// elevation grid — terrain codes change during play, elevations do not —
    /// eight of the thirteen saves matching exactly and the five Chanel Tunnel
    /// ones matching level 25 on 95% of 39,600 cells, with no counter-example.
    /// The list shows them next to those levels, so it says which is which
    /// instead of appearing to hold the same map twice.</summary>
    private static readonly System.Collections.Generic.Dictionary<string, int> SaveOfLevel =
        new() { ["map_DM_1"] = 26, ["map_DM_3"] = 25, ["map_DM_4"] = 21 };

    /// <summary>The name to show for a map.</summary>
    private static string MapLabel(string name)
        => SaveOfLevel.TryGetValue(name, out int lvl) ? $"{name} (Spielstand zu Level {lvl})" : name;

    /// <summary>Resolved per file, not per folder: the imported content and the
    /// development tree can each hold maps the other does not — since the
    /// importer reads the CDs, `user://data` has the levels 16 to 33 that the
    /// tree never had.</summary>
    private static string MapFile(string rel) => Core.Content.Path("Maps/" + rel);
    /// <summary>Absolute floor, only a guard against a degenerate map size. The
    /// zoom that actually stops the wheel is <see cref="FitZoom"/>: the point at
    /// which the map fills the window. Below it the black outside the map comes
    /// into view, which is what was reported.</summary>
    private const float MinZoomFloor = 0.02f;
    private const float MaxZoom = 8.0f;
    private const float ZoomStep = 1.15f;

    /// <summary>The zoom at which the map exactly covers the window — the
    /// smaller the number the further out, so this is the lower bound.</summary>
    private float FitZoom()
    {
        if (_sprite?.Texture == null) return MinZoomFloor;
        Vector2 tex = _sprite.Texture.GetSize();
        Vector2 view = GetViewportRect().Size;
        if (tex.X <= 0 || tex.Y <= 0) return MinZoomFloor;
        return Mathf.Max(MinZoomFloor, Mathf.Max(view.X / tex.X, view.Y / tex.Y));
    }

    /// <summary>Hold the camera inside the map, so neither the wheel nor a drag
    /// can put the black border on screen.</summary>
    /// <summary>ESC opens the pause screen and stops the game behind it;
    /// picking "Weiter" or ESC again lets it run on. OURS — the 1997 game has
    /// no pause screen, it leaves to the menu straight away. See UI/PauseMenu.
    /// </summary>
    private UI.PauseMenu? _pause;

    private void TogglePause()
    {
        if (_pause != null) { ClosePause(); return; }

        _pause = new UI.PauseMenu { CanSave = true };
        _pause.Resumed += ClosePause;
        _pause.SaveRequested += () =>
        {
            string name = Core.SaveGame.NewName();
            string label = $"{MapNames[_mapIndex]} — {System.DateTime.Now:dd.MM.yyyy HH:mm}";
            string json = _entities.SaveStateJson(MapNames[_mapIndex], label);
            GD.Print(Core.SaveGame.Write(name, json, out string err)
                ? $"gespeichert: {label}"
                : $"Speichern fehlgeschlagen: {err}");
            ClosePause();
        };
        _pause.Restarted += () =>
        {
            ClosePause();
            GetTree().ReloadCurrentScene();
        };
        _pause.Quit += () =>
        {
            ClosePause();
            UI.SkirmishSetup.Active = false;
            Audio.MidiMusic.Stop();
            GetTree().ChangeSceneToFile(UI.SkirmishSetup.MenuScene);
        };
        (_panelLayer ?? (CanvasLayer)GetTree().Root.GetChild(0)).AddChild(_pause);
        GetTree().Paused = true;
    }

    private void ClosePause()
    {
        GetTree().Paused = false;
        _pause?.QueueFree();
        _pause = null;
    }

    private void ClampCamera()
    {
        if (_sprite?.Texture == null) return;
        Vector2 tex = _sprite.Texture.GetSize();
        Vector2 half = GetViewportRect().Size / (2f * _camera.Zoom.X);
        // a map narrower than the window on one axis is centred on that axis
        float x = half.X * 2f >= tex.X ? tex.X / 2f
                : Mathf.Clamp(_camera.Position.X, half.X, tex.X - half.X);
        float y = half.Y * 2f >= tex.Y ? tex.Y / 2f
                : Mathf.Clamp(_camera.Position.Y, half.Y, tex.Y - half.Y);
        _camera.Position = new Vector2(x, y);
    }

    private Sprite2D _sprite = null!;
    private Camera2D _camera = null!;
    private Label _hud = null!;
    private ColorRect _hudBg = null!;
    private MapEntityLayer _entities = null!;

    private int _mapIndex;
    private bool _panDrag;        // actively panning the camera (middle mouse)
    private bool _leftDown;       // left button held (click select or selection box)
    private bool _boxSelect;      // the held left button became a rubber band
    private Vector2 _leftStart;   // where left went down (screen)
    private Vector2 _bandStart;   // where left went down (map)
    private Vector2 _dragLast;    // last pan reference (screen)
    private const float ClickSlop = 5f;    // px before a left-drag becomes a box
    private float _keyPanSpeed = 900f;     // px/s at zoom 1 for WASD / arrows (settings)

    public override void _Ready()
    {
        _sprite = new Sprite2D { Centered = false, TextureFilter = TextureFilterEnum.Nearest };
        AddChild(_sprite);

        _camera = new Camera2D { Enabled = true };
        AddChild(_camera);
        _camera.MakeCurrent();

        _entities = new MapEntityLayer();
        AddChild(_entities);

        var hudLayer = new CanvasLayer();
        AddChild(hudLayer);
        // the bitmap font has only a 1 px shadow, which disappears over bright
        // terrain — back the status text with a dark plate like the game does
        _hudBg = new ColorRect
        {
            Color = new Color(0.04f, 0.05f, 0.07f, 0.62f),
            Position = new Vector2(4, 2),
        };
        hudLayer.AddChild(_hudBg);
        _hud = new Label
        {
            Position = new Vector2(12, 8),
            Modulate = new Color(1, 1, 1),
            TextureFilter = TextureFilterEnum.Nearest,
        };
        _hud.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0));
        _hud.AddThemeConstantOverride("outline_size", 6);
        hudLayer.AddChild(_hud);

        BuildLegacyPanel();
        ApplyLegacyFont();

        UI.Settings.Apply();
        _keyPanSpeed = UI.Settings.PanSpeed;

        ParseCmdline();
        // a game started from the menu overrides the command line
        if (UI.SkirmishSetup.Active)
        {
            int mi = System.Array.IndexOf(MapNames, UI.SkirmishSetup.Map);
            if (mi >= 0) _mapIndex = mi;
        }
        LoadMap(_mapIndex);

        // a game picked in the main menu: the map is up, now put the state on it
        if (UI.SkirmishSetup.PendingSave.Length > 0)
        {
            string want = UI.SkirmishSetup.PendingSave;
            UI.SkirmishSetup.PendingSave = "";
            var root = Core.SaveGame.Read(want, out string err);
            if (root == null) GD.PrintErr($"Spielstand: {err}");
            else { _entities.ApplySaveState(root); GD.Print($"Spielstand {want} geladen"); }
        }
        if (UI.SkirmishSetup.Active)
        {
            // A campaign mission is not a skirmish: it keeps the army the level
            // brings and every other side is played, rather than standing about.
            int me = UI.SkirmishSetup.CampaignMission > 0
                ? _entities.StartCampaign(UI.SkirmishSetup.Human, UI.SkirmishSetup.Level)
                : _entities.StartSkirmish(UI.SkirmishSetup.Human,
                         UI.SkirmishSetup.AiCount, UI.SkirmishSetup.Level);
            // start looking at one's own base, not at the whole map
            if (_entities.PlayerHome(me) is { } home)
            {
                float z = Mathf.Max(1.6f, FitZoom());
                _camera.Zoom = new Vector2(z, z);
                _camera.Position = home;
                ClampCamera();
            }
            BuildEndBanner();
        }
        // --select=<n> before anything is photographed, so the panel is filled
        // by the time --shot fires
        if (_selectForShot >= 0) GD.Print(_entities.SelectForShot(_selectForShot));
        if (_navOverlay) _entities.ToggleNav();
        if (_buildingOverlay) _entities.ToggleBuildings();
        if (_railOverlay) _entities.ToggleRail();
        if (_navProbe.Length > 0) { _entities.NavProbe(_navProbe); GetTree().Quit(0); return; }
        if (_groundCheck)
        {
            GD.Print(_entities.GroundCheck(_sprite.Texture?.GetImage()));
            GetTree().Quit(0);
            return;
        }
        if (_infDeathCheck)
        {
            GD.Print(_entities.InfantryDeathCheck());
            GetTree().Quit(0);
            return;
        }
        if (_groupCheck)
        {
            GD.Print(_entities.GroupMoveCheck());
            GetTree().Quit(0);
            return;
        }
        if (_doorCheck)
        {
            GD.Print(_entities.DoorCheck());
            GetTree().Quit(0);
            return;
        }
        if (_animCheck)
        {
            GD.Print(_entities.AnimCheck());
            GetTree().Quit(0);
            return;
        }
        if (_saveCheck)
        {
            GD.Print(_entities.SaveRoundTripCheck(MapNames[_mapIndex]));
            GetTree().Quit(0);
            return;
        }
        if (_captureCheck)
        {
            GD.Print(_entities.CaptureCheck());
            GetTree().Quit(0);
            return;
        }
        if (_pickCheck)
        {
            GD.Print(_entities.PickCheck());
            GetTree().Quit(0);
            return;
        }
        if (_ruinDemo) _entities.RuinDemo();
        if (_ruinCheck)
        {
            GD.Print(_entities.RuinCheck());
            GetTree().Quit(0);
            return;
        }
        if (_corpseCheck)
        {
            GD.Print(_entities.CorpseCheck());
            GetTree().Quit(0);
            return;
        }
        if (_crushCheck)
        {
            GD.Print(_entities.CrushCheck());
            GetTree().Quit(0);
            return;
        }
        if (_buildCheck)
        {
            // The raised buildings are stamped into a copy of the baked map,
            // so the run leaves a picture one can actually look at.
            var img = _sprite.Texture?.GetImage();
            GD.Print(_entities.BuildCheck(img));
            if (img != null && _shotPath.Length > 0)
            {
                img.SavePng(_shotPath);
                GD.Print($"build-check: Bild nach {_shotPath}");
            }
            GetTree().Quit(0);
            return;
        }
        // put the site preview somewhere it can be photographed: the first
        // place that takes the building, and one that refuses it
        if (_buildPreview > 0)
        {
            _entities.DemoBuildPreview(_buildPreview);
            var at = _entities.BuildPreviewCentre;
            if (at.HasValue) { _camera.Position = at.Value; _camera.Zoom = new Vector2(2, 2); }
        }
        // harness: park the camera on a cell, for a screenshot
        if (_look.Length > 0)
        {
            var lp = _look.Split(',');
            if (lp.Length >= 2)
            {
                _camera.Position = new Vector2(lp[0].ToInt() * 40,
                                               lp[1].ToInt() * 20);
                _camera.Zoom = new Vector2(3, 3);
                ClampCamera();
            }
        }
        HookShotTrigger();
        if (_openPause) TogglePause();
        if (_demo) StartDemo();
    }

    /// <summary>`--nav-probe=c0,r0,c1,r1[,klasse]` — ask the grid for a route and
    /// print it. A script cannot drive a tank over a bridge by hand, so the
    /// route is quoted instead of looked at.</summary>
    private string _navProbe = "";

    /// <summary>`--ground-check` — hold the walk grid against the baked picture.</summary>
    private bool _groundCheck;
    private bool _buildCheck;
    private bool _crushCheck;
    private bool _pickCheck;
    private bool _corpseCheck;
    private bool _ruinCheck;
    private bool _ruinDemo;
    private bool _captureCheck;
    private bool _saveCheck;
    private bool _openPause;
    private bool _doorCheck;
    private bool _animCheck;
    private bool _infAnimCheck;
    private string _look = "";
    private bool _groupCheck;
    private bool _infDeathCheck;
    private int _buildPreview;

    // ---- preview helper -----------------------------------------------------
    // Run with:  Godot --path <proj> res://Scenes/Gameplay/MapViewer.tscn --
    //            --map=map_05 --demo --shot=C:/tmp/shot.png --shot-delay=240
    // Drives a scripted move order and writes a screenshot, so a change can be
    // verified visually without clicking through the viewer by hand.
    private string _shotPath = "";
    private int _shotDelay = 180;
    private int _frames;
    private bool _demo;
    private bool _demoNaval;
    private bool _demoFight;
    private int _selectForShot = -1;   // --select=<n>, for photographing the panel
    private bool _demoBuild;
    private bool _demoMine;
    private bool _demoResearch;
    private bool _demoState;
    private bool _demoAir;
    private bool _demoInf;
    private bool _demoCrush;
    private bool _demoSupply;
    private bool _demoCapture;
    private bool _demoTakeover;
    private bool _demoBuildPanel;
    private bool _demoBuy;
    private bool _demoShip;
    private bool _demoTrain;
    private bool _demoDesign;
    private bool _demoQueue;
    private bool _demoAi;
    private bool _demoGroups;
    private int _demoEnd;
    private float _fightDist;
    private bool _navOverlay;
    private bool _buildingOverlay;
    private bool _railOverlay;

    private void ParseCmdline()
    {
        foreach (string a in OS.GetCmdlineUserArgs())
        {
            if (a.StartsWith("--shot=")) _shotPath = a[7..];
            else if (a.StartsWith("--shot-delay=")) _shotDelay = a[13..].ToInt();
            // A headless run has no window to draw into, so the screenshot never
            // arrives and nothing ever calls Quit. This ends the run on the clock
            // instead, which is what a scripted check needs.
            else if (a.StartsWith("--quit-after=")) _quitAfter = a[13..].ToFloat();
            else if (a.StartsWith("--demo-leave=")) _demoLeave = a["--demo-leave=".Length..].ToFloat();
            else if (a == "--demo") _demo = true;
            else if (a == "--demo-naval") { _demo = true; _demoNaval = true; }
            else if (a == "--demo-fight") { _demo = true; _demoFight = true; }
            else if (a == "--demo-mine") { _demo = true; _demoMine = true; }
            else if (a == "--demo-research") { _demo = true; _demoResearch = true; }
            else if (a == "--demo-state") { _demo = true; _demoState = true; }
            else if (a == "--demo-air") { _demo = true; _demoAir = true; }
            else if (a == "--demo-inf") { _demo = true; _demoInf = true; }
            else if (a == "--demo-crush") { _demo = true; _demoCrush = true; }
            else if (a == "--demo-supply") { _demo = true; _demoSupply = true; }
            else if (a == "--demo-capture") { _demo = true; _demoCapture = true; }
            else if (a == "--demo-takeover") { _demo = true; _demoTakeover = true; }
            else if (a == "--demo-buildpanel") { _demo = true; _demoBuildPanel = true; }
            else if (a == "--demo-buy") { _demo = true; _demoBuy = true; }
            else if (a == "--demo-ship") { _demo = true; _demoShip = true; }
            else if (a == "--demo-train") { _demo = true; _demoTrain = true; }
            else if (a == "--demo-design") { _demo = true; _demoDesign = true; }
            else if (a == "--demo-queue") { _demo = true; _demoQueue = true; }
            else if (a == "--demo-ai") { _demo = true; _demoAi = true; }
            else if (a == "--demo-groups") { _demo = true; _demoGroups = true; }
            else if (a == "--demo-win") { _demo = true; _demoEnd = 1; }
            else if (a == "--demo-lose") { _demo = true; _demoEnd = 2; }
            else if (a.StartsWith("--fight-dist=")) { _demo = true; _demoFight = true; _fightDist = a[13..].ToFloat(); }
            else if (a == "--nav") _navOverlay = true;
            else if (a.StartsWith("--nav-probe=")) _navProbe = a["--nav-probe=".Length..];
            else if (a == "--ground-check") _groundCheck = true;
            else if (a == "--build-check") _buildCheck = true;
            else if (a == "--crush-check") _crushCheck = true;
            else if (a == "--pick-check") _pickCheck = true;
            else if (a == "--corpse-check") _corpseCheck = true;
            else if (a == "--ruin-check") _ruinCheck = true;
            else if (a == "--ruin-demo") _ruinDemo = true;
            else if (a == "--capture-check") _captureCheck = true;
            else if (a == "--save-check") _saveCheck = true;
            else if (a == "--pause") _openPause = true;
            else if (a == "--door-check") _doorCheck = true;
            else if (a == "--anim-check") _animCheck = true;
            else if (a == "--inf-anim-check") { _infAnimCheck = true; _demo = true; _demoInf = true; }
            else if (a.StartsWith("--look=")) _look = a["--look=".Length..];
            else if (a == "--group-check") _groupCheck = true;
            else if (a == "--infdeath-check") _infDeathCheck = true;
            else if (a.StartsWith("--build-preview=")) _buildPreview = a["--build-preview=".Length..].ToInt();
            else if (a == "--fog") MapEntityLayer.ForceFog = true;
            else if (a == "--buildings") _buildingOverlay = true;
            else if (a == "--rail") _railOverlay = true;
            else if (a == "--fps60") Engine.MaxFps = 60;   // deterministic captures
            else if (a.StartsWith("--map="))
            {
                int idx = System.Array.IndexOf(MapNames, a[6..]);
                if (idx >= 0) _mapIndex = idx;
            }
            // --select=<n> picks the n-th unit that has a weapon or a tank, so
            // a headless run can photograph the info panel
            else if (a.StartsWith("--select=")) _selectForShot = a["--select=".Length..].ToInt();
        }
    }

    private void StartDemo()
    {
        var focus = _demoEnd != 0 ? _entities.DebugDemoEnd(_demoEnd == 1)
                  : _demoGroups ? _entities.DebugDemoGroups()
                  : _demoBuy ? _entities.DebugDemoBuy()
                  : _demoShip ? _entities.DebugDemoShip()
                  : _demoDesign ? _entities.DebugDemoDesign()
                  : _demoQueue ? _entities.DebugDemoQueue()
                  : _demoTrain ? _entities.DebugDemoTrain()
                  : _demoAi ? _entities.DebugDemoAi()
                  : _demoCapture ? _entities.DebugDemoCapture()
                  : _demoTakeover ? _entities.DebugDemoTakeover()
                  : _demoBuildPanel ? _entities.DebugDemoBuildPanel()
                  : _demoSupply ? _entities.DebugDemoSupply()
                  : _demoCrush ? _entities.DebugDemoCrush()
                  : _demoInf ? _entities.DebugDemoInfantry()
                  : _demoAir ? _entities.DebugDemoAir()
                  : _demoState ? _entities.DebugDemoState()
                  : _demoResearch ? _entities.DebugDemoResearch()
                  : _demoMine ? _entities.DebugDemoMine()
                  : _demoFight ? _entities.DebugDemoFight(_fightDist)
                               : _entities.DebugDemoOrder(_demoNaval);
        if (focus != null)
        {
            float z = _fightDist > 0 ? 1.3f : _demoBuild ? 1.8f : 2.2f;
            _camera.Zoom = new Vector2(z, z);
            _camera.Position = focus.Value;
        }
    }

    /// <summary>Seconds after which a scripted run gives up and quits; 0 = never.</summary>
    private float _quitAfter;
    private float _upTime;

    /// <summary>`--demo-leave=<n>` sends the demo's unit back where it came from
    /// after n seconds. Without it the drain at @0x43D29E is never exercised:
    /// a besieger that never walks away never loses ground, so the branch that
    /// counts the progress DOWN would go untested in a running game.</summary>
    private float _demoLeave;
    private bool _demoLeft;

    private void DemoLeaveIfDue()
    {
        if (_demoLeave <= 0f || _demoLeft || _upTime < _demoLeave) return;
        _demoLeft = true;
        GD.Print($"demo-leave: nach {_upTime:0.0}s zurueck — " + _entities.DebugDemoLeave());
    }

    private void QuitIfDue(double delta)
    {
        if (_infAnimCheck) _entities.InfAnimSample();
        if (_quitAfter <= 0f) { _upTime += (float)delta; DemoLeaveIfDue(); return; }
        _upTime += (float)delta;
        DemoLeaveIfDue();
        if (_upTime >= _quitAfter)
        {
            GD.Print($"MapViewer: --quit-after {_quitAfter:0.0}s erreicht");
            string q = _entities.QueueWatchLine();
            if (q.Length > 0) GD.Print(q);
            string b = _entities.BuildWatchLine();
            if (b.Length > 0) GD.Print(b);
            string ai = _entities.AiLine();
            if (ai.Length > 0) GD.Print(ai);
            string sw = _entities.ShipWatchLine();
            if (sw.Length > 0) GD.Print(sw);
            string cr = _entities.CrushReport();
            if (cr.Length > 0) GD.Print(cr);
            GD.Print(_entities.CaptureWatchLine());
            GD.Print(_entities.TakeoverWatchLine());
            GD.Print(_entities.MinimapWatchLine(_minimap));
            GD.Print(_build?.WatchLine() ?? "bau-panel: nicht gebaut");
            GD.Print(_entities.EventWatchLine());
            GD.Print(_entities.VoiceWatchLine());
            GD.Print(_entities.PanelWatchLine());
            GD.Print(_entities.PoseWatchLine());
            if (_infAnimCheck) GD.Print(_entities.InfAnimReport());
            GD.Print(_entities.FogWatchLine());
            GetTree().Quit();
        }
    }

    /// <summary>
    /// The screenshot trigger hangs on <see cref="SceneTree.ProcessFrame"/>, not
    /// on this node's <c>_Process</c>.
    ///
    /// <para>Why: <c>GetTree().Paused = true</c> stops <c>_Process</c> here, so
    /// <c>--shot</c> together with <c>--pause</c> produced <b>no picture at
    /// all</b> — which is exactly the combination one needs to look at the pause
    /// screen. The tree's own frame signal keeps firing while the tree is
    /// paused, so the harness can photograph a paused game without putting the
    /// whole viewer on <c>ProcessMode.Always</c> and letting the battle run on
    /// behind the screen.</para>
    /// </summary>
    private void HookShotTrigger()
    {
        if (_shotHooked || _shotPath.Length == 0) return;
        _shotHooked = true;
        GetTree().ProcessFrame += TakeShotIfDue;
    }

    private bool _shotHooked;

    private void TakeShotIfDue()
    {
        if (_shotPath.Length == 0 || _frames++ < _shotDelay) return;
        // The window may be occluded, in which case the compositor stops drawing
        // and the viewport texture still holds a stale frame — force one draw.
        RenderingServer.ForceDraw();
        var img = GetViewport().GetTexture().GetImage();
        img.SavePng(_shotPath);
        GD.Print($"MapViewer: screenshot -> {_shotPath} " +
                 $"(frame {_frames}, t={Time.GetTicksMsec() / 1000.0:0.00}s, " +
                 $"sim={_entities.DebugClock:0.00}s over {_entities.DebugTicks} ticks)\n" +
                 $"   combat: {_entities.DebugCombatInfo()}");
        _shotPath = "";
        GetTree().Quit();
    }

    /// <summary>
    /// Build the original side panel (PANEL.DTA) as the HUD frame and hand its
    /// recessed display box to the entity layer for the info text.
    /// The panel is a 204x170 indexed bitmap; it is drawn at an integer scale
    /// with nearest filtering and pinned to the bottom-right corner.
    /// </summary>
    private void BuildLegacyPanel()
    {
        string path = Core.Content.Path("UI/panel.png");
        Texture2D? tex = ResourceLoader.Exists(path) ? ResourceLoader.Load<Texture2D>(path) : null;
        if (tex == null && FileAccess.FileExists(path))
        {
            var img = Image.LoadFromFile(path);
            if (img != null) tex = ImageTexture.CreateFromImage(img);
        }
        if (tex == null) { GD.Print("MapViewer: PANEL.DTA not exported yet"); return; }

        _panelLayer = new CanvasLayer { Layer = 2 };
        AddChild(_panelLayer);
        _panelSprite = new TextureRect
        {
            Texture = tex,
            TextureFilter = TextureFilterEnum.Nearest,
            StretchMode = TextureRect.StretchModeEnum.Keep,
            Scale = new Vector2(PanelScale, PanelScale),
        };
        _panelLayer.AddChild(_panelSprite);
        PlacePanel();
        GetViewport().SizeChanged += PlacePanel;
    }

    /// <summary>The overview map, sitting on top of the side panel.
    ///
    /// It goes ABOVE the panel rather than inside its recessed box, because that
    /// box is the info display and the original uses it as one. The original has
    /// no permanent minimap at all — its command bar offers "Karte des
    /// Einsatzgebietes zeigen" (@0x4ef50c), a map you call up — so this is ours,
    /// added where it displaces nothing.</summary>
    private Minimap? _minimap;
    private bool _showMinimap = true;

    private void BuildMinimap()
    {
        if (_panelLayer == null || _sprite.Texture == null) return;
        _minimap = new Minimap { MouseFilter = Control.MouseFilterEnum.Stop };
        _panelLayer.AddChild(_minimap);
        _minimap.Setup(
            _sprite.Texture,
            _entities.MapPixelSize(),
            _entities.MinimapDots,
            () => _camera.GetViewportRect().Size / _camera.Zoom is var s
                ? new Rect2(_camera.Position - s * 0.5f, s) : default,
            _entities.MinimapAlarms,
            // ⚠ ClampCamera() gehört dazu. Ohne sie schob ein Zug am weissen
            // Sichtfenster der Minimap die Kamera aus der Karte heraus — jeder
            // andere Weg (Tasten, rechte Maustaste ziehen, Sprung nach Hause)
            // ruft sie, nur dieser eine tat es nicht.
            world => { _camera.Position = world; ClampCamera(); },
            _entities.FogTexture);
        PlaceMinimap();
        GetViewport().SizeChanged += PlaceMinimap;
    }

    /// <summary>Sits on top of the panel, as wide as the panel and never taller
    /// than MinimapMaxH. A tall map keeps its proportions by getting narrower —
    /// squeezing it to fit would put the dots in the wrong places.</summary>
    private const float MinimapMaxH = 200;

    private void PlaceMinimap()
    {
        if (_minimap == null || _panelSprite?.Texture == null) return;
        Vector2 panel = _panelSprite.Texture.GetSize() * PanelScale;
        float w = panel.X;
        float h = _minimap.HeightFor(w);
        if (h > MinimapMaxH) { w *= MinimapMaxH / h; h = MinimapMaxH; }
        var view = GetViewportRect().Size;
        _minimap.Position = new Vector2(view.X - w, view.Y - panel.Y - h - 4);
        _minimap.Size = new Vector2(w, h);
        _minimap.Visible = _showMinimap;
        _minimap.QueueRedraw();
    }

    private CanvasLayer? _panelLayer;
    private TextureRect? _panelSprite;
    private const int PanelScale = 2;
    // recessed display box inside panel.png, from panel_index.json
    private static readonly Rect2 PanelBox = new(8, 43, 153, 94);

    private void PlacePanel()
    {
        if (_panelSprite?.Texture == null) return;
        Vector2 size = _panelSprite.Texture.GetSize() * PanelScale;
        Vector2 view = GetViewportRect().Size;
        var origin = new Vector2(view.X - size.X, view.Y - size.Y);
        _panelSprite.Position = origin;
        var box = new Rect2(origin + PanelBox.Position * PanelScale,
                            PanelBox.Size * PanelScale);
        _entities.SetPanelBox(box);
        if (_build != null)
        {
            _build.Position = box.Position + new Vector2(2, 2);
            _build.Size = box.Size - new Vector2(4, 4);
            _build.QueueRedraw();
        }
    }

    /// <summary>The production list, in the same recessed box as the info text —
    /// they take turns, because both belong to the selection. See
    /// UI/BuildPanel.cs for what the original does instead (a building screen of
    /// its own) and what is therefore ours here.</summary>
    private UI.BuildPanel? _build;

    private void BuildProductionPanel()
    {
        if (_panelLayer == null) return;
        _build = new UI.BuildPanel
        {
            MouseFilter = Control.MouseFilterEnum.Stop,
            Visible = false,
        };
        _panelLayer.AddChild(_build);
        _build.Setup(_entities.BuildPanelRows, _entities.BuildPanelTitle,
                     _entities.BuildPanelPick);
        PlacePanel();
    }

    /// <summary>Show the list whenever the selection is something of the
    /// player's that builds, and give the box back to the info text otherwise.
    /// </summary>
    private void UpdateProductionPanel()
    {
        if (_build == null) return;
        bool want = _entities.BuildPanelWanted && !_hidePanelList;
        if (want != _build.Visible)
        {
            _build.Visible = want;
            _entities.SetPanelTextVisible(!want);
        }
        if (want) _build.QueueRedraw();
    }

    /// <summary>`P` hides the list, for a look at the info text underneath.</summary>
    private bool _hidePanelList;

    /// <summary>
    /// Put the game's own typeface (FONT.CWD, exported as a BMFont) on the HUD.
    /// It is a 13 px bitmap font, so it is drawn at an integer multiple with
    /// nearest filtering — anything else turns it to mush.
    /// </summary>
    private void ApplyLegacyFont()
    {
        string path = Core.Content.Path("UI/akte_font.fnt");
        Font? font = ResourceLoader.Exists(path) ? ResourceLoader.Load<Font>(path) : null;
        if (font == null && FileAccess.FileExists(path))
        {
            // Content the importer wrote has no Godot import step, so the
            // resource loader cannot see it. A BMFont can be read at run time,
            // which is exactly what an imported game needs.
            var bmp = new FontFile();
            if (bmp.LoadBitmapFont(path) == Error.Ok) font = bmp;
        }
        if (font == null)
        {
            GD.Print("MapViewer: legacy font not imported yet — using the default");
            return;
        }
        int size = LegacyFontCell * LegacyFontScale;
        _hud.AddThemeFontOverride("font", font);
        _hud.AddThemeFontSizeOverride("font_size", size);
        _hud.AddThemeConstantOverride("outline_size", 0);
        _hud.AddThemeConstantOverride("line_spacing", 2 * LegacyFontScale);
        _entities.SetUiFont(font, size);
        _build?.SetFont(font, size, PanelScale);
        _legacyFont = font;
        GD.Print($"MapViewer: legacy FONT.CWD applied at {size}px");
    }

    private Font? _legacyFont;

    private const int LegacyFontCell = 13;   // the original glyph cell height
    private const int LegacyFontScale = 2;   // integer upscale for modern screens

    // ---- end of a skirmish -------------------------------------------------

    private Control? _endBanner;
    private Label? _endText;
    private bool _ended;

    /// <summary>A plate that drops in when the skirmish is decided. The verdict
    /// itself is the original's rule (out with nothing left, won once no
    /// unallied player has anything) — only the screen is ours.</summary>
    private void BuildEndBanner()
    {
        var layer = new CanvasLayer { Layer = 10 };
        AddChild(layer);
        _endBanner = new Control { Visible = false };
        _endBanner.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        layer.AddChild(_endBanner);

        var dim = new ColorRect { Color = new Color(0, 0, 0, 0.55f) };
        dim.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _endBanner.AddChild(dim);

        var box = new VBoxContainer
        {
            AnchorLeft = 0.5f, AnchorRight = 0.5f, AnchorTop = 0.5f, AnchorBottom = 0.5f,
            GrowHorizontal = Control.GrowDirection.Both,
            GrowVertical = Control.GrowDirection.Both,
            CustomMinimumSize = new Vector2(420, 0),
        };
        box.AddThemeConstantOverride("separation", 14);
        _endBanner.AddChild(box);

        _endText = new Label { HorizontalAlignment = HorizontalAlignment.Center };
        _endText.AddThemeFontSizeOverride("font_size", 40);
        box.AddChild(_endText);

        var back = new Button { Text = "Zurueck zum Menue", CustomMinimumSize = new Vector2(0, 42) };
        back.Pressed += ToMenu;
        box.AddChild(back);

        var watch = new Button { Text = "Weiter zusehen" };
        watch.Pressed += () => { if (_endBanner != null) _endBanner.Visible = false; };
        box.AddChild(watch);
    }

    private void ToMenu()
    {
        UI.SkirmishSetup.Active = false;
        GetTree().ChangeSceneToFile(UI.SkirmishSetup.MenuScene);
    }

    private void CheckEnd()
    {
        if (_ended || _endBanner == null || !UI.SkirmishSetup.Active) return;
        string v = _entities.Verdict();
        if (v.Length == 0) return;
        _ended = true;
        bool won = v.Contains("ERFUELLT");
        if (_endText != null)
        {
            _endText.Text = won ? "SIEG" : "NIEDERLAGE";
            _endText.Modulate = won ? new Color(0.6f, 1f, 0.6f) : new Color(1f, 0.55f, 0.5f);
        }
        _endBanner.Visible = true;
        int mission = UI.SkirmishSetup.CampaignMission;
        if (won && mission > 0)
        {
            Campaign.CampaignManager.Finished(mission);
            var next = Campaign.CampaignManager.Next();
            if (_endText != null)
                _endText.Text = next != null
                    ? $"SIEG\nWeiter: {next.Label}" : "SIEG\nKampagne beendet";
            GD.Print($"Kampagne: Mission {mission} geschafft, weiter mit " +
                     $"{(next != null ? next.Label : "— nichts mehr")}");
        }
        GD.Print($"{(mission > 0 ? "Mission" : "Gemetzel")} entschieden: {v}");
    }

    private void LoadMap(int index)
    {
        _mapIndex = Mathf.PosMod(index, MapNames.Length);
        string name = MapNames[_mapIndex];
        string pngPath = MapFile(name + ".png");

        // Prefer the Godot-imported texture (export-safe); fall back to a raw
        // file load so the viewer also works before an import pass has run.
        Texture2D? tex = ResourceLoader.Exists(pngPath)
            ? ResourceLoader.Load<Texture2D>(pngPath)
            : null;

        if (tex == null)
        {
            var img = Image.LoadFromFile(pngPath);
            if (img == null)
            {
                GD.PrintErr($"MapViewer: failed to load {pngPath}");
                _hud.Text = $"FAILED TO LOAD {name}.png";
                return;
            }
            tex = ImageTexture.CreateFromImage(img);
        }

        _sprite.Texture = tex;

        var meta = LoadMeta(name);
        FitToWindow();
        UpdateHud(name, tex.GetSize(), meta);
        _entities.Load(name, meta);
        BuildMinimap();          // needs both the picture and the loaded entities
        if (_build == null)
        {
            BuildProductionPanel();
            if (_legacyFont != null)
                _build?.SetFont(_legacyFont, LegacyFontCell * LegacyFontScale, PanelScale);
        }
        GD.Print($"MapViewer: loaded {name} ({tex.GetWidth()}x{tex.GetHeight()})");
    }

    private static GDict LoadMeta(string name)
    {
        string jsonPath = MapFile(name + ".json");
        if (!FileAccess.FileExists(jsonPath))
            return new GDict();

        using var f = FileAccess.Open(jsonPath, FileAccess.ModeFlags.Read);
        if (f == null)
            return new GDict();

        var json = new Json();
        if (json.Parse(f.GetAsText()) != Error.Ok || json.Data.VariantType != Variant.Type.Dictionary)
            return new GDict();

        return json.Data.AsGodotDictionary<string, Variant>();
    }

    private void UpdateHud(string name, Vector2 size, GDict meta)
    {
        string mission = meta.TryGetValue("mission", out var m) ? m.AsString() : "?";
        string dims = meta.TryGetValue("width", out var w) && meta.TryGetValue("height", out var h)
            ? $"{w.AsInt32()}x{h.AsInt32()} tiles"
            : "?";
        string tileset = meta.TryGetValue("tileset", out var ts) ? ts.AsString() : "?";

        _hud.Text =
            $"{MapLabel(name)}   \"{mission}\"\n" +
            $"grid {dims}   tileset {tileset}   image {(int)size.X}x{(int)size.Y}px\n" +
            $"[{_mapIndex + 1}/{MapNames.Length}]   click/drag=select  RIGHT-click=move  X=stop  E=eingraben  M=konstruieren  B=bauen  N=auswahl  O=forschen  K=reparieren  V=lagerausbau  C=prod.erw.  L=schienen  Y=flugzeuge  " +
            $"WASD+middle-drag=pan  wheel=zoom\n" +
            $"[ ]=map  F=fit  U=sprites  R=ranges  P=walkable  Z=zones  J=nebel  T=buildings  G=dots  H=karte  Tab=ereignis  Shift+rechts=anreihen  Esc=quit";

        _hudBase = _hud.Text;
        RefreshObjectives();
    }

    /// <summary>
    /// Append the mission's win-condition progress (CWM sec69) to the status
    /// plate. Only the .DM full-state maps carry targets, so this is empty on a
    /// plain campaign level.
    /// </summary>
    private void RefreshObjectives()
    {
        string obj = _entities.MoneyLine();
        // the per-player objective lists win over the flat one when the map
        // actually carries a player table
        string goals = _entities.MissionLine();
        if (goals.Length == 0) goals = _entities.ObjectiveSummary();
        if (goals.Length > 0) obj += "   " + goals;
        // the design screen takes the HUD over while it is up — it needs the
        // room, and nothing else is being ordered meanwhile
        if (_entities.Designer.Active)
        {
            _hud.Text = _entities.Designer.Text();
            return;
        }
        _hud.Text = obj.Length > 0 ? _hudBase + "\n" + obj : _hudBase;
        _hudBg.Size = _hud.GetMinimumSize() + new Vector2(16, 10);
    }

    private string _hudBase = "";
    private float _objTimer;

    private void FitToWindow()
    {
        if (_sprite.Texture == null)
            return;

        Vector2 tex = _sprite.Texture.GetSize();
        Vector2 view = GetViewportRect().Size;
        if (tex.X <= 0 || tex.Y <= 0)
            return;

        // never below the fit: further out only shows the black around the map
        float scale = Mathf.Max(Mathf.Min(view.X / tex.X, view.Y / tex.Y), FitZoom());
        _camera.Zoom = new Vector2(scale, scale);
        _camera.Position = tex * 0.5f; // sprite is top-left anchored, so center = half size
        ClampCamera();
    }

    public override void _Process(double delta)
    {
        QuitIfDue(delta);
        UpdateProductionPanel();

        _objTimer -= (float)delta;
        if (_objTimer <= 0f) { _objTimer = 0.5f; RefreshObjectives(); CheckEnd(); }

        // keyboard camera panning (left mouse is the selection box now)
        var dir = Vector2.Zero;
        if (Input.IsKeyPressed(Key.Left) || Input.IsKeyPressed(Key.A)) dir.X -= 1;
        if (Input.IsKeyPressed(Key.Right) || Input.IsKeyPressed(Key.D)) dir.X += 1;
        if (Input.IsKeyPressed(Key.Up) || Input.IsKeyPressed(Key.W)) dir.Y -= 1;
        if (Input.IsKeyPressed(Key.Down) || Input.IsKeyPressed(Key.S)) dir.Y += 1;
        if (dir != Vector2.Zero)
        {
            _camera.Position += dir.Normalized() * (float)delta * _keyPanSpeed / _camera.Zoom.X;
            ClampCamera();
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton mb)
        {
            switch (mb.ButtonIndex)
            {
                case MouseButton.Left:
                    if (mb.Pressed)
                    {
                        _leftDown = true;
                        _boxSelect = false;
                        _leftStart = mb.Position;
                        _bandStart = GetGlobalMousePosition();
                    }
                    else
                    {
                        if (_leftDown && _boxSelect)
                            _entities.BoxSelect(RectFrom(_bandStart, GetGlobalMousePosition()),
                                                mb.ShiftPressed);
                        else if (_leftDown)
                            _entities.SelectAt(GetGlobalMousePosition(), mb.ShiftPressed);
                        _leftDown = false;
                        _boxSelect = false;
                        _entities.SetBand(null);
                    }
                    break;
                case MouseButton.Right:
                    // A short right-click is an order — on an enemy attack, on
                    // the ground move, with Shift appended to what the unit
                    // already has. Holding and DRAGGING pans the map instead:
                    // the middle button alone is no use on a laptop trackpad.
                    if (mb.Pressed)
                    {
                        _rightDown = true;
                        _rightDrag = false;
                        _rightStart = mb.Position;
                        _dragLast = mb.Position;
                    }
                    else
                    {
                        if (_rightDown && !_rightDrag)
                        {
                            if (!_entities.IssueAttack(GetGlobalMousePosition(), mb.ShiftPressed))
                                _entities.IssueMove(GetGlobalMousePosition(), mb.ShiftPressed);
                        }
                        _rightDown = false;
                        _rightDrag = false;
                    }
                    break;
                case MouseButton.Middle:
                    _panDrag = mb.Pressed;
                    _dragLast = mb.Position;
                    break;
                case MouseButton.WheelUp:
                    if (mb.Pressed) ZoomAt(mb.Position, ZoomStep);
                    break;
                case MouseButton.WheelDown:
                    if (mb.Pressed) ZoomAt(mb.Position, 1f / ZoomStep);
                    break;
            }
        }
        else if (@event is InputEventMouseMotion motion)
        {
            // A held left button with enough travel becomes a selection box
            // (a small wobble on click still selects the unit under the cursor).
            if (_leftDown && !_boxSelect &&
                (motion.Position - _leftStart).Length() > ClickSlop)
                _boxSelect = true;

            // the right button becomes a pan once it has travelled far enough
            if (_rightDown && !_rightDrag && UI.Settings.RightDragPan &&
                (motion.Position - _rightStart).Length() > ClickSlop)
                _rightDrag = true;

            if (_panDrag || _rightDrag)
            {
                _camera.Position -= (motion.Position - _dragLast) / _camera.Zoom;
                ClampCamera();
                _dragLast = motion.Position;
            }
            else if (_boxSelect)
            {
                _entities.SetBand(RectFrom(_bandStart, GetGlobalMousePosition()));
            }
            else if (!_leftDown)
            {
                _entities.HoverAt(GetGlobalMousePosition());
                UpdateCursor(GetGlobalMousePosition());
            }
        }
        else if (@event is InputEventKey key && key.Pressed && !key.Echo)
        {
            // While the design screen is up the arrow keys belong to it, so the
            // camera does not wander off under the player's hands.
            if (_entities.Designer.Active && key.Keycode != Key.M)
            {
                switch (key.Keycode)
                {
                    case Key.Up: _entities.DesignerInput(-1, 0, false); return;
                    case Key.Down: _entities.DesignerInput(1, 0, false); return;
                    case Key.Left: _entities.DesignerInput(0, -1, false); return;
                    case Key.Right: _entities.DesignerInput(0, 1, false); return;
                    case Key.Enter:
                    case Key.KpEnter: _entities.DesignerInput(0, 0, true); return;
                    case Key.Escape: _entities.ToggleDesigner(); return;
                }
            }
            switch (key.Keycode)
            {
                case Key.Bracketright: LoadMap(_mapIndex + 1); break;
                case Key.Bracketleft: LoadMap(_mapIndex - 1); break;
                case Key.F: FitToWindow(); break;
                case Key.G: _entities.ToggleDots(); break;
                case Key.Z: _entities.ToggleZones(); break;
                case Key.J: _entities.ToggleFog(); break;
                case Key.T: _entities.ToggleBuildings(); break;
                case Key.U: _entities.ToggleSprites(); break;
                case Key.P: _entities.ToggleNav(); break;
                case Key.X: _entities.StopSelected(); break;
                case Key.R: _entities.ToggleRanges(); break;
                case Key.E: _entities.ToggleDigIn(); break;
                case Key.M: _entities.ToggleDesigner(); break;
                case Key.B: _entities.ProduceFromSelection(); break;
                case Key.N: _entities.CycleBuildMenu(); break;
                // the production list shares the display box with the info
                // text; I hides it for a look at what is underneath
                case Key.I: _hidePanelList = !_hidePanelList; UpdateProductionPanel(); break;
                case Key.O: _entities.StartResearch(); break;
                case Key.K: _entities.StartRepair(); break;
                case Key.L: _entities.ToggleRail(); break;
                case Key.Y: _entities.LaunchAircraft(_entities.ViewPlayer); break;
                case Key.V: _entities.StartUpgrade(true); break;   // Lagerausbau
                case Key.C: _entities.StartUpgrade(false); break;  // Produktionserw.
                case Key.Escape:
                    TogglePause();
                    break;
                // Space centres on what is selected — the map is up to
                // 10000 px wide, so losing the selection is easy
                case Key.Space: JumpToSelection(); break;
                // Tab jumps to the last thing that happened to us — a unit
                // taking fire, a finished build
                case Key.Tab: JumpToEvent(); break;
                case Key.H:                                  // Uebersichtskarte ein/aus
                    _showMinimap = !_showMinimap;
                    if (_minimap != null) _minimap.Visible = _showMinimap;
                    break;
                default: HandleGroupKey(key); break;
            }
        }
    }

    /// <summary>Control groups: Ctrl+1..9 stores the selection, 1..9 recalls it
    /// and a second press on the same number also centres the camera. RTS
    /// convention, ours — the original's key layout is not in the data.</summary>
    private int _lastGroup = -1;

    private void HandleGroupKey(InputEventKey key)
    {
        int n = key.Keycode switch
        {
            >= Key.Key1 and <= Key.Key9 => (int)(key.Keycode - Key.Key1) + 1,
            >= Key.Kp1 and <= Key.Kp9 => (int)(key.Keycode - Key.Kp1) + 1,
            _ => 0,
        };
        if (n == 0) return;
        if (key.CtrlPressed) { _entities.StoreGroup(n); _lastGroup = -1; return; }
        if (!_entities.RecallGroup(n)) return;
        if (_lastGroup == n) JumpToSelection();
        _lastGroup = n;
    }

    private void JumpToSelection()
    {
        var p = _entities.SelectionCenter();
        if (p != null) _camera.Position = p.Value;
    }

    /// <summary>Tab walks back through the recent incidents instead of always
    /// landing on the same one — with several things happening at once, one slot
    /// only ever showed the last of them.</summary>
    private void JumpToEvent()
    {
        var e = _entities.StepEvent();
        if (e != null) _camera.Position = e.Value.Pos;
    }

    private bool _rightDown, _rightDrag;
    private Vector2 _rightStart;
    private Input.CursorShape _cursor = Input.CursorShape.Arrow;

    /// <summary>Lets the pointer say what a click would do: a cross-hair over
    /// something worth shooting at, a pointing hand over one's own unit, the
    /// plain arrow over open ground. Only changed when it actually differs, so
    /// this costs nothing while the mouse moves.</summary>
    private void UpdateCursor(Vector2 mapPos)
    {
        if (!UI.Settings.CursorHints)
        {
            if (_cursor != Input.CursorShape.Arrow)
            {
                _cursor = Input.CursorShape.Arrow;
                Input.SetDefaultCursorShape(_cursor);
            }
            return;
        }
        var want = _entities.CursorHintAt(mapPos) switch
        {
            MapEntityLayer.Hint.Enemy => Input.CursorShape.Cross,
            MapEntityLayer.Hint.Own => Input.CursorShape.PointingHand,
            _ => Input.CursorShape.Arrow,
        };
        if (want == _cursor) return;
        _cursor = want;
        Input.SetDefaultCursorShape(want);
    }

    private static Rect2 RectFrom(Vector2 a, Vector2 b)
        => new Rect2(a, b - a).Abs();

    private void ZoomAt(Vector2 screenPos, float factor)
    {
        Vector2 before = _camera.GetGlobalMousePosition();
        float z = Mathf.Clamp(_camera.Zoom.X * factor, FitZoom(), MaxZoom);
        _camera.Zoom = new Vector2(z, z);
        Vector2 after = _camera.GetGlobalMousePosition();
        _camera.Position += before - after; // keep point under cursor fixed
        ClampCamera();
    }
}
