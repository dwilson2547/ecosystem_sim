namespace EcosystemSim;

public class Disease
{
    public required string Name { get; init; }

    // fraction of infected individuals that die per tick
    // effective deaths = Count * InfectionLevel * MortalityRate * (1 - Immunity)
    public float MortalityRate { get; init; } = 0.03f;

    // how fast InfectionLevel climbs in an exposed population per tick
    // effective spread = source.InfectionLevel * SpreadRate * (1 - target.Immunity) * densityFactor
    public float SpreadRate { get; init; } = 0.15f;

    // base InfectionLevel decay per tick before immunity modifier
    // effective recovery = RecoveryRate + Immunity * 0.05
    public float RecoveryRate { get; init; } = 0.02f;
}
