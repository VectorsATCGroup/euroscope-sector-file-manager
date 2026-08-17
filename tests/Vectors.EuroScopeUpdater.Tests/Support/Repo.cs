namespace Vectors.EuroScopeUpdater.Tests.Support;

/// <summary>Locates repository paths at test time by walking up to the solution file.</summary>
public static class Repo
{
    public static string Root
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Vectors.EuroScopeUpdater.sln")))
                dir = dir.Parent;
            return dir?.FullName
                   ?? throw new DirectoryNotFoundException("Could not locate the repository root (Vectors.EuroScopeUpdater.sln).");
        }
    }

    public static string FixturesDir => Path.Combine(Root, "fixtures");
    public static string ArchivesDir => Path.Combine(FixturesDir, "archives");
}
