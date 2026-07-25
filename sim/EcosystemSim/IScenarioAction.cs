namespace EcosystemSim;

public interface IScenarioAction
{
    string Name { get; }
    int Cost { get; }
    bool SupportsScenario(ScenarioKind kind);
    bool CanExecute(WorldState state, out string error);
    string Execute(WorldState state);
}

public sealed record ScenarioActionResult(bool Success, string Message, int ActionPointsRemaining);
