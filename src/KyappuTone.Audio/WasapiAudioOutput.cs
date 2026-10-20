using System.Runtime.InteropServices;
using NAudio.Wave;

namespace KyappuTone.Audio;

/// <summary>
/// Shared-mode WASAPI sink. NAudio pulls interleaved 32-bit float samples from
/// <see cref="RenderWaveProvider"/>, which forwards the request to the engine.
/// </summary>
public sealed class WasapiAudioOutput : IAudioOutput
{
    /// <summary>Requested buffer latency, in milliseconds.</summary>
    public const int DefaultLatencyMs = 10;

    private readonly int _latencyMs;
    private WasapiPlayer? _output;
    private RenderWaveProvider? _provider;
    private bool _disposed;
    private bool _stopping;
    private int _unexpectedStops;

    /// <summary>Creates a sink bound to the default output device.</summary>
    public WasapiAudioOutput(int latencyMs = DefaultLatencyMs)
    {
        _latencyMs = Math.Max(8, latencyMs);
    }

    /// <inheritdoc />
    public double LatencyMs => _latencyMs;

    /// <summary>Render callbacks completed by the current/most recent stream.</summary>
    public long RenderCallbacks => _provider?.CallbackCount ?? 0;

    /// <summary>Frames delivered to WASAPI by the current/most recent stream.</summary>
    public long RenderedFrames => _provider?.FramesRendered ?? 0;

    /// <summary>Number of times the device stopped without an explicit Stop/Dispose.</summary>
    public int UnexpectedStops => Volatile.Read(ref _unexpectedStops);

    /// <summary>Exception reported by the last unexpected device stop, if any.</summary>
    public Exception? LastStopException { get; private set; }

    /// <inheritdoc />
    public void Start(Action<Span<float>> render, int sampleRate, int channels)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(render);

        Stop();

        var provider = new RenderWaveProvider(render, sampleRate, channels);
        var output = new WasapiPlayerBuilder()
            .WithSharedMode()
            .WithEventSync()
            .WithLatency(_latencyMs)
            .WithLowLatency(false)
            .Build();
        try
        {
            output.Init(provider);
            output.Play();
        }
        catch
        {
            output.Dispose();
            throw;
        }

        output.PlaybackStopped += OnPlaybackStopped;
        _provider = provider;
        _output = output;
    }

    private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        if (_stopping || _disposed) return;

        LastStopException = e.Exception;
        Interlocked.Increment(ref _unexpectedStops);
    }

    /// <inheritdoc />
    public void Stop()
    {
        var output = _output;
        _output = null;

        if (output is null) return;

        _stopping = true;
        try
        {
            output.Stop();
        }
        finally
        {
            output.PlaybackStopped -= OnPlaybackStopped;
            output.Dispose();
            _stopping = false;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
    }
}

/// <summary>
/// Adapts the engine's float render callback to NAudio's byte-oriented
/// <see cref="IWaveProvider"/> contract.
/// </summary>
public sealed class RenderWaveProvider : IWaveProvider
{
    private readonly Action<Span<float>> _render;
    private readonly int _channels;
    private long _callbackCount;
    private long _frameCount;

    /// <summary>Creates a provider that always renders the full requested buffer.</summary>
    public RenderWaveProvider(Action<Span<float>> render, int sampleRate, int channels)
    {
        _render = render ?? throw new ArgumentNullException(nameof(render));
        ArgumentOutOfRangeException.ThrowIfLessThan(sampleRate, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(channels, 1);
        _channels = channels;
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channels);
    }

    /// <inheritdoc />
    public WaveFormat WaveFormat { get; }

    /// <summary>Number of completed <see cref="Read"/> calls.</summary>
    public long CallbackCount => Interlocked.Read(ref _callbackCount);

    /// <summary>Number of frames delivered to the device.</summary>
    public long FramesRendered => Interlocked.Read(ref _frameCount);

    /// <inheritdoc />
    public int Read(Span<byte> buffer)
    {
        var aligned = buffer.Length - buffer.Length % sizeof(float);
        var samples = MemoryMarshal.Cast<byte, float>(buffer[..aligned]);

        try
        {
            _render(samples);
        }
        catch (Exception ex)
        {
            // An exception on the audio thread would tear down the process.
            // Silence the buffer and keep the stream alive.
            System.Diagnostics.Debug.WriteLine($"Render callback failed: {ex}");
            buffer.Clear();
            return buffer.Length;
        }

        if (aligned < buffer.Length) buffer[aligned..].Clear();
        Interlocked.Increment(ref _callbackCount);
        Interlocked.Add(ref _frameCount, samples.Length / _channels);
        return buffer.Length;
    }
}
