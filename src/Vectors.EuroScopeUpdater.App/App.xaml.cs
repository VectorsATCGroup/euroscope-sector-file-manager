using System.IO;
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
using Vectors.EuroScopeUpdater.Infrastructure.Archives;
using Vectors.EuroScopeUpdater.Infrastructure.Logging;
using Vectors.EuroScopeUpdater.Infrastructure.Sources;

namespace Vectors.EuroScopeUpdater.App;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Global safety net — never fail silently again. Any unhandled error is written to a crash
        // log and shown, instead of the process disappearing without a trace.
        DispatcherUnhandledException += (_, args) => { LogCrash("Dispatcher", args.Exception); args.Handled = true; };
        AppDomain.CurrentDomain.UnhandledException += (_, args) => LogCrash("AppDomain", args.ExceptionObject as Exception);
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, args) => { LogCrash("Task", args.Exception); args.SetObserved(); };

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

        // View-models.
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<INavigationService>(sp => sp.GetRequiredService<MainViewModel>());
        services.AddTransient<SetupWizardViewModel>();
        services.AddTransient<DashboardViewModel>();
        services.AddTransient<SettingsViewModel>();

        services.AddSingleton<MainWindow>();

        Services = services.BuildServiceProvider();

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
