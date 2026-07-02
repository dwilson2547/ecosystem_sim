# EcosystemSim — AI Onboarding

Dinosaur ecosystem simulation. Populations compete for resources, form factions, go to war,
trade byproducts, evolve, catch diseases, and migrate across a tile-based world. The player is
a god-figure who seeds the world and intervenes in real time. All behavior is emergent — nothing
is scripted.

**Current state:** engine-complete simulation library + Godot 4.7 frontend scaffold. Terminal
prototype (`SimConsole`) still works but the real UI is in `godot/`.

---

## Tech stack

- **Language:** C# 12, .NET 8
- **Engine layer:** `EcosystemSim` — a class library, zero UI dependencies, engine-agnostic
- **Tests:** xUnit 3, `EcosystemSim.Tests` — 68 tests, run `dotnet test` from `sim/`
- **Console UI:** `SimConsole` — terminal renderer / prototype; run from `sim/`
- **Game UI:** Godot 4.7 (.NET), lives in `godot/`; references `EcosystemSim` via ProjectReference

---

## Solution structure

```
godot/                      # Godot 4.7 game frontend
├── project.godot           # Godot config (autoloads, display)
├── EcosystemGame.csproj    # C# project referencing EcosystemSim
├── scenes/Main.tscn        # Root scene (Node2D + SimMain.cs)
└── scripts/
    ├── SimManager.cs       # Autoload singleton: owns World, drives tick timer
    ├── DemoWorldSeeder.cs  # Creates the demo world for the Godot build
    ├── SimMain.cs          # Root node: spawns camera, renderer, HUD, FactionPanel, TileInfoPanel
    ├── HexMapRenderer.cs   # Instantiates HexTile × 100; PixelToTile + SelectTile
    ├── HexTile.cs          # One hex cell: Polygon2D terrain + pop Label + selection border
    ├── CameraController.cs # Camera2D: MMB-drag pan, scroll-wheel zoom
    ├── HUD.cs              # Tick / season / year / speed overlay
    ├── FactionPanel.cs     # Left-side panel: faction list, population summaries, relations
    └── TileInfoPanel.cs    # Right-side panel: terrain, resources, population details

sim/
├── EcosystemSim/           # The simulation engine (class library)
│   ├── World.cs            # Main tick loop + all system logic
│   ├── WorldState.cs       # Snapshot: tick, season, map, factions
│   ├── WorldMap.cs         # Variable-size tile grid, 6-way hex neighbor adjacency (odd-r offset)
│   ├── Tile.cs             # One map cell: terrain, resources, populations, byproducts
│   ├── Population.cs       # A live group of one species on one tile
│   ├── SpeciesDefinition.cs # Species blueprint (traits, consumption, food prefs, byproduct rates)
│   ├── Faction.cs          # Political grouping of populations
│   ├── FactionRelation.cs  # Diplomatic state between two factions
│   ├── ResourcePool.cs     # A tile's supply of one resource (typed by FoodSubtype for food pools)
│   ├── ByproductPool.cs    # A tile's accumulated byproduct (e.g. fertilizer)
│   ├── ResourceType.cs     # Enum: Food, Water, Prey
│   ├── FoodSubtype.cs      # Enum: Graze/Browse/Fruit/Roots/Fish/Shrimp/Crustacean/Squid/Whale
│   ├── PreyCategory.cs     # Enum: SmallHerbivore/LargeHerbivore/SmallMarine/LargeMarine
│   ├── ByproductType.cs    # Enum: Fertilizer
│   ├── TerrainType.cs      # Enum: Plains/Forest/Swamp/Desert/Highland/River/ShallowOcean/DeepOcean
│   ├── Season.cs           # Enum: Spring/Summer/Autumn/Winter
│   ├── Disease.cs          # Disease blueprint (spread, mortality, recovery rates)
│   └── *Command.cs         # IWorldCommand implementations for player interventions
│
├── EcosystemSim.Tests/     # xUnit tests
│   └── WorldTests.cs       # 68 tests; isolated worlds, no seeder dependency
│
└── SimConsole/             # Terminal prototype
    ├── Program.cs          # Input loop + tick scheduling
    ├── WorldSeeder.cs      # Creates the demo world (species, terrain map, initial pops)
    └── Renderer.cs         # Console map + population table + faction relations
```

---

## Quick start

```bash
cd sim
dotnet test                        # run all 68 tests
dotnet run --project SimConsole    # terminal prototype
```

SimConsole controls: `[Space]` pause, `[← →]` speed, `[D]` disease, `[T]` trade, `[Q]` quit.

