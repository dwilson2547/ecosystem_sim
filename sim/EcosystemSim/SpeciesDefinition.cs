namespace EcosystemSim;

public class SpeciesDefinition
{
    public required string Name { get; init; }

    // ancestor species name, shared across all derived species; null means use Name
    public string? RootName { get; init; }

    // the root of the lineage, used for naming derived species
    public string EffectiveRootName => RootName ?? Name;

    // aggregate food demand per individual per tick. Split across food pools at consumption time
    // based on EaseOfEating × what's actually available — see World.DistributeFood.
    public float FoodConsumptionRate { get; init; }

    // water consumed per individual per tick. Unaffected by SizeIndex.
    public float WaterConsumptionRate { get; init; }

    // prey (population count) consumed per individual per tick. Scales with SizeIndex.
    // zero means this species is not a carnivore.
    public float PreyConsumptionRate { get; init; }

    // how easily this species eats from each food subtype, on a 0 (can't eat) to 5 (trivial) scale.
    // entries left unset default to full ease (generalist that eats anything).
    public Dictionary<FoodSubtype, float> EaseOfEating { get; init; } = [];

    // ease-of-eating for a subtype, normalized to 0-1.
    // empty EaseOfEating → 1f for all subtypes (generalist backward-compat path).
    public float EffectiveEase(FoodSubtype? subtype)
    {
        if (subtype is null) return 0f;
        if (EaseOfEating.Count == 0) return 1f;
        return EaseOfEating.TryGetValue(subtype.Value, out var ease) ? ease / 5f : 0f;
    }

    // ── carnivore / predation ────────────────────────────────────────────────

    // what prey category this species represents when hunted (null = cannot be preyed upon)
    public PreyCategory? AsPreyCategory { get; init; }

    // prey categories this carnivore hunts at full satisfaction
    public HashSet<PreyCategory> PreferredPrey { get; init; } = [];

    // prey categories this carnivore will eat when preferred prey is scarce (2/3 satisfaction)
    public HashSet<PreyCategory> AcceptedPrey { get; init; } = [];

    public bool IsPredator => PreyConsumptionRate > 0;

    // ── shared traits ────────────────────────────────────────────────────────

    // fractional population growth per tick when fully satisfied
    public float ReproductionRate { get; init; } = 0.02f;

    // fractional population death per tick when fully resource-deprived
    public float StarvationRate { get; init; } = 0.05f;

    // satisfaction ratio below which the population will seek a better tile (0 = never migrate)
    public float MigrationThreshold { get; init; } = 0.5f;

    // ticks a population must wait before migrating again after it last moved (0 = no cooldown)
    public int MigrationCooldownTicks { get; init; }

    // if non-empty, this species can only migrate to tiles with one of these terrain types
    public HashSet<TerrainType> AllowedTerrains { get; init; } = [];

    // maximum population count; growth is capped here (0 = unlimited)
    public int MaxCount { get; init; }

    // tendency to escalate tension with nearby factions (0 = passive, 1 = very aggressive)
    public float WarAggression { get; init; } = 0.2f;

    // casualties inflicted per individual per tick during combat
    public float CombatStrength { get; init; } = 1.0f;

    // resistance to disease (0 = fully susceptible, 1 = immune)
    public float Immunity { get; init; } = 0.3f;

    // byproduct emitted per individual per tick
    public Dictionary<ByproductType, float> ByproductRates { get; init; } = [];
}
