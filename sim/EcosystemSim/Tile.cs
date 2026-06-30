namespace EcosystemSim;

public class Tile
{
    public int X { get; init; }
    public int Y { get; init; }
    public List<ResourcePool> Resources { get; init; } = [];
    public List<Population> Populations { get; init; } = [];
    public List<ByproductPool> Byproducts { get; init; } = [];

    public void AddPopulation(Population pop)
    {
        pop.CurrentTile = this;
        Populations.Add(pop);
    }

    internal void RemovePopulation(Population pop)
    {
        if (Populations.Remove(pop))
            pop.CurrentTile = null;
    }

    public ByproductPool GetOrAddByproduct(ByproductType type, float decayRate = 0.10f, float capacity = 200f)
    {
        var pool = Byproducts.FirstOrDefault(b => b.Type == type);
        if (pool is null)
        {
            pool = new ByproductPool { Type = type, DecayRate = decayRate, Capacity = capacity };
            Byproducts.Add(pool);
        }
        return pool;
    }

    // future: TerrainType, Climate, Elevation, FertilityModifier, etc.
}
