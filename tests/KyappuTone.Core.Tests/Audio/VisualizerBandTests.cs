using KyappuTone.Core.Audio;
using Shouldly;
using Xunit;

namespace KyappuTone.Core.Tests.Audio;

public class VisualizerBandTests
{
    [Theory]
    [InlineData(2048)]
    [InlineData(4096)]
    [InlineData(8192)]
    public void One_Kilohertz_Maps_To_Same_Bar_For_Every_Fft_Size(int fftSize)
    {
        const int sampleRate = 48000;
        const int barCount = 64;
        var magnitudes = new float[fftSize / 2];
        var bars = new float[barCount];

        var bin = (int)MathF.Round(1000f * fftSize / sampleRate);
        magnitudes[bin] = 1f;

        VisualizerBands.Map(magnitudes, sampleRate, barCount, bars);

        var peak = Array.IndexOf(bars, bars.Max());
        peak.ShouldBe(VisualizerBands.BarIndexForFrequency(1000f, barCount));
    }

    [Fact]
    public void Map_Puts_A_One_Kilohertz_Tone_In_The_Expected_Bar()
    {
        const int sampleRate = 48000;
        const int fftSize = 2048;
        const int barCount = 64;
        var magnitudes = new float[fftSize / 2];
        var bars = new float[barCount];

        var bin = (int)MathF.Round(1000f * fftSize / sampleRate);
        magnitudes[bin] = 0.5f;

        VisualizerBands.Map(magnitudes, sampleRate, barCount, bars);

        var expectedBar = VisualizerBands.BarIndexForFrequency(1000f, barCount);
        bars[expectedBar].ShouldBe(0.5f, tolerance: 1e-4f);
        bars.Count(value => value > 0f).ShouldBe(1);
    }

    [Fact]
    public void Map_Keeps_The_Strongest_Bin_Per_Bar()
    {
        const int sampleRate = 48000;
        const int fftSize = 4096;
        const int barCount = 32;
        var magnitudes = new float[fftSize / 2];
        var bars = new float[barCount];

        var bar = VisualizerBands.BarIndexForFrequency(440f, barCount);
        var lowBin = (int)MathF.Round(430f * fftSize / sampleRate);
        var highBin = (int)MathF.Round(445f * fftSize / sampleRate);
        magnitudes[lowBin] = 0.2f;
        magnitudes[highBin] = 0.6f;

        VisualizerBands.Map(magnitudes, sampleRate, barCount, bars);

        bars[bar].ShouldBe(0.6f, tolerance: 1e-4f);
    }

    [Fact]
    public void Map_Clears_Destination_And_Ignores_Out_Of_Range_Bins()
    {
        const int sampleRate = 48000;
        const int fftSize = 2048;
        const int barCount = 16;
        var magnitudes = new float[fftSize / 2];
        var bars = Enumerable.Repeat(9f, barCount).ToArray();

        magnitudes[0] = 10f;      // DC
        magnitudes[1] = 0.9f;     // ~23 Hz, in range
        magnitudes[^1] = 10f;     // ~24 kHz, out of range

        VisualizerBands.Map(magnitudes, sampleRate, barCount, bars);

        bars.ShouldAllBe(value => value <= 1f);
        bars[0].ShouldBe(0.9f, tolerance: 1e-4f);
    }

    [Fact]
    public void Bar_Index_Clamps_At_Both_Ends()
    {
        VisualizerBands.BarIndexForFrequency(1f, 32).ShouldBe(0);
        VisualizerBands.BarIndexForFrequency(96000f, 32).ShouldBe(31);
    }
}
