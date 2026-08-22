using Vectors.EuroScopeUpdater.Core.Install;
using Vectors.EuroScopeUpdater.Core.Manifest;
using Vectors.EuroScopeUpdater.Core.Model;
using Vectors.EuroScopeUpdater.Core.Operations;
using Vectors.EuroScopeUpdater.Core.Packaging;
using Vectors.EuroScopeUpdater.Core.Scanning;
using Vectors.EuroScopeUpdater.Tests.Support;
using Xunit;

namespace Vectors.EuroScopeUpdater.Tests;

/// <summary>
/// AeroNav's operations team sometimes re-issues a package inside the same AIRAC cycle by bumping the
/// RR pair of the YYNNRR field (260801 → 260802, shown as "2608/2"). The app must treat that as a newer
/// version everywhere: parsing, catalog choice, installed-version detection, status and the update itself.
/// </summary>
public class SectorVersionTests
{
    private static SectorVersion V(int cycle, int rr, int pkg = 1) => new(new AiracCycle(cycle), rr, pkg);

    [Fact]
    public void Orders_by_cycle_then_cycle_revision_then_package_revision()
    {
        Assert.True(V(2608, 2) > V(2608, 1));
        Assert.True(V(2609, 1) > V(2608, 9));
        Assert.True(V(2608, 1, 2) > V(2608, 1, 1));
        Assert.True(V(2608, 2, 1) > V(2608, 1, 9));
        Assert.Equal(V(2608, 2), V(2608, 2));
        Assert.True(V(2608, 2).SameIssue(V(2608, 2, 5)));
        Assert.False(V(2608, 2).SameIssue(V(2608, 1)));
    }

    [Theory]
    [InlineData(1, "2608")]
    [InlineData(2, "2608/2")]
    [InlineData(12, "2608/12")]
    public void Label_shows_the_cycle_and_the_reissue_only_when_present(int rr, string expected) =>
        Assert.Equal(expected, V(2608, rr, 3).Label);

    [Fact]
    public void Package_names_and_urls_expose_the_full_version()
    {
        Assert.True(PackageName.TryParseFromUrl("https://files.aero-nav.com/SBAO/Update-Package_20260812101500-260802-1.7z", out var fromUrl));
        Assert.Equal("2608/2", fromUrl.Version.Label);
        Assert.Equal(2, fromUrl.Version.CycleRevision);

        Assert.True(PackageName.TryParsePackage("SBAO-Update-Package_20260812101500-260802-0001.7z", out var local));
        Assert.Equal(fromUrl.Version, local.Version);

        Assert.True(PackageName.TryParseSectorFile("SBAO-SBAO_20260812101500-260802-0001.sct", out var sct));
        Assert.Equal(fromUrl.Version, sct.Version);
    }

    [Fact]
    public void Catalog_prefers_the_same_cycle_reissue()
    {
        PackageName.TryParsePackage(SyntheticPackages.PackageFileName("SBRE", PackageType.Update, "2608"), out var v1);
        PackageName.TryParsePackage(SyntheticPackages.PackageFileName("SBRE", PackageType.Update, "2608", cycleRevision: 2), out var v2);
        var catalog = new RemoteCatalog("SBXX", v2.Airac, new[]
        {
            new RemotePackage("SBRE", PackageType.Update, v2.Airac, v2, "b", "ref2"),
            new RemotePackage("SBRE", PackageType.Update, v1.Airac, v1, "a", "ref1"),
        });
        Assert.Equal("ref2", catalog.Best("SBRE", PackageType.Update)!.DownloadRef);
        Assert.Equal("2608/2", catalog.BestVersion("SBRE")!.Value.Label);
    }

    [Fact]
    public void Installed_version_is_read_from_the_stamped_sector_file()
    {
        using var ws = new TestWorkspace();
        var fir = ws.FirDir("SBRE");
        SyntheticPackages.BuildInstall(fir, "SBRE", "2608", cycleRevision: 2);
        var v = SectorFiles.InferInstalledVersion(fir);
        Assert.NotNull(v);
        Assert.Equal("2608/2", v!.Value.Label);
        Assert.Equal(2608, SectorFiles.InferInstalledAirac(fir)!.Value.Value);
    }
}

