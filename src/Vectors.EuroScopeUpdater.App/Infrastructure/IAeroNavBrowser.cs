using Vectors.EuroScopeUpdater.Core.Abstractions;

namespace Vectors.EuroScopeUpdater.App.Infrastructure;

/// <summary>
/// The isolated authenticated browser session used to talk to AeroNav. Implemented with a dedicated
/// WebView2 profile in a temporary user-data folder. Credentials are entered only on AeroNav's own
/// pages; this app never sees them. Cookies live in the isolated profile for the session lifetime and
/// are cleared on <see cref="LogoutAsync"/>/dispose. Nothing sensitive is ever exposed or logged.
/// </summary>
public interface IAeroNavBrowser : IAsyncDisposable
{
    /// <summary>True once a listing page has been reached (i.e. the session is authenticated).</summary>
    bool IsAuthenticated { get; }

    /// <summary>
    /// Silently check whether a persisted session is still valid, WITHOUT showing any window. Returns
    /// true if authenticated. Used at startup so the login UI only appears when actually required.
    /// </summary>
    Task<bool> TryRestoreSessionAsync(CancellationToken ct = default);

    /// <summary>
    /// Ensure the user is authenticated, showing the official AeroNav/VATSIM/Navigraph login UI in a
    /// visible WebView2 window if needed. Returns when the listing host has been reached, or throws
    /// <see cref="AeroNavAuthRequiredException"/> if the user cancels.
    /// </summary>
    Task EnsureAuthenticatedAsync(CancellationToken ct = default);

    /// <summary>Navigate to <paramref name="url"/> and return its rendered HTML (for catalog parsing).</summary>
    /// <exception cref="AeroNavAuthRequiredException">Landed on a login host instead.</exception>
    Task<string> GetListingHtmlAsync(string url, CancellationToken ct = default);

    /// <summary>
    /// Download a package the session is authorized for, into <paramref name="destinationFile"/>,
    /// reporting byte progress. Delete-on-failure. Uses the browser's own request semantics.
    /// </summary>
    Task DownloadAsync(string downloadRef, string destinationFile,
        IProgress<DownloadProgress>? progress, CancellationToken ct = default);

    /// <summary>Clear the isolated session (cookies/storage) and reset to unauthenticated.</summary>
    Task LogoutAsync();
}
