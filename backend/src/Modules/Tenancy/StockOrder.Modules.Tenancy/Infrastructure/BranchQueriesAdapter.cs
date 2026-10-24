using Microsoft.EntityFrameworkCore;
using StockOrder.Modules.Tenancy.Contracts;

namespace StockOrder.Modules.Tenancy.Infrastructure;

internal sealed class BranchQueriesAdapter(TenancyDbContext db) : IBranchQueries
{
    public async Task<IReadOnlyList<BranchSummary>> GetBranchesAsync(Guid tenantId, CancellationToken ct) =>
        await db.Branches
            .Where(b => b.TenantId == tenantId)
            .OrderBy(b => b.Code)
            .Select(b => new BranchSummary(b.Id, b.Code, b.Name, b.Address, b.Phone, b.IsActive))
            .ToListAsync(ct);

    public async Task<BranchSummary?> GetBranchAsync(Guid tenantId, Guid branchId, CancellationToken ct) =>
        await db.Branches
            .Where(b => b.TenantId == tenantId && b.Id == branchId)
            .Select(b => new BranchSummary(b.Id, b.Code, b.Name, b.Address, b.Phone, b.IsActive))
            .FirstOrDefaultAsync(ct);
}
