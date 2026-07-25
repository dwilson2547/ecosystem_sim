# Implementation Reference

Detailed mechanics for every system in the simulation. Read this when you need to understand
*why* something works the way it does, not just what the code does. For file locations and
architecture overview, see `CLAUDE.md`.

---

## Architecture

The simulation is a **pure state machine**. `World` holds `WorldState` (the only mutable data)
and exposes a small command surface:

```csharp
world.Tick();                         // advance simulation by one day
world.Apply(IWorldCommand cmd);       // general player intervention
world.StartScenario(kind);            // configure and seed a scenario
world.TryApplyScenarioAction(action); // validate a scenario intervention and its budget
```

There are no events, no callbacks, no async. The UI drives the tick rate externally. This makes
testing trivial and makes frontend integration straightforward — just call `Tick()` on a timer.

`WorldState` contains:
- `Tick` — total ticks elapsed
- `CurrentSeason`, `SeasonDay` — derived from the elapsed day count
- derived `DayOfSeason`, `DayOfYear`, and `Year` calendar values
- `Map` — the 10×10 `WorldMap` with all tiles
- `Factions` — list of all factions (including extinct ones)
- `Scenario` — current Sandbox or Challenge session, including objectives and action points
- `History` — bounded 30-day lineage/objective samples
- `Events` — bounded major ecosystem event log

---

## Tick loop order

**Per tile** (inner loop over all tiles):
1. `RegenerateResources` — pool regen scaled by season multiplier + fertilizer bonus
2. `DistributeResources` — proportional resource distribution, sets `LastSatisfaction`
3. `HuntPrey` — carnivores consume prey populations; sets predator `LastSatisfaction`
4. `ApplyPopulationChange` — seasonal cohort births plus delayed food/water deprivation mortality
5. `ApplyWaterExposure` — drowning losses for populations stranded on River terrain
6. `ProduceByproducts` — each individual emits byproducts at species rate
7. `DecayByproducts` — byproduct pools decay 10%/tick
8. `ApplyTerrainSuccession` — accumulates degradation/recovery pressure and performs eligible,
   seeded terrain transitions

**Global** (after all tiles processed):
8. `Migrate` — batch-compute all moves, then apply
9. `ResolveCombat` — simultaneous at-war casualties per tile
10. `SpreadDisease` — two-phase exposure collection then application
11. `ExecuteTrade` — byproduct equalization between trading faction pairs
12. `UpdateFactionRelations` — tension delta, state transitions
13. `ApplyEvolution` — size and immunity pressure accumulators
14. `ApplySpeciation` — fork populations that crossed size thresholds into derived species
15. `State.Tick++` (all calendar values derive from it)
16. Refresh scenario objectives and resolve an expired challenge
17. Detect major events and append a history sample every 30 days

**Why this order matters:**
- Resources are distributed before breeding/deprivation so population change reflects current access
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

Eight terrain types are set on `Tile.Terrain` during world creation and can later change through
configured ecological succession. Land and ocean are biome-separated: species cannot migrate across the
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
call, `TerrainStats.BuildResourcePools(terrain, random)` — this is also what terrain succession
calls at runtime, so a transitioned tile gets pools structurally identical to one seeded that way
from the start.

Migration cost is used as a **tiebreaker** in `BestNeighborByValue` — when multiple neighbors
have similar value, prefer the one with lower entry cost.

**Terrain succession.** `TerrainStats.Degradation` maps Forest to Plains, while
`TerrainStats.Recovery` maps Plains back to Forest. `ApplyTerrainSuccession` uses
`TerrainSuccessionSettings` for every threshold, pressure duration, chance, and decay rate.
The defaults are:
```
Forest → Plains:
  Fruit ratio < 6% for 90 pressure days; 100% daily chance once eligible

Plains → Forest:
  average vegetation ≥ 50%, fertilizer ≥ 5, and an adjacent Forest
  for 30 pressure days; 10% daily chance once eligible
```
Unhealthy days decay the relevant pressure by one rather than resetting it immediately. A
transition rebuilds the tile's resource pools, resets both pressure values, and records a located
world event. Chance rolls and rebuilt resource compositions use the world's seeded RNG, preserving
reproducible runs. Recovery requires nearby Forest, so woodland spreads from surviving seed sources
rather than appearing spontaneously.

