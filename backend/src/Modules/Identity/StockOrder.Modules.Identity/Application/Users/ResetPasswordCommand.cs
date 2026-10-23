using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using StockOrder.Modules.Identity.Domain;
using StockOrder.Modules.Identity.Infrastructure;
using StockOrder.SharedKernel.Context;
using StockOrder.SharedKernel.Errors;
using StockOrder.SharedKernel.Ids;
using StockOrder.SharedKernel.Results;
using StockOrder.SharedKernel.Time;

namespace StockOrder.Modules.Identity.Application.Users;

public sealed record ResetPasswordCommand(Guid UserId) : ICommand<Result<string>>;

public sealed class ResetPasswordCommandHandler(IdentityDbContext db, ICurrentUserContext currentUser, IClock clock)
    : ICommandHandler<ResetPasswordCommand, Result<string>>
{
    private static readonly PasswordHasher<User> PasswordHasher = new();

    public async ValueTask<Result<string>> Handle(ResetPasswordCommand command, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.TenantId == currentUser.TenantId && u.Id == command.UserId, ct);
        if (user is null)
        {
            return Result.Failure<string>(Error.NotFound("User"));
        }

        var oneTimePassword = OneTimePasswordGenerator.Generate();
        user.ResetPassword(PasswordHasher.HashPassword(user, oneTimePassword), clock.UtcNow, currentUser.UserId);

        await db.SaveChangesAsync(ct);
        return Result.Success(oneTimePassword);
    }
}

public sealed record SetPinCommand(string Pin) : ICommand<Result>;

public sealed class SetPinCommandHandler(IdentityDbContext db, ICurrentUserContext currentUser, IClock clock)
    : ICommandHandler<SetPinCommand, Result>
{
    public async ValueTask<Result> Handle(SetPinCommand command, CancellationToken ct)
    {
        if (command.Pin.Length is < 4 or > 6 || !command.Pin.All(char.IsDigit))
        {
            return Result.Failure(Error.Validation("The PIN must be 4-6 digits."));
        }

        var user = await db.Users.FirstOrDefaultAsync(u => u.TenantId == currentUser.TenantId && u.Id == currentUser.UserId, ct);
        if (user is null)
        {
            return Result.Failure(Error.NotFound("User"));
        }

        user.SetPin(new PasswordHasher<User>().HashPassword(user, command.Pin), clock.UtcNow, currentUser.UserId);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }
}
