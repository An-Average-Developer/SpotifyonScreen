using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Navigation;

namespace SpotifyOnScreen.Windows;

public partial class TwitchClientIdHelpWindow : Window
{
    public TwitchClientIdHelpWindow()
    {
        InitializeComponent();
    }

    private void CloseBtn_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = e.Uri.AbsoluteUri,
                UseShellExecute = true
            });
            e.Handled = true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to open URL: {ex.Message}");
        }
    }
}
