namespace SpotifyOnScreen.Models;

public class SpotifyAuthTokens
{
    public string AccessToken { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; } = DateTime.MinValue;
}
