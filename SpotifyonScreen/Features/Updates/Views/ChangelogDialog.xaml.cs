using System;
using System.Windows;

namespace SpotifyOnScreen.Features.Updates.Views;

public partial class ChangelogDialog : Window
{
    public bool UserAccepted { get; private set; }

    public ChangelogDialog(string changelog, string latestVersion, long fileSize)
    {
        InitializeComponent();

        var versionText = latestVersion.StartsWith("v", StringComparison.OrdinalIgnoreCase)
            ? latestVersion
            : $"v{latestVersion}";

        VersionText.Text = $"Update Available — {versionText}";
        ChangelogText.Text = changelog;

        if (fileSize > 0)
        {
            var sizeInMB = fileSize / (1024.0 * 1024.0);
            FileSizeText.Text = $"Download size: {sizeInMB:F2} MB";
        }
        else
        {
            FileSizeText.Text = "Download size: Unknown";
        }
    }

    private void DownloadButton_Click(object sender, RoutedEventArgs e)
    {
        UserAccepted = true;
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
