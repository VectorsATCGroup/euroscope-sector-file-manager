namespace Vectors.EuroScopeUpdater.Core.Locators;

public interface IEuroScopeLocator
{
    /// <summary>Best-guess EuroScope path (usually <c>%APPDATA%\EuroScope</c>), or null if not found.</summary>
    string? Detect();

    /// <summary>True if <paramref name="path"/> looks like a real EuroScope installation.</summary>
    bool LooksLikeEuroScope(string? path);
}

/// <summary>
/// Locates the EuroScope data directory without scanning whole drives. Primary candidate is
/// <c>%APPDATA%\EuroScope</c> (resolved via <see cref="Environment.GetFolderPath"/>, never hardcoded).
/// </summary>
public sealed class EuroScopeLocator : IEuroScopeLocator
{
    // Marker files/folders observed in a real EuroScope AppData install. Presence of any one
    // is a strong signal; we require at least one to avoid false positives on empty folders.
    private static readonly string[] MarkerFiles =
    {
        "version.txt", "euroscope_sector_providers.txt",
        "SectorFileProviderDescriptor.txt", "ipaddr.txt", "alias.txt",
    };
    private static readonly string[] MarkerDirs = { "DataFiles", "Settings" };

    public string? Detect()
    {
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "EuroScope"),
            // Some users keep a portable copy next to the executable in Documents.
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "EuroScope"),
        };

        foreach (var c in candidates)
            if (LooksLikeEuroScope(c))
                return c;

        // Fall back to the primary candidate if it merely exists (lets the wizard show it as unverified).
        return Directory.Exists(candidates[0]) ? candidates[0] : null;
    }

    public bool LooksLikeEuroScope(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) return false;

        foreach (var f in MarkerFiles)
            if (File.Exists(Path.Combine(path, f))) return true;

        foreach (var d in MarkerDirs)
            if (Directory.Exists(Path.Combine(path, d))) return true;

        // A EuroScope profile (*.prf) at the root is also conclusive.
        try
        {
            if (Directory.EnumerateFiles(path, "*.prf", SearchOption.TopDirectoryOnly).Any())
                return true;
        }
        catch (UnauthorizedAccessException) { /* ignore */ }

        return false;
    }
}
