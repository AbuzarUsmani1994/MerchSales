using Mediator;
using Microsoft.EntityFrameworkCore;
using StockOrder.Modules.Catalog.Contracts;
using StockOrder.Modules.Finance.Contracts;
using StockOrder.Modules.Identity.Contracts;
using StockOrder.Modules.Sales.Domain;
using StockOrder.Modules.Sales.Infrastructure;
using StockOrder.Modules.Tenancy.Contracts;
using StockOrder.SharedKernel.Context;
using StockOrder.SharedKernel.Errors;
using StockOrder.SharedKernel.Numbers;
using StockOrder.SharedKernel.Results;
using StockOrder.SharedKernel.Time;

namespace StockOrder.Modules.Sales.Application;

public sealed record ReceiptLine(decimal Quantity, string Name, decimal UnitPrice, decimal Amount);

public sealed record ReceiptPayment(string Method, decimal Amount, string? Reference);

/// <param name="Phone">Masked: only the last three digits show on paper.</param>
public sealed record ReceiptMember(string Name, string Phone, int Visits, int VisitsForReward, bool RewardReady);

/// <summary>Everything printed on a counter receipt (section 16: 80 mm, branch name and address in the header).</summary>
/// <param name="Tendered">Cash handed over, and <paramref name="Change"/>, only on the first print: they aren't stored.</param>
/// <param name="IsCopy">A reprint, marked "COPY" so it can't pass as a second sale.</param>
public sealed record ReceiptModel(
    string CompanyName,
    string BranchName,
    string? BranchAddress,
    string? BranchPhone,
    string? OrderNo,
    string InvoiceNo,
    string? ReceiptNo,
    DateTime PaidAtLocal,
    string? Cashier,
    string? Label,
    IReadOnlyList<ReceiptLine> Lines,
    decimal SubTotal,
    decimal Discount,
    string? DiscountLabel,
    decimal Total,
    IReadOnlyList<ReceiptPayment> Payments,
    decimal? Tendered,
    decimal? Change,
    ReceiptMember? Member,
    bool IsCopy);

/// <summary>The receipt of a paid order, as a PDF for the till's printer.</summary>
public sealed record GetReceiptQuery(Guid OrderId, decimal? Tendered, bool IsCopy) : IQuery<Result<byte[]>>;

public sealed class GetReceiptQueryHandler(
    SalesDbContext db,
    ICurrentUserContext currentUser,
    IAccessScope accessScope,
    ITenantQueries tenantQueries,
    IBranchQueries branches,
    IPaymentQueries payments,
    IUserDirectory users,
    ISalesCatalog catalog,
    LoyaltyStore loyalty,
    IClock clock)
    : IQueryHandler<GetReceiptQuery, Result<byte[]>>
{
    public async ValueTask<Result<byte[]>> Handle(GetReceiptQuery query, CancellationToken ct)
    {
        var tenantId = currentUser.TenantId;
        var order = await db.SalesOrders.AsNoTracking().Include(o => o.Lines)
            .FirstOrDefaultAsync(o => o.TenantId == tenantId && o.Id == query.OrderId, ct);
        if (order is null || !BranchScope.For(await accessScope.GetGrantsAsync(ct), SalesPermissions.View).Covers(order.BranchId))
        {
            return Result.Failure<byte[]>(Error.NotFound("Order"));
        }

        var invoice = await db.Invoices.AsNoTracking().FirstOrDefaultAsync(i => i.TenantId == tenantId && i.SalesOrderId == order.Id, ct);
        if (order.Status is not (SalesOrderStatus.Paid or SalesOrderStatus.Closed) || invoice is null)
        {
            return Result.Failure<byte[]>(Error.Conflict(ErrorCodes.InvalidStateTransition, "Only paid orders have a receipt."));
        }

        var paid = await payments.ForDocumentAsync(FinanceDocumentTypes.Invoice, invoice.Id, ct);
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(await tenantQueries.GetTimeZoneIdAsync(tenantId, ct));
        var paidAtUtc = paid.Count > 0 ? paid[^1].PostedAtUtc : order.DeliveredAtUtc ?? order.OrderDate;
        var cashierId = paid.Count > 0 ? paid[^1].PostedBy : null;
        var cashier = cashierId is { } id ? (await users.GetDisplayNamesAsync([id], ct)).GetValueOrDefault(id) : null;
        var branch = await branches.GetBranchAsync(tenantId, order.BranchId, ct);
        var names = await catalog.GetProductNamesAsync(order.Lines.Select(l => l.ProductId).ToList(), ct);

        var lines = order.Lines.Where(l => l.Status != SalesOrderLineStatus.Cancelled).OrderBy(l => l.LineNumber)
            .Select(l => new ReceiptLine(l.Quantity, names.GetValueOrDefault(l.ProductId, "Item"), l.UnitPrice, Rounding.Money(l.Quantity * l.UnitPrice)))
            .ToList();
        var rewardUsed = await db.LoyaltyVisits.AnyAsync(v => v.TenantId == tenantId && v.SalesOrderId == order.Id && v.Kind == LoyaltyVisitKind.RewardUsed, ct);

        // Cash handed over is known only right after payment; change is worked out here, never trusted from the client.
        var cash = paid.Where(p => p.MethodKind == "Cash").Sum(p => p.Amount);
        decimal? tendered = query.Tendered is { } t && cash > 0 && t >= cash ? t : null;
        decimal? change = tendered is { } handed ? Rounding.Money(handed - cash) : null;

        var model = new ReceiptModel(
            await tenantQueries.GetNameAsync(tenantId, ct),
            branch?.Name ?? string.Empty,
            branch?.Address,
            branch?.Phone,
            order.DocumentNo,
            invoice.DocumentNo,
            paid.Count > 0 ? paid[^1].DocumentNo : null,
            TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(paidAtUtc, DateTimeKind.Utc), timeZone),
            cashier,
            order.Notes,
            lines,
            order.SubTotal,
            order.DiscountTotal,
            order.DiscountTotal > 0 ? (rewardUsed ? "Visit reward" : "Discount") : null,
            order.GrandTotal,
            paid.Select(p => new ReceiptPayment(p.MethodName, p.Amount, p.Reference)).ToList(),
            tendered,
            change,
            await MemberAsync(order, timeZone, ct),
            query.IsCopy);

        return Result.Success(ReceiptPdf.Render(model));
    }

    private async Task<ReceiptMember?> MemberAsync(SalesOrder order, TimeZoneInfo timeZone, CancellationToken ct)
    {
        var member = await db.LoyaltyMembers.AsNoTracking().FirstOrDefaultAsync(m => m.TenantId == currentUser.TenantId && m.CustomerId == order.CustomerId, ct);
        if (member is null)
        {
            return null;
        }

        var name = await db.Customers.Where(c => c.Id == member.CustomerId).Select(c => c.Name).FirstAsync(ct);
        var rule = await loyalty.RulesAsync(ct);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(clock.UtcNow, timeZone));
        var progress = LoyaltyCalculator.Progress(await loyalty.LogAsync(member.Id, ct), rule, today);
        var display = PhoneNumbers.Display(member.Phone);
        var masked = display.Length > 4 ? new string('*', display.Length - 3) + display[^3..] : display;
        return new ReceiptMember(name, masked, Math.Min(progress.Visits, rule.VisitsForReward), rule.VisitsForReward, progress.RewardAvailable);
    }
}
