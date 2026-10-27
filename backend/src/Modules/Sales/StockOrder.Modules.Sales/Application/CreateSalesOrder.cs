using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
using StockOrder.Infrastructure.NumberSeries;
using StockOrder.Infrastructure.Outbox;
using StockOrder.Modules.Catalog.Contracts;
using StockOrder.Modules.Inventory.Contracts;
using StockOrder.Modules.Sales.Domain;
using StockOrder.Modules.Sales.Infrastructure;
using StockOrder.Modules.Tenancy.Contracts;
using StockOrder.SharedKernel.Context;
using StockOrder.SharedKernel.Errors;
using StockOrder.SharedKernel.Ids;
using StockOrder.SharedKernel.Numbers;
using StockOrder.SharedKernel.Results;
using StockOrder.SharedKernel.Time;

namespace StockOrder.Modules.Sales.Application;

public sealed record CreateSalesOrderLine(Guid ProductId, decimal Quantity, string? Notes);

/// <summary>
/// Takes an order for the walk-in customer at a branch: Draft → Submitted → Confirmed in one go
/// (section 8.6), priced by the server. Used by the REST endpoint and by the phone's
/// "CreateSalesOrder" sync command.
/// </summary>
/// <param name="ClientDocumentId">The phone's id for the order; a second create with the same id returns the first order.</param>
/// <param name="Notes">Who the order is for, e.g. "Table 4" or "Ali, takeaway".</param>
/// <param name="CustomerPhone">A visit-reward member's phone, noted for the cashier. A number that
/// isn't a usable phone is dropped rather than refusing the order (it may come from a phone offline).</param>
public sealed record CreateSalesOrderCommand(
    Guid BranchId,
    string Channel,
    string? Notes,
    IReadOnlyList<CreateSalesOrderLine> Lines,
    Guid? ClientDocumentId = null,
    Guid? SourceDeviceId = null,
    string? DeviceDocumentNo = null,
    string? CustomerPhone = null) : ICommand<Result<SalesOrderView>>;

public sealed class CreateSalesOrderCommandValidator : AbstractValidator<CreateSalesOrderCommand>
{
    public CreateSalesOrderCommandValidator()
    {
        RuleFor(c => c.BranchId).NotEmpty();
        RuleFor(c => c.Channel).Must(SalesChannels.Supported.Contains)
            .WithMessage($"Channel must be one of: {string.Join(", ", SalesChannels.Supported)}.");
        RuleFor(c => c.Notes).MaximumLength(1000);
        RuleFor(c => c.DeviceDocumentNo).MaximumLength(40);
        RuleFor(c => c.Lines).NotEmpty().WithMessage("Add at least one item.")
            .Must(l => l.Count <= 200).WithMessage("An order can have at most 200 lines.");
        RuleForEach(c => c.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.ProductId).NotEmpty();
            line.RuleFor(l => l.Quantity).GreaterThan(0).LessThanOrEqualTo(100_000);
            line.RuleFor(l => l.Notes).MaximumLength(500);
        });
    }
}

