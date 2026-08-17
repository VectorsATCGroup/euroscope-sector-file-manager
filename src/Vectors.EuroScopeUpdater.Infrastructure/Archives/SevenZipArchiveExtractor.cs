using Microsoft.Extensions.Logging;
using SharpCompress.Archives;
using SharpCompress.Archives.SevenZip;
using SharpCompress.Readers;
using Vectors.EuroScopeUpdater.Core.Abstractions;
using Vectors.EuroScopeUpdater.Core.Safety;

namespace Vectors.EuroScopeUpdater.Infrastructure.Archives;

/// <summary>
/// <see cref="IArchiveExtractor"/> for AeroNav <c>.7z</c> packages, backed by SharpCompress. Every entry
/// is run through <see cref="ArchiveSafety"/> before a byte is written, so a malicious archive cannot
/// escape the destination directory. Package contents are never executed.
/// </summary>
public sealed class SevenZipArchiveExtractor : IArchiveExtractor
{
    private readonly ILogger<SevenZipArchiveExtractor> _log;
    public SevenZipArchiveExtractor(ILogger<SevenZipArchiveExtractor> log) => _log = log;

    public IReadOnlyList<ArchiveEntry> List(string archiveFile)
    {
        try
        {
            using var stream = File.OpenRead(archiveFile);
            using var archive = SevenZipArchive.OpenArchive(stream, new ReaderOptions());
            var result = new List<ArchiveEntry>();
            foreach (var e in archive.Entries)
            {
                if (e.IsDirectory || string.IsNullOrEmpty(e.Key)) continue;
                result.Add(new ArchiveEntry(e.Key.Replace('\\', '/'), e.Size));
            }
            return result;
        }
        catch (UnsafeArchiveEntryException) { throw; }
        catch (Exception ex)
        {
            throw new InvalidArchiveException("The package could not be read (corrupt or unsupported archive).", ex);
        }
    }

    public IReadOnlyList<string> ExtractAll(string archiveFile, string destinationDirectory, CancellationToken ct = default)
    {
        Directory.CreateDirectory(destinationDirectory);
        var written = new List<string>();
        try
        {
            using var stream = File.OpenRead(archiveFile);
            using var archive = SevenZipArchive.OpenArchive(stream, new ReaderOptions());
            foreach (var entry in archive.Entries)
            {
                ct.ThrowIfCancellationRequested();
                if (entry.IsDirectory || string.IsNullOrEmpty(entry.Key)) continue;

                // Safety gate — throws UnsafeArchiveEntryException on traversal/absolute paths.
                var target = ArchiveSafety.ResolveSafeTarget(destinationDirectory, entry.Key);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);

                using (var input = entry.OpenEntryStream())
                using (var output = File.Create(target))
                    input.CopyTo(output);

                written.Add(entry.Key.Replace('\\', '/'));
            }
        }
        catch (UnsafeArchiveEntryException)
        {
            throw; // surface path-traversal rejections verbatim
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidArchiveException("The package could not be extracted (corrupt archive).", ex);
        }

        if (written.Count == 0)
            throw new InvalidArchiveException("The package contained no files.");

        _log.LogInformation("Extracted {Count} files to staging", written.Count);
        return written;
    }
}
