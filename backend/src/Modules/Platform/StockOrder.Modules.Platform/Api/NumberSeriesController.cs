using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StockOrder.Infrastructure.Api;
using StockOrder.Modules.Platform.Application.NumberSeries;

namespace StockOrder.Modules.Platform.Api;

[ApiController]
[Route("api/v1/platform/number-series")]
[Authorize(Policy = "Settings.Manage")]
public sealed class NumberSeriesController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var result = await mediator.Send(new GetNumberSeriesQuery(), ct);
        return result.ToActionResult();
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateNumberSeriesBody body, CancellationToken ct)
    {
        var result = await mediator.Send(new UpdateNumberSeriesCommand(id, body.Prefix, body.PaddingLength), ct);
        return result.ToActionResult();
    }

    public sealed record UpdateNumberSeriesBody(string Prefix, byte PaddingLength);
}
