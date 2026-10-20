namespace KyappuTone.Core.Imaging;

/// <summary>Generates collision-free thumbnail file names for parallel scans.</summary>
public static class ThumbnailNaming
{
    /// <summary>Returns a unique PNG file name for a generated cover thumbnail.</summary>
    public static string NewCoverFileName() => $"Cover_{Guid.NewGuid():N}.png";
}
