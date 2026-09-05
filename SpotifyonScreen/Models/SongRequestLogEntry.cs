namespace SpotifyOnScreen.Models;

public class SongRequestLogEntry
{
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public string Username { get; set; } = string.Empty;
    public string Query { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;

    public string DisplayText => $"[{Timestamp:HH:mm:ss}] {Username}: \"{Query}\" → {Message}";
}
