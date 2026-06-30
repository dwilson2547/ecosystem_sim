namespace EcosystemSim;

public class World
{
    public WorldState State { get; } = new();

    public void Tick()
    {
        foreach (var tile in State.Map.AllTiles())
        {
            RegenerateResources(tile);
            DistributeResources(tile);
            ApplyGrowthAndDeath(tile);
        }

        Migrate();
        State.Tick++;
    }

    public void Apply(IWorldCommand command) => command.Execute(State);

    private static void RegenerateResources(Tile tile)
    {
        foreach (var pool in tile.Resources)
            pool.Regen();
    }

    private static void DistributeResources(Tile tile)
    {
        foreach (var pop in tile.Populations)
            pop.LastSatisfaction = 1f;

        foreach (var resourceType in Enum.GetValues<ResourceType>())
        {
            var pool = tile.Resources.FirstOrDefault(r => r.Type == resourceType);

            var demands = tile.Populations
                .Select(p => (pop: p, demand: p.Count * p.Species.ConsumptionRates.GetValueOrDefault(resourceType)))
                .ToList();

            var totalDemand = demands.Sum(d => d.demand);
            if (totalDemand == 0) continue;

            var supplyRatio = pool is not null ? Math.Min(pool.Amount / totalDemand, 1f) : 0f;

            foreach (var (pop, demand) in demands)
            {
                if (demand == 0) continue;

                var received = demand * supplyRatio;
                pool?.Consume(received);

                pop.LastSatisfaction = Math.Min(pop.LastSatisfaction, received / demand);
            }
        }
    }

    private static void ApplyGrowthAndDeath(Tile tile)
    {
        foreach (var pop in tile.Populations)
        {
            var satisfaction = pop.LastSatisfaction;

            if (satisfaction >= 1f)
            {
                pop.Count += (int)(pop.Count * pop.Species.ReproductionRate);
            }
            else
            {
                var deficit = 1f - satisfaction;
                var deaths = (int)Math.Ceiling(pop.Count * pop.Species.StarvationRate * deficit);
                pop.Count = Math.Max(0, pop.Count - deaths);
            }
        }
    }

    private void Migrate()
    {
        // collect moves first so relocations don't affect each other within the same tick
        var moves = new List<(Population pop, Tile from, Tile to)>();

        foreach (var tile in State.Map.AllTiles())
        {
            foreach (var pop in tile.Populations)
            {
                if (pop.Count == 0) continue;
                if (pop.LastSatisfaction >= pop.Species.MigrationThreshold) continue;

                var lacking = MostLackingResource(pop, tile);
                if (lacking is null) continue;

                var destination = BestNeighborFor(tile, lacking.Value);
                if (destination is not null)
                    moves.Add((pop, tile, destination));
            }
        }

        foreach (var (pop, from, to) in moves)
        {
            from.Populations.Remove(pop);

            var existing = to.Populations.FirstOrDefault(p => p.Species == pop.Species);
            if (existing is not null)
                existing.Count += pop.Count;
            else
                to.Populations.Add(pop);
        }
    }

    // returns the resource type the population is most starved of on this tile
    private static ResourceType? MostLackingResource(Population pop, Tile tile)
    {
        ResourceType? worst = null;
        var worstRatio = float.MaxValue;

        foreach (var (resourceType, rate) in pop.Species.ConsumptionRates)
        {
            if (rate == 0) continue;
            var pool = tile.Resources.FirstOrDefault(r => r.Type == resourceType);
            var ratio = pool is null ? 0f : pool.Amount / (pop.Count * rate);
            if (ratio < worstRatio)
            {
                worstRatio = ratio;
                worst = resourceType;
            }
        }

        return worst;
    }

    // returns the neighboring tile with the most of the given resource, or null if no neighbor is better
    private Tile? BestNeighborFor(Tile current, ResourceType resourceType)
    {
        var currentAmount = current.Resources
            .FirstOrDefault(r => r.Type == resourceType)?.Amount ?? 0f;

        return State.Map.GetNeighbors(current)
            .Where(n => (n.Resources.FirstOrDefault(r => r.Type == resourceType)?.Amount ?? 0f) > currentAmount)
            .OrderByDescending(n => n.Resources.FirstOrDefault(r => r.Type == resourceType)?.Amount ?? 0f)
            .FirstOrDefault();
    }
}
