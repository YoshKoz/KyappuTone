namespace KyappuTone.Core.Audio;

/// <summary>
/// Soft-knee limiter. Signals below the knee pass through bit-exactly; louder
/// material is compressed towards the ceiling and can never exceed it.
/// </summary>
public sealed class SoftLimiter
{
    private readonly float _knee;

    /// <summary>Creates a limiter with the given output ceiling (0, 1].</summary>
    public SoftLimiter(float ceiling = 0.98f)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(ceiling, 0f);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(ceiling, 1f);

        Ceiling = ceiling;
        _knee = ceiling * 0.7f;
    }

    /// <summary>Maximum absolute output sample.</summary>
    public float Ceiling { get; }

    /// <summary>Limits an interleaved buffer in place.</summary>
    public void Process(Span<float> interleaved)
    {
        for (var i = 0; i < interleaved.Length; i++)
        {
            var sample = interleaved[i];
            var magnitude = MathF.Abs(sample);
            if (magnitude <= _knee) continue;

            var compressed = _knee + (Ceiling - _knee) * MathF.Tanh((magnitude - _knee) / (Ceiling - _knee));
            interleaved[i] = MathF.CopySign(compressed, sample);
        }
    }
}
