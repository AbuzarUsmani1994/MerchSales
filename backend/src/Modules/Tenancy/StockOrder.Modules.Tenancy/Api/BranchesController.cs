using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StockOrder.Infrastructure.Api;
using StockOrder.Infrastructure.Idempotency;
using StockOrder.Modules.Tenancy.Application.Branches;

namespace StockOrder.Modules.Tenancy.Api;

[ApiController]
[Route("api/v1/tenancy/branches")]
public sealed class BranchesController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = "Branches.Manage")]
    [BranchScoped]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var result = await mediator.Send(new GetBranchesQuery(), ct);
        return result.ToActionResult();
    }

    /// <summary>Tenant-wide (not <see cref="BranchScopedAttribute"/>): branch managers can't create branches.</summary>
    [HttpPost]
    [Authorize(Policy = "Branches.Manage")]
    public async Task<IActionResult> Create(CreateBranchCommand command, CancellationToken ct)
    {
        var result = await mediator.Send(command, ct);
        return result.ToActionResult();
    }

    /// <summary>The branch's customer menu link (restaurants): the code behind the QR waiters show.</summary>
    [HttpGet("{branchId:guid}/public-menu")]
    [Authorize(Policy = "Branches.Manage")]
    [BranchScoped]
    public async Task<IActionResult> GetPublicMenu(Guid branchId, CancellationToken ct) =>
        (await mediator.Send(new GetPublicMenuLinkQuery(branchId), ct)).ToActionResult();

    /// <summary>Switches the customer menu on with a new link; an earlier QR for the branch stops working.</summary>
    [HttpPost("{branchId:guid}/public-menu/new-link")]
    [Authorize(Policy = "Branches.Manage")]
    [BranchScoped]
    [Idempotent]
    public async Task<IActionResult> NewPublicMenuLink(Guid branchId, CancellationToken ct) =>
        (await mediator.Send(new SetPublicMenuLinkCommand(branchId, On: true), ct)).ToActionResult();

    [HttpPost("{branchId:guid}/public-menu/switch-off")]
    [Authorize(Policy = "Branches.Manage")]
    [BranchScoped]
    [Idempotent]
    public async Task<IActionResult> SwitchOffPublicMenu(Guid branchId, CancellationToken ct) =>
        (await mediator.Send(new SetPublicMenuLinkCommand(branchId, On: false), ct)).ToActionResult();
}
