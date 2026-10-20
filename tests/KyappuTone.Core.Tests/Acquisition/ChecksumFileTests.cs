using KyappuTone.Core.Acquisition;
using Shouldly;
using Xunit;

namespace KyappuTone.Core.Tests.Acquisition;

public class ChecksumFileTests
{
    [Fact]
    public void Finds_a_sha256_for_an_exact_file_name()
    {
        const string checksums = """
            # comment
            abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789  yt-dlp.exe
            1111111111111111111111111111111111111111111111111111111111111111  ffmpeg.zip
            """;

        ChecksumFile.TryFind(checksums, "yt-dlp.exe", out var hash).ShouldBeTrue();
        hash.ShouldBe("abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789");
    }

    [Fact]
    public void Ignores_comments_and_blank_lines()
    {
        const string checksums = """

            # only a comment
            2222222222222222222222222222222222222222222222222222222222222222 *tool.exe

            """;

        ChecksumFile.TryFind(checksums, "tool.exe", out var hash).ShouldBeTrue();
        hash.ShouldBe("2222222222222222222222222222222222222222222222222222222222222222");
    }

    [Fact]
    public void Rejects_a_mismatched_file_name()
    {
        const string checksums = "3333333333333333333333333333333333333333333333333333333333333333  other.exe";

        ChecksumFile.TryFind(checksums, "tool.exe", out _).ShouldBeFalse();
    }
}
