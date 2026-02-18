using SpotifyOnScreen.Models;

namespace SpotifyOnScreen.Services;

public interface IPlayerService : IDisposable
{
    event EventHandler<SpotifyTrackData>? TrackUpdated;
    event EventHandler? PlaybackStopped;
    event EventHandler<string>? ErrorOccurred;
    bool IsConnected { get; }
    void Start(int pollingIntervalMs = 3000);
    void Stop();
}
