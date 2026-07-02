namespace EcosystemSim;

public class SpeciesDefinition
{
    public required string Name { get; init; }

    // ancestor species name, shared across all derived species; null means use Name
    public string? RootName { get; init; }

    // the root of the lineage, used for naming derived species
    public string EffectiveRootName => RootName ?? Name;

    // resource consumed per individual per tick — add new ResourceType values to extend
    public Dictionary<ResourceType, float> ConsumptionRates { get; init; } = [];

    // food subtypes this species actively seeks (full satisfaction when eating these)
    // empty = eats any food pool at full satisfaction (backward-compat for untyped species)
    public HashSet<FoodSubtype> FoodPreferences { get; init; } = [];

    // food subtypes this species will eat if preferred food is scarce (2/3 satisfaction)
    public HashSet<FoodSubtype> AcceptedFoods { get; init; } = [];

    // what prey category this species represents when hunted (null = cannot be preyed upon)
    public PreyCategory? AsPreyCategory { get; init; } = null;

    // prey categories this carnivore actively hunts (full satisfaction)
    // empty + PreyConsumptionRate > 0 = hunts any prey at full satisfaction
    public HashSet<PreyCategory> PreferredPrey { get; init; } = [];

    // prey categories this carnivore will eat if preferred prey is scarce (2/3 satisfaction)
    public HashSet<PreyCategory> AcceptedPrey { get; init; } = [];

    // convenience accessor — true if this species is a predator
    public bool IsPredator => ConsumptionRates.ContainsKey(ResourceType.Prey);

    // fractional population growth per tick when fully satisfied
    public float ReproductionRate { get; init; } = 0.02f;

    // fractional population death per tick when fully resource-deprived
    public float StarvationRate { get; init; } = 0.05f;

    // satisfaction ratio below which the population will seek a better tile (0 = never migrate)
    public float MigrationThreshold { get; init; } = 0.5f;

    // tendency to escalate tension with nearby factions (0 = passive, 1 = very aggressive)
    public float WarAggression { get; init; } = 0.2f;

    // casualties inflicted per individual per tick during combat
    public float CombatStrength { get; init; } = 1.0f;

    // resistance to disease (0 = fully susceptible, 1 = immune)
    // scales both mortality reduction and recovery speed
    public float Immunity { get; init; } = 0.3f;

    // byproduct emitted per individual per tick (e.g. Fertilizer from herbivores)
    public Dictionary<ByproductType, float> ByproductRates { get; init; } = [];

    // future traits: TechAffinity, TerritorialRange, HerdInstinct, etc.
}
