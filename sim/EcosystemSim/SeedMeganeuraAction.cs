namespace EcosystemSim;

public sealed class SeedMeganeuraAction : IScenarioAction
{
    public required int TileX { get; init; }
    public required int TileY { get; init; }
    public string Name => "Seed Meganeura";
    public int Cost => 3;

    public bool CanExecute(WorldState state, out string error)
    {
        var tile = state.Map.GetTile(TileX, TileY);
        if (TerrainStats.IsOcean(tile.Terrain) || tile.Terrain == TerrainType.River)
        {
            error = "Meganeura can only be seeded on dry land.";
            return false;
        }

        var faction = state.Factions.FirstOrDefault(f =>
            f.PrimarySpecies.EffectiveRootName == "Meganeura");
        if (faction is null)
        {
            error = "The scenario has no Meganeura lineage available to seed.";
            return false;
        }

        var existing = tile.Populations.FirstOrDefault(p =>
            p.Count > 0 && p.Species.EffectiveRootName == "Meganeura");
        if (existing is not null
            && existing.Species.MaxCount > 0
            && existing.Count >= existing.Species.MaxCount)
        {
            error = "The Meganeura population on this tile is already at capacity.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    public string Execute(WorldState state)
    {
        var tile = state.Map.GetTile(TileX, TileY);
        var faction = state.Factions.First(f =>
            f.PrimarySpecies.EffectiveRootName == "Meganeura");
        var existing = tile.Populations.FirstOrDefault(p =>
            p.Count > 0 && p.Species == faction.PrimarySpecies && p.Faction == faction);

        if (existing is not null)
        {
            var cap = existing.Species.MaxCount;
            existing.Count = cap > 0 ? Math.Min(cap, existing.Count + 5) : existing.Count + 5;
        }
        else
        {
            var pop = new Population { Species = faction.PrimarySpecies, Count = 5 };
            faction.AddPopulation(pop);
            tile.AddPopulation(pop);
        }

        return $"Seeded five Meganeura on ({TileX},{TileY}).";
    }
}
