# Food Types & Ease of Eating

How food is split into Ground/Brush/Canopy strata, how species differ in what they can eat, and
how that feeds into consumption, migration, and terrain.

---

## Why

A single "Food" resource meant every herbivore competed for the exact same pool. In practice a
low-slung grazer and a treetop browser aren't actually competing — they're eating different
parts of the same landscape. Splitting food into strata lets species specialize, and lets terrain
express *what kind* of food it offers, not just how much.

---

## ResourceType

`ResourceType` has four values: `Ground`, `Brush`, `Canopy` (the food strata) and `Water`. Every
tile carries all three food pools (even if capacity is 0 for a stratum that terrain barely
offers) plus a `Water` pool on River/Swamp tiles only.

---

## Ease of eating

`SpeciesDefinition.EaseOfEating` is a `Dictionary<ResourceType, float>` on a 0 (can't eat it at
all) to 5 (trivial) scale — directly from the readme's table:

| Species | Ground | Brush | Canopy | Role |
|---------|--------|-------|--------|------|
| Triceratops | 5 | 3 | 1 | low-slung grazer |
| Parasaurolophus | 3 | 5 | 2 | mid-height browser |
| Alamosaurus | 0 | 2 | 5 | treetop browser |

A stratum left unset in `EaseOfEating` defaults to **5** — a species that doesn't specify a diet
is a generalist that can eat anything equally well. This keeps species (and tests) that don't
care about diet specialization working exactly as if the system didn't exist.

`SpeciesDefinition.EffectiveEase(stratum, terrain)` combines the species' base ease with a
terrain penalty (see below), clamps to `[0, 5]`, and normalizes to `[0, 1]`:
```
EffectiveEase = clamp(baseEase - TerrainStats.EaseOfEatingPenalty(terrain), 0, 5) / 5
```

**Ease is a hard gate, not a soft preference.** A stratum at ease 0 is never eaten, no matter how
much of it sits on the tile unconsumed.

---

## Consumption: `World.DistributeFood`

A population has one aggregate `FoodConsumptionRate` (scaled by `SizeIndex` — see
`Population.EffectiveFoodDemand`), not a separate rate per stratum. The split across
Ground/Brush/Canopy happens dynamically at consumption time:

```
weight[stratum]     = EffectiveEase(stratum, tile.Terrain) × pool[stratum].Amount
preferred[stratum]  = demand × weight[stratum] / Σ weight
```

This is why "favor areas with more easy-to-eat food, but factor in what's available" holds: a
tile stacked with a stratum the species is bad at scores low (ease near 0), and a stratum the
species is great at but which is nearly empty also scores low (amount near 0). Both matter.

If a population's easiest strata run dry, demand naturally spills onto harder-to-eat strata that
still have `ease > 0` — the population doesn't starve outright just because its favorite food ran
out, provided it has *some* ease for something else on the tile.

If total requested demand for a stratum (summed across every population on the tile) exceeds
what's available, each population's grant from that stratum is scaled down proportionally —
same rule as the original single-resource model, computed once before any pool is drawn down so
order-of-processing doesn't matter.

`LastSatisfaction` is the minimum of food satisfaction and water satisfaction (water is
distributed independently, unaffected by ease — see `World.DistributeWater`).

---

## Terrain composition

Each `TerrainType` has a total food regen/capacity budget (unchanged from before this system —
e.g. Forest regenerates 15/tick, Desert only 3/tick) that gets split across the three strata by
`TerrainStats.FoodComposition`: a `(FloatRange Ground, FloatRange Brush, FloatRange Canopy)` per
terrain, each range a min/max percentage (0-100).

| Terrain  | Ground   | Brush    | Canopy  | Character |
|----------|----------|----------|---------|-----------|
| Plains   | 60-75%   | 20-35%   | 0-5%    | mostly ground cover, some brush, almost no canopy |
| Forest   | 12-20%   | 12-20%   | 60-75%  | mostly canopy, much less brush and ground |
| Swamp    | 0-10%    | 40-55%   | 40-55%  | nearly no ground; roughly split brush/canopy |
| Desert   | 5-15%    | 65-85%   | 0-10%   | nearly no food at all; what little there is skews brush |
| Highland | 25-40%   | 55-70%   | 0-5%    | rich in brush, medium ground, almost no canopy |
| River    | 80-100%  | 5-10%    | 0-10%   | almost entirely ground-level (reeds/silt) |

At world-seed time (`WorldSeeder.CreateDemo()` / `DemoWorldSeeder.Create()`), each tile
independently samples a percentage within each stratum's range (`FloatRange.Sample`), then
normalizes so the three sampled values sum to 100% before splitting the terrain's regen/capacity
budget. Sampling independently rather than forcing ranges to sum to 100 upfront means a terrain
with a wide range (River's Ground 80-100) dominates while still leaving room for tile-to-tile
variety — two River tiles won't have identical compositions.

This randomization is a **world-seeding concern**, not an engine concern — `World`/`Tile` just
see whatever `ResourcePool`s they're given. Procedural map generation (on the roadmap) will use
the same `TerrainStats.FoodComposition` table.

---

## Terrain ease penalty

`TerrainStats.EaseOfEatingPenalty(terrain)` is a flat value (currently only River = 1) subtracted
from every species' base ease before normalizing, on that terrain. It represents terrain making
eating physically awkward independent of what's growing there — River is hard to graze from while
wading through it, regardless of species. A species with ease 1 on Ground has its effective ease
drop to 0 while standing in a River tile, even if the same Ground pool would be perfectly edible
one tile over on Plains.

This stacks with [water exposure](implementation.md#terrain) (the drowning/flee mechanic) — a
population on River terrain is fighting both a harder time eating *and* a ticking clock before it
has to leave.

---

## Testing

Tests that don't care about diet specialization can ignore this system entirely: leave
`EaseOfEating` unset (defaults to 5 across all strata) and add a single `Ground` pool as the food
source — see `AbundantFood()`/`EmptyFood()` in `WorldTests.cs`. Tests that exercise the ease
system directly set `EaseOfEating` explicitly and add multiple stratum pools — see
`Tick_SpeciesCannotEatFoodItHasNoEaseFor`, `Tick_MigratesTowardEasierFoodOverMoreAbundantHarderFood`,
and `Tick_RiverTerrainPenaltyCanMakeFoodInedible`.
