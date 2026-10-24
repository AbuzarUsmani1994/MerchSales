using Mediator;
using Microsoft.EntityFrameworkCore;
using StockOrder.Modules.Identity.Infrastructure;
using StockOrder.Modules.Tenancy.Contracts;
using StockOrder.SharedKernel.Context;
using StockOrder.SharedKernel.Errors;
using StockOrder.SharedKernel.Results;

namespace StockOrder.Modules.Identity.Application.Auth;

public sealed record MeQuery : IQuery<Result<MeResult>>;

/// <param name="Permissions">Every permission the user holds at any branch.</param>
/// <param name="TenantWidePermissions">The subset held through an all-branches role — what tenant-wide screens need.</param>
/// <param name="Branches">The branches the user holds a role at (every branch for an all-branches role).</param>
public sealed record MeResult(
    Guid UserId,
    string Username,
    string DisplayName,
    string? Email,
    bool MustChangePassword,
    bool MfaEnabled,
    IReadOnlyList<string> Permissions,
    IReadOnlyList<string> TenantWidePermissions,
    IReadOnlyList<string> Features,
    IReadOnlyList<BranchSummary> Branches);

public sealed class MeQueryHandler(
    IdentityDbContext db,
    ICurrentUserContext currentUser,
    IAccessScope accessScope,
    IFeatureQueries featureQueries,
    IBranchQueries branchQueries)
    : IQueryHandler<MeQuery, Result<MeResult>>
{
    public async ValueTask<Result<MeResult>> Handle(MeQuery query, CancellationToken ct)
    {
        var user = await db.Users
            .Where(u => u.TenantId == currentUser.TenantId && u.Id == currentUser.UserId)
            .Select(u => new { u.Username, u.DisplayName, u.Email, u.MustChangePassword, u.MfaEnabled })
            .FirstOrDefaultAsync(ct);

        if (user is null)
        {
            return Result.Failure<MeResult>(Error.NotFound("User"));
        }

        var grants = await accessScope.GetGrantsAsync(ct);
        var permissions = grants.Select(g => g.Code).Distinct().Order(StringComparer.Ordinal).ToList();
        var tenantWidePermissions = grants.Where(g => g.BranchId is null).Select(g => g.Code).Distinct().Order(StringComparer.Ordinal).ToList();

        var features = await featureQueries.GetEnabledFeatureKeysAsync(currentUser.TenantId, ct);
        var allowedBranches = BranchScope.For(grants, null);
        var branches = (await branchQueries.GetBranchesAsync(currentUser.TenantId, ct))
            .Where(b => allowedBranches.Covers(b.Id))
            .ToList();

        return Result.Success(new MeResult(
            currentUser.UserId, user.Username, user.DisplayName, user.Email,
            user.MustChangePassword, user.MfaEnabled, permissions, tenantWidePermissions, features, branches));
    }
}
