namespace AkteEuropaReborn.Core;

using System;
using System.IO;
using Godot;
using AkteEuropaReborn.Simulation;
using AkteEuropaReborn.Simulation.Components;
using AkteEuropaReborn.Simulation.ECS;
using AkteEuropaReborn.Core.Math;
using AkteEuropaReborn.Rendering;
using AkteEuropaReborn.Legacy;
using GridMap = AkteEuropaReborn.Simulation.GridMap;

[GlobalClass]
public partial class Game : Node
{
    public static Game Instance { get; private set; }
    public SimulationState SimState { get; private set; }
    public GameManager GameManager { get; private set; }
    public Camera2D GameCamera => _gameCamera;

    private Camera2D _gameCamera;
    private CanvasLayer _uiLayer;
    private Node2D _gameWorld;
    private Node2D _unitLayer;
    private Node2D _effectLayer;
    private IsoTileMap _tileMap;
    private ImageTexture[] _unitTextures = System.Array.Empty<ImageTexture>();
    private Godot.Collections.Array<ImageTexture> _terrainTextures = new();

    private double _accumulator = 0;
    private const double TargetTickRate = 1.0 / SimulationState.TicksPerSecond;
    private int _framesSinceTick = 0;

    public override void _Notification(int what)
    {
        if (what == NotificationSceneInstantiated)
            Instance = this;
    }

    public override void _Ready()
    {
        InitializeCore();
        InitializeSceneTree();
        InitializeGame();
        GD.Print("Akte Europa Reborn initialized");
    }

    private void InitializeCore()
    {
        var configPath = "user://config.json";
        if (Godot.FileAccess.FileExists(configPath))
            LoadConfig(configPath);

        SimState = new SimulationState();
        SimState.RandomSeed = (uint)Time.GetTicksMsec();

        GameManager = new GameManager();
        GameManager.Name = "GameManager";
        AddChild(GameManager);
    }

    private void InitializeSceneTree()
    {
        _gameWorld = new Node2D { Name = "GameWorld" };
        AddChild(_gameWorld);

        _tileMap = new IsoTileMap { Name = "TileMap" };
        _gameWorld.AddChild(_tileMap);

        _unitLayer = new Node2D { Name = "Units" };
        _gameWorld.AddChild(_unitLayer);

        _effectLayer = new Node2D { Name = "Effects" };
        _gameWorld.AddChild(_effectLayer);

        _gameCamera = new Camera2D
        {
            Name = "GameCamera",
            AnchorMode = Camera2D.AnchorModeEnum.DragCenter,
            Zoom = new Vector2(2f, 2f),
            Position = SimState.Grid.TileToWorld(128, 128)
        };
        _gameWorld.AddChild(_gameCamera);
        _gameCamera.MakeCurrent();

        _uiLayer = new CanvasLayer { Name = "UILayer", Layer = 10 };
        AddChild(_uiLayer);

        var uiScene = GD.Load<PackedScene>("res://Scenes/UI/MainUI.tscn");
        if (uiScene != null)
        {
            var ui = uiScene.Instantiate();
            _uiLayer.AddChild(ui);
        }
    }

    private void InitializeGame()
    {
        LoadUnitSprites();
        GenerateTestMap();
        SpawnTestUnits();
    }

    private void LoadUnitSprites()
    {
        var spritePath = Core.Content.Path("DATA/01.CWP");
        var palPath = Core.Content.Path("DATA/01.PAL");
        if (!Godot.FileAccess.FileExists(spritePath) || !Godot.FileAccess.FileExists(palPath))
        {
            GD.PrintErr("Akte Europa sprite assets not found, using placeholder units.");
            return;
        }
        try
        {
            var palette = LegacyReaders.LoadPal(palPath);
            var frames = LegacyReaders.DecodeCwpFrames(spritePath, palette);
            var arr = new Godot.Collections.Array<ImageTexture>();
            foreach (var frame in frames)
            {
                arr.Add(ImageTexture.CreateFromImage(frame));
            }
            _terrainTextures = arr;
            GD.Print($"Loaded {_terrainTextures.Count} Akte Europa terrain frames from 01.CWP");
        }
        catch (Exception e)
        {
            GD.PrintErr($"Failed to load Akte Europa sprites: {e.GetType().Name}: {e.Message}");
            GD.PrintErr(e.StackTrace);
        }
    }

