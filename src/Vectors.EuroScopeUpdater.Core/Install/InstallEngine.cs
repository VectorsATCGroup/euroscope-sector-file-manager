using Microsoft.Extensions.Logging;
using Vectors.EuroScopeUpdater.Core.Abstractions;
using Vectors.EuroScopeUpdater.Core.Backup;
using Vectors.EuroScopeUpdater.Core.Manifest;
using Vectors.EuroScopeUpdater.Core.Operations;
using Vectors.EuroScopeUpdater.Core.Packaging;
using Vectors.EuroScopeUpdater.Core.Safety;
using Vectors.EuroScopeUpdater.Core.Settings;
using Vectors.EuroScopeUpdater.Core.Time;

namespace Vectors.EuroScopeUpdater.Core.Install;

public interface IInstallEngine
{
    Task<InstallResult> RunAsync(InstallRequest request, IProgress<OperationProgress>? progress,
        CancellationToken ct = default);
}

/// <summary>
/// Runs Clean Install and Update as a transactional pipeline:
/// <c>Prepare → Download → ValidateArchive → Stage → ValidateStaging → Backup → Commit → Verify → Complete</c>,
/// with rollback if anything fails after commit begins. The live FIR folder is never mutated in place:
/// the final content is assembled in a work directory on the same volume and swapped in with directory
/// moves, so a failure before the swap leaves the installation untouched.
/// </summary>
public sealed class InstallEngine : IInstallEngine
{
    // Temp work folder created as a sibling of the FIR folders (same volume ⇒ atomic moves).
    public const string WorkFolderName = ".vatupd-tmp";

    private readonly ISectorPackageSource _source;
    private readonly IArchiveExtractor _extractor;
    private readonly IBackupManager _backup;
    private readonly IManifestService _manifest;
    private readonly IOperationJournal _journal;
    private readonly ISettingsService _settings;
    private readonly IClock _clock;
    private readonly ILogger<InstallEngine> _log;

    public InstallEngine(ISectorPackageSource source, IArchiveExtractor extractor, IBackupManager backup,
        IManifestService manifest, IOperationJournal journal, ISettingsService settings, IClock clock,
        ILogger<InstallEngine> log)
    {
        _source = source;
        _extractor = extractor;
        _backup = backup;
        _manifest = manifest;
        _journal = journal;
        _settings = settings;
        _clock = clock;
        _log = log;
    }

