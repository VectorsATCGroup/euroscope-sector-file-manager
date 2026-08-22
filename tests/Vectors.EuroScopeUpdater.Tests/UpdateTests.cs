using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Vectors.EuroScopeUpdater.Core.Abstractions;
using Vectors.EuroScopeUpdater.Core.Updates;
using Vectors.EuroScopeUpdater.Infrastructure.Updates;
using Xunit;

namespace Vectors.EuroScopeUpdater.Tests;

public class AppVersionsTests
{
    [Theory]
    [InlineData("v1.0.3", "1.0.3")]
    [InlineData("1.0.3", "1.0.3")]
    [InlineData("1.0.3+b92b0c0f73ae31", "1.0.3")]
    [InlineData("1.2.0-beta.1", "1.2.0")]
    [InlineData("1.0.3.0", "1.0.3")]
    [InlineData("  V2.10  ", "2.10.0")]
    public void Parses_tags_and_informational_versions(string input, string expected)
    {
        var v = AppVersions.Parse(input);
        Assert.NotNull(v);
        Assert.Equal(expected, AppVersions.Format(v!));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("latest")]
    [InlineData("v1")]
    [InlineData("1.x.3")]
    [InlineData("1.2.3.4.5")]
    public void Rejects_garbage(string? input) => Assert.Null(AppVersions.Parse(input));

    [Theory]
    [InlineData("1.0.4", "1.0.3", true)]
    [InlineData("1.1.0", "1.0.9", true)]
    [InlineData("2.0.0", "1.9.9", true)]
    [InlineData("1.0.3", "1.0.3", false)]
    [InlineData("1.0.3.7", "1.0.3", false)] // revision is ignored
    [InlineData("1.0.2", "1.0.3", false)]
    public void IsNewer_compares_major_minor_patch(string candidate, string current, bool expected) =>
        Assert.Equal(expected, AppVersions.IsNewer(AppVersions.Parse(candidate)!, AppVersions.Parse(current)!));

    [Fact]
    public void UpdateCheckResult_flags_newer_release()
    {
        var release = Release("v1.0.4");
        Assert.True(new UpdateCheckResult(new Version(1, 0, 3), release).IsUpdateAvailable);
        Assert.False(new UpdateCheckResult(new Version(1, 0, 4), release).IsUpdateAvailable);
        Assert.False(new UpdateCheckResult(new Version(1, 0, 3), null).IsUpdateAvailable);
    }

    internal static AppRelease Release(string tag, string? url = null, long? size = null, string? sha = null) =>
        new(AppVersions.Parse(tag)!, tag, $"Release {tag}", "notes", $"https://github.com/x/y/releases/tag/{tag}", url, size, sha, null);
}

public class GitHubReleaseParserTests
{
    // Shape of GET /repos/{owner}/{repo}/releases/latest (trimmed to the fields that matter).
    private const string Json = """
        {
          "html_url": "https://github.com/VectorsATCGroup/euroscope-sector-file-manager/releases/tag/v1.0.3",
          "tag_name": "v1.0.3",
          "name": "EuroScope Sector File Manager v1.0.3",
          "draft": false,
          "prerelease": false,
          "published_at": "2026-08-18T14:42:28Z",
          "assets": [
            {
              "name": "Some-Other-File.zip",
              "size": 10,
              "browser_download_url": "https://github.com/VectorsATCGroup/euroscope-sector-file-manager/releases/download/v1.0.3/Some-Other-File.zip"
            },
            {
              "name": "VectorsEuroScopeSectorFileManager-Setup.exe",
              "size": 52555421,
              "digest": "sha256:f9613ed0fcb9103c7dafc3234a5e34d137480536ec633005fe642684dd51a8b1",
              "browser_download_url": "https://github.com/VectorsATCGroup/euroscope-sector-file-manager/releases/download/v1.0.3/VectorsEuroScopeSectorFileManager-Setup.exe"
            }
          ],
          "body": "## What's Changed\r\n* fix(browser): auto-allow multiple downloads"
        }
        """;

    private const string Prefix = "https://github.com/VectorsATCGroup/euroscope-sector-file-manager/releases/download/";

    [Fact]
    public void Parses_latest_release_and_picks_installer_asset()
    {
        var r = GitHubReleaseChecker.ParseLatestRelease(Json, GitHubReleaseChecker.DefaultInstallerAssetName, Prefix);

        Assert.NotNull(r);
        Assert.Equal("1.0.3", r!.VersionText);
        Assert.Equal("v1.0.3", r.Tag);
        Assert.True(r.HasInstaller);
        Assert.EndsWith("/v1.0.3/VectorsEuroScopeSectorFileManager-Setup.exe", r.InstallerUrl);
        Assert.Equal(52555421, r.InstallerSize);
        Assert.Equal("f9613ed0fcb9103c7dafc3234a5e34d137480536ec633005fe642684dd51a8b1", r.InstallerSha256);
        Assert.Contains("What's Changed", r.Notes);
        Assert.Equal(new DateTimeOffset(2026, 8, 18, 14, 42, 28, TimeSpan.Zero), r.PublishedAt);
    }

    [Fact]
    public void Ignores_installer_hosted_outside_the_repository()
    {
        var tampered = Json.Replace(
            "https://github.com/VectorsATCGroup/euroscope-sector-file-manager/releases/download/v1.0.3/VectorsEuroScopeSectorFileManager-Setup.exe",
            "https://evil.example.com/VectorsEuroScopeSectorFileManager-Setup.exe");
        var r = GitHubReleaseChecker.ParseLatestRelease(tampered, GitHubReleaseChecker.DefaultInstallerAssetName, Prefix);
        Assert.NotNull(r);
        Assert.False(r!.HasInstaller); // release is known, but nothing will be downloaded
    }

