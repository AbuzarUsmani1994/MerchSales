using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
using StockOrder.Modules.Inventory.Contracts;
using StockOrder.Modules.Tenancy.Domain;
using StockOrder.Modules.Tenancy.Infrastructure;
using StockOrder.SharedKernel.Context;
using StockOrder.SharedKernel.Errors;
using StockOrder.SharedKernel.Ids;
using StockOrder.SharedKernel.Results;
using StockOrder.SharedKernel.Time;

namespace StockOrder.Modules.Tenancy.Application.Branches;

public sealed record NewBranchWarehouse(string Code, string Name);

/// <summary>
/// Creates a branch and its first warehouse (with MAIN/TRANSIT/QUARANTINE locations) in one
/// transaction, mirroring tenant provisioning (section 11): a branch without a warehouse
/// can't hold stock or take orders. Tenant-wide: the endpoint is not branch-scoped, so only
/// an all-branches <c>Branches.Manage</c> role (the Owner) gets here.
/// </summary>
public sealed record CreateBranchCommand(string Code, string Name, string? Address, string? Phone, NewBranchWarehouse Warehouse)
    : ICommand<Result<Guid>>;

public sealed class CreateBranchCommandValidator : AbstractValidator<CreateBranchCommand>
{
    public CreateBranchCommandValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(32);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Address).MaximumLength(500);
        RuleFor(x => x.Phone).MaximumLength(40);
        RuleFor(x => x.Warehouse).NotNull();
        RuleFor(x => x.Warehouse.Code).NotEmpty().MaximumLength(32).When(x => x.Warehouse is not null);
        RuleFor(x => x.Warehouse.Name).NotEmpty().MaximumLength(150).When(x => x.Warehouse is not null);
    }
}

public sealed class CreateBranchCommandHandler(
    TenancyDbContext db,
    ICurrentUserContext currentUser,
    IWarehouseService warehouseService,
    IClock clock,
    IIdGenerator idGenerator)
    : ICommandHandler<CreateBranchCommand, Result<Guid>>
{
    public async ValueTask<Result<Guid>> Handle(CreateBranchCommand command, CancellationToken ct)
    {
        var codeExists = await db.Branches
            .AnyAsync(b => b.TenantId == currentUser.TenantId && b.Code == command.Code, ct);
        if (codeExists)
        {
            return Result.Failure<Guid>(Error.Conflict(ErrorCodes.Duplicate, $"Branch code '{command.Code}' already exists."));
        }

        var branch = Branch.Create(
            currentUser.TenantId, idGenerator.NewId(), command.Code, command.Name, command.Address, command.Phone,
            clock.UtcNow, currentUser.UserId);

        db.Branches.Add(branch);
        await db.SaveChangesAsync(ct);

        // A failure here returns an error response, which rolls back the whole request
        // transaction (UnitOfWorkMiddleware) — the branch row above included.
        var warehouse = await warehouseService.CreateWarehouseAsync(branch.Id, command.Warehouse.Code, command.Warehouse.Name, ct);
        if (warehouse.IsFailure)
        {
            return Result.Failure<Guid>(warehouse.Error);
        }

        return Result.Success(branch.Id);
    }
}
