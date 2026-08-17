using Vectors.EuroScopeUpdater.Core.Paths;
using Vectors.EuroScopeUpdater.Core.Serialization;

namespace Vectors.EuroScopeUpdater.Core.Operations;

public interface IOperationJournal
{
    void Save(OperationState state, DateTime nowUtc);
    void Clear(string id);
    IReadOnlyList<OperationState> Pending();
}

/// <summary>Persists <see cref="OperationState"/> to <c>operations\&lt;id&gt;.json</c> for crash recovery.</summary>
public sealed class OperationJournal : IOperationJournal
{
    private readonly AppPaths _paths;
    public OperationJournal(AppPaths paths) => _paths = paths;

    private string File(string id) => Path.Combine(_paths.OperationsDir, id + ".json");

    public void Save(OperationState state, DateTime nowUtc)
    {
        _paths.EnsureCreated();
        state.UpdatedAtUtc = nowUtc.ToString("O");
        AppJson.WriteAtomic(File(state.Id), state);
    }

    public void Clear(string id)
    {
        var f = File(id);
        if (System.IO.File.Exists(f)) System.IO.File.Delete(f);
    }

    public IReadOnlyList<OperationState> Pending()
    {
        if (!Directory.Exists(_paths.OperationsDir)) return Array.Empty<OperationState>();
        var list = new List<OperationState>();
        foreach (var f in Directory.EnumerateFiles(_paths.OperationsDir, "*.json"))
        {
            var s = AppJson.Read<OperationState>(f);
            if (s is not null) list.Add(s);
        }
        return list;
    }
}
