namespace EcosystemSim;

public class FactionRelation
{
    public required Faction Other { get; init; }
    public DiplomaticState State { get; set; } = DiplomaticState.Neutral;

    // positive = tension building toward war, negative = cooperation building toward alliance
    public float TensionScore { get; set; }
}
