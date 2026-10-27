using Dapper;
using Mediator;
using Microsoft.Data.SqlClient;
using StockOrder.Infrastructure.Database;
using StockOrder.Infrastructure.Files;
using StockOrder.SharedKernel.Errors;
using StockOrder.SharedKernel.Results;
using StockOrder.SharedKernel.Time;

namespace StockOrder.Modules.Catalog.Application;

public sealed record PublicMenuItem(Guid Id, string Name, string? Description, decimal Price, Guid? ImageAttachmentId);

public sealed record PublicMenuSection(Guid? CategoryId, string? Name, IReadOnlyList<PublicMenuItem> Items);

public sealed record PublicMenu(string TenantName, string BranchName, string? BranchAddress, string? BranchPhone, IReadOnlyList<PublicMenuSection> Sections);

/// <summary>
/// The customer menu a waiter's QR opens (docs/decisions.md, "Public customer menu"): no login,
/// so the menu code alone names the company and branch. Shows exactly what the waiter's phone
/// menu shows for that branch — items on sale there with a price — and nothing else: no costs,
/// stock, customers or orders. Restaurants only.
/// </summary>
public sealed record GetPublicMenuQuery(string Code) : IQuery<Result<PublicMenu>>;

/// <summary>A picture on a customer menu: only of an item that menu shows.</summary>
public sealed record GetPublicMenuImageQuery(string Code, Guid ProductId) : IQuery<Result<ProductImageFile>>;

public sealed class GetPublicMenuQueryHandler(IPublicMenuLinkLookup links, ITenantConnectionFactory connections, IClock clock)
    : IQueryHandler<GetPublicMenuQuery, Result<PublicMenu>>
{
    public async ValueTask<Result<PublicMenu>> Handle(GetPublicMenuQuery query, CancellationToken ct)
    {
        if (await PublicMenuSql.FindLinkAsync(links, query.Code, ct) is not { } link)
        {
            return Result.Failure<PublicMenu>(Error.NotFound("Menu"));
        }

        // Its own connection for the link's tenant: this request has no token, so no request
        // connection exists, and RLS still limits every read below to that tenant.
        await using var connection = await connections.OpenAsync(link.TenantId, ct);
        var rows = (await connection.QueryAsync<MenuRow>(new CommandDefinition(
            $"""
            SELECT p.Id, p.Name, p.Description, p.ImageAttachmentId, c.Id AS CategoryId, c.Name AS CategoryName, price.UnitPrice AS Price
            {PublicMenuSql.VisibleProductsFrom}
            CROSS APPLY (
                SELECT TOP (1) i.UnitPrice
                FROM catalog.PriceListItem i
                JOIN catalog.PriceList l ON l.TenantId = i.TenantId AND l.Id = i.PriceListId
                WHERE i.TenantId = p.TenantId AND i.ProductId = p.Id AND i.UomId = COALESCE(p.SalesUomId, p.BaseUomId)
                  AND i.MinQuantity <= 1 AND (i.ValidFrom IS NULL OR i.ValidFrom <= @today) AND (i.ValidTo IS NULL OR i.ValidTo >= @today)
                  AND l.IsActive = 1 AND (l.BranchId = @branchId OR l.IsDefault = 1)
                ORDER BY CASE WHEN l.BranchId = @branchId THEN 0 ELSE 1 END, i.MinQuantity DESC
            ) price
            {PublicMenuSql.VisibleProductsWhere}
            ORDER BY CASE WHEN c.Id IS NULL THEN 1 ELSE 0 END, c.SortOrder, c.Name, p.Name;
            """,
            new { tenantId = link.TenantId, branchId = link.BranchId, today = clock.TodayIn(link.TimeZoneId).ToDateTime(TimeOnly.MinValue) },
            cancellationToken: ct))).ToList();

        var sections = rows
            .GroupBy(r => (r.CategoryId, r.CategoryName))
            .Select(g => new PublicMenuSection(
                g.Key.CategoryId,
                g.Key.CategoryName,
                g.Select(r => new PublicMenuItem(r.Id, r.Name, r.Description, r.Price, r.ImageAttachmentId)).ToList()))
            .ToList();

        return Result.Success(new PublicMenu(link.TenantName, link.BranchName, link.BranchAddress, link.BranchPhone, sections));
    }

    private sealed record MenuRow(Guid Id, string Name, string? Description, Guid? ImageAttachmentId, Guid? CategoryId, string? CategoryName, decimal Price);
}

