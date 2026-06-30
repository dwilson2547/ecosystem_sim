namespace EcosystemSim;

public class Population
{
    public required SpeciesDefinition Species { get; init; }
    public int Count { get; set; }

    // worst resource satisfaction seen this tick (0–1), reset each tick
    public float LastSatisfaction { get; internal set; } = 1f;

    // future state: faction, territory, morale, aggression level, etc.
}
