namespace KyappuTone.Audio;

/// <summary>Output that never touches hardware; tests drive <see cref="Pull"/> directly.</summary>
public sealed class NullAudioOutput : IAudioOutput
{
    private Action<Span<float>>? _render;

    public double LatencyMs => 0;

    public bool IsRunning { get; private set; }

    public int SampleRate { get; private set; }

    public int Channels { get; private set; }

    public void Start(Action<Span<float>> render, int sampleRate, int channels)
    {
        _render = render ?? throw new ArgumentNullException(nameof(render));
        SampleRate = sampleRate;
        Channels = channels;
        IsRunning = true;
    }

    public void Stop() => IsRunning = false;

    /// <summary>Renders exactly <paramref name="frames"/> frames into <paramref name="destination"/>.</summary>
    public void Pull(int frames, Span<float> destination)
    {
        if (!IsRunning) throw new InvalidOperationException("Output is not running.");
        _render!(destination[..(frames * Channels)]);
    }

    public void Dispose() => Stop();
}
