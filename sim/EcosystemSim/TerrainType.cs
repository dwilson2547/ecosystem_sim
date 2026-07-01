namespace EcosystemSim;

public enum TerrainType { Plains, Forest, Swamp, Desert, Highland, River }

// a min/max percentage (0-100) sampled once per tile at world-seed time
public readonly record struct FloatRange(float Min, float Max)
{
    public float Sample(Random random) => Min + random.NextSingle() * (Max - Min);
}

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

    // flat penalty subtracted from every species' per-stratum ease-of-eating (0-5 scale) on this
    // terrain. River is awkward to graze from while wading through it; everything else is normal
    // dry ground and gets no penalty.
    private static readonly Dictionary<TerrainType, float> _easeOfEatingPenalty = new()
    {
        [TerrainType.River] = 1.0f,
    };

    public static float EaseOfEatingPenalty(TerrainType terrain) => _easeOfEatingPenalty.GetValueOrDefault(terrain);

    // min/max percentage range (0-100) each food stratum can occupy on this terrain. Sampled
    // independently per tile at world-seed time, then normalized so the three sampled values sum
    // to 100% — a terrain with a wide range (e.g. River ground 80-100) dominates the tile's
    // composition while still leaving room for tile-to-tile variety.
    public static readonly Dictionary<TerrainType, (FloatRange Ground, FloatRange Brush, FloatRange Canopy)> FoodComposition = new()
    {
        // mostly ground cover, some brush, almost no canopy
        [TerrainType.Plains]   = (new FloatRange(60, 75), new FloatRange(20, 35), new FloatRange(0, 5)),
        // mostly canopy, much less brush and ground
        [TerrainType.Forest]   = (new FloatRange(12, 20), new FloatRange(12, 20), new FloatRange(60, 75)),
        // nearly no ground; what's there is roughly split between brush and canopy
        [TerrainType.Swamp]    = (new FloatRange(0, 10),  new FloatRange(40, 55), new FloatRange(40, 55)),
        // nearly no food at all; what little there is skews brush
        [TerrainType.Desert]   = (new FloatRange(5, 15),  new FloatRange(65, 85), new FloatRange(0, 10)),
        // rich in brush, medium ground, almost no canopy
        [TerrainType.Highland] = (new FloatRange(25, 40), new FloatRange(55, 70), new FloatRange(0, 5)),
        // almost entirely ground-level (reeds/silt), a little brush, occasional overhanging canopy
        [TerrainType.River]    = (new FloatRange(80, 100), new FloatRange(5, 10), new FloatRange(0, 10)),
    };

    // total food regen/capacity budget per terrain, split across Ground/Brush/Canopy by FoodComposition
    private static readonly Dictionary<TerrainType, (float Regen, float Capacity)> _foodBudget = new()
    {
        [TerrainType.Plains]   = (10f, 200f),
        [TerrainType.Forest]   = (15f, 300f),
        [TerrainType.Swamp]    = ( 7f, 140f),
        [TerrainType.Desert]   = ( 3f,  60f),
        [TerrainType.Highland] = ( 8f, 160f),
        [TerrainType.River]    = (12f, 240f),
    };

    // River is the reference "full" water tile; every other terrain gets a percentage of River's
    // regen/capacity, sampled per tile the same way as FoodComposition
    private static readonly (float Regen, float Capacity) _riverWaterBase = (15f, 200f);

    private static readonly Dictionary<TerrainType, FloatRange> _waterPercentOfRiver = new()
    {
        [TerrainType.Desert]   = new FloatRange(0, 5),
        [TerrainType.Plains]   = new FloatRange(8, 12),
        [TerrainType.Forest]   = new FloatRange(8, 12),
        [TerrainType.Swamp]    = new FloatRange(12, 18),
        [TerrainType.Highland] = new FloatRange(3, 7),
        [TerrainType.River]    = new FloatRange(100, 100),
    };

    // a terrain whose defining food stratum stays denuded long enough degrades into a lesser
    // terrain — e.g. a forest stripped of its canopy by sustained heavy browsing becomes plains.
    // See World.ApplyTerrainDegradation.
    public static readonly Dictionary<TerrainType, (ResourceType TriggerStratum, TerrainType DegradesTo)> Degradation = new()
    {
        [TerrainType.Forest] = (ResourceType.Canopy, TerrainType.Plains),
    };

    // builds a fresh set of Ground/Brush/Canopy/Water resource pools for a terrain, sampling
    // FoodComposition and water-percentage ranges. Used both at world-seed time (WorldSeeder /
    // DemoWorldSeeder) and when a tile's terrain changes at runtime, so both paths produce
    // identically-structured tiles.
    public static List<ResourcePool> BuildResourcePools(TerrainType terrain, Random random)
    {
        var pools = new List<ResourcePool>();

        var (foodRegen, foodCapacity) = _foodBudget[terrain];
        var comp   = FoodComposition[terrain];
        var ground = comp.Ground.Sample(random);
        var brush  = comp.Brush.Sample(random);
        var canopy = comp.Canopy.Sample(random);
        var total  = ground + brush + canopy;

        void AddFood(ResourceType type, float share)
        {
            var regen = foodRegen * share / total;
            var cap   = foodCapacity * share / total;
            pools.Add(new ResourcePool { Type = type, Amount = regen * 10f, Capacity = cap, RegenPerTick = regen });
        }

        AddFood(ResourceType.Ground, ground);
        AddFood(ResourceType.Brush,  brush);
        AddFood(ResourceType.Canopy, canopy);

        var waterPct   = _waterPercentOfRiver[terrain].Sample(random) / 100f;
        var waterRegen = _riverWaterBase.Regen    * waterPct;
        var waterCap   = _riverWaterBase.Capacity * waterPct;
        pools.Add(new ResourcePool { Type = ResourceType.Water, Amount = waterRegen * 10f, Capacity = waterCap, RegenPerTick = waterRegen });

        return pools;
    }
}