    private void CreateUnitVisual(Entity entity, Fixed x, Fixed y, int frameIndex)
    {
        var sprite = new Sprite2D { Name = entity.ToString() };
        var worldPos = SimState.Grid.TileToWorld(x.ToInt(), y.ToInt());
        sprite.Position = worldPos;
        sprite.ZIndex = 100;
        sprite.Visible = true;

        sprite.Texture = PlaceholderTexture(x);
        sprite.Scale = new Vector2(2f, 2f);
        _unitLayer.AddChild(sprite);
        if (entity.GetHashCode() % 7 == 0)
            GD.Print($"Unit {entity} at world {worldPos}");
    }

    private static ImageTexture PlaceholderTexture(Fixed seed)
    {
        var img = Image.CreateEmpty(16, 16, false, Image.Format.Rgba8);
        var hue = (float)((seed.ToInt() * 37 % 360) / 360f);
        var color = Color.FromHsv(hue, 0.7f, 0.9f);
        img.Fill(color);
        return ImageTexture.CreateFromImage(img);
    }

    private void GenerateTestMap()
    {
        var grid = SimState.Grid;
        for (int y = 0; y < GridMap.MapHeightTiles; y++)
        for (int x = 0; x < GridMap.MapWidthTiles; x++)
        {
            grid.SetTerrain(x, y, 0);
            grid.SetPassable(x, y, UnitType.Infantry, true);
            grid.SetPassable(x, y, UnitType.LightVehicle, true);
            grid.SetPassable(x, y, UnitType.HeavyVehicle, true);
            grid.SetPassable(x, y, UnitType.Aircraft, true);
        }

        for (int x = 50; x < 60; x++)
        for (int y = 0; y < 256; y++)
        {
            grid.SetTerrain(x, y, 1);
            grid.SetPassable(x, y, UnitType.Infantry, false);
            grid.SetPassable(x, y, UnitType.LightVehicle, false);
            grid.SetPassable(x, y, UnitType.HeavyVehicle, false);
        }

        _tileMap.BuildFromGrid(grid);
    }

    private void SpawnTestUnits()
    {
        var world = SimState.World;
        for (int i = 0; i < 10; i++)
        {
            var entity = world.Create();
            world.AddComponent(entity, new Position(Fixed.FromInt(124 + i * 2), Fixed.FromInt(124)));
            world.AddComponent(entity, new Velocity());
            world.AddComponent(entity, new Rotation());
            world.AddComponent(entity, new UnitStats
            {
                MaxHealth = 100,
                MoveSpeed = Fixed.FromFloat(3f),
                TurnRate = Fixed.FromFloat(180f),
                AttackDamage = 10,
                AttackRange = Fixed.FromFloat(3f),
                AttackCooldown = Fixed.FromFloat(1f),
                SightRange = 5,
                Type = UnitType.Infantry,
                Faction = Faction.Player1
            });
            world.AddComponent(entity, new Health { Current = 100, Max = 100 });
            world.AddComponent(entity, new Owner { Faction = Faction.Player1, PlayerIndex = 0 });
            CreateUnitVisual(entity, Fixed.FromInt(124 + i * 2), Fixed.FromInt(124), i % 8);
        }

        for (int i = 0; i < 5; i++)
        {
            var entity = world.Create();
            world.AddComponent(entity, new Position(Fixed.FromInt(132 + i * 2), Fixed.FromInt(132)));
            world.AddComponent(entity, new Velocity());
            world.AddComponent(entity, new Rotation());
            world.AddComponent(entity, new UnitStats
            {
                MaxHealth = 150,
                MoveSpeed = Fixed.FromFloat(2.5f),
                TurnRate = Fixed.FromFloat(120f),
                AttackDamage = 15,
                AttackRange = Fixed.FromFloat(4f),
                AttackCooldown = Fixed.FromFloat(1.5f),
                SightRange = 6,
                Type = UnitType.LightVehicle,
                Faction = Faction.Player2
            });
            world.AddComponent(entity, new Health { Current = 150, Max = 150 });
            world.AddComponent(entity, new Owner { Faction = Faction.Player2, PlayerIndex = 1 });
            world.AddComponent(entity, new AiState { Behavior = AiBehavior.Guard, AggressionLevel = 100 });
            CreateUnitVisual(entity, Fixed.FromInt(132 + i * 2), Fixed.FromInt(132), (i + 2) % 8);
        }

        var ore = world.Create();
        world.AddComponent(ore, new Position(Fixed.FromInt(50), Fixed.FromInt(50)));
        world.AddComponent(ore, new ResourceStorage { Credits = 5000, Energy = 0, MaxCredits = 5000, MaxEnergy = 0 });

        GD.Print($"Spawned units. Total entities: {world.EntityCount}, unit visuals: {_unitLayer.GetChildCount()}");
    }

