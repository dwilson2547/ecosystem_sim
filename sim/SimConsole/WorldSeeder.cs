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
            ReproductionRate = 0.05f,
            StarvationRate = 0.05f,
            MigrationThreshold = 0.75f  // proactive — moves before resources are fully depleted
        };

        var brachiosaurus = new SpeciesDefinition
        {
            Name = "Brachiosaurus",
            ConsumptionRates = { [ResourceType.Food] = 5f, [ResourceType.Water] = 2f },
            ReproductionRate = 0.03f,
            StarvationRate = 0.03f,
            MigrationThreshold = 0.6f
        };

        var pachycephalosaurus = new SpeciesDefinition
        {
            Name = "Pachycephalosaurus",
            ConsumptionRates = { [ResourceType.Food] = 1f },
            ReproductionRate = 0.08f,
            StarvationRate = 0.06f,
            MigrationThreshold = 0.5f
        };

        var world = new World();
        var map = world.State.Map;

        // food regen decreases diagonally NW (lush) → SE (arid)
        // water runs through a central horizontal band (y = 3–6)
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
                        RegenPerTick = 15f   // was 4 — enough to sustain meaningful population sizes
                    });
            }
        }

        // Triceratops: placed in lush NW but away from the water band —
        // should migrate south toward water within a few ticks
        map.GetTile(1, 1).Populations.Add(new Population { Species = triceratops, Count = 50 });
        map.GetTile(3, 2).Populations.Add(new Population { Species = triceratops, Count = 40 });

        // Brachiosaurus: center, already in the water band, heavy resource demand
        map.GetTile(5, 4).Populations.Add(new Population { Species = brachiosaurus, Count = 25 });

        // Pachycephalosaurus: SE, no water needed — will compete for sparse food and migrate
        // within the dry region as food depletes on their starting tiles
        map.GetTile(8, 8).Populations.Add(new Population { Species = pachycephalosaurus, Count = 80 });
        map.GetTile(7, 6).Populations.Add(new Population { Species = pachycephalosaurus, Count = 60 });

        return world;
    }
}
