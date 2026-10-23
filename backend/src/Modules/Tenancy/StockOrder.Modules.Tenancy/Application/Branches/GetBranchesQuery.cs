using Mediator;
using Microsoft.EntityFrameworkCore;
using StockOrder.Modules.Tenancy.Contracts;
using StockOrder.Modules.Tenancy.Infrastructure;
using StockOrder.SharedKernel.Context;
using StockOrder.SharedKernel.Results;

namespace StockOrder.Modules.Tenancy.Application.Branches;

public sealed record GetBranchesQuery : IQuery<Result<IReadOnlyList<BranchSummary>>>;

public sealed class GetBranchesQueryHandler(TenancyDbContext db, ICurrentUserContext currentUser)
    : IQueryHandler<GetBranchesQuery, Result<IReadOnlyList<BranchSummary>>>
{
    public async ValueTask<Result<IReadOnlyList<BranchSummary>>> Handle(GetBranchesQuery query, CancellationToken ct)
    {
        var branches = await db.Branches
            .Where(b => b.TenantId == currentUser.TenantId)
            .OrderBy(b => b.Code)
            .Select(b => new BranchSummary(b.Id, b.Code, b.Name, b.IsActive))
            .ToListAsync(ct);

        return Result.Success<IReadOnlyList<BranchSummary>>(branches);
    }
}
