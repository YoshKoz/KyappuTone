using System.Diagnostics;
using KyappuTone.Core.Acquisition;

namespace KyappuTone.Common.Services;

/// <summary>
/// Runs one yt-dlp download at a time, persists the queue in SQLite, and imports completed
/// files into the local library when the setting is enabled.
/// </summary>
public sealed class DownloadService
{
    private static DownloadService? _instance;
    private readonly SemaphoreSlim _wake = new(0, int.MaxValue);
    private readonly object _processGate = new();
    private Process? _currentProcess;
    private int _currentId;
    private bool _started;

    public static DownloadService Instance => _instance ??= new DownloadService();

    public event EventHandler? QueueChanged;

    public static string DefaultDownloadFolder
    {
        get
        {
            var configured = Windows.Storage.ApplicationData.Current.LocalSettings
                .Values[nameof(LocalSave.DownloadFolder)]?.ToString();
            if (!string.IsNullOrWhiteSpace(configured))
                return configured;

            var music = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
            return string.IsNullOrWhiteSpace(music)
                ? Path.Combine(Windows.Storage.ApplicationData.Current.LocalFolder.Path, "Downloads")
                : Path.Combine(music, "KyappuTone");
        }
    }

    public async Task StartAsync()
    {
        if (_started)
            return;

        _started = true;
        await DatabaseHelper.Instance.ResetRunningDownloads();
        _ = Task.Run(WorkerLoopAsync);
    }

    public async Task<DownloadQueueRow> EnqueueAsync(string url, string title, string targetFolder)
    {
        Directory.CreateDirectory(targetFolder);
        var id = await DatabaseHelper.Instance.AddDownload(url, title, targetFolder);
        var row = (await DatabaseHelper.Instance.GetDownloadQueue()).First(item => item.Id == id);
        QueueChanged?.Invoke(this, EventArgs.Empty);
        _wake.Release();
        return row;
    }

    public Task<List<DownloadQueueRow>> GetQueueAsync()
        => DatabaseHelper.Instance.GetDownloadQueue();

