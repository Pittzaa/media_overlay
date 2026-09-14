using System.IO;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace TopMediaBar;

/// <summary>Serializes SMTC callbacks on the UI thread and ignores obsolete asynchronous results.</summary>
public sealed class MediaSessionService : IDisposable
{
    public event Action? StateChanged;
    public event Action? TrackChanged;

    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _session;
    private PlaybackTimeline _timeline = new();
    private int _propertiesRequest;
    private int _notifiedRevision = -1;
    private bool _disposed;
    private bool _commandPending;
    private bool _seekEnabled;
    private TimeSpan _startTime;
    private TimeSpan _minSeek;
    private TimeSpan _maxSeek;
    private DateTimeOffset? _pendingSeekAt;
    private DateTimeOffset? _lastTimelineTimestamp;

    public int Revision { get; private set; }
    public string Title { get; private set; } = "";
    public string Artist { get; private set; } = "";
    public string SourceAppUserModelId { get; private set; } = "";
    public BitmapImage? Thumbnail { get; private set; }
    public bool IsPlaying => _timeline.IsPlaying;
    public bool HasSession => _session != null;
    public bool CanTogglePlayPause { get; private set; }
    public bool CanSkipNext { get; private set; }
    public bool CanSkipPrevious { get; private set; }
    public bool CanSeek => HasSession && _seekEnabled && Duration > TimeSpan.Zero && _maxSeek > _minSeek;
    public TimeSpan Duration => _timeline.Duration;

    public async Task InitializeAsync()
    {
        var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        if (_disposed) return;
        _manager = manager;
        _manager.CurrentSessionChanged += OnCurrentSessionChanged;
        AttachSession();
    }

    private void Dispatch(Action action)
    {
        if (!_disposed && !_dispatcher.HasShutdownStarted)
            _dispatcher.BeginInvoke(() => { if (!_disposed) action(); });
    }

    private void OnCurrentSessionChanged(GlobalSystemMediaTransportControlsSessionManager sender, CurrentSessionChangedEventArgs args)
        => Dispatch(AttachSession);

    private void DetachSession()
    {
        if (_session == null) return;
        _session.MediaPropertiesChanged -= OnMediaPropertiesChanged;
        _session.PlaybackInfoChanged -= OnPlaybackInfoChanged;
        _session.TimelinePropertiesChanged -= OnTimelinePropertiesChanged;
    }

    private void AttachSession()
    {
        var session = _manager?.GetCurrentSession();
        if (ReferenceEquals(session, _session)) return;
        DetachSession();
        _session = session;
        Revision++;
        _propertiesRequest++;
        Title = Artist = "";
        SourceAppUserModelId = _session?.SourceAppUserModelId ?? "";
        Thumbnail = null;
        _timeline = new();
        _pendingSeekAt = _lastTimelineTimestamp = null;
        _seekEnabled = CanTogglePlayPause = CanSkipNext = CanSkipPrevious = false;
        _startTime = _minSeek = _maxSeek = TimeSpan.Zero;

        if (_session != null)
        {
            _session.MediaPropertiesChanged += OnMediaPropertiesChanged;
            _session.PlaybackInfoChanged += OnPlaybackInfoChanged;
            _session.TimelinePropertiesChanged += OnTimelinePropertiesChanged;
            RefreshPlaybackInfo();
            RefreshTimeline();
            _ = RefreshMediaPropertiesAsync();
        }
        StateChanged?.Invoke();
    }

    private void OnMediaPropertiesChanged(GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args)
        => Dispatch(() => { if (ReferenceEquals(sender, _session)) _ = RefreshMediaPropertiesAsync(); });

    private void OnPlaybackInfoChanged(GlobalSystemMediaTransportControlsSession sender, PlaybackInfoChangedEventArgs args)
        => Dispatch(() => { if (ReferenceEquals(sender, _session)) RefreshPlaybackInfo(); });

    private void OnTimelinePropertiesChanged(GlobalSystemMediaTransportControlsSession sender, TimelinePropertiesChangedEventArgs args)
        => Dispatch(() => { if (ReferenceEquals(sender, _session)) RefreshTimeline(); });

    private async Task RefreshMediaPropertiesAsync()
    {
        var session = _session;
        var request = ++_propertiesRequest;
        if (session == null) return;
        try
        {
            var props = await session.TryGetMediaPropertiesAsync();
            if (_disposed || !ReferenceEquals(session, _session) || request != _propertiesRequest) return;
            var changed = Title != (props.Title ?? "") || Artist != (props.Artist ?? "");
            Title = props.Title ?? "";
            Artist = props.Artist ?? "";
            if (changed)
            {
                Revision++;
                Thumbnail = null;
                _pendingSeekAt = null;
                _lastTimelineTimestamp = null;
                RefreshTimeline();
            }
            StateChanged?.Invoke();
            var thumbnail = await LoadThumbnailAsync(props.Thumbnail);
            if (_disposed || !ReferenceEquals(session, _session) || request != _propertiesRequest) return;
            // A transient failure must not erase artwork already loaded for this track.
            // Track/session changes clear Thumbnail above before loading the new image.
            Thumbnail = thumbnail ?? Thumbnail;
            StateChanged?.Invoke();
            // A later metadata request can supersede the first artwork load for a track.
            if (_notifiedRevision != Revision && !string.IsNullOrWhiteSpace(Title))
            {
                _notifiedRevision = Revision;
                TrackChanged?.Invoke();
            }
        }
        catch
        {
            // The source can close while its metadata or artwork is loading.
        }
    }

