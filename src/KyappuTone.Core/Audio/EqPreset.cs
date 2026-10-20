namespace KyappuTone.Core.Audio;

/// <summary>Built-in equalizer presets. Every preset has ten gains in dB.</summary>
public static class EqPreset
{
    /// <summary>All presets in menu order. "Flat" is always first.</summary>
    public static IReadOnlyList<(string Name, float[] Gains)> All { get; } = new (string, float[])[]
    {
        ("Flat", new float[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }),
        ("Rock", new float[] { 5, 4, 3, 1, -1, -1, 1, 3, 4, 5 }),
        ("Pop", new float[] { -1, 1, 3, 4, 3, 1, -1, -1, 1, 2 }),
        ("Jazz", new float[] { 3, 2, 1, 2, -1, -1, 0, 1, 2, 3 }),
        ("Classical", new float[] { 4, 3, 2, 1, 0, 0, 0, 2, 3, 4 }),
        ("Bass Boost", new float[] { 8, 7, 5, 2, 0, 0, 0, 0, 0, 0 }),
        ("Treble Boost", new float[] { 0, 0, 0, 0, 0, 0, 3, 5, 7, 8 }),
        ("Vocal", new float[] { -2, -1, 0, 2, 5, 5, 3, 1, 0, -1 }),
        ("Electronic", new float[] { 5, 4, 2, 0, -2, -1, 2, 4, 5, 5 }),
        ("Hip-Hop", new float[] { 7, 6, 4, 1, 0, 0, 1, 2, 4, 5 }),
        ("Acoustic", new float[] { 3, 2, 1, 1, 2, 2, 2, 3, 3, 2 }),
        ("Loudness", new float[] { 6, 5, 3, 0, 0, 0, 0, 2, 4, 5 }),
        ("Custom", new float[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }),
    };
}
