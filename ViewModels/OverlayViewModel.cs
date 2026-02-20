using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Collections.Generic;
using SpotifyOnScreen.Models;
using SpotifyOnScreen.Services;
using Application = System.Windows.Application;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;

namespace SpotifyOnScreen.ViewModels;

public class OverlayViewModel : INotifyPropertyChanged
{
    private readonly ConfigurationService _configService;
    private readonly IPlayerService _playerService;
    private readonly HttpClient _httpClient = new();

    private AppSettings _settings;
    private string _cachedAlbumArtUrl = string.Empty;

    private double _durationMs;
    public double DurationMs => _durationMs;

    private string _trackName = "Not Playing";
    private string _artistName = string.Empty;
    private string _albumName = string.Empty;
    private BitmapImage? _albumArt;
    private double _progressPercent;
    private bool _isPlaying;
    private bool _hasTrack;

    private Brush _textBrush = Brushes.White;
    private Brush _secondaryTextBrush = new SolidColorBrush(Color.FromRgb(176, 176, 176));
    private Brush _backgroundBrush = new SolidColorBrush(Color.FromArgb(230, 30, 30, 46));
    private Brush _accentBrush = new SolidColorBrush(Color.FromRgb(29, 185, 84));
    private string _fontFamily = "Segoe UI";
    private int _trackFontSize = 18;
    private int _artistFontSize = 14;
    private int _cornerRadius = 12;
    private int _padding = 12;
    private int _albumArtSize = 64;
    private bool _showProgressBar = true;
    private bool _showAlbumArt = true;
    private bool _dynamicBackground;
    private double _backgroundOpacity = 0.9;
    private List<Color> _albumColors = [];
    private WindowPosition _position = new();
    private double _overlayWidth = 380;
    private double _overlayHeight = 0;
    private Brush _progressBarBrush = new SolidColorBrush(Color.FromRgb(29, 185, 84));
    private bool _progressBarGlow;
    private bool _progressBarDynamic;
    private Effect? _progressBarEffect;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string TrackName { get => _trackName; set { _trackName = value; OnPropertyChanged(); } }
    public string ArtistName { get => _artistName; set { _artistName = value; OnPropertyChanged(); } }
    public string AlbumName { get => _albumName; set { _albumName = value; OnPropertyChanged(); } }
    public BitmapImage? AlbumArt { get => _albumArt; set { _albumArt = value; OnPropertyChanged(); } }
    public double ProgressPercent { get => _progressPercent; set { _progressPercent = value; OnPropertyChanged(); } }
    public bool IsPlaying { get => _isPlaying; set { _isPlaying = value; OnPropertyChanged(); } }
    public bool HasTrack { get => _hasTrack; set { _hasTrack = value; OnPropertyChanged(); } }

