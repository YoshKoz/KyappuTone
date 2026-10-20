using KyappuTone.Core.Integrations.LastFm;
using Shouldly;
using Xunit;

namespace KyappuTone.Core.Tests.Integrations;

public class LastFmSignatureTests
{
    [Fact]
    public void Sign_MatchesKnownLastFmVector()
    {
        var parameters = new Dictionary<string, string>
        {
            ["method"] = "auth.getToken",
            ["api_key"] = "abc",
        };

        LastFmSignature.Sign(parameters, "secret")
            .ShouldBe("f86444211049e605f18c05a5964aabfc");
    }

    [Fact]
    public void Sign_SortsByNameAndIgnoresFormatAndCallback()
    {
        var unordered = new Dictionary<string, string>
        {
            ["format"] = "json",
            ["method"] = "auth.getToken",
            ["callback"] = "ignored",
            ["api_key"] = "abc",
        };

        LastFmSignature.Sign(unordered, "secret")
            .ShouldBe("f86444211049e605f18c05a5964aabfc");
    }

    [Fact]
    public void Sign_ChangesWhenASignedValueChanges()
    {
        var parameters = new Dictionary<string, string>
        {
            ["method"] = "auth.getToken",
            ["api_key"] = "abc",
        };

        LastFmSignature.Sign(parameters, "secret")
            .ShouldNotBe(LastFmSignature.Sign(parameters, "other-secret"));
    }
}