Godot: open `godot/project.godot` in Godot 4.7. Controls: `Space` pause, `+`/`-` speed,
middle-mouse drag to pan, scroll to zoom. See `docs/godot-frontend.md` for full details.

---

## All implemented systems

> Full mechanics and tuning constants are in `docs/implementation.md`.

### 1. Resources
Each tile has `ResourcePool`s. Food pools are tagged with a `FoodSubtype` (Graze/Browse/Fruit/Roots
for land, Fish/Shrimp/Crustacean/Squid/Whale for ocean) plus a Water pool on non-ocean tiles. Pools
regen each tick up to capacity. A population's food demand is split across pools by **ease-of-eating**
(see §2a) weighted by what's available — so a species gravitates to whatever's both easy for it AND
present. Water is distributed the plain supply/demand way, unaffected by ease.

### 2. Terrain
Eight types: Plains, Forest, Swamp, Desert, Highland, River, ShallowOcean, DeepOcean. Baked into
tile at world creation. Each terrain has a total food regen budget split across its typed food pools
by `TerrainStats.FoodComposition` (Plains is mostly Graze, Forest is mostly Fruit, ShallowOcean has
Fish/Shrimp/Crustacean, DeepOcean has Squid/Fish/Whale). Land and ocean form separate biomes —
species cannot migrate across the land/ocean boundary. Migration prefers lower-cost terrain when
resources are similar (Swamp=1.8×; Desert=0.8×). See `TerrainType.cs` and `docs/food-types.md`.

Water is present on every non-ocean terrain, scaled off River as the "full water" reference tile:
Desert 0-5%, Highland ~5%, Plains/Forest ~10%, Swamp ~15%, River 100%.