    public async Task<InstallResult> RunAsync(InstallRequest request, IProgress<OperationProgress>? progress,
        CancellationToken ct = default)
    {
        var now = _clock.UtcNow;
        var id = $"{request.Fir.Code}-{now:yyyyMMddHHmmss}-{Guid.NewGuid():N}".Substring(0, 40);
        var workRoot = Path.Combine(request.SectorFilesRoot, WorkFolderName, id);
        var state = new OperationState
        {
            Id = id,
            Fir = request.Fir.Code,
            Kind = request.Kind.ToString(),
            Phase = nameof(OperationPhase.Prepare),
            FirDirectory = request.FirDirectory,
            WorkRoot = workRoot,
            StartedAtUtc = now.ToString("O"),
        };

        void Report(OperationPhase phase, string message, double? frac = null, long? recv = null, long? total = null)
        {
            state.Phase = phase.ToString();
            _journal.Save(state, _clock.UtcNow);
            _log.LogInformation("[{Fir}] {Phase}: {Message}", request.Fir.Code, phase, message);
            progress?.Report(new OperationProgress(phase, message, frac, recv, total));
        }

        try
        {
            // ── PREPARE ─────────────────────────────────────────────────────────────────
            Report(OperationPhase.Prepare, $"Preparando {request.Fir.Code}…");
            Directory.CreateDirectory(workRoot);
            EnsureDiskSpace(request);

            var archivePath = Path.Combine(workRoot, request.Package.FileName);

            // ── DOWNLOAD ────────────────────────────────────────────────────────────────
            Report(OperationPhase.Download, "Baixando pacote…"); // indeterminate until byte progress arrives
            var dlProgress = new Progress<DownloadProgress>(p =>
                progress?.Report(new OperationProgress(OperationPhase.Download, "Baixando pacote…",
                    p.Fraction, p.BytesReceived, p.TotalBytes)));
            await _source.DownloadAsync(request.Package, archivePath, dlProgress, ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();

            // ── VALIDATE ARCHIVE ────────────────────────────────────────────────────────
            Report(OperationPhase.ValidateArchive, "Validando arquivo…");
            ValidateArchive(archivePath, request);

            // ── STAGE (extract) ─────────────────────────────────────────────────────────
            Report(OperationPhase.Stage, "Extraindo arquivos…");
            var extractedDir = Path.Combine(workRoot, "extracted");
            _extractor.ExtractAll(archivePath, extractedDir, ct);

            // ── VALIDATE STAGING ────────────────────────────────────────────────────────
            Report(OperationPhase.ValidateStaging, "Validando arquivos extraídos…");
            var newSct = ValidateStaging(extractedDir, request);

            // ── BACKUP ──────────────────────────────────────────────────────────────────
            Report(OperationPhase.Backup, "Fazendo backup da instalação atual…");
            var backup = _backup.CreateBackup(request.Fir.Code, request.FirDirectory, _clock.UtcNow);
            state.BackupDir = backup?.Directory;

            // ── BUILD FINAL (same volume, off to the side) ──────────────────────────────
            var finalDir = Path.Combine(workRoot, "final");
            BuildFinal(request, extractedDir, finalDir, newSct);

            // ── COMMIT (atomic swap) ─────────────────────────────────────────────────────
            Report(OperationPhase.Commit, "Instalando…");
            state.CommitStarted = true;
            _journal.Save(state, _clock.UtcNow);
            var previousDir = Path.Combine(workRoot, "previous");
            if (Directory.Exists(request.FirDirectory))
            {
                Directory.Move(request.FirDirectory, previousDir);
                state.PreviousDir = previousDir;
                _journal.Save(state, _clock.UtcNow);
            }
            Directory.CreateDirectory(Path.GetDirectoryName(request.FirDirectory)!);
            Directory.Move(finalDir, request.FirDirectory);
            state.CommitCompleted = true;
            _journal.Save(state, _clock.UtcNow);

            // ── VERIFY ──────────────────────────────────────────────────────────────────
            Report(OperationPhase.Verify, "Validando…");
            VerifyInstalled(request.FirDirectory, newSct);

            // Manifest for reliable future status.
            var manifest = _manifest.Build(request.FirDirectory, request.Package.Name, newSct, _clock.UtcNow, ct);
            _manifest.Write(manifest);

            // ── COMPLETE ────────────────────────────────────────────────────────────────
            _backup.Prune(request.Fir.Code, _settings.Current.BackupsToKeep);
            _journal.Clear(id);
            Report(OperationPhase.Complete,
                $"{request.Fir.Code} {(request.Kind == OperationKind.Update ? "atualizado" : "instalado")} com sucesso.");
            return InstallResult.Ok($"{request.Fir.Code} pronto (AIRAC {request.Package.Airac}).");
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "[{Fir}] operation failed", request.Fir.Code);
            return Rollback(request, state, ex, progress);
        }
        finally
        {
            TryCleanup(workRoot);
        }
    }

    // ───────────────────────────── pipeline steps ────────────────────────────────────────

