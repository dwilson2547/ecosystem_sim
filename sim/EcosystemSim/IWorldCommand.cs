namespace EcosystemSim;

public interface IWorldCommand
{
    void Execute(WorldState state);
}
