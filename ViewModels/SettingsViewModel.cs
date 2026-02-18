using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using SpotifyOnScreen.Configuration;
using Microsoft.Win32;
using System.Diagnostics;
using Application = System.Windows.Application;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;
using SpotifyOnScreen.Models;
using SpotifyOnScreen.Services;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MessageBox = System.Windows.MessageBox;

namespace SpotifyOnScreen.ViewModels;

public class SettingsViewModel : INotifyPropertyChanged
{
    private readonly ConfigurationService _configService;
    private readonly SpotifyAuthService _authService;
    private readonly IPlayerService _playerService;
    private readonly UpdateService _updateService;
    private readonly Action _onSettingsSaved;
    private readonly Action? _onDisableHotkeys;
    private readonly Action? _onEnableHotkeys;
    private AppSettings _settings;
    private bool _isAuthenticated;
    private string _authenticationStatus = "Not connected";
    private bool _isConnecting;

    // Update-related fields
    private string _currentVersion = AppVersion.GetDisplayVersion();
    private string _latestVersion = string.Empty;
    private bool _isUpdateAvailable;
    private bool _hasCheckedForUpdates;
    private bool _isDownloading;
    private int _downloadProgress;
    private string _updateStatusText = string.Empty;
    private Brush _updateStatusColor = new SolidColorBrush(Color.FromRgb(42, 42, 58));
    private bool _showingChangelogView;
    private string _changelog = string.Empty;

    public event PropertyChangedEventHandler? PropertyChanged;

    public AppSettings Settings
    {
        get => _settings;
        set { _settings = value; OnPropertyChanged(); }
    }

    public bool IsAuthenticated
    {
        get => _isAuthenticated;
        set { _isAuthenticated = value; OnPropertyChanged(); }
    }

    public string AuthenticationStatus
    {
        get => _authenticationStatus;
        set { _authenticationStatus = value; OnPropertyChanged(); }
    }

    public bool IsConnecting
    {
        get => _isConnecting;
        set { _isConnecting = value; OnPropertyChanged(); }
    }

    public string SettingsPath => _configService.GetSettingsPath();

    public ICommand ConnectSpotifyCommand { get; }
    public ICommand DisconnectSpotifyCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand ReregisterHotkeysCommand { get; }
    public ICommand ApplyDefaultSizeCommand { get; }
    public ICommand ApplySmallSizeCommand { get; }
    public ICommand ApplyCompactSizeCommand { get; }

    public ICommand ApplyTinyArtCommand { get; }
    public ICommand ApplySmallArtCommand { get; }
    public ICommand ApplyDefaultArtCommand { get; }
    public ICommand ApplyLargeArtCommand { get; }
    public ICommand ApplyXLArtCommand { get; }
    public ICommand ApplyXXLArtCommand { get; }

    // Update commands
    public ICommand CheckForUpdatesCommand { get; }
    public ICommand DownloadUpdateCommand { get; }
    public ICommand AcceptInstallationCommand { get; }
    public ICommand BackFromChangelogCommand { get; }
    public ICommand OpenGitHubCommand { get; }

    public IReadOnlyList<string> FontFamilies { get; } =
    [
        "Segoe UI",
        "Segoe UI Variable",
        "Calibri",
        "Arial",
        "Verdana",
        "Tahoma",
        "Trebuchet MS",
        "Georgia",
        "Times New Roman",
        "Bahnschrift",
        "Consolas",
        "Cascadia Code",
        "Courier New",
        "Lucida Console",
        "Comic Sans MS",
    ];

    // Update properties
    public string CurrentVersion
    {
        get => _currentVersion;
        set { _currentVersion = value; OnPropertyChanged(); }
    }

    public string LatestVersion
    {
        get => _latestVersion;
        set { _latestVersion = value; OnPropertyChanged(); }
    }

    public bool IsUpdateAvailable
    {
        get => _isUpdateAvailable;
        set { _isUpdateAvailable = value; OnPropertyChanged(); }
    }

    public bool HasCheckedForUpdates
    {
        get => _hasCheckedForUpdates;
        set { _hasCheckedForUpdates = value; OnPropertyChanged(); }
    }

    public bool IsDownloading
    {
        get => _isDownloading;
        set { _isDownloading = value; OnPropertyChanged(); }
    }

