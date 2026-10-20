namespace KyappuTone.Audio;

/// <summary>Audio sink that pulls interleaved float32 from a render callback.</summary>
public interface IAudioOutput : IDisposable
{
    /// <summary>Latency reported by the backend, in milliseconds.</summary>
    double LatencyMs { get; }

    /// <summary>Starts pulling audio. The callback fills the whole span.</summary>
    void Start(Action<Span<float>> render, int sampleRate, int channels);

    /// <summary>Stops pulling audio.</summary>
    void Stop();
}
