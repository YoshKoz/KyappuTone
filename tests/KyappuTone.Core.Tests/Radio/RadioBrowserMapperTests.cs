using KyappuTone.Core.Radio;
using Shouldly;
using Xunit;

namespace KyappuTone.Core.Tests.Radio;

public class RadioBrowserMapperTests
{
    [Fact]
    public void Parses_station_name_url_tags_and_bitrate()
    {
        const string json = """
            [
              {
                "stationuuid": "uuid-1",
                "name": "Test Radio",
                "url": "http://stream.example/radio",
                "url_resolved": "https://stream.example/radio",
                "favicon": "https://stream.example/icon.png",
                "tags": "jazz, smooth",
                "country": "Netherlands",
                "codec": "MP3",
                "bitrate": 192
              }
            ]
            """;

        var stations = RadioBrowserMapper.ParseStations(json);

        stations.Count.ShouldBe(1);
        var station = stations[0];
        station.Uuid.ShouldBe("uuid-1");
        station.Name.ShouldBe("Test Radio");
        station.Url.ShouldBe("https://stream.example/radio");
        station.FaviconUrl.ShouldBe("https://stream.example/icon.png");
        station.Tags.ShouldBe("jazz, smooth");
        station.Country.ShouldBe("Netherlands");
        station.Codec.ShouldBe("MP3");
        station.Bitrate.ShouldBe(192);
    }

    [Fact]
    public void Skips_stations_without_a_resolvable_url()
    {
        const string json = """
            [
              { "stationuuid": "uuid-1", "name": "No URL" },
              { "stationuuid": "uuid-2", "name": "Bad URL", "url": "not-a-url" }
            ]
            """;

        RadioBrowserMapper.ParseStations(json).ShouldBeEmpty();
    }

    [Fact]
    public void Prefers_https_url_when_present()
    {
        const string json = """
            [
              {
                "stationuuid": "uuid-1",
                "name": "Secure",
                "url": "http://stream.example/radio",
                "url_resolved": "https://stream.example/radio"
              }
            ]
            """;

        RadioBrowserMapper.ParseStations(json)[0].Url.ShouldBe("https://stream.example/radio");
    }
}
