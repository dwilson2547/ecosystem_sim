namespace EcosystemSim;

public class World
{
    public WorldState State { get; } = new();

    public void Tick()
    {
        foreach (var tile in State.Map.AllTiles())
        {
            RegenerateResources(tile);
            DistributeResources(tile);
            ApplyGrowthAndDeath(tile);
            ApplyWaterExposure(tile);
            ProduceByproducts(tile);
            DecayByproducts(tile);
            ApplyTerrainDegradation(tile);
        }

        Migrate();
        ResolveCombat();
        SpreadDisease();
        ExecuteTrade();
        UpdateFactionRelations();
        ApplyEvolution();
        ApplySpeciation();
        State.Tick++;
        AdvanceSeason();
    }

    public void Apply(IWorldCommand command) => command.Execute(State);

    public const int TicksPerSeason = 25;

    private const float FertilizerBoost = 0.02f;

    private void RegenerateResources(Tile tile)
    {
        var fertilizer = tile.Byproducts.FirstOrDefault(b => b.Type == ByproductType.Fertilizer);
        var fertBonus  = fertilizer is not null ? fertilizer.Amount * FertilizerBoost : 0f;

        foreach (var pool in tile.Resources)
        {
            var seasonMult = SeasonMultiplier(State.CurrentSeason, pool.Type);
            var bonus      = pool.Type != ResourceType.Water ? fertBonus : 0f;
            pool.Amount = MathF.Min(pool.Capacity, pool.Amount + pool.RegenPerTick * seasonMult + bonus);
        }
    }

    private static float SeasonMultiplier(Season season, ResourceType type)
    {
        if (type == ResourceType.Water)
            return season switch
            {
                Season.Spring => 1.4f,
                Season.Summer => 0.5f,
                Season.Autumn => 1.0f,
                Season.Winter => 0.2f,
                _             => 1.0f,
            };

        // Ground/Brush/Canopy all follow the same seasonal food curve
        return season switch
        {
            Season.Spring => 1.3f,
            Season.Summer => 1.0f,
            Season.Autumn => 0.8f,
            Season.Winter => 0.3f,
            _             => 1.0f,
        };
    }

    private void AdvanceSeason()
    {
        State.SeasonTick++;
        if (State.SeasonTick < TicksPerSeason) return;
        State.SeasonTick   = 0;
        State.CurrentSeason = (Season)(((int)State.CurrentSeason + 1) % 4);
    }

    private static void ProduceByproducts(Tile tile)
    {
        foreach (var pop in tile.Populations.Where(p => p.Count > 0))
        foreach (var (byproductType, rate) in pop.Species.ByproductRates)
            tile.GetOrAddByproduct(byproductType).Add(pop.Count * rate);
    }

    private static void DecayByproducts(Tile tile)
    {
        foreach (var pool in tile.Byproducts)
            pool.Decay();
    }

    // a terrain's defining food stratum (e.g. Forest's Canopy) staying denuded long enough means
    // sustained heavy browsing has permanently changed the landscape — the terrain converts to
    // TerrainStats.Degradation's target and gets a fresh pool set for the new terrain.
    private const float DegradationThresholdRatio = 0.10f; // stratum considered denuded below this fraction of capacity
    private const float DegradationPressureTarget = 60f;   // sustained ticks of denuded stratum before terrain converts

    private readonly Random _random = new();

    private void ApplyTerrainDegradation(Tile tile)
    {
        if (!TerrainStats.Degradation.TryGetValue(tile.Terrain, out var rule)) return;

        var pool  = tile.Resources.FirstOrDefault(r => r.Type == rule.TriggerStratum);
        var ratio = pool is { Capacity: > 0 } ? pool.Amount / pool.Capacity : 0f;

        if (ratio < DegradationThresholdRatio)
            tile.DegradationPressure++;
        else
            tile.DegradationPressure = Math.Max(0f, tile.DegradationPressure - 1f);

        if (tile.DegradationPressure < DegradationPressureTarget) return;

        tile.Terrain = rule.DegradesTo;
        tile.Resources.Clear();
        tile.Resources.AddRange(TerrainStats.BuildResourcePools(rule.DegradesTo, _random));
        tile.DegradationPressure = 0f;
    }

    // every 5 individuals of a population compounds its resource draw — a handful of dinosaurs
    // sip from a tile, but a mega-herd strip-mines it. Keeps single-tile blobs from being viable.
    private const float DensityDrainBase      = 1.15f;
    private const float DensityDrainGroupSize = 5f;

