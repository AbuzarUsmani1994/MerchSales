using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using StockOrder.Infrastructure.Api;
using StockOrder.Modules.Catalog.Application;
using StockOrder.SharedKernel.Errors;
using StockOrder.SharedKernel.Results;

namespace StockOrder.Modules.Catalog.Api;

/// <summary>
/// The menu (section 13 "Catalog"). Branch-scoped endpoints let a branch manager work on their
/// branch's items, prices and availability; the handlers enforce which (CatalogPolicy).
/// Categories are shared, so changing them needs an all-branches role.
/// </summary>
[ApiController]
[Route("api/v1/catalog")]
public sealed class CatalogController(IMediator mediator) : ControllerBase
{
    [HttpGet("lookups")]
    [Authorize(Policy = "Catalog.View")]
    [BranchScoped]
    public async Task<IActionResult> Lookups(CancellationToken ct) =>
        (await mediator.Send(new GetCatalogLookupsQuery(), ct)).ToActionResult();

    [HttpGet("products")]
    [Authorize(Policy = "Catalog.View")]
    [BranchScoped]
    public async Task<IActionResult> Products(
        [FromQuery] Guid? branchId, [FromQuery] string? search, [FromQuery] Guid? categoryId, CancellationToken ct) =>
        (await mediator.Send(new GetMenuQuery(branchId, search, categoryId), ct)).ToActionResult();

    [HttpPost("products")]
    [Authorize(Policy = "Catalog.Manage")]
    [BranchScoped]
    public async Task<IActionResult> CreateProduct(CreateProductCommand command, CancellationToken ct) =>
        (await mediator.Send(command, ct)).ToActionResult();

    public sealed record UpdateProductRequest(string Name, string? ShortName, string? Description, Guid? CategoryId, bool IsActive, decimal? Price);

    [HttpPut("products/{productId:guid}")]
    [Authorize(Policy = "Catalog.Manage")]
    [BranchScoped]
    public async Task<IActionResult> UpdateProduct(Guid productId, UpdateProductRequest request, CancellationToken ct) =>
        (await mediator.Send(new UpdateProductCommand(
            productId, request.Name, request.ShortName, request.Description, request.CategoryId, request.IsActive, request.Price), ct)).ToActionResult();

    public sealed record BranchProductRequest(decimal? Price, bool IsAvailable);

    [HttpPut("products/{productId:guid}/branches/{branchId:guid}")]
    [Authorize(Policy = "Pricing.Manage")]
    [BranchScoped]
    public async Task<IActionResult> SetBranchProduct(Guid productId, Guid branchId, BranchProductRequest request, CancellationToken ct) =>
        (await mediator.Send(new SetBranchProductCommand(productId, branchId, request.Price, request.IsAvailable), ct)).ToActionResult();

    [HttpPost("categories")]
    [Authorize(Policy = "Catalog.Manage")]
    public async Task<IActionResult> CreateCategory(CreateCategoryCommand command, CancellationToken ct) =>
        (await mediator.Send(command, ct)).ToActionResult();

    /// <param name="SendToKitchen">Missing = true, so older clients keep their categories on the kitchen screen.</param>
    public sealed record UpdateCategoryRequest(string Name, int SortOrder, bool IsActive, bool? SendToKitchen);

    /// <summary>Upload or replace an item's picture (multipart form, field "file").</summary>
    [HttpPut("products/{productId:guid}/image")]
    [Authorize(Policy = "Catalog.Manage")]
    [BranchScoped]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(ProductImageRules.MaxBytes + 64 * 1024)]
    public async Task<IActionResult> SetProductImage(Guid productId, IFormFile? file, CancellationToken ct)
    {
        if (file is null)
        {
            return Result.Failure(Error.Validation("Choose a picture to upload.")).ToActionResult();
        }

        using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer, ct);
        return (await mediator.Send(new SetProductImageCommand(productId, file.FileName, buffer.ToArray()), ct)).ToActionResult();
    }

    [HttpDelete("products/{productId:guid}/image")]
    [Authorize(Policy = "Catalog.Manage")]
    [BranchScoped]
    public async Task<IActionResult> RemoveProductImage(Guid productId, CancellationToken ct) =>
        (await mediator.Send(new RemoveProductImageCommand(productId), ct)).ToActionResult();

    /// <summary>
    /// The picture itself. Callers add ?v={imageAttachmentId}: a new picture gets a new URL, so
    /// browsers and phones may keep each one for good.
    /// </summary>
    [HttpGet("products/{productId:guid}/image")]
    [Authorize(Policy = "Catalog.View")]
    [BranchScoped]
    public async Task<IActionResult> GetProductImage(Guid productId, CancellationToken ct)
    {
        var result = await mediator.Send(new GetProductImageQuery(productId), ct);
        if (result.IsFailure)
        {
            return result.ToActionResult();
        }

        Response.Headers.CacheControl = "private, max-age=31536000, immutable";
        Response.Headers.ETag = $"\"{result.Value.AttachmentId:N}\"";
        return File(result.Value.Content, result.Value.ContentType);
    }

    [HttpPut("categories/{categoryId:guid}")]
    [Authorize(Policy = "Catalog.Manage")]
    public async Task<IActionResult> UpdateCategory(Guid categoryId, UpdateCategoryRequest request, CancellationToken ct) =>
        (await mediator.Send(new UpdateCategoryCommand(categoryId, request.Name, request.SortOrder, request.IsActive, request.SendToKitchen ?? true), ct)).ToActionResult();
}
