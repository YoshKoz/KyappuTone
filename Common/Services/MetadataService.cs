using System.Net.Http;
using KyappuTone.Core.Metadata;

namespace KyappuTone.Common.Services;

/// <summary>
/// Looks up missing track metadata on MusicBrainz. Requests are serialized and rate-limited,
/// cached in SQLite for seven days, and never allowed to block playback.
/// </summary>
public static class MetadataService
{
    private const string BaseUrl = "https://musicbrainz.org/ws/2/recording";
    private static readonly HttpClient Http = CreateClient();
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly TimeSpan MinimumInterval = TimeSpan.FromMilliseconds(1100);
    private static DateTime _lastRequestUtc = DateTime.MinValue;

    public static async Task<IReadOnlyList<MetadataCandidate>> SearchAsync(
        Song song,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(song.Title))
            return Array.Empty<MetadataCandidate>();

        var key = BuildCacheKey(song);
        var cached = await DatabaseHelper.Instance.GetMetadataCache(key);
        if (cached is not null
            && DateTimeOffset.UtcNow.ToUnixTimeSeconds() - cached.FetchedAtUtc < TimeSpan.FromDays(7).TotalSeconds)
        {
            return MusicBrainzMapper.ParseRecordings(cached.Json);
        }

        var json = await RequestAsync(song, cancellationToken);
        if (string.IsNullOrWhiteSpace(json))
            return Array.Empty<MetadataCandidate>();

        await DatabaseHelper.Instance.SetMetadataCache(key, json);
        return MusicBrainzMapper.ParseRecordings(json);
    }

    private static async Task<string?> RequestAsync(Song song, CancellationToken cancellationToken)
    {
        await Gate.WaitAsync(cancellationToken);
        try
        {
            var wait = MinimumInterval - (DateTime.UtcNow - _lastRequestUtc);
            if (wait > TimeSpan.Zero)
                await Task.Delay(wait, cancellationToken);

            _lastRequestUtc = DateTime.UtcNow;

            var query = BuildQuery(song);
            var url = $"{BaseUrl}?query={Uri.EscapeDataString(query)}&fmt=json&limit=5&inc=tags";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Accept.ParseAdd("application/json");
            request.Headers.UserAgent.ParseAdd("KyappuTone/0.3.0 (https://github.com/YoshKoz/KyappuTone)");

            using var response = await Http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return null;

            return await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            System.Diagnostics.Debug.WriteLine($"Metadata lookup failed: {ex.Message}");
            return null;
        }
        finally
        {
            Gate.Release();
        }
    }

    private static string BuildQuery(Song song)
    {
        var title = EscapeQueryValue(song.Title);
        if (string.IsNullOrWhiteSpace(song.Artists)
            || song.Artists.Equals("Unknown Artist", StringComparison.OrdinalIgnoreCase))
            return $"recording:\"{title}\"";

        return $"recording:\"{title}\" AND artist:\"{EscapeQueryValue(song.Artists)}\"";
    }

    private static string EscapeQueryValue(string value)
        => value.Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("\"", "\\\"", StringComparison.Ordinal)
                .Trim();

    private static string BuildCacheKey(Song song)
        => $"recording:{song.Title.Trim().ToLowerInvariant()}|{song.Artists.Trim().ToLowerInvariant()}";

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        return client;
    }
}
