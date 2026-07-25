using Xunit;

namespace EcosystemSim.Tests;

public class ScenarioTests
{
    [Fact]
    public void StartScenario_SandboxHasUnlimitedActionsAndNoObjectives()
    {
        var world = CreateScenarioWorld();

        var scenario = world.StartScenario(ScenarioKind.Sandbox);

        Assert.Equal(ScenarioMode.Sandbox, scenario.Mode);
        Assert.False(scenario.IsChallenge);
        Assert.Empty(scenario.Objectives);
        Assert.Equal(ScenarioStatus.Active, scenario.Status);
    }

    [Fact]
    public void StartScenario_LocustPlagueSeedsOutbreakAndObjectives()
    {
        var world = CreateScenarioWorld();

        var scenario = world.StartScenario(ScenarioKind.LocustPlague);
        var locustCount = world.State.Map.AllPopulations()
            .Where(p => p.Count > 0 && p.Species.EffectiveRootName == "Locust")
            .Sum(p => p.Count);

        Assert.Equal(504, locustCount);
        Assert.Equal(World.DaysPerYear * 3, scenario.DurationDays);
        Assert.Equal(10, scenario.ActionPointsRemaining);
        Assert.Equal(3, scenario.Objectives.Count);
        Assert.False(scenario.Objectives.Single(o => o.Id == "locust-control").IsMet);
        Assert.True(scenario.Objectives.Single(o => o.Id == "dinosaur-survival").IsMet);
    }

    [Fact]
    public void TryApplyScenarioAction_CullLocustsSpendsPointsAndRefreshesObjectives()
    {
        var world = CreateScenarioWorld();
        var scenario = world.StartScenario(ScenarioKind.LocustPlague);
        var tile = world.State.Map.AllTiles().First(t =>
            t.Populations.Any(p => p.Count > 0 && p.Species.EffectiveRootName == "Locust"));

        var result = world.TryApplyScenarioAction(new CullLocustsAction
        {
            TileX = tile.X,
            TileY = tile.Y,
        });

        Assert.True(result.Success);
        Assert.Equal(9, scenario.ActionPointsRemaining);
        Assert.DoesNotContain(tile.Populations, p =>
            p.Count > 0 && p.Species.EffectiveRootName == "Locust");
    }

    [Fact]
    public void TryApplyScenarioAction_InvalidActionDoesNotSpendPoints()
    {
        var world = CreateScenarioWorld();
        var scenario = world.StartScenario(ScenarioKind.LocustPlague);
        var forest = world.State.Map.GetTile(0, 0);
        forest.Terrain = TerrainType.Forest;

        var result = world.TryApplyScenarioAction(new RestoreGrassAction
        {
            TileX = forest.X,
            TileY = forest.Y,
        });

        Assert.False(result.Success);
        Assert.Equal(10, scenario.ActionPointsRemaining);
        Assert.Contains("Plains", result.Message);
    }

    [Fact]
    public void TryApplyScenarioAction_RestoreGrassAndSeedMeganeuraUpdateSelectedTile()
    {
        var world = CreateScenarioWorld();
        var scenario = world.StartScenario(ScenarioKind.LocustPlague);
        var tile = world.State.Map.AllTiles().First(t => t.Terrain == TerrainType.Plains);
        var grass = tile.Resources.Single(r => r.FoodSubtype == FoodSubtype.Graze);
        grass.Amount = 0f;

        var restore = world.TryApplyScenarioAction(new RestoreGrassAction
        {
            TileX = tile.X,
            TileY = tile.Y,
        });
        var seed = world.TryApplyScenarioAction(new SeedMeganeuraAction
        {
            TileX = tile.X,
            TileY = tile.Y,
        });

        Assert.True(restore.Success);
        Assert.True(seed.Success);
        Assert.Equal(grass.Capacity, grass.Amount, 3);
        Assert.Equal(5, tile.Populations.Single(p =>
            p.Count > 0 && p.Species.EffectiveRootName == "Meganeura").Count);
        Assert.Equal(5, scenario.ActionPointsRemaining);
    }

    [Fact]
    public void LocustPlague_ResolvesVictoryWhenFinalObjectivesAreMet()
    {
        var world = CreateScenarioWorld();
        var scenario = world.StartScenario(ScenarioKind.LocustPlague);
        var locustTiles = world.State.Map.AllTiles()
            .Where(t => t.Populations.Any(p =>
                p.Count > 0 && p.Species.EffectiveRootName == "Locust"))
            .Take(2)
            .ToList();

        foreach (var tile in locustTiles)
            world.TryApplyScenarioAction(new CullLocustsAction { TileX = tile.X, TileY = tile.Y });

        world.State.Tick = scenario.StartTick + scenario.DurationDays;
        scenario.Refresh(world.State);

        Assert.Equal(ScenarioStatus.Won, scenario.Status);
    }

