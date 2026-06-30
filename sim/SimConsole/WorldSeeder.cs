using EcosystemSim;

namespace SimConsole;

public static class WorldSeeder
{
    public static World CreateDemo()
    {
        var triceratops = new SpeciesDefinition
        {
            Name = "Triceratops",
            ConsumptionRates = { [ResourceType.Food] = 2f, [ResourceType.Water] = 1f },
            ByproductRates   = { [ByproductType.Fertilizer] = 0.08f },
            ReproductionRate = 0.05f,
            StarvationRate = 0.05f,
            MigrationThreshold = 0.75f,
            WarAggression = 0.3f,
            CombatStrength = 1.4f,
            Immunity = 0.3f
        };

        var brachiosaurus = new SpeciesDefinition
        {
            Name = "Brachiosaurus",
            ConsumptionRates = { [ResourceType.Food] = 5f, [ResourceType.Water] = 2f },
            ByproductRates   = { [ByproductType.Fertilizer] = 0.20f }, // largest animal, most impactful
            ReproductionRate = 0.03f,
            StarvationRate = 0.03f,
            MigrationThreshold = 0.6f,
            WarAggression = 0.1f,
            CombatStrength = 0.6f,
            Immunity = 0.15f  // large, slow immune response — very vulnerable
        };

        var pachycephalosaurus = new SpeciesDefinition
        {
            Name = "Pachycephalosaurus",
            ConsumptionRates = { [ResourceType.Food] = 1f },
            ByproductRates   = { [ByproductType.Fertilizer] = 0.06f },
            ReproductionRate = 0.08f,
            StarvationRate = 0.06f,
            MigrationThreshold = 0.5f,
            WarAggression = 0.5f,
            CombatStrength = 0.9f,
            Immunity = 0.55f  // scrappy and resilient
        };

        var world = new World();
        var map = world.State.Map;

        for (var x = 0; x < map.Width; x++)
        {
            for (var y = 0; y < map.Height; y++)
            {
                var tile = map.GetTile(x, y);

                var distFromNw = (float)(x + y) / (map.Width + map.Height - 2);
                var foodRegen = MathF.Round(20f * (1f - 0.7f * distFromNw), 1);
                tile.Resources.Add(new ResourcePool
                {
                    Type = ResourceType.Food,
                    Amount = foodRegen * 10f,
                    Capacity = foodRegen * 20f,
                    RegenPerTick = foodRegen
                });

                if (y is >= 3 and <= 6)
                    tile.Resources.Add(new ResourcePool
                    {
                        Type = ResourceType.Water,
                        Amount = 80f,
                        Capacity = 120f,
                        RegenPerTick = 15f
                    });
            }
        }

        // factions — same species can have multiple independent factions
        var highlandTric  = new Faction { Name = "Highland Tric",  PrimarySpecies = triceratops };
        var valleyTric    = new Faction { Name = "Valley Tric",    PrimarySpecies = triceratops };
        var riverBrachio  = new Faction { Name = "River Brachio",  PrimarySpecies = brachiosaurus };
        var easternPachy  = new Faction { Name = "Eastern Pachy",  PrimarySpecies = pachycephalosaurus };
        var midlandPachy  = new Faction { Name = "Midland Pachy",  PrimarySpecies = pachycephalosaurus };

        world.State.Factions.AddRange([highlandTric, valleyTric, riverBrachio, easternPachy, midlandPachy]);

        void Place(Faction faction, int x, int y, int count)
        {
            var pop = new Population { Species = faction.PrimarySpecies, Count = count };
            faction.AddPopulation(pop);
            map.GetTile(x, y).AddPopulation(pop);
        }

        // Triceratops: NW, starting away from water — should migrate south toward water band
        Place(highlandTric, 1, 1, 50);
        Place(valleyTric,   3, 2, 40);

        // Brachiosaurus: center, already in the water band
        Place(riverBrachio, 5, 4, 25);

        // Pachycephalosaurus: SE, no water needed — will compete for food and migrate
        Place(easternPachy, 8, 8, 80);
        Place(midlandPachy, 7, 6, 60);

        return world;
    }

    public static readonly Disease DinoFever = new()
    {
        Name = "Dino Fever",
        MortalityRate = 0.04f,
        SpreadRate = 0.18f,
        RecoveryRate = 0.015f
    };
}
