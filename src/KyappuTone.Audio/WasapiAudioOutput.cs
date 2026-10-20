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

    /// <summary>Creates a sink bound to the default output device.</summary>
    public WasapiAudioOutput(int latencyMs = DefaultLatencyMs)
    {
        _latencyMs = Math.Max(8, latencyMs);
    }

    /// <inheritdoc />
    public double LatencyMs => _latencyMs;

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

        _provider = provider;
        _output = output;
    }

    /// <inheritdoc />
    public void Stop()
    {
        var output = _output;
        _output = null;
        _provider = null;

        if (output is null) return;

        try
        {
            output.Stop();
        }
        finally
        {
            output.Dispose();
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

    /// <summary>Creates a provider that always renders the full requested buffer.</summary>
    public RenderWaveProvider(Action<Span<float>> render, int sampleRate, int channels)
    {
        _render = render ?? throw new ArgumentNullException(nameof(render));
        ArgumentOutOfRangeException.ThrowIfLessThan(sampleRate, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(channels, 1);
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channels);
    }

    /// <inheritdoc />
    public WaveFormat WaveFormat { get; }

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
        return buffer.Length;
    }
}
