using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using KyappuTone.Core.Acquisition;

namespace KyappuTone.Common.Services;

/// <summary>Installed-tool paths and versions shown in Settings.</summary>
public sealed record ToolStatus(
    string? YtDlpPath,
    string? YtDlpVersion,
    string? FfmpegPath,
    string? FfmpegVersion)
{
    public bool YtDlpReady => !string.IsNullOrWhiteSpace(YtDlpVersion);
    public bool FfmpegReady => !string.IsNullOrWhiteSpace(FfmpegVersion);
}

/// <summary>
/// Downloads yt-dlp and an LGPL FFmpeg Windows build into LocalState. Every archive is
/// checked against its release SHA-256 list before it is used.
/// </summary>
public static class ToolBootstrapService
{
    private const string YtDlpUrl = "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe";
    private const string YtDlpChecksumsUrl = "https://github.com/yt-dlp/yt-dlp/releases/latest/download/SHA2-256SUMS";
    private const string FfmpegArchiveName = "ffmpeg-master-latest-win64-lgpl.zip";
    private const string FfmpegUrl = $"https://github.com/BtbN/FFmpeg-Builds/releases/latest/download/{FfmpegArchiveName}";
    private const string FfmpegChecksumsUrl = "https://github.com/BtbN/FFmpeg-Builds/releases/latest/download/checksums.sha256";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(10) };

    public static string ToolsDirectory
        => Path.Combine(Windows.Storage.ApplicationData.Current.LocalFolder.Path, "Tools");

    public static string DefaultYtDlpPath => Path.Combine(ToolsDirectory, "yt-dlp.exe");

    public static string DefaultFfmpegPath => Path.Combine(ToolsDirectory, "ffmpeg.exe");

    public static string GetYtDlpPath()
    {
        var configured = Windows.Storage.ApplicationData.Current.LocalSettings
            .Values[nameof(LocalSave.YtDlpPath)]?.ToString();
        return !string.IsNullOrWhiteSpace(configured) && File.Exists(configured)
            ? configured
            : DefaultYtDlpPath;
    }

    public static string GetFfmpegPath()
    {
        var configured = Windows.Storage.ApplicationData.Current.LocalSettings
            .Values[nameof(LocalSave.FfmpegPath)]?.ToString();
        return !string.IsNullOrWhiteSpace(configured) && File.Exists(configured)
            ? configured
            : DefaultFfmpegPath;
    }

    public static void SaveManualPaths(string? ytDlpPath, string? ffmpegPath)
    {
        var settings = Windows.Storage.ApplicationData.Current.LocalSettings;
        if (string.IsNullOrWhiteSpace(ytDlpPath))
            settings.Values.Remove(nameof(LocalSave.YtDlpPath));
        else
            settings.Values[nameof(LocalSave.YtDlpPath)] = ytDlpPath.Trim();

        if (string.IsNullOrWhiteSpace(ffmpegPath))
            settings.Values.Remove(nameof(LocalSave.FfmpegPath));
        else
            settings.Values[nameof(LocalSave.FfmpegPath)] = ffmpegPath.Trim();
    }

    public static async Task<ToolStatus> GetStatusAsync()
    {
        var ytDlp = GetYtDlpPath();
        var ffmpeg = GetFfmpegPath();
        var ytVersion = File.Exists(ytDlp) ? await ReadVersionAsync(ytDlp, "--version", null) : null;
        var ffVersion = File.Exists(ffmpeg) ? await ReadVersionAsync(ffmpeg, "-version", "ffmpeg version ") : null;
        return new ToolStatus(
            File.Exists(ytDlp) ? ytDlp : null,
            ytVersion,
            File.Exists(ffmpeg) ? ffmpeg : null,
            ffVersion);
    }

    public static async Task InstallOrUpdateAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(ToolsDirectory);
        progress?.Report("Downloading yt-dlp...");
        var ytTemp = Path.Combine(ToolsDirectory, "yt-dlp.download");
        await DownloadFileAsync(YtDlpUrl, ytTemp, cancellationToken);
        var ytChecksums = await Http.GetStringAsync(YtDlpChecksumsUrl, cancellationToken);
        VerifyChecksum(ytTemp, ytChecksums, "yt-dlp.exe");
        File.Move(ytTemp, DefaultYtDlpPath, true);

        progress?.Report("Downloading FFmpeg (LGPL)...");
        var ffTemp = Path.Combine(ToolsDirectory, "ffmpeg.download.zip");
        await DownloadFileAsync(FfmpegUrl, ffTemp, cancellationToken);
        var ffChecksums = await Http.GetStringAsync(FfmpegChecksumsUrl, cancellationToken);
        VerifyChecksum(ffTemp, ffChecksums, FfmpegArchiveName);

        progress?.Report("Extracting FFmpeg...");
        ExtractFfmpeg(ffTemp, DefaultFfmpegPath);
        File.Delete(ffTemp);

        Windows.Storage.ApplicationData.Current.LocalSettings.Values.Remove(nameof(LocalSave.YtDlpPath));
        Windows.Storage.ApplicationData.Current.LocalSettings.Values.Remove(nameof(LocalSave.FfmpegPath));
        progress?.Report("Tools installed.");
    }

    private static async Task DownloadFileAsync(string url, string destination, CancellationToken cancellationToken)
    {
        using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var target = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
        await source.CopyToAsync(target, cancellationToken);
    }

    private static void VerifyChecksum(string filePath, string checksums, string fileName)
    {
        if (!ChecksumFile.TryFind(checksums, fileName, out var expected))
            throw new InvalidOperationException($"No SHA-256 checksum found for {fileName}.");

        using var stream = File.OpenRead(filePath);
        var actual = Convert.ToHexString(SHA256.HashData(stream));
        if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Checksum mismatch for {fileName}.");
    }

    private static void ExtractFfmpeg(string archivePath, string destination)
    {
        using var archive = ZipFile.OpenRead(archivePath);
        var entry = archive.Entries.FirstOrDefault(item =>
            item.Name.Equals("ffmpeg.exe", StringComparison.OrdinalIgnoreCase));
        if (entry is null || !SafeArchive.IsSafeEntry(ToolsDirectory, entry.FullName))
            throw new InvalidOperationException("The FFmpeg archive did not contain a safe ffmpeg.exe.");

        var temp = destination + ".download";
        entry.ExtractToFile(temp, true);
        File.Move(temp, destination, true);
    }

    private static async Task<string?> ReadVersionAsync(string path, string argument, string? prefix)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = path,
                    Arguments = argument,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                },
            };

            process.Start();
            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();
            if (process.ExitCode != 0)
                return null;

            var first = output.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim();
            if (string.IsNullOrWhiteSpace(first))
                return null;

            return prefix is not null && first.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                ? first[prefix.Length..].Trim()
                : first;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
