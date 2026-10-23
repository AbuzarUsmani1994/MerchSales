using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using StockOrder.Modules.Identity.Domain;
using StockOrder.Modules.Identity.Infrastructure;
using StockOrder.SharedKernel.Context;
using StockOrder.SharedKernel.Errors;
using StockOrder.SharedKernel.Results;
using StockOrder.SharedKernel.Time;

namespace StockOrder.Modules.Identity.Application.Auth;

public sealed record ChangePasswordCommand(string CurrentPassword, string NewPassword) : ICommand<Result>;

public sealed class ChangePasswordCommandValidator : AbstractValidator<ChangePasswordCommand>
{
    public ChangePasswordCommandValidator()
    {
        RuleFor(x => x.CurrentPassword).NotEmpty();
        RuleFor(x => x.NewPassword)
            .NotEmpty()
            .MinimumLength(8)
            .Must(p => p.Any(char.IsLetter))
            .WithMessage("The password must not be all digits.");
    }
}

public sealed class ChangePasswordCommandHandler(IdentityDbContext db, ICurrentUserContext currentUser, IClock clock)
    : ICommandHandler<ChangePasswordCommand, Result>
{
    private static readonly PasswordHasher<User> PasswordHasher = new();

    public async ValueTask<Result> Handle(ChangePasswordCommand command, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(
            u => u.TenantId == currentUser.TenantId && u.Id == currentUser.UserId, ct);
        if (user is null)
        {
            return Result.Failure(Error.NotFound("User"));
        }

        if (command.NewPassword.Equals(user.Username, StringComparison.OrdinalIgnoreCase))
        {
            return Result.Failure(Error.Validation("The password must not equal the username."));
        }

        var verification = PasswordHasher.VerifyHashedPassword(user, user.PasswordHash, command.CurrentPassword);
        if (verification == PasswordVerificationResult.Failed)
        {
            return Result.Failure(Error.Conflict(ErrorCodes.InvalidCredentials, "The current password is incorrect."));
        }

        var newHash = PasswordHasher.HashPassword(user, command.NewPassword);
        user.ChangePassword(newHash, clock.UtcNow, currentUser.UserId);

        await db.SaveChangesAsync(ct);
        return Result.Success();
    }
}
