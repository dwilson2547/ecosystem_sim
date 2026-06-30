namespace EcosystemSim;

public class SpeciesDefinition
{
    public required string Name { get; init; }

    // resource consumed per individual per tick — add new ResourceType values to extend
    public Dictionary<ResourceType, float> ConsumptionRates { get; init; } = [];

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
