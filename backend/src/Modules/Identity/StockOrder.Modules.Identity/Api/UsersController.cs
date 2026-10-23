using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StockOrder.Infrastructure.Api;
using StockOrder.Modules.Identity.Application.Users;

namespace StockOrder.Modules.Identity.Api;

[ApiController]
[Route("api/v1/identity/users")]
public sealed class UsersController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = "Users.View")]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var result = await mediator.Send(new GetUsersQuery(), ct);
        return result.ToActionResult();
    }

    [HttpPost]
    [Authorize(Policy = "Users.Manage")]
    public async Task<IActionResult> Create(CreateUserCommand command, CancellationToken ct)
    {
        var result = await mediator.Send(command, ct);
        return result.ToActionResult();
    }

    [HttpPost("{userId:guid}/reset-password")]
    [Authorize(Policy = "Users.Manage")]
    public async Task<IActionResult> ResetPassword(Guid userId, CancellationToken ct)
    {
        var result = await mediator.Send(new ResetPasswordCommand(userId), ct);
        return result.ToActionResult();
    }

    [HttpPost("roles")]
    [Authorize(Policy = "Users.Manage")]
    public async Task<IActionResult> AssignRole(AssignUserRoleCommand command, CancellationToken ct)
    {
        var result = await mediator.Send(command, ct);
        return result.ToActionResult();
    }

    [HttpDelete("roles/{userRoleId:guid}")]
    [Authorize(Policy = "Users.Manage")]
    public async Task<IActionResult> RemoveRole(Guid userRoleId, CancellationToken ct)
    {
        var result = await mediator.Send(new RemoveUserRoleCommand(userRoleId), ct);
        return result.ToActionResult();
    }
}
