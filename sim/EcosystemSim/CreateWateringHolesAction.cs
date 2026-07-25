namespace EcosystemSim;

public sealed class CreateWateringHolesAction : IScenarioAction
{
    public required int TileX { get; init; }
    public required int TileY { get; init; }
    public string Name => "Create watering holes";
    public int Cost => 2;
    public bool SupportsScenario(ScenarioKind kind) =>
        kind is ScenarioKind.Sandbox or ScenarioKind.DroughtRecovery;

    public bool CanExecute(WorldState state, out string error)
    {
        var tile = state.Map.GetTile(TileX, TileY);
        if (TerrainStats.IsOcean(tile.Terrain) || tile.Terrain == TerrainType.River)
        {
            error = "Watering holes can only be created on dry land.";
            return false;
        }

        var pools = AffectedTiles(state)
            .Select(t => t.Resources.FirstOrDefault(r => r.Type == ResourceType.Water))
            .Where(p => p is not null)
            .Cast<ResourcePool>()
            .ToList();
        if (pools.Count > 0 && pools.All(p => p.Amount >= Math.Max(p.Capacity, 80f)))
        {
            error = "Freshwater is already full across this area.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    public string Execute(WorldState state)
    {
        var restored = 0;
        foreach (var tile in AffectedTiles(state))
        {
            var pool = tile.Resources.FirstOrDefault(r => r.Type == ResourceType.Water);
            if (pool is null)
            {
                pool = new ResourcePool
                {
                    Type = ResourceType.Water,
                    Amount = 80f,
                    Capacity = 80f,
                    RegenPerTick = 4f,
                };
                tile.Resources.Add(pool);
            }
            else
            {
                pool.Capacity = Math.Max(pool.Capacity, 80f);
                pool.RegenPerTick = Math.Max(pool.RegenPerTick, 4f);
                pool.Amount = pool.Capacity;
            }
            restored++;
        }

        return $"Created watering holes across {restored} land tiles around ({TileX},{TileY}).";
    }

    private IEnumerable<Tile> AffectedTiles(WorldState state)
    {
        var selected = state.Map.GetTile(TileX, TileY);
        var visited = new HashSet<Tile> { selected };
        var frontier = new List<Tile> { selected };
        for (var depth = 0; depth < 3; depth++)
        {
            frontier = frontier
                .SelectMany(state.Map.GetNeighbors)
                .Where(t => !TerrainStats.IsOcean(t.Terrain) && t.Terrain != TerrainType.River)
                .Where(visited.Add)
                .ToList();
        }
        return visited;
    }
}
