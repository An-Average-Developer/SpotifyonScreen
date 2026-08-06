using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Navigation;
using SpotifyOnScreen.ViewModels;
using MessageBox = System.Windows.MessageBox;

namespace SpotifyOnScreen.Windows;

public partial class SettingsWindow : Window
{
    public SettingsWindow(SettingsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void AllowObsCaptureCheckBox_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.CheckBox { IsChecked: true }) return;

        MessageBox.Show(this,
            "To capture the overlay in OBS:\n\n" +
            "1. In OBS, click the + under Sources and choose \"Window Capture\".\n" +
            "2. Set Capture method to \"Windows 10 (1903 and up)\".\n" +
            "3. In the Window dropdown, select \"Spotify On Screen\".\n\n" +
            "The overlay must be running for it to appear in the window list.",
            "Enabling OBS Capture",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            DragMove();
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
            System.Diagnostics.Debug.WriteLine($"Failed to open URL: {ex.Message}");
        }
    }
}
