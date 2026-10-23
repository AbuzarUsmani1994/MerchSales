using Microsoft.Data.SqlClient;
using Microsoft.AspNetCore.Identity;
using StockOrder.Infrastructure.Database;
using StockOrder.SharedKernel.Ids;
using StockOrder.SharedKernel.Time;

namespace StockOrder.Modules.Platform.Infrastructure.Bootstrap;

/// <summary>
/// `StockOrder.Api.exe bootstrap-platform --admin &lt;username&gt;` (section 10): creates the
/// platform tenant (code PLATFORM, IsPlatform = 1), its PlatformAdmin role, and the first
/// platform admin user. Run once, by hand, before anyone can provision real tenants.
/// </summary>
internal sealed class BootstrapPlatformService(
    ITenantLookupService tenantLookup, ITenantProvisioningConnection provisioningConnection, IClock clock, IIdGenerator idGenerator)
    : IBootstrapPlatformService
{
    private const string PlatformTenantCode = "PLATFORM";

    private static readonly string[] PlatformAdminPermissions =
    [
        "Platform.Tenants.Manage", "Platform.AppVersion.Manage", "Users.Manage", "Users.View", "Roles.Manage", "Identity.Self.Manage",
    ];

    private static readonly PasswordHasher<object> PasswordHasher = new();

    public async Task<BootstrapResult> BootstrapAsync(string adminUsername, CancellationToken ct)
    {
        var existing = await tenantLookup.FindByCodeAsync(PlatformTenantCode, ct);
        if (existing is not null)
        {
            throw new InvalidOperationException(
                $"The platform tenant already exists (Id={existing.TenantId}). Bootstrap only runs once.");
        }

        var now = clock.UtcNow;
        await using var connection = await provisioningConnection.OpenAsync(ct);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(ct);

        var tenantId = idGenerator.NewId();
        await ExecAsync(connection, transaction, """
            INSERT tenancy.Tenant (Id, Code, Name, Industry, TimeZoneId, IsPlatform, CreatedAtUtc)
            VALUES (@id, @code, N'Platform Operator', 'Platform', 'Asia/Karachi', 1, @now);
            """,
            ("@id", tenantId), ("@code", PlatformTenantCode), ("@now", now));

        var roleId = idGenerator.NewId();
        await ExecAsync(connection, transaction, """
            INSERT [identity].Role (TenantId, Id, Name, IsSystem, CreatedAtUtc)
            VALUES (@tenantId, @id, 'PlatformAdmin', 1, @now);
            """,
            ("@tenantId", tenantId), ("@id", roleId), ("@now", now));

        foreach (var code in PlatformAdminPermissions)
        {
            await ExecAsync(connection, transaction, """
                INSERT [identity].RolePermission (TenantId, RoleId, PermissionCode) VALUES (@tenantId, @roleId, @code);
                """,
                ("@tenantId", tenantId), ("@roleId", roleId), ("@code", code));
        }

        var adminUserId = idGenerator.NewId();
        var oneTimePassword = OneTimePasswordGenerator.Generate();
        var passwordHash = PasswordHasher.HashPassword(new object(), oneTimePassword);

        await ExecAsync(connection, transaction, """
            INSERT [identity].[User] (TenantId, Id, Username, DisplayName, PasswordHash, CreatedAtUtc)
            VALUES (@tenantId, @id, @username, @displayName, @passwordHash, @now);
            """,
            ("@tenantId", tenantId), ("@id", adminUserId), ("@username", adminUsername),
            ("@displayName", adminUsername), ("@passwordHash", passwordHash), ("@now", now));

        await ExecAsync(connection, transaction, """
            INSERT [identity].UserRole (TenantId, Id, UserId, RoleId, BranchId) VALUES (@tenantId, @id, @userId, @roleId, NULL);
            """,
            ("@tenantId", tenantId), ("@id", idGenerator.NewId()), ("@userId", adminUserId), ("@roleId", roleId));

        await transaction.CommitAsync(ct);

        return new BootstrapResult(tenantId, adminUserId, oneTimePassword);
    }

    private static async Task ExecAsync(
        SqlConnection connection, SqlTransaction transaction, string sql, params (string Name, object? Value)[] parameters)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.Add(new SqlParameter(name, value ?? DBNull.Value));
        }

        await command.ExecuteNonQueryAsync();
    }
}
