namespace SpotifyOnScreen.Models;

public class SpotifyTrackData
{
    public string TrackName { get; set; } = string.Empty;
    public string ArtistName { get; set; } = string.Empty;
    public string AlbumName { get; set; } = string.Empty;
    public string AlbumArtUrl { get; set; } = string.Empty;
    public int DurationMs { get; set; }
    public int ProgressMs { get; set; }
    public bool IsPlaying { get; set; }
}