    private static float DensityMultiplier(int count) =>
        MathF.Pow(DensityDrainBase, count / DensityDrainGroupSize);

    private static readonly ResourceType[] FoodStrata = [ResourceType.Ground, ResourceType.Brush, ResourceType.Canopy];

    private static void DistributeResources(Tile tile)
    {
        foreach (var pop in tile.Populations)
            pop.LastSatisfaction = pop.Count > 0 ? 1f : 0f;

        DistributeWater(tile);
        DistributeFood(tile);
    }

    private static void DistributeWater(Tile tile)
    {
        var pool = tile.Resources.FirstOrDefault(r => r.Type == ResourceType.Water);

        var demands = tile.Populations
            .Where(p => p.Species.WaterConsumptionRate > 0)
            .Select(p => (pop: p, demand: p.Count * p.EffectiveWaterDemand * DensityMultiplier(p.Count)))
            .ToList();

        var totalDemand = demands.Sum(d => d.demand);
        if (totalDemand == 0) return;

        var supplyRatio = pool is not null ? Math.Min(pool.Amount / totalDemand, 1f) : 0f;

        foreach (var (pop, demand) in demands)
        {
            if (demand == 0) continue;

            var received = demand * supplyRatio;
            pool?.Consume(received);

            pop.LastSatisfaction = Math.Min(pop.LastSatisfaction, received / demand);
        }
    }

    // Food demand is aggregate per population, then split across Ground/Brush/Canopy at
    // consumption time — weighted by ease-of-eating × current availability, so a population
    // gravitates toward whatever's both easy for it AND actually present on the tile. If its
    // easiest strata run dry, some demand naturally spills onto harder-to-eat strata rather than
    // starving outright, but a stratum it can't eat at all (ease 0) is never touched regardless
    // of how much sits there unconsumed.
    private static void DistributeFood(Tile tile)
    {
        var pools = FoodStrata.ToDictionary(t => t, t => tile.Resources.FirstOrDefault(r => r.Type == t));

        var demands = tile.Populations
            .Where(p => p.Species.FoodConsumptionRate > 0)
            .Select(p => (pop: p, demand: p.Count * p.EffectiveFoodDemand * DensityMultiplier(p.Count)))
            .ToList();
        if (demands.Count == 0) return;

        var preferred      = new Dictionary<(Population, ResourceType), float>();
        var totalRequested = FoodStrata.ToDictionary(t => t, _ => 0f);

        foreach (var (pop, demand) in demands)
        {
            var weights = FoodStrata.ToDictionary(t =>
                t, t => pop.Species.EffectiveEase(t, tile.Terrain) * (pools[t]?.Amount ?? 0f));
            var totalWeight = weights.Values.Sum();
            if (totalWeight <= 0f) continue; // nothing on this tile that the population can eat

            foreach (var stratum in FoodStrata)
            {
                var amount = demand * weights[stratum] / totalWeight;
                preferred[(pop, stratum)] = amount;
                totalRequested[stratum]  += amount;
            }
        }

        var supplyRatio = FoodStrata.ToDictionary(t => t, t =>
            totalRequested[t] > 0 && pools[t] is not null ? Math.Min(pools[t]!.Amount / totalRequested[t], 1f) : 0f);

        foreach (var (pop, demand) in demands)
        {
            var received = 0f;
            foreach (var stratum in FoodStrata)
            {
                if (!preferred.TryGetValue((pop, stratum), out var wanted) || wanted <= 0f) continue;
                var got = wanted * supplyRatio[stratum];
                pools[stratum]?.Consume(got);
                received += got;
            }
            pop.LastSatisfaction = Math.Min(pop.LastSatisfaction, demand > 0 ? received / demand : 1f);
        }
    }

    private static void ApplyGrowthAndDeath(Tile tile)
    {
        foreach (var pop in tile.Populations)
        {
            var satisfaction = pop.LastSatisfaction;

            if (satisfaction >= 1f)
            {
                // ceiling ensures small-but-surviving populations can recover (avoids int-truncation limbo)
                pop.Count += (int)Math.Ceiling(pop.Count * pop.Species.ReproductionRate);
            }
            else
            {
                var deficit = 1f - satisfaction;
                var deaths = (int)Math.Ceiling(pop.Count * pop.Species.StarvationRate * deficit);
                pop.Count = Math.Max(0, pop.Count - deaths);
            }
        }
    }

    // nothing can live in the water for long — populations stranded on River terrain accumulate
    // exposure and start drowning past the threshold. Leaving lets them recover (a drink is fine,
    // moving in is not).
    private const float WaterSurvivalThreshold = 15f;  // ticks tolerated on River before drowning starts
    private const float WaterExposureMortality = 0.12f; // fraction lost per tick once past the threshold
    private const float WaterFleeThreshold     = 10f;  // ticks tolerated before Migrate() forces an evacuation attempt

