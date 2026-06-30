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
3. `ApplyGrowthAndDeath` — population change based on satisfaction
4. `ProduceByproducts` — each individual emits byproducts at species rate
5. `DecayByproducts` — byproduct pools decay 10%/tick

**Global** (after all tiles processed):
6. `Migrate` — batch-compute all moves, then apply
7. `ResolveCombat` — simultaneous at-war casualties per tile
8. `SpreadDisease` — two-phase exposure collection then application
9. `ExecuteTrade` — byproduct equalization between trading faction pairs
10. `UpdateFactionRelations` — tension delta, state transitions
11. `ApplyEvolution` — size and immunity pressure accumulators
12. `State.Tick++` then `AdvanceSeason()`

**Why this order matters:**
- Resources are distributed *before* growth so death/growth reflects current food access
- Migration happens *after* per-tile loop so a pop can't migrate and then immediately consume at
  its new tile in the same tick
- Disease exposure is collected in one pass then applied — prevents tile-loop order from making
  disease spread directionally biased
- Evolution runs last so it sees the satisfaction value from *this* tick's resource distribution

---

## Resources

`ResourcePool` fields: `Type`, `Amount`, `Capacity`, `RegenPerTick`.

Regen each tick:
```
effectiveRegen = RegenPerTick × seasonMultiplier
if Food: effectiveRegen += fertilizerAmount × 0.02   (FertilizerBoost)
pool.Amount = min(pool.Capacity, pool.Amount + effectiveRegen)
```

Distribution is proportional. When total demand > supply, each population gets
`(their demand / total demand) × available supply`. Satisfaction = `received / demanded`,
clamped per resource type, and a population's `LastSatisfaction` is the **minimum** across all
resources it needs. One scarce resource tanks full satisfaction even if others are plentiful.

---

## Terrain

Six terrain types set on `Tile.Terrain` during world creation. Never changes at runtime.

| Terrain  | Food regen | Water | Migration cost |
|----------|-----------|-------|----------------|
| Plains   | 10/tick   | —     | 1.0×           |
| Forest   | 15/tick   | —     | 1.4×           |
| Swamp    | 7/tick    | 8/tick| 1.8×           |
| Desert   | 3/tick    | —     | 0.8×           |
| Highland | 8/tick    | —     | 1.5×           |
| River    | 12/tick   | 15/tick | 1.0×         |

Migration cost is used as a **tiebreaker** in `BestNeighborFor` — when multiple neighbors have
more of the needed resource, prefer the one with lower entry cost. Resources are always the
primary driver; terrain only steers when options are similar.

**Demo world terrain map** (`WorldSeeder`):
```
     x: 0  1  2  3  4  5  6  7  8  9
y=0:    H  H  H  P  P  P  D  D  D  D
y=1:    H  H  P  P  P  D  D  D  D  D   ← Highland Tric starts at (1,1)
y=2:    H  F  F  F  P  P  P  D  D  D   ← Valley   Tric starts at (3,2)
y=3:    P  F  R  R  R  P  P  P  D  D
y=4:    P  F  R  R  R  R  P  P  P  D   ← River Brachio starts at (5,4)
y=5:    P  P  R  R  R  R  R  P  P  P
y=6:    P  S  S  R  R  R  P  P  F  P   ← Midland Pachy starts at (7,6)
y=7:    P  S  S  S  P  P  P  F  F  P
y=8:    D  S  P  P  P  P  F  F  F  D   ← Eastern Pachy starts at (8,8)
y=9:    D  D  D  P  P  P  P  P  D  D
H=Highland  F=Forest  R=River  S=Swamp  D=Desert  P=Plains
```

Triceratops start in Highland with no water — they need to migrate south to the River/Swamp
band. This creates early-game pressure and interesting BFS migration paths.

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
face starvation in Winter. Water-dependent species (Triceratops, Brachiosaurus) face additional
stress from nearly-frozen water sources.

`World.TicksPerSeason = 25` is a public constant so the renderer can compute the current year
(`tick / (TicksPerSeason × 4) + 1`).

---

## Populations & species

`SpeciesDefinition` is immutable shared data. `Population` is a live, mutable group on one tile.

