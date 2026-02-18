using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using SpotifyOnScreen.Models;

namespace SpotifyOnScreen.Services;

public class SpotifyPlayerService : IPlayerService
{
    private const string CurrentlyPlayingUrl = "https://api.spotify.com/v1/me/player/currently-playing";

    private readonly SpotifyAuthService _authService;
    private readonly HttpClient _httpClient = new();
    private System.Timers.Timer? _pollTimer;
    private bool _isConnected;
    private string _lastAlbumArtUrl = string.Empty;

    public event EventHandler<SpotifyTrackData>? TrackUpdated;
    public event EventHandler? PlaybackStopped;
    public event EventHandler<string>? ErrorOccurred;

    public bool IsConnected => _isConnected;

    public SpotifyPlayerService(SpotifyAuthService authService)
    {
        _authService = authService;
    }

    public void Start(int pollingIntervalMs = 3000)
    {
        Stop();

        _pollTimer = new System.Timers.Timer(pollingIntervalMs);
        _pollTimer.Elapsed += async (_, _) => await PollCurrentlyPlayingAsync();
        _pollTimer.AutoReset = true;
        _pollTimer.Start();

        _ = PollCurrentlyPlayingAsync();
    }

    public void Stop()
    {
        _pollTimer?.Stop();
        _pollTimer?.Dispose();
        _pollTimer = null;
        _isConnected = false;
    }

    private async Task PollCurrentlyPlayingAsync()
    {
        try
        {
            if (!await _authService.EnsureValidTokenAsync())
            {
                _isConnected = false;
                ErrorOccurred?.Invoke(this, "Failed to refresh Spotify token. Please reconnect.");
                return;
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, CurrentlyPlayingUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _authService.AccessToken);

            var response = await _httpClient.SendAsync(request);

            if (response.StatusCode == System.Net.HttpStatusCode.NoContent)
            {
                _isConnected = true;
                PlaybackStopped?.Invoke(this, EventArgs.Empty);
                return;
            }

            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                var refreshed = await _authService.RefreshTokenAsync();
                if (refreshed)
                {
                    using var retryRequest = new HttpRequestMessage(HttpMethod.Get, CurrentlyPlayingUrl);
                    retryRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _authService.AccessToken);
                    response = await _httpClient.SendAsync(retryRequest);
                }
                else
                {
                    _isConnected = false;
                    ErrorOccurred?.Invoke(this, "Authentication expired. Please reconnect in Settings.");
                    return;
                }
            }

            if (!response.IsSuccessStatusCode)
            {
                ErrorOccurred?.Invoke(this, $"Spotify API error: {(int)response.StatusCode}");
                return;
            }

            var json = await response.Content.ReadAsStringAsync();
            var trackData = ParseTrackData(json);

            if (trackData != null)
            {
                _isConnected = true;
                TrackUpdated?.Invoke(this, trackData);
            }
        }
        catch (HttpRequestException ex)
        {
            ErrorOccurred?.Invoke(this, $"Network error: {ex.Message}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Polling error: {ex}");
        }
    }

    private static SpotifyTrackData? ParseTrackData(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("item", out var item))
                return null;

            var trackName = item.GetProperty("name").GetString() ?? "Unknown Track";

            var artists = new List<string>();
            if (item.TryGetProperty("artists", out var artistsArray))
            {
                foreach (var artist in artistsArray.EnumerateArray())
                {
                    var name = artist.GetProperty("name").GetString();
                    if (!string.IsNullOrEmpty(name))
                        artists.Add(name);
                }
            }

            var albumName = "";
            var albumArtUrl = "";
            if (item.TryGetProperty("album", out var album))
            {
                albumName = album.GetProperty("name").GetString() ?? "";

                if (album.TryGetProperty("images", out var images))
                {
                    foreach (var image in images.EnumerateArray())
                    {
                        albumArtUrl = image.GetProperty("url").GetString() ?? "";
                        break; // First image is the largest
                    }
                }
            }

            var durationMs = item.TryGetProperty("duration_ms", out var dur) ? dur.GetInt32() : 0;
            var progressMs = root.TryGetProperty("progress_ms", out var prog) ? prog.GetInt32() : 0;
            var isPlaying = root.TryGetProperty("is_playing", out var playing) && playing.GetBoolean();

            return new SpotifyTrackData
            {
                TrackName = trackName,
                ArtistName = string.Join(", ", artists),
                AlbumName = albumName,
                AlbumArtUrl = albumArtUrl,
                DurationMs = durationMs,
                ProgressMs = progressMs,
                IsPlaying = isPlaying
            };
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to parse track data: {ex.Message}");
            return null;
        }
    }

    public void Dispose()
    {
        Stop();
        _httpClient.Dispose();
    }
}
