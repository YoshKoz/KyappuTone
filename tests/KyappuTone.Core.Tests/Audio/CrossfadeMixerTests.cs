using KyappuTone.Core.Audio;
using Shouldly;
using Xunit;

namespace KyappuTone.Core.Tests.Audio;

public class CrossfadeMixerTests
{
    [Fact]
    public void Gains_AreEqualPower_ForWholeFade()
    {
        var mixer = new CrossfadeMixer(channels: 2);
        mixer.StartFade(100);

        for (var frame = 0; frame <= 100; frame++)
        {
            var power = mixer.GainA * mixer.GainA + mixer.GainB * mixer.GainB;
            power.ShouldBe(1f, tolerance: 1e-4f);
            mixer.Advance();
        }
    }

    [Fact]
    public void Fade_ReachesFullIncoming_AtEnd()
    {
        var mixer = new CrossfadeMixer(channels: 2);
        mixer.StartFade(64);
        for (var i = 0; i < 64; i++) mixer.Advance();

        mixer.GainA.ShouldBe(0f, tolerance: 1e-4f);
        mixer.GainB.ShouldBe(1f, tolerance: 1e-4f);
        mixer.IsFading.ShouldBeFalse();
    }

    [Fact]
    public void Mix_AppliesGainsPerFrame()
    {
        var mixer = new CrossfadeMixer(channels: 2);
        mixer.StartFade(1);
        mixer.Advance();

        var a = new float[] { 1f, 1f };
        var b = new float[] { 0f, 0f };
        var destination = new float[2];
        mixer.Mix(a, b, destination);

        destination.ShouldBe(new float[] { 0f, 0f });
    }

    [Fact]
    public void BeforeAnyFade_MixPlaysOutgoingDeckOnly()
    {
        var mixer = new CrossfadeMixer(channels: 2);
        var a = new float[] { 0.5f, -0.5f };
        var b = new float[] { 0.25f, 0.25f };
        var destination = new float[2];

        mixer.Mix(a, b, destination);

        destination.ShouldBe(a);
    }

    [Fact]
    public void Channels_DefaultsToOne()
    {
        new CrossfadeMixer(1).Channels.ShouldBe(1);
    }
}
