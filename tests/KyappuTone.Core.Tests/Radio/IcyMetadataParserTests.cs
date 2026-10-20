using System.Text;
using KyappuTone.Core.Radio;
using Shouldly;
using Xunit;

namespace KyappuTone.Core.Tests.Radio;

public class IcyMetadataParserTests
{
    [Fact]
    public void Parses_stream_title_from_icy_metadata()
    {
        var metadata = Encoding.UTF8.GetBytes("StreamTitle='Artist - Song';");

        IcyMetadataParser.TryParseTitle(metadata).ShouldBe("Artist - Song");
    }

    [Fact]
    public void Returns_null_for_empty_metadata()
    {
        IcyMetadataParser.TryParseTitle(Array.Empty<byte>()).ShouldBeNull();
        IcyMetadataParser.TryParseTitle(Encoding.UTF8.GetBytes("\0\0\0")).ShouldBeNull();
    }

    [Fact]
    public void Handles_artist_dash_title_text_verbatim()
    {
        var metadata = Encoding.UTF8.GetBytes("StreamTitle='A - B - C';padding\0");

        IcyMetadataParser.TryParseTitle(metadata).ShouldBe("A - B - C");
    }
}
