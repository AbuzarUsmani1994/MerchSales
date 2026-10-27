using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StockOrder.Infrastructure.Api;
using StockOrder.Infrastructure.Idempotency;
using StockOrder.Modules.Sales.Application;

namespace StockOrder.Modules.Sales.Api;

/// <summary>
/// Visit rewards (docs/decisions.md "Visit rewards"). The cashier looks a phone up and, where
/// allowed, adds a member; owners and managers keep the members list and the switches. Each
/// handler checks the branch and who may do what; the policies here are the outer gate.
/// </summary>
[ApiController]
[Route("api/v1/sales/loyalty")]
public sealed class LoyaltyController(IMediator mediator) : ControllerBase
{
    [HttpGet("branch-status")]
    [Authorize(Policy = SalesPermissions.OrderFulfil)]
    [BranchScoped]
    public async Task<IActionResult> BranchStatus([FromQuery] Guid branchId, CancellationToken ct) =>
        (await mediator.Send(new GetLoyaltyBranchStatusQuery(branchId), ct)).ToActionResult();

    [HttpGet("lookup")]
    [Authorize(Policy = SalesPermissions.OrderFulfil)]
    [BranchScoped]
    public async Task<IActionResult> Lookup([FromQuery] string phone, [FromQuery] Guid branchId, CancellationToken ct) =>
        (await mediator.Send(new LookupLoyaltyMemberQuery(phone ?? string.Empty, branchId), ct)).ToActionResult();

    public sealed record AddMemberRequest(string Phone, string? Name, Guid? BranchId);

    /// <summary>Cashiers reach this too (OrderFulfil); the handler allows them only where a manager switched it on.</summary>
    [HttpPost("members")]
    [Authorize(Policy = SalesPermissions.OrderFulfil)]
    [BranchScoped]
    [Idempotent]
    public async Task<IActionResult> AddMember(AddMemberRequest request, CancellationToken ct) =>
        (await mediator.Send(new AddLoyaltyMemberCommand(request.Phone ?? string.Empty, request.Name, request.BranchId), ct)).ToActionResult();

    [HttpGet("members")]
    [Authorize(Policy = LoyaltyPermissions.Manage)]
    [BranchScoped]
    public async Task<IActionResult> Members([FromQuery] string? search, [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default) =>
        (await mediator.Send(new ListLoyaltyMembersQuery(search, page, pageSize), ct)).ToActionResult();

    public sealed record UpdateMemberRequest(string? Name, string? Status);

    [HttpPatch("members/{memberId:guid}")]
    [Authorize(Policy = LoyaltyPermissions.Manage)]
    [BranchScoped]
    public async Task<IActionResult> UpdateMember(Guid memberId, UpdateMemberRequest request, CancellationToken ct) =>
        (await mediator.Send(new UpdateLoyaltyMemberCommand(memberId, request.Name, request.Status), ct)).ToActionResult();

    [HttpGet("settings")]
    [Authorize(Policy = LoyaltyPermissions.Manage)]
    [BranchScoped]
    public async Task<IActionResult> Settings(CancellationToken ct) =>
        (await mediator.Send(new GetLoyaltySettingsQuery(), ct)).ToActionResult();

    /// <summary>The company's numbers: the owner only (an all-branches Settings.Manage).</summary>
    [HttpPut("settings/rules")]
    [Authorize(Policy = LoyaltyPermissions.Settings)]
    public async Task<IActionResult> SaveRules(SaveLoyaltyRulesCommand command, CancellationToken ct) =>
        (await mediator.Send(command, ct)).ToActionResult();

    public sealed record SetBranchRequest(bool? IsEnabled, bool? CashierCanAddMembers);

    [HttpPut("settings/branches/{branchId:guid}")]
    [Authorize(Policy = LoyaltyPermissions.Manage)]
    [BranchScoped]
    public async Task<IActionResult> SetBranch(Guid branchId, SetBranchRequest request, CancellationToken ct) =>
        (await mediator.Send(new SetLoyaltyBranchCommand(branchId, request.IsEnabled, request.CashierCanAddMembers), ct)).ToActionResult();
}
