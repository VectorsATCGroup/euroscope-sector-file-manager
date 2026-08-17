namespace Vectors.EuroScopeUpdater.Core.Settings;

/// <summary>
/// Persisted technical configuration (config.json). Contains ONLY paths and non-sensitive flags —
/// never credentials, tokens, or cookies.
/// </summary>
public sealed class ApplicationSettings
{
    /// <summary>Absolute path to the EuroScope installation (the folder holding EuroScope.exe / DataFiles).</summary>
    public string? EuroScopePath { get; set; }

    /// <summary>Absolute path to the sector-files root (recommended: &lt;EuroScope&gt;\Vatbrz).</summary>
    public string? SectorFilesPath { get; set; }

    /// <summary>Division identifier (only "SBXX" in this version).</summary>
    public string Division { get; set; } = Model.Division.VatsimBrasil.Id;

    /// <summary>True once the first-run wizard has completed.</summary>
    public bool SetupCompleted { get; set; }

    /// <summary>Number of per-FIR backups to retain (oldest pruned beyond this).</summary>
    public int BackupsToKeep { get; set; } = 5;

    /// <summary>UI theme: "Dark" (default) or "Light".</summary>
    public string Theme { get; set; } = "Dark";

    /// <summary>UI language: "Pt" (default) or "En".</summary>
    public string Language { get; set; } = "Pt";

    public bool IsComplete =>
        !string.IsNullOrWhiteSpace(EuroScopePath) &&
        !string.IsNullOrWhiteSpace(SectorFilesPath) &&
        SetupCompleted;
}
