using Microsoft.Extensions.Logging;
using Vectors.EuroScopeUpdater.Core.Abstractions;
using Vectors.EuroScopeUpdater.Core.Model;
using Vectors.EuroScopeUpdater.Core.Packaging;

namespace Vectors.EuroScopeUpdater.Infrastructure.Sources;

/// <summary>
/// An offline <see cref="ISectorPackageSource"/> that serves packages from a local folder of <c>.7z</c>
/// files whose names follow the AeroNav grammar. Enables full end-to-end use/testing (catalog →
/// download → install/update) without contacting AeroNav. Used for development, demos and tests.
/// </summary>
public sealed class FixturePackageSource : ISectorPackageSource
{
    private readonly string _root;
    private readonly ILogger<FixturePackageSource>? _log;

    public FixturePackageSource(string fixturesRoot, ILogger<FixturePackageSource>? log = null)
    {
        _root = fixturesRoot;
        _log = log;
    }

    public string DisplayName => "Fixtures (offline)";

    public Task<RemoteCatalog> GetCatalogAsync(Division division, CancellationToken ct = default)
    {
        if (!Directory.Exists(_root))
            throw new PackageSourceUnavailableException($"Fixtures folder not found: {_root}");

        var packages = new List<RemotePackage>();
        foreach (var file in Directory.EnumerateFiles(_root, "*.7z", SearchOption.AllDirectories))
        {
            var fileName = Path.GetFileName(file);
            if (!PackageName.TryParsePackage(fileName, out var name)) continue;
            packages.Add(new RemotePackage(
                FirCode: name.Fir,
                Type: name.Type,
                Airac: name.Airac,
                Name: name,
                FileName: fileName,
                DownloadRef: file,
                SizeBytes: new FileInfo(file).Length));
        }

        var airac = packages.Count > 0 ? new AiracCycle(packages.Max(p => p.Airac.Value)) : default;
        _log?.LogInformation("Fixture catalog: {Count} packages", packages.Count);
        return Task.FromResult(new RemoteCatalog(division.Id, airac, packages));
    }

    public async Task DownloadAsync(RemotePackage package, string destinationFile,
        IProgress<DownloadProgress>? progress, CancellationToken ct = default)
    {
        if (!File.Exists(package.DownloadRef))
            throw new PackageUnavailableException($"Fixture package not found: {package.FileName}");

        Directory.CreateDirectory(Path.GetDirectoryName(destinationFile)!);
        var total = new FileInfo(package.DownloadRef).Length;

        try
        {
            await using var input = File.OpenRead(package.DownloadRef);
            await using var output = File.Create(destinationFile);
            var buffer = new byte[81920];
            long copied = 0;
            int read;
            while ((read = await input.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                copied += read;
                progress?.Report(new DownloadProgress(copied, total));
            }
        }
        catch
        {
            TryDelete(destinationFile); // never leave a partial file behind
            throw;
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* ignore */ }
    }
}