    [Fact]
    public void LocustPlague_ResolvesDefeatWhenDinosaurLineageIsExtinct()
    {
        var world = CreateScenarioWorld();
        var scenario = world.StartScenario(ScenarioKind.LocustPlague);
        foreach (var pop in world.State.Map.AllPopulations()
                     .Where(p => p.Species.EffectiveRootName == "Tyrannosaurus"))
            pop.Count = 0;

        world.State.Tick = scenario.StartTick + scenario.DurationDays;
        scenario.Refresh(world.State);

        Assert.Equal(ScenarioStatus.Lost, scenario.Status);
        Assert.False(scenario.Objectives.Single(o => o.Id == "dinosaur-survival").IsMet);
    }

    [Fact]
    public void StartScenario_DroughtRecoverySeedsStressAndObjectives()
    {
        var world = CreateScenarioWorld();

        var scenario = world.StartScenario(ScenarioKind.DroughtRecovery);

        Assert.Equal(World.DaysPerYear * 2, scenario.DurationDays);
        Assert.Equal(10, scenario.ActionPointsRemaining);
        Assert.Equal(Weather.Drought, world.State.CurrentWeather);
        Assert.Equal(3, scenario.Objectives.Count);
        Assert.Equal("10%", scenario.Objectives.Single(o => o.Id == "freshwater-health").CurrentValue);
        Assert.Equal("20%", scenario.Objectives.Single(o => o.Id == "vegetation-health").CurrentValue);
    }

    [Fact]
    public void TryApplyScenarioAction_DroughtActionsRestoreResourcesAndSeedRain()
    {
        var world = CreateScenarioWorld();
        var scenario = world.StartScenario(ScenarioKind.DroughtRecovery);
        var tile = world.State.Map.GetTile(4, 4);

        var water = world.TryApplyScenarioAction(new CreateWateringHolesAction
        {
            TileX = tile.X,
            TileY = tile.Y,
        });
        var vegetation = world.TryApplyScenarioAction(new RestoreVegetationAction
        {
            TileX = tile.X,
            TileY = tile.Y,
        });
        var rain = world.TryApplyScenarioAction(new SeedRainAction
        {
            TileX = tile.X,
            TileY = tile.Y,
        });

        Assert.True(water.Success);
        Assert.True(vegetation.Success);
        Assert.True(rain.Success);
        Assert.Equal(3, scenario.ActionPointsRemaining);
        Assert.Equal(Weather.Rainy, world.State.CurrentWeather);
        Assert.Equal(SeedRainAction.RainDurationDays, world.State.WeatherTicksRemaining);
        var waterPool = tile.Resources.Single(r => r.Type == ResourceType.Water);
        Assert.True(waterPool.Capacity >= 80f);
        Assert.Equal(waterPool.Capacity, waterPool.Amount, 3);
        Assert.All(tile.Resources.Where(r => r.Type == ResourceType.Food),
            pool => Assert.Equal(pool.Capacity, pool.Amount, 3));
    }

    [Fact]
    public void TryApplyScenarioAction_RejectsActionFromDifferentChallenge()
    {
        var world = CreateScenarioWorld();
        var scenario = world.StartScenario(ScenarioKind.DroughtRecovery);
        var tile = world.State.Map.GetTile(0, 0);

        var result = world.TryApplyScenarioAction(new CullLocustsAction
        {
            TileX = tile.X,
            TileY = tile.Y,
        });

        Assert.False(result.Success);
        Assert.Equal(10, scenario.ActionPointsRemaining);
        Assert.Contains("not available", result.Message);
    }

    [Fact]
    public void DroughtRecovery_ResolvesVictoryWhenFinalObjectivesAreMet()
    {
        var world = CreateScenarioWorld();
        var scenario = world.StartScenario(ScenarioKind.DroughtRecovery);
        foreach (var pool in world.State.Map.AllTiles()
                     .Where(t => !TerrainStats.IsOcean(t.Terrain))
                     .SelectMany(t => t.Resources))
            pool.Amount = pool.Capacity;

        world.State.Tick = scenario.StartTick + scenario.DurationDays;
        scenario.Refresh(world.State);

        Assert.Equal(ScenarioStatus.Won, scenario.Status);
    }

    [Fact]
    public void DroughtRecovery_ReturnsToDroughtAfterSeededRainExpires()
    {
        var world = CreateScenarioWorld();
        world.StartScenario(ScenarioKind.DroughtRecovery);
        var tile = world.State.Map.GetTile(0, 0);
        world.TryApplyScenarioAction(new SeedRainAction { TileX = tile.X, TileY = tile.Y });
        world.State.WeatherTicksRemaining = 0;

        world.Tick();

        Assert.Equal(Weather.Drought, world.State.CurrentWeather);
        Assert.Equal(World.DaysPerSeason, world.State.WeatherTicksRemaining);
    }

