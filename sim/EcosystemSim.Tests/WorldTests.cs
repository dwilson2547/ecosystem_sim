using Xunit;

namespace EcosystemSim.Tests;

public class WorldTests
{
    private static SpeciesDefinition BasicSpecies(string name = "TestDino") => new()
    {
        Name = name,
        FoodConsumptionRate = 1f,
        ReproductionRate = 0.1f,
        StarvationRate = 0.5f
    };

    private static ResourcePool AbundantFood(float amount = 10_000f) => new()
    {
        Type        = ResourceType.Food,
        FoodSubtype = FoodSubtype.Graze,
        Amount      = amount,
        Capacity    = 10_000f,
        RegenPerTick = 500f
    };

    private static ResourcePool EmptyFood() => new()
    {
        Type        = ResourceType.Food,
        FoodSubtype = FoodSubtype.Graze,
        Amount      = 0f,
        Capacity    = 1_000f,
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
    public void Tick_SlowReproducerAccumulatesBirthsInsteadOfForcingPlusOne()
    {
        // ReproductionRate × Count = 0.05 per tick — under the old Math.Ceiling this rounded up to
        // +1 individual every single tick (doubling a Count=1 pop instantly). It must now bank the
        // fractional births and only add a whole individual once they cross 1.0.
        var species = new SpeciesDefinition
        {
            Name = "SlowBreeder",
            FoodConsumptionRate = 1f,
            ReproductionRate = 0.05f,
            StarvationRate = 0.5f
        };

        var world = new World();
        var tile = world.State.Map.GetTile(0, 0);
        tile.Resources.Add(AbundantFood());
        var pop = new Population { Species = species, Count = 1 };
        tile.Populations.Add(pop);

        world.Tick();
        Assert.Equal(1, pop.Count);                     // no rounding-up jump on a single tick
        Assert.True(pop.PredationAccumulator == 0f);
        Assert.InRange(pop.ReproductionAccumulator, 0.04f, 0.06f);

        // sustained abundance eventually banks a whole individual — growth still happens, just at
        // the true rate rather than every tick (preserves the no-stranding invariant).
        for (var i = 0; i < 25; i++) world.Tick();
        Assert.True(pop.Count >= 2, $"expected growth over 26 ticks, got {pop.Count}");
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
            Type        = ResourceType.Food,
            FoodSubtype = FoodSubtype.Graze,
            Amount      = 100f,
            Capacity    = 1_000f,
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
        var tile  = world.State.Map.GetTile(0, 0);
        var pop   = new Population
        {
            Species = new SpeciesDefinition { Name = "Fragile", FoodConsumptionRate = 1f, StarvationRate = 1f },
            Count = 1
        };
        tile.Populations.Add(pop);

        world.Tick();

        // count never goes negative; population stays on tile for history
        Assert.Equal(0, pop.Count);
        Assert.Equal(0f, pop.LastSatisfaction); // dead pops don't get 1.0 satisfaction
    }

    [Fact]
    public void Tick_ResourcePoolReplenishesEachTick()
    {
        var world = new World();
        var tile = world.State.Map.GetTile(0, 0);
        tile.Resources.Add(new ResourcePool
        {
            Type        = ResourceType.Food,
            FoodSubtype = FoodSubtype.Graze,
            Amount      = 0f,
            Capacity    = 1_000f,
            RegenPerTick = 100f
        });

        world.Tick();

        // seasons multiply base regen (Spring=1.3×), so we only assert the pool grew
        Assert.True(tile.Resources[0].Amount > 0f, "resource pool should regen each tick");
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
    public void Tick_PopulationMigratesAcrossResourceDesertTowardDistantSource()
    {
        // reproduce the Highland Tric bug: resource is several tiles away with nothing in between
        var world      = new World();
        var startTile  = world.State.Map.GetTile(0, 0);
        var sourceTile = world.State.Map.GetTile(0, 3); // 3 tiles south, no water in between

        var species = new SpeciesDefinition
        {
            Name = "WaterSeeker",
            WaterConsumptionRate = 1f,
            MigrationThreshold = 0.9f,
            StarvationRate = 0.01f
        };

        sourceTile.Resources.Add(new ResourcePool { Type = ResourceType.Water, Amount = 1_000f, Capacity = 1_000f, RegenPerTick = 50f });
        startTile.Populations.Add(new Population { Species = species, Count = 10 });

        world.Tick(); // BFS finds sourceTile and returns first step (0,1)

        Assert.Empty(startTile.Populations.Where(p => p.Count > 0));
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
            WaterConsumptionRate = 1f,
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
            WaterConsumptionRate = 1f,
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
    public void Tick_HighAggressionFactionsInProximityBuildTension()
    {
        var world   = new World();
        // aggression high enough to overcome peace drift even when well-fed
        var species = new SpeciesDefinition { Name = "Aggressive", WarAggression = 0.5f, FoodConsumptionRate = 1f, ReproductionRate = 0, StarvationRate = 0 };

        var factionA = new Faction { Name = "A", PrimarySpecies = species };
        var factionB = new Faction { Name = "B", PrimarySpecies = species };
        world.State.Factions.AddRange([factionA, factionB]);

        var popA = new Population { Species = species, Count = 10 };
        var popB = new Population { Species = species, Count = 10 };
        factionA.AddPopulation(popA);
        factionB.AddPopulation(popB);
        world.State.Map.GetTile(0, 0).AddPopulation(popA);
        world.State.Map.GetTile(1, 0).AddPopulation(popB);

        world.State.Map.GetTile(0, 0).Resources.Add(AbundantFood());
        world.State.Map.GetTile(1, 0).Resources.Add(AbundantFood());

        for (var i = 0; i < 5; i++) world.Tick();

        Assert.True(factionA.Relations[factionB].TensionScore > 0,
            "high-aggression factions in proximity should build tension even when well-fed");
    }

    [Fact]
    public void Tick_WellFedLowAggressionFactionsDoNotEscalate()
    {
        var world   = new World();
        var species = new SpeciesDefinition { Name = "Peaceful", WarAggression = 0.2f, FoodConsumptionRate = 1f, ReproductionRate = 0, StarvationRate = 0 };

        var factionA = new Faction { Name = "A", PrimarySpecies = species };
        var factionB = new Faction { Name = "B", PrimarySpecies = species };
        world.State.Factions.AddRange([factionA, factionB]);

        var popA = new Population { Species = species, Count = 10 };
        var popB = new Population { Species = species, Count = 10 };
        factionA.AddPopulation(popA);
        factionB.AddPopulation(popB);
        world.State.Map.GetTile(0, 0).AddPopulation(popA);
        world.State.Map.GetTile(1, 0).AddPopulation(popB);

        world.State.Map.GetTile(0, 0).Resources.Add(AbundantFood());
        world.State.Map.GetTile(1, 0).Resources.Add(AbundantFood());

        for (var i = 0; i < 10; i++) world.Tick();

        Assert.True(factionA.Relations[factionB].TensionScore <= 0,
            "moderate-aggression well-fed factions should find peace equilibrium, not escalate to war");
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
            WaterConsumptionRate = 1f,
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
        var speciesA = new SpeciesDefinition { Name = "A", FoodConsumptionRate = 1f, WarAggression = 0.1f, ReproductionRate = 0, StarvationRate = 0 };
        var speciesB = new SpeciesDefinition { Name = "B", WaterConsumptionRate = 1f, WarAggression = 0.1f, ReproductionRate = 0, StarvationRate = 0 };

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
            FoodConsumptionRate = 5f, // high consumption → stress fast
            WarAggression = 0.2f,
            ReproductionRate = 0,
            StarvationRate = 0
        };

        var factionA = new Faction { Name = "A", PrimarySpecies = species };
        var factionB = new Faction { Name = "B", PrimarySpecies = species };
        world.State.Factions.AddRange([factionA, factionB]);

        var tile = world.State.Map.GetTile(0, 0);
        tile.Resources.Add(new ResourcePool { Type = ResourceType.Food, FoodSubtype = FoodSubtype.Graze, Amount = 1f, Capacity = 100f, RegenPerTick = 1f });

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
        calmTile.Resources.Add(new ResourcePool { Type = ResourceType.Food, FoodSubtype = FoodSubtype.Graze, Amount = 10_000f, Capacity = 10_000f, RegenPerTick = 500f });
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
    public void Map_NeighborsAreHexagonal()
    {
        // center of 3×3 at odd row (1,1): all six hex neighbors are in-bounds
        var map = new WorldMap(3, 3);
        var center = map.GetTile(1, 1);
        var neighbors = map.GetNeighbors(center).ToList();

        Assert.Equal(6, neighbors.Count);
        Assert.DoesNotContain(map.GetTile(0, 0), neighbors); // true diagonal — not a hex neighbor
        Assert.DoesNotContain(map.GetTile(1, 1), neighbors); // self excluded
    }

    [Fact]
    public void Map_CornerTileHasTwoNeighbors()
    {
        // (0,0) even row: only E=(1,0) and SE=(0,1) are in-bounds
        var map = new WorldMap(3, 3);
        var neighbors = map.GetNeighbors(0, 0).ToList();
        Assert.Equal(2, neighbors.Count);
    }

    // ── Trade & Byproduct tests ─────────────────────────────────────────────

    private static SpeciesDefinition FertiliserSpecies(float rate = 0.1f) =>
        new() { Name = "Herbivore", ByproductRates = { [ByproductType.Fertilizer] = rate }, ReproductionRate = 0, StarvationRate = 0 };

    [Fact]
    public void Tick_PopulationProducesFertilizerOnTile()
    {
        var world = new World();
        var tile  = world.State.Map.GetTile(0, 0);
        var pop   = new Population { Species = FertiliserSpecies(0.1f), Count = 100 };
        tile.Populations.Add(pop);

        world.Tick();

        var fert = tile.Byproducts.FirstOrDefault(b => b.Type == ByproductType.Fertilizer);
        Assert.NotNull(fert);
        Assert.True(fert.Amount > 0, "population should have deposited fertilizer");
    }

    [Fact]
    public void Tick_FertilizerBoostsFoodRegen()
    {
        var world     = new World();
        var tileFert  = world.State.Map.GetTile(0, 0);
        var tilePlain = world.State.Map.GetTile(9, 9);

        const float regen = 10f, cap = 1000f;
        tileFert .Resources.Add(new ResourcePool { Type = ResourceType.Food, FoodSubtype = FoodSubtype.Graze, Amount = 0f, Capacity = cap, RegenPerTick = regen });
        tilePlain.Resources.Add(new ResourcePool { Type = ResourceType.Food, FoodSubtype = FoodSubtype.Graze, Amount = 0f, Capacity = cap, RegenPerTick = regen });

        // seed fertilizer on the first tile
        tileFert.GetOrAddByproduct(ByproductType.Fertilizer).Add(100f);

        world.Tick();

        var foodFert  = tileFert .Resources.First(r => r.Type == ResourceType.Food).Amount;
        var foodPlain = tilePlain.Resources.First(r => r.Type == ResourceType.Food).Amount;
        Assert.True(foodFert > foodPlain, "tile with fertilizer should regen more food");
    }

    [Fact]
    public void Tick_FertilizerDecaysEachTick()
    {
        var world = new World();
        var tile  = world.State.Map.GetTile(0, 0);
        tile.GetOrAddByproduct(ByproductType.Fertilizer).Add(100f);

        world.Tick();

        var fert = tile.Byproducts.First(b => b.Type == ByproductType.Fertilizer);
        Assert.True(fert.Amount < 100f, "fertilizer should decay each tick without new production");
    }

    [Fact]
    public void Tick_TradingFactionsExchangeFertilizer()
    {
        var world = new World();
        var tileA = world.State.Map.GetTile(0, 0);
        var tileB = world.State.Map.GetTile(1, 0);

        var species  = FertiliserSpecies(rate: 0f); // no production — we seed it manually
        var factionA = new Faction { Name = "A", PrimarySpecies = species };
        var factionB = new Faction { Name = "B", PrimarySpecies = species };
        world.State.Factions.AddRange([factionA, factionB]);

        var popA = new Population { Species = species, Count = 50 };
        var popB = new Population { Species = species, Count = 50 };
        factionA.AddPopulation(popA);
        factionB.AddPopulation(popB);
        tileA.AddPopulation(popA);
        tileB.AddPopulation(popB);

        // give A lots of fertilizer, B has none
        tileA.GetOrAddByproduct(ByproductType.Fertilizer).Add(100f);

        world.Apply(new EstablishTradeCommand { FactionA = factionA, FactionB = factionB });
        world.Tick();

        var bFert = tileB.Byproducts.FirstOrDefault(b => b.Type == ByproductType.Fertilizer);
        Assert.NotNull(bFert);
        Assert.True(bFert.Amount > 0, "trading partner should receive some fertilizer");
    }

    [Fact]
    public void EstablishTradeCommand_SetsAgreementOnBothFactions()
    {
        var world   = new World();
        var species = BasicSpecies();
        var a = new Faction { Name = "A", PrimarySpecies = species };
        var b = new Faction { Name = "B", PrimarySpecies = species };
        world.State.Factions.AddRange([a, b]);

        world.Apply(new EstablishTradeCommand { FactionA = a, FactionB = b });

        Assert.True(a.Relations[b].HasTradeAgreement);
        Assert.True(b.Relations[a].HasTradeAgreement);
    }

    [Fact]
    public void Tick_WarBreaksTradeAgreement()
    {
        var world   = new World();
        var (a, _)  = MakeFactionOnTile(world, "A", 0, 0, 50);
        var (b, _)  = MakeFactionOnTile(world, "B", 0, 0, 50);

        world.Apply(new EstablishTradeCommand { FactionA = a, FactionB = b });
        DeclareWar(a, b);

        // tension sync on next tick should detect AtWar and drop the agreement
        world.Tick();

        Assert.False(a.Relations[b].HasTradeAgreement, "trade agreement should be broken when at war");
    }

    // ── Evolution tests ─────────────────────────────────────────────────────

    private static Population PopOnTile(World world, Tile tile, int count, float consumptionRate = 1f)
    {
        var species = new SpeciesDefinition { Name = "Evo", FoodConsumptionRate = consumptionRate, ReproductionRate = 0, StarvationRate = 0 };
        var pop = new Population { Species = species, Count = count };
        tile.Populations.Add(pop);
        return pop;
    }

    [Fact]
    public void Tick_AbundanceAccumulatesPositiveSizePressure()
    {
        var world = new World();
        var tile  = world.State.Map.GetTile(0, 0);
        var pop   = PopOnTile(world, tile, 10);
        tile.Resources.Add(AbundantFood());

        world.Tick();

        Assert.True(pop.SizePressure > 0, "a well-fed population should accumulate positive size pressure");
    }

    [Fact]
    public void Tick_ScarcityAccumulatesNegativeSizePressure()
    {
        var world = new World();
        var tile  = world.State.Map.GetTile(0, 0);
        var pop   = PopOnTile(world, tile, 10);
        tile.Resources.Add(EmptyFood()); // no food → satisfaction 0 → scarcity

        world.Tick();

        Assert.True(pop.SizePressure < 0, "a starving population should accumulate negative size pressure");
    }

    [Fact]
    public void Tick_SizePressureThresholdGrowsSizeIndex()
    {
        var world = new World();
        var tile  = world.State.Map.GetTile(0, 0);
        var pop   = PopOnTile(world, tile, 10);
        tile.Resources.Add(AbundantFood());
        pop.SizePressure = 49f; // one tick of abundance pushes it over

        world.Tick();

        Assert.True(pop.SizeIndex > 1.0f, "pressure threshold crossing should increase SizeIndex");
        Assert.Equal(0f, pop.SizePressure); // resets after crossing
    }

    [Fact]
    public void Tick_SizePressureThresholdShrinksSize()
    {
        var world = new World();
        var tile  = world.State.Map.GetTile(0, 0);
        var pop   = PopOnTile(world, tile, 10);
        tile.Resources.Add(EmptyFood());
        pop.SizePressure = -49f; // one tick of scarcity pushes it over

        world.Tick();

        Assert.True(pop.SizeIndex < 1.0f, "negative pressure threshold crossing should decrease SizeIndex");
        Assert.Equal(0f, pop.SizePressure);
    }

    [Fact]
    public void Tick_LargerSizeConsumesMoreFood()
    {
        var world = new World();
        var tileA = world.State.Map.GetTile(0, 0);
        var tileB = world.State.Map.GetTile(9, 9);

        const float startFood = 500f;
        tileA.Resources.Add(new ResourcePool { Type = ResourceType.Food, FoodSubtype = FoodSubtype.Graze, Amount = startFood, Capacity = 1000f, RegenPerTick = 0 });
        tileB.Resources.Add(new ResourcePool { Type = ResourceType.Food, FoodSubtype = FoodSubtype.Graze, Amount = startFood, Capacity = 1000f, RegenPerTick = 0 });

        var large  = PopOnTile(world, tileA, 10); large.SizeIndex  = 2.0f;
        var normal = PopOnTile(world, tileB, 10); normal.SizeIndex = 1.0f;

        world.Tick();

        var foodAfterLarge  = tileA.Resources.First().Amount;
        var foodAfterNormal = tileB.Resources.First().Amount;
        Assert.True(foodAfterLarge < foodAfterNormal, "larger population should consume more food per individual");
    }

    [Fact]
    public void Tick_LargerSizeDealsMoreCombatDamage()
    {
        var world = new World();
        var (factionA, popA) = MakeFactionOnTile(world, "A", 0, 0, 100);
        var (factionB, popB) = MakeFactionOnTile(world, "B", 0, 0, 100);
        DeclareWar(factionA, factionB);

        popA.SizeIndex = 2.0f; // bigger attacker
        var initialB = popB.Count;

        world.Tick();

        // run a baseline with normal-sized A
        var world2 = new World();
        var (fa2, pa2) = MakeFactionOnTile(world2, "A", 0, 0, 100);
        var (fb2, pb2) = MakeFactionOnTile(world2, "B", 0, 0, 100);
        DeclareWar(fa2, fb2);
        world2.Tick();

        Assert.True(popB.Count < pb2.Count, "larger attacker (SizeIndex=2) should inflict more casualties");
    }

    [Fact]
    public void Tick_DiseaseExposureAccumulatesImmunityPressure()
    {
        var world = new World();
        var tile  = world.State.Map.GetTile(0, 0);
        var pop   = PopOnTile(world, tile, 100);
        pop.Disease       = TestDisease(spread: 0f);
        pop.InfectionLevel = 0.5f;

        world.Tick();

        Assert.True(pop.ImmunityPressure > 0, "a diseased population should accumulate immunity pressure");
    }

    [Fact]
    public void Tick_ImmunityPressureThresholdGainsImmunityDelta()
    {
        var world = new World();
        var tile  = world.State.Map.GetTile(0, 0);
        var pop   = PopOnTile(world, tile, 100);
        pop.Disease          = TestDisease(spread: 0f, mortality: 0f);
        pop.InfectionLevel   = 0.5f;
        pop.ImmunityPressure = 29f; // one more tick of infection crosses threshold

        world.Tick();

        Assert.True(pop.ImmunityDelta > 0f, "crossing immunity pressure threshold should increase ImmunityDelta");
        Assert.Equal(0f, pop.ImmunityPressure);
    }

    [Fact]
    public void Tick_EvolvedImmunityReducesDiseaseDeaths()
    {
        var world    = new World();
        var tileA    = world.State.Map.GetTile(0, 0);
        var tileB    = world.State.Map.GetTile(9, 9);
        var disease  = TestDisease(spread: 0f, mortality: 0.3f);
        var speciesA = new SpeciesDefinition { Name = "Evolved",  Immunity = 0f, ReproductionRate = 0, StarvationRate = 0 };
        var speciesB = new SpeciesDefinition { Name = "Baseline", Immunity = 0f, ReproductionRate = 0, StarvationRate = 0 };

        var evolved   = new Population { Species = speciesA, Count = 100, Disease = disease, InfectionLevel = 1f, ImmunityDelta = 0.5f };
        var baseline  = new Population { Species = speciesB, Count = 100, Disease = disease, InfectionLevel = 1f };
        tileA.Populations.Add(evolved);
        tileB.Populations.Add(baseline);

        world.Tick();

        Assert.True(evolved.Count > baseline.Count,
            "population with gained ImmunityDelta should lose fewer individuals to disease");
    }

    // ── Speciation tests ──────────────────────────────────────────────────────

    private static SpeciesDefinition BaseSpecies(string name = "TestDino") => new()
    {
        Name = name,
        RootName = name,
        FoodConsumptionRate = 2f,
        CombatStrength = 1.0f,
        Immunity = 0.2f,
        ByproductRates = { [ByproductType.Fertilizer] = 0.1f },
        ReproductionRate = 0.05f,
        StarvationRate = 0.05f,
    };

    [Fact]
    public void Tick_PopulationSpeciatesWhenSizeIndexExceedsLargeThreshold()
    {
        var world = new World();
        var tile  = world.State.Map.GetTile(0, 0);
        tile.Resources.Add(AbundantFood());

        var pop = new Population { Species = BaseSpecies(), Count = 50, SizeIndex = World.SpeciationLargeThreshold };
        tile.Populations.Add(pop);

        world.Tick();

        Assert.Equal("Greater TestDino", pop.Species.Name);
        Assert.Equal(1.0f, pop.SizeIndex);
        Assert.Equal(0f, pop.SizePressure);
    }

    [Fact]
    public void Tick_PopulationSpeciatesWhenSizeIndexDropsBelowSmallThreshold()
    {
        var world = new World();
        var tile  = world.State.Map.GetTile(0, 0);
        tile.Resources.Add(AbundantFood());

        var pop = new Population { Species = BaseSpecies(), Count = 50, SizeIndex = World.SpeciationSmallThreshold };
        tile.Populations.Add(pop);

        world.Tick();

        Assert.Equal("Lesser TestDino", pop.Species.Name);
        Assert.Equal(1.0f, pop.SizeIndex);
    }

    [Fact]
    public void Tick_SpeciatedSpeciesHasBakedInTraits()
    {
        var world = new World();
        var tile  = world.State.Map.GetTile(0, 0);
        tile.Resources.Add(AbundantFood());

        var parent = BaseSpecies();
        var pop = new Population { Species = parent, Count = 50, SizeIndex = World.SpeciationLargeThreshold };
        tile.Populations.Add(pop);

        world.Tick();

        // food consumption should be baked in at parent × sizeIndex
        var expectedFood = parent.FoodConsumptionRate * World.SpeciationLargeThreshold;
        Assert.Equal(expectedFood, pop.Species.FoodConsumptionRate, precision: 3);

        // combat strength baked in at parent × sqrt(sizeIndex)
        var expectedCombat = parent.CombatStrength * MathF.Sqrt(World.SpeciationLargeThreshold);
        Assert.Equal(expectedCombat, pop.Species.CombatStrength, precision: 3);
    }

    [Fact]
    public void Tick_SecondSpeciationCreatesGiantFromGreater()
    {
        var world = new World();
        var tile  = world.State.Map.GetTile(0, 0);
        tile.Resources.Add(AbundantFood());

        var greater = new SpeciesDefinition
        {
            Name = "Greater TestDino",
            RootName = "TestDino",
            FoodConsumptionRate = 3f,
            CombatStrength = 1.2f,
            Immunity = 0.2f,
            ReproductionRate = 0.04f,
            StarvationRate = 0.05f,
        };

        var pop = new Population { Species = greater, Count = 50, SizeIndex = World.SpeciationLargeThreshold };
        tile.Populations.Add(pop);

        world.Tick();

        Assert.Equal("Giant TestDino", pop.Species.Name);
    }

    [Fact]
    public void Tick_TwoPopulationsSpeciatingToSameNameShareSpecies()
    {
        var world = new World();
        var tileA = world.State.Map.GetTile(0, 0);
        var tileB = world.State.Map.GetTile(9, 9);
        tileA.Resources.Add(AbundantFood());
        tileB.Resources.Add(AbundantFood());

        var popA = new Population { Species = BaseSpecies(), Count = 50, SizeIndex = World.SpeciationLargeThreshold };
        var popB = new Population { Species = BaseSpecies(), Count = 30, SizeIndex = World.SpeciationLargeThreshold };
        tileA.Populations.Add(popA);
        tileB.Populations.Add(popB);

        world.Tick();

        Assert.Equal("Greater TestDino", popA.Species.Name);
        Assert.Equal("Greater TestDino", popB.Species.Name);
        Assert.Same(popA.Species, popB.Species); // same object — shared definition
    }

    // ── Season tests ─────────────────────────────────────────────────────────

    [Fact]
    public void Tick_SeasonAdvancesFromSpringToSummerAfterTicksPerSeason()
    {
        var world = new World();
        Assert.Equal(Season.Spring, world.State.CurrentSeason);

        for (var i = 0; i < World.TicksPerSeason; i++)
            world.Tick();

        Assert.Equal(Season.Summer, world.State.CurrentSeason);
    }

    [Fact]
    public void Tick_SeasonsCompleteFullCycleInFourSeasonLengths()
    {
        var world = new World();

        for (var i = 0; i < World.TicksPerSeason * 4; i++)
            world.Tick();

        Assert.Equal(Season.Spring, world.State.CurrentSeason);
    }

    [Fact]
    public void Tick_WinterFoodRegenIsLowerThanSpring()
    {
        var world = new World();
        var tile  = world.State.Map.GetTile(0, 0);
        tile.Resources.Add(new ResourcePool { Type = ResourceType.Food, FoodSubtype = FoodSubtype.Graze, Amount = 0f, Capacity = 10_000f, RegenPerTick = 10f });

        // fast-forward to Winter (3 full seasons)
        for (var i = 0; i < World.TicksPerSeason * 3; i++)
            world.Tick();
        Assert.Equal(Season.Winter, world.State.CurrentSeason);

        tile.Resources[0].Amount = 0f;
        world.Tick();
        var winterRegen = tile.Resources[0].Amount;

        // fast-forward to Spring (1 more season)
        for (var i = 0; i < World.TicksPerSeason; i++)
            world.Tick();
        Assert.Equal(Season.Spring, world.State.CurrentSeason);

        tile.Resources[0].Amount = 0f;
        world.Tick();
        var springRegen = tile.Resources[0].Amount;

        Assert.True(winterRegen < springRegen,
            $"winter regen ({winterRegen:F1}) should be much lower than spring regen ({springRegen:F1})");
    }

    [Fact]
    public void Tick_LargePopulationHasWorseSatisfactionThanSmallOnePerCapita()
    {
        // same food-per-head available on both tiles; only the group size differs.
        // density drain should make the crowded tile's per-capita satisfaction worse.
        var world = new World();

        var crowdedTile = world.State.Map.GetTile(0, 0);
        var sparseTile  = world.State.Map.GetTile(5, 5);

        crowdedTile.Resources.Add(new ResourcePool { Type = ResourceType.Food, FoodSubtype = FoodSubtype.Graze, Amount = 1_000f, Capacity = 1_000f, RegenPerTick = 0f });
        sparseTile.Resources.Add(new ResourcePool { Type = ResourceType.Food, FoodSubtype = FoodSubtype.Graze, Amount = 100f, Capacity = 1_000f, RegenPerTick = 0f });

        var crowded = new Population { Species = BasicSpecies(), Count = 100 }; // 1_000 food / 100 head = 10/head
        var sparse  = new Population { Species = BasicSpecies(), Count = 10 };  // 100 food / 10 head = 10/head
        crowdedTile.Populations.Add(crowded);
        sparseTile.Populations.Add(sparse);

        world.Tick();

        Assert.True(crowded.LastSatisfaction < sparse.LastSatisfaction,
            $"crowded tile satisfaction ({crowded.LastSatisfaction:F2}) should be worse than sparse tile ({sparse.LastSatisfaction:F2}) despite equal food-per-head");
    }

    [Fact]
    public void Tick_PopulationStrandedOnRiverEventuallyDrowns()
    {
        var world = new World();
        var river = world.State.Map.GetTile(5, 5); // interior tile — has all 6 neighbors
        river.Terrain = TerrainType.River;

        // box the tile in with River on every side so there's nowhere to flee to,
        // isolating the drowning mechanic from the flee-to-safety behavior
        foreach (var n in world.State.Map.GetNeighbors(river))
            n.Terrain = TerrainType.River;

        // abundant food/water so death is caused by drowning, not starvation
        river.Resources.Add(AbundantFood());
        river.Resources.Add(new ResourcePool { Type = ResourceType.Water, Amount = 10_000f, Capacity = 10_000f, RegenPerTick = 500f });

        var species = new SpeciesDefinition
        {
            Name = "Landlubber",
            FoodConsumptionRate = 1f,
            ReproductionRate = 0f,
            StarvationRate = 0f,
        };
        var pop = new Population { Species = species, Count = 100 };
        river.Populations.Add(pop);

        for (var i = 0; i < 40; i++)
            world.Tick();

        Assert.True(pop.Count < 100, "population boxed in on River past the survival threshold should take losses");
    }

    [Fact]
    public void Tick_PopulationFleesRiverBeforeDrowningWhenRefugeExists()
    {
        var world = new World();
        var river = world.State.Map.GetTile(0, 0);
        var bank  = world.State.Map.GetTile(1, 0); // plains neighbor to flee to
        river.Terrain = TerrainType.River;

        river.Resources.Add(AbundantFood());
        river.Resources.Add(new ResourcePool { Type = ResourceType.Water, Amount = 10_000f, Capacity = 10_000f, RegenPerTick = 500f });
        bank.Resources.Add(AbundantFood());

        var species = new SpeciesDefinition
        {
            Name = "Landlubber",
            FoodConsumptionRate = 1f,
            ReproductionRate = 0f,
            StarvationRate = 0f,
            MigrationThreshold = 0f, // fully satisfied throughout — only the flee-from-water logic should move it
        };
        var pop = new Population { Species = species, Count = 100 };
        river.Populations.Add(pop);

        // WaterFleeThreshold is 10 ticks; run just past it
        for (var i = 0; i < 12; i++)
            world.Tick();

        Assert.Empty(river.Populations.Where(p => p.Count > 0));
        Assert.Equal(100, bank.Populations.Single(p => p.Count > 0).Count);
    }

    [Fact]
    public void Tick_BriefVisitToRiverDoesNotCauseDrowning()
    {
        var world = new World();
        var river = world.State.Map.GetTile(0, 0);
        river.Terrain = TerrainType.River;
        river.Resources.Add(AbundantFood());
        river.Resources.Add(new ResourcePool { Type = ResourceType.Water, Amount = 10_000f, Capacity = 10_000f, RegenPerTick = 500f });

        var species = new SpeciesDefinition
        {
            Name = "Visitor",
            FoodConsumptionRate = 1f,
            ReproductionRate = 0f,
            StarvationRate = 0f,
        };
        var pop = new Population { Species = species, Count = 100 };
        river.Populations.Add(pop);

        // well under WaterSurvivalThreshold — a short stay should be harmless
        for (var i = 0; i < 5; i++)
            world.Tick();

        Assert.Equal(100, pop.Count);
    }

    // ── Food stratification & ease-of-eating tests ─────────────────────────────

    [Fact]
    public void Tick_SpeciesCannotEatFoodItHasNoEaseFor()
    {
        var world = new World();
        var tile  = world.State.Map.GetTile(0, 0);

        // abundant Browse and Fruit but none of the Graze this species relies on
        tile.Resources.Add(new ResourcePool { Type = ResourceType.Food, FoodSubtype = FoodSubtype.Graze,  Amount = 0f,      Capacity = 1_000f,  RegenPerTick = 0f });
        tile.Resources.Add(new ResourcePool { Type = ResourceType.Food, FoodSubtype = FoodSubtype.Browse, Amount = 10_000f, Capacity = 10_000f, RegenPerTick = 500f });
        tile.Resources.Add(new ResourcePool { Type = ResourceType.Food, FoodSubtype = FoodSubtype.Fruit,  Amount = 10_000f, Capacity = 10_000f, RegenPerTick = 500f });

        var species = new SpeciesDefinition
        {
            Name = "GrazeOnly",
            FoodConsumptionRate = 1f,
            EaseOfEating = { [FoodSubtype.Graze] = 5f }, // Browse and Fruit absent from dict → ease 0
            ReproductionRate = 0f,
            StarvationRate = 1f,
        };
        var pop = new Population { Species = species, Count = 100 };
        tile.Populations.Add(pop);

        world.Tick();

        Assert.Equal(0f, pop.LastSatisfaction, 3);
        Assert.True(pop.Count < 100, "a species with zero ease for Browse/Fruit should starve despite abundant food on the same tile");
    }

    [Fact]
    public void Tick_MigratesTowardEasierFoodOverMoreAbundantHarderFood()
    {
        var world = new World();
        var home = world.State.Map.GetTile(0, 0);
        var easy = world.State.Map.GetTile(1, 0); // less raw food, but this species can eat it easily
        var hard = world.State.Map.GetTile(0, 1); // far more raw food, but entirely inedible to this species

        home.Resources.Add(new ResourcePool { Type = ResourceType.Food, FoodSubtype = FoodSubtype.Graze, Amount = 0f,      Capacity = 100f,    RegenPerTick = 0f });
        easy.Resources.Add(new ResourcePool { Type = ResourceType.Food, FoodSubtype = FoodSubtype.Graze, Amount = 100f,    Capacity = 100f,    RegenPerTick = 0f });
        hard.Resources.Add(new ResourcePool { Type = ResourceType.Food, FoodSubtype = FoodSubtype.Fruit, Amount = 10_000f, Capacity = 10_000f, RegenPerTick = 0f });

        var species = new SpeciesDefinition
        {
            Name = "GrazeSpecialist",
            FoodConsumptionRate = 1f,
            EaseOfEating = { [FoodSubtype.Graze] = 5f }, // Fruit absent from dict → ease 0
            MigrationThreshold = 0.9f,
            StarvationRate = 0.01f,
        };
        var pop = new Population { Species = species, Count = 10 };
        home.Populations.Add(pop);

        world.Tick();

        Assert.Empty(home.Populations.Where(p => p.Count > 0));
        Assert.Single(easy.Populations.Where(p => p.Count > 0));
        Assert.Empty(hard.Populations.Where(p => p.Count > 0));
    }

    // ── Terrain degradation tests ───────────────────────────────────────────────

    [Fact]
    public void Tick_ForestDegradesToPlainsWhenFruitSustainedlyDenuded()
    {
        var world = new World();
        var tile  = world.State.Map.GetTile(0, 0);
        tile.Terrain = TerrainType.Forest;
        tile.Resources.Add(new ResourcePool { Type = ResourceType.Food, FoodSubtype = FoodSubtype.Fruit, Amount = 0f, Capacity = 100f, RegenPerTick = 10f });

        var species = new SpeciesDefinition
        {
            Name = "HeavyBrowser",
            FoodConsumptionRate = 50f, // far more than Fruit can regen — keeps the pool denuded
            EaseOfEating = { [FoodSubtype.Fruit] = 5f },
            ReproductionRate = 0f,
            StarvationRate = 0f,
        };
        tile.Populations.Add(new Population { Species = species, Count = 20 });

        for (var i = 0; i < 61; i++)
            world.Tick();

        Assert.Equal(TerrainType.Plains, tile.Terrain);
    }

    [Fact]
    public void Tick_ForestDoesNotDegradeWhenFruitStaysHealthy()
    {
        var world = new World();
        var tile  = world.State.Map.GetTile(0, 0);
        tile.Terrain = TerrainType.Forest;
        tile.Resources.Add(new ResourcePool { Type = ResourceType.Food, FoodSubtype = FoodSubtype.Fruit, Amount = 10_000f, Capacity = 10_000f, RegenPerTick = 500f });

        var species = new SpeciesDefinition
        {
            Name = "LightBrowser",
            FoodConsumptionRate = 1f,
            EaseOfEating = { [FoodSubtype.Fruit] = 5f },
            ReproductionRate = 0f,
            StarvationRate = 0f,
        };
        tile.Populations.Add(new Population { Species = species, Count = 5 });

        for (var i = 0; i < 61; i++)
            world.Tick();

        Assert.Equal(TerrainType.Forest, tile.Terrain);
    }

    // ── Predation / carnivore tests ──────────────────────────────────────────

    private static SpeciesDefinition PredatorSpecies(
        string name, float rate = 0.5f,
        PreyCategory? preferred = null,
        PreyCategory? accepted = null) => new()
    {
        Name = name,
        PreyConsumptionRate = rate,
        PreferredPrey = preferred.HasValue ? [preferred.Value] : [],
        AcceptedPrey  = accepted.HasValue  ? [accepted.Value]  : [],
        ReproductionRate = 0f,
        StarvationRate = 0f,
    };

    private static SpeciesDefinition PreySpecies(string name, PreyCategory category) => new()
    {
        Name = name,
        AsPreyCategory = category,
        ReproductionRate = 0f,
        StarvationRate = 0f,
    };

    [Fact]
    public void HuntPrey_CarnivoreWithPreferredPreyGetsFullSatisfaction()
    {
        var world     = new World();
        var tile      = world.State.Map.GetTile(0, 0);
        var predator  = PredatorSpecies("Predator", rate: 1f, preferred: PreyCategory.SmallHerbivore);
        var prey      = PreySpecies("Prey", PreyCategory.SmallHerbivore);

        var hunterPop = new Population { Species = predator, Count = 10 };
        var preyPop   = new Population { Species = prey,     Count = 100 };
        tile.Populations.AddRange([hunterPop, preyPop]);

        world.Tick();

        Assert.Equal(1f, hunterPop.LastSatisfaction, 3);
    }

    [Fact]
    public void HuntPrey_AcceptedPreyGivesPartialSatisfaction()
    {
        var world    = new World();
        var tile     = world.State.Map.GetTile(0, 0);
        // predator prefers LargeHerbivore (none present), accepts SmallHerbivore
        var predator = PredatorSpecies("Predator", rate: 1f,
                                       preferred: PreyCategory.LargeHerbivore,
                                       accepted:  PreyCategory.SmallHerbivore);
        var prey     = PreySpecies("Prey", PreyCategory.SmallHerbivore);

        var hunterPop = new Population { Species = predator, Count = 10 };
        var preyPop   = new Population { Species = prey,     Count = 1_000 };
        tile.Populations.AddRange([hunterPop, preyPop]);

        world.Tick();

        // accepted prey is worth 2/3 satisfaction
        Assert.True(hunterPop.LastSatisfaction < 1f, "accepted prey should give less than full satisfaction");
        Assert.True(hunterPop.LastSatisfaction > 0f, "accepted prey should give some satisfaction");
    }

    [Fact]
    public void HuntPrey_PreyPopulationDecreasesWhenHunted()
    {
        var world    = new World();
        var tile     = world.State.Map.GetTile(0, 0);
        var predator = PredatorSpecies("Predator", rate: 5f, preferred: PreyCategory.SmallHerbivore);
        var prey     = PreySpecies("Prey", PreyCategory.SmallHerbivore);

        var hunterPop = new Population { Species = predator, Count = 20 };
        var preyPop   = new Population { Species = prey,     Count = 200 };
        tile.Populations.AddRange([hunterPop, preyPop]);

        world.Tick();

        Assert.True(preyPop.Count < 200, "prey population should decrease after being hunted");
    }

    [Fact]
    public void HuntPrey_CarnivoreWithNoPreyStarves()
    {
        var world    = new World();
        var tile     = world.State.Map.GetTile(0, 0);
        var predator = PredatorSpecies("Predator", rate: 1f, preferred: PreyCategory.SmallHerbivore);

        var hunterPop = new Population { Species = predator, Count = 10 };
        tile.Populations.Add(hunterPop);

        world.Tick();

        Assert.Equal(0f, hunterPop.LastSatisfaction, 3);
    }

    [Fact]
    public void HuntPrey_TwoPredatorsShareAvailablePrey()
    {
        var world    = new World();
        var tile     = world.State.Map.GetTile(0, 0);
        var predator = PredatorSpecies("Predator", rate: 1f, preferred: PreyCategory.SmallHerbivore);
        var prey     = PreySpecies("Prey", PreyCategory.SmallHerbivore);

        // 2 hunters each need 1 prey-unit, only 1 prey individual available → both get partial sat
        var hunter1 = new Population { Species = predator, Count = 1 };
        var hunter2 = new Population { Species = predator, Count = 1 };
        var preyPop = new Population { Species = prey,     Count = 1 };
        tile.Populations.AddRange([hunter1, hunter2, preyPop]);

        world.Tick();

        Assert.True(hunter1.LastSatisfaction < 1f, "hunter1 should not get full satisfaction when prey is shared");
        Assert.True(hunter2.LastSatisfaction < 1f, "hunter2 should not get full satisfaction when prey is shared");
    }

    [Fact]
    public void HuntPrey_CarnivoreMigratesWhenNoPreyOnTile()
    {
        var world      = new World();
        var emptyTile  = world.State.Map.GetTile(0, 0);
        var preyTile   = world.State.Map.GetTile(1, 0);
        var predator   = new SpeciesDefinition
        {
            Name = "Predator",
            PreyConsumptionRate = 1f,
            PreferredPrey = [PreyCategory.SmallHerbivore],
            MigrationThreshold = 0.9f,
            ReproductionRate = 0f,
            StarvationRate = 0f,
        };
        var prey       = PreySpecies("Prey", PreyCategory.SmallHerbivore);

        preyTile.Populations.Add(new Population { Species = prey, Count = 1_000 });
        emptyTile.Populations.Add(new Population { Species = predator, Count = 10 });

        world.Tick();

        Assert.Empty(emptyTile.Populations.Where(p => p.Count > 0 && p.Species.IsPredator));
        Assert.Single(preyTile.Populations.Where(p => p.Count > 0 && p.Species.IsPredator));
    }

    [Fact]
    public void HuntPrey_FunctionalResponseLeavesRefugeInsteadOfWiping()
    {
        var world    = new World();
        var tile     = world.State.Map.GetTile(0, 0);
        // overwhelming predator demand (100 rate × 10 = 1000) against a small herd
        var predator = PredatorSpecies("Predator", rate: 100f, preferred: PreyCategory.SmallHerbivore);
        var prey     = PreySpecies("Prey", PreyCategory.SmallHerbivore);

        var hunterPop = new Population { Species = predator, Count = 10 };
        var preyPop   = new Population { Species = prey,     Count = 10 };
        tile.Populations.AddRange([hunterPop, preyPop]);

        world.Tick();

        // the density-dependent refuge means even a huge predator force cannot take the
        // whole herd in a single tick — some prey always survive to recover
        Assert.True(preyPop.Count > 0, "functional response should leave a prey refuge, not a single-tick wipeout");
        Assert.True(preyPop.Count < 10, "predation should still claim some of the herd");
    }

    [Fact]
    public void Migrate_PreyScatterFromInvadingPredator()
    {
        var world = new World();
        var tile  = world.State.Map.GetTile(0, 0);
        // low predator rate so hunting barely dents the herd — we're testing the scatter split,
        // not attrition. Prey have no resource pressure, so any move is purely the scatter trigger.
        var predator = PredatorSpecies("Predator", rate: 0.01f, preferred: PreyCategory.SmallHerbivore);
        var prey     = PreySpecies("Prey", PreyCategory.SmallHerbivore);

        tile.Populations.Add(new Population { Species = predator, Count = 5 });
        tile.Populations.Add(new Population { Species = prey,     Count = 20 });

        world.Tick();

        var scattered = world.State.Map.AllTiles()
            .Where(t => t != tile)
            .Sum(t => t.Populations.Where(p => p.Species == prey).Sum(p => p.Count));

        Assert.True(scattered > 0, "prey should split off a fleeing group to a neighbouring tile");
        var stayed = tile.Populations.Where(p => p.Species == prey).Sum(p => p.Count);
        Assert.True(stayed > 0, "stragglers should remain behind for the predator, not the whole herd fleeing");
    }
}
