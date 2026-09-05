using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using SpotifyOnScreen.Models;

namespace SpotifyOnScreen.Services;

public class SongRequestResult
{
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public string Username { get; set; } = string.Empty;
    public string Query { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
}

// Bridges Twitch chat and Spotify: watches chat (via TwitchChatService) for a
// song-request command and, when seen, resolves the query to a track and adds
// it to the user's Spotify queue via the Web API. Requests can't be echoed back
// into chat because the Twitch connection is anonymous/read-only (no OAuth
// token), so feedback is only surfaced inside the app (Settings > Twitch).
public class SongRequestService : IDisposable
{
    private static readonly Regex SpotifyLinkRegex = new(
        @"(?:spotify:track:|open\.spotify\.com/track/)([A-Za-z0-9]{22})",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly TwitchChatService _chatService;
    private readonly SpotifyAuthService _authService;
    private readonly HttpClient _httpClient = new();
    private readonly Dictionary<string, DateTime> _lastRequestByUser = new(StringComparer.OrdinalIgnoreCase);

    private string _commandName = "!sr";
    private int _cooldownSeconds = 30;
    private bool _disposed;

    public bool IsConnected => _chatService.IsConnected;

    public event EventHandler<SongRequestResult>? RequestProcessed;
    public event EventHandler<string>? StatusChanged;

    public SongRequestService(SpotifyAuthService authService)
    {
        _authService = authService;
        _chatService = new TwitchChatService();
        _chatService.MessageReceived += OnChatMessageReceived;
        _chatService.StatusChanged += (_, status) => StatusChanged?.Invoke(this, status);
    }

    public void Start(TwitchSettings settings)
    {
        _commandName = string.IsNullOrWhiteSpace(settings.CommandName) ? "!sr" : settings.CommandName.Trim();
        _cooldownSeconds = Math.Max(0, settings.CooldownSeconds);
        _lastRequestByUser.Clear();
        _chatService.Start(settings.Channel);
    }

    public void Stop() => _chatService.Stop();

    private async void OnChatMessageReceived(object? sender, TwitchChatMessage chatMessage)
    {
        var message = chatMessage.Message.Trim();
        if (!IsCommandMatch(message))
            return;

        var query = message[_commandName.Length..].Trim();
        if (string.IsNullOrWhiteSpace(query))
            return;

        if (_cooldownSeconds > 0 &&
            _lastRequestByUser.TryGetValue(chatMessage.Username, out var last) &&
            (DateTime.UtcNow - last).TotalSeconds < _cooldownSeconds)
        {
            return;
        }

        _lastRequestByUser[chatMessage.Username] = DateTime.UtcNow;

        try
        {
            var result = await ProcessRequestAsync(chatMessage.Username, query);
            RequestProcessed?.Invoke(this, result);
        }
        catch (Exception ex)
        {
            RequestProcessed?.Invoke(this, new SongRequestResult
            {
                Username = chatMessage.Username,
                Query = query,
                Success = false,
                Message = $"Error: {ex.Message}"
            });
        }
    }

    private bool IsCommandMatch(string message)
    {
        if (!message.StartsWith(_commandName, StringComparison.OrdinalIgnoreCase))
            return false;

        return message.Length == _commandName.Length || char.IsWhiteSpace(message[_commandName.Length]);
    }

    private async Task<SongRequestResult> ProcessRequestAsync(string username, string query)
    {
        var result = new SongRequestResult { Username = username, Query = query };

        if (!_authService.IsAuthenticated || !await _authService.EnsureValidTokenAsync())
        {
            result.Message = "Spotify isn't connected — open Settings > Twitch to connect your account.";
            return result;
        }

        var resolved = await ResolveTrackAsync(query);
        if (resolved == null)
        {
            result.Message = "No matching track found on Spotify.";
            return result;
        }

        var (success, error) = await QueueTrackAsync(resolved.Value.Uri);
        result.Success = success;
        result.Message = success ? $"Added: {resolved.Value.Label}" : error;
        return result;
    }

    private async Task<(string Uri, string Label)?> ResolveTrackAsync(string query)
    {
        var linkMatch = SpotifyLinkRegex.Match(query);
        if (linkMatch.Success)
        {
            var id = linkMatch.Groups[1].Value;
            var label = await FetchTrackLabelAsync($"https://api.spotify.com/v1/tracks/{id}") ?? id;
            return ($"spotify:track:{id}", label);
        }

        var searchUrl = $"https://api.spotify.com/v1/search?type=track&limit=1&q={Uri.EscapeDataString(query)}";

        using var request = new HttpRequestMessage(HttpMethod.Get, searchUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _authService.AccessToken);

        var response = await _httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode)
            return null;

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);

        if (!doc.RootElement.TryGetProperty("tracks", out var tracks) ||
            !tracks.TryGetProperty("items", out var items) ||
            items.GetArrayLength() == 0)
            return null;

        var track = items[0];
        var trackId = track.GetProperty("id").GetString();
        if (string.IsNullOrEmpty(trackId))
            return null;

        return ($"spotify:track:{trackId}", FormatTrackLabel(track));
    }

    private async Task<string?> FetchTrackLabelAsync(string url)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _authService.AccessToken);

            var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
                return null;

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            return FormatTrackLabel(doc.RootElement);
        }
        catch
        {
            return null;
        }
    }

    private static string FormatTrackLabel(JsonElement track)
    {
        var name = track.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? "Unknown" : "Unknown";

        var artists = new List<string>();
        if (track.TryGetProperty("artists", out var artistsArray))
        {
            foreach (var artist in artistsArray.EnumerateArray())
            {
                var artistName = artist.TryGetProperty("name", out var artistNameProp) ? artistNameProp.GetString() : null;
                if (!string.IsNullOrEmpty(artistName))
                    artists.Add(artistName);
            }
        }

        return artists.Count > 0 ? $"{name} - {string.Join(", ", artists)}" : name;
    }

    private async Task<(bool Success, string Error)> QueueTrackAsync(string uri)
    {
        var queueUrl = $"https://api.spotify.com/v1/me/player/queue?uri={Uri.EscapeDataString(uri)}";

        using var request = new HttpRequestMessage(HttpMethod.Post, queueUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _authService.AccessToken);

        var response = await _httpClient.SendAsync(request);

        if (response.StatusCode == HttpStatusCode.Unauthorized && await _authService.RefreshTokenAsync())
        {
            using var retry = new HttpRequestMessage(HttpMethod.Post, queueUrl);
            retry.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _authService.AccessToken);
            response = await _httpClient.SendAsync(retry);
        }

        if (response.IsSuccessStatusCode)
            return (true, string.Empty);

        return response.StatusCode switch
        {
            HttpStatusCode.NotFound => (false, "No active Spotify device — start playing something first."),
            HttpStatusCode.Forbidden => (false, "Spotify Premium is required, or reconnect Spotify to grant queue permission."),
            HttpStatusCode.Unauthorized => (false, "Spotify authentication expired — reconnect in Settings."),
            _ => (false, $"Spotify error ({(int)response.StatusCode}).")
        };
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _chatService.MessageReceived -= OnChatMessageReceived;
        _chatService.Dispose();
        _httpClient.Dispose();
    }
}
