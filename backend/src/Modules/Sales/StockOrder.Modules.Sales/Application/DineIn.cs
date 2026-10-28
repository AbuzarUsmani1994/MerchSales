using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
using StockOrder.Infrastructure.Outbox;
using StockOrder.Modules.Catalog.Contracts;
using StockOrder.Modules.Restaurant.Contracts;
using StockOrder.Modules.Sales.Contracts;
using StockOrder.Modules.Sales.Domain;
using StockOrder.Modules.Sales.Infrastructure;
using StockOrder.Modules.Identity.Contracts;
using StockOrder.Modules.Tenancy.Contracts;
using StockOrder.SharedKernel.Context;
using StockOrder.SharedKernel.Errors;
using StockOrder.SharedKernel.Ids;
using StockOrder.SharedKernel.Numbers;
using StockOrder.SharedKernel.Results;
using StockOrder.SharedKernel.Time;

namespace StockOrder.Modules.Sales.Application;

/// <summary>
/// The guests at a table order more (docs/decisions.md "Tables and kitchen screen"): the lines
/// join the open order, priced now, and go to the kitchen as the order's next round. Allowed while
/// the order is Confirmed, i.e. before the cashier takes payment (section 8.6 amendments).
/// </summary>
/// <param name="ExpectedETag">If-Match from the client (section 9).</param>
/// <param name="Notes">For the kitchen, on this round's ticket only ("no onions on the second karahi").</param>
public sealed record AddOrderRoundCommand(Guid OrderId, string? ExpectedETag, IReadOnlyList<CreateSalesOrderLine> Lines, string? Notes)
    : ICommand<Result<SalesOrderView>>;

public sealed class AddOrderRoundCommandValidator : AbstractValidator<AddOrderRoundCommand>
{
    public AddOrderRoundCommandValidator()
    {
        RuleFor(c => c.OrderId).NotEmpty();
        RuleFor(c => c.Notes).MaximumLength(1000);
        RuleFor(c => c.Lines).NotEmpty().WithMessage("Add at least one item.")
            .Must(l => l.Count <= 100).WithMessage("A round can have at most 100 lines.");
        RuleForEach(c => c.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.ProductId).NotEmpty();
            line.RuleFor(l => l.Quantity).GreaterThan(0).LessThanOrEqualTo(100_000);
            line.RuleFor(l => l.Notes).MaximumLength(500);
        });
    }
}

public sealed class AddOrderRoundCommandHandler(
    SalesDbContext db,
    ICurrentUserContext currentUser,
    IAccessScope accessScope,
    ISalesCatalog catalog,
    ITenantQueries tenantQueries,
    RestaurantOrders restaurant,
    IDiningTables tables,
    IOutboxWriter outbox,
    SalesOrderViewBuilder views,
    IClock clock,
    IIdGenerator idGenerator)
    : ICommandHandler<AddOrderRoundCommand, Result<SalesOrderView>>
{
    /// <summary>An order can't grow without end: past this many lines, start a new one.</summary>
    private const int MaxLines = 200;

    public async ValueTask<Result<SalesOrderView>> Handle(AddOrderRoundCommand command, CancellationToken ct)
    {
        var tenantId = currentUser.TenantId;
        var grants = await accessScope.GetGrantsAsync(ct);
        var order = await db.SalesOrders.Include(o => o.Lines).FirstOrDefaultAsync(o => o.TenantId == tenantId && o.Id == command.OrderId, ct);
        if (order is null || !BranchScope.For(grants, SalesPermissions.View).Covers(order.BranchId))
        {
            return Fail(Error.NotFound("Order"));
        }

        if (!BranchScope.For(grants, SalesPermissions.OrderCreate).Covers(order.BranchId))
        {
            return Fail(Error.PermissionDenied("You can only take orders at branches you work at."));
        }

        if (!ETags.Matches(command.ExpectedETag, order.RowVer))
        {
            return Fail(new Error(ErrorCodes.ConcurrencyConflict, "This order changed since you opened it. Reload and try again."));
        }

        if (order.Lines.Count + command.Lines.Count > MaxLines)
        {
            return Fail(Error.Validation($"An order can have at most {MaxLines} lines. Ask the cashier to close this one and start a new order."));
        }

        var businessDate = clock.TodayIn(await tenantQueries.GetTimeZoneIdAsync(tenantId, ct));
        var quotes = await catalog.QuoteAsync(
            order.BranchId, null, businessDate, command.Lines.Select(l => (l.ProductId, l.Quantity)).ToList(), ct);
        if (CreateSalesOrderCommandHandler.CheckLines(command.Lines, quotes) is { } lineError)
        {
            return Fail(lineError);
        }

        var now = clock.UtcNow;
        if (order.StartAmendment(currentUser.UserId, now) is { } stateError)
        {
            return Fail(stateError);
        }

        var added = new List<(SalesOrderLine, SaleLineQuote)>();
        for (var i = 0; i < command.Lines.Count; i++)
        {
            var line = command.Lines[i];
            var quote = quotes[i];
            added.Add((order.AddLine(
                idGenerator.NewId(), quote.ProductId, quote.UomId, line.Quantity, Rounding.QtyBase(line.Quantity * quote.FactorToBase),
                quote.UnitPrice!.Value, quote.TaxCodeId, quote.TaxRatePercent, line.Notes), quote));
        }

        // The ids are set in code, so EF would take lines reached through the tracked order for
        // existing rows: add them explicitly.
        foreach (var (line, _) in added)
        {
            db.Entry(line).State = EntityState.Added;
        }

        await db.SaveChangesAsync(ct);

        var features = await restaurant.GetFeaturesAsync(tenantId, ct);
        if (features.Kitchen)
        {
            var tableName = order.TableId is { } tableId ? (await tables.GetAsync(tableId, ct))?.Name : null;
            // The kitchen keeps a later round on the same trading day as the order's first ticket.
            await restaurant.SendRoundAsync(order, order.BusinessDate, added, tableName, command.Notes, ct);
        }

        await outbox.AddAsync(tenantId, "SalesOrderAmended", new { orderId = order.Id, order.DocumentNo, order.BranchId, order.Version, order.GrandTotal }, ct);
        return Result.Success(await views.BuildAsync(tenantId, order.Id, ct));
    }

    private static Result<SalesOrderView> Fail(Error error) => Result.Failure<SalesOrderView>(error);
}

