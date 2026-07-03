namespace EcosystemSim;

public class World
{
    public WorldState State { get; } = new();

    public World() { }
    public World(int width, int height) { State = new WorldState { Map = new WorldMap(width, height) }; }

    public void Tick()
    {
        foreach (var pop in State.Map.AllPopulations())
            pop.JustMigrated = false;

        foreach (var tile in State.Map.AllTiles())
        {
            RegenerateResources(tile);
            DistributeResources(tile);
            HuntPrey(tile);
            ApplyWaterExposure(tile);
            ProduceByproducts(tile);
            DecayByproducts(tile);
            ApplyTerrainDegradation(tile);
        }

        Migrate();
        HuntPreyForMigrants();
        foreach (var tile in State.Map.AllTiles())
            ApplyGrowthAndDeath(tile);
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
            var bonus      = pool.Type == ResourceType.Food ? fertBonus : 0f;
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

        if (type == ResourceType.Prey) return 1f;

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
        State.SeasonTick    = 0;
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

    // ── terrain degradation ───────────────────────────────────────────────────

    private const float DegradationThresholdRatio = 0.10f;
    private const float DegradationPressureTarget = 60f;

    private readonly Random _random = new();

    private void ApplyTerrainDegradation(Tile tile)
    {
        if (!TerrainStats.Degradation.TryGetValue(tile.Terrain, out var rule)) return;

        var pool  = tile.Resources.FirstOrDefault(r => r.Type == ResourceType.Food && r.FoodSubtype == rule.TriggerSubtype);
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

    // ── resource distribution ─────────────────────────────────────────────────

    // every 5 individuals of a population compounds its resource draw — keeps mega-herds from
    // strip-mining a single tile indefinitely.
    private const float DensityDrainBase      = 1.15f;
    private const float DensityDrainGroupSize = 5f;

    private static float DensityMultiplier(int count) =>
        MathF.Pow(DensityDrainBase, count / DensityDrainGroupSize);

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
            .Where(p => p.Count > 0 && p.Species.WaterConsumptionRate > 0)
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

    // Food demand is aggregate per population, split across food pools at consumption time
    // weighted by ease-of-eating × current availability — species gravitate toward whatever
    // they can actually eat AND is actually present. Density drain applies.
    private static void DistributeFood(Tile tile)
    {
        var foodPools = tile.Resources.Where(r => r.Type == ResourceType.Food).ToList();

        var demands = tile.Populations
            .Where(p => p.Count > 0 && p.Species.FoodConsumptionRate > 0)
            .Select(p => (pop: p, demand: p.Count * p.EffectiveFoodDemand * DensityMultiplier(p.Count)))
            .ToList();
        if (demands.Count == 0) return;

        var wanted         = new Dictionary<(Population, ResourcePool), float>();
        var totalRequested = foodPools.ToDictionary(p => p, _ => 0f);

        foreach (var (pop, demand) in demands)
        {
            var weights    = foodPools.ToDictionary(p => p, p => pop.Species.EffectiveEase(p.FoodSubtype) * p.Amount);
            var totalWeight = weights.Values.Sum();
            if (totalWeight <= 0f) continue;

            foreach (var pool in foodPools)
            {
                var amount = demand * weights[pool] / totalWeight;
                wanted[(pop, pool)]    = amount;
                totalRequested[pool]  += amount;
            }
        }

        var supplyRatio = foodPools.ToDictionary(p => p, p =>
            totalRequested[p] > 0 ? Math.Min(p.Amount / totalRequested[p], 1f) : 0f);

        foreach (var (pop, demand) in demands)
        {
            var received = 0f;
            foreach (var pool in foodPools)
            {
                if (!wanted.TryGetValue((pop, pool), out var w) || w <= 0f) continue;
                var got = w * supplyRatio[pool];
                pool.Consume(got);
                received += got;
            }
            pop.LastSatisfaction = Math.Min(pop.LastSatisfaction, demand > 0 ? received / demand : 1f);
        }
    }

    // ── predation ─────────────────────────────────────────────────────────────

    private const float AcceptedPreyValue = 2f / 3f;

    // Holling type-III prey refuge: the findable (huntable) portion of a herd is
    // count × count/(count + K), so hunting efficiency collapses as prey thin out and
    // predators can never drive a population to zero in one pass. Larger K = safer prey.
    private const float PreyRefugeHalfSaturation = 20f;

    // prey herds below this size don't scatter — they're already at refuge scale, and fragmenting
    // them further would spread prey into predator-free tiles faster than predators can follow
    private const int ScatterMinHerd = 12;

    // two-pass hunt mirroring the food distribution model: preferred prey first (full satisfaction),
    // then accepted prey for still-hungry hunters (2/3 satisfaction).
    private static void HuntPrey(Tile tile, bool migrantsOnly = false)
    {
        var hunters = tile.Populations
            .Where(p => p.Count > 0 && p.Species.IsPredator && (!migrantsOnly || p.JustMigrated))
            .ToList();
        if (hunters.Count == 0) return;

        var preyPops = tile.Populations
            .Where(p => p.Count > 0 && p.Species.AsPreyCategory.HasValue)
            .ToList();

        if (preyPops.Count == 0)
        {
            // no prey on the tile — a pure predator goes hungry (sat 0), but a dual-consumption
            // predator (FoodConsumptionRate > 0) lives off the food it ate this tick, so leave its
            // food-based satisfaction intact instead of zeroing it. mirrors "survives on fish
            // between hunts" and stops a well-fed apex reading as perpetually starving.
            foreach (var h in hunters)
                if (h.Species.FoodConsumptionRate <= 0f) h.LastSatisfaction = 0f;
            return;
        }

        var demand       = hunters.ToDictionary(h => h, h => h.Count * h.EffectivePreyDemand);
        var received     = hunters.ToDictionary(h => h, _ => 0f);
        var preyConsumed = preyPops.ToDictionary(p => p, _ => 0f);

        float RemainingDemand(Population h) => Math.Max(0f, demand[h] - received[h]);

        // functional response: only a density-dependent fraction of the herd is findable this
        // tick, so predation saturates when prey are abundant and leaves a refuge when they're scarce
        float AvailablePrey(Population prey)
        {
            var findable = prey.Count * (prey.Count / (prey.Count + PreyRefugeHalfSaturation));
            return Math.Max(0f, findable - preyConsumed[prey]);
        }

        // pass 1: preferred prey — full sat; empty PreferredPrey means "any prey at full sat"
        foreach (var preyPop in preyPops)
        {
            var cat      = preyPop.Species.AsPreyCategory!.Value;
            var eligible = hunters.Where(h =>
                h.Species.PreferredPrey.Count == 0 || h.Species.PreferredPrey.Contains(cat)).ToList();
            if (eligible.Count == 0) continue;

            var available   = AvailablePrey(preyPop);
            var totalDemand = eligible.Sum(RemainingDemand);
            if (totalDemand == 0 || available == 0) continue;

            var ratio = Math.Min(1f, available / totalDemand);
            foreach (var hunter in eligible)
            {
                var d = RemainingDemand(hunter);
                if (d <= 0) continue;
                var taken = d * ratio;
                preyConsumed[preyPop] += taken;
                received[hunter]      += taken;
            }
        }

        // pass 2: accepted prey — 2/3 sat, only for still-hungry hunters with explicit preferences
        foreach (var preyPop in preyPops)
        {
            var cat      = preyPop.Species.AsPreyCategory!.Value;
            var eligible = hunters.Where(h =>
                h.Species.PreferredPrey.Count > 0 &&
                !h.Species.PreferredPrey.Contains(cat) &&
                h.Species.AcceptedPrey.Contains(cat) &&
                RemainingDemand(h) > 0).ToList();
            if (eligible.Count == 0) continue;

            var available   = AvailablePrey(preyPop);
            var totalDemand = eligible.Sum(RemainingDemand);
            if (totalDemand == 0 || available == 0) continue;

            var ratio = Math.Min(1f, available / totalDemand);
            foreach (var hunter in eligible)
            {
                var d = RemainingDemand(hunter);
                if (d <= 0) continue;
                var taken = d * ratio;
                preyConsumed[preyPop] += taken;
                received[hunter]      += taken * AcceptedPreyValue;
            }
        }

        // apply prey deaths — fractional consumption accumulates so a sub-1 hunt (a thinned,
        // well-hidden herd) doesn't get rounded up into a full kill and wiped out
        foreach (var (preyPop, consumed) in preyConsumed)
        {
            preyPop.PredationAccumulator += consumed;
            var deaths = Math.Min(preyPop.Count, (int)Math.Floor(preyPop.PredationAccumulator));
            preyPop.PredationAccumulator -= deaths;
            preyPop.Count = Math.Max(0, preyPop.Count - deaths);
        }

        // update hunter satisfaction
        // pure predators: prey sat is the only resource — take min with whatever food/water set
        // dual-consumption predators (FoodConsumptionRate > 0): weighted average of food and prey
        // satisfaction so fish can sustain them at partial sat when prey is absent
        foreach (var hunter in hunters)
        {
            var d = demand[hunter];
            if (d == 0) continue;
            var preySat   = Math.Min(1f, received[hunter] / d);
            var foodWeight = hunter.Species.FoodConsumptionRate;
            var preyWeight = hunter.EffectivePreyDemand;
            if (foodWeight > 0f)
                hunter.LastSatisfaction = (hunter.LastSatisfaction * foodWeight + preySat * preyWeight)
                                          / (foodWeight + preyWeight);
            else
                hunter.LastSatisfaction = Math.Min(hunter.LastSatisfaction, preySat);
        }
    }

    // ── growth and death ──────────────────────────────────────────────────────

    private static void ApplyGrowthAndDeath(Tile tile)
    {
        const float GrowthThreshold     = 0.85f;
        const float StarvationThreshold = 0.50f;

        foreach (var pop in tile.Populations)
        {
            var satisfaction = pop.LastSatisfaction;

            if (satisfaction >= GrowthThreshold)
            {
                // fractional births accumulate so a very slow reproducer grows at its true rate
                // rather than the old Math.Ceiling forcing at least +1 every tick. a well-fed
                // Count=1 pop still grows eventually — it just takes several ticks to bank a whole
                // individual instead of doubling instantly (preserves the no-stranding invariant).
                pop.ReproductionAccumulator += pop.Count * pop.Species.ReproductionRate;
                var births = (int)pop.ReproductionAccumulator;
                pop.ReproductionAccumulator -= births;
                pop.Count += births;
                if (pop.Species.MaxCount > 0 && pop.Count >= pop.Species.MaxCount)
                {
                    pop.Count = pop.Species.MaxCount;
                    pop.ReproductionAccumulator = 0f; // at cap, don't hoard growth debt for later
                }
                pop.StarvationAccumulator = 0f;
            }
            else if (satisfaction <= StarvationThreshold)
            {
                var deficit = 1f - satisfaction;
                pop.StarvationAccumulator += pop.Count * pop.Species.StarvationRate * deficit;
                var deaths = Math.Min(pop.Count, (int)pop.StarvationAccumulator);
                pop.StarvationAccumulator -= deaths;
                pop.Count -= deaths;
                if (pop.Count == 0) pop.StarvationAccumulator = 0f;
                pop.ReproductionAccumulator = 0f; // starving — clear any banked growth
            }
            else
            {
                // neutral zone [0.50, 0.85) — neither grow nor starve; clear both debts
                pop.StarvationAccumulator = 0f;
                pop.ReproductionAccumulator = 0f;
            }
        }
    }

    // ── water exposure / river drowning ───────────────────────────────────────

    private const float WaterSurvivalThreshold = 15f;
    private const float WaterExposureMortality = 0.12f;
    private const float WaterFleeThreshold     = 10f;

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

    // ── migration ─────────────────────────────────────────────────────────────

    private void Migrate()
    {
        var moves = new List<(Population pop, int migrantCount, Tile from, Tile to)>();

        foreach (var tile in State.Map.AllTiles())
        {
            foreach (var pop in tile.Populations)
            {
                if (pop.Count == 0) continue;

                // river is hostile — flee before drowning, regardless of resource satisfaction
                if (tile.Terrain == TerrainType.River && pop.WaterExposure >= WaterFleeThreshold)
                {
                    var refuge = BestNeighborAwayFromWater(tile, pop.Species);
                    if (refuge is not null)
                    {
                        moves.Add((pop, pop.Count, tile, refuge));
                        continue;
                    }
                }

                if (pop.MigrationCooldown > 0) { pop.MigrationCooldown--; continue; }

                // scatter: prey bolt from a tile a predator has invaded even if well-fed. A third of
                // the herd splits to the safest reachable neighbour; the rest stay (and get hunted),
                // so predators aren't starved out. Only herds above ScatterMinHerd fragment — small
                // groups hold ground rather than dispersing into predator-free tiles and exploding,
                // which would starve the predators. Throttled by MigrationCooldownTicks (skittishness).
                if (pop.Species.AsPreyCategory.HasValue
                    && pop.Count >= ScatterMinHerd
                    && TileHasPredatorFor(tile, pop))
                {
                    var refuge = BestNeighborAwayFromPredators(tile, pop.Species);
                    if (refuge is not null)
                    {
                        var fleeing = Math.Max(1, pop.Count / 3);
                        moves.Add((pop, fleeing, tile, refuge));
                        pop.MigrationCooldown = pop.Species.MigrationCooldownTicks;
                        continue;
                    }
                }

                if (pop.LastSatisfaction >= pop.Species.MigrationThreshold) continue;

                var lacking = MostLackingNeed(pop, tile);
                if (lacking is null) continue;

                var destination = lacking switch
                {
                    ResourceNeed.Food  => BestNeighborForFood(tile, pop.Species),
                    ResourceNeed.Water => BestNeighborForWater(tile, pop.Species),
                    ResourceNeed.Prey  => BestNeighborForPrey(tile, pop.Species),
                    _                  => null,
                };
                if (destination is null) continue;

                var sustainable = lacking switch
                {
                    ResourceNeed.Food  => SustainableFoodCount(pop, tile),
                    ResourceNeed.Water => SustainableWaterCount(pop, tile),
                    ResourceNeed.Prey  => SustainablePreyCount(pop, tile),
                    _                  => 0,
                };
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
                from.RemovePopulation(pop);
                mover = pop;
            }
            else
            {
                pop.Count -= migrantCount;
                mover      = ForkFrom(pop, migrantCount);
            }

            var survivor = PlaceOrMerge(mover, to);
            survivor.MigrationCooldown = survivor.Species.MigrationCooldownTicks;
            survivor.JustMigrated      = true;
        }
    }

    private void HuntPreyForMigrants()
    {
        foreach (var tile in State.Map.AllTiles())
        {
            if (tile.Populations.Any(p => p.JustMigrated && p.Species.IsPredator && p.Count > 0))
                HuntPrey(tile, migrantsOnly: true);
        }
    }

    private enum ResourceNeed { Food, Water, Prey }

    private static ResourceNeed? MostLackingNeed(Population pop, Tile tile)
    {
        ResourceNeed? worst     = null;
        var           worstRatio = float.MaxValue;

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

        if (pop.Species.PreyConsumptionRate > 0)
        {
            var demand = pop.Count * pop.EffectivePreyDemand;
            var ratio  = demand > 0 ? EffectivePreyAmount(tile, pop.Species) / demand : float.MaxValue;
            if (ratio < worstRatio) { worstRatio = ratio; worst = ResourceNeed.Prey; }
        }

        return worst;
    }

    private int SustainableWaterCount(Population pop, Tile tile)
    {
        var pool = tile.Resources.FirstOrDefault(r => r.Type == ResourceType.Water);
        if (pool is null) return 0;
        var rate = pop.EffectiveWaterDemand;
        if (rate <= 0f) return pop.Count;
        var effectiveRegen = pool.RegenPerTick * SeasonMultiplier(State.CurrentSeason, ResourceType.Water);
        return (int)Math.Floor(effectiveRegen / rate);
    }

    private int SustainableFoodCount(Population pop, Tile tile)
    {
        var rate = pop.EffectiveFoodDemand;
        if (rate <= 0f) return pop.Count;
        var effectiveRegen = tile.Resources
            .Where(r => r.Type == ResourceType.Food)
            .Sum(pool => pop.Species.EffectiveEase(pool.FoodSubtype)
                         * pool.RegenPerTick
                         * SeasonMultiplier(State.CurrentSeason, ResourceType.Food));
        return (int)Math.Floor(effectiveRegen / rate);
    }

    private static int SustainablePreyCount(Population pop, Tile tile)
    {
        var rate = pop.EffectivePreyDemand;
        if (rate <= 0f) return pop.Count;
        // current prey count as proxy for sustainable supply
        return (int)Math.Floor(EffectivePreyAmount(tile, pop.Species) / rate);
    }

    // how much food value a tile represents for a species — each pool weighted by ease × amount
    private static float EffectiveFoodValue(Tile tile, SpeciesDefinition species) =>
        tile.Resources
            .Where(r => r.Type == ResourceType.Food)
            .Sum(pool => pool.Amount * species.EffectiveEase(pool.FoodSubtype));

    // how much effective prey a tile has for a carnivore — preferred at full weight, accepted at 2/3
    private static float EffectivePreyAmount(Tile tile, SpeciesDefinition species)
    {
        var total = 0f;
        foreach (var pop in tile.Populations.Where(p => p.Count > 0 && p.Species.AsPreyCategory.HasValue))
        {
            var cat = pop.Species.AsPreyCategory!.Value;
            if (species.PreferredPrey.Count == 0 || species.PreferredPrey.Contains(cat))
                total += pop.Count;
            else if (species.AcceptedPrey.Contains(cat))
                total += pop.Count * AcceptedPreyValue;
        }
        return total;
    }

    // true if any living predator on the tile can hunt this prey (preferred, accepted, or generalist)
    private static bool TileHasPredatorFor(Tile tile, Population prey) =>
        PredatorPressureOn(tile, prey.Species) > 0;

    // total count of predators on a tile that would hunt the given prey species
    private static int PredatorPressureOn(Tile tile, SpeciesDefinition preySpecies)
    {
        if (!preySpecies.AsPreyCategory.HasValue) return 0;
        var cat = preySpecies.AsPreyCategory.Value;
        return tile.Populations
            .Where(p => p.Count > 0 && p.Species.IsPredator
                     && (p.Species.PreferredPrey.Count == 0
                         || p.Species.PreferredPrey.Contains(cat)
                         || p.Species.AcceptedPrey.Contains(cat)))
            .Sum(p => p.Count);
    }

    // flee to the same-biome, terrain-allowed neighbour with the least predator pressure,
    // breaking ties by food value; avoid River so prey don't bolt straight into drowning
    private Tile? BestNeighborAwayFromPredators(Tile current, SpeciesDefinition species) =>
        State.Map.GetNeighbors(current)
            .Where(n => n.Terrain != TerrainType.River
                     && TerrainStats.IsOcean(n.Terrain) == TerrainStats.IsOcean(current.Terrain)
                     && IsTerrainAllowed(n, species))
            .OrderBy(n => PredatorPressureOn(n, species))
            .ThenByDescending(n => EffectiveFoodValue(n, species))
            .ThenBy(n => TerrainStats.MigrationCostOf(n.Terrain))
            .FirstOrDefault();

    // flee to the best non-River, same-biome neighbor
    private Tile? BestNeighborAwayFromWater(Tile current, SpeciesDefinition species) =>
        State.Map.GetNeighbors(current)
            .Where(n => n.Terrain != TerrainType.River
                     && TerrainStats.IsOcean(n.Terrain) == TerrainStats.IsOcean(current.Terrain)
                     && IsTerrainAllowed(n, species))
            .OrderByDescending(n => EffectiveFoodValue(n, species) + ResourceAmount(n, ResourceType.Water))
            .ThenBy(n => TerrainStats.MigrationCostOf(n.Terrain))
            .FirstOrDefault();

    private Tile? BestNeighborForWater(Tile current, SpeciesDefinition species) =>
        BestNeighborByValue(current, t => ResourceAmount(t, ResourceType.Water), species);

    private Tile? BestNeighborForFood(Tile current, SpeciesDefinition species) =>
        BestNeighborByValue(current, t => EffectiveFoodValue(t, species), species);

    private Tile? BestNeighborForPrey(Tile current, SpeciesDefinition species) =>
        BestNeighborByValue(current, t => EffectivePreyAmount(t, species), species);

    private static bool IsTerrainAllowed(Tile tile, SpeciesDefinition species) =>
        species.AllowedTerrains.Count == 0 || species.AllowedTerrains.Contains(tile.Terrain);

    private Tile? BestNeighborByValue(Tile current, Func<Tile, float> valueOf, SpeciesDefinition species)
    {
        const int MaxSearchDepth = 6;

        var currentValue = valueOf(current);
        var neighbors    = State.Map.GetNeighbors(current)
            .Where(n => TerrainStats.IsOcean(n.Terrain) == TerrainStats.IsOcean(current.Terrain)
                     && IsTerrainAllowed(n, species))
            .ToList();

        var immediate = neighbors
            .Where(n => valueOf(n) > currentValue)
            .OrderByDescending(valueOf)
            .ThenBy(n => TerrainStats.MigrationCostOf(n.Terrain))
            .FirstOrDefault();

        if (immediate is not null) return immediate;

        // BFS fallback: find nearest tile with more, return first step toward it
        var visited = new HashSet<Tile> { current };
        var queue   = new Queue<(Tile tile, Tile firstStep)>();
        foreach (var n in neighbors) { queue.Enqueue((n, n)); visited.Add(n); }

        while (queue.Count > 0)
        {
            var (tile, firstStep) = queue.Dequeue();
            if (HexDistance(tile, current) > MaxSearchDepth) continue;

            if (valueOf(tile) > currentValue)
                return firstStep;

            foreach (var next in State.Map.GetNeighbors(tile)
                .Where(n => TerrainStats.IsOcean(n.Terrain) == TerrainStats.IsOcean(current.Terrain)
                         && IsTerrainAllowed(n, species)))
                if (visited.Add(next))
                    queue.Enqueue((next, firstStep));
        }

        return null;
    }

    private static Population ForkFrom(Population source, int count) => new()
    {
        Species          = source.Species,
        Count            = count,
        SizeIndex        = source.SizeIndex,
        SizePressure     = source.SizePressure,
        ImmunityDelta    = source.ImmunityDelta,
        ImmunityPressure = source.ImmunityPressure,
        Disease          = source.Disease,
        InfectionLevel   = source.InfectionLevel,
        WaterExposure    = source.WaterExposure,
        Faction          = source.Faction,
    };

    private Population PlaceOrMerge(Population pop, Tile destination)
    {
        var existing = destination.Populations.FirstOrDefault(p =>
            p != pop && p.Count > 0 &&
            p.Species == pop.Species &&
            (pop.Faction is null || p.Faction == pop.Faction));

        if (existing is not null)
        {
            var total = (float)(existing.Count + pop.Count);
            existing.SizeIndex            = (existing.SizeIndex            * existing.Count + pop.SizeIndex            * pop.Count) / total;
            existing.ImmunityDelta        = (existing.ImmunityDelta        * existing.Count + pop.ImmunityDelta        * pop.Count) / total;
            existing.SizePressure         = (existing.SizePressure         * existing.Count + pop.SizePressure         * pop.Count) / total;
            existing.WaterExposure        = (existing.WaterExposure        * existing.Count + pop.WaterExposure        * pop.Count) / total;
            existing.StarvationAccumulator = (existing.StarvationAccumulator * existing.Count + pop.StarvationAccumulator * pop.Count) / total;
            existing.PredationAccumulator  = (existing.PredationAccumulator  * existing.Count + pop.PredationAccumulator  * pop.Count) / total;
            existing.ReproductionAccumulator = (existing.ReproductionAccumulator * existing.Count + pop.ReproductionAccumulator * pop.Count) / total;
            existing.Count += pop.Count;
            pop.Faction?.Populations.Remove(pop);
            return existing;
        }
        else
        {
            if (pop.Faction is not null && !pop.Faction.Populations.Contains(pop))
                pop.Faction.Populations.Add(pop);
            destination.AddPopulation(pop);
            return pop;
        }
    }

    // ── disease ───────────────────────────────────────────────────────────────

    private void SpreadDisease()
    {
        const float AdjacentSpreadFactor = 0.3f;

        var exposures = new Dictionary<Population, (Disease disease, float amount)>();

        foreach (var tile in State.Map.AllTiles())
        {
            foreach (var source in tile.Populations.Where(p => p.Count > 0 && p.Disease is not null && p.InfectionLevel > 0))
            {
                var disease      = source.Disease!;
                var densityBonus = 1f + source.Count / 500f;

                Expose(disease, source, tile.Populations,          source.InfectionLevel * disease.SpreadRate * densityBonus);
                Expose(disease, source, NeighborPops(tile), source.InfectionLevel * disease.SpreadRate * AdjacentSpreadFactor);
            }
        }

        foreach (var (pop, (disease, amount)) in exposures)
        {
            pop.Disease        = disease;
            pop.InfectionLevel = Math.Min(1f, pop.InfectionLevel + amount);
        }

        foreach (var pop in State.Map.AllPopulations().Where(p => p.Count > 0 && p.Disease is not null))
        {
            var disease  = pop.Disease!;
            var immunity = pop.EffectiveImmunity;

            var deaths = (int)Math.Ceiling(pop.Count * pop.InfectionLevel * disease.MortalityRate * (1f - immunity));
            pop.Count = Math.Max(0, pop.Count - deaths);

            var recovery = disease.RecoveryRate + immunity * 0.05f;
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

    // ── trade ─────────────────────────────────────────────────────────────────

    private void ExecuteTrade()
    {
        const float ByproductTradeFraction = 0.15f;
        const float TradeTensionBonus      = 0.04f;

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

                var rel      = faction.Relations[other];
                var newScore = Math.Clamp(rel.TensionScore - TradeTensionBonus, -2f, 2f);
                SyncRelation(faction, other, newScore);
            }
        }
    }

    private static void ExchangeByproducts(Tile aTile, Tile bTile, float fraction)
    {
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

    // ── combat ────────────────────────────────────────────────────────────────

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

        var casualties = new Dictionary<Population, int>();

        foreach (var attacker in pops)
        foreach (var defender in pops)
        {
            if (attacker == defender) continue;
            if (attacker.Faction is null || defender.Faction is null) continue;
            if (!attacker.Faction.Relations.TryGetValue(defender.Faction, out var relation)) continue;
            if (relation.State != DiplomaticState.AtWar) continue;

            var damage = (int)Math.Ceiling(attacker.Count * attacker.EffectiveCombatStrength * CombatRate);
            casualties[defender] = casualties.GetValueOrDefault(defender) + damage;
        }

        foreach (var (pop, loss) in casualties)
            pop.Count = Math.Max(0, pop.Count - loss);
    }

    // ── diplomacy ─────────────────────────────────────────────────────────────

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
        const float DecayRate          = 0.10f;
        const float AggressionScale    = 0.10f;
        const float PeaceDrift         = 0.03f;
        const int   CeasefireThreshold = 20;
        const float CeasefireDecay     = 0.15f;

        if (!a.Relations.ContainsKey(b)) a.Relations[b] = new FactionRelation { Other = b };
        if (!b.Relations.ContainsKey(a)) b.Relations[a] = new FactionRelation { Other = a };

        var relation = a.Relations[b];
        var minDist  = MinDistance(a, b);
        float delta;

        if (minDist > ProximityRange)
        {
            var sign = Math.Sign(relation.TensionScore);
            delta = -sign * Math.Min(DecayRate, Math.Abs(relation.TensionScore));
        }
        else
        {
            var proximityFactor  = (float)(ProximityRange - minDist) / ProximityRange;
            var aggressionFactor = (a.PrimarySpecies.WarAggression + b.PrimarySpecies.WarAggression) / 2f;

            delta  = aggressionFactor * proximityFactor * AggressionScale;
            delta += ResourceCompetitionPressure(a, b);

            if (relation.State != DiplomaticState.AtWar) delta -= PeaceDrift;

            if (relation.State == DiplomaticState.AtWar)
            {
                relation.TicksAtWar++;
                if (relation.TicksAtWar > CeasefireThreshold) delta -= CeasefireDecay;
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

    private static float ResourceCompetitionPressure(Faction a, Faction b)
    {
        var sharedResources = 0;
        if (a.PrimarySpecies.FoodConsumptionRate  > 0 && b.PrimarySpecies.FoodConsumptionRate  > 0) sharedResources++;
        if (a.PrimarySpecies.WaterConsumptionRate > 0 && b.PrimarySpecies.WaterConsumptionRate > 0) sharedResources++;
        if (a.PrimarySpecies.PreyConsumptionRate  > 0 && b.PrimarySpecies.PreyConsumptionRate  > 0) sharedResources++;

        if (sharedResources == 0) return -0.08f;

        var eitherStarving = a.Populations.Concat(b.Populations)
            .Where(p => p.Count > 0)
            .Any(p => p.LastSatisfaction < 0.5f);

        return eitherStarving ? 0.10f : 0.01f;
    }

    private static void SyncRelation(Faction a, Faction b, float tensionScore)
    {
        var state = TensionToState(tensionScore);

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

    // Declarative faction war is disabled for now. Dinosaur "factions" are still half-built
    // scaffolding (a real faction won't exist until symbiotic relationships between species land),
    // and tension-driven AtWar was warring peaceful species into extinction — e.g. a lone apex
    // Megalodon, which reads as perpetually starving with no prey on its tile, dragging every
    // neighbour to war and getting slaughtered as a Count=1 unit. Tension is still tracked
    // (Allied/Neutral/Tense) as dormant scaffolding; it simply never escalates to war, so
    // ResolveCombat never fires and trade never breaks on war. Combat still works when a war is set
    // directly (tests, future player commands). Replace with the territorial migrate-in-and-brawl
    // model when factions + symbiosis arrive; flip this to true to restore the old behaviour.
    private const bool DiplomaticWarEnabled = false;

    private static DiplomaticState TensionToState(float tension) => tension switch
    {
        < -0.5f => DiplomaticState.Allied,
        < 0.5f  => DiplomaticState.Neutral,
        < 1.5f  => DiplomaticState.Tense,
        _       => DiplomaticWarEnabled ? DiplomaticState.AtWar : DiplomaticState.Tense
    };

    private static int MinDistance(Faction a, Faction b) =>
        a.Populations
            .Where(p => p.Count > 0 && p.CurrentTile is not null)
            .SelectMany(p => b.Populations
                .Where(q => q.Count > 0 && q.CurrentTile is not null)
                .Select(q => HexDistance(p.CurrentTile!, q.CurrentTile!)))
            .DefaultIfEmpty(int.MaxValue)
            .Min();

    private static int HexDistance(Tile a, Tile b)
    {
        var aq = a.X - (a.Y - (a.Y & 1)) / 2;
        var bq = b.X - (b.Y - (b.Y & 1)) / 2;
        var ar = a.Y;
        var br = b.Y;
        return Math.Max(Math.Max(Math.Abs(aq - bq), Math.Abs(ar - br)),
                        Math.Abs((-aq - ar) - (-bq - br)));
    }

    // ── evolution ─────────────────────────────────────────────────────────────

    private void ApplyEvolution()
    {
        const float AbundanceThreshold    = 0.90f;
        const float ScarcityThreshold     = 0.50f;
        const float SizePressureTarget    = 50f;
        const float SizeStep              = 0.05f;
        const float SizeMin               = 0.50f;
        const float SizeMax               = 2.00f;
        const float ImmunityPressureTarget = 30f;
        const float ImmunityStep          = 0.02f;
        const float ImmunityMax           = 0.50f;

        foreach (var pop in State.Map.AllPopulations())
        {
            if (pop.Count == 0) continue;

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

            if (pop.Disease is not null && pop.InfectionLevel > 0.1f)
                pop.ImmunityPressure++;

            if (pop.ImmunityPressure >= ImmunityPressureTarget)
            {
                pop.ImmunityDelta    = Math.Min(ImmunityMax, pop.ImmunityDelta + ImmunityStep);
                pop.ImmunityPressure = 0f;
            }
        }
    }

    // ── speciation ────────────────────────────────────────────────────────────

    public const float SpeciationLargeThreshold = 1.5f;
    public const float SpeciationSmallThreshold = 0.65f;

    private void ApplySpeciation()
    {
        foreach (var pop in State.Map.AllPopulations())
        {
            if (pop.Count == 0) continue;
            if (pop.SizeIndex < SpeciationLargeThreshold && pop.SizeIndex > SpeciationSmallThreshold) continue;

            var derivedName = DeriveSpeciesName(pop.Species, pop.SizeIndex);
            if (derivedName == pop.Species.Name) continue;

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
            if (parent.Name == $"Giant {root}")   return $"Giant {root}";
            if (parent.Name == $"Greater {root}") return $"Giant {root}";
            return $"Greater {root}";
        }
        else
        {
            if (parent.Name == $"Dwarf {root}")  return $"Dwarf {root}";
            if (parent.Name == $"Lesser {root}") return $"Dwarf {root}";
            return $"Lesser {root}";
        }
    }

    private static SpeciesDefinition CreateDerivedSpecies(
        SpeciesDefinition parent, string name, float sizeIndex, float immunityDelta)
    {
        return new SpeciesDefinition
        {
            Name                 = name,
            RootName             = parent.EffectiveRootName,
            FoodConsumptionRate  = parent.FoodConsumptionRate  * sizeIndex,
            WaterConsumptionRate = parent.WaterConsumptionRate,
            PreyConsumptionRate  = parent.PreyConsumptionRate  * sizeIndex,
            EaseOfEating         = new Dictionary<FoodSubtype, float>(parent.EaseOfEating),
            AsPreyCategory       = parent.AsPreyCategory,
            PreferredPrey        = parent.PreferredPrey,
            AcceptedPrey         = parent.AcceptedPrey,
            ByproductRates       = parent.ByproductRates.ToDictionary(kv => kv.Key, kv => kv.Value * sizeIndex),
            CombatStrength       = parent.CombatStrength   * MathF.Sqrt(sizeIndex),
            ReproductionRate     = parent.ReproductionRate / MathF.Sqrt(sizeIndex),
            StarvationRate       = parent.StarvationRate,
            MigrationThreshold      = parent.MigrationThreshold,
            MigrationCooldownTicks  = parent.MigrationCooldownTicks,
            AllowedTerrains         = parent.AllowedTerrains,
            MaxCount                = parent.MaxCount,
            WarAggression        = parent.WarAggression,
            Immunity             = MathF.Min(1f, parent.Immunity + immunityDelta),
        };
    }

    private SpeciesDefinition? FindSpecies(string name) =>
        State.Map.AllPopulations()
             .Select(p => p.Species)
             .FirstOrDefault(s => s.Name == name);

    // ── helpers ───────────────────────────────────────────────────────────────

    private static float ResourceAmount(Tile tile, ResourceType type) =>
        tile.Resources.FirstOrDefault(r => r.Type == type)?.Amount ?? 0f;
}