Terrain isn't fully static: **`TerrainStats.Degradation`** maps a terrain to a `(TriggerSubtype,
DegradesTo)` pair — currently only `Forest → Plains` when `FoodSubtype.Fruit` stays below 10% of
capacity for 60 sustained ticks (`Tile.DegradationPressure`, `World.ApplyTerrainDegradation`).
Sustained heavy browsing by Alamosaurus herds can permanently clear a forest into grassland.

### 3. Seasons
25-tick seasons in order Spring→Summer→Autumn→Winter. Multipliers applied to `RegenPerTick`
each tick. Winter is brutal (0.3× food, 0.2× water). Spring is lush (1.3× food, 1.4× water).
Current season shown in the header. Stored in `WorldState.CurrentSeason`.

### 4. Populations & species
A `SpeciesDefinition` is a blueprint. A `Population` is a live group on a specific tile. The
same species can have multiple populations (same or different tiles, same or different factions).
Population grows on full satisfaction, shrinks (starvation death) on deficit. Uses `Math.Ceiling`
to prevent single-individual limbo.

Population grows on full satisfaction (sat≥0.85), shrinks in the starvation zone (sat≤0.50), and
holds stable in between — the neutral zone [0.50, 0.85) lets accepted-food species stabilize.

`EaseOfEating` is a `Dictionary<FoodSubtype, float>` (0–5 scale) on each `SpeciesDefinition`. Land
demo species: Triceratops (Graze 5, Browse 3, Fruit 1), Parasaurolophus (Browse 5, Graze 3, Fruit 2),
Alamosaurus (Fruit 5, Browse 2). Marine demo species: Mosasaurus (Fish 4, Shrimp 3, Crustacean 2),
Plesiosaur (Fish 5, Squid 3). Empty dict = generalist. See `docs/food-types.md`.

### 5. Byproducts & fertilizer
Species produce byproducts (e.g. Fertilizer) at a per-individual-per-tick rate. Fertilizer on
a tile boosts all food pool regen (0.02× per unit). Byproducts decay 10%/tick and cap at 200
units. Alamosaurus is the keystone fertilizer producer (0.20/tick per individual).

### 6. Migration
Two independent triggers, checked in `Migrate()` in this order:
1. **Flee from water** — a population stranded on River terrain past `WaterFleeThreshold` (10
   ticks) evacuates entirely to the best non-River neighbor, overriding `MigrationThreshold`
   outright (see §8 — a River tile can look fully satisfied while it's drowning them).
2. **Resource scarcity** — satisfaction below `MigrationThreshold`. The population moves toward
   whichever of Food, Water, or Prey is most lacking. For Food, "best neighbor" is ease-weighted
   (`EffectiveFoodValue` — pool amount × ease-of-eating), so a species is drawn to tiles with more
   of what it can actually eat, not just more raw food. For Prey, "best neighbor" is
   `EffectivePreyAmount` (preferred prey at full weight, accepted at 2/3). All searches respect
   the ocean biome barrier. BFS fallback navigates resource deserts up to 6 tiles deep. Merged
   populations blend SizeIndex, ImmunityDelta, WaterExposure weighted by count.

### 7. Density drain
Every 5 individuals in a population compounds its resource draw exponentially:
`demand × 1.15^(count / 5)`. A handful of dinosaurs barely dent a tile; a 100-strong herd draws
~16× its naive per-capita share. This is what keeps single-tile mega-herds from being viable —
satisfaction craters long before the raw pool is empty, pushing populations to disperse.

### 8. Water exposure ("can't live in the water")
River is the one terrain that counts as actually *being in the water* (Swamp is walkable
wetland, not submerged). A population accumulates `WaterExposure` while on River terrain; past
`WaterSurvivalThreshold` (15 ticks) it starts drowning at 12%/tick. Leaving decays the counter
back down — a quick drink is harmless, camping there is not.

### 9. Predation (carnivore mechanics)
Carnivore species set `PreyConsumptionRate` and declare `PreferredPrey` / `AcceptedPrey` as
`HashSet<PreyCategory>` (SmallHerbivore, LargeHerbivore, SmallMarine, LargeMarine). `HuntPrey`
runs per tile after `DistributeResources`: preferred prey → full satisfaction, accepted prey →
2/3 satisfaction. Prey deaths = `ceil(consumed)` — any nonzero hunt claims at least 1 individual.
Prey populations set `AsPreyCategory`. Carnivores migrate toward prey via the standard BFS.
Demo carnivore: **Kronosaurus** at DeepOcean, hunting Plesiosaur (preferred) and Mosasaurus (accepted).

### 10. Disease
Player triggers disease on a tile. It spreads intra-tile (rate × density bonus) and
inter-tile (30% of intra rate) each tick. Two-phase update: collect exposures first, apply
second (prevents order dependency). Mortality scales with infection level and (1 - immunity).
Populations recover based on base recovery + immunity. Cleared when InfectionLevel reaches 0.

### 11. Trade
Player sets trade agreements between factions. Each tick, byproducts are equalized 15%/tick
between the closest tile pair. Active trading reduces tension by 0.04/tick. War automatically
breaks trade. See `EstablishTradeCommand`, `BreakTradeCommand`.

### 12. Diplomacy & combat
Factions within proximity range (5 tiles) accumulate tension based on aggression, proximity,
and resource competition. Tension thresholds: Neutral < 0.5 < Tense < 1.5 < AtWar. Natural
peace drift (−0.03/tick) keeps moderate-aggression well-fed species from always warring. War
exhaustion kicks in after 20 ticks of conflict. Combat is simultaneous: damage =
attacker_count × combat_strength × 0.02/tick for every at-war pair on the same tile.

### 13. Evolution
Two pressure accumulators, not per-tick change:

- **Size**: +1/tick when well-fed (sat≥0.9), −1/tick when starving (sat<0.5). At ±50 ticks
  accumulated, SizeIndex shifts ±0.05 (range [0.5, 2.0]). Larger = more food demand + more
  combat strength. `EffectiveCombatStrength = CombatStrength × √SizeIndex`.
- **Immunity**: +1/tick while infected (InfectionLevel > 0.1). At 30 ticks, ImmunityDelta
  gains 0.02 (max 0.5 permanent gain). `EffectiveImmunity = min(1, BaseImmunity + ImmunityDelta)`.

### 14. Speciation
When `SizeIndex >= 1.5` (large) or `<= 0.65` (small), the population diverges into a new species.
A derived `SpeciesDefinition` is created with traits baked in at the evolved size (food demand,
combat strength, byproduct output, reproduction rate all scale; EaseOfEating carries over
unchanged — evolving bigger doesn't change what a species can physically eat). `SizeIndex` resets
to 1.0 on the new baseline. Naming tiers: base → Greater/Lesser → Giant/Dwarf. If two populations
independently reach the same tier, they share one definition. See **`docs/speciation.md`**.

---

## Tick order (per `World.Tick()`)

Per tile:
1. `RegenerateResources` — regen × season multiplier + fertilizer bonus
2. `DistributeResources` — `DistributeWater` (plain supply/demand) + `DistributeFood`
   (ease-weighted by FoodSubtype × availability); both feed into `LastSatisfaction`; density drain
   inflates demand
3. `HuntPrey` — predators consume prey populations; sets predator `LastSatisfaction`
4. `ApplyGrowthAndDeath` — grow if sat≥0.85, die if sat≤0.50, hold in [0.50,0.85)
5. `ApplyWaterExposure` — drowning losses for populations stranded on River terrain
6. `ProduceByproducts` — count × species rate
7. `DecayByproducts` — 10%/tick
8. `ApplyTerrainDegradation` — check FoodSubtype.Fruit ratio; if sustained below 10% for 60 ticks
   convert Forest→Plains and rebuild resource pools

Global:
9. `Migrate` — flee-from-water check first, then satisfaction-based scarcity check (Food/Water/Prey);
   collect moves, apply, merge or place; biome barrier prevents land↔ocean migration
10. `ResolveCombat` — simultaneous casualties for at-war factions on same tile
11. `SpreadDisease` — two-phase exposure + apply + mortality + recovery
12. `ExecuteTrade` — byproduct equalization + tension bonus
13. `UpdateFactionRelations` — tension delta, state transitions
14. `ApplyEvolution` — pressure accumulators + threshold crossings
15. `ApplySpeciation` — fork populations that crossed size thresholds into derived species
16. `State.Tick++`, `AdvanceSeason()`

---

## Key design invariants

- **Dead pops stay on tile** (`Count=0`). They render as `[EXTINCT]` and are excluded from all
  simulation logic. Removing them would erase history.
- **Moves are batched in Migrate** — all migrations computed before any are applied, preventing
  cascade relocation within a single tick.
- **Disease exposure is two-phase** — collect all exposures, then apply. Otherwise tick order
  within the tile loop would make disease spread order-dependent.
- **No global resource pools** — populations can only consume what's on their tile. Geography
  matters.
- **Math.Ceiling for growth** — `(int)(1 × 0.05) = 0` would permanently strand Count=1 pops.
  Ceiling ensures at least 1 individual grows/dies even in small populations.
- **Terrain is static** — set during world seeding, never changes at runtime except via terrain
  degradation. Seasonal and fertilizer modifiers apply at tick time, not to the terrain definition.
- **`Population.EffectiveFoodDemand` / `EffectiveWaterDemand` / `EffectivePreyDemand`** — food and
  prey demand scale with SizeIndex; water does not. Evolving larger has an asymmetric cost.
- **Ease-of-eating is a diet gate** — a pool absent from a species' `EaseOfEating` dict (or set to 0)
  is never consumed regardless of how much sits there unconsumed.
- **Ocean biome barrier** — `TerrainStats.IsOcean()` prevents migration across the land/ocean
  boundary. Marine and terrestrial species occupy entirely disjoint migration spaces.
- **Three-zone growth** — sat≥0.85 grows, sat≤0.50 starves, [0.50,0.85) neutral hold. The
  neutral zone lets accepted-food species stabilize rather than perpetually starving.
- **Prey two-pass mirrors food** — preferred prey at full satisfaction, accepted prey at 2/3 sat.
  `Math.Ceiling` on prey deaths prevents hunts from consuming fractional individuals.

---

## Testing patterns

Tests use `new World()` directly — never `WorldSeeder.CreateDemo()`. Each test sets up exactly
what it needs on specific tiles. Key helpers in `WorldTests.cs`:

- `BasicSpecies()` — food-only, empty `EaseOfEating` (generalist), 0.1 repro, 0.5 starvation
- `AbundantFood()` / `EmptyFood()` — saturated vs zero-regen `Food` pools tagged `FoodSubtype.Graze`
- `MakeFactionOnTile()` — faction + pop with 0 repro + 0 starvation (isolates combat)
- `DeclareWar()` — sets AtWar state directly without tension buildup
- `PredatorSpecies(name, rate, preferred?, accepted?)` — carnivore with `PreyConsumptionRate`; 0 repro/starvation
- `PreySpecies(name, PreyCategory)` — prey species tagged with `AsPreyCategory`; 0 repro/starvation
- `PopOnTile()` — evolution tests, no repro/starvation
- `TestDisease()` — configurable spread/mortality/recovery
- `FertiliserSpecies()` — byproduct-emitting species with zero growth/death

---

## What's next

1. **Godot frontend polish** — ocean tile rendering, disease/trade hotkeys, population history graphs
2. **Carnivore tuning** — Kronosaurus `ConsumptionRate` / `ReproductionRate` balance; consider
   `ReproductionAccumulator` to fix `Math.Ceiling` forcing +1 growth on very slow-reproducing species
3. **Land carnivore** — T-Rex consuming `SmallHerbivore`/`LargeHerbivore`
4. **Procedural map generation** — rivers, biomes, mountain ranges; replaces the hardcoded
   terrain string in `WorldSeeder`
5. **Player interventions** — meteor strike, terraforming, population seeding mid-run
6. **Faction memory** — grudges, reputation, vassal relationships

See `docs/implementation.md` for mechanics of every implemented system,
`docs/food-types.md` for typed food subtype mechanics, and
`docs/godot-frontend.md` for the Godot project architecture.
