namespace EcosystemSim;

public enum TerrainType { Plains, Forest, Swamp, Desert, Highland, River }

public static class TerrainStats
{
    // relative cost to enter a tile during migration (1.0 = baseline)
    // used as a tiebreaker when multiple destinations have similar resources
    private static readonly Dictionary<TerrainType, float> _migrationCost = new()
    {
        [TerrainType.Plains]   = 1.0f,
        [TerrainType.Forest]   = 1.4f,
        [TerrainType.Swamp]    = 1.8f,
        [TerrainType.Desert]   = 0.8f,
        [TerrainType.Highland] = 1.5f,
        [TerrainType.River]    = 1.0f,
    };

    public static float MigrationCostOf(TerrainType terrain) => _migrationCost[terrain];
}
