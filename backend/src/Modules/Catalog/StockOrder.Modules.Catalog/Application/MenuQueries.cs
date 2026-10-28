using Mediator;
using Microsoft.EntityFrameworkCore;
using StockOrder.Modules.Catalog.Domain;
using StockOrder.Modules.Catalog.Infrastructure;
using StockOrder.SharedKernel.Context;
using StockOrder.SharedKernel.Errors;
using StockOrder.SharedKernel.Results;

namespace StockOrder.Modules.Catalog.Application;

public sealed record LookupItem(Guid Id, string Code, string Name);

public sealed record CategoryItem(Guid Id, string Name, int SortOrder, bool IsActive, bool SendToKitchen);

public sealed record CatalogLookups(
    IReadOnlyList<LookupItem> Units,
    IReadOnlyList<LookupItem> ProductTypes,
    IReadOnlyList<CategoryItem> Categories,
    bool HasDefaultPriceList);

/// <summary>What the menu forms need: units, item types and categories (seeded from the industry template).</summary>
public sealed record GetCatalogLookupsQuery : IQuery<Result<CatalogLookups>>;

public sealed class GetCatalogLookupsQueryHandler(CatalogDbContext db, ICurrentUserContext currentUser)
    : IQueryHandler<GetCatalogLookupsQuery, Result<CatalogLookups>>
{
    public async ValueTask<Result<CatalogLookups>> Handle(GetCatalogLookupsQuery query, CancellationToken ct)
    {
        var tenantId = currentUser.TenantId;
        var units = await db.Units.Where(u => u.TenantId == tenantId && u.IsActive).OrderBy(u => u.Name)
            .Select(u => new LookupItem(u.Id, u.Code, u.Name)).ToListAsync(ct);
        var types = await db.ProductTypes.Where(t => t.TenantId == tenantId && t.IsActive).OrderBy(t => t.Name)
            .Select(t => new LookupItem(t.Id, t.Behaviour, t.Name)).ToListAsync(ct);
        var categories = await db.Categories.Where(c => c.TenantId == tenantId).OrderBy(c => c.SortOrder).ThenBy(c => c.Name)
            .Select(c => new CategoryItem(c.Id, c.Name, c.SortOrder, c.IsActive, c.SendToKitchen)).ToListAsync(ct);
        var hasDefault = await db.PriceLists.AnyAsync(p => p.TenantId == tenantId && p.IsDefault, ct);

        return Result.Success(new CatalogLookups(units, types, categories, hasDefault));
    }
}

public sealed record MenuItem(
    Guid Id,
    string Sku,
    string Name,
    string? ShortName,
    string? Description,
    Guid? CategoryId,
    string? CategoryName,
    Guid ProductTypeId,
    string ProductTypeName,
    Guid UomId,
    string UomCode,
    Guid? OwnerBranchId,
    bool IsActive,
    decimal? BasePrice,
    decimal? BranchPrice,
    decimal? Price,
    bool IsAvailable,
    bool CanEdit,
    bool CanSetBranchPricing,
    Guid? ImageAttachmentId);

/// <param name="BranchId">Show prices and availability as at this branch; null = the shared menu only.</param>
public sealed record GetMenuQuery(Guid? BranchId, string? Search, Guid? CategoryId) : IQuery<Result<IReadOnlyList<MenuItem>>>;

