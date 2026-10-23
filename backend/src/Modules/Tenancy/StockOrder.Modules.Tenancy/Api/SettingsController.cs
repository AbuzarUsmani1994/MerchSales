using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StockOrder.Infrastructure.Api;
using StockOrder.Modules.Tenancy.Application.Settings;

namespace StockOrder.Modules.Tenancy.Api;

[ApiController]
[Route("api/v1/tenancy/settings")]
[Authorize(Policy = "Settings.Manage")]
public sealed class SettingsController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var result = await mediator.Send(new GetSettingsQuery(), ct);
        return result.ToActionResult();
    }

    [HttpPut("{settingKey}")]
    public async Task<IActionResult> Set(string settingKey, [FromBody] string value, CancellationToken ct)
    {
        var result = await mediator.Send(new SetSettingCommand(settingKey, value), ct);
        return result.ToActionResult();
    }
}
