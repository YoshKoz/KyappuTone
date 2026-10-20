using System.Runtime.InteropServices;
using FFmpeg.AutoGen;

namespace KyappuTone.Audio.Decoding;

/// <summary>Thrown when a file cannot be opened, decoded or resampled.</summary>
public sealed class AudioFormatException : Exception
{
    public AudioFormatException(string message) : base(message) { }

    public AudioFormatException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>Output format of <see cref="FfmpegDecoder"/>: interleaved float32.</summary>
public sealed class DecodedFormat
{
    /// <summary>All decoded audio is resampled to this rate.</summary>
    public const int TargetSampleRate = 48000;

    /// <summary>All decoded audio is mixed to this many channels.</summary>
    public const int TargetChannels = 2;

    public int SampleRate => TargetSampleRate;
    public int Channels => TargetChannels;
}

/// <summary>Locates and registers the bundled FFmpeg shared libraries.</summary>
internal static class FfmpegBootstrapper
{
    private static readonly object Gate = new();
    private static bool _loaded;

    public static void EnsureLoaded()
    {
        if (_loaded) return;

        lock (Gate)
        {
            if (_loaded) return;
            ffmpeg.RootPath = FindRoot();
            _loaded = true;
        }
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "Assets", "FFmpeg");
            if (File.Exists(Path.Combine(candidate, "avcodec-63.dll")) ||
                File.Exists(Path.Combine(candidate, "avcodec-62.dll")))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("FFmpeg shared libraries not found under Assets/FFmpeg.");
    }

    public static unsafe string ErrorText(int error)
    {
        var buffer = stackalloc byte[1024];
        ffmpeg.av_strerror(error, buffer, 1024);
        return Marshal.PtrToStringAnsi((IntPtr)buffer) ?? $"FFmpeg error {error}";
    }
}
