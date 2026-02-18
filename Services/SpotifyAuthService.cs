using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SpotifyOnScreen.Models;

namespace SpotifyOnScreen.Services;

public class SpotifyAuthService : IDisposable
{
    private const string AuthorizeUrl = "https://accounts.spotify.com/authorize";
    private const string TokenUrl = "https://accounts.spotify.com/api/token";
    private const string RedirectUri = "http://127.0.0.1:4202";
    private const string ListenerPrefix = "http://127.0.0.1:4202/";
    private const string Scopes = "user-read-currently-playing user-read-playback-state";

    private readonly HttpClient _httpClient = new();
    private readonly string _tokensPath;
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    private readonly SemaphoreSlim _tokenLock = new(1, 1);

    private SpotifyAuthTokens _tokens = new();
    private string _clientId = string.Empty;

    public bool IsAuthenticated => !string.IsNullOrEmpty(_tokens.RefreshToken);
    public string AccessToken => _tokens.AccessToken;

    public SpotifyAuthService(string tokensPath)
    {
        _tokensPath = tokensPath;
    }

    public void LoadTokens()
    {
        try
        {
            if (File.Exists(_tokensPath))
            {
                var json = File.ReadAllText(_tokensPath);
                _tokens = JsonSerializer.Deserialize<SpotifyAuthTokens>(json, _jsonOptions) ?? new SpotifyAuthTokens();
            }
        }
        catch
        {
            _tokens = new SpotifyAuthTokens();
        }
    }

    public void ClearTokens()
    {
        _tokens = new SpotifyAuthTokens();
        SaveTokens();
    }

    public async Task<bool> AuthenticateAsync(string clientId)
    {
        _clientId = clientId;

        var codeVerifier = GenerateCodeVerifier();
        var codeChallenge = GenerateCodeChallenge(codeVerifier);

        var authUrl = $"{AuthorizeUrl}?client_id={Uri.EscapeDataString(clientId)}" +
                      $"&response_type=code" +
                      $"&redirect_uri={Uri.EscapeDataString(RedirectUri)}" +
                      $"&scope={Uri.EscapeDataString(Scopes)}" +
                      $"&code_challenge_method=S256" +
                      $"&code_challenge={codeChallenge}";

        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));

        try
        {
            var code = await ListenForCallbackAsync(authUrl, cts.Token);
            if (string.IsNullOrEmpty(code))
                return false;

            return await ExchangeCodeAsync(code, codeVerifier, clientId);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    public async Task<bool> EnsureValidTokenAsync()
    {
        await _tokenLock.WaitAsync();
        try
        {
            if (_tokens.ExpiresAt > DateTime.UtcNow.AddMinutes(1))
                return true;

            return await RefreshTokenInternalAsync();
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    public async Task<bool> RefreshTokenAsync()
    {
        await _tokenLock.WaitAsync();
        try
        {
            return await RefreshTokenInternalAsync();
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private async Task<bool> RefreshTokenInternalAsync()
    {
        if (string.IsNullOrEmpty(_tokens.RefreshToken) || string.IsNullOrEmpty(_clientId))
            return false;

        try
        {
            var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = _tokens.RefreshToken,
                ["client_id"] = _clientId
            });

            var response = await _httpClient.PostAsync(TokenUrl, content);
            if (!response.IsSuccessStatusCode)
                return false;

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            _tokens.AccessToken = root.GetProperty("access_token").GetString() ?? string.Empty;

            if (root.TryGetProperty("refresh_token", out var newRefresh))
                _tokens.RefreshToken = newRefresh.GetString() ?? _tokens.RefreshToken;

            var expiresIn = root.GetProperty("expires_in").GetInt32();
            _tokens.ExpiresAt = DateTime.UtcNow.AddSeconds(expiresIn);

            SaveTokens();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void SetClientId(string clientId)
    {
        _clientId = clientId;
    }

    private async Task<string?> ListenForCallbackAsync(string authUrl, CancellationToken ct)
    {
        using var listener = new HttpListener();
        listener.Prefixes.Add(ListenerPrefix);
        listener.Start();

        Process.Start(new ProcessStartInfo
        {
            FileName = authUrl,
            UseShellExecute = true
        });

        try
        {
            var contextTask = listener.GetContextAsync();
            var completedTask = await Task.WhenAny(contextTask, Task.Delay(Timeout.Infinite, ct));

            if (completedTask != contextTask)
                return null;

            var context = await contextTask;
            var code = context.Request.QueryString["code"];
            var error = context.Request.QueryString["error"];

            string responseHtml;
            if (!string.IsNullOrEmpty(error))
            {
                responseHtml = "<html><body style='font-family:Segoe UI;background:#1E1E2E;color:white;display:flex;justify-content:center;align-items:center;height:100vh;margin:0'>" +
                               $"<div style='text-align:center'><h1 style='color:#F87171'>Authentication Failed</h1><p>{error}</p></div></div></body></html>";
            }
            else
            {
                responseHtml = "<html><body style='font-family:Segoe UI;background:#1E1E2E;color:white;display:flex;justify-content:center;align-items:center;height:100vh;margin:0'>" +
                               "<div style='text-align:center'><h1 style='color:#1DB954'>Connected to Spotify!</h1><p>You can close this tab.</p></div></body></html>";
            }

            var buffer = Encoding.UTF8.GetBytes(responseHtml);
            context.Response.ContentType = "text/html";
            context.Response.ContentLength64 = buffer.Length;
            await context.Response.OutputStream.WriteAsync(buffer, ct);
            context.Response.Close();

            return code;
        }
        finally
        {
            listener.Stop();
        }
    }

    private async Task<bool> ExchangeCodeAsync(string code, string codeVerifier, string clientId)
    {
        try
        {
            var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["redirect_uri"] = RedirectUri,
                ["client_id"] = clientId,
                ["code_verifier"] = codeVerifier
            });

            var response = await _httpClient.PostAsync(TokenUrl, content);
            if (!response.IsSuccessStatusCode)
                return false;

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            _tokens.AccessToken = root.GetProperty("access_token").GetString() ?? string.Empty;
            _tokens.RefreshToken = root.GetProperty("refresh_token").GetString() ?? string.Empty;

            var expiresIn = root.GetProperty("expires_in").GetInt32();
            _tokens.ExpiresAt = DateTime.UtcNow.AddSeconds(expiresIn);

            SaveTokens();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void SaveTokens()
    {
        try
        {
            var dir = Path.GetDirectoryName(_tokensPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(_tokens, _jsonOptions);
            File.WriteAllText(_tokensPath, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to save tokens: {ex.Message}");
        }
    }

    private static string GenerateCodeVerifier()
    {
        var bytes = new byte[96];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes)
            .Replace("+", "-")
            .Replace("/", "_")
            .Replace("=", "");
    }

    private static string GenerateCodeChallenge(string codeVerifier)
    {
        var bytes = SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier));
        return Convert.ToBase64String(bytes)
            .Replace("+", "-")
            .Replace("/", "_")
            .Replace("=", "");
    }

    public void Dispose()
    {
        _httpClient.Dispose();
        _tokenLock.Dispose();
    }
}