    private static void ApplyWaterExposure(Tile tile)
    {
        var inWater = tile.Terrain == TerrainType.River;

        foreach (var pop in tile.Populations)
        {
            if (pop.Count == 0) continue;

            if (!inWater)
            {
                pop.WaterExposure = Math.Max(0f, pop.WaterExposure - 1f);
                continue;
            }

            pop.WaterExposure++;
            if (pop.WaterExposure <= WaterSurvivalThreshold) continue;

            var deaths = (int)Math.Ceiling(pop.Count * WaterExposureMortality);
            pop.Count = Math.Max(0, pop.Count - deaths);
        }
    }

    private void Migrate()
    {
        // collect all moves before applying so relocations don't cascade within one tick
        var moves = new List<(Population pop, int migrantCount, Tile from, Tile to)>();

        foreach (var tile in State.Map.AllTiles())
        {
            foreach (var pop in tile.Populations)
            {
                if (pop.Count == 0) continue;

                // water is actively hostile — flee it well before drowning starts, regardless of
                // how well-fed the population is. Resource satisfaction alone would never trigger
                // this since a population sitting on abundant food/water reads as fully satisfied.
                if (tile.Terrain == TerrainType.River && pop.WaterExposure >= WaterFleeThreshold)
                {
                    var refuge = BestNeighborAwayFromWater(tile, pop.Species);
                    if (refuge is not null)
                    {
                        moves.Add((pop, pop.Count, tile, refuge)); // evacuate entirely, not just the excess
                        continue;
                    }
                }

                if (pop.LastSatisfaction >= pop.Species.MigrationThreshold) continue;

                var lacking = MostLackingNeed(pop, tile);
                if (lacking is null) continue;

                var destination = lacking == ResourceNeed.Food
                    ? BestNeighborForFood(tile, pop.Species)
                    : BestNeighborForWater(tile);
                if (destination is null) continue;

                // only move the individuals that exceed the tile's sustainable capacity;
                // the rest stay and consume what's available rather than abandoning the tile
                var sustainable = lacking == ResourceNeed.Food
                    ? SustainableFoodCount(pop, tile)
                    : SustainableWaterCount(pop, tile);
                var migrants = pop.Count - sustainable;
                if (migrants <= 0) continue;

                moves.Add((pop, migrants, tile, destination));
            }
        }

        foreach (var (pop, migrantCount, from, to) in moves)
        {
            Population mover;

            if (migrantCount >= pop.Count)
            {
                // entire group leaves (sustainable == 0, e.g. resource absent)
                from.RemovePopulation(pop);
                mover = pop;
            }
            else
            {
                // partial migration: leave the sustainable portion in place, fork the excess
                pop.Count -= migrantCount;
                mover      = ForkFrom(pop, migrantCount);
            }

            PlaceOrMerge(mover, to);
        }
    }

    // Returns how many individuals of pop the tile can sustainably support based on water regen.
    private int SustainableWaterCount(Population pop, Tile tile)
    {
        var pool = tile.Resources.FirstOrDefault(r => r.Type == ResourceType.Water);
        if (pool is null) return 0;
        var rate = pop.EffectiveWaterDemand;
        if (rate <= 0f) return pop.Count;
        var effectiveRegen = pool.RegenPerTick * SeasonMultiplier(State.CurrentSeason, ResourceType.Water);
        return (int)Math.Floor(effectiveRegen / rate);
    }

    // Same idea for food, but regen is summed across strata weighted by ease-of-eating — a
    // stratum the population can't eat contributes nothing even if it regenerates quickly.
    private int SustainableFoodCount(Population pop, Tile tile)
    {
        var rate = pop.EffectiveFoodDemand;
        if (rate <= 0f) return pop.Count;

        var effectiveRegen = FoodStrata.Sum(stratum =>
        {
            var pool = tile.Resources.FirstOrDefault(r => r.Type == stratum);
            if (pool is null) return 0f;
            var ease = pop.Species.EffectiveEase(stratum, tile.Terrain);
            return ease * pool.RegenPerTick * SeasonMultiplier(State.CurrentSeason, stratum);
        });

        return (int)Math.Floor(effectiveRegen / rate);
    }

