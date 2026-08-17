using Vectors.EuroScopeUpdater.Core.Model;

namespace Vectors.EuroScopeUpdater.Core.Abstractions;

/// <summary>Progress of a download, in bytes.</summary>
public readonly record struct DownloadProgress(long BytesReceived, long? TotalBytes)
{
    public double? Fraction => TotalBytes is > 0 ? Math.Clamp((double)BytesReceived / TotalBytes.Value, 0, 1) : null;
}

/// <summary>
/// The single seam between the application and wherever packages come from. The initial
/// implementation is the authenticated AeroNav web source; a Sector-File-Provider/API source can
/// replace it later without touching UI, downloader, or the file engine.
/// Implementations must never expose credentials and must never log cookies/tokens/signed URLs.
/// </summary>
public interface ISectorPackageSource
{
    /// <summary>A stable name for logs/UI (e.g. "AeroNav (web)", "Fixtures").</summary>
    string DisplayName { get; }

    /// <summary>Fetch the available packages for a division.</summary>
    /// <exception cref="AeroNavAuthRequiredException">Authentication is required/expired.</exception>
    /// <exception cref="PackageSourceUnavailableException">The source could not be reached.</exception>
    Task<RemoteCatalog> GetCatalogAsync(Division division, CancellationToken ct = default);

    /// <summary>
    /// Download <paramref name="package"/> to <paramref name="destinationFile"/> (a caller-owned
    /// staging path), reporting byte progress. Must write atomically enough that a failure leaves no
    /// partial file the caller would mistake for complete (delete-on-failure).
    /// </summary>
    Task DownloadAsync(RemotePackage package, string destinationFile,
        IProgress<DownloadProgress>? progress, CancellationToken ct = default);
}

/// <summary>
/// Implemented by package sources that require an interactive authenticated session (the AeroNav web
/// source). Sources that don't implement this are treated as always-available (e.g. offline fixtures).
/// The app uses this to gate all tools behind authentication and to offer explicit sign-in / sign-out.
/// </summary>
public interface IAuthenticatingSource
{
    /// <summary>True once an authenticated session has been established.</summary>
    bool IsAuthenticated { get; }

    /// <summary>
    /// Silently check whether a persisted session is still valid (no UI shown). Returns true if
    /// authenticated. Lets the app skip the login prompt when the session is still good.
    /// </summary>
    Task<bool> TryRestoreSessionAsync(CancellationToken ct = default);

    /// <summary>Run the interactive authentication (shows the official pages). Throws
    /// <see cref="AeroNavAuthRequiredException"/> if the user cancels.</summary>
    Task AuthenticateAsync(CancellationToken ct = default);

    /// <summary>Clear the authenticated session.</summary>
    Task LogoutAsync();
}

/// <summary>Raised when the source needs the user to (re)authenticate before it can proceed.</summary>
public sealed class AeroNavAuthRequiredException(string? message = null)
    : Exception(message ?? "Authentication with AeroNav is required.");

/// <summary>Raised when the source is unreachable (network/host error).</summary>
public sealed class PackageSourceUnavailableException(string message, Exception? inner = null)
    : Exception(message, inner);

/// <summary>Raised when a requested package does not exist in the source.</summary>
public sealed class PackageUnavailableException(string message)
    : Exception(message);
