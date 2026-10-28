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
/// <param name="LoyaltyPhone">A visit-reward member's phone: the visit counts and the order becomes theirs.</param>
/// <param name="UseReward">Take the member's reward off this bill (it must be ready and the bill big enough).</param>
/// <param name="Payments">A split bill: several payments that together make the total (after any
/// reward). When given, <paramref name="PaymentMethodId"/>, <paramref name="AmountTendered"/> and
/// <paramref name="Reference"/> are ignored.</param>
public sealed record CompleteSalesOrderCommand(
    Guid OrderId, string? ExpectedETag, Guid PaymentMethodId, decimal? AmountTendered, string? Reference,
    string? LoyaltyPhone = null, bool UseReward = false, IReadOnlyList<PaymentPart>? Payments = null)
    : ICommand<Result<CompletedSale>>;

/// <param name="Tendered">Cash handed over for this part; at least <paramref name="Amount"/>. Ignored for card and wallet.</param>
public sealed record PaymentPart(Guid PaymentMethodId, decimal Amount, decimal? Tendered, string? Reference);

/// <param name="PaymentNo">The last payment's number (the receipt number).</param>
/// <param name="PaymentMethod">The method, or "Split" for a split bill.</param>
/// <param name="CashTendered">All the cash handed over, for the receipt's first print.</param>
public sealed record CompletedSale(
    SalesOrderView Order, string InvoiceNo, string? PaymentNo, string? PaymentMethod, decimal AmountPaid, decimal Change, LoyaltyOutcome? Loyalty,
    decimal? CashTendered = null);

public sealed class CompleteSalesOrderCommandValidator : AbstractValidator<CompleteSalesOrderCommand>
{
    public CompleteSalesOrderCommandValidator()
    {
        RuleFor(c => c.OrderId).NotEmpty();
        RuleFor(c => c.AmountTendered).GreaterThanOrEqualTo(0).PrecisionScale(19, 2, ignoreTrailingZeros: true);
        RuleFor(c => c.Reference).MaximumLength(100);
        RuleFor(c => c.LoyaltyPhone).MaximumLength(30);
        RuleFor(c => c.PaymentMethodId).NotEmpty().When(c => c.Payments is null);
        RuleFor(c => c.Payments).Must(p => p!.Count is >= 1 and <= 20).WithMessage("A bill can be split into 1 to 20 payments.")
            .When(c => c.Payments is not null);
        RuleForEach(c => c.Payments).ChildRules(part =>
        {
            part.RuleFor(p => p.PaymentMethodId).NotEmpty();
            part.RuleFor(p => p.Amount).GreaterThan(0).PrecisionScale(19, 2, ignoreTrailingZeros: true);
            part.RuleFor(p => p.Tendered).GreaterThanOrEqualTo(0).PrecisionScale(19, 2, ignoreTrailingZeros: true);
            part.RuleFor(p => p.Reference).MaximumLength(100);
        });
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
    LoyaltyCheckout loyalty,
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

        // Visit rewards: the reward (a discount) and the member's customer record go on before the invoice.
        var loyaltyCheck = await loyalty.PrepareAsync(order, command.LoyaltyPhone, command.UseReward, businessDate, ct);
        if (loyaltyCheck.IsFailure)
        {
            return Fail(loyaltyCheck.Error);
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

        // Payment: the whole total, allocated to the invoice, in one part or split. A free order
        // (total 0) has nothing to pay.
        string? paymentNo = null;
        string? methodName = null;
        var change = 0m;
        decimal? cashTendered = null;
        if (order.GrandTotal > 0)
        {
            var parts = command.Payments
                ?? [new PaymentPart(command.PaymentMethodId, order.GrandTotal, command.AmountTendered, command.Reference)];
            var partsTotal = parts.Sum(p => p.Amount);
            if (partsTotal != order.GrandTotal)
            {
                return Fail(Error.Validation($"The payments add up to {partsTotal:N2}, but the bill is {order.GrandTotal:N2}."));
            }

            foreach (var part in parts)
            {
                var received = await finance.ReceiveCustomerPaymentAsync(new ReceiveCustomerPayment(
                    order.CustomerId, order.BranchId, part.PaymentMethodId, part.Amount, businessDate, sessionId, part.Reference,
                    [new PaymentAllocationRequest(FinanceDocumentTypes.Invoice, invoice.Id, part.Amount)]), ct);
                if (received.IsFailure)
                {
                    return Fail(received.Error);
                }

                if (received.Value.MethodKind == "Cash")
                {
                    var tendered = part.Tendered ?? part.Amount;
                    if (tendered < part.Amount)
                    {
                        return Fail(Error.Validation($"The customer handed over {tendered:N2}, less than {part.Amount:N2}."));
                    }

                    change += Rounding.Money(tendered - part.Amount);
                    cashTendered = (cashTendered ?? 0m) + tendered;
                }

                paymentNo = received.Value.DocumentNo;
                methodName = parts.Count == 1 ? received.Value.MethodName : "Split";
                invoice.RecordPayment(part.Amount);
                order.RecordPayment(part.Amount);
            }
        }

        order.MoveTo(SalesOrderStatus.Paid, userId, now);
        order.MoveTo(SalesOrderStatus.Closed, userId, now);
        var loyaltyOutcome = loyaltyCheck.Value is { } prepared ? loyalty.Record(prepared, order, businessDate) : null;
        await db.SaveChangesAsync(ct);

        foreach (var status in new[] { SalesOrderStatus.Dispatched, SalesOrderStatus.Delivered, SalesOrderStatus.Invoiced, SalesOrderStatus.Paid, SalesOrderStatus.Closed })
        {
            await SalesEvents.WriteAsync(outbox, order, status, ct);
        }

        return Result.Success(new CompletedSale(
            await views.BuildAsync(tenantId, order.Id, ct), invoiceNo, paymentNo, methodName, order.PaidAmount, change, loyaltyOutcome, cashTendered));
    }

    private static Result<CompletedSale> Fail(Error error) => Result.Failure<CompletedSale>(error);
}
