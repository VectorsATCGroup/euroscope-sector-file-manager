using Vectors.EuroScopeUpdater.Core.Model;
using Vectors.EuroScopeUpdater.Core.Packaging;
using Xunit;

namespace Vectors.EuroScopeUpdater.Tests;

public class AiracCycleTests
{
    [Theory]
    [InlineData("2608", true, 26, 8)]
    [InlineData("2601", true, 26, 1)]
    [InlineData("2613", true, 26, 13)]
    [InlineData("2614", false, 0, 0)] // cycle 14 invalid
    [InlineData("260", false, 0, 0)]
    [InlineData("abcd", false, 0, 0)]
    public void Parses_cycles(string text, bool ok, int year, int number)
    {
        Assert.Equal(ok, AiracCycle.TryParse(text, out var c));
        if (ok) { Assert.Equal(year, c.Year); Assert.Equal(number, c.Number); }
    }

    [Fact]
    public void Orders_chronologically()
    {
        Assert.True(new AiracCycle(2607) < new AiracCycle(2608));
        Assert.True(new AiracCycle(2613) < new AiracCycle(2701));
        Assert.True(new AiracCycle(2608) > new AiracCycle(2601));
    }
}

public class PackageNameTests
{
    [Fact]
    public void Parses_install_package_name()
    {
        Assert.True(PackageName.TryParsePackage("SBBS-Install-Package_20260810143935-260801-0001.7z", out var p));
        Assert.Equal("SBBS", p.Fir);
        Assert.Equal(PackageType.Install, p.Type);
        Assert.Equal(2608, p.Airac.Value);
        Assert.Equal(1, p.CycleRevision);
        Assert.Equal(1, p.PackageRevision);
        Assert.Equal(new DateTime(2026, 8, 10, 14, 39, 35, DateTimeKind.Utc), p.BuildTimestampUtc);
    }

    [Fact]
    public void Parses_update_package_name()
    {
        Assert.True(PackageName.TryParsePackage("SBRE-Update-Package_20260710055743-260701-0001.7z", out var p));
        Assert.Equal("SBRE", p.Fir);
        Assert.Equal(PackageType.Update, p.Type);
        Assert.Equal(2607, p.Airac.Value);
    }

    [Theory]
    [InlineData("SBBS-SBBS_20260810143935-260801-0001.sct", "sct", 2608)]
    [InlineData("SBRE-SBRE_20260320215209-260301-0001.ese", "ese", 2603)]
    [InlineData("SBCW-SBCW_20260418052814-260401-0001.rwy", "rwy", 2604)]
    public void Parses_versioned_sector_files(string name, string ext, int airac)
    {
        Assert.True(PackageName.TryParseSectorFile(name, out var sf));
        Assert.Equal(ext, sf.Extension);
        Assert.Equal(airac, sf.Airac.Value);
    }

    [Theory]
    [InlineData("random.txt")]
    [InlineData("SBBS-Install-Package_bad.7z")]
    [InlineData("SBBS-SBBS_20260810143935-260801-0001.txt")]
    public void Rejects_non_matching_names(string name)
    {
        Assert.False(PackageName.TryParsePackage(name, out _));
        Assert.False(PackageName.TryParseSectorFile(name, out _));
    }

    [Theory]
    [InlineData("https://files.aero-nav.com/SBAO/Install-Package_20260810144829-260801-1.7z", "SBAO", 2608, PackageType.Install)]
    [InlineData("https://files.aero-nav.com/SBRE/Update-Package_20260810143628-260801-1.7z", "SBRE", 2608, PackageType.Update)]
    [InlineData("/SBCW/Install-Package_20260710055743-260701-1.7z", "SBCW", 2607, PackageType.Install)]
    public void Parses_real_aeronav_urls(string url, string fir, int airac, PackageType type)
    {
        Assert.True(PackageName.TryParseFromUrl(url, out var p));
        Assert.Equal(fir, p.Fir);
        Assert.Equal(airac, p.Airac.Value);
        Assert.Equal(type, p.Type);
    }

    [Theory]
    [InlineData("https://files.aero-nav.com/SBAO/readme.txt")]
    [InlineData("https://files.aero-nav.com/Install-Package_20260810144829-260801-1.7z")] // no FIR segment
    public void Rejects_non_package_urls(string url) => Assert.False(PackageName.TryParseFromUrl(url, out _));

    [Fact]
    public void Version_rank_orders_across_cycle_and_revision()
    {
        PackageName.TryParsePackage("SBBS-Update-Package_20260810143935-260801-0001.7z", out var a);
        PackageName.TryParsePackage("SBBS-Update-Package_20260910143935-260901-0001.7z", out var b);
        Assert.True(b.VersionRank > a.VersionRank);
    }
}
