namespace KyappuTone.Core.Library;

/// <summary>A configured library root with a display name.</summary>
public sealed record LibraryRoot(string Name, string Path);

/// <summary>An auto-playlist that mirrors an immediate subfolder of a library root.</summary>
public sealed record FolderPlaylist(string Name, string FolderPath, string LibraryRoot);

/// <summary>Persisted rules for an auto-managed folder playlist.</summary>
public sealed record FolderPlaylistRules(string FolderPath, string LibraryRoot);

/// <summary>
/// Builds the deterministic set of folder playlists for the configured library roots.
/// Only immediate subfolders are included; each playlist covers its subtree recursively.
/// </summary>
public static class FolderPlaylistPlanner
{
    public static IReadOnlyList<FolderPlaylist> Build(
        IEnumerable<LibraryRoot> roots,
        IEnumerable<string> folders)
    {
        ArgumentNullException.ThrowIfNull(roots);
        ArgumentNullException.ThrowIfNull(folders);

        var normalizedRoots = roots
            .Where(root => !string.IsNullOrWhiteSpace(root.Path))
            .Select(root => new LibraryRoot(
                string.IsNullOrWhiteSpace(root.Name)
                    ? Path.GetFileName(Path.TrimEndingDirectorySeparator(root.Path))
                    : root.Name.Trim(),
                Path.GetFullPath(root.Path)))
            .DistinctBy(root => root.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (normalizedRoots.Count == 0)
            return Array.Empty<FolderPlaylist>();

        var matches = new List<(LibraryRoot Root, string Folder)>();
        foreach (var folder in folders.Where(path => !string.IsNullOrWhiteSpace(path)))
        {
            var fullPath = Path.GetFullPath(folder);
            var parent = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(fullPath));
            if (string.IsNullOrEmpty(parent))
                continue;

            var root = normalizedRoots.FirstOrDefault(candidate =>
                string.Equals(
                    Path.TrimEndingDirectorySeparator(candidate.Path),
                    Path.TrimEndingDirectorySeparator(parent),
                    StringComparison.OrdinalIgnoreCase));

            if (root is not null)
                matches.Add((root, fullPath));
        }

        var byName = matches
            .GroupBy(match => Path.GetFileName(match.Folder), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);

        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<FolderPlaylist>(matches.Count);
        foreach (var (root, folder) in matches.OrderBy(match => match.Root.Name, StringComparer.OrdinalIgnoreCase)
                                               .ThenBy(match => match.Folder, StringComparer.OrdinalIgnoreCase))
        {
            var baseName = Path.GetFileName(folder);
            if (string.IsNullOrWhiteSpace(baseName))
                continue;

            var name = byName.TryGetValue(baseName, out var count) && count > 1
                ? $"{root.Name} - {baseName}"
                : baseName;

            var uniqueName = name;
            for (var suffix = 2; !usedNames.Add(uniqueName); suffix++)
                uniqueName = $"{name} ({suffix})";

            result.Add(new FolderPlaylist(uniqueName, folder, root.Path));
        }

        return result;
    }
}
