using Vectors.EuroScopeUpdater.Core.Paths;
using Vectors.EuroScopeUpdater.Core.Serialization;

namespace Vectors.EuroScopeUpdater.Core.Settings;

public interface ISettingsService
{
    ApplicationSettings Current { get; }
    ApplicationSettings Load();
    void Save(ApplicationSettings settings);
}

/// <summary>Loads/saves <see cref="ApplicationSettings"/> from <c>config.json</c>.</summary>
public sealed class SettingsService : ISettingsService
{
    private readonly AppPaths _paths;
    private ApplicationSettings _current = new();

    public SettingsService(AppPaths paths) => _paths = paths;

    public ApplicationSettings Current => _current;

    public ApplicationSettings Load()
    {
        _current = AppJson.Read<ApplicationSettings>(_paths.ConfigFile) ?? new ApplicationSettings();
        return _current;
    }

    public void Save(ApplicationSettings settings)
    {
        _paths.EnsureCreated();
        AppJson.WriteAtomic(_paths.ConfigFile, settings);
        _current = settings;
    }
}
