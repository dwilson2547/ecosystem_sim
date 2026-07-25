# EcosystemSim — AI Onboarding

Dinosaur ecosystem simulation. Populations compete for resources, form factions, go to war,
trade byproducts, evolve, catch diseases, and migrate across a tile-based world. The player is
a god-figure who seeds the world and intervenes in real time. All behavior is emergent — nothing
is scripted.

**Current state:** engine-complete simulation library + playable Godot 4.7 frontend with Sandbox
and Locust Plague challenge modes. Terminal prototype (`SimConsole`) still works.

---

## Tech stack

- **Language:** C# 12, .NET 8
- **Engine layer:** `EcosystemSim` — a class library, zero UI dependencies, engine-agnostic
- **Tests:** xUnit 3, `EcosystemSim.Tests` — 97 tests, run `dotnet test` from `sim/`
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
    ├── SimMain.cs          # Root node: spawns map, HUD, panels, and scenario selection overlay
    ├── HexMapRenderer.cs   # Instantiates HexTile × 100; PixelToTile + SelectTile
    ├── HexTile.cs          # One hex cell: Polygon2D terrain + pop Label + selection border
    ├── CameraController.cs # Camera2D: MMB-drag pan, scroll-wheel zoom
    ├── HUD.cs              # Day / season / year / speed overlay
    ├── FactionPanel.cs     # Left-side panel: faction list, population summaries, relations
    ├── ScenarioPanel.cs    # Challenge timer, AP budget, objectives, and final result
    ├── ScenarioSelectionOverlay.cs # Startup Sandbox/Challenge mode picker
    └── TileInfoPanel.cs    # Right-side tile details and intervention controls

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
│   ├── PreyCategory.cs     # Enum: SmallHerbivore/LargeHerbivore/SmallMarine/LargeMarine/Insect
│   ├── ByproductType.cs    # Enum: Fertilizer
│   ├── TerrainType.cs      # Enum: Plains/Forest/Swamp/Desert/Highland/River/ShallowOcean/DeepOcean
│   ├── Season.cs           # Enum: Spring/Summer/Autumn/Winter
│   ├── Weather.cs          # Enum: Normal/Rainy/Drought (world-level regen modifier over seasons)
│   ├── Disease.cs          # Disease blueprint (spread, mortality, recovery rates)
│   ├── ScenarioSession.cs  # Active mode, objectives, budget, duration, and result
│   ├── ScenarioFactory.cs  # Scenario setup and Locust Plague outbreak seeding
│   ├── *Action.cs          # Validated, tile-targeted scenario interventions
│   └── *Command.cs         # IWorldCommand implementations for player interventions
│
├── EcosystemSim.Tests/     # xUnit tests
│   ├── WorldTests.cs       # Simulation-system tests; isolated worlds, no seeder dependency
│   └── ScenarioTests.cs    # Scenario setup, objective, budget, and action tests
│
├── SimConsole/             # Terminal prototype
│   ├── Program.cs          # Input loop + tick scheduling
│   ├── WorldSeeder.cs      # Creates the demo world (species, terrain map, initial pops)
│   └── Renderer.cs         # Console map + population table + faction relations
│
└── EcoReport/              # Headless ecology stability report — balance-tuning tool (see its README)
    └── Program.cs          # Multi-run tick harness; per-lineage extinction/boom/oscillation metrics
```

---

## Quick start

```bash
cd sim
dotnet test                        # run all 97 tests
dotnet run --project SimConsole    # terminal prototype
dotnet run --project EcoReport -c Release   # headless ecology stability report (balance tuning)
```

SimConsole controls: `[Space]` pause, `[← →]` speed, `[D]` disease, `[T]` trade, `[Q]` quit.

Godot: open `godot/project.godot` in Godot 4.7, choose Sandbox or Locust Plague, then use
`Space` to pause, `+`/`-` for speed, middle-mouse drag to pan, and scroll to zoom. See
`docs/godot-frontend.md` for full details.

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
DegradesTo)` pair — currently only `Forest → Plains` when `FoodSubtype.Fruit` stays below 6% of
capacity for 90 sustained ticks (`Tile.DegradationPressure`, `World.ApplyTerrainDegradation`).
Sustained heavy browsing by Alamosaurus herds can permanently clear a forest into grassland.

