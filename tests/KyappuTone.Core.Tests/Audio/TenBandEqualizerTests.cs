using KyappuTone.Core.Audio;
using Shouldly;
using Xunit;

namespace KyappuTone.Core.Tests.Audio;

public class TenBandEqualizerTests
{
    private const int SampleRate = 48000;
    private const int Channels = 2;

    private static float[] MakeNoise(int frames, int seed = 7)
    {
        var random = new Random(seed);
        var buffer = new float[frames * Channels];
        for (var i = 0; i < buffer.Length; i++) buffer[i] = (float)(random.NextDouble() * 2 - 1);
        return buffer;
    }

    [Fact]
    public void CenterFrequencies_AreTheTenSpecifiedBands()
    {
        TenBandEqualizer.CenterFrequencies.ShouldBe(new float[]
        {
            31.5f, 63f, 125f, 250f, 500f, 1000f, 2000f, 4000f, 8000f, 16000f,
        });
    }

    [Fact]
    public void FlatEq_LeavesSignalUnchanged()
    {
        var eq = new TenBandEqualizer(SampleRate, Channels);
        var buffer = MakeNoise(1024);
        var copy = buffer.ToArray();

        eq.Apply(buffer);

        buffer.ShouldBe(copy);
    }

    [Fact]
    public void SetBandGain_ClampsToMinusTwelve_PlusTwelve()
    {
        var eq = new TenBandEqualizer(SampleRate, Channels);

        eq.SetBandGain(0, 40f);
        eq.SetBandGain(1, -40f);

        eq.BandGainsDb[0].ShouldBe(12f);
        eq.BandGainsDb[1].ShouldBe(-12f);
    }

    [Fact]
    public void Apply_WithBoost_ChangesSignal()
    {
        var eq = new TenBandEqualizer(SampleRate, Channels);
        eq.SetBandGain(5, 12f);
        var buffer = MakeNoise(4096);
        var copy = buffer.ToArray();

        eq.Apply(buffer);

        buffer.ShouldNotBe(copy);
    }

    [Fact]
    public void Apply_KeepsChannelsIndependent()
    {
        var eq = new TenBandEqualizer(SampleRate, Channels);
        eq.SetBandGain(0, 6f);
        var buffer = new float[512 * Channels];
        for (var i = 0; i < 512; i++)
        {
            buffer[i * 2] = 0.5f;
            buffer[i * 2 + 1] = -0.5f;
        }

        eq.Apply(buffer);

        for (var i = 0; i < 512; i++)
        {
            buffer[i * 2].ShouldBe(-buffer[i * 2 + 1], tolerance: 1e-6f);
        }
    }

    [Fact]
    public void Presets_ContainThirteenEntries_WithTenBandsEach()
    {
        EqPreset.All.Count.ShouldBe(13);
        foreach (var preset in EqPreset.All)
        {
            preset.Gains.Length.ShouldBe(10, preset.Name);
        }

        EqPreset.All[0].Name.ShouldBe("Flat");
        EqPreset.All[0].Gains.ShouldAllBe(g => g == 0f);
    }

    [Fact]
    public void ApplyPreset_SetsEveryBand()
    {
        var eq = new TenBandEqualizer(SampleRate, Channels);
        var rock = EqPreset.All.First(p => p.Name == "Rock");

        eq.ApplyPreset(rock.Gains);

        eq.BandGainsDb.ShouldBe(rock.Gains);
    }
}
