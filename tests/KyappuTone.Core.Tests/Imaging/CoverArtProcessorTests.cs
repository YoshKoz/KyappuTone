using KyappuTone.Core.Imaging;
using Shouldly;
using SkiaSharp;
using Xunit;

namespace KyappuTone.Core.Tests.Imaging;

public class CoverArtProcessorTests
{
    private static byte[] MakePng(int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static (int Width, int Height) SizeOf(byte[] png)
    {
        using var bitmap = SKBitmap.Decode(png);
        return (bitmap.Width, bitmap.Height);
    }

    [Fact]
    public void ResizeToPng_ScalesWithinBounds_AndPreservesAspect()
    {
        var result = CoverArtProcessor.ResizeToPng(MakePng(40, 20), 10, 10);
        SizeOf(result).ShouldBe((10, 5));
    }

    [Fact]
    public void ResizeToPng_PortraitSource_FitsLongestEdge()
    {
        var result = CoverArtProcessor.ResizeToPng(MakePng(30, 60), 20, 20);
        SizeOf(result).ShouldBe((10, 20));
    }

    [Fact]
    public void ReencodePng_KeepsDimensions()
    {
        var result = CoverArtProcessor.ReencodePng(MakePng(17, 9));
        SizeOf(result).ShouldBe((17, 9));
    }

    [Fact]
    public void ReencodePng_ThrowsOnGarbage()
    {
        var garbage = new byte[] { 0x00, 0x01, 0x02, 0x03 };
        Should.Throw<ArgumentException>(() => CoverArtProcessor.ReencodePng(garbage));
    }

    [Fact]
    public void ResizeToPng_ThrowsOnGarbage()
    {
        var garbage = new byte[] { 0xFF, 0xD8, 0x00, 0x00 };
        Should.Throw<ArgumentException>(() => CoverArtProcessor.ResizeToPng(garbage, 10, 10));
    }

    [Fact]
    public void ResizeToPng_DoesNotUpscaleSmallSource()
    {
        var result = CoverArtProcessor.ResizeToPng(MakePng(4, 4), 10, 10);
        SizeOf(result).ShouldBe((4, 4));
    }

    [Fact]
    public void BuildThumbnail_UsesFallback_WhenPrimaryIsInvalid()
    {
        var fallback = MakePng(8, 8);
        var result = CoverArtProcessor.BuildThumbnail(new byte[] { 0x00, 0x01 }, fallback, 10, 10);
        SizeOf(result).ShouldBe((8, 8));
    }

    [Fact]
    public void BuildThumbnail_UsesFallback_WhenPrimaryIsNull()
    {
        var fallback = MakePng(6, 6);
        var result = CoverArtProcessor.BuildThumbnail(null, fallback, 10, 10);
        SizeOf(result).ShouldBe((6, 6));
    }

    [Fact]
    public void BuildThumbnail_ResizesPrimary_WhenPrimaryIsValid()
    {
        var result = CoverArtProcessor.BuildThumbnail(MakePng(40, 20), MakePng(8, 8), 10, 10);
        SizeOf(result).ShouldBe((10, 5));
    }

    [Fact]
    public void CreatePlaceholderPng_ProducesDecodableSquare()
    {
        var result = CoverArtProcessor.CreatePlaceholderPng(64);
        SizeOf(result).ShouldBe((64, 64));
    }
}
