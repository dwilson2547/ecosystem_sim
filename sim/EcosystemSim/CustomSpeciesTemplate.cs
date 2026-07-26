using System.Text.Json.Serialization;

namespace EcosystemSim;

public sealed class CustomSpeciesTemplate
{
    public const int TraitPointBudget = 8;

    public string Name { get; set; } = string.Empty;
    public string BaseSpeciesName { get; set; } = string.Empty;
    public int SizeSteps { get; set; }
    public int ImmunitySteps { get; set; }
    public int BreedingSteps { get; set; }
    public int AggressionSteps { get; set; }

    [JsonIgnore]
    public int PointsSpent =>
        Math.Abs(SizeSteps)
        + ImmunitySteps
        + Math.Abs(BreedingSteps)
        + Math.Abs(AggressionSteps);

    [JsonIgnore]
    public float SizeScale => 1f + SizeSteps * 0.15f;
    [JsonIgnore]
    public float BreedingScale => 1f + BreedingSteps * 0.15f;

    public bool TryValidate(
        out string reason,
        IEnumerable<string>? reservedNames = null)
    {
        var name = Name.Trim();
        if (name.Length is < 3 or > 28)
        {
            reason = "Species names must be 3–28 characters.";
            return false;
        }
        if (name.Any(c => !char.IsLetterOrDigit(c) && c is not ' ' and not '-' and not '\''))
        {
            reason = "Use only letters, numbers, spaces, hyphens, or apostrophes.";
            return false;
        }
        if (string.IsNullOrWhiteSpace(BaseSpeciesName))
        {
            reason = "Choose a base species.";
            return false;
        }
        if (SizeSteps is < -2 or > 2
            || ImmunitySteps is < 0 or > 3
            || BreedingSteps is < -2 or > 2
            || AggressionSteps is < -2 or > 2)
        {
            reason = "One or more trait values are outside the supported range.";
            return false;
        }
        if (PointsSpent > TraitPointBudget)
        {
            reason = $"Trait budget exceeded: {PointsSpent}/{TraitPointBudget} points.";
            return false;
        }
        if (reservedNames?.Any(n =>
                string.Equals(n, name, StringComparison.OrdinalIgnoreCase)) == true)
        {
            reason = "That species name is already in use.";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    public SpeciesDefinition BuildSpecies(SpeciesDefinition baseSpecies)
    {
        if (!string.Equals(
                baseSpecies.Name,
                BaseSpeciesName,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"Expected base species '{BaseSpeciesName}', got '{baseSpecies.Name}'.",
                nameof(baseSpecies));
        }
        if (!TryValidate(out var reason))
            throw new InvalidOperationException(reason);

        var sizeScale = SizeScale;
        return new SpeciesDefinition
        {
            Name = Name.Trim(),
            RootName = Name.Trim(),
            FoodConsumptionRate = baseSpecies.FoodConsumptionRate * sizeScale,
            WaterConsumptionRate = baseSpecies.WaterConsumptionRate,
            PreyConsumptionRate = baseSpecies.PreyConsumptionRate * sizeScale,
            EaseOfEating = new Dictionary<FoodSubtype, float>(baseSpecies.EaseOfEating),
            AsPreyCategory = baseSpecies.AsPreyCategory,
            HerdDefense = baseSpecies.HerdDefense,
            HuntDifficulty = baseSpecies.HuntDifficulty,
            PreferredPrey = new HashSet<PreyCategory>(baseSpecies.PreferredPrey),
            AcceptedPrey = new HashSet<PreyCategory>(baseSpecies.AcceptedPrey),
            PursuesPreyWhenFed = baseSpecies.PursuesPreyWhenFed,
            PollinationBoost = baseSpecies.PollinationBoost,
            BreedingRate = baseSpecies.BreedingRate * BreedingScale,
            BreedingSeasons = new HashSet<Season>(baseSpecies.BreedingSeasons),
            BreedingDayOfSeason = baseSpecies.BreedingDayOfSeason,
            FoodDeprivationMortalityRate = baseSpecies.FoodDeprivationMortalityRate,
            FoodDeprivationToleranceDays = baseSpecies.FoodDeprivationToleranceDays,
            WaterDeprivationMortalityRate = baseSpecies.WaterDeprivationMortalityRate,
            WaterDeprivationToleranceDays = baseSpecies.WaterDeprivationToleranceDays,
            MigrationThreshold = baseSpecies.MigrationThreshold,
            MigrationCooldownTicks = baseSpecies.MigrationCooldownTicks,
            ViewRadius = baseSpecies.ViewRadius,
            AllowedTerrains = new HashSet<TerrainType>(baseSpecies.AllowedTerrains),
            MaxCount = baseSpecies.MaxCount,
            WarAggression = Math.Clamp(
                baseSpecies.WarAggression + AggressionSteps * 0.1f,
                0f,
                1f),
            CombatStrength = baseSpecies.CombatStrength * MathF.Sqrt(sizeScale),
            Immunity = MathF.Min(1f, baseSpecies.Immunity + ImmunitySteps * 0.05f),
            ByproductRates = baseSpecies.ByproductRates.ToDictionary(
                pair => pair.Key,
                pair => pair.Value * sizeScale),
        };
    }
}
