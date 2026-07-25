namespace EcosystemSim;

public sealed class ScenarioSession
{
    private static readonly string[] RequiredDinosaurLineages =
    [
        "Triceratops",
        "Alamosaurus",
        "Parasaurolophus",
        "Tyrannosaurus",
    ];

    public required ScenarioKind Kind { get; init; }
    public required ScenarioMode Mode { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public int StartTick { get; init; }
    public int DurationDays { get; init; }
    public int InitialActionPoints { get; init; }
    public int ActionPointsRemaining { get; internal set; }
    public ScenarioStatus Status { get; internal set; } = ScenarioStatus.Active;
    public string ResultMessage { get; internal set; } = string.Empty;
    public IReadOnlyList<ScenarioObjectiveProgress> Objectives { get; private set; } = [];

    public bool IsChallenge => Mode == ScenarioMode.Challenge;
    public int ElapsedDays(WorldState state) => Math.Max(0, state.Tick - StartTick);
    public int RemainingDays(WorldState state) =>
        IsChallenge ? Math.Max(0, DurationDays - ElapsedDays(state)) : int.MaxValue;

    public void Refresh(WorldState state)
    {
        if (Kind == ScenarioKind.Sandbox)
        {
            Objectives = [];
            return;
        }

        var livingLineages = state.Map.AllPopulations()
            .Where(p => p.Count > 0)
            .Select(p => p.Species.EffectiveRootName)
            .ToHashSet();
        var livingDinosaurCount = RequiredDinosaurLineages.Count(livingLineages.Contains);

        var locustCount = state.Map.AllPopulations()
            .Where(p => p.Count > 0 && p.Species.EffectiveRootName == "Locust")
            .Sum(p => p.Count);

        var plainsGraze = state.Map.AllTiles()
            .Where(t => t.Terrain == TerrainType.Plains)
            .SelectMany(t => t.Resources)
            .Where(r => r.Type == ResourceType.Food
                     && r.FoodSubtype == FoodSubtype.Graze
                     && r.Capacity > 0f)
            .ToList();
        var grassPercent = plainsGraze.Count == 0
            ? 0f
            : plainsGraze.Average(r => r.Amount / r.Capacity) * 100f;

        Objectives =
        [
            new(
                "dinosaur-survival",
                "Dinosaur lineages alive",
                $"{livingDinosaurCount}/{RequiredDinosaurLineages.Length}",
                $"{RequiredDinosaurLineages.Length}/{RequiredDinosaurLineages.Length}",
                livingDinosaurCount == RequiredDinosaurLineages.Length),
            new(
                "locust-control",
                "Locust population",
                locustCount.ToString(),
                "≤ 500",
                locustCount <= 500),
            new(
                "grass-health",
                "Average plains grass",
                $"{grassPercent:F0}%",
                "≥ 25%",
                grassPercent >= 25f),
        ];

        if (Status != ScenarioStatus.Active || ElapsedDays(state) < DurationDays) return;

        var won = Objectives.All(o => o.IsMet);
        Status = won ? ScenarioStatus.Won : ScenarioStatus.Lost;
        ResultMessage = won
            ? "Ecosystem stabilized: the dinosaur lineages survived the locust plague."
            : "Challenge failed: one or more ecosystem objectives were missed.";
    }
}
