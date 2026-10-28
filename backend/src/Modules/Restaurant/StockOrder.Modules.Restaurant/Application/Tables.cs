using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
using StockOrder.Modules.Restaurant.Contracts;
using StockOrder.Modules.Restaurant.Domain;
using StockOrder.Modules.Restaurant.Infrastructure;
using StockOrder.Modules.Sales.Contracts;
using StockOrder.Modules.Tenancy.Contracts;
using StockOrder.SharedKernel.Context;
using StockOrder.SharedKernel.Errors;
using StockOrder.SharedKernel.Ids;
using StockOrder.SharedKernel.Results;
using StockOrder.SharedKernel.Time;

namespace StockOrder.Modules.Restaurant.Application;

public static class RestaurantPermissions
{
    public const string TablesManage = "Restaurant.Tables.Manage";
    public const string KitchenOperate = "Restaurant.Kitchen.Operate";
    public const string KitchenServe = "Restaurant.Kitchen.Serve";

    /// <summary>The table map is for whoever takes orders (waiters).</summary>
    public const string OrderCreate = "Sales.Order.Create";
}

public sealed record DiningTableView(Guid Id, Guid AreaId, string Name, int Seats, int SortOrder, bool IsActive);

public sealed record DiningAreaView(Guid Id, string Name, int SortOrder, bool IsActive, IReadOnlyList<DiningTableView> Tables);

/// <summary>A branch's areas with their tables, for setting them up (inactive ones included).</summary>
public sealed record ListDiningAreasQuery(Guid BranchId) : IQuery<Result<IReadOnlyList<DiningAreaView>>>;

public sealed class ListDiningAreasQueryHandler(RestaurantDbContext db, ICurrentUserContext currentUser, IAccessScope accessScope)
    : IQueryHandler<ListDiningAreasQuery, Result<IReadOnlyList<DiningAreaView>>>
{
    public async ValueTask<Result<IReadOnlyList<DiningAreaView>>> Handle(ListDiningAreasQuery query, CancellationToken ct)
    {
        if (!(await accessScope.GetBranchScopeAsync(RestaurantPermissions.TablesManage, ct)).Covers(query.BranchId))
        {
            return Result.Failure<IReadOnlyList<DiningAreaView>>(Error.NotFound("Branch"));
        }

        return Result.Success(await TableQueries.AreasAsync(db, currentUser.TenantId, query.BranchId, activeOnly: false, ct));
    }
}

internal static class TableQueries
{
    public static async Task<IReadOnlyList<DiningAreaView>> AreasAsync(
        RestaurantDbContext db, Guid tenantId, Guid branchId, bool activeOnly, CancellationToken ct)
    {
        var areas = await db.DiningAreas.AsNoTracking()
            .Where(a => a.TenantId == tenantId && a.BranchId == branchId && (!activeOnly || a.IsActive))
            .ToListAsync(ct);
        var tables = await db.DiningTables.AsNoTracking()
            .Where(t => t.TenantId == tenantId && t.BranchId == branchId && (!activeOnly || t.IsActive))
            .ToListAsync(ct);

        return areas.OrderBy(a => a.SortOrder).ThenBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .Select(a => new DiningAreaView(a.Id, a.Name, a.SortOrder, a.IsActive, tables
                .Where(t => t.AreaId == a.Id)
                .OrderBy(t => t.SortOrder).ThenBy(t => t.Name, NaturalComparer.Instance)
                .Select(t => new DiningTableView(t.Id, t.AreaId, t.Name, t.Seats, t.SortOrder, t.IsActive))
                .ToList()))
            .ToList();
    }
}

/// <summary>Sorts "2" before "10" and "T2" before "T10", the way staff count tables.</summary>
internal sealed class NaturalComparer : IComparer<string>
{
    public static readonly NaturalComparer Instance = new();

    public int Compare(string? x, string? y)
    {
        var (px, nx) = Split(x ?? string.Empty);
        var (py, ny) = Split(y ?? string.Empty);
        var byPrefix = string.Compare(px, py, StringComparison.OrdinalIgnoreCase);
        if (byPrefix != 0)
        {
            return byPrefix;
        }

        var byNumber = nx.CompareTo(ny);
        return byNumber != 0 ? byNumber : string.Compare(x, y, StringComparison.OrdinalIgnoreCase);
    }

    private static (string Prefix, long Number) Split(string value)
    {
        var digits = value.Length;
        while (digits > 0 && char.IsAsciiDigit(value[digits - 1]))
        {
            digits--;
        }

        return digits < value.Length && long.TryParse(value[digits..], out var n) && value.Length - digits <= 9
            ? (value[..digits], n)
            : (value, -1);
    }
}

