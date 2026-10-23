using Mediator;
using Microsoft.EntityFrameworkCore;
using StockOrder.Infrastructure.Caching;
using StockOrder.Modules.Identity.Infrastructure;
using StockOrder.Modules.Tenancy.Contracts;
using StockOrder.SharedKernel.Context;
using StockOrder.SharedKernel.Errors;
using StockOrder.SharedKernel.Results;

namespace StockOrder.Modules.Identity.Application.Auth;

public sealed record MeQuery : IQuery<Result<MeResult>>;

public sealed record MeResult(
    Guid UserId,
    string Username,
    string DisplayName,
    string? Email,
    bool MustChangePassword,
    bool MfaEnabled,
    IReadOnlyList<string> Permissions,
    IReadOnlyList<string> Features,
    IReadOnlyList<BranchSummary> Branches);

public sealed class MeQueryHandler(
    IdentityDbContext db,
    ICurrentUserContext currentUser,
    IPermissionCache permissionCache,
    IPermissionLoader permissionLoader,
    IFeatureQueries featureQueries,
    IBranchQueries branchQueries)
    : IQueryHandler<MeQuery, Result<MeResult>>
{
    public async ValueTask<Result<MeResult>> Handle(MeQuery query, CancellationToken ct)
    {
        var user = await db.Users
            .Where(u => u.TenantId == currentUser.TenantId && u.Id == currentUser.UserId)
            .Select(u => new { u.Username, u.DisplayName, u.Email, u.MustChangePassword, u.MfaEnabled, u.SecurityStamp })
            .FirstOrDefaultAsync(ct);

        if (user is null)
        {
            return Result.Failure<MeResult>(Error.NotFound("User"));
        }

        var permissions = await permissionCache.GetOrCreateAsync(
            currentUser.TenantId, currentUser.UserId, user.SecurityStamp,
            innerCt => new ValueTask<IReadOnlyList<string>>(permissionLoader.LoadAsync(currentUser.TenantId, currentUser.UserId, innerCt)),
            ct);

        var features = await featureQueries.GetEnabledFeatureKeysAsync(currentUser.TenantId, ct);
        var branches = await branchQueries.GetBranchesAsync(currentUser.TenantId, ct);

        return Result.Success(new MeResult(
            currentUser.UserId, user.Username, user.DisplayName, user.Email,
            user.MustChangePassword, user.MfaEnabled, permissions, features, branches));
    }
}
