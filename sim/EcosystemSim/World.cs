namespace EcosystemSim;

public class World
{
    public WorldState State { get; private set; } = new();

    public void Tick()
    {
        State.Tick++;
    }

    public void Apply(IWorldCommand command)
    {
        command.Execute(State);
    }
}

public class WorldState
{
    public int Tick { get; set; }
    public List<Species> Species { get; set; } = [];
}

public class Species
{
    public required string Name { get; set; }
    public int Population { get; set; }
}

public interface IWorldCommand
{
    void Execute(WorldState state);
}
