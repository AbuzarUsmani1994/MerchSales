using Dapper;
using StockOrder.Infrastructure.Database;
using StockOrder.Modules.Catalog.Domain;
using StockOrder.Modules.Sync.Contracts;
using StockOrder.SharedKernel.Context;

namespace StockOrder.Modules.Catalog.Infrastructure;

// The phone's menu (section 12 entities uoms, categories, products, priceLists, priceListItems,
// plus productBranches from 014). Rows are sent as stored; the phone picks the branch price over
// the base price and hides unavailable items the same way GetMenuQuery does, and the server
// re-prices every order anyway (section 5.7). Branch-owned rows go only to users who can view
// that branch.

internal sealed class UomsSyncSource(IUnitOfWork unitOfWork, ICurrentUserContext currentUser) : ISyncSource
{
    public string Entity => "uoms";

    public async Task<SyncEntityPage> PullAsync(SyncPullRequest request, CancellationToken ct)
    {
        var rows = await CatalogSyncSql.QueryAsync<UomRow>(unitOfWork, """
            SELECT TOP (@limit) Id, Code, Name, DecimalPlaces, IsActive, RowVer
            FROM catalog.UnitOfMeasure
            WHERE TenantId = @tenantId AND RowVer > @after AND RowVer < @upper
            ORDER BY RowVer;
            """, CatalogSyncSql.Parameters(currentUser.TenantId, request), ct);

        return CatalogSyncSql.Page(rows, r => r.RowVer, r => new { r.Id, r.Code, r.Name, r.DecimalPlaces, r.IsActive }, request);
    }

    private sealed record UomRow(Guid Id, string Code, string Name, byte DecimalPlaces, bool IsActive, byte[] RowVer);
}

internal sealed class CategoriesSyncSource(IUnitOfWork unitOfWork, ICurrentUserContext currentUser) : ISyncSource
{
    public string Entity => "categories";

    public async Task<SyncEntityPage> PullAsync(SyncPullRequest request, CancellationToken ct)
    {
        var rows = await CatalogSyncSql.QueryAsync<CategoryRow>(unitOfWork, """
            SELECT TOP (@limit) Id, ParentId, Name, SortOrder, IsActive, RowVer
            FROM catalog.Category
            WHERE TenantId = @tenantId AND RowVer > @after AND RowVer < @upper
            ORDER BY RowVer;
            """, CatalogSyncSql.Parameters(currentUser.TenantId, request), ct);

        return CatalogSyncSql.Page(rows, r => r.RowVer, r => new { r.Id, r.ParentId, r.Name, r.SortOrder, r.IsActive }, request);
    }

    private sealed record CategoryRow(Guid Id, Guid? ParentId, string Name, int SortOrder, bool IsActive, byte[] RowVer);
}

internal sealed class ProductsSyncSource(IUnitOfWork unitOfWork, ICurrentUserContext currentUser, IAccessScope accessScope) : ISyncSource
{
    public string Entity => "products";

    public async Task<SyncEntityPage> PullAsync(SyncPullRequest request, CancellationToken ct)
    {
        var parameters = CatalogSyncSql.Parameters(currentUser.TenantId, request);
        await CatalogSyncSql.AddBranchScopeAsync(parameters, accessScope, ct);
        var rows = await CatalogSyncSql.QueryAsync<ProductRow>(unitOfWork, """
            SELECT TOP (@limit) p.Id, p.Sku, p.Name, p.ShortName, p.Description, p.CategoryId, p.ProductTypeId,
                   t.Behaviour, p.BaseUomId, p.SalesUomId, p.OwnerBranchId, p.IsActive, p.RowVer
            FROM catalog.Product p
            JOIN catalog.ProductType t ON t.TenantId = p.TenantId AND t.Id = p.ProductTypeId
            WHERE p.TenantId = @tenantId AND p.RowVer > @after AND p.RowVer < @upper
              AND (p.OwnerBranchId IS NULL OR @allBranches = 1 OR p.OwnerBranchId IN @branchIds)
            ORDER BY p.RowVer;
            """, parameters, ct);

        return CatalogSyncSql.Page(rows, r => r.RowVer, r => new
        {
            r.Id, r.Sku, r.Name, r.ShortName, r.Description, r.CategoryId, r.ProductTypeId, r.Behaviour,
            r.BaseUomId, r.SalesUomId, r.OwnerBranchId, r.IsActive,
        }, request);
    }

    private sealed record ProductRow(
        Guid Id, string Sku, string Name, string? ShortName, string? Description, Guid? CategoryId, Guid ProductTypeId,
        string Behaviour, Guid BaseUomId, Guid? SalesUomId, Guid? OwnerBranchId, bool IsActive, byte[] RowVer);
}

internal sealed class PriceListsSyncSource(IUnitOfWork unitOfWork, ICurrentUserContext currentUser, IAccessScope accessScope) : ISyncSource
{
    public string Entity => "priceLists";

    public async Task<SyncEntityPage> PullAsync(SyncPullRequest request, CancellationToken ct)
    {
        var parameters = CatalogSyncSql.Parameters(currentUser.TenantId, request);
        await CatalogSyncSql.AddBranchScopeAsync(parameters, accessScope, ct);
        var rows = await CatalogSyncSql.QueryAsync<PriceListRow>(unitOfWork, """
            SELECT TOP (@limit) Id, Name, IsDefault, BranchId, IsActive, RowVer
            FROM catalog.PriceList
            WHERE TenantId = @tenantId AND RowVer > @after AND RowVer < @upper
              AND (BranchId IS NULL OR @allBranches = 1 OR BranchId IN @branchIds)
            ORDER BY RowVer;
            """, parameters, ct);

        return CatalogSyncSql.Page(rows, r => r.RowVer, r => new { r.Id, r.Name, r.IsDefault, r.BranchId, r.IsActive }, request);
    }

