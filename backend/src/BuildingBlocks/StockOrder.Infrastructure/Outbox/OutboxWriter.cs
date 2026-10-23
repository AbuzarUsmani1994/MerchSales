using System.Text.Json;
using StockOrder.Infrastructure.Database;

namespace StockOrder.Infrastructure.Outbox;

internal sealed class OutboxWriter(IUnitOfWork unitOfWork) : IOutboxWriter
{
    public async Task AddAsync(Guid? tenantId, string messageType, object payload, CancellationToken ct)
    {
        await using var command = unitOfWork.Connection.CreateCommand();
        command.Transaction = unitOfWork.Transaction;
        command.CommandText = """
            INSERT platform.OutboxMessage (TenantId, MessageType, Payload)
            VALUES (@tenantId, @messageType, @payload);
            """;
        command.Parameters.AddWithValue("@tenantId", (object?)tenantId ?? DBNull.Value);
        command.Parameters.AddWithValue("@messageType", messageType);
        command.Parameters.AddWithValue("@payload", JsonSerializer.Serialize(payload));

        await command.ExecuteNonQueryAsync(ct);
    }
}
