namespace StockOrder.SharedKernel.Ids;

/// <summary>
/// Generates the ids entities use as their uniqueidentifier primary key (section 5.8, 7).
/// Sequential (time-ordered) so repeated nonclustered-index inserts don't fragment, and so
/// tests can assert ordering without touching the clock.
/// </summary>
public interface IIdGenerator
{
    Guid NewId();
}

/// <summary>UUIDv7 (RFC 9562): monotonically increasing, generated in code — no round trip to the database.</summary>
public sealed class SequentialIdGenerator : IIdGenerator
{
    public Guid NewId() => Guid.CreateVersion7();
}
