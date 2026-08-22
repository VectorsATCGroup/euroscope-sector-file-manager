using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Vectors.EuroScopeUpdater.Core.Abstractions;
using Vectors.EuroScopeUpdater.Core.Safety;
using Vectors.EuroScopeUpdater.Core.Updates;

namespace Vectors.EuroScopeUpdater.Infrastructure.Updates;

/// <summary>
/// <see cref="IUpdateChecker"/>/<see cref="IUpdateDownloader"/> backed by the project's public GitHub
/// Releases feed. Reads <c>/repos/{owner}/{repo}/releases/latest</c> (stable releases only, drafts and
/// pre-releases are never offered), picks the installer asset by its fixed file name, and only ever
/// downloads from the repository's own <c>releases/download/</c> URL. The downloaded installer is
/// verified against the SHA-256 digest and size GitHub publishes for the asset before it is returned.
/// The request carries no personal data (just a User-Agent naming this app and its version, which
/// GitHub requires).
/// </summary>
public sealed class GitHubReleaseChecker : IUpdateChecker, IUpdateDownloader
{
    public const string DefaultOwner = "VectorsATCGroup";
    public const string DefaultRepo = "euroscope-sector-file-manager";
    public const string DefaultInstallerAssetName = "VectorsEuroScopeSectorFileManager-Setup.exe";

    private static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(15);

    private readonly HttpClient _http;
    private readonly ILogger _log;
    private readonly string _owner;
    private readonly string _repo;
    private readonly string _assetName;

