using Godot;
using EcosystemSim;

namespace EcosystemGame;

/// <summary>
/// Overlay panel showing tick, season, year, and current tick speed.
/// Constructed entirely in code — no scene editor setup needed.
/// </summary>
public partial class HUD : CanvasLayer
{
    private Label _tickLabel   = null!;
    private Label _seasonLabel = null!;
    private Label _yearLabel   = null!;
    private Label _speedLabel  = null!;

    public override void _Ready()
    {
        var panel = new PanelContainer();
        panel.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
        panel.Position    = new Vector2(10f, 10f);
        panel.MouseFilter = Control.MouseFilterEnum.Ignore;
        AddChild(panel);

        var vbox = new VBoxContainer();
        vbox.MouseFilter = Control.MouseFilterEnum.Ignore;
        panel.AddChild(vbox);

        _tickLabel   = MakeLabel();
        _seasonLabel = MakeLabel();
        _yearLabel   = MakeLabel();
        _speedLabel  = MakeLabel();

        foreach (var lbl in new[] { _tickLabel, _seasonLabel, _yearLabel, _speedLabel })
            vbox.AddChild(lbl);

        var hint = new Label
        {
            Text        = "Space=pause  +/-=speed  R=restart  MMB=pan  Wheel=zoom  LClick=inspect",
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        hint.AddThemeColorOverride("font_color", new Color(0.7f, 0.7f, 0.7f));
        vbox.AddChild(hint);

        var resetBtn = new Button { Text = "Restart [R]" };
        resetBtn.Pressed += () => SimManager.Instance.Reset();
        vbox.AddChild(resetBtn);

        SimManager.Instance.Ticked        += Refresh;
        SimManager.Instance.PausedChanged += OnPausedChanged;
        SimManager.Instance.WorldReset    += Refresh;
        Refresh();
    }

    private void Refresh()
    {
        var sim   = SimManager.Instance;
        var state = sim.World.State;

        _tickLabel.Text   = $"Tick    {state.Tick}";
        _yearLabel.Text   = $"Year    {state.Tick / (World.TicksPerSeason * 4) + 1}";
        _seasonLabel.Text = state.CurrentSeason.ToString();
        _seasonLabel.AddThemeColorOverride("font_color", SeasonColor(state.CurrentSeason));
        _speedLabel.Text  = $"{sim.TickInterval:F2}s / tick";
    }

    private void OnPausedChanged(bool paused)
    {
        _speedLabel.Text = paused ? "PAUSED" : $"{SimManager.Instance.TickInterval:F2}s / tick";
    }

    private static Label MakeLabel() => new Label { MouseFilter = Control.MouseFilterEnum.Ignore };

    private static Color SeasonColor(Season s) => s switch
    {
        Season.Spring => new Color(0.5f, 1.0f, 0.5f),
        Season.Summer => Colors.Yellow,
        Season.Autumn => new Color(1.0f, 0.6f, 0.1f),
        Season.Winter => new Color(0.6f, 0.85f, 1.0f),
        _             => Colors.White,
    };
}
