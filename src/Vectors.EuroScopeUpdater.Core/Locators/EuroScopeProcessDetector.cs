using System.Diagnostics;

namespace Vectors.EuroScopeUpdater.Core.Locators;

public interface IEuroScopeProcessDetector
{
    /// <summary>True if an <c>EuroScope</c> process is currently running.</summary>
    bool IsRunning();
}

/// <summary>
/// Detects a running EuroScope so destructive file operations can warn the user first. The updater
/// never force-closes EuroScope — it only asks the user to close it and offers "Check again".
/// </summary>
public sealed class EuroScopeProcessDetector : IEuroScopeProcessDetector
{
    public bool IsRunning()
    {
        try
        {
            // Process names on Windows are without the .exe suffix.
            return Process.GetProcessesByName("EuroScope").Length > 0;
        }
        catch
        {
            // If we cannot enumerate processes, do not block the user; err toward allowing,
            // the transactional pipeline still protects against partial writes.
            return false;
        }
    }
}
