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
            StarvationRate = 0.08f
        };

        var brachiosaurus = new SpeciesDefinition
        {
            Name = "Brachiosaurus",
            ConsumptionRates = { [ResourceType.Food] = 5f, [ResourceType.Water] = 2f },
            ReproductionRate = 0.03f,
            StarvationRate = 0.04f
        };

        var pachycephalosaurus = new SpeciesDefinition
        {
            Name = "Pachycephalosaurus",
            ConsumptionRates = { [ResourceType.Food] = 1f },
            ReproductionRate = 0.08f,
            StarvationRate = 0.12f
        };

        var world = new World();
        var map = world.State.Map;

        // food regen decreases diagonally from NW (lush) to SE (arid)
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
                    Amount = foodRegen * 15f,
                    Capacity = foodRegen * 20f,
                    RegenPerTick = foodRegen
                });

                if (y is >= 3 and <= 6)
                    tile.Resources.Add(new ResourcePool
                    {
                        Type = ResourceType.Water,
                        Amount = 40f,
                        Capacity = 60f,
                        RegenPerTick = 4f
                    });
            }
        }

        // Triceratops: northwest — good food, access to water band
        map.GetTile(1, 1).Populations.Add(new Population { Species = triceratops, Count = 150 });
        map.GetTile(2, 4).Populations.Add(new Population { Species = triceratops, Count = 80 });

        // Brachiosaurus: center — heavy consumers competing for the water band
        map.GetTile(5, 4).Populations.Add(new Population { Species = brachiosaurus, Count = 50 });

        // Pachycephalosaurus: southeast — sparse resources, high pressure environment
        map.GetTile(8, 8).Populations.Add(new Population { Species = pachycephalosaurus, Count = 200 });
        map.GetTile(7, 6).Populations.Add(new Population { Species = pachycephalosaurus, Count = 120 });

        return world;
    }
}
