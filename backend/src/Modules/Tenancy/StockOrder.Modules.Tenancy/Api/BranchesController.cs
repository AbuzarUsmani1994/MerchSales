using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StockOrder.Infrastructure.Api;
using StockOrder.Modules.Tenancy.Application.Branches;

namespace StockOrder.Modules.Tenancy.Api;

[ApiController]
[Route("api/v1/tenancy/branches")]
public sealed class BranchesController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = "Branches.Manage")]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var result = await mediator.Send(new GetBranchesQuery(), ct);
        return result.ToActionResult();
    }

    [HttpPost]
    [Authorize(Policy = "Branches.Manage")]
    public async Task<IActionResult> Create(CreateBranchCommand command, CancellationToken ct)
    {
        var result = await mediator.Send(command, ct);
        return result.ToActionResult();
    }
}
