using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
using StockOrder.Modules.Tenancy.Domain;
using StockOrder.Modules.Tenancy.Infrastructure;
using StockOrder.SharedKernel.Context;
using StockOrder.SharedKernel.Errors;
using StockOrder.SharedKernel.Ids;
using StockOrder.SharedKernel.Results;
using StockOrder.SharedKernel.Time;

namespace StockOrder.Modules.Tenancy.Application.Branches;

public sealed record CreateBranchCommand(string Code, string Name, string? Address, string? Phone)
    : ICommand<Result<Guid>>;

public sealed class CreateBranchCommandValidator : AbstractValidator<CreateBranchCommand>
{
    public CreateBranchCommandValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(32);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Address).MaximumLength(500);
        RuleFor(x => x.Phone).MaximumLength(40);
    }
}

public sealed class CreateBranchCommandHandler(
    TenancyDbContext db, ICurrentUserContext currentUser, IClock clock, IIdGenerator idGenerator)
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
            currentUser.TenantId, idGenerator.NewId(), command.Code, command.Name, clock.UtcNow, currentUser.UserId);

        db.Branches.Add(branch);
        await db.SaveChangesAsync(ct);

        return Result.Success(branch.Id);
    }
}
