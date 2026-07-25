namespace EcosystemSim;

public sealed record ScenarioObjectiveProgress(
    string Id,
    string Label,
    string CurrentValue,
    string TargetValue,
    bool IsMet,
    float ProgressRatio);
