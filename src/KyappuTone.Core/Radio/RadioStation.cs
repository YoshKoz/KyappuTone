namespace KyappuTone.Core.Radio;

/// <summary>One internet radio station returned by Radio Browser.</summary>
public sealed record RadioStation(
    string Uuid,
    string Name,
    string Url,
    string? FaviconUrl,
    string? Tags,
    string? Country,
    string? Codec,
    int Bitrate);
