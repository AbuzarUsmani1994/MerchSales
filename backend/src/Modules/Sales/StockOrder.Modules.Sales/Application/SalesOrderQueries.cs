using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
using StockOrder.Infrastructure.Outbox;
using StockOrder.Modules.Restaurant.Contracts;
using StockOrder.Modules.Sales.Domain;
using StockOrder.Modules.Sales.Infrastructure;
using StockOrder.SharedKernel.Context;
using StockOrder.SharedKernel.Errors;
using StockOrder.SharedKernel.Results;
using StockOrder.SharedKernel.Time;

namespace StockOrder.Modules.Sales.Application;

public sealed record PagedList<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);

/// <summary>Orders the caller may see (Sales.View branches), newest first. The cashier queue asks for status=Confirmed.</summary>
/// <param name="Statuses">Empty = all statuses.</param>
/// <param name="Search">Matches the order number or the note ("Table 4").</param>
public sealed record ListSalesOrdersQuery(Guid? BranchId, IReadOnlyList<string> Statuses, string? Search, int Page, int PageSize)
    : IQuery<Result<PagedList<SalesOrderView>>>;

public sealed class ListSalesOrdersQueryHandler(SalesDbContext db, ICurrentUserContext currentUser, IAccessScope accessScope, SalesOrderViewBuilder views)
    : IQueryHandler<ListSalesOrdersQuery, Result<PagedList<SalesOrderView>>>
{
    public const int MaxPageSize = 200;

    public async ValueTask<Result<PagedList<SalesOrderView>>> Handle(ListSalesOrdersQuery query, CancellationToken ct)
    {
        var tenantId = currentUser.TenantId;
        var scope = BranchScope.For(await accessScope.GetGrantsAsync(ct), SalesPermissions.View);
        if (query.BranchId is { } requested && !scope.Covers(requested))
        {
            return Result.Failure<PagedList<SalesOrderView>>(Error.PermissionDenied("You can only see orders of branches you work at."));
        }

        var branchIds = scope.BranchIds.ToList();
        var orders = db.SalesOrders.AsNoTracking().Where(o => o.TenantId == tenantId)
            .Where(o => scope.AllBranches || branchIds.Contains(o.BranchId));
        if (query.BranchId is { } branchId)
        {
            orders = orders.Where(o => o.BranchId == branchId);
        }

        if (query.Statuses.Count > 0)
        {
            var statuses = query.Statuses.ToList();
            orders = orders.Where(o => statuses.Contains(o.Status));
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            // "#23" or "23" finds the restaurant's short order number too.
            var shortNo = int.TryParse(term.TrimStart('#'), out var n) ? n : (int?)null;
            orders = orders.Where(o => (o.DocumentNo != null && o.DocumentNo.Contains(term)) || (o.Notes != null && o.Notes.Contains(term))
                || (shortNo != null && o.OrderNo == shortNo));
        }

        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, MaxPageSize);
        var total = await orders.CountAsync(ct);
        var rows = await orders.OrderByDescending(o => o.OrderDate)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Include(o => o.Lines)
            .ToListAsync(ct);

        return Result.Success(new PagedList<SalesOrderView>(await views.BuildAsync(tenantId, rows, ct), page, pageSize, total));
    }
}

public sealed record GetSalesOrderQuery(Guid OrderId) : IQuery<Result<SalesOrderView>>;

public sealed class GetSalesOrderQueryHandler(SalesDbContext db, ICurrentUserContext currentUser, IAccessScope accessScope, SalesOrderViewBuilder views)
    : IQueryHandler<GetSalesOrderQuery, Result<SalesOrderView>>
{
    public async ValueTask<Result<SalesOrderView>> Handle(GetSalesOrderQuery query, CancellationToken ct)
    {
        var branchId = await db.SalesOrders.Where(o => o.TenantId == currentUser.TenantId && o.Id == query.OrderId)
            .Select(o => (Guid?)o.BranchId).FirstOrDefaultAsync(ct);
        if (branchId is null || !BranchScope.For(await accessScope.GetGrantsAsync(ct), SalesPermissions.View).Covers(branchId))
        {
            return Result.Failure<SalesOrderView>(Error.NotFound("Order"));
        }

        return Result.Success(await views.BuildAsync(currentUser.TenantId, query.OrderId, ct));
    }
}

/// <summary>Cancels an order before dispatch, with a reason (section 8.6). Nothing is reserved or paid yet, so nothing to release or refund.</summary>
/// <param name="ExpectedETag">If-Match from the client; null skips the check (only for sync commands).</param>
public sealed record CancelSalesOrderCommand(Guid OrderId, string? ExpectedETag, string Reason) : ICommand<Result<SalesOrderView>>;

public sealed class CancelSalesOrderCommandValidator : AbstractValidator<CancelSalesOrderCommand>
{
    public CancelSalesOrderCommandValidator()
    {
        RuleFor(c => c.OrderId).NotEmpty();
        RuleFor(c => c.Reason).NotEmpty().WithMessage("Say why the order is cancelled.").MaximumLength(500);
    }
}

public sealed class CancelSalesOrderCommandHandler(
    SalesDbContext db,
    ICurrentUserContext currentUser,
    IAccessScope accessScope,
    IOutboxWriter outbox,
    SalesOrderViewBuilder views,
    IKitchen kitchen,
    IClock clock)
    : ICommandHandler<CancelSalesOrderCommand, Result<SalesOrderView>>
{
    public async ValueTask<Result<SalesOrderView>> Handle(CancelSalesOrderCommand command, CancellationToken ct)
    {
        var grants = await accessScope.GetGrantsAsync(ct);
        var order = await db.SalesOrders.Include(o => o.Lines).FirstOrDefaultAsync(o => o.TenantId == currentUser.TenantId && o.Id == command.OrderId, ct);
        if (order is null || !BranchScope.For(grants, SalesPermissions.View).Covers(order.BranchId))
        {
            return Result.Failure<SalesOrderView>(Error.NotFound("Order"));
        }

        if (!BranchScope.For(grants, SalesPermissions.OrderCancel).Covers(order.BranchId))
        {
            return Result.Failure<SalesOrderView>(Error.PermissionDenied("You can't cancel orders at this branch."));
        }

        if (!ETags.Matches(command.ExpectedETag, order.RowVer))
        {
            return Result.Failure<SalesOrderView>(new Error(ErrorCodes.ConcurrencyConflict, "This order changed since you opened it. Reload and try again."));
        }

        if (order.MoveTo(SalesOrderStatus.Cancelled, currentUser.UserId, clock.UtcNow, command.Reason.Trim()) is { } error)
        {
            return Result.Failure<SalesOrderView>(error);
        }

        await db.SaveChangesAsync(ct);
        if (order.OrderNo is not null)
        {
            await kitchen.CancelOrderAsync(order.Id, order.BranchId, ct);
        }

        await SalesEvents.WriteAsync(outbox, order, SalesOrderStatus.Cancelled, ct);
        return Result.Success(await views.BuildAsync(currentUser.TenantId, order.Id, ct));
    }
}
