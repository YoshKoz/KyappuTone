namespace KyappuTone.Core.Audio;

/// <summary>
/// Equal-power crossfade between two decks. The outgoing deck gain follows
/// <c>cos</c>, the incoming deck gain follows <c>sin</c>, so power stays constant
/// across the fade.
/// </summary>
public sealed class CrossfadeMixer
{
    private int _fadeFrames;
    private int _elapsed;

    /// <summary>Creates a mixer for the given channel count.</summary>
    public CrossfadeMixer(int channels = 2)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(channels, 1);
        Channels = channels;
    }

    /// <summary>Number of interleaved channels.</summary>
    public int Channels { get; }

    /// <summary>True while a fade is in progress.</summary>
    public bool IsFading => _elapsed < _fadeFrames;

    /// <summary>Current gain of the outgoing deck.</summary>
    public float GainA => Progress switch
    {
        <= 0f => 1f,
        >= 1f => 0f,
        _ => MathF.Cos(Progress * MathF.PI / 2f),
    };

    /// <summary>Current gain of the incoming deck.</summary>
    public float GainB => Progress switch
    {
        <= 0f => 0f,
        >= 1f => 1f,
        _ => MathF.Sin(Progress * MathF.PI / 2f),
    };

    private float Progress => _fadeFrames <= 0 ? 0f : Math.Min(1f, _elapsed / (float)_fadeFrames);

    /// <summary>Starts a fade that reaches the incoming deck after <paramref name="frames"/>.</summary>
    public void StartFade(int frames)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(frames);
        _fadeFrames = frames;
        _elapsed = 0;
    }

    /// <summary>Advances the fade by one frame.</summary>
    public void Advance()
    {
        if (IsFading) _elapsed++;
    }

    /// <summary>Mixes outgoing and incoming buffers into <paramref name="destination"/>.</summary>
    public void Mix(ReadOnlySpan<float> outgoing, ReadOnlySpan<float> incoming, Span<float> destination)
    {
        var gainA = GainA;
        var gainB = GainB;

        for (var i = 0; i < destination.Length; i++)
        {
            var a = i < outgoing.Length ? outgoing[i] : 0f;
            var b = i < incoming.Length ? incoming[i] : 0f;
            destination[i] = a * gainA + b * gainB;
        }
    }
}
