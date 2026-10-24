using StockOrder.SharedKernel.Results;

namespace StockOrder.Modules.Inventory.Contracts;

/// <summary>
/// Lets other modules create a warehouse inside the current request's transaction (section
/// 5.4) — Tenancy uses it so a new branch and its first warehouse are created atomically.
/// </summary>
public interface IWarehouseService
{
    /// <summary>
    /// Creates the warehouse with its standard MAIN, TRANSIT and QUARANTINE locations (section 11).
    /// Fails with DUPLICATE if the code is taken. Does not check the caller's branch access.
    /// </summary>
    Task<Result<Guid>> CreateWarehouseAsync(Guid branchId, string code, string name, CancellationToken ct);
}
