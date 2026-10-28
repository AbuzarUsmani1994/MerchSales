namespace StockOrder.Modules.Restaurant.Contracts;

/// <summary>The tables Sales seats orders at.</summary>
public interface IDiningTables
{
    Task<DiningTableInfo?> GetAsync(Guid tableId, CancellationToken ct);

    Task<IReadOnlyDictionary<Guid, string>> GetNamesAsync(IReadOnlyCollection<Guid> tableIds, CancellationToken ct);
}

public sealed record DiningTableInfo(Guid Id, Guid BranchId, string Name, bool IsActive);
