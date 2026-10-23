using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StockOrder.Infrastructure.Api;
using StockOrder.Modules.Identity.Application.Devices;

namespace StockOrder.Modules.Identity.Api;

[ApiController]
[Route("api/v1/identity/devices")]
[Authorize(Policy = "Devices.Manage")]
public sealed class DevicesController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var result = await mediator.Send(new GetDevicesQuery(), ct);
        return result.ToActionResult();
    }

    public sealed record RevokeRequest(bool RequestWipe);

    [HttpPost("{deviceId:guid}/revoke")]
    public async Task<IActionResult> Revoke(Guid deviceId, RevokeRequest request, CancellationToken ct)
    {
        var result = await mediator.Send(new RevokeDeviceCommand(deviceId, request.RequestWipe), ct);
        return result.ToActionResult();
    }
}