    // Creates a new Population by splitting count individuals off of source.
    // The fork inherits all evolved state; faction registration and tile placement are caller's job.
    private static Population ForkFrom(Population source, int count)
    {
        var fork = new Population
        {
            Species         = source.Species,
            Count           = count,
            SizeIndex       = source.SizeIndex,
            SizePressure    = source.SizePressure,
            ImmunityDelta   = source.ImmunityDelta,
            ImmunityPressure = source.ImmunityPressure,
            Disease         = source.Disease,
            InfectionLevel  = source.InfectionLevel,
            WaterExposure   = source.WaterExposure,
        };
        fork.Faction = source.Faction; // internal set; faction list entry deferred to PlaceOrMerge
        return fork;
    }

    // Places pop on destination, merging into any compatible existing population.
    // Handles faction list bookkeeping for both full-migration and fork cases.
    private void PlaceOrMerge(Population pop, Tile destination)
    {
        var existing = destination.Populations.FirstOrDefault(p =>
            p != pop && p.Count > 0 &&
            p.Species == pop.Species &&
            (pop.Faction is null || p.Faction == pop.Faction));

        if (existing is not null)
        {
            var total = (float)(existing.Count + pop.Count);
            existing.SizeIndex     = (existing.SizeIndex     * existing.Count + pop.SizeIndex     * pop.Count) / total;
            existing.ImmunityDelta = (existing.ImmunityDelta * existing.Count + pop.ImmunityDelta * pop.Count) / total;
            existing.SizePressure  = (existing.SizePressure  * existing.Count + pop.SizePressure  * pop.Count) / total;
            existing.WaterExposure = (existing.WaterExposure * existing.Count + pop.WaterExposure * pop.Count) / total;
            existing.Count += pop.Count;

            // pop is absorbed: remove from faction list if it was registered there
            // (full-migration case); forks were never added so Remove is a no-op
            pop.Faction?.Populations.Remove(pop);
        }
        else
        {
            // fork case: register with faction before placing on tile
            if (pop.Faction is not null && !pop.Faction.Populations.Contains(pop))
                pop.Faction.Populations.Add(pop);
            destination.AddPopulation(pop);
        }
    }

    private void SpreadDisease()
    {
        const float AdjacentSpreadFactor = 0.3f; // disease seeps across tile borders much slower

        // phase 1: calculate exposures without modifying state
        var exposures = new Dictionary<Population, (Disease disease, float amount)>();

        foreach (var tile in State.Map.AllTiles())
        {
            foreach (var source in tile.Populations.Where(p => p.Count > 0 && p.Disease is not null && p.InfectionLevel > 0))
            {
                var disease      = source.Disease!;
                var densityBonus = 1f + source.Count / 500f; // denser populations spread faster

                Expose(disease, source, tile.Populations,          source.InfectionLevel * disease.SpreadRate * densityBonus);
                Expose(disease, source, NeighborPops(tile), source.InfectionLevel * disease.SpreadRate * AdjacentSpreadFactor);
            }
        }

        // phase 2: apply exposures
        foreach (var (pop, (disease, amount)) in exposures)
        {
            pop.Disease        = disease;
            pop.InfectionLevel = Math.Min(1f, pop.InfectionLevel + amount);
        }

        // phase 3: mortality then recovery for all infected populations
        foreach (var pop in State.Map.AllPopulations().Where(p => p.Count > 0 && p.Disease is not null))
        {
            var disease  = pop.Disease!;
            var immunity = pop.EffectiveImmunity;

            var deaths = (int)Math.Ceiling(pop.Count * pop.InfectionLevel * disease.MortalityRate * (1f - immunity));
            pop.Count = Math.Max(0, pop.Count - deaths);

            var recovery   = disease.RecoveryRate + immunity * 0.05f;
            pop.InfectionLevel = Math.Max(0f, pop.InfectionLevel - recovery);

            if (pop.InfectionLevel <= 0f)
                pop.Disease = null;
        }

        return;

        void Expose(Disease disease, Population source, IEnumerable<Population> targets, float baseAmount)
        {
            foreach (var target in targets)
            {
                if (target == source || target.Count == 0) continue;
                if (target.Disease is not null && target.Disease != disease) continue;

                var amount = baseAmount * (1f - target.EffectiveImmunity);
                if (amount <= 0) continue;

                if (exposures.TryGetValue(target, out var existing))
                    exposures[target] = (disease, existing.amount + amount);
                else
                    exposures[target] = (disease, amount);
            }
        }

        IEnumerable<Population> NeighborPops(Tile tile) =>
            State.Map.GetNeighbors(tile).SelectMany(n => n.Populations);
    }

