using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using StockOrder.Infrastructure.Api;
using StockOrder.Modules.Identity.Application.Auth;

namespace StockOrder.Modules.Identity.Api;

/// <summary>Every signed-in endpoint here acts only on the caller's own account, so a branch-level role is enough.</summary>
[ApiController]
[Route("api/v1/auth")]
[BranchScoped]
public sealed class AuthController(IMediator mediator) : ControllerBase
{
    public sealed record LoginRequest(string TenantCode, string Username, string Password, string? TotpCode, LoginDevice? Device);

    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> Login(LoginRequest request, CancellationToken ct)
    {
        var command = new LoginCommand(
            request.TenantCode, request.Username, request.Password, request.TotpCode, request.Device,
            HttpContext.Connection.RemoteIpAddress?.ToString());

        var result = await mediator.Send(command, ct);
        return result.ToActionResult();
    }

    public sealed record RefreshRequest(string RefreshToken);

    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<IActionResult> Refresh(RefreshRequest request, CancellationToken ct)
    {
        var result = await mediator.Send(
            new RefreshTokenCommand(request.RefreshToken, HttpContext.Connection.RemoteIpAddress?.ToString()), ct);
        return result.ToActionResult();
    }

    public sealed record LogoutRequest(string RefreshToken);

    [HttpPost("logout")]
    [Authorize(Policy = "Identity.Self.Manage")]
    public async Task<IActionResult> Logout(LogoutRequest request, CancellationToken ct)
    {
        var result = await mediator.Send(new LogoutCommand(request.RefreshToken), ct);
        return result.ToActionResult();
    }

    public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

    [HttpPost("change-password")]
    [Authorize(Policy = "Identity.Self.Manage")]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request, CancellationToken ct)
    {
        var result = await mediator.Send(new ChangePasswordCommand(request.CurrentPassword, request.NewPassword), ct);
        return result.ToActionResult();
    }

    [HttpGet("me")]
    [Authorize(Policy = "Identity.Self.Manage")]
    public async Task<IActionResult> Me(CancellationToken ct)
    {
        var result = await mediator.Send(new MeQuery(), ct);
        return result.ToActionResult();
    }

    [HttpPost("mfa/enroll")]
    [Authorize(Policy = "Identity.Self.Manage")]
    public async Task<IActionResult> EnrollMfa(CancellationToken ct)
    {
        var result = await mediator.Send(new EnrollMfaCommand(), ct);
        return result.ToActionResult();
    }

    public sealed record ConfirmMfaRequest(string TotpCode);

    [HttpPost("mfa/confirm")]
    [Authorize(Policy = "Identity.Self.Manage")]
    public async Task<IActionResult> ConfirmMfa(ConfirmMfaRequest request, CancellationToken ct)
    {
        var result = await mediator.Send(new ConfirmMfaCommand(request.TotpCode), ct);
        return result.ToActionResult();
    }

    public sealed record SetPinRequest(string Pin);

    [HttpPost("pin")]
    [Authorize(Policy = "Identity.Self.Manage")]
    public async Task<IActionResult> SetPin(SetPinRequest request, CancellationToken ct)
    {
        var result = await mediator.Send(new Application.Users.SetPinCommand(request.Pin), ct);
        return result.ToActionResult();
    }
}
