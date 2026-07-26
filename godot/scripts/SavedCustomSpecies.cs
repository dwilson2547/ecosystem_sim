using EcosystemSim;

namespace EcosystemGame;

public sealed class SavedCustomSpecies
{
    public CustomSpeciesTemplate Template { get; set; } = new();
    public string TintHex { get; set; } = "FFFFFF";
    public float SpriteScale { get; set; } = 1f;
}