    private sealed record PriceListRow(Guid Id, string Name, bool IsDefault, Guid? BranchId, bool IsActive, byte[] RowVer);
}

internal sealed class PriceListItemsSyncSource(IUnitOfWork unitOfWork, ICurrentUserContext currentUser, IAccessScope accessScope) : ISyncSource
{
    public string Entity => "priceListItems";

    public async Task<SyncEntityPage> PullAsync(SyncPullRequest request, CancellationToken ct)
    {
        var parameters = CatalogSyncSql.Parameters(currentUser.TenantId, request);
        await CatalogSyncSql.AddBranchScopeAsync(parameters, accessScope, ct);
        var rows = await CatalogSyncSql.QueryAsync<PriceItemRow>(unitOfWork, """
            SELECT TOP (@limit) i.Id, i.PriceListId, i.ProductId, i.UomId, i.MinQuantity, i.UnitPrice, i.ValidFrom, i.ValidTo, i.RowVer
            FROM catalog.PriceListItem i
            JOIN catalog.PriceList l ON l.TenantId = i.TenantId AND l.Id = i.PriceListId
            WHERE i.TenantId = @tenantId AND i.RowVer > @after AND i.RowVer < @upper
              AND (l.BranchId IS NULL OR @allBranches = 1 OR l.BranchId IN @branchIds)
            ORDER BY i.RowVer;
            """, parameters, ct);

        return CatalogSyncSql.Page(rows, r => r.RowVer, r => new
        {
            r.Id, r.PriceListId, r.ProductId, r.UomId, r.MinQuantity, r.UnitPrice,
            ValidFrom = r.ValidFrom is { } from ? DateOnly.FromDateTime(from) : (DateOnly?)null,
            ValidTo = r.ValidTo is { } to ? DateOnly.FromDateTime(to) : (DateOnly?)null,
        }, request);
    }

    private sealed record PriceItemRow(
        Guid Id, Guid PriceListId, Guid ProductId, Guid UomId, decimal MinQuantity, decimal UnitPrice,
        DateTime? ValidFrom, DateTime? ValidTo, byte[] RowVer);
}

internal sealed class ProductBranchesSyncSource(IUnitOfWork unitOfWork, ICurrentUserContext currentUser, IAccessScope accessScope) : ISyncSource
{
    public string Entity => "productBranches";

    public async Task<SyncEntityPage> PullAsync(SyncPullRequest request, CancellationToken ct)
    {
        var parameters = CatalogSyncSql.Parameters(currentUser.TenantId, request);
        await CatalogSyncSql.AddBranchScopeAsync(parameters, accessScope, ct);
        var rows = await CatalogSyncSql.QueryAsync<ProductBranchRow>(unitOfWork, """
            SELECT TOP (@limit) Id, ProductId, BranchId, IsAvailable, RowVer
            FROM catalog.ProductBranch
            WHERE TenantId = @tenantId AND RowVer > @after AND RowVer < @upper
              AND (@allBranches = 1 OR BranchId IN @branchIds)
            ORDER BY RowVer;
            """, parameters, ct);

        return CatalogSyncSql.Page(rows, r => r.RowVer, r => new { r.Id, r.ProductId, r.BranchId, r.IsAvailable }, request);
    }

    private sealed record ProductBranchRow(Guid Id, Guid ProductId, Guid BranchId, bool IsAvailable, byte[] RowVer);
}

internal static class CatalogSyncSql
{
    public static DynamicParameters Parameters(Guid tenantId, SyncPullRequest request)
    {
        var parameters = new DynamicParameters();
        parameters.Add("tenantId", tenantId);
        parameters.Add("limit", request.Limit);
        parameters.Add("after", RowVersionToken.ToBinary(RowVersionToken.Parse(request.Token)), System.Data.DbType.Binary, size: 8);
        parameters.Add("upper", RowVersionToken.ToBinary(request.UpperRowVersion), System.Data.DbType.Binary, size: 8);
        return parameters;
    }

    /// <summary>The branches whose menu the caller may see (Catalog.View), as @allBranches / @branchIds.</summary>
    public static async Task AddBranchScopeAsync(DynamicParameters parameters, IAccessScope accessScope, CancellationToken ct)
    {
        var scope = BranchScope.For(await accessScope.GetGrantsAsync(ct), CatalogPolicy.View);
        parameters.Add("allBranches", scope.AllBranches);
        // Dapper expands IN @branchIds; an empty list must still be valid SQL.
        parameters.Add("branchIds", scope.BranchIds.Count > 0 ? scope.BranchIds.ToArray() : [Guid.Empty]);
    }

    public static async Task<List<TRow>> QueryAsync<TRow>(IUnitOfWork unitOfWork, string sql, DynamicParameters parameters, CancellationToken ct) =>
        (await unitOfWork.Connection.QueryAsync<TRow>(new CommandDefinition(sql, parameters, unitOfWork.Transaction, cancellationToken: ct))).ToList();

    public static SyncEntityPage Page<TRow>(
        List<TRow> rows, Func<TRow, byte[]> rowVersion, Func<TRow, object> project, SyncPullRequest request)
    {
        var versions = rows.Select(r => RowVersionToken.FromBinary(rowVersion(r))).ToList();
        return new SyncEntityPage(
            rows.Select(project).ToList(),
            RowVersionToken.Next(versions, request.Limit, request.UpperRowVersion),
            HasMore: rows.Count >= request.Limit);
    }
}
