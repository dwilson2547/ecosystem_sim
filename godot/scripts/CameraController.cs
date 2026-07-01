using Godot;

namespace EcosystemGame;

/// <summary>
/// Camera2D with middle-mouse-drag panning and scroll-wheel zoom.
/// Starts zoomed out to show the full 10×10 hex map.
/// </summary>
public partial class CameraController : Camera2D
{
    [Export] public float ZoomStep { get; set; } = 0.1f;
    [Export] public float ZoomMin  { get; set; } = 0.15f;
    [Export] public float ZoomMax  { get; set; } = 3.0f;

    private bool    _dragging;
    private Vector2 _dragStart;
    private Vector2 _cameraOrigin;

    public override void _Ready()
    {
        // center on a 10×10 pointy-top hex grid with HexSize=60
        // map width ≈ 10 * 60*√3 ≈ 1039px, height ≈ 10 * 60*1.5 ≈ 900px
        Position = new Vector2(520f, 420f);
        Zoom     = Vector2.One * 0.55f;
    }

    public override void _UnhandledInput(InputEvent ev)
    {
        if (ev is InputEventMouseButton mb)
        {
            if (mb.ButtonIndex == MouseButton.Middle)
            {
                _dragging     = mb.Pressed;
                _dragStart    = mb.GlobalPosition;
                _cameraOrigin = GlobalPosition;
            }
            else if (mb.ButtonIndex == MouseButton.WheelUp && mb.Pressed)
                Zoom = Vector2.One * Mathf.Clamp(Zoom.X + ZoomStep, ZoomMin, ZoomMax);
            else if (mb.ButtonIndex == MouseButton.WheelDown && mb.Pressed)
                Zoom = Vector2.One * Mathf.Clamp(Zoom.X - ZoomStep, ZoomMin, ZoomMax);
        }

        if (ev is InputEventMouseMotion mm && _dragging)
            GlobalPosition = _cameraOrigin + (_dragStart - mm.GlobalPosition) / Zoom.X;
    }
}
