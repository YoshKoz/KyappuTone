using System.Diagnostics;
using System.Globalization;
using KyappuTone.Audio;
using KyappuTone.Core.Audio;

var minutes = 1.0;
string? csvPath = null;
var visualizer = false;
var fixtures = Path.Combine(Path.GetTempPath(), "kyapputone-soak-fixtures");

for (var index = 0; index < args.Length; index++)
{
    switch (args[index])
    {
        case "--minutes":
            minutes = double.Parse(args[++index], CultureInfo.InvariantCulture);
            break;
        case "--csv":
            csvPath = args[++index];
            break;
        case "--visualizer":
            visualizer = true;
            break;
        case "--fixtures":
            fixtures = args[++index];
            break;
        default:
            Console.Error.WriteLine($"Unknown argument: {args[index]}");
            return 2;
    }
}

Directory.CreateDirectory(fixtures);
var files = new[] { "soak-a.wav", "soak-b.wav", "soak-c.wav" }
    .Select((name, index) => EnsureFixture(Path.Combine(fixtures, name), 220 + index * 110))
    .ToArray();

csvPath ??= Path.Combine(Environment.CurrentDirectory, $"soak-{(visualizer ? "eq-viz" : "eq")}.csv");
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(csvPath))!);

var output = new WasapiAudioOutput();
using var engine = new AudioEngine(output);
engine.ConfigureEqualizer(equalizer =>
{
    equalizer.ApplyPreset(EqPreset.All.Single(preset => preset.Name == "Rock").Gains);
    equalizer.PreampDb = -3f;
});

var current = 0;
var nextQueued = false;
var bars = new float[64];
var magnitudes = new float[engine.Fft.BinCount];
var cancelled = false;

engine.TrackEnded += (_, _) =>
{
    current = (current + 1) % files.Length;
    nextQueued = false;
};

Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancelled = true;
};

Timer? visualizerTimer = null;
if (visualizer)
{
    visualizerTimer = new Timer(_ =>
    {
        if (engine.Fft.TryRead(magnitudes)) VisualizerBands.Map(magnitudes, engine.Fft.SampleRate, bars.Length, bars);
    }, null, TimeSpan.Zero, TimeSpan.FromMilliseconds(16));
}

engine.Load(files[current], play: true);
var started = Stopwatch.StartNew();
var lastCpu = Process.GetCurrentProcess().TotalProcessorTime;
var lastSample = TimeSpan.Zero;
var totalCpu = 0.0;
var sampleCount = 0;
var peakWorkingSet = 0.0;
var cpuSamples = new List<double>();
var workingSetSamples = new List<double>();
long lastRenderedFrames = 0;

using var writer = new StreamWriter(csvPath);
writer.WriteLine("elapsed_s,cpu_percent_one_core,working_set_mb,position_s,rendered_audio_s,callbacks,unexpected_stops,managed_heap_mb");

void Sample(TimeSpan elapsed)
{
    var process = Process.GetCurrentProcess();
    process.Refresh();

    var cpuNow = process.TotalProcessorTime;
    var wall = elapsed - lastSample;
    var cpu = wall.TotalSeconds > 0 ? (cpuNow - lastCpu).TotalSeconds / wall.TotalSeconds * 100 : 0;
    lastCpu = cpuNow;
    lastSample = elapsed;

    totalCpu += cpu;
    sampleCount++;
    cpuSamples.Add(cpu);

    var workingSet = process.WorkingSet64 / (1024.0 * 1024.0);
    workingSetSamples.Add(workingSet);
    peakWorkingSet = Math.Max(peakWorkingSet, workingSet);

    var renderedFrames = output.RenderedFrames;
    var renderedDelta = (renderedFrames - lastRenderedFrames) / (double)engine.Fft.SampleRate;
    lastRenderedFrames = renderedFrames;

    var managedHeap = GC.GetTotalMemory(false) / (1024.0 * 1024.0);

    writer.WriteLine(string.Join(',',
        elapsed.TotalSeconds.ToString("F1", CultureInfo.InvariantCulture),
        cpu.ToString("F2", CultureInfo.InvariantCulture),
        workingSet.ToString("F2", CultureInfo.InvariantCulture),
        engine.Position.TotalSeconds.ToString("F3", CultureInfo.InvariantCulture),
        renderedDelta.ToString("F3", CultureInfo.InvariantCulture),
        output.RenderCallbacks,
        output.UnexpectedStops,
        managedHeap.ToString("F2", CultureInfo.InvariantCulture)));
}

Sample(TimeSpan.Zero);

while (!cancelled && started.Elapsed < TimeSpan.FromMinutes(minutes))
{
    Thread.Sleep(250);

    if (!nextQueued && engine.Duration - engine.Position <= TimeSpan.FromSeconds(4))
    {
        engine.SetNext(files[(current + 1) % files.Length], TimeSpan.FromSeconds(2));
        nextQueued = true;
    }

    if (started.Elapsed - lastSample >= TimeSpan.FromSeconds(30))
    {
        Sample(started.Elapsed);
    }
}

started.Stop();
Sample(started.Elapsed);

engine.Pause();
visualizerTimer?.Dispose();

var renderedAudio = output.RenderedFrames / (double)engine.Fft.SampleRate;
var ratio = renderedAudio / started.Elapsed.TotalSeconds;

Console.WriteLine($"minutes={minutes:F1} visualizer={visualizer}");
Console.WriteLine($"cpu_avg_one_core={totalCpu / Math.Max(1, sampleCount):F2}%");
Console.WriteLine($"working_set_avg_mb={workingSetSamples.DefaultIfEmpty().Average():F1}");
Console.WriteLine($"working_set_peak_mb={peakWorkingSet:F1}");
Console.WriteLine($"rendered_audio_s={renderedAudio:F1}");
Console.WriteLine($"realtime_ratio={ratio:F3}");
Console.WriteLine($"callbacks={output.RenderCallbacks}");
Console.WriteLine($"unexpected_stops={output.UnexpectedStops}");
Console.WriteLine($"csv={csvPath}");

var ok = output.UnexpectedStops == 0 && output.RenderCallbacks > 0 && ratio >= 0.5;
return ok ? 0 : 1;

static string EnsureFixture(string path, double frequency)
{
    if (File.Exists(path)) return path;

    const int sampleRate = 48000;
    const int seconds = 60;
    var frames = sampleRate * seconds;

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

    for (var frame = 0; frame < frames; frame++)
    {
        var left = (short)(Math.Sin(2 * Math.PI * frequency * frame / sampleRate) * 0.2 * short.MaxValue);
        var right = (short)(Math.Sin(2 * Math.PI * (frequency * 1.5) * frame / sampleRate) * 0.2 * short.MaxValue);
        writer.Write(left);
        writer.Write(right);
    }

    return path;
}
