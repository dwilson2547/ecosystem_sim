# Food Type Diversity

This document describes the typed food subtype system added in the `feature/food-diversity` branch.

---

## Overview

Previously all food was a single generic pool on each tile. The typed food system assigns specific
food subtypes to each terrain type, and each species declares which subtypes it actively seeks
(preferred) and which it will eat if hungry (accepted). This drives more realistic migration,
competition, and diplomacy — a Triceratops won't compete with a Plesiosaur for food.

---

## FoodSubtype enum

```
// Terrestrial
Graze       — grass / ground plants   Plains, Desert, River
Browse      — leaves / shrubs         Forest, Highland, River
Fruit       — berries / canopy fruit  Forest, Swamp
Roots       — tubers / fungi          Swamp

// Marine
Fish        — schooling fish          River (freshwater), ShallowOcean, DeepOcean
Shrimp      — krill / small shrimp   ShallowOcean
Crustacean  — crabs / lobster        ShallowOcean
Squid       — cephalopods            DeepOcean
Whale       — large marine prey      DeepOcean
```

---

## Terrain → food mapping

Each terrain tile has one or more typed food pools. The `ResourcePool.FoodSubtype` property
identifies the subtype; `null` means a legacy generic pool (only ever used by tests).

| Terrain      | Food pools (regen/tick, cap)                                  |
|--------------|---------------------------------------------------------------|
| Plains       | Graze (10, 200)                                               |
| Forest       | Browse (12, 240), Fruit (4, 80)                               |
| Swamp        | Roots (5, 100), Fruit (3, 60)                                 |
| Desert       | Graze (2, 40)                                                 |
| Highland     | Browse (8, 160)                                               |
| River        | Graze (8, 160), Browse (6, 120), Fish (4, 80)                 |
| ShallowOcean | Fish (10, 200), Shrimp (15, 300), Crustacean (8, 160)         |
| DeepOcean    | Fish (5, 100), Squid (12, 240), Whale (3, 60)                 |

---

## Species food preferences

`SpeciesDefinition` gains two new fields:

```csharp
HashSet<FoodSubtype> FoodPreferences  // eats at full (1.0) satisfaction
HashSet<FoodSubtype> AcceptedFoods    // eats at 2/3 satisfaction when preferred food scarce
```

An empty `FoodPreferences` (the default, backward-compat) means the species eats any food pool
at full satisfaction — existing test species are unaffected.

### Demo species food preferences

| Species         | Prefers                   | Accepts                 |
|-----------------|---------------------------|-------------------------|
| Triceratops     | Graze                     | Browse, Roots           |
| Brachiosaurus   | Browse                    | Fruit, Graze            |
| Pachycephalosaurus | Fruit, Roots           | Graze, Browse           |
| Mosasaurus      | Shrimp, Crustacean        | Fish                    |
| Plesiosaur      | Whale, Squid              | Fish                    |

---

## Distribution algorithm (`DistributeFood`)

Food distribution uses a two-pass algorithm:

**Pass 1 — preferred food (full satisfaction value)**
For each food pool, all species that either have no preferences (generic eaters) or explicitly
prefer this subtype compete for it. Satisfaction credit = 100% of food received.

**Pass 2 — accepted food (2/3 satisfaction value)**
For each typed pool, species with preferences that did NOT get their preferred food but list this
subtype in `AcceptedFoods` compete for remaining supply. Satisfaction credit = 66% of food received.

A species with preferences that cannot eat any food on the tile receives 0 satisfaction and will
migrate or starve.

---

## Migration changes

- `BestNeighborFor` is now food-preference-aware: it computes `EffectiveFoodAmount` (sum of
  preferred food at full value + accepted food at 2/3 value) when comparing food across tiles.
- `SustainableCount` uses `EffectiveFoodRegen` (same weighting applied to regen rates × season
  multiplier) to determine how many individuals a tile can support across all consumed resources.
- **Ocean biome barrier**: `TerrainStats.SameBiome(a, b)` returns false when one terrain is ocean
  and the other is land. Land species never migrate to ocean tiles and vice versa.

---

## Ocean biomes (new terrain types)

`ShallowOcean` and `DeepOcean` were added to `TerrainType`. Migration costs:
- ShallowOcean: 1.0× (same as Plains)
- DeepOcean: 1.2× (slightly harder to navigate)

Marine species cannot migrate to land tiles and land species cannot migrate to ocean tiles.
This is enforced in `BestNeighborFor` and its BFS fallback via `TerrainStats.SameBiome`.

---

## Map expansion: 16×10

The demo world expanded from 10×10 to 16×10. Six new columns (x=10–15) form an ocean region
with a natural coastline. The terrain string for each row is 16 characters:

```
y=0: HHHPPPDDDPCCOOOO
y=1: HHPPPDDDPPCCOOOO
y=2: HFFFPPPDPCCOOOOO
y=3: PFRRRPPPPCCOOOOO
y=4: PFRRRRPPCCOOOOOO
y=5: PPRRRRRPCCOOOOOO
y=6: PSSRRRPPFCCOOOOO
y=7: PSSSPPPFFCCOOOOO
y=8: DSPPPPFFFCOOOOOO
y=9: DDDPPPPPDCCOOOOO
```

`C` = ShallowOcean, `O` = DeepOcean. The coastline pushes inland in the middle rows (4–5),
forming a shallow bay.

---

## New marine species (demo world)

**Mosasaurus** ("Shallow Fleet" faction)
- Start: (9, 5) — ShallowOcean tile
- Starting count: 20
- Prefers: Shrimp, Crustacean | Accepts: Fish
- Food consumption: 4/individual/tick | Migration threshold: 0.5
- Notable: high combat strength (2.0), will contest ShallowOcean tiles

**Plesiosaur** ("Deep Dwellers" faction)
- Start: (13, 5) — DeepOcean tile
- Starting count: 12
- Prefers: Whale, Squid | Accepts: Fish
- Food consumption: 4/individual/tick | Migration threshold: 0.5
- Notable: apex predator of the deep (combat 3.0), slow reproduction

Marine species produce no Fertilizer byproduct (ocean sediment cycling not yet modeled).

---

## Backward compatibility

- Existing tests use `new World()` (10×10) with `ResourcePool { Type = ResourceType.Food }` (no
  `FoodSubtype`). These pools have `FoodSubtype = null` and species with `FoodPreferences = []`.
  The `DistributeFood` code routes to the legacy `DistributeGenericFood` path. All 54 tests pass
  unchanged.
- `CreateDerivedSpecies` (speciation) carries `FoodPreferences` and `AcceptedFoods` forward to
  derived species so evolved lineages retain their food specialization.
