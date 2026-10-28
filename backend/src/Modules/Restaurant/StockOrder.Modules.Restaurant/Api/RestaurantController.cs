using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StockOrder.Infrastructure.Api;
using StockOrder.Infrastructure.Idempotency;
using StockOrder.Modules.Restaurant.Application;
using StockOrder.Modules.Restaurant.Domain;
using StockOrder.SharedKernel.Errors;
using StockOrder.SharedKernel.Results;

namespace StockOrder.Modules.Restaurant.Api;

/// <summary>Dining areas and tables (setup), and the waiter's live table map.</summary>
[ApiController]
[Route("api/v1/restaurant")]
public sealed class RestaurantController(IMediator mediator) : ControllerBase
{
    [HttpGet("areas")]
    [Authorize(Policy = RestaurantPermissions.TablesManage)]
    [BranchScoped]
    public async Task<IActionResult> Areas([FromQuery] Guid branchId, CancellationToken ct) =>
        (await mediator.Send(new ListDiningAreasQuery(branchId), ct)).ToActionResult();

    public sealed record AreaRequest(Guid BranchId, string Name, int SortOrder, bool? IsActive);

    [HttpPost("areas")]
    [Authorize(Policy = RestaurantPermissions.TablesManage)]
    [BranchScoped]
    [Idempotent]
    public async Task<IActionResult> CreateArea(AreaRequest request, CancellationToken ct) =>
        (await mediator.Send(new SaveDiningAreaCommand(null, request.BranchId, request.Name ?? string.Empty, request.SortOrder, true), ct)).ToActionResult();

    [HttpPut("areas/{areaId:guid}")]
    [Authorize(Policy = RestaurantPermissions.TablesManage)]
    [BranchScoped]
    public async Task<IActionResult> UpdateArea(Guid areaId, AreaRequest request, CancellationToken ct) =>
        (await mediator.Send(new SaveDiningAreaCommand(areaId, request.BranchId, request.Name ?? string.Empty, request.SortOrder, request.IsActive ?? true), ct))
            .ToActionResult();

    public sealed record TableRequest(Guid BranchId, Guid AreaId, string Name, int Seats, int SortOrder, bool? IsActive);

    [HttpPost("tables")]
    [Authorize(Policy = RestaurantPermissions.TablesManage)]
    [BranchScoped]
    [Idempotent]
    public async Task<IActionResult> CreateTable(TableRequest request, CancellationToken ct) =>
        (await mediator.Send(new SaveDiningTableCommand(
            null, request.BranchId, request.AreaId, request.Name ?? string.Empty, request.Seats, request.SortOrder, true), ct)).ToActionResult();

    [HttpPut("tables/{tableId:guid}")]
    [Authorize(Policy = RestaurantPermissions.TablesManage)]
    [BranchScoped]
    public async Task<IActionResult> UpdateTable(Guid tableId, TableRequest request, CancellationToken ct) =>
        (await mediator.Send(new SaveDiningTableCommand(
            tableId, request.BranchId, request.AreaId, request.Name ?? string.Empty, request.Seats, request.SortOrder, request.IsActive ?? true), ct))
            .ToActionResult();

    /// <summary>Every table with its open order, bill request and ready food, for the waiter's app.</summary>
    [HttpGet("table-map")]
    [Authorize(Policy = RestaurantPermissions.OrderCreate)]
    [BranchScoped]
    public async Task<IActionResult> TableMap([FromQuery] Guid branchId, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        return (await mediator.Send(new GetTableMapQuery(branchId), ct)).ToActionResult();
    }
}

/// <summary>The kitchen screen: the board, and its buttons on each ticket (If-Match with the ticket's ETag).</summary>
[ApiController]
[Route("api/v1/restaurant/kitchen")]
public sealed class KitchenController(IMediator mediator) : ControllerBase
{
    [HttpGet("board")]
    [Authorize(Policy = RestaurantPermissions.KitchenOperate)]
    [BranchScoped]
    public async Task<IActionResult> Board([FromQuery] Guid branchId, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        return (await mediator.Send(new GetKitchenBoardQuery(branchId), ct)).ToActionResult();
    }

    [HttpPost("tickets/{ticketId:guid}/start")]
    [Authorize(Policy = RestaurantPermissions.KitchenOperate)]
    [BranchScoped]
    [Idempotent]
    public Task<IActionResult> Start(Guid ticketId, CancellationToken ct) => Act(ticketId, KitchenAction.Start, ct);

    [HttpPost("tickets/{ticketId:guid}/ready")]
    [Authorize(Policy = RestaurantPermissions.KitchenOperate)]
    [BranchScoped]
    [Idempotent]
    public Task<IActionResult> Ready(Guid ticketId, CancellationToken ct) => Act(ticketId, KitchenAction.Ready, ct);

    /// <summary>Undo a mistaken Start or Ready.</summary>
    [HttpPost("tickets/{ticketId:guid}/back")]
    [Authorize(Policy = RestaurantPermissions.KitchenOperate)]
    [BranchScoped]
    [Idempotent]
    public Task<IActionResult> Back(Guid ticketId, CancellationToken ct) => Act(ticketId, KitchenAction.Back, ct);

    /// <summary>The food went out (served at the table or collected): also for waiters and cashiers.</summary>
    [HttpPost("tickets/{ticketId:guid}/served")]
    [Authorize(Policy = RestaurantPermissions.KitchenServe)]
    [BranchScoped]
    [Idempotent]
    public Task<IActionResult> Served(Guid ticketId, CancellationToken ct) => Act(ticketId, KitchenAction.Serve, ct);

    /// <summary>The waiter took everything that was ready for this order out to the table.</summary>
    [HttpPost("orders/{orderId:guid}/served")]
    [Authorize(Policy = RestaurantPermissions.KitchenServe)]
    [BranchScoped]
    [Idempotent]
    public async Task<IActionResult> ServeOrder(Guid orderId, CancellationToken ct) =>
        (await mediator.Send(new ServeReadyRoundsCommand(orderId), ct)).ToActionResult();

    private async Task<IActionResult> Act(Guid ticketId, KitchenAction action, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(Request.Headers.IfMatch))
        {
            return Result.Failure(Error.Validation("The 'If-Match' header with the ticket's ETag is required.")).ToActionResult();
        }

        return (await mediator.Send(new KitchenTicketActionCommand(ticketId, Request.Headers.IfMatch.ToString(), action), ct)).ToActionResult();
    }
}
