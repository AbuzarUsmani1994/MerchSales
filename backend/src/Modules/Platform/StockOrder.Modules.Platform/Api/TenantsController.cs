using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StockOrder.Infrastructure.Api;
using StockOrder.Modules.Platform.Application.Tenants;

namespace StockOrder.Modules.Platform.Api;

[ApiController]
[Route("api/v1/platform/tenants")]
[Authorize(Policy = "Platform.Tenants.Manage")]
public sealed class TenantsController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var result = await mediator.Send(new ListTenantsQuery(), ct);
        return result.ToActionResult();
    }

    [HttpPost]
    public async Task<IActionResult> Create(ProvisionTenantCommand command, CancellationToken ct)
    {
        var result = await mediator.Send(command, ct);
        return result.ToActionResult();
    }

    [HttpPost("{tenantId:guid}/suspend")]
    public async Task<IActionResult> Suspend(Guid tenantId, CancellationToken ct)
    {
        var result = await mediator.Send(new SetTenantStatusCommand(tenantId, "Suspended"), ct);
        return result.ToActionResult();
    }

    [HttpPost("{tenantId:guid}/activate")]
    public async Task<IActionResult> Activate(Guid tenantId, CancellationToken ct)
    {
        var result = await mediator.Send(new SetTenantStatusCommand(tenantId, "Active"), ct);
        return result.ToActionResult();
    }
}
