using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
using StockOrder.Modules.Identity.Domain;
using StockOrder.Modules.Identity.Infrastructure;
using StockOrder.SharedKernel.Context;
using StockOrder.SharedKernel.Errors;
using StockOrder.SharedKernel.Ids;
using StockOrder.SharedKernel.Results;

namespace StockOrder.Modules.Identity.Application.Users;

public sealed record AssignUserRoleCommand(Guid UserId, Guid RoleId, Guid? BranchId) : ICommand<Result>;

public sealed class AssignUserRoleCommandValidator : AbstractValidator<AssignUserRoleCommand>
{
    public AssignUserRoleCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.RoleId).NotEmpty();
    }
}

public sealed class AssignUserRoleCommandHandler(IdentityDbContext db, ICurrentUserContext currentUser, IIdGenerator idGenerator)
    : ICommandHandler<AssignUserRoleCommand, Result>
{
    public async ValueTask<Result> Handle(AssignUserRoleCommand command, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.TenantId == currentUser.TenantId && u.Id == command.UserId, ct);
        var roleExists = await db.Roles.AnyAsync(r => r.TenantId == currentUser.TenantId && r.Id == command.RoleId, ct);
        if (user is null || !roleExists)
        {
            return Result.Failure(Error.NotFound("User or role"));
        }

        var alreadyAssigned = await db.UserRoles.AnyAsync(
            ur => ur.TenantId == currentUser.TenantId && ur.UserId == command.UserId &&
                  ur.RoleId == command.RoleId && ur.BranchId == command.BranchId, ct);
        if (alreadyAssigned)
        {
            return Result.Failure(Error.Conflict(ErrorCodes.Duplicate, "This role is already assigned for that branch scope."));
        }

        db.UserRoles.Add(UserRole.Create(currentUser.TenantId, idGenerator.NewId(), command.UserId, command.RoleId, command.BranchId));

        // Rotating the stamp changes the permission cache key, so the new role takes effect
        // immediately instead of waiting for the cache entry to expire (section 10).
        user.RotateSecurityStamp();

        await db.SaveChangesAsync(ct);
        return Result.Success();
    }
}

public sealed record RemoveUserRoleCommand(Guid UserRoleId) : ICommand<Result>;

public sealed class RemoveUserRoleCommandHandler(IdentityDbContext db, ICurrentUserContext currentUser)
    : ICommandHandler<RemoveUserRoleCommand, Result>
{
    public async ValueTask<Result> Handle(RemoveUserRoleCommand command, CancellationToken ct)
    {
        var userRole = await db.UserRoles.FirstOrDefaultAsync(
            ur => ur.TenantId == currentUser.TenantId && ur.Id == command.UserRoleId, ct);
        if (userRole is null)
        {
            return Result.Failure(Error.NotFound("UserRole"));
        }

        var user = await db.Users.FirstOrDefaultAsync(u => u.TenantId == currentUser.TenantId && u.Id == userRole.UserId, ct);

        db.UserRoles.Remove(userRole);
        user?.RotateSecurityStamp();

        await db.SaveChangesAsync(ct);
        return Result.Success();
    }
}
