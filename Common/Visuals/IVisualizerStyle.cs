using Vortice.Direct2D1;
using Vortice.Mathematics;

namespace KyappuTone.Common.Visuals;

/// <summary>One visualizer drawing style. Brushes are owned by the renderer.</summary>
public interface IVisualizerStyle
{
    /// <summary>Display name used by the style picker.</summary>
    string Name { get; }

    /// <summary>Draws one frame into the device context.</summary>
    void Draw(
        ID2D1DeviceContext context,
        ID2D1SolidColorBrush brush,
        float width,
        float height,
        ReadOnlySpan<float> bands,
        TimeSpan position);
}

/// <summary>Shared HSV helpers for visualizer styles.</summary>
public static class VisualizerPalette
{
    /// <summary>Builds a fully saturated colour from a 0..1 hue.</summary>
    public static Color4 FromHue(float hue, float alpha = 1f)
    {
        hue = hue - MathF.Floor(hue);
        var sector = hue * 6f;
        var fraction = sector - MathF.Floor(sector);
        var low = 0.15f;
        var high = 1f;
        var rising = low + (high - low) * fraction;
        var falling = high - (high - low) * fraction;

        return (int)sector switch
        {
            0 => new Color4(high, rising, low, alpha),
            1 => new Color4(falling, high, low, alpha),
            2 => new Color4(low, high, rising, alpha),
            3 => new Color4(low, falling, high, alpha),
            4 => new Color4(rising, low, high, alpha),
            _ => new Color4(high, low, falling, alpha),
        };
    }
}
