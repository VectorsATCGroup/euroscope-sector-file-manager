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

        // The versioned .sct that EuroScope actually loads is the ground truth for what is installed,
        // including AeroNav's same-cycle re-issues (260801 → 260802): its name carries the cycle, the
        // within-cycle revision and the package revision. A manifest is only trustworthy when it agrees
        // with the files on disk (same cycle and same within-cycle revision); if it does not (an
        // interrupted install, a manual change, files replaced outside the app), the manifest is stale
        // and must not drive the reported version or the "modified files" comparison.
        var fileVersion = SectorFiles.InferInstalledVersion(firDirectory);
        var manifest = _manifest.Read(firCode);
        SectorVersion? manifestVersion = manifest is not null && manifest.TryGetAirac(out var mAirac)
            ? new SectorVersion(mAirac, manifest.CycleRevision, manifest.PackageRevision)
            : null;
        var manifestMatchesDisk = fileVersion is not null && manifestVersion is not null
            && manifestVersion.Value.SameIssue(fileVersion.Value);

        // When the manifest describes this very install, it also knows which PACKAGE revision was
        // applied (a re-packaged release can ship the same stamped .sct), so take the higher of the two
        // and never offer the package we just installed again.
        SectorVersion? installedVersion = fileVersion is { } fv
            ? (manifestMatchesDisk && manifestVersion!.Value.PackageRevision > fv.PackageRevision
                ? fv with { PackageRevision = manifestVersion.Value.PackageRevision }
                : fv)
            : manifestVersion;

        var modifiedCore = manifestMatchesDisk
            ? DetectModifiedCoreFiles(manifest!, firDirectory, ct)
            : Array.Empty<string>();

        var latestRemote = catalog?.BestVersion(firCode);

        // Priority: an available update is the most actionable signal. A newer within-cycle revision
        // (2608 → 2608/2) or a newer package revision counts as an update, not only a newer cycle.
        if (latestRemote is not null && installedVersion is not null && installedVersion.Value < latestRemote.Value)
            return State(InstallStatus.UpdateAvailable);

        if (modifiedCore.Count > 0)
            return State(InstallStatus.LocallyModified, modifiedCore);

        if (latestRemote is not null && installedVersion is not null)
            return State(InstallStatus.UpToDate); // installed >= latest, proven

        // We may know the installed version but have nothing to compare it to yet (e.g. pre-auth), or
        // it is a legacy manual install with no manifest and no parseable version.
        return State(InstallStatus.InstalledVersionUnknown);

        LocalFirState State(InstallStatus s, IReadOnlyList<string>? mods = null) =>
            new(firCode, s, installedVersion?.Airac, manifestMatchesDisk, mods ?? Array.Empty<string>())
            {
                InstalledVersion = installedVersion,
                AvailableVersion = latestRemote,
            };
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
}
