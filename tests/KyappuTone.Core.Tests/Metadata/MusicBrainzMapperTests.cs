using KyappuTone.Core.Metadata;
using Shouldly;
using Xunit;

namespace KyappuTone.Core.Tests.Metadata;

public class MusicBrainzMapperTests
{
    private const string RecordingJson = """
        {
          "count": 1,
          "recordings": [
            {
              "id": "recording-1",
              "score": 100,
              "title": "Blue Monday",
              "artist-credit": [
                { "name": "New Order", "artist": { "id": "artist-1", "name": "New Order" } }
              ],
              "releases": [
                {
                  "id": "release-1",
                  "title": "Power, Corruption & Lies",
                  "date": "1983-05-02",
                  "release-group": { "id": "group-1", "title": "Power, Corruption & Lies" }
                }
              ],
              "tags": [ { "count": 3, "name": "synth-pop" } ]
            }
          ]
        }
        """;

    [Fact]
    public void Parses_recording_title_artist_release_and_year()
    {
        var candidates = MusicBrainzMapper.ParseRecordings(RecordingJson);

        candidates.Count.ShouldBe(1);
        var candidate = candidates[0];
        candidate.RecordingId.ShouldBe("recording-1");
        candidate.Title.ShouldBe("Blue Monday");
        candidate.Artist.ShouldBe("New Order");
        candidate.Album.ShouldBe("Power, Corruption & Lies");
        candidate.Year.ShouldBe(1983);
        candidate.Genre.ShouldBe("synth-pop");
        candidate.Score.ShouldBe(100);
    }

    [Fact]
    public void Ignores_recordings_without_a_title()
    {
        const string json = """
            {
              "recordings": [
                { "id": "recording-1", "artist-credit": [ { "name": "Artist" } ] }
              ]
            }
            """;

        MusicBrainzMapper.ParseRecordings(json).ShouldBeEmpty();
    }

    [Fact]
    public void Builds_cover_art_archive_url_from_release_id()
    {
        var candidate = MusicBrainzMapper.ParseRecordings(RecordingJson)[0];

        candidate.CoverUrl.ShouldBe("https://coverartarchive.org/release/release-1/front-500");
    }

    [Fact]
    public void Maps_empty_genres_to_null()
    {
        const string json = """
            {
              "recordings": [
                {
                  "id": "recording-1",
                  "title": "Song",
                  "artist-credit": [ { "name": "Artist" } ],
                  "releases": [ { "id": "release-1", "title": "Album" } ],
                  "tags": []
                }
              ]
            }
            """;

        MusicBrainzMapper.ParseRecordings(json)[0].Genre.ShouldBeNull();
    }

    [Fact]
    public void Returns_no_album_year_or_cover_when_release_is_missing()
    {
        const string json = """
            {
              "recordings": [
                {
                  "id": "recording-1",
                  "title": "Song",
                  "artist-credit": [ { "name": "Artist" } ]
                }
              ]
            }
            """;

        var candidate = MusicBrainzMapper.ParseRecordings(json)[0];

        candidate.Album.ShouldBe(string.Empty);
        candidate.Year.ShouldBeNull();
        candidate.CoverUrl.ShouldBeNull();
    }
}
