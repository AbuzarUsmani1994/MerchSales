using Mediator;
using Microsoft.EntityFrameworkCore;
using StockOrder.Modules.Tenancy.Contracts;
using StockOrder.Modules.Tenancy.Infrastructure;
using StockOrder.SharedKernel.Context;
using StockOrder.SharedKernel.Results;

namespace StockOrder.Modules.Tenancy.Application.Branches;

/// <summary>Branches the caller's <c>Branches.Manage</c> role covers: all of them, or only their own.</summary>
public sealed record GetBranchesQuery : IQuery<Result<IReadOnlyList<BranchSummary>>>;

public sealed class GetBranchesQueryHandler(TenancyDbContext db, ICurrentUserContext currentUser, IAccessScope accessScope)
    : IQueryHandler<GetBranchesQuery, Result<IReadOnlyList<BranchSummary>>>
{
    public async ValueTask<Result<IReadOnlyList<BranchSummary>>> Handle(GetBranchesQuery query, CancellationToken ct)
    {
        var scope = await accessScope.GetBranchScopeAsync("Branches.Manage", ct);
        var branchIds = scope.BranchIds.ToList();

        var branches = await db.Branches
            .Where(b => b.TenantId == currentUser.TenantId)
            .Where(b => scope.AllBranches || branchIds.Contains(b.Id))
            .OrderBy(b => b.Code)
            .Select(b => new BranchSummary(b.Id, b.Code, b.Name, b.Address, b.Phone, b.IsActive))
            .ToListAsync(ct);

        return Result.Success<IReadOnlyList<BranchSummary>>(branches);
    }
}
