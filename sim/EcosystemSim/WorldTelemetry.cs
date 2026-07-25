namespace EcosystemSim;

public enum WorldEventSeverity
{
    Info,
    Warning,
    Critical,
}

public sealed record WorldEvent(
    int Tick,
    WorldEventSeverity Severity,
    string Message,
    int? TileX = null,
    int? TileY = null);

public sealed record WorldHistorySample(
    int Tick,
    IReadOnlyDictionary<string, int> LineagePopulations,
    IReadOnlyDictionary<string, float> ObjectiveProgress);