### 3. Seasons
One tick is one day. Four 90-day seasons form a 360-day year in
Spring→Summer→Autumn→Winter order. Multipliers apply to daily regeneration. The HUD exposes year,
day of year, season, and day within the season.

### 3a. Weather
A world-level regen multiplier layered **on top of** seasons (they stack multiplicatively). Runs in
stochastic multi-tick spells driven by the world RNG (so a seeded run reproduces its weather):
`Normal` (~64% of ticks), `Rainy` (~1.25× food, 1.6× water), `Drought` (~0.65× food, 0.4× water).
`World.AdvanceWeather` rolls a new spell (`Weather` enum) + duration when `WeatherTicksRemaining`
hits 0; `World.WeatherMultiplier` is the regen factor. Stored in `WorldState.CurrentWeather`, shown
in the Godot HUD. Droughts genuinely stress herbivores (a severe one can thin a herd below its
`HerdDefense` scale and expose it to predators) — the drought multipliers are the knob for how deadly
weather is.

### 4. Populations & species
A `SpeciesDefinition` is a blueprint. A `Population` is a live group on a specific tile. The
same species can have multiple populations (same or different tiles, same or different factions).
Species reproduce in configured seasonal cohorts (`BreedingSeasons`, `BreedingDayOfSeason`,
`BreedingRate`) and only when satisfaction is at least 0.85. Nutrition and water deprivation are
tracked independently. Mortality begins only when a required resource is effectively exhausted
(≤5% satisfaction) beyond the species' tolerance period, giving populations time to migrate.

`EaseOfEating` is a `Dictionary<FoodSubtype, float>` (0–5 scale) on each `SpeciesDefinition`. Land
demo species: Triceratops (Graze 5, Browse 3, Fruit 1), Parasaurolophus (Browse 5, Graze 3, Fruit 2),
Alamosaurus (Fruit 5, Browse 2). Marine demo species: Mosasaurus (Fish 4, Shrimp 3, Crustacean 2),
Plesiosaur (Fish 5, Squid 3), Xiphactinus (Shrimp 4, Fish 3, Crustacean 2 — a shallow predator that
also hunts Mosasaurus). Empty dict = generalist. See `docs/food-types.md`.

### 5. Byproducts & fertilizer
Species produce byproducts (e.g. Fertilizer) at a per-individual-per-tick rate. Fertilizer on
a tile boosts all food pool regen (0.02× per unit). Byproducts decay 10%/tick and cap at 200
units. Alamosaurus is the keystone fertilizer producer (0.20/tick per individual).

