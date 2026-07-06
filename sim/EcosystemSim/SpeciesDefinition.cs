namespace EcosystemSim;

public class SpeciesDefinition
{
    public required string Name { get; init; }

    // ancestor species name, shared across all derived species; null means use Name
    public string? RootName { get; init; }

    // the root of the lineage, used for naming derived species
    public string EffectiveRootName => RootName ?? Name;

    // aggregate food demand per individual per tick. Split across food pools at consumption time
    // based on EaseOfEating × what's actually available — see World.DistributeFood.
    public float FoodConsumptionRate { get; init; }

    // water consumed per individual per tick. Unaffected by SizeIndex.
    public float WaterConsumptionRate { get; init; }

    // prey (population count) consumed per individual per tick. Scales with SizeIndex.
    // zero means this species is not a carnivore.
    public float PreyConsumptionRate { get; init; }

    // how easily this species eats from each food subtype, on a 0 (can't eat) to 5 (trivial) scale.
    // entries left unset default to full ease (generalist that eats anything).
    public Dictionary<FoodSubtype, float> EaseOfEating { get; init; } = [];

    // ease-of-eating for a subtype, normalized to 0-1.
    // empty EaseOfEating → 1f for all subtypes (generalist backward-compat path).
    public float EffectiveEase(FoodSubtype? subtype)
    {
        if (subtype is null) return 0f;
        if (EaseOfEating.Count == 0) return 1f;
        return EaseOfEating.TryGetValue(subtype.Value, out var ease) ? ease / 5f : 0f;
    }

    // ── carnivore / predation ────────────────────────────────────────────────

    // what prey category this species represents when hunted (null = cannot be preyed upon)
    public PreyCategory? AsPreyCategory { get; init; }

    // herd defense: safety in numbers. A large herd on a tile collectively deters predators (mobbing,
    // vigilance), cutting the fraction of it a predator can take that tick. 0 = no defense (default);
    // approaches this ceiling as the herd grows (half-strength at HerdDefenseHalfSaturation head). This
    // keeps a big herd from being ground down into the vulnerable-tail regime, giving a survival floor
    // without inflating the ceiling — a thinned herd loses the bonus and is hunted normally.
    public float HerdDefense { get; init; }

    // hunt difficulty: how hard an individual is to catch (0 = easily caught, the default; higher =
    // more escapes/armour). Predation is modeled as a hunt with success probability = catchability
    // (1 - HuntDifficulty): each tick the fraction of a predator pack that connects is drawn around
    // that mean, with variance that shrinks with pack size — so a big pack averages out while a lone
    // apex feasts or goes hungry. A tough prey (e.g. armoured Triceratops) is caught less often.
    public float HuntDifficulty { get; init; }

    // prey categories this carnivore hunts at full satisfaction
    public HashSet<PreyCategory> PreferredPrey { get; init; } = [];

    // prey categories this carnivore will eat when preferred prey is scarce (2/3 satisfaction)
    public HashSet<PreyCategory> AcceptedPrey { get; init; } = [];

    public bool IsPredator => PreyConsumptionRate > 0;

    // ── symbiosis ──────────────────────────────────────────────────────────

    // pollination (mutualism): where this species is present, Fruit regen on the tile is lifted by up
    // to this fraction (saturating with the pollinator count). 0 = not a pollinator (default). The bee
    // feeds on the fruit it helps set, so both sides gain — and fruit-eaters (Alamosaurus) benefit too.
    public float PollinationBoost { get; init; }

    // ── shared traits ────────────────────────────────────────────────────────

    // fractional population growth per tick when fully satisfied
    public float ReproductionRate { get; init; } = 0.02f;

    // fractional population death per tick when fully resource-deprived
    public float StarvationRate { get; init; } = 0.05f;

    // satisfaction ratio below which the population will seek a better tile (0 = never migrate)
    public float MigrationThreshold { get; init; } = 0.5f;

    // ticks a population must wait before migrating again after it last moved (0 = no cooldown)
    public int MigrationCooldownTicks { get; init; }

    // how many tile layers out this species can "see" when choosing a resource/prey migration target.
    // 1 = only immediate neighbours (default). Higher lets it evaluate tiles further out and head
    // toward the richest patch in view instead of greedily hopping to the best adjacent tile (a local
    // optimum). Clamped to the migration search depth. See World.BestNeighborByValue.
    public int ViewRadius { get; init; } = 1;

    // if non-empty, this species can only migrate to tiles with one of these terrain types
    public HashSet<TerrainType> AllowedTerrains { get; init; } = [];

    // maximum population count; growth is capped here (0 = unlimited)
    public int MaxCount { get; init; }

    // tendency to escalate tension with nearby factions (0 = passive, 1 = very aggressive)
    public float WarAggression { get; init; } = 0.2f;

    // casualties inflicted per individual per tick during combat
    public float CombatStrength { get; init; } = 1.0f;

    // resistance to disease (0 = fully susceptible, 1 = immune)
    public float Immunity { get; init; } = 0.3f;

    // byproduct emitted per individual per tick
    public Dictionary<ByproductType, float> ByproductRates { get; init; } = [];
}