/// <summary>The waiter tells the cashier the table wants its bill; the table shows "Bill asked".</summary>
public sealed record RequestBillCommand(Guid OrderId, string? ExpectedETag) : ICommand<Result<SalesOrderView>>;

public sealed class RequestBillCommandHandler(
    SalesDbContext db,
    ICurrentUserContext currentUser,
    IAccessScope accessScope,
    SalesOrderViewBuilder views,
    IClock clock)
    : ICommandHandler<RequestBillCommand, Result<SalesOrderView>>
{
    public async ValueTask<Result<SalesOrderView>> Handle(RequestBillCommand command, CancellationToken ct)
    {
        var grants = await accessScope.GetGrantsAsync(ct);
        var order = await db.SalesOrders.FirstOrDefaultAsync(o => o.TenantId == currentUser.TenantId && o.Id == command.OrderId, ct);
        if (order is null || !BranchScope.For(grants, SalesPermissions.View).Covers(order.BranchId))
        {
            return Result.Failure<SalesOrderView>(Error.NotFound("Order"));
        }

        if (!BranchScope.For(grants, SalesPermissions.OrderCreate).Covers(order.BranchId))
        {
            return Result.Failure<SalesOrderView>(Error.PermissionDenied("You can only work on orders at branches you work at."));
        }

        if (!ETags.Matches(command.ExpectedETag, order.RowVer))
        {
            return Result.Failure<SalesOrderView>(new Error(ErrorCodes.ConcurrencyConflict, "This order changed since you opened it. Reload and try again."));
        }

        if (order.RequestBill(currentUser.UserId, clock.UtcNow) is { } error)
        {
            return Result.Failure<SalesOrderView>(error);
        }

        await db.SaveChangesAsync(ct);
        return Result.Success(await views.BuildAsync(currentUser.TenantId, order.Id, ct));
    }
}

/// <summary>Sales' side of the table map: the open orders at a branch's tables.</summary>
internal sealed class OpenTableOrders(SalesDbContext db, ICurrentUserContext currentUser, IUserDirectory users) : IOpenTableOrders
{
    public async Task<IReadOnlyList<OpenTableOrder>> ListAsync(Guid branchId, CancellationToken ct)
    {
        var rows = await db.SalesOrders.AsNoTracking()
            .Where(o => o.TenantId == currentUser.TenantId && o.BranchId == branchId && o.OpenTableId != null)
            .Select(o => new
            {
                o.Id, TableId = o.OpenTableId!.Value, o.OrderNo, o.DocumentNo, o.Covers, o.ServerUserId, o.SalesRepUserId,
                o.CreatedAtUtc, o.GrandTotal, o.BillRequestedAtUtc, o.RowVer,
            })
            .ToListAsync(ct);
        var names = await users.GetDisplayNamesAsync(
            rows.Select(r => r.ServerUserId ?? r.SalesRepUserId).Where(id => id is not null).Select(id => id!.Value).Distinct().ToList(), ct);

        return rows.Select(r => new OpenTableOrder(
            r.Id, r.TableId, r.OrderNo, r.DocumentNo, r.Covers,
            (r.ServerUserId ?? r.SalesRepUserId) is { } server ? names.GetValueOrDefault(server) : null,
            r.CreatedAtUtc, r.GrandTotal, r.BillRequestedAtUtc is not null, ETags.Format(r.RowVer))).ToList();
    }
}
