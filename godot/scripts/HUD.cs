using EcosystemSim;
using Godot;

namespace EcosystemGame;

/// <summary>Compact simulation toolbar and dismissible keyboard-help panel.</summary>
public partial class HUD : CanvasLayer
{
    private PanelContainer _toolbar = null!;
    private Button _collapsedButton = null!;
    private PanelContainer _helpPanel = null!;
    private Label _dateLabel = null!;
    private Label _weatherLabel = null!;
    private Label _speedLabel = null!;
    private Label _eventLabel = null!;
    private Button _pauseButton = null!;

    public override void _Ready()
    {
        _toolbar = new PanelContainer
        {
            Position = new Vector2(8f, 8f),
            CustomMinimumSize = new Vector2(0f, 40f),
        };
        _toolbar.AnchorRight = 1f;
        _toolbar.OffsetRight = -8f;
        AddChild(_toolbar);

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 6);
        _toolbar.AddChild(row);

        _dateLabel = MakeLabel(170f);
        _weatherLabel = MakeLabel(60f);
        _speedLabel = MakeLabel(80f);
        _eventLabel = MakeLabel(140f, expand: true);
        row.AddChild(_dateLabel);
        row.AddChild(_weatherLabel);
        row.AddChild(_speedLabel);
        row.AddChild(_eventLabel);

        _pauseButton = AddButton(row, "Pause", SimManager.Instance.TogglePause, "Pause/resume [Space]");
        AddButton(row, "−", SimManager.Instance.SpeedDown, "Slow simulation");
        AddButton(row, "+", SimManager.Instance.SpeedUp, "Speed up simulation");
        AddButton(row, "Factions", SimManager.Instance.ToggleFactions, "Toggle faction panel");
        AddButton(row, "Goals", SimManager.Instance.ToggleScenarioPanel, "Toggle scenario goals");
        AddButton(row, "History", SimManager.Instance.ToggleAnalysis, "History and events [H]");
        AddButton(row, "Species", SimManager.Instance.ToggleWorkshop, "Open Species Workshop");
        AddButton(row, "Restart", SimManager.Instance.Reset, "Restart current run [R]");
        AddButton(row, "New", SimManager.Instance.RequestScenarioSelection, "Choose a new scenario");
        AddButton(row, "?", ToggleHelp, "Show controls");
        AddButton(row, "▴", ToggleCollapsed, "Collapse toolbar [Tab]");

        _collapsedButton = new Button
        {
            Text = "Menu [Tab]",
            Position = new Vector2(8f, 8f),
            Visible = false,
        };
        _collapsedButton.Pressed += ToggleCollapsed;
        AddChild(_collapsedButton);

        _helpPanel = new PanelContainer
        {
            Position = new Vector2(8f, 54f),
            Visible = false,
        };
        var help = new Label
        {
            Text = "MMB drag: pan  ·  Wheel: zoom  ·  Click: inspect tile\n"
                 + "Space: pause  ·  +/-: speed  ·  H: history  ·  R: restart  ·  Tab: toolbar",
        };
        help.AddThemeColorOverride("font_color", new Color(0.78f, 0.82f, 0.9f));
        _helpPanel.AddChild(help);
        AddChild(_helpPanel);

        SimManager.Instance.Ticked += Refresh;
        SimManager.Instance.PausedChanged += OnPausedChanged;
        SimManager.Instance.WorldReset += Refresh;
        SimManager.Instance.ScenarioChanged += Refresh;
        Refresh();
    }

    public void ToggleCollapsed()
    {
        var collapsed = _toolbar.Visible;
        _toolbar.Visible = !collapsed;
        _collapsedButton.Visible = collapsed;
        if (collapsed)
            _helpPanel.Visible = false;
    }

    private void ToggleHelp() => _helpPanel.Visible = !_helpPanel.Visible;

    private void Refresh()
    {
        var sim = SimManager.Instance;
        var state = sim.World.State;
        _dateLabel.Text =
            $"Year {state.Year} · {state.CurrentSeason} {state.DayOfSeason}/{World.DaysPerSeason}";
        _dateLabel.AddThemeColorOverride("font_color", SeasonColor(state.CurrentSeason));
        _weatherLabel.Text = state.CurrentWeather switch
        {
            Weather.Rainy => "Rain",
            Weather.Drought => "Drought",
            _ => "Clear",
        };
        _weatherLabel.AddThemeColorOverride("font_color", WeatherColor(state.CurrentWeather));
        _speedLabel.Text = sim.Paused ? "PAUSED" : $"{sim.TickInterval:F2}s/day";
        _pauseButton.Text = sim.Paused ? "Resume" : "Pause";

        var latestEvent = state.Events.LastOrDefault();
        var eventText = latestEvent?.Message ?? "No events yet";
        if (eventText.Length > 46) eventText = eventText[..43] + "...";
        _eventLabel.Text = eventText;
        _eventLabel.TooltipText = latestEvent?.Message ?? string.Empty;
        _eventLabel.AddThemeColorOverride("font_color", latestEvent?.Severity switch
        {
            WorldEventSeverity.Critical => new Color(1f, 0.35f, 0.35f),
            WorldEventSeverity.Warning => new Color(1f, 0.7f, 0.25f),
            _ => new Color(0.7f, 0.8f, 0.9f),
        });
    }

    private void OnPausedChanged(bool paused) => Refresh();

    private static Label MakeLabel(float minimumWidth, bool expand = false) => new()
    {
        CustomMinimumSize = new Vector2(minimumWidth, 0f),
        SizeFlagsHorizontal = expand ? Control.SizeFlags.ExpandFill : Control.SizeFlags.ShrinkBegin,
        VerticalAlignment = VerticalAlignment.Center,
        MouseFilter = Control.MouseFilterEnum.Ignore,
    };

    private static Button AddButton(
        Container parent,
        string text,
        Action action,
        string tooltip = "")
    {
        var button = new Button { Text = text, TooltipText = tooltip };
        button.Pressed += action;
        parent.AddChild(button);
        return button;
    }

    private static Color SeasonColor(Season season) => season switch
    {
        Season.Spring => new Color(0.5f, 1f, 0.5f),
        Season.Summer => Colors.Yellow,
        Season.Autumn => new Color(1f, 0.6f, 0.1f),
        Season.Winter => new Color(0.6f, 0.85f, 1f),
        _ => Colors.White,
    };

    private static Color WeatherColor(Weather weather) => weather switch
    {
        Weather.Rainy => new Color(0.4f, 0.7f, 1f),
        Weather.Drought => new Color(0.9f, 0.5f, 0.2f),
        _ => new Color(0.8f, 0.8f, 0.8f),
    };
}
