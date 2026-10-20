using KyappuTone.Core.Metadata;
using Shouldly;
using Xunit;

namespace KyappuTone.Core.Tests.Metadata;

public class MetadataDiffBuilderTests
{
    private static MetadataValues Current(
        string title = "Blue Monday",
        string artist = "New Order",
        string album = "Power, Corruption & Lies",
        string year = "1983",
        string genre = "Synth-pop")
        => new(title, artist, album, year, genre);

    private static MetadataCandidate Candidate(
        string title = "Blue Monday",
        string artist = "New Order",
        string album = "Power, Corruption & Lies",
        int? year = 1983,
        string? genre = "Synth-pop")
        => new("recording-1", title, artist, album, year, genre, null, 100);

    [Fact]
    public void Only_reports_changed_or_missing_fields()
    {
        var diff = MetadataDiffBuilder.Build(
            Current(album: "Unknown Album", year: "Unknown Year"),
            Candidate());

        diff.HasChanges.ShouldBeTrue();
        diff.Changes.Select(change => change.Field).ShouldBe(new[] { "Album", "Year" });
    }

    [Fact]
    public void Does_not_replace_an_existing_value_with_an_empty_candidate()
    {
        var diff = MetadataDiffBuilder.Build(
            Current(album: "Existing Album"),
            Candidate(album: "", year: null, genre: null));

        diff.HasChanges.ShouldBeFalse();
        diff.Changes.ShouldBeEmpty();
    }

    [Fact]
    public void Treats_unknown_as_missing()
    {
        var diff = MetadataDiffBuilder.Build(
            Current(artist: "Unknown Artist"),
            Candidate(artist: "New Order"));

        diff.Changes.Count.ShouldBe(1);
        diff.Changes[0].Field.ShouldBe("Artist");
        diff.Changes[0].Current.ShouldBe("Unknown Artist");
        diff.Changes[0].Proposed.ShouldBe("New Order");
    }

    [Fact]
    public void Returns_no_changes_for_identical_values()
    {
        MetadataDiffBuilder.Build(Current(), Candidate()).HasChanges.ShouldBeFalse();
    }
}
