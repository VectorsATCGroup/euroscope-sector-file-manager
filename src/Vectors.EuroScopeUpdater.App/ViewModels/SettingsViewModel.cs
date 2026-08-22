using System.IO;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Vectors.EuroScopeUpdater.App.Infrastructure;
using Vectors.EuroScopeUpdater.App.Services;
using Vectors.EuroScopeUpdater.Core.Locators;
using Vectors.EuroScopeUpdater.Core.Paths;
using Vectors.EuroScopeUpdater.Core.Settings;
using Vectors.EuroScopeUpdater.Core.Updates;

namespace Vectors.EuroScopeUpdater.App.ViewModels;

/// <summary>Small settings area: paths, authentication/logout, updates, backups info and about.</summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settings;
    private readonly IEuroScopeLocator _euroScope;
    private readonly ISectorFilesLocator _sectorFiles;
    private readonly IAeroNavBrowser _browser;
    private readonly IDialogService _dialogs;
    private readonly INavigationService _nav;
    private readonly AppPaths _paths;
    private readonly IUpdateService _updates;
    private readonly ILogger<SettingsViewModel> _log;

    public SettingsViewModel(ISettingsService settings, IEuroScopeLocator euroScope,
        ISectorFilesLocator sectorFiles, IAeroNavBrowser browser, IDialogService dialogs,
        INavigationService nav, AppPaths paths, IUpdateService updates, ILogger<SettingsViewModel> log)
    {
        _settings = settings;
        _euroScope = euroScope;
        _sectorFiles = sectorFiles;
        _browser = browser;
        _dialogs = dialogs;
        _nav = nav;
        _paths = paths;
        _updates = updates;
        _log = log;

        EuroScopePath = settings.Current.EuroScopePath ?? "";
        SectorFilesPath = settings.Current.SectorFilesPath ?? "";
        BackupsToKeep = settings.Current.BackupsToKeep;
        _checkForUpdates = settings.Current.CheckForUpdates;
        if (_updates.AvailableRelease is { } r)
            UpdateStatusMessage = string.Format(Localization.Instance.T("Set_Updates_Available"), r.VersionText);
    }

    [ObservableProperty] private string _euroScopePath = "";
    [ObservableProperty] private string _sectorFilesPath = "";
    [ObservableProperty] private int _backupsToKeep;
    [ObservableProperty] private bool _checkForUpdates;
    [ObservableProperty] private bool _checkingUpdates;
    [ObservableProperty] private string? _updateStatusMessage;

    public string Version => AppVersions.Format(_updates.CurrentVersion);
    public string VersionLabel => string.Format(Localization.Instance.T("Set_Version"), Version);
    public string CurrentLanguageLabel => Localization.Instance.Language == AppLanguage.Pt ? "Português" : "English";
    public string AutoUpdateStateLabel => Localization.Instance.T(CheckForUpdates ? "Common_On" : "Common_Off");

    partial void OnCheckForUpdatesChanged(bool value)
    {
        var s = _settings.Current;
        if (s.CheckForUpdates != value)
        {
            s.CheckForUpdates = value;
            _settings.Save(s);
        }
        OnPropertyChanged(nameof(AutoUpdateStateLabel));
    }

    partial void OnCheckingUpdatesChanged(bool value) => CheckUpdatesNowCommand.NotifyCanExecuteChanged();

    [RelayCommand]
    private void SetLanguage(string code)
    {
        var lang = string.Equals(code, "En", StringComparison.OrdinalIgnoreCase) ? AppLanguage.En : AppLanguage.Pt;
        Localization.Instance.SetLanguage(lang);
        var s = _settings.Current;
        s.Language = lang.ToString();
        _settings.Save(s);
        OnPropertyChanged(nameof(CurrentLanguageLabel));
        OnPropertyChanged(nameof(VersionLabel));
        OnPropertyChanged(nameof(AutoUpdateStateLabel));
    }

    [RelayCommand]
    private void SetAutoUpdate(string state) => CheckForUpdates = string.Equals(state, "On", StringComparison.OrdinalIgnoreCase);

    private bool CanCheckUpdatesNow() => !CheckingUpdates;

    [RelayCommand(CanExecute = nameof(CanCheckUpdatesNow))]
    private async Task CheckUpdatesNowAsync()
    {
        CheckingUpdates = true;
        UpdateStatusMessage = Localization.Instance.T("Set_Updates_Checking");
        try
        {
            var result = await _updates.CheckAsync();
            if (result is null)
                UpdateStatusMessage = Localization.Instance.T("Set_Updates_Unavailable");
            else if (result.IsUpdateAvailable)
            {
                UpdateStatusMessage = string.Format(Localization.Instance.T("Set_Updates_Available"), result.Latest!.VersionText);
                _dialogs.ShowUpdate(new UpdateViewModel(result.Latest, _updates, _log));
            }
            else
                UpdateStatusMessage = string.Format(Localization.Instance.T("Set_Updates_UpToDate"), Version);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "manual update check failed");
            UpdateStatusMessage = Localization.Instance.T("Set_Updates_Unavailable");
        }
        finally { CheckingUpdates = false; }
    }

    [RelayCommand]
    private void ChangeEuroScope()
    {
        var picked = _dialogs.PickFolder("Localize a pasta do EuroScope", EuroScopePath);
        if (picked is null) return;
        EuroScopePath = picked;
        if (!_euroScope.LooksLikeEuroScope(picked))
            _dialogs.Info("EuroScope", "Essa pasta não parece uma instalação do EuroScope.");
    }

    [RelayCommand]
    private void ChangeLocation()
    {
        var picked = _dialogs.PickFolder("Escolha o local dos Sector Files", SectorFilesPath);
        if (picked is not null) SectorFilesPath = picked;
    }

    [RelayCommand]
    private void Save()
    {
        var s = _settings.Current;
        s.EuroScopePath = EuroScopePath;
        s.SectorFilesPath = _sectorFiles.EnsureExists(SectorFilesPath);
        s.BackupsToKeep = Math.Clamp(BackupsToKeep, 1, 50);
        _settings.Save(s);
        _dialogs.Info("Configurações", "Configurações salvas.");
    }

    [RelayCommand]
    private async Task LogoutAsync()
    {
        await _browser.LogoutAsync();
        _dialogs.Info("Sessão encerrada", "Sua sessão do AeroNav foi limpa. Autentique-se novamente para usar as ferramentas.");
        _nav.GoToDashboard(); // dashboard now shows the authentication gate
    }

    [RelayCommand] private void OpenAppData() => OpenFolder(_paths.Root);
    [RelayCommand] private void OpenLogs() => OpenFolder(_paths.LogsDir);
    [RelayCommand] private void OpenBackups() => OpenFolder(_paths.BackupsDir);

    [RelayCommand] private void Back() => _nav.GoToDashboard();

    private void OpenFolder(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
        }
        catch (Exception ex) { _dialogs.Error("Pasta", ex.Message); }
    }
}
