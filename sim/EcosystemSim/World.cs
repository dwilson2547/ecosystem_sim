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
        }

        Migrate();
        ResolveCombat();
        SpreadDisease();
        UpdateFactionRelations();
        State.Tick++;
    }

    public void Apply(IWorldCommand command) => command.Execute(State);

    private static void RegenerateResources(Tile tile)
    {
        foreach (var pool in tile.Resources)
            pool.Regen();
    }

    private static void DistributeResources(Tile tile)
    {
        foreach (var pop in tile.Populations)
            pop.LastSatisfaction = 1f;

        foreach (var resourceType in Enum.GetValues<ResourceType>())
        {
            var pool = tile.Resources.FirstOrDefault(r => r.Type == resourceType);

            var demands = tile.Populations
                .Select(p => (pop: p, demand: p.Count * p.Species.ConsumptionRates.GetValueOrDefault(resourceType)))
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
                pop.Count += (int)(pop.Count * pop.Species.ReproductionRate);
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
                existing.Count += pop.Count;
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
            var immunity = pop.Species.Immunity;

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

                var amount = baseAmount * (1f - target.Species.Immunity);
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

                var damage = (int)Math.Ceiling(attacker.Count * attacker.Species.CombatStrength * CombatRate);
                casualties[defender] = casualties.GetValueOrDefault(defender) + damage;
            }
        }

        foreach (var (pop, loss) in casualties)
            pop.Count = Math.Max(0, pop.Count - loss);
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
        const float DecayRate          = 0.08f; // tension moves toward 0 per tick when out of range
        const float AggressionScale    = 0.12f; // multiplier on aggression × proximity
        const int   CeasefireThreshold = 25;    // ticks at war before ceasefire pressure kicks in
        const float CeasefireDecay     = 0.10f; // tension reduction per tick once exhausted

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

    // shared scarce resources → escalation; complementary resources → slight peace bias
    private static float ResourceCompetitionPressure(Faction a, Faction b)
    {
        var sharedResources = a.PrimarySpecies.ConsumptionRates.Keys
            .Intersect(b.PrimarySpecies.ConsumptionRates.Keys)
            .Count();

        if (sharedResources == 0) return -0.05f;

        var eitherStressed = a.Populations.Concat(b.Populations)
            .Where(p => p.Count > 0)
            .Any(p => p.LastSatisfaction < 0.7f);

        return eitherStressed ? 0.20f : 0.05f;
    }

    private static void SyncRelation(Faction a, Faction b, float tensionScore)
    {
        var state = TensionToState(tensionScore);
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

        foreach (var (resourceType, rate) in pop.Species.ConsumptionRates)
        {
            if (rate == 0) continue;
            var pool = tile.Resources.FirstOrDefault(r => r.Type == resourceType);
            var ratio = pool is null ? 0f : pool.Amount / (pop.Count * rate);
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
