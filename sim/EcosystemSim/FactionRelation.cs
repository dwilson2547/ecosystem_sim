namespace EcosystemSim;

public class FactionRelation
{
    public required Faction Other { get; init; }
    public DiplomaticState State { get; set; } = DiplomaticState.Neutral;

    // positive = tension building toward war, negative = cooperation building toward alliance
    public float TensionScore { get; set; }

    // how many consecutive ticks this pair has been at war (drives ceasefire)
    public int TicksAtWar { get; set; }

    // player-established trade agreement; suspended while AtWar, broken if war starts
    public bool HasTradeAgreement { get; set; }
}