    public override void _PhysicsProcess(double delta)
    {
        _accumulator += delta;
        _framesSinceTick++;
        while (_accumulator >= TargetTickRate)
        {
            SimState.CurrentTick++;
            try { Simulation.Step(SimState); }
            catch (Exception e) { GD.PrintErr($"Sim error tick {SimState.CurrentTick}: {e.GetType().Name}: {e.Message}"); }
            _accumulator -= TargetTickRate;
            _framesSinceTick = 0;
        }
        float alpha = (float)(_accumulator / TargetTickRate);
        try { UpdateVisuals(alpha); }
        catch (Exception e) { GD.PrintErr($"Visual error: {e.GetType().Name}: {e.Message}"); }
    }

    private void UpdateVisuals(float alpha)
    {
        foreach (var (entity, pos, vel) in SimState.World.Query<Position, Velocity>().Iterate())
        {
            if (_unitLayer.FindChild(entity.ToString()) is Node2D visual)
            {
                var current = visual.GlobalPosition;
                var target = SimState.Grid.TileToWorld(pos.X.ToInt(), pos.Y.ToInt());
                visual.GlobalPosition = current.Lerp(target, alpha);
            }
        }
    }

    public override void _Process(double delta)
    {
        HandleCameraInput(delta);
    }

    private void HandleCameraInput(double delta)
    {
        var move = Vector2.Zero;
        if (Input.IsKeyPressed(Key.W) || Input.IsKeyPressed(Key.Up)) move.Y -= 1;
        if (Input.IsKeyPressed(Key.S) || Input.IsKeyPressed(Key.Down)) move.Y += 1;
        if (Input.IsKeyPressed(Key.A) || Input.IsKeyPressed(Key.Left)) move.X -= 1;
        if (Input.IsKeyPressed(Key.D) || Input.IsKeyPressed(Key.Right)) move.X += 1;
        if (move == Vector2.Zero && InputManager.Instance != null)
            move = InputManager.Instance.GetMoveVector();

        if (move != Vector2.Zero)
        {
            _gameCamera.GlobalPosition += move.Normalized() * (float)delta * 800f;
        }

        float zoom = 0;
        if (Input.IsKeyPressed(Key.Equal) || Input.IsKeyPressed(Key.KpAdd)) zoom += 1;
        if (Input.IsKeyPressed(Key.Minus) || Input.IsKeyPressed(Key.KpSubtract)) zoom -= 1;
        if (zoom == 0 && InputManager.Instance != null)
            zoom = InputManager.Instance.GetZoomDelta();
        if (zoom != 0)
        {
            var z = (_gameCamera.Zoom * (1f + zoom * 0.1f)).Clamp(
                new Vector2(0.1f, 0.1f),
                new Vector2(8f, 8f)
            );
            _gameCamera.Zoom = z;
        }
    }

    public void LoadLegacySave(string path)
    {
        var palette = LegacyReaders.LoadPal(Core.Content.Path("default.pal"));
        var map = LegacyReaders.LoadLev(path);
        ApplyLegacyMap(map, palette);
    }

    private void ApplyLegacyMap(LegacyReaders.LevelData map, Color[] palette)
    {
        for (int y = 0; y < map.Height; y++)
        for (int x = 0; x < map.Width; x++)
        {
            var terrain = map.Terrain[y * map.Width + x];
            SimState.Grid.SetTerrain(x, y, terrain);
        }
        foreach (var obj in map.Objects)
        {
            SpawnLegacyObject(obj);
        }
        _tileMap.BuildFromGrid(SimState.Grid);
    }

    private void SpawnLegacyObject(LegacyReaders.MapObject obj) { }

    private void LoadConfig(string path) { }
    public void SaveConfig(string path) { }
    public void TogglePause() => SimState.Phase = SimState.Phase == GamePhase.Paused ? GamePhase.Playing : GamePhase.Paused;
    public void SetGameSpeed(float multiplier) { }
}