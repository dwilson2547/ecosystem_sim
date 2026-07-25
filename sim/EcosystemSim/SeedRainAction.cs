namespace EcosystemSim;

public sealed class SeedRainAction : IScenarioAction
{
    public const int RainDurationDays = 45;

    public required int TileX { get; init; }
    public required int TileY { get; init; }
    public string Name => "Seed rain";
    public int Cost => 3;
    public bool SupportsScenario(ScenarioKind kind) =>
        kind is ScenarioKind.Sandbox or ScenarioKind.DroughtRecovery;

    public bool CanExecute(WorldState state, out string error)
    {
        var tile = state.Map.GetTile(TileX, TileY);
        if (TerrainStats.IsOcean(tile.Terrain))
        {
            error = "Rain seeding must be initiated from a land tile.";
            return false;
        }
        if (state.CurrentWeather == Weather.Rainy && state.WeatherTicksRemaining > 0)
        {
            error = "A seeded rain spell is already active.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    public string Execute(WorldState state)
    {
        state.CurrentWeather = Weather.Rainy;
        state.WeatherTicksRemaining = RainDurationDays;
        return $"Seeded {RainDurationDays} days of rain from ({TileX},{TileY}).";
    }
}
