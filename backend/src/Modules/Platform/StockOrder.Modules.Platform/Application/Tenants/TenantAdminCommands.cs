using Mediator;
using Microsoft.Data.SqlClient;
using StockOrder.Infrastructure.Database;
using StockOrder.SharedKernel.Errors;
using StockOrder.SharedKernel.Results;
using StockOrder.SharedKernel.Time;

namespace StockOrder.Modules.Platform.Application.Tenants;

public sealed record TenantSummary(Guid Id, string Code, string Name, string Industry, string Status, DateTime CreatedAtUtc);

public sealed record ListTenantsQuery : IQuery<Result<IReadOnlyList<TenantSummary>>>;

/// <summary>Platform.Tenants.Manage (section 10): lists every tenant, so it has to run on the bypass connection.</summary>
public sealed class ListTenantsQueryHandler(ITenantProvisioningConnection provisioningConnection)
    : IQueryHandler<ListTenantsQuery, Result<IReadOnlyList<TenantSummary>>>
{
    public async ValueTask<Result<IReadOnlyList<TenantSummary>>> Handle(ListTenantsQuery query, CancellationToken ct)
    {
        await using var connection = await provisioningConnection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, Code, Name, Industry, Status, CreatedAtUtc
            FROM tenancy.Tenant
            WHERE IsPlatform = 0
            ORDER BY CreatedAtUtc DESC;
            """;

        var tenants = new List<TenantSummary>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            tenants.Add(new TenantSummary(
                reader.GetGuid(0), reader.GetString(1), reader.GetString(2),
                reader.GetString(3), reader.GetString(4), reader.GetDateTime(5)));
        }

        return Result.Success<IReadOnlyList<TenantSummary>>(tenants);
    }
}

public sealed record SetTenantStatusCommand(Guid TenantId, string Status) : ICommand<Result>;

public sealed class SetTenantStatusCommandHandler(ITenantProvisioningConnection provisioningConnection, IClock clock)
    : ICommandHandler<SetTenantStatusCommand, Result>
{
    private static readonly string[] ValidStatuses = ["Active", "Suspended", "Closed"];

    public async ValueTask<Result> Handle(SetTenantStatusCommand command, CancellationToken ct)
    {
        if (!ValidStatuses.Contains(command.Status))
        {
            return Result.Failure(Error.Validation($"Status must be one of: {string.Join(", ", ValidStatuses)}."));
        }

        await using var connection = await provisioningConnection.OpenAsync(ct);
        await using var sqlCommand = connection.CreateCommand();
        sqlCommand.CommandText = """
            UPDATE tenancy.Tenant SET Status = @status, UpdatedAtUtc = @now
            WHERE Id = @id AND IsPlatform = 0;
            """;
        sqlCommand.Parameters.Add(new SqlParameter("@status", command.Status));
        sqlCommand.Parameters.Add(new SqlParameter("@now", clock.UtcNow));
        sqlCommand.Parameters.Add(new SqlParameter("@id", command.TenantId));

        var rows = await sqlCommand.ExecuteNonQueryAsync(ct);
        return rows == 0 ? Result.Failure(Error.NotFound("Tenant")) : Result.Success();
    }
}
