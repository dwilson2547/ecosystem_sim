# Systems Design

Living document capturing intended game systems and the reasoning behind them.
Update this as ideas evolve — the goal is to record *why*, not just *what*.

---

## Core philosophy

The simulation runs autonomously once parameters are set. The player is a god-figure:
they shape the world, seed species, trigger events, and watch consequences unfold.
Every system should produce *emergent* behavior — interesting outcomes that weren't
explicitly scripted.

---

## 1. Resources

Resources are the foundation everything else depends on.

- Each tile on the map has its own resource pools (food, water, etc.)
- Pools regen each tick up to a capacity ceiling
- Populations on a tile share that tile's resources proportionally when scarce
- Resource pressure is the primary driver of population dynamics

**Extensibility:** `ResourceType` is an enum — adding a new resource type (e.g. `Minerals`,
`Shelter`) is one line. Species consumption rates are a dictionary keyed by `ResourceType`,
so new resources are opt-in per species.

---

## 2. Territory & Map

The world is a 2D grid of tiles. Populations live on tiles, not in a global pool.

- Geography creates natural constraints — a species can only consume what's on its tile
- Resource richness varies by tile (fertile river valley vs. arid badlands)
- Proximity between tiles is the basis for migration, disease spread, and faction relations
- **Future:** terrain types (forest, plains, swamp, volcanic), climate zones, elevation

This prevents the "one giant pile" problem — a massive population in one region can't
monopolize resources in another region without migrating there first.

---

## 3. Populations & Species

A **species** is a blueprint (traits, needs, behaviors). A **population** is a live group
of that species occupying a tile.

The same species can have multiple populations on different tiles. Those populations may
diverge over time via evolution (see §7).

---

## 4. Factions & Diplomacy

Factions are political groupings — one or more populations that act together.

**Proximity dependency (key design goal):**
A faction's influence and relationships are geographically bounded. A T-Rex faction in
the northern highlands can't diplomatically coordinate with another T-Rex faction in
the southern swamps unless they're within range. This prevents a single large species
from automatically "ganging up" across the whole map.

**Diplomacy states:**
- Neutral — no active relation
- Trade agreement — resources/byproducts exchanged each tick
- Alliance — coordinate on shared threats
- War — active conflict, population losses each tick
- Vassalage — one faction subordinate to another (stretch goal)

**Relation drivers:**
- Proximity (nearby factions interact more)
- Resource pressure (scarce shared resources → tension)
- Species traits (high-aggression species → faster escalation)
- Historical events (wars leave lasting reputation effects)

**Future:** faction memory, grudges, reputation system

---

## 5. Trade & Barter

Trade is dino-flavored — not gold or money, but tangible resource exchange.

The core idea: dinosaur byproducts have real ecological value.
- Dino waste → natural fertilizer → boosts food `RegenPerTick` on nearby tiles
- Megaherbivore grazing → clears dense vegetation → opens land for other species
- Carnivore kills → carcasses → scavenger food source on the tile

**Migratory trade behavior:**
Some species follow the byproduct trail. A herbivore might migrate toward tiles
with high fertilizer output, which in turn attracts predators. Trade routes emerge
organically from movement patterns rather than being explicitly placed.

**Barter mechanics:**
Factions in a trade agreement exchange surplus resources each tick. What counts
as "surplus" and what gets offered is driven by species traits and current resource
levels — a species sitting on excess fertilizer but lacking water would offer the
former and seek the latter.

**Future:** trade route visualization, resource caravans, black market (faction-agnostic
traders), seasonal migration paths

---

## 6. Disease

Disease is a population pressure multiplier driven by density and proximity.

- High-density populations are at greater risk (overcrowding)
- Disease spreads to adjacent tiles over time
- Each species has a base immunity stat — high-immunity species resist or become carriers
- Carnivores eating infected prey can contract disease
- Extinction risk: a disease + food collapse in the same region can cascade rapidly

**Interaction with evolution (§7):** a population that survives a disease outbreak
gains an immunity modifier. Over many generations this becomes a permanent species trait.

**Future:** named diseases with specific species vulnerabilities, player-triggered plague
events, quarantine mechanics (factions closing borders)

---

## 7. Stat-Based Evolution

Not speciation — species don't turn into other species. Instead, species traits drift
gradually in response to environmental pressure.

**Abundance path:**
Consistent food surplus → population grows larger → size stat increases → more strength
(better in conflicts) → but also higher food consumption per individual → the species
becomes fragile to food shocks.

**Scarcity path:**
Sustained food pressure → smaller body size → less food needed → more resilient to
shocks but weaker in conflicts.

**Stats that can evolve:**
- `Size` — affects strength and food consumption
- `Speed` — affects migration range and combat evasion
- `Immunity` — resistance to disease
- `Intelligence` — affects diplomacy effectiveness and trade efficiency (stretch goal)
- `Aggression` — drives faction conflict behavior

**Mechanic:**
Evolution doesn't happen every tick — it accumulates pressure over N ticks and applies
a small modifier when a threshold is crossed. Changes are gradual and reversible (a
species under sustained scarcity will shrink back toward baseline).

**Future:** branching evolution paths, player-nudged evolution via environmental
manipulation, fossil record (track how a species changed over the run)

---

## 8. Player Interaction

The player sets initial parameters, then intervenes in real time:

- **Disasters** — meteor strike, drought, flood (wipes/reduces resources on tiles)
- **Conflicts** — trigger a war between two factions
- **Seeding** — place a new population on a tile mid-run
- **Terraforming** — modify tile resource capacity (limited uses, or costs a resource)
- **Speed controls** — pause, slow, fast-forward

The player never directly controls a population — they shape conditions and watch
the sim respond.

---

## Open questions

- Hex grid vs. square grid? (Hex feels more natural for territory but square is simpler to implement)
- Should faction membership be automatic (based on species) or emergent (populations join factions based on proximity and relations)?
- How do we handle species going fully extinct — respawn mechanic, or permanent loss?
- Map size — procedurally generated or hand-authored in the map builder?
