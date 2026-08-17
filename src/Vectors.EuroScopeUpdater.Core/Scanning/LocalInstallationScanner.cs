using Vectors.EuroScopeUpdater.Core.Install;
using Vectors.EuroScopeUpdater.Core.Manifest;
using Vectors.EuroScopeUpdater.Core.Model;
using Vectors.EuroScopeUpdater.Core.Safety;

namespace Vectors.EuroScopeUpdater.Core.Scanning;

public interface ILocalInstallationScanner
{
    /// <summary>Determine a FIR's local state, optionally correlating against a remote catalog.</summary>
    LocalFirState Scan(string firCode, string firDirectory, RemoteCatalog? catalog, CancellationToken ct = default);
}

/// <summary>
/// Correlates the local FIR folder, its manifest (if any) and the remote catalog into a
/// <see cref="LocalFirState"/>. Hashing (potentially many files) is done on demand and is intended to
/// be called off the UI thread. Never claims "Up to date" without proof (a manifest or a stamped
/// sector file whose AIRAC can be compared to the catalog).
/// </summary>
public sealed class LocalInstallationScanner : ILocalInstallationScanner
{
    private readonly IManifestService _manifest;
    public LocalInstallationScanner(IManifestService manifest) => _manifest = manifest;

    public LocalFirState Scan(string firCode, string firDirectory, RemoteCatalog? catalog, CancellationToken ct = default)
    {
        firCode = firCode.ToUpperInvariant();

        if (!Directory.Exists(firDirectory) || !Directory.EnumerateFileSystemEntries(firDirectory).Any())
            return LocalFirState.NotInstalled(firCode);

        var hasSct = SectorFiles.CurrentSctFileName(firDirectory) is not null;
        if (!hasSct)
            return new LocalFirState(firCode, InstallStatus.InstallationIncomplete, null, false, Array.Empty<string>());

        var manifest = _manifest.Read(firCode);
        AiracCycle? installedAirac =
            (manifest is not null && manifest.TryGetAirac(out var mAirac)) ? mAirac
            : SectorFiles.InferInstalledAirac(firDirectory);

        var modifiedCore = manifest is not null
            ? DetectModifiedCoreFiles(manifest, firDirectory, ct)
            : Array.Empty<string>();

        var latestRemote = LatestRemote(catalog, firCode);

        // Priority: an available update is the most actionable signal.
        if (latestRemote is not null && installedAirac is not null && installedAirac.Value < latestRemote.Value)
            return State(InstallStatus.UpdateAvailable);

        if (modifiedCore.Count > 0)
            return State(InstallStatus.LocallyModified, modifiedCore);

        if (latestRemote is not null && installedAirac is not null)
            return State(InstallStatus.UpToDate); // installedAirac >= latest, proven

        // We may know the installed AIRAC but have nothing to compare it to yet (e.g. pre-auth), or
        // it is a legacy manual install with no manifest and no parseable version.
        return State(InstallStatus.InstalledVersionUnknown);

        LocalFirState State(InstallStatus s, IReadOnlyList<string>? mods = null) =>
            new(firCode, s, installedAirac, manifest is not null, mods ?? Array.Empty<string>());
    }

    private static IReadOnlyList<string> DetectModifiedCoreFiles(LocalManifest manifest, string firDir, CancellationToken ct)
    {
        var modified = new List<string>();
        foreach (var entry in manifest.Files)
        {
            ct.ThrowIfCancellationRequested();
            if (PersonalizationHeuristics.IsUserOwned(entry.RelativePath))
                continue; // expected to change — ignore for status

            var full = Path.Combine(firDir, entry.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(full)) { modified.Add(entry.RelativePath); continue; }
            if (!string.Equals(FileHashing.Sha256(full), entry.Sha256, StringComparison.OrdinalIgnoreCase))
                modified.Add(entry.RelativePath);
        }
        return modified;
    }

    private static AiracCycle? LatestRemote(RemoteCatalog? catalog, string firCode)
    {
        if (catalog is null) return null;
        var update = catalog.Best(firCode, PackageType.Update)?.Airac;
        var install = catalog.Best(firCode, PackageType.Install)?.Airac;
        if (update is null) return install;
        if (install is null) return update;
        return update.Value > install.Value ? update : install;
    }
}
