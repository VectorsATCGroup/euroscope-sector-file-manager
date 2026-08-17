using System.Windows;
using Vectors.EuroScopeUpdater.Core.Settings;

namespace Vectors.EuroScopeUpdater.App.Services;

public enum AppTheme { Dark, Light }

public interface IThemeService
{
    AppTheme Current { get; }
    void Apply(AppTheme theme);
    void Toggle();
    /// <summary>Load the saved theme and apply it (called at startup).</summary>
    void InitializeFromSettings();
}

/// <summary>
/// Swaps the active theme dictionary (index 0 of the app's merged dictionaries) at runtime. All
/// theme colors are referenced via DynamicResource, so the whole UI re-colors instantly, and the
/// logo (DynamicResource LogoSource) switches to the variant made for the new background.
/// </summary>
public sealed class ThemeService : IThemeService
{
    private readonly ISettingsService _settings;

    public ThemeService(ISettingsService settings) => _settings = settings;

    public AppTheme Current { get; private set; } = AppTheme.Dark;

    public void InitializeFromSettings()
    {
        var saved = string.Equals(_settings.Current.Theme, "Light", StringComparison.OrdinalIgnoreCase)
            ? AppTheme.Light : AppTheme.Dark;
        Apply(saved, persist: false);
    }

    public void Apply(AppTheme theme) => Apply(theme, persist: true);

    public void Toggle() => Apply(Current == AppTheme.Dark ? AppTheme.Light : AppTheme.Dark);

    private void Apply(AppTheme theme, bool persist)
    {
        var uri = new Uri($"Theme/{(theme == AppTheme.Light ? "Light" : "Dark")}.xaml", UriKind.Relative);
        var dict = new ResourceDictionary { Source = uri };

        var merged = Application.Current.Resources.MergedDictionaries;
        if (merged.Count > 0) merged[0] = dict; // index 0 is the theme (see App.xaml)
        else merged.Insert(0, dict);

        Current = theme;

        if (persist)
        {
            var s = _settings.Current;
            s.Theme = theme.ToString();
            _settings.Save(s);
        }
    }
}
