using System.Globalization;
using KyappuTone.Core.Radio;

namespace KyappuTone.Common.Services;

/// <summary>
/// Reads one ICY metadata block from a live stream. The connection is closed immediately after
/// the block, so this costs only a small amount of bandwidth per poll.
/// </summary>
public static class IcyMetadataService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(12) };

    public static async Task<string?> GetCurrentTitleAsync(
        string url,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation("Icy-MetaData", "1");
            request.Headers.UserAgent.ParseAdd("KyappuTone/0.3.0");

            using var response = await Http.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            if (!response.IsSuccessStatusCode)
                return null;

            var metaintText = GetHeader(response, "icy-metaint");
            if (!int.TryParse(metaintText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var metaint)
                || metaint is <= 0 or > 256 * 1024)
                return null;

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var audioBytes = new byte[metaint];
            if (!await ReadExactAsync(stream, audioBytes, cancellationToken))
                return null;

            var lengthByte = stream.ReadByte();
            if (lengthByte <= 0)
                return null;

            var metadata = new byte[lengthByte * 16];
            if (!await ReadExactAsync(stream, metadata, cancellationToken))
                return null;

            return IcyMetadataParser.TryParseTitle(metadata);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            return null;
        }
    }

    private static string? GetHeader(HttpResponseMessage response, string name)
    {
        if (response.Headers.TryGetValues(name, out var values))
            return values.FirstOrDefault();
        if (response.Content.Headers.TryGetValues(name, out values))
            return values.FirstOrDefault();
        return null;
    }

    private static async Task<bool> ReadExactAsync(
        Stream stream,
        byte[] buffer,
        CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset), cancellationToken);
            if (read == 0)
                return false;
            offset += read;
        }

        return true;
    }
}
