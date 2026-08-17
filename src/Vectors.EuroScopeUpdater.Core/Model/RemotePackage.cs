using Vectors.EuroScopeUpdater.Core.Packaging;

namespace Vectors.EuroScopeUpdater.Core.Model;

/// <summary>
/// A package available from a <see cref="Abstractions.ISectorPackageSource"/> for one FIR.
/// <see cref="DownloadRef"/> is an opaque token the owning source understands (a URL, a DOM
/// handle, etc.) — the rest of the app never interprets it.
/// </summary>
public sealed record RemotePackage(
    string FirCode,
    PackageType Type,
    AiracCycle Airac,
    PackageName Name,
    string FileName,
    string DownloadRef,
    long? SizeBytes = null);

/// <summary>The catalog of packages offered for a division at a point in time.</summary>
public sealed record RemoteCatalog(
    string DivisionId,
    AiracCycle Airac,
    IReadOnlyList<RemotePackage> Packages)
{
    /// <summary>Best package of a given type for a FIR (highest version), or null.</summary>
    public RemotePackage? Best(string firCode, PackageType type) => Packages
        .Where(p => p.FirCode.Equals(firCode, StringComparison.OrdinalIgnoreCase) && p.Type == type)
        .OrderByDescending(p => p.Name.VersionRank)
        .FirstOrDefault();
}
