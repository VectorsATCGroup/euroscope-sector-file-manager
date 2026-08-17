using Vectors.EuroScopeUpdater.Core.Model;
using Vectors.EuroScopeUpdater.Core.Packaging;
using Vectors.EuroScopeUpdater.Core.Paths;
using Vectors.EuroScopeUpdater.Core.Safety;
using Vectors.EuroScopeUpdater.Core.Serialization;

namespace Vectors.EuroScopeUpdater.Core.Manifest;

public interface IManifestService
{
    LocalManifest? Read(string fir);
    void Write(LocalManifest manifest);
    void Delete(string fir);

    /// <summary>
    /// Build a manifest for a freshly installed/updated FIR by hashing every file currently in
    /// <paramref name="firDirectory"/>. <paramref name="package"/> supplies the version metadata.
    /// </summary>
    LocalManifest Build(string firDirectory, PackageName package, string sectorFileName, DateTime installedAtUtc,
        CancellationToken ct = default);
}

/// <summary>Reads/writes/derives per-FIR manifests (<c>state\&lt;FIR&gt;.json</c>).</summary>
public sealed class ManifestService : IManifestService
{
    private readonly AppPaths _paths;
    public ManifestService(AppPaths paths) => _paths = paths;

    public LocalManifest? Read(string fir) => AppJson.Read<LocalManifest>(_paths.ManifestFile(fir));

    public void Write(LocalManifest manifest)
    {
        _paths.EnsureCreated();
        AppJson.WriteAtomic(_paths.ManifestFile(manifest.Fir), manifest);
    }

    public void Delete(string fir)
    {
        var f = _paths.ManifestFile(fir);
        if (File.Exists(f)) File.Delete(f);
    }

    public LocalManifest Build(string firDirectory, PackageName package, string sectorFileName,
        DateTime installedAtUtc, CancellationToken ct = default)
    {
        var files = new List<ManifestFileEntry>();
        foreach (var (rel, full) in FileHashing.EnumerateFiles(firDirectory))
        {
            ct.ThrowIfCancellationRequested();
            var info = new FileInfo(full);
            files.Add(new ManifestFileEntry(rel, info.Length, FileHashing.Sha256(full)));
        }
        files.Sort((a, b) => string.CompareOrdinal(a.RelativePath, b.RelativePath));

        return new LocalManifest
        {
            Division = Division.VatsimBrasil.Id,
            Fir = package.Fir,
            Airac = package.Airac.ToString(),
            CycleRevision = package.CycleRevision,
            PackageRevision = package.PackageRevision,
            PackageType = package.Type.ToString(),
            BuildTimestampUtc = package.BuildTimestampUtc.ToString("O"),
            InstalledAtUtc = installedAtUtc.ToString("O"),
            SectorFileName = sectorFileName,
            Files = files,
        };
    }
}
