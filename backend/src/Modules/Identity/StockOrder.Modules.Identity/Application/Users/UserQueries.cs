using Mediator;
using Microsoft.EntityFrameworkCore;
using StockOrder.Modules.Identity.Infrastructure;
using StockOrder.SharedKernel.Context;
using StockOrder.SharedKernel.Results;

namespace StockOrder.Modules.Identity.Application.Users;

public sealed record UserSummary(Guid Id, string Username, string DisplayName, string? Email, bool IsActive, bool MfaEnabled);

public sealed record GetUsersQuery : IQuery<Result<IReadOnlyList<UserSummary>>>;

public sealed class GetUsersQueryHandler(IdentityDbContext db, ICurrentUserContext currentUser)
    : IQueryHandler<GetUsersQuery, Result<IReadOnlyList<UserSummary>>>
{
    public async ValueTask<Result<IReadOnlyList<UserSummary>>> Handle(GetUsersQuery query, CancellationToken ct)
    {
        var users = await db.Users
            .Where(u => u.TenantId == currentUser.TenantId)
            .OrderBy(u => u.Username)
            .Select(u => new UserSummary(u.Id, u.Username, u.DisplayName, u.Email, u.IsActive, u.MfaEnabled))
            .ToListAsync(ct);

        return Result.Success<IReadOnlyList<UserSummary>>(users);
    }
}
