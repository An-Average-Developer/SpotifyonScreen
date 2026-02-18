namespace SpotifyOnScreen.Models;

public class WindowPosition
{
    public double X { get; set; } = 10;
    public double Y { get; set; } = 10;
    public double Width { get; set; } = 380;
    public double Height { get; set; } = 0; // 0 = auto height
}

public class HotkeySettings
{
    public string ToggleVisibility { get; set; } = "Ctrl+Shift+H";
}

public class SpotifySettings
{
    public string ClientId { get; set; } = string.Empty;
    public int PollingIntervalMs { get; set; } = 3000;
    public bool ShowProgressBar { get; set; } = true;
    public bool ShowAlbumArt { get; set; } = true;
}

public class AppSettings
{
    public string PlayerMode { get; set; } = "Local"; // "Local" or "WebApi"
    public SpotifySettings Spotify { get; set; } = new SpotifySettings();
    public OverlayAppearance Appearance { get; set; } = new OverlayAppearance();
    public WindowPosition Position { get; set; } = new WindowPosition();
    public HotkeySettings Hotkeys { get; set; } = new HotkeySettings();
    public bool StartMinimized { get; set; } = false;
    public bool RunAtStartup { get; set; } = true;
}
