using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StockOrder.Infrastructure.Api;
using StockOrder.Modules.Platform.Application.AppVersions;

namespace StockOrder.Modules.Platform.Api;

[ApiController]
[Route("api/v1/platform/app-versions")]
[Authorize(Policy = "Platform.AppVersion.Manage")]
public sealed class AppVersionPoliciesController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var result = await mediator.Send(new GetAppVersionPoliciesQuery(), ct);
        return result.ToActionResult();
    }

    [HttpPut("{platform}")]
    public async Task<IActionResult> Set(string platform, [FromBody] SetAppVersionPolicyBody body, CancellationToken ct)
    {
        var result = await mediator.Send(new SetAppVersionPolicyCommand(platform, body.MinVersion, body.LatestVersion, body.StoreUrl), ct);
        return result.ToActionResult();
    }

    public sealed record SetAppVersionPolicyBody(string MinVersion, string LatestVersion, string? StoreUrl);
}
