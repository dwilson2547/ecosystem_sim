using Godot;
using EcosystemSim;
using System.Linq;

namespace EcosystemGame;

/// <summary>
/// Right-side panel that shows terrain, resources, and population details for the selected tile.
/// Rebuilt each tick while a tile is selected so values stay live.
/// </summary>
public partial class TileInfoPanel : CanvasLayer
{
    private PanelContainer _panel   = null!;
    private VBoxContainer  _content = null!;
    private Tile?          _tile;
    private string _actionFeedback = string.Empty;
    private bool _actionSucceeded;

    public override void _Ready()
    {
        _panel = new PanelContainer();
        // anchor to the full right edge, 300px wide
        _panel.AnchorLeft   = 1f;
        _panel.AnchorTop    = 0f;
        _panel.AnchorRight  = 1f;
        _panel.AnchorBottom = 1f;
        _panel.OffsetLeft   = -300f;
        _panel.OffsetRight  = 0f;
        _panel.OffsetTop    = 0f;
        _panel.OffsetBottom = 0f;
        AddChild(_panel);

        var scroll = new ScrollContainer();
        scroll.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        _panel.AddChild(scroll);

        _content = new VBoxContainer();
        _content.CustomMinimumSize = new Vector2(280f, 0f);
        scroll.AddChild(_content);

        _panel.MouseFilter   = Control.MouseFilterEnum.Stop;
        scroll.MouseFilter   = Control.MouseFilterEnum.Stop;
        _content.MouseFilter = Control.MouseFilterEnum.Pass;
        _panel.Visible = false;
        SimManager.Instance.Ticked += OnTicked;
        SimManager.Instance.WorldReset += HidePanel;
    }

    public void ShowTile(Tile tile)
    {
        _tile = tile;
        _actionFeedback = string.Empty;
        _panel.Visible = true;
        Rebuild();
    }

    public void HidePanel()
    {
        _tile = null;
        _panel.Visible = false;
    }

    private void OnTicked()
    {
        if (_tile is not null) Rebuild();
    }

    // ── Rebuild ───────────────────────────────────────────────────────────────

    private void Rebuild()
    {
        while (_content.GetChildCount() > 0)
        {
            var child = _content.GetChild(0);
            _content.RemoveChild(child);
            child.Free();
        }

        if (_tile is null) return;

        // header
        Row($"({_tile.X}, {_tile.Y})  {_tile.Terrain}", size: 14, color: Colors.White);
        Sep();

        // resources
        if (_tile.Resources.Count > 0)
        {
            Row("Resources", color: new Color(0.7f, 0.9f, 1f));
            foreach (var r in _tile.Resources)
            {
                var pct  = r.Capacity > 0 ? r.Amount / r.Capacity * 100f : 0f;
                var name = r.FoodSubtype.HasValue ? r.FoodSubtype.Value.ToString() : r.Type.ToString();
                Row($"  {name}  {r.Amount:F0}/{r.Capacity:F0} ({pct:F0}%)  +{r.RegenPerTick:F1}/tick",
                    color: new Color(0.78f, 0.9f, 0.78f));
            }
            Sep();
        }

        // fertilizer
        var fert = _tile.Byproducts.FirstOrDefault(b => b.Type == ByproductType.Fertilizer);
        if (fert?.Amount > 1f)
        {
            Row($"Fertilizer  {fert.Amount:F1}", color: new Color(0.6f, 0.9f, 0.4f));
            Sep();
        }

        BuildInterventions();

        // living populations
        var living = _tile.Populations.Where(p => p.Count > 0)
                         .OrderByDescending(p => p.Count).ToList();
        if (living.Count > 0)
        {
            Row("Populations", color: new Color(0.7f, 0.9f, 1f));
            foreach (var pop in living)
                PopBlock(pop);
        }

        // extinct populations (compact)
        var extinct = _tile.Populations.Where(p => p.Count == 0).ToList();
        if (extinct.Count > 0)
        {
            Sep();
            Row("Extinct", color: new Color(0.5f, 0.5f, 0.5f));
            foreach (var pop in extinct)
                Row($"  [{pop.Species.Name}]", color: new Color(0.38f, 0.38f, 0.38f));
        }
    }

    private void BuildInterventions()
    {
        if (_tile is null) return;
        var scenario = SimManager.Instance.World.State.Scenario;
        if (scenario is null) return;

        Row("Interventions", color: new Color(1f, 0.85f, 0.35f));

        if (scenario.Status != ScenarioStatus.Active)
        {
            Row("  Challenge complete — interventions are locked.",
                color: new Color(0.65f, 0.65f, 0.65f));
            Sep();
            return;
        }

        if (scenario.Kind is ScenarioKind.Sandbox or ScenarioKind.LocustPlague)
        {
            AddAction(new CullLocustsAction { TileX = _tile.X, TileY = _tile.Y });
            AddAction(new RestoreGrassAction { TileX = _tile.X, TileY = _tile.Y });
            AddAction(new SeedMeganeuraAction { TileX = _tile.X, TileY = _tile.Y });
        }

        if (scenario.Kind is ScenarioKind.Sandbox or ScenarioKind.DroughtRecovery)
        {
            AddAction(new CreateWateringHolesAction { TileX = _tile.X, TileY = _tile.Y });
            AddAction(new RestoreVegetationAction { TileX = _tile.X, TileY = _tile.Y });
            AddAction(new SeedRainAction { TileX = _tile.X, TileY = _tile.Y });
        }

        if (!string.IsNullOrEmpty(_actionFeedback))
            Row($"  {_actionFeedback}",
                color: _actionSucceeded
                    ? new Color(0.45f, 1f, 0.45f)
                    : new Color(1f, 0.4f, 0.35f));
        Sep();
    }

