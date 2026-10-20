namespace KyappuTone.Core.Acquisition;

/// <summary>Finds a file's SHA-256 in a release checksum list.</summary>
public static class ChecksumFile
{
    public static bool TryFind(string checksumText, string fileName, out string sha256)
    {
        sha256 = string.Empty;
        if (string.IsNullOrWhiteSpace(checksumText) || string.IsNullOrWhiteSpace(fileName))
            return false;

        foreach (var rawLine in checksumText.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
                continue;

            var separator = line.IndexOfAny([' ', '\t']);
            if (separator <= 0)
                continue;

            var hash = line[..separator].Trim();
            var name = line[(separator + 1)..].Trim().TrimStart('*');
            if (hash.Length != 64 || !hash.All(Uri.IsHexDigit))
                continue;

            if (!string.Equals(name, fileName, StringComparison.OrdinalIgnoreCase))
                continue;

            sha256 = hash;
            return true;
        }

        return false;
    }
}