These thresholds were relaxed from the original 10%/60 ticks. With predation alone (Tyrannosaurus +
Para curb) the land ecosystem is **bistable**: strong predation saves the forest but exterminates the
herbivore food chain, while weak predation lets Parasaurolophus reboom and raze the forest — the
sensitive trigger made "healthy forest *and* a living food chain" nearly unreachable. Letting the
forest tolerate moderate browsing (6%/90) is the piece that makes stable coexistence achievable;
across random seeds it lifted forest survival to ~92% and full coexistence to ~71%. If you re-tune
predation, keep this browsing tolerance in mind — tightening it back reintroduces the bistability.

**Demo world terrain map** (`WorldSeeder`, 16×16):
```
     x:  0  1  2  3  4  5  6  7  8  9 10 11 12 13 14 15
y=0:     H  H  F  F  F  F  F  P  D  D  A  A  B  B  B  B  ← northern forest ─┐
y=1:     H  H  F  F  F  F  F  P  D  D  A  A  B  B  B  B  ← Highland Tric at (1,1)
y=2:     H  P  F  F  F  F  F  P  D  D  A  A  B  B  B  B  ← Forest Alamo at (4,2)
y=3:     P  P  F  F  F  F  P  P  D  D  A  A  B  B  B  B  ← Valley Tric at (7,3); Mosasaurus at (10,3)
y=4:     P  P  P  F  F  R  P  P  P  D  A  A  B  B  B  B  ← northern forest ─┘  Megalodon at (14,4)
y=5:     P  P  P  R  R  R  P  P  P  D  A  A  B  B  B  B  ← Kronosaurus at (13,5)
y=6:     P  S  S  R  R  R  P  P  P  P  A  A  B  B  B  B  ← Midland Para at (7,6); Plesiosaur at (12,6)
y=7:     P  S  S  S  R  P  P  P  P  P  A  A  B  B  B  B
y=8:     D  S  S  P  P  P  P  F  F  P  A  A  B  B  B  B  ← Tyrant Pack at (6,8)
y=9:     D  D  P  P  P  P  F  F  F  D  A  A  B  B  B  B  ← Eastern Para at (7,9)
y=10:    D  D  P  P  P  P  F  F  P  D  A  A  B  B  B  B  ← southern forest
y=11:    P  P  P  P  P  P  P  P  P  P  A  A  B  B  B  B
y=12:    P  P  S  S  P  P  P  P  P  P  A  A  B  B  B  B
y=13:    P  P  S  S  P  P  P  P  P  P  A  A  B  B  B  B
y=14:    D  P  P  P  P  P  P  P  P  D  A  A  B  B  B  B
y=15:    D  D  P  P  P  P  P  P  D  D  A  A  B  B  B  B
H=Highland  F=Forest  R=River  S=Swamp  D=Desert  P=Plains
A=ShallowOcean  B=DeepOcean
```

The large northern forest (x=2-6, y=0-4) gives the Alamosaurus herd room to disperse. The Tyrant
Pack seeds in the central plains amid the herbivore range. Marine species occupy the right 6 columns
and cannot migrate onto land.

**Water exposure.** River is the one terrain that counts as *being in the water*, not just having
water nearby (Swamp has a water pool but is still walkable land). No species can live there
indefinitely:
```
if on River: WaterExposure++; if WaterExposure > 3, lose 2% of Count that day
else:        WaterExposure = max(0, WaterExposure - 1)   // recovers once they leave
```
A population attempts to flee after 2 days. Past `WaterSurvivalThreshold = 3`, gradual attrition
begins at `WaterExposureMortality = 0.02` per day. `WaterExposure` carries through migration forks
and is blended (count-weighted) on merge, same as `SizeIndex`.

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

