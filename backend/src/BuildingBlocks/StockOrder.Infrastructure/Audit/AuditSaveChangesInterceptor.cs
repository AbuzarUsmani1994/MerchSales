using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using StockOrder.Infrastructure.Correlation;
using StockOrder.SharedKernel.Context;
using StockOrder.SharedKernel.Entities;
using StockOrder.SharedKernel.Time;

namespace StockOrder.Infrastructure.Audit;

/// <summary>
/// Writes platform.AuditLog rows for every Added/Modified/Deleted <see cref="TenantEntity"/>,
/// in the same connection and transaction as the change itself (section 5, Phase 0).
/// </summary>
public sealed class AuditSaveChangesInterceptor(
    ICurrentUserContext currentUser,
    IClock clock,
    ICorrelationIdAccessor correlationId) : SaveChangesInterceptor
{
    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        var context = eventData.Context;
        if (context is null)
        {
            return result;
        }

        var entries = context.ChangeTracker.Entries()
            .Where(e => e.Entity is TenantEntity &&
                        e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .ToList();

        if (entries.Count == 0)
        {
            return result;
        }

        var connection = context.Database.GetDbConnection();
        var transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        var userId = currentUser.IsAuthenticated ? currentUser.UserId : (Guid?)null;
        var tenantId = ((TenantEntity)entries[0].Entity).TenantId;

        foreach (var entry in entries)
        {
            var entityName = entry.Metadata.ClrType.Name;
            var entityId = entry.Property(nameof(TenantEntity.Id)).CurrentValue?.ToString() ?? string.Empty;
            var action = entry.State switch
            {
                EntityState.Added => "Created",
                EntityState.Modified => "Updated",
                EntityState.Deleted => "Deleted",
                _ => "Unknown",
            };

            string? oldValues = entry.State is EntityState.Modified or EntityState.Deleted
                ? JsonSerializer.Serialize(entry.Properties.ToDictionary(p => p.Metadata.Name, p => p.OriginalValue))
                : null;

            string? newValues = entry.State is EntityState.Added or EntityState.Modified
                ? JsonSerializer.Serialize(entry.Properties.ToDictionary(p => p.Metadata.Name, p => p.CurrentValue))
                : null;

            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT platform.AuditLog (TenantId, UserId, EntityName, EntityId, Action, OldValues, NewValues, CorrelationId, AtUtc)
                VALUES (@tenantId, @userId, @entityName, @entityId, @action, @oldValues, @newValues, @correlationId, @atUtc);
                """;
            AddParameter(command, "@tenantId", tenantId);
            AddParameter(command, "@userId", userId);
            AddParameter(command, "@entityName", entityName);
            AddParameter(command, "@entityId", entityId);
            AddParameter(command, "@action", action);
            AddParameter(command, "@oldValues", oldValues);
            AddParameter(command, "@newValues", newValues);
            AddParameter(command, "@correlationId", correlationId.CorrelationId);
            AddParameter(command, "@atUtc", clock.UtcNow);

            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        return result;
    }

    private static void AddParameter(System.Data.Common.DbCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }
}
