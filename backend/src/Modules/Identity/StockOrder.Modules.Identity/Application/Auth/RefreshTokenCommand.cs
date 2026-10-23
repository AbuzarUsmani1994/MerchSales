using FluentValidation;
using Mediator;
using Microsoft.Data.SqlClient;
using StockOrder.Infrastructure.Auth;
using StockOrder.Infrastructure.Database;
using StockOrder.Modules.Identity.Infrastructure.Auth;
using StockOrder.SharedKernel.Errors;
using StockOrder.SharedKernel.Ids;
using StockOrder.SharedKernel.Results;
using StockOrder.SharedKernel.Time;

namespace StockOrder.Modules.Identity.Application.Auth;

public sealed record RefreshTokenCommand(string RefreshToken, string? IpAddress) : ICommand<Result<RefreshResult>>;

public sealed record RefreshResult(string AccessToken, int ExpiresInSeconds, string RefreshToken);

public sealed class RefreshTokenCommandValidator : AbstractValidator<RefreshTokenCommand>
{
    public RefreshTokenCommandValidator() => RuleFor(x => x.RefreshToken).NotEmpty();
}

/// <summary>
/// Rotation on every use; reuse of an already-rotated token revokes the whole family
/// (section 10). The tenant id is embedded in the token itself, so — like login — this
/// handler manages its own tenant-scoped connection rather than the per-request one.
/// </summary>
public sealed class RefreshTokenCommandHandler(
    ITenantConnectionFactory tenantConnectionFactory, IJwtTokenService tokenService, IClock clock, IIdGenerator idGenerator)
    : ICommandHandler<RefreshTokenCommand, Result<RefreshResult>>
{
    public async ValueTask<Result<RefreshResult>> Handle(RefreshTokenCommand command, CancellationToken ct)
    {
        var separatorIndex = command.RefreshToken.IndexOf('.');
        if (separatorIndex <= 0 || !Guid.TryParseExact(command.RefreshToken[..separatorIndex], "N", out var tenantId))
        {
            return Result.Failure<RefreshResult>(Error.Conflict(ErrorCodes.Unauthenticated, "Invalid refresh token."));
        }

        var hash = RefreshTokenFactory.Hash(command.RefreshToken);

        await using var connection = await tenantConnectionFactory.OpenAsync(tenantId, ct);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(ct);

        var existing = await FindTokenAsync(connection, transaction, hash, ct);
        if (existing is null)
        {
            await transaction.CommitAsync(ct);
            return Result.Failure<RefreshResult>(Error.Conflict(ErrorCodes.Unauthenticated, "Invalid refresh token."));
        }

        var now = clock.UtcNow;

        if (existing.RevokedAtUtc is not null)
        {
            await RevokeFamilyAsync(connection, transaction, tenantId, existing.FamilyId, now, ct);
            await transaction.CommitAsync(ct);
            return Result.Failure<RefreshResult>(Error.Conflict(ErrorCodes.RefreshTokenReused, "This refresh token was already used. All sessions for this login have been signed out."));
        }

        if (existing.ExpiresAtUtc <= now)
        {
            await transaction.CommitAsync(ct);
            return Result.Failure<RefreshResult>(Error.Conflict(ErrorCodes.Unauthenticated, "Refresh token expired."));
        }

        var user = await FindUserAsync(connection, transaction, tenantId, existing.UserId, ct);
        if (user is null || !user.IsActive)
        {
            await transaction.CommitAsync(ct);
            return Result.Failure<RefreshResult>(Error.Conflict(ErrorCodes.Unauthenticated, "Invalid refresh token."));
        }

        var newTokenId = idGenerator.NewId();
        var (plainToken, newHash) = RefreshTokenFactory.Create(tenantId);
        var lifetime = existing.DeviceId is null ? TimeSpan.FromHours(12) : TimeSpan.FromDays(30);

        await InsertRotatedTokenAsync(
            connection, transaction, tenantId, newTokenId, existing.UserId, existing.DeviceId,
            newHash, existing.FamilyId, now, now.Add(lifetime), command.IpAddress, ct);
        await RevokeTokenAsync(connection, transaction, existing.Id, now, "Rotated", newTokenId, ct);

        var accessToken = tokenService.IssueAccessToken(
            new AccessTokenRequest(existing.UserId, tenantId, user.TenantCode, user.SecurityStamp, existing.DeviceId));

        await transaction.CommitAsync(ct);

        return Result.Success(new RefreshResult(accessToken.Token, accessToken.ExpiresInSeconds, plainToken));
    }

    private static async Task<ExistingToken?> FindTokenAsync(
        SqlConnection connection, SqlTransaction transaction, string hash, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT Id, UserId, DeviceId, FamilyId, ExpiresAtUtc, RevokedAtUtc
            FROM [identity].RefreshToken
            WHERE TokenHash = @hash;
            """;
        command.Parameters.Add(new SqlParameter("@hash", hash));

        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            return null;
        }

        return new ExistingToken(
            reader.GetGuid(0), reader.GetGuid(1), reader.IsDBNull(2) ? null : reader.GetGuid(2),
            reader.GetGuid(3), reader.GetDateTime(4), reader.IsDBNull(5) ? null : reader.GetDateTime(5));
    }

    private static async Task<ExistingUser?> FindUserAsync(
        SqlConnection connection, SqlTransaction transaction, Guid tenantId, Guid userId, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT u.SecurityStamp, u.IsActive, t.Code
            FROM [identity].[User] u
            JOIN tenancy.Tenant t ON t.Id = u.TenantId
            WHERE u.TenantId = @tenantId AND u.Id = @id;
            """;
        command.Parameters.Add(new SqlParameter("@tenantId", tenantId));
        command.Parameters.Add(new SqlParameter("@id", userId));

        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            return null;
        }

        return new ExistingUser(reader.GetGuid(0), reader.GetBoolean(1), reader.GetString(2));
    }

    private static async Task RevokeFamilyAsync(
        SqlConnection connection, SqlTransaction transaction, Guid tenantId, Guid familyId, DateTime now, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE [identity].RefreshToken
            SET RevokedAtUtc = @now, RevokedReason = 'ReuseDetected'
            WHERE TenantId = @tenantId AND FamilyId = @familyId AND RevokedAtUtc IS NULL;
            """;
        command.Parameters.Add(new SqlParameter("@now", now));
        command.Parameters.Add(new SqlParameter("@tenantId", tenantId));
        command.Parameters.Add(new SqlParameter("@familyId", familyId));

        await command.ExecuteNonQueryAsync(ct);
    }

    private static async Task RevokeTokenAsync(
        SqlConnection connection, SqlTransaction transaction, Guid id, DateTime now, string reason, Guid replacedBy, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE [identity].RefreshToken
            SET RevokedAtUtc = @now, RevokedReason = @reason, ReplacedByTokenId = @replacedBy
            WHERE Id = @id;
            """;
        command.Parameters.Add(new SqlParameter("@now", now));
        command.Parameters.Add(new SqlParameter("@reason", reason));
        command.Parameters.Add(new SqlParameter("@replacedBy", replacedBy));
        command.Parameters.Add(new SqlParameter("@id", id));

        await command.ExecuteNonQueryAsync(ct);
    }

    private static async Task InsertRotatedTokenAsync(
        SqlConnection connection, SqlTransaction transaction, Guid tenantId, Guid id, Guid userId, Guid? deviceId,
        string hash, Guid familyId, DateTime createdAtUtc, DateTime expiresAtUtc, string? ip, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT [identity].RefreshToken (TenantId, Id, UserId, DeviceId, TokenHash, FamilyId, CreatedAtUtc, ExpiresAtUtc, CreatedByIp)
            VALUES (@tenantId, @id, @userId, @deviceId, @hash, @familyId, @createdAtUtc, @expiresAtUtc, @ip);
            """;
        command.Parameters.Add(new SqlParameter("@tenantId", tenantId));
        command.Parameters.Add(new SqlParameter("@id", id));
        command.Parameters.Add(new SqlParameter("@userId", userId));
        command.Parameters.Add(new SqlParameter("@deviceId", (object?)deviceId ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@hash", hash));
        command.Parameters.Add(new SqlParameter("@familyId", familyId));
        command.Parameters.Add(new SqlParameter("@createdAtUtc", createdAtUtc));
        command.Parameters.Add(new SqlParameter("@expiresAtUtc", expiresAtUtc));
        command.Parameters.Add(new SqlParameter("@ip", (object?)ip ?? DBNull.Value));

        await command.ExecuteNonQueryAsync(ct);
    }

    private sealed record ExistingToken(Guid Id, Guid UserId, Guid? DeviceId, Guid FamilyId, DateTime ExpiresAtUtc, DateTime? RevokedAtUtc);

    private sealed record ExistingUser(Guid SecurityStamp, bool IsActive, string TenantCode);
}
