using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
using StockOrder.Modules.Tenancy.Contracts;
using StockOrder.Modules.Tenancy.Domain;
using StockOrder.Modules.Tenancy.Infrastructure;
using StockOrder.SharedKernel.Context;
using StockOrder.SharedKernel.Results;
using StockOrder.SharedKernel.Time;

namespace StockOrder.Modules.Tenancy.Application.Features;

public sealed record SetFeatureCommand(string FeatureKey, bool IsEnabled) : ICommand<Result>;

public sealed class SetFeatureCommandValidator : AbstractValidator<SetFeatureCommand>
{
    public SetFeatureCommandValidator()
    {
        RuleFor(x => x.FeatureKey)
            .Must(key => FeatureKeys.All.Contains(key))
            .WithMessage("Unknown feature key.");
    }
}

public sealed class SetFeatureCommandHandler(TenancyDbContext db, ICurrentUserContext currentUser, IClock clock)
    : ICommandHandler<SetFeatureCommand, Result>
{
    public async ValueTask<Result> Handle(SetFeatureCommand command, CancellationToken ct)
    {
        var feature = await db.TenantFeatures
            .FirstOrDefaultAsync(f => f.TenantId == currentUser.TenantId && f.FeatureKey == command.FeatureKey, ct);

        if (feature is null)
        {
            feature = TenantFeature.Create(currentUser.TenantId, command.FeatureKey, command.IsEnabled, clock.UtcNow);
            db.TenantFeatures.Add(feature);
        }
        else
        {
            feature.SetEnabled(command.IsEnabled, clock.UtcNow);
        }

        await db.SaveChangesAsync(ct);
        return Result.Success();
    }
}
