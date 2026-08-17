using System.IO;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vectors.EuroScopeUpdater.App.Infrastructure;
using Vectors.EuroScopeUpdater.App.Services;
using Vectors.EuroScopeUpdater.Core.Locators;
using Vectors.EuroScopeUpdater.Core.Paths;
using Vectors.EuroScopeUpdater.Core.Settings;

namespace Vectors.EuroScopeUpdater.App.ViewModels;

/// <summary>Small settings area: paths, authentication/logout, backups info and about.</summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settings;
    private readonly IEuroScopeLocator _euroScope;
    private readonly ISectorFilesLocator _sectorFiles;
    private readonly IAeroNavBrowser _browser;
    private readonly IDialogService _dialogs;
    private readonly INavigationService _nav;
    private readonly AppPaths _paths;

    public SettingsViewModel(ISettingsService settings, IEuroScopeLocator euroScope,
        ISectorFilesLocator sectorFiles, IAeroNavBrowser browser, IDialogService dialogs,
        INavigationService nav, AppPaths paths)
    {
        _settings = settings;
        _euroScope = euroScope;
        _sectorFiles = sectorFiles;
        _browser = browser;
        _dialogs = dialogs;
        _nav = nav;
        _paths = paths;

        EuroScopePath = settings.Current.EuroScopePath ?? "";
        SectorFilesPath = settings.Current.SectorFilesPath ?? "";
        BackupsToKeep = settings.Current.BackupsToKeep;
    }

    [ObservableProperty] private string _euroScopePath = "";
    [ObservableProperty] private string _sectorFilesPath = "";
    [ObservableProperty] private int _backupsToKeep;

    public string Version => typeof(SettingsViewModel).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";
    public string VersionLabel => string.Format(Localization.Instance.T("Set_Version"), Version);
    public string CurrentLanguageLabel => Localization.Instance.Language == AppLanguage.Pt ? "Português" : "English";

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
