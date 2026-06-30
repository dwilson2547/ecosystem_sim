namespace EcosystemSim;

public class WorldState
{
    public int Tick { get; set; }
    public List<ResourcePool> Resources { get; init; } = [];
    public List<Population> Populations { get; init; } = [];
}