    public Brush TextBrush { get => _textBrush; set { _textBrush = value; OnPropertyChanged(); } }
    public Brush SecondaryTextBrush { get => _secondaryTextBrush; set { _secondaryTextBrush = value; OnPropertyChanged(); } }
    public Brush BackgroundBrush { get => _backgroundBrush; set { _backgroundBrush = value; OnPropertyChanged(); } }
    public Brush AccentBrush { get => _accentBrush; set { _accentBrush = value; OnPropertyChanged(); } }
    public string FontFamily { get => _fontFamily; set { _fontFamily = value; OnPropertyChanged(); } }
    public int TrackFontSize { get => _trackFontSize; set { _trackFontSize = value; OnPropertyChanged(); } }
    public int ArtistFontSize { get => _artistFontSize; set { _artistFontSize = value; OnPropertyChanged(); } }
    public int CornerRadius { get => _cornerRadius; set { _cornerRadius = value; OnPropertyChanged(); } }
    public int Padding { get => _padding; set { _padding = value; OnPropertyChanged(); } }
    public int AlbumArtSize { get => _albumArtSize; set { _albumArtSize = value; OnPropertyChanged(); } }
    public bool ShowProgressBar { get => _showProgressBar; set { _showProgressBar = value; OnPropertyChanged(); } }
    public bool ShowAlbumArt { get => _showAlbumArt; set { _showAlbumArt = value; OnPropertyChanged(); } }
    public bool DynamicBackground
    {
        get => _dynamicBackground;
        set
        {
            _dynamicBackground = value;
            OnPropertyChanged();
            if (value && AlbumArt != null)
            {
                AlbumColors = AlbumColorExtractor.ExtractColors(AlbumArt);
            }
        }
    }
    public double BackgroundOpacity { get => _backgroundOpacity; set { _backgroundOpacity = value; OnPropertyChanged(); } }
    public List<Color> AlbumColors
    {
        get => _albumColors;
        set
        {
            _albumColors = value;
            OnPropertyChanged();
            if (_progressBarDynamic && value.Count > 0)
                ProgressBarBrush = new SolidColorBrush(value[0]);
        }
    }
    public WindowPosition Position { get => _position; set { _position = value; OnPropertyChanged(); } }
    public double OverlayWidth { get => _overlayWidth; set { _overlayWidth = value; OnPropertyChanged(); } }
    public double OverlayHeight { get => _overlayHeight; set { _overlayHeight = value; OnPropertyChanged(); } }
    public Brush ProgressBarBrush
    {
        get => _progressBarBrush;
        set { _progressBarBrush = value; OnPropertyChanged(); UpdateProgressBarEffect(); }
    }
    public bool ProgressBarGlow
    {
        get => _progressBarGlow;
        set { _progressBarGlow = value; OnPropertyChanged(); UpdateProgressBarEffect(); }
    }
    public bool ProgressBarDynamic
    {
        get => _progressBarDynamic;
        set
        {
            _progressBarDynamic = value;
            OnPropertyChanged();
            if (value && _albumColors.Count > 0)
                ProgressBarBrush = new SolidColorBrush(_albumColors[0]);
        }
    }
    public Effect? ProgressBarEffect { get => _progressBarEffect; set { _progressBarEffect = value; OnPropertyChanged(); } }

    public OverlayViewModel(ConfigurationService configService, IPlayerService playerService)
    {
        _configService = configService;
        _playerService = playerService;
        _settings = configService.LoadSettings();

        _playerService.TrackUpdated += OnTrackUpdated;
        _playerService.PlaybackStopped += OnPlaybackStopped;

        LoadSettings();
    }

    public void LoadSettings()
    {
        _settings = _configService.LoadSettings();
        Position = _settings.Position;
        ShowProgressBar = _settings.Spotify.ShowProgressBar;
        ShowAlbumArt = _settings.Spotify.ShowAlbumArt;
        OverlayWidth = _settings.Position.Width > 0 ? _settings.Position.Width : 380;
        OverlayHeight = _settings.Position.Height;
        ApplyAppearance(_settings.Appearance);
    }

    public void ApplyAppearance(OverlayAppearance appearance)
    {
        FontFamily = appearance.FontFamily;
        TrackFontSize = appearance.TrackFontSize;
        ArtistFontSize = appearance.ArtistFontSize;
        TextBrush = (Brush)new BrushConverter().ConvertFrom(appearance.TextColor)!;
        SecondaryTextBrush = (Brush)new BrushConverter().ConvertFrom(appearance.SecondaryTextColor)!;
        AccentBrush = (Brush)new BrushConverter().ConvertFrom(appearance.AccentColor)!;

        var bgColor = (Color)ColorConverter.ConvertFromString(appearance.BackgroundColor);
        bgColor.A = (byte)(appearance.BackgroundOpacity * 255);
        BackgroundBrush = new SolidColorBrush(bgColor);

        BackgroundOpacity = appearance.BackgroundOpacity;
        CornerRadius = appearance.CornerRadius;
        Padding = appearance.Padding;
        AlbumArtSize = appearance.AlbumArtSize;
        DynamicBackground = appearance.DynamicBackground;
        ProgressBarGlow = appearance.ProgressBarGlow;
        ProgressBarDynamic = appearance.ProgressBarDynamic;
        if (!appearance.ProgressBarDynamic)
            ProgressBarBrush = (Brush)new BrushConverter().ConvertFrom(appearance.ProgressBarColor)!;
    }

