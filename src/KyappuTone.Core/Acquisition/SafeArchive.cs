namespace KyappuTone.Core.Acquisition;

/// <summary>Guards zip extraction against path traversal.</summary>
public static class SafeArchive
{
    public static bool IsSafeEntry(string destinationRoot, string entryName)
    {
        if (string.IsNullOrWhiteSpace(destinationRoot) || string.IsNullOrWhiteSpace(entryName))
            return false;

        var normalized = entryName.Replace('/', '\\').Trim();
        if (normalized.Length == 0
            || Path.IsPathRooted(normalized)
            || normalized.Contains(':'))
            return false;

        var segments = normalized.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Any(segment => segment == ".."))
            return false;

        var root = Path.GetFullPath(destinationRoot).TrimEnd('\\');
        var full = Path.GetFullPath(Path.Combine(root, normalized));
        return full.StartsWith(root + '\\', StringComparison.OrdinalIgnoreCase);
    }
}
