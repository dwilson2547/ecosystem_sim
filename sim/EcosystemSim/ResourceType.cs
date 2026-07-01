namespace EcosystemSim;

public enum ResourceType
{
    // food strata — how much of each a species can actually eat is governed by
    // SpeciesDefinition.EaseOfEating, not by the resource type itself
    Ground,
    Brush,
    Canopy,
    Water
}
