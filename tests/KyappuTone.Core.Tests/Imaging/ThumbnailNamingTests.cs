using KyappuTone.Core.Imaging;
using Shouldly;
using Xunit;

namespace KyappuTone.Core.Tests.Imaging;

public class ThumbnailNamingTests
{
    [Fact]
    public void NewCoverFileName_IsUniqueUnderParallelCalls()
    {
        var names = new string[5000];
        Parallel.For(0, names.Length, i => names[i] = ThumbnailNaming.NewCoverFileName());

        names.Distinct().Count().ShouldBe(names.Length);
    }

    [Fact]
    public void NewCoverFileName_EndsWithPngAndCoverPrefix()
    {
        var name = ThumbnailNaming.NewCoverFileName();
        name.ShouldStartWith("Cover_");
        name.ShouldEndWith(".png");
    }
}
