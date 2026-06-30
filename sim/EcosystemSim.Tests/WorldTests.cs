using Xunit;

namespace EcosystemSim.Tests;

public class WorldTests
{
    private static SpeciesDefinition BasicSpecies(string name = "TestDino") => new()
    {
        Name = name,
        ConsumptionRates = { [ResourceType.Food] = 1f },
        ReproductionRate = 0.1f,
        StarvationRate = 0.5f
    };

    private static ResourcePool AbundantFood(float amount = 10_000f) => new()
    {
        Type = ResourceType.Food,
        Amount = amount,
        Capacity = 10_000f,
        RegenPerTick = 500f
    };

    [Fact]
    public void Tick_AdvancesTickCount()
    {
        var world = new World();
        world.Tick();
        Assert.Equal(1, world.State.Tick);
    }

    [Fact]
    public void Tick_PopulationGrowsWhenResourcesAbundant()
    {
        var world = new World();
        world.State.Resources.Add(AbundantFood());
        world.State.Populations.Add(new Population { Species = BasicSpecies(), Count = 100 });

        world.Tick();

        Assert.True(world.State.Populations[0].Count > 100);
    }

    [Fact]
    public void Tick_PopulationDeclinesWhenResourcesDepleted()
    {
        var world = new World();
        world.State.Resources.Add(new ResourcePool
        {
            Type = ResourceType.Food,
            Amount = 0f,
            Capacity = 1000f,
            RegenPerTick = 0f
        });
        world.State.Populations.Add(new Population { Species = BasicSpecies(), Count = 100 });

        world.Tick();

        Assert.True(world.State.Populations[0].Count < 100);
    }

    [Fact]
    public void Tick_TwoSpeciesShareScarceResourceProportionally()
    {
        var world = new World();

        // only enough food for half the total demand
        world.State.Resources.Add(new ResourcePool
        {
            Type = ResourceType.Food,
            Amount = 100f,
            Capacity = 1000f,
            RegenPerTick = 0f
        });

        world.State.Populations.Add(new Population { Species = BasicSpecies("DinoA"), Count = 100 });
        world.State.Populations.Add(new Population { Species = BasicSpecies("DinoB"), Count = 100 });

        world.Tick();

        // both should decline (not just one wiping the other)
        Assert.True(world.State.Populations[0].Count < 100);
        Assert.True(world.State.Populations[1].Count < 100);
    }

    [Fact]
    public void Tick_PopulationNeverGoesBelowZero()
    {
        var world = new World();
        world.State.Populations.Add(new Population
        {
            Species = new SpeciesDefinition
            {
                Name = "Fragile",
                ConsumptionRates = { [ResourceType.Food] = 1f },
                StarvationRate = 1f
            },
            Count = 1
        });

        world.Tick();

        Assert.Equal(0, world.State.Populations[0].Count);
    }

    [Fact]
    public void Tick_ResourcePoolReplenishesEachTick()
    {
        var world = new World();
        world.State.Resources.Add(new ResourcePool
        {
            Type = ResourceType.Food,
            Amount = 0f,
            Capacity = 1000f,
            RegenPerTick = 100f
        });

        world.Tick();

        Assert.Equal(100f, world.State.Resources[0].Amount);
    }
}
