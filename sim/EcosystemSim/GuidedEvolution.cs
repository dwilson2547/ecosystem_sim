namespace EcosystemSim;

public enum GuidedEvolutionTrait
{
    Larger,
    Smaller,
    Immunity,
}

public sealed record GuidedEvolutionResult(
    bool Success,
    string Message,
    Population? CreatedPopulation = null);
