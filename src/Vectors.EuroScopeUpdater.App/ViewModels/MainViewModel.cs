using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Vectors.EuroScopeUpdater.App.Services;
using Vectors.EuroScopeUpdater.Core.Settings;
using Vectors.EuroScopeUpdater.Core.Updates;

namespace Vectors.EuroScopeUpdater.App.ViewModels;

/// <summary>
/// Shell view-model. Chooses the first screen (wizard vs. dashboard) from saved settings, hosts the
/// current content, and owns the "new version available" banner/prompt. Implements
/// <see cref="INavigationService"/> so child view-models can navigate.
/// </summary>
public sealed partial class MainViewModel : ObservableObject, INavigationService
{
    private readonly IServiceProvider _services;
    private readonly ISettingsService _settings;
    private readonly IThemeService _theme;
    private readonly IUpdateService _updates;
    private readonly IDialogService _dialogs;
    private readonly ILogger<MainViewModel> _log;
    private bool _updatePromptShown;

    [ObservableProperty] private ObservableObject? _currentView;

    public MainViewModel(IServiceProvider services, ISettingsService settings, IThemeService theme,
        IUpdateService updates, IDialogService dialogs, ILogger<MainViewModel> log)
    {
        _services = services;
        _settings = settings;
        _theme = theme;
        _updates = updates;
        _dialogs = dialogs;
        _log = log;
        _updates.AvailableReleaseChanged += (_, _) => RefreshUpdateBanner();
    }

    /// <summary>Segoe MDL2 glyph for the theme toggle (sun when dark, moon when light).</summary>
    public string ThemeGlyph => _theme.Current == AppTheme.Dark ? "\uE706" : "\uE708";

    [RelayCommand]
    private void ToggleTheme()
    {
        _theme.Toggle();
        OnPropertyChanged(nameof(ThemeGlyph));
    }

    // ── Update banner ─────────────────────────────────────────────────────────────────────
    public bool UpdateAvailable => _updates.AvailableRelease is not null;
    public string UpdateBannerText => _updates.AvailableRelease is { } r
        ? string.Format(Localization.Instance.T("Upd_BannerText"), r.VersionText) : "";

    private void RefreshUpdateBanner()
    {
        OnPropertyChanged(nameof(UpdateAvailable));
        OnPropertyChanged(nameof(UpdateBannerText));
    }

    /// <summary>Open the update dialog for the release found by the last check.</summary>
    [RelayCommand]
    private void ShowUpdate()
    {
        if (_updates.AvailableRelease is { } release)
            _dialogs.ShowUpdate(new UpdateViewModel(release, _updates, _log));
    }

    /// <summary>
    /// Chooses the first screen. Called by the host AFTER this singleton is fully constructed — never
    /// from the constructor, because navigating resolves a child view-model that depends back on
    /// <see cref="INavigationService"/> (this same singleton); doing that mid-construction would
    /// re-enter construction and overflow the stack.
    /// </summary>
    public void Initialize()
    {
        if (_settings.Current.IsComplete) GoToDashboard();
        else GoToWizard();

        if (_updates.AutoCheckEnabled)
            _ = CheckForUpdatesAtStartupAsync();
    }

    /// <summary>
    /// Opportunistic startup check: runs in the background, never blocks the UI, and is silent unless a
    /// newer release exists, in which case the update dialog pops up once per session.
    /// </summary>
    private async Task CheckForUpdatesAtStartupAsync()
    {
        try
        {
            var result = await _updates.CheckAsync();
            if (result is null || !result.IsUpdateAvailable || _updatePromptShown) return;
            _updatePromptShown = true;
            RefreshUpdateBanner();
            ShowUpdate();
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "startup update check failed");
        }
    }

    public void GoToDashboard() => CurrentView = _services.GetRequiredService<DashboardViewModel>();
    public void GoToWizard() => CurrentView = _services.GetRequiredService<SetupWizardViewModel>();
    public void GoToSettings() => CurrentView = _services.GetRequiredService<SettingsViewModel>();
}
