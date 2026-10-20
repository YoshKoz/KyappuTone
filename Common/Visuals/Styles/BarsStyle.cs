using KyappuTone.Core.Audio;
using Vortice;
using Vortice.Direct2D1;
using Vortice.Mathematics;

namespace KyappuTone.Common.Visuals.Styles;

/// <summary>Classic spectrum: one vertical bar per log-spaced band.</summary>
public sealed class BarsStyle : IVisualizerStyle
{
    public string Name => "Bars";

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
        var barWidth = MathF.Max(1f, slot * 0.72f);

        for (var band = 0; band < bands.Length; band++)
        {
            var level = MathF.Sqrt(Math.Clamp(bands[band], 0f, 1f));
            var barHeight = MathF.Max(2f, level * (height - 6f));
            var left = band * slot + (slot - barWidth) * 0.5f;

            brush.Color = VisualizerPalette.FromHue(band / (float)bands.Length);
            context.FillRectangle(new RawRectF(left, height - barHeight - 2f, left + barWidth, height - 2f), brush);
        }
    }
}
