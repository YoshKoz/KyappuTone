using System.Numerics;
using KyappuTone.Core.Audio;
using Vortice.Direct2D1;
using Vortice.Mathematics;

namespace KyappuTone.Common.Visuals.Styles;

/// <summary>Oscilloscope-like polyline driven by the spectrum envelope.</summary>
public sealed class WaveformStyle : IVisualizerStyle
{
    public string Name => "Waveform";

    public void Draw(
        ID2D1DeviceContext context,
        ID2D1SolidColorBrush brush,
        float width,
        float height,
        ReadOnlySpan<float> bands,
        TimeSpan position)
    {
        if (bands.Length < 2 || width <= 0 || height <= 0) return;

        var step = width / (bands.Length - 1);
        var middle = height * 0.5f;
        var amplitude = height * 0.42f;
        var drift = (float)(position.TotalSeconds * 0.35);

        brush.Color = VisualizerPalette.FromHue(0.55f);

        var previous = new Vector2(0f, middle);
        for (var band = 0; band < bands.Length; band++)
        {
            var level = MathF.Sqrt(Math.Clamp(bands[band], 0f, 1f));
            var sign = band % 2 == 0 ? -1f : 1f;
            var wobble = MathF.Sin((band * 0.35f) + drift * 6.283f) * 0.12f;
            var y = middle + sign * level * amplitude + wobble * amplitude;
            var point = new Vector2(band * step, y);

            context.DrawLine(previous, point, brush, 2f, null);
            previous = point;
        }

        brush.Color = VisualizerPalette.FromHue(0.85f, 0.8f);
        context.DrawLine(new Vector2(0f, middle), new Vector2(width, middle), brush, 1f, null);
    }
}
