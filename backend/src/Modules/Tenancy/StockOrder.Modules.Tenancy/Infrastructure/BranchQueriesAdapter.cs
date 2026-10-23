using Microsoft.EntityFrameworkCore;
using StockOrder.Modules.Tenancy.Contracts;

namespace StockOrder.Modules.Tenancy.Infrastructure;

internal sealed class BranchQueriesAdapter(TenancyDbContext db) : IBranchQueries
{
    public async Task<IReadOnlyList<BranchSummary>> GetBranchesAsync(Guid tenantId, CancellationToken ct) =>
        await db.Branches
            .Where(b => b.TenantId == tenantId)
            .Select(b => new BranchSummary(b.Id, b.Code, b.Name, b.IsActive))
            .ToListAsync(ct);
}