    private void ExecuteTrade()
    {
        const float ByproductTradeFraction = 0.15f; // fraction of byproduct imbalance transferred per tick
        const float TradeTensionBonus      = 0.04f; // tension reduction per tick while actively trading

        var seen = new HashSet<(Faction, Faction)>();

        foreach (var faction in State.Factions.Where(f => !f.IsExtinct))
        {
            foreach (var (other, relation) in faction.Relations)
            {
                if (!relation.HasTradeAgreement) continue;
                if (relation.State == DiplomaticState.AtWar)  continue;
                if (seen.Contains((other, faction)))           continue;
                seen.Add((faction, other));

                var (aTile, bTile) = ClosestTilePair(faction, other);
                if (aTile is null || bTile is null) continue;

                ExchangeByproducts(aTile, bTile, ByproductTradeFraction);

                // active trade softens diplomatic tension
                var rel = faction.Relations[other];
                var newScore = Math.Clamp(rel.TensionScore - TradeTensionBonus, -2f, 2f);
                SyncRelation(faction, other, newScore);
            }
        }
    }

    private static void ExchangeByproducts(Tile aTile, Tile bTile, float fraction)
    {
        // byproducts flow from whichever tile has more toward the other, equalizing gradually
        var types = aTile.Byproducts.Select(p => p.Type)
                         .Union(bTile.Byproducts.Select(p => p.Type))
                         .Distinct();

        foreach (var type in types)
        {
            var aPool = aTile.GetOrAddByproduct(type);
            var bPool = bTile.GetOrAddByproduct(type);
            var diff  = aPool.Amount - bPool.Amount;
            if (MathF.Abs(diff) < 0.1f) continue;

            var transfer = diff * fraction;
            aPool.Amount -= transfer;
            bPool.Amount += transfer;
        }
    }

    private static (Tile? a, Tile? b) ClosestTilePair(Faction a, Faction b)
    {
        Tile? bestA = null, bestB = null;
        var minDist = int.MaxValue;

        foreach (var ap in a.Populations.Where(p => p.Count > 0 && p.CurrentTile is not null))
        foreach (var bp in b.Populations.Where(p => p.Count > 0 && p.CurrentTile is not null))
        {
            var dist = Math.Abs(ap.CurrentTile!.X - bp.CurrentTile!.X)
                     + Math.Abs(ap.CurrentTile!.Y - bp.CurrentTile!.Y);
            if (dist < minDist) { minDist = dist; bestA = ap.CurrentTile; bestB = bp.CurrentTile; }
        }

        return (bestA, bestB);
    }

    private void ResolveCombat()
    {
        foreach (var tile in State.Map.AllTiles())
            ResolveTileCombat(tile);
    }

    private static void ResolveTileCombat(Tile tile)
    {
        const float CombatRate = 0.02f;

        var pops = tile.Populations.Where(p => p.Count > 0).ToList();
        if (pops.Count < 2) return;

        // collect all casualties before applying — simultaneous resolution
        var casualties = new Dictionary<Population, int>();

        foreach (var attacker in pops)
        {
            foreach (var defender in pops)
            {
                if (attacker == defender) continue;
                if (attacker.Faction is null || defender.Faction is null) continue;
                if (!attacker.Faction.Relations.TryGetValue(defender.Faction, out var relation)) continue;
                if (relation.State != DiplomaticState.AtWar) continue;

                var damage = (int)Math.Ceiling(attacker.Count * attacker.EffectiveCombatStrength * CombatRate);
                casualties[defender] = casualties.GetValueOrDefault(defender) + damage;
            }
        }

        foreach (var (pop, loss) in casualties)
            pop.Count = Math.Max(0, pop.Count - loss);
    }

    private void ApplyEvolution()
    {
        const float AbundanceThreshold    = 0.90f;
        const float ScarcityThreshold     = 0.50f;
        const float SizePressureTarget    = 50f;   // ticks until size shifts
        const float SizeStep              = 0.05f;
        const float SizeMin               = 0.50f;
        const float SizeMax               = 2.00f;
        const float ImmunityPressureTarget = 30f;  // ticks of disease exposure until immunity gains
        const float ImmunityStep          = 0.02f;
        const float ImmunityMax           = 0.50f; // cap on gained immunity above species baseline

        foreach (var pop in State.Map.AllPopulations())
        {
            if (pop.Count == 0) continue;

            // SIZE — sustained abundance grows the population, sustained scarcity shrinks it
            if (pop.LastSatisfaction >= AbundanceThreshold)
                pop.SizePressure++;
            else if (pop.LastSatisfaction < ScarcityThreshold)
                pop.SizePressure--;

            if (pop.SizePressure >= SizePressureTarget)
            {
                pop.SizeIndex    = Math.Min(SizeMax, pop.SizeIndex + SizeStep);
                pop.SizePressure = 0f;
            }
            else if (pop.SizePressure <= -SizePressureTarget)
            {
                pop.SizeIndex    = Math.Max(SizeMin, pop.SizeIndex - SizeStep);
                pop.SizePressure = 0f;
            }

            // IMMUNITY — surviving disease exposure permanently hardens the population
            if (pop.Disease is not null && pop.InfectionLevel > 0.1f)
                pop.ImmunityPressure++;

            if (pop.ImmunityPressure >= ImmunityPressureTarget)
            {
                pop.ImmunityDelta    = Math.Min(ImmunityMax, pop.ImmunityDelta + ImmunityStep);
                pop.ImmunityPressure = 0f;
            }
        }
    }

