using KyappuTone.Core.Audio;
using Shouldly;
using Xunit;

namespace KyappuTone.Core.Tests.Audio;

public class BiquadTests
{
    private const int SampleRate = 48000;

    private static float[] RunSine(Biquad biquad, float frequency, int frames = 4800)
    {
        var output = new float[frames];
        for (var i = 0; i < frames; i++)
        {
            output[i] = biquad.Process((float)Math.Sin(2 * Math.PI * frequency * i / SampleRate));
        }

        return output;
    }

    private static float Amplitude(float[] samples, int skip = 2400)
    {
        var peak = 0f;
        for (var i = skip; i < samples.Length; i++) peak = Math.Max(peak, Math.Abs(samples[i]));
        return peak;
    }

    [Theory]
    [InlineData(1000f, 6f)]
    [InlineData(1000f, -9f)]
    [InlineData(4000f, 12f)]
    public void PeakingFilter_AtCenterFrequency_HasExpectedGain(float frequency, float gainDb)
    {
        var biquad = new Biquad(SampleRate, frequency, 1.41f, gainDb);
        var expected = (float)Math.Pow(10, gainDb / 20.0);

        Amplitude(RunSine(biquad, frequency)).ShouldBe(expected, tolerance: 0.02f);
        biquad.ResponseAt(frequency).Magnitude.ShouldBe(expected, tolerance: 0.02f);
    }

    [Fact]
    public void ZeroGain_IsTransparent()
    {
        var biquad = new Biquad(SampleRate, 1000f, 1.41f, 0f);
        var input = new float[512];
        for (var i = 0; i < input.Length; i++) input[i] = (float)Math.Sin(i * 0.05);

        var output = new float[input.Length];
        for (var i = 0; i < input.Length; i++) output[i] = biquad.Process(input[i]);

        output.ShouldBe(input);
    }

    [Fact]
    public void ResponseAt_MatchesMeasuredSineAmplitude()
    {
        var biquad = new Biquad(SampleRate, 250f, 1.41f, 8f);
        var measured = Amplitude(RunSine(biquad, 250f));
        var predicted = biquad.ResponseAt(250f).Magnitude;

        measured.ShouldBe(predicted, tolerance: 0.02f);
    }

    [Fact]
    public void Reset_ClearsFilterState()
    {
        var biquad = new Biquad(SampleRate, 1000f, 1.41f, 6f);
        RunSine(biquad, 1000f, frames: 256);
        biquad.Reset();

        biquad.Process(0f).ShouldBe(0f, tolerance: 1e-6f);
        biquad.Process(0f).ShouldBe(0f, tolerance: 1e-6f);
    }
}
