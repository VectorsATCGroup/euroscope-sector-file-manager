namespace Vectors.EuroScopeUpdater.Core.Paths;

/// <summary>
/// Resolves the updater's local data layout under
/// <c>%LOCALAPPDATA%\VectorsATCGroup\EuroScopeSectorFileManager\</c>. Only technical data lives here
/// (config, state, logs, backups, operation journal). Nothing sensitive is stored.
/// Injectable root enables tests to redirect everything into a temp directory.
/// </summary>
public sealed class AppPaths
{
    public string Root { get; }

    public AppPaths(string? root = null)
    {
        Root = root ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "VectorsATCGroup", "EuroScopeSectorFileManager");
    }

    public string ConfigFile => Path.Combine(Root, "config.json");
    public string StateDir => Path.Combine(Root, "state");
    public string LogsDir => Path.Combine(Root, "logs");
    public string BackupsDir => Path.Combine(Root, "backups");
    public string OperationsDir => Path.Combine(Root, "operations");

    /// <summary>Per-FIR manifest file (<c>state\&lt;FIR&gt;.json</c>).</summary>
    public string ManifestFile(string fir) => Path.Combine(StateDir, $"{fir.ToUpperInvariant()}.json");

    /// <summary>Backup root for a FIR (<c>backups\&lt;FIR&gt;\</c>).</summary>
    public string FirBackupsDir(string fir) => Path.Combine(BackupsDir, fir.ToUpperInvariant());

    /// <summary>Ensure the whole directory tree exists.</summary>
    public void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(StateDir);
        Directory.CreateDirectory(LogsDir);
        Directory.CreateDirectory(BackupsDir);
        Directory.CreateDirectory(OperationsDir);
    }
}
