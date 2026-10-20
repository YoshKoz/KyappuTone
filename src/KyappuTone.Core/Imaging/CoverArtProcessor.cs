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
        var scale = Math.Min(1f, Math.Min((float)maxWidth / bitmap.Width, (float)maxHeight / bitmap.Height));
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

    /// <summary>
    /// Fits <paramref name="primary"/> inside the bounds, falling back to
    /// <paramref name="fallback"/> when the primary bytes are missing or undecodable.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown when the fallback bytes are not a decodable image.</exception>
    public static byte[] BuildThumbnail(byte[]? primary, byte[] fallback, int maxWidth, int maxHeight)
    {
        ArgumentNullException.ThrowIfNull(fallback);

        if (primary is { Length: > 0 })
        {
            try
            {
                return ResizeToPng(primary, maxWidth, maxHeight);
            }
            catch (ArgumentException)
            {
                // Undecodable embedded artwork: fall through to the fallback image.
            }
        }

        return ResizeToPng(fallback, maxWidth, maxHeight);
    }

    /// <summary>Creates a solid-colour square PNG used when no fallback asset is available.</summary>
    public static byte[] CreatePlaceholderPng(int size)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(size, 1);

        using var bitmap = new SKBitmap(size, size);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(new SKColor(0x20, 0x20, 0x20));
        }

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
