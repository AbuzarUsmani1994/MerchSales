using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StockOrder.Infrastructure.Api;
using StockOrder.Modules.Catalog.Application;

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

    public sealed record UpdateCategoryRequest(string Name, int SortOrder, bool IsActive);

    [HttpPut("categories/{categoryId:guid}")]
    [Authorize(Policy = "Catalog.Manage")]
    public async Task<IActionResult> UpdateCategory(Guid categoryId, UpdateCategoryRequest request, CancellationToken ct) =>
        (await mediator.Send(new UpdateCategoryCommand(categoryId, request.Name, request.SortOrder, request.IsActive), ct)).ToActionResult();
}
