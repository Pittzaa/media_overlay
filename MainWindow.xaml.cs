using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using System.Windows.Media;
using System.Windows.Threading;

namespace TopMediaBar;

public partial class MainWindow : Window
{
    // Segoe MDL2 Assets glyph codepoints for the transport icons.
    // Built from a plain hex literal at runtime (not a literal PUA character in source) —
    // embedding the raw invisible glyph character here has proven unreliable to author correctly.
    private static readonly string GlyphPlay = ((char)0xE768).ToString();
    private static readonly string GlyphPause = ((char)0xE769).ToString();

    private const double ShadowMargin = 32;

    // How close the cursor must get to the very top edge to reveal the bar.
    private const double RevealThresholdDip = 4;
    // Once expanded, how far below/beside the card the cursor can wander before hiding starts.
    private const double HideMarginDip = 32;
    private const int HideDelayMs = 500;
    private const int PollIntervalMs = 40;
    private const int OpenAnimationMs = 200;
    private const int CloseAnimationMs = 150;

    private readonly MediaSessionService _media = new();
    private readonly VolumeService _volume = new();
    private readonly ToastWindow _toast = new();

    private DispatcherTimer? _pollTimer;
    private DispatcherTimer? _positionTimer;
    private double _barWidth;
    private double _barScreenLeft;
    private double _barHeight;
    private double _collapsedTop;
    private bool _isExpanded;
    private DateTime _lastInsideZone = DateTime.MinValue;
    private bool _isDraggingProgress;
    private bool _suppressVolumeEvent = true;
    private bool _suppressProgressEvent = true;
    private bool _isSeeking;
    private bool _transportPending;
    private int _dragRevision;
    private int _displayRevision = -1;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += MainWindow_Loaded;
        Closed += (_, _) =>
        {
            _pollTimer?.Stop();
            _positionTimer?.Stop();
            _media.Dispose();
            _toast.Close();
        };
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        _barWidth = Math.Clamp(SystemParameters.PrimaryScreenWidth / 5.0, 300, 420);
        Bar.Width = _barWidth;
        Bar.Measure(new System.Windows.Size(_barWidth, double.PositiveInfinity));
        _barHeight = Bar.DesiredSize.Height;

        Width = _barWidth + ShadowMargin * 2;
        Height = _barHeight + ShadowMargin * 2;
        Left = (SystemParameters.PrimaryScreenWidth - Width) / 2;
        Top = -2000; // keep off-screen while content settles, avoids a visible flash
        UpdateLayout();
        _barScreenLeft = Left + ShadowMargin;
        _collapsedTop = -(_barHeight + ShadowMargin * 2 + 20);
        Top = _collapsedTop;

        var hwnd = new WindowInteropHelper(this).Handle;
        NativeMethods.SetToolWindowNoActivate(hwnd);

        _toast.Show();

        _media.StateChanged += UpdateMediaUI;
        _media.TrackChanged += OnTrackChanged;
        try
        {
            await _media.InitializeAsync();
        }
        catch
        {
            TitleText.Text = "ไม่พบระบบ Media Controls ของ Windows";
        }

        _positionTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _positionTimer.Tick += (_, _) =>
        {
            UpdateProgressDisplay();
            UpdateVolumeUI();
        };
        _positionTimer.Start();

        UpdateVolumeUI();

        UpdateMediaUI();

