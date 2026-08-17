using Microsoft.Extensions.Logging.Abstractions;
using Vectors.EuroScopeUpdater.Core.Abstractions;
using Vectors.EuroScopeUpdater.Core.Backup;
using Vectors.EuroScopeUpdater.Core.Install;
using Vectors.EuroScopeUpdater.Core.Manifest;
using Vectors.EuroScopeUpdater.Core.Operations;
using Vectors.EuroScopeUpdater.Core.Settings;
using Vectors.EuroScopeUpdater.Core.Time;

namespace Vectors.EuroScopeUpdater.Tests.Support;

/// <summary>Wires a real <see cref="InstallEngine"/> against a test workspace and the folder fakes.</summary>
public sealed class EngineBuilder
{
    private readonly TestWorkspace _ws;
    public FolderPackageSource Source { get; } = new();
    public IArchiveExtractor Extractor { get; set; } = new FolderArchiveExtractor();
    public IManifestService Manifest { get; set; }
    public BackupManager Backup { get; }
    public OperationJournal Journal { get; }
    public SettingsService Settings { get; }
    public FixedClock Clock { get; } = new(new DateTime(2026, 8, 16, 12, 0, 0, DateTimeKind.Utc));

    public EngineBuilder(TestWorkspace ws)
    {
        _ws = ws;
        Manifest = new ManifestService(ws.AppPaths);
        Backup = new BackupManager(ws.AppPaths, NullLogger<BackupManager>.Instance);
        Journal = new OperationJournal(ws.AppPaths);
        Settings = new SettingsService(ws.AppPaths);
        Settings.Save(new ApplicationSettings
        {
            EuroScopePath = ws.EuroScopeDir,
            SectorFilesPath = ws.SectorFilesDir,
            SetupCompleted = true,
            BackupsToKeep = 3,
        });
    }

    public InstallEngine Build() => new(
        Source, Extractor, Backup, Manifest, Journal, Settings, Clock,
        NullLogger<InstallEngine>.Instance);
}
