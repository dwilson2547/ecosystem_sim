namespace EcosystemSim;

public enum ResourceType
{
    Food,   // any ResourcePool tagged with a FoodSubtype; distributed via ease-of-eating
    Water,
    Prey,   // consumed directly from other populations, not from resource pools
}