public class SameCycleRevisionScannerTests
{
    private static RemoteCatalog Catalog(string fir, string cycle, int cycleRevision, int packageRevision = 1, PackageType type = PackageType.Update)
    {
        PackageName.TryParsePackage(SyntheticPackages.PackageFileName(fir, type, cycle, cycleRevision, packageRevision), out var name);
        var pkg = new RemotePackage(fir, type, name.Airac, name, name.ToString() ?? "", "ref");
        return new RemoteCatalog("SBXX", name.Airac, new[] { pkg });
    }

    [Fact]
    public void Reissue_of_the_same_cycle_is_an_available_update()
    {
        using var ws = new TestWorkspace();
        var fir = ws.FirDir("SBRE");
        SyntheticPackages.BuildInstall(fir, "SBRE", "2608");                       // 260801 on disk
        var state = new LocalInstallationScanner(new ManifestService(ws.AppPaths))
            .Scan("SBRE", fir, Catalog("SBRE", "2608", cycleRevision: 2));          // 260802 published

        Assert.Equal(InstallStatus.UpdateAvailable, state.Status);
        Assert.Equal("2608", state.InstalledVersion!.Value.Label);
        Assert.Equal("2608/2", state.AvailableVersion!.Value.Label);
        Assert.Equal(2608, state.InstalledAirac!.Value.Value);
    }

    [Fact]
    public void Same_reissue_installed_is_up_to_date_and_an_older_reissue_is_not_offered()
    {
        using var ws = new TestWorkspace();
        var fir = ws.FirDir("SBRE");
        SyntheticPackages.BuildInstall(fir, "SBRE", "2608", cycleRevision: 2);     // 260802 on disk
        var scanner = new LocalInstallationScanner(new ManifestService(ws.AppPaths));

        Assert.Equal(InstallStatus.UpToDate, scanner.Scan("SBRE", fir, Catalog("SBRE", "2608", cycleRevision: 2)).Status);
        Assert.Equal(InstallStatus.UpToDate, scanner.Scan("SBRE", fir, Catalog("SBRE", "2608", cycleRevision: 1)).Status);
        Assert.Equal(InstallStatus.UpdateAvailable, scanner.Scan("SBRE", fir, Catalog("SBRE", "2609", cycleRevision: 1)).Status);
    }

    [Fact]
    public void Newer_package_revision_is_offered_once_and_not_again_after_it_is_installed()
    {
        using var ws = new TestWorkspace();
        var fir = ws.FirDir("SBRE");
        SyntheticPackages.BuildInstall(fir, "SBRE", "2608");                       // .sct says 260801-0001
        var svc = new ManifestService(ws.AppPaths);
        var scanner = new LocalInstallationScanner(svc);

        // AeroNav re-packs the same issue as package revision 2 (same stamped .sct inside).
        var repack = Catalog("SBRE", "2608", cycleRevision: 1, packageRevision: 2);
        Assert.Equal(InstallStatus.UpdateAvailable, scanner.Scan("SBRE", fir, repack).Status);

        // After installing it, the manifest records package revision 2 for this same issue, so the
        // re-pack is not offered over and over even though the .sct name still says -0001.
        PackageName.TryParsePackage(SyntheticPackages.PackageFileName("SBRE", PackageType.Install, "2608", 1, 2), out var installed);
        svc.Write(svc.Build(fir, installed, SectorFiles.CurrentSctFileName(fir)!, DateTime.UtcNow));
        var state = scanner.Scan("SBRE", fir, repack);
        Assert.Equal(InstallStatus.UpToDate, state.Status);
        Assert.True(state.HasManifest);
        Assert.Equal(2, state.InstalledVersion!.Value.PackageRevision);
    }

