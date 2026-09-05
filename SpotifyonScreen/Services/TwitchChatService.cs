using System.Net.WebSockets;
using System.Text;
using System.Text.RegularExpressions;

namespace SpotifyOnScreen.Services;

public class TwitchChatMessage
{
    public string Username { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}

// Connects to Twitch chat anonymously (read-only) over the IRC-over-WebSocket
// endpoint. No Twitch account, app registration, or OAuth token is needed —
// Twitch allows anonymous "justinfanNNNNN" logins for reading public chat.
public class TwitchChatService : IDisposable
{
    private const string WebSocketUrl = "wss://irc-ws.chat.twitch.tv:443";

    private static readonly Regex ChannelLinkRegex = new(@"twitch\.tv/([A-Za-z0-9_]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex PrivMsgRegex = new(@"^:(?<nick>[^!\s]+)!\S+\s+PRIVMSG\s+#\S+\s+:(?<message>.*)$", RegexOptions.Compiled);

    private ClientWebSocket? _webSocket;
    private CancellationTokenSource? _cts;
    private Task? _runTask;
    private string _channel = string.Empty;
    private bool _disposed;

    public bool IsConnected { get; private set; }

    public event EventHandler<TwitchChatMessage>? MessageReceived;
    public event EventHandler<string>? StatusChanged;

    public void Start(string channelOrUrl)
    {
        Stop();

        _channel = ParseChannelName(channelOrUrl);
        if (string.IsNullOrWhiteSpace(_channel))
        {
            StatusChanged?.Invoke(this, "Invalid Twitch channel name or link.");
            return;
        }

        _cts = new CancellationTokenSource();
        _runTask = Task.Run(() => RunAsync(_channel, _cts.Token));
    }

    public void Stop()
    {
        try
        {
            _cts?.Cancel();
            _webSocket?.Abort();
        }
        catch
        {
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
            _webSocket?.Dispose();
            _webSocket = null;
            IsConnected = false;
        }
    }

    public static string ParseChannelName(string input)
    {
        input = input.Trim();
        if (string.IsNullOrEmpty(input))
            return string.Empty;

        var linkMatch = ChannelLinkRegex.Match(input);
        var name = linkMatch.Success
            ? linkMatch.Groups[1].Value
            : input.TrimStart('#', '/').Split('/')[0].Split('?')[0];

        return name.Trim().ToLowerInvariant();
    }

    private async Task RunAsync(string channel, CancellationToken ct)
    {
        var attempt = 0;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                _webSocket = new ClientWebSocket();
                await _webSocket.ConnectAsync(new Uri(WebSocketUrl), ct);

                var nick = $"justinfan{Random.Shared.Next(10000, 99999)}";
                await SendAsync($"NICK {nick}", ct);
                await SendAsync($"JOIN #{channel}", ct);

                IsConnected = true;
                attempt = 0;
                StatusChanged?.Invoke(this, $"Connected to #{channel}");

                await ReceiveLoopAsync(ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                IsConnected = false;
                StatusChanged?.Invoke(this, $"Disconnected: {ex.Message}");
            }

            IsConnected = false;
            if (ct.IsCancellationRequested)
                break;

            attempt++;
            var delaySeconds = Math.Min(30, 5 * attempt);

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(delaySeconds), ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        IsConnected = false;
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        var buffer = new byte[8192];
        var messageBuilder = new StringBuilder();

        while (_webSocket!.State == WebSocketState.Open && !ct.IsCancellationRequested)
        {
            var result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), ct);

            if (result.MessageType == WebSocketMessageType.Close)
                break;

            messageBuilder.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));

            if (!result.EndOfMessage)
                continue;

            var text = messageBuilder.ToString();
            messageBuilder.Clear();

            foreach (var line in text.Split("\r\n", StringSplitOptions.RemoveEmptyEntries))
                await ProcessLineAsync(line, ct);
        }
    }

    private async Task ProcessLineAsync(string line, CancellationToken ct)
    {
        if (line.StartsWith("PING", StringComparison.Ordinal))
        {
            await SendAsync(line.Replace("PING", "PONG"), ct);
            return;
        }

        if (!line.Contains("PRIVMSG", StringComparison.Ordinal))
            return;

        var match = PrivMsgRegex.Match(line);
        if (!match.Success)
            return;

        MessageReceived?.Invoke(this, new TwitchChatMessage
        {
            Username = match.Groups["nick"].Value,
            Message = match.Groups["message"].Value.TrimEnd('\r', '\n')
        });
    }

    private async Task SendAsync(string message, CancellationToken ct)
    {
        if (_webSocket == null || _webSocket.State != WebSocketState.Open)
            return;

        var bytes = Encoding.UTF8.GetBytes(message + "\r\n");
        await _webSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
    }
}
