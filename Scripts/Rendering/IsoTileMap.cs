namespace AkteEuropaReborn.Rendering;

using System.Collections.Generic;
using Godot;
using AkteEuropaReborn.Simulation;
using SimGridMap = AkteEuropaReborn.Simulation.GridMap;

[GlobalClass]
public partial class IsoTileMap : Node2D
{
    private readonly List<ColorRect> _tiles = new();

    public void BuildFromGrid(SimGridMap grid)
    {
        try
        {
            const int step = 2;
            foreach (var child in GetChildren())
                child.QueueFree();
            _tiles.Clear();

            var terrainColors = new Color[]
            {
                new Color(0.25f, 0.45f, 0.20f), // 0 grass
                new Color(0.55f, 0.45f, 0.30f), // 1 dirt/road
                new Color(0.20f, 0.35f, 0.55f), // 2 water
                new Color(0.45f, 0.45f, 0.45f), // 3 rock
            };

            for (int cy = 0; cy < SimGridMap.MapHeightTiles; cy += step)
            for (int cx = 0; cx < SimGridMap.MapWidthTiles; cx += step)
            {
                var terrain = grid.GetTerrain(cx, cy);
                var color = terrain < terrainColors.Length ? terrainColors[terrain] : Colors.Magenta;
                var rect = new ColorRect
                {
                    Color = color,
                    Position = grid.TileToWorld(cx, cy) - new Vector2(step * 8f, step * 4f),
                    Size = new Vector2(step * 16f, step * 8f),
                    ZIndex = -1000
                };
                AddChild(rect);
                _tiles.Add(rect);
            }
            GD.Print($"IsoTileMap built: {_tiles.Count} tiles");
        }
        catch (System.Exception e)
        {
            GD.PrintErr($"IsoTileMap.BuildFromGrid error: {e.GetType().Name}: {e.Message}");
        }
    }
}
