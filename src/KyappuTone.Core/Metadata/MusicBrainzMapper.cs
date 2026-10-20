using System.Text.Json;

namespace KyappuTone.Core.Metadata;

/// <summary>Parses MusicBrainz recording-search responses into preview candidates.</summary>
public static class MusicBrainzMapper
{
    public static IReadOnlyList<MetadataCandidate> ParseRecordings(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return Array.Empty<MetadataCandidate>();

        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("recordings", out var recordings)
                || recordings.ValueKind != JsonValueKind.Array)
                return Array.Empty<MetadataCandidate>();

            var result = new List<MetadataCandidate>();
            foreach (var recording in recordings.EnumerateArray())
            {
                var title = GetString(recording, "title");
                if (string.IsNullOrWhiteSpace(title))
                    continue;

                var id = GetString(recording, "id") ?? string.Empty;
                var artist = GetArtist(recording);
                var release = GetRelease(recording);
                var album = release is null ? string.Empty : GetString(release.Value, "title") ?? string.Empty;
                var year = release is null ? null : GetYear(GetString(release.Value, "date"));
                var genre = GetGenre(recording, release);
                var releaseId = release is null ? null : GetString(release.Value, "id");
                var coverUrl = string.IsNullOrWhiteSpace(releaseId)
                    ? null
                    : $"https://coverartarchive.org/release/{releaseId}/front-500";

                result.Add(new MetadataCandidate(
                    id,
                    title.Trim(),
                    artist,
                    album.Trim(),
                    year,
                    genre,
                    coverUrl,
                    GetDouble(recording, "score")));
            }

            return result;
        }
        catch (JsonException)
        {
            return Array.Empty<MetadataCandidate>();
        }
    }

    private static string GetArtist(JsonElement recording)
    {
        if (!recording.TryGetProperty("artist-credit", out var credits)
            || credits.ValueKind != JsonValueKind.Array)
            return string.Empty;

        var names = credits
            .EnumerateArray()
            .Select(credit => GetString(credit, "name") ?? GetNestedString(credit, "artist", "name"))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!.Trim())
            .ToList();

        return string.Join(", ", names);
    }

    private static JsonElement? GetRelease(JsonElement recording)
    {
        if (!recording.TryGetProperty("releases", out var releases)
            || releases.ValueKind != JsonValueKind.Array)
            return null;

        foreach (var release in releases.EnumerateArray())
        {
            if (!string.IsNullOrWhiteSpace(GetString(release, "title")))
                return release;
        }

        return null;
    }

    private static string? GetGenre(JsonElement recording, JsonElement? release)
    {
        var genre = GetFirstTag(recording);
        if (!string.IsNullOrWhiteSpace(genre))
            return genre;

        if (release is not null)
        {
            genre = GetFirstTag(release.Value);
            if (!string.IsNullOrWhiteSpace(genre))
                return genre;

            if (release.Value.TryGetProperty("release-group", out var group))
                return GetFirstTag(group);
        }

        return null;
    }

    private static string? GetFirstTag(JsonElement element)
    {
        if (!element.TryGetProperty("tags", out var tags) || tags.ValueKind != JsonValueKind.Array)
            return null;

        foreach (var tag in tags.EnumerateArray())
        {
            var name = GetString(tag, "name");
            if (!string.IsNullOrWhiteSpace(name))
                return name.Trim();
        }

        return null;
    }

    private static int? GetYear(string? date)
        => date is { Length: >= 4 } && int.TryParse(date[..4], out var year) ? year : null;

    private static double GetDouble(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.TryGetDouble(out var number) ? number : 0;

    private static string? GetNestedString(JsonElement element, string child, string name)
        => element.TryGetProperty(child, out var nested) ? GetString(nested, name) : null;

    private static string? GetString(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
