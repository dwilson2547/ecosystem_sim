using Godot;
using EcosystemSim;

namespace EcosystemGame;

public partial class ScenarioPanel : CanvasLayer
{
    private VBoxContainer _content = null!;

    public override void _Ready()
    {
        var panel = new PanelContainer();
        panel.AnchorLeft = 1f;
        panel.AnchorRight = 1f;
        panel.OffsetLeft = -650f;
        panel.OffsetRight = -310f;
        panel.OffsetTop = 10f;
        panel.CustomMinimumSize = new Vector2(340f, 0f);
        AddChild(panel);

        _content = new VBoxContainer();
        _content.AddThemeConstantOverride("separation", 4);
        panel.AddChild(_content);

        SimManager.Instance.Ticked += Rebuild;
        SimManager.Instance.WorldReset += Rebuild;
        SimManager.Instance.ScenarioChanged += Rebuild;
        Rebuild();
    }

    private void Rebuild()
    {
        while (_content.GetChildCount() > 0)
        {
            var child = _content.GetChild(0);
            _content.RemoveChild(child);
            child.Free();
        }

        var scenario = SimManager.Instance.World.State.Scenario;
        if (scenario is null)
        {
            Row("Choose a scenario to begin.", 13, new Color(0.7f, 0.7f, 0.7f));
            return;
        }

        Row(scenario.Name.ToUpperInvariant(), 15, new Color(0.65f, 0.9f, 1f));
        if (!scenario.IsChallenge)
        {
            Row("Unlimited time · Unlimited actions", 12, new Color(0.5f, 1f, 0.5f));
            Row("Select a tile to use interventions.", 11, new Color(0.75f, 0.75f, 0.8f));
        }
        else
        {
            var state = SimManager.Instance.World.State;
            Row($"Days remaining: {scenario.RemainingDays(state)}", 12, Colors.White);
            Row($"Action points: {scenario.ActionPointsRemaining}/{scenario.InitialActionPoints}",
                12, new Color(1f, 0.85f, 0.35f));
            _content.AddChild(new HSeparator());

            foreach (var objective in scenario.Objectives)
            {
                var color = objective.IsMet
                    ? new Color(0.45f, 1f, 0.45f)
                    : new Color(1f, 0.55f, 0.25f);
                Row($"{(objective.IsMet ? "✓" : "○")} {objective.Label}", 12, color);
                Row($"    {objective.CurrentValue}  target {objective.TargetValue}", 11,
                    new Color(0.75f, 0.75f, 0.8f));
            }

            if (scenario.Status == ScenarioStatus.Active)
                Row("Select a tile to spend action points.", 11, new Color(0.75f, 0.75f, 0.8f));

            if (scenario.Status != ScenarioStatus.Active)
            {
                _content.AddChild(new HSeparator());
                var color = scenario.Status == ScenarioStatus.Won
                    ? new Color(0.4f, 1f, 0.4f)
                    : new Color(1f, 0.35f, 0.35f);
                Row(scenario.Status == ScenarioStatus.Won ? "VICTORY" : "DEFEAT", 16, color);
                var result = Row(scenario.ResultMessage, 12, color);
                result.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            }
        }

        var buttons = new HBoxContainer();
        var restart = new Button { Text = "Restart" };
        restart.Pressed += () => SimManager.Instance.CallDeferred(SimManager.MethodName.Reset);
        buttons.AddChild(restart);
        var choose = new Button { Text = "New scenario" };
        choose.Pressed += () =>
            SimManager.Instance.CallDeferred(SimManager.MethodName.RequestScenarioSelection);
        buttons.AddChild(choose);
        _content.AddChild(buttons);
    }

    private Label Row(string text, int size, Color color)
    {
        var label = new Label { Text = text };
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", color);
        _content.AddChild(label);
        return label;
    }
}
