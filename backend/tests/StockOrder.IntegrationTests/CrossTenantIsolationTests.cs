using Microsoft.Data.SqlClient;
using Shouldly;
using Xunit;

namespace StockOrder.IntegrationTests;

/// <summary>
/// Section 19 (mandatory test): "cross-tenant isolation (EF + Dapper + insert)". This
/// covers the raw-SQL half as the app_runtime login, which is what actually proves RLS
/// works — EF's query filter is only the first guard (section 6). This is the regression
/// test for the EF model-caching bug fixed during Phase 0 development (see
/// StockOrder.Infrastructure.Database.PerScopeModelCacheKeyFactory).
/// </summary>
[Collection("Database")]
public sealed class CrossTenantIsolationTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task Session_with_no_tenant_context_sees_zero_rows_of_any_tenant()
    {
        var (tenantA, _) = await SeedTenantAsync();

        await using var connection = new SqlConnection(fixture.AppRuntimeConnectionString);
        await connection.OpenAsync();
        // Deliberately not setting TenantId session context.

        var count = await CountTenantRowsAsync(connection, tenantA);
        count.ShouldBe(0);
    }

    [Fact]
    public async Task Session_scoped_to_tenant_a_cannot_see_tenant_bs_rows()
    {
        var (tenantA, tenantB) = await SeedTenantAsync();

        await using var connection = new SqlConnection(fixture.AppRuntimeConnectionString);
        await connection.OpenAsync();
        await SetTenantSessionAsync(connection, tenantA);

        (await CountTenantRowsAsync(connection, tenantA)).ShouldBe(1);
        (await CountTenantRowsAsync(connection, tenantB)).ShouldBe(0);
    }

    [Fact]
    public async Task Inserting_a_row_for_another_tenant_is_blocked()
    {
        var (tenantA, tenantB) = await SeedTenantAsync();

        await using var connection = new SqlConnection(fixture.AppRuntimeConnectionString);
        await connection.OpenAsync();
        await SetTenantSessionAsync(connection, tenantA);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT tenancy.Branch (TenantId, Code, Name) VALUES (@tenantId, 'SNEAKY', 'Sneaky Branch');
            """;
        command.Parameters.AddWithValue("@tenantId", tenantB);

        await Should.ThrowAsync<SqlException>(() => command.ExecuteNonQueryAsync());
    }

    private async Task<(Guid TenantA, Guid TenantB)> SeedTenantAsync()
    {
        await using var connection = new SqlConnection(fixture.AppRuntimeConnectionString);
        await connection.OpenAsync();

        // sp_set_session_context with @read_only = 1 can only be set ONCE per physical
        // session (section 6) -- set it once here, not inside InsertTenantWithBranchAsync,
        // which is called twice on this same connection. Calling it a second time throws
        // "The key has been set as read_only for this session." This mirrors how the real
        // IBypassConnectionFactory sets it exactly once per newly-opened connection.
        await using (var bypassCommand = connection.CreateCommand())
        {
            bypassCommand.CommandText = "EXEC sp_set_session_context @key = N'RlsBypass', @value = 1, @read_only = 1;";
            await bypassCommand.ExecuteNonQueryAsync();
        }

        var tenantA = await InsertTenantWithBranchAsync(connection);
        var tenantB = await InsertTenantWithBranchAsync(connection);
        return (tenantA, tenantB);
    }

    private static async Task<Guid> InsertTenantWithBranchAsync(SqlConnection connection)
    {
        var tenantId = Guid.NewGuid();

        await using var insertTenant = connection.CreateCommand();
        insertTenant.CommandText = """
            INSERT tenancy.Tenant (Id, Code, Name, Industry) VALUES (@id, @code, 'Test Tenant', 'Retail');
            """;
        insertTenant.Parameters.AddWithValue("@id", tenantId);
        insertTenant.Parameters.AddWithValue("@code", $"T{Guid.NewGuid():N}"[..20]);
        await insertTenant.ExecuteNonQueryAsync();

        await using var insertBranch = connection.CreateCommand();
        insertBranch.CommandText = """
            INSERT tenancy.Branch (TenantId, Code, Name) VALUES (@tenantId, 'MAIN', 'Main Branch');
            """;
        insertBranch.Parameters.AddWithValue("@tenantId", tenantId);
        await insertBranch.ExecuteNonQueryAsync();

        return tenantId;
    }

    private static async Task SetTenantSessionAsync(SqlConnection connection, Guid tenantId)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "EXEC sp_set_session_context @key = N'TenantId', @value = @tenantId, @read_only = 1;";
        command.Parameters.AddWithValue("@tenantId", tenantId);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<int> CountTenantRowsAsync(SqlConnection connection, Guid tenantId)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM tenancy.Branch WHERE TenantId = @tenantId;";
        command.Parameters.AddWithValue("@tenantId", tenantId);
        return (int)(await command.ExecuteScalarAsync())!;
    }
}