    public async Task CancelAsync(int id)
    {
        Process? process;
        lock (_processGate)
            process = _currentId == id ? _currentProcess : null;

        if (process is not null)
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

        var row = (await DatabaseHelper.Instance.GetDownloadQueue()).FirstOrDefault(item => item.Id == id);
        if (row is not null && row.Status is not "completed")
        {
            row.Status = "cancelled";
            row.Error = string.Empty;
            await DatabaseHelper.Instance.UpdateDownload(row);
            QueueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public async Task RetryAsync(int id)
    {
        var row = (await DatabaseHelper.Instance.GetDownloadQueue()).FirstOrDefault(item => item.Id == id);
        if (row is null)
            return;

        row.Status = "queued";
        row.Progress = 0;
        row.Error = string.Empty;
        await DatabaseHelper.Instance.UpdateDownload(row);
        QueueChanged?.Invoke(this, EventArgs.Empty);
        _wake.Release();
    }

    private async Task WorkerLoopAsync()
    {
        while (true)
        {
            try
            {
                var next = (await DatabaseHelper.Instance.GetDownloadQueue())
                    .FirstOrDefault(item => item.Status == "queued");
                if (next is null)
                {
                    await _wake.WaitAsync(TimeSpan.FromSeconds(30));
                    continue;
                }

                await ProcessAsync(next);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Download worker failed: {ex.Message}");
                await Task.Delay(1000);
            }
        }
    }

    private async Task ProcessAsync(DownloadQueueRow row)
    {
        var ytDlp = ToolBootstrapService.GetYtDlpPath();
        var ffmpeg = ToolBootstrapService.GetFfmpegPath();
        if (!File.Exists(ytDlp))
        {
            await FailAsync(row, "yt-dlp is not installed. Install it in Settings.");
            return;
        }
        if (!File.Exists(ffmpeg))
        {
            await FailAsync(row, "FFmpeg is not installed. Install it in Settings.");
            return;
        }

        Directory.CreateDirectory(row.TargetFolder);
        row.Status = "running";
        row.Progress = 0;
        row.Error = string.Empty;
        await DatabaseHelper.Instance.UpdateDownload(row);
        QueueChanged?.Invoke(this, EventArgs.Empty);

        var outputPathFile = Path.Combine(Path.GetTempPath(), $"kyapputone-download-{row.Id}.path");
        TryDelete(outputPathFile);

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

        var args = process.StartInfo.ArgumentList;
        args.Add("-x");
        args.Add("--audio-format");
        args.Add("mp3");
        args.Add("--audio-quality");
        args.Add("0");
        args.Add("--embed-metadata");
        args.Add("--embed-thumbnail");
        args.Add("--newline");
        args.Add("--no-playlist");
        args.Add("--ffmpeg-location");
        args.Add(Path.GetDirectoryName(ffmpeg)!);
        args.Add("--print-to-file");
        args.Add("after_move:filepath");
        args.Add(outputPathFile);
        args.Add("--progress-template");
        args.Add("download:%(progress._percent_str)s|%(progress._speed_str)s|%(progress._eta_str)s");
        args.Add("-o");
        args.Add(Path.Combine(row.TargetFolder, "%(title)s [%(id)s].%(ext)s"));
        args.Add(row.Url);

        try
        {
            process.Start();
            lock (_processGate)
            {
                _currentProcess = process;
                _currentId = row.Id;
            }

            var lastProgress = 0.0;
            var lastUiUpdate = DateTime.MinValue;
            var stderrTask = ReadErrorsAsync(process);
            var progressTask = ReadProgressAsync(process, async progress =>
            {
                if (progress.Percent - lastProgress < 1
                    && (DateTime.UtcNow - lastUiUpdate).TotalMilliseconds < 750)
                    return;

                lastProgress = progress.Percent;
                lastUiUpdate = DateTime.UtcNow;
                row.Progress = progress.Percent;
                await DatabaseHelper.Instance.UpdateDownload(row);
                QueueChanged?.Invoke(this, EventArgs.Empty);
            });

            await process.WaitForExitAsync();
            await progressTask;
            var stderr = await stderrTask;

            var current = (await DatabaseHelper.Instance.GetDownloadQueue())
                .FirstOrDefault(item => item.Id == row.Id);
            if (current?.Status == "cancelled")
            {
                TryDelete(outputPathFile);
                return;
            }

            if (process.ExitCode != 0)
            {
                await FailAsync(row, string.IsNullOrWhiteSpace(stderr) ? "yt-dlp failed." : LastLine(stderr));
                return;
            }

            var outputPath = ReadOutputPath(outputPathFile) ?? FindNewestMp3(row.TargetFolder);
            TryDelete(outputPathFile);
            if (string.IsNullOrWhiteSpace(outputPath) || !File.Exists(outputPath))
            {
                await FailAsync(row, "yt-dlp finished without an output file.");
                return;
            }

            if (Windows.Storage.ApplicationData.Current.LocalSettings
                .Values[nameof(LocalSave.ImportDownloads)]?.ToString() is not "false")
            {
                try
                {
                    var (song, succeeded) = await LibraryScanner.ExtractSongMetadata(outputPath, 0);
                    if (succeeded)
                        await DatabaseHelper.Instance.InsertMultipleSongs(new List<Song> { song });
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Download import failed: {ex.Message}");
                }
            }

            row.Status = "completed";
            row.Progress = 100;
            row.OutputPath = outputPath;
            row.Error = string.Empty;
            await DatabaseHelper.Instance.UpdateDownload(row);
            QueueChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            var current = (await DatabaseHelper.Instance.GetDownloadQueue())
                .FirstOrDefault(item => item.Id == row.Id);
            if (current?.Status != "cancelled")
                await FailAsync(row, ex.Message);
        }
        finally
        {
            lock (_processGate)
            {
                _currentProcess = null;
                _currentId = 0;
            }
            TryDelete(outputPathFile);
        }
    }

    private static async Task ReadProgressAsync(Process process, Func<DownloadProgress, Task> onProgress)
    {
        while (await process.StandardOutput.ReadLineAsync() is { } line)
        {
            var progress = YtDlpOutputParser.ParseProgress(line);
            if (progress is not null)
                await onProgress(progress);
        }
    }

    private static async Task<string> ReadErrorsAsync(Process process)
    {
        var lines = new List<string>();
        while (await process.StandardError.ReadLineAsync() is { } line)
        {
            if (!string.IsNullOrWhiteSpace(line))
                lines.Add(line.Trim());
        }

        return string.Join(Environment.NewLine, lines.TakeLast(12));
    }

    private async Task FailAsync(DownloadQueueRow row, string error)
    {
        row.Status = "failed";
        row.Error = error.Length > 500 ? error[..500] : error;
        await DatabaseHelper.Instance.UpdateDownload(row);
        QueueChanged?.Invoke(this, EventArgs.Empty);
    }

    private static string? ReadOutputPath(string path)
    {
        try
        {
            return File.Exists(path)
                ? File.ReadLines(path).LastOrDefault(line => !string.IsNullOrWhiteSpace(line))?.Trim()
                : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string? FindNewestMp3(string folder)
    {
        try
        {
            return Directory.EnumerateFiles(folder, "*.mp3", SearchOption.TopDirectoryOnly)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string LastLine(string text)
        => text.Split('\n', StringSplitOptions.RemoveEmptyEntries).LastOrDefault()?.Trim() ?? text;

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception)
        {
            // Best effort.
        }
    }
}
