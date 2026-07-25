namespace EcosystemSim;

public sealed record TerrainSuccessionSettings
{
    public float DegradationThresholdRatio { get; init; } = 0.06f;
    public int DegradationPressureDays { get; init; } = 90;
    public float DegradationDailyChance { get; init; } = 1f;

    public float RecoveryVegetationRatio { get; init; } = 0.50f;
    public float RecoveryFertilizerAmount { get; init; } = 5f;
    public int RecoveryPressureDays { get; init; } = 30;
    public float RecoveryDailyChance { get; init; } = 0.10f;

    public float PressureDecayPerDay { get; init; } = 1f;

    internal void Validate()
    {
        if (DegradationThresholdRatio is < 0f or > 1f)
            throw new ArgumentOutOfRangeException(nameof(DegradationThresholdRatio));
        if (RecoveryVegetationRatio is < 0f or > 1f)
            throw new ArgumentOutOfRangeException(nameof(RecoveryVegetationRatio));
        if (DegradationDailyChance is < 0f or > 1f)
            throw new ArgumentOutOfRangeException(nameof(DegradationDailyChance));
        if (RecoveryDailyChance is < 0f or > 1f)
            throw new ArgumentOutOfRangeException(nameof(RecoveryDailyChance));
        if (DegradationPressureDays <= 0)
            throw new ArgumentOutOfRangeException(nameof(DegradationPressureDays));
        if (RecoveryPressureDays <= 0)
            throw new ArgumentOutOfRangeException(nameof(RecoveryPressureDays));
        if (RecoveryFertilizerAmount < 0f)
            throw new ArgumentOutOfRangeException(nameof(RecoveryFertilizerAmount));
        if (PressureDecayPerDay < 0f)
            throw new ArgumentOutOfRangeException(nameof(PressureDecayPerDay));
    }
}
