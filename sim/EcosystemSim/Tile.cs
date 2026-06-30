namespace EcosystemSim;

public class Tile
{
    public int X { get; init; }
    public int Y { get; init; }
    public List<ResourcePool> Resources { get; init; } = [];
    public List<Population> Populations { get; init; } = [];

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

    // future: TerrainType, Climate, Elevation, FertilityModifier, etc.
}
