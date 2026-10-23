namespace StockOrder.Infrastructure.Outbox;

/// <summary>A module registers one of these per outbox MessageType it wants to consume.</summary>
public interface IOutboxMessageHandler
{
    string MessageType { get; }
    Task HandleAsync(string payloadJson, Guid? tenantId, CancellationToken ct);
}
