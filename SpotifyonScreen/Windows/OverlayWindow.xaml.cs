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
    private const int GWLP_HWNDPARENT = -8;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_LAYERED = 0x00080000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
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

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetWindowLong", SetLastError = true)]
    private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

    // GWLP_HWNDPARENT holds a pointer-sized handle, so on 64-bit processes it must go through
    // SetWindowLongPtr — the plain SetWindowLong above would truncate the handle to 32 bits.
    private static IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong) =>
        Environment.Is64BitProcess
            ? SetWindowLongPtr64(hWnd, nIndex, dwNewLong)
            : new IntPtr(SetWindowLong32(hWnd, nIndex, dwNewLong.ToInt32()));

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowEx(
        int dwExStyle, string lpClassName, string lpWindowName, uint dwStyle,
        int x, int y, int nWidth, int nHeight,
        IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr hWnd);

    // Invisible top-level "STATIC" window used purely as an Owner anchor: an owned window
    // gets no taskbar button / Alt+Tab entry without needing WS_EX_TOOLWINDOW — which matters
    // because OBS's window-capture enumeration explicitly skips any WS_EX_TOOLWINDOW window.
    private IntPtr _hiddenOwnerHwnd;

    private readonly OverlayViewModel _viewModel;
    private System.Threading.Timer? _keyboardTimer;
    private DispatcherTimer? _tornadoTimer;
    private DateTime? _ctrlShiftPressedTime;
    private DateTime? _ctrlShiftAltPressedTime;
    // volatile so the background keyboard thread can read them safely
    private volatile bool _isDragHandleVisible;
    private volatile bool _isResizeHandleVisible;
    private System.Windows.Point _resizeStartScreenPos;
    private double _resizeStartWidth;
    private IntPtr _hwnd;

    private bool _tornadoTransitioning;
    private DateTime _lastTornadoTick = DateTime.Now;

    // One tornado "layer" for the classic full-overlay swirl, plus one per Boxed-style chip —
    // each chip gets its own independent swirl clipped to its own rounded bounds.
    private TornadoLayer? _mainLayer;
    private TornadoLayer? _chip1Layer;
    private TornadoLayer? _chip2Layer;

    // Persistent brushes so we can animate their Color property smoothly
    private readonly SolidColorBrush _dynamicBaseBrush = new(Color.FromArgb(210, 6, 6, 9));
    private readonly SolidColorBrush _dynamicOverlayBrush = new(Color.FromArgb(90, 5, 5, 7));
    private readonly SolidColorBrush _chip1BaseBrush = new(Color.FromArgb(210, 6, 6, 9));
    private readonly SolidColorBrush _chip1OverlayBrush = new(Color.FromArgb(90, 5, 5, 7));
    private readonly SolidColorBrush _chip2BaseBrush = new(Color.FromArgb(210, 6, 6, 9));
    private readonly SolidColorBrush _chip2OverlayBrush = new(Color.FromArgb(90, 5, 5, 7));

    // Mutable inner brush for the boxed style's dotted progress fill, kept in sync with ProgressBarBrush
    private readonly SolidColorBrush _dotFillInnerBrush = new(Color.FromRgb(29, 185, 84));
    private DispatcherTimer? _timeLabelTimer;
    private double _dottedTrackWidth;

    public OverlayWindow(OverlayViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = _viewModel;

        _mainLayer = new TornadoLayer
        {
            Canvas = TornadoCanvas, DynamicGrid = DynamicBgGrid,
            BaseBrush = _dynamicBaseBrush, OverlayBrush = _dynamicOverlayBrush, SizeSource = ClippedContainer
        };
        _chip1Layer = new TornadoLayer
        {
            Canvas = Chip1TornadoCanvas, DynamicGrid = Chip1DynamicGrid,
            BaseBrush = _chip1BaseBrush, OverlayBrush = _chip1OverlayBrush, SizeSource = Chip1Border
        };
        _chip2Layer = new TornadoLayer
        {
            Canvas = Chip2TornadoCanvas, DynamicGrid = Chip2DynamicGrid,
            BaseBrush = _chip2BaseBrush, OverlayBrush = _chip2OverlayBrush, SizeSource = Chip2Border
        };
        DynamicBaseBorder.Background = _dynamicBaseBrush;
        DynamicDarkOverlay.Background = _dynamicOverlayBrush;
        Chip1DynamicBase.Background = _chip1BaseBrush;
        Chip1DynamicOverlay.Background = _chip1OverlayBrush;
        Chip2DynamicBase.Background = _chip2BaseBrush;
        Chip2DynamicOverlay.Background = _chip2OverlayBrush;

        SourceInitialized += OnSourceInitialized;

        // Run keyboard polling on a thread-pool thread so the UI dispatcher is
        // never woken unless Ctrl+Shift is actually held or a handle needs dismissing.
        _keyboardTimer = new System.Threading.Timer(KeyboardPoll, null, 50, 50);

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
            else
                // No animation needed here — just start the flat box in the right state
                // (hidden if Boxed style is on, since the chips carry their own background).
                StaticBgBorder.Visibility = _viewModel.BoxedStyle ? Visibility.Collapsed : Visibility.Visible;

            InitializeDottedProgressBrushes();
            _timeLabelTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
            _timeLabelTimer.Tick += (_, _) => UpdateElapsedTimeLabel();
            if (_viewModel.BoxedStyle)
                _timeLabelTimer.Start();
        };
    }

    private void InitializeDottedProgressBrushes()
    {
        if (_viewModel.ProgressBarBrush is SolidColorBrush initial)
            _dotFillInnerBrush.Color = initial.Color;

        DottedTrackBg.Background = BuildDotBrush(new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)), freeze: true);
        DottedProgressFill.Background = BuildDotBrush(_dotFillInnerBrush, freeze: false);
    }

    private static DrawingBrush BuildDotBrush(System.Windows.Media.Brush dotBrush, bool freeze)
    {
        var geometry = new EllipseGeometry(new System.Windows.Point(1.5, 2.5), 1.4, 1.4);
        geometry.Freeze();
        var drawing = new GeometryDrawing(dotBrush, null, geometry);
        var brush = new DrawingBrush(drawing)
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, 6, 5),
            ViewportUnits = BrushMappingMode.Absolute,
            Stretch = Stretch.None
        };
        if (freeze) brush.Freeze();
        return brush;
    }

    private void UpdateElapsedTimeLabel()
    {
        if (!_viewModel.HasTrack) return;
        _viewModel.ElapsedTimeText = OverlayViewModel.FormatTime(_viewModel.GetEstimatedElapsedMs());
    }

    // The dotted fill can't use a RenderTransform scale (it would squash the dots into
    // ellipses) so it's a real Width instead. That means, unlike the classic bar, we need
    // to know the track's actual pixel width to convert a 0..1 fraction into a Width value.
    private void DottedProgressTrack_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        _dottedTrackWidth = e.NewSize.Width;
        DottedProgressFill.BeginAnimation(FrameworkElement.WidthProperty, null);
        DottedProgressFill.Width = _dottedTrackWidth * _viewModel.ProgressPercent;
    }

    // Called from XAML SizeChanged on ClippedContainer
    private void ClippedContainer_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateClip();
    }

    // A Border's CornerRadius only clips its own Background/BorderBrush, not arbitrary child
    // content — the per-chip swirl needs an explicit rounded clip to match the chip's shape.
    private void ChipBorder_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        double w = e.NewSize.Width;
        double h = e.NewSize.Height;
        if (w <= 0 || h <= 0) return;

        var clip = new RectangleGeometry(new Rect(0, 0, w, h), 8, 8);
        if (ReferenceEquals(sender, Chip1Border))
            Chip1DynamicGrid.Clip = clip;
        else if (ReferenceEquals(sender, Chip2Border))
            Chip2DynamicGrid.Clip = clip;
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
            case nameof(OverlayViewModel.AlbumArt):
                AlbumArtBorder.BeginAnimation(UIElement.OpacityProperty,
                    new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(400)));
                break;
            case nameof(OverlayViewModel.AlbumColors):
                var colors = _viewModel.AlbumColors;
                Dispatcher.InvokeAsync(() => UpdateTornadoColors(colors), System.Windows.Threading.DispatcherPriority.Background);
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
                var progress = _viewModel.ProgressPercent;
                Dispatcher.InvokeAsync(() => OnProgressUpdated(progress), System.Windows.Threading.DispatcherPriority.Background);
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
            case nameof(OverlayViewModel.ProgressBarBrush):
                if (_viewModel.ProgressBarBrush is SolidColorBrush scb)
                    _dotFillInnerBrush.Color = scb.Color;
                break;
            case nameof(OverlayViewModel.BoxedStyle):
                if (_viewModel.BoxedStyle)
                    _timeLabelTimer?.Start();
                else
                    _timeLabelTimer?.Stop();
                // Re-evaluate whether the flat background box or the swirl should show,
                // since Boxed style changes which one is allowed to render.
                ToggleDynamicBackground(_viewModel.DynamicBackground);
                break;
            case nameof(OverlayViewModel.AllowObsCapture):
                ApplyObsCaptureMode(_viewModel.AllowObsCapture);
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
            SetProgressScale(target);
            _animationRunning = false;
            return;
        }

        if (isNewTrack || isSeek || !_animationRunning)
        {
            // Start fresh animation from polled position to end of song
            _animatingTrack = trackId;
            var remainingMs = duration * (1.0 - target);
            AnimateProgressScale(target, 1.0, remainingMs);
            _animationRunning = true;
            return;
        }

        // Regular poll, same track, animation already running — do nothing
    }

    // The classic bar's RenderTransform and the boxed style's dotted-fill Width are
    // driven off the same target/duration so they stay in lockstep.
    private void SetProgressScale(double value)
    {
        ProgressScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        ProgressScale.ScaleX = value;

        DottedProgressFill.BeginAnimation(FrameworkElement.WidthProperty, null);
        DottedProgressFill.Width = _dottedTrackWidth * value;
    }

    private void AnimateProgressScale(double from, double to, double durationMs)
    {
        ProgressScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        DottedProgressFill.BeginAnimation(FrameworkElement.WidthProperty, null);

        var duration = TimeSpan.FromMilliseconds(durationMs);

        var scaleAnim = new DoubleAnimation { From = from, To = to, Duration = duration };
        // Cap render rate: 15fps is imperceptible for a slow progress bar,
        // but cuts per-frame DWM compositing from 60/s down to 15/s.
        Timeline.SetDesiredFrameRate(scaleAnim, 15);
        ProgressScale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnim);

        var widthAnim = new DoubleAnimation
        {
            From = _dottedTrackWidth * from,
            To = _dottedTrackWidth * to,
            Duration = duration
        };
        Timeline.SetDesiredFrameRate(widthAnim, 15);
        DottedProgressFill.BeginAnimation(FrameworkElement.WidthProperty, widthAnim);
    }

    private void ToggleDynamicBackground(bool enabled)
    {
        // Reset any in-flight tornado fade so the flag doesn't get stuck
        _tornadoTransitioning = false;
        bool boxed = _viewModel.BoxedStyle;

        if (enabled)
        {
            EnsureTornadoTimerRunning();

            if (boxed)
            {
                // Boxed style never shows the overall box — the swirl lives inside the chips instead.
                StaticBgBorder.BeginAnimation(UIElement.OpacityProperty, null);
                StaticBgBorder.Visibility = Visibility.Collapsed;
                DynamicBgGrid.BeginAnimation(UIElement.OpacityProperty, null);
                DynamicBgGrid.Visibility = Visibility.Collapsed;

                ShowChipLayer(_chip1Layer!);
                ShowChipLayer(_chip2Layer!);
            }
            else
            {
                HideChipLayerImmediate(_chip1Layer!);
                HideChipLayerImmediate(_chip2Layer!);

                // Build blobs immediately so they're ready when the grid fades in
                if (_viewModel.AlbumColors.Count > 0)
                    RebuildTornadoBlobs(_mainLayer!, _viewModel.AlbumColors);

                // Fade: static out, dynamic in
                DynamicBgGrid.Opacity = 0;
                DynamicBgGrid.Visibility = Visibility.Visible;

                var fadeInDynamic = new DoubleAnimation(0.0, _viewModel.BackgroundOpacity, TimeSpan.FromMilliseconds(400));
                fadeInDynamic.Completed += (_, _) =>
                {
                    StaticBgBorder.BeginAnimation(UIElement.OpacityProperty, null);
                    StaticBgBorder.Opacity = 1;
                    StaticBgBorder.Visibility = Visibility.Collapsed;
                    // Anchor the opacity as a base value so BackgroundOpacity changes work normally
                    DynamicBgGrid.BeginAnimation(UIElement.OpacityProperty, null);
                    DynamicBgGrid.Opacity = _viewModel.BackgroundOpacity;
                };

                StaticBgBorder.BeginAnimation(UIElement.OpacityProperty,
                    new DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(400)));
                DynamicBgGrid.BeginAnimation(UIElement.OpacityProperty, fadeInDynamic);
            }
        }
        else if (boxed)
        {
            HideChipLayer(_chip1Layer!);
            HideChipLayer(_chip2Layer!);
            _tornadoTimer?.Stop();
        }
        else
        {
            HideChipLayerImmediate(_chip1Layer!);
            HideChipLayerImmediate(_chip2Layer!);

            // Fade: dynamic out, static in
            StaticBgBorder.Opacity = 0;
            StaticBgBorder.Visibility = Visibility.Visible;

            var fadeInStatic = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(400));
            fadeInStatic.Completed += (_, _) =>
            {
                DynamicBgGrid.BeginAnimation(UIElement.OpacityProperty, null);
                DynamicBgGrid.Opacity = _viewModel.BackgroundOpacity;
                DynamicBgGrid.Visibility = Visibility.Collapsed;
                StaticBgBorder.BeginAnimation(UIElement.OpacityProperty, null);
                StaticBgBorder.Opacity = 1;
                _tornadoTimer?.Stop();
            };

            DynamicBgGrid.BeginAnimation(UIElement.OpacityProperty,
                new DoubleAnimation(DynamicBgGrid.Opacity, 0.0, TimeSpan.FromMilliseconds(400)));
            StaticBgBorder.BeginAnimation(UIElement.OpacityProperty, fadeInStatic);
        }
    }

    private void EnsureTornadoTimerRunning()
    {
        if (_tornadoTimer == null)
        {
            // Render priority keeps up with WPF's own render cycle at 30fps
            _tornadoTimer = new DispatcherTimer(DispatcherPriority.Render)
            {
                Interval = TimeSpan.FromMilliseconds(33)
            };
            _tornadoTimer.Tick += TornadoTimer_Tick;
        }
        _lastTornadoTick = DateTime.Now;
        _tornadoTimer.Start();
    }

    private void ShowChipLayer(TornadoLayer layer)
    {
        if (_viewModel.AlbumColors.Count > 0 && layer.Blobs.Count == 0)
            RebuildTornadoBlobs(layer, _viewModel.AlbumColors);

        layer.DynamicGrid.BeginAnimation(UIElement.OpacityProperty, null);
        layer.DynamicGrid.Opacity = 0;
        layer.DynamicGrid.Visibility = Visibility.Visible;
        layer.DynamicGrid.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(400)));
    }

    private static void HideChipLayer(TornadoLayer layer)
    {
        var fadeOut = new DoubleAnimation(layer.DynamicGrid.Opacity, 0.0, TimeSpan.FromMilliseconds(400));
        fadeOut.Completed += (_, _) =>
        {
            layer.DynamicGrid.BeginAnimation(UIElement.OpacityProperty, null);
            layer.DynamicGrid.Opacity = 1;
            layer.DynamicGrid.Visibility = Visibility.Collapsed;
        };
        layer.DynamicGrid.BeginAnimation(UIElement.OpacityProperty, fadeOut);
    }

    private static void HideChipLayerImmediate(TornadoLayer layer)
    {
        layer.DynamicGrid.BeginAnimation(UIElement.OpacityProperty, null);
        layer.DynamicGrid.Opacity = 1;
        layer.DynamicGrid.Visibility = Visibility.Collapsed;
    }

    // Crossfades old blobs out and new blobs in for whichever layer(s) are currently active
    // (the main full-overlay swirl in classic mode, or both chips in Boxed style).
    private void UpdateTornadoColors(List<Color> colors)
    {
        if (!_viewModel.DynamicBackground || colors.Count == 0) return;
        if (_tornadoTransitioning) return;

        _tornadoTransitioning = true;

        if (_viewModel.BoxedStyle)
        {
            int pending = 2;
            void OnLayerDone() { if (--pending == 0) _tornadoTransitioning = false; }
            CrossfadeTornadoLayer(_chip1Layer!, colors, OnLayerDone);
            CrossfadeTornadoLayer(_chip2Layer!, colors, OnLayerDone);
        }
        else
        {
            CrossfadeTornadoLayer(_mainLayer!, colors, () => _tornadoTransitioning = false);
        }
    }

    private void CrossfadeTornadoLayer(TornadoLayer layer, List<Color> colors, Action onComplete)
    {
        var duration = TimeSpan.FromMilliseconds(700);

        // Animate the base background color in place (no grid flicker)
        var dominant = colors[0];
        layer.BaseBrush.BeginAnimation(SolidColorBrush.ColorProperty,
            new ColorAnimation(
                Color.FromArgb(210, (byte)(dominant.R * 0.2), (byte)(dominant.G * 0.2), (byte)(dominant.B * 0.2)),
                duration));
        layer.OverlayBrush.BeginAnimation(SolidColorBrush.ColorProperty,
            new ColorAnimation(
                Color.FromArgb(90, (byte)(dominant.R * 0.15), (byte)(dominant.G * 0.15), (byte)(dominant.B * 0.15)),
                duration));

        // Fade out current blobs (they keep orbiting while fading)
        var oldBlobs = new List<TornadoBlob>(layer.Blobs);
        foreach (var blob in oldBlobs)
        {
            var startOp = blob.Element.Opacity;
            var fadeOut = new DoubleAnimation(startOp, 0.0, duration);
            fadeOut.Completed += (_, _) =>
            {
                layer.Canvas.Children.Remove(blob.Element);
                layer.Blobs.Remove(blob);
            };
            blob.Element.BeginAnimation(UIElement.OpacityProperty, fadeOut);
        }

        // Create new blobs at opacity 0, add to canvas and layer.Blobs so the timer moves them too
        var newBlobs = CreateBlobs(colors, layer.SizeSource.ActualWidth, layer.SizeSource.ActualHeight);
        int remaining = newBlobs.Count > 0 ? newBlobs.Count : 1;
        foreach (var blob in newBlobs)
        {
            blob.Element.Opacity = 0;
            layer.Canvas.Children.Add(blob.Element);
            layer.Blobs.Add(blob);

            var target = blob.TargetOpacity;
            var fadeIn = new DoubleAnimation(0.0, target, duration);
            fadeIn.Completed += (_, _) =>
            {
                blob.Element.BeginAnimation(UIElement.OpacityProperty, null);
                blob.Element.Opacity = target;
                if (--remaining == 0)
                    onComplete();
            };
            blob.Element.BeginAnimation(UIElement.OpacityProperty, fadeIn);
        }
        if (newBlobs.Count == 0) onComplete();

        UpdateBlobPositions(layer.Blobs);
    }

    // Used by ToggleDynamicBackground — instant build, no crossfade needed (grid is already fading in).
    private void RebuildTornadoBlobs(TornadoLayer layer, List<Color> colors)
    {
        layer.Canvas.Children.Clear();
        layer.Blobs.Clear();
        if (colors.Count == 0) return;

        var dominant = colors[0];
        layer.BaseBrush.BeginAnimation(SolidColorBrush.ColorProperty, null);
        layer.BaseBrush.Color = Color.FromArgb(210,
            (byte)(dominant.R * 0.2), (byte)(dominant.G * 0.2), (byte)(dominant.B * 0.2));
        layer.OverlayBrush.BeginAnimation(SolidColorBrush.ColorProperty, null);
        layer.OverlayBrush.Color = Color.FromArgb(90,
            (byte)(dominant.R * 0.15), (byte)(dominant.G * 0.15), (byte)(dominant.B * 0.15));

        var newBlobs = CreateBlobs(colors, layer.SizeSource.ActualWidth, layer.SizeSource.ActualHeight);
        foreach (var blob in newBlobs)
        {
            layer.Canvas.Children.Add(blob.Element);
            layer.Blobs.Add(blob);
        }

        UpdateBlobPositions(layer.Blobs);
    }

    private List<TornadoBlob> CreateBlobs(List<Color> colors, double w, double h)
    {
        var result = new List<TornadoBlob>();
        if (w <= 0) w = 380;
        if (h <= 0) h = 100;
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
                double targetOpacity = 0.55 + random.NextDouble() * 0.35;

                var ellipse = new Ellipse
                {
                    Width = blobSize,
                    Height = blobSize,
                    Opacity = targetOpacity,
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

                result.Add(new TornadoBlob
                {
                    Element = ellipse,
                    CenterX = cx,
                    CenterY = cy,
                    OrbitRadius = orbitRadius,
                    Angle = startAngle,
                    Speed = speed,
                    Size = blobSize,
                    TargetOpacity = targetOpacity
                });
            }
        }

        return result;
    }

    private void TornadoTimer_Tick(object? sender, EventArgs e)
    {
        var now = DateTime.Now;
        double dt = (now - _lastTornadoTick).TotalSeconds;
        _lastTornadoTick = now;

        // Cap dt so a stall (e.g. from a transition fade) doesn't cause a jump
        dt = Math.Min(dt, 0.1);

        AdvanceLayer(_mainLayer, dt);
        AdvanceLayer(_chip1Layer, dt);
        AdvanceLayer(_chip2Layer, dt);
    }

    private static void AdvanceLayer(TornadoLayer? layer, double dt)
    {
        if (layer == null || layer.Blobs.Count == 0) return;
        foreach (var blob in layer.Blobs)
            blob.Angle += blob.Speed * 1.25 * dt;
        UpdateBlobPositions(layer.Blobs);
    }

    private static void UpdateBlobPositions(List<TornadoBlob> blobs)
    {
        foreach (var blob in blobs)
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
        ApplyObsCaptureMode(_viewModel.AllowObsCapture);
    }

    // OBS's window-capture enumeration (window-helpers.c, check_window_valid) unconditionally
    // rejects any window with WS_EX_TOOLWINDOW set — so that style can never be used here.
    // To still keep the overlay out of the taskbar/Alt+Tab, give it a real (invisible) owner
    // window instead: owned top-level windows get no taskbar button on their own, and OBS's
    // enumeration doesn't check for an owner at all, so the window stays capturable.
    private void ApplyObsCaptureMode(bool enabled)
    {
        if (_hwnd == IntPtr.Zero) return;

        if (enabled)
        {
            if (_hiddenOwnerHwnd == IntPtr.Zero)
                _hiddenOwnerHwnd = CreateWindowEx(0, "STATIC", "", 0, 0, 0, 0, 0,
                    IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);

            SetWindowLongPtr(_hwnd, GWLP_HWNDPARENT, _hiddenOwnerHwnd);
            int exStyle = GetWindowLong(_hwnd, GWL_EXSTYLE);
            SetWindowLong(_hwnd, GWL_EXSTYLE, exStyle & ~WS_EX_TOOLWINDOW);
        }
        else
        {
            // Let WPF re-establish its own default taskbar-hiding behavior.
            SetWindowLongPtr(_hwnd, GWLP_HWNDPARENT, IntPtr.Zero);
            ShowInTaskbar = true;
            ShowInTaskbar = false;
        }
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

    protected override void OnClosed(EventArgs e)
    {
        _keyboardTimer?.Dispose();
        _tornadoTimer?.Stop();
        _timeLabelTimer?.Stop();
        if (_hiddenOwnerHwnd != IntPtr.Zero)
        {
            DestroyWindow(_hiddenOwnerHwnd);
            _hiddenOwnerHwnd = IntPtr.Zero;
        }
        base.OnClosed(e);
    }

    private static bool IsKeyDown(int vKey) => (GetAsyncKeyState(vKey) & 0x8000) != 0;

    // Runs on a thread-pool thread every 50ms.
    // Only dispatches to the UI thread when Ctrl+Shift is held or a handle needs hiding.
    private void KeyboardPoll(object? state)
    {
        bool ctrl = IsKeyDown(VK_LCONTROL) || IsKeyDown(VK_RCONTROL);
        bool shift = IsKeyDown(VK_LSHIFT) || IsKeyDown(VK_RSHIFT);
        bool alt = IsKeyDown(VK_LALT) || IsKeyDown(VK_RALT);

        if ((ctrl && shift) || _isDragHandleVisible || _isResizeHandleVisible)
            Dispatcher.InvokeAsync(() => ProcessKeyboardState(ctrl, shift, alt), DispatcherPriority.Input);
    }

    private void ProcessKeyboardState(bool isCtrlPressed, bool isShiftPressed, bool isAltPressed)
    {
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
        public double TargetOpacity { get; init; }
    }

    // Bundles everything one independent swirl instance needs: its own canvas, blob list,
    // mutable base/overlay tint brushes, and the element whose size the blobs are scaled to.
    private class TornadoLayer
    {
        public required Canvas Canvas { get; init; }
        public required Grid DynamicGrid { get; init; }
        public required SolidColorBrush BaseBrush { get; init; }
        public required SolidColorBrush OverlayBrush { get; init; }
        public required FrameworkElement SizeSource { get; init; }
        public List<TornadoBlob> Blobs { get; } = [];
    }
}
