using FluentValidation;
using Mediator;
using StockOrder.Infrastructure.Database;
using StockOrder.Modules.Platform.Infrastructure.Provisioning;
using StockOrder.SharedKernel.Errors;
using StockOrder.SharedKernel.Results;

namespace StockOrder.Modules.Platform.Application.Tenants;

public sealed record OwnerSpec(string Username, string DisplayName, string? Email, string? Phone);

public sealed record BranchSpec(string Code, string Name);

public sealed record WarehouseSpec(string Code, string Name);

public sealed record ProvisionTenantCommand(
    string Code, string Name, string Industry, OwnerSpec Owner, BranchSpec Branch, WarehouseSpec Warehouse)
    : ICommand<Result<ProvisionTenantResult>>;

public sealed class ProvisionTenantCommandValidator : AbstractValidator<ProvisionTenantCommand>
{
    private static readonly string[] AllowedIndustries = ["Restaurant", "Pharma", "Retail"];

    public ProvisionTenantCommandValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(32);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Industry).Must(i => AllowedIndustries.Contains(i))
            .WithMessage($"Industry must be one of: {string.Join(", ", AllowedIndustries)}.");
        RuleFor(x => x.Owner.Username).NotEmpty();
        RuleFor(x => x.Owner.DisplayName).NotEmpty();
        RuleFor(x => x.Branch.Code).NotEmpty();
        RuleFor(x => x.Branch.Name).NotEmpty();
        RuleFor(x => x.Warehouse.Code).NotEmpty();
        RuleFor(x => x.Warehouse.Name).NotEmpty();
    }
}

/// <summary>
/// Platform.Tenants.Manage (section 10, 11). This is the one command in the whole system
/// that legitimately runs before any TenantId exists, so it delegates straight to
/// <see cref="ITenantProvisioningService"/> rather than going through the per-request
/// <c>IUnitOfWork</c>.
/// </summary>
public sealed class ProvisionTenantCommandHandler(ITenantProvisioningService provisioningService, ITenantLookupService tenantLookup)
    : ICommandHandler<ProvisionTenantCommand, Result<ProvisionTenantResult>>
{
    public async ValueTask<Result<ProvisionTenantResult>> Handle(ProvisionTenantCommand command, CancellationToken ct)
    {
        var existing = await tenantLookup.FindByCodeAsync(command.Code, ct);
        if (existing is not null)
        {
            return Result.Failure<ProvisionTenantResult>(
                Error.Conflict(ErrorCodes.Duplicate, $"Tenant code '{command.Code}' already exists."));
        }

        var result = await provisioningService.ProvisionAsync(
            new ProvisionTenantRequest(
                command.Code, command.Name, command.Industry,
                command.Owner.Username, command.Owner.DisplayName, command.Owner.Email, command.Owner.Phone,
                command.Branch.Code, command.Branch.Name, command.Warehouse.Code, command.Warehouse.Name),
            ct);

        return Result.Success(result);
    }
}
