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
    public void Tick_PopulationMigratesWhenResourceMissing()
    {
        var world = new World();
        var dryTile = world.State.Map.GetTile(0, 0); // no water
        var wetTile = world.State.Map.GetTile(1, 0); // has water

        var species = new SpeciesDefinition
        {
            Name = "WaterSeeker",
            ConsumptionRates = { [ResourceType.Water] = 1f },
            MigrationThreshold = 0.9f,
            StarvationRate = 0.01f // low so population survives long enough to migrate
        };

        wetTile.Resources.Add(new ResourcePool { Type = ResourceType.Water, Amount = 1_000f, Capacity = 1_000f, RegenPerTick = 50f });
        dryTile.Populations.Add(new Population { Species = species, Count = 10 });

        world.Tick();

        Assert.Empty(dryTile.Populations.Where(p => p.Count > 0));
        Assert.Single(wetTile.Populations.Where(p => p.Count > 0));
    }

    [Fact]
    public void Tick_MigratingPopulationMergesWithSameSpeciesOnDestination()
    {
        var world = new World();
        var dryTile = world.State.Map.GetTile(0, 0);
        var wetTile = world.State.Map.GetTile(1, 0);

        var species = new SpeciesDefinition
        {
            Name = "Merger",
            ConsumptionRates = { [ResourceType.Water] = 1f },
            MigrationThreshold = 0.9f,
            StarvationRate = 0.01f
        };

        wetTile.Resources.Add(new ResourcePool { Type = ResourceType.Water, Amount = 1_000f, Capacity = 1_000f, RegenPerTick = 50f });
        dryTile.Populations.Add(new Population { Species = species, Count = 10 });
        wetTile.Populations.Add(new Population { Species = species, Count = 20 });

        world.Tick();

        // dry tile should be empty, wet tile should have exactly one merged population
        Assert.Empty(dryTile.Populations.Where(p => p.Count > 0));
        Assert.Single(wetTile.Populations.Where(p => p.Count > 0));
        Assert.True(wetTile.Populations.Single(p => p.Count > 0).Count > 20, "merged count should exceed the destination's original count");
    }

    [Fact]
    public void Tick_PopulationDoesNotMigrateWhenSatisfied()
    {
        var world = new World();
        var tile = world.State.Map.GetTile(0, 0);
        var neighbor = world.State.Map.GetTile(1, 0);

        tile.Resources.Add(AbundantFood());
        neighbor.Resources.Add(AbundantFood());
        tile.Populations.Add(new Population { Species = BasicSpecies(), Count = 10 });

        world.Tick();

        Assert.Single(tile.Populations.Where(p => p.Count > 0));
        Assert.Empty(neighbor.Populations.Where(p => p.Count > 0));
    }

    [Fact]
    public void Tick_FactionsInProximityBuildTension()
    {
        var world = new World();
        var species = BasicSpecies();

        var factionA = new Faction { Name = "A", PrimarySpecies = species };
        var factionB = new Faction { Name = "B", PrimarySpecies = species };
        world.State.Factions.AddRange([factionA, factionB]);

        // place them adjacent — will compete for same food resource
        var popA = new Population { Species = species, Count = 10 };
        var popB = new Population { Species = species, Count = 10 };
        factionA.AddPopulation(popA);
        factionB.AddPopulation(popB);
        world.State.Map.GetTile(0, 0).AddPopulation(popA);
        world.State.Map.GetTile(1, 0).AddPopulation(popB);

        world.State.Map.GetTile(0, 0).Resources.Add(AbundantFood());
        world.State.Map.GetTile(1, 0).Resources.Add(AbundantFood());

        world.Tick();
        world.Tick();
        world.Tick();

        Assert.True(factionA.Relations[factionB].TensionScore > 0, "competing factions in proximity should build tension");
    }

    [Fact]
    public void Tick_FactionsOutOfRangeDoNotBuildTension()
    {
        var world = new World();
        var species = BasicSpecies();

        var factionA = new Faction { Name = "A", PrimarySpecies = species };
        var factionB = new Faction { Name = "B", PrimarySpecies = species };
        world.State.Factions.AddRange([factionA, factionB]);

        // place them far apart (distance > 5)
        var popA = new Population { Species = species, Count = 10 };
        var popB = new Population { Species = species, Count = 10 };
        factionA.AddPopulation(popA);
        factionB.AddPopulation(popB);
        world.State.Map.GetTile(0, 0).AddPopulation(popA);
        world.State.Map.GetTile(9, 9).AddPopulation(popB);

        world.State.Map.GetTile(0, 0).Resources.Add(AbundantFood());
        world.State.Map.GetTile(9, 9).Resources.Add(AbundantFood());

        world.Tick();
        world.Tick();
        world.Tick();

        Assert.False(factionA.Relations.ContainsKey(factionB) && factionA.Relations[factionB].TensionScore > 0.1f,
            "factions out of range should not build meaningful tension");
    }

    [Fact]
    public void Tick_DifferentFactionsOfSameSpeciesDoNotMergeOnMigration()
    {
        var world = new World();
        var species = new SpeciesDefinition
        {
            Name = "Rivals",
            ConsumptionRates = { [ResourceType.Water] = 1f },
            MigrationThreshold = 0.9f,
            StarvationRate = 0.01f
        };

        var factionA = new Faction { Name = "A", PrimarySpecies = species };
        var factionB = new Faction { Name = "B", PrimarySpecies = species };
        world.State.Factions.AddRange([factionA, factionB]);

        var wetTile  = world.State.Map.GetTile(1, 0);
        var dryTileA = world.State.Map.GetTile(0, 0);

        wetTile.Resources.Add(new ResourcePool { Type = ResourceType.Water, Amount = 1_000f, Capacity = 1_000f, RegenPerTick = 50f });

        var popA = new Population { Species = species, Count = 10 };
        var popB = new Population { Species = species, Count = 10 };
        factionA.AddPopulation(popA);
        factionB.AddPopulation(popB);
        dryTileA.AddPopulation(popA);
        wetTile.AddPopulation(popB);

        world.Tick();

        // factionA migrated to wetTile but should NOT merge into factionB's population
        Assert.Equal(2, wetTile.Populations.Count(p => p.Count > 0));
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
