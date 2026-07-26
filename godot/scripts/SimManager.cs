using Godot;
using EcosystemSim;

namespace EcosystemGame;

/// <summary>
/// Autoloaded singleton. Owns the World instance and drives the tick timer.
/// All other nodes read World state through SimManager.Instance.
/// </summary>
public partial class SimManager : Node
{
    public static SimManager Instance { get; private set; } = null!;

    public World World { get; private set; } = null!;
    public ScenarioKind? CurrentScenarioKind { get; private set; }
    public bool HasStarted => CurrentScenarioKind.HasValue;

    // Seconds between ticks — 2.0 by default (intentionally slow; tune in settings later)
    [Export] public float TickInterval { get; set; } = 2.0f;

    private float _elapsed;
    private bool  _paused;

    public bool Paused
    {
        get => _paused;
        set { _paused = value; EmitSignal(SignalName.PausedChanged, value); }
    }

    [Signal] public delegate void TickedEventHandler();
    [Signal] public delegate void PausedChangedEventHandler(bool paused);
    [Signal] public delegate void WorldResetEventHandler();
    [Signal] public delegate void ScenarioChangedEventHandler();
    [Signal] public delegate void ScenarioSelectionRequestedEventHandler();
    [Signal] public delegate void AnalysisToggleRequestedEventHandler();
    [Signal] public delegate void FactionToggleRequestedEventHandler();
    [Signal] public delegate void ScenarioToggleRequestedEventHandler();

    public override void _Ready()
    {
        Instance = this;
        World    = DemoWorldSeeder.Create();
        Paused   = true;
    }

    public override void _Process(double delta)
    {
        if (_paused) return;
        _elapsed += (float)delta;
        if (_elapsed < TickInterval) return;
        _elapsed = 0f;
        World.Tick();
        EmitSignal(SignalName.Ticked);
        if (World.State.Scenario is { IsChallenge: true, Status: not ScenarioStatus.Active })
            Paused = true;
    }

    public void TogglePause()
    {
        if (!HasStarted) return;
        if (World.State.Scenario is { IsChallenge: true, Status: not ScenarioStatus.Active }) return;
        Paused = !_paused;
    }

    public void StartScenario(ScenarioKind kind)
    {
        _elapsed = 0f;
        World = DemoWorldSeeder.Create();
        World.StartScenario(kind);
        CurrentScenarioKind = kind;
        GD.Print($"[Scenario] started {kind}");
        EmitSignal(SignalName.WorldReset);
        EmitSignal(SignalName.ScenarioChanged);
        Paused = false;
    }

    public void Reset()
    {
        if (CurrentScenarioKind is { } kind)
            StartScenario(kind);
    }

    public void RequestScenarioSelection()
    {
        Paused = true;
        EmitSignal(SignalName.ScenarioSelectionRequested);
    }

    public void ToggleAnalysis() => EmitSignal(SignalName.AnalysisToggleRequested);
    public void ToggleFactions() => EmitSignal(SignalName.FactionToggleRequested);
    public void ToggleScenarioPanel() => EmitSignal(SignalName.ScenarioToggleRequested);

    public ScenarioActionResult TryApplyScenarioAction(IScenarioAction action)
    {
        var result = World.TryApplyScenarioAction(action);
        GD.Print($"[Scenario] action {action.Name}: {(result.Success ? "success" : "failed")} — {result.Message}");
        if (result.Success)
            CallDeferred(MethodName.NotifyScenarioActionChanged);
        return result;
    }

    public GuidedEvolutionResult TryGuideEvolution(
        Population population,
        GuidedEvolutionTrait trait)
    {
        var result = World.TryGuideEvolution(population, trait);
        GD.Print($"[Evolution] {trait}: {(result.Success ? "success" : "failed")} — {result.Message}");
        if (result.Success)
            CallDeferred(MethodName.NotifyScenarioActionChanged);
        return result;
    }

    public GuidedEvolutionResult TryCreateGuidedSubspecies(
        Population population,
        string name)
    {
        var result = World.TryCreateGuidedSubspecies(population, name);
        GD.Print($"[Evolution] branch: {(result.Success ? "success" : "failed")} — {result.Message}");
        if (result.Success)
            CallDeferred(MethodName.NotifyScenarioActionChanged);
        return result;
    }

    private void NotifyScenarioActionChanged()
    {
        EmitSignal(SignalName.Ticked);
        EmitSignal(SignalName.ScenarioChanged);
    }

    // Each call shrinks/grows the tick interval by a fixed step
    public void SpeedUp()   => TickInterval = MathF.Max(0.25f, TickInterval - 0.25f);
    public void SpeedDown() => TickInterval = MathF.Min(8.0f,  TickInterval + 0.5f);
}
