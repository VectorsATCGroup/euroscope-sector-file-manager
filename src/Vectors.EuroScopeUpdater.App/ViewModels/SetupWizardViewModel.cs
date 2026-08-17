using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vectors.EuroScopeUpdater.App.Services;
using Vectors.EuroScopeUpdater.Core.Abstractions;
using Vectors.EuroScopeUpdater.Core.Locators;
using Vectors.EuroScopeUpdater.Core.Model;
using Vectors.EuroScopeUpdater.Core.Settings;

namespace Vectors.EuroScopeUpdater.App.ViewModels;

public enum WizardStep { Welcome, EuroScope, Location, Authentication, Ready }

/// <summary>
/// First-run wizard: Welcome → EuroScope detection → Sector-files location → Authentication → Ready.
/// Persists only technical settings (paths, division, setupCompleted). No credentials are handled here.
/// </summary>
public sealed partial class SetupWizardViewModel : ObservableObject
{
    private readonly IEuroScopeLocator _euroScope;
    private readonly ISectorFilesLocator _sectorFiles;
    private readonly ISettingsService _settings;
    private readonly ISectorPackageSource _source;
    private readonly IDialogService _dialogs;
    private readonly INavigationService _nav;

    public SetupWizardViewModel(IEuroScopeLocator euroScope, ISectorFilesLocator sectorFiles,
        ISettingsService settings, ISectorPackageSource source, IDialogService dialogs, INavigationService nav)
    {
        _euroScope = euroScope;
        _sectorFiles = sectorFiles;
        _settings = settings;
        _source = source;
        _dialogs = dialogs;
        _nav = nav;
        Detect();
    }

    public string DivisionName => Division.VatsimBrasil.Name;
    public string SourceName => _source.DisplayName;

    [ObservableProperty] private WizardStep _step = WizardStep.Welcome;

    [ObservableProperty] private string? _euroScopePath;
    [ObservableProperty] private bool _euroScopeValid;
    [ObservableProperty] private bool _useRecommendedLocation = true;
    [ObservableProperty] private string? _customLocation;
    [ObservableProperty] private bool _authenticated;
    [ObservableProperty] private bool _busy;
    [ObservableProperty] private string? _statusMessage;

    public int StepIndex => (int)Step;
    partial void OnStepChanged(WizardStep value)
    {
        OnPropertyChanged(nameof(StepIndex));
        NextCommand.NotifyCanExecuteChanged();
        BackCommand.NotifyCanExecuteChanged();
    }

    public string ResolvedSectorFilesPath =>
        UseRecommendedLocation && !string.IsNullOrWhiteSpace(EuroScopePath)
            ? _sectorFiles.RecommendedPath(EuroScopePath!)
            : CustomLocation ?? "";

    partial void OnEuroScopePathChanged(string? value)
    {
        EuroScopeValid = _euroScope.LooksLikeEuroScope(value);
        OnPropertyChanged(nameof(ResolvedSectorFilesPath));
        NextCommand.NotifyCanExecuteChanged();
    }
    partial void OnUseRecommendedLocationChanged(bool value) => OnPropertyChanged(nameof(ResolvedSectorFilesPath));
    partial void OnCustomLocationChanged(string? value)
    {
        OnPropertyChanged(nameof(ResolvedSectorFilesPath));
        NextCommand.NotifyCanExecuteChanged();
    }
    partial void OnAuthenticatedChanged(bool value) => NextCommand.NotifyCanExecuteChanged();

    [RelayCommand]
    private void Detect()
    {
        EuroScopePath = _euroScope.Detect();
        StatusMessage = EuroScopeValid ? "EuroScope detectado." : "O EuroScope não foi detectado automaticamente.";
    }

    [RelayCommand]
    private void LocateEuroScope()
    {
        var picked = _dialogs.PickFolder("Localize a pasta do EuroScope", EuroScopePath);
        if (picked is null) return;
        EuroScopePath = picked;
        if (!EuroScopeValid)
            _dialogs.Info("EuroScope", "Essa pasta não parece uma instalação do EuroScope. Você ainda pode continuar se tiver certeza.");
    }

    [RelayCommand]
    private void PickCustomLocation()
    {
        var picked = _dialogs.PickFolder("Escolha onde manter os Sector Files", EuroScopePath);
        if (picked is not null) CustomLocation = picked;
    }

    [RelayCommand]
    private async Task AuthenticateAsync()
    {
        Busy = true;
        StatusMessage = "Abrindo o login oficial do AeroNav…";
        try
        {
            if (_source is IAuthenticatingSource auth)
            {
                await auth.AuthenticateAsync();
                Authenticated = auth.IsAuthenticated;
            }
            else
            {
                Authenticated = true; // offline source needs no authentication
            }
            StatusMessage = Authenticated ? "Autenticado no serviço oficial." : "A autenticação não foi concluída.";
        }
        catch (AeroNavAuthRequiredException)
        {
            StatusMessage = "A autenticação não foi concluída. Você pode tentar novamente ou concluir e autenticar depois.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Não foi possível acessar a fonte de pacotes: {ex.Message}. Você pode concluir e tentar depois.";
        }
        finally { Busy = false; }
    }

    private bool CanNext() => Step switch
    {
        WizardStep.EuroScope => !string.IsNullOrWhiteSpace(EuroScopePath),
        WizardStep.Location => !string.IsNullOrWhiteSpace(ResolvedSectorFilesPath),
        _ => true,
    };

    [RelayCommand(CanExecute = nameof(CanNext))]
    private void Next()
    {
        if (Step == WizardStep.Ready) { Finish(); return; }
        Step = (WizardStep)((int)Step + 1);
    }

    private bool CanBack() => Step != WizardStep.Welcome;

    [RelayCommand(CanExecute = nameof(CanBack))]
    private void Back()
    {
        if (Step != WizardStep.Welcome) Step = (WizardStep)((int)Step - 1);
    }

    private void Finish()
    {
        var s = _settings.Current;
        s.EuroScopePath = EuroScopePath;
        s.SectorFilesPath = _sectorFiles.EnsureExists(ResolvedSectorFilesPath);
        s.Division = Division.VatsimBrasil.Id;
        s.SetupCompleted = true;
        _settings.Save(s);
        _nav.GoToDashboard();
    }
}
