using System.Net.Http;
using KyappuTone.Core.Metadata;
using TagLib;

namespace KyappuTone.Common.Services;

/// <summary>Writes a confirmed MusicBrainz candidate into the file's tags and the local database.</summary>
public static class TagMetadataWriter
{
    private static readonly HttpClient Http = CreateClient();

    public static async Task<Song> ApplyAsync(
        Song song,
        MetadataCandidate candidate,
        bool writeCover,
        CancellationToken cancellationToken = default)
    {
        var diff = MetadataDiffBuilder.Build(
            new MetadataValues(song.Title, song.Artists, song.Album, song.Year, song.Genre),
            candidate);

        if (!diff.HasChanges && !writeCover)
            return song;

        using var file = TagLib.File.Create(song.Path);
        if (diff.Changes.Any(change => change.Field == "Title"))
            file.Tag.Title = candidate.Title.Trim();
        if (diff.Changes.Any(change => change.Field == "Artist"))
            file.Tag.Performers = new[] { candidate.Artist.Trim() };
        if (diff.Changes.Any(change => change.Field == "Album"))
            file.Tag.Album = candidate.Album.Trim();
        if (diff.Changes.Any(change => change.Field == "Year") && candidate.Year is not null)
            file.Tag.Year = (uint)candidate.Year.Value;
        if (diff.Changes.Any(change => change.Field == "Genre") && !string.IsNullOrWhiteSpace(candidate.Genre))
            file.Tag.Genres = new[] { candidate.Genre.Trim() };

        if (writeCover && !string.IsNullOrWhiteSpace(candidate.CoverUrl))
        {
            var cover = await TryDownloadCoverAsync(candidate.CoverUrl, cancellationToken);
            if (cover is not null)
            {
                file.Tag.Pictures = new IPicture[]
                {
                    new TagLib.Picture(new ByteVector(cover))
                    {
                        Type = PictureType.FrontCover,
                        MimeType = DetectMimeType(cover),
                        Description = "Front cover",
                    },
                };
            }
        }

        LibraryWatcherService.MarkSelfInitiated(song.Path);
        file.Save();

        if (!string.IsNullOrWhiteSpace(candidate.Title))
            song.Title = candidate.Title.Trim();
        if (!string.IsNullOrWhiteSpace(candidate.Artist))
            song.Artists = candidate.Artist.Trim();
        if (!string.IsNullOrWhiteSpace(candidate.Album))
            song.Album = candidate.Album.Trim();
        if (candidate.Year is not null)
            song.Year = candidate.Year.Value.ToString();
        if (!string.IsNullOrWhiteSpace(candidate.Genre))
            song.Genre = candidate.Genre.Trim();

        song.Cover = ImageResizer.CreateThumbnailImage(
            ThumbnailFolder.AllSongView,
            file.Tag.Pictures,
            300);

        await DatabaseHelper.Instance.InsertMultipleSongs(new List<Song> { song });
        return song;
    }

    private static async Task<byte[]?> TryDownloadCoverAsync(string url, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await Http.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return null;

            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            return bytes.Length is > 0 and <= 10 * 1024 * 1024 ? bytes : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            System.Diagnostics.Debug.WriteLine($"Cover download failed: {ex.Message}");
            return null;
        }
    }

    private static string DetectMimeType(byte[] data)
        => data.Length >= 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF
            ? "image/jpeg"
            : "image/png";

    private static HttpClient CreateClient()
        => new() { Timeout = TimeSpan.FromSeconds(30) };
}
