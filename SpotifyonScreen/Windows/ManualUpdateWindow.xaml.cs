using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using SpotifyOnScreen.Configuration;
using SpotifyOnScreen.Services;
using Color = System.Windows.Media.Color;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;

namespace SpotifyOnScreen.Windows;

// Fallback for when the automatic updater (see SettingsWindow's Updates tab) doesn't
// detect or install a new version. Always fetches the latest published release —
// regardless of what the local version comparison thinks — so the user can force a
// reinstall, or fall back to downloading it manually in a browser.
public partial class ManualUpdateWindow : Window
{
    private readonly UpdateService _updateService;
    private UpdateInfo? _releaseInfo;
    private bool _isDownloading;

    public ManualUpdateWindow(UpdateService updateService)
    {
        InitializeComponent();
        _updateService = updateService;

        DownloadBtn.IsEnabled = false;
        SetStatus("Checking for the latest release...", "", Color.FromRgb(0x1D, 0xB9, 0x54));

        Loaded += async (_, _) => await CheckAsync();
    }

    private async Task CheckAsync()
    {
        _releaseInfo = await _updateService.GetLatestReleaseInfoAsync();

        if (!string.IsNullOrWhiteSpace(_releaseInfo.DownloadUrl))
        {
            SetStatus(
                $"Version v{_releaseInfo.LatestVersion} is ready to download.",
                $"{_releaseInfo.FileName}  |  {UpdateService.FormatFileSize(_releaseInfo.FileSize)}",
                Color.FromRgb(0x1D, 0xB9, 0x54));
            DownloadBtn.IsEnabled = true;
        }
        else
        {
            SetStatus("Couldn't reach the update server.", "Try again later, or open the download page in your browser instead.",
                Color.FromRgb(0xF8, 0x71, 0x71));
        }
    }

    private void SetStatus(string title, string message, Color color)
    {
        StatusTitleText.Text = title;
        StatusMessageText.Text = message;
        var brush = new SolidColorBrush(color);
        StatusIcon.Fill = brush;
    }

    private async void DownloadBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_isDownloading || _releaseInfo == null) return;
        _isDownloading = true;

        DownloadBtn.IsEnabled = false;
        BrowserBtn.IsEnabled = false;
        DownloadProgressPanel.Visibility = Visibility.Visible;

        var progress = new Progress<int>(pct =>
            Dispatcher.BeginInvoke(new Action(() =>
            {
                DownloadProgressBar.Value = pct;
                DownloadProgressText.Text = $"Downloading update... {pct}%";
            })));

        var ok = await _updateService.DownloadAndInstallLauncherExeAsync(progress);

        if (!ok)
        {
            _isDownloading = false;
            DownloadProgressPanel.Visibility = Visibility.Collapsed;
            DownloadBtn.IsEnabled = true;
            BrowserBtn.IsEnabled = true;
            SetStatus("Couldn't reach the update server.", "Try again later, or open the download page in your browser instead.",
                Color.FromRgb(0xF8, 0x71, 0x71));
        }
    }

    private void BrowserBtn_Click(object sender, RoutedEventArgs e)
    {
        var url = string.IsNullOrWhiteSpace(_releaseInfo?.ReleaseUrl)
            ? $"{AppVersion.GetGitHubRepoUrl()}/releases/latest"
            : _releaseInfo.ReleaseUrl;

        try
        {
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ManualUpdateWindow] Failed to open browser: {ex.Message}");
        }
    }

    private void CloseBtn_Click(object sender, RoutedEventArgs e) => Close();
}