**Growth:**
```csharp
if (satisfaction >= 1f)
    count += ceil(count × ReproductionRate)   // ceiling prevents single-individual limbo
else
    deaths = ceil(count × StarvationRate × (1 - satisfaction))
    count  = max(0, count - deaths)
```

Dead populations (`Count = 0`) **stay on their tile** forever. They're rendered as `[EXTINCT]`
and skipped by all simulation logic. Removing them would erase run history.

`LastSatisfaction` is set to `0f` for dead populations (not the default `1f`) — otherwise a
Count=0 pop would appear to have 100% satisfaction.

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
- Brachiosaurus: 0.20/individual/tick (keystone producer)
- Pachycephalosaurus: 0.06/individual/tick

Fertilizer bonus on food regen: `fertAmount × 0.02` added to effective regen per tick.
At max capacity (200 units), that's +4 food/tick on top of terrain base — meaningful for large
River/Forest tiles with resident herbivore populations.

---

## Migration

Trigger: `pop.LastSatisfaction < pop.Species.MigrationThreshold`.

Process (in `Migrate()`):
1. Find the most-lacking resource (`MostLackingResource`) — the resource with the worst
   supply/demand ratio
2. Find best destination (`BestNeighborFor`) for that resource type
3. Collect all moves without applying
4. Apply all moves; merge into existing same-species same-faction pop if present, blending
   evolved traits weighted by count

`BestNeighborFor`:
- **Primary**: immediate neighbor with strictly more of the resource, prefer lower migration cost
  as tiebreaker
- **BFS fallback**: when no immediate neighbor has more (e.g. population in a resource desert),
  BFS up to 6 tiles deep, returns the *first step* toward the nearest tile with more

**Merge blending:**
```csharp
existing.SizeIndex     = (existing.SizeIndex     × existing.Count + pop.SizeIndex     × pop.Count) / total
existing.ImmunityDelta = (existing.ImmunityDelta × existing.Count + pop.ImmunityDelta × pop.Count) / total
existing.SizePressure  = (existing.SizePressure  × existing.Count + pop.SizePressure  × pop.Count) / total
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

A species with base immunity 0.55 (Pachycephalosaurus) is very difficult to kill with DinoFever.
Brachiosaurus (immunity 0.15) is most vulnerable.

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

`ResourceCompetitionPressure`:
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

SizeIndex range: [0.5, 2.0]. Affects food consumption (`EffectiveConsumptionRate × SizeIndex`
for Food), combat, and migration cost (indirectly, through resource consumption pressure).

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
baked in at the evolved size (food consumption `× sizeIndex`, combat `× √sizeIndex`, reproduction
`÷ √sizeIndex`, byproducts `× sizeIndex`, immunity `+ ImmunityDelta`). The population then resets
to `SizeIndex = 1.0` with pressure accumulators zeroed.

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

| Species | Food | Water | Repro | Starv | Aggression | Combat | Immunity | Notes |
|---------|------|-------|-------|-------|------------|--------|---------|-------|
| Triceratops | 2/ind | 1/ind | 5% | 5% | 0.3 | 1.4 | 0.30 | water-dependent, strong |
| Brachiosaurus | 5/ind | 2/ind | 3% | 3% | 0.1 | 0.6 | 0.15 | keystone fertilizer, very vulnerable to disease |
| Pachycephalosaurus | 1/ind | — | 8% | 6% | 0.5 | 0.9 | 0.55 | food only, aggressive, disease-resistant |

---

## Testing notes

- Tests create `new World()` directly. The 10×10 map starts empty (no resources, no terrain
  variation beyond default Plains). Tests add exactly what they need.
- `AbundantFood()` uses `RegenPerTick = 500f` and `Amount = 10_000f` — effectively unlimited.
  Don't use it if you're testing regen amounts (seasons will scale it).
- Faction tests use `ReproductionRate = 0, StarvationRate = 0` to freeze population so combat
  math is predictable.
- Disease tests pass `spread: 0f` to isolate single-population infection without spread.
- The `Tick_ResourcePoolReplenishesEachTick` test uses `Assert.True(amount > 0)` rather than
  checking the exact value because seasons multiply the base regen.
