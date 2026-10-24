using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
using StockOrder.Infrastructure.NumberSeries;
using StockOrder.Infrastructure.Outbox;
using StockOrder.Modules.Catalog.Contracts;
using StockOrder.Modules.Finance.Contracts;
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

/// <summary>
/// The cashier takes payment for a confirmed order and hands it over. Like a counter sale
/// (section 8.6) this runs Dispatched → Delivered → Invoiced → Paid → Closed in one transaction,
/// writing every status-history row: the invoice debits the walk-in customer and the payment
/// credits them back.
/// </summary>
/// <param name="ExpectedETag">If-Match from the client; null skips the check (only for sync commands).</param>
/// <param name="AmountTendered">Cash handed over; at least the total. Ignored for card/wallet (exact amount).</param>
public sealed record CompleteSalesOrderCommand(
    Guid OrderId, string? ExpectedETag, Guid PaymentMethodId, decimal? AmountTendered, string? Reference)
    : ICommand<Result<CompletedSale>>;

public sealed record CompletedSale(SalesOrderView Order, string InvoiceNo, string? PaymentNo, string? PaymentMethod, decimal AmountPaid, decimal Change);

public sealed class CompleteSalesOrderCommandValidator : AbstractValidator<CompleteSalesOrderCommand>
{
    public CompleteSalesOrderCommandValidator()
    {
        RuleFor(c => c.OrderId).NotEmpty();
        RuleFor(c => c.PaymentMethodId).NotEmpty();
        RuleFor(c => c.AmountTendered).GreaterThanOrEqualTo(0).PrecisionScale(19, 2, ignoreTrailingZeros: true);
        RuleFor(c => c.Reference).MaximumLength(100);
    }
}

