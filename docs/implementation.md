# Implementation Reference

Detailed mechanics for every system in the simulation. Read this when you need to understand
*why* something works the way it does, not just what the code does. For file locations and
architecture overview, see `CLAUDE.md`.

---

## Architecture

The simulation is a **pure state machine**. `World` holds `WorldState` (the only mutable data)
and exposes two methods:

```csharp
world.Tick();                    // advance simulation by one step
world.Apply(IWorldCommand cmd);  // player intervention
```

There are no events, no callbacks, no async. The UI drives the tick rate externally. This makes
testing trivial and makes frontend integration straightforward — just call `Tick()` on a timer.

`WorldState` contains:
- `Tick` — total ticks elapsed
- `CurrentSeason`, `SeasonTick` — which season and how far through it
- `Map` — the 10×10 `WorldMap` with all tiles
- `Factions` — list of all factions (including extinct ones)

---

## Tick loop order

**Per tile** (inner loop over all tiles):
1. `RegenerateResources` — pool regen scaled by season multiplier + fertilizer bonus
2. `DistributeResources` — proportional resource distribution, sets `LastSatisfaction`
3. `HuntPrey` — carnivores consume prey populations; sets predator `LastSatisfaction`
4. `ApplyGrowthAndDeath` — population change based on satisfaction (three-zone: grow ≥ 0.85, die ≤ 0.50)
5. `ApplyWaterExposure` — drowning losses for populations stranded on River terrain
6. `ProduceByproducts` — each individual emits byproducts at species rate
7. `DecayByproducts` — byproduct pools decay 10%/tick
8. `ApplyTerrainDegradation` — converts terrain (e.g. Forest → Plains) if its defining food
   subtype has stayed denuded long enough

**Global** (after all tiles processed):
8. `Migrate` — batch-compute all moves, then apply
9. `ResolveCombat` — simultaneous at-war casualties per tile
10. `SpreadDisease` — two-phase exposure collection then application
11. `ExecuteTrade` — byproduct equalization between trading faction pairs
12. `UpdateFactionRelations` — tension delta, state transitions
13. `ApplyEvolution` — size and immunity pressure accumulators
14. `ApplySpeciation` — fork populations that crossed size thresholds into derived species
15. `State.Tick++` then `AdvanceSeason()`

**Why this order matters:**
- Resources are distributed *before* growth so death/growth reflects current food access
- Migration happens *after* per-tile loop so a pop can't migrate and then immediately consume at
  its new tile in the same tick
- Disease exposure is collected in one pass then applied — prevents tile-loop order from making
  disease spread directionally biased
- Evolution runs last so it sees the satisfaction value from *this* tick's resource distribution

---

## Resources & food pools

`ResourceType` has three values: `Food`, `Water`, and `Prey`. Every food `ResourcePool` also
carries a nullable `FoodSubtype` tag that identifies what it contains — `Graze`, `Browse`, `Fruit`,
`Roots` for land pools, and `Fish`, `Shrimp`, `Crustacean`, `Squid`, `Whale` for ocean pools.
`Prey` is consumed directly from other populations (not from resource pools) — see **Predation**.

`ResourcePool` fields: `Type`, `FoodSubtype?`, `Amount`, `Capacity`, `RegenPerTick`.

Regen each tick:
```
effectiveRegen = RegenPerTick × seasonMultiplier
if pool.Type == Food: effectiveRegen += fertilizerAmount × 0.02   (FertilizerBoost)
pool.Amount = min(pool.Capacity, pool.Amount + effectiveRegen)
```

`DistributeResources` splits into two independent passes:

**`DistributeWater`** — plain supply/demand: when total demand > supply, each population gets
`(their demand / total demand) × available supply`. Satisfaction = `received / demanded`.

