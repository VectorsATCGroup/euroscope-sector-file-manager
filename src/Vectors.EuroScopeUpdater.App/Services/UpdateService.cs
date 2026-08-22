using System.Diagnostics;
using System.IO;
using System.Windows;
using Microsoft.Extensions.Logging;
using Vectors.EuroScopeUpdater.Core.Abstractions;
using Vectors.EuroScopeUpdater.Core.Settings;
using Vectors.EuroScopeUpdater.Core.Updates;

namespace Vectors.EuroScopeUpdater.App.Services;

/// <summary>
/// Application-level self-update orchestration: knows the running version, asks the release feed for
/// the newest one, remembers whether a newer release is available (for the banner), and performs the
/// "update now" flow: download the official installer to a temp folder, verify it, hand off to it
/// (silent, per-user, relaunch when done) and exit this process.
/// </summary>
public interface IUpdateService
{
    Version CurrentVersion { get; }

    /// <summary>A release newer than the running version found by the last check, else null.</summary>
    AppRelease? AvailableRelease { get; }
    event EventHandler? AvailableReleaseChanged;

    /// <summary>Whether the automatic startup check is enabled in settings.</summary>
    bool AutoCheckEnabled { get; }

    /// <summary>Check the feed now. Null means "could not check" (offline, rate limited…), never an error.</summary>
    Task<UpdateCheckResult?> CheckAsync(CancellationToken ct = default);

    /// <summary>Download + verify the installer, launch it and shut this app down. Throws on failure (nothing launched).</summary>
    Task DownloadAndInstallAsync(AppRelease release, IProgress<DownloadProgress>? progress, CancellationToken ct = default);
}

public sealed class UpdateService : IUpdateService
{
    private readonly IUpdateChecker _checker;
    private readonly IUpdateDownloader _downloader;
    private readonly ISettingsService _settings;
    private readonly ILogger<UpdateService> _log;

    public UpdateService(IUpdateChecker checker, IUpdateDownloader downloader, ISettingsService settings, ILogger<UpdateService> log)
    {
        _checker = checker;
        _downloader = downloader;
        _settings = settings;
        _log = log;
        CurrentVersion = AppVersions.Current(typeof(UpdateService).Assembly);
    }

    public Version CurrentVersion { get; }
    public AppRelease? AvailableRelease { get; private set; }
    public event EventHandler? AvailableReleaseChanged;
    public bool AutoCheckEnabled => _settings.Current.CheckForUpdates;

    public async Task<UpdateCheckResult?> CheckAsync(CancellationToken ct = default)
    {
        var latest = await _checker.GetLatestAsync(ct).ConfigureAwait(false);
        if (latest is null) return null;

        var result = new UpdateCheckResult(CurrentVersion, latest);
        var newer = result.IsUpdateAvailable ? latest : null;
        if (!Equals(newer?.Tag, AvailableRelease?.Tag))
        {
            AvailableRelease = newer;
            AvailableReleaseChanged?.Invoke(this, EventArgs.Empty);
        }
        _log.LogInformation("Update check: current {Current}, latest {Latest}, update available: {Available}",
            AppVersions.Format(CurrentVersion), latest.VersionText, result.IsUpdateAvailable);
        return result;
    }

    public async Task DownloadAndInstallAsync(AppRelease release, IProgress<DownloadProgress>? progress, CancellationToken ct = default)
    {
        if (!release.HasInstaller)
            throw new InvalidOperationException("Esta versão não tem um instalador publicado.");

        var dir = Path.Combine(Path.GetTempPath(), "VectorsATCGroup", "EuroScopeSectorFileManager", "updates");
        var installer = await _downloader.DownloadInstallerAsync(release, dir, progress, ct).ConfigureAwait(false);

        // Inno Setup switches: silent (progress window only), no message boxes, never reboot, close the
        // running app if it still holds files (the installer's RestartManager also sweeps up any stale
        // background instance left by an older version), keep the same per-user/per-machine scope as
        // the current install, and relaunch the app when done (handled by setup.iss, /RELAUNCH=1).
        var scope = IsPerUserInstall() ? "/CURRENTUSER" : "/ALLUSERS";
        var args = $"/SILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /FORCECLOSEAPPLICATIONS {scope} /RELAUNCH=1";

        _log.LogInformation("Launching installer for {Tag} ({Scope}); the app will now exit", release.Tag, scope);
        Process.Start(new ProcessStartInfo(installer, args) { UseShellExecute = true });

        await Application.Current.Dispatcher.InvokeAsync(() => Application.Current.Shutdown());
    }

    /// <summary>True when this exe runs from the per-user Programs folder (the installer's default).</summary>
    private static bool IsPerUserInstall()
    {
        try
        {
            var exe = Environment.ProcessPath ?? "";
            var perUserRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs");
            return exe.StartsWith(perUserRoot, StringComparison.OrdinalIgnoreCase)
                   || !exe.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), StringComparison.OrdinalIgnoreCase);
        }
        catch { return true; }
    }
}
