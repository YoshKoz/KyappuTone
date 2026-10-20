using KyappuTone.Core.Integrations.GitHub;
using Shouldly;
using Xunit;

namespace KyappuTone.Core.Tests.Integrations;

public class GitHubReleaseParserTests
{
    [Fact]
    public void Parses_a_published_release()
    {
        const string json = """
            {
              "tag_name": "v0.4.0",
              "html_url": "https://github.com/example/releases/v0.4.0",
              "body": "Release notes",
              "draft": false,
              "prerelease": false
            }
            """;

        var release = GitHubReleaseParser.Parse(json);

        release.ShouldNotBeNull();
        release.Version.ShouldBe(new Version(0, 4, 0));
        release.Tag.ShouldBe("v0.4.0");
        release.ReleaseUrl.ShouldBe("https://github.com/example/releases/v0.4.0");
        release.Notes.ShouldBe("Release notes");
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void Rejects_draft_and_prerelease(bool draft, bool prerelease)
    {
        var json = $$"""
            {
              "tag_name": "v0.4.0",
              "html_url": "https://github.com/example/releases/v0.4.0",
              "body": "",
              "draft": {{draft.ToString().ToLowerInvariant()}},
              "prerelease": {{prerelease.ToString().ToLowerInvariant()}}
            }
            """;

        GitHubReleaseParser.Parse(json).ShouldBeNull();
    }

    [Theory]
    [InlineData("""{"html_url":"https://example.test","body":""}""")]
    [InlineData("""{"tag_name":"v0.4.0","body":""}""")]
    [InlineData("""{"tag_name":"not-a-version","html_url":"https://example.test"}""")]
    public void Rejects_missing_or_invalid_fields(string json)
    {
        GitHubReleaseParser.Parse(json).ShouldBeNull();
    }

    [Fact]
    public void Accepts_v_prefixed_semantic_tag()
    {
        const string json = """
            {
              "tag_name": "V1.2.3",
              "html_url": "https://example.test/release",
              "body": null,
              "draft": false,
              "prerelease": false
            }
            """;

        var release = GitHubReleaseParser.Parse(json);

        release.ShouldNotBeNull();
        release.Version.ShouldBe(new Version(1, 2, 3));
        release.Notes.ShouldBeEmpty();
    }
}
