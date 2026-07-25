namespace EcosystemSim;

public sealed class RestoreGrassAction : IScenarioAction
{
    public required int TileX { get; init; }
    public required int TileY { get; init; }
    public string Name => "Restore grass";
    public int Cost => 2;

    public bool CanExecute(WorldState state, out string error)
    {
        var tile = state.Map.GetTile(TileX, TileY);
        if (tile.Terrain != TerrainType.Plains)
        {
            error = "Grass restoration is only available on Plains tiles.";
            return false;
        }

        var pools = AffectedGrassPools(state).ToList();
        if (pools.Count == 0)
        {
            error = "This area has no Plains Graze resource pools.";
            return false;
        }

        if (pools.All(pool => pool.Amount >= pool.Capacity))
        {
            error = "Grass is already at full capacity in this area.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    public string Execute(WorldState state)
    {
        var restored = 0;
        foreach (var pool in AffectedGrassPools(state))
        {
            pool.Amount = pool.Capacity;
            restored++;
        }
        return $"Fully restored grass across {restored} Plains tiles around ({TileX},{TileY}).";
    }

    private IEnumerable<ResourcePool> AffectedGrassPools(WorldState state)
    {
        var selected = state.Map.GetTile(TileX, TileY);
        var visited = new HashSet<Tile> { selected };
        var frontier = new List<Tile> { selected };
        for (var depth = 0; depth < 4; depth++)
        {
            frontier = frontier
                .SelectMany(state.Map.GetNeighbors)
                .Where(visited.Add)
                .ToList();
        }
        return visited
            .Where(t => t.Terrain == TerrainType.Plains)
            .SelectMany(t => t.Resources)
            .Where(r => r.FoodSubtype == FoodSubtype.Graze);
    }
}
