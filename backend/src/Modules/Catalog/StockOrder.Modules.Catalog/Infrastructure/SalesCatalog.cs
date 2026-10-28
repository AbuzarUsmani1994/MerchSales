using Dapper;
using StockOrder.Infrastructure.Database;
using StockOrder.Modules.Catalog.Contracts;
using StockOrder.SharedKernel.Context;

namespace StockOrder.Modules.Catalog.Infrastructure;

/// <summary>
/// Section 8.7 price resolution, per line: the customer's list, then the branch's own list
/// (014_BranchCatalog), then the tenant default. In each list: the selling unit, the highest
/// MinQuantity not above the quantity, valid on the business date. A base-unit price is used
/// times the factor when the selling unit has none.
/// </summary>
internal sealed class SalesCatalog(IUnitOfWork unitOfWork, ICurrentUserContext currentUser) : ISalesCatalog
{
    public async Task<IReadOnlyList<SaleLineQuote>> QuoteAsync(
        Guid branchId, Guid? customerPriceListId, DateOnly businessDate,
        IReadOnlyList<(Guid ProductId, decimal Quantity)> lines, CancellationToken ct)
    {
        var tenantId = currentUser.TenantId;
        var productIds = lines.Select(l => l.ProductId).Distinct().ToArray();
        if (productIds.Length == 0)
        {
            return [];
        }

        using var grid = await unitOfWork.Connection.QueryMultipleAsync(new CommandDefinition(
            """
            SELECT p.Id, p.Name, t.Behaviour, p.BaseUomId, COALESCE(p.SalesUomId, p.BaseUomId) AS UomId,
                   u.DecimalPlaces, COALESCE(c.FactorToBase, 1) AS FactorToBase,
                   p.IsActive, p.IsSellable, p.OwnerBranchId, pb.IsAvailable AS BranchAvailable,
                   p.TaxCodeId, COALESCE(tc.RatePercent, 0) AS TaxRatePercent, CAST(COALESCE(cat.SendToKitchen, 1) AS bit) AS SendToKitchen
            FROM catalog.Product p
            JOIN catalog.ProductType t ON t.TenantId = p.TenantId AND t.Id = p.ProductTypeId
            JOIN catalog.UnitOfMeasure u ON u.TenantId = p.TenantId AND u.Id = COALESCE(p.SalesUomId, p.BaseUomId)
            LEFT JOIN catalog.ProductUomConversion c
                   ON c.TenantId = p.TenantId AND c.ProductId = p.Id AND c.UomId = COALESCE(p.SalesUomId, p.BaseUomId)
            LEFT JOIN catalog.ProductBranch pb ON pb.TenantId = p.TenantId AND pb.ProductId = p.Id AND pb.BranchId = @branchId
            LEFT JOIN catalog.TaxCode tc ON tc.TenantId = p.TenantId AND tc.Id = p.TaxCodeId
            LEFT JOIN catalog.Category cat ON cat.TenantId = p.TenantId AND cat.Id = p.CategoryId
            WHERE p.TenantId = @tenantId AND p.Id IN @productIds;

            SELECT l.Id,
                   CASE WHEN l.Id = @customerListId THEN 0 WHEN l.BranchId = @branchId THEN 1 ELSE 2 END AS Rank
            FROM catalog.PriceList l
            WHERE l.TenantId = @tenantId AND l.IsActive = 1
              AND (l.Id = @customerListId OR l.BranchId = @branchId OR l.IsDefault = 1)
              AND (l.ValidFrom IS NULL OR l.ValidFrom <= @date) AND (l.ValidTo IS NULL OR l.ValidTo >= @date);

            SELECT i.PriceListId, i.ProductId, i.UomId, i.MinQuantity, i.UnitPrice, i.ValidFrom
            FROM catalog.PriceListItem i
            JOIN catalog.PriceList l ON l.TenantId = i.TenantId AND l.Id = i.PriceListId
            WHERE i.TenantId = @tenantId AND i.ProductId IN @productIds
              AND (l.Id = @customerListId OR l.BranchId = @branchId OR l.IsDefault = 1)
              AND (i.ValidFrom IS NULL OR i.ValidFrom <= @date) AND (i.ValidTo IS NULL OR i.ValidTo >= @date);
            """,
            new
            {
                tenantId,
                branchId,
                productIds,
                customerListId = customerPriceListId ?? Guid.Empty,
                date = businessDate.ToDateTime(TimeOnly.MinValue),
            },
            unitOfWork.Transaction,
            cancellationToken: ct));

        var products = (await grid.ReadAsync<ProductRow>()).ToDictionary(p => p.Id);
        var listRank = (await grid.ReadAsync<ListRow>()).ToDictionary(l => l.Id, l => l.Rank);
        var items = (await grid.ReadAsync<ItemRow>()).Where(i => listRank.ContainsKey(i.PriceListId)).ToList();

        return lines.Select(line =>
        {
            if (!products.TryGetValue(line.ProductId, out var p))
            {
                return new SaleLineQuote(line.ProductId, SaleAvailability.NotFound, string.Empty, string.Empty, Guid.Empty, 0, 1m, null, null, 0m);
            }

            var availability =
                !p.IsActive || !p.IsSellable ? SaleAvailability.Inactive
                : p.OwnerBranchId is { } owner && owner != branchId ? SaleAvailability.NotSoldHere
                : p.BranchAvailable == false ? SaleAvailability.Unavailable
                : SaleAvailability.Available;

            return new SaleLineQuote(
                p.Id, availability, p.Name, p.Behaviour, p.UomId, p.DecimalPlaces, p.FactorToBase,
                ResolvePrice(p, line.Quantity, items, listRank), p.TaxCodeId, p.TaxRatePercent, p.SendToKitchen);
        }).ToList();
    }

