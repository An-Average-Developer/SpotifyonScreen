using System.Windows.Input;
using NHotkey;
using NHotkey.Wpf;
using SpotifyOnScreen.Models;

namespace SpotifyOnScreen.Managers;

public class GlobalHotkeyManager
{
    private readonly Action _onToggleVisibility;

    public GlobalHotkeyManager(Action onToggleVisibility)
    {
        _onToggleVisibility = onToggleVisibility;
    }

    public void RegisterHotkeys(HotkeySettings settings)
    {
        try
        {
            UnregisterHotkeys();

            if (TryParseHotkey(settings.ToggleVisibility, out var toggleKey, out var toggleModifiers))
            {
                HotkeyManager.Current.AddOrReplace("ToggleVisibility", toggleKey, toggleModifiers, OnToggleVisibility);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to register hotkeys: {ex.Message}");
        }
    }

    public void UnregisterHotkeys()
    {
        try
        {
            HotkeyManager.Current.Remove("ToggleVisibility");
        }
        catch
        {
            // Ignore errors when unregistering non-existent hotkeys
        }
    }

    private void OnToggleVisibility(object? sender, HotkeyEventArgs e)
    {
        _onToggleVisibility();
        e.Handled = true;
    }

    private bool TryParseHotkey(string hotkeyString, out Key key, out ModifierKeys modifiers)
    {
        key = Key.None;
        modifiers = ModifierKeys.None;

        if (string.IsNullOrWhiteSpace(hotkeyString))
            return false;

        try
        {
            var parts = hotkeyString.Split('+');
            if (parts.Length == 0)
                return false;

            var keyPart = parts[^1].Trim();

            foreach (var part in parts[..^1])
            {
                var modifier = part.Trim();
                if (modifier.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) ||
                    modifier.Equals("Control", StringComparison.OrdinalIgnoreCase))
                    modifiers |= ModifierKeys.Control;
                else if (modifier.Equals("Alt", StringComparison.OrdinalIgnoreCase))
                    modifiers |= ModifierKeys.Alt;
                else if (modifier.Equals("Shift", StringComparison.OrdinalIgnoreCase))
                    modifiers |= ModifierKeys.Shift;
                else if (modifier.Equals("Win", StringComparison.OrdinalIgnoreCase) ||
                         modifier.Equals("Windows", StringComparison.OrdinalIgnoreCase))
                    modifiers |= ModifierKeys.Windows;
            }

            return Enum.TryParse<Key>(keyPart, true, out key);
        }
        catch
        {
            return false;
        }
    }
}
