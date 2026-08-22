using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Vectors.EuroScopeUpdater.App.Infrastructure;
using Vectors.EuroScopeUpdater.App.Services;
using Vectors.EuroScopeUpdater.App.ViewModels;
using Vectors.EuroScopeUpdater.App.Views;
using Vectors.EuroScopeUpdater.Core.Abstractions;
using Vectors.EuroScopeUpdater.Core.Backup;
using Vectors.EuroScopeUpdater.Core.Install;
using Vectors.EuroScopeUpdater.Core.Locators;
using Vectors.EuroScopeUpdater.Core.Manifest;
using Vectors.EuroScopeUpdater.Core.Operations;
using Vectors.EuroScopeUpdater.Core.Paths;
using Vectors.EuroScopeUpdater.Core.Scanning;
using Vectors.EuroScopeUpdater.Core.Settings;
using Vectors.EuroScopeUpdater.Core.Time;
using Vectors.EuroScopeUpdater.Core.Updates;
using Vectors.EuroScopeUpdater.Infrastructure.Archives;
using Vectors.EuroScopeUpdater.Infrastructure.Logging;
using Vectors.EuroScopeUpdater.Infrastructure.Sources;
using Vectors.EuroScopeUpdater.Infrastructure.Updates;

namespace Vectors.EuroScopeUpdater.App;

public partial class App : Application
{
    private const string SingleInstanceMutexName = @"Local\VectorsATCGroup.EuroScopeSectorFileManager.SingleInstance";

    public static IServiceProvider Services { get; private set; } = null!;

