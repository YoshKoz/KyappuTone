using System.Globalization;
using System.Text.Json;

namespace KyappuTone.Core.Acquisition;

/// <summary>One YouTube search result returned by yt-dlp's flat-playlist JSON.</summary>
public sealed record YouTubeSearchResult(
    string Id,
    string Title,
    string Uploader,
    double? Duration,
    string Url,
    string? ThumbnailUrl);

/// <summary>One parsed progress update from yt-dlp.</summary>
public sealed record DownloadProgress(double Percent, string Speed, string Eta);

/// <summary>Parses the stable parts of yt-dlp's JSON and progress output.</summary>
public static class YtDlpOutputParser
{
    public static IReadOnlyList<YouTubeSearchResult> ParseSearch(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return Array.Empty<YouTubeSearchResult>();

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Array)
                return ParseEntries(root);

            if (root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("entries", out var entries)
                && entries.ValueKind == JsonValueKind.Array)
                return ParseEntries(entries);

            return Array.Empty<YouTubeSearchResult>();
        }
        catch (JsonException)
        {
            return Array.Empty<YouTubeSearchResult>();
        }
    }

    public static DownloadProgress? ParseProgress(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return null;

        var trimmed = line.Trim();
        if (!trimmed.StartsWith("download:", StringComparison.OrdinalIgnoreCase))
            return null;

        var parts = trimmed["download:".Length..].Trim().Split('|');
        if (parts.Length < 3)
            return null;

        var percentText = parts[0].Trim().TrimEnd('%');
        if (!double.TryParse(percentText, NumberStyles.Float, CultureInfo.InvariantCulture, out var percent))
            return null;

        return new DownloadProgress(percent, parts[1].Trim(), parts[2].Trim());
    }

    private static IReadOnlyList<YouTubeSearchResult> ParseEntries(JsonElement entries)
    {
        var results = new List<YouTubeSearchResult>();
        foreach (var entry in entries.EnumerateArray())
        {
            var id = GetString(entry, "id");
            var title = GetString(entry, "title");
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(title))
                continue;

            var url = GetString(entry, "webpage_url")
                ?? GetString(entry, "url")
                ?? $"https://www.youtube.com/watch?v={id}";
            var uploader = GetString(entry, "uploader")
                ?? GetString(entry, "channel")
                ?? string.Empty;
            var thumbnail = GetString(entry, "thumbnail") ?? GetLastThumbnail(entry);

            results.Add(new YouTubeSearchResult(
                id,
                title.Trim(),
                uploader.Trim(),
                GetDouble(entry, "duration"),
                url,
                thumbnail));
        }

        return results;
    }

    private static string? GetLastThumbnail(JsonElement entry)
    {
        if (!entry.TryGetProperty("thumbnails", out var thumbnails)
            || thumbnails.ValueKind != JsonValueKind.Array)
            return null;

        string? last = null;
        foreach (var thumbnail in thumbnails.EnumerateArray())
        {
            var url = GetString(thumbnail, "url");
            if (!string.IsNullOrWhiteSpace(url))
                last = url;
        }

        return last;
    }

    private static double? GetDouble(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.TryGetDouble(out var number) ? number : null;

    private static string? GetString(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