    private void AddAction(IScenarioAction action)
    {
        var scenario = SimManager.Instance.World.State.Scenario!;
        var valid = action.CanExecute(SimManager.Instance.World.State, out var reason);
        if (valid && scenario.IsChallenge && scenario.ActionPointsRemaining < action.Cost)
        {
            valid = false;
            reason = $"Needs {action.Cost} action points; {scenario.ActionPointsRemaining} remain.";
        }

        var cost = scenario.IsChallenge ? $"{action.Cost} AP" : "Free";
        var button = new Button
        {
            Text = $"{action.Name}  [{cost}]",
            Disabled = !valid,
            TooltipText = valid ? string.Empty : reason,
        };
        button.Pressed += () =>
        {
            var result = SimManager.Instance.TryApplyScenarioAction(action);
            _actionFeedback = result.Message;
            _actionSucceeded = result.Success;
            CallDeferred(MethodName.Rebuild);
        };
        _content.AddChild(button);

        if (!valid)
            Row($"  {reason}", color: new Color(0.6f, 0.6f, 0.65f));
    }

    private void PopBlock(Population pop)
    {
        var bg = new StyleBoxFlat { BgColor = new Color(0.14f, 0.16f, 0.22f) };
        bg.SetContentMarginAll(8f);

        var outerPanel = new PanelContainer();
        outerPanel.AddThemeStyleboxOverride("panel", bg);
        outerPanel.MouseFilter = Control.MouseFilterEnum.Ignore;
        _content.AddChild(outerPanel);

        var vbox = new VBoxContainer();
        vbox.MouseFilter = Control.MouseFilterEnum.Ignore;
        outerPanel.AddChild(vbox);

        AddTo(vbox, $"{pop.Species.Name}  ×{pop.Count}", size: 13, color: Colors.White);
        if (pop.Faction is not null)
            AddTo(vbox, $"Faction: {pop.Faction.Name}", color: new Color(0.75f, 0.75f, 1f));

        AddTo(vbox, $"Satisfaction  {pop.LastSatisfaction * 100f:F0}%",
              color: SatColor(pop.LastSatisfaction));
        AddTo(vbox,
            $"Breeding      {string.Join("/", pop.Species.BreedingSeasons)} day {pop.Species.BreedingDayOfSeason}");

        if (pop.FoodDeprivationDays > 0f)
            AddTo(vbox,
                $"Food exhausted {pop.FoodDeprivationDays:F0}/{pop.Species.FoodDeprivationToleranceDays} days",
                color: new Color(1f, 0.6f, 0.2f));

        if (pop.WaterDeprivationDays > 0f)
            AddTo(vbox,
                $"Water exhausted {pop.WaterDeprivationDays:F0}/{pop.Species.WaterDeprivationToleranceDays} days",
                color: new Color(0.4f, 0.7f, 1f));

        AddTo(vbox, $"Size index    {pop.SizeIndex:F2}");

        if (pop.ImmunityDelta > 0f)
            AddTo(vbox, $"Immunity +{pop.ImmunityDelta:F2}", color: new Color(0.6f, 1f, 0.6f));

        if (pop.Disease is not null)
            AddTo(vbox, $"INFECTED {pop.InfectionLevel * 100f:F0}%  ({pop.Disease.Name})",
                  color: new Color(1f, 0.35f, 0.35f));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void Row(string text, int size = 12, Color? color = null)
        => AddTo(_content, text, size, color);

    private void Sep()
    {
        var sep = new HSeparator();
        sep.MouseFilter = Control.MouseFilterEnum.Ignore;
        _content.AddChild(sep);
    }

    private void AddTo(Control parent, string text, int size = 12, Color? color = null)
    {
        var lbl = new Label { Text = text, MouseFilter = Control.MouseFilterEnum.Ignore };
        lbl.AddThemeFontSizeOverride("font_size", size);
        if (color.HasValue)
            lbl.AddThemeColorOverride("font_color", color.Value);
        parent.AddChild(lbl);
    }

    private static Color SatColor(float sat) => sat switch
    {
        >= 0.9f => new Color(0.4f, 1.0f, 0.4f),
        >= 0.6f => new Color(1.0f, 1.0f, 0.4f),
        >= 0.3f => new Color(1.0f, 0.55f, 0.2f),
        _       => new Color(1.0f, 0.3f, 0.3f),
    };
}
