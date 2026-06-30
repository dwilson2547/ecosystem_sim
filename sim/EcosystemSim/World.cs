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
            ProduceByproducts(tile);
            DecayByproducts(tile);
        }

        Migrate();
        ResolveCombat();
        SpreadDisease();
        ExecuteTrade();
        UpdateFactionRelations();
        ApplyEvolution();
        State.Tick++;
    }

    public void Apply(IWorldCommand command) => command.Execute(State);

    private const float FertilizerBoost = 0.02f; // food regen bonus per unit of fertilizer on tile

    private static void RegenerateResources(Tile tile)
    {
        var fertilizer = tile.Byproducts.FirstOrDefault(b => b.Type == ByproductType.Fertilizer);
        var fertBonus  = fertilizer is not null ? fertilizer.Amount * FertilizerBoost : 0f;

        foreach (var pool in tile.Resources)
            pool.Regen(pool.Type == ResourceType.Food ? fertBonus : 0f);
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

    private static void DistributeResources(Tile tile)
    {
        foreach (var pop in tile.Populations)
            pop.LastSatisfaction = pop.Count > 0 ? 1f : 0f;

        foreach (var resourceType in Enum.GetValues<ResourceType>())
        {
            var pool = tile.Resources.FirstOrDefault(r => r.Type == resourceType);

            var demands = tile.Populations
                .Select(p => (pop: p, demand: p.Count * p.EffectiveConsumptionRate(resourceType)))
                .ToList();

            var totalDemand = demands.Sum(d => d.demand);
            if (totalDemand == 0) continue;

            var supplyRatio = pool is not null ? Math.Min(pool.Amount / totalDemand, 1f) : 0f;

            foreach (var (pop, demand) in demands)
            {
                if (demand == 0) continue;

                var received = demand * supplyRatio;
                pool?.Consume(received);

                pop.LastSatisfaction = Math.Min(pop.LastSatisfaction, received / demand);
            }
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

    private void Migrate()
    {
        // collect all moves before applying so relocations don't cascade within one tick
        var moves = new List<(Population pop, Tile from, Tile to)>();

        foreach (var tile in State.Map.AllTiles())
        {
            foreach (var pop in tile.Populations)
            {
                if (pop.Count == 0) continue;
                if (pop.LastSatisfaction >= pop.Species.MigrationThreshold) continue;

                var lacking = MostLackingResource(pop, tile);
                if (lacking is null) continue;

                var destination = BestNeighborFor(tile, lacking.Value);
                if (destination is not null)
                    moves.Add((pop, tile, destination));
            }
        }

        foreach (var (pop, from, to) in moves)
        {
            from.RemovePopulation(pop);

            // only merge with populations from the same faction (or unfactioned same-species)
            var existing = to.Populations.FirstOrDefault(p =>
                p.Species == pop.Species && (pop.Faction is null || p.Faction == pop.Faction));

            if (existing is not null)
            {
                // blend evolved traits weighted by count before merging
                var total = (float)(existing.Count + pop.Count);
                existing.SizeIndex     = (existing.SizeIndex     * existing.Count + pop.SizeIndex     * pop.Count) / total;
                existing.ImmunityDelta = (existing.ImmunityDelta * existing.Count + pop.ImmunityDelta * pop.Count) / total;
                existing.SizePressure  = (existing.SizePressure  * existing.Count + pop.SizePressure  * pop.Count) / total;
                existing.Count += pop.Count;
            }
            else
                to.AddPopulation(pop);
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
        var sharedResources = a.PrimarySpecies.ConsumptionRates.Keys
            .Intersect(b.PrimarySpecies.ConsumptionRates.Keys)
            .Count();

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
                .Select(q => Math.Abs(p.CurrentTile!.X - q.CurrentTile!.X)
                           + Math.Abs(p.CurrentTile!.Y - q.CurrentTile!.Y)))
            .DefaultIfEmpty(int.MaxValue)
            .Min();

    private static ResourceType? MostLackingResource(Population pop, Tile tile)
    {
        ResourceType? worst = null;
        var worstRatio = float.MaxValue;

        foreach (var resourceType in pop.Species.ConsumptionRates.Keys)
        {
            var effectiveRate = pop.EffectiveConsumptionRate(resourceType);
            if (effectiveRate == 0) continue;
            var pool = tile.Resources.FirstOrDefault(r => r.Type == resourceType);
            var ratio = pool is null ? 0f : pool.Amount / (pop.Count * effectiveRate);
            if (ratio < worstRatio)
            {
                worstRatio = ratio;
                worst = resourceType;
            }
        }

        return worst;
    }

    private Tile? BestNeighborFor(Tile current, ResourceType resourceType)
    {
        var currentAmount = current.Resources
            .FirstOrDefault(r => r.Type == resourceType)?.Amount ?? 0f;

        return State.Map.GetNeighbors(current)
            .Where(n => (n.Resources.FirstOrDefault(r => r.Type == resourceType)?.Amount ?? 0f) > currentAmount)
            .OrderByDescending(n => n.Resources.FirstOrDefault(r => r.Type == resourceType)?.Amount ?? 0f)
            .FirstOrDefault();
    }
}
