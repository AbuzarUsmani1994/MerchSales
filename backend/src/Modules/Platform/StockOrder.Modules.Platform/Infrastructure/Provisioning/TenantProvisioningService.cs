using Microsoft.Data.SqlClient;
using Microsoft.AspNetCore.Identity;
using StockOrder.Infrastructure.Database;
using StockOrder.SharedKernel.Ids;
using StockOrder.SharedKernel.Time;

namespace StockOrder.Modules.Platform.Infrastructure.Provisioning;

/// <summary>
/// Creates a tenant, its first branch and warehouse (with the three standard locations),
/// the owner user, the template's roles and permissions, feature flags, settings, number
/// series and catalog basics — all in one transaction (section 11).
///
/// This is a deliberate, documented exception to "each module owns its own schema"
/// (section 5.2): provisioning writes directly into tenancy, identity, inventory and
/// platform tables with raw SQL on the bypass connection, because no tenant context exists
/// yet for any module's normal per-tenant DbContext to use. Phase 1 modules (Catalog,
/// Inventory, etc.) still own those schemas for everything else.
/// </summary>
internal sealed class TenantProvisioningService(
    ITenantProvisioningConnection provisioningConnection, IClock clock, IIdGenerator idGenerator)
    : ITenantProvisioningService
{
    private static readonly PasswordHasher<object> PasswordHasher = new();

    public async Task<ProvisionTenantResult> ProvisionAsync(ProvisionTenantRequest request, CancellationToken ct)
    {
        var template = IndustryTemplate.Load(request.Industry);
        var now = clock.UtcNow;

        await using var connection = await provisioningConnection.OpenAsync(ct);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(ct);

        var tenantId = idGenerator.NewId();
        await ExecAsync(connection, transaction, """
            INSERT tenancy.Tenant (Id, Code, Name, Industry, TimeZoneId, IsPlatform, CreatedAtUtc)
            VALUES (@id, @code, @name, @industry, @timeZoneId, 0, @now);
            """,
            ("@id", tenantId), ("@code", request.Code), ("@name", request.Name),
            ("@industry", request.Industry), ("@timeZoneId", "Asia/Karachi"), ("@now", now));

        var branchId = idGenerator.NewId();
        await ExecAsync(connection, transaction, """
            INSERT tenancy.Branch (TenantId, Id, Code, Name, CreatedAtUtc)
            VALUES (@tenantId, @id, @code, @name, @now);
            """,
            ("@tenantId", tenantId), ("@id", branchId), ("@code", request.BranchCode), ("@name", request.BranchName), ("@now", now));

        var warehouseId = idGenerator.NewId();
        await ExecAsync(connection, transaction, """
            INSERT inventory.Warehouse (TenantId, Id, BranchId, Code, Name, CreatedAtUtc)
            VALUES (@tenantId, @id, @branchId, @code, @name, @now);
            """,
            ("@tenantId", tenantId), ("@id", warehouseId), ("@branchId", branchId),
            ("@code", request.WarehouseCode), ("@name", request.WarehouseName), ("@now", now));

        await CreateStandardLocationsAsync(connection, transaction, tenantId, warehouseId, now);

        var allNonPlatformPermissions = await GetAllNonPlatformPermissionCodesAsync(connection, transaction, ct);
        var rolePermissionMap = new Dictionary<string, IReadOnlyList<string>>();
        Guid? ownerRoleId = null;

        foreach (var roleTemplate in template.Roles)
        {
            var permissionCodes = (roleTemplate.IsAllNonPlatform
                    ? allNonPlatformPermissions
                    : roleTemplate.ExplicitPermissionCodes.Append("Identity.Self.Manage"))
                .Distinct()
                .OrderBy(c => c, StringComparer.Ordinal)
                .ToList();

            var roleId = await CreateRoleAsync(connection, transaction, tenantId, roleTemplate, permissionCodes, now, ct);
            rolePermissionMap[roleTemplate.Name] = permissionCodes;

            if (roleTemplate.Name == "Owner")
            {
                ownerRoleId = roleId;
            }
        }

        if (ownerRoleId is null)
        {
            throw new InvalidOperationException($"Template for industry '{request.Industry}' has no 'Owner' role.");
        }

        var ownerUserId = idGenerator.NewId();
        var oneTimePassword = OneTimePasswordGenerator.Generate();
        var passwordHash = PasswordHasher.HashPassword(new object(), oneTimePassword);

        await ExecAsync(connection, transaction, """
            INSERT [identity].[User] (TenantId, Id, Username, DisplayName, Email, Phone, PasswordHash, DefaultBranchId, CreatedAtUtc)
            VALUES (@tenantId, @id, @username, @displayName, @email, @phone, @passwordHash, @branchId, @now);
            """,
            ("@tenantId", tenantId), ("@id", ownerUserId), ("@username", request.OwnerUsername),
            ("@displayName", request.OwnerDisplayName), ("@email", request.OwnerEmail), ("@phone", request.OwnerPhone),
            ("@passwordHash", passwordHash), ("@branchId", branchId), ("@now", now));

        await ExecAsync(connection, transaction, """
            INSERT [identity].UserRole (TenantId, Id, UserId, RoleId, BranchId)
            VALUES (@tenantId, @id, @userId, @roleId, NULL);
            """,
            ("@tenantId", tenantId), ("@id", idGenerator.NewId()), ("@userId", ownerUserId), ("@roleId", ownerRoleId.Value));

        foreach (var (featureKey, isEnabled) in template.Features)
        {
            await ExecAsync(connection, transaction, """
                INSERT tenancy.TenantFeature (TenantId, FeatureKey, IsEnabled, UpdatedAtUtc)
                VALUES (@tenantId, @key, @enabled, @now);
                """,
                ("@tenantId", tenantId), ("@key", featureKey), ("@enabled", isEnabled), ("@now", now));
        }

        foreach (var (settingKey, value) in template.Settings)
        {
            await ExecAsync(connection, transaction, """
                INSERT tenancy.TenantSetting (TenantId, SettingKey, Value, UpdatedAtUtc)
                VALUES (@tenantId, @key, @value, @now);
                """,
                ("@tenantId", tenantId), ("@key", settingKey), ("@value", value), ("@now", now));
        }

        foreach (var series in template.NumberSeries)
        {
            await ExecAsync(connection, transaction, """
                INSERT platform.NumberSeries (TenantId, DocumentType, BranchId, Prefix, PaddingLength)
                VALUES (@tenantId, @documentType, NULL, @prefix, @padding);
                """,
                ("@tenantId", tenantId), ("@documentType", series.DocumentType),
                ("@prefix", series.Prefix), ("@padding", series.PaddingLength));
        }

        await TemplateCatalogSeeder.SeedAsync(connection, transaction, tenantId, template, now, ct);

        await transaction.CommitAsync(ct);

        return new ProvisionTenantResult(tenantId, branchId, warehouseId, ownerUserId, oneTimePassword, rolePermissionMap);
    }

    public async Task<int?> ApplyMissingTemplateDataAsync(Guid tenantId, CancellationToken ct)
    {
        await using var connection = await provisioningConnection.OpenAsync(ct);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(ct);

        await using var lookup = connection.CreateCommand();
        lookup.Transaction = transaction;
        lookup.CommandText = "SELECT Industry FROM tenancy.Tenant WHERE Id = @id AND IsPlatform = 0;";
        lookup.Parameters.Add(new SqlParameter("@id", tenantId));
        if (await lookup.ExecuteScalarAsync(ct) is not string industry)
        {
            return null;
        }

        var inserted = await TemplateCatalogSeeder.SeedAsync(connection, transaction, tenantId, IndustryTemplate.Load(industry), clock.UtcNow, ct);
        await transaction.CommitAsync(ct);
        return inserted;
    }

    private static async Task CreateStandardLocationsAsync(
        SqlConnection connection, SqlTransaction transaction, Guid tenantId, Guid warehouseId, DateTime now)
    {
        var locations = new[]
        {
            ("MAIN", "Main Storage", "Storage", true),
            ("TRANSIT", "In Transit", "InTransit", false),
            ("QUARANTINE", "Quarantine", "Quarantine", false),
        };

        foreach (var (code, name, type, isSellable) in locations)
        {
            await ExecAsync(connection, transaction, """
                INSERT inventory.Location (TenantId, WarehouseId, Code, Name, LocationType, IsSellable, CreatedAtUtc)
                VALUES (@tenantId, @warehouseId, @code, @name, @type, @isSellable, @now);
                """,
                ("@tenantId", tenantId), ("@warehouseId", warehouseId), ("@code", code),
                ("@name", name), ("@type", type), ("@isSellable", isSellable), ("@now", now));
        }
    }

    private static async Task<List<string>> GetAllNonPlatformPermissionCodesAsync(
        SqlConnection connection, SqlTransaction transaction, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT Code FROM [identity].Permission WHERE IsPlatformOnly = 0;";

        var codes = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            codes.Add(reader.GetString(0));
        }

        return codes;
    }

    private async Task<Guid> CreateRoleAsync(
        SqlConnection connection, SqlTransaction transaction, Guid tenantId, RoleTemplate roleTemplate,
        List<string> permissionCodes, DateTime now, CancellationToken ct)
    {
        var roleId = idGenerator.NewId();
        await ExecAsync(connection, transaction, """
            INSERT [identity].Role (TenantId, Id, Name, IsSystem, CreatedAtUtc)
            VALUES (@tenantId, @id, @name, @isSystem, @now);
            """,
            ("@tenantId", tenantId), ("@id", roleId), ("@name", roleTemplate.Name),
            ("@isSystem", roleTemplate.IsSystem), ("@now", now));

        foreach (var code in permissionCodes)
        {
            await ExecAsync(connection, transaction, """
                INSERT [identity].RolePermission (TenantId, RoleId, PermissionCode)
                VALUES (@tenantId, @roleId, @code);
                """,
                ("@tenantId", tenantId), ("@roleId", roleId), ("@code", code));
        }

        return roleId;
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
