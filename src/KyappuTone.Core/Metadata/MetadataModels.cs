namespace KyappuTone.Core.Metadata;

/// <summary>One MusicBrainz recording candidate returned for a local track.</summary>
public sealed record MetadataCandidate(
    string RecordingId,
    string Title,
    string Artist,
    string Album,
    int? Year,
    string? Genre,
    string? CoverUrl,
    double Score);

/// <summary>The metadata values currently stored for a local track.</summary>
public sealed record MetadataValues(
    string Title,
    string Artist,
    string Album,
    string Year,
    string Genre);

/// <summary>One proposed metadata change shown in the preview.</summary>
public sealed record MetadataChange(string Field, string Current, string Proposed);

/// <summary>The set of changes that would be written if the user applies a candidate.</summary>
public sealed record MetadataDiff(IReadOnlyList<MetadataChange> Changes)
{
    public bool HasChanges => Changes.Count > 0;
}

/// <summary>Builds a conservative diff between a local track and a MusicBrainz candidate.</summary>
public static class MetadataDiffBuilder
{
    public static MetadataDiff Build(MetadataValues current, MetadataCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(candidate);

        var changes = new List<MetadataChange>();
        Add(changes, "Title", current.Title, candidate.Title);
        Add(changes, "Artist", current.Artist, candidate.Artist);
        Add(changes, "Album", current.Album, candidate.Album);
        Add(changes, "Year", current.Year, candidate.Year?.ToString() ?? string.Empty);
        Add(changes, "Genre", current.Genre, candidate.Genre ?? string.Empty);
        return new MetadataDiff(changes);
    }

    private static void Add(List<MetadataChange> changes, string field, string current, string proposed)
    {
        var trimmed = proposed.Trim();
        if (trimmed.Length == 0)
            return;

        var normalizedCurrent = Normalize(current);
        if (string.Equals(normalizedCurrent, trimmed, StringComparison.OrdinalIgnoreCase))
            return;

        changes.Add(new MetadataChange(field, current.Trim(), trimmed));
    }

    private static string Normalize(string value)
        => value.Trim().Equals("Unknown", StringComparison.OrdinalIgnoreCase)
            || value.Trim().StartsWith("Unknown ", StringComparison.OrdinalIgnoreCase)
            ? string.Empty
            : value.Trim();
}