    private static void EnsureDiskSpace(InstallRequest request)
    {
        var size = request.Package.SizeBytes ?? 0;
        if (size <= 0) return; // unknown size ⇒ skip the check
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(request.SectorFilesRoot));
            if (root is null) return;
            var drive = new DriveInfo(root);
            // Need room for the archive + extracted copy + a full FIR copy for the swap.
            var required = size * 3;
            if (drive.AvailableFreeSpace < required)
                throw new IOException(
                    $"Espaço em disco insuficiente em {root}: são necessários ~{required / 1_000_000} MB livres.");
        }
        catch (ArgumentException) { /* non-standard root, skip */ }
    }

    private void ValidateArchive(string archivePath, InstallRequest request)
    {
        var entries = _extractor.List(archivePath);
        if (entries.Count == 0)
            throw new InvalidArchiveException("O pacote baixado está vazio.");

        foreach (var e in entries)
            if (!ArchiveSafety.IsSafe(request.SectorFilesRoot, e.RelativePath))
                throw new UnsafeArchiveEntryException(e.RelativePath);

        // Sanity: a package must contain a versioned .sct for the requested FIR.
        var hasSct = entries.Any(e =>
        {
            var name = Path.GetFileName(e.RelativePath);
            return PackageName.TryParseSectorFile(name, out var sf)
                   && sf.Extension == "sct"
                   && sf.Fir.Equals(request.Fir.Code, StringComparison.OrdinalIgnoreCase);
        });
        if (!hasSct)
            throw new InvalidArchiveException(
                $"O pacote não contém um arquivo de setor (.sct) para {request.Fir.Code}.");
    }

    /// <summary>Validate the extracted tree and return the new .sct file name.</summary>
    private static string ValidateStaging(string extractedDir, InstallRequest request)
    {
        if (!Directory.Exists(extractedDir))
            throw new InvalidArchiveException("A extração não gerou nenhum arquivo.");

        var sct = SectorFiles.CurrentSctFileName(extractedDir)
                  ?? throw new InvalidArchiveException("Nenhum arquivo .sct encontrado no pacote extraído.");

        // Install packages must additionally carry the nested FIR data folder.
        if (request.Kind == OperationKind.CleanInstall)
        {
            var nested = Path.Combine(extractedDir, request.Fir.Code);
            if (!Directory.Exists(nested))
                throw new InvalidArchiveException(
                    $"O pacote de instalação não contém a pasta de dados '{request.Fir.Code}'.");
        }
        return sct;
    }

    /// <summary>
    /// Assemble the final FIR content in <paramref name="finalDir"/> without touching the live folder.
    /// Clean install: the extracted content is the final content. Update: start from a copy of the
    /// current install, overlay the update files, drop stale versioned sector files, re-point profiles.
    /// </summary>
    private void BuildFinal(InstallRequest request, string extractedDir, string finalDir, string newSct)
    {
        if (request.Kind == OperationKind.CleanInstall)
        {
            Directory.Move(extractedDir, finalDir); // same volume rename
            return;
        }

        // Update requires an existing installation.
        if (!Directory.Exists(request.FirDirectory))
            throw new InvalidOperationException(
                $"{request.Fir.Code} não está instalado. Faça uma Instalação Limpa primeiro.");

        BackupManager.CopyDirectory(request.FirDirectory, finalDir);  // start from current install
        OverlayFiles(extractedDir, finalDir);                          // apply exactly the update's files
        DropStaleSectorFiles(finalDir, newSct);                        // remove previous AIRAC's .sct/.ese/.rwy
        ProfileRepointer.RepointAll(finalDir, newSct);                 // point profiles at the new .sct
    }

    private static void OverlayFiles(string source, string dest)
    {
        foreach (var (rel, full) in FileHashing.EnumerateFiles(source))
        {
            var target = Path.Combine(dest, rel.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(full, target, overwrite: true);
        }
    }

    /// <summary>Remove versioned sector files whose base name differs from the new .sct's base.</summary>
    private static void DropStaleSectorFiles(string firDir, string newSct)
    {
        var keepBase = Path.GetFileNameWithoutExtension(newSct);
        foreach (var f in SectorFiles.FindVersioned(firDir))
            if (!Path.GetFileNameWithoutExtension(f).Equals(keepBase, StringComparison.OrdinalIgnoreCase))
                File.Delete(f);
    }

    private static void VerifyInstalled(string firDir, string expectedSct)
    {
        var sctPath = Path.Combine(firDir, Path.GetFileName(expectedSct));
        if (!File.Exists(sctPath))
            throw new InvalidOperationException("A verificação pós-instalação falhou: arquivo de setor ausente.");

        // Every profile's sector reference must point at an existing .sct in the folder.
        foreach (var prf in Directory.EnumerateFiles(firDir, "*.prf", SearchOption.TopDirectoryOnly))
        {
            var reference = ProfileRepointer.ReadSectorReference(prf);
            if (reference is null) continue;
            var referenced = Path.Combine(firDir, Path.GetFileName(reference.Replace('\\', '/')));
            if (!File.Exists(referenced))
                throw new InvalidOperationException(
                    $"A verificação pós-instalação falhou: {Path.GetFileName(prf)} aponta para um arquivo de setor inexistente.");
        }
    }

    // ───────────────────────────── rollback / cleanup ─────────────────────────────────────

    private InstallResult Rollback(InstallRequest request, OperationState state, Exception error,
        IProgress<OperationProgress>? progress)
    {
        // If the swap never began, the live folder was never touched — nothing to restore.
        if (!state.CommitStarted)
        {
            _journal.Clear(state.Id);
            return new InstallResult(false, OperationPhase.Failed, Describe(error), RolledBack: false, Error: error);
        }

        progress?.Report(new OperationProgress(OperationPhase.RollingBack, "Revertendo…"));
        _log.LogWarning("[{Fir}] rolling back", request.Fir.Code);
        try
        {
            if (Directory.Exists(request.FirDirectory))
                Directory.Delete(request.FirDirectory, recursive: true);

            if (state.PreviousDir is { } prev && Directory.Exists(prev))
                Directory.Move(prev, request.FirDirectory);            // restore the pre-commit folder
            else if (state.BackupDir is { } backupDir && Directory.Exists(backupDir))
                BackupManager.CopyDirectory(backupDir, request.FirDirectory); // fall back to the backup
            // else: there was no prior install (fresh clean-install failure) — leaving it removed is correct.

            _journal.Clear(state.Id);
            progress?.Report(new OperationProgress(OperationPhase.RolledBack, "Revertido para o estado anterior."));
            return new InstallResult(false, OperationPhase.RolledBack,
                $"A operação falhou e foi revertida: {Describe(error)}", RolledBack: true, Error: error);
        }
        catch (Exception rbEx)
        {
            _log.LogError(rbEx, "[{Fir}] ROLLBACK FAILED", request.Fir.Code);
            return new InstallResult(false, OperationPhase.Failed,
                $"A operação falhou E a reversão falhou. Há um backup na pasta de dados do aplicativo. ({Describe(error)})",
                RolledBack: false, RollbackFailed: true, Error: error);
        }
    }

    private void TryCleanup(string workRoot)
    {
        try
        {
            if (Directory.Exists(workRoot)) Directory.Delete(workRoot, recursive: true);
            // Remove the parent .vatupd-tmp folder if now empty.
            var parent = Path.GetDirectoryName(workRoot);
            if (parent is not null && Directory.Exists(parent) && !Directory.EnumerateFileSystemEntries(parent).Any())
                Directory.Delete(parent);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to clean up work directory {Dir}", workRoot);
        }
    }

    private static string Describe(Exception ex) => ex switch
    {
        AeroNavAuthRequiredException => "sua sessão do AeroNav expirou.",
        PackageSourceUnavailableException => "não foi possível acessar o AeroNav.",
        InvalidArchiveException e => e.Message,
        UnsafeArchiveEntryException => "o pacote continha um caminho de arquivo inseguro e foi rejeitado.",
        IOException e => e.Message,
        _ => ex.Message,
    };
}
