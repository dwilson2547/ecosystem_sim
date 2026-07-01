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
- **Tests:** xUnit 3, `EcosystemSim.Tests` — 54 tests, run `dotnet test` from `sim/`
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
│   ├── ResourceType.cs     # Enum: Food, Water
│   ├── FoodSubtype.cs      # Enum: Graze/Browse/Fruit/Roots/Fish/Shrimp/Crustacean/Squid/Whale
│   ├── ByproductType.cs    # Enum: Fertilizer
│   ├── TerrainType.cs      # Enum: Plains/Forest/Swamp/Desert/Highland/River/ShallowOcean/DeepOcean
│   ├── Season.cs           # Enum: Spring/Summer/Autumn/Winter
│   ├── Disease.cs          # Disease blueprint (spread, mortality, recovery rates)
│   └── *Command.cs         # IWorldCommand implementations for player interventions
│
├── EcosystemSim.Tests/     # xUnit tests
│   └── WorldTests.cs       # 54 tests; isolated worlds, no seeder dependency
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
dotnet test                        # run all 54 tests
dotnet run --project SimConsole    # terminal prototype
```

SimConsole controls: `[Space]` pause, `[← →]` speed, `[D]` disease, `[T]` trade, `[Q]` quit.

Godot: open `godot/project.godot` in Godot 4.7. Controls: `Space` pause, `+`/`-` speed,
middle-mouse drag to pan, scroll to zoom. See `docs/godot-frontend.md` for full details.

---

## All implemented systems

> Full mechanics and tuning constants are in `docs/implementation.md`.

### 1. Resources
Each tile has `ResourcePool`s (Food, Water). Pools regen each tick up to capacity. Populations
on the same tile share resources proportionally when scarce. Satisfaction = supply/demand ratio.

### 2. Terrain
Eight types: Plains, Forest, Swamp, Desert, Highland, River, ShallowOcean, DeepOcean. Baked into
tile at world creation. Each terrain spawns specific typed food pools (see §2a). River and Swamp
also have Water pools. Migration cost varies by terrain (Swamp=1.8×, DeepOcean=1.2×). Land and
ocean biomes are separated — species cannot migrate across the land/ocean boundary. See
`TerrainType.cs` and `docs/food-types.md`.

### 2a. Food type diversity
Each food pool has a `FoodSubtype` (Graze, Browse, Fruit, Roots, Fish, Shrimp, Crustacean, Squid,
Whale). Species declare `FoodPreferences` (full satisfaction) and `AcceptedFoods` (2/3 satisfaction).
Two-pass distribution: preferred food first, then accepted food fills remaining hunger. Species with
no preferences consume any food pool at full satisfaction (backward-compat). See `docs/food-types.md`.

### 3. Seasons
25-tick seasons in order Spring→Summer→Autumn→Winter. Multipliers applied to `RegenPerTick`
each tick. Winter is brutal (0.3× food, 0.2× water). Spring is lush (1.3× food, 1.4× water).
Current season shown in the header. Stored in `WorldState.CurrentSeason`.

### 4. Populations & species
A `SpeciesDefinition` is a blueprint. A `Population` is a live group on a specific tile. The
same species can have multiple populations (same or different tiles, same or different factions).
Population grows on full satisfaction, shrinks (starvation death) on deficit. Uses `Math.Ceiling`
to prevent single-individual limbo.

### 5. Byproducts & fertilizer
Species produce byproducts (e.g. Fertilizer) at a per-individual-per-tick rate. Fertilizer on
a tile boosts food regen (0.02× per unit). Byproducts decay 10%/tick and cap at 200 units.
Brachiosaurus is the keystone fertilizer producer (0.20/tick per individual).

### 6. Migration
Populations migrate when satisfaction falls below their `MigrationThreshold`. They move toward
the neighbor with the most of their most-lacking resource (food comparisons use effective food
amount weighted by species preferences). BFS fallback navigates resource deserts up to 6 tiles
deep. Ocean biome barrier prevents land↔ocean migration. Partial migration: only excess individuals
above `SustainableCount` (tile regen / consumption across all resource types) move out. When
merging into an existing same-species same-faction group, evolved traits (SizeIndex, ImmunityDelta)
are blended weighted by count.

### 7. Disease
Player triggers disease on a tile. It spreads intra-tile (rate × density bonus) and
inter-tile (30% of intra rate) each tick. Two-phase update: collect exposures first, apply
second (prevents order dependency). Mortality scales with infection level and (1 - immunity).
Populations recover based on base recovery + immunity. Cleared when InfectionLevel reaches 0.

### 8. Trade
Player sets trade agreements between factions. Each tick, byproducts are equalized 15%/tick
between the closest tile pair. Active trading reduces tension by 0.04/tick. War automatically
breaks trade. See `EstablishTradeCommand`, `BreakTradeCommand`.

### 9. Diplomacy & combat
Factions within proximity range (5 tiles) accumulate tension based on aggression, proximity,
and resource competition. Tension thresholds: Neutral < 0.5 < Tense < 1.5 < AtWar. Natural
peace drift (−0.03/tick) keeps moderate-aggression well-fed species from always warring. War
exhaustion kicks in after 20 ticks of conflict. Combat is simultaneous: damage =
attacker_count × combat_strength × 0.02/tick for every at-war pair on the same tile.

### 10. Evolution
Two pressure accumulators, not per-tick change:

- **Size**: +1/tick when well-fed (sat≥0.9), −1/tick when starving (sat<0.5). At ±50 ticks
  accumulated, SizeIndex shifts ±0.05 (range [0.5, 2.0]). Larger = more food demand + more
  combat strength. `EffectiveCombatStrength = CombatStrength × √SizeIndex`.
- **Immunity**: +1/tick while infected (InfectionLevel > 0.1). At 30 ticks, ImmunityDelta
  gains 0.02 (max 0.5 permanent gain). `EffectiveImmunity = min(1, BaseImmunity + ImmunityDelta)`.

### 11. Speciation
When `SizeIndex >= 1.5` (large) or `<= 0.65` (small), the population diverges into a new species.
A derived `SpeciesDefinition` is created with traits baked in at the evolved size (food demand,
combat strength, byproduct output, reproduction rate all scale). `SizeIndex` resets to 1.0 on the
new baseline. Naming tiers: base → Greater/Lesser → Giant/Dwarf. If two populations independently
reach the same tier, they share one definition. See **`docs/speciation.md`** for full mechanics.

---

## Tick order (per `World.Tick()`)

Per tile:
1. `RegenerateResources` — regen × season multiplier + fertilizer bonus
2. `DistributeResources` — proportional share, updates `LastSatisfaction`
3. `ApplyGrowthAndDeath` — grow if full, die if starving
4. `ProduceByproducts` — count × species rate
5. `DecayByproducts` — 10%/tick

Global:
6. `Migrate` — collect moves, apply, merge or place
7. `ResolveCombat` — simultaneous casualties for at-war factions on same tile
8. `SpreadDisease` — two-phase exposure + apply + mortality + recovery
9. `ExecuteTrade` — byproduct equalization + tension bonus
10. `UpdateFactionRelations` — tension delta, state transitions
11. `ApplyEvolution` — pressure accumulators + threshold crossings
12. `ApplySpeciation` — fork populations that crossed size thresholds into derived species
13. `State.Tick++`, `AdvanceSeason()`

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
- **Terrain is static** — set during world seeding, never changes at runtime. Seasonal and
  fertilizer modifiers apply at tick time, not to the terrain definition.
- **`EffectiveConsumptionRate`** — food consumption scales with SizeIndex; water does not.
  This means evolving larger has an asymmetric cost for water-dependent species.
- **Ocean biome barrier** — `TerrainStats.SameBiome` returns false when one tile is ocean and
  the other is land. This is enforced in `BestNeighborFor` and its BFS fallback; marine and
  terrestrial species occupy entirely disjoint migration spaces.
- **Typed food two-pass** — preferred food (full sat) consumed before accepted food (2/3 sat);
  species with empty `FoodPreferences` compete for any pool at full sat (backward-compat path).

---

## Testing patterns

Tests use `new World()` directly — never `WorldSeeder.CreateDemo()`. Each test sets up exactly
what it needs on specific tiles. Key helpers in `WorldTests.cs`:

- `BasicSpecies()` — food-only, 0.1 repro, 0.5 starvation
- `AbundantFood()` / `EmptyFood()` — saturated vs zero-regen food pools
- `MakeFactionOnTile()` — faction + pop with 0 repro + 0 starvation (isolates combat)
- `DeclareWar()` — sets AtWar state directly without tension buildup
- `PopOnTile()` — evolution tests, no repro/starvation
- `TestDisease()` — configurable spread/mortality/recovery
- `FertiliserSpecies()` — byproduct-emitting species with zero growth/death

---

## What's next

1. **Godot frontend polish** — disease/trade hotkeys, population history graphs
2. **Carnivore mechanics** — T-Rex consuming other populations as food (Prey food subtype);
   lone-wanderer instinct; territorial hunting range
3. **Procedural map generation** — rivers, biomes, mountain ranges; replaces the hardcoded
   terrain string in `WorldSeeder`
4. **Player interventions** — meteor strike, terraforming, population seeding mid-run
5. **Faction memory** — grudges, reputation, vassal relationships

See `docs/implementation.md` for mechanics of every implemented system,
`docs/food-types.md` for typed food subtype mechanics, and
`docs/godot-frontend.md` for the Godot project architecture.