### 6. Migration
Two independent triggers, checked in `Migrate()` in this order:
1. **Flee from water** — a population stranded on River terrain for 2 days evacuates entirely to
   the best non-River neighbor, overriding `MigrationThreshold`
   outright (see §8 — a River tile can look fully satisfied while it's drowning them).
2. **Resource scarcity** — satisfaction below `MigrationThreshold`. The population moves toward
   whichever of Food, Water, or Prey is most lacking. For Food, "best neighbor" is ease-weighted
   (`EffectiveFoodValue` — pool amount × ease-of-eating), so a species is drawn to tiles with more
   of what it can actually eat, not just more raw food. For Prey, "best neighbor" is
   `EffectivePreyAmount` (preferred prey at full weight, accepted at 2/3). All searches respect
   the ocean biome barrier. BFS fallback navigates resource deserts up to 6 tiles deep. Merged
   populations blend SizeIndex, ImmunityDelta, WaterExposure weighted by count.

   **View radius** — `SpeciesDefinition.ViewRadius` (default 1) is how many tile layers out a species
   evaluates when picking a scarcity destination (`BestNeighborByValue`). At 1 it greedily takes the
   best *immediate* neighbour; at ≥2 it sees whole patches and commits toward the richest tile within
   view, skipping a merely-adequate adjacent tile for a far richer one two/three layers out (avoids a
   local optimum). Past ViewRadius it still falls back to the nearest-better BFS out to 6 tiles.
   Currently: Alamosaurus 3; Triceratops, Megalodon, Plesiosaur, Kronosaurus 2; everyone else 1. Only
   the resource/prey scarcity search is view-aware — reactive flee-from-water and predator-scatter
   moves stay immediate-neighbour (nearest-refuge, not best-distant).

### 7. Density drain
Every 5 individuals in a population compounds its resource draw exponentially:
`demand × 1.15^(count / 5)`. A handful of dinosaurs barely dent a tile; a 100-strong herd draws
~16× its naive per-capita share. This is what keeps single-tile mega-herds from being viable —
satisfaction craters long before the raw pool is empty, pushing populations to disperse.

### 8. Water exposure ("can't live in the water")
River is the one terrain that counts as actually *being in the water* (Swamp is walkable
wetland, not submerged). A population accumulates `WaterExposure` while on River terrain; past
`WaterSurvivalThreshold` (3 days) it starts losing 2%/day. It attempts to flee after 2 days;
leaving decays the counter back down, so a brief crossing is harmless.

### 9. Predation (carnivore mechanics)
Carnivore species set `PreyConsumptionRate` and declare `PreferredPrey` / `AcceptedPrey` as
`HashSet<PreyCategory>` (SmallHerbivore, LargeHerbivore, SmallMarine, LargeMarine). `HuntPrey`
runs per tile after `DistributeResources`: preferred prey → full satisfaction, accepted prey →
2/3 satisfaction. A **Holling type-III functional response** makes only `count²/(count+K)` of a herd
huntable per tick (`PreyRefugeHalfSaturation` K), so efficiency collapses as prey thin out and a
predator can never zero a herd in one pass. On top of that, prey may set **`HerdDefense`** (0–1):
safety in numbers cuts the predator's *realized kill* on a massed herd by
`HerdDefense × count/(count+HerdDefenseHalfSaturation)`, fading to nothing as the herd thins. Where
the refuge protects *thinned* herds (low count), herd defense protects *massed* herds (high count) —
together they stop a big herd being ground down into the vulnerable-tail death spiral, giving a
survival floor without inflating the ceiling. This is why Parasaurolophus (`HerdDefense 0.8`) no
longer goes extinct under T-Rex pressure: a lone apex can't efficiently crop a large hadrosaur herd.
Predation is also a **hunt, not a guaranteed drain**: prey set `HuntDifficulty` (0 = easily caught,
default) and a pack's realized kill is scaled by a stochastic hunt-success fraction
(`World.HuntSuccessFraction`) drawn around catchability `1 - HuntDifficulty`, with spread that shrinks
as the pack grows — a big pod reliably takes its mean share while a lone apex feasts or goes hungry
(seeded RNG → reproducible). Tough prey are caught less (armoured Triceratops 0.35 vs agile
Parasaurolophus 0.25); predator `PreyConsumptionRate`s are bumped to offset the average. Note the demo
world now runs greener — hunt difficulty + herd defense make abundant herbivores food/space-limited
rather than predation-limited, with predators cropping the margin.
Prey deaths accumulate fractionally via
`PredationAccumulator` (whole individuals only when it crosses 1) rather than `ceil`-ing every hunt.
Prey also **scatter**: a herd ≥ `ScatterMinHerd` (12) that a predator invades splits a third off to
the safest reachable neighbour (`BestNeighborAwayFromPredators`), throttled by migration cooldown,
so it disperses under pressure even when well-fed while leaving stragglers behind. Prey populations
set `AsPreyCategory`. Carnivores migrate toward prey via the standard BFS.
Demo carnivores: **Kronosaurus** at DeepOcean, hunting Plesiosaur (preferred) and Mosasaurus
(accepted); **Tyrannosaurus** on land — an obligate-carnivore apex pack that thins Parasaurolophus
(SmallHerbivore, preferred) and Triceratops (LargeHerbivore, accepted), keeping the hadrosaur swarm
from razing the forests; **Xiphactinus** in ShallowOcean — a dual-consumption predator pinned to the
shallow strip that *prefers* Mosasaurus (SmallMarine). It exists to close a spatial refuge: hunting is
per-tile, and the deep-water apexes (which only *accept* Mosasaurus) stay in deep water on their
preferred Plesiosaur, so the shallow strip where Mosasaurus breeds had no predator on it at all —
Mosasaurus grew unbounded (30→~1000). Xiphactinus crops it at the source; a per-tile `MaxCount` keeps
the shoal below its prey and Mosasaurus's own `MaxCount` is a coarse safety ceiling. Prey categories:
Parasaurolophus=SmallHerbivore, Triceratops=LargeHerbivore, Mosasaurus=SmallMarine, Plesiosaur=LargeMarine.

### 9a. Insects
A self-contained insect guild on the land side (`PreyCategory.Insect`): **Locust** — an r-selected
grazing pest that swarms the plains and competes with the big grazers for `Graze`, with `HerdDefense`
so a dense swarm is never fully consumed and a short deprivation tolerance so it crashes when grass is
stripped (boom-bust plagues, oscillating ~40→2000+); and **Meganeura** — a giant dragonfly, an
obligate insectivore capped small (`MaxCount`) so it lives off the swarm's margin rather than
controlling it (locusts are food-limited, not predation-limited). Adding the locust competitor also
pulled the over-abundant grazers leaner. No dedicated dino insectivore yet — insects are a prey base
future species can build on.

### 9b. Symbiosis — pollination (mutualism)
The first mutualism. A species with **`PollinationBoost`** (0–1) lifts `Fruit` regen on any tile it
occupies by `PollinationBoost × count/(count + PollinationHalfSaturation)` (saturating with the colony
size), applied in `RegenerateResources` (`World.PollinationBoostOn`). The demo's **Bee** is the
pollinator: it sips nectar (a light `Fruit` draw) and pollinates in return, so a bee-worked forest
regenerates fruit faster — feeding the bees *and* the fruit-eaters (Alamosaurus, Parasaurolophus). Both
sides gain. Note: bees settle on the richest fruit tiles rather than dispersing (normal food-seeking
migration), and the demo's main fruit-eater Alamosaurus is reproduction-limited, so the population-level
benefit is subtle — the mechanic is verified in isolation by `Pollination_LiftsFruitRegen`. Currently
only Fruit is pollinated; extend `RegenerateResources` for other flowering subtypes.

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

> **Declarative war is currently DISABLED** (`World.DiplomaticWarEnabled = false`). The faction layer
> is half-built scaffolding — a *true* faction won't exist until symbiotic relationships between
> species are introduced — and tension-driven AtWar was warring peaceful species into extinction
> (a lone apex Megalodon reads as perpetually starving with no prey on its tile, drags every
> neighbour to war, and dies as a Count=1 unit). Tension still accumulates and states still resolve
> up to **Tense**; they just never escalate to AtWar, so `ResolveCombat` never fires and war never
> breaks trade. Combat still works when a war is set directly (tests, future player commands). The
> intended replacement is a **territorial model** — populations migrate into each other and brawl on
> a shared tile until one retreats — built alongside the faction/symbiosis work, not declarative war.
> Don't extend or "fix" the tension→war system; it's slated for replacement.

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

### 15. Scenarios and interventions
`World.StartScenario()` creates either an unrestricted Sandbox session or the three-year Locust
Plague challenge. Challenge state lives in `WorldState.Scenario`: remaining days, a shared 10-point
budget, objective progress, and won/lost status. The plague starts with 12 Plains outbreaks of 42
locusts and suppresses Plains grass to at most 30%.

Victory requires all four land dinosaur lineages to survive, locusts to finish at 500 or fewer,
and average Plains Graze to finish at 25% or more. Tile-targeted `IScenarioAction`s currently cull
99% of locusts within two hexes for 1 AP, fully restore Plains Graze within four hexes for 2 AP,
or seed five Meganeura for 3 AP. Sandbox uses the same actions without costs or a time limit.

---

## Tick order (per `World.Tick()`)

Per tile:
1. `RegenerateResources` — regen × season multiplier + fertilizer bonus
2. `DistributeResources` — `DistributeWater` (plain supply/demand) + `DistributeFood`
   (ease-weighted by FoodSubtype × availability); both feed into `LastSatisfaction`; density drain
   inflates demand
3. `HuntPrey` — predators consume prey populations; sets predator `LastSatisfaction`
4. `ApplyPopulationChange` — seasonal cohorts plus delayed nutrition/water deprivation mortality
5. `ApplyWaterExposure` — drowning losses for populations stranded on River terrain
6. `ProduceByproducts` — count × species rate
7. `DecayByproducts` — 10%/tick
8. `ApplyTerrainDegradation` — check FoodSubtype.Fruit ratio; if sustained below 6% for 90 ticks
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
16. `State.Tick++` — season/day/year values derive from elapsed days

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
- **Fractional population change, no rounding spikes** — seasonal births, deprivation deaths, and
  predation bank fractional totals and apply whole individuals only when an accumulator crosses 1.
- **Terrain is static** — set during world seeding, never changes at runtime except via terrain
  degradation. Seasonal and fertilizer modifiers apply at tick time, not to the terrain definition.
- **`Population.EffectiveFoodDemand` / `EffectiveWaterDemand` / `EffectivePreyDemand`** — food and
  prey demand scale with SizeIndex; water does not. Evolving larger has an asymmetric cost.
- **Ease-of-eating is a diet gate** — a pool absent from a species' `EaseOfEating` dict (or set to 0)
  is never consumed regardless of how much sits there unconsumed.
- **Ocean biome barrier** — `TerrainStats.IsOcean()` prevents migration across the land/ocean
  boundary. Marine and terrestrial species occupy entirely disjoint migration spaces.
- **Exhaustion before deprivation death** — partial supply can limit breeding and trigger migration,
  but food/water mortality only begins below 5% satisfaction after the configured tolerance period.
- **Prey two-pass mirrors food** — preferred prey at full satisfaction, accepted prey at 2/3 sat.
  A Holling type-III functional response (`count²/(count+K)`) makes only a density-dependent fraction
  of a herd huntable, so a predator can't wipe a tile in one pass; prey deaths accumulate fractionally
  (`PredationAccumulator`) rather than rounding each hunt up to a full kill.

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

1. **More challenge scenarios** — reuse the scenario/objective/action framework for drought,
   disease, predator imbalance, and restoration objectives
2. **Godot frontend polish** — ocean tile rendering, disease/trade hotkeys, population history graphs
3. **Predator-prey balance tuning** — the refuge/scatter/accumulator *mechanisms* are in place but
   deliberately un-tuned: in a sparse-predator sandbox the equilibrium currently favours prey. Tune
   `PreyRefugeHalfSaturation`, `ScatterMinHerd`, Kronosaurus daily prey demand / `BreedingRate`,
   and per-species `MigrationCooldownTicks` (skittishness) once the roster grows
4. **More land predators** — the Tyrannosaurus apex is in (§9); a second tier (nimble pack hunter
   pressuring hadrosaurs/juveniles, or a scavenger niche) would make land predation less monolithic
5. **Procedural map generation** — rivers, biomes, mountain ranges; replaces the hardcoded
   terrain string in `WorldSeeder`
6. **More player interventions** — meteor strikes, terraforming, and broader population seeding
7. **Symbiosis + real factions** — the current faction layer is provisional scaffolding. A true
   faction should emerge from **symbiotic relationships between different dino types**, not be assigned
   at seed time. This is the gateware for the rest of the faction/diplomacy work below.
8. **Territorial conflict** (replaces declarative war, see §12) — populations migrate into each other
   and brawl on a shared tile until one retreats, instead of accumulating tension → AtWar. Build with
   the faction/symbiosis work; re-enable or delete `DiplomaticWarEnabled` at that point.
9. **Active apex predators** — the Megalodon currently survives fully on fish and never migrates to
   hunt (sat stays at 1.0). If apexes should exert top-down control, fish must not *fully* sate a
   hunter (lower food ease / raise `MigrationThreshold`, or a hunger-to-hunt drive).
10. **Faction memory** — grudges, reputation, vassal relationships

See `docs/implementation.md` for mechanics of every implemented system,
`docs/food-types.md` for typed food subtype mechanics, and
`docs/godot-frontend.md` for the Godot project architecture.
