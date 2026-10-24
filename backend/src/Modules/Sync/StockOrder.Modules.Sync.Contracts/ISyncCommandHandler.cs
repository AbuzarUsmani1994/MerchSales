using System.Text.Json;

namespace StockOrder.Modules.Sync.Contracts;

/// <summary>
/// Applies one pushed offline command (section 12). Each type maps to the same application
/// command its REST endpoint uses — no duplicate business logic in the sync path. Runs inside
/// a savepoint of the push request's transaction: a Rejected or Failed outcome undoes only
/// this command's writes.
/// </summary>
public interface ISyncCommandHandler
{
    string CommandType { get; }

    Task<SyncCommandOutcome> ApplyAsync(SyncCommandContext context, JsonElement payload, CancellationToken ct);
}

public sealed record SyncCommandContext(Guid CommandId, Guid DeviceId, Guid UserId, DateTime CreatedOnDeviceAtUtc);

public enum SyncCommandStatus
{
    Applied,
    AppliedWithChanges,
    Rejected,
    Failed,
}

public sealed record SyncCommandOutcome(SyncCommandStatus Status, string? ErrorCode, string? Message, object? Result)
{
    public static SyncCommandOutcome Applied(object? result = null) => new(SyncCommandStatus.Applied, null, null, result);

    public static SyncCommandOutcome Rejected(string errorCode, string message) =>
        new(SyncCommandStatus.Rejected, errorCode, message, null);
}

public static class SyncErrorCodes
{
    public const string DependencyRejected = "DEPENDENCY_REJECTED";
    public const string UnknownCommandType = "UNKNOWN_COMMAND_TYPE";
    public const string InvalidPayload = "INVALID_PAYLOAD";
}
