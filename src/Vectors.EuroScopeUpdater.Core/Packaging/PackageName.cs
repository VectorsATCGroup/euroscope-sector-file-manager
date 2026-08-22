using System.Globalization;
using System.Text.RegularExpressions;
using Vectors.EuroScopeUpdater.Core.Model;

namespace Vectors.EuroScopeUpdater.Core.Packaging;

/// <summary>
/// Parses the AeroNav file-name contract, which encodes everything needed to identify a package
/// or a versioned sector file (see <c>docs/package-analysis.md</c>):
/// <list type="bullet">
/// <item>Package: <c>&lt;FIR&gt;-&lt;Install|Update&gt;-Package_&lt;TS14&gt;-&lt;AIRAC6&gt;-&lt;REV4&gt;.7z</c></item>
/// <item>Sector file: <c>&lt;FIR&gt;-&lt;FIR&gt;_&lt;TS14&gt;-&lt;AIRAC6&gt;-&lt;REV4&gt;.&lt;ext&gt;</c></item>
/// </list>
/// where <c>AIRAC6</c> is <c>YYNNRR</c> (cycle <c>YYNN</c> + revision <c>RR</c>).
/// </summary>
public sealed record PackageName
{
    private static readonly Regex PackageRegex = new(
        @"^(?<fir>[A-Z]{2}[A-Z0-9]{2})-(?<type>Install|Update)-Package_(?<ts>\d{14})-(?<airac>\d{4})(?<rev2>\d{2})-(?<rev4>\d{4})\.7z$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Real AeroNav download URL form: .../<FIR>/<Install|Update>-Package_<TS14>-<AIRAC6>-<REV>.7z
    // The FIR is a PATH segment (not a filename prefix) and the revision is variable-width (e.g. "-1").
    private static readonly Regex UrlFileRegex = new(
        @"^(?<type>Install|Update)-Package_(?<ts>\d{14})-(?<airac>\d{4})(?<rev2>\d{2})-(?<rev>\d+)\.7z$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex FirSegmentRegex = new(@"^[A-Za-z]{2}[A-Za-z0-9]{2}$", RegexOptions.Compiled);

    private static readonly Regex SectorFileRegex = new(
        @"^(?<fir>[A-Z]{2}[A-Z0-9]{2})-(?<fir2>[A-Z]{2}[A-Z0-9]{2})_(?<ts>\d{14})-(?<airac>\d{4})(?<rev2>\d{2})-(?<rev4>\d{4})\.(?<ext>sct|ese|rwy)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public string Fir { get; init; } = "";
    public PackageType Type { get; init; }
    public DateTime BuildTimestampUtc { get; init; }
    public AiracCycle Airac { get; init; }
    /// <summary>Revision within the AIRAC cycle (the <c>RR</c> pair, e.g. 01).</summary>
    public int CycleRevision { get; init; }
    /// <summary>Package revision (the trailing 4-digit group, e.g. 0001).</summary>
    public int PackageRevision { get; init; }

    /// <summary>Try to parse an Install/Update package file name (with or without directory).</summary>
    public static bool TryParsePackage(string? fileName, out PackageName result)
    {
        result = null!;
        if (string.IsNullOrWhiteSpace(fileName)) return false;
        var name = Path.GetFileName(fileName.Trim());
        var m = PackageRegex.Match(name);
        if (!m.Success) return false;
        if (!TryStamp(m, out var ts, out var airac)) return false;

        result = new PackageName
        {
            Fir = m.Groups["fir"].Value.ToUpperInvariant(),
            Type = m.Groups["type"].Value.Equals("Install", StringComparison.OrdinalIgnoreCase)
                ? PackageType.Install : PackageType.Update,
            BuildTimestampUtc = ts,
            Airac = airac,
            CycleRevision = int.Parse(m.Groups["rev2"].Value, CultureInfo.InvariantCulture),
            PackageRevision = int.Parse(m.Groups["rev4"].Value, CultureInfo.InvariantCulture),
        };
        return true;
    }

    /// <summary>
    /// Parse a real AeroNav package URL, where the FIR is the parent path segment and the file is
    /// <c>&lt;Install|Update&gt;-Package_&lt;TS&gt;-&lt;AIRAC6&gt;-&lt;REV&gt;.7z</c> (e.g.
    /// <c>https://files.aero-nav.com/SBAO/Install-Package_20260810144829-260801-1.7z</c>).
    /// Accepts absolute URLs and site-relative paths (<c>/SBAO/Install-Package_….7z</c>).
    /// </summary>
    public static bool TryParseFromUrl(string? url, out PackageName result)
    {
        result = null!;
        if (string.IsNullOrWhiteSpace(url)) return false;

        var clean = url.Trim().Split('?', '#')[0];
        var path = Uri.TryCreate(clean, UriKind.Absolute, out var abs) ? abs.AbsolutePath : clean;
        var segs = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segs.Length < 2) return false;

        var firSeg = segs[^2];
        var fileName = segs[^1];
        if (!FirSegmentRegex.IsMatch(firSeg)) return false;

        var m = UrlFileRegex.Match(fileName);
        if (!m.Success || !TryStamp(m, out var ts, out var airac)) return false;

        result = new PackageName
        {
            Fir = firSeg.ToUpperInvariant(),
            Type = m.Groups["type"].Value.Equals("Install", StringComparison.OrdinalIgnoreCase)
                ? PackageType.Install : PackageType.Update,
            BuildTimestampUtc = ts,
            Airac = airac,
            CycleRevision = int.Parse(m.Groups["rev2"].Value, CultureInfo.InvariantCulture),
            PackageRevision = int.Parse(m.Groups["rev"].Value, CultureInfo.InvariantCulture),
        };
        return true;
    }

    /// <summary>Identifying info parsed from a versioned sector (.sct/.ese/.rwy) file name.</summary>
    public readonly record struct SectorFile(string Fir, string Extension, DateTime BuildTimestampUtc,
        AiracCycle Airac, int CycleRevision, int PackageRevision)
    {
        /// <summary>Full version (cycle, within-cycle revision, package revision) carried by the file name.</summary>
        public SectorVersion Version => new(Airac, CycleRevision, PackageRevision);
    }

    /// <summary>Try to parse a versioned sector file name.</summary>
    public static bool TryParseSectorFile(string? fileName, out SectorFile result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(fileName)) return false;
        var name = Path.GetFileName(fileName.Trim());
        var m = SectorFileRegex.Match(name);
        if (!m.Success) return false;
        if (!TryStamp(m, out var ts, out var airac)) return false;

        result = new SectorFile(
            Fir: m.Groups["fir"].Value.ToUpperInvariant(),
            Extension: m.Groups["ext"].Value.ToLowerInvariant(),
            BuildTimestampUtc: ts,
            Airac: airac,
            CycleRevision: int.Parse(m.Groups["rev2"].Value, CultureInfo.InvariantCulture),
            PackageRevision: int.Parse(m.Groups["rev4"].Value, CultureInfo.InvariantCulture));
        return true;
    }

    private static bool TryStamp(Match m, out DateTime timestampUtc, out AiracCycle airac)
    {
        timestampUtc = default;
        airac = default;
        if (!DateTime.TryParseExact(m.Groups["ts"].Value, "yyyyMMddHHmmss",
                CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out timestampUtc))
            return false;
        return AiracCycle.TryParse(m.Groups["airac"].Value, out airac);
    }

    /// <summary>Full version (cycle, within-cycle revision, package revision) carried by the name.</summary>
    public SectorVersion Version => new(Airac, CycleRevision, PackageRevision);

    /// <summary>
    /// Total ordering key that distinguishes even same-cycle re-issues:
    /// AIRAC cycle, then cycle revision (RR), then package revision.
    /// </summary>
    public long VersionRank => Version.Rank;
}
