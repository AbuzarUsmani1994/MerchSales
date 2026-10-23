using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
using StockOrder.Modules.Identity.Infrastructure;
using StockOrder.Modules.Identity.Infrastructure.Auth;
using StockOrder.SharedKernel.Context;
using StockOrder.SharedKernel.Errors;
using StockOrder.SharedKernel.Results;
using StockOrder.SharedKernel.Time;

namespace StockOrder.Modules.Identity.Application.Auth;

public sealed record EnrollMfaCommand : ICommand<Result<EnrollMfaResult>>;

public sealed record EnrollMfaResult(string OtpAuthUri);

/// <summary>Generates a secret and returns the otpauth:// URI for an authenticator app QR code; not yet enabled until confirmed.</summary>
public sealed class EnrollMfaCommandHandler(IdentityDbContext db, ICurrentUserContext currentUser, IMfaSecretProtector protector, IClock clock)
    : ICommandHandler<EnrollMfaCommand, Result<EnrollMfaResult>>
{
    public async ValueTask<Result<EnrollMfaResult>> Handle(EnrollMfaCommand command, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.TenantId == currentUser.TenantId && u.Id == currentUser.UserId, ct);
        if (user is null)
        {
            return Result.Failure<EnrollMfaResult>(Error.NotFound("User"));
        }

        var secret = TotpVerifier.GenerateSecret();
        user.BeginMfaEnrollment(protector.Protect(secret), clock.UtcNow, currentUser.UserId);
        await db.SaveChangesAsync(ct);

        var uri = $"otpauth://totp/StockOrder:{Uri.EscapeDataString(user.Username)}?secret={secret}&issuer=StockOrder";
        return Result.Success(new EnrollMfaResult(uri));
    }
}

public sealed record ConfirmMfaCommand(string TotpCode) : ICommand<Result>;

public sealed class ConfirmMfaCommandValidator : AbstractValidator<ConfirmMfaCommand>
{
    public ConfirmMfaCommandValidator() => RuleFor(x => x.TotpCode).NotEmpty().Length(6);
}

public sealed class ConfirmMfaCommandHandler(IdentityDbContext db, ICurrentUserContext currentUser, IMfaSecretProtector protector, IClock clock)
    : ICommandHandler<ConfirmMfaCommand, Result>
{
    public async ValueTask<Result> Handle(ConfirmMfaCommand command, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.TenantId == currentUser.TenantId && u.Id == currentUser.UserId, ct);
        if (user is null || user.MfaSecretProtected is null)
        {
            return Result.Failure(Error.Conflict(ErrorCodes.ValidationFailed, "MFA enrollment was not started."));
        }

        if (!TotpVerifier.Verify(protector.Unprotect(user.MfaSecretProtected), command.TotpCode))
        {
            return Result.Failure(Error.Conflict(ErrorCodes.InvalidCredentials, "Invalid TOTP code."));
        }

        user.ConfirmMfaEnrollment(clock.UtcNow, currentUser.UserId);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }
}
