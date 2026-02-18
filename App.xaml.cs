using System.Windows;
using SpotifyOnScreen.Managers;
using SpotifyOnScreen.Models;
using SpotifyOnScreen.Services;
using SpotifyOnScreen.ViewModels;
using SpotifyOnScreen.Windows;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;
using System.Threading.Tasks;

namespace SpotifyOnScreen;

public partial class App : Application
{
    private ConfigurationService? _configService;
    private SpotifyAuthService? _authService;
    private IPlayerService? _playerService;
    private UpdateService? _updateService;
    private GlobalHotkeyManager? _hotkeyManager;
    private TrayIconManager? _trayManager;
    private OverlayWindow? _overlayWindow;
    private OverlayViewModel? _overlayViewModel;

    private void Application_Startup(object sender, StartupEventArgs e)
    {
        try
        {
            _configService = new ConfigurationService();
            _updateService = new UpdateService();
            var settings = _configService.LoadSettings();

            _authService = new SpotifyAuthService(_configService.GetTokensPath());
            _authService.LoadTokens();

            if (!string.IsNullOrEmpty(settings.Spotify.ClientId))
                _authService.SetClientId(settings.Spotify.ClientId);

            if (settings.PlayerMode == "WebApi")
                _playerService = new SpotifyPlayerService(_authService);
            else
                _playerService = new LocalPlayerService();

            _overlayViewModel = new OverlayViewModel(_configService, _playerService);
            _overlayWindow = new OverlayWindow(_overlayViewModel);

            _hotkeyManager = new GlobalHotkeyManager(
                onToggleVisibility: ToggleOverlayVisibility
            );

            _trayManager = new TrayIconManager(
                onOpenSettings: OpenSettings,
                onToggleOverlay: ToggleOverlayVisibility,
                onExit: ExitApplication,
                onCheckForUpdates: CheckForUpdatesFromTray
            );

            _trayManager.Initialize();
            _hotkeyManager.RegisterHotkeys(settings.Hotkeys);
            _ = CheckForUpdatesOnStartupAsync();

            if (!settings.StartMinimized)
                _overlayWindow.Show();

            if (settings.PlayerMode == "Local")
            {
                _playerService.Start(settings.Spotify.PollingIntervalMs);
            }
            else
            {
                if (_authService.IsAuthenticated)
                    _playerService.Start(settings.Spotify.PollingIntervalMs);

                if (string.IsNullOrWhiteSpace(settings.Spotify.ClientId) || !_authService.IsAuthenticated)
                {
                    OpenSettings();
                    _trayManager.ShowNotification("Spotify On Screen",
                        "Please configure your Spotify Client ID in Settings.");
                }
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to start application: {ex.Message}", "Startup Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
        }
    }

    private void Application_Exit(object sender, ExitEventArgs e)
    {
        try
        {
            _playerService?.Stop();
            _playerService?.Dispose();
            _authService?.Dispose();
            _updateService?.Dispose();
            _hotkeyManager?.UnregisterHotkeys();
            _trayManager?.Dispose();
        }
        catch
        {
            // Ignore cleanup errors
        }
    }

    private void ToggleOverlayVisibility()
    {
        Dispatcher.Invoke(() =>
        {
            if (_overlayWindow != null)
            {
                if (_overlayWindow.Visibility == Visibility.Visible)
                    _overlayWindow.Hide();
                else
                    _overlayWindow.Show();
            }
        });
    }

    private void OpenSettings()
    {
        Dispatcher.Invoke(() =>
        {
            try
            {
                if (_configService == null || _authService == null || _playerService == null) return;

                var settingsViewModel = new SettingsViewModel(
                    _configService,
                    _authService,
                    _playerService,
                    _updateService!,
                    OnSettingsSaved,
                    DisableHotkeys,
                    EnableHotkeys);
                var settingsWindow = new SettingsWindow(settingsViewModel);
                settingsWindow.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to open settings: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        });
    }

    private void DisableHotkeys()
    {
        _hotkeyManager?.UnregisterHotkeys();
    }

    private void EnableHotkeys()
    {
        if (_configService == null) return;
        var settings = _configService.LoadSettings();
        _hotkeyManager?.RegisterHotkeys(settings.Hotkeys);
    }

    private void OnSettingsSaved()
    {
        try
        {
            if (_configService == null) return;

            var settings = _configService.LoadSettings();
            _overlayViewModel?.LoadSettings();
            _hotkeyManager?.RegisterHotkeys(settings.Hotkeys);

            if (_authService?.IsAuthenticated == true)
            {
                _playerService?.Stop();
                _playerService?.Start(settings.Spotify.PollingIntervalMs);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to apply settings: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ExitApplication()
    {
        Shutdown();
    }

    private async Task CheckForUpdatesOnStartupAsync()
    {
        try
        {
            var updateInfo = await _updateService!.CheckForUpdatesAsync();
            if (updateInfo.IsUpdateAvailable)
            {
                _trayManager?.ShowNotification(
                    "Spotify On Screen",
                    $"Update {updateInfo.LatestVersion} is available! Open Settings > Updates to download.");
            }
        }
        catch
        {
            // Silently ignore startup update check failures
        }
    }

    private void CheckForUpdatesFromTray()
    {
        _ = CheckForUpdatesFromTrayAsync();
    }

    private async Task CheckForUpdatesFromTrayAsync()
    {
        try
        {
            var updateInfo = await _updateService!.CheckForUpdatesAsync();
            Dispatcher.Invoke(() =>
            {
                if (updateInfo.IsUpdateAvailable)
                {
                    _trayManager?.ShowNotification(
                        "Spotify On Screen",
                        $"Update {updateInfo.LatestVersion} available! Open Settings > Updates to download.");
                }
                else
                {
                    _trayManager?.ShowNotification(
                        "Spotify On Screen",
                        "You have the latest version.");
                }
            });
        }
        catch
        {
            _trayManager?.ShowNotification("Spotify On Screen", "Failed to check for updates.");
        }
    }
}