/// <summary>
/// The menu as the caller may see it: shared items plus branch-only items of their branches.
/// For a branch: the price charged there (its own price, else the base price) and whether the
/// item is available there.
/// </summary>
public sealed class GetMenuQueryHandler(CatalogDbContext db, ICurrentUserContext currentUser, IAccessScope accessScope)
    : IQueryHandler<GetMenuQuery, Result<IReadOnlyList<MenuItem>>>
{
    public async ValueTask<Result<IReadOnlyList<MenuItem>>> Handle(GetMenuQuery query, CancellationToken ct)
    {
        var tenantId = currentUser.TenantId;
        var grants = await accessScope.GetGrantsAsync(ct);
        var viewScope = BranchScope.For(grants, CatalogPolicy.View);
        if (query.BranchId is { } requested && !viewScope.Covers(requested))
        {
            return Result.Failure<IReadOnlyList<MenuItem>>(Error.PermissionDenied("You can only see branches you work at."));
        }

        var visibleBranches = viewScope.BranchIds.ToList();
        var products = db.Products.Where(p => p.TenantId == tenantId)
            .Where(p => p.OwnerBranchId == null || viewScope.AllBranches || visibleBranches.Contains(p.OwnerBranchId.Value));
        if (query.BranchId is { } branchId)
        {
            products = products.Where(p => p.OwnerBranchId == null || p.OwnerBranchId == branchId);
        }

        if (query.CategoryId is { } categoryId)
        {
            products = products.Where(p => p.CategoryId == categoryId);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            products = products.Where(p => p.Name.Contains(term) || p.Sku.Contains(term) || (p.ShortName != null && p.ShortName.Contains(term)));
        }

        var rows = await (
                from p in products
                join t in db.ProductTypes on p.ProductTypeId equals t.Id
                join u in db.Units on p.BaseUomId equals u.Id
                join c in db.Categories on p.CategoryId equals c.Id into cs
                from c in cs.DefaultIfEmpty()
                orderby p.Name
                select new { Product = p, TypeName = t.Name, UomCode = u.Code, CategoryName = c != null ? c.Name : null })
            .Take(2000)
            .ToListAsync(ct);

        var productIds = rows.Select(r => r.Product.Id).ToList();
        var defaultListId = await db.PriceLists.Where(l => l.TenantId == tenantId && l.IsDefault).Select(l => (Guid?)l.Id).FirstOrDefaultAsync(ct);
        var basePrices = await PricesAsync(defaultListId, productIds, ct);

        Dictionary<Guid, decimal> branchPrices = [];
        Dictionary<Guid, bool> availability = [];
        if (query.BranchId is { } atBranch)
        {
            var branchListId = await db.PriceLists.Where(l => l.TenantId == tenantId && l.BranchId == atBranch).Select(l => (Guid?)l.Id).FirstOrDefaultAsync(ct);
            branchPrices = await PricesAsync(branchListId, productIds, ct);
            availability = await db.ProductBranches
                .Where(pb => pb.TenantId == tenantId && pb.BranchId == atBranch && productIds.Contains(pb.ProductId))
                .ToDictionaryAsync(pb => pb.ProductId, pb => pb.IsAvailable, ct);
        }

        var items = rows.Select(r =>
        {
            var p = r.Product;
            decimal? basePrice = basePrices.TryGetValue(p.Id, out var bp) ? bp : null;
            decimal? branchPrice = branchPrices.TryGetValue(p.Id, out var brp) ? brp : null;
            return new MenuItem(
                p.Id, p.Sku, p.Name, p.ShortName, p.Description, p.CategoryId, r.CategoryName, p.ProductTypeId, r.TypeName,
                p.BaseUomId, r.UomCode, p.OwnerBranchId, p.IsActive,
                basePrice, branchPrice, branchPrice ?? basePrice,
                IsAvailable: !availability.TryGetValue(p.Id, out var available) || available,
                CanEdit: CatalogPolicy.CanManageItem(grants, p.OwnerBranchId) is null,
                CanSetBranchPricing: query.BranchId is { } b && CatalogPolicy.CanSetBranchPricing(grants, p.OwnerBranchId, b) is null,
                ImageAttachmentId: p.ImageAttachmentId);
        }).ToList();

        return Result.Success<IReadOnlyList<MenuItem>>(items);
    }

    /// <summary>The menu row of a price list per product: MinQuantity 1, no date range, in the product's base unit.</summary>
    private async Task<Dictionary<Guid, decimal>> PricesAsync(Guid? priceListId, List<Guid> productIds, CancellationToken ct)
    {
        if (priceListId is null || productIds.Count == 0)
        {
            return [];
        }

        var items = await (
                from i in db.PriceListItems
                join p in db.Products on i.ProductId equals p.Id
                where i.TenantId == currentUser.TenantId && i.PriceListId == priceListId && productIds.Contains(i.ProductId)
                      && i.UomId == (p.SalesUomId ?? p.BaseUomId) && i.MinQuantity == 1m && i.ValidFrom == null
                select new { i.ProductId, i.UnitPrice })
            .ToListAsync(ct);

        return items.GroupBy(i => i.ProductId).ToDictionary(g => g.Key, g => g.First().UnitPrice);
    }
}
