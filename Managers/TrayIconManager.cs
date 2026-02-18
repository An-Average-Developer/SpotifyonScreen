using System.Drawing;
using System.Reflection;
using System.Windows;
using System.Windows.Forms;
using Application = System.Windows.Application;

namespace SpotifyOnScreen.Managers;

public class TrayIconManager : IDisposable
{
    private NotifyIcon? _notifyIcon;
    private readonly Action _onOpenSettings;
    private readonly Action _onToggleOverlay;
    private readonly Action? _onCheckForUpdates;
    private readonly Action _onExit;

    public TrayIconManager(Action onOpenSettings, Action onToggleOverlay, Action onExit, Action? onCheckForUpdates = null)
    {
        _onOpenSettings = onOpenSettings;
        _onToggleOverlay = onToggleOverlay;
        _onCheckForUpdates = onCheckForUpdates;
        _onExit = onExit;
    }

    public void Initialize()
    {
        _notifyIcon = new NotifyIcon
        {
            Icon = LoadIconFromResource(),
            Visible = true,
            Text = "Spotify On Screen"
        };

        var contextMenu = new ContextMenuStrip();
        contextMenu.Items.Add("Settings", null, (s, e) => _onOpenSettings());
        contextMenu.Items.Add("Toggle Overlay", null, (s, e) => _onToggleOverlay());
        contextMenu.Items.Add("Check for Updates", null, (s, e) => _onCheckForUpdates?.Invoke());
        contextMenu.Items.Add(new ToolStripSeparator());
        contextMenu.Items.Add("Exit", null, (s, e) => _onExit());

        _notifyIcon.ContextMenuStrip = contextMenu;
        _notifyIcon.DoubleClick += (s, e) => _onOpenSettings();
    }

    private Icon LoadIconFromResource()
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            var resourceName = "SpotifyOnScreen.Resources.Icons.icon.png";

            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream != null)
            {
                using var bitmap = new Bitmap(stream);
                return Icon.FromHandle(bitmap.GetHicon());
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load icon: {ex.Message}");
        }

        return SystemIcons.Application;
    }

    public void ShowNotification(string title, string message)
    {
        if (_notifyIcon != null)
        {
            _notifyIcon.BalloonTipTitle = title;
            _notifyIcon.BalloonTipText = message;
            _notifyIcon.ShowBalloonTip(3000);
        }
    }

    public void Dispose()
    {
        if (_notifyIcon != null)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _notifyIcon = null;
        }
    }
}
