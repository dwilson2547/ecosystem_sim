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
            FoodConsumptionRate  = 2f,
            WaterConsumptionRate = 0.5f,
            // ease of eating (readme): ground 5/5, brush 3/5, canopy 1/5 — a low-slung grazer
            EaseOfEating = { [ResourceType.Ground] = 5f, [ResourceType.Brush] = 3f, [ResourceType.Canopy] = 1f },
            ByproductRates   = { [ByproductType.Fertilizer] = 0.08f },
            ReproductionRate = 0.015f,
            StarvationRate   = 0.015f,
            MigrationThreshold = 0.75f,
            WarAggression = 0.3f,
            CombatStrength = 1.4f,
            Immunity = 0.3f
        };

        var alamosaurus = new SpeciesDefinition
        {
            Name = "Alamosaurus",
            RootName = "Alamosaurus",
            FoodConsumptionRate  = 5f,
            WaterConsumptionRate = 1f,
            // ease of eating (readme): ground 0/5, brush 2/5, canopy 5/5 — a treetop browser
            EaseOfEating = { [ResourceType.Ground] = 0f, [ResourceType.Brush] = 2f, [ResourceType.Canopy] = 5f },
            ByproductRates   = { [ByproductType.Fertilizer] = 0.20f },
            ReproductionRate = 0.008f,
            StarvationRate   = 0.008f,
            MigrationThreshold = 0.6f,
            WarAggression = 0.1f,
            CombatStrength = 0.6f,
            Immunity = 0.15f
        };

        var parasaurolophus = new SpeciesDefinition
        {
            Name = "Parasaurolophus",
            RootName = "Parasaurolophus",
            FoodConsumptionRate  = 1f,
            WaterConsumptionRate = 0f,
            // ease of eating (readme): ground 3/5, brush 5/5, canopy 2/5 — a mid-height browser
            EaseOfEating = { [ResourceType.Ground] = 3f, [ResourceType.Brush] = 5f, [ResourceType.Canopy] = 2f },
            ByproductRates   = { [ByproductType.Fertilizer] = 0.06f },
            ReproductionRate = 0.02f,
            StarvationRate   = 0.015f,
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
            "PFRRRRPPPD", // y=4  — River Alamo starts at (5,4): River
            "PPRRRRRPPP", // y=5
            "PSSRRRPPFP", // y=6  — Midland  Para starts at (7,6): Plains
            "PSSSPPPFFP", // y=7
            "DSPPPPFFFD", // y=8  — Eastern  Para starts at (8,8): Forest
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

        var rng = new Random();

        for (var y = 0; y < map.Height; y++)
        {
            for (var x = 0; x < map.Width; x++)
            {
                var terrain = charToTerrain[terrainRows[y][x]];
                var tile    = map.GetTile(x, y);
                tile.Terrain = terrain;
                tile.Resources.AddRange(TerrainStats.BuildResourcePools(terrain, rng));
            }
        }

        var highlandTric = new Faction { Name = "Highland Tric", PrimarySpecies = triceratops };
        var valleyTric   = new Faction { Name = "Valley Tric",   PrimarySpecies = triceratops };
        var riverAlamo   = new Faction { Name = "River Alamo",   PrimarySpecies = alamosaurus };
        var easternPara  = new Faction { Name = "Eastern Para",  PrimarySpecies = parasaurolophus };
        var midlandPara  = new Faction { Name = "Midland Para",  PrimarySpecies = parasaurolophus };

        world.State.Factions.AddRange([highlandTric, valleyTric, riverAlamo, easternPara, midlandPara]);

        void Place(Faction faction, int x, int y, int count)
        {
            var pop = new Population { Species = faction.PrimarySpecies, Count = count };
            faction.AddPopulation(pop);
            map.GetTile(x, y).AddPopulation(pop);
        }

        // Triceratops: start in Highland/Forest — no water nearby, must migrate south toward River/Swamp band
        Place(highlandTric, 1, 1, 50);
        Place(valleyTric,   3, 2, 40);

        // Alamosaurus: River — water-rich, high food demand
        Place(riverAlamo, 5, 4, 25);

        // Parasaurolophus: south — food only, will compete and migrate freely
        Place(easternPara, 8, 8, 80);
        Place(midlandPara, 7, 6, 60);

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
