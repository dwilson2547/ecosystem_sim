namespace EcosystemSim;

public class Population
{
    public required SpeciesDefinition Species { get; set; }
    public int Count { get; set; }

    // worst resource satisfaction seen this tick (0–1), reset each tick
    public float LastSatisfaction { get; internal set; } = 1f;

    // set by Tile.AddPopulation / Tile.RemovePopulation during placement and migration
    public Tile? CurrentTile { get; internal set; }

    // set by Faction.AddPopulation — null means unfactioned (e.g. in tests)
    public Faction? Faction { get; internal set; }

    // active disease and current infection level (0 = healthy, 1 = fully infected)
    public Disease? Disease { get; set; }
    public float InfectionLevel { get; set; }

    // ── evolution ────────────────────────────────────────────────────────────

    // >1 = grown larger (stronger + hungrier), <1 = shrunk (weaker + leaner)
    public float SizeIndex { get; set; } = 1.0f;

    // accumulates +1 per tick of abundance, -1 per tick of scarcity;
    // each ±50-tick crossing shifts SizeIndex by 0.05 and resets to zero
    public float SizePressure { get; set; }

    // permanently gained disease resistance (0–0.5); never decreases
    public float ImmunityDelta { get; set; }

    // accumulates +1 per tick while infected; each 30-tick crossing adds 0.02 to ImmunityDelta
    public float ImmunityPressure { get; set; }

    // ticks spent stranded on River terrain; decays by 1/tick when off water.
    // past WaterSurvivalThreshold the population starts drowning.
    public float WaterExposure { get; set; }

    // ticks remaining before this population can migrate again; decrements each tick
    public int MigrationCooldown { get; internal set; }

    // set during Migrate(); cleared at the start of every tick
    public bool JustMigrated { get; internal set; }

    // fractional starvation deaths carried across ticks; applied when accumulator ≥ 1
    public float StarvationAccumulator { get; set; }

    // ── effective stats (base species trait + evolution modifier) ─────────────

    public float EffectiveCombatStrength => Species.CombatStrength * MathF.Sqrt(SizeIndex);

    public float EffectiveImmunity => MathF.Min(1f, Species.Immunity + ImmunityDelta);

    // food and prey consumption scale with size; water does not
    public float EffectiveFoodDemand  => Species.FoodConsumptionRate  * SizeIndex;
    public float EffectiveWaterDemand => Species.WaterConsumptionRate;
    public float EffectivePreyDemand  => Species.PreyConsumptionRate  * SizeIndex;
}
