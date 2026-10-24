using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StockOrder.Infrastructure.Database;

namespace StockOrder.Infrastructure.Outbox;

/// <summary>Registered only by the Worker host (section 15) — <see cref="OutboxDispatcherService"/> itself is internal.</summary>
public static class OutboxDispatcherServiceCollectionExtensions
{
    public static IServiceCollection AddOutboxDispatcher(this IServiceCollection services) =>
        services.AddHostedService<OutboxDispatcherService>();
}

/// <summary>
/// Polls platform.OutboxMessage every 2 seconds and dispatches to in-process handlers
/// (section 15). Registered only by the Worker host. Reads/writes span every tenant, so —
/// like the Worker's other cross-tenant jobs — it runs on the RLS bypass connection
/// (section 6); each handler still only touches the single TenantId carried on its message.
/// </summary>
internal sealed class OutboxDispatcherService(
    IBypassConnectionFactory bypassFactory,
    IEnumerable<IOutboxMessageHandler> handlers,
    ILogger<OutboxDispatcherService> logger) : BackgroundService
{
    private const int MaxAttempts = 10;
    private readonly Dictionary<string, IOutboxMessageHandler> _handlersByType =
        handlers.ToDictionary(h => h.MessageType, StringComparer.Ordinal);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DispatchPendingAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Outbox dispatch loop failed");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Shutting down.
            }
        }
    }

    private async Task DispatchPendingAsync(CancellationToken ct)
    {
        await using var connection = await bypassFactory.OpenAsync(ct);

        var messages = new List<PendingMessage>();
        await using (var selectCommand = connection.CreateCommand())
        {
            selectCommand.CommandText = """
                SELECT TOP (50) Id, TenantId, MessageType, Payload, Attempts
                FROM platform.OutboxMessage
                WHERE ProcessedAtUtc IS NULL AND Attempts < @maxAttempts
                ORDER BY ClusterKey;
                """;
            selectCommand.Parameters.Add(new SqlParameter("@maxAttempts", MaxAttempts));

            await using var reader = await selectCommand.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                messages.Add(new PendingMessage(
                    reader.GetGuid(0),
                    reader.IsDBNull(1) ? null : reader.GetGuid(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetInt32(4)));
            }
        }

        foreach (var message in messages)
        {
            await DispatchOneAsync(connection, message, ct);
        }
    }

    private async Task DispatchOneAsync(SqlConnection connection, PendingMessage message, CancellationToken ct)
    {
        if (!_handlersByType.TryGetValue(message.MessageType, out var handler))
        {
            // Events nobody consumes yet (e.g. SalesOrderConfirmed before any handler exists) are done:
            // left pending they would be re-polled forever and, once 50 piled up, starve real messages.
            if (logger.IsEnabled(LogLevel.Debug))
            {
                logger.LogDebug("No outbox handler registered for MessageType={MessageType}; marking processed", message.MessageType);
            }

            await ExecuteAsync(connection, """
                UPDATE platform.OutboxMessage
                SET ProcessedAtUtc = SYSUTCDATETIME(), LastError = @error
                WHERE Id = @id;
                """, message.Id, "No handler registered", ct);
            return;
        }

        try
        {
            await handler.HandleAsync(message.Payload, message.TenantId, ct);
            await ExecuteAsync(connection, """
                UPDATE platform.OutboxMessage SET ProcessedAtUtc = SYSUTCDATETIME() WHERE Id = @id;
                """, message.Id, null, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Outbox message {Id} ({MessageType}) failed on attempt {Attempt}",
                message.Id,
                message.MessageType,
                message.Attempts + 1);

            await ExecuteAsync(connection, """
                UPDATE platform.OutboxMessage
                SET Attempts = Attempts + 1, LastError = @error
                WHERE Id = @id;
                """, message.Id, ex.Message, ct);
        }
    }

    private static async Task ExecuteAsync(SqlConnection connection, string sql, Guid id, string? error, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.Add(new SqlParameter("@id", id));
        if (error is not null)
        {
            command.Parameters.Add(new SqlParameter("@error", error.Length > 2000 ? error[..2000] : error));
        }

        await command.ExecuteNonQueryAsync(ct);
    }

    private sealed record PendingMessage(Guid Id, Guid? TenantId, string MessageType, string Payload, int Attempts);
}
