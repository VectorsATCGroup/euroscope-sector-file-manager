using Vectors.EuroScopeUpdater.Core.Install;
using Xunit;

namespace Vectors.EuroScopeUpdater.Tests;

public class ProfileRepointerTests
{
    [Fact]
    public void Repoints_only_the_sector_line_and_preserves_everything_else()
    {
        var dir = Path.Combine(Path.GetTempPath(), "vatupd-prf", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var prf = Path.Combine(dir, "sbre_radar.prf");
        var original = string.Join('\n', new[]
        {
            "Settings\tsector\t\\SBRE-SBRE_20260320215209-260301-0001.sct",
            "Settings\tSettingsfileSYMBOLOGY\t\\SBRE\\Settings\\RADAR\\symbology.txt",
            "LastSession\tcallsign\tSBRE_CTR",
            "LastSession\trange\t500",
        });
        File.WriteAllText(prf, original);

        var changed = ProfileRepointer.RepointFile(prf, "SBRE-SBRE_20260710055743-260701-0001.sct");

        Assert.True(changed);
        var updated = File.ReadAllLines(prf);
        Assert.Equal("Settings\tsector\t\\SBRE-SBRE_20260710055743-260701-0001.sct", updated[0]);
        // Untouched lines:
        Assert.Contains("LastSession\tcallsign\tSBRE_CTR", updated);
        Assert.Contains("LastSession\trange\t500", updated);
        Assert.Contains("Settings\tSettingsfileSYMBOLOGY\t\\SBRE\\Settings\\RADAR\\symbology.txt", updated);
        Assert.Equal("\\SBRE-SBRE_20260710055743-260701-0001.sct", ProfileRepointer.ReadSectorReference(prf));

        Directory.Delete(dir, true);
    }

    [Fact]
    public void Repoint_is_idempotent()
    {
        var dir = Path.Combine(Path.GetTempPath(), "vatupd-prf2", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var prf = Path.Combine(dir, "x.prf");
        File.WriteAllText(prf, "Settings\tsector\t\\NEW.sct\n");
        Assert.False(ProfileRepointer.RepointFile(prf, "NEW.sct")); // already correct → no change
        Directory.Delete(dir, true);
    }
}

public class SectorFilesTests
{
    [Fact]
    public void Finds_versioned_files_and_infers_airac()
    {
        var dir = Path.Combine(Path.GetTempPath(), "vatupd-sf", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "SBRE-SBRE_20260710055743-260701-0001.sct"), "x");
        File.WriteAllText(Path.Combine(dir, "SBRE-SBRE_20260710055743-260701-0001.ese"), "x");
        File.WriteAllText(Path.Combine(dir, "SBRE-SBRE_20260710055743-260701-0001.rwy"), "x");
        File.WriteAllText(Path.Combine(dir, "sbre_radar.prf"), "x");   // not versioned
        File.WriteAllText(Path.Combine(dir, "notes.txt"), "x");

        Assert.Equal(3, SectorFiles.FindVersioned(dir).Count);
        Assert.Equal("SBRE-SBRE_20260710055743-260701-0001.sct", SectorFiles.CurrentSctFileName(dir));
        Assert.Equal(2607, SectorFiles.InferInstalledAirac(dir)!.Value.Value);

        Directory.Delete(dir, true);
    }
}
