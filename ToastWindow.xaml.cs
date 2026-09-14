using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace TopMediaBar;

public partial class ToastWindow : Window
{
    private const double ShadowMargin = 32;
    private const int ShowDurationMs = 2500;
    private const int OpenAnimationMs = 200;
    private const int CloseAnimationMs = 150;

    private double _barWidth;
    private double _barHeight;
    private double _collapsedTop;
    private DispatcherTimer? _hideTimer;
    private bool _isShown;
    private int _trackRevision = -1;

    public ToastWindow()
    {
        InitializeComponent();
        Loaded += ToastWindow_Loaded;
        Closed += (_, _) => _hideTimer?.Stop();
    }

    private void ToastWindow_Loaded(object sender, RoutedEventArgs e)
    {
        _barWidth = SystemParameters.PrimaryScreenWidth / 5.0;
        Bar.Width = _barWidth;
        Bar.Measure(new System.Windows.Size(_barWidth, double.PositiveInfinity));
        _barHeight = Bar.DesiredSize.Height;

        Width = _barWidth + ShadowMargin * 2;
        Height = _barHeight + ShadowMargin * 2;
        Left = (SystemParameters.PrimaryScreenWidth - Width) / 2;
        Top = -2000; // keep off-screen while content settles, avoids a visible flash
        UpdateLayout();

        _collapsedTop = -(_barHeight + ShadowMargin * 2 + 20);
        Top = _collapsedTop;

        var hwnd = new WindowInteropHelper(this).Handle;
        NativeMethods.SetToolWindowNoActivate(hwnd);
    }

    public void ShowToast(int trackRevision, string title, string artist, BitmapImage? art)
    {
        _trackRevision = trackRevision;
        TitleText.Text = title;
        ArtistText.Text = artist;
        AlbumArt.Source = art;

        _hideTimer?.Stop();
        AnimateSurface(true);
        AnimateTop(0);
        _isShown = true;

        _hideTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ShowDurationMs) };
        _hideTimer.Tick += (_, _) =>
        {
            _hideTimer!.Stop();
            HideToast();
        };
        _hideTimer.Start();
    }

    public void UpdateArtwork(int trackRevision, BitmapImage? art)
    {
        // Chrome can publish its icon first, then the cover in a later metadata event.
        // Refresh only the visible track, without reopening the toast or extending its timer.
        if (_isShown && _trackRevision == trackRevision && art != null)
            AlbumArt.Source = art;
    }

    public void HideToast()
    {
        if (!_isShown) return;
        _isShown = false;
        _hideTimer?.Stop();
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
}
