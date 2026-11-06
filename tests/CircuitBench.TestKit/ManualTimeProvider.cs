namespace CircuitBench.TestKit;

/// <summary>手動推進的 TimeProvider：計時器只在 <see cref="Advance"/> 時觸發（不另外引入測試套件）。</summary>
public sealed class ManualTimeProvider(DateTimeOffset start) : TimeProvider
{
    private readonly List<ManualTimer> _timers = [];
    private DateTimeOffset _now = start;
    private long _ticks;

    public ManualTimeProvider()
        : this(new DateTimeOffset(2026, 1, 1, 8, 0, 0, TimeSpan.Zero))
    {
    }

    public override DateTimeOffset GetUtcNow() => _now;

    public override long GetTimestamp() => _ticks;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var t = new ManualTimer(this, callback, state, dueTime, period);
        lock (_timers)
        {
            _timers.Add(t);
        }

        return t;
    }

    public void Advance(TimeSpan delta)
    {
        var target = _now + delta;
        while (true)
        {
            ManualTimer? next;
            lock (_timers)
            {
                next = _timers.Where(t => t.DueAt is { } d && d <= target).MinBy(t => t.DueAt);
            }

            if (next is null)
            {
                break;
            }

            _ticks += (next.DueAt!.Value - _now).Ticks;
            _now = next.DueAt.Value;
            next.Fire();
        }

        _ticks += (target - _now).Ticks;
        _now = target;
    }

    private sealed class ManualTimer(ManualTimeProvider owner, TimerCallback callback, object? state, TimeSpan due, TimeSpan period) : ITimer
    {
        private TimeSpan _period = period;

        public DateTimeOffset? DueAt { get; private set; } = due == Timeout.InfiniteTimeSpan ? null : owner._now + due;

        public void Fire()
        {
            DueAt = _period == Timeout.InfiniteTimeSpan || _period == TimeSpan.Zero ? null : DueAt + _period;
            callback(state);
        }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            _period = period;
            DueAt = dueTime == Timeout.InfiniteTimeSpan ? null : owner._now + dueTime;
            return true;
        }

        public void Dispose() => DueAt = null;

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