    private Mutex? _singleInstance;
    private ILogger<App>? _log;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Global safety net — never fail silently again. Any unhandled error is written to a crash
        // log and shown, instead of the process disappearing without a trace.
        DispatcherUnhandledException += (_, args) => { LogCrash("Dispatcher", args.Exception); args.Handled = true; };
        AppDomain.CurrentDomain.UnhandledException += (_, args) => LogCrash("AppDomain", args.ExceptionObject as Exception);
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, args) => { LogCrash("Task", args.Exception); args.SetObserved(); };

        // Only one instance at a time: two instances would fight over the same settings and the same
        // WebView2 profile. If one is already running, bring its window to the front and leave.
        _singleInstance = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out var isFirst);
        if (!isFirst)
        {
            ActivateExistingInstance();
            _singleInstance.Dispose();
            _singleInstance = null;
            Shutdown(0);
            return;
        }

        try
        {
            Bootstrap();
        }
        catch (Exception ex)
        {
            LogCrash("Startup", ex);
            Shutdown(1);
        }
    }

    private void Bootstrap()
    {
        var paths = new AppPaths();
        paths.EnsureCreated();

        var services = new ServiceCollection();

        // Logging — local file only, no remote telemetry.
        services.AddLogging(b =>
        {
            b.SetMinimumLevel(LogLevel.Information);
            b.AddProvider(new FileLoggerProvider(paths.LogsDir));
        });

        // Core singletons.
        services.AddSingleton(paths);
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<IManifestService, ManifestService>();
        services.AddSingleton<IOperationJournal, OperationJournal>();
        services.AddSingleton<IBackupManager, BackupManager>();
        services.AddSingleton<ILocalInstallationScanner, LocalInstallationScanner>();
        services.AddSingleton<IEuroScopeLocator, EuroScopeLocator>();
        services.AddSingleton<ISectorFilesLocator, SectorFilesLocator>();
        services.AddSingleton<IEuroScopeProcessDetector, EuroScopeProcessDetector>();
        services.AddSingleton<IArchiveExtractor, SevenZipArchiveExtractor>();
        services.AddSingleton<IAeroNavBrowser, WebView2AeroNavBrowser>();

        // Package source: AeroNav web (default), or the offline fixtures source when
        // VECTORS_FIXTURES points at a folder of synthetic packages (dev/demo).
        services.AddSingleton<ISectorPackageSource>(sp =>
        {
            var fixtures = Environment.GetEnvironmentVariable("VECTORS_FIXTURES");
            if (!string.IsNullOrWhiteSpace(fixtures) && Directory.Exists(fixtures))
                return new FixturePackageSource(fixtures!,
                    sp.GetRequiredService<ILoggerFactory>().CreateLogger<FixturePackageSource>());
            return ActivatorUtilities.CreateInstance<AeroNavWebPackageSource>(sp);
        });

        services.AddSingleton<IInstallEngine, InstallEngine>();
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<IThemeService, ThemeService>();

        // Self-update: the public GitHub Releases feed of this project (no personal data is sent).
        services.AddSingleton(_ => new HttpClient(new SocketsHttpHandler
        {
            AllowAutoRedirect = true,
            AutomaticDecompression = DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        }));
        services.AddSingleton(sp => new GitHubReleaseChecker(
            sp.GetRequiredService<HttpClient>(),
            sp.GetRequiredService<ILogger<GitHubReleaseChecker>>(),
            AppVersions.Current(typeof(App).Assembly)));
        services.AddSingleton<IUpdateChecker>(sp => sp.GetRequiredService<GitHubReleaseChecker>());
        services.AddSingleton<IUpdateDownloader>(sp => sp.GetRequiredService<GitHubReleaseChecker>());
        services.AddSingleton<IUpdateService, UpdateService>();

        // View-models.
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<INavigationService>(sp => sp.GetRequiredService<MainViewModel>());
        services.AddTransient<SetupWizardViewModel>();
        services.AddTransient<DashboardViewModel>();
        services.AddTransient<SettingsViewModel>();

        services.AddSingleton<MainWindow>();

        Services = services.BuildServiceProvider();
        _log = Services.GetRequiredService<ILogger<App>>();
        _log.LogInformation("Starting EuroScope Sector File Manager {Version}", AppVersions.Format(AppVersions.Current(typeof(App).Assembly)));

        // Heal machines where an older version left invisible background instances behind (the old
        // shutdown mode kept the process alive after its window was closed). Those hold the WebView2
        // profile open and make the saved session unreliable, so retire them before we touch it.
        TerminateStaleInstances();

        var settings = Services.GetRequiredService<ISettingsService>().Load();
        global::Vectors.EuroScopeUpdater.App.Services.Localization.Instance.Language =
            string.Equals(settings.Language, "En", StringComparison.OrdinalIgnoreCase) ? AppLanguage.En : AppLanguage.Pt;
        Services.GetRequiredService<IThemeService>().InitializeFromSettings();
        var window = Services.GetRequiredService<MainWindow>();
        var mainVm = Services.GetRequiredService<MainViewModel>();
        window.DataContext = mainVm;
        MainWindow = window;
        window.Show();

        // Navigate only AFTER the singleton is fully built and the window is shown.
        mainVm.Initialize();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            // Close the hidden WebView2 host window and release the browser so no msedgewebview2
            // processes outlive us (the profile itself is kept: it carries the saved AeroNav session).
            if (Services is not null && Services.GetService<IAeroNavBrowser>() is { } browser)
            {
                var dispose = browser.DisposeAsync().AsTask();
                dispose.Wait(TimeSpan.FromSeconds(5));
            }
            _log?.LogInformation("Exiting (code {Code})", e.ApplicationExitCode);
            (Services as IDisposable)?.Dispose();
        }
        catch { /* shutting down */ }
        finally
        {
            try { _singleInstance?.ReleaseMutex(); } catch { /* not owned */ }
            _singleInstance?.Dispose();
        }
        base.OnExit(e);
    }

    // ── Single instance / stale instance handling ─────────────────────────────────────────
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hWnd);
    private const int SW_RESTORE = 9;

    private static IEnumerable<Process> OtherInstances()
    {
        var me = Process.GetCurrentProcess();
        string? myPath = null;
        try { myPath = Environment.ProcessPath; } catch { /* ignore */ }
        foreach (var p in Process.GetProcessesByName(me.ProcessName))
        {
            if (p.Id == me.Id) continue;
            string? path = null;
            try { path = p.MainModule?.FileName; } catch { /* access denied: different user/elevation */ }
            if (myPath is not null && path is not null && !string.Equals(myPath, path, StringComparison.OrdinalIgnoreCase))
                continue; // a different install (e.g. a dev build) is not "us"
            yield return p;
        }
    }

    private static void ActivateExistingInstance()
    {
        try
        {
            foreach (var p in OtherInstances())
            {
                var h = p.MainWindowHandle;
                if (h == IntPtr.Zero) continue;
                if (IsIconic(h)) ShowWindow(h, SW_RESTORE);
                SetForegroundWindow(h);
                return;
            }
        }
        catch { /* best effort */ }
    }

    private void TerminateStaleInstances()
    {
        try
        {
            foreach (var p in OtherInstances())
            {
                bool stale;
                try
                {
                    // No window at all and running for a while: an invisible leftover, not a starting app.
                    stale = p.MainWindowHandle == IntPtr.Zero && DateTime.Now - p.StartTime > TimeSpan.FromSeconds(60);
                }
                catch { stale = false; }
                if (!stale) continue;
                _log?.LogWarning("Terminating stale background instance PID {Pid} (started {Start:u})", p.Id, p.StartTime.ToUniversalTime());
                try { p.Kill(entireProcessTree: true); } catch (Exception ex) { _log?.LogWarning("Could not terminate PID {Pid}: {Message}", p.Id, ex.Message); }
            }
        }
        catch (Exception ex)
        {
            _log?.LogWarning("Stale-instance sweep failed: {Message}", ex.Message);
        }
    }

    private static void LogCrash(string source, Exception? ex)
    {
        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "VectorsATCGroup", "EuroScopeSectorFileManager", "logs");
            Directory.CreateDirectory(dir);
            var line = $"{DateTime.UtcNow:O} [{source}] {ex}{Environment.NewLine}";
            File.AppendAllText(Path.Combine(dir, "startup-crash.log"), line);
            MessageBox.Show(
                $"Vectors EuroScope Sector File Manager hit an error and cannot continue.\n\n{ex?.Message}\n\n" +
                $"Details were written to:\n{Path.Combine(dir, "startup-crash.log")}",
                "Vectors EuroScope Sector File Manager", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch { /* logging must never throw */ }
    }
}
