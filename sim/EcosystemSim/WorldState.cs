namespace EcosystemSim;

public class WorldState
{
    public int Tick { get; set; }
    public WorldMap Map { get; init; } = new(10, 10);
}
