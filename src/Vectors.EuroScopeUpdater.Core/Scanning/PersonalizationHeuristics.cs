namespace Vectors.EuroScopeUpdater.Core.Scanning;

/// <summary>
/// A DISPLAY-ONLY heuristic for which files controllers typically personalize, derived from the
/// observed set of files an Update package omits (see <c>docs/package-analysis.md §3</c>). It is used
/// solely to keep the "Locally modified" status from firing on expected personalization edits.
/// <br/><br/>
/// It is NOT used to decide what an update writes — an update writes exactly the files contained in
/// the Update package and never consults this list. This avoids "inventing" a preservation list while
/// still giving a sensible status signal.
/// </summary>
public static class PersonalizationHeuristics
{
    public static bool IsUserOwned(string relativePath)
    {
        var p = relativePath.Replace('\\', '/');

        // EuroScope profiles (session state) — omitted by updates.
        if (p.EndsWith(".prf", StringComparison.OrdinalIgnoreCase)) return true;

        // Per-controller radar/solo customization folders — omitted by updates.
        if (p.Contains("/Settings/RADAR/", StringComparison.OrdinalIgnoreCase)) return true;
        if (p.Contains("/Settings/SOLO/", StringComparison.OrdinalIgnoreCase)) return true;

        // Binary plugins — omitted by updates.
        if (p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) return true;

        return false;
    }
}
