using Vectors.EuroScopeUpdater.Core.Abstractions;
using Vectors.EuroScopeUpdater.Core.Manifest;
using Vectors.EuroScopeUpdater.Core.Model;
using Vectors.EuroScopeUpdater.Core.Packaging;
using Vectors.EuroScopeUpdater.Core.Safety;

namespace Vectors.EuroScopeUpdater.Tests.Support;

/// <summary>
/// Test doubles that let the real <c>InstallEngine</c> run end-to-end without a 7z file. An "archive"
/// is represented by a pointer file whose content is the path to a content directory built by
/// <see cref="SyntheticPackages"/>. The extractor still enforces <see cref="ArchiveSafety"/> on copy.
/// </summary>
public sealed class FolderArchiveExtractor : IArchiveExtractor
{
    public IReadOnlyList<ArchiveEntry> List(string archiveFile)
    {
        var dir = ReadDir(archiveFile);
        return FileHashing.EnumerateFiles(dir)
            .Select(f => new ArchiveEntry(f.Relative, new FileInfo(f.Full).Length))
            .ToList();
    }

    public IReadOnlyList<string> ExtractAll(string archiveFile, string destinationDirectory, CancellationToken ct = default)
    {
        var dir = ReadDir(archiveFile);
        var written = new List<string>();
        foreach (var (rel, full) in FileHashing.EnumerateFiles(dir))
        {
            ct.ThrowIfCancellationRequested();
            var target = ArchiveSafety.ResolveSafeTarget(destinationDirectory, rel); // exercise safety
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(full, target, overwrite: true);
            written.Add(rel);
        }
        return written;
    }

    private static string ReadDir(string archiveFile) => File.ReadAllText(archiveFile).Trim();
}

/// <summary>A source that serves packages whose "download" is the content directory pointer.</summary>
public sealed class FolderPackageSource : ISectorPackageSource
{
    private readonly Dictionary<string, (RemotePackage Package, string ContentDir)> _byFile = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _divisionId;
    public bool FailDownload { get; set; }

    public FolderPackageSource(string divisionId = "SBXX") => _divisionId = divisionId;

    public string DisplayName => "Folder (test)";

    public RemotePackage Add(string fir, PackageType type, string cycle, string contentDir, int cycleRevision = 1, int packageRevision = 1)
    {
        var fileName = SyntheticPackages.PackageFileName(fir, type, cycle, cycleRevision, packageRevision);
        PackageName.TryParsePackage(fileName, out var name);
        var pkg = new RemotePackage(fir, type, name.Airac, name, fileName, contentDir,
            SizeBytes: null);
        _byFile[fileName] = (pkg, contentDir);
        return pkg;
    }

    public Task<RemoteCatalog> GetCatalogAsync(Division division, CancellationToken ct = default)
    {
        var packages = _byFile.Values.Select(v => v.Package).ToList();
        var airac = packages.Count > 0 ? new AiracCycle(packages.Max(p => p.Airac.Value)) : default;
        return Task.FromResult(new RemoteCatalog(_divisionId, airac, packages));
    }

    public Task DownloadAsync(RemotePackage package, string destinationFile,
        IProgress<DownloadProgress>? progress, CancellationToken ct = default)
    {
        if (FailDownload) throw new PackageSourceUnavailableException("simulated network failure");
        var contentDir = _byFile[package.FileName].ContentDir;
        Directory.CreateDirectory(Path.GetDirectoryName(destinationFile)!);
        File.WriteAllText(destinationFile, contentDir); // pointer "archive"
        progress?.Report(new DownloadProgress(100, 100));
        return Task.CompletedTask;
    }
}

/// <summary>A manifest service that throws from Build to force a post-commit failure (rollback test).</summary>
public sealed class ThrowingManifestService : IManifestService
{
    private readonly IManifestService _inner;
    public ThrowingManifestService(IManifestService inner) => _inner = inner;

    public LocalManifest? Read(string fir) => _inner.Read(fir);
    public void Write(LocalManifest manifest) => _inner.Write(manifest);
    public void Delete(string fir) => _inner.Delete(fir);

    public LocalManifest Build(string firDirectory, PackageName package, string sectorFileName,
        DateTime installedAtUtc, CancellationToken ct = default)
        => throw new InvalidOperationException("simulated post-commit failure");
}
