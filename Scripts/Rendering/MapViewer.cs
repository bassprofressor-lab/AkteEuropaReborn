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

    /// <summary>Sagt an, was der Cheat-Mode gerade tut. ⚠ Ein Schummel, der
    /// still laeuft, verfaelscht jeden spaeteren Pruefstand — darum meldet
    /// sich jeder Wechsel, und der Zustand steht in der Statuszeile.</summary>
    private void SayCheat()
    {
        string t = MapEntityLayer.CheatLine();
        GD.Print(t.Length > 0 ? t : "CHEAT: alle aus");
        _entities.Say(t.Length > 0 ? t : "Cheat-Mode aus");
    }

    private void ClosePause()
    {
        // Der Kartenlauf wird in `_Ready` EINMAL gelesen; ohne diese Zeile wirkt
        // der Regler im Pausenmenü erst beim nächsten Missionsstart. Änderbar
        // ist er nur hier, darum genügt das Nachziehen beim Schliessen.
        _keyPanSpeed = UI.Settings.PanSpeed;
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

        BuildResourceBar(hudLayer);

        BuildLegacyPanel();
        ApplyLegacyFont();

        UI.Settings.Apply();
        _keyPanSpeed = UI.Settings.PanSpeed;

        ParseCmdline();
        // --skirmish: den Gefechtsmodus von der Kommandozeile aus anwerfen,
        // damit ein Prueflauf ihn ueberhaupt erreichen kann. Steht hier und
        // nicht in ParseCmdline(), weil --map= und --skirmish= in beliebiger
        // Reihenfolge kommen duerfen.
        if (_skirmish)
        {
            UI.SkirmishSetup.Active = true;
            UI.SkirmishSetup.CampaignMission = 0;
            UI.SkirmishSetup.Map = _skirmishMap.Length > 0 ? _skirmishMap : MapNames[_mapIndex];
        }
        // a game started from the menu overrides the command line
        if (UI.SkirmishSetup.Active)
        {
            int mi = System.Array.IndexOf(MapNames, UI.SkirmishSetup.Map);
            if (mi >= 0) _mapIndex = mi;
        }
        // Der Karteneditor schickt eine Karte NUR ZUM ANSEHEN her: kein
        // Gefecht, keine KI, keine Abrechnung — siehe UI/SkirmishSetup.ViewMap,
        // wo auch steht, warum ein Gefecht darauf nicht ginge.
        //
        // ⚠ Die Kartenliste wird dafuer VERWORFEN und neu eingelesen. Sie ist
        // ein statisches Feld (_mapNames) und ueberlebt den Szenenwechsel; eine
        // Karte, die der Spieler eben erst erzeugt hat, steht also nicht darin,
        // wenn in derselben Sitzung schon einmal eine Partie lief. IndexOf
        // ergaebe dann -1, _mapIndex bliebe stehen und gezeigt wuerde
        // klammheimlich map_01 — genau die stille Kartenvertauschung, die
        // --skirmish= sich am 11.08.2026 schon einmal eingehandelt hat.
        else if (UI.SkirmishSetup.ViewMap.Length > 0)
        {
            string want = UI.SkirmishSetup.ViewMap;
            UI.SkirmishSetup.ViewMap = "";
            _mapNames = null;
            int mi = System.Array.IndexOf(MapNames, want);
            if (mi >= 0) _mapIndex = mi;
            else GD.PrintErr($"Karteneditor: {want} liegt nicht in {Core.Content.UserRoot}Maps/ " +
                             "— es wird die zuletzt gewaehlte Karte gezeigt");
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
            // Der Kontostand geht von einer Mission in die naechste mit — das
            // ist gelesen, siehe Campaign.CampaignManager.Balance. Vorher fing
            // JEDE Kampagnenmission bei $0 an; genau das hat der Spieler als
            // »in Kampagne 2 habe ich kein Geld« gemeldet.
            if (UI.SkirmishSetup.CampaignMission > 0)
            {
                // ⚠ 13.08.2026 — DER SPIELSTAND EINES FRUEHEREN LAUFS. Ein
                // Prueflauf, der ihn stillschweigend mitschleppt, meldet die
                // Zahlen der VORIGEN Laeufe: gemessen wurden $47465 in
                // Mission 23 und $970 statt $470 in Mission 5, beides Erloese
                // eigener Prueffahrten. Dieselbe Fehlerklasse wie »ein Leser,
                // der Zustand ueber eine Grenze mittraegt«. Also entweder
                // wegraeumen (--fresh-campaign) oder ausdruecklich sagen, dass
                // er vorgefunden wurde — schweigen darf er nicht.
                if (_freshCampaign)
                {
                    Campaign.CampaignManager.Reset();
                    GD.Print("Kampagne: --fresh-campaign — Spielstand " +
                             "(geschaffte Missionen und Kontostand) auf 0 gesetzt");
                }
                int geschafft = Campaign.CampaignManager.Completed;
                // ⚠⚠ 18.08.2026 — DER ANFANGSSTAND DIESER MISSION, nicht der
                // laufende. Gemeldet: »das ist doof, wenn jemand Kampagne 1
                // immer und immer wieder spielen will, dann hat er ja immer
                // einen groesseren Kontostand, bloss weil er schon in
                // Wirklichkeit bei Kampagne 22 ist.«
                // Siehe CampaignManager.BalanceForStartOf.
                int mit = Campaign.CampaignManager.BalanceForStartOf(
                              UI.SkirmishSetup.CampaignMission);
                _entities.SetStartMoney(me, mit);
                GD.Print($"Kampagne: Mission {UI.SkirmishSetup.CampaignMission} " +
                         $"beginnt mit Kontostand ${mit}" +
                         (Campaign.CampaignManager.Balance != mit
                             ? $" (gemerkter Anfangsstand dieser Mission; laufend waeren es " +
                               $"${Campaign.CampaignManager.Balance})"
                             : "") +
                         (mit != 0 || geschafft != 0
                             ? $"   ⚠ AUS DEM SPIELSTAND {Campaign.CampaignManager.SavePath} " +
                               $"({geschafft} Missionen geschafft) — fuer eine Geldmessung " +
                               "--fresh-campaign nehmen"
                             : ""));
            }
            // ⚠⚠ 16.08.2026 — DAS GEFECHT BEKOMMT SEIN STARTGELD (Fehler E-Geld).
            // Bis hierher wurde SetStartMoney NUR im Kampagnenzweig darueber
            // gerufen, und im Gefecht kam _money allein aus sec73 der Karte --
            // die KEINE Gefechtskarte hat. Jeder Spieler stand also auf $0,
            // und weil zugleich der MARKT fehlt, war Geld im Gefecht eine tote
            // Zahl: keine Quelle, keine Ausgabe.
            //
            // Das Original setzt es beim Gefechtsaufbau @0x41ABD6 in einer
            // Schleife an ALLE ACHT Spieler -- nicht nur an den Menschen.
            // Sonst koennte die KI nie kaufen.
            else if (UI.SkirmishSetup.StartMoney > 0)
            {
                for (int p = 0; p < 8; p++)
                    _entities.SetStartMoney(p, UI.SkirmishSetup.StartMoney);
                GD.Print($"Gefecht: Startkonto ${UI.SkirmishSetup.StartMoney} " +
                         "fuer alle 8 Spieler (@0x41ABD6)");
            }

            // start looking at one's own base, not at the whole map
            if (_entities.PlayerHome(me) is { } home)
            {
                float z = Mathf.Max(1.6f, FitZoom());
                _camera.Zoom = new Vector2(z, z);
                _camera.Position = home;
                ClampCamera();
                // B7: denselben Punkt, auf den die Kamera faehrt, behaelt die
                // Minimap als Startplatz — er wandert danach nicht mehr mit.
                _entities.MarkHome(home);
            }
            BuildEndBanner();
        }
        // ⚠ Steht der Bildprüfstand mit `--shot` zusammen, meldet er GENAU das
        // photographierte Bild. Sonst beschreiben seine Zahlen einen anderen
        // Augenblick als das Bild, das man sich ansieht — und dann ist der
        // Vergleich wertlos. Hier und nicht in ParseCmdline(), weil
        // --shot-delay= und --portrait-check= in beliebiger Reihenfolge kommen
        // dürfen (dieselbe Überlegung wie bei --skirmish= oben).
        if (_portraitCheckAt >= 0 && _shotPath.Length > 0)
            _portraitCheckAt = Mathf.Max(_portraitCheckAt, _shotDelay);

        // --select=<n> before anything is photographed, so the panel is filled
        // by the time --shot fires
        if (_selectForShot >= 0)
            GD.Print(_entities.SelectForShot(_selectForShot, _selectTypeForShot,
                                             _selectBuildingForShot));
        if (_designWindowDemo && !_entities.Designer.Active) _entities.ToggleDesigner();
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
        // ⚠ KEIN Quit hier: dieser Pruefstand braucht die Zeit danach. Der
        // Befehl geht jetzt raus, gezaehlt wird beim --quit-after.
        if (_speedCheck) GD.Print(_entities.SpeedCheckStart());
        else if (_stuckCheck) GD.Print(_entities.StuckCheckStart());
        if (_depotCheck)
        {
            GD.Print(_entities.DepotCheck());
            GetTree().Quit(0);
            return;
        }
        if (_overdrawCheck)
        {
            GD.Print(_entities.OverdrawCheck());
            GetTree().Quit(0);
            return;
        }
        if (_depotFlow) _entities.DepotFlowStart();
        if (_sellCheck) _entities.SellCheckStart();
        if (_shopCheckFlag) _entities.ShopCheckStart();
        if (_buyCheckFlag) _entities.BuyCheckStart();
        if (_dockCheckFlag) _entities.DockCheckStart();
        if (_powerCheckFlag) _entities.PowerCheckStart();
        if (_radarCheckFlag) _entities.RadarCheckStart();
        if (_bauCheckFlag) _entities.BauCheckStart(_bauCheckOrder);
        if (_ausbauCheckFlag) _entities.AusbauCheckStart();
        if (_mechCheckFlag) _entities.MechanikerCheckStart();
        if (_flugCheckFlag) _entities.FlugCheckStart();
        if (_schiffCheckFlag) _entities.SchiffWaffeCheckStart();
        if (_knopfCheckFlag) _entities.KnopfCheckStart();
        if (_m21CheckFlag) _entities.M21CheckStart();
        if (_ringCheckTicks > 0) _entities.RingCheckStart(_ringCheckTicks);
        if (_marketCheck)
        {
            GD.Print(_entities.MarketCheck());
            GetTree().Quit(0);
            return;
        }
        if (_hangarCheck)
        {
            GD.Print(_entities.HangarCheck());
            GetTree().Quit(0);
            return;
        }
        if (_producePics)
        {
            GD.Print(_entities.ProducePicsCheck());
            GetTree().Quit(0);
            return;
        }
        if (_wagonFacingCheck)
        {
            GD.Print(_entities.WagonFacingCheck());
            GetTree().Quit(0);
            return;
        }
        if (_coverageCheck)
        {
            GD.Print(_entities.ScriptCoverage());
            GetTree().Quit(0);
            return;
        }
        if (_tickCheck > 0f)
        {
            GD.Print(_entities.TickCheck(_tickCheck));
            GetTree().Quit(0);
            return;
        }
        if (_tutorialCheck)
        {
            GD.Print(_entities.TutorialCheck());
            // Mit `--shot` bleibt der Lauf stehen, damit das letzte Fenster auch
            // im Bild landet; ohne ist der Pruefstand fertig.
            if (_shotPath.Length == 0) { GetTree().Quit(0); return; }
        }
        if (_soundCheck)
        {
            _entities.SetListener(_camera.Position);
            GD.Print(_entities.SoundDistanceCheck());
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
        if (_bAnimDemo) _entities.BAnimDemo();
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
        if (_terraCheck)
        {
            GD.Print(_entities.TerraCheckLine());
            GetTree().Quit(0);
            return;
        }
        if (_shipCheck)
        {
            GD.Print(_entities.ShipCheckLine());
            GetTree().Quit(0);
            return;
        }
        if (_buildCheck)
        {
            // ⚠ Erst das Missionsskript, dann die Bauplaetze. Die
            // ROHSTOFFVORKOMMEN kommen aus dem Missionsaufbau, nicht von der
            // Karte, und sie stehen erst da, wenn das Skript steht. Ohne diese
            // Zeile meldete der Prueflauf auf map_23 »Feld-Rohstoffmine:
            // 0 Bauplaetze« — richtig gemessen, nur zu frueh.
            _entities.EnsureMissionScript();
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
                // ⚠ 13.08.2026 — hier stand `spalte*40, zeile*20`, und das ist
                // NICHT die Stelle, an der die Zelle gezeichnet wird: es fehlten
                // der Zeichenursprung der Karte, die halbe Zelle und die
                // Gelaendehoehe (siehe MapEntityLayer.RailCellPoint). --look
                // zielte damit systematisch daneben -- auf Zelle 163,46 der
                // map_NET02 um zweieinhalb Spalten. Gefunden hat es der
                // Rundgang --rail-tour, der auf einer Rampe stehen sollte und
                // Wiese zeigte.
                _camera.Position = _entities.RailCellPoint(lp[0].ToInt(), lp[1].ToInt());
                // --look=<spalte>,<zeile>[,<zoom>] — ohne Zoomwert wie bisher 3.
                float z = lp.Length >= 3 ? Mathf.Max(0.2f, lp[2].ToFloat()) : 3f;
                _camera.Zoom = new Vector2(z, z);
                ClampCamera();
            }
        }
        // `--rail-hit-check` — Gleisschaden und Reparatur ausueben, nicht setzen
        if (_railHitCheck) { GD.Print(_entities.RailHitCheck()); GetTree().Quit(); return; }
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
    /// <summary>`--banim-check` — die Zellanimation der Gebäude gegen das
    /// Original halten und im Lauf abtasten. Siehe
    /// <see cref="MapEntityLayer.BAnimCheck"/>.</summary>
    private bool _bAnimCheck;
    /// <summary>`--banim-demo` — die animierten Gebäude beschädigt stehen
    /// lassen, damit ein Bildschirmfoto Auflage und Band zugleich zeigt.</summary>
    private bool _bAnimDemo;
    private bool _infAnimCheck;
    /// <summary>`--veh-anim-check` — the vehicle half of Fehlerliste Punkt 2:
    /// do Mechs and Spinnen step through their gait while driving?</summary>
    private bool _vehAnimCheck;
    /// <summary>`--rail-hit-check` — Gleisschaden und Reparatur ausüben.</summary>
    private bool _railHitCheck;
    private float _railRepairCheck;

    /// <summary><c>--supply-check[=sek]</c> — schreibt die Netzzeile mitsamt dem
    /// Nachschubheli-Zaehler regelmaessig heraus, damit ein headless-Lauf sie
    /// lesen kann. Begruendung an der Auswertestelle.</summary>
    private float _supplyCheck, _supplyPeriod = 10f, _supplyPeriodLeft, _supplyElapsed;
    private bool _railRepairReady;

    private string _look = "";
    private bool _groupCheck;

    /// <summary>Bei welchem BILD der Bildprüfstand meldet, −1 = aus. Siehe
    /// <c>--portrait-check</c>.</summary>
    private int _portraitCheckAt = -1;
    private int _portraitFrames;
    private bool _soundCheck;
    private bool _tutorialCheck;
    private bool _coverageCheck;
    private bool _depotCheck;
    /// <summary><c>--overdraw-check</c> — welche Gebäudekachel übermalt welche
    /// Einheit und welches Gleisstück. Siehe <c>MapEntityLayer.OverdrawCheck</c>.</summary>
    private bool _overdrawCheck;
    /// <summary><c>--produce-pics</c> — hat jede Zeile der Produktion ein Bild?</summary>
    private bool _producePics;
    /// <summary><c>--hangar-check</c> — kaufen, im Hangar liegen, starten.</summary>
    private bool _hangarCheck;
    /// <summary><c>--market-check</c> — Markt, Ware, Plattenpruefung.</summary>
    private bool _marketCheck;
    /// <summary><c>--sell-check</c> — eine Einheit verkaufen und zusehen.
    /// ⚠ Er laeuft ueber TAKTE (der Abholer braucht Sekunden), also nicht wie
    /// die einmaligen Pruefstaende hier drueber, sondern ueber
    /// <c>MapEntityLayer.PollSellCheck</c> im SimTick.</summary>
    private bool _sellCheck;
    /// <summary><c>--shop-check</c> — legt der Laden nach, und das Richtige?
    /// Ebenfalls taktgetrieben: er wartet die Phase <c>%100 == 77</c> ab,
    /// statt den Nachschub aufzurufen.</summary>
    private bool _shopCheckFlag;
    /// <summary><c>--buy-check</c> — kaufen und der Ware beim Ankommen zusehen.
    /// ⚠ Braucht Ware im Regal: auf einer frischen Karte muss der Nachschub
    /// erst laufen (2 s), also mit <c>--quit-after</c> &gt; 3 starten.</summary>
    private bool _buyCheckFlag;
    /// <summary><c>--dock-check</c> — steht das Schiff erst im Dock und laeuft
    /// dann aus? Er stellt BEIDE Faelle her: erst ohne Raeumen (es muss warten),
    /// dann mit. Am besten auf <c>map_DM_4</c>, wo die Ausfahrten von Haus aus
    /// die belegten Liegeplaetze sind.</summary>
    private bool _dockCheckFlag;
    /// <summary><c>--power-check</c> — bremst Strommangel die Fertigung, und
    /// zwar anteilig? Er stellt den Mangel HER (er nimmt dem Spieler die
    /// Generatoren) und misst gegen eine Vorhersage, nicht gegen sich
    /// selbst.</summary>
    private bool _powerCheckFlag;
    /// <summary><c>--radar-check</c> — setzt eine Einheit mit Radarstab einen
    /// Mast, und oeffnet der Mast wirklich Sicht? Er misst die beobachteten
    /// Zellen vor und nach dem Setzen.</summary>
    private bool _radarCheckFlag;
    /// <summary><c>--bau-check</c> — setzt ein Gebaeude-Techniker ein Depot, und
    /// zwar ERST BEI DER ANKUNFT? Siehe Simulation/BuildOrdersCheck.cs.</summary>
    private bool _bauCheckFlag;
    /// <summary>Welcher der drei — 5 Depot, 6 Mine, 7 Generator. ⚠ Ein Lauf, ein
    /// Auftrag: der zweite Fall duerfte sonst auf dem Zustand des ersten
    /// messen.</summary>
    private int _bauCheckOrder = 5;
    /// <summary><c>--ausbau-check</c> — tut der Knopf »Lagerausbau« wirklich
    /// etwas? Siehe Simulation/UpgradeCheck.cs.</summary>
    private bool _ausbauCheckFlag;

    /// <summary><c>--mechaniker-check</c> — repariert der Mechaniker die
    /// Nachbarzellen? Siehe Simulation/MechanicCheck.cs.</summary>
    private bool _mechCheckFlag;

    /// <summary><c>--flug-check</c> — was tut ein gekauftes Kampfflugzeug?
    /// Siehe Simulation/AirControlCheck.cs.</summary>
    private bool _flugCheckFlag;

    /// <summary><c>--schiff-waffe-check</c> — wo verlaesst ein Schuss ein
    /// Schiff? Siehe Simulation/ShipWeaponCheck.cs.</summary>
    private bool _schiffCheckFlag;
    /// <summary><c>--knopf-check</c> — stehen »Abbrechen« und »Starten« da,
    /// wenn es etwas zu tun gibt, und bleiben sie sonst weg?</summary>
    private bool _knopfCheckFlag;
    /// <summary><c>--m21-check</c> — gewinnt Mission 21, wenn der Spieler die
    /// neun Bahnverbindungen haelt? Siehe Simulation/RailLinkCheck.cs.</summary>
    private bool _m21CheckFlag;
    /// <summary><c>--ring-check[=takte]</c> — »orange Ringe ohne Körper«. 0 = aus.
    /// ⚠ Er braucht Takte: die Meldung spricht von Stellen GEFALLENER, und die
    /// gibt es im ersten Takt noch nicht.</summary>
    private int _ringCheckTicks;
    /// <summary><c>--depot-flow</c> — bestellen, im Depot liegen, aussenden.</summary>
    private bool _depotFlow;
    /// <summary><c>--wagon-facing-check</c> — zeigt jeder Waggon in die Richtung
    /// seines Gleises? Siehe <c>MapEntityLayer.WagonFacingCheck</c>.</summary>
    private bool _wagonFacingCheck;
    private bool _infDeathCheck;
    private int _buildPreview;

    // ---- preview helper -----------------------------------------------------
    // Run with:  Godot --path <proj> res://Scenes/Gameplay/MapViewer.tscn --
    //            --map=map_05 --demo --shot=C:/tmp/shot.png --shot-delay=240
    // Drives a scripted move order and writes a screenshot, so a change can be
    // verified visually without clicking through the viewer by hand.
    /// <summary>Ist die Kamera des <c>--shot-when</c>-Auslösers schon einen
    /// ganzen Frame lang an ihrem Ziel? Siehe die Begründung am Auslöser: eine
    /// im selben Bild gesetzte Kameralage wirkt erst im nächsten.</summary>
    private bool _shotArmed;

    private string _shotPath = "";
    private int _shotDelay = 180;
    private int _frames;
    private bool _demo;
    private bool _demoNaval;
    private bool _demoFight;
    private int _selectForShot = -1;   // --select=<n>, for photographing the panel
    // --select-building[=<n>]: das n-te GEBÄUDE anwaehlen. Gebraucht fuer den
    // Beleg, dass der Bedienblock einem Gebaeude kein Bild gibt — siehe
    // PortraitBank.BuildingTrouble.
    private bool _selectBuildingForShot;
    private int _selectTypeForShot = -1;  // --select-type=<unit_type>, engt sie ein
    private bool _demoBuild;
    private bool _demoMine;
    private bool _demoRailGap;
    private bool _demoInfDeath;
    private bool _demoBehind;
    private bool _demoAuswahl;
    private bool _demoFront;
    private bool _demoResearch;
    // --queue-check (Fehler C8): Bestellzeitpunkt, Abrechnungszeitpunkt, Anzahl.
    private float _queueCheckAt, _queueCheckDue;
    private float _supplyReloadAt, _supplyReloadDue;
    private float _railZigzagAt;
    private float _railGapAt;
    private float _doorCheckAt;
    private float _capEnemyAt, _capEnemyDue;
    private int _queueCheckN = 3;
    private bool _demoState;
    private bool _demoAir;
    private bool _demoAirPic;

    /// <summary>`--demo-infpic` — einen Fusssoldaten anklicken und das Bild der
    /// Folge 403 unten links zeigen. Das Gegenstück zu `--demo-airpic`.</summary>
    private bool _demoInfPic;
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
    private float _endWindowDemo;    // > 0: Abschlussfenster nach n Sekunden zeigen
    private bool _designWindowDemo;  // --design-window: Erstellung offen lassen
    private float _fightDist;
    private bool _navOverlay;
    private bool _buildingOverlay;
    private bool _railOverlay;

    private void ParseCmdline()
    {
        // Core.CommandLine statt OS.GetCmdlineUserArgs(): ohne den Trenner »--«
        // gibt Godot einer ausgelieferten Fassung gar keinen Schalter weiter,
        // und dann greift hier keine einzige Prueffahne. Siehe Core/CommandLine.cs.
        foreach (string a in Core.CommandLine.Args)
        {
            if (a.StartsWith("--shot=")) _shotPath = a[7..];
            else if (a.StartsWith("--shot-delay=")) _shotDelay = a[13..].ToInt();
            else if (a.StartsWith("--rail-tour="))
            {
                var q = a["--rail-tour=".Length..].Split(',');
                _tourPrefix = q[0];
                if (q.Length >= 2) _tourZoom = Mathf.Max(0.2f, q[1].ToFloat());
            }
            else if (a == "--dreh-alt") MapEntityLayer.DrehAlt = true;
            else if (a.StartsWith("--shot-when="))
            {
                var q = a["--shot-when=".Length..].Split(',');
                _shotWhen = q[0];
                if (q.Length >= 2) _shotWhenN = Mathf.Max(2, q[1].ToInt());
            }
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
            // das Flugzeugbild im Bedienblock: ein Flugzeug angewaehlt
            else if (a == "--demo-airpic") { _demo = true; _demoAirPic = true; }
            else if (a == "--demo-infpic") { _demo = true; _demoInfPic = true; }
            else if (a == "--demo-inf") { _demo = true; _demoInf = true; }
            else if (a == "--demo-crush") { _demo = true; _demoCrush = true; }
            else if (a == "--demo-supply") { _demo = true; _demoSupply = true; }
            // Schreibt je Takt Flugrichtung gegen Blickrichtung der
            // Versorgungshelis mit — siehe MapEntityLayer.AirFacingTrace.
            else if (a == "--air-facing-check") MapEntityLayer.AirFacingTrace = true;
            else if (a == "--demo-capture") { _demo = true; _demoCapture = true; }
            else if (a == "--demo-takeover") { _demo = true; _demoTakeover = true; }
            else if (a == "--demo-buildpanel") { _demo = true; _demoBuildPanel = true; }
            else if (a == "--demo-buy") { _demo = true; _demoBuy = true; }
            else if (a == "--demo-ship") { _demo = true; _demoShip = true; }
            else if (a == "--demo-train") { _demo = true; _demoTrain = true; }
            else if (a == "--demo-design") { _demo = true; _demoDesign = true; }
            else if (a == "--demo-queue") { _demo = true; _demoQueue = true; }
            else if (a == "--demo-ai") { _demo = true; _demoAi = true; }
            else if (a.StartsWith("--script-check")) _scriptCheck = 15f;
            // --pay-check[=sek]: die Geldregeln der Mission einmal ausloesen,
            // damit sich der Fall »Nebenmission GEMACHT« fahren laesst. Siehe
            // Campaign.MissionScript.ForceMoneyRules.
            else if (a == "--pay-check") _payCheck = 3f;
            else if (a.StartsWith("--pay-check=")) _payCheck = a["--pay-check=".Length..].ToFloat();
            // --tick-check[=<sekunden>]: der Takt des Missionsskripts und was er
            // in den ersten Sekunden von selbst ausloest. Siehe
            // MapEntityLayer.TickCheck.
            else if (a.StartsWith("--tick-check"))
                _tickCheck = a.Contains('=') ? a[(a.IndexOf('=') + 1)..].ToFloat() : 10f;
            // --hud-check[=<sek>]: was die STEHENDE Rohstoffleiste anzeigt, im
            // selben Takt wie --econ-check. Beide zusammen sind die Gegenprobe:
            // oben die Summe und der Zuwachs, darunter die einzelnen Lager, aus
            // denen die Summe kommt. Siehe UI/GameHud.WatchLine.
            else if (a == "--hud-check" || a.StartsWith("--hud-check="))
            {
                _hudPeriod = a.Contains('=')
                    ? Mathf.Max(0.05f, a[(a.IndexOf('=') + 1)..].ToFloat()) : 1f;
                _hudCheck = 0.001f;
            }
            else if (a == "--store-check") _storeCheck = 0.001f;
            // --econ-check[=<sek>]: der Vorrat, den das Bedienfeld einer Fabrik
            // zeigt, im gewaehlten Takt. Siehe MapEntityLayer.EconCheckLine.
            else if (a == "--econ-check" || a.StartsWith("--econ-check="))
            {
                _econPeriod = a.Contains('=')
                    ? Mathf.Max(0.05f, a[(a.IndexOf('=') + 1)..].ToFloat()) : 1f;
                _econCheck = 0.001f;
            }
            // --store-check=<sek>: derselbe Bericht, aber in einem Takt, den man
            // waehlen kann. Ein Vorrat, der zwischen zwei Werten PENDELT, ist
            // mit fuenf Sekunden Abstand nicht von einem stehenden zu
            // unterscheiden — genau das war am 11.08.2026 die Meldung.
            else if (a.StartsWith("--store-check="))
            {
                _storePeriod = Mathf.Max(0.05f, a["--store-check=".Length..].ToFloat());
                _storeCheck = 0.001f;
            }
            // --skirmish[=<karte>]: ein GEFECHT ohne den Weg ueber das Menue.
            // Ohne ihn liesse sich der Gefechtsmodus gar nicht mit einem
            // Prueflauf messen — --map= laedt nur die Karte, setzt aber
            // SkirmishSetup.Active NICHT und startet also keine Wirtschaft mit
            // Startvorrat, keine KI und keinen Spieler.
            else if (a == "--skirmish" || a.StartsWith("--skirmish="))
            {
                _skirmish = true;
                // ⚠ 11.08.2026 — hier wurde NICHT am Komma getrennt. Bei
                // `--skirmish=map_NET07,2,hard` landete die ganze Zeichenkette
                // im Kartennamen, die Suche in MapNames ergab -1, der Index
                // blieb 0 — gespielt wurde map_01, und dabei ueberschrieb der
                // Rueckfall auch noch die richtige Karte, die das Menue schon
                // gesetzt hatte. Still, ohne Fehlerzeile.
                //
                // Das entwertet jeden Prueflauf, der ueber `--skirmish=<karte>`
                // eine BESTIMMTE Karte meint: er misst dann auf map_01. Genau
                // diese Sorte stiller Kartenvertauschung hat am 02.08.2026
                // schon einmal Messungen wertlos gemacht.
                if (a.Contains('='))
                    _skirmishMap = a[(a.IndexOf('=') + 1)..].Split(',')[0];
            }
            // »Alle Einheiten« von der Befehlszeile, damit die Option nicht nur
            // ueber den Menuehaken zu erreichen ist und ein Prueflauf sie
            // ein- UND ausschalten kann. Siehe UI.SkirmishSetup.AllUnits.
            else if (a == "--all-units") UI.SkirmishSetup.AllUnits = true;
            else if (a == "--all-units=0") UI.SkirmishSetup.AllUnits = false;
            else if (a == "--rail-hit-check") _railHitCheck = true;
            // --rail-repair-check[=<sek>]: die REPARATURKETTE. Stellt die Lage
            // selbst her (drei Stuecke zerschiessen, ein Fahrzeug mit Aufsatz 73
            // daraufstellen) und misst nach der Zeit, was in der Karte steht.
            // Ohne Zahl 30 Sekunden — eine Reparatur dauert 20 Takte, und die
            // Kette soll mehrere schaffen.
            else if (a == "--supply-check") _supplyCheck = 60f;
            else if (a.StartsWith("--supply-check="))
                _supplyCheck = Mathf.Max(1f, a["--supply-check=".Length..].ToFloat());
            else if (a.StartsWith("--supply-period="))
                _supplyPeriod = Mathf.Max(0.5f, a["--supply-period=".Length..].ToFloat());
            else if (a == "--rail-repair-check") _railRepairCheck = 30f;
            else if (a.StartsWith("--rail-repair-check="))
                _railRepairCheck = Mathf.Max(1f, a["--rail-repair-check=".Length..].ToFloat());
            else if (a == "--rail-check") { _railCheck = 1f; _railHead = true; }
            else if (a.StartsWith("--rail-check="))
            { _railCheck = 0.001f; _railHead = true;
              _railPeriod = Mathf.Max(0.05f, a["--rail-check=".Length..].ToFloat()); }
            // --cheats schaltet alle drei, --cheat=god,ammo,fuel einzelne.
            // Fuer den Pruefstand, und damit ein Lauf reproduzierbar bleibt.
            else if (a == "--cheats")
                MapEntityLayer.CheatGodMode = MapEntityLayer.CheatAmmo =
                    MapEntityLayer.CheatFuel = true;
            else if (a.StartsWith("--cheat="))
                foreach (string w in a["--cheat=".Length..].Split(','))
                    switch (w.Trim().ToLowerInvariant())
                    {
                        case "god": case "gott": MapEntityLayer.CheatGodMode = true; break;
                        case "ammo": case "munition": MapEntityLayer.CheatAmmo = true; break;
                        case "fuel": case "sprit": MapEntityLayer.CheatFuel = true; break;
                    }
            else if (a == "--damage-check") _damageCheck = 2f;
            else if (a == "--hit-check") _hitCheck = 2f;
            // ⚠ Holt die zwei technischen Kopfzeilen des HUD zurueck, die im
            // Spiel seit dem 14.08.2026 wegbleiben (siehe UpdateHud). Ein
            // Prueflauf, der die Rastergroesse oder den Kachelsatz belegen
            // will, setzt ihn.
            else if (a == "--hud-debug") HudDebug = true;
            // Messgeraet zur Blickrichtung der Flugzeuge, siehe
            // MapEntityLayer.AirFacingOffset — verschiebt die Bildnummer, damit
            // zwei Lesarten im selben Lauf nebeneinander stehen koennen.
            else if (a.StartsWith("--air-facing-offset="))
                MapEntityLayer.AirFacingOffset = a["--air-facing-offset=".Length..].ToInt();
            else if (a == "--cheat-check") _cheatCheck = 2f;
            else if (a == "--air-buy-check") _airBuyCheck = 3f;
            else if (a == "--produce-check") _produceCheck = 2f;
            else if (a == "--no-build-queue") MapEntityLayer.NoBuildQueue = true;
            // --supply-reload-check (Fehler C5): leeren Heli herstellen und sehen,
            // ob er einen Nachladeplatz findet. Siehe SupplyReloadCheckLine.
            else if (a == "--supply-reload-check") _supplyReloadAt = 3f;
            // --rail-zigzag (Fehler C17): die gezeichnete Schiene gegen den
            // gefahrenen Weg, je Knick einzeln. Siehe RailZigzagLine.
            else if (a == "--rail-zigzag") _railZigzagAt = 3f;
            // --rail-gap-check (C14): Abstand des letzten Gleisstuecks zum Gebaeude.
            else if (a == "--rail-gap-check") _railGapAt = 3f;
            // --door-check (Fehler C9/C11): kommt man an die Tueren heran?
            else if (a == "--door-check") _doorCheckAt = 3f;
            // --capture-enemy-check (C9/C11): ein FEINDLICHES Gebaeude einnehmen.
            else if (a == "--capture-enemy-check") _capEnemyAt = 3f;
            else if (a == "--capture-by-attack") MapEntityLayer.CaptureByAttack = true;
            // --no-rail-lift: die Gegenprobe zu C17 — der Weg nimmt wieder die
            // Hoehe der gerundeten Zelle statt die des Gleisbildes.
            else if (a == "--no-rail-lift") MapEntityLayer.RailNoLift = true;
            // --no-unit-occlusion (C23): Gegenprobe, jede Einheit ueber jedem Gebaeude.
            else if (a == "--no-unit-occlusion") MapEntityLayer.NoUnitOcclusion = true;
            // --no-step-out: das frische Fahrzeug bleibt in der Tuer stehen.
            else if (a == "--no-step-out") MapEntityLayer.NoStepOutOfDoor = true;
            // --no-clear-idle (C24): Truppen unbeteiligter Plaetze stehen lassen.
            else if (a == "--no-clear-idle") MapEntityLayer.NoClearIdleSlots = true;
            else if (a == "--no-building-body") MapEntityLayer.NoBuildingBody = true;
            // --demo-behind (C23): eine Einheit MITTEN in einen Gebaeudegrundriss
            // setzen und die Kamera daraufsetzen, damit der Bildvergleich den Fall
            // ueberhaupt enthaelt. Siehe BehindCheckSetup.
            else if (a == "--demo-behind") { _demo = true; _demoBehind = true; }
            // eine Einheit ANWAEHLEN, damit die Auswahlmarken aufs Bild kommen
            else if (a == "--demo-auswahl") { _demo = true; _demoAuswahl = true; }
            // Das Gegenstueck: eine Einheit VOR ein Gebaeude (Fehlerliste D).
            else if (a == "--demo-front") { _demo = true; _demoFront = true; }
            // --demo-infdeath (C13): eine Traube Fusssoldaten toeten, jeden zweiten.
            else if (a == "--demo-infdeath") { _demo = true; _demoInfDeath = true; }
            else if (a == "--demo-railgap") { _demo = true; _demoRailGap = true; }
            // --queue-check[=n]: n Einheiten in EINEM Zug bestellen und danach
            // nachrechnen, ob bezahlt und geliefert zusammenpassen. Siehe
            // MapEntityLayer.QueueCheckOrder — der eigentliche Fehler C8 war
            // »bezahlt drei, geliefert eins«, und das sieht nur ein Prüfstand,
            // der BEIDE Zahlen führt.
            else if (a == "--queue-check") { _queueCheckAt = 2f; _queueCheckN = 3; }
            else if (a.StartsWith("--queue-check="))
            { _queueCheckAt = 2f; _queueCheckN = Mathf.Max(1, a["--queue-check=".Length..].ToInt()); }
            // --fresh-campaign: den Kampagnenspielstand (user://campaign.cfg)
            // VOR dem Missionsstart auf 0 setzen. Ohne ihn traegt jeder
            // Prueflauf den Kontostand der vorigen mit, und eine Geldmessung
            // misst dann die vorigen Laeufe.
            else if (a == "--fresh-campaign") _freshCampaign = true;
            // --terra-check: die Rohstoffvorkommen der Mission und ob auf ihnen
            // eine Feld-Rohstoffmine stehen kann. Siehe
            // MapEntityLayer.TerraCheckLine.
            else if (a == "--terra-check") _terraCheck = true;
            // --ship-check: was ein Schiff belegt und was es belegen muesste.
            // Siehe MapEntityLayer.ShipCheckLine (Simulation/ShipCheck.cs).
            else if (a == "--ship-check") _shipCheck = true;
            // ⚠ --ship-check=<sek>: MIT Zahl laeuft das Spiel erst so lange und
            // prueft DANN. Ohne Zahl prueft es beim Laden und beendet — das
            // sieht die Aufstellung der Karte, aber nie, was nach BEWEGUNG aus
            // dem Rumpfstempel wird. Und genau dort entscheidet sich, ob
            // SetOccupant und ClearOccupant dieselbe Flaeche anfassen.
            else if (a.StartsWith("--ship-check="))
                _shipCheckAfter = Mathf.Max(0.1f, a["--ship-check=".Length..].ToFloat());
            // --place-check[=<sek>]: die Einsetzungen (place_unit @0x4D0810)
            // einer Mission an ihrer Messlatte. Siehe
            // MapEntityLayer.PlaceCheckLine. Ohne Zahl 15 Sekunden — lange
            // genug, dass ein Zaehler, der die Einsetzung anstoesst, durch ist.
            else if (a == "--place-check") _placeCheck = 15f;
            // --place-force: die Bedingungen der einsetzenden Regeln herstellen
            // und dann laufen lassen. Siehe MapEntityLayer.PlaceForceLine.
            else if (a == "--place-force") _placeForce = 3f;
            // --stock-check (B6): bewirkt die Einnahme eines Gebaeudes etwas?
            else if (a == "--stock-check") _stockCheck = 3f;
            // B6-Gegenprobe: die Wirkung der bestueckenden Regeln wieder
            // leer lassen (Stand vor dem 16.08.2026). --stock-check MUSS
            // damit durchfallen, sonst misst er nicht, was er behauptet.
            else if (a == "--no-stock") Campaign.MissionScript.StockOld = true;
            else if (a.StartsWith("--place-check="))
                _placeCheck = Mathf.Max(0.05f, a["--place-check=".Length..].ToFloat());
            else if (a == "--demo-groups") { _demo = true; _demoGroups = true; }
            // Prüfstand für das Abschlussfenster: es geht nach n Sekunden auf,
            // damit man es photographieren kann, ohne eine ganze Mission
            // durchzuspielen. --end-window=<sek>, ohne Zahl 2 Sekunden.
            // --design-window: den Entwurfsdialog aufmachen und OFFEN lassen.
            // --demo-design schliesst ihn am Ende wieder, taugt also nicht zum
            // Fotografieren.
            else if (a == "--design-window") _designWindowDemo = true;
            else if (a == "--end-window") _endWindowDemo = 2f;
            else if (a.StartsWith("--end-window=")) _endWindowDemo = a["--end-window=".Length..].ToFloat();
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
            // ⚠ --banim-check braucht LAUFZEIT: die halbe Prüfung tastet das
            // laufende Bild ab. Ohne --quit-after meldet sie nur den statischen
            // Teil. Ein Fenster braucht sie NICHT — sie zählt Kachelcodes,
            // keine Pixel.
            else if (a == "--banim-check") _bAnimCheck = true;
            else if (a == "--banim-demo") _bAnimDemo = true;
            else if (a == "--inf-anim-check") { _infAnimCheck = true; _demo = true; _demoInf = true; }
            // ⚠ NOT --demo-inf here: that one hands the selection to the foot
            // soldiers. The vehicle check issues its own drive order.
            else if (a == "--veh-anim-check") { _vehAnimCheck = true; _demo = true; }
            else if (a.StartsWith("--look=")) _look = a["--look=".Length..];
            else if (a == "--group-check") _groupCheck = true;
            // Der Prüfstand zu B9/B10 — was überlebt den Weg zurück ins Menue?
            // Er stellt die Fehlerklasse HER (Popup auf, Bearbeitungsmodus an)
            // und geht dann den ECHTEN Ausstieg über ChangeSceneToFile; gezählt
            // wird drüben in MainMenu._Ready. `=alt` stellt über
            // Core.LeaveToMenu.Skip die Fassung vor dem 15.08.2026 im selben
            // Programm nach und MUSS durchfallen — sonst wäre nicht zu sehen,
            // ob der Zähler überhaupt etwas sehen kann. Braucht --quit-after.
            // B2: bleibt eine Gruppe an der Engstelle liegen? Braucht LAUFZEIT
            // (--quit-after) — der Schaden entsteht erst Takte nach dem Befehl,
            // deshalb kann --group-check ihn nicht sehen. `=alt` stellt die
            // Fassung vor dem 15.08.2026 im selben Programm nach und MUSS
            // durchfallen.
            // B4-Gegenprobe: die Fahrt wie vor dem 16.08.2026 rechnen — feste
            // Bildpunktgeschwindigkeit auf die Zellmitte zu und Geroell ×1,45.
            // Muss in --speed-check einen Unterschied machen, sonst misst der
            // Pruefstand nicht, was er zu messen behauptet.
            else if (a == "--old-move-cost") Simulation.NavGrid.MoveCostOld = true;
            // Gegenprobe zur Steiglimite (UNSERE Setzung, das Original hat
            // keinen solchen Test): schliesst sie Einheiten ein?
            else if (a == "--no-climb-limit") Simulation.NavGrid.ClimbOff = true;
            // Gegenprobe zur Schwester von B2: den zweiten Versuch abschalten.
            // Muss den Fehler zurueckbringen (Einheiten ohne Weg stehen fuer
            // immer). Siehe MapEntityLayer.RetryPath.
            else if (a == "--no-path-retry") MapEntityLayer.RetryOff = true;
            // B4: Takte je Zelle, nach Bodenart und gerade/schraeg getrennt.
            // Fahrt wie --stuck-check, aber eine ANDERE Frage — und zwei Fragen
            // gehoeren nie in denselben Zaehler (Arbeitsweise I).
            else if (a == "--speed-check") { _speedCheck = true; _stuckCheck = true; }
            else if (a == "--stuck-check") _stuckCheck = true;
            else if (a == "--stuck-check=alt")
            { _stuckCheck = true; MapEntityLayer.BlockOld = true; }
            // --stuck-check=<c>,<r>[,alt] — ein AUSDRUECKLICHES Ziel, damit die
            // Frage von B1 stellbar ist: dorthin fahren, wo der Spieler es
            // schlecht fand.
            else if (a.StartsWith("--stuck-check="))
            {
                var q = a["--stuck-check=".Length..].Split(',');
                if (q.Length >= 2 && int.TryParse(q[0], out int qc) && int.TryParse(q[1], out int qr))
                {
                    _stuckCheck = true;
                    MapEntityLayer.StuckGoalWanted = new Vector2I(qc, qr);
                    if (q.Length > 2 && q[2] == "alt") MapEntityLayer.BlockOld = true;
                }
            }
            // B8-Gegenprobe: die Startbasen NICHT zuteilen (Stand vor dem
            // 15.08.2026). Muss auf einer Eroberungskarte einen Unterschied
            // machen, sonst hat die Zuteilung nichts getan.
            else if (a == "--no-min-range") MapEntityLayer.NoMinRange = true;
            else if (a == "--no-start-base") MapEntityLayer.NoStartBase = true;
            else if (a == "--leave-check") _leaveCheck = true;
            else if (a == "--leave-check=alt") { _leaveCheck = true; Core.LeaveToMenu.Skip = true; }
            // Prüfstand für die Bauteilbilder. ⚠ Er braucht LAUFZEIT: die
            // Feldgrössen und das »wieviele Bilder wurden gemalt« stehen erst
            // nach dem ersten Zeichenlauf, und die Fenster ordnen sich erst
            // nach ein paar Bildern ein. Ohne Zahl 90 Bilder.
            //
            // ⚠ Gezählt werden BILDER, nicht Sekunden, und mit `--shot` wird
            // daraus <b>genau das photographierte Bild</b> (siehe _Ready). Der
            // erste Anlauf zählte Sekunden: mit `--shot-delay=500` fiel die
            // Meldung dann still aus, und ein Prüfstand, der bei langer
            // Vorlaufzeit schweigt, ist schlimmer als keiner. Ausserdem MÜSSEN
            // die Zahlen das Bild beschreiben, das man sich ansieht — sonst
            // vergleicht man zwei verschiedene Augenblicke.
            //
            // Er wählt sich selbst eine Einheit (--select=0), damit der
            // Bedienblock etwas zu zeigen hat, und macht das Erstellungsfenster
            // auf — sonst prüft er drei leere Felder.
            else if (a == "--portrait-check" || a.StartsWith("--portrait-check="))
            {
                _portraitCheckAt = a.Length > 17
                    ? Mathf.Max(1, a["--portrait-check=".Length..].ToInt()) : 90;
                if (_selectForShot < 0) _selectForShot = 0;
                _designWindowDemo = true;
            }
            // Der Waffensitz auf dem Schiff — gemeldet am 13.08.2026 als »die
            // Waffe sitzt einige Felder daneben«. Er braucht ein paar Bilder,
            // bis die Bildpaare geladen sind.
            else if (a == "--waffensitz-check" || a.StartsWith("--waffensitz-check="))
                _turretSeatCheckAt = a.Length > 19
                    ? Mathf.Max(1, a["--waffensitz-check=".Length..].ToInt()) : 20;
            // Beruehren sich Rahmen und Bild? — dieselbe Bauart wie der
            // Waffensitz-Pruefstand, aus demselben Grund: die Bildpaare sind
            // erst nach ein paar Bildern geladen. `--stempel-check[=platz]`
            // nennt dazu EINE Einheit mit allen vier Punkten.
            else if (a == "--stempel-check" || a.StartsWith("--stempel-check="))
            {
                _stempelCheckAt = 20;
                if (a.Length > 16) _stempelSlot = a["--stempel-check=".Length..].ToInt();
            }
            else if (a == "--stempel-alt") MapEntityLayer.StempelAlt = true;
            // Gegenprobe zu C14: das Zeilenfach wieder fest auf `Zeile + 3`,
            // statt es aus der Tuer zu holen. Siehe BuildingDrawRowFor.
            else if (a == "--tuer-alt") MapEntityLayer.TuerAlt = true;
            // Das Startkonto eines Gefechts fuer Prueflaeufe -- im Spiel
            // stellt es der Regler "Konto" im Gefechtsschirm.
            else if (a.StartsWith("--konto="))
                UI.SkirmishSetup.StartMoney = a["--konto=".Length..].ToInt();
            else if (a == "--tueren-spaet") MapEntityLayer.TuerenSpaet = true;
            // Gegenprobe zu C14: auch der Gebaeudeboden laeuft wieder im
            // Zeilenfach mit — der Stand, den der Spieler photographiert hat.
            else if (a == "--boden-alt") MapEntityLayer.BodenAlt = true;
            // Gegenprobe zum Waggonbild: nur vier Richtungen, dy gewinnt.
            else if (a == "--stueck-alt") MapEntityLayer.StueckAlt = true;
            else if (a == "--wagon-facing-check") _wagonFacingCheck = true;
            else if (a == "--overdraw-check") _overdrawCheck = true;
            else if (a == "--produce-pics") _producePics = true;
            else if (a == "--hangar-check") _hangarCheck = true;
            else if (a == "--market-check") _marketCheck = true;
            else if (a == "--sell-check") _sellCheck = true;
            else if (a == "--shop-check") _shopCheckFlag = true;
            else if (a == "--buy-check") _buyCheckFlag = true;
            else if (a == "--dock-check") _dockCheckFlag = true;
            else if (a == "--power-check") _powerCheckFlag = true;
            else if (a == "--radar-check") _radarCheckFlag = true;
            else if (a == "--ausbau-check") _ausbauCheckFlag = true;
            else if (a == "--nebel-alt") MapEntityLayer.FogDimOld = true;
            else if (a == "--keine-objekt-verdeckung")
                MapEntityLayer.NoObjectOcclusion = true;
            else if (a == "--mechaniker-check") _mechCheckFlag = true;
            else if (a == "--flug-check") _flugCheckFlag = true;
            else if (a == "--schiff-waffe-check") _schiffCheckFlag = true;
            else if (a == "--knopf-check") _knopfCheckFlag = true;
            else if (a == "--m21-check") _m21CheckFlag = true;
            else if (a == "--ring-check") _ringCheckTicks = 3000;
            else if (a.StartsWith("--ring-check="))
                _ringCheckTicks = Mathf.Max(60, a["--ring-check=".Length..].ToInt());
            else if (a == "--bau-check") { _bauCheckFlag = true; _bauCheckOrder = 5; }
            else if (a.StartsWith("--bau-check="))
            {
                _bauCheckFlag = true;
                _bauCheckOrder = a[12..] switch
                {
                    "mine" => 6, "generator" => 7, _ => 5,
                };
            }
            else if (a == "--depot-flow") _depotFlow = true;
            else if (a == "--depot-flow=dock")
            { _depotFlow = true; MapEntityLayer.DepotFlowDock = true; }
            else if (a == "--sound-check") _soundCheck = true;
            else if (a == "--tutorial-check") _tutorialCheck = true;
            else if (a == "--script-coverage") _coverageCheck = true;
            else if (a == "--depot-check") _depotCheck = true;
            else if (a == "--infdeath-check") _infDeathCheck = true;
            else if (a.StartsWith("--build-preview=")) _buildPreview = a["--build-preview=".Length..].ToInt();
            else if (a == "--fog") MapEntityLayer.ForceFog = true;
            else if (a == "--buildings") _buildingOverlay = true;
            else if (a == "--rail") _railOverlay = true;
            // Prueflauf fuer die Legeart der Strecke, siehe DrawRailTrack.
            else if (a.StartsWith("--rail-lay="))
            {
                var q = a["--rail-lay=".Length..].Split(',');
                MapEntityLayer.RailProbeSkipCols = q[0] == "cols";
            }
            else if (a == "--fps60") Engine.MaxFps = 60;   // deterministic captures
            // ⚠ Die Bildrate frei setzen — das ist die Gegenprobe zum FESTEN
            // TAKT: dieselbe simulierte Zeit muss bei 30, 60 und 144 Bildern/s
            // denselben Zustand ergeben. Vor dem 15.08.2026 ergab sie drei
            // verschiedene (siehe MapEntityLayer.SimHz).
            else if (a.StartsWith("--fps=")) Engine.MaxFps = a["--fps=".Length..].ToInt();
            else if (a.StartsWith("--map="))
            {
                int idx = System.Array.IndexOf(MapNames, a[6..]);
                if (idx >= 0) _mapIndex = idx;
            }
            // --select=<n> picks the n-th unit that has a weapon or a tank, so
            // a headless run can photograph the info panel
            else if (a.StartsWith("--select=")) _selectForShot = a["--select=".Length..].ToInt();
            // --select-type=<unit_type> engt --select= auf einen Fahrwerkstyp ein
            // — gebraucht fuer das Schiffsbild, siehe MapEntityLayer.SelectForShot
            else if (a.StartsWith("--select-type="))
            {
                _selectTypeForShot = a["--select-type=".Length..].ToInt();
                if (_selectForShot < 0) _selectForShot = 0;
            }
            // --select-building[=<n>]: das n-te Gebaeude anwaehlen statt der
            // n-ten bewaffneten Einheit. Fuer das Bildschirmfoto zum Befund
            // »ein Gebaeude bekommt kein Bild« — ohne das laesst sich die
            // Abwesenheit nicht photographieren.
            else if (a == "--select-building" || a.StartsWith("--select-building="))
            {
                _selectBuildingForShot = true;
                _selectForShot = a.Length > 17
                    ? a["--select-building=".Length..].ToInt() : 0;
            }
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
                  : _demoAuswahl ? _entities.AuswahlDemoSetup()
                  : _demoBehind ? _entities.BehindCheckSetup()
                  : _demoFront ? _entities.FrontCheckSetup()
                  : _demoCrush ? _entities.DebugDemoCrush()
                  : _demoRailGap ? _entities.DebugDemoRailGap()
                  : _demoInfDeath ? _entities.DebugDemoInfDeath()
                  : _demoInf ? _entities.DebugDemoInfantry()
                  : _demoInfPic ? _entities.DebugDemoInfPortrait()
                  : _demoAirPic ? _entities.DebugDemoAirPortrait()
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
    private bool _leaveCheck;
    private bool _stuckCheck;
    private bool _speedCheck;
    private float _scriptCheck;
    private float _payCheck;

    /// <summary>`--tick-check[=<sekunden>]` — wieviele Sekunden Missionsskript
    /// der Prueflauf ohne jedes Zutun laufen laesst. 0 = aus.</summary>
    private float _tickCheck;
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

    /// <summary>`--store-check`: alle fuenf Sekunden die Teilelager melden, die
    /// die Mission beobachtet. Ein einzelner Blick am Ende wuerde nicht sagen,
    /// ob sie WACHSEN — und genau das ist Mission 5s Bedingung.</summary>
    private float _storeCheck;
    private float _storePeriod = 5f;
    private float _econCheck, _econPeriod = 1f;

    /// <summary>`--hud-check[=<sek>]`: die Zahlen der stehenden Rohstoffleiste
    /// mitschreiben, damit sich der ANGEZEIGTE Zuwachs gegen die Lager aus
    /// <c>--econ-check</c> halten lässt.</summary>
    private float _hudCheck, _hudPeriod = 1f;
    private bool _skirmish;
    private string _skirmishMap = "";

    /// <summary>`--produce-check`: nach zwei Sekunden die Produktionskette der
    /// Mission an Spieler 0 übergeben und dann echt laufen lassen. Zwei Sekunden,
    /// damit das Skript vorher einen Takt hatte — es merkt sich das Lager erst,
    /// wenn ihm ein Gebäude der Klasse 1 gehört.</summary>
    private float _produceCheck;
    private float _placeCheck;
    private bool _freshCampaign;
    private bool _terraCheck;
    private bool _shipCheck;
    private float _shipCheckAfter;
    private float _placeForce;
    private float _stockCheck;

    /// <summary>`--damage-check`: die Schadensstufen durchfahren.</summary>
    private float _damageCheck;

    /// <summary>`--hud-debug`: die zwei technischen Kopfzeilen des HUD
    /// (Kartenname/Mission, Raster/Kachelsatz/Bildgroesse) auch im Spiel
    /// zeigen. Im Kartenbetrachter stehen sie ohnehin.</summary>
    public static bool HudDebug;

    /// <summary>`--hit-check`: die TREFFERRECHNUNG mit den Einheiten dieser
    /// Karte durchrechnen — siehe <see cref="MapEntityLayer.HitCheckLine"/>.
    /// Gebraucht, weil der Fehler vom 14.08.2026 (fehlende Schuetzenhoehe,
    /// vertauschte Felder) in keinem Zaehler sichtbar war: er aendert nur eine
    /// Zahl, die niemand ausgab.</summary>
    private float _hitCheck;

    /// <summary>`--rail-check`: das Bahnsystem beobachten. Einmal der Kopf (was
    /// die Karte an Linien hat und was die Typmatrix daraus macht), dann alle
    /// zehn Sekunden der Fahrplan und — das eigentliche Beweisstück — <b>wie
    /// sich die Lager der Zielgebäude ÜBER DIE ZEIT ändern</b>. Eine einzelne
    /// Momentaufnahme koennte nicht zeigen, ob etwas ankommt.</summary>
    private float _railCheck;
    private bool _railHead;
    private float _railPeriod = 10f;

    /// <summary>`--cheat-check`: die drei Schummelschalter ausüben.</summary>
    private float _cheatCheck;

    /// <summary>`--air-buy-check`: Flugzeugkauf am Flughafen ausüben.</summary>
    private float _airBuyCheck;

    private void QuitIfDue(double delta)
    {
        if (_infAnimCheck) _entities.InfAnimSample();
        if (_vehAnimCheck) _entities.VehAnimSample();
        if (_bAnimCheck) _entities.BAnimSample();
        if (_airBuyCheck > 0f)
        {
            _airBuyCheck -= (float)delta;
            if (_airBuyCheck <= 0f)
            {
                _airBuyCheck = -1f;
                GD.Print(_entities.AirBuyCheckLine());
            }
        }
        if (_cheatCheck > 0f)
        {
            _cheatCheck -= (float)delta;
            if (_cheatCheck <= 0f)
            {
                _cheatCheck = -1f;
                GD.Print(_entities.CheatCheckLine());
            }
        }
        if (_damageCheck > 0f)
        {
            _damageCheck -= (float)delta;
            if (_damageCheck <= 0f)
            {
                _damageCheck = -1f;
                GD.Print(_entities.DamageCheckLine());
            }
        }
        if (_hitCheck > 0f)
        {
            _hitCheck -= (float)delta;
            if (_hitCheck <= 0f)
            {
                _hitCheck = -1f;
                GD.Print(_entities.HitCheckLine());
            }
        }
        if (_railRepairCheck > 0f)
        {
            if (!_railRepairReady)
            {
                _railRepairReady = true;
                var at = _entities.RailRepairSetup();
                if (at is { } p2)
                {
                    _camera.Zoom = new Vector2(3f, 3f);
                    _camera.Position = p2;
                }
            }
            _railRepairCheck -= (float)delta;
            if (_railRepairCheck <= 0f)
            {
                _railRepairCheck = -1f;
                GD.Print(_entities.RailRepairLine());
            }
        }
        // ⚠ WARUM ES DIESEN SCHALTER GEBEN MUSS. Der Zaehler zu den
        // Nachschubhelis stand nur in der Bildschirmleiste — headless war er
        // nicht zu lesen, und damit war die Heimkehr, die er beweisen soll,
        // ohne Beleg. Genau der Fall, den die Arbeitsweise verbietet: eine
        // Aenderung, die uebersetzt, aber nichts zeigt. Hier wird KEINE zweite
        // Zaehlung geschrieben, sondern dieselbe Zeile ausgegeben, die auch am
        // Bildschirm steht — sonst koennten die zwei auseinanderlaufen.
        if (_supplyCheck > 0f)
        {
            _supplyPeriodLeft -= (float)delta;
            _supplyCheck -= (float)delta;
            _supplyElapsed += (float)delta;
            if (_supplyPeriodLeft <= 0f)
            {
                _supplyPeriodLeft = _supplyPeriod;
                GD.Print($"[{Mathf.RoundToInt(_supplyElapsed)}s] supply: {_entities.NetworkLine()}");
            }
        }
        if (_shipCheckAfter > 0f)
        {
            _shipCheckAfter -= (float)delta;
            if (_shipCheckAfter <= 0f)
            {
                _shipCheckAfter = -1f;
                GD.Print(_entities.ShipCheckLine());
            }
        }
        if (_produceCheck > 0f)
        {
            _produceCheck -= (float)delta;
            if (_produceCheck <= 0f)
            {
                _produceCheck = -1f;
                GD.Print(_entities.ProduceCheckLine());
            }
        }
        // --queue-check: erst bestellen, dann warten, dann abrechnen. Die zwei
        // Zeitpunkte MUESSEN auseinanderliegen — eine Schlange, die sofort
        // abgerechnet wird, hat noch nichts abgearbeitet, und der Prueflauf
        // haette »0 von 3 angekommen« gemeldet und dabei die Wartezeit gemessen
        // statt die Schlange.
        if (_queueCheckAt > 0f)
        {
            _queueCheckAt -= (float)delta;
            if (_queueCheckAt <= 0f)
            {
                _queueCheckAt = -1f;
                _entities.QueueCheckOrder(_queueCheckN);
                // reichlich Luft: BuildSeconds je Stueck plus Zuschlag fuer eine
                // Basis, die kurz keine freie Zelle findet
                _queueCheckDue = _queueCheckN * 6f + 8f;
                GD.Print($"queue-check: {_queueCheckN} bestellt, Abrechnung in " +
                         $"{_queueCheckDue:0.0}s");
            }
        }
        else if (_queueCheckDue > 0f)
        {
            _queueCheckDue -= (float)delta;
            if (_queueCheckDue <= 0f)
            {
                _queueCheckDue = -1f;
                GD.Print(_entities.QueueCheckLine());
            }
        }
        // --supply-reload-check: erst den leeren Heli herstellen, dann ihm Zeit
        // zum Hinfliegen geben, dann abrechnen. Die Wartezeit ist grosszuegig —
        // ein Flughafen kann am anderen Kartenrand liegen.
        if (_railZigzagAt > 0f)
        {
            _railZigzagAt -= (float)delta;
            if (_railZigzagAt <= 0f)
            {
                _railZigzagAt = -1f;
                GD.Print(_entities.RailZigzagLine());
            }
        }
        if (_railGapAt > 0f)
        {
            _railGapAt -= (float)delta;
            if (_railGapAt <= 0f)
            {
                _railGapAt = -1f;
                GD.Print(_entities.RailGapCheckLine());
            }
        }
        if (_doorCheckAt > 0f)
        {
            _doorCheckAt -= (float)delta;
            if (_doorCheckAt <= 0f)
            {
                _doorCheckAt = -1f;
                GD.Print(_entities.DoorCheckLine());
            }
        }
        // Erst befehlen, dann fahren lassen, dann abrechnen. Die Einnahme
        // braucht so viele Takte, wie das Gebaeude Trefferpunkte hat — bei
        // TickScale 16 sind das fuer eine 1000er-Fabrik rund eine Minute.
        if (_capEnemyAt > 0f)
        {
            _capEnemyAt -= (float)delta;
            if (_capEnemyAt <= 0f)
            {
                _capEnemyAt = -1f;
                GD.Print(_entities.CaptureEnemyOrder());
                _capEnemyDue = 90f;
            }
        }
        else if (_capEnemyDue > 0f)
        {
            _capEnemyDue -= (float)delta;
            if (_capEnemyDue <= 0f)
            {
                _capEnemyDue = -1f;
                GD.Print(_entities.CaptureEnemyResult());
            }
        }
        if (_supplyReloadAt > 0f)
        {
            _supplyReloadAt -= (float)delta;
            if (_supplyReloadAt <= 0f)
            {
                _supplyReloadAt = -1f;
                GD.Print(_entities.SupplyReloadCheckLine());
                _supplyReloadDue = 60f;
            }
        }
        else if (_supplyReloadDue > 0f)
        {
            _supplyReloadDue -= (float)delta;
            if (_supplyReloadDue <= 0f)
            {
                _supplyReloadDue = -1f;
                GD.Print(_entities.SupplyReloadResult());
            }
        }
        if (_placeForce > 0f)
        {
            _placeForce -= (float)delta;
            if (_placeForce <= 0f)
            {
                _placeForce = -1f;
                GD.Print(_entities.PlaceForceLine());
            }
        }
        // --stock-check (B6): die Bedingungen der bestueckenden Regeln stellen
        // und nachsehen, ob die Lager danach ihren Sollwert tragen.
        if (_stockCheck > 0f)
        {
            _stockCheck -= (float)delta;
            if (_stockCheck <= 0f)
            {
                _stockCheck = -1f;
                GD.Print(_entities.StockCheckLine());
                GetTree().Quit(_entities.StockCheckRc());
                return;
            }
        }
        if (_placeCheck > 0f)
        {
            _placeCheck -= (float)delta;
            if (_placeCheck <= 0f)
            {
                _placeCheck = -1f;
                GD.Print(_entities.PlaceCheckLine());
            }
        }
        if (_railCheck > 0f)
        {
            _railCheck -= (float)delta;
            if (_railCheck <= 0f)
            {
                _railCheck = _railPeriod;
                if (_railHead) { _railHead = false; GD.Print(_entities.RailCheckHead()); }
                GD.Print($"[{_upTime:0}s] " + _entities.RailCheckLine());
            }
        }
        if (_storeCheck > 0f)
        {
            _storeCheck -= (float)delta;
            if (_storeCheck <= 0f)
            {
                _storeCheck = _storePeriod;
                GD.Print($"[{_upTime:0}s] " + _entities.StoreCheckLine());
            }
        }
        if (_econCheck > 0f)
        {
            _econCheck -= (float)delta;
            if (_econCheck <= 0f)
            {
                _econCheck = _econPeriod;
                GD.Print($"[{_upTime:0}s] " + _entities.EconCheckLine());
            }
        }
        if (_hudCheck > 0f)
        {
            _hudCheck -= (float)delta;
            if (_hudCheck <= 0f)
            {
                _hudCheck = _hudPeriod;
                // ⚠ Die SIMULATIONSSEKUNDE dazu, nicht nur die Uhrzeit des
                // Rechners: die Rate wird über die Simulationszeit gemittelt,
                // und ohne sie liesse sich die Zahl nicht nachrechnen.
                GD.Print($"[{_upTime:0}s sim={_entities.DebugClock:0.0}s] " +
                         (_resourceBar?.WatchLine() ?? "hud: nicht gebaut"));
            }
        }
        if (_scriptCheck > 0f)
        {
            _scriptCheck -= (float)delta;
            if (_scriptCheck <= 0f)
            {
                _scriptCheck = -1f;
                GD.Print(_entities.MissionScriptForceCheck());
            }
        }
        if (_payCheck > 0f)
        {
            _payCheck -= (float)delta;
            if (_payCheck <= 0f)
            {
                _payCheck = -1f;
                GD.Print(Campaign.MissionScript.Current?.ForceMoneyRules()
                         ?? "pay-check: diese Mission hat kein Skript");
            }
        }
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
            string plan = _entities.AiPlanLine();
            if (plan.Length > 0) GD.Print(plan);
            string ms = _entities.MissionScriptLine();
            if (ms.Length > 0) GD.Print(ms);
            string sw = _entities.ShipWatchLine();
            if (sw.Length > 0) GD.Print(sw);
            string cr = _entities.CrushReport();
            if (cr.Length > 0) GD.Print(cr);
            GD.Print(_entities.CaptureWatchLine());
            GD.Print(_entities.TakeoverWatchLine());
            // ⚠ Die Stromzeile gehoert in JEDEN Lauf, nicht nur in --power-check:
            // die zwei Balken im Bedienblock sind das einzige, was der Spieler
            // davon sieht, und aus einem Bild ist ihre LAENGE nicht abzulesen
            // (Regel 22). Hier stehen die Zahlen dahinter.
            GD.Print(_entities.PowerLine());
            GD.Print(_entities.MinimapWatchLine(_minimap));
            GD.Print(_baseWindow?.WatchLine() ?? "basis-fenster: nicht gebaut");
            GD.Print(_designWindow?.WatchLine() ?? "erstellung: nicht gebaut");
            GD.Print(_entities.EventWatchLine());
            GD.Print(_entities.VoiceWatchLine());
            GD.Print(_entities.PanelWatchLine());
            GD.Print(_entities.PoseWatchLine());
            if (_infAnimCheck) GD.Print(_entities.InfAnimReport());
            if (_vehAnimCheck) GD.Print(_entities.VehAnimReport());
            if (_bAnimCheck) GD.Print(_entities.BAnimCheck());
            GD.Print(_entities.FogWatchLine());
            // ⚠ Regel 33: ohne diese Zahl ist »die Flugzeuge bewegen sich
            // jetzt« nicht von »das Bild hat sich anderswo geaendert« zu
            // unterscheiden. Siehe MapEntityLayer.AirDrift (Fehler D6).
            GD.Print(_entities.AirDriftLine());
            GD.Print(_entities.RangeWatchLine());
            if (_speedCheck)
            {
                GD.Print(_entities.SpeedCheckLine());
                GetTree().Quit(_entities.SpeedCheckRc());
                return;
            }
            if (_stuckCheck)
            {
                GD.Print(_entities.StuckCheckLine());
                GetTree().Quit(_entities.StuckCheckRc());
                return;
            }
            if (_leaveCheck) { LeaveCheckGo(); return; }
            GetTree().Quit();
        }
    }

    /// <summary>
    /// `--leave-check` — was ueberlebt den Weg zurueck ins Hauptmenue?
    ///
    /// <para>Gemeldet als B9 (»das Editorfeld ist im Gefecht noch da«) und B10
    /// (»die Popups der Mission stehen im Hauptmenue«). Beide kommen aus
    /// derselben Ursache; der Befund steht in <c>Core/LeaveToMenu.cs</c>.</para>
    ///
    /// <para>⚠ Der Prueflauf STELLT DIE FEHLERKLASSE HER, statt sie zu
    /// erschliessen (Arbeitsweise 32): er macht ein Hilfefenster auf und
    /// schaltet den Bearbeitungsmodus ein — also genau den Zustand, den eine
    /// gespielte Mission und ein Besuch im Editor hinterlassen. Danach geht er
    /// den ECHTEN Ausstieg, denselben, den der Knopf »Beenden« im Pausenmenue
    /// nimmt. Gezaehlt wird drueben in <c>MainMenu._Ready</c>, denn nur dort ist
    /// die Frage ueberhaupt stellbar.</para>
    ///
    /// <para>⚠ Und er sagt es, wenn er NICHT messen konnte: ohne
    /// <c>help.json</c> gibt es kein Fenster, das man aufmachen koennte — dann
    /// faellt die Popup-Haelfte aus, und ein gruenes Ergebnis waere hier eine
    /// Luege (Arbeitsweise 33).</para>
    /// </summary>
    private void LeaveCheckGo()
    {
        int id = -1;
        for (int i = 1; i <= 200 && id < 0; i++)
            if (UI.HelpWindow.TextOf(i) is { Count: > 0 }) id = i;
        if (id < 0)
        {
            GD.PrintErr("leave-check: kein Hilfetext vorhanden (help.json fehlt) — die " +
                        "Popup-Haelfte ist NICHT gemessen. --reexport-help=<Quelle> schreibt sie.");
            GetTree().Quit(2);
            return;
        }
        UI.HelpWindow.Show(GetTree().Root, id, 120, 120);
        Editor.MapEditSession.Active = true;
        Editor.MapEditSession.Watch(GetTree());
        GD.Print($"leave-check vorher: Popup #{id} offen ({UI.HelpWindow.OpenCount} gesamt), " +
                 "Bearbeitungsmodus AN, Waechter gesetzt" +
                 (Core.LeaveToMenu.Skip
                     ? " — GEGENPROBE =alt: es wird absichtlich NICHT aufgeraeumt"
                     : ""));
        Core.LeaveToMenu.Report = true;
        // Derselbe Ausstieg wie im Pausenmenue (siehe _pause.Quit weiter oben) —
        // ein Pruefstand, der eine eigene, kuerzere Tuer nimmt, prueft seine
        // eigene Tuer.
        UI.SkirmishSetup.Active = false;
        Audio.MidiMusic.Stop();
        GetTree().ChangeSceneToFile(UI.SkirmishSetup.MenuScene);
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
        if (_shotHooked || (_shotPath.Length == 0 && _tourPrefix.Length == 0)) return;
        _shotHooked = true;
        GetTree().ProcessFrame += TakeShotIfDue;
    }

    private bool _shotHooked;

    /// <summary>`--shot-when=squash[,n]` — nicht nach n Bildern auslösen,
    /// sondern <b>sobald n Waggons einer Linie auf EINER Zelle stehen</b>, und
    /// die Kamera dorthin stellen.
    ///
    /// <para>Warum es das braucht: die Stauchung beim Wenden dauert etwa eine
    /// Sekunde je Abfahrt und macht rund ein Zehntel der Fahrzeit aus — ein
    /// Bild auf gut Glück trifft sie in einem von zehn Fällen, und ein Fehler,
    /// den nur das Bild zeigt, ist ohne Bild nicht zu beurteilen.
    /// <c>--shot-delay</c> bleibt die Aufwärmzeit, ab der überhaupt gesucht
    /// wird.</para></summary>
    private string _shotWhen = "";
    private int _shotWhenN = 4;

    /// <summary>`--rail-tour=<c>&lt;praefix&gt;[,&lt;zoom&gt;]</c>` — die
    /// interessanten Stellen der Bahnstrecke in EINEM Lauf fotografieren, ein
    /// Bild je Bild.
    ///
    /// <para>Warum: ein Fehler an der Bahn ist nur im Bild zu sehen, und ein
    /// Bild kostete bisher einen eigenen Lauf von anderthalb Minuten samt
    /// geratener Koordinaten — von außen weiß niemand, wo auf einer 230×230
    /// grossen Karte eine Rampe oder ein Streckenende liegt. Welche Stellen es
    /// sind, entscheidet <see cref="MapEntityLayer.RailTourSpots"/> nach der
    /// FORM, nicht nach der Karte.</para></summary>
    private string _tourPrefix = "";
    private float _tourZoom = 4f;
    private System.Collections.Generic.List<(string Label, int Col, int Row)>? _tour;

    private void TakeShotIfDue()
    {
        if (_tourPrefix.Length > 0)
        {
            if (_frames++ < _shotDelay) return;
            _tour ??= _entities.RailTourSpots();
            if (_tour.Count == 0)
            {
                GD.Print("MapViewer: --rail-tour — diese Karte hat keine Bahnstrecke");
                GetTree().Quit();
                return;
            }
            var s = _tour[0];
            _tour.RemoveAt(0);
            _camera.Position = _entities.RailCellPoint(s.Col, s.Row);
            _camera.Zoom = new Vector2(_tourZoom, _tourZoom);
            ClampCamera();
            RenderingServer.ForceDraw();
            string file = $"{_tourPrefix}_{s.Label}.png";
            GetViewport().GetTexture().GetImage().SavePng(file);
            GD.Print($"MapViewer: rail-tour -> {file}   ({s.Label} auf Zelle {s.Col},{s.Row})");
            if (_tour.Count == 0) GetTree().Quit();
            return;
        }
        if (_shotPath.Length == 0 || _frames++ < _shotDelay) return;
        // ⚠⚠ 16.08.2026 — EIN BILD WARTEN, NACHDEM DIE KAMERA GESETZT WURDE.
        //
        // Alle `--shot-when`-Auslöser setzen die Kamera und schiessen im
        // SELBEN Bild. Die neue Kameralage wird aber erst im nächsten wirksam:
        // `RenderingServer.ForceDraw()` zeichnet den Baum, nicht die soeben
        // geänderte Transformation. Der Schuss zeigte deshalb immer die
        // ALTE Stelle — beim Waggonbild die Basis statt des Gleises, und weil
        // beide Läufe dieselbe falsche Stelle zeigten, meldete der Bildvergleich
        // dreimal »0 geänderte Bildpunkte«. Das las sich wie ein Freispruch und
        // war einer über den falschen Gegenstand (Regel 31).
        //
        // Erkannt an der UHR im Bild: sie stand auf 00:02, während der Auslöser
        // `sim=10,13s` meldete — das Bild war acht Sekunden alt.
        // Solange der Auslöser noch nicht »scharf« ist, wird NUR die Kamera
        // gesetzt und dann ein Bild verstreichen gelassen.
        if (_shotWhen.Length > 0 && !_shotArmed)
        {
            if (_shotWhen == "diagonal")
            {
                if (!_entities.RailWagonOnCorner(out var dat, out int dline)) return;
                _camera.Position = _entities.RailCellPoint(Mathf.RoundToInt(dat.X),
                                                           Mathf.RoundToInt(dat.Y));
                ClampCamera();
                GD.Print($"MapViewer: --shot-when=diagonal ausgeloest — Waggon auf Eckstueck " +
                         $"({dat.X:0},{dat.Y:0}), Linie {dline}");
            }
            // ⚠ Der Auslöser für das WAGGONBILD: er wartet auf eine Treppe der
            // Kette, nicht auf ein Eckstück des Gleisbildes. Siehe
            // RailFreight.RailWagonOnStairs.
            else if (_shotWhen == "treppe")
            {
                if (!_entities.RailWagonOnStairs(out var sat, out int sline)) return;
                _camera.Position = _entities.RailCellPoint(Mathf.RoundToInt(sat.X),
                                                           Mathf.RoundToInt(sat.Y));
                ClampCamera();
                GD.Print($"MapViewer: --shot-when=treppe ausgeloest — Waggon auf Treppe " +
                         $"({sat.X:0},{sat.Y:0}), Linie {sline} | {_entities.StairsWhat}");
            }
            // ⚠ Der Auslöser für das MARKTFENSTER. Er wartet, bis der Laden
            // wirklich offen ist (eine eigene Einheit steht auf einer Platte),
            // setzt die Kamera auf den Markt und lässt das übliche eine Bild
            // verstreichen. Ohne ihn ist das neu gebaute Fenster nie zu sehen.
            else if (_shotWhen == "markt")
            {
                var at = _entities.MarketShotSetup();
                if (at == null) return;
                _camera.Position = _entities.RailCellPoint(at.Value.X, at.Value.Y);
                ClampCamera();
                if (_baseWindow != null) _baseWindow.Visible = true;
                GD.Print($"MapViewer: --shot-when=markt ausgeloest — Markt auf " +
                         $"({at.Value.X},{at.Value.Y}), Fenster offen");
            }
            // ⚠ Der Auslöser für die zwei AUSBAUKNÖPFE. Sie stehen nur bei einer
            // eigenen Fabrik; ohne diesen Griff zeigt jedes Bild ein Fenster
            // ohne sie und bewiese das Gegenteil von dem, was gemeint ist.
            else if (_shotWhen == "ausbau")
            {
                var at = _entities.FactoryShotSetup();
                if (at == null) return;
                _camera.Position = _entities.RailCellPoint(at.Value.X, at.Value.Y);
                ClampCamera();
                if (_baseWindow != null) { _baseWindow.Visible = true; _baseWindow.Refresh(); }
                GD.Print("MapViewer: --shot-when=ausbau ausgeloest — " +
                         (_baseWindow?.WatchLine() ?? "kein Fenster"));
            }
            // ⚠ Der Auslöser für die BEFEHLSLEISTE der Einheit. Sie steht nur,
            // wenn etwas Verkäufliches gewählt ist — ohne diesen Griff zeigt
            // jedes Bild eine leere Karte und beweist gar nichts.
            else if (_shotWhen == "verkauf")
            {
                var at = _entities.SellShotSetup();
                if (at == null) return;
                _camera.Position = _entities.RailCellPoint(at.Value.X, at.Value.Y);
                ClampCamera();
                UpdateUnitOrderBar();
                GD.Print($"MapViewer: --shot-when=verkauf ausgeloest — Einheit auf " +
                         $"({at.Value.X},{at.Value.Y}), Leiste " +
                         $"{(_orderBar is { Visible: true } ? "steht" : "STEHT NICHT")}");
            }
            // ⚠ Die BEFEHLSLEISTE mit den Bauknoepfen bzw. dem Radarknopf.
            // Beide brauchen eine Einheit mit dem passenden Bauteil, und die
            // steht auf keiner Karte von Haus aus — der Ausloeser setzt sie und
            // sagt es. Zwei Bilder, weil eine Leiste immer nur EINE Einheit
            // bedient: der Gebaeude-Techniker kann Depot und Mine, der Radar
            // Installer setzt Masten.
            else if (_shotWhen is "bauleiste" or "radarleiste")
            {
                int teil = _shotWhen == "radarleiste"
                         ? MapEntityLayer.RadarKitWeapon
                         : MapEntityLayer.PartBuildingTech;
                var at = _entities.OrderBarShotSetup(teil);
                if (at == null) return;
                _camera.Position = _entities.RailCellPoint(at.Value.X, at.Value.Y);
                ClampCamera();
                UpdateUnitOrderBar();
                GD.Print($"MapViewer: --shot-when={_shotWhen} ausgeloest — Leiste " +
                         $"{(_orderBar is { Visible: true } ? "steht" : "STEHT NICHT")}");
            }
            // ⚠ Das Schiff, das IM DOCK wartet. Ohne diesen Griff zeigt jedes
            // Bild ein leeres Hafenbecken — der Gegenstand steht nur ein paar
            // Sekunden dort.
            else if (_shotWhen == "dock")
            {
                var at = _entities.DockShotSetup();
                if (at == null) return;
                _camera.Position = _entities.RailCellPoint(at.Value.X, at.Value.Y);
                ClampCamera();
                GD.Print($"MapViewer: --shot-when=dock ausgeloest — Schiff auf " +
                         $"({at.Value.X},{at.Value.Y})");
            }
            else if (_shotWhen == "squash")
            {
                if (_entities.RailSquashNow < _shotWhenN) return;
                var at = _entities.RailSquashAt;
                // ⚠ Hier stand `at.X * 40, at.Y * 20` — daneben, siehe
                // MapEntityLayer.RailCellPoint. Deshalb zeigte die erste Aufnahme
                // der Stauchung eine ANDERE Linie.
                _camera.Position = _entities.RailCellPoint(Mathf.RoundToInt(at.X),
                                                           Mathf.RoundToInt(at.Y));
                ClampCamera();
                GD.Print($"MapViewer: --shot-when=squash ausgeloest — {_entities.RailSquashNow} " +
                         $"Waggons auf Zelle ({at.X:0.00},{at.Y:0.00}), Linie {_entities.RailSquashLine}\n" +
                         $"   Waggons: {_entities.RailSquashWagons()}");
            }
            _shotArmed = true;
            return;                             // ein Bild warten, dann schiessen
        }
        // The window may be occluded, in which case the compositor stops drawing
        // and the viewport texture still holds a stale frame — force one draw.
        RenderingServer.ForceDraw();
        var img = GetViewport().GetTexture().GetImage();
        img.SavePng(_shotPath);
        GD.Print($"MapViewer: screenshot -> {_shotPath} " +
                 $"(frame {_frames}, t={Time.GetTicksMsec() / 1000.0:0.00}s, " +
                 $"sim={_entities.DebugClock:0.00}s over {_entities.DebugTicks} ticks)\n" +
                 $"   combat: {_entities.DebugCombatInfo()}\n" +
                 $"   sprites: {_entities.DebugSpriteInfo()}");
        _shotPath = "";
        GetTree().Quit();
    }

    /// <summary>
    /// Der Bedienblock des Originals (PANEL.DTA) als Rahmen der Statusanzeige;
    /// sein eingelassener Kasten geht an die Entity-Ebene fuer den Infotext.
    /// Das Bild ist ein 204x170-Bitmap mit Palette, gezeichnet in ganzzahliger
    /// Vergroesserung mit Nearest-Filter.
    ///
    /// <para>⚠ <b>KORRIGIERT 11.08.2026: er gehoert nach unten LINKS.</b> Er
    /// hing hier an der unteren RECHTEN Ecke und las sich damit wie ein
    /// Seitenpanel. Das Original hat gar keines — im Bildschirmfoto
    /// (akte-europa_8.png, 1280×1024) fuellt die Karte das ganze Fenster, und
    /// unten links sitzt genau dieser Block: drei Gruppenknoepfe mit
    /// Miniaturbildern nebeneinander, darunter der Kasten »Gruppe / Einheiten
    /// 6 / Geschw. 8/10 / Zustand 66% …«, ganz unten eine schmale Leiste mit
    /// der Missionszeit (00:23) und den Rohstoffanzeigen. Das ist Zug um Zug
    /// der Inhalt von PANEL.DTA — es war nur die falsche Ecke. Alles andere im
    /// Original sind frei schwebende Fenster mit Titelleiste und X (»Basis 2«,
    /// »Erstellung«, das Tutorialfenster, das Pausefenster).</para>
    ///
    /// <para>⚠ <b>OFFEN und ausdruecklich unseres:</b> die Vergroesserung.
    /// Das Original zeichnet den Block 1:1 auf einem 1280×1024-Schirm, also auf
    /// 16 % der Breite; wir zeichnen ihn doppelt so gross, damit die 13-px-
    /// Schrift auf heutigen Bildschirmen lesbar bleibt. Er nimmt dadurch mehr
    /// Platz weg als im Original.</para>
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
        BuildPanelClock();
        BuildPanelPortrait();
        BuildPowerBars();
        PlacePanel();
        GetViewport().SizeChanged += PlacePanel;
    }

    // ---- das Einheitenbild im Bedienblock -----------------------------------

    /// <summary>
    /// <b>Das Bild der gewaehlten Einheit unten links</b> — genau das, was der
    /// Spieler gemeldet hat: »kleine bilder, die man unten links im HUD gesehen
    /// hat, wenn die Einheit angewaehlt war«.
    ///
    /// <para><b>Die Stelle ist gelesen, nicht gesetzt.</b> Der Zeichner des
    /// Bedienblocks ruft bei <c>0x4701A9</c>
    /// <c>0x4508A0(kind, entwurf, surf, 0x0B, 0x3D)</c> — <c>0x0B = 11</c>,
    /// <c>0x3D = 61</c>, also fensterrelativ <b>(11, 61)</b> im 204x170 grossen
    /// PANEL.DTA, und das Bild ist 60x60. Das Feld (11,61)..(70,120) liegt
    /// vollstaendig im eingelassenen Anzeigekasten (x 8..160, y 43..136 aus
    /// panel_index.json), links; die drei Statusbalken des Originals stehen
    /// RECHTS davon bei (91,66), (91,86) und (91,107), der Name darueber bei
    /// (11,45).</para>
    ///
    /// <para>⚠ <b>Damit ist eine aeltere Aussage dieses Baums widerlegt.</b> In
    /// <see cref="UI.GameHud"/> steht, <c>0x46FE10</c> sei »bis zur
    /// int3-Polsterung vollstaendig gelesen« und enthalte »GENAU 37
    /// Zeichenaufrufe«. Die Laenge stimmt (5048 B), die Zaehlung nicht: ein
    /// ROHER E8-Abtast findet in demselben Bereich <b>72</b> Aufrufe mit Ziel
    /// im .text, darunter ZWEI des Bildzeichners (0x4701A9 und 0x470C41) und
    /// einen WINDOWS.CWW-Element-Blit (0x46FF62, die Nordrose bei (90,147)). Die
    /// 37 kamen aus einem linearen Capstone-Lauf, und ein linearer Lauf ist eine
    /// UNTERGRENZE, nie ein Beweis fuer Abwesenheit — dieselbe Ursache
    /// verschluckte in 0x46C490 vier Aufrufe. Die Aussage »dauerhaft sind dort
    /// nur Uhr und zwei Strombalken« ist also falsch, und dieses Bild ist der
    /// Beweis. (Der Satz steht noch in GameHud.cs; die Datei gehoert einem
    /// anderen Agenten.)</para>
    ///
    /// <para>Welche Einheit welches Bild bekommt, entscheidet
    /// <c>MapEntityLayer.PanelPortrait()</c> — dort steht auch, warum Schiffe,
    /// Flugzeuge, Infanterie und Gebaeude (noch) keines bekommen und warum eine
    /// GRUPPE im Original keines hat.</para>
    /// </summary>
    private PanelPortrait? _panelPortrait;

    /// <summary>Die Ecke des Bildfeldes im PANEL.DTA, aus
    /// <c>push 0x3D; push 0x0B</c> @0x4701A9, und seine Groesse aus der Bank
    /// (<see cref="UI.PortraitBank.Box"/> = 60).</summary>
    private static readonly Vector2I PanelPortraitAt = new(11, 61);

    /// <summary>Wo der Infotext hin muss, WENN das Bild steht: rechts daneben.
    /// Das Original schreibt dort seine drei Balken (x = 91) und haette den
    /// Namen bei (11,45) darueber — ⚠ dass bei uns auch der NAME nach rechts
    /// wandert, ist unsere Setzung: unser Bedienblock hat EINE Textmarke, das
    /// Original setzt jede Zeile einzeln.</summary>
    private static readonly Rect2 PanelBoxRight = new(71, 43, 90, 94);

    private void BuildPanelPortrait()
    {
        if (_panelLayer == null) return;
        _panelPortrait = new PanelPortrait
        {
            Visible = false,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Size = new Vector2(UI.PortraitBank.Box * PanelScale,
                               UI.PortraitBank.Box * PanelScale),
            // ⚠ ohne Nearest wird aus einem 60x60-Bild bei doppelter
            // Vergroesserung Matsch — dieselbe Regel wie beim Panel selbst
            TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
        };
        _panelLayer.AddChild(_panelPortrait);
    }

    /// <summary>Das Bild nachziehen. Ändert sich seine Sichtbarkeit, wandert der
    /// Infotext mit — er darf nicht darunter liegen.</summary>
    private void UpdatePanelPortrait()
    {
        if (_panelPortrait == null || _entities == null) return;
        var p = _entities.PanelPortrait();
        bool want = p.ChassisPic > 0 && UI.PortraitBank.Ready;
        _panelPortrait.Set(p.ChassisPic, p.TurretPic);
        if (want == _panelPortrait.Visible) return;
        _panelPortrait.Visible = want;
        PlacePanel();
    }

    /// <summary>
    /// Der Prüfstand aus <c>--portrait-check</c>: erst zählen lassen
    /// (<c>MapEntityLayer.PortraitCheck</c>), dann die STELLEN melden.
    ///
    /// <para>⚠ <b>Die Stelle ist die halbe Prüfung.</b> Ein Prüfstand, der nur
    /// »Bild da« sagt, prüft nichts — er muss sagen, WO. Darum steht hier zu
    /// jedem der drei Felder die Ecke und die Grösse in Bildschirmpunkten, dazu
    /// die Ecke des Bedienblocks, sodass sich die fensterrelative Stelle
    /// (11,61) nachrechnen lässt: <c>(Feldecke − Blockecke) / 2 = (11,61)</c>
    /// bei <see cref="PanelScale"/> = 2.</para>
    ///
    /// <para>Und er sagt, welche DATEI die Engine wirklich liest — das ist die
    /// Falle, in die am 13.08.2026 zweimal Arbeit gelaufen ist:
    /// <c>Core.Content.Path</c> bevorzugt <c>user://data/</c>, eine Änderung im
    /// Baum unter <c>Assets/Legacy/</c> bleibt also unsichtbar.</para>
    /// </summary>
    private void PortraitCheckTick()
    {
        if (_portraitCheckAt < 0) return;
        if (_portraitFrames++ < _portraitCheckAt) return;
        _portraitCheckAt = -1;

        GD.Print(_entities.PortraitCheck());
        Vector2 corner = _panelSprite?.Position ?? Vector2.Zero;
        GD.Print($"   Bedienblock: Ecke {corner.X:0},{corner.Y:0}, Vergroesserung {PanelScale}x");
        GD.Print(_panelPortrait == null
            ? "   Bedienblock-Bild: kein Feld gebaut (PANEL.DTA nicht exportiert?)"
            : $"   Bedienblock-Bild: {(_panelPortrait.Visible ? "steht" : "verborgen")}, " +
              _panelPortrait.WatchLine() +
              $" -> fensterrelativ {(_panelPortrait.Position.X - corner.X) / PanelScale:0}," +
              $"{(_panelPortrait.Position.Y - corner.Y) / PanelScale:0} " +
              $"(gelesen @0x4701A9: {PanelPortraitAt.X},{PanelPortraitAt.Y})");
        GD.Print("   " + (_designWindow != null
            ? _designWindow.WatchLine() : "erstellung: kein Fenster"));
        GD.Print("   " + (_baseWindow != null
            ? _baseWindow.WatchLine() : "basis-fenster: kein Fenster"));

        // Mit `--shot` bleibt der Lauf stehen, damit das Bild auch photographiert
        // werden kann — dieselbe Regel wie beim --tutorial-check.
        //
        // ⚠⚠ HIER endet ein `--portrait-check`-Lauf, und zwar nach 90 BILDERN.
        // `--quit-after=12` in derselben Zeile wird NIE erreicht: der Block bei
        // »--quit-after … erreicht« kommt nicht mehr dran, und keine seiner
        // Meldungen steht im Log. Das ist am 13.08.2026 eine halbe Leckjagd wert
        // gewesen — vier Messungen liefen ins Leere, weil die Sonde vor
        // `GetTree().Quit()` IM QUIT-AFTER-BLOCK sass und ihre eigene Zeile nie
        // druckte. Wer an einem `--portrait-check`-Lauf etwas messen will, muss es
        // HIER tun, und wer die Sekundenzahl braucht, darf `--portrait-check`
        // nicht mitgeben. Die Regel dahinter ist die alte: erst nachsehen, ob die
        // eigene Meldung im Log steht, dann die Zahl deuten.
        if (_shotPath.Length == 0) GetTree().Quit(0);
    }

    // =====================================================================
    // DER BEFUND »97 LECKZEILEN UND RUECKGABEWERT 139 AUF GROSSEN KARTEN«
    // — was daran gemessen ist und was NICHT (13.08.2026, Leckjagd).
    //
    // Gemeldet war: `--skirmish=map_NET02 --portrait-check` endet mit 139 (bzw.
    // 132) und meldet vorher 97 Zeilen
    //    ERROR: Leaked unsafe reference to object: ():<Image#…>   bzw. <JSON#…>
    // (87…89 Image, 8…9 JSON), waehrend map_DM_1 sauber durchlaeuft. Die
    // naheliegende Deutung war »ein Image oder Json ohne `using`«.
    //
    // ⚠ DIESE DEUTUNG IST WIDERLEGT, mit einer Sonde an genau dieser Stelle.
    // Alle drei Formen, in denen ein nicht freigegebenes Bild ueberhaupt
    // vorkommen kann, wurden hier kurz vor dem Quit kuenstlich hergestellt — 97
    // Stueck, so viele wie der Befund nennt — und jede meldete 0 Leckzeilen bei
    // Rueckgabewert 0, je zweimal auf map_NET02:
    //   * 97 Image.CreateEmpty, weggeworfen und nicht freigegeben            -> 0
    //   * 97 Image, in einer statischen Liste FESTGEHALTEN                   -> 0
    //   * 97 ImageTexture.CreateFromImage(bild), Bild nicht freigegeben, nur
    //     die Textur gehalten — die Form ALLER echten Ladestellen            -> 0
    // Jede Sonde hat ihre eigene Zeile ins Log gedruckt, sie ist also wirklich
    // gelaufen (die vier Messungen davor nicht, siehe den Vermerk oben). Ein
    // nicht freigegebenes Image beim Herunterfahren erzeugt diese Meldung
    // schlicht nicht.
    //
    // WAS AUSSERDEM AUSGESCHLOSSEN IST, alles auf map_NET02 / map_DM_3 /
    // map_DM_1 gemessen und alles 0 Leckzeilen bei Rueckgabe 0: der Nebel
    // (`--fog` baut das Nebelbild alle 0,2 s neu — der einzige Kandidat, der
    // »je Kartenflaeche« skaliert); 24-fache Rechenlast auf der Maschine; ein
    // Gen0-Etat von 537 MB (DOTNET_GCgen0size), damit gar keine Muellabfuhr
    // laeuft; erst Debug und dann Release gebaut, also genau das Verfahren der
    // 97er-Messung. Und die Muellabfuhr ist ohnehin nicht die Erklaerung: bis
    // zu dieser Stelle sind 4 bis 6 volle Abfuhren gelaufen (Speicherstand am
    // Ausstieg map_NET02 298,4 MB, map_DM_3 228,8 MB, map_DM_1 160,1 MB,
    // jeweils auf 0,1 MB wiederholbar).
    //
    // UND DER BEFUND SELBST IST NICHT MEHR HERZUSTELLEN. Derselbe Befehl,
    // dieselben Karten, ueber 40 Laeufe: 0 Leckzeilen, Rueckgabe 0, dreimal
    // hintereinander auf map_NET02. Der Code kann es nicht sein — seit der
    // 97er-Messung um 18:45 sind nur EINFUEGUNGEN dazugekommen (badc6c5: 121
    // Zeilen, 0 Loeschungen), keine geaenderte Zeile in einem Pfad, den diese
    // Laeufe nehmen. Und das Log eines gescheiterten Laufs ist Zeile fuer Zeile
    // gleich dem eines gelungenen, bis zur letzten Meldung vor dem Absturz:
    // derselbe Lauf, dieselbe Arbeit, nur das Herunterfahren unterscheidet sich.
    //
    // WAS DARAUS FOLGT, fuer den naechsten Anlauf. Der Absturz steckt in
    // System.GC.RunFinalizers() -> GodotObject.Finalize() ->
    // godotsharp_internal_object_get_associated_gchandle, also in
    // Endbehandlern, die NACH dem Herunterfahren der Engine in die Engine
    // zurueckrufen. Das ist ein Wettlauf beim Beenden, keine fehlende Freigabe
    // an einer Ladestelle; »ein `using` mehr« hat die Zahl jetzt dreimal nicht
    // bewegt (Settings, CampaignManager, PortraitBank) und wird sie beim
    // vierten Mal auch nicht bewegen. Wer den Absturz wieder sieht, sollte
    // zuerst aufschreiben, WAS SONST auf der Maschine lief, und den Lauf sofort
    // mit `--verbose` wiederholen: nur dort nennt die Engine die undichten
    // ObjectDB-Objekte einzeln.
    // =====================================================================

    /// <summary>Wann der Waffensitz-Prüfstand meldet, in Bildern; −1 = nie.
    /// <c>--waffensitz-check[=n]</c>.</summary>
    private int _turretSeatCheckAt = -1;
    private int _turretSeatFrames;

    /// <summary>Meldet EINMAL, wo die Waffe auf dem Schiff sitzt — siehe
    /// <see cref="MapEntityLayer.TurretSeatCheck"/>. Der Lauf bleibt stehen,
    /// wenn ein <c>--shot</c> gewünscht ist, damit dasselbe Bild auch
    /// photographiert werden kann.</summary>
    private void TurretSeatCheckTick()
    {
        if (_turretSeatCheckAt < 0) return;
        if (_turretSeatFrames++ < _turretSeatCheckAt) return;
        _turretSeatCheckAt = -1;
        GD.Print(_entities.TurretSeatCheck());
        if (_shotPath.Length == 0) GetTree().Quit(0);
    }

    /// <summary>Wann der Stempel-Prüfstand meldet, in Bildern; −1 = nie.
    /// <c>--stempel-check[=&lt;platz&gt;]</c>.</summary>
    private int _stempelCheckAt = -1;
    private int _stempelSlot = -1;
    private int _stempelFrames;

    /// <summary>Meldet EINMAL, ob sich Rahmen und Bild berühren — siehe
    /// <see cref="MapEntityLayer.StempelCheck"/>.</summary>
    private void StempelCheckTick()
    {
        if (_stempelCheckAt < 0) return;
        if (_stempelFrames++ < _stempelCheckAt) return;
        _stempelCheckAt = -1;
        GD.Print(_entities.StempelCheck(_stempelSlot));
        if (_shotPath.Length == 0) GetTree().Quit(0);
    }

    /// <summary>Das 60x60-Feld selbst. Es rechnet nichts — es malt, was
    /// <see cref="UI.PortraitBank"/> ihm gibt.</summary>
    private sealed partial class PanelPortrait : Control
    {
        /// <summary>⚠ BILDNUMMERN, keine Bauteilzeilen — der Einheitensatz trägt
        /// sie selbst (+0x0b und +0x0c), siehe
        /// <c>MapEntityLayer.PanelPortrait</c>.</summary>
        private int _chassisPic, _turretPic;

        /// <summary>Wieviele Bilder der letzte Lauf gemalt hat: 2 bei einem
        /// bewaffneten Fahrzeug, 1 bei einem unbewaffneten.</summary>
        public int Drawn { get; private set; } = -1;

        public void Set(int chassisPic, int turretPic)
        {
            if (chassisPic == _chassisPic && turretPic == _turretPic) return;
            _chassisPic = chassisPic; _turretPic = turretPic;
            QueueRedraw();
        }

        public override void _Draw()
        {
            Drawn = UI.PortraitBank.DrawPictures(this, new Rect2(Vector2.Zero, Size),
                                                 _chassisPic, _turretPic);
        }

        public string WatchLine()
            => $"Fahrwerksbild {_chassisPic} + Aufsatzbild {_turretPic} " +
               $"= {Drawn} Bilder, {Size.X:0}x{Size.Y:0} an {Position.X:0},{Position.Y:0}";
    }

    // ---- die Missionsuhr im Bedienfeld -------------------------------------

    /// <summary>
    /// Das Zeitfeld unten links im Bedienblock — im Original der kleine
    /// eingelassene Kasten links neben der Ampel, im Bildschirmfoto
    /// <c>akte-europa_8.png</c> mit »00:23«. Bei uns fehlte er ganz.
    ///
    /// <para><b>Belegt, nicht geschaetzt:</b> die Zeichenroutine des Blocks
    /// baut die Zeichenkette aus Stundenbyte 0x8154E4 (@0x46FF57) + ":"
    /// (0x501d48) + Minutenbyte 0x81AA2C (@0x47000F/0x47001D), beide mit
    /// fuehrender Null aus 0x4f8004, und uebergibt sie an den Textzeichner
    /// 0x40102E mit den Ecken <c>push 0x94; push 0x17</c> (@0x470097) —
    /// also x=23, y=148 im 204×170 grossen PANEL.DTA. Auf dem Foto beginnt
    /// die erste Ziffer bei x=23 des Blocks, das passt aufs Pixel.</para>
    ///
    /// <para><b>Gegengeprueft an der zweiten Fassung</b> (F:, 1.420.800 B):
    /// dieselbe Form bei Dateiversatz 0x6DD9A — <c>push 0x94; push 0x17</c>,
    /// davor Stundenbyte 0x814544 und Minutenbyte 0x819A8C. Die Adressen
    /// unterscheiden sich, die Ecken nicht.</para>
    ///
    /// <para>⚠ UNSERE SETZUNG bleibt die FARBE (das Original nimmt seine
    /// Palettenfarbe aus 01.PAL, wir das helle Grau der uebrigen HUD-Schrift)
    /// und dass die Zeile bei uns links ausgerichtet an x=23 sitzt statt im
    /// Kasten zentriert — die 23 ist gemessen, die Ausrichtung dahinter
    /// nicht.</para>
    /// </summary>
    private Label? _panelClock;

    /// <summary>Ecke des Zeitfeldes im PANEL.DTA, aus <c>push 0x94; push 0x17</c>
    /// @0x470097.</summary>
    private static readonly Vector2I PanelClockAt = new(23, 148);

    /// <summary>
    /// <b>DIE ZWEI STROMBALKEN im Bedienblock</b> — und ihre Masse sind
    /// gelesen, nicht gewählt.
    ///
    /// <para><c>panel_draw</c> @0x46FE10 zeichnet sie als zwei Paare: einen
    /// Grund von <b>58×4</b> bei <b>(140,148)</b> und <b>(140,157)</b>, darüber
    /// den Wert <b>2 px hoch</b> bei (141,149) bzw. (141,158) mit der Breite
    /// <c>Prozent·56/100</c> (@0x46FE66, 0x46FEB3, 0x46FEDB, 0x46FF20). Links
    /// davor sitzt im Bild <c>panel.png</c> der orange BLITZ — es sind die
    /// STROMbalken, und deshalb war der Platz die ganze Zeit da und leer.</para>
    ///
    /// <para><b>Oben die erbrachte Leistung, unten der Bedarf</b>, beide gegen
    /// den grösseren der zwei skaliert: @0x4405F6 setzt bei Überschuss den
    /// oberen auf voll und den unteren auf <c>100·Bedarf/Ist</c>, bei Mangel
    /// umgekehrt. Wer ausreichend Strom hat, sieht also den oberen voll.</para>
    ///
    /// <para>⚠ <b>UNSERE Setzung ist nur die FARBE</b> (das Original nimmt
    /// seine Palettenfarbe aus 01.PAL) — dieselbe Lücke wie bei der
    /// Panel-Uhr.</para></summary>
    private ColorRect? _powerBarTop, _powerBarBottom;

    private static readonly Vector2I PowerBarTopAt = new(141, 149);
    private static readonly Vector2I PowerBarBottomAt = new(141, 158);
    private const int PowerBarWide = 56, PowerBarHigh = 2;

    private void BuildPowerBars()
    {
        if (_panelLayer == null) return;
        var farbe = new Color(1.0f, 0.62f, 0.15f);      // ⚠ unsere Wahl, wie beim Blitz
        _powerBarTop = new ColorRect
        {
            Color = farbe, MouseFilter = Control.MouseFilterEnum.Ignore,
            Size = new Vector2(0, PowerBarHigh * PanelScale),
        };
        _powerBarBottom = new ColorRect
        {
            Color = farbe, MouseFilter = Control.MouseFilterEnum.Ignore,
            Size = new Vector2(0, PowerBarHigh * PanelScale),
        };
        _panelLayer.AddChild(_powerBarTop);
        _panelLayer.AddChild(_powerBarBottom);
    }

    /// <summary>Die zwei Balken nachziehen — dieselbe Rechnung wie @0x4405E5.
    /// ⚠ Die Zahlen kommen aus <c>MapEntityLayer.PowerOf</c>, also aus der
    /// Abrechnung selbst; eine zweite Rechnung hier wäre eine zweite
    /// Wahrheit.</summary>
    private void UpdatePowerBars()
    {
        if (_powerBarTop == null || _powerBarBottom == null || _entities == null) return;
        var (_, _, ist, soll) = _entities.PowerOf(_entities.ViewPlayer);
        int oben, unten;
        if (soll < ist) { oben = 100; unten = ist > 0 ? 100 * soll / ist : 0; }
        else if (soll > 0) { unten = 100; oben = 100 * ist / soll; }
        // ⚠ Bedarf 0 UND Leistung 0: das Original schreibt dann GAR NICHTS
        // (@0x44061E prueft `test cx,cx / jle` und faellt durch) — die Balken
        // behalten, was sie hatten. Wer hier auf 0 setzt, laesst sie beim
        // Verlust der letzten Fabrik zusammenfallen, statt sie stehenzulassen.
        else return;
        _powerBarTop.Size = new Vector2(Mathf.Clamp(oben, 0, 100) * PowerBarWide / 100f * PanelScale,
                                        PowerBarHigh * PanelScale);
        _powerBarBottom.Size = new Vector2(Mathf.Clamp(unten, 0, 100) * PowerBarWide / 100f * PanelScale,
                                           PowerBarHigh * PanelScale);
    }

    private void BuildPanelClock()
    {
        if (_panelLayer == null) return;
        _panelClock = new Label
        {
            Text = "00:00",
            MouseFilter = Control.MouseFilterEnum.Ignore,
            // ⚠ Always, aus demselben Grund wie beim Abschlussfenster: bei
            // angehaltenem Baum soll die Uhr STEHEN, aber sichtbar bleiben —
            // ohne diesen Modus verschwindet sie nicht, sie friert nur ein.
            // Das ist erwuenscht; die Zahl kommt ohnehin aus der Simulation.
        };
        _panelClock.AddThemeColorOverride("font_color", new Color(0.85f, 0.85f, 0.80f));
        _panelClock.AddThemeConstantOverride("outline_size", 0);
        if (_legacyFont != null)
        {
            _panelClock.AddThemeFontOverride("font", _legacyFont);
            _panelClock.AddThemeFontSizeOverride("font_size", LegacyFontCell * LegacyFontScale);
        }
        _panelLayer.AddChild(_panelClock);
    }

    /// <summary>Die Uhr nachfuehren. Sie fragt <see cref="MapEntityLayer.
    /// MissionClockText"/> — dieselbe Quelle, aus der das Abschlussfenster
    /// seine »Missionszeit« nimmt, damit beide nie auseinanderlaufen.</summary>
    private void UpdatePanelClock()
    {
        if (_panelClock == null) return;
        string t = _entities.MissionClockText;
        if (t != _panelClock.Text) _panelClock.Text = t;
    }

    // ======================= die stehende Rohstoffleiste =====================

    /// <summary>Die Leiste selbst liegt in <see cref="UI.GameHud"/>; dort steht
    /// auch, was an ihr belegt und was unsere Zutat ist.</summary>
    private UI.GameHud? _resourceBar;

    /// <summary>`Q` legt die Leiste weg — sie liegt über der Karte, und wer ein
    /// freies Bild will, muss sie loswerden können (dieselbe Überlegung wie bei
    /// `I` fürs Baufenster).</summary>
    private bool _hideResourceBar;

    private void BuildResourceBar(CanvasLayer layer)
    {
        _resourceBar = new UI.GameHud();
        layer.AddChild(_resourceBar);
        _resourceBar.Read = ReadStocks;
        // ⚠ DebugClock ist die SIMULATIONSUHR (sie zählt in SimTick, nicht im
        // Bildlauf). Genau darum steht sie hier und nicht `_upTime`: der Zuwachs
        // je Sekunde muss aus dem festen Takt kommen.
        _resourceBar.SimClock = () => _entities.DebugClock;
        // ⚠ 17.08.2026 — DIE BAUZEILE IST ANGESCHLOSSEN. Hier stand: »sie bleibt
        // LEER, MapEntityLayer gibt nach aussen nirgends heraus, welches Gebäude
        // gerade woran baut« — das stimmte, und es war mit der Bauwarteschlange
        // (Fehler C8) nicht mehr haltbar: eine Schlange, die man nicht sieht,
        // ist von einer verschluckten Bestellung nicht zu unterscheiden, und
        // genau das war der gemeldete Fehler.
        //
        // ⚠ Der Vorbehalt von damals gilt weiter und steht jetzt IM TEXT: das
        // Original führt für eine Einheit gar keine Bauzeit (siehe
        // UI/GameHud.cs), die Sekundenzahl ist unsere Erfindung. Deshalb sagt
        // BuildQueueLine() »(Bauzeit ist unsere Zutat)« dazu, solange nichts in
        // der Schlange steht — dort ist Platz dafür.
        _resourceBar.Building = () => _entities.BuildQueueLine();
    }

    /// <summary>
    /// Was die Leiste anzeigt: die Summe der Lager aller Gebäude des
    /// Sichtspielers, plus der Kontostand.
    ///
    /// <para>⚠ <b>NOTWEG, und er ist als solcher gemeint.</b> Die Bestände
    /// liegen in <c>MapEntityLayer</c> (Entity.StockT/W/F/S), und diese Datei
    /// gehört einem anderen Agenten; sie gibt keine Summe und keine Gebäudeliste
    /// heraus. Die Zahlen liefert <c>MapEntityLayer.PlayerStocks(spieler)</c>.</para>
    ///
    /// <para>⚠ <s>Hier stand beschrieben, wie die vier Summen aus der ZEILE von
    /// <c>EconCheckLine()</c> zerlegt werden — Klammer für Klammer, Wort für
    /// Wort.</s> Erledigt am 13.08.2026: der Zugriff ist gebaut, die Zerlegung
    /// ist raus. Beide benutzen dieselbe Gebäudeauswahl
    /// (<c>MapEntityLayer.CountsForStocks</c>), damit Anzeige und Prüfstand nicht
    /// auseinanderlaufen können.</para>
    ///
    /// <para>⚠ Was die Leiste NICHT zeigt: Terranium, das noch in einer MINE
    /// liegt oder auf dem Nahweg unterwegs ist. Gezählt werden Fabriken und
    /// Basis, also genau das, woraus BEZAHLT wird (@0x44A6D8/ED/08 prüfen die
    /// Lager des Gebäudes, dessen Fenster offen ist). Das ist die ehrliche
    /// Lesart der Zahl — »was ich ausgeben kann« — und nicht »was ich
    /// besitze«.</para>
    /// </summary>
    private UI.GameHud.Stocks ReadStocks()
    {
        // ⚠ 13.08.2026 — HIER STAND EIN NOTWEG: die vier Summen wurden aus der
        // ZEICHENKETTE von EconCheckLine() zerlegt, Klammer für Klammer, Wort
        // für Wort, und der Kontostand aus »Kontostand : $ 44850«. Das hat
        // funktioniert und wäre bei der nächsten Änderung an einem
        // Prüfstands-WORTLAUT still falsch geworden — eine Anzeige darf nicht
        // vom Text eines Prüfstands abhängen.
        //
        // PlayerStocks() liefert die Zahlen jetzt direkt, und es benutzt
        // dieselbe Gebäudeauswahl wie EconCheckLine() (CountsForStocks), damit
        // Anzeige und Prüfstand nicht auseinanderlaufen können. Der Kontostand
        // kommt aus demselben Aufruf; MoneyLine() ist nur noch die Textzeile.
        //
        // Die Lesart der Zahl bleibt die alte und bleibt die ehrliche: gezählt
        // werden Fabriken und Basis, also das, woraus BEZAHLT wird
        // (@0x44A6D8/ED/08 prüfen die Lager des Gebäudes, dessen Fenster offen
        // ist) — »was ich ausgeben kann«, nicht »was ich besitze«.
        var p = _entities.PlayerStocks(_entities.ViewPlayer is >= 0 and <= 7
            ? _entities.ViewPlayer : 0);
        return new UI.GameHud.Stocks(p.T, p.W, p.F, p.S, p.Money, p.Buildings > 0, p.Buildings);
    }

    /// <summary>Die Leiste eine Probe nehmen lassen und sie an ihre Stelle
    /// setzen. Sie entscheidet selbst, ob eine neue SIMULATIONSSEKUNDE begonnen
    /// hat — hier wird nur angeklopft.</summary>
    private void UpdateResourceBar()
    {
        if (_resourceBar == null) return;
        bool want = !_hideResourceBar && !_entities.Designer.Active;
        if (_resourceBar.Visible != want) _resourceBar.Visible = want;
        if (!want) return;
        _resourceBar.Sample();
        // Unter der Statusplatte durch: die ist links oben und so hoch, wie ihr
        // Text sie macht — darum wird ihre Höhe gefragt und nicht geraten.
        _resourceBar.Place(GetViewportRect().Size, 4f);
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
            _entities.FogTexture,
            _entities.MinimapHome);
        PlaceMinimap();
        GetViewport().SizeChanged += PlaceMinimap;
    }

    /// <summary>So breit wie der Bedienblock und nie hoeher als MinimapMaxH.
    /// Eine hohe Karte behaelt ihr Seitenverhaeltnis, indem sie schmaler wird —
    /// sie passend zu quetschen saetze die Punkte an die falschen Stellen.
    ///
    /// <para>⚠ Sie sitzt seit dem 11.08.2026 in der unteren RECHTEN Ecke, weil
    /// der Bedienblock nach links gewandert ist (siehe BuildLegacyPanel). Beides
    /// uebereinander haette die linke Bildhaelfte zugebaut. Die Uebersichtskarte
    /// ist ohnehin UNSERE Zutat — das Original hat keine staendige, es bietet in
    /// der Befehlsleiste »Karte des Einsatzgebietes zeigen« (@0x4ef50c) an.
    /// Rechts unten verdraengt sie nichts, was das Original dort haette.</para>
    /// </summary>
    private const float MinimapMaxH = 200;

    private void PlaceMinimap()
    {
        if (_minimap == null || _panelSprite?.Texture == null) return;
        Vector2 panel = _panelSprite.Texture.GetSize() * PanelScale;
        float w = panel.X;
        float h = _minimap.HeightFor(w);
        if (h > MinimapMaxH) { w *= MinimapMaxH / h; h = MinimapMaxH; }
        var view = GetViewportRect().Size;
        _minimap.Position = new Vector2(view.X - w - 4, view.Y - h - 4);
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
        // untere LINKE Ecke — siehe BuildLegacyPanel fuer den Befund
        var origin = new Vector2(0, view.Y - size.Y);
        _panelSprite.Position = origin;
        // Steht das Einheitenbild, geht der Infotext in die rechte Spalte —
        // sonst laege er darunter. Siehe PanelBoxRight.
        Rect2 rel = _panelPortrait is { Visible: true } ? PanelBoxRight : PanelBox;
        var box = new Rect2(origin + rel.Position * PanelScale, rel.Size * PanelScale);
        _entities.SetPanelBox(box);
        if (_panelClock != null)
            _panelClock.Position = origin + (Vector2)PanelClockAt * PanelScale;
        if (_panelPortrait != null)
            _panelPortrait.Position = origin + (Vector2)PanelPortraitAt * PanelScale;
        if (_powerBarTop != null)
            _powerBarTop.Position = origin + (Vector2)PowerBarTopAt * PanelScale;
        if (_powerBarBottom != null)
            _powerBarBottom.Position = origin + (Vector2)PowerBarBottomAt * PanelScale;
    }

    /// <summary>Das Baumenü: seit dem 11.08.2026 ein frei schwebendes FENSTER,
    /// so wie im Original — siehe UI/BaseWindow.cs. Vorher stand die Bauliste
    /// als nackter Text im eingelassenen Kasten des Bedienfelds; genau das hat
    /// der Spieler als »das Bau Menu, das ist nicht das Originale« gemeldet.
    ///
    /// <para>Die alte Liste (UI/BuildPanel.cs) wird nicht mehr gezeigt. Die
    /// Datei bleibt, weil <c>MapEntityLayer.BuildPanelRows()</c> ihren
    /// <c>Row</c>-Satz als Schnittstelle benutzt.</para></summary>
    private UI.BaseWindow? _baseWindow;

    /// <summary>Die Ebene des Fensters: ÜBER dem Bedienfeld (2), aber UNTER den
    /// Hilfefenstern der Mission (90) und der Abrechnung (95) — dieselbe Regel,
    /// die BuildEndBanner sich schon einmal einhandeln musste.</summary>
    private const int BaseWindowLayer = 80;

    private void BuildBaseWindow()
    {
        var layer = new CanvasLayer { Layer = BaseWindowLayer };
        AddChild(layer);
        _baseWindow = new UI.BaseWindow { Visible = false };
        layer.AddChild(_baseWindow);
        _baseWindow.Rows = _entities.BuildPanelRows;
        _baseWindow.TitleLine = _entities.BuildPanelTitle;
        // Name, Energie und Zustand des gewaehlten Gebaeudes — damit in der
        // Titelleiste »Basis 2« steht statt des ersten Wortes der Zeile
        // darunter, und Energiebalken und Statuszeile ueberhaupt etwas haben.
        _baseWindow.Head = _entities.BuildPanelHead;
        _baseWindow.Produce = _entities.BuildPanelPick;
        _baseWindow.OnClose = () => { _hidePanelList = true; UpdateProductionPanel(); };
        // »Erstellen« macht dasselbe wie die Taste M — und weil das Fenster
        // »Erstellung« nur die Entwurfsstelle anzeigt, laufen Knopf und Taste
        // nie auseinander.
        _baseWindow.OnDesign = () => _entities.ToggleDesigner();
        // ⚠ 17.08.2026 — die zwei toten Reiter sind angeschlossen (Fehler C2
        // und C6). Beide Mechaniken lagen seit langem auf den Tasten O und K und
        // waren über die Oberflaeche schlicht nicht zu finden; das Fenster hat
        // stattdessen »noch nicht angeschlossen« behauptet, was fuer die
        // MECHANIK falsch war und nur fuer den REITER stimmte.
        _baseWindow.ResearchNote = _entities.ResearchNote;
        _baseWindow.OnResearch = () => _entities.ResearchFromPanel();
        _baseWindow.RepairNote = _entities.RepairNote;
        _baseWindow.OnRepair = () => _entities.RepairFromPanel();
        // ⚠ 18.08.2026 — DER SECHSTE FALL desselben Fehlers: Lagerausbau und
        // Produktionserweiterung lagen nur auf den Tasten V und C. Die Mechanik
        // war seit langem fertig und befehlsgenau gebaut (Lagerplatz +10,
        // Produktionsgeschwindigkeit +1, jeder Ausbau verteuert NUR seinen
        // eigenen Preis um die Haelfte) — und fuer den Spieler nicht vorhanden.
        _baseWindow.UpgradeChoice = () => _entities.UpgradeChoiceOfSelection();
        _baseWindow.OnUpgrade = lager => _entities.StartUpgrade(lager);
        // ⚠ Der SIEBTE und ACHTE Fall: »Bau abbrechen« lag nur auf Umschalt+B,
        // »Starten« nur auf Y — und die Bestandszeile des Flughafens sagte dem
        // Spieler sogar »(Y startet)«, was das Eingestaendnis dafuer ist.
        _baseWindow.CancelChoice = () => _entities.CancelChoiceOfSelection();
        _baseWindow.OnCancelBuild = () => _entities.CancelBuild();
        _baseWindow.HangarCount = () => _entities.HangarOfSelection();
        _baseWindow.OnLaunch = () => _entities.LaunchAircraft(_entities.ViewPlayer);
        // ⚠ 16.08.2026 — der DRITTE tote Reiter ist angeschlossen (Fehler D1).
        // Anders als bei Forschung und Reparatur lag hier keine fertige Mechanik
        // auf einer Taste: das Depot gab es gar nicht, fertige Einheiten
        // sprangen direkt auf die Karte. Siehe MapEntityLayer.Entity.Depot.
        _baseWindow.DepotRows = _entities.DepotRows;
        _baseWindow.OnSendOut = k => _entities.SendOutFromPanel(k);
        _baseWindow.IsMarket = () => _entities.PanelIsMarket();

        _designWindow = new UI.DesignWindow
        {
            Visible = false,
            Screen = _entities.Designer,
            Input = _entities.DesignerInput,
            OnClose = () => { if (_entities.Designer.Active) _entities.ToggleDesigner(); },
        };
        layer.AddChild(_designWindow);

        // ⚠ Die Befehlsleiste der EINHEIT — siehe UI/UnitOrderBar.cs. Sie liegt
        // auf derselben Ebene wie das Basisfenster, weil sie dieselbe Rolle
        // spielt: das Fenster bedient ein Gebaeude, die Leiste eine Einheit.
        _orderBar = new UI.UnitOrderBar { Visible = false };
        layer.AddChild(_orderBar);
        _orderBar.SetWords(MapEntityLayer.OrderWord(MapEntityLayer.OrderSell),
                           MapEntityLayer.OrderWord(7) + "/" + MapEntityLayer.OrderWord(8),
                           MapEntityLayer.OrderWord(26),
                           // 20 = »Radar setzen«, wieder das Wort des SPIELS
                           MapEntityLayer.OrderWord(20));
        _orderBar.SellChoice = () => _entities.SellChoiceOfSelection() is { } w
                                   ? (w.Name, w.Price) : null;
        _orderBar.OnSell = () => _entities.SellFromPanel();
        _orderBar.RadarCharges = () => _entities.RadarChoiceOfSelection()?.Charges;
        _orderBar.OnPlaceRadar = () => _entities.PlaceRadarFromPanel();
        // Die drei anderen Bauauftraege — siehe Simulation/BuildOrders.cs. Sie
        // schalten den Setzmodus ein; der naechste Linksklick auf die Karte
        // setzt den Befehl ab (_UnhandledInput weiter unten).
        _orderBar.BuildChoices = () =>
        {
            var ws = _entities.BuildChoicesOfSelection();
            var outp = new (int, string)[ws.Count];
            for (int k = 0; k < ws.Count; k++) outp[k] = (ws[k].Order, ws[k].Word);
            return outp;
        };
        _orderBar.OnBuildOrder = order => _entities.BeginPlacementFromPanel(order);
        _orderBar.OnDigIn = () => _entities.ToggleDigIn();
        _orderBar.OnStop = () => _entities.StopSelected();
        // ⚠ Im Setzmodus hat die Bauzeile Vorrang: sie sagt, worauf gewartet
        // wird. Sonst stuende dort weiter die letzte Verkaufsmeldung, und der
        // Spieler saehe nicht, dass sein Klick jetzt etwas anderes bedeutet.
        _orderBar.Note = () => _entities.PlacementMode != 0 || _entities.SellNote.Length == 0
                             ? _entities.BuildOrderNote : _entities.SellNote;
    }

    /// <summary>Die Befehlsleiste der gewählten Einheit — siehe
    /// UI/UnitOrderBar.cs.</summary>
    private UI.UnitOrderBar? _orderBar;

    /// <summary>Sie steht da, sobald eine eigene, bewegliche Einheit gewählt
    /// ist, und geht weg, wenn die Auswahl weg ist.</summary>
    private void UpdateUnitOrderBar()
    {
        if (_orderBar == null || _entities == null) return;
        bool want = _entities.SellChoiceOfSelection() != null
                 || _entities.RadarChoiceOfSelection() != null
                 || _entities.BuildChoicesOfSelection().Count > 0;
        if (want != _orderBar.Visible)
        {
            _orderBar.Visible = want;
            if (want) _orderBar.PlaceBottomCentre();
        }
        if (want) { _orderBar.Refresh(); _orderBar.PlaceBottomCentre(); }
    }

    /// <summary>Der Entwurfsdialog — siehe UI/DesignWindow.cs. Er hat keinen
    /// eigenen Zustand: er zeigt <c>MapEntityLayer.Designer</c> an und ist
    /// genau dann offen, wenn die auf ist. So bedienen Tastatur (M, Pfeile,
    /// Enter) und Fenster dieselbe Stelle.</summary>
    private UI.DesignWindow? _designWindow;

    private void UpdateDesignWindow()
    {
        if (_designWindow == null) return;
        bool want = _entities.Designer.Active;
        if (want != _designWindow.Visible) _designWindow.Visible = want;
        if (want) _designWindow.Refresh();
    }

    /// <summary>Das Fenster geht auf, sobald etwas Bauendes gewählt ist, und
    /// wieder zu, wenn die Auswahl weg ist. Der Infotext im Bedienfeld bleibt
    /// dabei stehen — im Original stehen beide nebeneinander.</summary>
    private void UpdateProductionPanel()
    {
        if (_baseWindow == null) return;
        bool want = _entities.BuildPanelWanted && !_hidePanelList;
        if (want != _baseWindow.Visible)
        {
            _baseWindow.Visible = want;
            if (want) _baseWindow.PlaceTopRight();
        }
        // Ein geschlossenes Fenster geht beim nächsten Anklicken wieder auf.
        if (!_entities.BuildPanelWanted) _hidePanelList = false;
        if (want) _baseWindow.Refresh();
    }

    /// <summary>`I` legt das Baufenster weg, für einen freien Blick.</summary>
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
        _baseWindow?.SetFont(font, size);
        _orderBar?.SetFont(font, size);
        _designWindow?.SetFont(font, size);
        // Die Rohstoffleiste MUSS diese Schrift haben: ihre drei Sinnbilder sind
        // die Zeichen ] [ { aus FONT.CWD — mit einer anderen Schrift stünden
        // dort Klammern (siehe UI/GameHud.cs).
        _resourceBar?.SetFont(font, size);
        // ⚠ Reihenfolge: BuildLegacyPanel() laeuft VOR ApplyLegacyFont() (siehe
        // _Ready), die Uhr entsteht also noch ohne Schrift — dieselbe Falle wie
        // bei der Tabelle im Abschlussfenster. Sie bekommt sie hier.
        if (_panelClock != null)
        {
            _panelClock.AddThemeFontOverride("font", font);
            _panelClock.AddThemeFontSizeOverride("font_size", size);
        }
        _legacyFont = font;
        GD.Print($"MapViewer: legacy FONT.CWD applied at {size}px");
    }

    private Font? _legacyFont;

    private const int LegacyFontCell = 13;   // the original glyph cell height
    private const int LegacyFontScale = 2;   // integer upscale for modern screens

    // ---- end of a skirmish -------------------------------------------------

    private Control? _endBanner;
    private UI.MissionEndWindow? _endWindow;
    private bool _ended;

    /// <summary>Das Fenster, das eine entschiedene Mission abrechnet. Der
    /// SPRUCH ist die Regel des Originals (raus, wenn nichts mehr steht;
    /// gewonnen, sobald kein unverbuendeter Spieler mehr etwas hat) — der
    /// AUFBAU des Fensters ist vom Bildschirmfoto abgeschrieben, siehe
    /// UI/MissionEndWindow.cs.</summary>
    private void BuildEndBanner()
    {
        // ⚠ Ebene 95, nicht 10: die Hilfefenster der Mission liegen auf 90
        // (UI/HelpWindow.LayerName). Im ersten Bild stand die Abrechnung HINTER
        // dem Tutorialfenster von Mission 2 und war halb verdeckt.
        var layer = new CanvasLayer { Layer = 95 };
        AddChild(layer);
        // Der Deckel faengt die Maus ab (Control faengt sie von Haus aus), aber
        // er DUNKELT NICHT AB: im Bildschirmfoto des Originals liegt die Karte
        // hinter dem Fenster in voller Helligkeit. Die alte Fassung legte ein
        // 55-%-Schwarz darueber — das war unsere Zutat.
        _endBanner = new Control { Visible = false, ProcessMode = ProcessModeEnum.Always };
        // ⚠ 17.08.2026 — hier stand `SetAnchorsPreset`, und damit blieb der
        // Deckel null Pixel gross: er hat GAR KEINE Maus abgefangen, obwohl
        // genau das sein Zweck ist. Das Abschlussfenster selbst ist davon
        // unberuehrt — es setzt seine Lage in ShowEndWindow absolut gegen das
        // Sichtfeld (:2349). Siehe LoadGameScreen._Ready, Fehler C20.
        _endBanner.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        layer.AddChild(_endBanner);

        _endWindow = new UI.MissionEndWindow();
        _endBanner.AddChild(_endWindow);
        _endWindow.OnClose = () => { if (_endBanner != null) _endBanner.Visible = false; };
        _endWindow.OnContinue = ContinueAfterMission;
        if (_legacyFont != null)
            _endWindow.SetFont(_legacyFont, LegacyFontCell * LegacyFontScale);
    }

    private void ToMenu()
    {
        UI.SkirmishSetup.Active = false;
        GetTree().ChangeSceneToFile(UI.SkirmishSetup.MenuScene);
    }

    /// <summary>Was der Knopf »Weiter« tut. Gibt es eine Folgemission, dann
    /// startet sie; sonst geht es ins Menue (⚠ unsere Setzung — wohin das
    /// Original am Kampagnenende fuehrt, ist ungelesen).</summary>
    private void ContinueAfterMission()
    {
        if (_nextMission > 0) StartNextMission(); else ToMenu();
    }

    private void CheckEnd()
    {
        if (_ended || _endBanner == null || _endWindow == null || !UI.SkirmishSetup.Active) return;
        string v = _entities.Verdict();
        if (v.Length == 0) return;
        ShowEnd(v.Contains("ERFUELLT"), v);
    }

    /// <summary>Das Abschlussfenster aufmachen. Getrennt von
    /// <see cref="CheckEnd"/>, damit der Pruefstand (--end-window) es
    /// hochziehen kann, ohne dass eine Mission wirklich entschieden ist.
    /// </summary>
    /// <param name="record">false fuer den Pruefstand: dann wird der
    /// Kampagnenfortschritt NICHT geschrieben. ⚠ Der erste Lauf tat es doch und
    /// hat user://campaign.cfg auf »Mission 2 geschafft« gesetzt, ohne dass
    /// jemand Mission 2 gespielt hatte.</param>
    private void ShowEnd(bool won, string v, bool record = true)
    {
        if (_ended || _endBanner == null || _endWindow == null) return;
        _ended = true;
        // `close_message_windows()` @0x447560 — was noch offen ist, raeumt das
        // Original vor einem neuen Fenster weg, und die Abrechnung ist das
        // letzte Fenster der Mission.
        UI.HelpWindow.CloseAll();
        UI.HelpWindow.CommitClose();
        int mission = UI.SkirmishSetup.CampaignMission;

        // Der Missionsname in Rot ist im Original schlicht »Mission N«: der
        // Kartenlader indiziert die Namenstabelle mit dem Kampagnenzaehler
        // selbst (@0x41e25e, `21*counter + 0x4f81c0`), und die Eintraege lauten
        // "Mission 1" … "Mission 33" (siehe Campaign/CampaignManager).
        string name = mission > 0 ? $"Mission {mission}" : "Gefecht";
        string label = "Weiter";
        string hint = "";
        if (won && mission > 0)
        {
            if (record) Campaign.CampaignManager.Finished(mission);
            var next = Campaign.CampaignManager.Next();
            // ⚠ HIER lag der gemeldete Fehler: `_nextMission` wurde nie
            // gesetzt und blieb -1, also fand StartNextMission() ueber
            // ByIndex(-1) nie eine Mission und fiel ins Menue zurueck. Der
            // Knopf war ausserdem unsichtbar. Ohne diese eine Zeile gab es
            // keinen Weg von einer beendeten Mission zur naechsten.
            _nextMission = next?.Index ?? -1;
            hint = next != null ? $"Naechste Mission: {next.Label}" : "Die Kampagne ist zu Ende";
            GD.Print($"Kampagne: Mission {mission} geschafft, weiter mit " +
                     $"{(next != null ? next.Label : "— nichts mehr")}");
        }
        // Der Knopf heisst im Original schlicht »Weiter«, auch am Ende der
        // Kampagne — wohin er dort fuehrt, ist ungelesen. Wohin er bei UNS
        // fuehrt, steht deshalb nur im Zeigefaehnchen und nicht auf dem Knopf.
        if (_nextMission <= 0 && hint.Length == 0) hint = "Zurueck zum Hauptmenue";

        var report = _entities.BuildEndReport();
        // Der Kontostand wandert in die naechste Mission mit. Das Original
        // addiert die Missionsbezahlung auf den laufenden Stand (@0x4169E3:
        // `mov ecx,[0xA9C600]; add ecx,[0xA9A1D8]; mov [0xA9C600],ecx`) und
        // setzt ihn nirgends zurueck — `report.Balance` IST dieser laufende
        // Stand. Siehe Campaign.CampaignManager.Balance fuer den vollen Beleg.
        //
        // ⚠ 11.08.2026 — DIE MISSIONSBEZAHLUNG FEHLTE. `report.Pay` war die
        // Summe der Geldbuchungen aus dem Missionsskript (Busbefehl 528), und
        // das war eine Setzung: »was ausgezahlt wird, ist die Bezahlung«. Sie
        // ist falsch. Die Bezahlung ist ein FESTER Betrag je Mission, den der
        // Missionsblock beim START in 0xA9A1D8 einsetzt und der am ENDE
        // obendrauf kommt — 36 Konstanten, Mission 1 = 320. Beleg und Herleitung
        // in Campaign.CampaignManager.PayFor. Die Skriptbuchungen sind schon
        // waehrend der Mission aufs Konto gegangen (MapEntityLayer.AddMoney) und
        // stecken bereits in `report.Balance`; hier kommt nur noch die
        // Bezahlung dazu, sonst stuenden sie doppelt drin.
        if (mission > 0)
        {
            report.Pay = Campaign.CampaignManager.PayFor(mission);
            report.Balance += report.Pay;
        }
        if (record && mission > 0) Campaign.CampaignManager.Balance = report.Balance;
        _endWindow.Fill(report, won, name, label, hint);
        _endBanner.Visible = true;
        CenterEndWindow();
        // erst im naechsten Bild steht die Mindestgroesse fest
        CallDeferred(nameof(CenterEndWindow));
        GD.Print($"{(mission > 0 ? "Mission" : "Gemetzel")} entschieden: {v} — " +
                 $"Zeit {report.Minutes / 60:00}:{report.Minutes % 60:00}, " +
                 $"gebaut {report.Built}, ausgeschaltet {report.Kills}, " +
                 $"Verluste {report.Losses}, Untermissionen {report.SubDone}/{report.SubTotal}, " +
                 $"Bezahlung ${report.Pay}, Kontostand ${report.Balance}");
    }

    private void CenterEndWindow()
    {
        if (_endWindow == null) return;
        // ⚠ Ein Control an einem blossen Control bekommt keine Layoutgroesse —
        // dieselbe Falle wie im HelpWindow: es bliebe 0x0 und klemmte seinen
        // Inhalt weg.
        _endWindow.Size = _endWindow.GetCombinedMinimumSize();
        var vp = GetViewportRect().Size;
        _endWindow.Position = ((vp - _endWindow.Size) * 0.5f).Floor();
    }

    private int _nextMission = -1;

    /// <summary>Die naechste Kampagnenmission unmittelbar starten.
    ///
    /// ⚠ Das Briefing wird dabei UEBERSPRUNGEN: es haengt im Hauptmenue
    /// (MainMenu.StartMission), und der Weg dorthin und zurueck waere ein
    /// Szenenwechsel mehr. Wer es sehen will, geht ueber das Menue -- das steht
    /// so auch auf dem Knopf daneben.</summary>
    private void StartNextMission()
    {
        var m = Campaign.CampaignManager.ByIndex(_nextMission);
        if (m == null) { ToMenu(); return; }
        UI.SkirmishSetup.Map = m.Map;
        UI.SkirmishSetup.Human = 0;
        UI.SkirmishSetup.AiCount = 0;
        UI.SkirmishSetup.CampaignMission = m.Index;
        UI.SkirmishSetup.Active = true;
        GD.Print($"Kampagne: weiter mit {m.Label} ({m.Map})");
        GetTree().Paused = false;
        GetTree().ReloadCurrentScene();
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
            // ⚠ `using`, und ausdruecklich als HYGIENE, nicht als Kur: das ist das
            // GROESSTE Godot-Objekt des ganzen Laufs (map_NET02.png ist 9200x4805,
            // also 44 Megapunkte = 177 MB im Speicher), und `ImageTexture
            // .CreateFromImage` nimmt sich seine eigene Kopie — nach der Zeile
            // darunter ist das Bild tot. Es deterministisch freizugeben ist
            // richtig; die Leckjagd vom 13.08.2026 hat aber gemessen, dass es an
            // dem gesuchten Fehler NICHTS aendert: der Speicherstand am Ausstieg
            // von `--portrait-check` ist auf map_NET02 mit und ohne `using` genau
            // gleich (statisch 298,4 MB, verwaltet 96,3 MB), weil bis dahin
            // ohnehin 4 volle Muellabfuhren gelaufen sind. Siehe den Vermerk bei
            // <see cref="PortraitCheckTick"/>.
            using var img = Image.LoadFromFile(pngPath);
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
        if (_baseWindow == null)
        {
            BuildBaseWindow();
            if (_legacyFont != null)
            {
                _baseWindow?.SetFont(_legacyFont, LegacyFontCell * LegacyFontScale);
                _designWindow?.SetFont(_legacyFont, LegacyFontCell * LegacyFontScale);
            }
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

        // ⚠ 14.08.2026 — DIE ZWEI TECHNISCHEN ZEILEN SIND IM SPIEL WEG.
        //
        // Gemeldet: »Das Fenster allgemein, wo oben steht Map_02 "Hidden Bases"
        // Grid, Tileset, Image usw. kann raus, aus Kampagne sowie Gefecht.«
        // Zu Recht — Kartenname, Missionstext, Rasterweite, Kachelsatznummer
        // und Bildgroesse in Pixeln sind Angaben ueber die DATEI, nicht ueber
        // die Lage auf dem Schlachtfeld.
        //
        // ⚠ Sie werden NICHT geloescht: an ihnen haengen Prueflaeufe und
        // Belegbilder (»loaded <name>« mitlesen ist eine feste Regel, und die
        // Rastergroesse steht in mehreren Screenshots als Beleg). `--hud-debug`
        // holt sie zurueck. Ohne Kampagne und ohne Gefecht — also im blossen
        // Kartenbetrachter, wo man wirklich Karten durchblaettert — bleiben sie
        // ebenfalls stehen, samt der Blaetterzahl [n/N].
        bool imSpiel = UI.SkirmishSetup.Active || UI.SkirmishSetup.CampaignMission > 0;
        bool technik = HudDebug || !imSpiel;
        _hud.Text =
            (technik
                ? $"{MapLabel(name)}   \"{mission}\"\n" +
                  $"grid {dims}   tileset {tileset}   image {(int)size.X}x{(int)size.Y}px\n"
                : "") +
            (UI.SkirmishSetup.Active || !technik ? "" : $"[{_mapIndex + 1}/{MapNames.Length}]   ") +
            // ⚠ 14.08.2026, zweiter Schritt — DIE TASTENLEGENDE GEHT MIT.
            //
            // Der Spieler auf die Rueckfrage, ob auch sie verschwinden soll:
            // »Ja, ganz raus«. Sie haengt darum an demselben Schalter wie die
            // zwei technischen Zeilen: im Spiel weg, mit `--hud-debug` zurueck,
            // im blossen Kartenbetrachter unveraendert da — dort blaettert man
            // Karten durch und braucht sie.
            //
            // ⚠ Damit ist die Bedienung im Spiel NIRGENDS mehr aufgeschrieben.
            // Das ist eine bewusste Luecke und keine Loesung: solange es kein
            // Menue und keine Hilfe dafuer gibt, ist der einzige Weg zu den
            // Tasten diese Zeile — oder `--hud-debug`. Gehoert vermerkt, bevor
            // jemand sie sucht und fuer verloren haelt.
            (technik
                ? "click/drag=select  RIGHT-click=move  X=stop  E=eingraben  M=konstruieren  B=bauen  N=auswahl  O=forschen  K=reparieren  V=lagerausbau  C=prod.erw.  L=schienen  Y=flugzeuge  " +
                  "WASD+middle-drag=pan  wheel=zoom\n" +
                  "[ ]=map  F=fit  U=sprites  R=ranges  P=walkable  Z=zones  J=nebel  T=buildings  G=dots  H=karte  Q=rohstoffleiste  Tab=ereignis  Shift+rechts=anreihen  Esc=quit"
                : "");

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
        // ⚠ 14.08.2026 — DAS HAUPTZIEL, und es kommt aus dem Original.
        //
        // Der Spieler wollte im Kampagnen-HUD »das Ziel der Hauptmission sowie
        // der Nebenmission« sehen. Die Nebenmissionen zeigen wir schon; das
        // Hauptziel stand nirgends, weil es die ENDREGEL ist und die keinen Text
        // hat. Den Text hat OBJECTG.TXT, und die liegt nicht nur lose in einer
        // Installation, sondern in DATA1.CAB auf CD 1 — siehe
        // Campaign/MissionObjectives und Import/ObjectivesExporter.
        //
        // Es steht VOR den Untermissionen: das ist der Auftrag, der Rest ist
        // Zuarbeit.
        if (UI.SkirmishSetup.CampaignMission > 0)
        {
            string auftrag = Campaign.MissionObjectives.Line(UI.SkirmishSetup.CampaignMission);
            if (auftrag.Length > 0) obj = obj.Length > 0 ? auftrag + "   " + obj : auftrag;
        }
        // the per-player objective lists win over the flat one when the map
        // actually carries a player table
        // Die Ziele des MISSIONSSKRIPTS zuerst: eine Kampagnenkarte traegt
        // keine sec69, also lieferten die beiden Zeilen darunter dort nichts.
        string goals = _entities.MissionObjectiveLine();
        if (goals.Length == 0) goals = _entities.MissionLine();
        if (goals.Length == 0) goals = _entities.ObjectiveSummary();
        if (goals.Length > 0) obj += "   " + goals;
        // the design screen takes the HUD over while it is up — it needs the
        // room, and nothing else is being ordered meanwhile.
        // ⚠ Seit dem 11.08.2026 nur noch als Rückfall: das Fenster
        // »Erstellung« zeigt dasselbe an, und beides gleichzeitig wäre doppelt.
        if (_entities.Designer.Active && _designWindow is not { Visible: true })
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
        // ⚠ Die Leiste ZUERST: QuitIfDue() schreibt die Prüfzeilen, und
        // `--hud-check` fragt die Leiste. Stand der Aufruf danach, meldete die
        // erste Zeile jedes Laufs »nichts zu zeigen«, obwohl es etwas zu zeigen
        // gab — der Prüfstand las die Leiste, bevor sie ihre erste Probe hatte.
        UpdateResourceBar();
        QuitIfDue(delta);
        UpdateProductionPanel();
        UpdateUnitOrderBar();
        UpdateDesignWindow();
        UpdatePanelClock();
        UpdatePanelPortrait();
        UpdatePowerBars();
        PortraitCheckTick();
        TurretSeatCheckTick();
        StempelCheckTick();

        // Das Ohr steht in der Mitte des Bildes. Das Original rechnet jeden
        // Klang gegen genau diesen Punkt (`play_sound` @0x4047E0 zieht
        // halbe Bildbreite und Kameraecke ab) — siehe
        // Audio.SoundBankPlayer.ListenerCell.
        _entities?.SetListener(_camera.Position);

        _objTimer -= (float)delta;
        if (_objTimer <= 0f) { _objTimer = 0.5f; RefreshObjectives(); CheckEnd(); }

        // Prüfstand: das Abschlussfenster ohne durchgespielte Mission zeigen
        if (_endWindowDemo > 0f)
        {
            _endWindowDemo -= (float)delta;
            if (_endWindowDemo <= 0f)
                ShowEnd(true, "MISSION ERFUELLT (--end-window)", record: false);
        }

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
                        // ⚠⚠ IM SETZMODUS BEDEUTET DER LINKSKLICK ETWAS ANDERES:
                        // er waehlt den Bauplatz, statt eine Einheit zu waehlen.
                        // Genau so ist es im Original — der Klickbehandler
                        // @0x437FAB prueft dword[0x502ACC], BEVOR er zur
                        // gewoehnlichen Auswahl kommt, und nullt den Modus
                        // danach. Ohne diese Weiche waere der Setzmodus ein
                        // Knopf, der nichts bewirkt: der Klick ginge in die
                        // Auswahl und der Bauauftrag verfiele.
                        if (_leftDown && !_boxSelect && _entities.PlacementMode != 0 &&
                            _entities.CellAt(GetGlobalMousePosition()) is { } bc)
                            _entities.PlacementClick(bc.X, bc.Y);
                        else if (_leftDown && _boxSelect)
                            _entities.BoxSelect(RectFrom(_bandStart, GetGlobalMousePosition()),
                                                mb.ShiftPressed);
                        else if (_leftDown)
                            _entities.SelectAt(GetGlobalMousePosition(), mb.ShiftPressed);
                        _leftDown = false;
                        _boxSelect = false;
                        _entities.SetBand(null);
                        UpdateUnitOrderBar();
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
                        // Ein Rechtsklick bricht den Setzmodus ab — und gibt
                        // KEINEN Fahrbefehl. Das ist unsere Zutat (das Original
                        // hat für den Abbruch keinen gelesenen Weg); ohne sie
                        // säße der Spieler in einem Modus fest, den nur ein
                        // gültiger Bauplatz wieder beendet.
                        if (_rightDown && !_rightDrag && _entities.PlacementMode != 0)
                        {
                            _entities.CancelPlacement();
                            _entities.BuildOrderNote = "abgebrochen";
                            UpdateUnitOrderBar();
                            _rightDown = false; _rightDrag = false;
                            break;
                        }
                        if (_rightDown && !_rightDrag)
                        {
                            // ⚠ EINGABEN WERDEN DATEN. Der Klick setzt einen
                            // Befehl ab; gewirkt wird am nächsten Taktanfang
                            // (MapEntityLayer.SimTick → CommandTick). Vorher
                            // schrieb ein Mausklick mitten im Bildlauf direkt in
                            // die Einheiten — für ein Netzspiel nicht
                            // reparierbar, weil der zweite Rechner den Klick
                            // nicht hat und der Zeitpunkt an der Leitung hängt.
                            //
                            // Das Original macht es genauso: post() @0x4C1C50
                            // schickt auch den EIGENEN Befehl über DirectPlay und
                            // führt ihn erst aus, wenn er über Receive im Ring
                            // (0xB4FA38, 1000 Plätze) zurückkommt. Satzlänge
                            // 236 Byte, dreifach belegt. Siehe
                            // Simulation/Commands/CommandBridge.cs.
                            //
                            // PostAttack gibt wie IssueAttack false zurück, wenn
                            // der Klick kein Ziel getroffen hat — die Weiche
                            // bleibt dieselbe.
                            // ⚠ 17.08.2026 — STRG MACHT DARAUS »EINNEHMEN«
                            // (Fehler C9 und C11). Ohne diese Weiche gewinnt
                            // immer der Angriff: ein feindliches Gebaeude IST
                            // ein Ziel, also kam PostMove nie dran und die
                            // Einheit konnte die Tuerzelle gar nicht erreichen.
                            // Neutrale Gebaeude griff niemand an, deshalb ging
                            // es dort und nur dort. Die ganze Herleitung samt
                            // Messung steht bei PostCapture.
                            // ⚠ 18.08.2026 — EIN ANGEWÄHLTES FLUGZEUG BEKOMMT
                            // EIN FLUGZIEL. Gemeldet als »im Gefecht wäre es
                            // doch sinnvoll die Einheiten eigenständig zu
                            // steuern«. Die Weiche steht ganz vorn, weil ein
                            // Flugzeug und eine Bodenauswahl sich ausschliessen
                            // (SetPrimary) — es kann also nichts anderes meinen.
                            //
                            // Ob der Befehl überhaupt angenommen wird, entscheidet
                            // der BEHANDLER (nur ausserhalb der Kampagne, siehe
                            // CommandBridge.ApplyAirMove) — nicht diese Stelle.
                            // Eine Sperre in der Eingabe wäre auf der zweiten
                            // Maschine nicht vorhanden.
                            if (_entities.PostAirMove(GetGlobalMousePosition(),
                                                      mb.ShiftPressed) > 0)
                            {
                                _rightDown = false; _rightDrag = false;
                                break;
                            }
                            if (mb.CtrlPressed)
                            {
                                if (!_entities.PostCapture(GetGlobalMousePosition(), mb.ShiftPressed))
                                    _entities.PostMove(GetGlobalMousePosition(), mb.ShiftPressed);
                            }
                            else if (!_entities.PostAttack(GetGlobalMousePosition(), mb.ShiftPressed))
                                _entities.PostMove(GetGlobalMousePosition(), mb.ShiftPressed);
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
            // Die Bauvorschau folgt dem Zeiger, solange der Setzmodus laeuft —
            // das ist `0x421200` mit `Merken = 1`, die jede gepruefte Zelle mit
            // ihrem Ja/Nein nach 0xA32188 schreibt. Unsere Vorschau war schon
            // da (Simulation/Construction.cs), sie hatte nur nie einen Bediener.
            if (_entities.PlacementMode != 0 &&
                _entities.CellAt(GetGlobalMousePosition()) is { } hover)
                _entities.PlacementHover(hover.X, hover.Y);

            // A held left button with enough travel becomes a selection box
            // (a small wobble on click still selects the unit under the cursor).
            // ⚠ Im Setzmodus NICHT: dort ist der Zug kein Auswahlrahmen.
            if (_leftDown && !_boxSelect && _entities.PlacementMode == 0 &&
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
                // Cheat-Mode: Strg+Umschalt+G / M / S (Gott, Munition, Sprit).
                // Absichtlich sperrig, damit ihn niemand versehentlich trifft,
                // und jeder Wechsel meldet sich in der Statuszeile.
                case Key.G when key.CtrlPressed && key.ShiftPressed:
                    MapEntityLayer.CheatGodMode = !MapEntityLayer.CheatGodMode;
                    SayCheat(); break;
                case Key.M when key.CtrlPressed && key.ShiftPressed:
                    MapEntityLayer.CheatAmmo = !MapEntityLayer.CheatAmmo;
                    SayCheat(); break;
                case Key.S when key.CtrlPressed && key.ShiftPressed:
                    MapEntityLayer.CheatFuel = !MapEntityLayer.CheatFuel;
                    SayCheat(); break;
                // ⚠ 11.08.2026 — das Durchblaettern ist ein WERKZEUG des
                // Kartenbetrachters, kein Spielweg. In einer laufenden Partie
                // hat es nichts verloren: im Gefecht bot es alle 107
                // Kartendateien an, also auch die 33 Kampagnenkarten ("im
                // Gefecht sehe ich immer noch die Kampagnenauswahl"), und
                // mitten in einer Mission auf eine andere Karte zu springen
                // ergibt ohnehin keinen Sinn.
                case Key.Bracketright:
                    if (!UI.SkirmishSetup.Active) LoadMap(_mapIndex + 1);
                    break;
                case Key.Bracketleft:
                    if (!UI.SkirmishSetup.Active) LoadMap(_mapIndex - 1);
                    break;
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
                // ⚠ 17.08.2026 — Umschalt+B nimmt die LETZTE Bestellung zurück
                // und erstattet sie. Die Zeile muss VOR dem schlichten `Key.B`
                // stehen: C# nimmt in einem switch den ersten passenden Fall,
                // und `case Key.B:` ohne when würde Umschalt+B mitfangen.
                // Warum es die Taste überhaupt gibt: seit der Bauwarteschlange
                // wird beim EINREIHEN bezahlt, also muss sich eine Bestellung
                // auch wieder auflösen lassen (siehe Entity.BuildQueue).
                case Key.B when key.ShiftPressed: _entities.CancelBuild(); break;
                case Key.B: _entities.ProduceFromSelection(); break;
                case Key.N: _entities.CycleBuildMenu(); break;
                // the production list shares the display box with the info
                // text; I hides it for a look at what is underneath
                case Key.I: _hidePanelList = !_hidePanelList; UpdateProductionPanel(); break;
                // Q legt die Rohstoffleiste weg — siehe BuildResourceBar.
                case Key.Q: _hideResourceBar = !_hideResourceBar; UpdateResourceBar(); break;
                case Key.O: _entities.StartResearch(); break;
                case Key.K: _entities.StartRepair(); break;
                case Key.L: _entities.ToggleRail(); break;
                case Key.Y: _entities.LaunchAircraft(_entities.ViewPlayer); break;
                case Key.V: _entities.StartUpgrade(true); break;   // Lagerausbau
                case Key.C: _entities.StartUpgrade(false); break;  // Produktionserw.
                case Key.Escape:
                    // ⚠ Erst der Setzmodus, dann die Pause. Wer im Setzmodus
                    // Esc drueckt, will den Bauauftrag los und nicht das Spiel
                    // anhalten — und haette sonst keinen Weg zurueck ausser
                    // einem Klick, der etwas baut.
                    if (_entities.PlacementMode != 0)
                    {
                        _entities.CancelPlacement();
                        _entities.BuildOrderNote = "abgebrochen";
                        UpdateUnitOrderBar();
                        break;
                    }
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
