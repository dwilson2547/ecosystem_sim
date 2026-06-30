namespace EcosystemSim;

public class EstablishTradeCommand : IWorldCommand
{
    public required Faction FactionA { get; init; }
    public required Faction FactionB { get; init; }

    public void Execute(WorldState state)
    {
        EnsureRelation(FactionA, FactionB);
        EnsureRelation(FactionB, FactionA);
        FactionA.Relations[FactionB].HasTradeAgreement = true;
        FactionB.Relations[FactionA].HasTradeAgreement = true;
    }

    private static void EnsureRelation(Faction a, Faction b)
    {
        if (!a.Relations.ContainsKey(b))
            a.Relations[b] = new FactionRelation { Other = b };
    }
}
