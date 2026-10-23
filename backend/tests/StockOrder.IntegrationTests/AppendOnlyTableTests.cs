using Microsoft.Data.SqlClient;
using Shouldly;
using Xunit;

namespace StockOrder.IntegrationTests;

/// <summary>Section 19 (mandatory test): "append-only tables reject UPDATE/DELETE".</summary>
[Collection("Database")]
public sealed class AppendOnlyTableTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task AuditLog_denies_update_to_app_runtime()
    {
        await using var connection = new SqlConnection(fixture.AppRuntimeConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE platform.AuditLog SET Action = 'Tampered' WHERE Id = 1;";

        var ex = await Should.ThrowAsync<SqlException>(() => command.ExecuteNonQueryAsync());
        ex.Message.ToLowerInvariant().ShouldContain("permission");
    }

    [Fact]
    public async Task AuditLog_denies_delete_to_app_runtime()
    {
        await using var connection = new SqlConnection(fixture.AppRuntimeConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM platform.AuditLog WHERE Id = 1;";

        await Should.ThrowAsync<SqlException>(() => command.ExecuteNonQueryAsync());
    }
}
