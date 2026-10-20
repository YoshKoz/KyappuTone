using KyappuTone.Core.Audio;
using Vortice;
using Vortice.Direct2D1;
using Vortice.Mathematics;

namespace KyappuTone.Common.Visuals.Styles;

/// <summary>Symmetric spectrum mirrored around the vertical centre.</summary>
public sealed class MirrorStyle : IVisualizerStyle
{
    public string Name => "Mirror";

    public void Draw(
        ID2D1DeviceContext context,
        ID2D1SolidColorBrush brush,
        float width,
        float height,
        ReadOnlySpan<float> bands,
        TimeSpan position)
    {
        if (bands.Length == 0 || width <= 0 || height <= 0) return;

        var slot = width / bands.Length;
        var barWidth = MathF.Max(1f, slot * 0.66f);
        var middle = height * 0.5f;

        for (var band = 0; band < bands.Length; band++)
        {
            var level = MathF.Sqrt(Math.Clamp(bands[band], 0f, 1f));
            var halfHeight = MathF.Max(1f, level * (middle - 4f));
            var left = band * slot + (slot - barWidth) * 0.5f;

            brush.Color = VisualizerPalette.FromHue(0.08f + band / (float)bands.Length * 0.45f);
            context.FillRectangle(new RawRectF(left, middle - halfHeight, left + barWidth, middle), brush);

            brush.Color = VisualizerPalette.FromHue(0.08f + band / (float)bands.Length * 0.45f, 0.45f);
            context.FillRectangle(new RawRectF(left, middle, left + barWidth, middle + halfHeight * 0.7f), brush);
        }
    }
}