public sealed class CompleteSalesOrderCommandHandler(
    SalesDbContext db,
    ICurrentUserContext currentUser,
    IAccessScope accessScope,
    IFeatureQueries featureQueries,
    ITenantQueries tenantQueries,
    IFinancePosting finance,
    ISalesCatalog catalog,
    INumberSeriesService numberSeries,
    IOutboxWriter outbox,
    SalesOrderViewBuilder views,
    IClock clock,
    IIdGenerator idGenerator)
    : ICommandHandler<CompleteSalesOrderCommand, Result<CompletedSale>>
{
    public async ValueTask<Result<CompletedSale>> Handle(CompleteSalesOrderCommand command, CancellationToken ct)
    {
        var tenantId = currentUser.TenantId;
        var grants = await accessScope.GetGrantsAsync(ct);
        var order = await db.SalesOrders.Include(o => o.Lines).FirstOrDefaultAsync(o => o.TenantId == tenantId && o.Id == command.OrderId, ct);
        if (order is null || !BranchScope.For(grants, SalesPermissions.View).Covers(order.BranchId))
        {
            return Fail(Error.NotFound("Order"));
        }

        foreach (var permission in new[] { SalesPermissions.OrderFulfil, SalesPermissions.InvoicePost, SalesPermissions.PaymentReceive })
        {
            if (!BranchScope.For(grants, permission).Covers(order.BranchId))
            {
                return Fail(Error.PermissionDenied("You can't take payment for orders at this branch."));
            }
        }

        if (!ETags.Matches(command.ExpectedETag, order.RowVer))
        {
            return Fail(new Error(ErrorCodes.ConcurrencyConflict, "This order changed since you opened it. Reload and try again."));
        }

        if (order.Status is not (SalesOrderStatus.Confirmed or SalesOrderStatus.Fulfilling))
        {
            return Fail(Error.Conflict(ErrorCodes.InvalidStateTransition, $"Only confirmed orders can be paid; this one is {order.Status}."));
        }

        // Business date: the cashier's open shift when cash sessions are on (section 8.11), else today.
        Guid? sessionId = null;
        DateOnly businessDate;
        var features = await featureQueries.GetEnabledFeatureKeysAsync(tenantId, ct);
        if (features.Contains(FeatureKeys.FinanceCashSessions))
        {
            if (await finance.GetOpenCashSessionAsync(order.BranchId, currentUser.UserId, ct) is not { } session)
            {
                return Fail(Error.Validation("Open your cash session before taking payments."));
            }

            sessionId = session.Id;
            businessDate = session.BusinessDate;
            order.AttachCashSession(session.Id);
        }
        else
        {
            businessDate = clock.TodayIn(await tenantQueries.GetTimeZoneIdAsync(tenantId, ct));
        }

        var periodCheck = await finance.EnsurePeriodOpenAsync(businessDate, ct);
        if (periodCheck.IsFailure)
        {
            return Fail(periodCheck.Error);
        }

        var now = clock.UtcNow;
        var userId = currentUser.UserId;
        foreach (var status in new[] { SalesOrderStatus.Dispatched, SalesOrderStatus.Delivered })
        {
            if (order.MoveTo(status, userId, now) is { } error)
            {
                return Fail(error);
            }
        }

        // Invoice (gap-free number in this transaction, section 8.9) and the customer debit.
        var names = await catalog.GetProductNamesAsync(order.Lines.Select(l => l.ProductId).ToList(), ct);
        var invoiceNo = await numberSeries.NextAsync(tenantId, SalesDocumentTypes.Invoice, order.BranchId, ct);
        var invoice = Invoice.ForOrder(tenantId, idGenerator.NewId(), invoiceNo, order, businessDate, names, idGenerator.NewId, userId, now);
        db.Invoices.Add(invoice);
        order.MoveTo(SalesOrderStatus.Invoiced, userId, now);
        await db.SaveChangesAsync(ct);
        await finance.DebitCustomerAsync(
            new CustomerLedgerPosting(order.CustomerId, FinanceDocumentTypes.Invoice, invoice.Id, invoiceNo, invoice.GrandTotal, businessDate), ct);

        // Payment: the whole total, allocated to the invoice. A free order (total 0) has nothing to pay.
        string? paymentNo = null;
        string? methodName = null;
        var change = 0m;
        if (order.GrandTotal > 0)
        {
            var received = await finance.ReceiveCustomerPaymentAsync(new ReceiveCustomerPayment(
                order.CustomerId, order.BranchId, command.PaymentMethodId, order.GrandTotal, businessDate, sessionId, command.Reference,
                [new PaymentAllocationRequest(FinanceDocumentTypes.Invoice, invoice.Id, order.GrandTotal)]), ct);
            if (received.IsFailure)
            {
                return Fail(received.Error);
            }

            if (received.Value.MethodKind == "Cash")
            {
                var tendered = command.AmountTendered ?? order.GrandTotal;
                if (tendered < order.GrandTotal)
                {
                    return Fail(Error.Validation($"The customer handed over {tendered:N2}, less than the total of {order.GrandTotal:N2}."));
                }

                change = Rounding.Money(tendered - order.GrandTotal);
            }

            paymentNo = received.Value.DocumentNo;
            methodName = received.Value.MethodName;
            invoice.RecordPayment(order.GrandTotal);
            order.RecordPayment(order.GrandTotal);
        }

        order.MoveTo(SalesOrderStatus.Paid, userId, now);
        order.MoveTo(SalesOrderStatus.Closed, userId, now);
        await db.SaveChangesAsync(ct);

        foreach (var status in new[] { SalesOrderStatus.Dispatched, SalesOrderStatus.Delivered, SalesOrderStatus.Invoiced, SalesOrderStatus.Paid, SalesOrderStatus.Closed })
        {
            await SalesEvents.WriteAsync(outbox, order, status, ct);
        }

        return Result.Success(new CompletedSale(
            await views.BuildAsync(tenantId, order.Id, ct), invoiceNo, paymentNo, methodName, order.PaidAmount, change));
    }

    private static Result<CompletedSale> Fail(Error error) => Result.Failure<CompletedSale>(error);
}