public sealed record SaveDiningAreaCommand(Guid? Id, Guid BranchId, string Name, int SortOrder, bool IsActive) : ICommand<Result<Guid>>;

public sealed class SaveDiningAreaCommandValidator : AbstractValidator<SaveDiningAreaCommand>
{
    public SaveDiningAreaCommandValidator()
    {
        RuleFor(c => c.BranchId).NotEmpty();
        RuleFor(c => c.Name).NotEmpty().MaximumLength(60);
        RuleFor(c => c.SortOrder).InclusiveBetween(0, 10_000);
    }
}

/// <summary>Adds or changes an area. Areas are never deleted (old orders point at their tables): switch one off instead.</summary>
public sealed class SaveDiningAreaCommandHandler(
    RestaurantDbContext db, ICurrentUserContext currentUser, IAccessScope accessScope, IBranchQueries branches, IClock clock, IIdGenerator ids)
    : ICommandHandler<SaveDiningAreaCommand, Result<Guid>>
{
    public async ValueTask<Result<Guid>> Handle(SaveDiningAreaCommand command, CancellationToken ct)
    {
        var tenantId = currentUser.TenantId;
        if (!(await accessScope.GetBranchScopeAsync(RestaurantPermissions.TablesManage, ct)).Covers(command.BranchId) ||
            await branches.GetBranchAsync(tenantId, command.BranchId, ct) is null)
        {
            return Result.Failure<Guid>(Error.NotFound("Branch"));
        }

        var name = command.Name.Trim();
        if (await db.DiningAreas.AnyAsync(a => a.TenantId == tenantId && a.BranchId == command.BranchId && a.Name == name && a.Id != command.Id, ct))
        {
            return Result.Failure<Guid>(Error.Conflict(ErrorCodes.Duplicate, $"This branch already has an area called '{name}'."));
        }

        var now = clock.UtcNow;
        if (command.Id is not { } id)
        {
            var area = DiningArea.Create(tenantId, ids.NewId(), command.BranchId, name, command.SortOrder, now, currentUser.UserId);
            db.DiningAreas.Add(area);
            await db.SaveChangesAsync(ct);
            return Result.Success(area.Id);
        }

        var existing = await db.DiningAreas.FirstOrDefaultAsync(a => a.TenantId == tenantId && a.Id == id && a.BranchId == command.BranchId, ct);
        if (existing is null)
        {
            return Result.Failure<Guid>(Error.NotFound("Area"));
        }

        existing.Update(name, command.SortOrder, command.IsActive, now, currentUser.UserId);
        await db.SaveChangesAsync(ct);
        return Result.Success(existing.Id);
    }
}

public sealed record SaveDiningTableCommand(Guid? Id, Guid BranchId, Guid AreaId, string Name, int Seats, int SortOrder, bool IsActive)
    : ICommand<Result<Guid>>;

public sealed class SaveDiningTableCommandValidator : AbstractValidator<SaveDiningTableCommand>
{
    public SaveDiningTableCommandValidator()
    {
        RuleFor(c => c.BranchId).NotEmpty();
        RuleFor(c => c.AreaId).NotEmpty();
        RuleFor(c => c.Name).NotEmpty().MaximumLength(20);
        RuleFor(c => c.Seats).InclusiveBetween(1, 100);
        RuleFor(c => c.SortOrder).InclusiveBetween(0, 10_000);
    }
}

