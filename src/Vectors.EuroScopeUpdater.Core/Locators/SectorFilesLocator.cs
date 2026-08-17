namespace Vectors.EuroScopeUpdater.Core.Locators;

public interface ISectorFilesLocator
{
    /// <summary>Recommended sector-files root inside a EuroScope install (<c>&lt;EuroScope&gt;\Vatbrz</c>).</summary>
    string RecommendedPath(string euroScopePath);

    /// <summary>Ensure the sector-files root exists, creating it if needed. Returns the resolved path.</summary>
    string EnsureExists(string sectorFilesPath);
}

/// <summary>
/// Resolves where FIR folders live. The recommended location is <c>&lt;EuroScope&gt;\Vatbrz</c>, but any
/// custom directory is supported — the engine only ever works with the configured path.
/// </summary>
public sealed class SectorFilesLocator : ISectorFilesLocator
{
    public const string DefaultFolderName = "Vatbrz";

    public string RecommendedPath(string euroScopePath) => Path.Combine(euroScopePath, DefaultFolderName);

    public string EnsureExists(string sectorFilesPath)
    {
        Directory.CreateDirectory(sectorFilesPath);
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(sectorFilesPath));
    }
}
