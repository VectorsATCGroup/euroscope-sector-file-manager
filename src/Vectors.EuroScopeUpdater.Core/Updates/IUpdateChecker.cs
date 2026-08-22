using Vectors.EuroScopeUpdater.Core.Abstractions;

namespace Vectors.EuroScopeUpdater.Core.Updates;

/// <summary>
/// Looks up the newest published release of this application. Implementations talk only to the
/// project's own public release feed (GitHub Releases), send no personal data, and must never throw
/// for ordinary network trouble: "could not check" is reported as <c>null</c>, never as a crash, because
/// the check runs opportunistically at startup.
/// </summary>
public interface IUpdateChecker
{
    /// <summary>Newest stable release, or null when it cannot be determined right now.</summary>
    Task<AppRelease?> GetLatestAsync(CancellationToken ct = default);
}

/// <summary>
/// Downloads a release's installer into a caller-owned directory, reporting byte progress, and
/// verifies it (SHA-256 digest and size when published) before handing the path back. A download that
/// fails verification is deleted and reported as an exception, never returned.
/// </summary>
public interface IUpdateDownloader
{
    Task<string> DownloadInstallerAsync(AppRelease release, string destinationDirectory,
        IProgress<DownloadProgress>? progress, CancellationToken ct = default);
}

/// <summary>Raised when a downloaded installer does not match its published digest/size.</summary>
public sealed class UpdateVerificationException(string message) : Exception(message);