/// <summary>
/// Adds or changes a table. A table with an open order can't be switched off (the guests are
/// still there); it can be renamed or moved to another area.
/// </summary>
public sealed class SaveDiningTableCommandHandler(
    RestaurantDbContext db, ICurrentUserContext currentUser, IAccessScope accessScope, IOpenTableOrders openOrders, IClock clock, IIdGenerator ids)
    : ICommandHandler<SaveDiningTableCommand, Result<Guid>>
{
    public async ValueTask<Result<Guid>> Handle(SaveDiningTableCommand command, CancellationToken ct)
    {
        var tenantId = currentUser.TenantId;
        if (!(await accessScope.GetBranchScopeAsync(RestaurantPermissions.TablesManage, ct)).Covers(command.BranchId))
        {
            return Result.Failure<Guid>(Error.NotFound("Branch"));
        }

        if (!await db.DiningAreas.AnyAsync(a => a.TenantId == tenantId && a.Id == command.AreaId && a.BranchId == command.BranchId, ct))
        {
            return Result.Failure<Guid>(Error.NotFound("Area"));
        }

        var name = command.Name.Trim();
        if (await db.DiningTables.AnyAsync(t => t.TenantId == tenantId && t.BranchId == command.BranchId && t.Name == name && t.Id != command.Id, ct))
        {
            return Result.Failure<Guid>(Error.Conflict(ErrorCodes.Duplicate, $"This branch already has a table called '{name}'."));
        }

        var now = clock.UtcNow;
        if (command.Id is not { } id)
        {
            var table = DiningTable.Create(tenantId, ids.NewId(), command.BranchId, command.AreaId, name, command.Seats, command.SortOrder, now, currentUser.UserId);
            db.DiningTables.Add(table);
            await db.SaveChangesAsync(ct);
            return Result.Success(table.Id);
        }

        var existing = await db.DiningTables.FirstOrDefaultAsync(t => t.TenantId == tenantId && t.Id == id && t.BranchId == command.BranchId, ct);
        if (existing is null)
        {
            return Result.Failure<Guid>(Error.NotFound("Table"));
        }

        if (existing.IsActive && !command.IsActive && (await openOrders.ListAsync(command.BranchId, ct)).Any(o => o.TableId == id))
        {
            return Result.Failure<Guid>(Error.Validation($"Table {existing.Name} has an open order. Take payment for it first."));
        }

        existing.Update(command.AreaId, name, command.Seats, command.SortOrder, command.IsActive, now, currentUser.UserId);
        await db.SaveChangesAsync(ct);
        return Result.Success(existing.Id);
    }
}

public static class TableStatus
{
    public const string Free = "Free";
    public const string Occupied = "Occupied";
    public const string BillRequested = "BillRequested";
}

/// <param name="ReadyRounds">Rounds the kitchen marked ready that nobody has served yet: food is waiting.</param>
public sealed record TableOrderSummary(
    Guid OrderId, int? OrderNo, string? DocumentNo, int? Covers, string? ServerName, DateTime OpenedAtUtc, decimal GrandTotal,
    string Etag, int ReadyRounds, int InKitchenRounds);

public sealed record TableMapTable(Guid Id, string Name, int Seats, string Status, TableOrderSummary? Order);

public sealed record TableMapArea(Guid Id, string Name, IReadOnlyList<TableMapTable> Tables);

/// <summary>
/// The waiter's table map: every table of the branch, free or with its open order, the bill
/// request, and whether the kitchen has food ready for it.
/// </summary>
public sealed record GetTableMapQuery(Guid BranchId) : IQuery<Result<IReadOnlyList<TableMapArea>>>;

public sealed class GetTableMapQueryHandler(
    RestaurantDbContext db, ICurrentUserContext currentUser, IAccessScope accessScope, IFeatureQueries features,
    IOpenTableOrders openOrders, IKitchen kitchen)
    : IQueryHandler<GetTableMapQuery, Result<IReadOnlyList<TableMapArea>>>
{
    public async ValueTask<Result<IReadOnlyList<TableMapArea>>> Handle(GetTableMapQuery query, CancellationToken ct)
    {
        var tenantId = currentUser.TenantId;
        if (!(await accessScope.GetBranchScopeAsync(RestaurantPermissions.OrderCreate, ct)).Covers(query.BranchId))
        {
            return Result.Failure<IReadOnlyList<TableMapArea>>(Error.NotFound("Branch"));
        }

        if (!(await features.GetEnabledFeatureKeysAsync(tenantId, ct)).Contains(FeatureKeys.RestaurantTables))
        {
            return Result.Success<IReadOnlyList<TableMapArea>>([]);
        }

        var areas = await TableQueries.AreasAsync(db, tenantId, query.BranchId, activeOnly: true, ct);
        var orders = (await openOrders.ListAsync(query.BranchId, ct)).ToDictionary(o => o.TableId);
        var progress = await kitchen.GetProgressAsync(orders.Values.Select(o => o.OrderId).ToList(), ct);

        return Result.Success<IReadOnlyList<TableMapArea>>(areas.Select(a => new TableMapArea(a.Id, a.Name, a.Tables.Select(t =>
        {
            if (!orders.TryGetValue(t.Id, out var o))
            {
                return new TableMapTable(t.Id, t.Name, t.Seats, TableStatus.Free, null);
            }

            var p = progress.GetValueOrDefault(o.OrderId) ?? new KitchenProgress(0, 0, 0, 0);
            return new TableMapTable(t.Id, t.Name, t.Seats, o.BillRequested ? TableStatus.BillRequested : TableStatus.Occupied,
                new TableOrderSummary(o.OrderId, o.OrderNo, o.DocumentNo, o.Covers, o.ServerName, o.OpenedAtUtc, o.GrandTotal, o.Etag,
                    p.Ready, p.Queued + p.Preparing));
        }).ToList())).Where(a => a.Tables.Count > 0).ToList());
    }
}
