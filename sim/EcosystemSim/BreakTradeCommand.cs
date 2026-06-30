namespace EcosystemSim;

public class BreakTradeCommand : IWorldCommand
{
    public required Faction FactionA { get; init; }
    public required Faction FactionB { get; init; }

    public void Execute(WorldState state)
    {
        if (FactionA.Relations.TryGetValue(FactionB, out var ab)) ab.HasTradeAgreement = false;
        if (FactionB.Relations.TryGetValue(FactionA, out var ba)) ba.HasTradeAgreement = false;
    }
}
