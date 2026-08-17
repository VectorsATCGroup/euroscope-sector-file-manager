using Vectors.EuroScopeUpdater.Core.Abstractions;
using Vectors.EuroScopeUpdater.Core.Safety;
using Xunit;

namespace Vectors.EuroScopeUpdater.Tests;

public class ArchiveSafetyTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "vatupd-safety-root");

    [Theory]
    [InlineData("SBRE/NavData/airway.txt")]
    [InlineData("SBRE\\NavData\\isec.txt")]
    [InlineData("sbre_radar.prf")]
    [InlineData("./SBRE/ICAO/ICAO_Airports.txt")]
    public void Accepts_safe_relative_entries(string entry)
    {
        var target = ArchiveSafety.ResolveSafeTarget(Root, entry);
        Assert.StartsWith(Path.GetFullPath(Root), target);
        Assert.True(ArchiveSafety.IsSafe(Root, entry));
    }

    [Theory]
    [InlineData("../evil.txt")]
    [InlineData("..\\..\\Windows\\System32\\evil.dll")]
    [InlineData("SBRE/../../escape.txt")]
    [InlineData("/etc/passwd")]
    [InlineData("\\\\server\\share\\x.txt")]
    [InlineData("C:\\Windows\\evil.txt")]
    [InlineData("foo/../../bar")]
    [InlineData("")]
    public void Rejects_traversal_and_absolute_entries(string entry)
    {
        Assert.False(ArchiveSafety.IsSafe(Root, entry));
        Assert.Throws<UnsafeArchiveEntryException>(() => ArchiveSafety.ResolveSafeTarget(Root, entry));
    }

    [Fact]
    public void Does_not_accept_sibling_prefix_directory()
    {
        // "root-evil" must not be treated as being inside "root".
        var root = Path.Combine(Path.GetTempPath(), "vatupd-prefix", "root");
        Assert.False(ArchiveSafety.IsSafe(root, "../root-evil/x.txt"));
    }
}
