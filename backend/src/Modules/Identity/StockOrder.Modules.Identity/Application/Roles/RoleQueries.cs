using Mediator;
using Microsoft.EntityFrameworkCore;
using StockOrder.Modules.Identity.Infrastructure;
using StockOrder.SharedKernel.Context;
using StockOrder.SharedKernel.Results;

namespace StockOrder.Modules.Identity.Application.Roles;

public sealed record RoleSummary(Guid Id, string Name, string? Description, bool IsSystem, IReadOnlyList<string> PermissionCodes);

public sealed record GetRolesQuery : IQuery<Result<IReadOnlyList<RoleSummary>>>;

public sealed class GetRolesQueryHandler(IdentityDbContext db, ICurrentUserContext currentUser)
    : IQueryHandler<GetRolesQuery, Result<IReadOnlyList<RoleSummary>>>
{
    public async ValueTask<Result<IReadOnlyList<RoleSummary>>> Handle(GetRolesQuery query, CancellationToken ct)
    {
        var roles = await db.Roles
            .Where(r => r.TenantId == currentUser.TenantId)
            .OrderBy(r => r.Name)
            .Select(r => new
            {
                r.Id,
                r.Name,
                r.Description,
                r.IsSystem,
                Permissions = db.RolePermissions
                    .Where(rp => rp.TenantId == currentUser.TenantId && rp.RoleId == r.Id)
                    .Select(rp => rp.PermissionCode)
                    .ToList(),
            })
            .ToListAsync(ct);

        var result = roles
            .Select(r => new RoleSummary(r.Id, r.Name, r.Description, r.IsSystem, r.Permissions))
            .ToList();

        return Result.Success<IReadOnlyList<RoleSummary>>(result);
    }
}

public sealed record PermissionCatalogueEntry(string Code, string Module, string Description, bool IsPlatformOnly);

public sealed record GetPermissionCatalogueQuery : IQuery<Result<IReadOnlyList<PermissionCatalogueEntry>>>;

public sealed class GetPermissionCatalogueQueryHandler(IdentityDbContext db)
    : IQueryHandler<GetPermissionCatalogueQuery, Result<IReadOnlyList<PermissionCatalogueEntry>>>
{
    public async ValueTask<Result<IReadOnlyList<PermissionCatalogueEntry>>> Handle(GetPermissionCatalogueQuery query, CancellationToken ct)
    {
        var permissions = await db.Permissions
            .OrderBy(p => p.Module).ThenBy(p => p.Code)
            .Select(p => new PermissionCatalogueEntry(p.Code, p.Module, p.Description, p.IsPlatformOnly))
            .ToListAsync(ct);

        return Result.Success<IReadOnlyList<PermissionCatalogueEntry>>(permissions);
    }
}
