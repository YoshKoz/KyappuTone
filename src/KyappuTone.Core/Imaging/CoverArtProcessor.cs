using SkiaSharp;

namespace KyappuTone.Core.Imaging;

/// <summary>Decode, resize and re-encode embedded cover art without external licenses.</summary>
public static class CoverArtProcessor
{
    /// <summary>Decodes <paramref name="source"/> and fits it inside the given bounds, preserving aspect ratio.</summary>
    /// <exception cref="ArgumentException">Thrown when the bytes are not a decodable image.</exception>
    public static byte[] ResizeToPng(byte[] source, int maxWidth, int maxHeight)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxWidth, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxHeight, 1);

        using var bitmap = Decode(source);
        var scale = Math.Min((float)maxWidth / bitmap.Width, (float)maxHeight / bitmap.Height);
        var width = Math.Max(1, (int)Math.Round(bitmap.Width * scale));
        var height = Math.Max(1, (int)Math.Round(bitmap.Height * scale));

        using var resized = bitmap.Resize(new SKImageInfo(width, height), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear))
            ?? throw new ArgumentException("Image resize failed.", nameof(source));
        return Encode(resized);
    }

    /// <summary>Decodes and re-encodes <paramref name="source"/> as PNG at its original size.</summary>
    /// <exception cref="ArgumentException">Thrown when the bytes are not a decodable image.</exception>
    public static byte[] ReencodePng(byte[] source)
    {
        ArgumentNullException.ThrowIfNull(source);
        using var bitmap = Decode(source);
        return Encode(bitmap);
    }

    private static SKBitmap Decode(byte[] source)
    {
        if (source.Length == 0) throw new ArgumentException("Image data is empty.", nameof(source));
        return SKBitmap.Decode(source) ?? throw new ArgumentException("Image data could not be decoded.", nameof(source));
    }

    private static byte[] Encode(SKBitmap bitmap)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
