using EcosystemSim;

namespace SimConsole;

public static class WorldSeeder
{
    public static World CreateDemo()
    {
        // ── land species ──────────────────────────────────────────────────────

        var triceratops = new SpeciesDefinition
        {
            Name = "Triceratops",
            RootName = "Triceratops",
            ViewRadius = 2,
            FoodConsumptionRate  = 2f,
            WaterConsumptionRate = 0.5f,
            // low-slung grazer: grazes easily, browses adequately, can't reach canopy fruit
            EaseOfEating = { [FoodSubtype.Graze] = 5f, [FoodSubtype.Browse] = 3f, [FoodSubtype.Fruit] = 1f },
            AsPreyCategory   = PreyCategory.LargeHerbivore,   // armoured; T-Rex takes it only when hadrosaurs are scarce
            ByproductRates   = { [ByproductType.Fertilizer] = 0.08f },
            ReproductionRate = 0.015f,
            StarvationRate   = 0.015f,
            MigrationThreshold = 0.75f,
            WarAggression    = 0.3f,
            CombatStrength   = 1.4f,
            Immunity         = 0.3f,
        };

        var alamosaurus = new SpeciesDefinition
        {
            Name = "Alamosaurus",
            RootName = "Alamosaurus",
            ViewRadius = 3,
            FoodConsumptionRate  = 5f,
            WaterConsumptionRate = 1f,
            // treetop browser: fruit and upper browse only, can't graze
            EaseOfEating = { [FoodSubtype.Fruit] = 5f, [FoodSubtype.Browse] = 2f },
            ByproductRates   = { [ByproductType.Fertilizer] = 0.20f },
            ReproductionRate = 0.008f,
            StarvationRate   = 0.008f,
            MigrationThreshold = 0.6f,
            WarAggression    = 0.1f,
            CombatStrength   = 0.6f,
            Immunity         = 0.15f,
        };

        var parasaurolophus = new SpeciesDefinition
        {
            Name = "Parasaurolophus",
            RootName = "Parasaurolophus",
            FoodConsumptionRate  = 1f,
            WaterConsumptionRate = 0f,
            // mid-height browser: browse specialist, decent grazer, some fruit
            EaseOfEating = { [FoodSubtype.Browse] = 5f, [FoodSubtype.Graze] = 3f, [FoodSubtype.Fruit] = 2f },
            AsPreyCategory   = PreyCategory.SmallHerbivore,   // the hadrosaur swarm — T-Rex's staple
            ByproductRates   = { [ByproductType.Fertilizer] = 0.06f },
            ReproductionRate = 0.014f,   // curbed from 0.02 — unchecked it carpeted the map and razed forests
            StarvationRate   = 0.015f,
            MigrationThreshold = 0.5f,
            MaxCount         = 45,       // per-tile density cap; predation does the real population control
            WarAggression    = 0.5f,
            CombatStrength   = 0.9f,
            Immunity         = 0.55f,
        };

        // land apex predator — checks the herbivores (esp. Parasaurolophus) so forests aren't razed
        var tyrannosaurus = new SpeciesDefinition
        {
            Name = "Tyrannosaurus",
            RootName = "Tyrannosaurus",
            ViewRadius = 2,
            // obligate carnivore: no plant food, lives purely on prey — reads sat 0 on a preyless
            // tile and migrates toward the nearest herd. Parasaurolophus preferred, Triceratops accepted.
            FoodConsumptionRate  = 0f,
            WaterConsumptionRate = 0f,
            PreyConsumptionRate  = 0.6f,
            PreferredPrey = [PreyCategory.SmallHerbivore],
            AcceptedPrey  = [PreyCategory.LargeHerbivore],
            ByproductRates   = {},
            ReproductionRate = 0.015f,
            StarvationRate   = 0.012f,   // persists between hunts rather than crashing when a tile is cleared
            MigrationThreshold = 0.5f,
            MigrationCooldownTicks = 2,
            WarAggression    = 0.4f,
            CombatStrength   = 4.0f,
            Immunity         = 0.4f,
        };

        // ── marine species ────────────────────────────────────────────────────

        var mosasaurus = new SpeciesDefinition
        {
            Name = "Mosasaurus",
            RootName = "Mosasaurus",
            FoodConsumptionRate  = 2f,
            WaterConsumptionRate = 0f,
            // ambush hunter: fish and shrimp in shallow water
            EaseOfEating = { [FoodSubtype.Fish] = 4f, [FoodSubtype.Shrimp] = 3f, [FoodSubtype.Crustacean] = 2f },
            AsPreyCategory   = PreyCategory.SmallMarine,
            ByproductRates   = {},
            ReproductionRate = 0.015f,
            StarvationRate   = 0.015f,
            MigrationThreshold = 0.6f,
            WarAggression    = 0.2f,
            CombatStrength   = 1.0f,
            Immunity         = 0.3f,
        };

        var plesiosaur = new SpeciesDefinition
        {
            Name = "Plesiosaur",
            RootName = "Plesiosaur",
            ViewRadius = 2,
            FoodConsumptionRate  = 3f,
            WaterConsumptionRate = 0f,
            // open-water fisher: fast pursuit predator, eats squid in deeper water
            EaseOfEating = { [FoodSubtype.Fish] = 5f, [FoodSubtype.Squid] = 3f },
            AsPreyCategory     = PreyCategory.LargeMarine,
            ByproductRates     = {},
            ReproductionRate   = 0.010f,
            StarvationRate     = 0.012f,
            MigrationThreshold = 0.55f,
            MigrationCooldownTicks = 4,
            WarAggression    = 0.1f,
            CombatStrength   = 0.8f,
            Immunity         = 0.25f,
        };

        var kronosaurus = new SpeciesDefinition
        {
            Name = "Kronosaurus",
            RootName = "Kronosaurus",
            ViewRadius = 2,
            WaterConsumptionRate = 0f,
            // apex pliosaur: plesiosaurs preferred, mosasaurs accepted;
            // subsists on raw fish/squid at low ease (partial satisfaction) when prey is absent
            FoodConsumptionRate  = 0.5f,
            EaseOfEating = { [FoodSubtype.Fish] = 1f, [FoodSubtype.Squid] = 1f },
            // prey eaten per predator per tick — whole individuals, so a small fraction (~1 kill / 7 ticks)
            PreyConsumptionRate = 0.15f,
            PreferredPrey = [PreyCategory.LargeMarine],
            AcceptedPrey  = [PreyCategory.SmallMarine],
            ByproductRates   = {},
            ReproductionRate = 0.005f,
            StarvationRate   = 0.010f,
            MigrationThreshold = 0.5f,
            WarAggression    = 0.15f,
            CombatStrength   = 2.5f,
            Immunity         = 0.4f,
        };

        var megalodon = new SpeciesDefinition
        {
            Name = "Megalodon",
            RootName = "Megalodon",
            ViewRadius = 2,
            WaterConsumptionRate = 0f,
            // singleton apex predator: eats all marine prey; survives on fish/squid/whale between hunts
            FoodConsumptionRate  = 1f,
            EaseOfEating = { [FoodSubtype.Fish] = 2f, [FoodSubtype.Squid] = 2f, [FoodSubtype.Whale] = 3f },
            PreyConsumptionRate  = 3f,
            PreferredPrey        = [PreyCategory.LargeMarine],
            AcceptedPrey         = [PreyCategory.SmallMarine],
            ByproductRates       = {},
            ReproductionRate     = 0f,
            StarvationRate       = 0.001f,
            MigrationThreshold   = 0.3f,
            MigrationCooldownTicks = 2,
            WarAggression        = 0f,
            CombatStrength       = 5.0f,
            Immunity             = 0.95f,
            MaxCount             = 1,
            AllowedTerrains      = [TerrainType.DeepOcean],
        };

        // ── world + terrain ───────────────────────────────────────────────────

        var world = new World(16, 16);
        var map   = world.State.Map;

        // terrain layout — y=0 (north) to y=15 (south), x=0 (west) to x=15 (east)
        // H=Highland  F=Forest  R=River  S=Swamp  D=Desert  P=Plains
        // A=ShallowOcean  B=DeepOcean
        // Large northern forest biome (x=2-6, y=0-4) gives the Alamosaurus herd room to disperse.
        var terrainRows = new[]
        {
            "HHFFFFFPDDAABBBB", // y=0   northern forest ─┐
            "HHFFFFFPDDAABBBB", // y=1   Highland Tric at (1,1)
            "HPFFFFFPDDAABBBB", // y=2   Forest Alamo at (4,2)
            "PPFFFFPPDDAABBBB", // y=3   Valley Tric at (7,3); Mosasaurus at (10,3)
            "PPPFFRPPPDAABBBB", // y=4   northern forest ─┘  Megalodon at (14,4)
            "PPPRRRPPPDAABBBB", // y=5   Kronosaurus at (13,5)
            "PSSRRRPPPPAABBBB", // y=6   Midland Para at (7,6); Plesiosaur at (12,6) DeepOcean
            "PSSSRPPPPPAABBBB", // y=7
            "DSSPPPPFFPAABBBB", // y=8
            "DDPPPPFFFDAABBBB", // y=9   Eastern Para at (7,9)
            "DDPPPPFFPDAABBBB", // y=10  southern forest
            "PPPPPPPPPPAABBBB", // y=11
            "PPSSPPPPPPAABBBB", // y=12
            "PPSSPPPPPPAABBBB", // y=13
            "DPPPPPPPPDAABBBB", // y=14
            "DDPPPPPPDDAABBBB", // y=15
        };

        var charToTerrain = new Dictionary<char, TerrainType>
        {
            ['H'] = TerrainType.Highland,
            ['F'] = TerrainType.Forest,
            ['R'] = TerrainType.River,
            ['S'] = TerrainType.Swamp,
            ['D'] = TerrainType.Desert,
            ['P'] = TerrainType.Plains,
            ['A'] = TerrainType.ShallowOcean,
            ['B'] = TerrainType.DeepOcean,
        };

        var rng = new Random();

        for (var y = 0; y < map.Height; y++)
        for (var x = 0; x < map.Width;  x++)
        {
            var terrain = charToTerrain[terrainRows[y][x]];
            var tile    = map.GetTile(x, y);
            tile.Terrain = terrain;
            tile.Resources.AddRange(TerrainStats.BuildResourcePools(terrain, rng));
        }

        // ── factions ──────────────────────────────────────────────────────────

        var highlandTric   = new Faction { Name = "Highland Tric",    PrimarySpecies = triceratops };
        var valleyTric     = new Faction { Name = "Valley Tric",      PrimarySpecies = triceratops };
        var forestAlamo     = new Faction { Name = "Forest Alamo",      PrimarySpecies = alamosaurus };
        var easternPara    = new Faction { Name = "Eastern Para",     PrimarySpecies = parasaurolophus };
        var midlandPara    = new Faction { Name = "Midland Para",     PrimarySpecies = parasaurolophus };
        var tyrantPack     = new Faction { Name = "Tyrant Pack",      PrimarySpecies = tyrannosaurus };
        var mosaPack       = new Faction { Name = "Mosasaurus Pack",  PrimarySpecies = mosasaurus };
        var plesioDrift    = new Faction { Name = "Plesiosaur Drift", PrimarySpecies = plesiosaur };
        var kronosPod      = new Faction { Name = "Kronosaurus Pod",  PrimarySpecies = kronosaurus };
        var theMegalodon   = new Faction { Name = "The Megalodon",    PrimarySpecies = megalodon };

        world.State.Factions.AddRange([highlandTric, valleyTric, forestAlamo, easternPara, midlandPara,
                                       tyrantPack, mosaPack, plesioDrift, kronosPod, theMegalodon]);

        void Place(Faction faction, int x, int y, int count)
        {
            var pop = new Population { Species = faction.PrimarySpecies, Count = count };
            faction.AddPopulation(pop);
            map.GetTile(x, y).AddPopulation(pop);
        }

        // land
        Place(highlandTric, 1,  1, 50);
        Place(valleyTric,   7,  3, 40);
        Place(forestAlamo,  4,  2, 25);
        Place(easternPara,  7,  9, 80);
        Place(midlandPara,  7,  6, 60);
        Place(tyrantPack,   6,  8,  5);   // central plains, amid the herbivore range

        // marine: shallow zone (x=10-11) and deep zone (x=12-15)
        Place(mosaPack,    10,  3, 30);
        Place(plesioDrift, 12,  6, 20);   // DeepOcean home; forays to adjacent ShallowOcean for fish
        Place(kronosPod,   13,  5,  8);
        Place(theMegalodon, 14,  4,  1);

        return world;
    }

    public static readonly Disease DinoFever = new()
    {
        Name = "Dino Fever",
        MortalityRate = 0.04f,
        SpreadRate    = 0.18f,
        RecoveryRate  = 0.015f
    };
}
