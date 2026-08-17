using Vectors.EuroScopeUpdater.Core.Packaging;

namespace Vectors.EuroScopeUpdater.Core.Install;

/// <summary>
/// Surgically re-points EuroScope profile (<c>.prf</c>) files at a new versioned <c>.sct</c> without
/// disturbing any other line. Rationale (see <c>docs/package-analysis.md §5</c>): an Update package
/// omits the <c>.prf</c> to preserve the controller's profile, yet the profile's
/// <c>Settings⇥sector⇥\&lt;name&gt;.sct</c> line references the sector file by its stamped name. On update
/// we rewrite only that single value.
/// </summary>
public static class ProfileRepointer
{
    /// <summary>
    /// Rewrite the <c>sector</c> line of every <c>.prf</c> in <paramref name="firDirectory"/> (top level)
    /// to point at <paramref name="newSctFileName"/>. Returns the count of profiles changed.
    /// Preserves the file's original line endings per line and leaves all other lines byte-identical.
    /// </summary>
    public static int RepointAll(string firDirectory, string newSctFileName)
    {
        var changed = 0;
        foreach (var prf in Directory.EnumerateFiles(firDirectory, "*.prf", SearchOption.TopDirectoryOnly))
            if (RepointFile(prf, newSctFileName))
                changed++;
        return changed;
    }

    /// <summary>Re-point a single .prf file. Returns true if it was modified.</summary>
    public static bool RepointFile(string prfPath, string newSctFileName)
    {
        // .prf uses \ path separators and a leading backslash on the value.
        var newValue = "\\" + Path.GetFileName(newSctFileName);

        var text = File.ReadAllText(prfPath);
        var lines = text.Split('\n');
        var modified = false;

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var trimmedEnd = line.TrimEnd('\r');
            var parts = trimmedEnd.Split('\t');
            // Match:  Settings \t sector \t <value>
            if (parts.Length >= 3 &&
                parts[0].Equals("Settings", StringComparison.OrdinalIgnoreCase) &&
                parts[1].Equals("sector", StringComparison.OrdinalIgnoreCase))
            {
                if (parts[2] == newValue) continue; // already correct
                parts[2] = newValue;
                var rebuilt = string.Join('\t', parts);
                lines[i] = line.EndsWith('\r') ? rebuilt + "\r" : rebuilt;
                modified = true;
            }
        }

        if (modified)
            File.WriteAllText(prfPath, string.Join('\n', lines));
        return modified;
    }

    /// <summary>Read the current sector value from a .prf, or null if absent.</summary>
    public static string? ReadSectorReference(string prfPath)
    {
        foreach (var raw in File.ReadLines(prfPath))
        {
            var parts = raw.TrimEnd('\r').Split('\t');
            if (parts.Length >= 3 &&
                parts[0].Equals("Settings", StringComparison.OrdinalIgnoreCase) &&
                parts[1].Equals("sector", StringComparison.OrdinalIgnoreCase))
                return parts[2];
        }
        return null;
    }
}
