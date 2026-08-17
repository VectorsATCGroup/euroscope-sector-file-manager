using Microsoft.Extensions.Logging.Abstractions;
using Vectors.EuroScopeUpdater.Core.Backup;
using Vectors.EuroScopeUpdater.Core.Install;
using Vectors.EuroScopeUpdater.Core.Manifest;
using Vectors.EuroScopeUpdater.Core.Model;
using Vectors.EuroScopeUpdater.Core.Packaging;
using Vectors.EuroScopeUpdater.Core.Scanning;
using Vectors.EuroScopeUpdater.Tests.Support;
using Xunit;

namespace Vectors.EuroScopeUpdater.Tests;

public class BackupManagerTests
{
    [Fact]
    public void Create_restore_roundtrips_content()
    {
        using var ws = new TestWorkspace();
        var fir = ws.FirDir("SBRE");
        Directory.CreateDirectory(fir);
        File.WriteAllText(Path.Combine(fir, "a.txt"), "original");

        var mgr = new BackupManager(ws.AppPaths, NullLogger<BackupManager>.Instance);
        var backup = mgr.CreateBackup("SBRE", fir, new DateTime(2026, 8, 16, 12, 0, 0, DateTimeKind.Utc));
        Assert.NotNull(backup);

        File.WriteAllText(Path.Combine(fir, "a.txt"), "CHANGED");
        mgr.Restore(backup!, fir);
        Assert.Equal("original", File.ReadAllText(Path.Combine(fir, "a.txt")));
    }

    [Fact]
    public void Prune_keeps_only_newest_n()
    {
        using var ws = new TestWorkspace();
        var fir = ws.FirDir("SBRE");
        Directory.CreateDirectory(fir);
        File.WriteAllText(Path.Combine(fir, "a.txt"), "x");
        var mgr = new BackupManager(ws.AppPaths, NullLogger<BackupManager>.Instance);

        var t = new DateTime(2026, 8, 16, 12, 0, 0, DateTimeKind.Utc);
        for (var i = 0; i < 5; i++) mgr.CreateBackup("SBRE", fir, t.AddSeconds(i));

        mgr.Prune("SBRE", keep: 2);
        Assert.Equal(2, mgr.List("SBRE").Count);
    }
}

public class ManifestServiceTests
{
    [Fact]
    public void Build_read_roundtrip()
    {
        using var ws = new TestWorkspace();
        var fir = ws.FirDir("SBRE");
        SyntheticPackages.BuildInstall(fir, "SBRE", "2607");
        var svc = new ManifestService(ws.AppPaths);

        PackageName.TryParsePackage(SyntheticPackages.PackageFileName("SBRE", PackageType.Install, "2607"), out var name);
        var sct = SectorFiles.CurrentSctFileName(fir)!;
        var manifest = svc.Build(fir, name, sct, DateTime.UtcNow);
        svc.Write(manifest);

        var read = svc.Read("SBRE")!;
        Assert.Equal("2607", read.Airac);
        Assert.NotEmpty(read.Files);
        Assert.Contains(read.Files, f => f.RelativePath.EndsWith("NavData/airway.txt"));
    }
}

public class LocalInstallationScannerTests
{
    private static RemoteCatalog CatalogWithUpdate(string fir, string cycle)
    {
        PackageName.TryParsePackage(SyntheticPackages.PackageFileName(fir, PackageType.Update, cycle), out var name);
        var pkg = new RemotePackage(fir, PackageType.Update, name.Airac, name, name.ToString() ?? "", "ref");
        return new RemoteCatalog("SBXX", name.Airac, new[] { pkg });
    }

    [Fact]
    public void Not_installed_when_directory_missing()
    {
        using var ws = new TestWorkspace();
        var scanner = new LocalInstallationScanner(new ManifestService(ws.AppPaths));
        var state = scanner.Scan("SBRE", ws.FirDir("SBRE"), null);
        Assert.Equal(InstallStatus.NotInstalled, state.Status);
    }

    [Fact]
    public void Incomplete_when_no_sct()
    {
        using var ws = new TestWorkspace();
        var fir = ws.FirDir("SBRE");
        Directory.CreateDirectory(fir);
        File.WriteAllText(Path.Combine(fir, "readme.txt"), "no sct here");
        var scanner = new LocalInstallationScanner(new ManifestService(ws.AppPaths));
        Assert.Equal(InstallStatus.InstallationIncomplete, scanner.Scan("SBRE", fir, null).Status);
    }

    [Fact]
    public void Update_available_when_remote_newer()
    {
        using var ws = new TestWorkspace();
        var fir = ws.FirDir("SBRE");
        SyntheticPackages.BuildInstall(fir, "SBRE", "2607");
        var scanner = new LocalInstallationScanner(new ManifestService(ws.AppPaths));
        var state = scanner.Scan("SBRE", fir, CatalogWithUpdate("SBRE", "2608"));
        Assert.Equal(InstallStatus.UpdateAvailable, state.Status);
        Assert.Equal(2607, state.InstalledAirac!.Value.Value);
    }

