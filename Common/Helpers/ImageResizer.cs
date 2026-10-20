using KyappuTone.Core.Imaging;
using TagLib;

namespace KyappuTone.Common.Helpers;

/// <summary>
/// Provides utilities for resizing and creating image thumbnails.
/// </summary>
public static class ImageResizer
{
	private static readonly Lazy<byte[]> FallbackImage = new(LoadFallbackImage, LazyThreadSafetyMode.ExecutionAndPublication);

	/// <summary>
	/// Creates a thumbnail image from the provided pictures and saves it as a PNG file in the specified thumbnail folder.
	/// </summary>
	/// <param name="thumbnailFolder">The folder where the thumbnail image will be stored. This determines the subfolder under the Thumbnails directory.</param>
	/// <param name="pictures">An array of IPicture objects representing the image(s) to be resized. If no valid images are provided, a default image will be used.</param>
	/// <param name="width">The desired width of the thumbnail image in pixels.</param>
	/// <param name="height">The desired height of the thumbnail image in pixels.</param>
	/// <returns>The file path of the created thumbnail image.</returns>
	public static string CreateThumbnailImage(ThumbnailFolder thumbnailFolder, IPicture[]? pictures, int width, int height)
	{
		var thumbnailFilePath = Path.Combine(Constants.ThumbnailsFolder, thumbnailFolder.ToString(), ThumbnailNaming.NewCoverFileName());
		EnsureDirectory(thumbnailFilePath);

		var imageData = ProcessThumbnail(pictures, width, height);
		System.IO.File.WriteAllBytes(thumbnailFilePath, imageData);

		return thumbnailFilePath;
	}

	/// <summary>
	/// Creates a thumbnail image from the provided pictures and saves it as a PNG file in the specified thumbnail folder.
	/// If no valid image data is available, a default application icon is used.
	/// </summary>
	/// <param name="thumbnailFolder">The folder where the thumbnail image will be stored. This defines the subfolder under the main Thumbnails directory.</param>
	/// <param name="pictures">An array of IPicture objects containing the image data to be processed. If no pictures are provided, a default image will be used.</param>
	/// <param name="fileName">An optional string specifying the name of the thumbnail file. If not provided, a randomly generated name will be used.</param>
	/// <returns>The file path of the created thumbnail image.</returns>
	public static string CreateThumbnailImage(ThumbnailFolder thumbnailFolder, IPicture[]? pictures, string? fileName = null)
	{
		var thumbnailFilePath = Path.Combine(Constants.ThumbnailsFolder, thumbnailFolder.ToString(), fileName ?? ThumbnailNaming.NewCoverFileName());
		EnsureDirectory(thumbnailFilePath);

		var primary = TryGetPrimaryPictureData(pictures);
		var imageData = primary is null
			? CoverArtProcessor.ReencodePng(FallbackImage.Value)
			: TryReencode(primary) ?? CoverArtProcessor.ReencodePng(FallbackImage.Value);
		System.IO.File.WriteAllBytes(thumbnailFilePath, imageData);

		return thumbnailFilePath;
	}

	/// <summary>
	/// Creates a thumbnail image from the provided pictures and saves it as a PNG file in the specified thumbnail folder.
	/// </summary>
	/// <param name="thumbnailFolder">The folder where the thumbnail image will be stored. This determines the subfolder under the Thumbnails directory.</param>
	/// <param name="pictures">An array of IPicture objects representing the image(s) to be resized. If no valid images are provided, a default image will be used.</param>
	/// <param name="size">The desired dimension for both the width and height of the square thumbnail image in pixels.</param>
	/// <returns>The file path of the created thumbnail image.</returns>
	public static string CreateThumbnailImage(ThumbnailFolder thumbnailFolder, IPicture[]? pictures, int size)
	{
		return CreateThumbnailImage(thumbnailFolder, pictures, size, size);
	}

	private static byte[] ProcessThumbnail(IPicture[]? pictures, int width, int height)
	{
		var primary = TryGetPrimaryPictureData(pictures);
		return CoverArtProcessor.BuildThumbnail(primary, FallbackImage.Value, width, height);
	}

	private static byte[]? TryGetPrimaryPictureData(IPicture[]? pictures)
	{
		try
		{
			return pictures?.Length > 0 ? pictures[0].Data.Data : null;
		}
		catch (Exception)
		{
			return null;
		}
	}

	private static byte[]? TryReencode(byte[] data)
	{
		try
		{
			return CoverArtProcessor.ReencodePng(data);
		}
		catch (ArgumentException)
		{
			return null;
		}
	}

	private static byte[] LoadFallbackImage()
	{
		try
		{
			return System.IO.File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.png"));
		}
		catch (Exception)
		{
			// The packaged asset is the normal source; a generated placeholder keeps thumbnails
			// working if it is ever missing or unreadable.
			return CoverArtProcessor.CreatePlaceholderPng(256);
		}
	}

	private static void EnsureDirectory(string thumbnailFilePath)
	{
		var dir = Path.GetDirectoryName(thumbnailFilePath);
		if (dir != null) Directory.CreateDirectory(dir);
	}
}
