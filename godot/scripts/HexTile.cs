using Godot;
using EcosystemSim;
using System.Linq;

namespace EcosystemGame;

/// <summary>
/// One hex cell with an overview population summary and a zoomed-in complete population layout.
/// Set SimTile and HexSize before AddChild — _Ready reads both.
/// </summary>
public partial class HexTile : Node2D
{
    public Tile?  SimTile { get; set; }
    public float  HexSize { get; set; } = 60f;

    private Polygon2D _bg     = null!;
    private Line2D    _border = null!;
    private Label     _label  = null!;
    private Label     _warning = null!;
    private bool      _showDetailedContents;

    private static readonly Texture2D GrassTexture = GD.Load<Texture2D>("res://assets/grass.png");

    private static readonly Dictionary<TerrainType, Texture2D> TerrainTextures = new()
    {
        [TerrainType.Plains] = GrassTexture,
    };

    private const int   MaxSpeciesIcons  = 5;
    private const float IconSize         = 20f;
    private const float IconSpacing      = 22f;
    private const int   CountPerIcon     = 20; // individuals represented by each icon, capped at MaxSpeciesIcons
    private const float DetailIconSize   = 17f;
    private const float DetailSpacing    = 22f;
    private const float DetailSpan       = 76f;

    // per-species icon size multiplier (default 1). The Megalodon is a singleton apex, so it's
    // drawn oversized to stand out from the herds around it.
    private static readonly Dictionary<string, float> IconScales = new()
    {
        ["Megalodon"] = 2.5f,
    };

    // 1-5 icon cluster layouts (offsets from tile center), a 3-over-2 pentagon pattern at 5
    private static readonly Vector2[][] IconLayouts =
    [
        [Vector2.Zero],
        [new Vector2(-IconSpacing / 2, 0), new Vector2(IconSpacing / 2, 0)],
        [new Vector2(-IconSpacing / 2, -IconSpacing / 2), new Vector2(IconSpacing / 2, -IconSpacing / 2), new Vector2(0, IconSpacing / 2)],
        [new Vector2(-IconSpacing / 2, -IconSpacing / 2), new Vector2(IconSpacing / 2, -IconSpacing / 2), new Vector2(-IconSpacing / 2, IconSpacing / 2), new Vector2(IconSpacing / 2, IconSpacing / 2)],
        [new Vector2(-IconSpacing, -IconSpacing / 2), new Vector2(0, -IconSpacing / 2), new Vector2(IconSpacing, -IconSpacing / 2), new Vector2(-IconSpacing / 2, IconSpacing / 2), new Vector2(IconSpacing / 2, IconSpacing / 2)],
    ];

    // one shared pool of icon sprites, reused for whichever species is dominant on this tile
    // (only one species is ever dominant at a time, so no need for a pool per species)
    private readonly List<Sprite2D> _speciesIcons = [];
    private readonly List<Sprite2D> _detailIcons = [];
    private readonly List<Label> _detailLabels = [];

    public override void _Ready()
    {
        // pointy-top hexagon: first vertex at -30° (top-right), stepping 60° clockwise
        var verts = new Vector2[6];
        for (var i = 0; i < 6; i++)
        {
            var angle = Mathf.DegToRad(60f * i - 30f);
            verts[i]  = new Vector2(HexSize * MathF.Cos(angle), HexSize * MathF.Sin(angle));
        }

        _bg = new Polygon2D { Polygon = verts, TextureRepeat = CanvasItem.TextureRepeatEnum.Enabled };
        AddChild(_bg);

        _border = new Line2D { Width = 1.5f, DefaultColor = new Color(0f, 0f, 0f, 0.3f) };
        foreach (var v in verts) _border.AddPoint(v);
        _border.AddPoint(verts[0]); // close the shape
        AddChild(_border);

        _label = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment   = VerticalAlignment.Center,
            AutowrapMode        = TextServer.AutowrapMode.Off,
            Position            = new Vector2(-HexSize * 0.5f, -HexSize * 0.4f),
            Size                = new Vector2(HexSize, HexSize * 0.8f),
        };
        _label.AddThemeColorOverride("font_color",   Colors.White);
        _label.AddThemeFontSizeOverride("font_size", (int)(HexSize * 0.28f));
        AddChild(_label);

