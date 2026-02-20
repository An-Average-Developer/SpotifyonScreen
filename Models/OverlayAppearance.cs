namespace SpotifyOnScreen.Models;

public class OverlayAppearance
{
    public string FontFamily { get; set; } = "Segoe UI";
    public int TrackFontSize { get; set; } = 18;
    public int ArtistFontSize { get; set; } = 14;
    public string TextColor { get; set; } = "#FFFFFFFF";
    public string SecondaryTextColor { get; set; } = "#FFB0B0B0";
    public string AccentColor { get; set; } = "#FF1DB954";
    public string BackgroundColor { get; set; } = "#FF1E1E2E";
    public double BackgroundOpacity { get; set; } = 0.9;
    public int CornerRadius { get; set; } = 12;
    public int Padding { get; set; } = 12;
    public int AlbumArtSize { get; set; } = 64;
    public bool DynamicBackground { get; set; } = false;
    public string ProgressBarColor { get; set; } = "#FF1DB954";
    public bool ProgressBarGlow { get; set; } = false;
    public bool ProgressBarDynamic { get; set; } = false;
}
