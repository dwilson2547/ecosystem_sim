namespace EcosystemSim;

public enum FoodSubtype
{
    // Terrestrial
    Graze,       // grass / ground plants  — Plains, Desert, River
    Browse,      // leaves / shrubs        — Forest, Highland, River
    Fruit,       // berries / canopy fruit — Forest, Swamp
    Roots,       // tubers / fungi         — Swamp

    // Marine
    Fish,        // schooling fish         — River (freshwater), ShallowOcean, DeepOcean
    Shrimp,      // krill / small shrimp   — ShallowOcean
    Crustacean,  // crabs / lobster        — ShallowOcean
    Squid,       // cephalopods            — DeepOcean
    Whale,       // large marine prey      — DeepOcean
}
