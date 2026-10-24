using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
using StockOrder.Modules.Identity.Domain;
using StockOrder.Modules.Identity.Infrastructure;
using StockOrder.SharedKernel.Context;
using StockOrder.SharedKernel.Errors;
using StockOrder.SharedKernel.Ids;
using StockOrder.SharedKernel.Results;
using StockOrder.SharedKernel.Time;

namespace StockOrder.Modules.Identity.Application.Roles;

public sealed record CreateRoleCommand(string Name, string? Description) : ICommand<Result<Guid>>;

public sealed class CreateRoleCommandValidator : AbstractValidator<CreateRoleCommand>
{
    public CreateRoleCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Description).MaximumLength(500);
    }
}

public sealed class CreateRoleCommandHandler(IdentityDbContext db, ICurrentUserContext currentUser, IClock clock, IIdGenerator idGenerator)
    : ICommandHandler<CreateRoleCommand, Result<Guid>>
{
    public async ValueTask<Result<Guid>> Handle(CreateRoleCommand command, CancellationToken ct)
    {
        var exists = await db.Roles.AnyAsync(r => r.TenantId == currentUser.TenantId && r.Name == command.Name, ct);
        if (exists)
        {
            return Result.Failure<Guid>(Error.Conflict(ErrorCodes.Duplicate, $"Role '{command.Name}' already exists."));
        }

        var role = Role.Create(
            currentUser.TenantId, idGenerator.NewId(), command.Name, command.Description, isSystem: false, clock.UtcNow, currentUser.UserId);
        db.Roles.Add(role);
        await db.SaveChangesAsync(ct);

        return Result.Success(role.Id);
    }
}

public sealed record SetRolePermissionsCommand(Guid RoleId, IReadOnlyList<string> PermissionCodes) : ICommand<Result>;

public sealed class SetRolePermissionsCommandHandler(
    IdentityDbContext db, ICurrentUserContext currentUser, IAccessScope accessScope)
    : ICommandHandler<SetRolePermissionsCommand, Result>
{
    /// <summary>Every role keeps this one; see 013_SelfServicePermission.sql.</summary>
    private const string SelfServicePermission = "Identity.Self.Manage";

    public async ValueTask<Result> Handle(SetRolePermissionsCommand command, CancellationToken ct)
    {
        var role = await db.Roles.FirstOrDefaultAsync(r => r.TenantId == currentUser.TenantId && r.Id == command.RoleId, ct);
        if (role is null)
        {
            return Result.Failure(Error.NotFound("Role"));
        }

        var requestedCodes = command.PermissionCodes.Append(SelfServicePermission).Distinct().ToList();

        var catalogue = await db.Permissions
            .Where(p => requestedCodes.Contains(p.Code))
            .Select(p => new { p.Code, p.IsPlatformOnly })
            .ToListAsync(ct);
        var validCodes = catalogue.Select(p => p.Code).ToList();

        var unknown = requestedCodes.Except(validCodes).ToList();
        if (unknown.Count > 0)
        {
            return Result.Failure(Error.Validation($"Unknown permission code(s): {string.Join(", ", unknown)}"));
        }

        // Platform.* codes act across every tenant; only the operator tenant may hold them.
        var platformOnly = catalogue.Where(p => p.IsPlatformOnly).Select(p => p.Code).ToList();
        if (platformOnly.Count > 0 && !currentUser.IsPlatformAdmin)
        {
            return Result.Failure(Error.PermissionDenied(
                $"Platform permissions can't be given to a tenant role: {string.Join(", ", platformOnly)}"));
        }

        var currentCodes = await db.GetRolePermissionCodesAsync(currentUser.TenantId, role.Id, ct);
        var callerHoldsRole = await db.UserRoles.AnyAsync(
            ur => ur.TenantId == currentUser.TenantId && ur.UserId == currentUser.UserId && ur.RoleId == role.Id, ct);
        var grants = await accessScope.GetGrantsAsync(ct);
        if (UserAdministrationPolicy.CanSetRolePermissions(grants, callerHoldsRole, currentCodes, validCodes) is { } denied)
        {
            return Result.Failure(denied);
        }

        var existing = await db.RolePermissions
            .Where(rp => rp.TenantId == currentUser.TenantId && rp.RoleId == command.RoleId)
            .ToListAsync(ct);
        db.RolePermissions.RemoveRange(existing);

        foreach (var code in validCodes)
        {
            db.RolePermissions.Add(RolePermission.Create(currentUser.TenantId, command.RoleId, code));
        }

        // Every user holding this role needs the cache invalidated — rotate all their stamps (section 10).
        var affectedUserIds = await db.UserRoles
            .Where(ur => ur.TenantId == currentUser.TenantId && ur.RoleId == command.RoleId)
            .Select(ur => ur.UserId)
            .Distinct()
            .ToListAsync(ct);

        var affectedUsers = await db.Users
            .Where(u => u.TenantId == currentUser.TenantId && affectedUserIds.Contains(u.Id))
            .ToListAsync(ct);
        foreach (var user in affectedUsers)
        {
            user.RotateSecurityStamp();
        }

        await db.SaveChangesAsync(ct);
        return Result.Success();
    }
}
