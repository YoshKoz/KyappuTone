using KyappuTone.Audio;
using KyappuTone.Audio.Decoding;
using Shouldly;
using Xunit;

namespace KyappuTone.Audio.Tests;

public class AudioEngineTests
{
    private static string WriteTone(string name, double seconds, double frequency = 440, double amplitude = 0.25)
    {
        const int sampleRate = 48000;
        var path = Path.Combine(Path.GetTempPath(), $"kyapputone-engine-{name}-{Guid.NewGuid():N}.wav");
        var frames = (int)(sampleRate * seconds);

        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream);

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
            var value = (short)(Math.Sin(2 * Math.PI * frequency * i / sampleRate) * amplitude * short.MaxValue);
            writer.Write(value);
            writer.Write(value);
        }

        return path;
    }

    private static (AudioEngine Engine, NullAudioOutput Output) CreateEngine()
    {
        var output = new NullAudioOutput();
        var engine = new AudioEngine(output);
        return (engine, output);
    }

    private static void Pull(NullAudioOutput output, int frames, float[] buffer)
    {
        output.Pull(frames, buffer);
    }

    [Fact]
    public void Load_Then_Play_AdvancesPosition_ByRenderedFrames()
    {
        var path = WriteTone("advance", 2.0);
        try
        {
            var (engine, output) = CreateEngine();
            using var _ = engine;

            engine.Load(path, play: true);
            var buffer = new float[4800 * 2];
            Pull(output, 4800, buffer);
            Pull(output, 4800, buffer);

            engine.Position.TotalSeconds.ShouldBe(0.2, tolerance: 0.01);
            engine.IsPlaying.ShouldBeTrue();
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Pause_StopsAdvancing()
    {
        var path = WriteTone("pause", 2.0);
        try
        {
            var (engine, output) = CreateEngine();
            using var _ = engine;

            engine.Load(path, play: true);
            var buffer = new float[4800 * 2];
            Pull(output, 4800, buffer);
            var position = engine.Position;

            engine.Pause();
            Pull(output, 4800, buffer);

            engine.Position.ShouldBe(position);
            engine.IsPlaying.ShouldBeFalse();
            buffer.ShouldAllBe(sample => sample == 0f);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Seek_MovesPosition_WithinTolerance()
    {
        var path = WriteTone("seek", 4.0);
        try
        {
            var (engine, output) = CreateEngine();
            using var _ = engine;

            engine.Load(path, play: true);
            var buffer = new float[4800 * 2];
            Pull(output, 4800, buffer);

            engine.Seek(TimeSpan.FromSeconds(2));
            engine.Position.TotalSeconds.ShouldBe(2.0, tolerance: 0.05);

            Pull(output, 4800, buffer);
            engine.Position.TotalSeconds.ShouldBe(2.1, tolerance: 0.06);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void GaplessAdvance_RaisesTrackEnded_Once_And_StartsNext()
    {
        var first = WriteTone("g1", 0.5);
        var second = WriteTone("g2", 1.0, 550);
        try
        {
            var (engine, output) = CreateEngine();
            using var _ = engine;

            var ended = 0;
            engine.TrackEnded += (_, _) => ended++;

            engine.Load(first, play: true);
            engine.SetNext(second, TimeSpan.Zero);

            var buffer = new float[4800 * 2];
            for (var i = 0; i < 12; i++) Pull(output, 4800, buffer);

            ended.ShouldBe(1);
            engine.IsPlaying.ShouldBeTrue();
            engine.Position.TotalSeconds.ShouldBeLessThan(1.0);
        }
        finally
        {
            File.Delete(first);
            File.Delete(second);
        }
    }

    [Fact]
    public void Crossfade_OverlapsDecks_AndKeepsPeakBelowCeiling()
    {
        var first = WriteTone("c1", 2.0, 440);
        var second = WriteTone("c2", 2.0, 660);
        try
        {
            var (engine, output) = CreateEngine();
            using var _ = engine;

            engine.Load(first, play: true);
            engine.SetNext(second, TimeSpan.FromMilliseconds(300));

            var buffer = new float[4800 * 2];
            var peak = 0f;
            for (var i = 0; i < 25; i++)
            {
                Pull(output, 4800, buffer);
                foreach (var sample in buffer) peak = Math.Max(peak, Math.Abs(sample));
            }

            peak.ShouldBeGreaterThan(0f);
            peak.ShouldBeLessThanOrEqualTo(0.98f + 1e-6f);
            engine.IsPlaying.ShouldBeTrue();
        }
        finally
        {
            File.Delete(first);
            File.Delete(second);
        }
    }

    [Fact]
    public void Volume_Zero_RendersSilence_And_Mute_Overrides_Gain()
    {
        var path = WriteTone("volume", 1.0);
        try
        {
            var (engine, output) = CreateEngine();
            using var _ = engine;

            engine.Load(path, play: true);
            var buffer = new float[4800 * 2];

            engine.Volume = 0f;
            Pull(output, 4800, buffer);
            buffer.ShouldAllBe(sample => sample == 0f);

            engine.Volume = 1f;
            engine.IsMuted = true;
            Pull(output, 4800, buffer);
            buffer.ShouldAllBe(sample => sample == 0f);

            engine.IsMuted = false;
            Pull(output, 4800, buffer);
            buffer.ShouldContain(sample => sample != 0f);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Volume_Half_Halves_Peak()
    {
        var path = WriteTone("volume-half", 1.0, 440, 0.5);
        try
        {
            var (loud, loudOutput) = CreateEngine();
            using var _ = loud;
            var (half, halfOutput) = CreateEngine();
            using var __ = half;

            var bufferLoud = new float[4800 * 2];
            var bufferHalf = new float[4800 * 2];

            loud.Load(path, play: true);
            Pull(loudOutput, 4800, bufferLoud);

            half.Load(path, play: true);
            half.Volume = 0.5f;
            Pull(halfOutput, 4800, bufferHalf);

            var loudPeak = bufferLoud.Max(sample => Math.Abs(sample));
            var halfPeak = bufferHalf.Max(sample => Math.Abs(sample));

            halfPeak.ShouldBe(loudPeak * 0.5f, tolerance: 0.01f);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void OfflineRender_60Seconds_WithEqBoost_IsFastAndBounded()
    {
        var path = WriteTone("soak", 60.0, 220, 0.6);
        try
        {
            var (engine, output) = CreateEngine();
            using var _ = engine;

            for (var band = 0; band < 10; band++) engine.Equalizer.SetBandGain(band, 6f);

            engine.Load(path, play: true);

            var buffer = new float[4800 * 2];
            var peak = 0f;
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            for (var i = 0; i < 600; i++)
            {
                Pull(output, 4800, buffer);
                foreach (var sample in buffer)
                {
                    float.IsNaN(sample).ShouldBeFalse();
                    peak = Math.Max(peak, Math.Abs(sample));
                }
            }

            stopwatch.Stop();
            stopwatch.Elapsed.TotalSeconds.ShouldBeLessThan(10.0);
            peak.ShouldBeLessThanOrEqualTo(0.98f + 1e-6f);
            engine.Position.TotalSeconds.ShouldBe(60.0, tolerance: 0.2);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_WhileCrossfading_StartsNewTrackOnce()
    {
        var first = WriteTone("l1", 2.0);
        var second = WriteTone("l2", 2.0, 660);
        var replacement = WriteTone("l3", 1.0, 880);
        try
        {
            var (engine, output) = CreateEngine();
            using var _ = engine;

            engine.Load(first, play: true);
            engine.SetNext(second, TimeSpan.FromMilliseconds(500));

            var buffer = new float[4800 * 2];
            for (var i = 0; i < 30; i++) Pull(output, 4800, buffer);

            engine.Load(replacement, play: true);
            Pull(output, 4800, buffer);
            engine.Position.TotalSeconds.ShouldBe(0.1, tolerance: 0.03);
            engine.IsPlaying.ShouldBeTrue();
            engine.Duration.TotalSeconds.ShouldBe(1.0, tolerance: 0.05);
        }
        finally
        {
            File.Delete(first);
            File.Delete(second);
            File.Delete(replacement);
        }
    }
}
