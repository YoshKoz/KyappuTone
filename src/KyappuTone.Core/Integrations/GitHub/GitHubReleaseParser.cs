using System.Text.Json;

namespace KyappuTone.Core.Integrations.GitHub;

/// <summary>A published GitHub release that can be compared with the installed version.</summary>
public sealed record GitHubRelease(Version Version, string Tag, string ReleaseUrl, string Notes);

/// <summary>Parses the JSON returned by the GitHub latest-release API.</summary>
public static class GitHubReleaseParser
{
    /// <summary>
    /// Returns the published release, or null when the response is a draft, a prerelease,
    /// incomplete, or carries an invalid semantic tag.
    /// </summary>
    public static GitHubRelease? Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return null;

            if (root.TryGetProperty("draft", out var draft) && draft.ValueKind == JsonValueKind.True)
                return null;
            if (root.TryGetProperty("prerelease", out var prerelease) && prerelease.ValueKind == JsonValueKind.True)
                return null;

            var tag = GetString(root, "tag_name");
            var url = GetString(root, "html_url");
            if (string.IsNullOrWhiteSpace(tag) || string.IsNullOrWhiteSpace(url))
                return null;

            var versionText = tag.TrimStart('v', 'V');
            if (!Version.TryParse(versionText, out var version))
                return null;

            return new GitHubRelease(
                version,
                tag,
                url,
                GetString(root, "body") ?? string.Empty);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? GetString(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
