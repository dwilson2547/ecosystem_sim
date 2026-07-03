namespace EcosystemSim;

public enum FoodSubtype
{
    // land subtypes — matched against species EaseOfEating by terrain layer
    Graze,        // ground-level grass/sedge (Plains, River banks)
    Browse,       // mid-height shrubs and woody stems
    Fruit,        // canopy fruit and foliage (Forest dominant)
    Roots,        // underground/swamp-floor plant matter

    // ocean subtypes
    Fish,
    Shrimp,
    Crustacean,
    Squid,
    Whale,
}
