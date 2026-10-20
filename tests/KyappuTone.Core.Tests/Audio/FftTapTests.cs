using KyappuTone.Core.Audio;
using Shouldly;
using Xunit;

namespace KyappuTone.Core.Tests.Audio;

public class FftTapTests
{
    private const int SampleRate = 48000;
    private const int FftSize = 2048;

    private static void PushSine(FftTap tap, float frequency, int frames = FftSize)
    {
        var interleaved = new float[frames * 2];
        for (var i = 0; i < frames; i++)
        {
            var sample = (float)(Math.Sin(2 * Math.PI * frequency * i / SampleRate) * 0.5);
            interleaved[i * 2] = sample;
            interleaved[i * 2 + 1] = sample;
        }

        tap.Push(interleaved);
    }

    [Fact]
    public void BinCount_IsHalfFftSize()
    {
        var tap = new FftTap(FftSize, SampleRate);
        tap.BinCount.ShouldBe(FftSize / 2);
    }

    [Fact]
    public void TryRead_ReturnsFalse_BeforeEnoughSamples()
    {
        var tap = new FftTap(FftSize, SampleRate);
        PushSine(tap, 1000f, frames: FftSize / 2);
        tap.TryRead(new float[tap.BinCount]).ShouldBeFalse();
    }

    [Fact]
    public void TryRead_PutsSinePeakInExpectedBin()
    {
        var tap = new FftTap(FftSize, SampleRate);
        PushSine(tap, 1000f);

        var magnitudes = new float[tap.BinCount];
        tap.TryRead(magnitudes).ShouldBeTrue();

        var peakBin = 0;
        for (var i = 1; i < magnitudes.Length; i++)
        {
            if (magnitudes[i] > magnitudes[peakBin]) peakBin = i;
        }

        // 1000 Hz * 2048 / 48000 = 42.67, so the peak sits in bin 42 or 43.
        peakBin.ShouldBeOneOf(42, 43);
    }

    [Fact]
    public void Silence_ProducesNearZeroMagnitudes()
    {
        var tap = new FftTap(FftSize, SampleRate);
        tap.Push(new float[FftSize * 2]);

        var magnitudes = new float[tap.BinCount];
        tap.TryRead(magnitudes).ShouldBeTrue();
        magnitudes.ShouldAllBe(m => m < 1e-3f);
    }
}
