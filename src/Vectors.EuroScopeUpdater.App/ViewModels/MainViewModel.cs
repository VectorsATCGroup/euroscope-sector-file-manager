using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Vectors.EuroScopeUpdater.App.Services;
using Vectors.EuroScopeUpdater.Core.Settings;

namespace Vectors.EuroScopeUpdater.App.ViewModels;

/// <summary>
/// Shell view-model. Chooses the first screen (wizard vs. dashboard) from saved settings and hosts the
/// current content. Implements <see cref="INavigationService"/> so child view-models can navigate.
/// </summary>
public sealed partial class MainViewModel : ObservableObject, INavigationService
{
    private readonly IServiceProvider _services;
    private readonly ISettingsService _settings;
    private readonly IThemeService _theme;

    [ObservableProperty] private ObservableObject? _currentView;

    public MainViewModel(IServiceProvider services, ISettingsService settings, IThemeService theme)
    {
        _services = services;
        _settings = settings;
        _theme = theme;
    }

    /// <summary>Segoe MDL2 glyph for the theme toggle (sun when dark, moon when light).</summary>
    public string ThemeGlyph => _theme.Current == AppTheme.Dark ? "\uE706" : "\uE708";

    [RelayCommand]
    private void ToggleTheme()
    {
        _theme.Toggle();
        OnPropertyChanged(nameof(ThemeGlyph));
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
    }

    public void GoToDashboard() => CurrentView = _services.GetRequiredService<DashboardViewModel>();
    public void GoToWizard() => CurrentView = _services.GetRequiredService<SetupWizardViewModel>();
    public void GoToSettings() => CurrentView = _services.GetRequiredService<SettingsViewModel>();
}