    [Fact]
    public void Manifest_from_a_different_reissue_does_not_describe_the_install()
    {
        using var ws = new TestWorkspace();
        var fir = ws.FirDir("SBRE");
        SyntheticPackages.BuildInstall(fir, "SBRE", "2608", cycleRevision: 2);     // 260802 on disk
        var svc = new ManifestService(ws.AppPaths);
        var scratch = Path.Combine(ws.Root, "scratch-260801");
        SyntheticPackages.BuildInstall(scratch, "SBRE", "2608");                   // manifest built from 260801
        PackageName.TryParsePackage(SyntheticPackages.PackageFileName("SBRE", PackageType.Install, "2608"), out var old);
        svc.Write(svc.Build(scratch, old, SectorFiles.CurrentSctFileName(scratch)!, DateTime.UtcNow));

        var state = new LocalInstallationScanner(svc).Scan("SBRE", fir, Catalog("SBRE", "2608", cycleRevision: 2));

        Assert.Equal("2608/2", state.InstalledVersion!.Value.Label); // disk truth
        Assert.False(state.HasManifest);                              // stale manifest ignored
        Assert.Equal(InstallStatus.UpToDate, state.Status);
        Assert.Empty(state.ModifiedFiles);
    }
}

public class SameCycleRevisionEngineTests
{
    private static readonly Fir Sbre = new("SBRE", "Recife FIR");

    private static InstallRequest Request(TestWorkspace ws, EngineBuilder b, PackageType type, string cycle, int rr, Action<string> build)
    {
        var dir = Path.Combine(ws.Root, "content", $"{type}-{cycle}-{rr}");
        Directory.CreateDirectory(dir);
        build(dir);
        var pkg = b.Source.Add("SBRE", type, cycle, dir, cycleRevision: rr);
        return new InstallRequest(Sbre, type == PackageType.Install ? OperationKind.CleanInstall : OperationKind.Update,
            pkg, ws.FirDir("SBRE"), ws.SectorFilesDir);
    }

    [Fact]
    public async Task Updating_to_a_same_cycle_reissue_replaces_sector_files_and_repoints_profiles()
    {
        using var ws = new TestWorkspace();
        var b = new EngineBuilder(ws);
        var fir = ws.FirDir("SBRE");

        Assert.True((await b.Build().RunAsync(Request(ws, b, PackageType.Install, "2608", 1,
            d => SyntheticPackages.BuildInstall(d, "SBRE", "2608")), null)).Success);
        File.WriteAllText(Path.Combine(fir, "SBRE", "Settings", "RADAR", "symbology.txt"), "MY CUSTOM COLORS");

        var result = await b.Build().RunAsync(Request(ws, b, PackageType.Update, "2608", 2,
            d => SyntheticPackages.BuildUpdate(d, "SBRE", "2608", cycleRevision: 2)), null);

        Assert.True(result.Success, result.Message);
        Assert.True(File.Exists(Path.Combine(fir, $"{SyntheticPackages.SectorBase("SBRE", "2608", 2)}.sct")));
        Assert.False(File.Exists(Path.Combine(fir, $"{SyntheticPackages.SectorBase("SBRE", "2608", 1)}.sct")));
        Assert.False(File.Exists(Path.Combine(fir, $"{SyntheticPackages.SectorBase("SBRE", "2608", 1)}.ese")));
        Assert.Equal("airway 2608/2", File.ReadAllText(Path.Combine(fir, "SBRE", "NavData", "airway.txt")));
        Assert.Equal("MY CUSTOM COLORS", File.ReadAllText(Path.Combine(fir, "SBRE", "Settings", "RADAR", "symbology.txt")));
        Assert.EndsWith("260802-0001.sct", ProfileRepointer.ReadSectorReference(Path.Combine(fir, "sbre_radar.prf")));

        var manifest = new ManifestService(ws.AppPaths).Read("SBRE")!;
        Assert.Equal("2608", manifest.Airac);
        Assert.Equal(2, manifest.CycleRevision);

        // And the scanner now agrees the FIR is current for 2608/2.
        PackageName.TryParsePackage(SyntheticPackages.PackageFileName("SBRE", PackageType.Update, "2608", 2), out var name);
        var catalog = new RemoteCatalog("SBXX", name.Airac, new[] { new RemotePackage("SBRE", PackageType.Update, name.Airac, name, "x", "ref") });
        var state = new LocalInstallationScanner(new ManifestService(ws.AppPaths)).Scan("SBRE", fir, catalog);
        Assert.Equal(InstallStatus.UpToDate, state.Status);
        Assert.True(state.HasManifest);
        Assert.Equal("2608/2", state.InstalledVersion!.Value.Label);
    }
}
