namespace Vectors.EuroScopeUpdater.Core.Model;

/// <summary>Local installation state of a FIR, correlated against the remote catalog.</summary>
public enum InstallStatus
{
    /// <summary>No install detected in the sector-files location.</summary>
    NotInstalled,

    /// <summary>Files present, but the version could not be proven (e.g. legacy manual install, no manifest).</summary>
    InstalledVersionUnknown,

    /// <summary>Installed version matches the newest available AIRAC.</summary>
    UpToDate,

    /// <summary>A newer AIRAC is available.</summary>
    UpdateAvailable,

    /// <summary>Installed by the updater, but files no longer match the recorded manifest hashes.</summary>
    LocallyModified,

    /// <summary>Expected core files are missing — the install looks partial/corrupt.</summary>
    InstallationIncomplete,
}

/// <summary>
/// Snapshot of a FIR's local state. <see cref="InstalledAirac"/> is null when unknown.
/// <see cref="ModifiedFiles"/> lists manifest-relative paths whose hash changed (only meaningful
/// when a manifest exists).
/// </summary>
public sealed record LocalFirState(
    string FirCode,
    InstallStatus Status,
    AiracCycle? InstalledAirac,
    bool HasManifest,
    IReadOnlyList<string> ModifiedFiles)
{
    /// <summary>Full installed version (cycle, within-cycle revision, package revision) when known.</summary>
    public SectorVersion? InstalledVersion { get; init; }

    /// <summary>Newest version the catalog offers for this FIR (null when no catalog was given).</summary>
    public SectorVersion? AvailableVersion { get; init; }

    public static LocalFirState NotInstalled(string fir) =>
        new(fir, InstallStatus.NotInstalled, null, false, Array.Empty<string>());
}
