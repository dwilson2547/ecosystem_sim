namespace EcosystemSim;

public class TriggerDiseaseCommand : IWorldCommand
{
    public required Disease Disease { get; init; }
    public required int TileX { get; init; }
    public required int TileY { get; init; }
    public float InitialInfection { get; init; } = 0.3f;

    public void Execute(WorldState state)
    {
        var tile = state.Map.GetTile(TileX, TileY);
        foreach (var pop in tile.Populations.Where(p => p.Count > 0 && p.Disease is null))
        {
            pop.Disease = Disease;
            pop.InfectionLevel = InitialInfection;
        }
    }
}
