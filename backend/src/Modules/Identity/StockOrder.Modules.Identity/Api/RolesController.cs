using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StockOrder.Infrastructure.Api;
using StockOrder.Modules.Identity.Application.Roles;

namespace StockOrder.Modules.Identity.Api;

[ApiController]
[Route("api/v1/identity/roles")]
[Authorize(Policy = "Roles.Manage")]
public sealed class RolesController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var result = await mediator.Send(new GetRolesQuery(), ct);
        return result.ToActionResult();
    }

    [HttpPost]
    public async Task<IActionResult> Create(CreateRoleCommand command, CancellationToken ct)
    {
        var result = await mediator.Send(command, ct);
        return result.ToActionResult();
    }

    [HttpPut("{roleId:guid}/permissions")]
    public async Task<IActionResult> SetPermissions(Guid roleId, [FromBody] IReadOnlyList<string> permissionCodes, CancellationToken ct)
    {
        var result = await mediator.Send(new SetRolePermissionsCommand(roleId, permissionCodes), ct);
        return result.ToActionResult();
    }

    [HttpGet("permissions")]
    public async Task<IActionResult> GetPermissionCatalogue(CancellationToken ct)
    {
        var result = await mediator.Send(new GetPermissionCatalogueQuery(), ct);
        return result.ToActionResult();
    }
}
