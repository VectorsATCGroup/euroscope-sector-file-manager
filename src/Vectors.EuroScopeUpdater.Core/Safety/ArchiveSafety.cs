using Vectors.EuroScopeUpdater.Core.Abstractions;

namespace Vectors.EuroScopeUpdater.Core.Safety;

/// <summary>
/// Path-safety guards for extracting untrusted archives. The single rule: a resolved entry path must
/// stay strictly inside the destination directory. Everything else (traversal, absolute paths,
/// rooted paths, alternate separators, drive letters, UNC) is rejected.
/// </summary>
public static class ArchiveSafety
{
    /// <summary>
    /// Resolve an archive entry's relative path against <paramref name="destinationRoot"/> and return
    /// the absolute target path, or throw <see cref="UnsafeArchiveEntryException"/> if it would escape.
    /// </summary>
    public static string ResolveSafeTarget(string destinationRoot, string entryPath)
    {
        if (string.IsNullOrWhiteSpace(entryPath))
            throw new UnsafeArchiveEntryException(entryPath ?? "<null>");

        // Normalize separators so the check is OS-independent (packages use both '/' and '\').
        var normalized = entryPath.Replace('\\', '/').Trim();

        // Reject absolute / rooted / drive-qualified / UNC entries outright.
        if (normalized.StartsWith('/') || normalized.StartsWith("//") ||
            (normalized.Length >= 2 && normalized[1] == ':'))
            throw new UnsafeArchiveEntryException(entryPath);

        // Reject any explicit parent-traversal segment.
        foreach (var segment in normalized.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == "..")
                throw new UnsafeArchiveEntryException(entryPath);
        }

        var rootFull = Path.TrimEndingDirectorySeparator(Path.GetFullPath(destinationRoot));
        var candidate = Path.GetFullPath(Path.Combine(rootFull, normalized.Replace('/', Path.DirectorySeparatorChar)));

        // Defense in depth: after full canonicalization the candidate must be under the root,
        // compared as whole path segments (so "root-evil" is not accepted as being under "root").
        var rootWithSep = rootFull + Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!candidate.StartsWith(rootWithSep, comparison))
            throw new UnsafeArchiveEntryException(entryPath);

        return candidate;
    }

    /// <summary>True if the entry is safe (does not throw), for filtering/validation.</summary>
    public static bool IsSafe(string destinationRoot, string entryPath)
    {
        try { ResolveSafeTarget(destinationRoot, entryPath); return true; }
        catch (UnsafeArchiveEntryException) { return false; }
    }
}
