using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
using StockOrder.Infrastructure.Database;
using StockOrder.Infrastructure.Sync;
using StockOrder.Modules.Catalog.Domain;
using StockOrder.Modules.Catalog.Infrastructure;
using StockOrder.Modules.Inventory.Contracts;
using StockOrder.Modules.Tenancy.Contracts;
using StockOrder.SharedKernel.Context;
using StockOrder.SharedKernel.Errors;
using StockOrder.SharedKernel.Ids;
using StockOrder.SharedKernel.Results;
using StockOrder.SharedKernel.Time;

namespace StockOrder.Modules.Catalog.Application;

/// <param name="OwnerBranchId">NULL = shared menu item (Owner); a branch = sold only there (its manager).</param>
/// <param name="Sku">Optional; generated as ITEM-0001, ITEM-0002... when empty.</param>
public sealed record CreateProductCommand(
    string Name,
    string? ShortName,
    string? Description,
    string? Sku,
    Guid? CategoryId,
    Guid ProductTypeId,
    Guid UomId,
    decimal Price,
    Guid? OwnerBranchId) : ICommand<Result<Guid>>;

public sealed class CreateProductCommandValidator : AbstractValidator<CreateProductCommand>
{
    public CreateProductCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.ShortName).MaximumLength(60);
        RuleFor(x => x.Description).MaximumLength(2000);
        RuleFor(x => x.Sku).MaximumLength(64);
        RuleFor(x => x.ProductTypeId).NotEmpty();
        RuleFor(x => x.UomId).NotEmpty();
    }
}

