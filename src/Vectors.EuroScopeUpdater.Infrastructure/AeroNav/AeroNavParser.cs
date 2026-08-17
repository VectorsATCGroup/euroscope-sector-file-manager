using System.Text.RegularExpressions;
using Vectors.EuroScopeUpdater.Core.Model;
using Vectors.EuroScopeUpdater.Core.Packaging;

namespace Vectors.EuroScopeUpdater.Infrastructure.AeroNav;

/// <summary>
/// Pure HTML → package-model parser for the AeroNav SBXX listing. Deliberately keyed off the stable
/// package file-name contract embedded in download links (see <c>docs/aeronav-integration.md</c>),
/// not on generated CSS classes or DOM positions, so it degrades gracefully across site restyles.
/// This class does no I/O and is fully unit-testable against saved HTML snapshots.
/// </summary>
public sealed class AeroNavParser
{
    // Any href value; we then test each candidate against the package grammar.
    private static readonly Regex HrefRegex = new(
        "(?:href|data-href|data-url)\\s*=\\s*[\"']([^\"']+)[\"']",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Fallback: bare package URLs/paths appearing anywhere in text (FIR is a path segment).
    private static readonly Regex BarePackageRegex = new(
        @"[A-Za-z]{2}[A-Za-z0-9]{2}/(?:Install|Update)-Package_\d{14}-\d{6}-\d+\.7z",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Extract every package found in <paramref name="html"/>. <paramref name="baseUrl"/> resolves
    /// relative hrefs into absolute download references.
    /// </summary>
    public RemoteCatalog Parse(string divisionId, string html, string baseUrl)
    {
        var byFile = new Dictionary<string, RemotePackage>(StringComparer.OrdinalIgnoreCase);

        void Consider(string reference)
        {
            // Real AeroNav links carry the FIR as a path segment (…/SBRE/Update-Package_….7z).
            // Fall back to the legacy filename-prefixed grammar for older/synthetic links.
            if (!PackageName.TryParseFromUrl(reference, out var name))
            {
                var legacyName = Path.GetFileName(reference.Split('?', '#')[0]);
                if (!PackageName.TryParsePackage(legacyName, out name)) return;
            }

            var downloadRef = ResolveUrl(baseUrl, reference);
            var fileName = $"{name.Fir}-{Path.GetFileName(reference.Split('?', '#')[0])}";
            var pkg = new RemotePackage(
                FirCode: name.Fir,
                Type: name.Type,
                Airac: name.Airac,
                Name: name,
                FileName: fileName,
                DownloadRef: downloadRef);

            // Keep the first (href-based) occurrence; only replace for a strictly higher version.
            // This preserves the real download URL over a bare file-name fallback of the same file.
            if (!byFile.TryGetValue(fileName, out var existing) || name.VersionRank > existing.Name.VersionRank)
                byFile[fileName] = pkg;
        }

        foreach (Match m in HrefRegex.Matches(html))
            Consider(m.Groups[1].Value);

        foreach (Match m in BarePackageRegex.Matches(html))
            Consider(m.Value);

        var packages = byFile.Values.ToList();
        var airac = packages.Count > 0
            ? new AiracCycle(packages.Max(p => p.Airac.Value))
            : default;

        return new RemoteCatalog(divisionId, airac, packages);
    }

    /// <summary>Resolve a possibly-relative reference against a base URL.</summary>
    public static string ResolveUrl(string baseUrl, string reference)
    {
        if (Uri.TryCreate(reference, UriKind.Absolute, out var abs)) return abs.ToString();
        if (Uri.TryCreate(new Uri(baseUrl, UriKind.Absolute), reference, out var combined)) return combined.ToString();
        return reference;
    }
}
