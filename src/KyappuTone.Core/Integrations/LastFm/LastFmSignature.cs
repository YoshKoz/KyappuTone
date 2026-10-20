using System.Security.Cryptography;
using System.Text;

namespace KyappuTone.Core.Integrations.LastFm;

/// <summary>
/// Builds Last.fm API signatures: MD5 over alphabetically sorted parameter names and
/// values followed by the API secret. <c>format</c> and <c>callback</c> are not signed.
/// </summary>
public static class LastFmSignature
{
    public static string Sign(IEnumerable<KeyValuePair<string, string>> parameters, string apiSecret)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(apiSecret);

        var builder = new StringBuilder();
        foreach (var pair in parameters
                     .Where(pair => !string.Equals(pair.Key, "format", StringComparison.Ordinal)
                                    && !string.Equals(pair.Key, "callback", StringComparison.Ordinal))
                     .OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            builder.Append(pair.Key).Append(pair.Value);
        }

        builder.Append(apiSecret);
        var hash = MD5.HashData(Encoding.UTF8.GetBytes(builder.ToString()));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
