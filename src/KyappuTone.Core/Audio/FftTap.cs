namespace KyappuTone.Core.Audio;

/// <summary>
/// Single-producer/single-consumer FFT analyser. The audio thread pushes
/// interleaved stereo; the UI thread reads the most recent magnitude spectrum.
/// No locks: the producer publishes an immutable snapshot reference.
/// </summary>
public sealed class FftTap
{
    private readonly int _fftSize;
    private readonly double[] _samples;
    private readonly double[] _window;
    private int _fill;
    private float[]? _snapshot;

    /// <summary>Creates a tap for the given FFT size and sample rate.</summary>
    public FftTap(int fftSize = 2048, int sampleRate = 48000)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(fftSize, 256);
        ArgumentOutOfRangeException.ThrowIfLessThan(sampleRate, 8000);
        ArgumentOutOfRangeException.ThrowIfNotEqual((fftSize & (fftSize - 1)), 0);

        _fftSize = fftSize;
        _samples = new double[fftSize];
        _window = BuildHannWindow(fftSize);
        BinCount = fftSize / 2;
        SampleRate = sampleRate;
    }

    /// <summary>Number of usable magnitude bins.</summary>
    public int BinCount { get; }

    /// <summary>Sample rate the spectrum is computed at.</summary>
    public int SampleRate { get; }

    /// <summary>Pushes interleaved stereo samples; computes a spectrum whenever a full window is available.</summary>
    public void Push(ReadOnlySpan<float> interleavedStereo)
    {
        for (var i = 0; i + 1 < interleavedStereo.Length; i += 2)
        {
            _samples[_fill++] = (interleavedStereo[i] + interleavedStereo[i + 1]) * 0.5;
            if (_fill < _fftSize) continue;

            Publish();
            _fill = 0;
        }
    }

    /// <summary>Copies the latest spectrum. Returns false before the first full window.</summary>
    public bool TryRead(Span<float> magnitudes)
    {
        var snapshot = Volatile.Read(ref _snapshot);
        if (snapshot is null) return false;

        var count = Math.Min(snapshot.Length, magnitudes.Length);
        snapshot.AsSpan(0, count).CopyTo(magnitudes);
        return true;
    }

    private static double[] BuildHannWindow(int size)
    {
        var window = new double[size];
        for (var i = 0; i < size; i++)
        {
            window[i] = 0.5 * (1 - Math.Cos(2 * Math.PI * i / (size - 1)));
        }

        return window;
    }

    private void Publish()
    {
        var frame = new double[_fftSize];
        for (var i = 0; i < _fftSize; i++) frame[i] = _samples[i] * _window[i];

        var spectrum = FftSharp.FFT.Forward(frame);
        var magnitudes = new float[BinCount];
        for (var bin = 0; bin < BinCount; bin++)
        {
            magnitudes[bin] = (float)(spectrum[bin].Magnitude / _fftSize);
        }

        Volatile.Write(ref _snapshot, magnitudes);
    }
}