public sealed class GetPublicMenuImageQueryHandler(
    IPublicMenuLinkLookup links, ITenantConnectionFactory connections, IAttachmentStore attachments)
    : IQueryHandler<GetPublicMenuImageQuery, Result<ProductImageFile>>
{
    public async ValueTask<Result<ProductImageFile>> Handle(GetPublicMenuImageQuery query, CancellationToken ct)
    {
        if (await PublicMenuSql.FindLinkAsync(links, query.Code, ct) is not { } link)
        {
            return Result.Failure<ProductImageFile>(Error.NotFound("Picture"));
        }

        await using var connection = await connections.OpenAsync(link.TenantId, ct);
        var attachmentId = await connection.QuerySingleOrDefaultAsync<Guid?>(new CommandDefinition(
            $"""
            SELECT p.ImageAttachmentId
            {PublicMenuSql.VisibleProductsFrom}
            {PublicMenuSql.VisibleProductsWhere}
              AND p.Id = @productId;
            """,
            new { tenantId = link.TenantId, branchId = link.BranchId, productId = query.ProductId },
            cancellationToken: ct));

        // The attachment row is read on the same tenant connection, then the file streamed.
        if (attachmentId is not { } id || await FindAsync(connection, link.TenantId, id, ct) is not { } stored)
        {
            return Result.Failure<ProductImageFile>(Error.NotFound("Picture"));
        }

        return Result.Success(new ProductImageFile(stored.Id, stored.ContentType, await attachments.OpenReadAsync(stored, ct)));
    }

    private static Task<StoredAttachment?> FindAsync(SqlConnection connection, Guid tenantId, Guid attachmentId, CancellationToken ct) =>
        connection.QuerySingleOrDefaultAsync<StoredAttachment>(new CommandDefinition(
            """
            SELECT Id, FileName, ContentType, SizeBytes, StoragePath
            FROM platform.Attachment
            WHERE TenantId = @tenantId AND Id = @attachmentId;
            """,
            new { tenantId, attachmentId },
            cancellationToken: ct));
}

internal static class PublicMenuSql
{
    /// <summary>
    /// The items a branch sells, the same rule as the waiter's phone menu: active, shared or
    /// this branch's own, a sellable type (not stock items or kits), in an active category (or
    /// none), and not switched off at the branch. The phone also hides items without a price;
    /// the menu query does that with its price lookup (a CROSS APPLY between these two parts).
    /// </summary>
    public const string VisibleProductsFrom =
        """
        FROM catalog.Product p
        JOIN catalog.ProductType t ON t.TenantId = p.TenantId AND t.Id = p.ProductTypeId
        LEFT JOIN catalog.Category c ON c.TenantId = p.TenantId AND c.Id = p.CategoryId
        """;

    public const string VisibleProductsWhere =
        """
        WHERE p.TenantId = @tenantId AND p.IsActive = 1
          AND (p.OwnerBranchId IS NULL OR p.OwnerBranchId = @branchId)
          AND t.Behaviour NOT IN ('Stocked', 'Kit')
          AND (c.Id IS NULL OR c.IsActive = 1)
          AND NOT EXISTS (SELECT 1 FROM catalog.ProductBranch pb
                          WHERE pb.TenantId = p.TenantId AND pb.ProductId = p.Id AND pb.BranchId = @branchId AND pb.IsAvailable = 0)
        """;

    /// <summary>The link, if it opens a menu: a live restaurant and an open branch.</summary>
    public static async Task<PublicMenuLink?> FindLinkAsync(IPublicMenuLinkLookup links, string code, CancellationToken ct) =>
        await links.FindAsync(code, ct) is { TenantStatus: "Active", Industry: "Restaurant", BranchIsActive: true } link ? link : null;
}
