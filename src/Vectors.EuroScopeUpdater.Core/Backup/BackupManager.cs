using Microsoft.Extensions.Logging;
using Vectors.EuroScopeUpdater.Core.Paths;

namespace Vectors.EuroScopeUpdater.Core.Backup;

/// <summary>Metadata describing one backup of a FIR folder.</summary>
public sealed record BackupInfo(string FirCode, string TimestampId, string Directory, DateTime CreatedUtc);

public interface IBackupManager
{
    /// <summary>
    /// Snapshot <paramref name="firDirectory"/> into <c>backups\&lt;FIR&gt;\&lt;timestamp&gt;\</c>.
    /// Returns null if the FIR directory does not exist (nothing to back up, e.g. fresh install).
    /// </summary>
    BackupInfo? CreateBackup(string firCode, string firDirectory, DateTime nowUtc);

    /// <summary>Restore a backup back over <paramref name="firDirectory"/> (used by rollback).</summary>
    void Restore(BackupInfo backup, string firDirectory);

    IReadOnlyList<BackupInfo> List(string firCode);

    /// <summary>Delete oldest backups beyond <paramref name="keep"/> for a FIR.</summary>
    void Prune(string firCode, int keep);
}

/// <summary>
/// Simple timestamped backup store under the app-data <c>backups\</c> tree. Backups are full copies of
/// a FIR folder; a small retention policy (keep N) prevents unbounded growth.
/// </summary>
public sealed class BackupManager : IBackupManager
{
    private const string TimestampFormat = "yyyyMMdd-HHmmss";
    private readonly AppPaths _paths;
    private readonly ILogger<BackupManager> _log;

    public BackupManager(AppPaths paths, ILogger<BackupManager> log)
    {
        _paths = paths;
        _log = log;
    }

    public BackupInfo? CreateBackup(string firCode, string firDirectory, DateTime nowUtc)
    {
        if (!Directory.Exists(firDirectory)) return null;

        var id = nowUtc.ToString(TimestampFormat);
        var dest = Path.Combine(_paths.FirBackupsDir(firCode), id);
        // Guarantee a unique folder even if two backups land in the same second.
        var suffix = 0;
        var unique = dest;
        while (Directory.Exists(unique)) unique = dest + "-" + (++suffix).ToString("D2");
        dest = unique;

        CopyDirectory(firDirectory, dest);
        _log.LogInformation("Backup created for {Fir} at {Dir}", firCode, dest);
        return new BackupInfo(firCode, Path.GetFileName(dest), dest, nowUtc);
    }

    public void Restore(BackupInfo backup, string firDirectory)
    {
        if (!Directory.Exists(backup.Directory))
            throw new DirectoryNotFoundException($"Backup directory not found: {backup.Directory}");

        // Replace the live folder with the backup content.
        if (Directory.Exists(firDirectory)) Directory.Delete(firDirectory, recursive: true);
        CopyDirectory(backup.Directory, firDirectory);
        _log.LogInformation("Restored {Fir} from backup {Id}", backup.FirCode, backup.TimestampId);
    }

    public IReadOnlyList<BackupInfo> List(string firCode)
    {
        var root = _paths.FirBackupsDir(firCode);
        if (!Directory.Exists(root)) return Array.Empty<BackupInfo>();

        var list = new List<BackupInfo>();
        foreach (var dir in Directory.EnumerateDirectories(root))
        {
            var id = Path.GetFileName(dir);
            var created = Directory.GetCreationTimeUtc(dir);
            list.Add(new BackupInfo(firCode, id, dir, created));
        }
        // Newest first (id sorts chronologically because of the fixed timestamp format).
        list.Sort((a, b) => string.CompareOrdinal(b.TimestampId, a.TimestampId));
        return list;
    }

    public void Prune(string firCode, int keep)
    {
        if (keep < 1) keep = 1;
        var all = List(firCode);
        foreach (var old in all.Skip(keep))
        {
            try
            {
                Directory.Delete(old.Directory, recursive: true);
                _log.LogInformation("Pruned old backup {Fir}/{Id}", firCode, old.TimestampId);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Failed to prune backup {Dir}", old.Directory);
            }
        }
    }

    internal static void CopyDirectory(string source, string dest)
    {
        Directory.CreateDirectory(dest);
        foreach (var dir in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(dest, Path.GetRelativePath(source, dir)));
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(dest, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }
}
