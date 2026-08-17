using Vectors.EuroScopeUpdater.Core.Paths;
using Vectors.EuroScopeUpdater.Core.Time;

namespace Vectors.EuroScopeUpdater.Tests.Support;

/// <summary>
/// An isolated temp directory tree for a single test: an app-data root, a EuroScope root and a
/// sector-files root, all under one folder that is deleted on <see cref="Dispose"/>. Nothing here
/// ever touches a real installation.
/// </summary>
public sealed class TestWorkspace : IDisposable
{
    public string Root { get; }
    public AppPaths AppPaths { get; }
    public string EuroScopeDir { get; }
    public string SectorFilesDir { get; }

    public TestWorkspace()
    {
        Root = Path.Combine(Path.GetTempPath(), "vatupd-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
        AppPaths = new AppPaths(Path.Combine(Root, "appdata"));
        AppPaths.EnsureCreated();
        EuroScopeDir = Path.Combine(Root, "EuroScope");
        SectorFilesDir = Path.Combine(EuroScopeDir, "Vatbrz");
        Directory.CreateDirectory(SectorFilesDir);
    }

    public string FirDir(string fir) => Path.Combine(SectorFilesDir, fir.ToUpperInvariant());

    public void Dispose()
    {
        try { if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true); }
        catch { /* best effort */ }
    }
}

/// <summary>Deterministic clock for tests.</summary>
public sealed class FixedClock(DateTime utcNow) : IClock
{
    public DateTime UtcNow { get; set; } = utcNow;
    public void Advance(TimeSpan by) => UtcNow += by;
}
