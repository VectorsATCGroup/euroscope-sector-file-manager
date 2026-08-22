using System.Globalization;
using System.Reflection;

namespace Vectors.EuroScopeUpdater.Core.Updates;

/// <summary>
/// Version parsing/comparison for the self-update feature. Release tags look like <c>v1.2.3</c>; the
/// running version comes from the assembly's informational version (<c>1.2.3+commit</c>) or, failing
/// that, the assembly version. Only the numeric <c>major.minor.patch[.revision]</c> part is compared;
/// build metadata (<c>+…</c>) and pre-release suffixes (<c>-beta</c>) are ignored, which is safe because
/// the release pipeline only publishes stable <c>vN.N.N</c> tags.
/// </summary>
public static class AppVersions
{
    /// <summary>
    /// Parse "v1.2.3", "1.2.3", "1.2.3+sha", "1.2.3-beta.1", "1.2.3.0" into a <see cref="Version"/>.
    /// Returns null for anything that does not start with at least <c>major.minor</c>.
    /// </summary>
    public static Version? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var s = text.Trim();
        if (s.StartsWith("v", StringComparison.OrdinalIgnoreCase) || s.StartsWith("V"))
            s = s[1..];

        // Cut build metadata / pre-release suffixes.
        var cut = s.IndexOfAny(new[] { '+', '-', ' ' });
        if (cut >= 0) s = s[..cut];

        var parts = s.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length is < 2 or > 4) return null;
        var nums = new int[4];
        for (var i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out nums[i]))
                return null;
        }
        return parts.Length switch
        {
            2 => new Version(nums[0], nums[1]),
            3 => new Version(nums[0], nums[1], nums[2]),
            _ => new Version(nums[0], nums[1], nums[2], nums[3]),
        };
    }

    /// <summary>Compare on major.minor.patch only (revision/build number is ignored).</summary>
    public static bool IsNewer(Version candidate, Version current) =>
        Normalize(candidate) > Normalize(current);

    public static string Format(Version v) => Normalize(v).ToString(3);

    private static Version Normalize(Version v) =>
        new(Math.Max(0, v.Major), Math.Max(0, v.Minor), Math.Max(0, v.Build));

    /// <summary>
    /// The running application's version: the informational version (set from the release tag via
    /// <c>-p:Version=</c>) with build metadata stripped, else the assembly version.
    /// </summary>
    public static Version Current(Assembly? assembly = null)
    {
        assembly ??= Assembly.GetEntryAssembly() ?? typeof(AppVersions).Assembly;
        var info = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        return Parse(info) ?? assembly.GetName().Version ?? new Version(0, 0, 0);
    }
}
