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

    // future traits: WarAggression, TechAffinity, TerritorialRange, HerdInstinct, etc.
}
