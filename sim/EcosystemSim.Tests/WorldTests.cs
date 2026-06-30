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

    private static Disease TestDisease(float mortality = 0.1f, float spread = 0.5f, float recovery = 0.01f) =>
        new() { Name = "Test Plague", MortalityRate = mortality, SpreadRate = spread, RecoveryRate = recovery };

    [Fact]
    public void Tick_DiseaseKillsInfectedPopulation()
    {
        var world = new World();
        var tile = world.State.Map.GetTile(0, 0);
        var species = new SpeciesDefinition { Name = "Victim", Immunity = 0f, ReproductionRate = 0, StarvationRate = 0 };
        var pop = new Population { Species = species, Count = 100, Disease = TestDisease(mortality: 0.1f), InfectionLevel = 1f };
        tile.Populations.Add(pop);

        world.Tick();

        Assert.True(pop.Count < 100, "fully infected population with 0 immunity should lose members to disease");
    }

    [Fact]
    public void Tick_DiseaseSpreadsToSameTilePopulation()
    {
        var world = new World();
        var tile = world.State.Map.GetTile(0, 0);
        var species = new SpeciesDefinition { Name = "Spreader", Immunity = 0f, ReproductionRate = 0, StarvationRate = 0 };
        var disease = TestDisease(spread: 0.5f);

        var infected = new Population { Species = species, Count = 50, Disease = disease, InfectionLevel = 1f };
        var healthy  = new Population { Species = species, Count = 50 };
        tile.Populations.AddRange([infected, healthy]);

        world.Tick();

        Assert.NotNull(healthy.Disease);
        Assert.True(healthy.InfectionLevel > 0, "healthy population on same tile should catch disease");
    }

    [Fact]
    public void Tick_DiseaseSpreadsToAdjacentTile()
    {
        var world = new World();
        var sourceTile = world.State.Map.GetTile(0, 0);
        var targetTile = world.State.Map.GetTile(1, 0);
        var species = new SpeciesDefinition { Name = "Spreader", Immunity = 0f, ReproductionRate = 0, StarvationRate = 0 };
        var disease = TestDisease(spread: 1.0f);

        var infected = new Population { Species = species, Count = 100, Disease = disease, InfectionLevel = 1f };
        var healthy  = new Population { Species = species, Count = 50 };
        sourceTile.AddPopulation(infected);
        targetTile.AddPopulation(healthy);

        world.Tick();

        Assert.True(healthy.InfectionLevel > 0, "disease should seep to adjacent tile populations");
    }

    [Fact]
    public void Tick_HighImmunityReducesDiseaseDeaths()
    {
        var world = new World();
        var tile = world.State.Map.GetTile(0, 0);
        var disease = TestDisease(mortality: 0.2f);

        var vulnerable = new Population { Species = new SpeciesDefinition { Name = "V", Immunity = 0f,   ReproductionRate = 0, StarvationRate = 0 }, Count = 100, Disease = disease, InfectionLevel = 1f };
        var resistant  = new Population { Species = new SpeciesDefinition { Name = "R", Immunity = 0.9f, ReproductionRate = 0, StarvationRate = 0 }, Count = 100, Disease = disease, InfectionLevel = 1f };
        tile.Populations.AddRange([vulnerable, resistant]);

        world.Tick();

        Assert.True(resistant.Count > vulnerable.Count, "high immunity species should lose fewer individuals to disease");
    }

    [Fact]
    public void Tick_PopulationRecoversFromDiseaseWithHighImmunity()
    {
        var world = new World();
        var tile = world.State.Map.GetTile(0, 0);
        var species = new SpeciesDefinition { Name = "Hardy", Immunity = 1f, ReproductionRate = 0, StarvationRate = 0 };
        var disease = TestDisease(spread: 0f, recovery: 0.01f); // no spread, just recovery
        var pop = new Population { Species = species, Count = 100, Disease = disease, InfectionLevel = 0.1f };
        tile.Populations.Add(pop);

        // recovery per tick = RecoveryRate + Immunity * 0.05 = 0.01 + 0.05 = 0.06; run until clear
        for (var i = 0; i < 10 && pop.Disease is not null; i++)
            world.Tick();

        Assert.Null(pop.Disease);
        Assert.Equal(0f, pop.InfectionLevel);
    }

    [Fact]
    public void TriggerDiseaseCommand_InfectsPopulationsOnTile()
    {
        var world = new World();
        var tile = world.State.Map.GetTile(3, 3);
        var species = new SpeciesDefinition { Name = "Target", ReproductionRate = 0, StarvationRate = 0 };
        tile.Populations.Add(new Population { Species = species, Count = 50 });

        var disease = TestDisease();
        world.Apply(new TriggerDiseaseCommand { Disease = disease, TileX = 3, TileY = 3 });

        Assert.Same(disease, tile.Populations[0].Disease);
        Assert.True(tile.Populations[0].InfectionLevel > 0);
    }

    private static (Faction, Population) MakeFactionOnTile(World world, string name, int x, int y, int count, float combatStrength = 1f)
    {
        var species = new SpeciesDefinition
        {
            Name = name,
            CombatStrength = combatStrength,
            ReproductionRate = 0f, // isolate combat tests from resource growth/death
            StarvationRate = 0f
        };
        var faction = new Faction { Name = name, PrimarySpecies = species };
        var pop = new Population { Species = species, Count = count };
        faction.AddPopulation(pop);
        world.State.Map.GetTile(x, y).AddPopulation(pop);
        world.State.Factions.Add(faction);
        return (faction, pop);
    }

    private static void DeclareWar(Faction a, Faction b)
    {
        a.Relations[b] = new FactionRelation { Other = b, State = DiplomaticState.AtWar, TensionScore = 2f };
        b.Relations[a] = new FactionRelation { Other = a, State = DiplomaticState.AtWar, TensionScore = 2f };
    }

    [Fact]
    public void Tick_PopulationsAtWarTakeMutualCasualties()
    {
        var world = new World();
        var (factionA, popA) = MakeFactionOnTile(world, "A", 0, 0, 100);
        var (factionB, popB) = MakeFactionOnTile(world, "B", 0, 0, 100);
        DeclareWar(factionA, factionB);

        world.Tick();

        Assert.True(popA.Count < 100, "faction A should take casualties");
        Assert.True(popB.Count < 100, "faction B should take casualties");
    }

    [Fact]
    public void Tick_CombatIsSimultaneous()
    {
        var world = new World();
        // A is tiny but should still deal damage to B in the same tick it is wiped out
        var (factionA, popA) = MakeFactionOnTile(world, "A", 0, 0, 1, combatStrength: 10f);
        var (factionB, popB) = MakeFactionOnTile(world, "B", 0, 0, 1_000, combatStrength: 1f);
        DeclareWar(factionA, factionB);

        world.Tick();

        Assert.Equal(0, popA.Count);              // A wiped out
        Assert.True(popB.Count < 1_000, "B should still take damage from A in the same tick");
    }

    [Fact]
    public void Tick_NeutralFactionsDoNotFight()
    {
        var world = new World();
        var (_, popA) = MakeFactionOnTile(world, "A", 0, 0, 100);
        var (_, popB) = MakeFactionOnTile(world, "B", 0, 0, 100);
        // no war declared — factions are neutral

        world.State.Map.GetTile(0, 0).Resources.Add(AbundantFood());
        world.Tick();

        Assert.True(popA.Count >= 100, "neutral faction A should not take combat casualties");
        Assert.True(popB.Count >= 100, "neutral faction B should not take combat casualties");
    }

    [Fact]
    public void Tick_PopulationsOnSeparateTilesDoNotFight()
    {
        var world = new World();
        var (factionA, popA) = MakeFactionOnTile(world, "A", 0, 0, 100);
        var (factionB, popB) = MakeFactionOnTile(world, "B", 1, 0, 100);
        DeclareWar(factionA, factionB);

        world.Tick();

        Assert.Equal(100, popA.Count);
        Assert.Equal(100, popB.Count);
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
    public void Tick_TensionDecaysWhenFactionsOutOfRange()
    {
        var world = new World();
        var (factionA, _) = MakeFactionOnTile(world, "A", 0, 0, 50);
        var (factionB, _) = MakeFactionOnTile(world, "B", 9, 9, 50);

        // seed with high tension manually
        DeclareWar(factionA, factionB);
        Assert.Equal(DiplomaticState.AtWar, factionA.Relations[factionB].State);

        // populations are 18 tiles apart (> ProximityRange of 5), so tension should decay
        world.Tick();
        world.Tick();
        world.Tick();

        Assert.True(factionA.Relations[factionB].TensionScore < 2f, "tension should decay when out of range");
    }

    [Fact]
    public void Tick_WarEndsAfterSustainedConflict()
    {
        var world = new World();
        var (factionA, _) = MakeFactionOnTile(world, "A", 0, 0, 100);
        var (factionB, _) = MakeFactionOnTile(world, "B", 9, 9, 100);

        DeclareWar(factionA, factionB);

        // run long enough for ceasefire pressure (25 ticks) plus decay to kick in
        // populations are far apart so decay drives tension down
        for (var i = 0; i < 50; i++)
            world.Tick();

        Assert.NotEqual(DiplomaticState.AtWar, factionA.Relations[factionB].State);
    }

    [Fact]
    public void Tick_ComplementarySpeciesDeEscalate()
    {
        var world = new World();

        // species A eats only food, species B eats only water — no competition
        var speciesA = new SpeciesDefinition { Name = "A", ConsumptionRates = { [ResourceType.Food] = 1f }, WarAggression = 0.1f, ReproductionRate = 0, StarvationRate = 0 };
        var speciesB = new SpeciesDefinition { Name = "B", ConsumptionRates = { [ResourceType.Water] = 1f }, WarAggression = 0.1f, ReproductionRate = 0, StarvationRate = 0 };

        var factionA = new Faction { Name = "A", PrimarySpecies = speciesA };
        var factionB = new Faction { Name = "B", PrimarySpecies = speciesB };
        world.State.Factions.AddRange([factionA, factionB]);

        var popA = new Population { Species = speciesA, Count = 10 };
        var popB = new Population { Species = speciesB, Count = 10 };
        factionA.AddPopulation(popA);
        factionB.AddPopulation(popB);
        world.State.Map.GetTile(0, 0).AddPopulation(popA);
        world.State.Map.GetTile(1, 0).AddPopulation(popB); // adjacent but no shared resources

        for (var i = 0; i < 10; i++)
            world.Tick();

        Assert.True(factionA.Relations[factionB].TensionScore < 0,
            "complementary species in proximity should build cooperation, not tension");
    }

    [Fact]
    public void Tick_ResourceScarcityEscalatesTensionFaster()
    {
        var world = new World();

        // two species competing for the same scarce food
        var species = new SpeciesDefinition
        {
            Name = "Rival",
            ConsumptionRates = { [ResourceType.Food] = 5f }, // high consumption → stress fast
            WarAggression = 0.2f,
            ReproductionRate = 0,
            StarvationRate = 0
        };

        var factionA = new Faction { Name = "A", PrimarySpecies = species };
        var factionB = new Faction { Name = "B", PrimarySpecies = species };
        world.State.Factions.AddRange([factionA, factionB]);

        var tile = world.State.Map.GetTile(0, 0);
        tile.Resources.Add(new ResourcePool { Type = ResourceType.Food, Amount = 1f, Capacity = 100f, RegenPerTick = 1f });

        var popA = new Population { Species = species, Count = 50 };
        var popB = new Population { Species = species, Count = 50 };
        factionA.AddPopulation(popA);
        factionB.AddPopulation(popB);
        tile.AddPopulation(popA);
        tile.AddPopulation(popB);

        // run a calm (no stress) baseline faction for comparison
        var calmWorld = new World();
        var calmA = new Faction { Name = "CA", PrimarySpecies = species };
        var calmB = new Faction { Name = "CB", PrimarySpecies = species };
        calmWorld.State.Factions.AddRange([calmA, calmB]);
        var calmTile = calmWorld.State.Map.GetTile(0, 0);
        calmTile.Resources.Add(new ResourcePool { Type = ResourceType.Food, Amount = 10_000f, Capacity = 10_000f, RegenPerTick = 500f });
        var calmPopA = new Population { Species = species, Count = 50 };
        var calmPopB = new Population { Species = species, Count = 50 };
        calmA.AddPopulation(calmPopA);
        calmB.AddPopulation(calmPopB);
        calmTile.AddPopulation(calmPopA);
        calmTile.AddPopulation(calmPopB);

        for (var i = 0; i < 5; i++)
        {
            world.Tick();
            calmWorld.Tick();
        }

        Assert.True(
            factionA.Relations[factionB].TensionScore > calmA.Relations[calmB].TensionScore,
            "resource-stressed factions should escalate faster than well-fed ones");
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
