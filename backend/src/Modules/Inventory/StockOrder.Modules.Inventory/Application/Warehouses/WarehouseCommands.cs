using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
using StockOrder.Modules.Inventory.Contracts;
using StockOrder.Modules.Inventory.Infrastructure;
using StockOrder.Modules.Tenancy.Contracts;
using StockOrder.SharedKernel.Context;
using StockOrder.SharedKernel.Errors;
using StockOrder.SharedKernel.Results;

namespace StockOrder.Modules.Inventory.Application.Warehouses;

public sealed record LocationSummary(Guid Id, string Code, string Name, string LocationType, bool IsSellable, bool IsActive);

public sealed record WarehouseSummary(
    Guid Id, Guid BranchId, string Code, string Name, bool IsActive, IReadOnlyList<LocationSummary> Locations);

/// <summary>Warehouses (with their locations) at the branches the caller's <c>Branches.Manage</c> role covers.</summary>
public sealed record GetWarehousesQuery(Guid? BranchId) : IQuery<Result<IReadOnlyList<WarehouseSummary>>>;

public sealed class GetWarehousesQueryHandler(InventoryDbContext db, ICurrentUserContext currentUser, IAccessScope accessScope)
    : IQueryHandler<GetWarehousesQuery, Result<IReadOnlyList<WarehouseSummary>>>
{
    public async ValueTask<Result<IReadOnlyList<WarehouseSummary>>> Handle(GetWarehousesQuery query, CancellationToken ct)
    {
        var scope = await accessScope.GetBranchScopeAsync(WarehousePermissions.Manage, ct);
        var branchIds = scope.BranchIds.ToList();

        var warehouses = await db.Warehouses
            .Where(w => w.TenantId == currentUser.TenantId)
            .Where(w => scope.AllBranches || branchIds.Contains(w.BranchId))
            .Where(w => query.BranchId == null || w.BranchId == query.BranchId)
            .OrderBy(w => w.Code)
            .Select(w => new { w.Id, w.BranchId, w.Code, w.Name, w.IsActive })
            .ToListAsync(ct);

        var warehouseIds = warehouses.Select(w => w.Id).ToList();
        var locations = (await db.Locations
                .Where(l => l.TenantId == currentUser.TenantId && warehouseIds.Contains(l.WarehouseId))
                .OrderBy(l => l.Code)
                .Select(l => new { l.WarehouseId, Summary = new LocationSummary(l.Id, l.Code, l.Name, l.LocationType, l.IsSellable, l.IsActive) })
                .ToListAsync(ct))
            .ToLookup(l => l.WarehouseId, l => l.Summary);

        var result = warehouses
            .Select(w => new WarehouseSummary(w.Id, w.BranchId, w.Code, w.Name, w.IsActive, locations[w.Id].ToList()))
            .ToList();

        return Result.Success<IReadOnlyList<WarehouseSummary>>(result);
    }
}

public sealed record CreateWarehouseCommand(Guid BranchId, string Code, string Name) : ICommand<Result<Guid>>;

public sealed class CreateWarehouseCommandValidator : AbstractValidator<CreateWarehouseCommand>
{
    public CreateWarehouseCommandValidator()
    {
        RuleFor(x => x.BranchId).NotEmpty();
        RuleFor(x => x.Code).NotEmpty().MaximumLength(32);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(150);
    }
}

public sealed class CreateWarehouseCommandHandler(
    IWarehouseService warehouseService,
    IBranchQueries branchQueries,
    ICurrentUserContext currentUser,
    IAccessScope accessScope)
    : ICommandHandler<CreateWarehouseCommand, Result<Guid>>
{
    public async ValueTask<Result<Guid>> Handle(CreateWarehouseCommand command, CancellationToken ct)
    {
        var branch = await branchQueries.GetBranchAsync(currentUser.TenantId, command.BranchId, ct);
        if (branch is null || !branch.IsActive)
        {
            return Result.Failure<Guid>(Error.NotFound("Branch"));
        }

        var scope = await accessScope.GetBranchScopeAsync(WarehousePermissions.Manage, ct);
        if (!scope.Covers(command.BranchId))
        {
            return Result.Failure<Guid>(Error.PermissionDenied("You can only add warehouses to branches you manage."));
        }

        return await warehouseService.CreateWarehouseAsync(command.BranchId, command.Code, command.Name, ct);
    }
}

/// <summary>011_SeedReferenceData.sql: "Branches.Manage — Branches and warehouses".</summary>
internal static class WarehousePermissions
{
    public const string Manage = "Branches.Manage";
}
