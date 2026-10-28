using Microsoft.EntityFrameworkCore;
using StockOrder.Modules.Catalog.Contracts;
using StockOrder.Modules.Identity.Contracts;
using StockOrder.Modules.Restaurant.Contracts;
using StockOrder.Modules.Sales.Domain;
using StockOrder.Modules.Sales.Infrastructure;
using StockOrder.Modules.Tenancy.Contracts;

namespace StockOrder.Modules.Sales.Application;

public static class SalesPermissions
{
    public const string View = "Sales.View";
    public const string OrderCreate = "Sales.Order.Create";
    public const string OrderFulfil = "Sales.Order.Fulfil";
    public const string OrderCancel = "Sales.Order.Cancel";
    public const string InvoicePost = "Sales.Invoice.Post";
    public const string PaymentReceive = "Finance.Payment.Receive";
}

public sealed record SalesOrderLineView(
    Guid Id, int LineNumber, Guid ProductId, string ProductName, decimal Quantity, decimal UnitPrice, decimal LineTotal, string? Notes);

/// <param name="Etag">Base64 RowVer; send it back as If-Match on actions (section 9).</param>
/// <param name="TakenBy">Display name of whoever took the order (waiter, rep).</param>
/// <param name="OrderNo">The restaurant's short daily number (#23); null where the kitchen screen is off.</param>
/// <param name="TableName">The table of a dine-in order.</param>
public sealed record SalesOrderView(
    Guid Id,
    string? DocumentNo,
    string Status,
    string Channel,
    Guid BranchId,
    string BranchName,
    DateTime OrderDate,
    DateOnly BusinessDate,
    string? Notes,
    string? TakenBy,
    decimal SubTotal,
    decimal DiscountTotal,
    decimal TaxTotal,
    decimal GrandTotal,
    decimal PaidAmount,
    Guid? ClientDocumentId,
    string? LoyaltyPhone,
    string Etag,
    IReadOnlyList<SalesOrderLineView> Lines,
    int? OrderNo = null,
    Guid? TableId = null,
    string? TableName = null,
    int? Covers = null,
    bool BillRequested = false);

/// <summary>Builds <see cref="SalesOrderView"/>s with product, branch and user names looked up in one go.</summary>
public sealed class SalesOrderViewBuilder(
    SalesDbContext db, IUserDirectory users, IBranchQueries branches, ISalesCatalog catalog, IDiningTables tables)
{
    public async Task<IReadOnlyList<SalesOrderView>> BuildAsync(Guid tenantId, IReadOnlyList<SalesOrder> orders, CancellationToken ct)
    {
        var lines = orders.SelectMany(o => o.Lines).ToList();
        var names = await catalog.GetProductNamesAsync(lines.Select(l => l.ProductId).Distinct().ToList(), ct);
        var takers = await users.GetDisplayNamesAsync(orders.Where(o => o.SalesRepUserId is not null).Select(o => o.SalesRepUserId!.Value).ToList(), ct);
        var branchNames = (await branches.GetBranchesAsync(tenantId, ct)).ToDictionary(b => b.Id, b => b.Name);
        var tableIds = orders.Where(o => o.TableId is not null).Select(o => o.TableId!.Value).Distinct().ToList();
        var tableNames = tableIds.Count > 0 ? await tables.GetNamesAsync(tableIds, ct) : new Dictionary<Guid, string>();

        return orders.Select(o => new SalesOrderView(
            o.Id, o.DocumentNo, o.Status, o.Channel, o.BranchId, branchNames.GetValueOrDefault(o.BranchId, string.Empty),
            o.OrderDate, o.BusinessDate, o.Notes,
            o.SalesRepUserId is { } rep ? takers.GetValueOrDefault(rep) : null,
            o.SubTotal, o.DiscountTotal, o.TaxTotal, o.GrandTotal, o.PaidAmount, o.ClientDocumentId,
            o.LoyaltyPhone is { } phone ? PhoneNumbers.Display(phone) : null,
            ETags.Format(o.RowVer),
            o.Lines.OrderBy(l => l.LineNumber).Select(l => new SalesOrderLineView(
                l.Id, l.LineNumber, l.ProductId, names.GetValueOrDefault(l.ProductId, "Item"), l.Quantity, l.UnitPrice, l.LineTotal, l.Notes)).ToList(),
            o.OrderNo,
            o.TableId,
            o.TableId is { } tableId ? tableNames.GetValueOrDefault(tableId) : null,
            o.Covers,
            o.BillRequestedAtUtc is not null))
            .ToList();
    }

    public async Task<SalesOrderView> BuildAsync(Guid tenantId, Guid orderId, CancellationToken ct)
    {
        var order = await db.SalesOrders.AsNoTracking().Include(o => o.Lines).FirstAsync(o => o.TenantId == tenantId && o.Id == orderId, ct);
        return (await BuildAsync(tenantId, [order], ct))[0];
    }
}

/// <summary>ETag = base64 of the row's rowversion (section 9). Quotes around the header value are optional.</summary>
public static class ETags
{
    public static string Format(byte[] rowVersion) => Convert.ToBase64String(rowVersion);

    /// <summary>True when they match, or when no If-Match was given (sync commands; REST controllers require the header).</summary>
    public static bool Matches(string? ifMatch, byte[] rowVersion) =>
        ifMatch is null || string.Equals(ifMatch.Trim().Trim('"'), Format(rowVersion), StringComparison.Ordinal);
}
