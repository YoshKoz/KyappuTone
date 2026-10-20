using System.Diagnostics;
using KyappuTone.Core.Acquisition;

namespace KyappuTone.Common.Services;

/// <summary>Searches YouTube through yt-dlp without a YouTube API key.</summary>
public static class YouTubeService
{
    public static async Task<IReadOnlyList<YouTubeSearchResult>> SearchAsync(
        string query,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return Array.Empty<YouTubeSearchResult>();

        var ytDlp = ToolBootstrapService.GetYtDlpPath();
        if (!File.Exists(ytDlp))
            throw new InvalidOperationException("yt-dlp is not installed. Install it in Settings → Integrations → Download tools.");

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = ytDlp,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            },
        };
        process.StartInfo.ArgumentList.Add("--flat-playlist");
        process.StartInfo.ArgumentList.Add("--dump-single-json");
        process.StartInfo.ArgumentList.Add($"ytsearch20:{query.Trim()}");

        process.Start();
        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw new TimeoutException("YouTube search timed out.");
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        if (process.ExitCode != 0)
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(stderr) ? "YouTube search failed." : stderr.Trim());

        return YtDlpOutputParser.ParseSearch(stdout);
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(true);
        }
        catch (Exception)
        {
            // Best effort.
        }
    }
}
