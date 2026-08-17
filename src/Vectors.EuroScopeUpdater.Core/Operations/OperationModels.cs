using Vectors.EuroScopeUpdater.Core.Model;

namespace Vectors.EuroScopeUpdater.Core.Operations;

/// <summary>Which destructive operation is being performed.</summary>
public enum OperationKind { CleanInstall, Update }

/// <summary>Ordered phases of the transactional pipeline (also used for progress + recovery).</summary>
public enum OperationPhase
{
    Prepare, Download, ValidateArchive, Stage, ValidateStaging,
    Backup, Commit, Verify, Complete,
    RollingBack, RolledBack, Failed,
}

/// <summary>Progress notification pushed to the UI during an operation.</summary>
public sealed record OperationProgress(
    OperationPhase Phase,
    string Message,
    double? Fraction = null,
    long? BytesReceived = null,
    long? TotalBytes = null);

/// <summary>Inputs for one install/update operation.</summary>
public sealed record InstallRequest(
    Fir Fir,
    OperationKind Kind,
    RemotePackage Package,
    string FirDirectory,
    string SectorFilesRoot);

/// <summary>Outcome of an operation.</summary>
public sealed record InstallResult(
    bool Success,
    OperationPhase FinalPhase,
    string? Message,
    bool RolledBack,
    bool RollbackFailed = false,
    Exception? Error = null)
{
    public static InstallResult Ok(string message) =>
        new(true, OperationPhase.Complete, message, RolledBack: false);
}

/// <summary>
/// Durable record of an in-flight operation (operations\&lt;id&gt;.json), enabling recovery if the app
/// is killed mid-operation. Contains only technical fields.
/// </summary>
public sealed class OperationState
{
    public string Id { get; set; } = "";
    public string Fir { get; set; } = "";
    public string Kind { get; set; } = nameof(OperationKind.Update);
    public string Phase { get; set; } = nameof(OperationPhase.Prepare);
    public string FirDirectory { get; set; } = "";
    public string WorkRoot { get; set; } = "";
    public string? BackupDir { get; set; }
    public string? PreviousDir { get; set; }
    public string StartedAtUtc { get; set; } = "";
    public string? UpdatedAtUtc { get; set; }
    public bool CommitStarted { get; set; }
    public bool CommitCompleted { get; set; }
}
