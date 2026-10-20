using KyappuTone.Core.Acquisition;
using Shouldly;
using Xunit;

namespace KyappuTone.Core.Tests.Acquisition;

public class YtDlpOutputParserTests
{
    [Fact]
    public void Parses_flat_search_entries()
    {
        const string json = """
            {
              "entries": [
                {
                  "id": "abc123",
                  "title": "Test Track",
                  "uploader": "Test Channel",
                  "duration": 212.5,
                  "url": "https://www.youtube.com/watch?v=abc123",
                  "thumbnails": [ { "url": "https://img.example/abc.jpg" } ]
                }
              ]
            }
            """;

        var results = YtDlpOutputParser.ParseSearch(json);

        results.Count.ShouldBe(1);
        results[0].Id.ShouldBe("abc123");
        results[0].Title.ShouldBe("Test Track");
        results[0].Uploader.ShouldBe("Test Channel");
        results[0].Duration.ShouldBe(212.5);
        results[0].Url.ShouldBe("https://www.youtube.com/watch?v=abc123");
        results[0].ThumbnailUrl.ShouldBe("https://img.example/abc.jpg");
    }

    [Fact]
    public void Skips_entries_without_an_id_or_title()
    {
        const string json = """
            {
              "entries": [
                { "id": "abc123" },
                { "title": "No id" }
              ]
            }
            """;

        YtDlpOutputParser.ParseSearch(json).ShouldBeEmpty();
    }

    [Fact]
    public void Parses_percent_speed_and_eta()
    {
        var progress = YtDlpOutputParser.ParseProgress("download:  45.6%|2.50MiB/s|00:12");

        progress.ShouldNotBeNull();
        progress.Percent.ShouldBe(45.6);
        progress.Speed.ShouldBe("2.50MiB/s");
        progress.Eta.ShouldBe("00:12");
    }

    [Fact]
    public void Ignores_non_progress_lines()
    {
        YtDlpOutputParser.ParseProgress("[youtube] Extracting URL").ShouldBeNull();
    }
}
