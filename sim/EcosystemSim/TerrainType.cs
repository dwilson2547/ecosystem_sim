namespace EcosystemSim;

public enum TerrainType { Plains, Forest, Swamp, Desert, Highland, River, ShallowOcean, DeepOcean }

// a min/max percentage (0-100) sampled once per tile at world-seed time
public readonly record struct FloatRange(float Min, float Max)
{
    public float Sample(Random random) => Min + random.NextSingle() * (Max - Min);
}

public static class TerrainStats
{
    public static bool IsOcean(TerrainType terrain) =>
        terrain is TerrainType.ShallowOcean or TerrainType.DeepOcean;

    // relative cost to enter a tile during migration (1.0 = baseline)
    private static readonly Dictionary<TerrainType, float> _migrationCost = new()
    {
        [TerrainType.Plains]      = 1.0f,
        [TerrainType.Forest]      = 1.4f,
        [TerrainType.Swamp]       = 1.8f,
        [TerrainType.Desert]      = 0.8f,
        [TerrainType.Highland]    = 1.5f,
        [TerrainType.River]       = 1.0f,
        [TerrainType.ShallowOcean] = 1.0f,
        [TerrainType.DeepOcean]   = 1.2f,
    };

    public static float MigrationCostOf(TerrainType terrain) => _migrationCost[terrain];

    // percentage composition of each FoodSubtype on a terrain, sampled per tile at seed time
    // then normalized so the sampled values sum to 100%. Land terrains use Graze/Browse/Fruit/Roots;
    // ocean terrains use Fish/Shrimp/Crustacean/Squid/Whale.
    public static readonly Dictionary<TerrainType, Dictionary<FoodSubtype, FloatRange>> FoodComposition = new()
    {
        [TerrainType.Plains]   = new() { [FoodSubtype.Graze]  = new(60, 75), [FoodSubtype.Browse] = new(20, 35), [FoodSubtype.Fruit]      = new(0,  5)  },
        [TerrainType.Forest]   = new() { [FoodSubtype.Graze]  = new(12, 20), [FoodSubtype.Browse] = new(12, 20), [FoodSubtype.Fruit]      = new(60, 75) },
        [TerrainType.Swamp]    = new() { [FoodSubtype.Roots]  = new(40, 55), [FoodSubtype.Browse] = new(40, 55), [FoodSubtype.Graze]      = new(0,  10) },
        [TerrainType.Desert]   = new() { [FoodSubtype.Graze]  = new(5,  15), [FoodSubtype.Browse] = new(65, 85), [FoodSubtype.Fruit]      = new(0,  10) },
        [TerrainType.Highland] = new() { [FoodSubtype.Graze]  = new(25, 40), [FoodSubtype.Browse] = new(55, 70), [FoodSubtype.Fruit]      = new(0,   5) },
        [TerrainType.River]    = new() { [FoodSubtype.Graze]  = new(80,100), [FoodSubtype.Browse] = new(5,  10), [FoodSubtype.Fish]       = new(0,  10) },
        [TerrainType.ShallowOcean] = new() { [FoodSubtype.Fish] = new(30, 40), [FoodSubtype.Shrimp] = new(40, 55), [FoodSubtype.Crustacean] = new(15, 25) },
        [TerrainType.DeepOcean]    = new() { [FoodSubtype.Fish] = new(20, 30), [FoodSubtype.Squid]  = new(50, 65), [FoodSubtype.Whale]      = new(10, 20) },
    };

    // total food regen/capacity budget per terrain, split across food subtypes by FoodComposition
    private static readonly Dictionary<TerrainType, (float Regen, float Capacity)> _foodBudget = new()
    {
        [TerrainType.Plains]      = (10f, 200f),
        [TerrainType.Forest]      = (15f, 300f),
        [TerrainType.Swamp]       = ( 7f, 140f),
        [TerrainType.Desert]      = ( 3f,  60f),
        [TerrainType.Highland]    = ( 8f, 160f),
        [TerrainType.River]       = (12f, 240f),
        [TerrainType.ShallowOcean] = (20f, 400f),
        [TerrainType.DeepOcean]   = (15f, 300f),
    };

    // River is the reference "full" water tile; land terrains get a percentage of its regen/capacity.
    // Ocean terrains have no water pool.
    private static readonly (float Regen, float Capacity) _riverWaterBase = (15f, 200f);

    private static readonly Dictionary<TerrainType, FloatRange> _waterPercentOfRiver = new()
    {
        [TerrainType.Desert]   = new FloatRange(0,   5),
        [TerrainType.Plains]   = new FloatRange(8,  12),
        [TerrainType.Forest]   = new FloatRange(8,  12),
        [TerrainType.Swamp]    = new FloatRange(12, 18),
        [TerrainType.Highland] = new FloatRange(3,   7),
        [TerrainType.River]    = new FloatRange(100, 100),
    };

    // a terrain whose defining food subtype stays depleted long enough degrades into a lesser terrain.
    // Forest → Plains when Fruit (canopy) stays below 10% of capacity for 60 sustained ticks.
    public static readonly Dictionary<TerrainType, (FoodSubtype TriggerSubtype, TerrainType DegradesTo)> Degradation = new()
    {
        [TerrainType.Forest] = (FoodSubtype.Fruit, TerrainType.Plains),
    };

    // builds a fresh set of Food + Water resource pools for a terrain, sampling FoodComposition
    // and water-percentage ranges. Used at world-seed time and when terrain degrades at runtime.
    public static List<ResourcePool> BuildResourcePools(TerrainType terrain, Random random)
    {
        var pools = new List<ResourcePool>();

        var (foodRegen, foodCapacity) = _foodBudget[terrain];
        var comp    = FoodComposition[terrain];
        var samples = comp.ToDictionary(kv => kv.Key, kv => kv.Value.Sample(random));
        var total   = samples.Values.Sum();

        foreach (var (subtype, share) in samples)
        {
            var regen = foodRegen * share / total;
            var cap   = foodCapacity * share / total;
            pools.Add(new ResourcePool
            {
                Type         = ResourceType.Food,
                FoodSubtype  = subtype,
                Amount       = regen * 10f,
                Capacity     = cap,
                RegenPerTick = regen,
            });
        }

        // ocean tiles have no fresh water
        if (!IsOcean(terrain) && _waterPercentOfRiver.TryGetValue(terrain, out var waterRange))
        {
            var waterPct   = waterRange.Sample(random) / 100f;
            var waterRegen = _riverWaterBase.Regen    * waterPct;
            var waterCap   = _riverWaterBase.Capacity * waterPct;
            pools.Add(new ResourcePool
            {
                Type         = ResourceType.Water,
                Amount       = waterRegen * 10f,
                Capacity     = waterCap,
                RegenPerTick = waterRegen,
            });
        }

        return pools;
    }
}