public sealed class CreateSalesOrderCommandHandler(
    SalesDbContext db,
    ICurrentUserContext currentUser,
    IAccessScope accessScope,
    IBranchQueries branchQueries,
    ITenantQueries tenantQueries,
    IWarehouseQueries warehouseQueries,
    ISalesCatalog catalog,
    INumberSeriesService numberSeries,
    IOutboxWriter outbox,
    SalesOrderViewBuilder views,
    IClock clock,
    IIdGenerator idGenerator)
    : ICommandHandler<CreateSalesOrderCommand, Result<SalesOrderView>>
{
    /// <summary>
    /// Product types whose sale moves stock. Selling them needs stock on hand, which needs stock
    /// receiving (not built yet), so for now they are refused rather than sold from nothing.
    /// </summary>
    private static readonly HashSet<string> StockMovingBehaviours = ["Stocked", "Kit"];

    public async ValueTask<Result<SalesOrderView>> Handle(CreateSalesOrderCommand command, CancellationToken ct)
    {
        var tenantId = currentUser.TenantId;
        if (!BranchScope.For(await accessScope.GetGrantsAsync(ct), SalesPermissions.OrderCreate).Covers(command.BranchId))
        {
            return Fail(Error.PermissionDenied("You can only take orders at branches you work at."));
        }

        if (command.ClientDocumentId is { } clientId &&
            await db.SalesOrders.Where(o => o.TenantId == tenantId && o.ClientDocumentId == clientId).Select(o => (Guid?)o.Id).FirstOrDefaultAsync(ct) is { } existingId)
        {
            return Result.Success(await views.BuildAsync(tenantId, existingId, ct));
        }

        if (await branchQueries.GetBranchAsync(tenantId, command.BranchId, ct) is not { IsActive: true })
        {
            return Fail(Error.NotFound("Branch"));
        }

        if (await warehouseQueries.GetSellingWarehouseIdAsync(command.BranchId, ct) is not { } warehouseId)
        {
            return Fail(Error.Validation("This branch has no warehouse to sell from."));
        }

        var walkIn = await db.Customers.FirstOrDefaultAsync(c => c.TenantId == tenantId && c.IsWalkIn && c.IsActive, ct);
        if (walkIn is null)
        {
            return Fail(Error.Validation("The walk-in customer is missing. Ask the platform admin to run 'Add missing template data' for this company."));
        }

        var businessDate = clock.TodayIn(await tenantQueries.GetTimeZoneIdAsync(tenantId, ct));
        var quotes = await catalog.QuoteAsync(
            command.BranchId, walkIn.PriceListId, businessDate, command.Lines.Select(l => (l.ProductId, l.Quantity)).ToList(), ct);
        if (CheckLines(command.Lines, quotes) is { } lineError)
        {
            return Fail(lineError);
        }

        var now = clock.UtcNow;
        var order = SalesOrder.CreateDraft(
            tenantId, idGenerator.NewId(), command.Channel, walkIn.Id, command.BranchId, warehouseId, businessDate,
            command.Notes, command.ClientDocumentId, command.SourceDeviceId, command.DeviceDocumentNo, currentUser.UserId, now);
        order.NoteLoyaltyPhone(PhoneNumbers.Normalize(command.CustomerPhone));
        for (var i = 0; i < command.Lines.Count; i++)
        {
            var line = command.Lines[i];
            var quote = quotes[i];
            order.AddLine(
                idGenerator.NewId(), quote.ProductId, quote.UomId, line.Quantity, Rounding.QtyBase(line.Quantity * quote.FactorToBase),
                quote.UnitPrice!.Value, quote.TaxCodeId, quote.TaxRatePercent, line.Notes);
        }

        // Submit, then the automatic checks (section 8.6). Credit control only applies to customers
        // with a credit limit; walk-in sales have none, so the order confirms straight away.
        order.MoveTo(SalesOrderStatus.Submitted, currentUser.UserId, now);
        order.MoveTo(SalesOrderStatus.Confirmed, currentUser.UserId, now);
        order.AssignDocumentNo(await numberSeries.NextAsync(tenantId, SalesDocumentTypes.SalesOrder, command.BranchId, ct));

        db.SalesOrders.Add(order);
        await db.SaveChangesAsync(ct);
        await SalesEvents.WriteAsync(outbox, order, SalesOrderStatus.Submitted, ct);
        await SalesEvents.WriteAsync(outbox, order, SalesOrderStatus.Confirmed, ct);

        return Result.Success(await views.BuildAsync(tenantId, order.Id, ct));
    }

    private static Error? CheckLines(IReadOnlyList<CreateSalesOrderLine> lines, IReadOnlyList<SaleLineQuote> quotes)
    {
        var problems = new Dictionary<string, object?>();
        var onlyMissingPrices = true;
        for (var i = 0; i < lines.Count; i++)
        {
            var (problem, isMissingPrice) = CheckLine(lines[i], quotes[i]);
            if (problem is null)
            {
                continue;
            }

            problems[$"lines[{i}]"] = new[] { problem };
            onlyMissingPrices &= isMissingPrice;
        }

        if (problems.Count == 0)
        {
            return null;
        }

        // PRICE_NOT_FOUND (409) when a price is all that's missing; anything else is invalid input (400).
        var code = onlyMissingPrices ? ErrorCodes.PriceNotFound : ErrorCodes.ValidationFailed;
        var message = problems.Count == 1 ? ((string[])problems.Values.First()!)[0] : "Some items can't be ordered.";
        return new Error(code, message, new Dictionary<string, object?> { ["errors"] = problems });
    }

    private static (string? Problem, bool IsMissingPrice) CheckLine(CreateSalesOrderLine line, SaleLineQuote quote)
    {
        var problem = quote.Availability switch
        {
            SaleAvailability.NotFound => "This item doesn't exist.",
            SaleAvailability.Inactive => $"{quote.Name} is no longer on the menu.",
            SaleAvailability.NotSoldHere => $"{quote.Name} isn't sold at this branch.",
            SaleAvailability.Unavailable => $"{quote.Name} is unavailable at this branch right now.",
            _ when StockMovingBehaviours.Contains(quote.Behaviour) =>
                $"{quote.Name} is a stock-tracked item. Stock-tracked items can't be ordered until stock receiving is set up.",
            _ when decimal.Round(line.Quantity, quote.UomDecimalPlaces) != line.Quantity =>
                quote.UomDecimalPlaces == 0 ? $"{quote.Name} is sold in whole units." : $"{quote.Name} allows at most {quote.UomDecimalPlaces} decimals.",
            _ => null,
        };

        if (problem is not null)
        {
            return (problem, false);
        }

        return quote.UnitPrice is null ? ($"{quote.Name} has no price at this branch.", true) : (null, false);
    }

    private static Result<SalesOrderView> Fail(Error error) => Result.Failure<SalesOrderView>(error);
}

public static class SalesDocumentTypes
{
    public const string SalesOrder = "SalesOrder";
    public const string Invoice = "Invoice";
}

/// <summary>Section 8.6: every transition writes an outbox event, e.g. "SalesOrderConfirmed".</summary>
internal static class SalesEvents
{
    public static Task WriteAsync(IOutboxWriter outbox, SalesOrder order, string status, CancellationToken ct) =>
        outbox.AddAsync(order.TenantId, $"SalesOrder{status}", new
        {
            orderId = order.Id,
            order.DocumentNo,
            order.BranchId,
            status,
            order.GrandTotal,
        }, ct);
}
