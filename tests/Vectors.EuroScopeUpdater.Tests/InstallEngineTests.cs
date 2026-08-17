using Vectors.EuroScopeUpdater.Core.Install;
using Vectors.EuroScopeUpdater.Core.Manifest;
using Vectors.EuroScopeUpdater.Core.Model;
using Vectors.EuroScopeUpdater.Core.Operations;
using Vectors.EuroScopeUpdater.Tests.Support;
using Xunit;

namespace Vectors.EuroScopeUpdater.Tests;

public class InstallEngineTests
{
    private static readonly Fir Sbre = new("SBRE", "Recife FIR");

    private static string BuildContent(TestWorkspace ws, string tag, Action<string> build)
    {
        var dir = Path.Combine(ws.Root, "content", tag);
        Directory.CreateDirectory(dir);
        build(dir);
        return dir;
    }

    private static InstallRequest Request(TestWorkspace ws, EngineBuilder b, PackageType type, string cycle, string contentTag, Action<string> build)
    {
        var content = BuildContent(ws, contentTag, d => build(d));
        var pkg = b.Source.Add("SBRE", type, cycle, content);
        return new InstallRequest(Sbre, type == PackageType.Install ? OperationKind.CleanInstall : OperationKind.Update,
            pkg, ws.FirDir("SBRE"), ws.SectorFilesDir);
    }

    [Fact]
    public async Task Clean_install_lays_down_full_fir_and_manifest()
    {
        using var ws = new TestWorkspace();
        var b = new EngineBuilder(ws);
        var req = Request(ws, b, PackageType.Install, "2607", "inst2607",
            d => SyntheticPackages.BuildInstall(d, "SBRE", "2607"));

        var result = await b.Build().RunAsync(req, null);

        Assert.True(result.Success);
        var fir = ws.FirDir("SBRE");
        Assert.True(File.Exists(Path.Combine(fir, $"{SyntheticPackages.SectorBase("SBRE", "2607")}.sct")));
        Assert.True(File.Exists(Path.Combine(fir, "SBRE", "NavData", "airway.txt")));
        Assert.True(File.Exists(Path.Combine(fir, "sbre_radar.prf")));
        Assert.NotNull(new ManifestService(ws.AppPaths).Read("SBRE"));
        // Profile points at the installed sct.
        var reference = ProfileRepointer.ReadSectorReference(Path.Combine(fir, "sbre_radar.prf"));
        Assert.EndsWith("260701-0001.sct", reference);
    }

    [Fact]
    public async Task Update_refreshes_airac_data_and_preserves_personalization()
    {
        using var ws = new TestWorkspace();
        var b = new EngineBuilder(ws);
        var fir = ws.FirDir("SBRE");

        // 1) Base install at 2607.
        await b.Build().RunAsync(Request(ws, b, PackageType.Install, "2607", "inst2607",
            d => SyntheticPackages.BuildInstall(d, "SBRE", "2607")), null);

        // 2) The controller personalizes: edit a preserved file and add a new personal file.
        var symbology = Path.Combine(fir, "SBRE", "Settings", "RADAR", "symbology.txt");
        File.WriteAllText(symbology, "MY CUSTOM COLORS");
        var extra = Path.Combine(fir, "SBRE", "Settings", "RADAR", "my_extra_layout.txt");
        File.WriteAllText(extra, "personal layout");

        // 3) Apply the 2608 update.
        var result = await b.Build().RunAsync(Request(ws, b, PackageType.Update, "2608", "upd2608",
            d => SyntheticPackages.BuildUpdate(d, "SBRE", "2608")), null);

        Assert.True(result.Success);

        // AIRAC data refreshed:
        Assert.Equal("airway 2608", File.ReadAllText(Path.Combine(fir, "SBRE", "NavData", "airway.txt")));
        // New sector files present, old ones gone:
        Assert.True(File.Exists(Path.Combine(fir, $"{SyntheticPackages.SectorBase("SBRE", "2608")}.sct")));
        Assert.False(File.Exists(Path.Combine(fir, $"{SyntheticPackages.SectorBase("SBRE", "2607")}.sct")));
        Assert.False(File.Exists(Path.Combine(fir, $"{SyntheticPackages.SectorBase("SBRE", "2607")}.ese")));
        // Personalization PRESERVED:
        Assert.Equal("MY CUSTOM COLORS", File.ReadAllText(symbology));
        Assert.True(File.Exists(extra));
        Assert.Equal("personal layout", File.ReadAllText(extra));
        // Plugin dll (omitted by update) preserved:
        Assert.True(File.Exists(Path.Combine(fir, "SBRE", "Plugins", "DiscordEuroscope.dll")));
        // Profile re-pointed to the new sct, its own content preserved:
        var prf = Path.Combine(fir, "sbre_radar.prf");
        Assert.EndsWith("260801-0001.sct", ProfileRepointer.ReadSectorReference(prf));
        Assert.Contains("LastSession\tcallsign\tSBRE_CTR", File.ReadAllLines(prf));
        // Manifest now at 2608:
        Assert.Equal("2608", new ManifestService(ws.AppPaths).Read("SBRE")!.Airac);
    }

