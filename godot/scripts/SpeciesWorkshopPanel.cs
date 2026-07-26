using EcosystemSim;
using Godot;

namespace EcosystemGame;

public partial class SpeciesWorkshopPanel : CanvasLayer
{
    private ColorRect _root = null!;
    private VBoxContainer _savedList = null!;
    private OptionButton _baseSelect = null!;
    private LineEdit _name = null!;
    private SpinBox _size = null!;
    private SpinBox _immunity = null!;
    private SpinBox _breeding = null!;
    private SpinBox _aggression = null!;
    private ColorPickerButton _tint = null!;
    private HSlider _spriteScale = null!;
    private TextureRect _preview = null!;
    private Label _points = null!;
    private Label _stats = null!;
    private Label _feedback = null!;
    private Label _scaleLabel = null!;
    private string? _editingOriginalName;
    private string? _pendingDeleteName;
    private bool _resumeAfterClose;
    private readonly Dictionary<string, SpeciesDefinition> _baseSpecies =
        new(StringComparer.OrdinalIgnoreCase);

    public override void _Ready()
    {
        Layer = 110;
        LoadBaseSpecies();

        _root = new ColorRect
        {
            Color = new Color(0.015f, 0.02f, 0.035f, 0.96f),
            MouseFilter = Control.MouseFilterEnum.Stop,
            Visible = false,
        };
        _root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(_root);

        var center = new CenterContainer();
        center.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _root.AddChild(center);

        var panel = new PanelContainer { CustomMinimumSize = new Vector2(1040f, 650f) };
        center.AddChild(panel);
        var columns = new HBoxContainer();
        columns.AddThemeConstantOverride("separation", 18);
        panel.AddChild(columns);

        columns.AddChild(BuildSavedColumn());
        columns.AddChild(new VSeparator());
        columns.AddChild(BuildEditorColumn());

        var confirm = new ConfirmationDialog
        {
            Title = "Delete custom species?",
            DialogText = "This removes the saved template. Existing running populations are unaffected.",
        };
        confirm.Confirmed += ConfirmDelete;
        AddChild(confirm);
        confirm.Name = "DeleteConfirmation";

        SimManager.Instance.WorkshopToggleRequested += Toggle;
        CustomSpeciesLibrary.Changed += OnLibraryChanged;
        ResetForm();
        RebuildSavedList();
    }

    private Control BuildSavedColumn()
    {
        var column = new VBoxContainer { CustomMinimumSize = new Vector2(300f, 0f) };
        var titleRow = new HBoxContainer();
        var title = Label("SAVED SPECIES", 17, new Color(0.65f, 0.9f, 1f));
        title.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        titleRow.AddChild(title);
        var create = new Button { Text = "New" };
        create.Pressed += ResetForm;
        titleRow.AddChild(create);
        column.AddChild(titleRow);

        var scroll = new ScrollContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        column.AddChild(scroll);
        _savedList = new VBoxContainer { CustomMinimumSize = new Vector2(280f, 0f) };
        _savedList.AddThemeConstantOverride("separation", 8);
        scroll.AddChild(_savedList);
        return column;
    }

    private Control BuildEditorColumn()
    {
        var scroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(690f, 0f),
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        var form = new VBoxContainer { CustomMinimumSize = new Vector2(660f, 0f) };
        form.AddThemeConstantOverride("separation", 8);
        scroll.AddChild(form);

        var header = new HBoxContainer();
        var title = Label("SPECIES WORKSHOP", 20, Colors.White);
        title.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        header.AddChild(title);
        var close = new Button { Text = "×", TooltipText = "Close workshop" };
        close.Pressed += Toggle;
        header.AddChild(close);
        form.AddChild(header);
        form.AddChild(Label(
            "Build from an existing body plan. Every trait step costs one of eight design points.",
            12,
            new Color(0.72f, 0.76f, 0.84f)));
        form.AddChild(new HSeparator());

        _name = new LineEdit { PlaceholderText = "Species name", MaxLength = 28 };
        _name.TextChanged += _ => RefreshPreview();
        AddField(form, "Name", _name);

        _baseSelect = new OptionButton();
        foreach (var baseName in DemoWorldSeeder.CustomizableBaseNames)
            _baseSelect.AddItem(baseName);
        _baseSelect.ItemSelected += _ => RefreshPreview();
        AddField(form, "Base body", _baseSelect);

        _points = Label(string.Empty, 14, Colors.White);
        form.AddChild(_points);
        _size = AddTrait(form, "Body size", -2, 2);
        _immunity = AddTrait(form, "Immunity", 0, 3);
        _breeding = AddTrait(form, "Breeding rate", -2, 2);
        _aggression = AddTrait(form, "Aggression", -2, 2);

        var appearance = new HBoxContainer();
        appearance.AddThemeConstantOverride("separation", 12);
        _preview = new TextureRect
        {
            CustomMinimumSize = new Vector2(150f, 120f),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
        };
        appearance.AddChild(_preview);
        var appearanceFields = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        _tint = new ColorPickerButton { Color = Colors.White, CustomMinimumSize = new Vector2(0f, 38f) };
        _tint.ColorChanged += _ => RefreshPreview();
        AddField(appearanceFields, "Sprite tint", _tint);
        _spriteScale = new HSlider { MinValue = 0.7, MaxValue = 1.4, Step = 0.1, Value = 1 };
        _spriteScale.ValueChanged += _ => RefreshPreview();
        _scaleLabel = Label(string.Empty, 11, new Color(0.7f, 0.74f, 0.82f));
        appearanceFields.AddChild(_scaleLabel);
        appearanceFields.AddChild(_spriteScale);
        appearance.AddChild(appearanceFields);
        form.AddChild(appearance);

        _stats = Label(string.Empty, 12, new Color(0.76f, 0.86f, 0.96f));
        _stats.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        form.AddChild(_stats);

        _feedback = Label(string.Empty, 12, Colors.White);
        _feedback.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        form.AddChild(_feedback);

        var actions = new HBoxContainer();
        var save = new Button
        {
            Text = "Save species",
            CustomMinimumSize = new Vector2(180f, 42f),
        };
        save.Pressed += Save;
        actions.AddChild(save);
        var cancel = new Button { Text = "Reset form" };
        cancel.Pressed += ResetForm;
        actions.AddChild(cancel);
        form.AddChild(actions);
        return scroll;
    }

