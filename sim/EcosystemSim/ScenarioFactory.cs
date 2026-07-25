namespace EcosystemSim;

public static class ScenarioFactory
{
    public static ScenarioSession Start(WorldState state, ScenarioKind kind)
    {
        var session = kind switch
        {
            ScenarioKind.LocustPlague => StartLocustPlague(state),
            _ => new ScenarioSession
            {
                Kind = ScenarioKind.Sandbox,
                Mode = ScenarioMode.Sandbox,
                Name = "Sandbox",
                Description = "Unrestricted ecosystem simulation with unlimited interventions.",
                StartTick = state.Tick,
            },
        };

        state.Scenario = session;
        session.Refresh(state);
        return session;
    }

    private static ScenarioSession StartLocustPlague(WorldState state)
    {
        SeedLocustOutbreak(state);

        return new ScenarioSession
        {
            Kind = ScenarioKind.LocustPlague,
            Mode = ScenarioMode.Challenge,
            Name = "Locust Plague",
            Description = "Protect the dinosaur lineages and plains vegetation during a three-year outbreak.",
            StartTick = state.Tick,
            DurationDays = World.DaysPerYear * 3,
            InitialActionPoints = 10,
            ActionPointsRemaining = 10,
        };
    }

    private static void SeedLocustOutbreak(WorldState state)
    {
        var locustFaction = state.Factions.First(f => f.PrimarySpecies.EffectiveRootName == "Locust");
        var locustSpecies = locustFaction.PrimarySpecies;

        foreach (var pop in locustFaction.Populations.Where(p => p.Count > 0))
            pop.Count = 0;

        var outbreakTiles = state.Map.AllTiles()
            .Where(t => t.Terrain == TerrainType.Plains)
            .OrderByDescending(t => t.Resources
                .Where(r => r.FoodSubtype == FoodSubtype.Graze)
                .Sum(r => r.Capacity))
            .Take(12)
            .ToList();

        foreach (var tile in outbreakTiles)
        {
            var pop = new Population { Species = locustSpecies, Count = 42 };
            locustFaction.AddPopulation(pop);
            tile.AddPopulation(pop);
        }

        foreach (var pool in state.Map.AllTiles()
                     .Where(t => t.Terrain == TerrainType.Plains)
                     .SelectMany(t => t.Resources)
                     .Where(r => r.FoodSubtype == FoodSubtype.Graze))
            pool.Amount = Math.Min(pool.Amount, pool.Capacity * 0.30f);
    }
}