    [Theory]
    [InlineData("\"draft\": false", "\"draft\": true")]
    [InlineData("\"prerelease\": false", "\"prerelease\": true")]
    [InlineData("\"tag_name\": \"v1.0.3\"", "\"tag_name\": \"nightly\"")]
    public void Rejects_drafts_prereleases_and_unversioned_tags(string from, string to) =>
        Assert.Null(GitHubReleaseChecker.ParseLatestRelease(Json.Replace(from, to), GitHubReleaseChecker.DefaultInstallerAssetName, Prefix));

    [Theory]
    [InlineData("sha256:F9613ED0FCB9103C7DAFC3234A5E34D137480536EC633005FE642684DD51A8B1", "f9613ed0fcb9103c7dafc3234a5e34d137480536ec633005fe642684dd51a8b1")]
    [InlineData("sha256:abc", null)]
    [InlineData("md5:f9613ed0fcb9103c7dafc3234a5e34d137480536ec633005fe642684dd51a8b1", null)]
    [InlineData(null, null)]
    public void Parses_sha256_digest(string? digest, string? expected) =>
        Assert.Equal(expected, GitHubReleaseChecker.ParseSha256Digest(digest));
}

public class GitHubReleaseCheckerHttpTests
{
    private static readonly byte[] Installer = Encoding.ASCII.GetBytes("MZ fake installer payload, definitely not a real exe");
    private static string Sha(byte[] b) => Convert.ToHexString(SHA256.HashData(b)).ToLowerInvariant();

    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = new();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request);
            return Task.FromResult(respond(request));
        }
    }

    private static GitHubReleaseChecker Checker(FakeHandler handler) =>
        new(new HttpClient(handler), NullLogger<GitHubReleaseChecker>.Instance, new Version(1, 0, 3));

    [Fact]
    public async Task GetLatest_sends_user_agent_and_parses_payload()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"tag_name":"v9.9.9","name":"x","html_url":"https://github.com/VectorsATCGroup/euroscope-sector-file-manager/releases/tag/v9.9.9","assets":[]}""", Encoding.UTF8, "application/json"),
        });
        var checker = Checker(handler);

        var r = await checker.GetLatestAsync();

        Assert.NotNull(r);
        Assert.Equal("9.9.9", r!.VersionText);
        Assert.False(r.HasInstaller);
        var req = Assert.Single(handler.Requests);
        Assert.Equal(checker.LatestReleaseApiUrl, req.RequestUri!.ToString());
        Assert.Contains(req.Headers.UserAgent, p => p.Product?.Name == "VectorsEuroScopeSectorFileManager" && p.Product.Version == "1.0.3");
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Forbidden)] // rate limited
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task GetLatest_returns_null_on_http_errors(HttpStatusCode status)
    {
        var checker = Checker(new FakeHandler(_ => new HttpResponseMessage(status)));
        Assert.Null(await checker.GetLatestAsync());
    }

    [Fact]
    public async Task GetLatest_returns_null_when_offline_or_garbage()
    {
        var offline = Checker(new FakeHandler(_ => throw new HttpRequestException("no network")));
        Assert.Null(await offline.GetLatestAsync());

        var garbage = Checker(new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html>not json</html>") }));
        Assert.Null(await garbage.GetLatestAsync());
    }

    [Fact]
    public async Task Download_verifies_digest_and_size_and_reports_progress()
    {
        var url = "https://github.com/VectorsATCGroup/euroscope-sector-file-manager/releases/download/v1.0.4/VectorsEuroScopeSectorFileManager-Setup.exe";
        var release = AppVersionsTests.Release("v1.0.4", url, Installer.Length, Sha(Installer));
        var checker = Checker(new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Installer) }));
        var dir = Path.Combine(Path.GetTempPath(), "vatupd-update", Guid.NewGuid().ToString("N"));
        try
        {
            var reports = new List<DownloadProgress>();
            var path = await checker.DownloadInstallerAsync(release, dir, new SyncProgress(reports));

            Assert.True(File.Exists(path));
            Assert.Equal("VectorsEuroScopeSectorFileManager-Setup-1.0.4.exe", Path.GetFileName(path));
            Assert.Equal(Installer, await File.ReadAllBytesAsync(path));
            Assert.Empty(Directory.GetFiles(dir, "*.partial"));
            Assert.NotEmpty(reports);
            Assert.Equal(Installer.Length, reports[^1].BytesReceived);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task Download_rejects_tampered_payload_and_leaves_nothing_behind()
    {
        var url = "https://github.com/VectorsATCGroup/euroscope-sector-file-manager/releases/download/v1.0.4/VectorsEuroScopeSectorFileManager-Setup.exe";
        var release = AppVersionsTests.Release("v1.0.4", url, Installer.Length, Sha(Installer));
        var tampered = (byte[])Installer.Clone();
        tampered[0] ^= 0xFF;
        var checker = Checker(new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(tampered) }));
        var dir = Path.Combine(Path.GetTempPath(), "vatupd-update", Guid.NewGuid().ToString("N"));
        try
        {
            await Assert.ThrowsAsync<UpdateVerificationException>(() => checker.DownloadInstallerAsync(release, dir, null));
            Assert.Empty(Directory.GetFiles(dir));
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task Download_refuses_urls_outside_the_repository()
    {
        var release = AppVersionsTests.Release("v1.0.4", "https://evil.example.com/Setup.exe", 1, null);
        var checker = Checker(new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Installer) }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => checker.DownloadInstallerAsync(release, Path.GetTempPath(), null));
    }

    private sealed class SyncProgress(List<DownloadProgress> sink) : IProgress<DownloadProgress>
    {
        public void Report(DownloadProgress value) => sink.Add(value);
    }
}