    public const float SpeciationLargeThreshold = 1.5f;
    public const float SpeciationSmallThreshold = 0.65f;

    private void ApplySpeciation()
    {
        foreach (var pop in State.Map.AllPopulations())
        {
            if (pop.Count == 0) continue;
            if (pop.SizeIndex < SpeciationLargeThreshold && pop.SizeIndex > SpeciationSmallThreshold) continue;

            var derivedName = DeriveSpeciesName(pop.Species, pop.SizeIndex);
            if (derivedName == pop.Species.Name) continue; // already at this naming cap

            var newSpecies = FindSpecies(derivedName)
                ?? CreateDerivedSpecies(pop.Species, derivedName, pop.SizeIndex, pop.ImmunityDelta);

            pop.Species        = newSpecies;
            pop.SizeIndex      = 1.0f;
            pop.SizePressure   = 0f;
            pop.ImmunityDelta  = 0f;
            pop.ImmunityPressure = 0f;
        }
    }

    private static string DeriveSpeciesName(SpeciesDefinition parent, float sizeIndex)
    {
        var root = parent.EffectiveRootName;

        if (sizeIndex >= SpeciationLargeThreshold)
        {
            if (parent.Name == $"Giant {root}")   return $"Giant {root}";   // cap
            if (parent.Name == $"Greater {root}") return $"Giant {root}";
            return $"Greater {root}";
        }
        else
        {
            if (parent.Name == $"Dwarf {root}")  return $"Dwarf {root}";    // cap
            if (parent.Name == $"Lesser {root}") return $"Dwarf {root}";
            return $"Lesser {root}";
        }
    }

    private static SpeciesDefinition CreateDerivedSpecies(
        SpeciesDefinition parent, string name, float sizeIndex, float immunityDelta)
    {
        // bake the evolved size into the new species baseline so traits are continuous
        // across speciation (effective values at moment of split == effective values right after)
        var newByproducts = parent.ByproductRates
            .ToDictionary(kv => kv.Key, kv => kv.Value * sizeIndex);

        return new SpeciesDefinition
        {
            Name                 = name,
            RootName             = parent.EffectiveRootName,
            FoodConsumptionRate  = parent.FoodConsumptionRate * sizeIndex,
            WaterConsumptionRate = parent.WaterConsumptionRate, // unaffected by size, same invariant as before
            EaseOfEating         = new Dictionary<ResourceType, float>(parent.EaseOfEating), // diet doesn't change with size
            ByproductRates       = newByproducts,
            CombatStrength       = parent.CombatStrength   * MathF.Sqrt(sizeIndex),
            ReproductionRate     = parent.ReproductionRate / MathF.Sqrt(sizeIndex), // larger → slower repro
            StarvationRate       = parent.StarvationRate,
            MigrationThreshold   = parent.MigrationThreshold,
            WarAggression        = parent.WarAggression,
            Immunity             = MathF.Min(1f, parent.Immunity + immunityDelta),
        };
    }

    // find an existing species by name across all live populations
    private SpeciesDefinition? FindSpecies(string name) =>
        State.Map.AllPopulations()
             .Select(p => p.Species)
             .FirstOrDefault(s => s.Name == name);

    private void UpdateFactionRelations()
    {
        var active = State.Factions.Where(f => !f.IsExtinct).ToList();

        for (var i = 0; i < active.Count; i++)
            for (var j = i + 1; j < active.Count; j++)
                UpdateRelationBetween(active[i], active[j]);
    }