    [Fact]
    public void StartScenario_InitializesHistoryAndStartEvent()
    {
        var world = CreateScenarioWorld();

        world.StartScenario(ScenarioKind.LocustPlague);

        var sample = Assert.Single(world.State.History);
        Assert.Equal(0, sample.Tick);
        Assert.Equal(504, sample.LineagePopulations["Locust"]);
        Assert.Contains("locust-control", sample.ObjectiveProgress.Keys);
        Assert.Contains(world.State.Events, e => e.Message == "Started Locust Plague.");
    }

    [Fact]
    public void Tick_RecordsMonthlyPopulationAndObjectiveHistory()
    {
        var world = CreateScenarioWorld();
        world.StartScenario(ScenarioKind.LocustPlague);

        for (var day = 0; day < World.HistoryIntervalDays; day++)
            world.Tick();

        Assert.Equal(2, world.State.History.Count);
        Assert.Equal(World.HistoryIntervalDays, world.State.History[^1].Tick);
        Assert.Contains("dinosaur-survival", world.State.History[^1].ObjectiveProgress.Keys);
    }

    [Fact]
    public void ScenarioAction_AddsLocatedWorldEvent()
    {
        var world = CreateScenarioWorld();
        world.StartScenario(ScenarioKind.LocustPlague);
        var tile = world.State.Map.AllTiles().First(t =>
            t.Populations.Any(p => p.Count > 0 && p.Species.EffectiveRootName == "Locust"));

        world.TryApplyScenarioAction(new CullLocustsAction { TileX = tile.X, TileY = tile.Y });

        var worldEvent = world.State.Events.Last();
        Assert.Equal(tile.X, worldEvent.TileX);
        Assert.Equal(tile.Y, worldEvent.TileY);
        Assert.Contains("Culled", worldEvent.Message);
    }

    [Fact]
    public void Tick_RecordsLineageExtinctionAsCriticalEvent()
    {
        var world = new World(1, 1, seed: 42);
        var species = new SpeciesDefinition
        {
            Name = "Fragile Lineage",
            RootName = "Fragile Lineage",
            FoodConsumptionRate = 1f,
            BreedingRate = 0f,
            FoodDeprivationToleranceDays = 0,
            FoodDeprivationMortalityRate = 1f,
            MigrationThreshold = 0f,
        };
        world.State.Map.GetTile(0, 0).AddPopulation(new Population
        {
            Species = species,
            Count = 1,
        });
        world.StartScenario(ScenarioKind.Sandbox);

        world.Tick();

        Assert.Contains(world.State.Events, e =>
            e.Severity == WorldEventSeverity.Critical
            && e.Message == "Fragile Lineage became extinct.");
        for (var day = 1; day < World.HistoryIntervalDays; day++)
            world.Tick();
        Assert.Equal(0, world.State.History[^1].LineagePopulations["Fragile Lineage"]);
    }

    private static World CreateScenarioWorld()
    {
        var world = new World(10, 10, seed: 42);
        foreach (var tile in world.State.Map.AllTiles())
        {
            tile.Terrain = TerrainType.Plains;
            tile.Resources.Add(new ResourcePool
            {
                Type = ResourceType.Food,
                FoodSubtype = FoodSubtype.Graze,
                Amount = 100f,
                Capacity = 100f,
                RegenPerTick = 5f,
            });
            tile.Resources.Add(new ResourcePool
            {
                Type = ResourceType.Water,
                Amount = 100f,
                Capacity = 100f,
                RegenPerTick = 5f,
            });
        }

        AddFaction(world, "Locust Swarm", "Locust", 0, 0, 20, PreyCategory.Insect);
        AddFaction(world, "Dragonflies", "Meganeura", 1, 0, 5);
        AddFaction(world, "Triceratops", "Triceratops", 2, 0, 10);
        AddFaction(world, "Alamosaurus", "Alamosaurus", 3, 0, 10);
        AddFaction(world, "Parasaurolophus", "Parasaurolophus", 4, 0, 10);
        AddFaction(world, "Tyrant Pack", "Tyrannosaurus", 5, 0, 5);
        return world;
    }

    private static void AddFaction(
        World world,
        string factionName,
        string speciesName,
        int x,
        int y,
        int count,
        PreyCategory? preyCategory = null)
    {
        var species = new SpeciesDefinition
        {
            Name = speciesName,
            RootName = speciesName,
            AsPreyCategory = preyCategory,
            MaxCount = speciesName == "Meganeura" ? 18 : 0,
            BreedingRate = 0f,
            FoodDeprivationMortalityRate = 0f,
        };
        var faction = new Faction { Name = factionName, PrimarySpecies = species };
        var pop = new Population { Species = species, Count = count };
        faction.AddPopulation(pop);
        world.State.Factions.Add(faction);
        world.State.Map.GetTile(x, y).AddPopulation(pop);
    }
}
