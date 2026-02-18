using System.IO;
using System.Text.Json;
using SpotifyOnScreen.Models;

namespace SpotifyOnScreen.Services;

public class ConfigurationService
{
    private readonly string _appFolder;
    private readonly string _settingsPath;
    private readonly JsonSerializerOptions _jsonOptions;

    public ConfigurationService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        _appFolder = Path.Combine(appData, "SpotifyOnScreen");
        Directory.CreateDirectory(_appFolder);
        _settingsPath = Path.Combine(_appFolder, "settings.json");

        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };
    }

    public AppSettings LoadSettings()
    {
        try
        {
            if (!File.Exists(_settingsPath))
                return GetDefaultSettings();

            var json = File.ReadAllText(_settingsPath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, _jsonOptions);
            return settings ?? GetDefaultSettings();
        }
        catch
        {
            return GetDefaultSettings();
        }
    }

    public void SaveSettings(AppSettings settings)
    {
        try
        {
            var json = JsonSerializer.Serialize(settings, _jsonOptions);
            File.WriteAllText(_settingsPath, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to save settings: {ex.Message}");
        }
    }

    private static AppSettings GetDefaultSettings()
    {
        return new AppSettings
        {
            Spotify = new SpotifySettings(),
            Appearance = new OverlayAppearance(),
            Position = new WindowPosition { X = 10, Y = 10 },
            Hotkeys = new HotkeySettings(),
            StartMinimized = false
        };
    }

    public string GetSettingsPath() => _settingsPath;
    public string GetTokensPath() => Path.Combine(_appFolder, "tokens.json");
}
