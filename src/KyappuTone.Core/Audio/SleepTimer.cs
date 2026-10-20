namespace KyappuTone.Core.Audio;

/// <summary>
/// One-shot countdown that follows a <see cref="TimeProvider"/>, so tests can
/// drive it with a manual clock. Fires <see cref="Tick"/> roughly once per
/// second while running and invokes the callback once when it elapses.
/// </summary>
public sealed class SleepTimer : IDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

    private readonly TimeProvider _time;
    private readonly object _gate = new();

    private ITimer? _timer;
    private DateTimeOffset _deadline;
    private Action? _onElapsed;
    private bool _isRunning;
    private bool _disposed;

    /// <summary>Creates a timer driven by <paramref name="time"/>.</summary>
    public SleepTimer(TimeProvider time)
    {
        _time = time ?? throw new ArgumentNullException(nameof(time));
    }

    /// <summary>Raised once per poll and when the timer starts, is cancelled or elapses.</summary>
    public event EventHandler? Tick;

    /// <summary>True while a countdown is pending.</summary>
    public bool IsRunning
    {
        get
        {
            lock (_gate) return _isRunning;
        }
    }

    /// <summary>Time left before the callback fires; zero when idle.</summary>
    public TimeSpan Remaining
    {
        get
        {
            lock (_gate)
            {
                if (!_isRunning) return TimeSpan.Zero;
                var remaining = _deadline - _time.GetUtcNow();
                return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
            }
        }
    }

    /// <summary>Starts (or restarts) the countdown.</summary>
    public void Start(TimeSpan after, Action onElapsed)
    {
        ArgumentNullException.ThrowIfNull(onElapsed);
        if (after <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(after));

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            StopTimerCore();
            _deadline = _time.GetUtcNow() + after;
            _onElapsed = onElapsed;
            _isRunning = true;

            var due = after < PollInterval ? after : PollInterval;
            _timer = _time.CreateTimer(_ => OnTimer(), null, due, PollInterval);
        }

        Tick?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Cancels a running countdown. Safe to call when idle.</summary>
    public void Cancel()
    {
        lock (_gate)
        {
            StopTimerCore();
        }

        Tick?.Invoke(this, EventArgs.Empty);
    }

    private void OnTimer()
    {
        Action? callback = null;

        lock (_gate)
        {
            if (!_isRunning) return;

            if (_time.GetUtcNow() >= _deadline)
            {
                callback = _onElapsed;
                StopTimerCore();
            }
        }

        callback?.Invoke();
        Tick?.Invoke(this, EventArgs.Empty);
    }

    private void StopTimerCore()
    {
        _timer?.Dispose();
        _timer = null;
        _isRunning = false;
        _onElapsed = null;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            StopTimerCore();
        }
    }
}
