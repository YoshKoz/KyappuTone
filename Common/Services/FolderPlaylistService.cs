using System.Text.Json;
using KyappuTone.Core.Library;

namespace KyappuTone.Common.Services;

/// <summary>
/// Reconciles the auto-managed folder playlists with the configured libraries and the
/// immediate subfolders that contain supported audio files.
/// </summary>
public static class FolderPlaylistService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };
    private static readonly SemaphoreSlim ReconcileGate = new(1, 1);

    public static async Task ReconcileAsync()
    {
        await ReconcileGate.WaitAsync();
        try
        {
            await ReconcileCoreAsync();
        }
        finally
        {
            ReconcileGate.Release();
        }
    }

    private static async Task ReconcileCoreAsync()
    {
        var libraries = await DatabaseHelper.Instance.GetAllLibraries();
        var roots = libraries
            .Where(library => !string.IsNullOrWhiteSpace(library.Path))
            .Select(library => new LibraryRoot(library.Name, library.Path))
            .ToList();

        if (roots.Count == 0)
        {
            foreach (var existing in await DatabaseHelper.Instance.GetAutoPlaylists())
                await DatabaseHelper.Instance.RemoveAutoPlaylist(existing.Name);
            return;
        }

        var extensions = (await LibraryScanner.GetEnabledExtensions())
            .Select(extension => extension.Trim().ToLowerInvariant())
            .Where(extension => extension.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var folders = new List<string>();
        foreach (var root in roots) // enumerate immediate subfolders only
        {
            if (!Directory.Exists(root.Path))
                continue;

            try
            {
                folders.AddRange(Directory.EnumerateDirectories(root.Path));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PathTooLongException)
            {
                System.Diagnostics.Debug.WriteLine($"Folder playlist scan skipped '{root.Path}': {ex.Message}");
            }
        }

        var folderPlaylists = FolderPlaylistPlanner.Build(
            roots,
            folders.Where(folder => ContainsAudio(folder, extensions)));

        var plannedByName = folderPlaylists.ToDictionary(
            playlist => playlist.Name,
            StringComparer.OrdinalIgnoreCase);
        var existingPlaylists = await DatabaseHelper.Instance.GetAutoPlaylists();
        var allSongs = await DatabaseHelper.Instance.LoadSongsFromDB();

        foreach (var playlist in existingPlaylists)
        {
            var rules = Deserialize(playlist.RulesJson);
            if (rules is null)
            {
                if (!plannedByName.ContainsKey(playlist.Name))
                    await DatabaseHelper.Instance.RemoveAutoPlaylist(playlist.Name);
                continue;
            }

            var planned = folderPlaylists.FirstOrDefault(candidate =>
                string.Equals(
                    Path.TrimEndingDirectorySeparator(candidate.FolderPath),
                    Path.TrimEndingDirectorySeparator(rules.FolderPath),
                    StringComparison.OrdinalIgnoreCase));

            if (planned is null
                || !string.Equals(planned.Name, playlist.Name, StringComparison.OrdinalIgnoreCase))
            {
                await DatabaseHelper.Instance.RemoveAutoPlaylist(playlist.Name);
            }
        }

        foreach (var playlist in folderPlaylists)
        {
            var rulesJson = JsonSerializer.Serialize(
                new FolderPlaylistRules(playlist.FolderPath, playlist.LibraryRoot));
            if (!await DatabaseHelper.Instance.UpsertAutoPlaylist(playlist.Name, rulesJson))
                continue;

            var songs = GetSongsUnderFolder(playlist.FolderPath, allSongs);
            await DatabaseHelper.Instance.ReplacePlaylistSongs(
                playlist.Name,
                songs.Select(song => song.Path).ToList());
        }
    }

    /// <summary>Re-evaluates one folder playlist from the current database contents.</summary>
    public static async Task RefreshAsync(string playlistName)
    {
        await ReconcileGate.WaitAsync();
        try
        {
            var info = await DatabaseHelper.Instance.GetPlaylistInfo(playlistName);
            if (info is null || !string.Equals(info.Kind, "auto", StringComparison.OrdinalIgnoreCase))
                return;

            var rules = Deserialize(info.RulesJson);
            if (rules is null)
                return;

            var allSongs = await DatabaseHelper.Instance.LoadSongsFromDB();
            var songs = GetSongsUnderFolder(rules.FolderPath, allSongs);
            await DatabaseHelper.Instance.ReplacePlaylistSongs(
                playlistName,
                songs.Select(song => song.Path).ToList());
        }
        finally
        {
            ReconcileGate.Release();
        }
    }

    private static List<Song> GetSongsUnderFolder(string folderPath, IReadOnlyList<Song> songs)
    {
        var prefix = Path.TrimEndingDirectorySeparator(folderPath) + Path.DirectorySeparatorChar;
        return songs
            .Where(song => song.Path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .OrderBy(song => song.Artists, StringComparer.OrdinalIgnoreCase)
            .ThenBy(song => song.Album, StringComparer.OrdinalIgnoreCase)
            .ThenBy(song => song.Track ?? int.MaxValue)
            .ThenBy(song => song.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool ContainsAudio(string folder, HashSet<string> extensions)
    {
        try
        {
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
            };

            return Directory.EnumerateFiles(folder, "*.*", options)
                .Any(file => extensions.Contains(Path.GetExtension(file)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PathTooLongException)
        {
            System.Diagnostics.Debug.WriteLine($"Folder playlist scan skipped '{folder}': {ex.Message}");
            return false;
        }
    }

    private static FolderPlaylistRules? Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            return JsonSerializer.Deserialize<FolderPlaylistRules>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