    private SpinBox AddTrait(Control parent, string title, int min, int max)
    {
        var row = new HBoxContainer();
        var label = Label(title, 12, Colors.White);
        label.CustomMinimumSize = new Vector2(180f, 0f);
        row.AddChild(label);
        var value = new SpinBox
        {
            MinValue = min,
            MaxValue = max,
            Step = 1,
            AllowGreater = false,
            AllowLesser = false,
            CustomMinimumSize = new Vector2(120f, 0f),
        };
        value.ValueChanged += _ => RefreshPreview();
        row.AddChild(value);
        parent.AddChild(row);
        return value;
    }

    private void RebuildSavedList()
    {
        Clear(_savedList);
        if (!string.IsNullOrEmpty(CustomSpeciesLibrary.LastError))
            _savedList.AddChild(Label(
                CustomSpeciesLibrary.LastError,
                11,
                new Color(1f, 0.4f, 0.35f)));

        if (CustomSpeciesLibrary.Species.Count == 0)
        {
            var empty = Label(
                "No custom species yet.\nCreate one here, then select it when starting Sandbox.",
                12,
                new Color(0.65f, 0.68f, 0.75f));
            empty.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            _savedList.AddChild(empty);
            return;
        }

        foreach (var saved in CustomSpeciesLibrary.Species.OrderBy(item => item.Template.Name))
        {
            var card = new PanelContainer();
            var row = new HBoxContainer();
            card.AddChild(row);
            var text = Label(
                $"{saved.Template.Name}\n{saved.Template.BaseSpeciesName} · {saved.Template.PointsSpent}/8 pts",
                12,
                Colors.White);
            text.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            row.AddChild(text);
            var edit = new Button { Text = "Edit" };
            edit.Pressed += () => Edit(saved);
            row.AddChild(edit);
            var delete = new Button { Text = "Delete" };
            delete.Pressed += () => RequestDelete(saved.Template.Name);
            row.AddChild(delete);
            _savedList.AddChild(card);
        }
    }

    private void RefreshPreview()
    {
        if (_baseSelect.ItemCount == 0) return;
        var template = CurrentTemplate();
        var baseSpecies = _baseSpecies[template.BaseSpeciesName];
        _points.Text = $"Design points: {template.PointsSpent}/{CustomSpeciesTemplate.TraitPointBudget}";
        _points.AddThemeColorOverride(
            "font_color",
            template.PointsSpent > CustomSpeciesTemplate.TraitPointBudget
                ? new Color(1f, 0.35f, 0.35f)
                : new Color(1f, 0.85f, 0.35f));
        _scaleLabel.Text = $"Sprite scale: {_spriteScale.Value:F1}×";
        _preview.Texture = SpeciesVisuals.IconForBase(template.BaseSpeciesName);
        _preview.Modulate = _tint.Color;
        _preview.Scale = Vector2.One * (float)_spriteScale.Value;

        var sizeScale = template.SizeScale;
        var diet = baseSpecies.IsPredator
            ? "Prey: " + string.Join(", ", baseSpecies.PreferredPrey)
            : "Diet: " + string.Join(", ", baseSpecies.EaseOfEating.Keys);
        var terrain = baseSpecies.AllowedTerrains.Count > 0
            ? string.Join(", ", baseSpecies.AllowedTerrains)
            : "land biomes";
        _stats.Text =
            $"Food demand {baseSpecies.FoodConsumptionRate * sizeScale:F2} · "
            + $"Prey demand {baseSpecies.PreyConsumptionRate * sizeScale:F3} · "
            + $"Combat {baseSpecies.CombatStrength * MathF.Sqrt(sizeScale):F2}\n"
            + $"Immunity {MathF.Min(1f, baseSpecies.Immunity + template.ImmunitySteps * 0.05f):P0} · "
            + $"Breeding {baseSpecies.BreedingRate * template.BreedingScale:P0} · "
            + $"Aggression {Math.Clamp(baseSpecies.WarAggression + template.AggressionSteps * 0.1f, 0f, 1f):P0}\n"
            + $"{diet} · Terrain: {terrain}";
    }

