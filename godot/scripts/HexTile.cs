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

        _label.Text = dominant is not null
            ? $"{dominant.Species.Name[0]}\n{dominant.Count}"
            : string.Empty;
    }

    private static Color TerrainColor(TerrainType t) => t switch
    {
        TerrainType.Plains   => new Color(0.55f, 0.72f, 0.35f),
        TerrainType.Forest   => new Color(0.15f, 0.42f, 0.18f),
        TerrainType.Swamp    => new Color(0.26f, 0.42f, 0.32f),
        TerrainType.Desert   => new Color(0.88f, 0.78f, 0.42f),
        TerrainType.Highland => new Color(0.56f, 0.56f, 0.56f),
        TerrainType.River    => new Color(0.25f, 0.52f, 0.88f),
        _                    => Colors.Gray,
    };
}
