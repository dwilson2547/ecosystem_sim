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

    public override void _Ready()
    {
        Instance = this;
        World    = DemoWorldSeeder.Create();
    }

    public override void _Process(double delta)
    {
        if (_paused) return;
        _elapsed += (float)delta;
        if (_elapsed < TickInterval) return;
        _elapsed = 0f;
        World.Tick();
        EmitSignal(SignalName.Ticked);
    }

    public void TogglePause() => Paused = !_paused;

    // Each call shrinks/grows the tick interval by a fixed step
    public void SpeedUp()   => TickInterval = MathF.Max(0.25f, TickInterval - 0.25f);
    public void SpeedDown() => TickInterval = MathF.Min(8.0f,  TickInterval + 0.5f);
}