One tick is one day. Four 90-day seasons form a 360-day year and cycle indefinitely. Calendar state
is derived from `WorldState.Tick`, with one-based `DayOfSeason`, `DayOfYear`, and `Year` values
exposed for frontends. The elapsed day count is the single calendar source of truth.

| Season | Food mult | Water mult |
|--------|-----------|------------|
| Spring | 1.3×      | 1.4×       |
| Summer | 1.0×      | 0.5×       |
| Autumn | 0.8×      | 1.0×       |
| Winter | 0.3×      | 0.2×       |

Winter is the primary population pressure event. A species that overexpanded in Summer will
face starvation in Winter. Water-dependent species (Triceratops, Alamosaurus) face additional
stress from nearly-frozen water sources.

`World.DaysPerSeason = 90` and `World.DaysPerYear = 360` are public constants.

---

## Populations & species

`SpeciesDefinition` is immutable shared data. `Population` is a live, mutable group on one tile.

**Seasonal breeding.** Species define `BreedingSeasons`, `BreedingDayOfSeason`, and a
`BreedingRate` (the fraction of the current population added in that cohort). A cohort is produced
only on the configured day and only when overall satisfaction is at least 0.85:
```csharp
if (isBreedingDay && satisfaction >= 0.85f)
    ReproductionAccumulator += count × BreedingRate
    births = (int)ReproductionAccumulator
    ReproductionAccumulator -= births
    count += births
```
Fractional cohort remainders carry into the species' next breeding event, so small populations can
still reproduce without being rounded up every day.

**Deprivation mortality.** Nutrition and water satisfaction are tracked independently. Pressure
only accumulates when a required resource is effectively exhausted (≤5% satisfaction), not merely
scarce. `FoodDeprivationToleranceDays` and `WaterDeprivationToleranceDays` give populations time to
migrate. Once a tolerance is exceeded, fractional deaths accumulate at the corresponding daily
mortality rate. Any meaningful supply causes pressure to recover and clears pending mortality debt.
Predation remains immediate and uses its separate `PredationAccumulator`.

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
- **In-view pick**: one BFS gathers every tile within `species.ViewRadius` (clamped to [1, 6])
  along with the first step toward it, then commits toward the tile with strictly more value than
  current — the *richest* one, prefer lower migration cost then nearer as tiebreak; biome barrier
  enforced. At `ViewRadius == 1` this is exactly the old immediate-neighbour pick; at ≥2 the
  species can skip a merely-adequate adjacent tile for a far richer patch two/three layers out
  (avoids a local optimum). Set per species — Alamosaurus 3; Triceratops, Megalodon, Plesiosaur,
  Kronosaurus 2; everyone else 1.
- **BFS fallback**: when nothing within view has more (e.g. population in a resource desert), the
  same BFS continues to 6 tiles deep and returns the *first step* toward the nearest tile with more

`SustainableFoodCount`/`SustainableWaterCount`/`SustainablePreyCount` decide how many individuals
a tile can sustain (only the excess migrates) — Food sums ease-weighted regen across all food
pools, Water uses plain pool regen, Prey uses current `EffectivePreyAmount` as supply proxy.

Predators with `SpeciesDefinition.PursuesPreyWhenFed` bypass the normal satisfaction gate when a
richer prey tile is visible. Megalodon enables this trait so its singleton patrols DeepOcean while
still using fish/squid/whale as a survival floor. Its prey demand is deliberately low (`0.002`) so
the patrol exerts pressure without driving Plesiosaur or Mosasaurus extinct.

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

## Scenarios and interventions

