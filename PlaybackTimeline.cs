namespace TopMediaBar;

/// <summary>Relative playback time, anchored again on pause/resume, speed changes and seeking.</summary>
internal sealed class PlaybackTimeline
{
    private TimeSpan _position;
    private DateTimeOffset _anchor;
    private DateTimeOffset _playbackChangedAt;
    private double _rate = 1;
    private bool _hasTimeline;
    public bool IsPlaying { get; private set; }
    public TimeSpan Duration { get; private set; }

    public TimeSpan Estimate(DateTimeOffset now)
    {
        var elapsed = IsPlaying ? Math.Max(0, (now - _anchor).TotalSeconds) * _rate : 0;
        return TimeSpan.FromSeconds(Math.Clamp(_position.TotalSeconds + elapsed, 0, Duration.TotalSeconds));
    }

    public void SetPlayback(bool playing, double rate, DateTimeOffset now)
    {
        rate = double.IsFinite(rate) && rate >= 0 ? rate : 1;
        if (playing == IsPlaying && rate == _rate) return;
        _position = Estimate(now);
        _anchor = now;
        _playbackChangedAt = _hasTimeline ? now : DateTimeOffset.MinValue;
        IsPlaying = playing;
        _rate = rate;
    }

    public void Update(TimeSpan position, TimeSpan duration, DateTimeOffset updatedAt, DateTimeOffset now)
    {
        Duration = duration > TimeSpan.Zero ? duration : TimeSpan.Zero;
        // Never count time spent paused when the source reuses its old timeline timestamp.
        var effectiveUpdate = updatedAt > _playbackChangedAt ? updatedAt : _playbackChangedAt;
        var elapsed = IsPlaying ? Math.Max(0, (now - effectiveUpdate).TotalSeconds) * _rate : 0;
        _position = TimeSpan.FromSeconds(Math.Clamp(position.TotalSeconds + elapsed, 0, Duration.TotalSeconds));
        _anchor = now;
        _hasTimeline = true;
    }

    public void Seek(TimeSpan position, DateTimeOffset now)
    {
        _position = TimeSpan.FromSeconds(Math.Clamp(position.TotalSeconds, 0, Duration.TotalSeconds));
        _anchor = now;
    }
}
