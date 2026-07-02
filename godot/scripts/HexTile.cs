using Godot;
using EcosystemSim;
using System.Linq;

namespace EcosystemGame;

/// <summary>
/// One hex cell: a colored Polygon2D for terrain, an outline, and a Label for the dominant pop.
/// Set SimTile and HexSize before AddChild — _Ready reads both.
/// </summary>
public partial class HexTile : Node2D
{
    public Tile?  SimTile { get; set; }
    public float  HexSize { get; set; } = 60f;

    private Polygon2D _bg     = null!;
    private Line2D    _border = null!;
    private Label     _label  = null!;

    // species with map icon art — sized so ~5 fit on a single hex tile. Add an entry here (and
    // a processed PNG in assets/sprites/) to give another species its own map icon.
    private const int   MaxSpeciesIcons  = 5;
    private const float IconSize         = 20f;
    private const float IconSpacing      = 22f;
    private const int   CountPerIcon     = 20; // individuals represented by each icon, capped at MaxSpeciesIcons

    private static readonly Dictionary<string, string> IconPaths = new()
    {
        ["Alamosaurus"] = "res://assets/sprites/alamosaurus.png",
        ["Triceratops"] = "res://assets/sprites/triceratops.png",
    };

    private static readonly Dictionary<string, Texture2D> _iconCache = new();

    private static Texture2D? IconFor(string rootName)
    {
        if (!IconPaths.TryGetValue(rootName, out var path)) return null;
        if (!_iconCache.TryGetValue(rootName, out var tex))
            _iconCache[rootName] = tex = GD.Load<Texture2D>(path);
        return tex;
    }

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

    public override void _Ready()
    {
        // pointy-top hexagon: first vertex at -30° (top-right), stepping 60° clockwise
        var verts = new Vector2[6];
        for (var i = 0; i < 6; i++)
        {
            var angle = Mathf.DegToRad(60f * i - 30f);
            verts[i]  = new Vector2(HexSize * MathF.Cos(angle), HexSize * MathF.Sin(angle));
        }

        _bg = new Polygon2D { Polygon = verts };
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

    public void Refresh()
    {
        if (SimTile is null) return;

        _bg.Color = TerrainColor(SimTile.Terrain);

        // subtle green tint on tiles where fertilizer is accumulating
        var fert = SimTile.Byproducts.FirstOrDefault(b => b.Type == ByproductType.Fertilizer);
        if (fert?.Amount > 40f)
            _bg.Color = _bg.Color.Lerp(new Color(0.1f, 0.6f, 0.1f), 0.15f);

        var dominant = SimTile.Populations
            .Where(p => p.Count > 0)
            .OrderByDescending(p => p.Count)
            .FirstOrDefault();

        // species with icon art render as a repeated icon (quantity = icon count) instead of
        // text; every other species keeps the letter+count label until they get their own art
        var icon = dominant is not null ? IconFor(dominant.Species.EffectiveRootName) : null;

        if (icon is not null)
        {
            _label.Text = string.Empty;

            var iconCount = Mathf.Clamp(Mathf.CeilToInt((float)dominant!.Count / CountPerIcon), 1, MaxSpeciesIcons);
            var layout    = IconLayouts[iconCount - 1];
            var scale     = new Vector2(IconSize / icon.GetWidth(), IconSize / icon.GetHeight());

            for (var i = 0; i < _speciesIcons.Count; i++)
            {
                var visible = i < iconCount;
                _speciesIcons[i].Visible = visible;
                if (!visible) continue;

                _speciesIcons[i].Texture  = icon;
                _speciesIcons[i].Scale    = scale;
                _speciesIcons[i].Position = layout[i];
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
