using System;
using Godot;

namespace EcosystemGame;

/// <summary>
/// Root node. Creates the camera, map renderer, HUD, and tile-info panel in _Ready.
/// Input for pause/speed and tile selection is handled here.
/// </summary>
public partial class SimMain : Node2D
{
    private HexMapRenderer _hexMap = null!;
    private TileInfoPanel  _panel  = null!;

    public override void _Ready()
    {
        AddChild(new CameraController());

        _hexMap = new HexMapRenderer();
        AddChild(_hexMap);

        AddChild(new HUD());
        AddChild(new FactionPanel());

        _panel = new TileInfoPanel();
        AddChild(_panel);
    }

    public override void _UnhandledInput(InputEvent ev)
    {
        if (ev is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left && mb.Pressed)
        {
            try
            {
                var worldPos = GetGlobalMousePosition();
                var tile = _hexMap.PixelToTile(worldPos);
                _hexMap.SelectTile(tile);
                if (tile is not null) _panel.ShowTile(tile);
                else _panel.HidePanel();
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[SimMain] click handler: {ex.Message}");
            }
            return;
        }

        if (ev is not InputEventKey key || !key.Pressed || key.Echo) return;

        switch (key.PhysicalKeycode)
        {
            case Key.Space:
                SimManager.Instance.TogglePause();
                break;
            case Key.Equal:
                SimManager.Instance.SpeedUp();
                break;
            case Key.Minus:
                SimManager.Instance.SpeedDown();
                break;
        }
    }
}
