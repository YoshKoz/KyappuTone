using KyappuTone.Core.Acquisition;
using Shouldly;
using Xunit;

namespace KyappuTone.Core.Tests.Acquisition;

public class SafeArchiveTests
{
    [Fact]
    public void Accepts_paths_below_the_destination()
    {
        SafeArchive.IsSafeEntry(@"C:\Tools", @"ffmpeg\bin\ffmpeg.exe").ShouldBeTrue();
    }

    [Fact]
    public void Rejects_parent_directory_escape()
    {
        SafeArchive.IsSafeEntry(@"C:\Tools", @"..\evil.exe").ShouldBeFalse();
        SafeArchive.IsSafeEntry(@"C:\Tools", @"ffmpeg\..\..\evil.exe").ShouldBeFalse();
    }

    [Fact]
    public void Rejects_rooted_paths()
    {
        SafeArchive.IsSafeEntry(@"C:\Tools", @"C:\Windows\evil.exe").ShouldBeFalse();
        SafeArchive.IsSafeEntry(@"C:\Tools", "/etc/passwd").ShouldBeFalse();
    }
}
