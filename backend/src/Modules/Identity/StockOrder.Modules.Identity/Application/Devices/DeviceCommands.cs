using Mediator;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using StockOrder.Infrastructure.Database;
using StockOrder.Modules.Identity.Domain;
using StockOrder.Modules.Identity.Infrastructure;
using StockOrder.SharedKernel.Context;
using StockOrder.SharedKernel.Errors;
using StockOrder.SharedKernel.Results;
using StockOrder.SharedKernel.Time;

namespace StockOrder.Modules.Identity.Application.Devices;

public sealed record DeviceSummary(
    Guid Id,
    string Platform,
    string? DeviceName,
    string? AppVersion,
    DateTime? LastSeenAtUtc,
    bool IsRevoked,
    bool WipeRequested,
    Guid UserId,
    string UserDisplayName,
    string Username);

public sealed record GetDevicesQuery : IQuery<Result<IReadOnlyList<DeviceSummary>>>;

/// <summary>
/// sync.Device is Sync's table, but Identity owns device lifecycle actions (section 10, 18) — queried directly via the shared connection rather than a full Sync module.
/// Filtered by <see cref="UserAdministrationPolicy.CanManageDevice"/>: a branch manager sees only their branch staff's devices.
/// </summary>
public sealed class GetDevicesQueryHandler(
    IUnitOfWork unitOfWork, ICurrentUserContext currentUser, IAccessScope accessScope, IdentityDbContext db)
    : IQueryHandler<GetDevicesQuery, Result<IReadOnlyList<DeviceSummary>>>
{
    public async ValueTask<Result<IReadOnlyList<DeviceSummary>>> Handle(GetDevicesQuery query, CancellationToken ct)
    {
        await using var command = unitOfWork.Connection.CreateCommand();
        command.Transaction = unitOfWork.Transaction;
        command.CommandText = """
            SELECT d.Id, d.Platform, d.DeviceName, d.AppVersion, d.LastSeenAtUtc, d.IsRevoked, d.WipeRequested,
                   d.UserId, u.DisplayName, u.Username
            FROM sync.Device d
            JOIN [identity].[User] u ON u.TenantId = d.TenantId AND u.Id = d.UserId
            WHERE d.TenantId = @tenantId
            ORDER BY d.RegisteredAtUtc DESC;
            """;
        command.Parameters.Add(new SqlParameter("@tenantId", currentUser.TenantId));

        var devices = new List<DeviceSummary>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            devices.Add(new DeviceSummary(
                reader.GetGuid(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetDateTime(4),
                reader.GetBoolean(5), reader.GetBoolean(6), reader.GetGuid(7), reader.GetString(8), reader.GetString(9)));
        }

        await reader.CloseAsync();

        var grants = await accessScope.GetGrantsAsync(ct);
        if (BranchScope.For(grants, UserAdministrationPolicy.DevicesManage).AllBranches)
        {
            return Result.Success<IReadOnlyList<DeviceSummary>>(devices);
        }

        var userIds = devices.Select(d => d.UserId).Distinct().ToList();
        var heldRoles = await db.GetHeldRolesByUserAsync(currentUser.TenantId, userIds, ct);
        var defaultBranches = await db.Users
            .Where(u => u.TenantId == currentUser.TenantId && userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.DefaultBranchId, ct);

        var visible = devices
            .Where(d => UserAdministrationPolicy.CanManageDevice(
                grants, heldRoles.GetValueOrDefault(d.UserId) ?? [], defaultBranches.GetValueOrDefault(d.UserId)) is null)
            .ToList();

        return Result.Success<IReadOnlyList<DeviceSummary>>(visible);
    }
}

public sealed record RevokeDeviceCommand(Guid DeviceId, bool RequestWipe) : ICommand<Result>;

public sealed class RevokeDeviceCommandHandler(
    IUnitOfWork unitOfWork, ICurrentUserContext currentUser, IAccessScope accessScope, IdentityDbContext db, IClock clock)
    : ICommandHandler<RevokeDeviceCommand, Result>
{
    public async ValueTask<Result> Handle(RevokeDeviceCommand command, CancellationToken ct)
    {
        var deviceUserId = await GetDeviceUserIdAsync(command.DeviceId, ct);
        if (deviceUserId is null)
        {
            return Result.Failure(Error.NotFound("Device"));
        }

        var grants = await accessScope.GetGrantsAsync(ct);
        var heldRoles = await db.GetHeldRolesAsync(currentUser.TenantId, deviceUserId.Value, ct);
        var defaultBranchId = await db.Users
            .Where(u => u.TenantId == currentUser.TenantId && u.Id == deviceUserId.Value)
            .Select(u => u.DefaultBranchId)
            .FirstOrDefaultAsync(ct);
        if (UserAdministrationPolicy.CanManageDevice(grants, heldRoles, defaultBranchId) is { } denied)
        {
            return Result.Failure(denied);
        }

        await using var sqlCommand = unitOfWork.Connection.CreateCommand();
        sqlCommand.Transaction = unitOfWork.Transaction;
        sqlCommand.CommandText = """
            UPDATE sync.Device
            SET IsRevoked = 1, RevokedAtUtc = @now, RevokedBy = @revokedBy, WipeRequested = @wipe
            WHERE TenantId = @tenantId AND Id = @id;
            """;
        sqlCommand.Parameters.Add(new SqlParameter("@now", clock.UtcNow));
        sqlCommand.Parameters.Add(new SqlParameter("@revokedBy", currentUser.UserId));
        sqlCommand.Parameters.Add(new SqlParameter("@wipe", command.RequestWipe));
        sqlCommand.Parameters.Add(new SqlParameter("@tenantId", currentUser.TenantId));
        sqlCommand.Parameters.Add(new SqlParameter("@id", command.DeviceId));

        var rows = await sqlCommand.ExecuteNonQueryAsync(ct);
        if (rows == 0)
        {
            return Result.Failure(Error.NotFound("Device"));
        }

        await using var revokeTokensCommand = unitOfWork.Connection.CreateCommand();
        revokeTokensCommand.Transaction = unitOfWork.Transaction;
        revokeTokensCommand.CommandText = """
            UPDATE [identity].RefreshToken
            SET RevokedAtUtc = @now, RevokedReason = 'DeviceRevoked'
            WHERE TenantId = @tenantId AND DeviceId = @deviceId AND RevokedAtUtc IS NULL;
            """;
        revokeTokensCommand.Parameters.Add(new SqlParameter("@now", clock.UtcNow));
        revokeTokensCommand.Parameters.Add(new SqlParameter("@tenantId", currentUser.TenantId));
        revokeTokensCommand.Parameters.Add(new SqlParameter("@deviceId", command.DeviceId));
        await revokeTokensCommand.ExecuteNonQueryAsync(ct);

        return Result.Success();
    }

    private async Task<Guid?> GetDeviceUserIdAsync(Guid deviceId, CancellationToken ct)
    {
        await using var command = unitOfWork.Connection.CreateCommand();
        command.Transaction = unitOfWork.Transaction;
        command.CommandText = "SELECT UserId FROM sync.Device WHERE TenantId = @tenantId AND Id = @id;";
        command.Parameters.Add(new SqlParameter("@tenantId", currentUser.TenantId));
        command.Parameters.Add(new SqlParameter("@id", deviceId));
        return await command.ExecuteScalarAsync(ct) is Guid userId ? userId : null;
    }
}
