# Speciation

When a population's body size diverges far enough from its species baseline, it becomes
reproductively and ecologically distinct enough to be considered a new species. This document
covers the mechanics, naming, trait inheritance, and known design boundaries.

---

## Trigger thresholds

Speciation is evaluated each tick in `ApplySpeciation()` (runs after `ApplyEvolution()`).

| Direction | SizeIndex threshold | Constant |
|-----------|--------------------|----|
| Larger    | ≥ 1.5              | `World.SpeciationLargeThreshold` |
| Smaller   | ≤ 0.65             | `World.SpeciationSmallThreshold` |

Reaching SizeIndex 1.5 from a baseline of 1.0 requires 10 pressure-threshold crossings at 50
ticks each — roughly 500 ticks (5 in-game years). SizeIndex 0.65 requires ~7 crossings (~350
ticks / 3.5 years). These timescales mean speciation is meaningful but observable within a
medium-length run.

---

## Naming tiers

Derived species names are built from the population's `EffectiveRootName` (inherited from the
ancestor species, never changes through the lineage).

| Trigger | Parent name       | Derived name         |
|---------|-------------------|----------------------|
| Large   | `[Root]`          | `Greater [Root]`     |
| Large   | `Greater [Root]`  | `Giant [Root]`       |
| Large   | `Giant [Root]`    | `Giant [Root]` (cap) |
| Small   | `[Root]`          | `Lesser [Root]`      |
| Small   | `Lesser [Root]`   | `Dwarf [Root]`       |
| Small   | `Dwarf [Root]`    | `Dwarf [Root]` (cap) |

Cross-direction speciation is also possible: a `Greater [Root]` population under sustained
scarcity can shrink and speciate to `Lesser [Root]`. This models a dramatic evolutionary
reversal — previously giant animals forced to miniaturize by famine.

If two separate populations independently evolve to the same name (e.g., two isolated groups
both become "Greater Triceratops"), they share the first-derived `SpeciesDefinition` object.
The second population joins the existing species rather than creating a duplicate definition.

---

## Trait inheritance

When a new species is derived, its `SpeciesDefinition` is created with traits baked in at the
evolved size. The population's `SizeIndex` then resets to 1.0 — the new species baseline.
This guarantees continuity: effective values at the moment of speciation are identical before
and after.

| Trait | How it scales |
|-------|---------------|
| `FoodConsumptionRate` | `× sizeIndex` |
| `WaterConsumptionRate` | unchanged |
| `EaseOfEating` | unchanged (copied) — evolving size doesn't change what a species can eat |
| `CombatStrength` | `× √sizeIndex` (matches `EffectiveCombatStrength` formula) |
| `ReproductionRate` | `÷ √sizeIndex` — larger = slower (K-strategy) |
| `StarvationRate` | unchanged |
| `ByproductRates[*]` | `× sizeIndex` — larger bodies produce more per individual |
| `Immunity` | `parent.Immunity + ImmunityDelta`, capped at 1.0 |
| `WarAggression` | unchanged |
| `MigrationThreshold` | unchanged |
| `RootName` | inherited from parent (lineage anchor) |

After speciation, the population's accumulated evolution state is reset:
```
pop.SizeIndex       = 1.0
pop.SizePressure    = 0
pop.ImmunityDelta   = 0
pop.ImmunityPressure = 0
```
Subsequent evolution is relative to the new species' baseline.

---

## What doesn't change at speciation

**`Faction.PrimarySpecies`** is not updated when a faction's populations speciate. The
faction retains its original species' `WarAggression` and `FoodConsumptionRate`/
`WaterConsumptionRate` for all diplomacy calculations (`ResourceCompetitionPressure`, aggression
factor). This is intentional — a faction's cultural character doesn't shift just because
individual animals evolved.

Consequence: two factions of "Greater Triceratops" and "Lesser Triceratops" (both derived from
base Triceratops) will have their diplomatic pressure calculated using the *base Triceratops*
stats, not the derived ones. This is acceptable in the current prototype but should be revisited
when factions gain more sophisticated identity mechanics.

**Disease**: `pop.Disease` and `pop.InfectionLevel` are not cleared at speciation. A
mid-outbreak population carries the disease into its new species form.

---

## Visual representation

The renderer assigns colors per species name. A newly speciated species gets a fresh color
from the palette (cycled from `Renderer.Palette`). The first letter of the new species name
appears on the map — "Greater Triceratops" renders as `G`, "Lesser Triceratops" as `L`.

This means a player watching the map will see the species letter change when speciation occurs,
which serves as the primary visual signal since there's no event log yet.

---

## Extending speciation

To add more triggers beyond SizeIndex (e.g., high ImmunityDelta → disease-specialist sub-species,
or prolonged isolation on a specific terrain):

1. Add the trigger condition in `ApplySpeciation()`
2. Add a naming case in `DeriveSpeciesName()` (or a separate naming function)
3. Add trait scaling in `CreateDerivedSpecies()`
4. Update this doc

The `IWorldCommand` pattern could also allow a `TriggerSpeciationCommand` for player-forced
speciation events (a meteor that creates sudden selection pressure).
