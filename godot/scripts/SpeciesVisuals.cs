using EcosystemSim;
using Godot;

namespace EcosystemGame;

public static class SpeciesVisuals
{
    private static readonly Dictionary<string, string> IconPaths = new()
    {
        ["Alamosaurus"] = "res://assets/sprites/alamosaurus.png",
        ["Triceratops"] = "res://assets/sprites/triceratops.png",
        ["Megalodon"] = "res://assets/sprites/megalodon.png",
    };

    private static readonly Dictionary<string, Texture2D> IconCache = [];

    public static string BaseFor(string speciesName, string rootName) =>
        FindSaved(speciesName, rootName)?.Template.BaseSpeciesName ?? rootName;

    public static Texture2D? IconFor(string speciesName, string rootName)
    {
        var visualRoot = BaseFor(speciesName, rootName);
        if (!IconPaths.TryGetValue(visualRoot, out var path)) return null;
        if (!IconCache.TryGetValue(visualRoot, out var texture))
            IconCache[visualRoot] = texture = GD.Load<Texture2D>(path);
        return texture;
    }

    public static Texture2D? IconForBase(string baseSpeciesName) =>
        IconFor(baseSpeciesName, baseSpeciesName);

    public static Color TintFor(string speciesName, string rootName)
    {
        var tint = FindSaved(speciesName, rootName)?.TintHex;
        return tint is not null && Color.HtmlIsValid(tint) ? Color.FromHtml(tint) : Colors.White;
    }

    public static float ScaleFor(string speciesName, string rootName) =>
        FindSaved(speciesName, rootName)?.SpriteScale ?? 1f;

    private static SavedCustomSpecies? FindSaved(string speciesName, string rootName)
    {
        var active = SimManager.Instance is not null
            ? SimManager.Instance.CurrentCustomSpecies.FirstOrDefault(saved =>
                string.Equals(saved.Template.Name, speciesName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(saved.Template.Name, rootName, StringComparison.OrdinalIgnoreCase))
            : null;
        return active
            ?? CustomSpeciesLibrary.Find(speciesName)
            ?? CustomSpeciesLibrary.Find(rootName);
    }
}