    private CustomSpeciesTemplate CurrentTemplate() => new()
    {
        Name = _name.Text,
        BaseSpeciesName = _baseSelect.GetItemText(_baseSelect.Selected),
        SizeSteps = (int)_size.Value,
        ImmunitySteps = (int)_immunity.Value,
        BreedingSteps = (int)_breeding.Value,
        AggressionSteps = (int)_aggression.Value,
    };

    private void Save()
    {
        var saved = new SavedCustomSpecies
        {
            Template = CurrentTemplate(),
            TintHex = _tint.Color.ToHtml(false),
            SpriteScale = (float)_spriteScale.Value,
        };
        var success = CustomSpeciesLibrary.Upsert(saved, _editingOriginalName, out var message);
        SetFeedback(message, success);
        if (!success) return;
        _editingOriginalName = saved.Template.Name;
        RebuildSavedList();
    }

    private void Edit(SavedCustomSpecies saved)
    {
        _editingOriginalName = saved.Template.Name;
        _name.Text = saved.Template.Name;
        SelectBase(saved.Template.BaseSpeciesName);
        _size.Value = saved.Template.SizeSteps;
        _immunity.Value = saved.Template.ImmunitySteps;
        _breeding.Value = saved.Template.BreedingSteps;
        _aggression.Value = saved.Template.AggressionSteps;
        _tint.Color = Color.FromHtml(saved.TintHex);
        _spriteScale.Value = saved.SpriteScale;
        SetFeedback($"Editing {saved.Template.Name}.", true);
        RefreshPreview();
    }

    private void ResetForm()
    {
        _editingOriginalName = null;
        _name.Text = string.Empty;
        if (_baseSelect.ItemCount > 0) _baseSelect.Select(0);
        _size.Value = 0;
        _immunity.Value = 0;
        _breeding.Value = 0;
        _aggression.Value = 0;
        _tint.Color = Colors.White;
        _spriteScale.Value = 1;
        SetFeedback(string.Empty, true);
        RefreshPreview();
    }

    private void RequestDelete(string name)
    {
        _pendingDeleteName = name;
        var dialog = GetNode<ConfirmationDialog>("DeleteConfirmation");
        dialog.DialogText =
            $"Delete {name}? This removes the saved template. Existing running populations are unaffected.";
        dialog.PopupCentered();
    }

    private void ConfirmDelete()
    {
        if (_pendingDeleteName is null) return;
        var name = _pendingDeleteName;
        _pendingDeleteName = null;
        var success = CustomSpeciesLibrary.Delete(name, out var message);
        SetFeedback(message, success);
        if (success && string.Equals(_editingOriginalName, name, StringComparison.OrdinalIgnoreCase))
            ResetForm();
    }

    private void Toggle()
    {
        var opening = !_root.Visible;
        _root.Visible = opening;
        SimManager.Instance.SetModalOpen("species-workshop", opening);
        if (opening)
        {
            _resumeAfterClose = SimManager.Instance.HasStarted && !SimManager.Instance.Paused;
            SimManager.Instance.Paused = true;
            RebuildSavedList();
            RefreshPreview();
        }
        else if (_resumeAfterClose)
        {
            _resumeAfterClose = false;
            SimManager.Instance.Paused = false;
        }
    }

    private void OnLibraryChanged() => CallDeferred(MethodName.RebuildSavedList);

    private void LoadBaseSpecies()
    {
        var world = DemoWorldSeeder.Create();
        foreach (var population in world.State.Map.AllPopulations())
        {
            if (DemoWorldSeeder.CustomizableBaseNames.Contains(population.Species.Name))
                _baseSpecies[population.Species.Name] = population.Species;
        }
    }

    private void SelectBase(string baseName)
    {
        for (var i = 0; i < _baseSelect.ItemCount; i++)
        {
            if (_baseSelect.GetItemText(i) != baseName) continue;
            _baseSelect.Select(i);
            return;
        }
    }

    private void SetFeedback(string message, bool success)
    {
        _feedback.Text = message;
        _feedback.AddThemeColorOverride(
            "font_color",
            success ? new Color(0.45f, 1f, 0.45f) : new Color(1f, 0.4f, 0.35f));
    }

    private static void AddField(Control parent, string title, Control field)
    {
        parent.AddChild(Label(title, 11, new Color(0.7f, 0.74f, 0.82f)));
        parent.AddChild(field);
    }

    private static Label Label(string text, int size, Color color)
    {
        var label = new Label { Text = text };
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", color);
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
