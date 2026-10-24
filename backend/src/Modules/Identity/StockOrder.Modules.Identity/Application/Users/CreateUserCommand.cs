using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using StockOrder.Modules.Identity.Domain;
using StockOrder.Modules.Identity.Infrastructure;
using StockOrder.Modules.Tenancy.Contracts;
using StockOrder.SharedKernel.Context;
using StockOrder.SharedKernel.Errors;
using StockOrder.SharedKernel.Ids;
using StockOrder.SharedKernel.Results;
using StockOrder.SharedKernel.Time;

namespace StockOrder.Modules.Identity.Application.Users;

/// <summary>
/// Creates a user at <paramref name="DefaultBranchId"/> (NULL = not tied to one branch) and,
/// when <paramref name="RoleId"/> is given, assigns that role for the same branch in the
/// same transaction (NULL branch = the role applies at all branches).
/// </summary>
public sealed record CreateUserCommand(
    string Username, string DisplayName, string? Email, string? Phone, Guid? DefaultBranchId, Guid? RoleId = null)
    : ICommand<Result<CreateUserResult>>;

public sealed record CreateUserResult(Guid UserId, string OneTimePassword);

public sealed class CreateUserCommandValidator : AbstractValidator<CreateUserCommand>
{
    public CreateUserCommandValidator()
    {
        RuleFor(x => x.Username).NotEmpty().MaximumLength(100);
        RuleFor(x => x.DisplayName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Email).MaximumLength(256).EmailAddress().When(x => x.Email is not null);
        RuleFor(x => x.Phone).MaximumLength(40);
    }
}

public sealed class CreateUserCommandHandler(
    IdentityDbContext db,
    ICurrentUserContext currentUser,
    IAccessScope accessScope,
    IBranchQueries branchQueries,
    IClock clock,
    IIdGenerator idGenerator)
    : ICommandHandler<CreateUserCommand, Result<CreateUserResult>>
{
    private static readonly PasswordHasher<User> PasswordHasher = new();

    public async ValueTask<Result<CreateUserResult>> Handle(CreateUserCommand command, CancellationToken ct)
    {
        var grants = await accessScope.GetGrantsAsync(ct);

        if (command.DefaultBranchId is { } branchId)
        {
            var branch = await branchQueries.GetBranchAsync(currentUser.TenantId, branchId, ct);
            if (branch is null || !branch.IsActive)
            {
                return Result.Failure<CreateUserResult>(Error.NotFound("Branch"));
            }
        }

        if (!BranchScope.For(grants, UserAdministrationPolicy.UsersManage).Covers(command.DefaultBranchId))
        {
            return Result.Failure<CreateUserResult>(Error.PermissionDenied(command.DefaultBranchId is null
                ? "Choose one of your branches for this user."
                : "You can only create users at branches you manage."));
        }

        if (command.RoleId is { } roleId)
        {
            if (!await db.Roles.AnyAsync(r => r.TenantId == currentUser.TenantId && r.Id == roleId, ct))
            {
                return Result.Failure<CreateUserResult>(Error.NotFound("Role"));
            }

            var rolePermissions = await db.GetRolePermissionCodesAsync(currentUser.TenantId, roleId, ct);
            if (UserAdministrationPolicy.CanAssignRole(grants, rolePermissions, command.DefaultBranchId) is { } denied)
            {
                return Result.Failure<CreateUserResult>(denied);
            }
        }

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

        // Saved separately: the EF model has no User-UserRole relationship to order the
        // inserts by, and the FK needs the user row first. Same request transaction either way.
        if (command.RoleId is { } assignedRoleId)
        {
            db.UserRoles.Add(UserRole.Create(
                currentUser.TenantId, idGenerator.NewId(), user.Id, assignedRoleId, command.DefaultBranchId));
            await db.SaveChangesAsync(ct);
        }

        return Result.Success(new CreateUserResult(user.Id, oneTimePassword));
    }
}
