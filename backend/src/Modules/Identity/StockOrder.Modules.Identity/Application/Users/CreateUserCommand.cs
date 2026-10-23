using FluentValidation;
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

public sealed record CreateUserCommand(string Username, string DisplayName, string? Email, string? Phone, Guid? DefaultBranchId)
    : ICommand<Result<CreateUserResult>>;

public sealed record CreateUserResult(Guid UserId, string OneTimePassword);

public sealed class CreateUserCommandValidator : AbstractValidator<CreateUserCommand>
{
    public CreateUserCommandValidator()
    {
        RuleFor(x => x.Username).NotEmpty().MaximumLength(100);
        RuleFor(x => x.DisplayName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Email).MaximumLength(256).EmailAddress().When(x => x.Email is not null);
    }
}

public sealed class CreateUserCommandHandler(
    IdentityDbContext db, ICurrentUserContext currentUser, IClock clock, IIdGenerator idGenerator)
    : ICommandHandler<CreateUserCommand, Result<CreateUserResult>>
{
    private static readonly PasswordHasher<User> PasswordHasher = new();

    public async ValueTask<Result<CreateUserResult>> Handle(CreateUserCommand command, CancellationToken ct)
    {
        var exists = await db.Users.AnyAsync(u => u.TenantId == currentUser.TenantId && u.Username == command.Username, ct);
        if (exists)
        {
            return Result.Failure<CreateUserResult>(Error.Conflict(ErrorCodes.Duplicate, $"Username '{command.Username}' already exists."));
        }

        var oneTimePassword = OneTimePasswordGenerator.Generate();
        var userId = idGenerator.NewId();

        var user = User.Create(
            currentUser.TenantId, userId, command.Username, command.DisplayName, command.Email, command.Phone,
            string.Empty, command.DefaultBranchId, clock.UtcNow, currentUser.UserId);
        user.ResetPassword(PasswordHasher.HashPassword(user, oneTimePassword), clock.UtcNow, currentUser.UserId);

        db.Users.Add(user);
        await db.SaveChangesAsync(ct);

        return Result.Success(new CreateUserResult(user.Id, oneTimePassword));
    }
}
