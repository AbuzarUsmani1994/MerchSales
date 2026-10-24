using System.Text.Json;
using Dapper;
using Microsoft.EntityFrameworkCore;
using StockOrder.Infrastructure.Database;
using StockOrder.Modules.Sync.Contracts;
using StockOrder.SharedKernel.Context;

namespace StockOrder.Modules.Identity.Infrastructure.Sync;

/// <summary>
/// Section 12 entity "permissions (for this user)". A user's permissions come from several
/// tables (UserRole, RolePermission) without one rowversion to page by, but every change to
/// them rotates the user's security stamp (section 10). So the token is the stamp: an unchanged
/// stamp sends nothing, a changed one sends the full set, each with its branch scope.
/// </summary>
internal sealed class PermissionsSyncSource(IAccessScope accessScope, IdentityDbContext db, ICurrentUserContext currentUser)
    : ISyncSource
{
    public string Entity => "permissions";

    public async Task<SyncEntityPage> PullAsync(SyncPullRequest request, CancellationToken ct)
    {
        var stamp = await db.Users
            .Where(u => u.TenantId == currentUser.TenantId && u.Id == request.UserId)
            .Select(u => u.SecurityStamp)
            .FirstAsync(ct);
        var token = stamp.ToString("N");

        if (request.Token == token)
        {
            return new SyncEntityPage([], token, HasMore: false);
        }

        var grants = await accessScope.GetGrantsAsync(ct);
        return new SyncEntityPage(
            grants.Select(g => (object)new { g.Code, g.BranchId }).ToList(),
            token,
            HasMore: false);
    }
}

/// <summary>
/// Section 12 command "UpdatePushToken": the phone's FCM token changed (reinstall, token
/// refresh). Payload <c>{ "pushToken": "..." | null }</c>. Identity owns the device lifecycle.
/// </summary>
internal sealed class UpdatePushTokenCommandHandler(IUnitOfWork unitOfWork, ICurrentUserContext currentUser) : ISyncCommandHandler
{
    private const int MaxPushTokenLength = 500;

    public string CommandType => "UpdatePushToken";

    public async Task<SyncCommandOutcome> ApplyAsync(SyncCommandContext context, JsonElement payload, CancellationToken ct)
    {
        if (payload.ValueKind != JsonValueKind.Object ||
            !payload.TryGetProperty("pushToken", out var tokenElement) ||
            tokenElement.ValueKind is not (JsonValueKind.String or JsonValueKind.Null))
        {
            return SyncCommandOutcome.Rejected(SyncErrorCodes.InvalidPayload, "Expected { \"pushToken\": string | null }.");
        }

        var pushToken = tokenElement.ValueKind == JsonValueKind.Null ? null : tokenElement.GetString();
        if (pushToken is { Length: > MaxPushTokenLength })
        {
            return SyncCommandOutcome.Rejected(SyncErrorCodes.InvalidPayload, $"pushToken is longer than {MaxPushTokenLength} characters.");
        }

        await unitOfWork.Connection.ExecuteAsync(new CommandDefinition(
            "UPDATE sync.Device SET PushToken = @pushToken WHERE TenantId = @tenantId AND Id = @deviceId;",
            new { pushToken, tenantId = currentUser.TenantId, deviceId = context.DeviceId },
            unitOfWork.Transaction,
            cancellationToken: ct));

        return SyncCommandOutcome.Applied();
    }
}
