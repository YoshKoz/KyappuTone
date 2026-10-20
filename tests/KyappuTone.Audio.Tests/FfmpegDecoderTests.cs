using KyappuTone.Audio.Decoding;
using Shouldly;
using Xunit;

namespace KyappuTone.Audio.Tests;

public class FfmpegDecoderTests
{
    private static string WriteWav(string name, int sampleRate, int channels, double seconds, double amplitude = 0.25)
    {
        var path = Path.Combine(Path.GetTempPath(), $"kyapputone-{name}-{Guid.NewGuid():N}.wav");
        var frames = (int)(sampleRate * seconds);
        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream);

        var dataBytes = frames * channels * 2;
        writer.Write("RIFF"u8.ToArray());
        writer.Write(36 + dataBytes);
        writer.Write("WAVE"u8.ToArray());
        writer.Write("fmt "u8.ToArray());
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)channels);
        writer.Write(sampleRate);
        writer.Write(sampleRate * channels * 2);
        writer.Write((short)(channels * 2));
        writer.Write((short)16);
        writer.Write("data"u8.ToArray());
        writer.Write(dataBytes);

        for (var frame = 0; frame < frames; frame++)
        {
            var value = (short)(Math.Sin(2 * Math.PI * 440 * frame / sampleRate) * amplitude * short.MaxValue);
            for (var channel = 0; channel < channels; channel++) writer.Write(value);
        }

        return path;
    }

    private static float[] ReadAll(FfmpegDecoder decoder)
    {
        var chunk = new float[8192];
        using var all = new MemoryStream();
        int read;
        while ((read = decoder.Read(chunk)) > 0)
        {
            var bytes = new byte[read * sizeof(float)];
            Buffer.BlockCopy(chunk, 0, bytes, 0, bytes.Length);
            all.Write(bytes);
        }

        var result = new float[all.Length / sizeof(float)];
        Buffer.BlockCopy(all.ToArray(), 0, result, 0, (int)all.Length);
        return result;
    }

    [Fact]
    public void Open_ReportsDuration_And_NormalizesFormat()
    {
        var path = WriteWav("mono441", 44100, 1, 3.0);
        try
        {
            using var decoder = FfmpegDecoder.Open(path);
            decoder.Duration.TotalSeconds.ShouldBe(3.0, tolerance: 0.05);
            decoder.Format.SampleRate.ShouldBe(48000);
            decoder.Format.Channels.ShouldBe(2);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Read_ReturnsInterleavedStereo48k()
    {
        var path = WriteWav("stereo48", 48000, 2, 1.0);
        try
        {
            using var decoder = FfmpegDecoder.Open(path);
            var samples = ReadAll(decoder);

            (samples.Length % 2).ShouldBe(0);
            var frames = samples.Length / 2;
            ((double)frames).ShouldBe(48000, tolerance: 480);

            for (var i = 0; i < 512; i++) samples[i * 2].ShouldBe(samples[i * 2 + 1], tolerance: 1e-6f);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Seek_ChangesPosition()
    {
        var path = WriteWav("seek", 48000, 2, 5.0);
        try
        {
            using var decoder = FfmpegDecoder.Open(path);
            decoder.Read(new float[4096]);

            decoder.Seek(TimeSpan.FromSeconds(2));
            decoder.Position.TotalSeconds.ShouldBe(2.0, tolerance: 0.1);

            var samples = ReadAll(decoder);
            ((double)(samples.Length / 2)).ShouldBe(144000, tolerance: 2400);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Read_PastEnd_ReturnsZero()
    {
        var path = WriteWav("short", 48000, 2, 0.1);
        try
        {
            using var decoder = FfmpegDecoder.Open(path);
            ReadAll(decoder);
            decoder.Read(new float[1024]).ShouldBe(0);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Open_GarbageFile_ThrowsAudioFormatException()
    {
        var path = Path.Combine(Path.GetTempPath(), $"kyapputone-garbage-{Guid.NewGuid():N}.bin");
        File.WriteAllBytes(path, new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });
        try
        {
            Should.Throw<AudioFormatException>(() => FfmpegDecoder.Open(path));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
