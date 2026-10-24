namespace StockOrder.Modules.Catalog.Contracts;

/// <summary>
/// What Sales needs from the catalog to price an order line at a branch (section 8.7). Prices
/// are resolved server-side only; phones and the web never send a price.
/// </summary>
public interface ISalesCatalog
{
    /// <param name="branchId">The selling branch: decides branch prices, availability and branch-only items.</param>
    /// <param name="customerPriceListId">The customer's (or customer group's) list, tried first; null for walk-in.</param>
    /// <param name="businessDate">Price list items must be valid on this date.</param>
    /// <param name="lines">Product and quantity (in the product's selling unit) per line.</param>
    Task<IReadOnlyList<SaleLineQuote>> QuoteAsync(
        Guid branchId, Guid? customerPriceListId, DateOnly businessDate,
        IReadOnlyList<(Guid ProductId, decimal Quantity)> lines, CancellationToken ct);

    /// <summary>Current product names, for showing orders.</summary>
    Task<IReadOnlyDictionary<Guid, string>> GetProductNamesAsync(IReadOnlyCollection<Guid> productIds, CancellationToken ct);
}

public enum SaleAvailability
{
    Available,

    /// <summary>No such product for this tenant.</summary>
    NotFound,

    /// <summary>Deactivated, or not sellable.</summary>
    Inactive,

    /// <summary>A branch-only item of another branch.</summary>
    NotSoldHere,

    /// <summary>Switched off at this branch (catalog.ProductBranch.IsAvailable = 0).</summary>
    Unavailable,
}

/// <param name="Behaviour">catalog.ProductType.Behaviour: Stocked, NonStock, Service, Kit, Recipe.</param>
/// <param name="UomId">The selling unit (SalesUomId, else the base unit) the quantity and price are in.</param>
/// <param name="FactorToBase">Base units per selling unit, for QtyBase (section 8.1).</param>
/// <param name="UnitPrice">Null when no price list has a price: PRICE_NOT_FOUND.</param>
public sealed record SaleLineQuote(
    Guid ProductId,
    SaleAvailability Availability,
    string Name,
    string Behaviour,
    Guid UomId,
    byte UomDecimalPlaces,
    decimal FactorToBase,
    decimal? UnitPrice,
    Guid? TaxCodeId,
    decimal TaxRatePercent);