    public int DownloadProgress
    {
        get => _downloadProgress;
        set { _downloadProgress = value; OnPropertyChanged(); }
    }

    public string UpdateStatusText
    {
        get => _updateStatusText;
        set { _updateStatusText = value; OnPropertyChanged(); }
    }

    public Brush UpdateStatusColor
    {
        get => _updateStatusColor;
        set { _updateStatusColor = value; OnPropertyChanged(); }
    }

    public bool ShowingChangelogView
    {
        get => _showingChangelogView;
        set { _showingChangelogView = value; OnPropertyChanged(); }
    }

    public string Changelog
    {
        get => _changelog;
        set { _changelog = value; OnPropertyChanged(); }
    }

    public bool IsLocalMode
    {
        get => _settings.PlayerMode == "Local";
        set
        {
            _settings.PlayerMode = value ? "Local" : "WebApi";
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsWebApiMode));
        }
    }

    public bool IsWebApiMode
    {
        get => _settings.PlayerMode == "WebApi";
        set
        {
            _settings.PlayerMode = value ? "WebApi" : "Local";
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsLocalMode));
        }
    }

    public SettingsViewModel(
        ConfigurationService configService,
        SpotifyAuthService authService,
        IPlayerService playerService,
        UpdateService updateService,
        Action onSettingsSaved,
        Action? onDisableHotkeys = null,
        Action? onEnableHotkeys = null)
    {
        _configService = configService;
        _authService = authService;
        _playerService = playerService;
        _updateService = updateService;
        _onSettingsSaved = onSettingsSaved;
        _onDisableHotkeys = onDisableHotkeys;
        _onEnableHotkeys = onEnableHotkeys;
        _settings = configService.LoadSettings();
        _settings.RunAtStartup = IsRegistryStartupEnabled();

        IsAuthenticated = authService.IsAuthenticated;
        AuthenticationStatus = IsAuthenticated ? "Connected to Spotify" : "Not connected";

        ConnectSpotifyCommand = new RelayCommand(async () => await ConnectSpotifyAsync());
        DisconnectSpotifyCommand = new RelayCommand(DisconnectSpotify);
        SaveCommand = new RelayCommand(Save);
        ReregisterHotkeysCommand = new RelayCommand(ReregisterHotkeys);
        ApplyDefaultSizeCommand = new RelayCommand(ApplyDefaultSize);
        ApplySmallSizeCommand = new RelayCommand(() => ApplySize(260, 0));
        ApplyCompactSizeCommand = new RelayCommand(ApplyCompact);

        ApplyTinyArtCommand = new RelayCommand(() => ApplyArtSize(32));
        ApplySmallArtCommand = new RelayCommand(() => ApplyArtSize(48));
        ApplyDefaultArtCommand = new RelayCommand(() => ApplyArtSize(64));
        ApplyLargeArtCommand = new RelayCommand(() => ApplyArtSize(80));
        ApplyXLArtCommand = new RelayCommand(() => ApplyArtSize(100));
        ApplyXXLArtCommand = new RelayCommand(() => ApplyArtSize(128));

        CheckForUpdatesCommand = new RelayCommand(async () => await CheckForUpdatesAsync());
        DownloadUpdateCommand = new RelayCommand(ShowChangelogView, () => IsUpdateAvailable && !IsDownloading);
        AcceptInstallationCommand = new RelayCommand(async () => await AcceptInstallationAsync(), () => !IsDownloading);
        BackFromChangelogCommand = new RelayCommand(() => ShowingChangelogView = false, () => !IsDownloading);
        OpenGitHubCommand = new RelayCommand(() => _updateService.OpenGitHubPage());

        // Restore cached update state if already checked (e.g. on startup)
        if (_updateService.LastUpdateInfo != null)
            ApplyUpdateInfo(_updateService.LastUpdateInfo);
    }

    private async Task ConnectSpotifyAsync()
    {
        if (string.IsNullOrWhiteSpace(_settings.Spotify.ClientId))
        {
            MessageBox.Show("Please enter your Spotify Client ID first.",
                "Client ID Required", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        IsConnecting = true;
        AuthenticationStatus = "Waiting for Spotify login...";

        try
        {
            _authService.SetClientId(_settings.Spotify.ClientId);
            var success = await _authService.AuthenticateAsync(_settings.Spotify.ClientId);

            if (success)
            {
                IsAuthenticated = true;
                AuthenticationStatus = "Connected to Spotify";
                Save();
                _playerService.Start(_settings.Spotify.PollingIntervalMs);
            }
            else
            {
                AuthenticationStatus = "Authentication failed";
                MessageBox.Show("Authentication failed. Please check your Client ID and try again.",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        catch (Exception ex)
        {
            AuthenticationStatus = "Connection error";
            MessageBox.Show($"Error connecting to Spotify: {ex.Message}",
                "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsConnecting = false;
        }
    }

    private void DisconnectSpotify()
    {
        var result = MessageBox.Show("Disconnect from Spotify?",
            "Confirm Disconnect", MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            _playerService.Stop();
            _authService.ClearTokens();
            IsAuthenticated = false;
            AuthenticationStatus = "Not connected";
        }
    }

    private void Save()
    {
        _configService.SaveSettings(_settings);
        ApplyStartupRegistry(_settings.RunAtStartup);
        _onSettingsSaved();
    }

    private void ApplyDefaultSize() => ApplySize(380, 0);

    private void ApplySize(double width, double height)
    {
        _settings.Position.Width = width;
        _settings.Position.Height = height;
        OnPropertyChanged(nameof(Settings));
        Save();
    }

    private void ApplyCompact()
    {
        _settings.Position.Width = 300;
        _settings.Position.Height = 70;
        _settings.Appearance.AlbumArtSize = 44;
        _settings.Appearance.TrackFontSize = 13;
        _settings.Appearance.ArtistFontSize = 11;
        _settings.Appearance.Padding = 8;
        OnPropertyChanged(nameof(Settings));
        Save();
    }

    private void ApplyArtSize(int size)
    {
        _settings.Appearance.AlbumArtSize = size;
        OnPropertyChanged(nameof(Settings));
        Save();
    }

    private void ReregisterHotkeys()
    {
        var dialog = new HotkeyDialog(_settings.Hotkeys, _onDisableHotkeys, _onEnableHotkeys);
        if (dialog.ShowDialog() == true)
        {
            _settings.Hotkeys.ToggleVisibility = dialog.ToggleVisibilityHotkey;
            _configService.SaveSettings(_settings);
            _onSettingsSaved();
        }
    }

    private async Task CheckForUpdatesAsync()
    {
        if (IsDownloading) return;

        try
        {
            var updateInfo = await _updateService.CheckForUpdatesAsync();
            ApplyUpdateInfo(updateInfo);
        }
        catch (Exception ex)
        {
            HasCheckedForUpdates = true;
            UpdateStatusText = $"Update check failed: {ex.Message}";
            UpdateStatusColor = new SolidColorBrush(Color.FromRgb(255, 152, 0));
        }
    }

    private void ApplyUpdateInfo(UpdateInfo updateInfo)
    {
        HasCheckedForUpdates = true;

        var latestVersionFormatted = updateInfo.LatestVersion.StartsWith("v", StringComparison.OrdinalIgnoreCase)
            ? updateInfo.LatestVersion
            : $"v{updateInfo.LatestVersion}";

        LatestVersion = latestVersionFormatted;
        IsUpdateAvailable = updateInfo.IsUpdateAvailable;

        if (updateInfo.IsUpdateAvailable)
        {
            UpdateStatusText = $"Update Available! Version {latestVersionFormatted} is ready to download.";
            UpdateStatusColor = new SolidColorBrush(Color.FromRgb(76, 175, 80));
        }
        else
        {
            UpdateStatusText = $"You're up to date! ({CurrentVersion})";
            UpdateStatusColor = new SolidColorBrush(Color.FromRgb(33, 150, 243));
        }
    }

    private void ShowChangelogView()
    {
        if (_updateService.LastUpdateInfo == null || !IsUpdateAvailable) return;
        Changelog = _updateService.LastUpdateInfo.Changelog;
        ShowingChangelogView = true;
    }

    private async Task AcceptInstallationAsync()
    {
        if (IsDownloading || _updateService.LastUpdateInfo == null || !IsUpdateAvailable) return;

        var updateInfo = _updateService.LastUpdateInfo;
        IsDownloading = true;
        DownloadProgress = 0;

        try
        {
            var progress = new Progress<int>(percent =>
            {
                Application.Current.Dispatcher.Invoke(() => DownloadProgress = percent);
            });

            var success = await _updateService.DownloadAndInstallUpdateAsync(updateInfo, progress);

            if (!success)
            {
                MessageBox.Show(
                    "Failed to install the update. Please download manually from GitHub.",
                    "Update Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                ShowingChangelogView = false;
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error downloading update: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
            ShowingChangelogView = false;
        }
        finally
        {
            IsDownloading = false;
            DownloadProgress = 0;
        }
    }

    private bool IsRegistryStartupEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run");
            return key?.GetValue("SpotifyOnScreen") != null;
        }
        catch
        {
            return false;
        }
    }

    private void ApplyStartupRegistry(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);
            if (key == null) return;

            if (enable)
            {
                var exePath = Process.GetCurrentProcess().MainModule?.FileName ?? "";
                key.SetValue("SpotifyOnScreen", $"\"{exePath}\"");
            }
            else
            {
                key.DeleteValue("SpotifyOnScreen", false);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to update startup registry: {ex.Message}");
        }
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public class RelayCommand : ICommand
{
    private readonly Action? _execute;
    private readonly Func<Task>? _executeAsync;
    private readonly Func<bool>? _canExecute;

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public RelayCommand(Action execute, Func<bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public RelayCommand(Func<Task> executeAsync, Func<bool>? canExecute = null)
    {
        _executeAsync = executeAsync;
        _canExecute = canExecute;
    }

    public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;

    public async void Execute(object? parameter)
    {
        if (_executeAsync != null)
            await _executeAsync();
        else
            _execute?.Invoke();
    }
}

public class HotkeyDialog : Window
{
    private readonly System.Windows.Controls.TextBox _currentHotkeyTextBox;
    private readonly System.Windows.Controls.TextBox _toggleVisibilityTextBox;
    private readonly System.Windows.Controls.Button _saveButton;
    private readonly Action? _onDisableHotkeys;
    private readonly Action? _onEnableHotkeys;

    private string _toggleVisibilityHotkey = "";
    private readonly HashSet<Key> _pressedKeys = new();
    private string _capturedHotkey = "";

    public string ToggleVisibilityHotkey => _toggleVisibilityHotkey;

    public HotkeyDialog(HotkeySettings currentSettings, Action? onDisableHotkeys = null, Action? onEnableHotkeys = null)
    {
        Title = "Change Hotkey";
        Width = 500;
        Height = 290;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        _toggleVisibilityHotkey = currentSettings.ToggleVisibility;
        _onDisableHotkeys = onDisableHotkeys;
        _onEnableHotkeys = onEnableHotkeys;

        var grid = new System.Windows.Controls.Grid { Margin = new Thickness(20) };
        grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = GridLength.Auto });

        var titleLabel = new System.Windows.Controls.TextBlock
        {
            Text = "Toggle Visibility:",
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 5)
        };
        System.Windows.Controls.Grid.SetRow(titleLabel, 0);

        _toggleVisibilityTextBox = new System.Windows.Controls.TextBox
        {
            Text = _toggleVisibilityHotkey,
            IsReadOnly = true,
            Margin = new Thickness(0, 0, 0, 20)
        };
        System.Windows.Controls.Grid.SetRow(_toggleVisibilityTextBox, 1);

        var instructionLabel = new System.Windows.Controls.TextBlock
        {
            Text = "Press new keys for Toggle Visibility:",
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 10)
        };
        System.Windows.Controls.Grid.SetRow(instructionLabel, 2);

        var hotkeyPanel = new System.Windows.Controls.Border
        {
            BorderBrush = System.Windows.Media.Brushes.Gray,
            BorderThickness = new Thickness(2),
            Background = System.Windows.Media.Brushes.WhiteSmoke,
            Padding = new Thickness(15),
            Margin = new Thickness(0, 0, 0, 10)
        };

        _currentHotkeyTextBox = new System.Windows.Controls.TextBox
        {
            Text = "Hold at least 3 keys together...",
            FontSize = 16,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
            Foreground = System.Windows.Media.Brushes.DarkBlue,
            IsReadOnly = true,
            BorderThickness = new Thickness(0),
            Background = System.Windows.Media.Brushes.Transparent,
            TextAlignment = TextAlignment.Center
        };
        hotkeyPanel.Child = _currentHotkeyTextBox;
        System.Windows.Controls.Grid.SetRow(hotkeyPanel, 3);

        var infoText = new System.Windows.Controls.TextBlock
        {
            Text = "Hold at least 3 keys together (e.g., Ctrl+Shift+H)",
            FontStyle = FontStyles.Italic,
            Foreground = System.Windows.Media.Brushes.Gray,
            Margin = new Thickness(0, 0, 0, 10),
            TextWrapping = TextWrapping.Wrap
        };
        System.Windows.Controls.Grid.SetRow(infoText, 4);

        var buttonPanel = new System.Windows.Controls.StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right
        };
        System.Windows.Controls.Grid.SetRow(buttonPanel, 6);

        _saveButton = new System.Windows.Controls.Button
        {
            Content = "Save",
            Width = 80,
            Margin = new Thickness(0, 0, 5, 0),
            IsEnabled = false
        };
        _saveButton.Click += SaveButton_Click;

        var cancelButton = new System.Windows.Controls.Button
        {
            Content = "Cancel",
            Width = 80,
            IsCancel = true
        };
        cancelButton.Click += (s, e) => Close();

        buttonPanel.Children.Add(_saveButton);
        buttonPanel.Children.Add(cancelButton);

        grid.Children.Add(titleLabel);
        grid.Children.Add(_toggleVisibilityTextBox);
        grid.Children.Add(instructionLabel);
        grid.Children.Add(hotkeyPanel);
        grid.Children.Add(infoText);
        grid.Children.Add(buttonPanel);

        Content = grid;

        KeyDown += HotkeyDialog_KeyDown;
        KeyUp += HotkeyDialog_KeyUp;
        Loaded += HotkeyDialog_Loaded;
        Closed += HotkeyDialog_Closed;
    }

    private void HotkeyDialog_Loaded(object sender, RoutedEventArgs e)
    {
        _onDisableHotkeys?.Invoke();
    }

    private void HotkeyDialog_Closed(object? sender, EventArgs e)
    {
        _onEnableHotkeys?.Invoke();
    }

    private void HotkeyDialog_KeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        _pressedKeys.Add(key);
        UpdateHotkeyDisplay();
    }

    private void HotkeyDialog_KeyUp(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        _pressedKeys.Remove(key);
        UpdateHotkeyDisplay();
    }

    private void UpdateHotkeyDisplay()
    {
        var parts = new List<string>();
        var modifierKeys = new List<Key>();
        var regularKeys = new List<Key>();

        foreach (var key in _pressedKeys)
        {
            if (key == Key.LeftCtrl || key == Key.RightCtrl)
                modifierKeys.Add(Key.LeftCtrl);
            else if (key == Key.LeftShift || key == Key.RightShift)
                modifierKeys.Add(Key.LeftShift);
            else if (key == Key.LeftAlt || key == Key.RightAlt || key == Key.System)
                modifierKeys.Add(Key.LeftAlt);
            else
                regularKeys.Add(key);
        }

        var distinctModifiers = modifierKeys.Distinct().ToList();
        foreach (var mod in distinctModifiers)
        {
            if (mod == Key.LeftCtrl) parts.Add("Ctrl");
            else if (mod == Key.LeftShift) parts.Add("Shift");
            else if (mod == Key.LeftAlt) parts.Add("Alt");
        }

        foreach (var key in regularKeys.Distinct())
            parts.Add(key.ToString());

        var hotkeyString = string.Join("+", parts);
        int totalKeys = distinctModifiers.Count + regularKeys.Distinct().Count();

        if (totalKeys >= 3)
        {
            _capturedHotkey = hotkeyString;
            _currentHotkeyTextBox.Text = hotkeyString;
        }
        else if (!string.IsNullOrEmpty(_capturedHotkey))
        {
            _currentHotkeyTextBox.Text = _capturedHotkey;
        }
        else
        {
            _currentHotkeyTextBox.Text = string.IsNullOrEmpty(hotkeyString)
                ? "Hold at least 3 keys together..."
                : hotkeyString;
        }

        _saveButton.IsEnabled = !string.IsNullOrEmpty(_capturedHotkey);
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        _toggleVisibilityHotkey = _capturedHotkey;
        _toggleVisibilityTextBox.Text = _toggleVisibilityHotkey;
        DialogResult = true;
        Close();
    }
}
