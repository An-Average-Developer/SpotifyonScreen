namespace SpotifyOnScreen.Models;

public class TwitchSettings
{
    public bool Enabled { get; set; } = false;
    public string Channel { get; set; } = string.Empty;
    public string CommandName { get; set; } = "!sr";
    public int CooldownSeconds { get; set; } = 30;
}