`ScenarioFactory.Start()` configures a session without adding UI dependencies to the engine.
Sandbox has unlimited time and free actions. Locust Plague lasts `3 × World.DaysPerYear`, starts
with 10 action points, and seeds 12 Plains tiles with 42 locusts each while reducing Plains Graze
to at most 30% capacity. Drought Recovery lasts `2 × World.DaysPerYear`, starts with 10 points,
forces persistent drought weather, caps land freshwater at 10%, and caps land vegetation at 20%.

| Objective | Target |
|-----------|--------|
| Dinosaur survival | Triceratops, Alamosaurus, Parasaurolophus, and Tyrannosaurus all remain alive |
| Locust control | Total living locust population ≤ 500 |
| Plains recovery | Average Plains Graze amount/capacity ≥ 25% |

Drought Recovery uses dinosaur survival plus average land freshwater ≥55% and average land
vegetation ≥50%. A seeded rain spell temporarily overrides the persistent drought; when it ends,
the challenge restores drought weather instead of returning to stochastic weather.

`IScenarioAction` exposes `Name`, `Cost`, `CanExecute(WorldState, out error)`, and
`Execute(WorldState)`. `World.TryApplyScenarioAction()` rejects missing or inactive scenarios,
actions unsupported by the active scenario, invalid targets, completed challenges, and insufficient
budgets before spending points.

| Action | Cost | Effect |
|--------|------|--------|
| `CullLocustsAction` | 1 AP | Removes 99% of locusts within two hexes of the selected tile |
| `RestoreGrassAction` | 2 AP | Refills Plains Graze pools within four hexes |
| `SeedMeganeuraAction` | 3 AP | Adds five Meganeura to a selected dry-land tile |
| `CreateWateringHolesAction` | 2 AP | Refills or creates freshwater pools within three hexes |
| `RestoreVegetationAction` | 2 AP | Refills land food pools within three hexes |
| `SeedRainAction` | 3 AP | Replaces drought with 45 days of global rainy weather |

Objective progress refreshes after every tick and successful action. Challenge status becomes
`Won` only if all objectives are met when its duration expires; otherwise it becomes `Lost`.

---

## History and major events

`WorldState.History` stores up to 120 `WorldHistorySample` records, sampled every
`World.HistoryIntervalDays` (30 days). Each sample contains total population by lineage root plus
normalized objective progress, so speciation remains folded into the parent lineage and challenge
trends always use "higher is better."

`WorldState.Events` stores up to 200 `WorldEvent` records with tick, severity, message, and optional
tile coordinates. `World.Tick()` detects extinctions, ≥25%/10-head population swings, migration
waves, first disease outbreaks, weather transitions, and challenge results. Successful scenario
actions add located events immediately. Old records are removed from the front of each bounded list.

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
| Triceratops | 2/ind | 0.5/ind | 1.5% | 1.5% | 0.3 | 1.4 | 0.30 | 5/3/1 | grazer; prey=LargeHerbivore (T-Rex accepts) |
| Alamosaurus | 5/ind | 1/ind | 0.8% | 0.8% | 0.1 | 0.6 | 0.15 | 0/2/5 | keystone fertilizer, very vulnerable to disease, treetop browser |
| Parasaurolophus | 1/ind | — | 1.4% | 1.5% | 0.5 | 0.9 | 0.55 | 3/5/2 | mid-height browser; repro curbed + MaxCount 45; prey=SmallHerbivore (T-Rex preferred) |
| Tyrannosaurus | prey-only | — | 1.5% | 1.2% | 0.4 | 4.0 | 0.40 | — | land apex; obligate carnivore, hunts Para (preferred) + Trike (accepted), ViewRadius 2 |

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
- Faction tests disable breeding and deprivation mortality to freeze population so combat
  math is predictable.
- Disease tests pass `spread: 0f` to isolate single-population infection without spread.
- The `Tick_ResourcePoolReplenishesEachTick` test uses `Assert.True(amount > 0)` rather than
  checking the exact value because seasons multiply the base regen.
