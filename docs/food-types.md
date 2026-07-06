# Food Types & Ease of Eating

How food is split into subtypes per terrain, how species differ in what they eat, and how that
drives consumption, migration, and terrain.

---

## Why

A single "Food" resource meant every herbivore competed for the exact same pool. In practice a
low-slung grazer and a treetop browser aren't competing — they're eating different parts of the
same landscape. Splitting food into typed pools lets species specialize and lets terrain express
*what kind* of food it offers, not just how much.

---

## FoodSubtype enum

`ResourceType` has three values: `Food`, `Water`, and `Prey`. Every food pool has a `FoodSubtype`
tag indicating what it contains. `FoodSubtype` values:

**Land:** `Graze` · `Browse` · `Fruit` · `Roots`

**Ocean:** `Fish` · `Shrimp` · `Crustacean` · `Squid` · `Whale`

Only food pools carry a subtype; `Water` and `Prey` pools do not.

---

## Ease of eating

`SpeciesDefinition.EaseOfEating` is a `Dictionary<FoodSubtype, float>` on a **0–5 scale** (0 = can't
eat it, 5 = trivial). Internally it is normalized to 0–1 by dividing by 5. A subtype left unset
defaults to **0** — which is how specialist species work: only list what they eat, absent entries
are inedible. A species with an *empty* `EaseOfEating` dict is a **generalist** that treats all
pools equally at full ease.

```csharp
// specialist: only Graze at full ease; Browse/Fruit not listed → ease 0
EaseOfEating = { [FoodSubtype.Graze] = 5f }

// generalist: empty dict → EffectiveEase returns 1f for any subtype
EaseOfEating = {}
```

`SpeciesDefinition.EffectiveEase(FoodSubtype?)` normalizes and handles null:
- `null` → `0f` (pool has no subtype)
- empty dict → `1f` (generalist)
- subtype in dict → `ease / 5f`
- subtype not in dict → `0f`

**Ease is a hard gate.** A pool at ease 0 is never consumed, regardless of how much sits on the
tile. This differs from the old FoodPreferences/AcceptedFoods binary model — ease on a 0–5 scale
means a species that can *technically* eat something (ease 1–2) will migrate away from it more
readily than one with ease 5, because the weighted food value is lower.

---

## Land species diet table

| Species | Graze | Browse | Fruit | Roots | Notes |
|---------|-------|--------|-------|-------|-------|
| Triceratops | 5 | 3 | 1 | — | low-slung grazer |
| Parasaurolophus | 3 | 5 | 2 | — | mid-height browser |
| Alamosaurus | — | 2 | 5 | — | treetop browser |

("—" = absent from dict → ease 0)

## Marine species diet table

| Species | Fish | Shrimp | Crustacean | Squid | Notes |
|---------|------|--------|------------|-------|-------|
| Mosasaurus | 4 | 3 | 2 | — | ambush hunter, shallow water |
| Plesiosaur | 5 | — | — | 3 | pursuit hunter, shallow + deep |
| Xiphactinus | 3 | 4 | 2 | — | shallow predator; also hunts Mosasaurus (prefers SmallMarine), pinned to ShallowOcean |

Kronosaurus has no `EaseOfEating` — it's a pure predator (`PreyConsumptionRate`), not a food consumer.
Megalodon is pinned to DeepOcean and Xiphactinus to ShallowOcean (`AllowedTerrains`), which is why the
shallow strip needed its own hunter — the deep apexes cannot reach the Mosasaurus nursery.

---

## Consumption: `World.DistributeFood`

A population has one aggregate `FoodConsumptionRate` (scaled by `SizeIndex`) and the density
drain multiplier. The split across subtypes happens dynamically:

```
weight[pool]  = species.EffectiveEase(pool.FoodSubtype) × pool.Amount
demand[pool]  = totalDemand × weight[pool] / Σ weight
```

Demand gravitates toward pools that are both easy to eat AND actually present. If collectively
over-requested, each pool's grant is scaled proportionally before any pool is drawn down — order
of processing doesn't affect the result.

If a species' most-preferred pools run dry, demand spills onto other pools with ease > 0.
Pools with ease 0 are never drawn from regardless of fullness.

`LastSatisfaction` is the **minimum** of food and water satisfaction. One scarce resource tanks
total satisfaction even if the other is plentiful.

---

## Terrain food composition

Each terrain's total food budget (regen/capacity) is split across its food subtypes via
`TerrainStats.FoodComposition` — min/max percentage ranges sampled independently per tile at
world-seed time, then normalized to sum to 100%:

### Land terrains

| Terrain  | Graze    | Browse   | Fruit   | Roots   | Fish   |
|----------|----------|----------|---------|---------|--------|
| Plains   | 60-75%   | 20-35%   | 0-5%    | —       | —      |
| Forest   | 12-20%   | 12-20%   | 60-75%  | —       | —      |
| Swamp    | 0-10%    | 40-55%   | —       | 40-55%  | —      |
| Desert   | 5-15%    | 65-85%   | 0-10%   | —       | —      |
| Highland | 25-40%   | 55-70%   | 0-5%    | —       | —      |
| River    | 80-100%  | 5-10%    | —       | —       | 0-10%  |

### Ocean terrains

| Terrain      | Fish   | Shrimp  | Crustacean | Squid  | Whale  |
|--------------|--------|---------|------------|--------|--------|
| ShallowOcean | 30-40% | 40-55%  | 15-25%     | —      | —      |
| DeepOcean    | 20-30% | —       | —          | 50-65% | 10-20% |

Per-terrain food budgets:

| Terrain      | Regen/tick | Capacity |
|--------------|-----------|----------|
| Plains       | 10        | 200      |
| Forest       | 15        | 300      |
| Swamp        | 7         | 140      |
| Desert       | 3         | 60       |
| Highland     | 8         | 160      |
| River        | 12        | 240      |
| ShallowOcean | 20        | 400      |
| DeepOcean    | 15        | 300      |

Ocean tiles carry no Water pool (not fresh water). All land tiles have a Water pool scaled as a
percentage of River's regen (15/tick, 200 cap).

---

## Migration: food value

Migration toward food uses the same ease-weighted model. `EffectiveFoodValue(tile, species)` sums
across all food pools: `Σ (pool.Amount × EffectiveEase(pool.FoodSubtype))`. A tile full of food
a species can't eat scores 0 — migration sees through raw pool size to actual usability. This is
why a Graze-specialist migrates past a Fruit-stacked Forest tile toward a smaller Graze pool on
the next Plains tile.

---

## Testing

Tests that don't care about diet specialization use `AbundantFood()`/`EmptyFood()` — generic
`ResourceType.Food` pools tagged `FoodSubtype.Graze`. Since `BasicSpecies()` has an empty
`EaseOfEating` dict, it counts as a generalist and treats any subtype with full ease. Tests that
exercise diet specialization explicitly set `EaseOfEating` with FoodSubtype keys and add pools of
specific subtypes — see `Tick_SpeciesCannotEatFoodItHasNoEaseFor` and
`Tick_MigratesTowardEasierFoodOverMoreAbundantHarderFood`.
