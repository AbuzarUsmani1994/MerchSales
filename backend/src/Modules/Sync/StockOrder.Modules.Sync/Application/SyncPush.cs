using System.Text.Json;
using Dapper;
using Mediator;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using StockOrder.Infrastructure.Database;
using StockOrder.Modules.Sync.Contracts;
using StockOrder.SharedKernel.Context;
using StockOrder.SharedKernel.Errors;
using StockOrder.SharedKernel.Results;
using StockOrder.SharedKernel.Time;

namespace StockOrder.Modules.Sync.Application;

public sealed record SyncPushCommandItem(Guid Id, string Type, long ClientSequence, DateTime CreatedOnDeviceAtUtc, JsonElement Payload);

public sealed record SyncPushCommand(Guid DeviceId, IReadOnlyList<SyncPushCommandItem> Commands) : ICommand<Result<SyncPushResult>>;

public sealed record SyncCommandResult(Guid Id, string Status, string? ErrorCode, string? Message, object? Result);

public sealed record SyncPushResult(IReadOnlyList<SyncCommandResult> Results);

/// <summary>
/// Section 12 push. Commands run in clientSequence order, each isolated by a savepoint (the
/// request already holds one transaction, section 5.4), so a rejected or failed command undoes
/// only its own writes.
/// <list type="bullet">
/// <item>Applied and Rejected outcomes are final: stored in sync.CommandLog, and sending the
/// same command id again replays the stored result instead of applying it twice.</item>
/// <item>Failed (an unexpected server error, nothing written) is not stored, so the phone
/// retries it with the same id later. Commands that depend on it fail the same way for now,
/// instead of being rejected for good.</item>
/// </list>
/// </summary>
public sealed class SyncPushCommandHandler(
    IEnumerable<ISyncCommandHandler> handlers,
    IUnitOfWork unitOfWork,
    ScopedDbContexts dbContexts,
    ICurrentUserContext currentUser,
    IClock clock,
    ILogger<SyncPushCommandHandler> logger)
    : ICommandHandler<SyncPushCommand, Result<SyncPushResult>>
{
    public const int MaxCommands = 100;
    private const int MaxCommandTypeLength = 60;

    private static readonly JsonSerializerOptions StoredJson = new(JsonSerializerDefaults.Web);

    public async ValueTask<Result<SyncPushResult>> Handle(SyncPushCommand command, CancellationToken ct)
    {
        // The token's device claim, not the body, says which phone this is (section 6 spirit:
        // identity comes from the validated token). The body must agree with it.
        if (currentUser.DeviceId is not { } deviceId || deviceId != command.DeviceId)
        {
            return Result.Failure<SyncPushResult>(Error.PermissionDenied("Sign in on this device before pushing commands."));
        }

        if (Validate(command) is { } invalid)
        {
            return Result.Failure<SyncPushResult>(invalid);
        }

        var handlersByType = handlers.ToDictionary(h => h.CommandType, StringComparer.Ordinal);
        var transaction = (SqlTransaction)unitOfWork.Transaction!;
        var rejectedIds = new HashSet<Guid>();
        var failedIds = new HashSet<Guid>();
        var results = new List<SyncCommandResult>();

        foreach (var item in command.Commands.OrderBy(c => c.ClientSequence))
        {
            if (await FindStoredAsync(item.Id, ct) is { } stored)
            {
                results.Add(stored);
                if (stored.Status == nameof(SyncCommandStatus.Rejected))
                {
                    rejectedIds.Add(item.Id);
                }

                continue;
            }

            var outcome = await ApplyAsync(item, deviceId, handlersByType, rejectedIds, failedIds, transaction, ct);
            var result = new SyncCommandResult(item.Id, outcome.Status.ToString(), outcome.ErrorCode, outcome.Message, outcome.Result);
            results.Add(result);

            if (outcome.Status == SyncCommandStatus.Failed)
            {
                failedIds.Add(item.Id);
                continue;
            }

            if (outcome.Status == SyncCommandStatus.Rejected)
            {
                rejectedIds.Add(item.Id);
            }

            await StoreAsync(item, deviceId, result, ct);
        }

        return Result.Success(new SyncPushResult(results));
    }

    private async Task<SyncCommandOutcome> ApplyAsync(
        SyncPushCommandItem item,
        Guid deviceId,
        Dictionary<string, ISyncCommandHandler> handlersByType,
        HashSet<Guid> rejectedIds,
        HashSet<Guid> failedIds,
        SqlTransaction transaction,
        CancellationToken ct)
    {
        var referenced = PayloadGuids.Find(item.Payload).Where(id => id != item.Id).ToHashSet();

        if (await ReferencesRejectedCommandAsync(referenced, rejectedIds, ct))
        {
            return SyncCommandOutcome.Rejected(
                SyncErrorCodes.DependencyRejected, "This command depends on an earlier command that was rejected.");
        }

        if (referenced.Overlaps(failedIds))
        {
            return new SyncCommandOutcome(
                SyncCommandStatus.Failed, SyncErrorCodes.DependencyFailed, "Waiting for an earlier command that failed; will retry.", null);
        }

        if (!handlersByType.TryGetValue(item.Type, out var handler))
        {
            return SyncCommandOutcome.Rejected(SyncErrorCodes.UnknownCommandType, $"Unknown command type '{item.Type}'.");
        }

        // Savepoint names are limited to 32 characters, so not the command's GUID.
        var savepoint = "sync_cmd_" + item.ClientSequence.ToString(System.Globalization.CultureInfo.InvariantCulture);
        transaction.Save(savepoint);
        try
        {
            var outcome = await handler.ApplyAsync(
                new SyncCommandContext(item.Id, deviceId, currentUser.UserId, item.CreatedOnDeviceAtUtc), item.Payload, ct);
            if (outcome.Status is SyncCommandStatus.Rejected or SyncCommandStatus.Failed)
            {
                transaction.Rollback(savepoint);
                dbContexts.ClearChangeTrackers();
            }

            return outcome;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            transaction.Rollback(savepoint);
            dbContexts.ClearChangeTrackers();
            logger.LogError(e, "Sync command {CommandId} of type {CommandType} failed", item.Id, item.Type);
            return new SyncCommandOutcome(SyncCommandStatus.Failed, "COMMAND_FAILED", "The server could not apply this command.", null);
        }
    }

    /// <summary>
    /// Section 12: a command whose payload references the client id of a rejected command is
    /// rejected too. Any GUID string in the payload counts as a reference — client ids are
    /// GUIDs and appear wherever a later command points at an earlier one.
    /// </summary>
    private async Task<bool> ReferencesRejectedCommandAsync(HashSet<Guid> referenced, HashSet<Guid> rejectedIds, CancellationToken ct)
    {
        if (referenced.Count == 0)
        {
            return false;
        }

        if (referenced.Overlaps(rejectedIds))
        {
            return true;
        }

        // Rejected in an earlier push.
        var earlier = await unitOfWork.Connection.ExecuteScalarAsync<int>(new CommandDefinition(
            """
            SELECT COUNT(*) FROM sync.CommandLog
            WHERE TenantId = @tenantId AND Id IN @ids AND Status = 'Rejected';
            """,
            new { tenantId = currentUser.TenantId, ids = referenced },
            unitOfWork.Transaction,
            cancellationToken: ct));
        return earlier > 0;
    }

    private async Task<SyncCommandResult?> FindStoredAsync(Guid commandId, CancellationToken ct)
    {
        var resultJson = await unitOfWork.Connection.QueryFirstOrDefaultAsync<string?>(new CommandDefinition(
            "SELECT ResultJson FROM sync.CommandLog WHERE TenantId = @tenantId AND Id = @id;",
            new { tenantId = currentUser.TenantId, id = commandId },
            unitOfWork.Transaction,
            cancellationToken: ct));

        return resultJson is null ? null : JsonSerializer.Deserialize<SyncCommandResult>(resultJson, StoredJson);
    }

    private async Task StoreAsync(SyncPushCommandItem item, Guid deviceId, SyncCommandResult result, CancellationToken ct) =>
        await unitOfWork.Connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT sync.CommandLog (TenantId, Id, DeviceId, UserId, CommandType, ClientSequence, CreatedOnDeviceAtUtc,
                                    PayloadJson, ProcessedAtUtc, Status, ErrorCode, ResultJson)
            VALUES (@tenantId, @id, @deviceId, @userId, @type, @clientSequence, @createdOnDevice,
                    @payload, @now, @status, @errorCode, @resultJson);
            """,
            new
            {
                tenantId = currentUser.TenantId,
                id = item.Id,
                deviceId,
                userId = currentUser.UserId,
                type = item.Type,
                clientSequence = item.ClientSequence,
                createdOnDevice = item.CreatedOnDeviceAtUtc,
                payload = item.Payload.GetRawText(),
                now = clock.UtcNow,
                status = result.Status,
                errorCode = result.ErrorCode,
                resultJson = JsonSerializer.Serialize(result, StoredJson),
            },
            unitOfWork.Transaction,
            cancellationToken: ct));

    private static Error? Validate(SyncPushCommand command)
    {
        if (command.Commands is null || command.Commands.Count == 0)
        {
            return Error.Validation("commands must contain at least one command.");
        }

        if (command.Commands.Count > MaxCommands)
        {
            return Error.Validation($"At most {MaxCommands} commands per push.");
        }

        if (command.Commands.Select(c => c.Id).Distinct().Count() != command.Commands.Count)
        {
            return Error.Validation("Command ids must be unique within a push.");
        }

        foreach (var item in command.Commands)
        {
            if (item.Id == Guid.Empty || string.IsNullOrWhiteSpace(item.Type) || item.Type.Length > MaxCommandTypeLength)
            {
                return Error.Validation("Every command needs an id and a type of at most 60 characters.");
            }

            if (item.Payload.ValueKind != JsonValueKind.Object)
            {
                return Error.Validation($"Command {item.Id}: payload must be a JSON object.");
            }
        }

        return null;
    }
}

/// <summary>Every GUID-shaped string anywhere in a JSON payload.</summary>
public static class PayloadGuids
{
    public static IEnumerable<Guid> Find(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                if (Guid.TryParse(element.GetString(), out var id))
                {
                    yield return id;
                }

                break;
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    foreach (var nested in Find(property.Value))
                    {
                        yield return nested;
                    }
                }

                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    foreach (var nested in Find(item))
                    {
                        yield return nested;
                    }
                }

                break;
        }
    }
}
