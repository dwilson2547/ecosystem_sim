namespace EcosystemSim;

public class SpeciesDefinition
{
    public required string Name { get; init; }

    // ancestor species name, shared across all derived species; null means use Name
    public string? RootName { get; init; }

    // the root of the lineage, used for naming derived species
    public string EffectiveRootName => RootName ?? Name;

    // aggregate food demand per individual per tick. Split across the Ground/Brush/Canopy
    // strata at consumption time based on EaseOfEating × what's actually available on the tile —
    // see World.DistributeFood.
    public float FoodConsumptionRate { get; init; }

    // water consumed per individual per tick. Unaffected by SizeIndex — see Population.EffectiveWaterDemand.
    public float WaterConsumptionRate { get; init; }

    // how easily this species can eat from each food stratum, on a 0 (can't at all) to 5 (trivial)
    // scale — mirrors the readme's ease-of-eating table. Strata left unset default to 5 (a
    // generalist that can eat anything), so species that don't care about diet specialization
    // behave exactly as before this system existed.
    public Dictionary<ResourceType, float> EaseOfEating { get; init; } = [];

    // ease-of-eating for a stratum on a given terrain, normalized to 0-1. Terrain can make eating
    // harder (e.g. River) regardless of the species' innate skill at that stratum.
    public float EffectiveEase(ResourceType stratum, TerrainType terrain)
    {
        var baseEase = EaseOfEating.TryGetValue(stratum, out var ease) ? ease : 5f;
        return Math.Clamp(baseEase - TerrainStats.EaseOfEatingPenalty(terrain), 0f, 5f) / 5f;
    }

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
