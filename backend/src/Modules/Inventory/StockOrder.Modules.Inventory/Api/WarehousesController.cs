using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StockOrder.Infrastructure.Api;
using StockOrder.Modules.Inventory.Application.Warehouses;

namespace StockOrder.Modules.Inventory.Api;

/// <summary>A branch manager sees and adds warehouses at their own branch; the handlers check which.</summary>
[ApiController]
[Route("api/v1/inventory/warehouses")]
[BranchScoped]
public sealed class WarehousesController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = "Branches.Manage")]
    public async Task<IActionResult> List([FromQuery] Guid? branchId, CancellationToken ct)
    {
        var result = await mediator.Send(new GetWarehousesQuery(branchId), ct);
        return result.ToActionResult();
    }

    [HttpPost]
    [Authorize(Policy = "Branches.Manage")]
    public async Task<IActionResult> Create(CreateWarehouseCommand command, CancellationToken ct)
    {
        var result = await mediator.Send(command, ct);
        return result.ToActionResult();
    }
}
