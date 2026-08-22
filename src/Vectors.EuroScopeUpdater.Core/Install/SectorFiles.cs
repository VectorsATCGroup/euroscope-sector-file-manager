using Vectors.EuroScopeUpdater.Core.Model;
using Vectors.EuroScopeUpdater.Core.Packaging;

namespace Vectors.EuroScopeUpdater.Core.Install;

/// <summary>Helpers for locating and comparing the versioned sector files in a FIR folder.</summary>
public static class SectorFiles
{
    private static readonly string[] SectorExtensions = { ".sct", ".ese", ".rwy" };

    /// <summary>All top-level versioned sector files (.sct/.ese/.rwy) in a FIR folder.</summary>
    public static IReadOnlyList<string> FindVersioned(string firDirectory)
    {
        if (!Directory.Exists(firDirectory)) return Array.Empty<string>();
        var result = new List<string>();
        foreach (var f in Directory.EnumerateFiles(firDirectory, "*", SearchOption.TopDirectoryOnly))
        {
            var name = Path.GetFileName(f);
            if (SectorExtensions.Contains(Path.GetExtension(name).ToLowerInvariant())
                && PackageName.TryParseSectorFile(name, out _))
                result.Add(f);
        }
        return result;
    }

    /// <summary>The stamped base name (without extension) of the current .sct, or null.</summary>
    public static string? CurrentSctFileName(string firDirectory) =>
        FindVersioned(firDirectory)
            .Select(Path.GetFileName)
            .FirstOrDefault(n => n!.EndsWith(".sct", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Highest installed version (AIRAC cycle, within-cycle revision and package revision) inferred from
    /// the versioned sector file names, or null. The stamped <c>.sct</c> EuroScope loads is the ground
    /// truth for what is installed, including AeroNav's same-cycle re-issues (<c>260801</c> → <c>260802</c>).
    /// </summary>
    public static SectorVersion? InferInstalledVersion(string firDirectory)
    {
        SectorVersion? best = null;
        foreach (var f in FindVersioned(firDirectory))
            if (PackageName.TryParseSectorFile(Path.GetFileName(f), out var sf))
                if (best is null || sf.Version > best.Value)
                    best = sf.Version;
        return best;
    }

    /// <summary>Highest installed AIRAC cycle inferred from the versioned sector file names, or null.</summary>
    public static AiracCycle? InferInstalledAirac(string firDirectory) => InferInstalledVersion(firDirectory)?.Airac;
}
