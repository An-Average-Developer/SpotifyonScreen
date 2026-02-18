using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using SpotifyOnScreen.ViewModels;
using Color = System.Windows.Media.Color;

namespace SpotifyOnScreen.Windows;

public partial class OverlayWindow : Window
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_LAYERED = 0x00080000;
    private const int VK_LCONTROL = 0xA2;
    private const int VK_RCONTROL = 0xA3;
    private const int VK_LSHIFT = 0xA0;
    private const int VK_RSHIFT = 0xA1;
    private const int VK_LALT = 0xA4;
    private const int VK_RALT = 0xA5;

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    private readonly OverlayViewModel _viewModel;
    private DispatcherTimer? _keyboardCheckTimer;
    private DispatcherTimer? _tornadoTimer;
    private DateTime? _ctrlShiftPressedTime;
    private DateTime? _ctrlShiftAltPressedTime;
    private bool _isDragHandleVisible;
    private bool _isResizeHandleVisible;
    private System.Windows.Point _resizeStartScreenPos;
    private double _resizeStartWidth;
    private IntPtr _hwnd;

    private readonly List<TornadoBlob> _blobs = [];

    public OverlayWindow(OverlayViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = _viewModel;

        SourceInitialized += OnSourceInitialized;

        _keyboardCheckTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(50)
        };
        _keyboardCheckTimer.Tick += KeyboardCheckTimer_Tick;
        _keyboardCheckTimer.Start();

        _viewModel.PropertyChanged += ViewModel_PropertyChanged;

        // Apply size immediately so the window never renders at the XAML default.
        Width = _viewModel.OverlayWidth;
        if (_viewModel.OverlayHeight > 0)
        {
            SizeToContent = SizeToContent.Manual;
            Height = _viewModel.OverlayHeight;
        }

        // After layout is complete, apply the saved position with screen-bounds clamping
        // so the overlay can't start off-screen.
        Loaded += (_, _) =>
        {
            Width = _viewModel.OverlayWidth;
            if (_viewModel.OverlayHeight > 0)
            {
                SizeToContent = SizeToContent.Manual;
                Height = _viewModel.OverlayHeight;
            }
            ApplyPositionClamped(_viewModel.Position.X, _viewModel.Position.Y);
            UpdateClip();
            if (_viewModel.DynamicBackground)
                ToggleDynamicBackground(true);
        };
    }

    // Called from XAML SizeChanged on ClippedContainer
    private void ClippedContainer_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateClip();
    }

    private void UpdateClip()
    {
        double w = ClippedContainer.ActualWidth;
        double h = ClippedContainer.ActualHeight;
        if (w <= 0 || h <= 0) return;

        double r = _viewModel.CornerRadius;
        ClippedContainer.Clip = new RectangleGeometry(new Rect(0, 0, w, h), r, r);
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(OverlayViewModel.AlbumColors):
                UpdateTornadoColors(_viewModel.AlbumColors);
                break;
            case nameof(OverlayViewModel.DynamicBackground):
                ToggleDynamicBackground(_viewModel.DynamicBackground);
                break;
            case nameof(OverlayViewModel.BackgroundOpacity):
                DynamicBgGrid.Opacity = _viewModel.BackgroundOpacity;
                break;
            case nameof(OverlayViewModel.CornerRadius):
                UpdateClip();
                break;
            case nameof(OverlayViewModel.Position):
                ApplyPositionClamped(_viewModel.Position.X, _viewModel.Position.Y);
                break;
            case nameof(OverlayViewModel.ProgressPercent):
                OnProgressUpdated(_viewModel.ProgressPercent);
                break;
            case nameof(OverlayViewModel.OverlayWidth):
                Width = _viewModel.OverlayWidth;
                break;
            case nameof(OverlayViewModel.OverlayHeight):
                if (_viewModel.OverlayHeight > 0)
                {
                    SizeToContent = SizeToContent.Manual;
                    Height = _viewModel.OverlayHeight;
                }
                else
                {
                    SizeToContent = SizeToContent.Height;
                }
                break;
        }
    }

    private void ApplyPositionClamped(double x, double y)
    {
        var workArea = SystemParameters.WorkArea;
        Left = Math.Max(workArea.Left, Math.Min(x, workArea.Right - ActualWidth));
        Top = Math.Max(workArea.Top, Math.Min(y, workArea.Bottom - ActualHeight));
    }

    private bool _animationRunning;
    private string _animatingTrack = "";

    private void OnProgressUpdated(double target)
    {
        var duration = _viewModel.DurationMs;
        if (duration <= 0) return;

        var trackId = _viewModel.TrackName + "|" + _viewModel.ArtistName;
        var isNewTrack = trackId != _animatingTrack;
        var current = ProgressScale.ScaleX;
        var isSeek = Math.Abs(target - current) > 0.05;
        var isPaused = !_viewModel.IsPlaying;

        if (isPaused)
        {
            // Stop animation, hold at polled position
            ProgressScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            ProgressScale.ScaleX = target;
            _animationRunning = false;
            return;
        }

        if (isNewTrack || isSeek || !_animationRunning)
        {
            // Start fresh animation from polled position to end of song
            _animatingTrack = trackId;
            var remainingMs = duration * (1.0 - target);

            ProgressScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            var anim = new DoubleAnimation
            {
                From = target,
                To = 1.0,
                Duration = TimeSpan.FromMilliseconds(remainingMs)
            };
            ProgressScale.BeginAnimation(ScaleTransform.ScaleXProperty, anim);
            _animationRunning = true;
            return;
        }

        // Regular poll, same track, animation already running — do nothing
    }

    private void ToggleDynamicBackground(bool enabled)
    {
        if (enabled)
        {
            StaticBgBorder.Visibility = Visibility.Collapsed;
            DynamicBgGrid.Visibility = Visibility.Visible;
            DynamicBgGrid.Opacity = _viewModel.BackgroundOpacity;

            if (_tornadoTimer == null)
            {
                _tornadoTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(16)
                };
                _tornadoTimer.Tick += TornadoTimer_Tick;
            }
            _tornadoTimer.Start();

            if (_viewModel.AlbumColors.Count > 0)
                UpdateTornadoColors(_viewModel.AlbumColors);
        }
        else
        {
            DynamicBgGrid.Visibility = Visibility.Collapsed;
            StaticBgBorder.Visibility = Visibility.Visible;
            _tornadoTimer?.Stop();
        }
    }

    private void UpdateTornadoColors(List<Color> colors)
    {
        if (!_viewModel.DynamicBackground || colors.Count == 0) return;

        TornadoCanvas.Children.Clear();
        _blobs.Clear();

        var dominant = colors[0];
        var darkBase = Color.FromArgb(210,
            (byte)(dominant.R * 0.2),
            (byte)(dominant.G * 0.2),
            (byte)(dominant.B * 0.2));
        DynamicBaseBorder.Background = new SolidColorBrush(darkBase);

        var darkOverlay = Color.FromArgb(90,
            (byte)(dominant.R * 0.15),
            (byte)(dominant.G * 0.15),
            (byte)(dominant.B * 0.15));
        DynamicDarkOverlay.Background = new SolidColorBrush(darkOverlay);

        double w = ClippedContainer.ActualWidth > 0 ? ClippedContainer.ActualWidth : 380;
        double h = ClippedContainer.ActualHeight > 0 ? ClippedContainer.ActualHeight : 100;
        double cx = w / 2;
        double cy = h / 2;
        double maxDim = Math.Max(w, h);

        var random = new Random();

        for (int i = 0; i < colors.Count && i < 5; i++)
        {
            var color = colors[i];

            for (int j = 0; j < 3; j++)
            {
                double orbitRadius = 10 + random.NextDouble() * (maxDim * 0.5);
                double startAngle = (2 * Math.PI / colors.Count) * i + (j * 2 * Math.PI / 3);
                double speed = 0.5 + random.NextDouble() * 1.5;
                if (j % 2 == 1) speed = -speed;

                double blobSize = maxDim * 0.5 + random.NextDouble() * maxDim * 0.6;

                var ellipse = new Ellipse
                {
                    Width = blobSize,
                    Height = blobSize,
                    Opacity = 0.55 + random.NextDouble() * 0.35,
                    Fill = new RadialGradientBrush
                    {
                        GradientStops =
                        [
                            new GradientStop(Color.FromArgb(220, color.R, color.G, color.B), 0),
                            new GradientStop(Color.FromArgb(140, color.R, color.G, color.B), 0.35),
                            new GradientStop(Color.FromArgb(50, color.R, color.G, color.B), 0.7),
                            new GradientStop(Color.FromArgb(0, color.R, color.G, color.B), 1.0)
                        ]
                    }
                };

                TornadoCanvas.Children.Add(ellipse);

                _blobs.Add(new TornadoBlob
                {
                    Element = ellipse,
                    CenterX = cx,
                    CenterY = cy,
                    OrbitRadius = orbitRadius,
                    Angle = startAngle,
                    Speed = speed,
                    Size = blobSize
                });
            }
        }

        UpdateBlobPositions();
    }

    private void TornadoTimer_Tick(object? sender, EventArgs e)
    {
        foreach (var blob in _blobs)
            blob.Angle += blob.Speed * 0.02;
        UpdateBlobPositions();
    }

    private void UpdateBlobPositions()
    {
        foreach (var blob in _blobs)
        {
            double x = blob.CenterX + Math.Cos(blob.Angle) * blob.OrbitRadius - blob.Size / 2;
            double y = blob.CenterY + Math.Sin(blob.Angle) * blob.OrbitRadius - blob.Size / 2;
            Canvas.SetLeft(blob.Element, x);
            Canvas.SetTop(blob.Element, y);
        }
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _hwnd = new WindowInteropHelper(this).Handle;
        SetClickThrough(true);
    }

    private void SetClickThrough(bool enable)
    {
        if (_hwnd == IntPtr.Zero) return;

        int exStyle = GetWindowLong(_hwnd, GWL_EXSTYLE);

        if (enable)
            exStyle |= WS_EX_TRANSPARENT | WS_EX_LAYERED;
        else
            exStyle &= ~WS_EX_TRANSPARENT;

        SetWindowLong(_hwnd, GWL_EXSTYLE, exStyle);
    }

    private static bool IsKeyDown(int vKey) => (GetAsyncKeyState(vKey) & 0x8000) != 0;

    private void KeyboardCheckTimer_Tick(object? sender, EventArgs e)
    {
        bool isCtrlPressed = IsKeyDown(VK_LCONTROL) || IsKeyDown(VK_RCONTROL);
        bool isShiftPressed = IsKeyDown(VK_LSHIFT) || IsKeyDown(VK_RSHIFT);
        bool isAltPressed = IsKeyDown(VK_LALT) || IsKeyDown(VK_RALT);
        bool isCtrlShiftAltPressed = isCtrlPressed && isShiftPressed && isAltPressed;
        bool isCtrlShiftOnlyPressed = isCtrlPressed && isShiftPressed && !isAltPressed;

        // Handle Ctrl+Shift+Alt → resize mode
        if (isCtrlShiftAltPressed)
        {
            // Dismiss drag handle if it was shown
            _ctrlShiftPressedTime = null;
            if (_isDragHandleVisible)
            {
                DragHandle.Visibility = Visibility.Collapsed;
                InteractiveOverlay.Visibility = Visibility.Collapsed;
                _isDragHandleVisible = false;
            }

            if (_ctrlShiftAltPressedTime == null)
            {
                _ctrlShiftAltPressedTime = DateTime.Now;
            }
            else if (!_isResizeHandleVisible)
            {
                var elapsed = DateTime.Now - _ctrlShiftAltPressedTime.Value;
                if (elapsed.TotalSeconds >= 1.0)
                {
                    SetClickThrough(false);
                    ResizeHandle.Visibility = Visibility.Visible;
                    _isResizeHandleVisible = true;
                }
            }
        }
        else
        {
            _ctrlShiftAltPressedTime = null;
            if (_isResizeHandleVisible && !ResizeHandle.IsMouseCaptured)
            {
                ResizeHandle.Visibility = Visibility.Collapsed;
                _isResizeHandleVisible = false;
                if (!_isDragHandleVisible)
                    SetClickThrough(true);
            }
        }

        // Handle Ctrl+Shift (no Alt) → drag mode
        if (isCtrlShiftOnlyPressed)
        {
            if (_ctrlShiftPressedTime == null)
            {
                _ctrlShiftPressedTime = DateTime.Now;
            }
            else if (!_isDragHandleVisible)
            {
                var elapsed = DateTime.Now - _ctrlShiftPressedTime.Value;
                if (elapsed.TotalSeconds >= 1.0)
                {
                    SetClickThrough(false);
                    DragHandle.Visibility = Visibility.Visible;
                    InteractiveOverlay.Visibility = Visibility.Visible;
                    _isDragHandleVisible = true;
                }
            }
        }
        else
        {
            _ctrlShiftPressedTime = null;
            if (_isDragHandleVisible)
            {
                DragHandle.Visibility = Visibility.Collapsed;
                InteractiveOverlay.Visibility = Visibility.Collapsed;
                _isDragHandleVisible = false;
                if (!_isResizeHandleVisible)
                    SetClickThrough(true);
            }
        }
    }

    private void DragHandle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left && _isDragHandleVisible)
        {
            e.Handled = true;
            try
            {
                DragMove();
                _viewModel.SavePosition(Left, Top);
            }
            catch
            {
                // Ignore exceptions during drag
            }
        }
    }

    private void ResizeHandle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!_isResizeHandleVisible) return;

        _resizeStartScreenPos = PointToScreen(e.GetPosition(this));
        _resizeStartWidth = ActualWidth;
        SizeToContent = SizeToContent.Height;
        ((UIElement)sender).CaptureMouse();
        e.Handled = true;
    }

    private void ResizeHandle_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!((UIElement)sender).IsMouseCaptured) return;

        var currentScreenPos = PointToScreen(e.GetPosition(this));
        var deltaX = currentScreenPos.X - _resizeStartScreenPos.X;

        Width = Math.Max(200, _resizeStartWidth + deltaX);
    }

    private void ResizeHandle_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!((UIElement)sender).IsMouseCaptured) return;
        ((UIElement)sender).ReleaseMouseCapture();
        _viewModel.SaveSize(ActualWidth, 0);
        e.Handled = true;
    }

    private class TornadoBlob
    {
        public required Ellipse Element { get; init; }
        public double CenterX { get; init; }
        public double CenterY { get; init; }
        public double OrbitRadius { get; init; }
        public double Angle { get; set; }
        public double Speed { get; init; }
        public double Size { get; init; }
    }
}
