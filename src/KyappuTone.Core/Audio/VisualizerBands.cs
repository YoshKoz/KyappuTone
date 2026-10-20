namespace KyappuTone.Core.Audio;

/// <summary>
/// Maps FFT bins to log-spaced visualizer bars. Frequency is the only input to
/// the bar index, so the same tone lands on the same bar for any FFT size or
/// sample rate.
/// </summary>
public static class VisualizerBands
{
    /// <summary>Lowest frequency represented by the bar layout.</summary>
    public const float MinFrequency = 20f;

    /// <summary>Highest frequency represented by the bar layout.</summary>
    public const float MaxFrequency = 20000f;

    /// <summary>Returns the log-spaced bar index for a frequency.</summary>
    public static int BarIndexForFrequency(float frequency, int barCount)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(barCount, 1);

        var clamped = Math.Clamp(frequency, MinFrequency, MaxFrequency);
        var normalized = MathF.Log(clamped / MinFrequency) / MathF.Log(MaxFrequency / MinFrequency);
        return Math.Clamp((int)(normalized * barCount), 0, barCount - 1);
    }

    /// <summary>
    /// Folds half-spectrum magnitudes into <paramref name="barCount"/> bars,
    /// keeping the strongest bin that falls inside each bar. Values are clamped
    /// to 0..1 and the destination is cleared first.
    /// </summary>
    public static void Map(ReadOnlySpan<float> magnitudes, int sampleRate, int barCount, Span<float> destination)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(sampleRate, 8000);
        ArgumentOutOfRangeException.ThrowIfLessThan(barCount, 1);
        if (destination.Length < barCount)
            throw new ArgumentException($"Destination needs at least {barCount} slots.", nameof(destination));

        destination[..barCount].Clear();
        if (magnitudes.Length == 0) return;

        var fftSize = magnitudes.Length * 2;
        for (var bin = 1; bin < magnitudes.Length; bin++)
        {
            var frequency = bin * (float)sampleRate / fftSize;
            if (frequency < MinFrequency || frequency > MaxFrequency) continue;

            var value = Math.Clamp(magnitudes[bin], 0f, 1f);
            var index = BarIndexForFrequency(frequency, barCount);
            if (value > destination[index]) destination[index] = value;
        }
    }
}
