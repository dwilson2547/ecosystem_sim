namespace EcosystemSim;

public class World
{
    public WorldState State { get; } = new();

    public void Tick()
    {
        RegenerateResources();
        DistributeResources();
        ApplyGrowthAndDeath();
        State.Tick++;
    }

    public void Apply(IWorldCommand command) => command.Execute(State);

    private void RegenerateResources()
    {
        foreach (var pool in State.Resources)
            pool.Regen();
    }

    private void DistributeResources()
    {
        foreach (var pop in State.Populations)
            pop.LastSatisfaction = 1f;

        foreach (var resourceType in Enum.GetValues<ResourceType>())
        {
            var pool = State.Resources.FirstOrDefault(r => r.Type == resourceType);

            var demands = State.Populations
                .Select(p => (pop: p, demand: p.Count * p.Species.ConsumptionRates.GetValueOrDefault(resourceType)))
                .ToList();

            var totalDemand = demands.Sum(d => d.demand);
            if (totalDemand == 0) continue;

            // if the pool is missing entirely, treat supply as zero
            var supplyRatio = pool is not null ? Math.Min(pool.Amount / totalDemand, 1f) : 0f;

            foreach (var (pop, demand) in demands)
            {
                if (demand == 0) continue;

                var received = demand * supplyRatio;
                pool?.Consume(received);

                // track the worst single-resource satisfaction across all resource types
                pop.LastSatisfaction = Math.Min(pop.LastSatisfaction, received / demand);
            }
        }
    }

    private void ApplyGrowthAndDeath()
    {
        foreach (var pop in State.Populations)
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
