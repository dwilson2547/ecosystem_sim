using EcosystemSim;

namespace EcosystemGame;

// Creates the demo world used by the Godot frontend.
// Mirrors SimConsole/WorldSeeder.cs — both will be retired once map generation is live.
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
        // ── land species ──────────────────────────────────────────────────────

        var triceratops = new SpeciesDefinition
        {
            Name             = "Triceratops",
            RootName         = "Triceratops",
            ViewRadius       = 2,
            FoodConsumptionRate  = 2f,
            WaterConsumptionRate = 0.5f,
            // low-slung grazer: grazes easily, browses adequately, can't reach canopy fruit
            EaseOfEating     = { [FoodSubtype.Graze] = 5f, [FoodSubtype.Browse] = 3f, [FoodSubtype.Fruit] = 1f },
            AsPreyCategory   = PreyCategory.LargeHerbivore,   // armoured; T-Rex takes it only when hadrosaurs are scarce
            HuntDifficulty   = 0.35f,    // armoured and horned — harder to bring down than a hadrosaur
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
            Name             = "Alamosaurus",
            RootName         = "Alamosaurus",
            ViewRadius       = 3,
            FoodConsumptionRate  = 5f,
            WaterConsumptionRate = 1f,
            // treetop browser: fruit and upper browse only, can't graze
            EaseOfEating     = { [FoodSubtype.Fruit] = 5f, [FoodSubtype.Browse] = 2f },
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
            Name             = "Parasaurolophus",
            RootName         = "Parasaurolophus",
            FoodConsumptionRate  = 1f,
            WaterConsumptionRate = 0f,
            // mid-height browser: browse specialist, decent grazer, some fruit
            EaseOfEating     = { [FoodSubtype.Browse] = 5f, [FoodSubtype.Graze] = 3f, [FoodSubtype.Fruit] = 2f },
            AsPreyCategory   = PreyCategory.SmallHerbivore,   // the hadrosaur swarm — T-Rex's staple
            HerdDefense      = 0.8f,   // safety in numbers: a massed hadrosaur herd deters the T-Rex,
                                       // so it can't grind a big herd into the vulnerable-tail death spiral
            HuntDifficulty   = 0.25f,  // an agile hadrosaur — the T-Rex misses about a quarter of hunts
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
            Name             = "Tyrannosaurus",
            RootName         = "Tyrannosaurus",
            ViewRadius       = 2,
            // obligate carnivore: no plant food, lives purely on prey — reads sat 0 on a preyless
            // tile and migrates toward the nearest herd. Parasaurolophus preferred, Triceratops accepted.
            FoodConsumptionRate  = 0f,
            WaterConsumptionRate = 0f,
            PreyConsumptionRate  = 0.8f,   // raised from 0.6 to offset Parasaurolophus hunt difficulty (catchability 0.75)
            PreferredPrey    = [PreyCategory.SmallHerbivore],
            AcceptedPrey     = [PreyCategory.LargeHerbivore],
            ByproductRates   = {},
            ReproductionRate = 0.015f,
            StarvationRate   = 0.012f,   // persists between hunts rather than crashing when a tile is cleared
            MigrationThreshold     = 0.5f,
            MigrationCooldownTicks = 2,
            WarAggression    = 0.4f,
            CombatStrength   = 4.0f,
            Immunity         = 0.4f,
        };

        // ── insects ─────────────────────────────────────────────────────────
        // A self-contained insect food web: locusts graze the plains and boom fast; Meganeura (giant
        // dragonfly) is the insectivore that checks them. Locusts compete with the big grazers for
        // Graze, so a swarm pulls food away from the hadrosaurs until the dragonflies (or a food crash)
        // knock them back — a new boom-bust layer that doesn't lean on the dinosaur balance.
        var locust = new SpeciesDefinition
        {
            Name             = "Locust",
            RootName         = "Locust",
            FoodConsumptionRate  = 0.2f,    // tiny appetite each, but they swarm
            WaterConsumptionRate = 0f,
            EaseOfEating     = { [FoodSubtype.Graze] = 5f, [FoodSubtype.Browse] = 2f },
            AsPreyCategory   = PreyCategory.Insect,
            HerdDefense      = 0.4f,        // a dense swarm can't be fully consumed — a floor so it never wipes
            HuntDifficulty   = 0.25f,
            ByproductRates   = {},
            ReproductionRate = 0.028f,      // r-selected; ceiling set by grass (food), MaxCount, and dragonflies
            StarvationRate   = 0.06f,       // crashes fast when the grass is stripped — boom-bust plagues
            MigrationThreshold = 0.5f,
            MaxCount         = 45,          // per-tile swarm cap — keeps a plague moderate
            WarAggression    = 0f,
            CombatStrength   = 0.1f,
            Immunity         = 0.2f,
        };

        var meganeura = new SpeciesDefinition
        {
            Name             = "Meganeura",
            RootName         = "Meganeura",
            ViewRadius       = 3,           // ranges widely for swarms
            // obligate insectivore — a small, capped population that lives off the locust swarm's margin
            // rather than controlling it (locusts are food-limited); MaxCount stops it over-cropping.
            FoodConsumptionRate  = 0f,
            WaterConsumptionRate = 0f,
            PreyConsumptionRate  = 0.5f,
            PreferredPrey    = [PreyCategory.Insect],
            ByproductRates   = {},
            ReproductionRate = 0.02f,
            StarvationRate   = 0.01f,       // sips through lean spells rather than crashing
            MigrationThreshold     = 0.5f,
            MigrationCooldownTicks = 1,
            MaxCount         = 18,          // per-tile cap — crops the swarm but can't wipe it (locust herd defense)
            WarAggression    = 0f,
            CombatStrength   = 0.3f,
            Immunity         = 0.25f,
        };

        // pollinator — the mutualist half of the insect guild. Bees sip nectar (feed on Fruit) and in
        // return lift Fruit regen where they settle, so a pollinated forest yields more fruit — which
        // feeds the bees AND the fruit-eaters (Alamosaurus, Parasaurolophus). Food-limited on Fruit.
        var bee = new SpeciesDefinition
        {
            Name             = "Bee",
            RootName         = "Bee",
            FoodConsumptionRate  = 0.03f,   // sips nectar — a light draw on Fruit
            WaterConsumptionRate = 0f,
            EaseOfEating     = { [FoodSubtype.Fruit] = 5f },
            PollinationBoost = 0.7f,        // up to +70% Fruit regen where a colony works the tile
            ByproductRates   = {},
            ReproductionRate = 0.02f,
            StarvationRate   = 0.04f,
            MigrationThreshold = 0.5f,
            MaxCount         = 20,          // per-tile cap — a colony works the tile without stripping it
            WarAggression    = 0f,
            CombatStrength   = 0.1f,
            Immunity         = 0.2f,
        };

        // ── marine species ────────────────────────────────────────────────────

        var mosasaurus = new SpeciesDefinition
        {
            Name             = "Mosasaurus",
            RootName         = "Mosasaurus",
            FoodConsumptionRate  = 2f,
            WaterConsumptionRate = 0f,
            // ambush hunter: fish and shrimp in shallow water
            EaseOfEating     = { [FoodSubtype.Fish] = 4f, [FoodSubtype.Shrimp] = 3f, [FoodSubtype.Crustacean] = 2f },
            AsPreyCategory   = PreyCategory.SmallMarine,
            HuntDifficulty   = 0.2f,
            ByproductRates   = {},
            ReproductionRate = 0.015f,
            StarvationRate   = 0.015f,
            MigrationThreshold = 0.6f,
            WarAggression    = 0.2f,
            CombatStrength   = 1.0f,
            Immunity         = 0.3f,
            MaxCount         = 40,       // per-tile safety ceiling; Xiphactinus does the real cropping.
                                         // Kronosaurus/Megalodon only *accept* Mosasaurus and stay in deep
                                         // water on Plesiosaur, so the shallow strip was a predator-free
                                         // nursery and Mosasaurus carpeted the map (30→~1000 uncropped).
        };

        var plesiosaur = new SpeciesDefinition
        {
            Name             = "Plesiosaur",
            RootName         = "Plesiosaur",
            ViewRadius       = 2,
            FoodConsumptionRate  = 3f,
            WaterConsumptionRate = 0f,
            // open-water fisher: fast pursuit predator, eats squid in deeper water
            EaseOfEating     = { [FoodSubtype.Fish] = 5f, [FoodSubtype.Squid] = 3f },
            AsPreyCategory   = PreyCategory.LargeMarine,
            HuntDifficulty   = 0.2f,
            ByproductRates   = {},
            ReproductionRate = 0.010f,
            StarvationRate   = 0.012f,
            MigrationThreshold     = 0.55f,
            MigrationCooldownTicks = 4,
            WarAggression    = 0.1f,
            CombatStrength   = 0.8f,
            Immunity         = 0.25f,
        };

        var kronosaurus = new SpeciesDefinition
        {
            Name             = "Kronosaurus",
            RootName         = "Kronosaurus",
            ViewRadius       = 2,
            WaterConsumptionRate = 0f,
            // apex pliosaur: plesiosaurs preferred, mosasaurs accepted;
            // subsists on raw fish/squid at low ease (partial satisfaction) when prey is absent
            FoodConsumptionRate  = 0.5f,
            EaseOfEating     = { [FoodSubtype.Fish] = 1f, [FoodSubtype.Squid] = 1f },
            // prey eaten per predator per tick — whole individuals, so a small fraction (~1 kill / 7 ticks)
            PreyConsumptionRate = 0.19f,  // raised from 0.15 to offset Plesiosaur hunt difficulty
            PreferredPrey    = [PreyCategory.LargeMarine],
            AcceptedPrey     = [PreyCategory.SmallMarine],
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
            Name             = "Megalodon",
            RootName         = "Megalodon",
            ViewRadius       = 2,
            WaterConsumptionRate = 0f,
            // singleton apex predator: eats all marine prey; survives on fish/squid/whale between hunts
            FoodConsumptionRate  = 1f,
            EaseOfEating     = { [FoodSubtype.Fish] = 2f, [FoodSubtype.Squid] = 2f, [FoodSubtype.Whale] = 3f },
            PreyConsumptionRate  = 3.75f,  // raised to offset marine prey hunt difficulty
            PreferredPrey    = [PreyCategory.LargeMarine],
            AcceptedPrey     = [PreyCategory.SmallMarine],
            ByproductRates   = {},
            ReproductionRate = 0f,
            StarvationRate   = 0.001f,
            MigrationThreshold     = 0.3f,
            MigrationCooldownTicks = 2,
            WarAggression    = 0f,
            CombatStrength   = 5.0f,
            Immunity         = 0.95f,
            MaxCount         = 1,
            AllowedTerrains  = [TerrainType.DeepOcean],
        };

        // dedicated shallow-water hunter — closes the Mosasaurus refuge. Pinned to ShallowOcean (where
        // Mosasaurus breeds), it *prefers* SmallMarine so it actively crops the hadrosaur-of-the-sea the
        // deep-water apexes never reach. A dual-consumption predator that subsists on shallow fish/shrimp
        // between hunts: the fish floor is deliberate — it stops the shoal crashing when it thins a tile
        // (a pure obligate version over-cropped Mosasaurus to extinction by t33, then starved out). The
        // fish subsidy alone let it plateau at ~135 head (an inverted pyramid), so a per-tile MaxCount
        // keeps the shoal below its prey. Left with no AsPreyCategory — a second shallow apex, not itself
        // prey; tagging it SmallMarine would make it hunt its own kind, and feeding it to the Megalodon
        // for trophic depth needs a new meso prey tier.
        var xiphactinus = new SpeciesDefinition
        {
            Name             = "Xiphactinus",
            RootName         = "Xiphactinus",
            ViewRadius       = 2,
            FoodConsumptionRate  = 1f,
            WaterConsumptionRate = 0f,
            EaseOfEating     = { [FoodSubtype.Shrimp] = 4f, [FoodSubtype.Fish] = 3f, [FoodSubtype.Crustacean] = 2f },
            PreyConsumptionRate  = 0.38f,  // raised from 0.30 to offset Mosasaurus hunt difficulty
            PreferredPrey    = [PreyCategory.SmallMarine],
            ByproductRates   = {},
            ReproductionRate = 0.008f,
            StarvationRate   = 0.012f,
            MigrationThreshold     = 0.5f,
            MigrationCooldownTicks = 2,
            WarAggression    = 0.15f,
            CombatStrength   = 1.8f,
            Immunity         = 0.3f,
            MaxCount         = 15,       // per-tile cap — keep the shoal below its Mosasaurus prey
            AllowedTerrains  = [TerrainType.ShallowOcean],
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

        var highlandTric = new Faction { Name = "Highland Tric",    PrimarySpecies = triceratops };
        var valleyTric   = new Faction { Name = "Valley Tric",      PrimarySpecies = triceratops };
        var forestAlamo   = new Faction { Name = "Forest Alamo",      PrimarySpecies = alamosaurus };
        var easternPara  = new Faction { Name = "Eastern Para",     PrimarySpecies = parasaurolophus };
        var midlandPara  = new Faction { Name = "Midland Para",     PrimarySpecies = parasaurolophus };
        var tyrantPack   = new Faction { Name = "Tyrant Pack",      PrimarySpecies = tyrannosaurus };
        var mosaPack     = new Faction { Name = "Mosasaurus Pack",  PrimarySpecies = mosasaurus };
        var plesioDrift  = new Faction { Name = "Plesiosaur Drift", PrimarySpecies = plesiosaur };
        var kronosPod    = new Faction { Name = "Kronosaurus Pod",  PrimarySpecies = kronosaurus };
        var theMegalodon = new Faction { Name = "The Megalodon",    PrimarySpecies = megalodon };
        var xiphShoal    = new Faction { Name = "Xiphactinus Shoal", PrimarySpecies = xiphactinus };
        var locustSwarm  = new Faction { Name = "Locust Swarm",     PrimarySpecies = locust };
        var dragonflies  = new Faction { Name = "Dragonflies",      PrimarySpecies = meganeura };
        var beeColony    = new Faction { Name = "Bee Colony",       PrimarySpecies = bee };

        world.State.Factions.AddRange([highlandTric, valleyTric, forestAlamo, easternPara, midlandPara,
                                       tyrantPack, mosaPack, plesioDrift, kronosPod, theMegalodon, xiphShoal,
                                       locustSwarm, dragonflies, beeColony]);

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

        // insects — southern plains
        Place(locustSwarm,  3, 12, 120);
        Place(dragonflies,  4, 12, 10);
        Place(beeColony,    4,  2, 15);   // northern forest — nectar among the fruit

        // marine
        Place(mosaPack,    10,  3, 30);
        Place(plesioDrift, 12,  6, 20);   // DeepOcean home; forays to adjacent ShallowOcean for fish
        Place(kronosPod,   13,  5,  8);
        Place(theMegalodon, 14,  4,  1);
        Place(xiphShoal,   11,  4, 12);   // shallow strip, amid the Mosasaurus nursery

        return world;
    }
}
