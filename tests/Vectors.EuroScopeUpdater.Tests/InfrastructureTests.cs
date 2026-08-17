using Microsoft.Extensions.Logging.Abstractions;
using Vectors.EuroScopeUpdater.Core.Locators;
using Vectors.EuroScopeUpdater.Core.Model;
using Vectors.EuroScopeUpdater.Infrastructure.AeroNav;
using Vectors.EuroScopeUpdater.Infrastructure.Archives;
using Vectors.EuroScopeUpdater.Infrastructure.Logging;
using Vectors.EuroScopeUpdater.Tests.Support;
using Xunit;

namespace Vectors.EuroScopeUpdater.Tests;

public class SevenZipArchiveExtractorTests
{
    [Fact]
    public void Lists_and_extracts_synthetic_7z_fixture()
    {
        var archive = Path.Combine(Repo.ArchivesDir, "SBRE-Install-Package_20260710055743-260701-0001.7z");
        Assert.True(File.Exists(archive), $"Missing synthetic fixture: {archive}");

        var extractor = new SevenZipArchiveExtractor(NullLogger<SevenZipArchiveExtractor>.Instance);

        var entries = extractor.List(archive);
        Assert.Contains(entries, e => e.RelativePath.EndsWith("SBRE-SBRE_20260710055743-260701-0001.sct"));
        Assert.Contains(entries, e => e.RelativePath.EndsWith("SBRE/NavData/airway.txt"));

        var dest = Path.Combine(Path.GetTempPath(), "vatupd-7z", Guid.NewGuid().ToString("N"));
        try
        {
            var written = extractor.ExtractAll(archive, dest);
            Assert.NotEmpty(written);
            Assert.True(File.Exists(Path.Combine(dest, "SBRE-SBRE_20260710055743-260701-0001.sct")));
            Assert.True(File.Exists(Path.Combine(dest, "SBRE", "NavData", "airway.txt")));
            // Nothing escaped the destination.
            Assert.All(Directory.GetFiles(dest, "*", SearchOption.AllDirectories),
                f => Assert.StartsWith(Path.GetFullPath(dest), Path.GetFullPath(f)));
        }
        finally { if (Directory.Exists(dest)) Directory.Delete(dest, true); }
    }
}

public class AeroNavParserTests
{
    // Real AeroNav format: FIR is a path segment, revision is a single digit.
    private const string Html = """
        <html><body>
          <a href="https://files.aero-nav.com/SBAO/Install-Package_20260810144829-260801-1.7z">Install</a>
          <a href="https://files.aero-nav.com/SBAO/Update-Package_20260810144815-260801-1.7z">Update</a>
          <a href="/SBAZ/Update-Package_20260810144508-260801-1.7z">Update (relative)</a>
          Older bare link: SBCW/Install-Package_20260710055743-260701-1.7z
        </body></html>
        """;

    [Fact]
    public void Parses_real_aeronav_listing()
    {
        var catalog = new AeroNavParser().Parse("SBXX", Html, "https://files.aero-nav.com/SBXX");

        Assert.Equal(2608, catalog.Airac.Value);
        Assert.Equal(PackageType.Install, catalog.Best("SBAO", PackageType.Install)!.Type);
        Assert.Equal(2608, catalog.Best("SBAO", PackageType.Update)!.Airac.Value);

        // Absolute href preserved (FIR in the path):
        Assert.StartsWith("https://files.aero-nav.com/SBAO/Install-Package_", catalog.Best("SBAO", PackageType.Install)!.DownloadRef);
        // Relative href resolved against the base host:
        Assert.StartsWith("https://files.aero-nav.com/SBAZ/", catalog.Best("SBAZ", PackageType.Update)!.DownloadRef);
        // Bare reference (no anchor) still discovered:
        Assert.Equal(2607, catalog.Best("SBCW", PackageType.Install)!.Airac.Value);
    }
}

public class LogRedactionTests
{
    [Theory]
    [InlineData("Cookie: session=abc123secret", "session=abc123secret")]
    [InlineData("Authorization: Bearer eyJhbGciOi", "eyJhbGciOi")]
    [InlineData("access_token=abc.def.ghi", "abc.def.ghi")]
    [InlineData("GET https://files.aero-nav.com/dl?token=SECRETVALUE", "SECRETVALUE")]
    public void Scrubs_sensitive_material(string input, string secret)
    {
        var scrubbed = LogRedaction.Scrub(input);
        Assert.DoesNotContain(secret, scrubbed);
        Assert.Contains("redacted", scrubbed);
    }
}

public class EuroScopeLocatorTests
{
    [Fact]
    public void Recognizes_marker_and_rejects_empty()
    {
        using var ws = new TestWorkspace();
        var locator = new EuroScopeLocator();
        Assert.False(locator.LooksLikeEuroScope(ws.EuroScopeDir)); // empty so far

        File.WriteAllText(Path.Combine(ws.EuroScopeDir, "version.txt"), "public\t3.2.10");
        Assert.True(locator.LooksLikeEuroScope(ws.EuroScopeDir));
    }

    [Fact]
    public void Rejects_missing_directory()
    {
        Assert.False(new EuroScopeLocator().LooksLikeEuroScope(Path.Combine(Path.GetTempPath(), "does-not-exist-xyz")));
    }
}
