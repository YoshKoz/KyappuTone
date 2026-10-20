using KyappuTone.Audio.Decoding;
using KyappuTone.Core.Audio;

namespace KyappuTone.Audio;

/// <summary>
/// Pull-based playback engine. One render callback mixes the active decks,
/// applies the equalizer, limiter and FFT tap, and updates the clock.
/// </summary>
public sealed class AudioEngine : IDisposable
{
    private const int MaxFramesPerCallback = 8192;

    private readonly object _gate = new();
    private readonly IAudioOutput _output;
    private readonly CrossfadeMixer _mixer = new(DecodedFormat.TargetChannels);
    private readonly SoftLimiter _limiter = new();
    private readonly FftTap _fft;
    private readonly float[] _bufferA = new float[MaxFramesPerCallback * DecodedFormat.TargetChannels];
    private readonly float[] _bufferB = new float[MaxFramesPerCallback * DecodedFormat.TargetChannels];

    private Deck? _current;
    private Deck? _next;
    private TimeSpan _crossfadeConfigured;
    private bool _crossfading;
    private bool _playing;
    private bool _endedRaised;
    private bool _outputStarted;
    private long _framesInTrack;
    private TimeSpan _positionOrigin;
    private float _volume = 1f;
    private bool _muted;
    private bool _disposed;

    /// <summary>Creates an engine that renders into <paramref name="output"/>.</summary>
    public AudioEngine(IAudioOutput output)
    {
        _output = output ?? throw new ArgumentNullException(nameof(output));
        Equalizer = new TenBandEqualizer(DecodedFormat.TargetSampleRate, DecodedFormat.TargetChannels);
        _fft = new FftTap(2048, DecodedFormat.TargetSampleRate);
    }

    /// <summary>Ten-band equalizer applied to the mix.</summary>
    public TenBandEqualizer Equalizer { get; }

    /// <summary>FFT analyser fed from the post-limiter mix.</summary>
    public FftTap Fft => _fft;

    /// <summary>Current track position.</summary>
    public TimeSpan Position
    {
        get
        {
            lock (_gate)
            {
                return _positionOrigin + TimeSpan.FromSeconds(_framesInTrack / (double)DecodedFormat.TargetSampleRate);
            }
        }
    }

    /// <summary>Duration of the current track, or zero when nothing is loaded.</summary>
    public TimeSpan Duration
    {
        get
        {
            lock (_gate) return _current?.Duration ?? TimeSpan.Zero;
        }
    }

    /// <summary>Linear output gain (0..1), applied before the equalizer.</summary>
    public float Volume
    {
        get
        {
            lock (_gate) return _volume;
        }
        set
        {
            lock (_gate) _volume = Math.Clamp(value, 0f, 1f);
        }
    }

    /// <summary>Mutes the mix without changing <see cref="Volume"/>.</summary>
    public bool IsMuted
    {
        get
        {
            lock (_gate) return _muted;
        }
        set
        {
            lock (_gate) _muted = value;
        }
    }

    /// <summary>True while the engine is advancing the clock.</summary>
    public bool IsPlaying
    {
        get
        {
            lock (_gate) return _playing;
        }
    }

    /// <summary>Raised after each rendered callback with the new position.</summary>
    public event EventHandler<TimeSpan>? PositionChanged;

    /// <summary>Raised once when a track finishes (including before a gapless swap).</summary>
    public event EventHandler? TrackEnded;

    /// <summary>Loads a track, optionally starting playback.</summary>
    public void Load(string path, bool play = false)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            _current?.Dispose();
            _next?.Dispose();
            _current = null;
            _next = null;

            var deck = new Deck(path);
            _current = deck;
            _next = null;
            _crossfading = false;
            _framesInTrack = 0;
            _positionOrigin = TimeSpan.Zero;
            _endedRaised = false;