/// <summary>
/// Adds a menu item with everything the spec says every product gets (section 8.1, 8.2, 11):
/// its base-unit conversion row and default batch, plus its price — in the default price list
/// for a shared item, in the branch's own price list for a branch-only item.
/// </summary>
public sealed class CreateProductCommandHandler(
    CatalogDbContext db,
    ICurrentUserContext currentUser,
    IAccessScope accessScope,
    IBranchQueries branchQueries,
    IProductStockSetup stockSetup,
    IClock clock,
    IIdGenerator idGenerator)
    : ICommandHandler<CreateProductCommand, Result<Guid>>
{
    public async ValueTask<Result<Guid>> Handle(CreateProductCommand command, CancellationToken ct)
    {
        var tenantId = currentUser.TenantId;
        var grants = await accessScope.GetGrantsAsync(ct);
        if (CatalogPolicy.CanManageItem(grants, command.OwnerBranchId) is { } denied)
        {
            return Result.Failure<Guid>(denied);
        }

        if (CatalogPolicy.ValidatePrice(command.Price) is { } badPrice)
        {
            return Result.Failure<Guid>(badPrice);
        }

        BranchSummary? ownerBranch = null;
        if (command.OwnerBranchId is { } ownerId)
        {
            ownerBranch = await branchQueries.GetBranchAsync(tenantId, ownerId, ct);
            if (ownerBranch is null || !ownerBranch.IsActive)
            {
                return Result.Failure<Guid>(Error.NotFound("Branch"));
            }
        }

        var type = await db.ProductTypes.FirstOrDefaultAsync(t => t.TenantId == tenantId && t.Id == command.ProductTypeId && t.IsActive, ct);
        var uomExists = await db.Units.AnyAsync(u => u.TenantId == tenantId && u.Id == command.UomId && u.IsActive, ct);
        if (type is null || !uomExists)
        {
            return Result.Failure<Guid>(Error.NotFound("Item type or unit"));
        }

        // Section 3: serial-number tracking exists in the schema but is out of scope.
        if (type.DefaultTracking == "Serial")
        {
            return Result.Failure<Guid>(Error.Validation("Serial-number tracking isn't supported."));
        }

        if (command.CategoryId is { } categoryId && !await db.Categories.AnyAsync(c => c.TenantId == tenantId && c.Id == categoryId, ct))
        {
            return Result.Failure<Guid>(Error.NotFound("Category"));
        }

        var priceListId = await ResolvePriceListAsync(ownerBranch, ct);
        if (priceListId.IsFailure)
        {
            return Result.Failure<Guid>(priceListId.Error);
        }

        var sku = string.IsNullOrWhiteSpace(command.Sku) ? await NextSkuAsync(ct) : command.Sku.Trim();
        if (await db.Products.AnyAsync(p => p.TenantId == tenantId && p.Sku == sku, ct))
        {
            return Result.Failure<Guid>(Error.Conflict(ErrorCodes.Duplicate, $"Code '{sku}' is already used by another item."));
        }

        var now = clock.UtcNow;
        var product = Product.Create(
            tenantId, idGenerator.NewId(), sku, command.Name.Trim(), Clean(command.ShortName), Clean(command.Description),
            type, command.CategoryId, command.UomId, command.OwnerBranchId, now, currentUser.UserId);
        db.Products.Add(product);

        // Saved first: there are no EF relationships to order inserts by, and the rows below
        // reference the product. All in the request's one transaction.
        await db.SaveChangesAsync(ct);

        db.Conversions.Add(ProductUomConversion.Base(tenantId, idGenerator.NewId(), product.Id, product.BaseUomId));
        db.PriceListItems.Add(PriceListItem.Create(tenantId, idGenerator.NewId(), priceListId.Value, product.Id, product.PricingUomId, command.Price));
        await db.SaveChangesAsync(ct);

        await stockSetup.CreateDefaultBatchAsync(product.Id, ct);

        return Result.Success(product.Id);
    }

    private async Task<Result<Guid>> ResolvePriceListAsync(BranchSummary? ownerBranch, CancellationToken ct)
    {
        if (ownerBranch is null)
        {
            var defaultId = await db.PriceLists
                .Where(l => l.TenantId == currentUser.TenantId && l.IsDefault)
                .Select(l => (Guid?)l.Id)
                .FirstOrDefaultAsync(ct);
            return defaultId is { } id
                ? Result.Success(id)
                : Result.Failure<Guid>(Error.Validation(
                    "This company has no default price list yet. A platform administrator can add it with \"Add missing template data\"."));
        }

        return Result.Success(await BranchPriceLists.GetOrCreateAsync(db, currentUser, idGenerator, clock, ownerBranch, ct));
    }

    private async Task<string> NextSkuAsync(CancellationToken ct)
    {
        const string prefix = "ITEM-";
        var existing = await db.Products
            .Where(p => p.TenantId == currentUser.TenantId && p.Sku.StartsWith(prefix))
            .Select(p => p.Sku)
            .ToListAsync(ct);

        var highest = existing
            .Select(s => int.TryParse(s.AsSpan(prefix.Length), out var n) ? n : 0)
            .DefaultIfEmpty(0)
            .Max();
        return $"{prefix}{highest + 1:D4}";
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <param name="Price">The base price (shared item) or the item's own price (branch-only item); null = unchanged.</param>
public sealed record UpdateProductCommand(
    Guid Id, string Name, string? ShortName, string? Description, Guid? CategoryId, bool IsActive, decimal? Price) : ICommand<Result>;

public sealed class UpdateProductCommandValidator : AbstractValidator<UpdateProductCommand>
{
    public UpdateProductCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.ShortName).MaximumLength(60);
        RuleFor(x => x.Description).MaximumLength(2000);
    }
}

