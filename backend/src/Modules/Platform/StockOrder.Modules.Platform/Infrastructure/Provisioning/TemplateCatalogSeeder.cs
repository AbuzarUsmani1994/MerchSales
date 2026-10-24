using Microsoft.Data.SqlClient;

namespace StockOrder.Modules.Platform.Infrastructure.Provisioning;

/// <summary>
/// Writes a template's catalog basics for one tenant: units, product types, tax codes and the
/// default price list ("Menu" / "Trade price"). Insert-if-missing by natural key, so it is safe
/// both inside provisioning and when re-applied to a tenant created before this existed.
/// Runs on the provisioning (bypass) connection, like the rest of provisioning.
/// </summary>
internal static class TemplateCatalogSeeder
{
    public static async Task<int> SeedAsync(
        SqlConnection connection, SqlTransaction transaction, Guid tenantId, IndustryTemplate template, DateTime now, CancellationToken ct)
    {
        var inserted = 0;

        foreach (var unit in template.Units)
        {
            inserted += await ExecAsync(connection, transaction, """
                INSERT catalog.UnitOfMeasure (TenantId, Code, Name, DecimalPlaces)
                SELECT @tenantId, @code, @name, @decimals
                WHERE NOT EXISTS (SELECT 1 FROM catalog.UnitOfMeasure WHERE TenantId = @tenantId AND Code = @code);
                """, ct,
                ("@tenantId", tenantId), ("@code", unit.Code), ("@name", unit.Name), ("@decimals", unit.DecimalPlaces));
        }

        foreach (var type in template.ProductTypes)
        {
            inserted += await ExecAsync(connection, transaction, """
                INSERT catalog.ProductType (TenantId, Name, Behaviour, DefaultTracking, DefaultIssueRule)
                SELECT @tenantId, @name, @behaviour, @tracking, @issueRule
                WHERE NOT EXISTS (SELECT 1 FROM catalog.ProductType WHERE TenantId = @tenantId AND Name = @name);
                """, ct,
                ("@tenantId", tenantId), ("@name", type.Code), ("@behaviour", type.Behaviour),
                ("@tracking", type.DefaultTracking), ("@issueRule", type.IssueRule));
        }

        foreach (var tax in template.TaxCodes)
        {
            inserted += await ExecAsync(connection, transaction, """
                INSERT catalog.TaxCode (TenantId, Code, Name, RatePercent)
                SELECT @tenantId, @code, @code, @rate
                WHERE NOT EXISTS (SELECT 1 FROM catalog.TaxCode WHERE TenantId = @tenantId AND Code = @code);
                """, ct,
                ("@tenantId", tenantId), ("@code", tax.Code), ("@rate", tax.RatePercent));
        }

        if (template.Records.DefaultPriceList is { } priceList)
        {
            // Default only if the tenant has none yet (one default per tenant, UX_PriceList_OneDefault).
            inserted += await ExecAsync(connection, transaction, """
                INSERT catalog.PriceList (TenantId, Name, Currency, IsDefault, CreatedAtUtc)
                SELECT @tenantId, @name, 'PKR',
                       CASE WHEN EXISTS (SELECT 1 FROM catalog.PriceList WHERE TenantId = @tenantId AND IsDefault = 1) THEN 0 ELSE 1 END,
                       @now
                WHERE NOT EXISTS (SELECT 1 FROM catalog.PriceList WHERE TenantId = @tenantId AND Name = @name);
                """, ct,
                ("@tenantId", tenantId), ("@name", priceList.Name), ("@now", now));
        }

        return inserted;
    }

    private static async Task<int> ExecAsync(
        SqlConnection connection, SqlTransaction transaction, string sql, CancellationToken ct, params (string Name, object? Value)[] parameters)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.Add(new SqlParameter(name, value ?? DBNull.Value));
        }

        return await command.ExecuteNonQueryAsync(ct);
    }
}
