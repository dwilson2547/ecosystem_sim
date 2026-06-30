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
}
