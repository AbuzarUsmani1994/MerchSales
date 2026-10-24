namespace StockOrder.Modules.Tenancy.Contracts;

/// <summary>Read-only port other modules use instead of referencing Tenancy's Infrastructure directly (section 5.1).</summary>
public interface IBranchQueries
{
    Task<IReadOnlyList<BranchSummary>> GetBranchesAsync(Guid tenantId, CancellationToken ct);

    Task<BranchSummary?> GetBranchAsync(Guid tenantId, Guid branchId, CancellationToken ct);
}

public sealed record BranchSummary(Guid Id, string Code, string Name, string? Address, string? Phone, bool IsActive);
