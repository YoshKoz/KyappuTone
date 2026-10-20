using KyappuTone.Core.Audio;
using Shouldly;
using Xunit;

namespace KyappuTone.Core.Tests.Audio;

public class SoftLimiterTests
{
    [Fact]
    public void KeepsSamplesWithinCeiling()
    {
        var limiter = new SoftLimiter();
        var buffer = new float[4096];
        var random = new Random(11);
        for (var i = 0; i < buffer.Length; i++) buffer[i] = (float)(random.NextDouble() * 3 - 1.5);

        limiter.Process(buffer);

        foreach (var sample in buffer)
        {
            Math.Abs(sample).ShouldBeLessThanOrEqualTo(limiter.Ceiling + 1e-6f);
            float.IsNaN(sample).ShouldBeFalse();
        }
    }

    [Fact]
    public void LeavesQuietSignalUnchanged()
    {
        var limiter = new SoftLimiter();
        var buffer = new float[] { 0.1f, -0.2f, 0.05f, -0.15f };
        var copy = buffer.ToArray();

        limiter.Process(buffer);

        buffer.ShouldBe(copy);
    }

    [Fact]
    public void IsMemoriless_AcrossCalls()
    {
        var limiter = new SoftLimiter();
        var loud = new float[] { 1.4f, -1.4f, 1.4f, -1.4f };
        limiter.Process(loud);

        var quiet = new float[] { 0.1f, 0.1f };
        var copy = quiet.ToArray();
        limiter.Process(quiet);

        quiet.ShouldBe(copy);
    }

    [Fact]
    public void Ceiling_IsClampedToUnitRange()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new SoftLimiter(ceiling: 1.5f));
    }
}
