using EcosystemSim;
using Godot;

namespace EcosystemGame;

public partial class ScenarioSelectionOverlay : CanvasLayer
{
    private Control _root = null!;
    private VBoxContainer _content = null!;
    private Label _error = null!;
    private readonly Dictionary<string, CheckBox> _customRoster =
        new(StringComparer.OrdinalIgnoreCase);

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

        var panel = new PanelContainer { CustomMinimumSize = new Vector2(720f, 680f) };
        center.AddChild(panel);
        var scroll = new ScrollContainer();
        panel.AddChild(scroll);
        _content = new VBoxContainer { CustomMinimumSize = new Vector2(680f, 0f) };
        _content.AddThemeConstantOverride("separation", 12);
        scroll.AddChild(_content);

        SimManager.Instance.ScenarioSelectionRequested += ShowSelection;
        CustomSpeciesLibrary.Changed += OnLibraryChanged;
        Rebuild();
        SimManager.Instance.SetModalOpen("scenario-selection", true);
    }

    private void Rebuild()
    {
        var selected = _customRoster
            .Where(item => item.Value.ButtonPressed)
            .Select(item => item.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Clear(_content);
        _customRoster.Clear();

        AddLabel(_content, "ECOSYSTEMSIM", 24, new Color(0.65f, 0.9f, 1f));
        AddLabel(_content, "Choose how you want to shape the world.", 15, Colors.White);
        _content.AddChild(new HSeparator());

        var sandbox = new VBoxContainer();
        sandbox.AddThemeConstantOverride("separation", 6);
        _content.AddChild(sandbox);
        AddLabel(sandbox, "SANDBOX", 18, new Color(0.8f, 0.9f, 1f));
        var sandboxDescription = AddLabel(
            sandbox,
            "Unrestricted simulation. Interventions are free and there is no time limit.",
            13,
            new Color(0.78f, 0.8f, 0.85f));
        sandboxDescription.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        AddCustomRoster(sandbox, selected);
        var sandboxStart = new Button
        {
            Text = "Start Sandbox",
            CustomMinimumSize = new Vector2(0f, 42f),
        };
        sandboxStart.Pressed += () => Start(ScenarioKind.Sandbox);
        sandbox.AddChild(sandboxStart);

        _content.AddChild(new HSeparator());
        AddMode(
            "LOCUST PLAGUE",
            "Three years · 10 action points\n"
            + "Keep all dinosaur lineages alive, reduce locusts to 500 or fewer, "
            + "and preserve at least 25% average plains grass.",
            ScenarioKind.LocustPlague);
        _content.AddChild(new HSeparator());
        AddMode(
            "DROUGHT RECOVERY",
            "Two years · 10 action points\n"
            + "Keep all dinosaur lineages alive and restore average land freshwater to 55% "
            + "and vegetation to 50%.",
            ScenarioKind.DroughtRecovery);

        _error = AddLabel(_content, CustomSpeciesLibrary.LastError, 12, new Color(1f, 0.35f, 0.35f));
    }

    private void AddCustomRoster(Control parent, HashSet<string> selected)
    {
        var header = new HBoxContainer();
        var title = AddLabel(
            header,
            "Custom starting roster",
            12,
            new Color(1f, 0.85f, 0.35f));
        title.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        var workshop = new Button { Text = "Species Workshop" };
        workshop.Pressed += SimManager.Instance.ToggleWorkshop;
        header.AddChild(workshop);
        parent.AddChild(header);

        if (CustomSpeciesLibrary.Species.Count == 0)
        {
            var empty = AddLabel(
                parent,
                "No saved custom species. Open the workshop to create one.",
                11,
                new Color(0.62f, 0.64f, 0.72f));
            empty.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            return;
        }

        foreach (var saved in CustomSpeciesLibrary.Species.OrderBy(item => item.Template.Name))
        {
            var checkbox = new CheckBox
            {
                Text =
                    $"{saved.Template.Name} — {saved.Template.BaseSpeciesName}, "
                    + $"{saved.Template.PointsSpent}/{CustomSpeciesTemplate.TraitPointBudget} points",
                ButtonPressed = selected.Contains(saved.Template.Name),
            };
            parent.AddChild(checkbox);
            _customRoster[saved.Template.Name] = checkbox;
        }
    }

    private void AddMode(string title, string description, ScenarioKind kind)
    {
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 6);
        _content.AddChild(box);
        AddLabel(box, title, 18, new Color(0.8f, 0.9f, 1f));
        var descriptionLabel = AddLabel(box, description, 13, new Color(0.78f, 0.8f, 0.85f));
        descriptionLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;

        var button = new Button
        {
            Text = "Start Challenge",
            CustomMinimumSize = new Vector2(0f, 42f),
        };
        button.Pressed += () => Start(kind);
        box.AddChild(button);
    }

    private void Start(ScenarioKind kind)
    {
        try
        {
            var customSpecies = kind == ScenarioKind.Sandbox
                ? _customRoster
                    .Where(item => item.Value.ButtonPressed)
                    .Select(item => CustomSpeciesLibrary.Find(item.Key))
                    .Where(saved => saved is not null)
                    .Cast<SavedCustomSpecies>()
                    .ToList()
                : [];
            SimManager.Instance.StartScenario(kind, customSpecies);
            _error.Text = string.Empty;
            _root.Visible = false;
            SimManager.Instance.SetModalOpen("scenario-selection", false);
        }
        catch (Exception ex)
        {
            _error.Text = $"Unable to start scenario: {ex.Message}";
            GD.PrintErr(ex);
        }
    }

    private void ShowSelection()
    {
        Rebuild();
        _root.Visible = true;
        SimManager.Instance.SetModalOpen("scenario-selection", true);
    }

    private void OnLibraryChanged() => CallDeferred(MethodName.Rebuild);

    private static Label AddLabel(Control parent, string text, int size, Color color)
    {
        var label = new Label { Text = text };
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", color);
        parent.AddChild(label);
        return label;
    }

    private static void Clear(Node node)
    {
        while (node.GetChildCount() > 0)
        {
            var child = node.GetChild(0);
            node.RemoveChild(child);
            child.Free();
        }
    }
}
