using KyappuTone.Core.Integrations.Spotify;
using Shouldly;
using Xunit;

namespace KyappuTone.Core.Tests.Integrations;

public class TrackMatchIndexTests
{
    private static TrackMatchIndex CreateIndex(params TrackCandidate[] candidates) => new(candidates);

    [Fact]
    public void Find_PrefersTitleAndArtistExactMatch()
    {
        var index = CreateIndex(
            new TrackCandidate("a.flac", "Song", "Artist"),
            new TrackCandidate("b.flac", "Song", "Other"));

        index.Find("Song", "Artist")!.Path.ShouldBe("a.flac");
    }

    [Fact]
    public void Find_IgnoresRemasterAndFeaturedSuffixes()
    {
        var index = CreateIndex(new TrackCandidate("a.flac", "Song (2011 Remaster)", "Artist feat. Guest"));

        index.Find("Song", "Artist")!.Path.ShouldBe("a.flac");
    }

    [Fact]
    public void Find_IgnoresDashVersionSuffix()
    {
        var index = CreateIndex(new TrackCandidate("a.flac", "Song - Live", "Artist"));

        index.Find("Song", "Artist")!.Path.ShouldBe("a.flac");
    }

    [Fact]
    public void Find_UsesArtistTokensWhenTitleIsAmbiguous()
    {
        var index = CreateIndex(
            new TrackCandidate("a.flac", "Intro", "Alpha"),
            new TrackCandidate("b.flac", "Intro", "Beta"));

        index.Find("Intro", "Alpha")!.Path.ShouldBe("a.flac");
    }

    [Fact]
    public void Find_ReturnsNullWhenAmbiguousWithoutArtist()
    {
        var index = CreateIndex(
            new TrackCandidate("a.flac", "Intro", "Alpha"),
            new TrackCandidate("b.flac", "Intro", "Beta"));

        index.Find("Intro", null).ShouldBeNull();
    }

    [Fact]
    public void Find_ReturnsNullWhenNothingMatches()
    {
        var index = CreateIndex(new TrackCandidate("a.flac", "Song", "Artist"));

        index.Find("Different", "Artist").ShouldBeNull();
    }
}