        _warning = new Label
        {
            Text = "!",
            Position = new Vector2(-HexSize * 0.42f, -HexSize * 0.55f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Visible = false,
        };
        _warning.AddThemeFontSizeOverride("font_size", (int)(HexSize * 0.32f));
        AddChild(_warning);

        for (var i = 0; i < MaxSpeciesIcons; i++)
        {
            var sprite = new Sprite2D { Visible = false };
            AddChild(sprite);
            _speciesIcons.Add(sprite);
        }

        Refresh();
    }

    public void SetSelected(bool selected)
    {
        _border.DefaultColor = selected ? new Color(1f, 1f, 1f, 0.95f) : new Color(0f, 0f, 0f, 0.3f);
        _border.Width        = selected ? 3.0f : 1.5f;
    }

    public void SetDetailedContents(bool showDetails)
    {
        if (_showDetailedContents == showDetails) return;
        _showDetailedContents = showDetails;
        if (IsNodeReady())
            Refresh();
    }

    public void Refresh()
    {
        if (SimTile is null) return;

        var texture = TerrainTextures.GetValueOrDefault(SimTile.Terrain);
        _bg.Texture = texture;
        // a textured tile samples continuously across the shared image using its own map
        // position as the UV offset, so neighboring tiles of the same terrain read as one
        // continuous surface instead of an obviously repeating per-hex stamp
        _bg.TextureOffset = -Position;
        _bg.Color = texture is not null ? Colors.White : TerrainColor(SimTile.Terrain);

        // subtle green tint on tiles where fertilizer is accumulating
        var fert = SimTile.Byproducts.FirstOrDefault(b => b.Type == ByproductType.Fertilizer);
        if (fert?.Amount > 40f)
            _bg.Color = _bg.Color.Lerp(new Color(0.1f, 0.6f, 0.1f), 0.15f);

        var living = SimTile.Populations.Where(p => p.Count > 0).ToList();
        var diseased = living.Any(p => p.Disease is not null);
        var lowestSatisfaction = living.Count == 0 ? 1f : living.Min(p => p.LastSatisfaction);
        _warning.Visible = diseased || lowestSatisfaction < 0.6f;
        if (_warning.Visible)
        {
            var critical = diseased || lowestSatisfaction < 0.3f;
            var color = critical
                ? new Color(1f, 0.2f, 0.2f)
                : new Color(1f, 0.7f, 0.15f);
            _warning.AddThemeColorOverride("font_color", color);
            _warning.TooltipText = diseased
                ? "Disease detected on this tile."
                : $"Population satisfaction is {lowestSatisfaction * 100f:F0}%.";
            _bg.Color = _bg.Color.Lerp(color, critical ? 0.28f : 0.16f);
        }

        var dominant = living
            .OrderByDescending(p => p.Count)
            .FirstOrDefault();

        if (_showDetailedContents)
        {
            RefreshDetailedContents(living);
            _label.Visible = false;
            foreach (var sprite in _speciesIcons) sprite.Visible = false;
            return;
        }

        HideDetailedContents();
        _label.Visible = true;

        // species with icon art render as a repeated icon (quantity = icon count) instead of
        // text; every other species keeps the letter+count label until they get their own art
        var icon = dominant is not null
            ? SpeciesVisuals.IconFor(dominant.Species.Name, dominant.Species.EffectiveRootName)
            : null;

        if (icon is not null)
        {
            _label.Text = string.Empty;

            var iconCount = Mathf.Clamp(Mathf.CeilToInt((float)dominant!.Count / CountPerIcon), 1, MaxSpeciesIcons);
            var layout    = IconLayouts[iconCount - 1];
            var visualBase = SpeciesVisuals.BaseFor(
                dominant.Species.Name,
                dominant.Species.EffectiveRootName);
            var sizeMult = IconScales.GetValueOrDefault(visualBase, 1f)
                         * SpeciesVisuals.ScaleFor(
                             dominant.Species.Name,
                             dominant.Species.EffectiveRootName);
            var drawSize  = IconSize * sizeMult;
            var scale     = new Vector2(drawSize / icon.GetWidth(), drawSize / icon.GetHeight());
            var tint = SpeciesVisuals.TintFor(
                dominant.Species.Name,
                dominant.Species.EffectiveRootName);

            for (var i = 0; i < _speciesIcons.Count; i++)
            {
                var visible = i < iconCount;
                _speciesIcons[i].Visible = visible;
                if (!visible) continue;

                _speciesIcons[i].Texture  = icon;
                _speciesIcons[i].Scale    = scale;
                _speciesIcons[i].Position = layout[i];
                _speciesIcons[i].Modulate = tint;
            }
        }
        else
        {
            foreach (var sprite in _speciesIcons) sprite.Visible = false;

            _label.Text = dominant is not null
                ? $"{dominant.Species.Name[0]}\n{dominant.Count}"
                : string.Empty;
        }
    }

