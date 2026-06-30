namespace EcosystemSim;

public class Population
{
    public required SpeciesDefinition Species { get; init; }
    public int Count { get; set; }

    // worst resource satisfaction seen this tick (0–1), reset each tick
    public float LastSatisfaction { get; internal set; } = 1f;

    // set by Tile.AddPopulation / Tile.RemovePopulation during placement and migration
    public Tile? CurrentTile { get; internal set; }

    // set by Faction.AddPopulation — null means unfactioned (e.g. in tests)
    public Faction? Faction { get; internal set; }

    // future state: morale, aggression level, etc.
}