    public async Task<IReadOnlyDictionary<Guid, string>> GetProductNamesAsync(IReadOnlyCollection<Guid> productIds, CancellationToken ct)
    {
        if (productIds.Count == 0)
        {
            return new Dictionary<Guid, string>();
        }

        var rows = await unitOfWork.Connection.QueryAsync<(Guid Id, string Name)>(new CommandDefinition(
            "SELECT Id, Name FROM catalog.Product WHERE TenantId = @tenantId AND Id IN @ids;",
            new { tenantId = currentUser.TenantId, ids = productIds.ToArray() },
            unitOfWork.Transaction,
            cancellationToken: ct));
        return rows.ToDictionary(r => r.Id, r => r.Name);
    }

    private static decimal? ResolvePrice(ProductRow product, decimal quantity, List<ItemRow> items, Dictionary<Guid, int> listRank)
    {
        foreach (var rank in listRank.Values.Distinct().Order())
        {
            var inList = items.Where(i => i.ProductId == product.Id && listRank[i.PriceListId] == rank && i.MinQuantity <= quantity).ToList();
            var direct = Best(inList.Where(i => i.UomId == product.UomId));
            if (direct is not null)
            {
                return direct.UnitPrice;
            }

            if (product.UomId != product.BaseUomId && Best(inList.Where(i => i.UomId == product.BaseUomId)) is { } basePrice)
            {
                return basePrice.UnitPrice * product.FactorToBase;
            }
        }

        return null;
    }

    private static ItemRow? Best(IEnumerable<ItemRow> candidates) =>
        candidates.OrderByDescending(i => i.MinQuantity).ThenByDescending(i => i.ValidFrom).FirstOrDefault();

    private sealed record ProductRow(
        Guid Id, string Name, string Behaviour, Guid BaseUomId, Guid UomId, byte DecimalPlaces, decimal FactorToBase,
        bool IsActive, bool IsSellable, Guid? OwnerBranchId, bool? BranchAvailable, Guid? TaxCodeId, decimal TaxRatePercent,
        bool SendToKitchen);

    private sealed record ListRow(Guid Id, int Rank);

    private sealed record ItemRow(Guid PriceListId, Guid ProductId, Guid UomId, decimal MinQuantity, decimal UnitPrice, DateTime? ValidFrom);
}
