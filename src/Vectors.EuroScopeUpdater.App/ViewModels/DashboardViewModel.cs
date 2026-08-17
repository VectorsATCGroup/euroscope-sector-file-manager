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

    public ObservableCollection<FirItemViewModel> Firs { get; } = new();
    public OperationViewModel Operation { get; } = new();

    [ObservableProperty] private string _airacText = "—";
    [ObservableProperty] private bool _busy;
    [ObservableProperty] private bool _needsAuth;
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

        foreach (var fir in _division.Firs)
            Firs.Add(new FirItemViewModel(fir, RunOperationAsync));

        _ = RefreshAsync();
    }

    /// <summary>The source requires an authenticated session before any tool can be used.</summary>
    public bool RequiresAuth => _auth is not null;
    private bool Authenticated => _auth?.IsAuthenticated ?? true;

    /// <summary>True while the app is gated behind authentication (blocks all tools).</summary>
    public bool ToolsEnabled => !NeedsAuth;

    partial void OnBusyChanged(bool value) => UpdateAllCommand.NotifyCanExecuteChanged();
    partial void OnNeedsAuthChanged(bool value) => OnPropertyChanged(nameof(ToolsEnabled));

    private string FirDir(string fir) => Path.Combine(SectorFilesPath, fir);

    [RelayCommand]
    private void OpenSettings() => _nav.GoToSettings();

    [RelayCommand]
    private async Task RefreshAsync()
    {
        // Gate: without an authenticated session the tools stay blocked and we do NOT contact AeroNav.
        if (RequiresAuth && !Authenticated)
        {
            // First try to silently restore a persisted session (no window shown). The login UI only
            // appears if the session is actually gone/expired.
            Busy = true;
            StatusMessage = Localization.Instance.T("Auth_Checking");
            var restored = false;
            try { restored = await Task.Run(() => _auth!.TryRestoreSessionAsync()); }
            catch { /* treat as not authenticated */ }
            Busy = false;
            StatusMessage = null;

            if (!restored)
            {
                NeedsAuth = true;
                await RescanAsync(); // local-only status behind the gate
                return;
            }
            NeedsAuth = false;
        }

        Busy = true;
        NeedsAuth = false;
        StatusMessage = "Carregando pacotes disponíveis…";
        try
        {
            _catalog = await Task.Run(() => _source.GetCatalogAsync(_division));
            AiracText = _catalog.Airac.Value > 0 ? $"AIRAC {_catalog.Airac}" : "—";
            StatusMessage = null;
        }
        catch (AeroNavAuthRequiredException)
        {
            NeedsAuth = true;
            StatusMessage = "Sua sessão do AeroNav expirou. Autentique-se novamente para continuar.";
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "catalog load failed");
            StatusMessage = "Não foi possível acessar o AeroNav. Exibindo apenas o status local.";
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
            item.InstalledAirac = state.InstalledAirac;
            item.BestInstall = catalog?.Best(item.Code, PackageType.Install);
            item.BestUpdate = catalog?.Best(item.Code, PackageType.Update);
            item.AvailableAirac = item.BestUpdate?.Airac ?? item.BestInstall?.Airac;
            item.Refreshed();
        }
        UpdateAllCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private async Task AuthenticateAsync()
    {
        if (_auth is null) { await RefreshAsync(); return; }
        Busy = true;
        StatusMessage = "Abrindo o login oficial do AeroNav…";
        try
        {
            await _auth.AuthenticateAsync();
            NeedsAuth = false;
            Busy = false;
            await RefreshAsync();
        }
        catch (AeroNavAuthRequiredException)
        {
            NeedsAuth = true;
            StatusMessage = "A autenticação não foi concluída. Clique em Autenticar para tentar novamente.";
            Busy = false;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "authentication failed");
            StatusMessage = $"Falha na autenticação: {ex.Message}";
            Busy = false;
        }
    }

    private bool CanUpdateAll() => !Busy && Firs.Any(f => f.CanUpdate);

    [RelayCommand(CanExecute = nameof(CanUpdateAll))]
    private async Task UpdateAllAsync()
    {
        // Sequential — never run parallel destructive operations over the same tree.
        foreach (var item in Firs.Where(f => f.CanUpdate).ToList())
            await RunOperationAsync(item, OperationKind.Update);
    }

    private async Task RunOperationAsync(FirItemViewModel item, OperationKind kind)
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
        Operation.Reset(string.Format(Localization.Instance.T(kind == OperationKind.Update ? "Op_Updating" : "Op_Installing"), item.Code));
        Operation.IsVisible = true;

        var progress = new Progress<OperationProgress>(Operation.Update);
        var request = new InstallRequest(item.Fir, kind, package, FirDir(item.Code), SectorFilesPath);
        _operationCts = new CancellationTokenSource();

        try
        {
            var result = await Task.Run(() => _engine.RunAsync(request, progress, _operationCts.Token));
            Operation.IsVisible = false;
            if (result.Success)
                _dialogs.Info($"{item.Code} pronto",
                    $"{item.Code} {(kind == OperationKind.Update ? "atualizado" : "instalado")} com sucesso.\n\n{result.Message}");
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
    }

    [RelayCommand]
    private void CancelOperation()
    {
        _operationCts?.Cancel();
        Operation.PhaseText = Localization.Instance.T("Op_Cancelling");
        Operation.CanCancel = false;
    }
}
