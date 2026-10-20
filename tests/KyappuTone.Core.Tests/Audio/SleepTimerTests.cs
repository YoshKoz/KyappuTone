using KyappuTone.Core.Audio;
using Shouldly;
using Xunit;

namespace KyappuTone.Core.Tests.Audio;

public class SleepTimerTests
{
    private static (SleepTimer Timer, ManualTimeProvider Clock, int[] Fired) Create()
    {
        var clock = new ManualTimeProvider();
        var fired = new int[1];
        var timer = new SleepTimer(clock);
        timer.Start(TimeSpan.FromSeconds(10), () => fired[0]++);
        return (timer, clock, fired);
    }

    [Fact]
    public void Start_Sets_Remaining_And_IsRunning()
    {
        var (timer, _, _) = Create();
        using var _timer = timer;

        timer.IsRunning.ShouldBeTrue();
        timer.Remaining.ShouldBe(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void Remaining_Decreases_And_Tick_Fires()
    {
        var (timer, clock, _) = Create();
        using var _timer = timer;
        var ticks = 0;
        timer.Tick += (_, _) => ticks++;

        clock.Advance(TimeSpan.FromSeconds(4));

        timer.Remaining.ShouldBe(TimeSpan.FromSeconds(6));
        ticks.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void Elapses_Once_Then_Stops()
    {
        var (timer, clock, fired) = Create();
        using var _timer = timer;

        clock.Advance(TimeSpan.FromSeconds(10));
        clock.Advance(TimeSpan.FromSeconds(30));

        fired[0].ShouldBe(1);
        timer.IsRunning.ShouldBeFalse();
        timer.Remaining.ShouldBe(TimeSpan.Zero);
    }

    [Fact]
    public void Cancel_Prevents_Elapsed()
    {
        var (timer, clock, fired) = Create();
        using var _timer = timer;

        clock.Advance(TimeSpan.FromSeconds(5));
        timer.Cancel();
        clock.Advance(TimeSpan.FromSeconds(30));

        fired[0].ShouldBe(0);
        timer.IsRunning.ShouldBeFalse();
    }

    [Fact]
    public void Restart_Resets_Deadline()
    {
        var (timer, clock, fired) = Create();
        using var _timer = timer;

        clock.Advance(TimeSpan.FromSeconds(5));
        timer.Start(TimeSpan.FromSeconds(20), () => fired[0]++);

        clock.Advance(TimeSpan.FromSeconds(15));
        fired[0].ShouldBe(0);
        timer.Remaining.ShouldBe(TimeSpan.FromSeconds(5));

        clock.Advance(TimeSpan.FromSeconds(5));
        fired[0].ShouldBe(1);
    }

    [Fact]
    public void Start_Rejects_Non_Positive_Duration()
    {
        var clock = new ManualTimeProvider();
        using var timer = new SleepTimer(clock);

        Should.Throw<ArgumentOutOfRangeException>(() => timer.Start(TimeSpan.Zero, () => { }));
        Should.Throw<ArgumentOutOfRangeException>(() => timer.Start(TimeSpan.FromSeconds(-1), () => { }));
    }

    [Fact]
    public void Dispose_Stops_Future_Elapsed()
    {
        var clock = new ManualTimeProvider();
        var fired = 0;
        var timer = new SleepTimer(clock);
        timer.Start(TimeSpan.FromSeconds(10), () => fired++);

        timer.Dispose();
        clock.Advance(TimeSpan.FromSeconds(30));

        fired.ShouldBe(0);
    }
}

/// <summary>Deterministic clock that fires timers only when <see cref="Advance"/> is called.</summary>
internal sealed class ManualTimeProvider : TimeProvider
{
    private readonly List<ManualTimer> _timers = new();
    private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => _now;

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualTimer(this, callback, state, dueTime, period);
        _timers.Add(timer);
        return timer;
    }

    public void Advance(TimeSpan delta)
    {
        var target = _now + delta;

        while (true)
        {
            ManualTimer? next = null;
            foreach (var timer in _timers)
            {
                if (!timer.Enabled) continue;
                if (next is null || timer.Due < next.Due) next = timer;
            }

            if (next is null || next.Due > target) break;

            _now = next.Due;
            next.Fire();
        }

        _now = target;
    }

    internal sealed class ManualTimer : ITimer
    {
        private readonly ManualTimeProvider _owner;
        private readonly TimerCallback _callback;
        private readonly object? _state;
        private TimeSpan _period;

        public ManualTimer(ManualTimeProvider owner, TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            _owner = owner;
            _callback = callback;
            _state = state;
            _period = period;
            Due = dueTime == Timeout.InfiniteTimeSpan ? DateTimeOffset.MaxValue : owner._now + dueTime;
            Enabled = dueTime != Timeout.InfiniteTimeSpan;
        }

        public DateTimeOffset Due { get; private set; }

        public bool Enabled { get; private set; }

        public void Fire()
        {
            if (!Enabled) return;

            var period = _period;
            if (period == Timeout.InfiniteTimeSpan)
            {
                Enabled = false;
            }
            else
            {
                Due += period <= TimeSpan.Zero ? TimeSpan.FromMilliseconds(1) : period;
            }

            _callback(_state);
        }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            _period = period;

            if (dueTime == Timeout.InfiniteTimeSpan)
            {
                Enabled = false;
                Due = DateTimeOffset.MaxValue;
            }
            else
            {
                Due = _owner._now + dueTime;
                Enabled = true;
            }

            return true;
        }

        public void Dispose() => Enabled = false;

        public ValueTask DisposeAsync()
        {
            Enabled = false;
            return ValueTask.CompletedTask;
        }
    }
}
