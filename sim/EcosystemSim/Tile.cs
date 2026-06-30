namespace EcosystemSim;

public class Tile
{
    public int X { get; init; }
    public int Y { get; init; }
    public List<ResourcePool> Resources { get; init; } = [];
    public List<Population> Populations { get; init; } = [];

    // future: TerrainType, Climate, Elevation, FertilityModifier, etc.
}
