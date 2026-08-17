using Vectors.EuroScopeUpdater.Core.Model;

namespace Vectors.EuroScopeUpdater.Core.Manifest;

/// <summary>One file recorded in a manifest: path relative to the FIR folder, size and SHA-256.</summary>
public sealed record ManifestFileEntry(string RelativePath, long Size, string Sha256);

/// <summary>
/// Local record of what the updater installed for one FIR. Written after a successful
/// install/update, it lets later scans prove the version and detect local modification with
/// high confidence. Contains only technical data — never anything sensitive.
/// </summary>
public sealed record LocalManifest
{
    public string Division { get; init; } = Model.Division.VatsimBrasil.Id;
    public string Fir { get; init; } = "";
    public string Airac { get; init; } = "";
    public int CycleRevision { get; init; }
    public int PackageRevision { get; init; }
    public string PackageType { get; init; } = nameof(Model.PackageType.Install);
    public string BuildTimestampUtc { get; init; } = "";
    public string InstalledAtUtc { get; init; } = "";
    /// <summary>Name of the versioned .sct the profile(s) point at after this operation.</summary>
    public string SectorFileName { get; init; } = "";
    public IReadOnlyList<ManifestFileEntry> Files { get; init; } = Array.Empty<ManifestFileEntry>();

    public bool TryGetAirac(out AiracCycle airac) => AiracCycle.TryParse(Airac, out airac);
}
