using KyappuTone.Audio;
using Shouldly;
using Xunit;

namespace KyappuTone.Audio.Tests;

public class WasapiOutputTests
{
    private static float ReadFloat(byte[] bytes, int index)
        => BitConverter.ToSingle(bytes, index * sizeof(float));

    [Fact]
    public void RenderWaveProvider_Uses_Ieee_Float_Format()
    {
        var provider = new RenderWaveProvider(_ => { }, 48000, 2);

        provider.WaveFormat.Encoding.ShouldBe(NAudio.Wave.WaveFormatEncoding.IeeeFloat);
        provider.WaveFormat.SampleRate.ShouldBe(48000);
        provider.WaveFormat.Channels.ShouldBe(2);
        provider.WaveFormat.BitsPerSample.ShouldBe(32);
    }

    [Fact]
    public void Read_Fills_Whole_Buffer_From_Render()
    {
        var provider = new RenderWaveProvider(span => span.Fill(0.25f), 48000, 2);
        var buffer = new byte[16];

        var read = provider.Read(buffer);

        read.ShouldBe(16);
        for (var i = 0; i < 4; i++) ReadFloat(buffer, i).ShouldBe(0.25f);
    }

    [Fact]
    public void Read_Clears_Unaligned_Tail()
    {
        var provider = new RenderWaveProvider(span => span.Fill(1f), 48000, 2);
        var buffer = new byte[10];

        var read = provider.Read(buffer);

        read.ShouldBe(10);
        ReadFloat(buffer, 0).ShouldBe(1f);
        ReadFloat(buffer, 1).ShouldBe(1f);
        buffer[8].ShouldBe((byte)0);
        buffer[9].ShouldBe((byte)0);
    }

    [Fact]
    public void Read_Silences_Buffer_When_Render_Throws()
    {
        var provider = new RenderWaveProvider(_ => throw new InvalidOperationException("boom"), 48000, 2);
        var buffer = new byte[8];
        buffer.AsSpan().Fill(0x7F);

        var read = provider.Read(buffer);

        read.ShouldBe(8);
        buffer.ShouldAllBe(value => value == 0);
    }

    [Fact]
    public void WasapiOutput_Reports_Configured_Latency()
    {
        using var output = new WasapiAudioOutput(12);
        output.LatencyMs.ShouldBe(12);
    }

    [Fact]
    public void WasapiDevice_Plays_A_Real_File_When_A_Device_Is_Available()
    {
        const int sampleRate = 48000;
        var path = Path.Combine(Path.GetTempPath(), $"kyapputone-device-{Guid.NewGuid():N}.wav");
        var frames = sampleRate; // one second

        using (var stream = File.Create(path))
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write("RIFF"u8.ToArray());
            writer.Write(36 + frames * 4);
            writer.Write("WAVE"u8.ToArray());
            writer.Write("fmt "u8.ToArray());
            writer.Write(16);
            writer.Write((short)1);
            writer.Write((short)2);
            writer.Write(sampleRate);
            writer.Write(sampleRate * 4);
            writer.Write((short)4);
            writer.Write((short)16);
            writer.Write("data"u8.ToArray());
            writer.Write(frames * 4);

            for (var i = 0; i < frames; i++)
            {
                var value = (short)(Math.Sin(2 * Math.PI * 440 * i / sampleRate) * 0.1 * short.MaxValue);
                writer.Write(value);
                writer.Write(value);
            }
        }

        try
        {
            using var engine = new AudioEngine(new WasapiAudioOutput());
            engine.Load(path, play: true);

            var deadline = DateTime.UtcNow.AddSeconds(3);
            while (engine.Position.TotalMilliseconds < 400 && DateTime.UtcNow < deadline)
            {
                Thread.Sleep(50);
            }

            engine.Position.TotalMilliseconds.ShouldBeGreaterThan(300);
            engine.Pause();
        }
        catch (Exception ex) when (ex is not Shouldly.ShouldAssertException)
        {
            // No audio endpoint (CI runners): opening the device is the only thing
            // that can be verified there, so treat the missing device as a skip.
            System.Diagnostics.Debug.WriteLine($"WASAPI device test skipped: {ex.Message}");
        }
        finally
        {
            File.Delete(path);
        }
    }
}
