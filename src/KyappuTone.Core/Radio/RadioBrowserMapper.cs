using System.Text.Json;

namespace KyappuTone.Core.Radio;

/// <summary>Parses Radio Browser station-search responses into playable stations.</summary>
public static class RadioBrowserMapper
{
    public static IReadOnlyList<RadioStation> ParseStations(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return Array.Empty<RadioStation>();

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
                return Array.Empty<RadioStation>();

            var stations = new List<RadioStation>();
            foreach (var item in document.RootElement.EnumerateArray())
            {
                var name = GetString(item, "name");
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                var url = ChooseUrl(item);
                if (url is null)
                    continue;

                stations.Add(new RadioStation(
                    GetString(item, "stationuuid") ?? string.Empty,
                    name.Trim(),
                    url,
                    GetString(item, "favicon"),
                    GetTags(item),
                    GetString(item, "country"),
                    GetString(item, "codec"),
                    GetInt(item, "bitrate")));
            }

            return stations;
        }
        catch (JsonException)
        {
            return Array.Empty<RadioStation>();
        }
    }

    private static string? ChooseUrl(JsonElement item)
    {
        var candidates = new[]
        {
            GetString(item, "url_resolved"),
            GetString(item, "url"),
        }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim())
            .Where(value => Uri.TryCreate(value, UriKind.Absolute, out var uri)
                && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            .ToList();

        return candidates.FirstOrDefault(value => value.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            ?? candidates.FirstOrDefault();
    }

    private static string? GetTags(JsonElement item)
    {
        if (!item.TryGetProperty("tags", out var tags))
            return null;

        if (tags.ValueKind == JsonValueKind.String)
        {
            var text = tags.GetString()?.Trim();
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }

        if (tags.ValueKind != JsonValueKind.Array)
            return null;

        var values = tags.EnumerateArray()
            .Where(tag => tag.ValueKind == JsonValueKind.String)
            .Select(tag => tag.GetString()?.Trim())
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .ToList();
        return values.Count == 0 ? null : string.Join(", ", values);
    }

    private static int GetInt(JsonElement item, string name)
    {
        if (!item.TryGetProperty(name, out var value))
            return 0;

        return value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)
            ? number
            : 0;
    }

    private static string? GetString(JsonElement item, string name)
        => item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
