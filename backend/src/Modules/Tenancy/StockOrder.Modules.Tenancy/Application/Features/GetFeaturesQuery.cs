using Mediator;
using Microsoft.EntityFrameworkCore;
using StockOrder.Modules.Tenancy.Infrastructure;
using StockOrder.SharedKernel.Context;
using StockOrder.SharedKernel.Results;

namespace StockOrder.Modules.Tenancy.Application.Features;

public sealed record FeatureFlagDto(string FeatureKey, bool IsEnabled);

public sealed record GetFeaturesQuery : IQuery<Result<IReadOnlyList<FeatureFlagDto>>>;

public sealed class GetFeaturesQueryHandler(TenancyDbContext db, ICurrentUserContext currentUser)
    : IQueryHandler<GetFeaturesQuery, Result<IReadOnlyList<FeatureFlagDto>>>
{
    public async ValueTask<Result<IReadOnlyList<FeatureFlagDto>>> Handle(GetFeaturesQuery query, CancellationToken ct)
    {
        var features = await db.TenantFeatures
            .Where(f => f.TenantId == currentUser.TenantId)
            .OrderBy(f => f.FeatureKey)
            .Select(f => new FeatureFlagDto(f.FeatureKey, f.IsEnabled))
            .ToListAsync(ct);

        return Result.Success<IReadOnlyList<FeatureFlagDto>>(features);
    }
}
