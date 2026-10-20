using System.Text.RegularExpressions;

namespace KyappuTone.Core.Integrations.Spotify;

/// <summary>One local track that can be matched against a Spotify playlist entry.</summary>
public sealed record TrackCandidate(string Path, string Title, string Artist);

/// <summary>
/// Matches Spotify playlist entries to local files without network access. The index is built
/// once per import so matching a 10k-track library stays inexpensive.
/// </summary>
public sealed class TrackMatchIndex
{
    private static readonly Regex BracketContent = new(@"\s*[\(\[].*?[\)\]]", RegexOptions.Compiled);
    private static readonly Regex FeaturedArtist = new(@"\s+(?:feat\.?|ft\.?|featuring)\s+.*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex TrailingVersion = new(@"\s+-\s+(?:remaster(?:ed)?|live|deluxe|radio edit|single version|album version|mono|stereo).*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex NonWord = new(@"[^\p{L}\p{N}]+", RegexOptions.Compiled);

    private readonly Dictionary<string, List<TrackCandidate>> _byTitle = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TrackCandidate> _byTitleAndArtist = new(StringComparer.Ordinal);

    public TrackMatchIndex(IEnumerable<TrackCandidate> candidates)
    {
        foreach (var candidate in candidates)
        {
            var title = NormalizeTitle(candidate.Title);
            var artist = NormalizeArtist(candidate.Artist);
            if (title.Length == 0 || artist.Length == 0)
                continue;

            if (!_byTitle.TryGetValue(title, out var list))
            {
                list = new List<TrackCandidate>();
                _byTitle[title] = list;
            }

            list.Add(candidate);
            _byTitleAndArtist.TryAdd($"{title}|{artist}", candidate);
        }
    }

    /// <summary>Returns the best unambiguous local match, or null when none is close enough.</summary>
    public TrackCandidate? Find(string? title, string? artist)
    {
        var normalizedTitle = NormalizeTitle(title);
        var normalizedArtist = NormalizeArtist(artist);
        if (normalizedTitle.Length == 0)
            return null;

        if (normalizedArtist.Length > 0
            && _byTitleAndArtist.TryGetValue($"{normalizedTitle}|{normalizedArtist}", out var exact))
            return exact;

        if (!_byTitle.TryGetValue(normalizedTitle, out var candidates) || candidates.Count == 0)
            return null;

        if (candidates.Count == 1)
            return candidates[0];

        if (normalizedArtist.Length == 0)
            return null;

        var best = candidates
            .Select(candidate => (Candidate: candidate, Score: ArtistScore(normalizedArtist, NormalizeArtist(candidate.Artist))))
            .OrderByDescending(item => item.Score)
            .ToList();

        if (best[0].Score < 0.5)
            return null;

        return best.Count > 1 && Math.Abs(best[0].Score - best[1].Score) < 0.001
            ? null
            : best[0].Candidate;
    }

    public static string NormalizeTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return string.Empty;

        var value = title.ToLowerInvariant();
        value = FeaturedArtist.Replace(value, " ");
        value = BracketContent.Replace(value, " ");
        value = TrailingVersion.Replace(value, " ");
        value = NonWord.Replace(value, " ");
        return value.Trim();
    }

    public static string NormalizeArtist(string? artist)
    {
        if (string.IsNullOrWhiteSpace(artist))
            return string.Empty;

        var value = artist.ToLowerInvariant();
        value = FeaturedArtist.Replace(value, " ");
        value = NonWord.Replace(value, " ");
        return value.Trim();
    }

    private static double ArtistScore(string query, string candidate)
    {
        if (candidate.Length == 0)
            return 0;

        if (candidate.Contains(query, StringComparison.Ordinal) || query.Contains(candidate, StringComparison.Ordinal))
            return 1;

        var queryTokens = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (queryTokens.Length == 0)
            return 0;

        var candidateTokens = candidate.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
        var hits = queryTokens.Count(candidateTokens.Contains);
        return (double)hits / queryTokens.Length;
    }
}
