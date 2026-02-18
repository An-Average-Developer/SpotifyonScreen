using System.IO;
using System.Security.Cryptography;
using System.Text;
using Windows.Media.Control;
using Windows.Storage.Streams;
using SpotifyOnScreen.Models;

namespace SpotifyOnScreen.Services;

public class LocalPlayerService : IPlayerService
{
    private System.Timers.Timer? _pollTimer;
    private bool _isConnected;
    private string _lastTrackKey = string.Empty;
    private string? _cachedAlbumArtPath;
    private int _consecutiveFailures;

    public event EventHandler<SpotifyTrackData>? TrackUpdated;
    public event EventHandler? PlaybackStopped;
#pragma warning disable CS0067
    public event EventHandler<string>? ErrorOccurred;
#pragma warning restore CS0067

    public bool IsConnected => _isConnected;

    public void Start(int pollingIntervalMs = 3000)
    {
        Stop();

        _pollTimer = new System.Timers.Timer(pollingIntervalMs);
        _pollTimer.Elapsed += async (_, _) => await PollAsync();
        _pollTimer.AutoReset = true;
        _pollTimer.Start();

        _ = PollAsync();
    }

    public void Stop()
    {
        _pollTimer?.Stop();
        _pollTimer?.Dispose();
        _pollTimer = null;
        _isConnected = false;
    }

    private async Task PollAsync()
    {
        try
        {
            var sessionManager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            var session = GetSpotifySession(sessionManager);

            if (session == null)
            {
                _consecutiveFailures++;
                // Only fire PlaybackStopped after 3 consecutive failures (~9 seconds)
                // to avoid false resets from GSMTC API flakiness
                if (_consecutiveFailures >= 3)
                {
                    _isConnected = false;
                    PlaybackStopped?.Invoke(this, EventArgs.Empty);
                }
                return;
            }

            var mediaProperties = await session.TryGetMediaPropertiesAsync();
            var playbackInfo = session.GetPlaybackInfo();
            var timeline = session.GetTimelineProperties();

            if (mediaProperties == null)
            {
                _consecutiveFailures++;
                if (_consecutiveFailures >= 3)
                    PlaybackStopped?.Invoke(this, EventArgs.Empty);
                return;
            }

            var trackName = mediaProperties.Title ?? "Unknown Track";
            var artistName = mediaProperties.Artist ?? "";
            var albumName = mediaProperties.AlbumTitle ?? "";
            var isPlaying = playbackInfo?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;

            var durationMs = (int)timeline.EndTime.TotalMilliseconds;
            var progressMs = (int)timeline.Position.TotalMilliseconds;

            var trackKey = $"{trackName}|{artistName}|{albumName}";
            string albumArtPath = "";

            if (trackKey != _lastTrackKey)
            {
                _lastTrackKey = trackKey;
                albumArtPath = await ExtractThumbnailAsync(mediaProperties.Thumbnail, trackKey);
                _cachedAlbumArtPath = albumArtPath;
            }
            else
            {
                albumArtPath = _cachedAlbumArtPath ?? "";
            }

            _isConnected = true;
            _consecutiveFailures = 0;

            var trackData = new SpotifyTrackData
            {
                TrackName = trackName,
                ArtistName = artistName,
                AlbumName = albumName,
                AlbumArtUrl = albumArtPath,
                DurationMs = durationMs,
                ProgressMs = progressMs,
                IsPlaying = isPlaying
            };

            TrackUpdated?.Invoke(this, trackData);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Local polling error: {ex}");
        }
    }

    private static GlobalSystemMediaTransportControlsSession? GetSpotifySession(
        GlobalSystemMediaTransportControlsSessionManager manager)
    {
        var sessions = manager.GetSessions();
        foreach (var session in sessions)
        {
            var sourceId = session.SourceAppUserModelId?.ToLowerInvariant() ?? "";
            if (sourceId.Contains("spotify"))
                return session;
        }

        // No Spotify session found — return null so other sources (YouTube, etc.) are ignored
        return null;
    }

    private static async Task<string> ExtractThumbnailAsync(IRandomAccessStreamReference? thumbnail, string trackKey)
    {
        if (thumbnail == null)
            return "";

        try
        {
            var hash = Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(trackKey)));
            var tempPath = Path.Combine(Path.GetTempPath(), $"SpotifyOnScreen_{hash}.png");

            using var stream = await thumbnail.OpenReadAsync();
            using var fileStream = File.Create(tempPath);
            var inputStream = stream.AsStreamForRead();
            await inputStream.CopyToAsync(fileStream);

            return tempPath;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to extract thumbnail: {ex.Message}");
            return "";
        }
    }

    public void Dispose()
    {
        Stop();
    }
}
