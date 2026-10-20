using KyappuTone.Audio;
using KyappuTone.Common.Services;
using KyappuTone.Core.Audio;

namespace KyappuTone.Common.Services.Backends;

/// <summary>
/// Plays audio through the in-process KyappuTone engine (FFmpeg decode, WASAPI
/// shared-mode output, DSP chain). Used for every format the engine can open,
/// with the Windows/Flyleaf backends kept as fallbacks.
/// </summary>
internal sealed class KyappuAudioBackend : IMediaBackend
{
	private static readonly long PositionIntervalTicks = TimeSpan.FromMilliseconds(950).Ticks;

	private readonly AudioEngine _engine;
	private readonly System.Threading.Timer _positionTimer;
	private readonly object _stateGate = new();

	private PlaybackState _lastState = PlaybackState.Stopped;
	private int _volume = 100;
	private bool _muted;
	private bool _disposed;

	/// <summary>Creates the backend. The WASAPI device is opened on first playback.</summary>
	public KyappuAudioBackend()
	{
		_engine = new AudioEngine(new WasapiAudioOutput());
		_engine.TrackEnded += OnTrackEnded;

		var settings = AudioSettingsService.Instance;
		_engine.ConfigureEqualizer(settings.ApplyTo);
		settings.Changed += OnAudioSettingsChanged;

		_positionTimer = new System.Threading.Timer(_ => PublishPosition(), null, Timeout.Infinite, Timeout.Infinite);
	}

	/// <inheritdoc />
	public event EventHandler<PlaybackStateChangedArgs>? StateChanged;

	/// <inheritdoc />
	public event EventHandler? OpenCompleted;

	/// <inheritdoc />
	public event EventHandler<long>? PositionChanged;

	/// <inheritdoc />
	public bool IsPlaying => _engine.IsPlaying;

	/// <summary>FFT analyser fed by the engine's post-limiter mix.</summary>
	internal FftTap Fft => _engine.Fft;

	/// <inheritdoc />
	public long CurTimeTicks
	{
		get => _engine.Position.Ticks;
		set => _engine.Seek(TimeSpan.FromTicks(Math.Max(0, value)));
	}

	/// <inheritdoc />
	public int Volume
	{
		get => _volume;
		set
		{
			_volume = Math.Clamp(value, 0, 100);
			_engine.Volume = _volume / 100f;
		}
	}

	/// <inheritdoc />
	public bool IsMuted
	{
		get => _muted;
		set
		{
			_muted = value;
			_engine.IsMuted = value;
		}
	}

	/// <inheritdoc />
	public async Task OpenAsync(string path)
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
		await Task.Run(() => _engine.Load(path, play: false)).ConfigureAwait(false);
		OpenCompleted?.Invoke(this, EventArgs.Empty);
	}

	/// <inheritdoc />
	public void Play()
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
		_engine.Play();
		_positionTimer.Change(0, (int)(PositionIntervalTicks / TimeSpan.TicksPerMillisecond));
		RaiseState(PlaybackState.Playing);
	}

	/// <inheritdoc />
	public void Pause()
	{
		_engine.Pause();
		_positionTimer.Change(Timeout.Infinite, Timeout.Infinite);
		RaiseState(PlaybackState.Paused);
	}

	/// <inheritdoc />
	public void Stop()
	{
		_engine.Pause();
		_engine.Seek(TimeSpan.Zero);
		_positionTimer.Change(Timeout.Infinite, Timeout.Infinite);
		RaiseState(PlaybackState.Stopped);
	}

	private void PublishPosition()
	{
		if (_disposed) return;
		PositionChanged?.Invoke(this, _engine.Position.Ticks);
	}

	private void OnTrackEnded(object? sender, EventArgs e)
	{
		_positionTimer.Change(Timeout.Infinite, Timeout.Infinite);
		RaiseState(PlaybackState.Ended);
	}

	private void OnAudioSettingsChanged(object? sender, EventArgs e)
		=> _engine.ConfigureEqualizer(AudioSettingsService.Instance.ApplyTo);

	private void RaiseState(PlaybackState state)
	{
		lock (_stateGate)
		{
			if (_lastState == state) return;
			_lastState = state;
		}

		StateChanged?.Invoke(this, new PlaybackStateChangedArgs(state));
	}

	/// <inheritdoc />
	public void Dispose()
	{
		if (_disposed) return;
		_disposed = true;

		_engine.TrackEnded -= OnTrackEnded;
		AudioSettingsService.Instance.Changed -= OnAudioSettingsChanged;
		_positionTimer.Dispose();
		_engine.Dispose();
	}
}
