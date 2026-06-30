namespace EcosystemSim;

public class WorldState
{
    public int Tick { get; set; }
    public Season CurrentSeason { get; set; } = Season.Spring;
    public int SeasonTick { get; set; }
    public WorldMap Map { get; init; } = new(10, 10);
    public List<Faction> Factions { get; init; } = [];
}
