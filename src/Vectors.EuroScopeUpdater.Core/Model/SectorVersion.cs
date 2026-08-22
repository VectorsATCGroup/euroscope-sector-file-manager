namespace Vectors.EuroScopeUpdater.Core.Model;

/// <summary>
/// The full version of a sector-file package or of an installed FIR: the AIRAC cycle, the
/// within-cycle revision (the <c>RR</c> pair of AeroNav's <c>YYNNRR</c> field; AeroNav's operations team
/// re-issues a cycle as <c>260802</c>, <c>260803</c>… when they correct something inside the same AIRAC)
/// and the package revision (the trailing group). Ordered by cycle, then cycle revision, then package
/// revision, so a re-issue inside the same AIRAC counts as a newer version.
/// </summary>
public readonly record struct SectorVersion(AiracCycle Airac, int CycleRevision, int PackageRevision)
    : IComparable<SectorVersion>
{
    /// <summary>Total ordering key: cycle → cycle revision → package revision.</summary>
    public long Rank => ((long)Airac.Value * 100 + CycleRevision) * 10000 + PackageRevision;

    public int CompareTo(SectorVersion other) => Rank.CompareTo(other.Rank);
    public static bool operator <(SectorVersion a, SectorVersion b) => a.Rank < b.Rank;
    public static bool operator >(SectorVersion a, SectorVersion b) => a.Rank > b.Rank;
    public static bool operator <=(SectorVersion a, SectorVersion b) => a.Rank <= b.Rank;
    public static bool operator >=(SectorVersion a, SectorVersion b) => a.Rank >= b.Rank;

    /// <summary>True when both describe the same AIRAC cycle and the same within-cycle revision.</summary>
    public bool SameIssue(SectorVersion other) => Airac == other.Airac && CycleRevision == other.CycleRevision;

    /// <summary>
    /// Display form: <c>2608</c> for a cycle's first issue, <c>2608/2</c> for a within-cycle re-issue
    /// (RR = 02). The package revision is an internal tie-breaker and is not shown.
    /// </summary>
    public string Label => CycleRevision <= 1 ? Airac.ToString() : $"{Airac}/{CycleRevision}";

    public override string ToString() => Label;
}
