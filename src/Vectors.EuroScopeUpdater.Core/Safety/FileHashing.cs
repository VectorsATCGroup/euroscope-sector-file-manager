using System.Security.Cryptography;

namespace Vectors.EuroScopeUpdater.Core.Safety;

/// <summary>SHA-256 helpers used for manifest generation and local-modification detection.</summary>
public static class FileHashing
{
    public static string Sha256(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant();
    }

    /// <summary>Enumerate files under <paramref name="root"/> as (relativePath, fullPath), forward-slash relative.</summary>
    public static IEnumerable<(string Relative, string Full)> EnumerateFiles(string root)
    {
        var rootFull = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        foreach (var full in Directory.EnumerateFiles(rootFull, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(rootFull, full).Replace('\\', '/');
            yield return (rel, full);
        }
    }
}
