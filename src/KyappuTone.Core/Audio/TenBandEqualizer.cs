namespace KyappuTone.Core.Audio;

/// <summary>
/// Ten-band peaking equalizer with preamp. One filter chain per channel; the
/// interleaved buffer layout is <c>[L, R, L, R, ...]</c>.
/// </summary>
public sealed class TenBandEqualizer
{
    private static readonly float[] Bands =
    {
        31.5f, 63f, 125f, 250f, 500f, 1000f, 2000f, 4000f, 8000f, 16000f,
    };

    private readonly int _channels;
    private readonly float _sampleRate;
    private readonly Biquad[][] _filters;
    private readonly float[] _gainsDb = new float[Bands.Length];
    private float _preampDb;

    /// <summary>Creates an equalizer for the given sample rate and channel count.</summary>
    public TenBandEqualizer(int sampleRate, int channels)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(sampleRate, 8000);
        ArgumentOutOfRangeException.ThrowIfLessThan(channels, 1);

        _sampleRate = sampleRate;
        _channels = channels;
        _filters = new Biquad[Bands.Length][];

        for (var band = 0; band < Bands.Length; band++)
        {
            _filters[band] = new Biquad[channels];
            for (var channel = 0; channel < channels; channel++)
            {
                _filters[band][channel] = new Biquad(sampleRate, Math.Min(Bands[band], sampleRate / 2f - 1f), 1.41f, 0f);
            }
        }
    }

    /// <summary>Center frequencies of the ten bands, in Hz.</summary>
    public static IReadOnlyList<float> CenterFrequencies => Bands;

    /// <summary>Preamp gain in dB, clamped to ±12 dB.</summary>
    public float PreampDb
    {
        get => _preampDb;
        set => _preampDb = Math.Clamp(value, -12f, 12f);
    }

    /// <summary>Current gain for each band in dB.</summary>
    public float[] BandGainsDb => (float[])_gainsDb.Clone();

    /// <summary>True when every band and the preamp are at 0 dB.</summary>
    public bool IsFlat => _preampDb == 0f && Array.TrueForAll(_gainsDb, g => g == 0f);

    /// <summary>Sets one band gain in dB, clamped to ±12 dB.</summary>
    public void SetBandGain(int band, float gainDb)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(band);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(band, Bands.Length);

        var clamped = Math.Clamp(gainDb, -12f, 12f);
        if (_gainsDb[band] == clamped) return;

        _gainsDb[band] = clamped;
        var frequency = Math.Min(Bands[band], _sampleRate / 2f - 1f);
        for (var channel = 0; channel < _channels; channel++)
        {
            // Rebuilding the section resets its state; the click is masked by the
            // engine's short gain ramp in the output stage.
            _filters[band][channel] = new Biquad(_sampleRate, frequency, 1.41f, clamped);
        }
    }

    /// <summary>Applies a full preset.</summary>
    public void ApplyPreset(IReadOnlyList<float> gainsDb)
    {
        ArgumentNullException.ThrowIfNull(gainsDb);
        if (gainsDb.Count != Bands.Length)
            throw new ArgumentException($"Expected {Bands.Length} band gains.", nameof(gainsDb));

        for (var band = 0; band < Bands.Length; band++) SetBandGain(band, gainsDb[band]);
    }

    /// <summary>Processes an interleaved buffer in place.</summary>
    public void Apply(Span<float> interleaved)
    {
        if (IsFlat) return;

        var preamp = MathF.Pow(10f, _preampDb / 20f);

        for (var frame = 0; frame + _channels <= interleaved.Length; frame += _channels)
        {
            for (var channel = 0; channel < _channels; channel++)
            {
                var sample = interleaved[frame + channel] * preamp;
                for (var band = 0; band < Bands.Length; band++)
                {
                    sample = _filters[band][channel].Process(sample);
                }

                interleaved[frame + channel] = sample;
            }
        }
    }

    /// <summary>Clears all filter state.</summary>
    public void Reset()
    {
        for (var band = 0; band < Bands.Length; band++)
        {
            for (var channel = 0; channel < _channels; channel++) _filters[band][channel].Reset();
        }
    }
}
