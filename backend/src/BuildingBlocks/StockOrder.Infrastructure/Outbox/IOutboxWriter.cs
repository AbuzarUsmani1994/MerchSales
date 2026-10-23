namespace StockOrder.Infrastructure.Outbox;

/// <summary>
/// Writes a platform.OutboxMessage row on the current request's connection and transaction
/// (section 5.5), so the message only becomes visible if the whole command commits. The
/// Worker's dispatcher picks these up and invokes the matching in-process handler
/// (notifications, FCM pushes, reorder suggestions...).
/// </summary>
public interface IOutboxWriter
{
    /// <param name="tenantId">Null for platform-level events.</param>
    Task AddAsync(Guid? tenantId, string messageType, object payload, CancellationToken ct);
}
