namespace StockOrder.Modules.Identity.Contracts;

/// <summary>Display names for screens that show who did something, e.g. which waiter took an order.</summary>
public interface IUserDirectory
{
    Task<IReadOnlyDictionary<Guid, string>> GetDisplayNamesAsync(IReadOnlyCollection<Guid> userIds, CancellationToken ct);
}
