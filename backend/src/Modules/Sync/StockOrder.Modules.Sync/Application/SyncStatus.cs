using Dapper;
using Mediator;
using StockOrder.Infrastructure.Database;
using StockOrder.Modules.Tenancy.Contracts;
using StockOrder.SharedKernel.Context;
using StockOrder.SharedKernel.Errors;
using StockOrder.SharedKernel.Results;
using StockOrder.SharedKernel.Time;

namespace StockOrder.Modules.Sync.Application;

public sealed record SyncStatusResult(
    string? MinVersion,
    string? LatestVersion,
    string? StoreUrl,
    bool DeviceRevoked,
    bool WipeRequested,
    int MaxOfflineHours,
    DateTime ServerTimeUtc,
    string Industry);

public sealed record SyncStatusQuery : IQuery<Result<SyncStatusResult>>;

/// <summary>Section 12 <c>GET /sync/status</c>: what the phone checks before and between syncs.</summary>
public sealed class SyncStatusQueryHandler(
    IUnitOfWork unitOfWork, ICurrentUserContext currentUser, ISettingQueries settings, ITenantQueries tenants, IClock clock)
    : IQueryHandler<SyncStatusQuery, Result<SyncStatusResult>>
{
    public const string MaxOfflineHoursSetting = "Mobile.MaxOfflineHours";

    /// <summary>Used when a tenant has no setting; the restaurant template's value (section 11).</summary>
    public const int DefaultMaxOfflineHours = 24;

    public async ValueTask<Result<SyncStatusResult>> Handle(SyncStatusQuery query, CancellationToken ct)
    {
        if (currentUser.DeviceId is not { } deviceId)
        {
            return Result.Failure<SyncStatusResult>(Error.PermissionDenied("Sign in on this device first."));
        }

        var device = await unitOfWork.Connection.QueryFirstOrDefaultAsync<DeviceRow>(new CommandDefinition(
            """
            SELECT d.IsRevoked, d.WipeRequested, p.MinVersion, p.LatestVersion, p.StoreUrl
            FROM sync.Device d
            LEFT JOIN sync.AppVersionPolicy p ON p.Platform = d.Platform
            WHERE d.TenantId = @tenantId AND d.Id = @deviceId;
            """,
            new { tenantId = currentUser.TenantId, deviceId },
            unitOfWork.Transaction,
            cancellationToken: ct));

        if (device is null)
        {
            return Result.Failure<SyncStatusResult>(Error.NotFound("Device"));
        }

        var maxOfflineSetting = await settings.GetSettingAsync(currentUser.TenantId, MaxOfflineHoursSetting, ct);
        var maxOfflineHours = int.TryParse(maxOfflineSetting, out var hours) && hours > 0 ? hours : DefaultMaxOfflineHours;

        return Result.Success(new SyncStatusResult(
            device.MinVersion, device.LatestVersion, device.StoreUrl, device.IsRevoked, device.WipeRequested,
            maxOfflineHours, clock.UtcNow, await tenants.GetIndustryAsync(currentUser.TenantId, ct)));
    }

    private sealed record DeviceRow(bool IsRevoked, bool WipeRequested, string? MinVersion, string? LatestVersion, string? StoreUrl);
}