**`DistributeFood`** — a population has one aggregate `FoodConsumptionRate` (density-drained).
It's split across food pools at consumption time, weighted by ease and availability:
```
weight[pool]  = species.EffectiveEase(pool.FoodSubtype) × pool.Amount
demand[pool]  = totalDemand × weight[pool] / Σ weight
```
Demand gravitates toward pools that are both easy to eat AND actually present. If collectively
over-requested, each pool's grant is scaled proportionally before any pool is consumed — order
of processing doesn't affect the result. A pool with EffectiveEase == 0 gets zero weight and is
never touched — ease-of-eating is a hard diet gate, not a soft preference.

A population's `LastSatisfaction` is the **minimum** of food and water satisfaction. One scarce
resource tanks full satisfaction even if the other is plentiful.

See `docs/food-types.md` for the ease-of-eating table and terrain composition design.

**Density drain.** A population's demand is scaled up exponentially with its own size, in steps
of 5 individuals:
```
demand = count × EffectiveConsumptionRate × 1.15^(count / 5)
```
5 individuals draw 1.15× their per-capita share, 50 draw ~4.0×, 100 draw ~16.4×. This is what
`received / demand` (i.e. satisfaction) is computed against, so a single tile packed with a huge
herd looks scarce to itself even when the raw resource pool is large — a tile that comfortably
sustains 20 individuals will starve a 100-strong herd on the exact same food. Small, spread-out
groups are unaffected; the penalty only bites once a tile gets crowded. Constants:
`DensityDrainBase = 1.15`, `DensityDrainGroupSize = 5` (both in `World.cs`).

---

## Terrain

