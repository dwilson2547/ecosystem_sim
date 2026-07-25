namespace EcosystemSim;

public sealed class CullLocustsAction : IScenarioAction
{
    public required int TileX { get; init; }
    public required int TileY { get; init; }
    public string Name => "Cull locusts";
    public int Cost => 1;
    public bool SupportsScenario(ScenarioKind kind) =>
        kind is ScenarioKind.Sandbox or ScenarioKind.LocustPlague;

    public bool CanExecute(WorldState state, out string error)
    {
        var hasLocusts = AffectedTiles(state).SelectMany(t => t.Populations)
            .Any(p => p.Count > 0 && p.Species.EffectiveRootName == "Locust");
        error = hasLocusts
            ? string.Empty
            : "No living locust population is present on this tile or its neighbors.";
        return hasLocusts;
    }

    public string Execute(WorldState state)
    {
        var removed = 0;
        foreach (var pop in AffectedTiles(state).SelectMany(t => t.Populations)
                     .Where(p => p.Count > 0 && p.Species.EffectiveRootName == "Locust"))
        {
            var deaths = Math.Max(1, (int)Math.Ceiling(pop.Count * 0.99f));
            pop.Count -= deaths;
            removed += deaths;
        }
        return $"Culled {removed} locusts around ({TileX},{TileY}).";
    }

    private IEnumerable<Tile> AffectedTiles(WorldState state)
    {
        var selected = state.Map.GetTile(TileX, TileY);
        var visited = new HashSet<Tile> { selected };
        var frontier = new List<Tile> { selected };
        for (var depth = 0; depth < 2; depth++)
        {
            frontier = frontier
                .SelectMany(state.Map.GetNeighbors)
                .Where(visited.Add)
                .ToList();
        }
        return visited;
    }
}