    private void RefreshDetailedContents(List<Population> living)
    {
        EnsureDetailSlots(living.Count);
        var columns = Math.Max(1, Mathf.CeilToInt(MathF.Sqrt(living.Count)));
        var rows = Math.Max(1, Mathf.CeilToInt((float)living.Count / columns));
        var longestAxis = Math.Max(columns, rows);
        var spacing = longestAxis <= 1
            ? 0f
            : MathF.Min(DetailSpacing, DetailSpan / (longestAxis - 1));
        var iconSize = MathF.Min(DetailIconSize, MathF.Max(8f, spacing * 0.75f));

        for (var i = 0; i < _detailIcons.Count; i++)
        {
            var visible = i < living.Count;
            var sprite = _detailIcons[i];
            var label = _detailLabels[i];
            sprite.Visible = visible;
            label.Visible = visible;
            if (!visible) continue;

            var pop = living[i];
            var col = i % columns;
            var row = i / columns;
            var center = new Vector2(
                (col - (columns - 1) / 2f) * spacing,
                (row - (rows - 1) / 2f) * spacing);
            var icon = SpeciesVisuals.IconFor(pop.Species.Name, pop.Species.EffectiveRootName);
            label.TooltipText = $"{pop.Species.Name} ×{pop.Count}";

            if (icon is not null)
            {
                sprite.Texture = icon;
                sprite.Scale = Vector2.One
                    * (iconSize * SpeciesVisuals.ScaleFor(
                           pop.Species.Name,
                           pop.Species.EffectiveRootName)
                       / Math.Max(icon.GetWidth(), icon.GetHeight()));
                sprite.Position = center + new Vector2(0f, -4f);
                sprite.Modulate = SpeciesVisuals.TintFor(
                    pop.Species.Name,
                    pop.Species.EffectiveRootName);
                sprite.Visible = true;
                label.Text = pop.Count.ToString();
                label.Position = center + new Vector2(-14f, 3f);
                label.Size = new Vector2(28f, 14f);
            }
            else
            {
                sprite.Visible = false;
                label.Text = $"{pop.Species.EffectiveRootName[0]}\n{pop.Count}";
                label.Position = center + new Vector2(-14f, -13f);
                label.Size = new Vector2(28f, 28f);
            }
        }
    }

    private void EnsureDetailSlots(int count)
    {
        while (_detailIcons.Count < count)
        {
            var sprite = new Sprite2D { Visible = false };
            AddChild(sprite);
            _detailIcons.Add(sprite);

            var label = new Label
            {
                Visible = false,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                MouseFilter = Control.MouseFilterEnum.Pass,
            };
            label.AddThemeFontSizeOverride("font_size", 9);
            label.AddThemeColorOverride("font_color", Colors.White);
            label.AddThemeColorOverride("font_shadow_color", Colors.Black);
            label.AddThemeConstantOverride("shadow_offset_x", 1);
            label.AddThemeConstantOverride("shadow_offset_y", 1);
            AddChild(label);
            _detailLabels.Add(label);
        }
    }

    private void HideDetailedContents()
    {
        foreach (var sprite in _detailIcons) sprite.Visible = false;
        foreach (var label in _detailLabels) label.Visible = false;
    }

    private static Color TerrainColor(TerrainType t) => t switch
    {
        TerrainType.Plains       => new Color(0.55f, 0.72f, 0.35f),
        TerrainType.Forest       => new Color(0.15f, 0.42f, 0.18f),
        TerrainType.Swamp        => new Color(0.26f, 0.42f, 0.32f),
        TerrainType.Desert       => new Color(0.88f, 0.78f, 0.42f),
        TerrainType.Highland     => new Color(0.56f, 0.56f, 0.56f),
        TerrainType.River        => new Color(0.25f, 0.52f, 0.88f),
        TerrainType.ShallowOcean => new Color(0.20f, 0.65f, 0.85f),
        TerrainType.DeepOcean    => new Color(0.08f, 0.20f, 0.55f),
        _                        => Colors.Gray,
    };
}