/// <summary>Never deletes: items referenced by orders stay; switching IsActive off hides them (section 7).</summary>
public sealed class UpdateProductCommandHandler(
    CatalogDbContext db,
    ICurrentUserContext currentUser,
    IAccessScope accessScope,
    IBranchQueries branchQueries,
    IClock clock,
    IIdGenerator idGenerator)
    : ICommandHandler<UpdateProductCommand, Result>
{
    public async ValueTask<Result> Handle(UpdateProductCommand command, CancellationToken ct)
    {
        var tenantId = currentUser.TenantId;
        var product = await db.Products.FirstOrDefaultAsync(p => p.TenantId == tenantId && p.Id == command.Id, ct);
        var grants = await accessScope.GetGrantsAsync(ct);
        if (product is null || !CatalogPolicy.CanSee(BranchScope.For(grants, CatalogPolicy.View), product.OwnerBranchId))
        {
            return Result.Failure(Error.NotFound("Item"));
        }

        if (CatalogPolicy.CanManageItem(grants, product.OwnerBranchId) is { } denied)
        {
            return Result.Failure(denied);
        }

        if (command.CategoryId is { } categoryId && !await db.Categories.AnyAsync(c => c.TenantId == tenantId && c.Id == categoryId, ct))
        {
            return Result.Failure(Error.NotFound("Category"));
        }

        product.Update(command.Name.Trim(), Blank(command.ShortName), Blank(command.Description), command.CategoryId, command.IsActive,
            clock.UtcNow, currentUser.UserId);

        if (command.Price is { } price)
        {
            if (CatalogPolicy.ValidatePrice(price) is { } badPrice)
            {
                return Result.Failure(badPrice);
            }

            Guid priceListId;
            if (product.OwnerBranchId is { } ownerId)
            {
                var branch = await branchQueries.GetBranchAsync(tenantId, ownerId, ct);
                priceListId = await BranchPriceLists.GetOrCreateAsync(db, currentUser, idGenerator, clock, branch!, ct);
            }
            else
            {
                priceListId = await db.PriceLists.Where(l => l.TenantId == tenantId && l.IsDefault).Select(l => l.Id).FirstAsync(ct);
            }

            await BranchPriceLists.SetPriceAsync(db, currentUser, idGenerator, priceListId, product, price, ct);
        }

        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <param name="Price">This branch's price; null = charge the base price (not allowed for branch-only items).</param>
public sealed record SetBranchProductCommand(Guid ProductId, Guid BranchId, decimal? Price, bool IsAvailable) : ICommand<Result>;

/// <summary>A branch manager's control over a menu item at their branch: price and availability.</summary>
public sealed class SetBranchProductCommandHandler(
    CatalogDbContext db,
    ICurrentUserContext currentUser,
    IAccessScope accessScope,
    IBranchQueries branchQueries,
    IClock clock,
    IIdGenerator idGenerator,
    IUnitOfWork unitOfWork)
    : ICommandHandler<SetBranchProductCommand, Result>
{
    public async ValueTask<Result> Handle(SetBranchProductCommand command, CancellationToken ct)
    {
        var tenantId = currentUser.TenantId;
        var grants = await accessScope.GetGrantsAsync(ct);
        var product = await db.Products.FirstOrDefaultAsync(p => p.TenantId == tenantId && p.Id == command.ProductId, ct);
        if (product is null || !CatalogPolicy.CanSee(BranchScope.For(grants, CatalogPolicy.View), product.OwnerBranchId))
        {
            return Result.Failure(Error.NotFound("Item"));
        }

        if (CatalogPolicy.CanSetBranchPricing(grants, product.OwnerBranchId, command.BranchId) is { } denied)
        {
            return Result.Failure(denied);
        }

        var branch = await branchQueries.GetBranchAsync(tenantId, command.BranchId, ct);
        if (branch is null)
        {
            return Result.Failure(Error.NotFound("Branch"));
        }

        var listId = await BranchPriceLists.GetOrCreateAsync(db, currentUser, idGenerator, clock, branch, ct);
        if (command.Price is { } price)
        {
            if (CatalogPolicy.ValidatePrice(price) is { } badPrice)
            {
                return Result.Failure(badPrice);
            }

            await BranchPriceLists.SetPriceAsync(db, currentUser, idGenerator, listId, product, price, ct);
        }
        else if (product.OwnerBranchId is not null)
        {
            return Result.Failure(Error.Validation("An item sold only at this branch needs its own price."));
        }
        else
        {
            var existing = await db.PriceListItems.Where(i => i.TenantId == tenantId && i.PriceListId == listId && i.ProductId == product.Id).ToListAsync(ct);
            db.PriceListItems.RemoveRange(existing);
            await SyncTombstones.WriteAsync(unitOfWork, tenantId, "priceListItems", existing.Select(i => i.Id), ct);
        }

        var availability = await db.ProductBranches.FirstOrDefaultAsync(
            pb => pb.TenantId == tenantId && pb.ProductId == product.Id && pb.BranchId == command.BranchId, ct);
        if (availability is null)
        {
            db.ProductBranches.Add(ProductBranch.Create(
                tenantId, idGenerator.NewId(), product.Id, command.BranchId, command.IsAvailable, clock.UtcNow, currentUser.UserId));
        }
        else
        {
            availability.SetAvailable(command.IsAvailable, clock.UtcNow, currentUser.UserId);
        }

        await db.SaveChangesAsync(ct);
        return Result.Success();
    }
}

/// <summary>Each branch's own price list (014_BranchCatalog.sql), created the first time it's needed.</summary>
internal static class BranchPriceLists
{
    public static async Task<Guid> GetOrCreateAsync(
        CatalogDbContext db, ICurrentUserContext currentUser, IIdGenerator idGenerator, IClock clock, BranchSummary branch, CancellationToken ct)
    {
        var existing = await db.PriceLists
            .Where(l => l.TenantId == currentUser.TenantId && l.BranchId == branch.Id)
            .Select(l => (Guid?)l.Id)
            .FirstOrDefaultAsync(ct);
        if (existing is { } id)
        {
            return id;
        }

        var list = PriceList.ForBranch(currentUser.TenantId, idGenerator.NewId(), branch.Id, $"{branch.Code} prices", clock.UtcNow, currentUser.UserId);
        db.PriceLists.Add(list);
        await db.SaveChangesAsync(ct);
        return list.Id;
    }

    /// <summary>Upserts the menu row (MinQuantity 1, no date range, the product's pricing unit).</summary>
    public static async Task SetPriceAsync(
        CatalogDbContext db, ICurrentUserContext currentUser, IIdGenerator idGenerator, Guid priceListId, Product product, decimal price, CancellationToken ct)
    {
        var uomId = product.PricingUomId;
        var item = await db.PriceListItems.FirstOrDefaultAsync(
            i => i.TenantId == currentUser.TenantId && i.PriceListId == priceListId && i.ProductId == product.Id &&
                 i.UomId == uomId && i.MinQuantity == 1m && i.ValidFrom == null, ct);
        if (item is null)
        {
            db.PriceListItems.Add(PriceListItem.Create(currentUser.TenantId, idGenerator.NewId(), priceListId, product.Id, uomId, price));
        }
        else
        {
            item.SetPrice(price);
        }
    }
}

public sealed record CreateCategoryCommand(string Name, int SortOrder) : ICommand<Result<Guid>>;

public sealed class CreateCategoryCommandValidator : AbstractValidator<CreateCategoryCommand>
{
    public CreateCategoryCommandValidator() => RuleFor(x => x.Name).NotEmpty().MaximumLength(150);
}

/// <summary>Categories are shared by all branches; the endpoint needs an all-branches Catalog.Manage role.</summary>
public sealed class CreateCategoryCommandHandler(CatalogDbContext db, ICurrentUserContext currentUser, IClock clock, IIdGenerator idGenerator)
    : ICommandHandler<CreateCategoryCommand, Result<Guid>>
{
    public async ValueTask<Result<Guid>> Handle(CreateCategoryCommand command, CancellationToken ct)
    {
        var name = command.Name.Trim();
        if (await db.Categories.AnyAsync(c => c.TenantId == currentUser.TenantId && c.Name == name, ct))
        {
            return Result.Failure<Guid>(Error.Conflict(ErrorCodes.Duplicate, $"Category '{name}' already exists."));
        }

        var category = Category.Create(currentUser.TenantId, idGenerator.NewId(), name, command.SortOrder, clock.UtcNow, currentUser.UserId);
        db.Categories.Add(category);
        await db.SaveChangesAsync(ct);
        return Result.Success(category.Id);
    }
}

public sealed record UpdateCategoryCommand(Guid Id, string Name, int SortOrder, bool IsActive) : ICommand<Result>;

public sealed class UpdateCategoryCommandValidator : AbstractValidator<UpdateCategoryCommand>
{
    public UpdateCategoryCommandValidator() => RuleFor(x => x.Name).NotEmpty().MaximumLength(150);
}

public sealed class UpdateCategoryCommandHandler(CatalogDbContext db, ICurrentUserContext currentUser, IClock clock)
    : ICommandHandler<UpdateCategoryCommand, Result>
{
    public async ValueTask<Result> Handle(UpdateCategoryCommand command, CancellationToken ct)
    {
        var category = await db.Categories.FirstOrDefaultAsync(c => c.TenantId == currentUser.TenantId && c.Id == command.Id, ct);
        if (category is null)
        {
            return Result.Failure(Error.NotFound("Category"));
        }

        var name = command.Name.Trim();
        if (await db.Categories.AnyAsync(c => c.TenantId == currentUser.TenantId && c.Name == name && c.Id != command.Id, ct))
        {
            return Result.Failure(Error.Conflict(ErrorCodes.Duplicate, $"Category '{name}' already exists."));
        }

        category.Update(name, command.SortOrder, command.IsActive, clock.UtcNow, currentUser.UserId);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }
}
