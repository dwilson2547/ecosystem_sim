using Godot;
using EcosystemSim;

namespace EcosystemGame;

public partial class ScenarioSelectionOverlay : CanvasLayer
{
    private Control _root = null!;
    private Label _error = null!;

    public override void _Ready()
    {
        Layer = 100;

        _root = new ColorRect
        {
            Color = new Color(0.02f, 0.03f, 0.05f, 0.94f),
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        _root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(_root);

        var center = new CenterContainer();
        center.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _root.AddChild(center);

        var panel = new PanelContainer { CustomMinimumSize = new Vector2(640f, 0f) };
        center.AddChild(panel);

        var content = new VBoxContainer();
        content.AddThemeConstantOverride("separation", 14);
        panel.AddChild(content);

        AddLabel(content, "ECOSYSTEMSIM", 24, new Color(0.65f, 0.9f, 1f));
        AddLabel(content, "Choose how you want to shape the world.", 15, Colors.White);
        content.AddChild(new HSeparator());

        AddMode(
            content,
            "SANDBOX",
            "Unrestricted simulation. Interventions are free and there is no time limit.",
            "Start Sandbox",
            ScenarioKind.Sandbox);

        content.AddChild(new HSeparator());

        AddMode(
            content,
            "LOCUST PLAGUE",
            "Three years · 10 action points\n"
            + "Keep all dinosaur lineages alive, reduce locusts to 500 or fewer, "
            + "and preserve at least 25% average plains grass.",
            "Start Challenge",
            ScenarioKind.LocustPlague);

        _error = AddLabel(content, string.Empty, 12, new Color(1f, 0.35f, 0.35f));
        SimManager.Instance.ScenarioSelectionRequested += ShowSelection;
    }

    private void AddMode(
        Control parent,
        string title,
        string description,
        string buttonText,
        ScenarioKind kind)
    {
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 6);
        parent.AddChild(box);
        AddLabel(box, title, 18, new Color(0.8f, 0.9f, 1f));
        var descriptionLabel = AddLabel(box, description, 13, new Color(0.78f, 0.8f, 0.85f));
        descriptionLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;

        var button = new Button { Text = buttonText, CustomMinimumSize = new Vector2(0f, 42f) };
        button.Pressed += () => Start(kind);
        box.AddChild(button);
    }

    private void Start(ScenarioKind kind)
    {
        try
        {
            SimManager.Instance.StartScenario(kind);
            _error.Text = string.Empty;
            _root.Visible = false;
        }
        catch (Exception ex)
        {
            _error.Text = $"Unable to start scenario: {ex.Message}";
            GD.PrintErr(ex);
        }
    }

    private void ShowSelection()
    {
        _error.Text = string.Empty;
        _root.Visible = true;
    }

    private static Label AddLabel(Control parent, string text, int size, Color color)
    {
        var label = new Label { Text = text };
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", color);
        parent.AddChild(label);
        return label;
    }
}
