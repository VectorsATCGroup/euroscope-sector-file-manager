namespace Vectors.EuroScopeUpdater.Core.Abstractions;

/// <summary>A single file entry read from an archive (directories are not surfaced).</summary>
public readonly record struct ArchiveEntry(string RelativePath, long Size);

/// <summary>
/// Extracts an archive (AeroNav ships <c>.7z</c>) into a destination directory. Implementations MUST
/// enforce archive-safety: normalize entry paths, reject path traversal and absolute paths, and never
/// write outside <c>destinationDirectory</c>. See <see cref="Safety.ArchiveSafety"/>.
/// </summary>
public interface IArchiveExtractor
{
    /// <summary>List entries without extracting (for validation / file counts).</summary>
    IReadOnlyList<ArchiveEntry> List(string archiveFile);

    /// <summary>
    /// Extract every file entry into <paramref name="destinationDirectory"/>, preserving the
    /// archive's internal folder structure. Returns the relative paths written.
    /// </summary>
    IReadOnlyList<string> ExtractAll(string archiveFile, string destinationDirectory, CancellationToken ct = default);
}

/// <summary>Raised when an archive is corrupt, empty, or structurally invalid.</summary>
public sealed class InvalidArchiveException(string message, Exception? inner = null)
    : Exception(message, inner);

/// <summary>Raised when an archive entry attempts to escape the destination (path traversal).</summary>
public sealed class UnsafeArchiveEntryException(string entryPath)
    : Exception($"Archive entry '{entryPath}' resolves outside the destination directory and was rejected.");