    private void UpdateProgressBarEffect()
    {
        if (_progressBarGlow)
        {
            var color = (_progressBarBrush as SolidColorBrush)?.Color ?? Colors.White;
            if (_progressBarEffect is DropShadowEffect existing)
            {
                existing.Color = color;
            }
            else
            {
                ProgressBarEffect = new DropShadowEffect
                {
                    BlurRadius = 10,
                    ShadowDepth = 0,
                    Opacity = 1.0,
                    Color = color
                };
            }
        }
        else
        {
            ProgressBarEffect = null;
        }
    }

    public void SavePosition(double x, double y)
    {
        _position.X = x;
        _position.Y = y;
        _settings.Position = _position;
        _configService.SaveSettings(_settings);
    }

    public void SaveSize(double width, double height)
    {
        OverlayWidth = width;
        OverlayHeight = height;
        _settings.Position.Width = width;
        _settings.Position.Height = height;
        _configService.SaveSettings(_settings);
    }

    private void OnTrackUpdated(object? sender, SpotifyTrackData data)
    {
        Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            TrackName = data.TrackName;
            ArtistName = data.ArtistName;
            AlbumName = data.AlbumName;
            IsPlaying = data.IsPlaying;
            HasTrack = true;

            _durationMs = data.DurationMs;

            if (_durationMs > 0)
                ProgressPercent = Math.Clamp((double)data.ProgressMs / _durationMs, 0.0, 1.0);

            if (data.AlbumArtUrl != _cachedAlbumArtUrl && !string.IsNullOrEmpty(data.AlbumArtUrl))
            {
                _cachedAlbumArtUrl = data.AlbumArtUrl;

                if (File.Exists(data.AlbumArtUrl))
                    _ = LoadAlbumArtFromFileAsync(data.AlbumArtUrl);
                else
                    _ = LoadAlbumArtAsync(data.AlbumArtUrl);
            }
        });
    }

    private void OnPlaybackStopped(object? sender, EventArgs e)
    {
        Application.Current?.Dispatcher.Invoke(() =>
        {
            TrackName = "Not Playing";
            ArtistName = string.Empty;
            AlbumName = string.Empty;
            IsPlaying = false;
            HasTrack = false;
            ProgressPercent = 0;
        });
    }



    private async Task LoadAlbumArtFromFileAsync(string filePath)
    {
        try
        {
            var decodeWidth = AlbumArtSize * 2;
            var extractColors = DynamicBackground || ProgressBarDynamic;

            var (bitmap, colors) = await Task.Run(() =>
            {
                var bytes = File.ReadAllBytes(filePath);
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.StreamSource = new MemoryStream(bytes);
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.DecodePixelWidth = decodeWidth;
                bmp.EndInit();
                bmp.Freeze();

                List<Color>? clrs = null;
                if (extractColors)
                    clrs = AlbumColorExtractor.ExtractColors(bmp);

                return (bmp, clrs);
            });

            AlbumArt = bitmap;
            if (colors != null)
                AlbumColors = colors;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load album art from file: {ex.Message}");
        }
    }

    private async Task LoadAlbumArtAsync(string url)
    {
        try
        {
            var bytes = await _httpClient.GetByteArrayAsync(url);

            var decodeWidth = AlbumArtSize * 2; // 2x for high DPI
            var extractColors = DynamicBackground || ProgressBarDynamic;

            var (bitmap, colors) = await Task.Run(() =>
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.StreamSource = new MemoryStream(bytes);
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.DecodePixelWidth = decodeWidth;
                bmp.EndInit();
                bmp.Freeze();

                List<Color>? clrs = null;
                if (extractColors)
                    clrs = AlbumColorExtractor.ExtractColors(bmp);

                return (bmp, clrs);
            });

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                AlbumArt = bitmap;
                if (colors != null)
                    AlbumColors = colors;
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load album art: {ex.Message}");
        }
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
