using Microsoft.Extensions.Logging;
using Vectors.EuroScopeUpdater.Core.Abstractions;
using Vectors.EuroScopeUpdater.Core.Model;
using Vectors.EuroScopeUpdater.Infrastructure.AeroNav;

namespace Vectors.EuroScopeUpdater.App.Infrastructure;

/// <summary>
/// <see cref="ISectorPackageSource"/> backed by the authenticated AeroNav website via
/// <see cref="IAeroNavBrowser"/>. Discovery and downloading go through the official authenticated
/// session; the pure <see cref="AeroNavParser"/> turns the listing HTML into the catalog. See
/// <c>docs/aeronav-integration.md</c> — the concrete listing URL/selectors are localized here and in
/// the parser so a future Provider/API source can replace this class without touching the rest of the app.
/// </summary>
public sealed class AeroNavWebPackageSource : ISectorPackageSource, IAuthenticatingSource
{
    // The public SBXX listing entry point. TODO(confirm-live): confirm final URL/behavior against the
    // authenticated site; only this class and AeroNavParser should ever need to change.
    private const string ListingUrlTemplate = "https://files.aero-nav.com/{0}";

    private readonly IAeroNavBrowser _browser;
    private readonly AeroNavParser _parser;
    private readonly ILogger<AeroNavWebPackageSource> _log;

    public AeroNavWebPackageSource(IAeroNavBrowser browser, ILogger<AeroNavWebPackageSource> log)
    {
        _browser = browser;
        _parser = new AeroNavParser();
        _log = log;
    }

    public string DisplayName => "AeroNav (web)";

    // ── IAuthenticatingSource ─────────────────────────────────────────────────────────────
    public bool IsAuthenticated => _browser.IsAuthenticated;
    public Task<bool> TryRestoreSessionAsync(CancellationToken ct = default) => _browser.TryRestoreSessionAsync(ct);
    public Task AuthenticateAsync(CancellationToken ct = default) => _browser.EnsureAuthenticatedAsync(ct);
    public Task LogoutAsync() => _browser.LogoutAsync();

    public async Task<RemoteCatalog> GetCatalogAsync(Division division, CancellationToken ct = default)
    {
        var url = string.Format(ListingUrlTemplate, division.Id);
        await _browser.EnsureAuthenticatedAsync(ct).ConfigureAwait(false);
        var html = await _browser.GetListingHtmlAsync(url, ct).ConfigureAwait(false);
        var catalog = _parser.Parse(division.Id, html, url);
        _log.LogInformation("AeroNav catalog for {Division}: {Count} packages (AIRAC {Airac})",
            division.Id, catalog.Packages.Count, catalog.Airac);

        // Diagnostic: if nothing parsed, snapshot the listing so the parser can be calibrated to the
        // real page structure. Local file only; never uploaded. Safe to delete. An empty listing is
        // never a real answer from AeroNav (every FIR always has packages), so report it as the source
        // being unavailable instead of silently showing an empty dashboard.
        if (catalog.Packages.Count == 0)
        {
            TryWriteListingSnapshot(html);
            throw new PackageSourceUnavailableException("A lista de pacotes do AeroNav não carregou.");
        }

        return catalog;
    }

    public async Task DownloadAsync(RemotePackage package, string destinationFile,
        IProgress<DownloadProgress>? progress, CancellationToken ct = default)
    {
        await _browser.EnsureAuthenticatedAsync(ct).ConfigureAwait(false);
        await _browser.DownloadAsync(package.DownloadRef, destinationFile, progress, ct).ConfigureAwait(false);
    }

    private void TryWriteListingSnapshot(string html)
    {
        try
        {
            var dir = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "VectorsATCGroup", "EuroScopeSectorFileManager", "logs");
            System.IO.Directory.CreateDirectory(dir);
            System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "last-listing.html"), html);
            _log.LogWarning("Catalog parsed empty — wrote listing snapshot to logs\\last-listing.html for diagnosis.");
        }
        catch { /* diagnostics must never break the flow */ }
    }
}
