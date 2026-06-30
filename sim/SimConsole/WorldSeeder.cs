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
            ByproductRates   = { [ByproductType.Fertilizer] = 0.20f },
            ReproductionRate = 0.03f,
            StarvationRate = 0.03f,
            MigrationThreshold = 0.6f,
            WarAggression = 0.1f,
            CombatStrength = 0.6f,
            Immunity = 0.15f
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
            Immunity = 0.55f
        };

        var world = new World();
        var map   = world.State.Map;

        // terrain layout — rows are y=0 (north) to y=9 (south), columns x=0 (west) to x=9 (east)
        // H=Highland  F=Forest  R=River  S=Swamp  D=Desert  P=Plains
        var terrainRows = new[]
        {
            "HHHPPPDDDD", // y=0
            "HHPPPDDDDD", // y=1  — Highland Tric starts at (1,1): Highland
            "HFFFPPPDDD", // y=2  — Valley   Tric starts at (3,2): Forest
            "PFRRRPPPDD", // y=3
            "PFRRRRPPPD", // y=4  — River Brachio starts at (5,4): River
            "PPRRRRRPPP", // y=5
            "PSSRRRPPFP", // y=6  — Midland  Pachy starts at (7,6): Plains
            "PSSSPPPFFP", // y=7
            "DSPPPPFFFD", // y=8  — Eastern  Pachy starts at (8,8): Forest
            "DDDPPPPPDD", // y=9
        };

        var charToTerrain = new Dictionary<char, TerrainType>
        {
            ['H'] = TerrainType.Highland,
            ['F'] = TerrainType.Forest,
            ['R'] = TerrainType.River,
            ['S'] = TerrainType.Swamp,
            ['D'] = TerrainType.Desert,
            ['P'] = TerrainType.Plains,
        };

        // food regen per tick by terrain; water only present on River and Swamp tiles
        var foodRegen = new Dictionary<TerrainType, (float regen, float capacity)>
        {
            [TerrainType.Plains]   = (10f, 200f),
            [TerrainType.Forest]   = (15f, 300f),
            [TerrainType.Swamp]    = ( 7f, 140f),
            [TerrainType.Desert]   = ( 3f,  60f),
            [TerrainType.Highland] = ( 8f, 160f),
            [TerrainType.River]    = (12f, 240f),
        };

        var waterByTerrain = new Dictionary<TerrainType, (float regen, float capacity)>
        {
            [TerrainType.River] = (15f, 200f),
            [TerrainType.Swamp] = ( 8f, 120f),
        };

        for (var y = 0; y < map.Height; y++)
        {
            for (var x = 0; x < map.Width; x++)
            {
                var terrain = charToTerrain[terrainRows[y][x]];
                var tile    = map.GetTile(x, y);
                tile.Terrain = terrain;

                var (fr, fc) = foodRegen[terrain];
                tile.Resources.Add(new ResourcePool
                {
                    Type = ResourceType.Food,
                    Amount = fr * 10f,
                    Capacity = fc,
                    RegenPerTick = fr,
                });

                if (waterByTerrain.TryGetValue(terrain, out var water))
                {
                    tile.Resources.Add(new ResourcePool
                    {
                        Type = ResourceType.Water,
                        Amount = water.regen * 10f,
                        Capacity = water.capacity,
                        RegenPerTick = water.regen,
                    });
                }
            }
        }

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

        // Triceratops: start in Highland/Forest — no water nearby, must migrate south toward River/Swamp band
        Place(highlandTric, 1, 1, 50);
        Place(valleyTric,   3, 2, 40);

        // Brachiosaurus: River — water-rich, high food demand
        Place(riverBrachio, 5, 4, 25);

        // Pachycephalosaurus: south — food only, will compete and migrate freely
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