Eight terrain types set on `Tile.Terrain` during world creation. Static at the individual-tile
level (a Plains tile's baseline stays Plains) but no longer immutable overall — see **Terrain
degradation** below. Land and ocean are biome-separated: species cannot migrate across the
land/ocean boundary (enforced in `BestNeighborByValue` via `TerrainStats.IsOcean()`).

| Terrain      | Total food regen | Water            | Migration cost |
|--------------|-------------------|------------------|----------------|
| Plains       | 10/tick           | ~10% of River    | 1.0×           |
| Forest       | 15/tick           | ~10% of River    | 1.4×           |
| Swamp        | 7/tick            | ~15% of River    | 1.8×           |
| Desert       | 3/tick            | 0-5% of River    | 0.8×           |
| Highland     | 8/tick            | ~5% of River     | 1.5×           |
| River        | 12/tick           | 100% (15/tick, 200 cap) | 1.0×    |
| ShallowOcean | 20/tick           | none             | 1.0×           |
| DeepOcean    | 15/tick           | none             | 1.2×           |

**Food composition.** Each terrain's total food regen/capacity is split across typed food pools
by `TerrainStats.FoodComposition` — a min/max percentage range per FoodSubtype, sampled
independently per tile at world-seed time then normalized to sum to 100%:

| Terrain      | Graze   | Browse   | Fruit   | Roots   | Fish    | Shrimp  | Crustacean | Squid   | Whale   |
|--------------|---------|----------|---------|---------|---------|---------|------------|---------|---------|
| Plains       | 60-75%  | 20-35%   | 0-5%    | —       | —       | —       | —          | —       | —       |
| Forest       | 12-20%  | 12-20%   | 60-75%  | —       | —       | —       | —          | —       | —       |
| Swamp        | 0-10%   | 40-55%   | —       | 40-55%  | —       | —       | —          | —       | —       |
| Desert       | 5-15%   | 65-85%   | 0-10%   | —       | —       | —       | —          | —       | —       |
| Highland     | 25-40%  | 55-70%   | 0-5%    | —       | —       | —       | —          | —       | —       |
| River        | 80-100% | 5-10%    | —       | —       | 0-10%   | —       | —          | —       | —       |
| ShallowOcean | —       | —        | —       | —       | 30-40%  | 40-55%  | 15-25%     | —       | —       |
| DeepOcean    | —       | —        | —       | —       | 20-30%  | —       | —          | 50-65%  | 10-20%  |

**Water.** Every non-ocean terrain has a Water pool, scaled off River as the reference tile:
`waterRegen = 15 × pct/100`, `waterCapacity = 200 × pct/100`. Ocean tiles carry no Water pool.

Both `WorldSeeder` and `DemoWorldSeeder` build every tile's resource pools with a single shared
call, `TerrainStats.BuildResourcePools(terrain, random)` — this is also what
`World.ApplyTerrainDegradation` calls at runtime, so a degraded tile gets pools structurally
identical to one seeded that way from the start.

Migration cost is used as a **tiebreaker** in `BestNeighborByValue` — when multiple neighbors
have similar value, prefer the one with lower entry cost.

**Terrain degradation.** `TerrainStats.Degradation` maps a terrain to `(TriggerSubtype,
DegradesTo)` — currently only `Forest → (FoodSubtype.Fruit, Plains)`. Each tick,
`ApplyTerrainDegradation` checks the trigger pool's `Amount / Capacity`:
```
if ratio < 0.10: tile.DegradationPressure++
else:            tile.DegradationPressure = max(0, pressure - 1)
if pressure >= 60: tile.Terrain = Plains; rebuild pools; pressure = 0
```
Fruit (canopy-equivalent food) sustained below 10% capacity for ~60 ticks converts the Forest to
Plains permanently. The tile's composition is rebuilt fresh from the Plains distribution — the old
Forest pool set is discarded. Same pressure-accumulator shape as `WaterExposure`/`SizePressure`:
sustained denudation, not a single bad tick, triggers conversion.

**Demo world terrain map** (`WorldSeeder`, 16×10):
```
     x:  0  1  2  3  4  5  6  7  8  9 10 11 12 13 14 15
y=0:     H  H  H  P  P  P  D  D  D  D  A  A  B  B  B  B
y=1:     H  H  P  P  P  D  D  D  D  D  A  A  B  B  B  B  ← Highland Tric at (1,1)
y=2:     H  F  F  F  P  P  P  D  D  D  A  A  B  B  B  B  ← Valley   Tric at (3,2)
y=3:     P  F  R  R  R  P  P  P  D  D  A  A  B  B  B  B  ← Mosasaurus at (10,3)
y=4:     P  F  R  R  R  R  P  P  P  D  A  A  B  B  B  B  ← River Alamo at (5,4)
y=5:     P  P  R  R  R  R  R  P  P  P  A  A  B  B  B  B  ← Kronosaurus at (13,5)
y=6:     P  S  S  R  R  R  P  P  F  P  A  A  B  B  B  B  ← Midland Para at (7,6); Plesiosaur at (11,6)
y=7:     P  S  S  S  P  P  P  F  F  P  A  A  B  B  B  B
y=8:     D  S  P  P  P  P  F  F  F  D  A  A  B  B  B  B  ← Eastern Para at (8,8)
y=9:     D  D  D  P  P  P  P  P  D  D  A  A  B  B  B  B
H=Highland  F=Forest  R=River  S=Swamp  D=Desert  P=Plains
A=ShallowOcean  B=DeepOcean
```

Triceratops start in Highland with no water — they need to migrate south to the River/Swamp band.
Marine species occupy the right 6 columns and cannot migrate onto land.

**Water exposure.** River is the one terrain that counts as *being in the water*, not just having
water nearby (Swamp has a water pool but is still walkable land). No species can live there
indefinitely:
```
if on River: WaterExposure++; if WaterExposure > 15, lose 12% of Count that tick
else:        WaterExposure = max(0, WaterExposure - 1)   // recovers once they leave
```
A population can wade in to drink for a while, but past `WaterSurvivalThreshold = 15` ticks it
starts drowning at `WaterExposureMortality = 0.12`/tick (both constants in `World.cs`) — roughly
half a season of grace before attrition sets in, and death within a few more ticks if they don't
leave. `WaterExposure` carries through migration forks and is blended (count-weighted) on merge,
same as `SizeIndex`.

Resource satisfaction alone would never rescue a population from this — a River tile can have
abundant food and water, so satisfaction reads 1.0 the entire time it's drowning. `Migrate()`
therefore checks water exposure *before* the normal satisfaction check: once `WaterExposure >=
WaterFleeThreshold = 10` (a 5-tick buffer before drowning actually starts), the whole population
evacuates to the best non-River neighbor (`BestNeighborAwayFromWater` — most combined food+water,
cheapest terrain as tiebreak), ignoring `MigrationThreshold` entirely. If every neighbor is also
River (mid-channel), there's nowhere to flee that tick and it falls through to drowning; it
retries the escape every subsequent tick.

---

## Seasons

Four seasons, 25 ticks each, cycling indefinitely. Stored in `WorldState.CurrentSeason` and
`WorldState.SeasonTick`. Advancing in `AdvanceSeason()` after each tick.

| Season | Food mult | Water mult |
|--------|-----------|------------|
| Spring | 1.3×      | 1.4×       |
| Summer | 1.0×      | 0.5×       |
| Autumn | 0.8×      | 1.0×       |
| Winter | 0.3×      | 0.2×       |

Winter is the primary population pressure event. A species that overexpanded in Summer will
face starvation in Winter. Water-dependent species (Triceratops, Alamosaurus) face additional
stress from nearly-frozen water sources.

`World.TicksPerSeason = 25` is a public constant so the renderer can compute the current year
(`tick / (TicksPerSeason × 4) + 1`).

---

## Populations & species

`SpeciesDefinition` is immutable shared data. `Population` is a live, mutable group on one tile.

**Growth — three-zone model with fractional accumulators:**
```csharp
if (satisfaction >= 0.85f)                          // abundance zone
    ReproductionAccumulator += count × ReproductionRate
    births = (int)ReproductionAccumulator           // whole individuals only
    ReproductionAccumulator -= births
    count += births
    StarvationAccumulator = 0f
else if (satisfaction <= 0.50f)                     // starvation zone
    StarvationAccumulator += count × StarvationRate × (1 - satisfaction)
    deaths = min(count, (int)StarvationAccumulator)
    StarvationAccumulator -= deaths
    count -= deaths
    ReproductionAccumulator = 0f
// neutral zone [0.50, 0.85): neither grow nor shrink; both accumulators cleared
```
Births and starvation deaths **bank fractionally** across ticks and only apply a whole individual
once the running total crosses 1 — a slow reproducer or lightly-starved pop changes at its true rate
instead of being rounded up to ±1 every tick, while a `Count=1` pop still grows/dies after enough
sustained ticks (no single-individual limbo). Growth debt is cleared whenever a pop leaves the growth
zone, so a brief spell of abundance doesn't bank a birth that lands ticks later during scarcity.
Predation deaths use the same shape (`PredationAccumulator`, see the predation section).

The neutral zone lets species that accept "adequate" food (ease 3/5) stabilize instead of being
perpetually starved. An accepted-food species can hold a tile even when better food is scarce.

Dead populations (`Count = 0`) **stay on their tile** forever. They're rendered as `[EXTINCT]`
and skipped by all simulation logic. Removing them would erase run history.

`LastSatisfaction` is set to `0f` for dead populations (not the default `1f`) — otherwise a
Count=0 pop would appear to have 100% satisfaction.

`SpeciesDefinition.EaseOfEating` (0–5 scale keyed by FoodSubtype; empty dict = generalist at
full ease) governs which food pools a population can eat and how readily. Land demo species:
Triceratops (Graze 5, Browse 3, Fruit 1), Parasaurolophus (Browse 5, Graze 3, Fruit 2),
Alamosaurus (Fruit 5, Browse 2). Marine demo species: Mosasaurus (Fish 4, Shrimp 3, Crustacean 2),
Plesiosaur (Fish 5, Squid 3). Kronosaurus has no EaseOfEating (pure predator). See `docs/food-types.md`.

---

## Byproducts & fertilizer

`ByproductPool` per tile per type. `Tile.GetOrAddByproduct()` lazily creates pools.

Each tick, per living individual:
```
tile.byproductPool[type].Amount += count × species.ByproductRates[type]
```

Then decay:
```
pool.Amount = max(0, pool.Amount × (1 - DecayRate))   // DecayRate = 0.10
pool.Amount = min(pool.Amount, Capacity)               // Capacity  = 200
```

Demo species byproduct rates (Fertilizer):
- Triceratops: 0.08/individual/tick
- Alamosaurus: 0.20/individual/tick (keystone producer)
- Parasaurolophus: 0.06/individual/tick

Fertilizer bonus on food regen: `fertAmount × 0.02` added to effective regen per tick, applied to
all `ResourceType.Food` pools, not Water. At max capacity (200 units), that's
+4 food/tick on top of terrain base — meaningful for large River/Forest tiles with resident
herbivore populations.

---

## Migration

Two independent triggers, checked in order in `Migrate()`:

1. **Flee from water** — `tile.Terrain == River && pop.WaterExposure >= WaterFleeThreshold`.
   Overrides `MigrationThreshold` entirely (a population reads fully satisfied while drowning, so
   satisfaction can never trigger this on its own). See [Water exposure](#terrain). Evacuates the
   whole population to `BestNeighborAwayFromWater`, not just the excess.
2. **Resource scarcity** — `pop.LastSatisfaction < pop.Species.MigrationThreshold`. Only runs if
   the water-flee check didn't already move (or fail to move) the population this tick.

Process for the resource-scarcity path (in `Migrate()`):
1. Find the most-lacking need (`MostLackingNeed`) — Food, Water, or Prey, whichever has the
   worst supply/demand ratio. Food supply = `EffectiveFoodValue` (ease-weighted sum across
   food pools). Prey supply = `EffectivePreyAmount` (preferred prey at full weight, accepted at 2/3).
2. Find best destination: `BestNeighborForFood`, `BestNeighborForWater`, or `BestNeighborForPrey`
   depending on which need is lacking — all are thin wrappers over `BestNeighborByValue`. All
   searches respect the biome barrier (`IsOcean()` filter).
3. Collect all moves without applying
4. Apply all moves; merge into existing same-species same-faction pop if present, blending
   evolved traits weighted by count

`BestNeighborByValue` (parameterized on a `Tile -> float` value function):
- **Primary**: immediate neighbor with strictly more value, prefer lower migration cost as
  tiebreaker; biome barrier enforced
- **BFS fallback**: when no immediate neighbor has more (e.g. population in a resource desert),
  BFS up to 6 tiles deep, returns the *first step* toward the nearest tile with more

`SustainableFoodCount`/`SustainableWaterCount`/`SustainablePreyCount` decide how many individuals
a tile can sustain (only the excess migrates) — Food sums ease-weighted regen across all food
pools, Water uses plain pool regen, Prey uses current `EffectivePreyAmount` as supply proxy.

**Merge blending:**
```csharp
existing.SizeIndex     = (existing.SizeIndex     × existing.Count + pop.SizeIndex     × pop.Count) / total
existing.ImmunityDelta = (existing.ImmunityDelta × existing.Count + pop.ImmunityDelta × pop.Count) / total
existing.SizePressure  = (existing.SizePressure  × existing.Count + pop.SizePressure  × pop.Count) / total
existing.WaterExposure = (existing.WaterExposure × existing.Count + pop.WaterExposure × pop.Count) / total
```

Two populations of different factions (even same species) never merge.

---

## Disease

Player triggers with `TriggerDiseaseCommand`. Sets `Disease` and `InfectionLevel = 0.3f` on a
target population.

`Disease` blueprint fields: `Name`, `SpreadRate`, `MortalityRate`, `RecoveryRate`.

Demo disease (DinoFever): `MortalityRate=0.04`, `SpreadRate=0.18`, `RecoveryRate=0.015`.

**Spread (two-phase):**

Phase 1 — collect exposures:
```
exposure = infectionLevel × SpreadRate × densityBonus × (1 - target.EffectiveImmunity)
densityBonus = 1 + count / 500
adjacent tile factor = 0.3×
```

Phase 2 — apply:
```
pop.InfectionLevel = min(1, InfectionLevel + exposure)
```

Phase 3 — mortality + recovery:
```
deaths = ceil(count × InfectionLevel × MortalityRate × (1 - immunity))
recovery = RecoveryRate + immunity × 0.05
InfectionLevel = max(0, InfectionLevel - recovery)
if InfectionLevel == 0: pop.Disease = null
```

A species with base immunity 0.55 (Parasaurolophus) is very difficult to kill with DinoFever.
Alamosaurus (immunity 0.15) is most vulnerable.

---

## Trade

Set with `EstablishTradeCommand` (player action), cleared with `BreakTradeCommand` or
automatically when factions go to war.

Each tick, for each active trade pair:
1. Find the closest tile pair between the two factions
2. For each byproduct type present on either tile: transfer 15% of the imbalance toward
   equalization
3. Apply −0.04 tension per tick (trade actively reduces diplomatic friction)

```csharp
diff     = aPool.Amount - bPool.Amount
transfer = diff × 0.15
aPool.Amount -= transfer
bPool.Amount += transfer
```

War breaks trade immediately via `SyncRelation` when state transitions to `AtWar`.

---

## Diplomacy & combat

**Tension model:**

Each tick, for every in-range faction pair:
```
delta  = aggressionFactor × proximityFactor × 0.10   (base pressure)
delta += ResourceCompetitionPressure()                 (shared scarce resource stress)
delta -= 0.03                                          (peace drift when not at war)

if at war: delta -= 0.15 after 20 ticks of conflict   (ceasefire pressure)
```

`ResourceCompetitionPressure` — "shared resources" means both factions' primary species have
`FoodConsumptionRate > 0` and/or both have `WaterConsumptionRate > 0` (0, 1, or 2 shared):
- No shared resources: −0.08 (complementary niches → cooperation)
- Shared resources, either starving (sat < 0.5): +0.10
- Shared resources, neither starving: +0.01

Tension states: `< −0.5` Allied | `< 0.5` Neutral | `< 1.5` Tense | `≥ 1.5` AtWar

Out of range (> 5 tiles): tension decays toward 0 at 0.10/tick without overshooting.

**Combat (per tile):**
```csharp
damage = ceil(attacker.Count × attacker.EffectiveCombatStrength × 0.02)
```
Simultaneous resolution: all casualties computed before any are applied.

`EffectiveCombatStrength = CombatStrength × √SizeIndex`

The square root on SizeIndex means a pop twice as large (SizeIndex=2) is only ~41% stronger in
combat, not 100% — size is advantageous but not dominant.

---

## Evolution

Two independent accumulators. Both use pressure thresholds to produce discrete trait shifts
rather than continuous drift.

**Size:**

| Condition | Effect |
|-----------|--------|
| sat ≥ 0.90 | SizePressure++ |
| sat < 0.50 | SizePressure-- |
| SizePressure ≥ 50 | SizeIndex += 0.05; pressure = 0 |
| SizePressure ≤ −50 | SizeIndex -= 0.05; pressure = 0 |

SizeIndex range: [0.5, 2.0]. Affects food consumption (`EffectiveFoodDemand = FoodConsumptionRate
× SizeIndex`; water is unaffected — `EffectiveWaterDemand = WaterConsumptionRate`), combat, and
migration cost (indirectly, through resource consumption pressure).

**Immunity:**

| Condition | Effect |
|-----------|--------|
| Disease present + InfectionLevel > 0.1 | ImmunityPressure++ |
| ImmunityPressure ≥ 30 | ImmunityDelta += 0.02; pressure = 0 |

ImmunityDelta cap: 0.5 (can't gain more than 50% immunity above species baseline).
ImmunityDelta never decreases — immunity gained through disease survival is permanent.

`EffectiveImmunity = min(1.0, Species.Immunity + ImmunityDelta)`

---

## Speciation

> Full mechanics in **`docs/speciation.md`**. Summary below.

Runs in `ApplySpeciation()` after `ApplyEvolution()` each tick. Evaluates every living population:

- `SizeIndex >= 1.5` → forks to "Greater [Root]" (then "Giant" on a second crossing)
- `SizeIndex <= 0.65` → forks to "Lesser [Root]" (then "Dwarf")

When speciation fires, `CreateDerivedSpecies()` builds a new `SpeciesDefinition` with traits
baked in at the evolved size (`FoodConsumptionRate × sizeIndex`, `WaterConsumptionRate` unchanged,
combat `× √sizeIndex`, reproduction `÷ √sizeIndex`, byproducts `× sizeIndex`, immunity `+
ImmunityDelta`). `EaseOfEating` is copied unchanged — evolving bigger or smaller doesn't change
what a species can physically eat. The population then resets to `SizeIndex = 1.0` with pressure
accumulators zeroed.

Two populations speciating to the same name in the same tick share one definition (via
`FindSpecies()` scan before creating a new one).

`SpeciesDefinition.RootName` anchors the lineage name. Must be set explicitly in WorldSeeder for
base species; propagated automatically to derived species.

---

## Player commands

All implement `IWorldCommand` with a single `Execute(WorldState)` method.

| Command | Effect |
|---------|--------|
| `TriggerDiseaseCommand` | Sets disease + 0.3 infection on populations of the target tile |
| `EstablishTradeCommand` | Sets `HasTradeAgreement = true` on both faction relations |
| `BreakTradeCommand` | Sets `HasTradeAgreement = false` on both faction relations |

Adding a new intervention = add a class implementing `IWorldCommand`, no changes to `World`.

---

## Demo species (WorldSeeder)

| Species | Food | Water | Repro | Starv | Aggression | Combat | Immunity | Ease (G/B/C) | Notes |
|---------|------|-------|-------|-------|------------|--------|---------|--------------|-------|
| Triceratops | 2/ind | 0.5/ind | 5% | 5% | 0.3 | 1.4 | 0.30 | 5/3/1 | water-dependent, strong, grazer |
| Alamosaurus | 5/ind | 1/ind | 3% | 3% | 0.1 | 0.6 | 0.15 | 0/2/5 | keystone fertilizer, very vulnerable to disease, treetop browser |
| Parasaurolophus | 1/ind | — | 8% | 6% | 0.5 | 0.9 | 0.55 | 3/5/2 | food only, aggressive, disease-resistant, mid-height browser |

Ease-of-eating (0-5 scale, from the readme's table) governs how readily each species draws from
Ground/Brush/Canopy — see `docs/food-types.md`.

---

## Testing notes

- Tests create `new World()` directly. The 10×10 map starts empty (no resources, no terrain
  variation beyond default Plains). Tests add exactly what they need.
- `AbundantFood()` / `EmptyFood()` create a single `Ground` pool — the default test species don't
  set `EaseOfEating`, so it defaults to 5 (generalist) across all three strata and Ground acts as
  the sole food source. `AbundantFood()` uses `RegenPerTick = 500f` and `Amount = 10_000f` —
  effectively unlimited. Don't use it if you're testing regen amounts (seasons will scale it).
- Density drain (`1.15^(count/5)`) inflates demand at higher counts — tests asserting an exact
  satisfaction value should use small counts (≤10) or a correspondingly large pool so the
  multiplier stays negligible.
- Faction tests use `ReproductionRate = 0, StarvationRate = 0` to freeze population so combat
  math is predictable.
- Disease tests pass `spread: 0f` to isolate single-population infection without spread.
- The `Tick_ResourcePoolReplenishesEachTick` test uses `Assert.True(amount > 0)` rather than
  checking the exact value because seasons multiply the base regen.