    public GitHubReleaseChecker(HttpClient http, ILogger<GitHubReleaseChecker> log, Version? appVersion = null,
        string owner = DefaultOwner, string repo = DefaultRepo, string installerAssetName = DefaultInstallerAssetName)
    {
        _http = http;
        _log = log;
        _owner = owner;
        _repo = repo;
        _assetName = installerAssetName;

        if (_http.DefaultRequestHeaders.UserAgent.Count == 0)
        {
            var ver = appVersion is null ? "0.0.0" : AppVersions.Format(appVersion);
            _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("VectorsEuroScopeSectorFileManager", ver));
            _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue($"(+https://github.com/{_owner}/{_repo})"));
        }
    }

    public string LatestReleaseApiUrl => $"https://api.github.com/repos/{_owner}/{_repo}/releases/latest";

    /// <summary>Only assets served from the repository's own release download path are accepted.</summary>
    public string AllowedDownloadPrefix => $"https://github.com/{_owner}/{_repo}/releases/download/";

    public async Task<AppRelease?> GetLatestAsync(CancellationToken ct = default)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(CheckTimeout);

            using var req = new HttpRequestMessage(HttpMethod.Get, LatestReleaseApiUrl);
            req.Headers.Accept.ParseAdd("application/vnd.github+json");
            req.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");

            using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                _log.LogWarning("Update check: GitHub returned {Status}", (int)resp.StatusCode);
                return null;
            }
            var json = await resp.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
            var release = ParseLatestRelease(json, _assetName, AllowedDownloadPrefix);
            if (release is null)
                _log.LogWarning("Update check: latest release could not be parsed");
            else
                _log.LogInformation("Update check: latest release is {Tag} (installer: {HasInstaller})", release.Tag, release.HasInstaller);
            return release;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Offline, DNS, TLS, timeout, rate limit… none of these should ever bother the user.
            _log.LogWarning("Update check failed: {Message}", ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Pure parser for a GitHub "latest release" JSON document. Public/static so it can be pinned by
    /// tests against a saved payload. Returns null for drafts, pre-releases, or unparseable tags.
    /// </summary>
    public static AppRelease? ParseLatestRelease(string json, string installerAssetName, string allowedDownloadPrefix)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object) return null;
        if (GetBool(root, "draft") || GetBool(root, "prerelease")) return null;

        var tag = GetString(root, "tag_name");
        var version = AppVersions.Parse(tag);
        if (version is null || tag is null) return null;

        string? installerUrl = null;
        long? installerSize = null;
        string? installerSha = null;
        if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
        {
            foreach (var asset in assets.EnumerateArray())
            {
                if (!string.Equals(GetString(asset, "name"), installerAssetName, StringComparison.OrdinalIgnoreCase))
                    continue;
                var url = GetString(asset, "browser_download_url");
                if (url is null || !url.StartsWith(allowedDownloadPrefix, StringComparison.OrdinalIgnoreCase))
                    continue; // never download from anywhere but the repository's own releases
                installerUrl = url;
                if (asset.TryGetProperty("size", out var size) && size.ValueKind == JsonValueKind.Number && size.TryGetInt64(out var sz))
                    installerSize = sz;
                installerSha = ParseSha256Digest(GetString(asset, "digest"));
                break;
            }
        }

        DateTimeOffset? published = null;
        if (DateTimeOffset.TryParse(GetString(root, "published_at"), null, System.Globalization.DateTimeStyles.RoundtripKind, out var p))
            published = p;

        return new AppRelease(
            version,
            tag,
            GetString(root, "name") ?? tag,
            GetString(root, "body"),
            GetString(root, "html_url") ?? $"https://github.com/{DefaultOwner}/{DefaultRepo}/releases/tag/{tag}",
            installerUrl,
            installerSize,
            installerSha,
            published);
    }

    /// <summary>GitHub publishes asset digests as "sha256:&lt;hex&gt;". Returns the lowercase hex, or null.</summary>
    public static string? ParseSha256Digest(string? digest)
    {
        if (string.IsNullOrWhiteSpace(digest)) return null;
        const string prefix = "sha256:";
        if (!digest.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;
        var hex = digest[prefix.Length..].Trim().ToLowerInvariant();
        return hex.Length == 64 && hex.All(Uri.IsHexDigit) ? hex : null;
    }

    public async Task<string> DownloadInstallerAsync(AppRelease release, string destinationDirectory,
        IProgress<DownloadProgress>? progress, CancellationToken ct = default)
    {
        if (!release.HasInstaller)
            throw new InvalidOperationException("This release has no installer asset.");
        if (!release.InstallerUrl!.StartsWith(AllowedDownloadPrefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Refusing to download an installer from outside the project's releases.");

        Directory.CreateDirectory(destinationDirectory);
        var finalPath = Path.Combine(destinationDirectory,
            $"{Path.GetFileNameWithoutExtension(_assetName)}-{release.VersionText}{Path.GetExtension(_assetName)}");
        var partialPath = finalPath + ".partial";

        // A previously downloaded, still-valid installer (e.g. the user clicked "later", then "update")
        // is reused instead of pulling 50 MB again.
        if (File.Exists(finalPath) && Verifies(finalPath, release, out _))
        {
            progress?.Report(new DownloadProgress(release.InstallerSize ?? 0, release.InstallerSize));
            return finalPath;
        }

        try
        {
            using var resp = await _http.GetAsync(release.InstallerUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            var total = resp.Content.Headers.ContentLength ?? release.InstallerSize;

            await using (var src = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
            await using (var dst = new FileStream(partialPath, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true))
            {
                var buffer = new byte[1 << 16];
                long received = 0, lastReported = 0;
                int read;
                while ((read = await src.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
                {
                    await dst.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                    received += read;
                    if (received - lastReported >= 256 * 1024 || received == total)
                    {
                        lastReported = received;
                        progress?.Report(new DownloadProgress(received, total));
                    }
                }
                progress?.Report(new DownloadProgress(received, total ?? received));
            }

            if (!Verifies(partialPath, release, out var why))
                throw new UpdateVerificationException(why);

            File.Move(partialPath, finalPath, overwrite: true);
            _log.LogInformation("Update installer for {Tag} downloaded and verified", release.Tag);
            return finalPath;
        }
        catch
        {
            TryDelete(partialPath);
            throw;
        }
    }

    private static bool Verifies(string path, AppRelease release, out string reason)
    {
        reason = "";
        var length = new FileInfo(path).Length;
        if (length == 0) { reason = "The downloaded installer is empty."; return false; }
        if (release.InstallerSize is > 0 && length != release.InstallerSize)
        {
            reason = $"The downloaded installer size ({length} bytes) does not match the published size ({release.InstallerSize} bytes).";
            return false;
        }
        if (!string.IsNullOrEmpty(release.InstallerSha256))
        {
            var actual = FileHashing.Sha256(path);
            if (!string.Equals(actual, release.InstallerSha256, StringComparison.OrdinalIgnoreCase))
            {
                reason = "The downloaded installer does not match the published SHA-256 digest.";
                return false;
            }
        }
        return true;
    }

    private static bool GetBool(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    private static string? GetString(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* ignore */ }
    }
}
