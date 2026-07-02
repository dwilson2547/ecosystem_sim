using EcosystemSim;

namespace SimConsole;

public static class WorldSeeder
{
    public static World CreateDemo()
    {
        var triceratops = new SpeciesDefinition
        {
            Name = "Triceratops",
            RootName = "Triceratops",
            ConsumptionRates = { [ResourceType.Food] = 2f, [ResourceType.Water] = 1f },
            FoodPreferences  = { FoodSubtype.Graze },
            AcceptedFoods    = { FoodSubtype.Browse, FoodSubtype.Roots },
            ByproductRates   = { [ByproductType.Fertilizer] = 0.08f },
            ReproductionRate = 0.015f,
            StarvationRate   = 0.015f,
            MigrationThreshold = 0.75f,
            WarAggression = 0.3f,
            CombatStrength = 1.4f,
            Immunity = 0.3f
        };

        var brachiosaurus = new SpeciesDefinition
        {
            Name = "Brachiosaurus",
            RootName = "Brachiosaurus",
            ConsumptionRates = { [ResourceType.Food] = 5f, [ResourceType.Water] = 2f },
            FoodPreferences  = { FoodSubtype.Browse },
            AcceptedFoods    = { FoodSubtype.Fruit, FoodSubtype.Graze },
            ByproductRates   = { [ByproductType.Fertilizer] = 0.20f },
            ReproductionRate = 0.008f,
            StarvationRate   = 0.008f,
            MigrationThreshold = 0.6f,
            WarAggression = 0.1f,
            CombatStrength = 0.6f,
            Immunity = 0.15f
        };

        var pachycephalosaurus = new SpeciesDefinition
        {
            Name = "Pachycephalosaurus",
            RootName = "Pachycephalosaurus",
            ConsumptionRates = { [ResourceType.Food] = 1f },
            FoodPreferences  = { FoodSubtype.Fruit, FoodSubtype.Roots },
            AcceptedFoods    = { FoodSubtype.Graze, FoodSubtype.Browse },
            ByproductRates   = { [ByproductType.Fertilizer] = 0.06f },
            ReproductionRate = 0.02f,
            StarvationRate   = 0.015f,
            MigrationThreshold = 0.5f,
            WarAggression = 0.5f,
            CombatStrength = 0.9f,
            Immunity = 0.55f
        };

        var mosasaurus = new SpeciesDefinition
        {
            Name = "Mosasaurus",
            RootName = "Mosasaurus",
            ConsumptionRates = { [ResourceType.Food] = 4f },
            FoodPreferences  = { FoodSubtype.Shrimp, FoodSubtype.Crustacean },
            AcceptedFoods    = { FoodSubtype.Fish },
            ReproductionRate = 0.01f,
            StarvationRate   = 0.012f,
            MigrationThreshold = 0.85f,
            WarAggression = 0.4f,
            CombatStrength = 2.0f,
            Immunity = 0.4f
        };

        var plesiosaur = new SpeciesDefinition
        {
            Name = "Plesiosaur",
            RootName = "Plesiosaur",
            ConsumptionRates = { [ResourceType.Food] = 4f },
            FoodPreferences  = { FoodSubtype.Whale, FoodSubtype.Squid },
            AcceptedFoods    = { FoodSubtype.Fish },
            ReproductionRate = 0.006f,
            StarvationRate   = 0.008f,
            MigrationThreshold = 0.85f,
            WarAggression = 0.2f,
            CombatStrength = 3.0f,
            Immunity = 0.35f
        };

        var world = new World(16, 10);
        var map   = world.State.Map;

        // terrain layout — rows y=0 (north) to y=9 (south), cols x=0 (west) to x=15 (east)
        // H=Highland  F=Forest  R=River  S=Swamp  D=Desert  P=Plains
        // C=ShallowOcean  O=DeepOcean
        // coastline runs diagonally; ocean columns expand mid-map forming a shallow bay
        var terrainRows = new[]
        {
            "HHHPPPDDDPCCOOOO", // y=0
            "HHPPPDDDPPCCOOOO", // y=1  Highland Tric at (1,1)
            "HFFFPPPDPCCOOOOO", // y=2  Valley   Tric at (3,2)
            "PFRRRPPPPCCOOOOO", // y=3
            "PFRRRRPPCCOOOOOO", // y=4  River Brachio at (5,4)
            "PPRRRRRPCCOOOOOO", // y=5  Mosasaurus at (9,5); Plesiosaur at (13,5)
            "PSSRRRPPFCCOOOOO", // y=6  Midland  Pachy at (7,6)
            "PSSSPPPFFCCOOOOO", // y=7
            "DSPPPPFFFCOOOOOO", // y=8  Eastern  Pachy at (8,8)
            "DDDPPPPPDC COOOOO", // y=9 — NOTE: strip the space before use
        };

        // fix the y=9 string (written with space for readability)
        terrainRows[9] = "DDDPPPPPDCCOOOOO";

        var charToTerrain = new Dictionary<char, TerrainType>
        {
            ['H'] = TerrainType.Highland,
            ['F'] = TerrainType.Forest,
            ['R'] = TerrainType.River,
            ['S'] = TerrainType.Swamp,
            ['D'] = TerrainType.Desert,
            ['P'] = TerrainType.Plains,
            ['C'] = TerrainType.ShallowOcean,
            ['O'] = TerrainType.DeepOcean,
        };

        // typed food pools per terrain type: (subtype, regenPerTick, capacity)
        var landFoodByTerrain = new Dictionary<TerrainType, List<(FoodSubtype subtype, float regen, float cap)>>
        {
            [TerrainType.Plains]   = [(FoodSubtype.Graze,  10f, 200f)],
            [TerrainType.Forest]   = [(FoodSubtype.Browse, 12f, 240f), (FoodSubtype.Fruit,  4f,  80f)],
            [TerrainType.Swamp]    = [(FoodSubtype.Roots,   5f, 100f), (FoodSubtype.Fruit,  3f,  60f)],
            [TerrainType.Desert]   = [(FoodSubtype.Graze,   2f,  40f)],
            [TerrainType.Highland] = [(FoodSubtype.Browse,  8f, 160f)],
            [TerrainType.River]    = [(FoodSubtype.Graze,   8f, 160f), (FoodSubtype.Browse, 6f, 120f), (FoodSubtype.Fish, 4f, 80f)],
        };

        var oceanFoodByTerrain = new Dictionary<TerrainType, List<(FoodSubtype subtype, float regen, float cap)>>
        {
            [TerrainType.ShallowOcean] = [(FoodSubtype.Fish, 10f, 200f), (FoodSubtype.Shrimp, 15f, 300f), (FoodSubtype.Crustacean, 8f, 160f)],
            [TerrainType.DeepOcean]    = [(FoodSubtype.Fish,  5f, 100f), (FoodSubtype.Squid,  12f, 240f), (FoodSubtype.Whale,      3f,  60f)],
        };

        var waterByTerrain = new Dictionary<TerrainType, (float regen, float capacity)>
        {
            [TerrainType.River] = (15f, 200f),
            [TerrainType.Swamp] = ( 8f, 120f),
        };

        for (var y = 0; y < map.Height; y++)
        for (var x = 0; x < map.Width;  x++)
        {
            var terrain = charToTerrain[terrainRows[y][x]];
            var tile    = map.GetTile(x, y);
            tile.Terrain = terrain;

            var foodList = landFoodByTerrain.TryGetValue(terrain, out var lf) ? lf
                         : oceanFoodByTerrain.TryGetValue(terrain, out var of) ? of
                         : null;

            if (foodList is not null)
                foreach (var (subtype, regen, cap) in foodList)
                    tile.Resources.Add(new ResourcePool
                    {
                        Type         = ResourceType.Food,
                        FoodSubtype  = subtype,
                        Amount       = regen * 2f,
                        Capacity     = cap,
                        RegenPerTick = regen,
                    });

            if (waterByTerrain.TryGetValue(terrain, out var water))
                tile.Resources.Add(new ResourcePool
                {
                    Type = ResourceType.Water, Amount = water.regen * 2f,
                    Capacity = water.capacity, RegenPerTick = water.regen,
                });
        }

        var highlandTric  = new Faction { Name = "Highland Tric",  PrimarySpecies = triceratops };
        var valleyTric    = new Faction { Name = "Valley Tric",    PrimarySpecies = triceratops };
        var riverBrachio  = new Faction { Name = "River Brachio",  PrimarySpecies = brachiosaurus };
        var easternPachy  = new Faction { Name = "Eastern Pachy",  PrimarySpecies = pachycephalosaurus };
        var midlandPachy  = new Faction { Name = "Midland Pachy",  PrimarySpecies = pachycephalosaurus };
        var shallowFleet  = new Faction { Name = "Shallow Fleet",  PrimarySpecies = mosasaurus };
        var deepDwellers  = new Faction { Name = "Deep Dwellers",  PrimarySpecies = plesiosaur };

        world.State.Factions.AddRange([highlandTric, valleyTric, riverBrachio, easternPachy, midlandPachy, shallowFleet, deepDwellers]);

        void Place(Faction faction, int x, int y, int count)
        {
            var pop = new Population { Species = faction.PrimarySpecies, Count = count };
            faction.AddPopulation(pop);
            map.GetTile(x, y).AddPopulation(pop);
        }

        // Triceratops: start in Highland/Forest — no water nearby, must migrate toward River/Swamp
        Place(highlandTric, 1, 1, 50);
        Place(valleyTric,   3, 2, 40);

        // Brachiosaurus: River — water-rich, high food demand
        Place(riverBrachio, 5, 4, 25);

        // Pachycephalosaurus: south — food only, will compete and migrate freely
        Place(easternPachy, 8, 8, 80);
        Place(midlandPachy, 7, 6, 60);

        // Marine species: strictly in their ocean biome
        // y=5: "PPRRRRRPCCOOOOOO" → col 9=C (ShallowOcean), col 13=O (DeepOcean)
        Place(shallowFleet, 9, 5,  20);
        Place(deepDwellers, 13, 5, 12);

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
