namespace Vectors.EuroScopeUpdater.Core.Updates;

/// <summary>
/// A published release of this application, as discovered from the project's release feed
/// (GitHub Releases). Carries only what the updater needs: the version, where the human-readable
/// notes live, and the installer asset to download (with its published size and SHA-256 digest so the
/// download can be verified before it is executed).
/// </summary>
public sealed record AppRelease(
    Version Version,
    string Tag,
    string Name,
    string? Notes,
    string HtmlUrl,
    string? InstallerUrl,
    long? InstallerSize,
    string? InstallerSha256,
    DateTimeOffset? PublishedAt)
{
    /// <summary>True when the release ships an installer asset we can download.</summary>
    public bool HasInstaller => !string.IsNullOrWhiteSpace(InstallerUrl);

    /// <summary>"1.2.3" (three components).</summary>
    public string VersionText => AppVersions.Format(Version);
}

/// <summary>Outcome of an update check.</summary>
public sealed record UpdateCheckResult(Version CurrentVersion, AppRelease? Latest)
{
    /// <summary>True when the latest published release is newer than the running version.</summary>
    public bool IsUpdateAvailable => Latest is not null && AppVersions.IsNewer(Latest.Version, CurrentVersion);
}
