using Godot;

namespace EcosystemGame;

/// <summary>
/// Display settings (resolution, fullscreen) persisted to user://settings.cfg across launches.
/// </summary>
public static class GameSettings
{
    public static readonly (string Label, Vector2I Size)[] Resolutions =
    [
        ("1280 × 720 (Default)", new Vector2I(1280, 720)),
        ("1920 × 1080", new Vector2I(1920, 1080)),
        ("2560 × 1440", new Vector2I(2560, 1440)),
    ];

    private const string ConfigPath = "user://settings.cfg";

    public static Vector2I Resolution { get; private set; } = Resolutions[0].Size;
    public static bool Fullscreen { get; private set; }

    public static void Load()
    {
        var cfg = new ConfigFile();
        if (cfg.Load(ConfigPath) != Error.Ok) return;

        var width  = (int)cfg.GetValue("display", "width", Resolution.X);
        var height = (int)cfg.GetValue("display", "height", Resolution.Y);
        Resolution  = new Vector2I(width, height);
        Fullscreen  = (bool)cfg.GetValue("display", "fullscreen", false);
    }

    public static void Apply()
    {
        if (Fullscreen)
        {
            DisplayServer.WindowSetMode(DisplayServer.WindowMode.Fullscreen);
            return;
        }

        DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
        DisplayServer.WindowSetSize(Resolution);

        var screenSize = DisplayServer.ScreenGetSize();
        DisplayServer.WindowSetPosition((screenSize - Resolution) / 2);
    }

    public static void SetResolution(Vector2I size)
    {
        Resolution = size;
        Fullscreen = false;
        Apply();
        Save();
    }

    public static void SetFullscreen(bool enabled)
    {
        Fullscreen = enabled;
        Apply();
        Save();
    }

    private static void Save()
    {
        var cfg = new ConfigFile();
        cfg.SetValue("display", "width", Resolution.X);
        cfg.SetValue("display", "height", Resolution.Y);
        cfg.SetValue("display", "fullscreen", Fullscreen);
        cfg.Save(ConfigPath);
    }
}
