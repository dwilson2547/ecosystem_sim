using Godot;
using EcosystemSim;
using System.Collections.Generic;

namespace EcosystemGame;

/// <summary>
/// Builds one HexTile child per sim tile and refreshes them on each tick.
/// Coordinate system matches WorldMap.GetNeighbors: pointy-top hexes, odd rows shift right.
/// </summary>
public partial class HexMapRenderer : Node2D
{
    [Export] public float HexSize { get; set; } = 60f;

    private readonly Dictionary<(int col, int row), HexTile> _tiles = [];
    private HexTile? _selectedTile;

    public override void _Ready()
    {
        BuildMap();
        SimManager.Instance.Ticked += RefreshAll;
    }

    private void BuildMap()
    {
        var map = SimManager.Instance.World.State.Map;
        for (var y = 0; y < map.Height; y++)
        for (var x = 0; x < map.Width;  x++)
        {
            var hexTile = new HexTile { SimTile = map.GetTile(x, y), HexSize = HexSize };
            hexTile.Position = HexToPixel(x, y);
            AddChild(hexTile);
            _tiles[(x, y)] = hexTile;
        }
    }

    private void RefreshAll()
    {
        foreach (var tile in _tiles.Values)
            tile.Refresh();
    }

    public Vector2 HexToPixel(int col, int row)
    {
        // odd-r offset: odd rows are indented right by half a hex-width
        var px = HexSize * MathF.Sqrt(3f) * (col + (row % 2 == 1 ? 0.5f : 0f));
        var py = HexSize * 1.5f * row;
        return new Vector2(px, py);
    }

    /// <summary>
    /// Returns the sim tile nearest to worldPos, or null if the click is off-map.
    /// Checks a 3×3 candidate grid around the coarse estimate and picks the closest center.
    /// </summary>
    public Tile? PixelToTile(Vector2 worldPos)
    {
        var map  = SimManager.Instance.World.State.Map;
        float sq3 = MathF.Sqrt(3f);

        int rowEst = Mathf.RoundToInt(worldPos.Y / (HexSize * 1.5f));
        float colOffset = (rowEst % 2 == 1) ? 0.5f : 0f;
        int colEst = Mathf.RoundToInt(worldPos.X / (HexSize * sq3) - colOffset);

        Tile?  best  = null;
        float  bestD = HexSize * 1.05f; // reject clicks further than just outside the outer radius

        for (var dr = -1; dr <= 1; dr++)
        for (var dc = -1; dc <= 1; dc++)
        {
            int col = colEst + dc, row = rowEst + dr;
            if (col < 0 || col >= map.Width || row < 0 || row >= map.Height) continue;
            float d = worldPos.DistanceTo(HexToPixel(col, row));
            if (d < bestD) { bestD = d; best = map.GetTile(col, row); }
        }
        return best;
    }

    /// <summary>Highlights the given tile and unhighlights any previously selected one.</summary>
    public void SelectTile(Tile? tile)
    {
        if (_selectedTile is not null)
            _selectedTile.SetSelected(false);

        _selectedTile = tile is not null && _tiles.TryGetValue((tile.X, tile.Y), out var hexTile)
            ? hexTile : null;

        _selectedTile?.SetSelected(true);
    }
}