    private static void UpdateRelationBetween(Faction a, Faction b)
    {
        const int   ProximityRange     = 5;
        const float DecayRate          = 0.10f; // tension moves toward 0 per tick when out of range
        const float AggressionScale    = 0.10f; // multiplier on aggression × proximity
        const float PeaceDrift         = 0.03f; // natural de-escalation each tick when not at war
        const int   CeasefireThreshold = 20;    // ticks at war before ceasefire pressure kicks in
        const float CeasefireDecay     = 0.15f; // tension reduction per tick once exhausted

        if (!a.Relations.ContainsKey(b))
            a.Relations[b] = new FactionRelation { Other = b };
        if (!b.Relations.ContainsKey(a))
            b.Relations[a] = new FactionRelation { Other = a };

        var relation = a.Relations[b];
        var minDist  = MinDistance(a, b);
        float delta;

        if (minDist > ProximityRange)
        {
            // out of range: decay toward neutral without overshooting zero
            var sign = Math.Sign(relation.TensionScore);
            delta = -sign * Math.Min(DecayRate, Math.Abs(relation.TensionScore));
        }
        else
        {
            var proximityFactor  = (float)(ProximityRange - minDist) / ProximityRange;
            var aggressionFactor = (a.PrimarySpecies.WarAggression + b.PrimarySpecies.WarAggression) / 2f;

            // base pressure: aggression × how close they are
            delta = aggressionFactor * proximityFactor * AggressionScale;

            // resource competition: shared scarce resources escalate, complementary resources de-escalate
            delta += ResourceCompetitionPressure(a, b);

            // natural tendency toward peace when not actively at war
            if (relation.State != DiplomaticState.AtWar)
                delta -= PeaceDrift;

            // war exhaustion: after sustained conflict, ceasefire pressure builds
            if (relation.State == DiplomaticState.AtWar)
            {
                relation.TicksAtWar++;
                if (relation.TicksAtWar > CeasefireThreshold)
                    delta -= CeasefireDecay;
            }
        }

        var newScore = Math.Clamp(relation.TensionScore + delta, -2f, 2f);
        SyncRelation(a, b, newScore);

        if (TensionToState(newScore) != DiplomaticState.AtWar)
        {
            a.Relations[b].TicksAtWar = 0;
            b.Relations[a].TicksAtWar = 0;
        }
    }

    // shared starving resources → escalation; complementary resources → cooperation bias
    private static float ResourceCompetitionPressure(Faction a, Faction b)
    {
        var sharedResources = 0;
        if (a.PrimarySpecies.FoodConsumptionRate  > 0 && b.PrimarySpecies.FoodConsumptionRate  > 0) sharedResources++;
        if (a.PrimarySpecies.WaterConsumptionRate > 0 && b.PrimarySpecies.WaterConsumptionRate > 0) sharedResources++;

        if (sharedResources == 0) return -0.08f; // complementary niches actively build cooperation

        // only escalate when populations are genuinely starving, not just a bit hungry
        var eitherStarving = a.Populations.Concat(b.Populations)
            .Where(p => p.Count > 0)
            .Any(p => p.LastSatisfaction < 0.5f);

        return eitherStarving ? 0.10f : 0.01f;
    }

    private static void SyncRelation(Faction a, Faction b, float tensionScore)
    {
        var state = TensionToState(tensionScore);

        // going to war breaks any active trade agreement
        if (state == DiplomaticState.AtWar)
        {
            a.Relations[b].HasTradeAgreement = false;
            b.Relations[a].HasTradeAgreement = false;
        }

        a.Relations[b].TensionScore = tensionScore;
        a.Relations[b].State        = state;
        b.Relations[a].TensionScore = tensionScore;
        b.Relations[a].State        = state;
        b.Relations[a].TicksAtWar   = a.Relations[b].TicksAtWar;
    }

    private static DiplomaticState TensionToState(float tension) => tension switch
    {
        < -0.5f => DiplomaticState.Allied,
        < 0.5f  => DiplomaticState.Neutral,
        < 1.5f  => DiplomaticState.Tense,
        _       => DiplomaticState.AtWar
    };

    private static int MinDistance(Faction a, Faction b) =>
        a.Populations
            .Where(p => p.Count > 0 && p.CurrentTile is not null)
            .SelectMany(p => b.Populations
                .Where(q => q.Count > 0 && q.CurrentTile is not null)
                .Select(q => HexDistance(p.CurrentTile!, q.CurrentTile!)))
            .DefaultIfEmpty(int.MaxValue)
            .Min();

