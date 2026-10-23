using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StockOrder.Infrastructure.Api;
using StockOrder.Modules.Tenancy.Application.Features;

namespace StockOrder.Modules.Tenancy.Api;

[ApiController]
[Route("api/v1/tenancy/features")]
[Authorize(Policy = "Settings.Manage")]
public sealed class FeaturesController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var result = await mediator.Send(new GetFeaturesQuery(), ct);
        return result.ToActionResult();
    }

    [HttpPut("{featureKey}")]
    public async Task<IActionResult> Set(string featureKey, [FromBody] bool isEnabled, CancellationToken ct)
    {
        var result = await mediator.Send(new SetFeatureCommand(featureKey, isEnabled), ct);
        return result.ToActionResult();
    }
}
