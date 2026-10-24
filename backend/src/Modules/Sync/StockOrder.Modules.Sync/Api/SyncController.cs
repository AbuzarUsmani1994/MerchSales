using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StockOrder.Infrastructure.Api;
using StockOrder.Modules.Sync.Application;

namespace StockOrder.Modules.Sync.Api;

/// <summary>
/// Section 12 mobile sync. Mobile staff hold branch-level roles, so a branch grant of
/// Mobile.Use is enough; what each entity returns is scoped by its own source.
/// </summary>
[ApiController]
[Route("api/v1/sync")]
[Authorize(Policy = "Mobile.Use")]
[BranchScoped]
public sealed class SyncController(IMediator mediator) : ControllerBase
{
    [HttpGet("status")]
    public async Task<IActionResult> Status(CancellationToken ct)
    {
        var result = await mediator.Send(new SyncStatusQuery(), ct);
        return result.ToActionResult();
    }

    [HttpGet("pull")]
    public async Task<IActionResult> Pull([FromQuery] string? tokens, [FromQuery] int limit = SyncPullQueryHandler.MaxLimit, CancellationToken ct = default)
    {
        var result = await mediator.Send(new SyncPullQuery(tokens, limit), ct);
        return result.ToActionResult();
    }

    [HttpPost("push")]
    public async Task<IActionResult> Push(SyncPushCommand command, CancellationToken ct)
    {
        var result = await mediator.Send(command, ct);
        return result.ToActionResult();
    }
}
