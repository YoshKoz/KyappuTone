using System.Buffers.Binary;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using KyappuTone.Core.Integrations.Discord;
using KyappuTone.Core.Radio;

namespace KyappuTone.Common.Services;

/// <summary>Sends Discord Rich Presence over the local IPC pipe. Failures never affect playback.</summary>
public sealed class DiscordPresenceService : IDisposable
{
    private static DiscordPresenceService? _instance;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private NamedPipeClientStream? _pipe;
    private bool _disposed;

    public static DiscordPresenceService Instance => _instance ??= new DiscordPresenceService();

    public string Status { get; private set; } = "Discord presence is off.";

    public bool IsEnabled
    {
        get
        {
            var settings = Windows.Storage.ApplicationData.Current.LocalSettings;
            var enabled = bool.Parse(settings.Values[nameof(LocalSave.DiscordPresenceEnabled)]?.ToString() ?? "false");
            var clientId = settings.Values[nameof(LocalSave.DiscordClientId)]?.ToString();
            return enabled && !string.IsNullOrWhiteSpace(clientId);
        }
    }

    public async Task ConfigureAsync()
    {
        if (!IsEnabled)
        {
            await CloseAsync();
            Status = "Discord presence is off.";
            return;
        }

        await _gate.WaitAsync();
        try
        {
            await ClosePipeAsync();
            await EnsureConnectedAsync();
            Status = _pipe is null ? "Discord is not running." : "Connected to Discord.";
        }
        finally
        {
            _gate.Release();
        }
    }

    public void UpdateTrack(Song? song, bool isPlaying)
    {
        if (!IsEnabled) return;
        if (song is null) { Clear(); return; }
        _ = Task.Run(() => UpdateActivityAsync(song.Title, song.Artists, isPlaying));
    }

    public void UpdateStream(RadioStation? station, bool isPlaying)
    {
        if (!IsEnabled) return;
        if (station is null) { Clear(); return; }
        _ = Task.Run(() => UpdateActivityAsync(station.Name, "Internet radio", isPlaying));
    }

    public void Clear()
    {
        if (IsEnabled) _ = Task.Run(ClearActivityAsync);
    }

    private async Task UpdateActivityAsync(string details, string state, bool isPlaying)
    {
        if (!await EnsureConnectedSafeAsync()) return;

        var activity = new Dictionary<string, object?>
        {
            ["details"] = Truncate(details),
            ["state"] = Truncate(state),
            ["instance"] = false,
        };
        if (isPlaying)
            activity["timestamps"] = new Dictionary<string, object> { ["start"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() };

        var payload = JsonSerializer.Serialize(new
        {
            cmd = "SET_ACTIVITY",
            args = new { pid = Environment.ProcessId, activity },
            nonce = Guid.NewGuid().ToString(),
        });
        await SendWithRetryAsync(DiscordIpcProtocol.Encode(1, payload));
    }

    private async Task ClearActivityAsync()
    {
        if (!await EnsureConnectedSafeAsync()) return;

        var payload = JsonSerializer.Serialize(new
        {
            cmd = "SET_ACTIVITY",
            args = new { pid = Environment.ProcessId, activity = (object?)null },
            nonce = Guid.NewGuid().ToString(),
        });
        await SendWithRetryAsync(DiscordIpcProtocol.Encode(1, payload));
    }

    private async Task<bool> EnsureConnectedSafeAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (_pipe is not null && _pipe.IsConnected) return true;
            await EnsureConnectedAsync();
            return _pipe is not null;
        }
        finally { _gate.Release(); }
    }

    private async Task EnsureConnectedAsync()
    {
        if (_pipe is not null && _pipe.IsConnected) return;
        await ClosePipeAsync();

        var clientId = Windows.Storage.ApplicationData.Current.LocalSettings
            .Values[nameof(LocalSave.DiscordClientId)]?.ToString()?.Trim();
        if (string.IsNullOrWhiteSpace(clientId)) return;

        for (var index = 0; index < 10; index++)
        {
            var pipe = new NamedPipeClientStream(".", $"discord-ipc-{index}", PipeDirection.InOut, PipeOptions.Asynchronous);
            try
            {
                await pipe.ConnectAsync(120);
                if (!await HandshakeAsync(pipe, clientId))
                {
                    pipe.Dispose();
                    continue;
                }

                _pipe = pipe;
                Status = "Connected to Discord.";
                return;
            }
            catch (Exception)
            {
                pipe.Dispose();
            }
        }

        Status = "Discord is not running.";
    }

    private static async Task<bool> HandshakeAsync(NamedPipeClientStream pipe, string clientId)
    {
        var payload = JsonSerializer.Serialize(new { v = 1, client_id = clientId });
        await WriteFrameAsync(pipe, DiscordIpcProtocol.Encode(0, payload));
        var response = await ReadFrameAsync(pipe);
        return response is not null && response.Contains("\"evt\":\"READY\"", StringComparison.Ordinal);
    }

    private async Task SendWithRetryAsync(byte[] frame)
    {
        await _gate.WaitAsync();
        try
        {
            if (_pipe is null || !_pipe.IsConnected)
            {
                await EnsureConnectedAsync();
                if (_pipe is null) return;
            }

            try
            {
                await WriteFrameAsync(_pipe, frame);
            }
            catch (Exception)
            {
                await ClosePipeAsync();
                await EnsureConnectedAsync();
                if (_pipe is not null) await WriteFrameAsync(_pipe, frame);
            }
        }
        catch (Exception ex)
        {
            Status = $"Discord error: {ex.Message}";
        }
        finally { _gate.Release(); }
    }

    private static async Task WriteFrameAsync(Stream stream, byte[] frame)
    {
        await stream.WriteAsync(frame);
        await stream.FlushAsync();
    }

    private static async Task<string?> ReadFrameAsync(Stream stream)
    {
        var header = new byte[8];
        if (!await ReadExactAsync(stream, header)) return null;

        var length = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(4, 4));
        if (length is < 0 or > 1024 * 1024) return null;

        var payload = new byte[length];
        return await ReadExactAsync(stream, payload) ? Encoding.UTF8.GetString(payload) : null;
    }

    private static async Task<bool> ReadExactAsync(Stream stream, byte[] buffer)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset));
            if (read == 0) return false;
            offset += read;
        }
        return true;
    }

    private async Task CloseAsync()
    {
        await _gate.WaitAsync();
        try { await ClosePipeAsync(); }
        finally { _gate.Release(); }
    }

    private Task ClosePipeAsync()
    {
        if (_pipe is not null)
        {
            try { _pipe.Dispose(); }
            catch (Exception) { }
            _pipe = null;
        }
        return Task.CompletedTask;
    }

    private static string Truncate(string value) => value.Length <= 128 ? value : value[..128];

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _pipe?.Dispose(); }
        catch (Exception) { }
        _gate.Dispose();
    }
}