            if (play) StartPlayback();
        }
    }

    /// <summary>Queues the next track and the crossfade length used to reach it.</summary>
    public void SetNext(string path, TimeSpan crossfade)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            _next?.Dispose();
            _next = new Deck(path);
            _crossfadeConfigured = crossfade < TimeSpan.Zero ? TimeSpan.Zero : crossfade;
        }
    }

    /// <summary>Starts or resumes playback.</summary>
    public void Play()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_current is null) return;
            StartPlayback();
        }
    }

    /// <summary>Pauses playback without losing the position.</summary>
    public void Pause()
    {
        lock (_gate)
        {
            _playing = false;
        }
    }

    /// <summary>Seeks the current deck.</summary>
    public void Seek(TimeSpan position)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_current is null) return;
            if (position < TimeSpan.Zero) position = TimeSpan.Zero;

            _current.Seek(position);
            _framesInTrack = 0;
            _positionOrigin = position;
            _endedRaised = false;
        }
    }

    private void StartPlayback()
    {
        _playing = true;
        if (_outputStarted) return;
        _output.Start(Render, DecodedFormat.TargetSampleRate, DecodedFormat.TargetChannels);
        _outputStarted = true;
    }

    private void Render(Span<float> destination)
    {
        TimeSpan position;
        lock (_gate)
        {
            destination.Clear();
            if (!_playing || _current is null) return;

            var frames = Math.Min(destination.Length / DecodedFormat.TargetChannels, MaxFramesPerCallback);
            var span = destination[..(frames * DecodedFormat.TargetChannels)];

            if (_crossfading)
            {
                RenderCrossfade(span, frames);
            }
            else
            {
                RenderSingle(span, frames);
            }

            if (span.Length > 0)
            {
                var gain = _muted ? 0f : _volume;
                if (gain != 1f)
                {
                    for (var i = 0; i < span.Length; i++) span[i] *= gain;
                }

                Equalizer.Apply(span);
                _limiter.Process(span);
                _fft.Push(span);
            }

            position = _positionOrigin + TimeSpan.FromSeconds(_framesInTrack / (double)DecodedFormat.TargetSampleRate);
        }

        PositionChanged?.Invoke(this, position);
    }

    private void RenderSingle(Span<float> span, int frames)
    {
        var read = _current!.Read(span);
        if (read < span.Length) span[read..].Clear();
        _framesInTrack += read / DecodedFormat.TargetChannels;

        if (read == 0)
        {
            if (_next is not null)
            {
                AdvanceGapless();
            }
            else
            {
                _playing = false;
                RaiseTrackEnded();
            }

            return;
        }

        if (_next is null || _crossfadeConfigured <= TimeSpan.Zero) return;

        var remaining = _current.Duration - Position;
        if (remaining <= _crossfadeConfigured)
        {
            StartCrossfade(remaining);
        }
    }

    private void StartCrossfade(TimeSpan remaining)
    {
        var fadeFrames = (int)Math.Round(remaining.TotalSeconds * DecodedFormat.TargetSampleRate);
        var configuredFrames = (int)Math.Round(_crossfadeConfigured.TotalSeconds * DecodedFormat.TargetSampleRate);
        fadeFrames = Math.Clamp(fadeFrames, 1, Math.Max(1, configuredFrames));

        _mixer.StartFade(fadeFrames);
        _crossfading = true;
    }

    private void RenderCrossfade(Span<float> span, int frames)
    {
        var a = _current!.Read(_bufferA.AsSpan(0, frames * DecodedFormat.TargetChannels));
        var b = _next!.Read(_bufferB.AsSpan(0, frames * DecodedFormat.TargetChannels));

        for (var frame = 0; frame < frames; frame++)
        {
            var gainA = _mixer.GainA;
            var gainB = _mixer.GainB;

            for (var channel = 0; channel < DecodedFormat.TargetChannels; channel++)
            {
                var index = frame * DecodedFormat.TargetChannels + channel;
                var sampleA = index < a ? _bufferA[index] : 0f;
                var sampleB = index < b ? _bufferB[index] : 0f;
                span[index] = sampleA * gainA + sampleB * gainB;
            }

            _mixer.Advance();
        }

        _framesInTrack += frames;

        if (!_mixer.IsFading) FinishCrossfade();
    }

    private void FinishCrossfade()
    {
        RaiseTrackEnded();

        _current!.Dispose();
        _current = _next;
        _next = null;
        _crossfading = false;
        _framesInTrack = 0;
        _positionOrigin = TimeSpan.Zero;
        _endedRaised = false;
    }

    private void AdvanceGapless()
    {
        RaiseTrackEnded();

        _current!.Dispose();
        _current = _next;
        _next = null;
        _framesInTrack = 0;
        _positionOrigin = TimeSpan.Zero;
        _endedRaised = false;
    }

    private void RaiseTrackEnded()
    {
        if (_endedRaised) return;
        _endedRaised = true;
        TrackEnded?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _playing = false;

            _current?.Dispose();
            _next?.Dispose();
            _current = null;
            _next = null;
        }

        _output.Dispose();
    }

    private sealed class Deck : IDisposable
    {
        private readonly FfmpegDecoder _decoder;

        public Deck(string path)
        {
            _decoder = FfmpegDecoder.Open(path);
            Duration = _decoder.Duration;
        }

        public TimeSpan Duration { get; }

        public int Read(Span<float> destination) => _decoder.Read(destination);

        public void Seek(TimeSpan position) => _decoder.Seek(position);

        public void Dispose() => _decoder.Dispose();
    }
}
