using System.IO;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Vectors.EuroScopeUpdater.App.Services;
using Vectors.EuroScopeUpdater.Core.Abstractions;
using Vectors.EuroScopeUpdater.Core.Install;
using Vectors.EuroScopeUpdater.Core.Locators;
using Vectors.EuroScopeUpdater.Core.Model;
using Vectors.EuroScopeUpdater.Core.Operations;
using Vectors.EuroScopeUpdater.Core.Scanning;
using Vectors.EuroScopeUpdater.Core.Settings;

namespace Vectors.EuroScopeUpdater.App.ViewModels;

/// <summary>Where the dashboard stands with respect to the AeroNav session.</summary>
public enum AuthState
{
    /// <summary>The package source needs no authentication (offline fixtures).</summary>
    NotRequired,
    /// <summary>Silently checking whether a saved session is still valid (no window shown).</summary>
    Checking,
    /// <summary>No valid session: tools are gated until the user signs in.</summary>
    Required,
    /// <summary>Signed in; the catalog can be loaded and tools are available.</summary>
    Authenticated,
}

/// <summary>Main screen: FIR list with status, contextual install/update actions and a progress overlay.</summary>
public sealed partial class DashboardViewModel : ObservableObject
{
    private readonly ISectorPackageSource _source;
    private readonly ILocalInstallationScanner _scanner;
    private readonly IInstallEngine _engine;
    private readonly IEuroScopeProcessDetector _euroScopeProcess;
    private readonly ISettingsService _settings;
    private readonly IDialogService _dialogs;
    private readonly INavigationService _nav;
    private readonly ILogger<DashboardViewModel> _log;

    private RemoteCatalog? _catalog;
    private readonly Division _division = Division.VatsimBrasil;
    private readonly IAuthenticatingSource? _auth;
    private CancellationTokenSource? _operationCts;
    private CancellationTokenSource? _restoreCts;
    private Task<bool>? _restoreTask;
    private bool _authInProgress;

    public ObservableCollection<FirItemViewModel> Firs { get; } = new();
    public OperationViewModel Operation { get; } = new();

    [ObservableProperty] private string _airacText = "—";
    [ObservableProperty] private bool _busy;
    [ObservableProperty] private AuthState _authState;
    [ObservableProperty] private string? _statusMessage;

    public string DivisionName => _division.Name;
    public string SourceName => _source.DisplayName;
    public string SectorFilesPath => _settings.Current.SectorFilesPath ?? "";
    public bool EuroScopeDetected => !string.IsNullOrWhiteSpace(_settings.Current.EuroScopePath);

    public DashboardViewModel(ISectorPackageSource source, ILocalInstallationScanner scanner,
        IInstallEngine engine, IEuroScopeProcessDetector euroScopeProcess, ISettingsService settings,
        IDialogService dialogs, INavigationService nav, ILogger<DashboardViewModel> log)
    {
        _source = source;
        _scanner = scanner;
        _engine = engine;
        _euroScopeProcess = euroScopeProcess;
        _settings = settings;
        _dialogs = dialogs;
        _nav = nav;
        _log = log;
        _auth = source as IAuthenticatingSource;
        _authState = _auth is null ? AuthState.NotRequired
            : _auth.IsAuthenticated ? AuthState.Authenticated : AuthState.Required;

        foreach (var fir in _division.Firs)
            Firs.Add(new FirItemViewModel(fir, (item, kind) => RunOperationAsync(item, kind)));

        _ = RefreshAsync();
    }

    private static Localization Loc => Localization.Instance;

    /// <summary>The source requires an authenticated session before any tool can be used.</summary>
    public bool RequiresAuth => _auth is not null;
    private bool Authenticated => _auth?.IsAuthenticated ?? true;

    /// <summary>The authentication banner is shown while checking a saved session and while sign-in is required.</summary>
    public bool NeedsAuth => AuthState is AuthState.Checking or AuthState.Required;

    /// <summary>True while the saved session is being verified silently.</summary>
    public bool AuthChecking => AuthState == AuthState.Checking;

    /// <summary>True while the app is gated behind authentication (blocks all tools).</summary>
    public bool ToolsEnabled => !NeedsAuth;

    /// <summary>The Authenticate button stays available during the silent check (it simply skips ahead to sign-in).</summary>
    public bool CanAuthenticate => !_authInProgress;

    partial void OnBusyChanged(bool value) => UpdateAllCommand.NotifyCanExecuteChanged();
    partial void OnAuthStateChanged(AuthState value)
    {
        OnPropertyChanged(nameof(NeedsAuth));
        OnPropertyChanged(nameof(AuthChecking));
        OnPropertyChanged(nameof(ToolsEnabled));
    }

    private string FirDir(string fir) => Path.Combine(SectorFilesPath, fir);