    private void RefreshPlaybackInfo()
    {
        if (_session == null) return;
        try
        {
            var info = _session.GetPlaybackInfo();
            _timeline.SetPlayback(info.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing,
                info.PlaybackRate ?? 1, DateTimeOffset.UtcNow);
            CanSkipNext = info.Controls?.IsNextEnabled ?? false;
            CanSkipPrevious = info.Controls?.IsPreviousEnabled ?? false;
            CanTogglePlayPause = IsPlaying ? info.Controls?.IsPauseEnabled ?? false : info.Controls?.IsPlayEnabled ?? false;
            _seekEnabled = info.Controls?.IsPlaybackPositionEnabled ?? false;
        }
        catch
        {
            _timeline.SetPlayback(false, 1, DateTimeOffset.UtcNow);
            _seekEnabled = CanTogglePlayPause = CanSkipNext = CanSkipPrevious = false;
        }
        StateChanged?.Invoke();
    }

    private void RefreshTimeline()
    {
        if (_session == null) return;
        try
        {
            var tl = _session.GetTimelineProperties();
            var now = DateTimeOffset.UtcNow;
            // Give a successful seek time to be acknowledged; an old event must not undo it.
            if (_pendingSeekAt is { } seekAt && tl.LastUpdatedTime < seekAt && now - seekAt < TimeSpan.FromSeconds(2)) return;
            _pendingSeekAt = null;
            if (_lastTimelineTimestamp == tl.LastUpdatedTime) return;
            _lastTimelineTimestamp = tl.LastUpdatedTime;
            _startTime = tl.StartTime;
            _minSeek = tl.MinSeekTime > tl.StartTime ? tl.MinSeekTime : tl.StartTime;
            _maxSeek = tl.MaxSeekTime < tl.EndTime ? tl.MaxSeekTime : tl.EndTime;
            _timeline.Update(tl.Position - tl.StartTime, tl.EndTime - tl.StartTime, tl.LastUpdatedTime, now);
        }
        catch
        {
            _seekEnabled = false;
        }
        StateChanged?.Invoke();
    }

    public TimeSpan GetEstimatedPosition()
    {
        if (_pendingSeekAt is { } seekAt && DateTimeOffset.UtcNow - seekAt >= TimeSpan.FromSeconds(2))
        {
            _lastTimelineTimestamp = null;
            RefreshTimeline();
        }
        return _timeline.Estimate(DateTimeOffset.UtcNow);
    }

    private static async Task<BitmapImage?> LoadThumbnailAsync(IRandomAccessStreamReference? thumbRef)
    {
        if (thumbRef == null) return null;
        try
        {
            using IRandomAccessStreamWithContentType stream = await thumbRef.OpenReadAsync();
            using var reader = new DataReader(stream.GetInputStreamAt(0));
            await reader.LoadAsync((uint)stream.Size);
            var bytes = new byte[stream.Size];
            reader.ReadBytes(bytes);
            using var memoryStream = new MemoryStream(bytes);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = memoryStream;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch { return null; }
    }

    private async Task<bool> RunCommandAsync(Func<GlobalSystemMediaTransportControlsSession, Task<bool>> command)
    {
        var session = _session;
        if (session == null || _commandPending || _disposed) return false;
        _commandPending = true;
        try { return await command(session); }
        catch { return false; }
        finally
        {
            _commandPending = false;
            if (!_disposed && ReferenceEquals(session, _session)) RefreshPlaybackInfo();
        }
    }

    public Task<bool> TogglePlayPauseAsync() => !CanTogglePlayPause ? Task.FromResult(false) :
        RunCommandAsync(async session => IsPlaying ? await session.TryPauseAsync() : await session.TryPlayAsync());

    public Task<bool> SkipNextAsync() => !CanSkipNext ? Task.FromResult(false) :
        RunCommandAsync(async session => await session.TrySkipNextAsync());

    public Task<bool> SkipPreviousAsync() => !CanSkipPrevious ? Task.FromResult(false) :
        RunCommandAsync(async session => await session.TrySkipPreviousAsync());

    public async Task<bool> SeekToAsync(TimeSpan position, int revision)
    {
        if (!CanSeek || revision != Revision) return false;
        var target = TimeSpan.FromTicks(Math.Clamp((_startTime + position).Ticks, _minSeek.Ticks, _maxSeek.Ticks));
        var seekAt = DateTimeOffset.UtcNow;
        var accepted = await RunCommandAsync(async session => await session.TryChangePlaybackPositionAsync(target.Ticks));
        if (_disposed || revision != Revision) return false;
        if (accepted)
        {
            // Prefer a timeline acknowledgement that arrived while the command was in flight.
            if (_lastTimelineTimestamp is not { } updated || updated < seekAt)
            {
                _timeline.Seek(target - _startTime, DateTimeOffset.UtcNow);
                _pendingSeekAt = seekAt;
            }
        }
        else
        {
            _lastTimelineTimestamp = null;
            RefreshTimeline();
        }
        StateChanged?.Invoke();
        return accepted;
    }

    public void Dispose()
    {
        _disposed = true;
        DetachSession();
        if (_manager != null) _manager.CurrentSessionChanged -= OnCurrentSessionChanged;
        _session = null;
    }
}
