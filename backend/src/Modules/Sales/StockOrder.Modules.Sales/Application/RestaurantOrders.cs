using Dapper;
using StockOrder.Infrastructure.Database;
using StockOrder.Modules.Catalog.Contracts;
using StockOrder.Modules.Finance.Contracts;
using StockOrder.Modules.Restaurant.Contracts;
using StockOrder.Modules.Sales.Domain;
using StockOrder.Modules.Tenancy.Contracts;
using StockOrder.SharedKernel.Time;

namespace StockOrder.Modules.Sales.Application;

/// <summary>Which restaurant features a company has on (016_RestaurantPack).</summary>
public sealed record RestaurantFeatures(bool Tables, bool Kitchen);

/// <summary>
/// The restaurant side of taking an order (docs/decisions.md "Tables and kitchen screen"): the
/// short daily order number and sending each round to the kitchen, inside the order's transaction.
/// </summary>
public sealed class RestaurantOrders(
    IFeatureQueries featureQueries,
    IFinancePosting finance,
    ITenantQueries tenantQueries,
    IUnitOfWork unitOfWork,
    IKitchen kitchen,
    IClock clock)
{
    public async Task<RestaurantFeatures> GetFeaturesAsync(Guid tenantId, CancellationToken ct)
    {
        var keys = await featureQueries.GetEnabledFeatureKeysAsync(tenantId, ct);
        return new RestaurantFeatures(keys.Contains(FeatureKeys.RestaurantTables), keys.Contains(FeatureKeys.RestaurantKitchen));
    }

    /// <summary>
    /// The next short number at the branch: #1 again each trading day. The day is the branch's
    /// open cash session's business date when one is open (so a shift running past midnight keeps
    /// counting), else today in the company's time zone. One atomic statement per order under a
    /// range lock, so two tills never get the same number.
    /// </summary>
    public async Task<(int OrderNo, DateOnly BusinessDate)> NextOrderNoAsync(Guid tenantId, Guid branchId, CancellationToken ct)
    {
        var businessDate = await finance.GetOpenBusinessDateAsync(branchId, ct)
            ?? clock.TodayIn(await tenantQueries.GetTimeZoneIdAsync(tenantId, ct));
        var number = await unitOfWork.Connection.ExecuteScalarAsync<int>(new CommandDefinition(
            """
            DECLARE @n int;
            UPDATE sales.DailyOrderCounter WITH (UPDLOCK, HOLDLOCK)
               SET @n = LastNumber = LastNumber + 1
             WHERE TenantId = @tenantId AND BranchId = @branchId AND BusinessDate = @businessDate;
            IF @@ROWCOUNT = 0
            BEGIN
                INSERT sales.DailyOrderCounter (TenantId, BranchId, BusinessDate, LastNumber) VALUES (@tenantId, @branchId, @businessDate, 1);
                SET @n = 1;
            END;
            SELECT @n;
            """,
            new { tenantId, branchId, businessDate = businessDate.ToDateTime(TimeOnly.MinValue) },
            unitOfWork.Transaction,
            cancellationToken: ct));
        return (number, businessDate);
    }

    /// <summary>
    /// Sends the given new lines to the kitchen as the order's next round, leaving out items whose
    /// category is kept off the kitchen screen. A round of only such items (drinks) sends nothing.
    /// </summary>
    public async Task SendRoundAsync(
        SalesOrder order, DateOnly businessDate, IReadOnlyList<(SalesOrderLine Line, SaleLineQuote Quote)> newLines,
        string? tableName, string? notes, CancellationToken ct)
    {
        if (order.OrderNo is not { } orderNo)
        {
            return;
        }

        var lines = newLines
            .Where(l => l.Quote.SendToKitchen)
            .Select(l => new KitchenRoundLine(l.Line.Id, l.Line.ProductId, l.Quote.Name, l.Line.Quantity, l.Line.Notes))
            .ToList();
        if (lines.Count == 0)
        {
            return;
        }

        await kitchen.SendRoundAsync(
            new KitchenRound(order.Id, order.BranchId, businessDate, orderNo, order.Channel, tableName, notes, lines), ct);
    }
}
