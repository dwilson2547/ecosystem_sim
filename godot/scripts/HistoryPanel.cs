using Godot;
using EcosystemSim;

namespace EcosystemGame;

public partial class HistoryPanel : CanvasLayer
{
    private PanelContainer _panel = null!;
    private VBoxContainer _body = null!;
    private bool _showEvents;
    private string _selectedLineage = string.Empty;

    public override void _Ready()
    {
        Layer = 20;
        _panel = new PanelContainer();
        _panel.AnchorLeft = 0f;
        _panel.AnchorRight = 1f;
        _panel.AnchorTop = 1f;
        _panel.AnchorBottom = 1f;
        _panel.OffsetLeft = 270f;
        _panel.OffsetRight = -310f;
        _panel.OffsetTop = -250f;
        _panel.OffsetBottom = -10f;
        _panel.Visible = false;
        AddChild(_panel);

        var root = new VBoxContainer();
        root.AddThemeConstantOverride("separation", 6);
        _panel.AddChild(root);

        var header = new HBoxContainer();
        root.AddChild(header);
        var title = new Label
        {
            Text = "ECOSYSTEM ANALYSIS",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        title.AddThemeColorOverride("font_color", new Color(0.65f, 0.9f, 1f));
        header.AddChild(title);

        var history = new Button { Text = "History" };
        history.Pressed += () => { _showEvents = false; Rebuild(); };
        header.AddChild(history);
        var eventsButton = new Button { Text = "Events" };
        eventsButton.Pressed += () => { _showEvents = true; Rebuild(); };
        header.AddChild(eventsButton);
        var close = new Button { Text = "Close [H]" };
        close.Pressed += Toggle;
        header.AddChild(close);

        var scroll = new ScrollContainer
        {
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        root.AddChild(scroll);

        _body = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        scroll.AddChild(_body);

        SimManager.Instance.AnalysisToggleRequested += Toggle;
        SimManager.Instance.Ticked += RefreshIfVisible;
        SimManager.Instance.WorldReset += OnWorldReset;
    }

    private void Toggle()
    {
        _panel.Visible = !_panel.Visible;
        if (_panel.Visible) Rebuild();
    }

    private void RefreshIfVisible()
    {
        if (_panel.Visible) Rebuild();
    }

    private void OnWorldReset()
    {
        _selectedLineage = string.Empty;
        if (_panel.Visible) Rebuild();
    }

    private void Rebuild()
    {
        while (_body.GetChildCount() > 0)
        {
            var child = _body.GetChild(0);
            _body.RemoveChild(child);
            child.Free();
        }

        if (_showEvents)
            BuildEvents();
        else
            BuildHistory();
    }

    private void BuildHistory()
    {
        var state = SimManager.Instance.World.State;
        var lineages = state.Map.AllPopulations()
            .Select(p => p.Species.EffectiveRootName)
            .Concat(state.History.SelectMany(s => s.LineagePopulations.Keys))
            .Distinct()
            .Order()
            .ToList();
        if (lineages.Count == 0)
        {
            Message("No lineage data is available for this world.");
            return;
        }

        if (string.IsNullOrEmpty(_selectedLineage) || !lineages.Contains(_selectedLineage))
            _selectedLineage = lineages[0];

        var selector = new OptionButton();
        foreach (var lineage in lineages)
            selector.AddItem(lineage);
        selector.Selected = lineages.IndexOf(_selectedLineage);
        selector.ItemSelected += index =>
        {
            _selectedLineage = selector.GetItemText((int)index);
            CallDeferred(MethodName.Rebuild);
        };
        _body.AddChild(selector);

        var samples = state.History
            .Where(s => s.LineagePopulations.ContainsKey(_selectedLineage))
            .ToList();
        if (samples.Count < 2)
        {
            Message($"History for {_selectedLineage} is recorded every {World.HistoryIntervalDays} days.");
            return;
        }

        var values = samples.Select(s => s.LineagePopulations[_selectedLineage]).ToList();
        var change = values[^1] - values[0];
        Message(
            $"Start {values[0]}  Current {values[^1]}  Min {values.Min()}  Max {values.Max()}  "
            + $"Change {(change >= 0 ? "+" : string.Empty)}{change}",
            new Color(0.8f, 0.85f, 0.9f));

        var chart = new LineageChart { Lineage = _selectedLineage };
        chart.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        _body.AddChild(chart);
        chart.QueueRedraw();
    }

    private void BuildEvents()
    {
        var events = SimManager.Instance.World.State.Events
            .TakeLast(12)
            .Reverse()
            .ToList();
        if (events.Count == 0)
        {
            Message("No major ecosystem events yet.");
            return;
        }

        foreach (var worldEvent in events)
        {
            var year = worldEvent.Tick / World.DaysPerYear + 1;
            var day = worldEvent.Tick % World.DaysPerYear + 1;
            var location = worldEvent.TileX.HasValue
                ? $" ({worldEvent.TileX},{worldEvent.TileY})"
                : string.Empty;
            Message($"Y{year} D{day}: {worldEvent.Message}{location}", EventColor(worldEvent.Severity));
        }
    }

    private void Message(string text, Color? color = null)
    {
        var label = new Label
        {
            Text = text,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        if (color.HasValue)
            label.AddThemeColorOverride("font_color", color.Value);
        _body.AddChild(label);
    }

    private static Color EventColor(WorldEventSeverity severity) => severity switch
    {
        WorldEventSeverity.Critical => new Color(1f, 0.35f, 0.35f),
        WorldEventSeverity.Warning => new Color(1f, 0.7f, 0.25f),
        _ => new Color(0.72f, 0.82f, 0.9f),
    };
}
