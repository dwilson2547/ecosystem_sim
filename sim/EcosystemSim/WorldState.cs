namespace EcosystemSim;

public class WorldState
{
    public int Tick { get; set; }
    public Season CurrentSeason => (Season)((Tick / World.DaysPerSeason) % 4);
    public int SeasonDay => Tick % World.DaysPerSeason;
    public int DayOfSeason => SeasonDay + 1;
    public int DayOfYear => Tick % World.DaysPerYear + 1;
    public int Year => Tick / World.DaysPerYear + 1;
    public Weather CurrentWeather { get; set; } = Weather.Normal;
    public int WeatherTicksRemaining { get; set; }
    public ScenarioSession? Scenario { get; set; }
    public WorldMap Map { get; init; } = new(10, 10);
    public List<Faction> Factions { get; init; } = [];
}