    [Fact]
    public async Task Download_failure_leaves_existing_install_untouched()
    {
        using var ws = new TestWorkspace();
        var b = new EngineBuilder(ws);
        var fir = ws.FirDir("SBRE");
        await b.Build().RunAsync(Request(ws, b, PackageType.Install, "2607", "inst2607",
            d => SyntheticPackages.BuildInstall(d, "SBRE", "2607")), null);
        var before = File.ReadAllText(Path.Combine(fir, "SBRE", "NavData", "airway.txt"));

        b.Source.FailDownload = true;
        var result = await b.Build().RunAsync(Request(ws, b, PackageType.Update, "2608", "upd2608",
            d => SyntheticPackages.BuildUpdate(d, "SBRE", "2608")), null);

        Assert.False(result.Success);
        Assert.False(result.RolledBack); // never reached commit
        Assert.Equal(before, File.ReadAllText(Path.Combine(fir, "SBRE", "NavData", "airway.txt")));
        // No leftover work directory.
        Assert.False(Directory.Exists(Path.Combine(ws.SectorFilesDir, InstallEngine.WorkFolderName)));
    }

    [Fact]
    public async Task Post_commit_failure_rolls_back_to_previous_state()
    {
        using var ws = new TestWorkspace();
        var b = new EngineBuilder(ws);
        var fir = ws.FirDir("SBRE");

        await b.Build().RunAsync(Request(ws, b, PackageType.Install, "2607", "inst2607",
            d => SyntheticPackages.BuildInstall(d, "SBRE", "2607")), null);
        var before = File.ReadAllText(Path.Combine(fir, "SBRE", "NavData", "airway.txt"));

        // Force a failure AFTER commit (manifest build throws).
        b.Manifest = new ThrowingManifestService(new ManifestService(ws.AppPaths));
        var result = await b.Build().RunAsync(Request(ws, b, PackageType.Update, "2608", "upd2608",
            d => SyntheticPackages.BuildUpdate(d, "SBRE", "2608")), null);

        Assert.False(result.Success);
        Assert.True(result.RolledBack);
        Assert.False(result.RollbackFailed);
        // Restored: old data + old sct back, new sct gone.
        Assert.Equal(before, File.ReadAllText(Path.Combine(fir, "SBRE", "NavData", "airway.txt")));
        Assert.True(File.Exists(Path.Combine(fir, $"{SyntheticPackages.SectorBase("SBRE", "2607")}.sct")));
        Assert.False(File.Exists(Path.Combine(fir, $"{SyntheticPackages.SectorBase("SBRE", "2608")}.sct")));
    }

    [Fact]
    public async Task Update_without_existing_install_fails_cleanly()
    {
        using var ws = new TestWorkspace();
        var b = new EngineBuilder(ws);
        var result = await b.Build().RunAsync(Request(ws, b, PackageType.Update, "2608", "upd2608",
            d => SyntheticPackages.BuildUpdate(d, "SBRE", "2608")), null);

        Assert.False(result.Success);
        Assert.False(Directory.Exists(ws.FirDir("SBRE")));
    }
}
