using Mediator;
using Microsoft.Data.SqlClient;
using StockOrder.Infrastructure.Database;
using StockOrder.SharedKernel.Results;

namespace StockOrder.Modules.Platform.Application.AppVersions;

public sealed record AppVersionPolicySummary(string Platform, string MinVersion, string LatestVersion, string? StoreUrl);

public sealed record GetAppVersionPoliciesQuery : IQuery<Result<IReadOnlyList<AppVersionPolicySummary>>>;

/// <summary>sync.AppVersionPolicy is global, not tenant-owned (section 8), so it is read with the bypass connection.</summary>
public sealed class GetAppVersionPoliciesQueryHandler(ITenantProvisioningConnection provisioningConnection)
    : IQueryHandler<GetAppVersionPoliciesQuery, Result<IReadOnlyList<AppVersionPolicySummary>>>
{
    public async ValueTask<Result<IReadOnlyList<AppVersionPolicySummary>>> Handle(GetAppVersionPoliciesQuery query, CancellationToken ct)
    {
        await using var connection = await provisioningConnection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Platform, MinVersion, LatestVersion, StoreUrl FROM sync.AppVersionPolicy;";

        var policies = new List<AppVersionPolicySummary>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            policies.Add(new AppVersionPolicySummary(
                reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetString(3)));
        }

        return Result.Success<IReadOnlyList<AppVersionPolicySummary>>(policies);
    }
}

public sealed record SetAppVersionPolicyCommand(string Platform, string MinVersion, string LatestVersion, string? StoreUrl)
    : ICommand<Result>;

public sealed class SetAppVersionPolicyCommandHandler(ITenantProvisioningConnection provisioningConnection)
    : ICommandHandler<SetAppVersionPolicyCommand, Result>
{
    public async ValueTask<Result> Handle(SetAppVersionPolicyCommand command, CancellationToken ct)
    {
        await using var connection = await provisioningConnection.OpenAsync(ct);
        await using var sqlCommand = connection.CreateCommand();
        sqlCommand.CommandText = """
            UPDATE sync.AppVersionPolicy
            SET MinVersion = @minVersion, LatestVersion = @latestVersion, StoreUrl = @storeUrl, UpdatedAtUtc = SYSUTCDATETIME()
            WHERE Platform = @platform;
            """;
        sqlCommand.Parameters.Add(new SqlParameter("@minVersion", command.MinVersion));
        sqlCommand.Parameters.Add(new SqlParameter("@latestVersion", command.LatestVersion));
        sqlCommand.Parameters.Add(new SqlParameter("@storeUrl", (object?)command.StoreUrl ?? DBNull.Value));
        sqlCommand.Parameters.Add(new SqlParameter("@platform", command.Platform));

        await sqlCommand.ExecuteNonQueryAsync(ct);
        return Result.Success();
    }
}