        _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(PollIntervalMs) };
        _pollTimer.Tick += (_, _) => PollCursor();
        _pollTimer.Start();
    }

    /// <summary>
    /// Reveals/hides the bar by polling the real screen cursor position, rather than relying
    /// on WPF's MouseEnter/MouseLeave hit-testing on an animating, screen-edge-anchored window
    /// (which is what caused the flicker/bounce — the window's own bounds moving under a
    /// stationary cursor near a screen edge is a known source of spurious enter/leave events).
    /// </summary>
    private void PollCursor()
    {
        if (IsMouseCaptureWithin || _isDraggingProgress || _isSeeking)
        {
            _lastInsideZone = DateTime.Now;
            return;
        }
        var source = PresentationSource.FromVisual(this);
        if (source?.CompositionTarget == null) return;

        var (px, py) = NativeMethods.GetCursorScreenPos();
        var dip = source.CompositionTarget.TransformFromDevice.Transform(new System.Windows.Point(px, py));

        double thresholdY = _isExpanded ? _barHeight + ShadowMargin * 2 + HideMarginDip : RevealThresholdDip;
        double marginX = _isExpanded ? HideMarginDip : 0;
        bool insideX = dip.X >= _barScreenLeft - marginX && dip.X <= _barScreenLeft + _barWidth + marginX;
        bool insideZone = insideX && dip.Y >= 0 && dip.Y <= thresholdY;

        if (insideZone)
        {
            _lastInsideZone = DateTime.Now;
            Expand();
        }
        else if (_isExpanded && (DateTime.Now - _lastInsideZone).TotalMilliseconds > HideDelayMs)
        {
            Collapse();
        }
    }

    private void Expand()
    {
        if (_isExpanded) return;
        _isExpanded = true;
        _toast.HideToast();
        AnimateSurface(true);
        AnimateTop(0);
    }

    /// <summary>Flashes a brief "now playing" toast on track change, unless the full card is already visible.</summary>
    private void OnTrackChanged()
    {
        if (_isExpanded || !_media.HasSession) return;
        _toast.ShowToast(_media.Revision, _media.Title, _media.Artist, _media.Thumbnail);
    }

    private void Collapse()
    {
        if (!_isExpanded) return;
        _isExpanded = false;
        AnimateSurface(false);
        AnimateTop(_collapsedTop);
    }

    private void AnimateSurface(bool show)
    {
        var duration = TimeSpan.FromMilliseconds(show ? OpenAnimationMs : CloseAnimationMs);
        var ease = new CubicEase { EasingMode = show ? EasingMode.EaseOut : EasingMode.EaseIn };
        PopupSurface.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(show ? 1 : 0, duration) { EasingFunction = ease });
        var scale = (ScaleTransform)PopupSurface.RenderTransform;
        scale.BeginAnimation(ScaleTransform.ScaleYProperty,
            new DoubleAnimation(show ? 1 : 0.97, duration) { EasingFunction = ease });
    }

    private void AnimateTop(double target)
    {
        var anim = new DoubleAnimation(target, TimeSpan.FromMilliseconds(target == 0 ? OpenAnimationMs : CloseAnimationMs))
        {
            EasingFunction = new CubicEase { EasingMode = target == 0 ? EasingMode.EaseOut : EasingMode.EaseIn }
        };
        BeginAnimation(TopProperty, anim);
    }

    private void UpdateMediaUI()
    {
        if (_displayRevision != _media.Revision)
        {
            _displayRevision = _media.Revision;
            _toast.HideToast();
            _isDraggingProgress = false;
            if (ProgressSlider.IsMouseCaptureWithin) System.Windows.Input.Mouse.Capture(null);
        }
        if (!_media.HasSession)
        {
            TitleText.Text = "ไม่มีสื่อกำลังเล่น";
            ArtistText.Text = "";
            AlbumArt.Source = null;
            PlayPauseIcon.Text = GlyphPlay;
            ProgressSlider.IsEnabled = false;
            SetProgressValue(0);
            ElapsedText.Text = "0:00";
            DurationText.Text = "0:00";
            PrevButton.IsEnabled = false;
            NextButton.IsEnabled = false;
            PlayPauseButton.IsEnabled = false;
            return;
        }

        PlayPauseButton.IsEnabled = _media.CanTogglePlayPause && !_transportPending;
        ProgressSlider.IsEnabled = _media.CanSeek && !_isSeeking && !_transportPending;
        ProgressSlider.ToolTip = _media.CanSeek ? "เลื่อนตำแหน่งการเล่น" : "แอปนี้ยังไม่รองรับการเลื่อนตำแหน่ง";
        TitleText.Text = string.IsNullOrWhiteSpace(_media.Title) ? "ไม่ทราบชื่อเพลง" : _media.Title;
        ArtistText.Text = _media.Artist;
        AlbumArt.Source = _media.Thumbnail;
        _toast.UpdateArtwork(_media.Revision, _media.Thumbnail);
        PlayPauseIcon.Text = _media.IsPlaying ? GlyphPause : GlyphPlay;
        PrevButton.IsEnabled = _media.CanSkipPrevious && !_transportPending;
        NextButton.IsEnabled = _media.CanSkipNext && !_transportPending;
        DurationText.Text = FormatTime(_media.Duration);
        UpdateProgressDisplay();
    }

    private void SetProgressValue(double value)
    {
        _suppressProgressEvent = true;
        try { ProgressSlider.Value = value; }
        finally { _suppressProgressEvent = false; }
    }

    private void UpdateProgressDisplay()
    {
        if (_isDraggingProgress || _isSeeking || !_media.HasSession) return;
        var pos = _media.GetEstimatedPosition();
        ElapsedText.Text = FormatTime(pos);
        SetProgressValue(_media.Duration.TotalSeconds > 0
            ? pos.TotalSeconds / _media.Duration.TotalSeconds * 100 : 0);
    }

    private static string FormatTime(TimeSpan t) =>
        t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");

    private async Task RunTransportAsync(Func<Task<bool>> command)
    {
        if (_transportPending) return;
        _transportPending = true;
        UpdateMediaUI();
        try
        {
            if (!await command()) TitleText.ToolTip = "แอปเล่นสื่อไม่รับคำสั่ง กรุณาลองอีกครั้ง";
            else TitleText.ToolTip = null;
        }
        finally
        {
            _transportPending = false;
            UpdateMediaUI();
        }
    }

    private async void PlayPauseButton_Click(object sender, RoutedEventArgs e) => await RunTransportAsync(_media.TogglePlayPauseAsync);
    private async void NextButton_Click(object sender, RoutedEventArgs e) => await RunTransportAsync(_media.SkipNextAsync);
    private async void PrevButton_Click(object sender, RoutedEventArgs e) => await RunTransportAsync(_media.SkipPreviousAsync);

    private void ProgressSlider_DragStarted(object sender, DragStartedEventArgs e)
    {
        _isDraggingProgress = true;
        _dragRevision = _media.Revision;
    }

    private async void ProgressSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_suppressProgressEvent || !_media.CanSeek) return;
        ElapsedText.Text = FormatTime(TimeSpan.FromSeconds(e.NewValue / 100 * _media.Duration.TotalSeconds));
        // Dragging previews the time; track clicks and keyboard changes commit immediately.
        if (!_isDraggingProgress) await SeekProgressAsync(_media.Revision);
    }

    private async void ProgressSlider_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        if (!_isDraggingProgress) return;
        _isDraggingProgress = false;
        if (!e.Canceled) await SeekProgressAsync(_dragRevision);
        else UpdateProgressDisplay();
    }

    private async Task SeekProgressAsync(int revision)
    {
        if (_isSeeking || !_media.CanSeek || revision != _media.Revision) return;
        var target = TimeSpan.FromSeconds(ProgressSlider.Value / 100 * _media.Duration.TotalSeconds);
        _isSeeking = true;
        UpdateMediaUI();
        try
        {
            var accepted = await _media.SeekToAsync(target, revision);
            TitleText.ToolTip = accepted ? null : "แอปเล่นสื่อไม่รับคำสั่งเลื่อนตำแหน่ง";
        }
        finally
        {
            _isSeeking = false;
            UpdateMediaUI();
        }
    }

    private void UpdateVolumeUI()
    {
        // Do not fight the user's thumb with the periodic system-volume refresh.
        if (VolumeSlider.IsMouseCaptureWithin) return;
        var available = _volume.TryGetState(_media.SourceAppUserModelId, out var volume, out var muted);
        VolumeSlider.IsEnabled = MuteButton.IsEnabled = available;
        _suppressVolumeEvent = true;
        try { VolumeSlider.Value = volume * 100; }
        finally { _suppressVolumeEvent = false; }
        VolumeIcon.Text = ((char)(muted || volume == 0 ? 0xE74F : 0xE995)).ToString();
        VolumeSlider.ToolTip = available ? $"ระดับเสียงแท็บ {volume:P0}" : "ไม่พบแท็บเสียงที่ควบคุมได้";
    }

    private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_suppressVolumeEvent) return;
        if (_volume.SetVolume(_media.SourceAppUserModelId, (float)(e.NewValue / 100)))
            VolumeIcon.Text = ((char)(e.NewValue == 0 ? 0xE74F : 0xE995)).ToString();
        else UpdateVolumeUI();
    }

    private void MuteButton_Click(object sender, RoutedEventArgs e)
    {
        _volume.ToggleMute(_media.SourceAppUserModelId);
        UpdateVolumeUI();
    }
}
