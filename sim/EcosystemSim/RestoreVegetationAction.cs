namespace EcosystemSim;

public sealed class RestoreVegetationAction : IScenarioAction
{
    public required int TileX { get; init; }
    public required int TileY { get; init; }
    public string Name => "Restore vegetation";
    public int Cost => 2;
    public bool SupportsScenario(ScenarioKind kind) =>
        kind is ScenarioKind.Sandbox or ScenarioKind.DroughtRecovery;

    public bool CanExecute(WorldState state, out string error)
    {
        var tile = state.Map.GetTile(TileX, TileY);
        if (TerrainStats.IsOcean(tile.Terrain))
        {
            error = "Vegetation restoration is only available on land.";
            return false;
        }

        var pools = AffectedPools(state).ToList();
        if (pools.Count == 0)
        {
            error = "This area has no vegetation resource pools.";
            return false;
        }
        if (pools.All(p => p.Amount >= p.Capacity))
        {
            error = "Vegetation is already at full capacity in this area.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    public string Execute(WorldState state)
    {
        var restored = 0;
        foreach (var pool in AffectedPools(state))
        {
            pool.Amount = pool.Capacity;
            restored++;
        }
        return $"Restored vegetation across {restored} resource pools around ({TileX},{TileY}).";
    }

    private IEnumerable<ResourcePool> AffectedPools(WorldState state)
    {
        var selected = state.Map.GetTile(TileX, TileY);
        var visited = new HashSet<Tile> { selected };
        var frontier = new List<Tile> { selected };
        for (var depth = 0; depth < 3; depth++)
        {
            frontier = frontier
                .SelectMany(state.Map.GetNeighbors)
                .Where(t => !TerrainStats.IsOcean(t.Terrain))
                .Where(visited.Add)
                .ToList();
        }
        return visited
            .SelectMany(t => t.Resources)
            .Where(r => r.Type == ResourceType.Food);
    }
}
