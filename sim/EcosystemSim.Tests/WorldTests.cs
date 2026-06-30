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

    private static ResourcePool EmptyFood() => new()
    {
        Type = ResourceType.Food,
        Amount = 0f,
        Capacity = 1_000f,
        RegenPerTick = 0f
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
        var tile = world.State.Map.GetTile(0, 0);
        tile.Resources.Add(AbundantFood());
        tile.Populations.Add(new Population { Species = BasicSpecies(), Count = 100 });

        world.Tick();

        Assert.True(tile.Populations[0].Count > 100);
    }

    [Fact]
    public void Tick_PopulationDeclinesWhenResourcesDepleted()
    {
        var world = new World();
        var tile = world.State.Map.GetTile(0, 0);
        tile.Resources.Add(EmptyFood());
        tile.Populations.Add(new Population { Species = BasicSpecies(), Count = 100 });

        world.Tick();

        Assert.True(tile.Populations[0].Count < 100);
    }

    [Fact]
    public void Tick_TwoSpeciesShareScarceResourceProportionally()
    {
        var world = new World();
        var tile = world.State.Map.GetTile(0, 0);
        tile.Resources.Add(new ResourcePool
        {
            Type = ResourceType.Food,
            Amount = 100f,
            Capacity = 1_000f,
            RegenPerTick = 0f
        });
        tile.Populations.Add(new Population { Species = BasicSpecies("DinoA"), Count = 100 });
        tile.Populations.Add(new Population { Species = BasicSpecies("DinoB"), Count = 100 });

        world.Tick();

        Assert.True(tile.Populations[0].Count < 100);
        Assert.True(tile.Populations[1].Count < 100);
    }

    [Fact]
    public void Tick_PopulationNeverGoesBelowZero()
    {
        var world = new World();
        var tile = world.State.Map.GetTile(0, 0);
        tile.Populations.Add(new Population
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

        Assert.Equal(0, tile.Populations[0].Count);
    }

    [Fact]
    public void Tick_ResourcePoolReplenishesEachTick()
    {
        var world = new World();
        var tile = world.State.Map.GetTile(0, 0);
        tile.Resources.Add(new ResourcePool
        {
            Type = ResourceType.Food,
            Amount = 0f,
            Capacity = 1_000f,
            RegenPerTick = 100f
        });

        world.Tick();

        Assert.Equal(100f, tile.Resources[0].Amount);
    }

    [Fact]
    public void Tick_PopulationsOnDifferentTilesDoNotShareResources()
    {
        var world = new World();

        var richTile = world.State.Map.GetTile(0, 0);
        richTile.Resources.Add(AbundantFood());
        richTile.Populations.Add(new Population { Species = BasicSpecies("Rich"), Count = 100 });

        var poorTile = world.State.Map.GetTile(5, 5);
        poorTile.Resources.Add(EmptyFood());
        poorTile.Populations.Add(new Population { Species = BasicSpecies("Poor"), Count = 100 });

        world.Tick();

        Assert.True(richTile.Populations[0].Count > 100, "rich tile population should grow");
        Assert.True(poorTile.Populations[0].Count < 100, "poor tile population should shrink");
    }

    [Fact]
    public void Map_NeighborsAreCardinalOnly()
    {
        var map = new WorldMap(3, 3);
        var center = map.GetTile(1, 1);
        var neighbors = map.GetNeighbors(center).ToList();

        Assert.Equal(4, neighbors.Count);
        Assert.DoesNotContain(map.GetTile(0, 0), neighbors); // diagonal excluded
        Assert.DoesNotContain(map.GetTile(1, 1), neighbors); // self excluded
    }

    [Fact]
    public void Map_CornerTileHasTwoNeighbors()
    {
        var map = new WorldMap(3, 3);
        var neighbors = map.GetNeighbors(0, 0).ToList();
        Assert.Equal(2, neighbors.Count);
    }
}
