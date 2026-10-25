using Mediator;
using Microsoft.EntityFrameworkCore;
using StockOrder.Infrastructure.Files;
using StockOrder.Modules.Catalog.Domain;
using StockOrder.Modules.Catalog.Infrastructure;
using StockOrder.SharedKernel.Context;
using StockOrder.SharedKernel.Errors;
using StockOrder.SharedKernel.Results;
using StockOrder.SharedKernel.Time;

namespace StockOrder.Modules.Catalog.Application;

/// <summary>
/// The picture of a menu item. Whoever may edit the item (CatalogPolicy.CanManageItem) may change
/// its picture. The web shrinks photos before upload; the server only checks the file really is
/// an image of an allowed type and not too big.
/// </summary>
public static class ProductImageRules
{
    /// <summary>The web sends about 100-300 KB; this leaves room for a phone photo sent as is.</summary>
    public const int MaxBytes = 2 * 1024 * 1024;

    /// <summary>The content type, read from the file's first bytes (never trusted from the client).</summary>
    public static string? DetectContentType(ReadOnlySpan<byte> bytes) =>
        bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF ? "image/jpeg"
        : bytes.Length >= 8 && bytes[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }) ? "image/png"
        : bytes.Length >= 12 && bytes[..4].SequenceEqual("RIFF"u8) && bytes[8..12].SequenceEqual("WEBP"u8) ? "image/webp"
        : null;
}

public sealed record SetProductImageCommand(Guid ProductId, string FileName, byte[] Content) : ICommand<Result<ProductImageInfo>>;

public sealed record ProductImageInfo(Guid ProductId, Guid? ImageAttachmentId);

public sealed class SetProductImageCommandHandler(
    CatalogDbContext db, ICurrentUserContext currentUser, IAccessScope accessScope, IAttachmentStore attachments, IClock clock)
    : ICommandHandler<SetProductImageCommand, Result<ProductImageInfo>>
{
    public async ValueTask<Result<ProductImageInfo>> Handle(SetProductImageCommand command, CancellationToken ct)
    {
        var lookup = await ProductImageAccess.FindManageableAsync(db, currentUser, accessScope, command.ProductId, ct);
        if (lookup.IsFailure)
        {
            return Result.Failure<ProductImageInfo>(lookup.Error);
        }

        if (command.Content.Length == 0 || command.Content.Length > ProductImageRules.MaxBytes)
        {
            return Result.Failure<ProductImageInfo>(Error.Validation($"The picture must be under {ProductImageRules.MaxBytes / (1024 * 1024)} MB."));
        }

        if (ProductImageRules.DetectContentType(command.Content) is not { } contentType)
        {
            return Result.Failure<ProductImageInfo>(Error.Validation("Upload a JPEG, PNG or WebP picture."));
        }

        var product = lookup.Value;
        var extension = contentType["image/".Length..].Replace("jpeg", "jpg", StringComparison.Ordinal);
        var stored = await attachments.SaveAsync(
            nameof(Product), product.Id, $"product-{product.Sku}.{extension}", contentType, command.Content, ct);
        product.SetImage(stored.Id, clock.UtcNow, currentUser.UserId);
        await db.SaveChangesAsync(ct);
        return Result.Success(new ProductImageInfo(product.Id, stored.Id));
    }
}

/// <summary>Takes the picture off an item; it shows the placeholder again.</summary>
public sealed record RemoveProductImageCommand(Guid ProductId) : ICommand<Result<ProductImageInfo>>;

public sealed class RemoveProductImageCommandHandler(CatalogDbContext db, ICurrentUserContext currentUser, IAccessScope accessScope, IClock clock)
    : ICommandHandler<RemoveProductImageCommand, Result<ProductImageInfo>>
{
    public async ValueTask<Result<ProductImageInfo>> Handle(RemoveProductImageCommand command, CancellationToken ct)
    {
        var lookup = await ProductImageAccess.FindManageableAsync(db, currentUser, accessScope, command.ProductId, ct);
        if (lookup.IsFailure)
        {
            return Result.Failure<ProductImageInfo>(lookup.Error);
        }

        lookup.Value.SetImage(null, clock.UtcNow, currentUser.UserId);
        await db.SaveChangesAsync(ct);
        return Result.Success(new ProductImageInfo(command.ProductId, null));
    }
}

public sealed record ProductImageFile(Guid AttachmentId, string ContentType, Stream Content);

/// <summary>The picture of an item the caller can see on their menu (same visibility as the menu itself).</summary>
public sealed record GetProductImageQuery(Guid ProductId) : IQuery<Result<ProductImageFile>>;

public sealed class GetProductImageQueryHandler(CatalogDbContext db, ICurrentUserContext currentUser, IAccessScope accessScope, IAttachmentStore attachments)
    : IQueryHandler<GetProductImageQuery, Result<ProductImageFile>>
{
    public async ValueTask<Result<ProductImageFile>> Handle(GetProductImageQuery query, CancellationToken ct)
    {
        var product = await db.Products.AsNoTracking()
            .Where(p => p.TenantId == currentUser.TenantId && p.Id == query.ProductId)
            .Select(p => new { p.OwnerBranchId, p.ImageAttachmentId })
            .FirstOrDefaultAsync(ct);
        if (product is null || !CatalogPolicy.CanSee(BranchScope.For(await accessScope.GetGrantsAsync(ct), CatalogPolicy.View), product.OwnerBranchId))
        {
            return Result.Failure<ProductImageFile>(Error.NotFound("Item"));
        }

        if (product.ImageAttachmentId is not { } attachmentId || await attachments.FindAsync(attachmentId, ct) is not { } stored)
        {
            return Result.Failure<ProductImageFile>(Error.NotFound("Picture"));
        }

        return Result.Success(new ProductImageFile(stored.Id, stored.ContentType, await attachments.OpenReadAsync(stored, ct)));
    }
}

internal static class ProductImageAccess
{
    public static async Task<Result<Product>> FindManageableAsync(
        CatalogDbContext db, ICurrentUserContext currentUser, IAccessScope accessScope, Guid productId, CancellationToken ct)
    {
        var grants = await accessScope.GetGrantsAsync(ct);
        var product = await db.Products.FirstOrDefaultAsync(p => p.TenantId == currentUser.TenantId && p.Id == productId, ct);
        if (product is null || !CatalogPolicy.CanSee(BranchScope.For(grants, CatalogPolicy.View), product.OwnerBranchId))
        {
            return Result.Failure<Product>(Error.NotFound("Item"));
        }

        return CatalogPolicy.CanManageItem(grants, product.OwnerBranchId) is { } denied
            ? Result.Failure<Product>(denied)
            : Result.Success(product);
    }
}