    [Fact]
    public void Up_to_date_when_versions_match_with_manifest()
    {
        using var ws = new TestWorkspace();
        var fir = ws.FirDir("SBRE");
        SyntheticPackages.BuildInstall(fir, "SBRE", "2608");
        var svc = new ManifestService(ws.AppPaths);
        PackageName.TryParsePackage(SyntheticPackages.PackageFileName("SBRE", PackageType.Install, "2608"), out var name);
        svc.Write(svc.Build(fir, name, SectorFiles.CurrentSctFileName(fir)!, DateTime.UtcNow));

        var state = new LocalInstallationScanner(svc).Scan("SBRE", fir, CatalogWithUpdate("SBRE", "2608"));
        Assert.Equal(InstallStatus.UpToDate, state.Status);
    }

    [Fact]
    public void Locally_modified_when_core_file_changed_and_up_to_date()
    {
        using var ws = new TestWorkspace();
        var fir = ws.FirDir("SBRE");
        SyntheticPackages.BuildInstall(fir, "SBRE", "2608");
        var svc = new ManifestService(ws.AppPaths);
        PackageName.TryParsePackage(SyntheticPackages.PackageFileName("SBRE", PackageType.Install, "2608"), out var name);
        svc.Write(svc.Build(fir, name, SectorFiles.CurrentSctFileName(fir)!, DateTime.UtcNow));

        // Tamper with a CORE (non-personalization) file.
        File.WriteAllText(Path.Combine(fir, "SBRE", "NavData", "airway.txt"), "HAND EDITED");

        var state = new LocalInstallationScanner(svc).Scan("SBRE", fir, CatalogWithUpdate("SBRE", "2608"));
        Assert.Equal(InstallStatus.LocallyModified, state.Status);
        Assert.Contains(state.ModifiedFiles, f => f.EndsWith("NavData/airway.txt"));
    }

    [Fact]
    public void Stale_manifest_does_not_override_the_actual_installed_files()
    {
        // Reproduces a real machine state: the manifest on record claimed a newer AIRAC (2608)
        // than the sector file actually on disk (2607), e.g. after an interrupted install or an
        // external change. The scanner must trust the .sct EuroScope loads, not the stale manifest.
        using var ws = new TestWorkspace();
        var fir = ws.FirDir("SBRE");
        SyntheticPackages.BuildInstall(fir, "SBRE", "2607"); // what is really on disk

        // Write a manifest that describes a DIFFERENT (2608) tree, not present in the FIR folder.
        var scratch = Path.Combine(ws.FirDir("SBRE"), "..", "scratch2608");
        SyntheticPackages.BuildInstall(scratch, "SBRE", "2608");
        var svc = new ManifestService(ws.AppPaths);
        PackageName.TryParsePackage(SyntheticPackages.PackageFileName("SBRE", PackageType.Install, "2608"), out var name2608);
        svc.Write(svc.Build(scratch, name2608, SectorFiles.CurrentSctFileName(scratch)!, DateTime.UtcNow));

        var state = new LocalInstallationScanner(svc).Scan("SBRE", fir, CatalogWithUpdate("SBRE", "2608"));

        Assert.Equal(2607, state.InstalledAirac!.Value.Value);      // disk truth, not the manifest's 2608
        Assert.Equal(InstallStatus.UpdateAvailable, state.Status);  // actionable, offers the 2608 update
        Assert.False(state.HasManifest);                            // the manifest does not describe this install
        Assert.Empty(state.ModifiedFiles);                          // no false "modified" from the wrong manifest
    }

    [Fact]
    public void Personalization_edit_does_not_flag_modified()
    {
        using var ws = new TestWorkspace();
        var fir = ws.FirDir("SBRE");
        SyntheticPackages.BuildInstall(fir, "SBRE", "2608");
        var svc = new ManifestService(ws.AppPaths);
        PackageName.TryParsePackage(SyntheticPackages.PackageFileName("SBRE", PackageType.Install, "2608"), out var name);
        svc.Write(svc.Build(fir, name, SectorFiles.CurrentSctFileName(fir)!, DateTime.UtcNow));

        // Edit a personalization file — expected, must NOT be reported as a modification.
        File.WriteAllText(Path.Combine(fir, "SBRE", "Settings", "RADAR", "symbology.txt"), "my colors");

        var state = new LocalInstallationScanner(svc).Scan("SBRE", fir, CatalogWithUpdate("SBRE", "2608"));
        Assert.Equal(InstallStatus.UpToDate, state.Status);
    }
}
