using KyappuTone.Core.Audio;
using Shouldly;
using Xunit;

namespace KyappuTone.Core.Tests.Audio;

public class EqPresetTests
{
    [Fact]
    public void Serialize_Parse_RoundTrips_All_Settings()
    {
        var settings = new EqSettings
        {
            Enabled = false,
            PreampDb = -3.5f
        };
        settings.LoadPreset("Rock");
        settings.SetBandGain(0, 6f);
        settings.SetBandGain(9, -2f);

        var serialized = settings.Serialize();
        var restored = EqSettings.Parse(serialized);

        restored.Enabled.ShouldBeFalse();
        restored.PreampDb.ShouldBe(-3.5f, tolerance: 0.001f);
        restored.ActivePreset.ShouldBe("Custom");
        restored.BandGainsDb[0].ShouldBe(6f, tolerance: 0.001f);
        restored.BandGainsDb[9].ShouldBe(-2f, tolerance: 0.001f);
        restored.BandGainsDb.Count.ShouldBe(10);
    }

    [Theory]
    [InlineData("Flat")]
    [InlineData("Rock")]
    [InlineData("Bass Boost")]
    [InlineData("Treble Boost")]
    public void LoadPreset_Copies_Every_Band(string name)
    {
        var settings = new EqSettings();
        settings.LoadPreset("Flat");

        settings.LoadPreset(name);

        var expected = EqPreset.All.Single(preset => preset.Name == name).Gains;
        settings.ActivePreset.ShouldBe(name);
        settings.BandGainsDb.ShouldBe(expected);
    }

    [Fact]
    public void ApplyTo_Sets_All_Ten_Bands_And_Preamp()
    {
        var settings = new EqSettings { PreampDb = 2.5f };
        settings.LoadPreset("Vocal");
        var equalizer = new TenBandEqualizer(48000, 2);

        settings.ApplyTo(equalizer);

        equalizer.PreampDb.ShouldBe(2.5f, tolerance: 0.001f);
        equalizer.BandGainsDb.ShouldBe(EqPreset.All.Single(p => p.Name == "Vocal").Gains);
        equalizer.IsFlat.ShouldBeFalse();
    }

    [Fact]
    public void ApplyTo_When_Disabled_Is_Flat()
    {
        var settings = new EqSettings { Enabled = false, PreampDb = 6f };
        settings.LoadPreset("Rock");
        var equalizer = new TenBandEqualizer(48000, 2);

        settings.ApplyTo(equalizer);

        equalizer.PreampDb.ShouldBe(0f);
        equalizer.BandGainsDb.ShouldAllBe(gain => gain == 0f);
        equalizer.IsFlat.ShouldBeTrue();
    }

    [Fact]
    public void Parse_Invalid_Text_Returns_Defaults()
    {
        var settings = EqSettings.Parse("not-a-valid-payload");

        settings.Enabled.ShouldBeTrue();
        settings.ActivePreset.ShouldBe("Flat");
        settings.PreampDb.ShouldBe(0f);
        settings.BandGainsDb.ShouldAllBe(gain => gain == 0f);
    }

    [Fact]
    public void SetBandGain_Clamps_And_Switches_To_Custom()
    {
        var settings = new EqSettings();
        settings.LoadPreset("Pop");

        settings.SetBandGain(4, 99f);
        settings.SetBandGain(5, -99f);

        settings.ActivePreset.ShouldBe("Custom");
        settings.BandGainsDb[4].ShouldBe(12f);
        settings.BandGainsDb[5].ShouldBe(-12f);
    }

    [Fact]
    public void PresetNames_Contains_Flat_First_And_Custom_Last()
    {
        EqSettings.PresetNames[0].ShouldBe("Flat");
        EqSettings.PresetNames[^1].ShouldBe("Custom");
        EqSettings.PresetNames.Count.ShouldBe(EqPreset.All.Count);
    }
}
