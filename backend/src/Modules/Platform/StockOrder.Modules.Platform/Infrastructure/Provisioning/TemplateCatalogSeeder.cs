using Microsoft.Data.SqlClient;

namespace StockOrder.Modules.Platform.Infrastructure.Provisioning;

/// <summary>
/// Writes a template's catalog and sales basics for one tenant: units, product types, tax codes, the
/// default price list ("Menu" / "Trade price"), payment methods, the walk-in customer and the
/// current month's accounting period. Insert-if-missing by natural key, so it is safe
/// both inside provisioning and when re-applied to a tenant created before this existed.
/// Runs on the provisioning (bypass) connection, like the rest of provisioning.
/// </summary>
internal static class TemplateCatalogSeeder
{
    public static async Task<int> SeedAsync(
        SqlConnection connection, SqlTransaction transaction, Guid tenantId, IndustryTemplate template, DateTime now, DateOnly today, CancellationToken ct)
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

        foreach (var code in template.PaymentMethods)
        {
            if (!PaymentMethodKinds.ByCode.TryGetValue(code, out var method))
            {
                throw new InvalidOperationException($"Template payment method '{code}' is not known.");
            }

            inserted += await ExecAsync(connection, transaction, """
                INSERT finance.PaymentMethod (TenantId, Code, Name, Kind)
                SELECT @tenantId, @code, @name, @kind
                WHERE NOT EXISTS (SELECT 1 FROM finance.PaymentMethod WHERE TenantId = @tenantId AND Code = @code);
                """, ct,
                ("@tenantId", tenantId), ("@code", code), ("@name", method.Name), ("@kind", method.Kind));
        }

        if (template.Records.WalkInCustomer is { } walkIn)
        {
            // One walk-in row per tenant (UX_Customer_OneWalkIn); anonymous sales are booked to it.
            inserted += await ExecAsync(connection, transaction, """
                INSERT sales.Customer (TenantId, Code, Name, IsWalkIn, CreatedAtUtc)
                SELECT @tenantId, @code, @name, 1, @now
                WHERE NOT EXISTS (SELECT 1 FROM sales.Customer WHERE TenantId = @tenantId AND (IsWalkIn = 1 OR Code = @code));
                """, ct,
                ("@tenantId", tenantId), ("@code", walkIn.Code), ("@name", walkIn.Name), ("@now", now));
        }

        if (template.Records.OpenAccountingPeriodForCurrentMonth)
        {
            var start = new DateOnly(today.Year, today.Month, 1);
            inserted += await ExecAsync(connection, transaction, """
                INSERT finance.AccountingPeriod (TenantId, StartDate, EndDate)
                SELECT @tenantId, @start, @end
                WHERE NOT EXISTS (SELECT 1 FROM finance.AccountingPeriod
                                  WHERE TenantId = @tenantId AND StartDate <= @end AND EndDate >= @start);
                """, ct,
                ("@tenantId", tenantId), ("@start", start.ToDateTime(TimeOnly.MinValue)),
                ("@end", start.AddMonths(1).AddDays(-1).ToDateTime(TimeOnly.MinValue)));
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