    // cube-coordinate distance for odd-r offset hex grid
    private static int HexDistance(Tile a, Tile b)
    {
        var aq = a.X - (a.Y - (a.Y & 1)) / 2;
        var bq = b.X - (b.Y - (b.Y & 1)) / 2;
        var ar = a.Y;
        var br = b.Y;
        return Math.Max(Math.Max(Math.Abs(aq - bq), Math.Abs(ar - br)),
                        Math.Abs((-aq - ar) - (-bq - br)));
    }

    private enum ResourceNeed { Food, Water }

    private static ResourceNeed? MostLackingNeed(Population pop, Tile tile)
    {
        ResourceNeed? worst = null;
        var worstRatio = float.MaxValue;

        if (pop.Species.FoodConsumptionRate > 0)
        {
            var demand = pop.Count * pop.EffectiveFoodDemand;
            var ratio  = demand > 0 ? EffectiveFoodValue(tile, pop.Species) / demand : float.MaxValue;
            if (ratio < worstRatio) { worstRatio = ratio; worst = ResourceNeed.Food; }
        }

        if (pop.Species.WaterConsumptionRate > 0)
        {
            var pool   = tile.Resources.FirstOrDefault(r => r.Type == ResourceType.Water);
            var demand = pop.Count * pop.EffectiveWaterDemand;
            var ratio  = demand > 0 ? (pool?.Amount ?? 0f) / demand : float.MaxValue;
            if (ratio < worstRatio) { worstRatio = ratio; worst = ResourceNeed.Water; }
        }

        return worst;
    }

    // how much food value a tile represents to a given species — each stratum's raw amount
    // weighted by how easily the species can actually eat it there. This is what "favor areas
    // with more easy-to-eat food, but factor in what's available" comes down to: a tile stuffed
    // with a stratum the species is bad at scores low, and a stratum it's great at but which is
    // nearly empty also scores low. Both ease AND availability have to be there.
    private static float EffectiveFoodValue(Tile tile, SpeciesDefinition species) =>
        FoodStrata.Sum(stratum => ResourceAmount(tile, stratum) * species.EffectiveEase(stratum, tile.Terrain));

    // dry ground to flee to — prefers whichever non-River neighbor has the most combined
    // effective food (for this species) plus water on hand, cheapest terrain to enter as
    // tiebreaker. Returns null if boxed in by River on every side (e.g. mid-channel); the
    // population takes another tick of losses and retries next tick.
    private Tile? BestNeighborAwayFromWater(Tile current, SpeciesDefinition species) =>
        State.Map.GetNeighbors(current)
            .Where(n => n.Terrain != TerrainType.River)
            .OrderByDescending(n => EffectiveFoodValue(n, species) + ResourceAmount(n, ResourceType.Water))
            .ThenBy(n => TerrainStats.MigrationCostOf(n.Terrain))
            .FirstOrDefault();

    private Tile? BestNeighborForWater(Tile current) =>
        BestNeighborByValue(current, t => ResourceAmount(t, ResourceType.Water));

    private Tile? BestNeighborForFood(Tile current, SpeciesDefinition species) =>
        BestNeighborByValue(current, t => EffectiveFoodValue(t, species));

    private Tile? BestNeighborByValue(Tile current, Func<Tile, float> valueOf)
    {
        const int MaxSearchDepth = 6; // maximum tile distance to search for a resource source

        var currentValue = valueOf(current);
        var neighbors     = State.Map.GetNeighbors(current).ToList();

        // primary: immediate neighbor with strictly more value
        // tiebreak on migration cost so populations naturally avoid swamp/highland when routes are similar
        var immediate = neighbors
            .Where(n => valueOf(n) > currentValue)
            .OrderByDescending(valueOf)
            .ThenBy(n => TerrainStats.MigrationCostOf(n.Terrain))
            .FirstOrDefault();

        if (immediate is not null) return immediate;

        // fallback BFS: when no immediate gradient exists (e.g. resource is several tiles away),
        // find the nearest tile with more and take the first step toward it
        var visited = new HashSet<Tile> { current };
        var queue   = new Queue<(Tile tile, Tile firstStep)>();
        foreach (var n in neighbors) { queue.Enqueue((n, n)); visited.Add(n); }

        while (queue.Count > 0)
        {
            var (tile, firstStep) = queue.Dequeue();
            if (HexDistance(tile, current) > MaxSearchDepth) continue;

            if (valueOf(tile) > currentValue)
                return firstStep;

            foreach (var next in State.Map.GetNeighbors(tile))
                if (visited.Add(next))
                    queue.Enqueue((next, firstStep));
        }

        return null;
    }

    private static float ResourceAmount(Tile tile, ResourceType type) =>
        tile.Resources.FirstOrDefault(r => r.Type == type)?.Amount ?? 0f;
}