    [RelayCommand]
    private void OpenSettings() => _nav.GoToSettings();

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (Busy || _authInProgress || _restoreTask is not null) return;

        // Gate: without an authenticated session the tools stay blocked and we do NOT contact AeroNav
        // beyond a silent check of the saved session (no window shown). The login UI only appears when
        // the session is actually gone/expired, or when the user clicks Authenticate to skip the wait.
        if (RequiresAuth && !Authenticated)
        {
            AuthState = AuthState.Checking;
            StatusMessage = null;
            await RescanAsync(); // show what is installed right away, even before the check finishes

            var restored = false;
            _restoreCts = new CancellationTokenSource();
            try
            {
                _restoreTask = _auth!.TryRestoreSessionAsync(_restoreCts.Token);
                restored = await _restoreTask;
            }
            catch (OperationCanceledException)
            {
                return; // the user clicked Authenticate; that flow takes over from here
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "silent session check failed");
            }
            finally
            {
                _restoreCts.Dispose();
                _restoreCts = null;
                _restoreTask = null;
            }

            if (!restored)
            {
                AuthState = AuthState.Required;
                return;
            }
        }

        AuthState = RequiresAuth ? AuthState.Authenticated : AuthState.NotRequired;
        await LoadCatalogAsync();
    }

    /// <summary>Load the remote catalog (requires a valid session) and recompute every FIR's status.</summary>
    private async Task LoadCatalogAsync()
    {
        Busy = true;
        StatusMessage = Loc.T("Dash_LoadingPackages");
        try
        {
            _catalog = await Task.Run(() => _source.GetCatalogAsync(_division));
            AiracText = _catalog.Airac.Value > 0 ? $"AIRAC {_catalog.Airac}" : "—";
            StatusMessage = null;
        }
        catch (AeroNavAuthRequiredException)
        {
            _catalog = null;
            AiracText = "—";
            AuthState = AuthState.Required;
            StatusMessage = Loc.T("Dash_SessionExpired");
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "catalog load failed");
            _catalog = null;
            AiracText = "—";
            StatusMessage = Loc.T("Dash_SourceUnavailable");
        }

        await RescanAsync();
        Busy = false;
    }

    private async Task RescanAsync()
    {
        var catalog = _catalog;
        foreach (var item in Firs)
        {
            var state = await Task.Run(() => _scanner.Scan(item.Code, FirDir(item.Code), catalog));
            item.Status = state.Status;
            item.InstalledVersion = state.InstalledVersion;
            item.BestInstall = catalog?.Best(item.Code, PackageType.Install);
            item.BestUpdate = catalog?.Best(item.Code, PackageType.Update);
            item.AvailableVersion = state.AvailableVersion; // newest across Install/Update, incl. same-cycle re-issues
            item.Refreshed();
        }
        UpdateAllCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private async Task AuthenticateAsync()
    {
        if (_auth is null) { await RefreshAsync(); return; }
        if (_authInProgress) return;
        _authInProgress = true;
        OnPropertyChanged(nameof(CanAuthenticate));
        try
        {
            // Stop a silent session check that may still be running: we are going interactive now.
            _restoreCts?.Cancel();
            if (_restoreTask is { } pending)
            {
                try { await pending; } catch { /* cancelled or failed, either way we proceed */ }
            }

            // The silent check may have just succeeded in the meantime: nothing to sign in to.
            if (_auth.IsAuthenticated)
            {
                AuthState = AuthState.Authenticated;
                StatusMessage = null;
                if (!Busy && _catalog is null) await LoadCatalogAsync();
                return;
            }

            AuthState = AuthState.Required;
            StatusMessage = Loc.T("Dash_OpeningLogin");
            await _auth.AuthenticateAsync();
            AuthState = AuthState.Authenticated;
            StatusMessage = null;
            await LoadCatalogAsync();
        }
        catch (AeroNavAuthRequiredException)
        {
            AuthState = AuthState.Required;
            StatusMessage = Loc.T("Dash_AuthNotCompleted");
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "authentication failed");
            AuthState = AuthState.Required;
            StatusMessage = string.Format(Loc.T("Dash_AuthFailed"), ex.Message);
        }
        finally
        {
            _authInProgress = false;
            OnPropertyChanged(nameof(CanAuthenticate));
        }
    }

    private bool CanUpdateAll() => !Busy && Firs.Any(f => f.CanUpdate);

    [RelayCommand(CanExecute = nameof(CanUpdateAll))]
    private async Task UpdateAllAsync()
    {
        // Sequential — never run parallel destructive operations over the same tree.
        foreach (var item in Firs.Where(f => f.CanUpdate).ToList())
        {
            await RunOperationAsync(item, OperationKind.Update);
            if (AuthState == AuthState.Required) break; // session gone: stop instead of failing every FIR
        }
    }

    private async Task RunOperationAsync(FirItemViewModel item, OperationKind kind, bool isRetry = false)
    {
        if (Busy) return;

        // Refuse to modify files while EuroScope is running.
        if (_euroScopeProcess.IsRunning())
        {
            var again = _dialogs.Confirm("EuroScope em execução",
                "O EuroScope está em execução. Feche o EuroScope antes de instalar atualizações de Sector Files.\n\nVerificar novamente?");
            if (!again || _euroScopeProcess.IsRunning())
            {
                _dialogs.Info("EuroScope em execução", "Feche o EuroScope e tente novamente.");
                return;
            }
        }

        var package = kind == OperationKind.Update ? (item.BestUpdate ?? item.BestInstall) : item.BestInstall;
        if (package is null)
        {
            _dialogs.Error("Pacote indisponível", $"Nenhum pacote disponível para {item.Code}.");
            return;
        }
        // If an update was requested but only a full package exists, it becomes a clean install.
        if (kind == OperationKind.Update && item.BestUpdate is null) kind = OperationKind.CleanInstall;

        Busy = true;
        item.Busy = true;
        item.Refreshed();
        Operation.Reset(string.Format(Loc.T(kind == OperationKind.Update ? "Op_Updating" : "Op_Installing"), item.Code));
        Operation.IsVisible = true;

        var progress = new Progress<OperationProgress>(Operation.Update);
        var request = new InstallRequest(item.Fir, kind, package, FirDir(item.Code), SectorFilesPath);
        _operationCts = new CancellationTokenSource();
        var sessionExpired = false;

        try
        {
            var result = await Task.Run(() => _engine.RunAsync(request, progress, _operationCts.Token));
            Operation.IsVisible = false;
            if (result.Success)
                _dialogs.Info($"{item.Code} pronto",
                    $"{item.Code} {(kind == OperationKind.Update ? "atualizado" : "instalado")} com sucesso.\n\n{result.Message}");
            else if (result.Error is AeroNavAuthRequiredException)
                sessionExpired = true; // handled below, after the operation state is cleaned up
            else if (result.RolledBack)
                _dialogs.Error($"{item.Code} sem alterações", result.Message ?? "A operação falhou e foi revertida.");
            else if (result.RollbackFailed)
                _dialogs.Error($"{item.Code}, atenção necessária", result.Message ?? "A operação e a reversão falharam. Há um backup disponível na pasta de dados do aplicativo.");
            else
                _dialogs.Error($"{item.Code} falhou", result.Message ?? "A operação falhou.");
        }
        catch (OperationCanceledException)
        {
            Operation.IsVisible = false;
            _dialogs.Info($"{item.Code} cancelado", $"A operação foi cancelada. Nada foi alterado em {item.Code}.");
        }
        catch (Exception ex)
        {
            Operation.IsVisible = false;
            _log.LogError(ex, "operation crashed");
            _dialogs.Error($"{item.Code} falhou", ex.Message);
        }
        finally
        {
            _operationCts?.Dispose();
            _operationCts = null;
            item.Busy = false;
            Busy = false;
            await RescanAsync();
        }

        if (sessionExpired)
            await HandleSessionExpiredAsync(item, kind, isRetry);
    }

    /// <summary>
    /// The AeroNav session died underneath an operation (nothing was changed on disk). Gate the tools,
    /// offer to sign in right now and, once signed in, retry that same operation once.
    /// </summary>
    private async Task HandleSessionExpiredAsync(FirItemViewModel item, OperationKind kind, bool isRetry)
    {
        AuthState = AuthState.Required;
        StatusMessage = Loc.T("Dash_SessionExpired");
        _log.LogInformation("[{Fir}] operation needs re-authentication (retry={IsRetry})", item.Code, isRetry);

        if (isRetry || _auth is null)
        {
            _dialogs.Error(Loc.T("Dash_SessionExpiredTitle"), Loc.T("Dash_SessionExpired"));
            return;
        }

        var signIn = _dialogs.Confirm(Loc.T("Dash_SessionExpiredTitle"),
            string.Format(Loc.T("Dash_SessionExpiredRetry"), item.Code));
        if (!signIn) return;

        await AuthenticateAsync(); // reloads the catalog and every FIR status when it succeeds
        if (AuthState != AuthState.Authenticated) return;

        var again = Firs.FirstOrDefault(f => f.Code == item.Code);
        if (again is null) return;
        var stillApplies = kind == OperationKind.Update ? again.CanUpdate : again.CanInstall;
        if (stillApplies)
            await RunOperationAsync(again, kind, isRetry: true);
    }

    [RelayCommand]
    private void CancelOperation()
    {
        _operationCts?.Cancel();
        Operation.PhaseText = Loc.T("Op_Cancelling");
        Operation.CanCancel = false;
    }
}
