using System.Text;

namespace KyappuTone.Core.Radio;

/// <summary>Extracts the current StreamTitle value from an ICY metadata block.</summary>
public static class IcyMetadataParser
{
    public static string? TryParseTitle(byte[] metadata)
    {
        if (metadata is null || metadata.Length == 0)
            return null;

        var text = Encoding.UTF8.GetString(metadata).TrimEnd('\0').Trim();
        if (text.Length == 0)
            return null;

        const string marker = "StreamTitle='";
        var start = text.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (start < 0)
            return null;

        start += marker.Length;
        var end = text.IndexOf("';", start, StringComparison.Ordinal);
        if (end < 0)
            end = text.IndexOf('\'', start);
        if (end < 0)
            end = text.Length;

        var title = text[start..end].Trim();
        return title.Length == 0 ? null : title;
    }
}
