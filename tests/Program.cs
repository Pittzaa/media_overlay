using System.Reflection;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage;
using TopMediaBar;

internal static class Program
{
    private static int _checks;

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            TimelineTests();
            LayoutTests();
            ToastArtworkTests();
            if (args.Contains("--audio")) AudioTests();
            if (args.Contains("--media") || args.Contains("--artwork"))
            {
                var dispatcher = Dispatcher.CurrentDispatcher;
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
                var task = args.Contains("--artwork") ? InspectArtworkAsync() : MediaTests();
                var frame = new DispatcherFrame();
                _ = task.ContinueWith(_ => dispatcher.BeginInvoke(() => frame.Continue = false));
                Dispatcher.PushFrame(frame);
                task.GetAwaiter().GetResult();
            }
            Console.WriteLine($"PASS: {_checks} regression checks");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        _checks++;
        Console.WriteLine($"PASS: {message}");
    }

    private static void At(PlaybackTimeline timeline, DateTimeOffset now, double expected, string message)
        => Check(Math.Abs(timeline.Estimate(now).TotalSeconds - expected) < 0.001, message);

    private static void TimelineTests()
    {
        var now = DateTimeOffset.UtcNow;
        var timeline = new PlaybackTimeline();
        timeline.SetPlayback(true, 1, now);
        timeline.Update(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(120), now.AddSeconds(-2), now);
        At(timeline, now, 12, "Initial timeline uses the source timestamp");
        At(timeline, now.AddSeconds(3), 15, "Playback advances between source events");
        timeline.SetPlayback(false, 1, now.AddSeconds(3));
        At(timeline, now.AddSeconds(30), 15, "Pause freezes the estimated position");
        timeline.SetPlayback(true, 1, now.AddSeconds(30));
        At(timeline, now.AddSeconds(31), 16, "Resume excludes time spent paused");
        timeline.SetPlayback(true, 2, now.AddSeconds(31));
        At(timeline, now.AddSeconds(33), 20, "Playback rate changes retain the current position");
        timeline.Seek(TimeSpan.FromSeconds(60), now.AddSeconds(33));
        At(timeline, now.AddSeconds(34), 62, "Accepted seek advances from the new position");
        At(timeline, now.AddSeconds(100), 120, "Position is clamped to duration");
        timeline.Seek(TimeSpan.FromSeconds(-10), now.AddSeconds(100));
        At(timeline, now.AddSeconds(100), 0, "Negative seek is clamped to zero");
        timeline.Update(TimeSpan.FromSeconds(90), TimeSpan.Zero, now, now);
        At(timeline, now, 0, "Unknown duration clears stale progress");
        timeline.Update(TimeSpan.Zero, TimeSpan.FromSeconds(-10), now, now);
        Check(timeline.Duration == TimeSpan.Zero, "Invalid duration cannot create a negative slider value");

        var paused = new PlaybackTimeline();
        paused.Update(TimeSpan.FromSeconds(25), TimeSpan.FromSeconds(100), now, now);
        paused.SetPlayback(true, 1, now.AddSeconds(20));
        paused.Update(TimeSpan.FromSeconds(25), TimeSpan.FromSeconds(100), now, now.AddSeconds(21));
        At(paused, now.AddSeconds(21), 26, "A stale source timestamp does not count paused time");
    }

    private static void LayoutTests()
    {
        var window = new MainWindow();
        var bar = (Border)window.FindName("Bar");
        bar.Width = 300;
        bar.Measure(new Size(300, 500));
        bar.Arrange(new Rect(0, 0, 300, bar.DesiredSize.Height));
        bar.UpdateLayout();
        var volume = (Slider)window.FindName("VolumeSlider");
        var progress = (Slider)window.FindName("ProgressSlider");
        Check(volume.ActualWidth >= 140, $"Volume track has usable width ({volume.ActualWidth:0} DIP)");
        Check(progress.ActualWidth >= 180, "Progress track fills the media column");
        Check(volume.ActualHeight >= 18, "Slider hit area contains its thumb");
        var track = (Track)volume.Template.FindName("PART_Track", volume);
        volume.Value = 75;
        Check(Math.Abs(track.Value - 75) < 0.01, "Custom track follows the slider value");
        Check(volume.IsMoveToPointEnabled && progress.IsMoveToPointEnabled, "Track clicks move directly to the pointer");

        var update = typeof(MainWindow).GetMethod("UpdateMediaUI", BindingFlags.NonPublic | BindingFlags.Instance)!;
        update.Invoke(window, null);
        Check(!progress.IsEnabled && progress.Value == 0, "No session disables seeking and resets progress");
        Check(!((Button)window.FindName("PlayPauseButton")).IsEnabled, "No session disables transport controls");
        window.Close();
    }

    private static void AudioTests()
    {
        var volume = new VolumeService();
        Check(volume.TryGetState("chrome", out var original, out var originalMuted), "Chrome audio session is available");
        try
        {
            // Lower by one percent for the write/read check, then restore in finally.
            var target = Math.Max(0, original - 0.01f);
            Check(volume.SetVolume("chrome", target), "Chrome session volume write succeeds");
            Check(volume.TryGetState("chrome", out var current, out _) && Math.Abs(current - target) < 0.002,
                "Chrome session reports the requested volume");
            Check(volume.ToggleMute("chrome"), "Chrome session mute succeeds");
            Check(volume.TryGetState("chrome", out _, out var muted), "Chrome mute state can be read back");
            Check(volume.ToggleMute("chrome"), "Chrome session unmute succeeds");
            Check(volume.TryGetState("chrome", out _, out var unmuted) && muted != unmuted, "Chrome mute toggles session state");
        }
        finally
        {
            volume.SetVolume("chrome", original);
            if (volume.TryGetState("chrome", out _, out var muted) && muted != originalMuted) volume.ToggleMute("chrome");
        }
    }

    private static void ToastArtworkTests()
    {
        var toast = new ToastWindow { Top = -1000 };
        try
        {
            var icon = new System.Windows.Media.Imaging.BitmapImage();
            var cover = new System.Windows.Media.Imaging.BitmapImage();
            var art = (Image)toast.FindName("AlbumArt");
            toast.ShowToast(1, "Track A", "Artist", icon);
            var timerField = typeof(ToastWindow).GetField("_hideTimer", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var timer = timerField.GetValue(toast);
            toast.UpdateArtwork(1, cover);
            Check(ReferenceEquals(art.Source, cover), "Late album art replaces the initial icon in the toast");
            Check(ReferenceEquals(timer, timerField.GetValue(toast)), "Artwork updates do not restart the toast lifetime");
            toast.UpdateArtwork(1, null);
            Check(ReferenceEquals(art.Source, cover), "A failed artwork refresh preserves the current cover");
            toast.ShowToast(2, "Track B", "Artist", null);
            Check(art.Source == null, "A new track clears the previous track's cover");
            toast.UpdateArtwork(1, cover);
            Check(art.Source == null, "Late artwork from the previous track is ignored");
            toast.HideToast();
            toast.UpdateArtwork(2, cover);
            Check(art.Source == null, "Late artwork does not update a hidden toast");
        }
        finally { toast.Close(); }
    }

    private static async Task Until(Func<bool> ready, string message)
    {
        var deadline = DateTime.UtcNow.AddSeconds(8);
        while (!ready() && DateTime.UtcNow < deadline) await Task.Delay(100);
        Check(ready(), message);
    }

    private static async Task InspectArtworkAsync()
    {
        var manager = await Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        var session = manager.GetCurrentSession();
        Check(session != null, "An active media session is available for artwork inspection");
        var props = await session!.TryGetMediaPropertiesAsync();
        Console.WriteLine($"Artwork source: {session.SourceAppUserModelId}");
        Check(props.Thumbnail != null, "The source supplies a thumbnail stream");
        using var stream = await props.Thumbnail!.OpenReadAsync();
        Console.WriteLine($"Thumbnail stream: {stream.Size} bytes, {stream.ContentType}");
        using var input = stream.AsStreamForRead();
        using (var output = File.Create(Path.Combine(AppContext.BaseDirectory, "source-artwork.bin")))
            await input.CopyToAsync(output);
        var load = typeof(MediaSessionService).GetMethod("LoadThumbnailAsync", BindingFlags.Static | BindingFlags.NonPublic)!;
        var image = await (Task<System.Windows.Media.Imaging.BitmapImage?>)load.Invoke(null, new object[] { props.Thumbnail })!;
        Check(image != null, "The application's thumbnail decoder loads the source image");
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(image!));
        var path = Path.Combine(AppContext.BaseDirectory, "source-artwork.png");
        using (var output = File.Create(path)) encoder.Save(output);
        Console.WriteLine($"Decoded artwork: {image!.PixelWidth}x{image.PixelHeight} at {path}");
    }

    private static async Task MediaTests()
    {
        // A silent local source exercises real Windows SMTC without touching the user's player.
        var path = Path.Combine(AppContext.BaseDirectory, "regression-silence.wav");
        using (var writer = new BinaryWriter(File.Create(path)))
        {
            const int bytes = 8000 * 2 * 120;
            writer.Write("RIFF"u8); writer.Write(36 + bytes); writer.Write("WAVEfmt "u8);
            writer.Write(16); writer.Write((short)1); writer.Write((short)1);
            writer.Write(8000); writer.Write(16000); writer.Write((short)2); writer.Write((short)16);
            writer.Write("data"u8); writer.Write(bytes); writer.Write(new byte[bytes]);
        }
        var window = new MainWindow();
        var service = (MediaSessionService)typeof(MainWindow).GetField("_media", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
        var update = typeof(MainWindow).GetMethod("UpdateMediaUI", BindingFlags.Instance | BindingFlags.NonPublic)!;
        service.StateChanged += () => update.Invoke(window, null);
        using var player = new MediaPlayer { Volume = 0 };
        try
        {
            using var source = MediaSource.CreateFromStorageFile(await StorageFile.GetFileFromPathAsync(path));
            var item = new MediaPlaybackItem(source);
            var display = item.GetDisplayProperties();
            display.Type = Windows.Media.MediaPlaybackType.Music;
            display.MusicProperties.Title = "TopMediaBar regression test";
            item.ApplyDisplayProperties(display);
            player.Source = item;
            player.Play();
            await Until(() => player.PlaybackSession.NaturalDuration.TotalSeconds > 0, "Silent test media opens");
            await service.InitializeAsync();
            await Until(() => service.Title == "TopMediaBar regression test" && service.Duration.TotalSeconds == 120 && service.CanSeek,
                "SMTC exposes the test player's seek capability");
            Console.WriteLine($"Test session: {service.Title}; playing={service.IsPlaying}");
            await Until(() => service.IsPlaying, "SMTC reports playing state");
            Check(await service.TogglePlayPauseAsync(), "Pause command is accepted");
            await Until(() => !service.IsPlaying, "Pause state reaches the service");
            var paused = service.GetEstimatedPosition();
            await Task.Delay(600);
            Check(Math.Abs((service.GetEstimatedPosition() - paused).TotalSeconds) < 0.1, "Paused progress stays still in the real session");
            Check(await service.TogglePlayPauseAsync(), "Resume command is accepted");
            await Until(() => service.IsPlaying, "Resume state reaches the service");

            var slider = (Slider)window.FindName("ProgressSlider");
            slider.Value = 50; // Same ValueChanged route used by a click on the track.
            await Until(() => player.PlaybackSession.Position.TotalSeconds >= 59 && player.PlaybackSession.Position.TotalSeconds < 65,
                "A progress track change seeks the real player");
            await Task.Delay(600);
            Check(service.GetEstimatedPosition().TotalSeconds >= 59, "Progress does not jump back after the seek");

            slider.RaiseEvent(new DragStartedEventArgs(0, 0) { RoutedEvent = Thumb.DragStartedEvent });
            slider.Value = 75;
            await Task.Delay(200);
            Check(player.PlaybackSession.Position.TotalSeconds < 70, "Dragging previews without sending intermediate seeks");
            slider.RaiseEvent(new DragCompletedEventArgs(0, 0, false) { RoutedEvent = Thumb.DragCompletedEvent });
            await Until(() => player.PlaybackSession.Position.TotalSeconds >= 89 && player.PlaybackSession.Position.TotalSeconds < 95,
                "Releasing the progress thumb seeks the real player");
        }
        finally
        {
            player.Pause();
            player.Source = null;
            service.Dispose();
            window.Close();
            File.Delete(path);
        }
    }
}
