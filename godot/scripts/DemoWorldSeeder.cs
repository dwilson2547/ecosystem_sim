using EcosystemSim;

namespace EcosystemGame;

/// <summary>
/// Creates the demo world used by the Godot frontend.
/// Mirrors SimConsole/WorldSeeder.cs — both will be retired once map generation is live.
/// </summary>
public static class DemoWorldSeeder
{
    public static readonly Disease DinoFever = new()
    {
        Name = "Dino Fever",
        MortalityRate = 0.04f,
        SpreadRate    = 0.18f,
        RecoveryRate  = 0.015f
    };

    public static World Create()
    {
        var triceratops = new SpeciesDefinition
        {
            Name             = "Triceratops",
            RootName         = "Triceratops",
            ConsumptionRates = { [ResourceType.Food] = 2f, [ResourceType.Water] = 1f },
            ByproductRates   = { [ByproductType.Fertilizer] = 0.08f },
            ReproductionRate = 0.015f,
            StarvationRate   = 0.015f,
            MigrationThreshold = 0.75f,
            WarAggression    = 0.3f,
            CombatStrength   = 1.4f,
            Immunity         = 0.3f,
        };

        var brachiosaurus = new SpeciesDefinition
        {
            Name             = "Brachiosaurus",
            RootName         = "Brachiosaurus",
            ConsumptionRates = { [ResourceType.Food] = 5f, [ResourceType.Water] = 2f },
            ByproductRates   = { [ByproductType.Fertilizer] = 0.20f },
            ReproductionRate = 0.008f,
            StarvationRate   = 0.008f,
            MigrationThreshold = 0.6f,
            WarAggression    = 0.1f,
            CombatStrength   = 0.6f,
            Immunity         = 0.15f,
        };

        var pachycephalosaurus = new SpeciesDefinition
        {
            Name             = "Pachycephalosaurus",
            RootName         = "Pachycephalosaurus",
            ConsumptionRates = { [ResourceType.Food] = 1f },
            ByproductRates   = { [ByproductType.Fertilizer] = 0.06f },
            ReproductionRate = 0.02f,
            StarvationRate   = 0.015f,
            MigrationThreshold = 0.5f,
            WarAggression    = 0.5f,
            CombatStrength   = 0.9f,
            Immunity         = 0.55f,
        };

        var world = new World();
        var map   = world.State.Map;

        // terrain layout — rows y=0 (north) to y=9 (south), cols x=0 (west) to x=9 (east)
        // H=Highland  F=Forest  R=River  S=Swamp  D=Desert  P=Plains
        var terrainRows = new[]
        {
            "HHHPPPDDDD", // y=0
            "HHPPPDDDDD", // y=1  Highland Tric at (1,1)
            "HFFFPPPDDD", // y=2  Valley   Tric at (3,2)
            "PFRRRPPPDD", // y=3
            "PFRRRRPPPD", // y=4  River Brachio at (5,4)
            "PPRRRRRPPP", // y=5
            "PSSRRRPPFP", // y=6  Midland  Pachy at (7,6)
            "PSSSPPPFFP", // y=7
            "DSPPPPFFFD", // y=8  Eastern  Pachy at (8,8)
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
        for (var x = 0; x < map.Width;  x++)
        {
            var terrain = charToTerrain[terrainRows[y][x]];
            var tile    = map.GetTile(x, y);
            tile.Terrain = terrain;

            var (fr, fc) = foodRegen[terrain];
            tile.Resources.Add(new ResourcePool
            {
                Type = ResourceType.Food, Amount = fr * 10f,
                Capacity = fc, RegenPerTick = fr,
            });

            if (waterByTerrain.TryGetValue(terrain, out var water))
                tile.Resources.Add(new ResourcePool
                {
                    Type = ResourceType.Water, Amount = water.regen * 10f,
                    Capacity = water.capacity, RegenPerTick = water.regen,
                });
        }

        var highlandTric = new Faction { Name = "Highland Tric",  PrimarySpecies = triceratops };
        var valleyTric   = new Faction { Name = "Valley Tric",    PrimarySpecies = triceratops };
        var riverBrachio = new Faction { Name = "River Brachio",  PrimarySpecies = brachiosaurus };
        var easternPachy = new Faction { Name = "Eastern Pachy",  PrimarySpecies = pachycephalosaurus };
        var midlandPachy = new Faction { Name = "Midland Pachy",  PrimarySpecies = pachycephalosaurus };

        world.State.Factions.AddRange([highlandTric, valleyTric, riverBrachio, easternPachy, midlandPachy]);

        void Place(Faction faction, int x, int y, int count)
        {
            var pop = new Population { Species = faction.PrimarySpecies, Count = count };
            faction.AddPopulation(pop);
            map.GetTile(x, y).AddPopulation(pop);
        }

        Place(highlandTric, 1, 1, 50);
        Place(valleyTric,   3, 2, 40);
        Place(riverBrachio, 5, 4, 25);
        Place(easternPachy, 8, 8, 80);
        Place(midlandPachy, 7, 6, 60);

        return world;
    }
}
