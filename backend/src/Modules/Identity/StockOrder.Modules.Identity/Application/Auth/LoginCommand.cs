using FluentValidation;
using Mediator;
using Microsoft.Data.SqlClient;
using Microsoft.AspNetCore.Identity;
using StockOrder.Infrastructure.Auth;
using StockOrder.Infrastructure.Database;
using StockOrder.Modules.Identity.Domain;
using StockOrder.Modules.Identity.Infrastructure.Auth;
using StockOrder.Modules.Tenancy.Contracts;
using StockOrder.SharedKernel.Errors;
using StockOrder.SharedKernel.Ids;
using StockOrder.SharedKernel.Results;
using StockOrder.SharedKernel.Time;

namespace StockOrder.Modules.Identity.Application.Auth;

public sealed record LoginDevice(Guid Id, string Platform, string? Name, string? AppVersion, string? OsVersion, string? PushToken);

public sealed record LoginCommand(
    string TenantCode, string Username, string Password, string? TotpCode, LoginDevice? Device, string? IpAddress)
    : ICommand<Result<LoginResult>>;

public sealed record LoginResult(
    string AccessToken,
    int ExpiresInSeconds,
    string RefreshToken,
    bool MustChangePassword,
    bool MfaRequired,
    Guid UserId,
    string Username,
    string DisplayName,
    Guid TenantId,
    string TenantCode,
    string TenantName);

public sealed class LoginCommandValidator : AbstractValidator<LoginCommand>
{
    public LoginCommandValidator()
    {
        RuleFor(x => x.TenantCode).NotEmpty();
        RuleFor(x => x.Username).NotEmpty();
        RuleFor(x => x.Password).NotEmpty();
    }
}

/// <summary>
/// There is no tenant context yet when login starts (section 6), so this handler manages
/// its own tenant-scoped connection and transaction directly with raw SQL rather than going
/// through the shared per-request <see cref="IUnitOfWork"/>/EF Core pipeline every other
/// handler uses — that pipeline only exists once a validated token has set the tenant.
/// </summary>
public sealed class LoginCommandHandler(
    ITenantLookupService tenantLookup,
    ITenantConnectionFactory tenantConnectionFactory,
    IJwtTokenService tokenService,
    IMfaSecretProtector mfaSecretProtector,
    IClock clock,
    IIdGenerator idGenerator)
    : ICommandHandler<LoginCommand, Result<LoginResult>>
{
    private const string MobileUsePermission = "Mobile.Use";
    private static readonly PasswordHasher<User> PasswordHasher = new();

    public async ValueTask<Result<LoginResult>> Handle(LoginCommand command, CancellationToken ct)
    {
        var tenant = await tenantLookup.FindByCodeAsync(command.TenantCode, ct);
        if (tenant is null)
        {
            return Result.Failure<LoginResult>(Error.Conflict(ErrorCodes.InvalidCredentials, "Invalid tenant code, username or password."));
        }

        if (tenant.Status != "Active")
        {
            return Result.Failure<LoginResult>(Error.Conflict(ErrorCodes.TenantSuspended, "This tenant is not active."));
        }

        await using var connection = await tenantConnectionFactory.OpenAsync(tenant.TenantId, ct);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(ct);

        var user = await FindUserAsync(connection, transaction, tenant.TenantId, command.Username, ct);
        if (user is null)
        {
            await transaction.CommitAsync(ct);
            return Result.Failure<LoginResult>(Error.Conflict(ErrorCodes.InvalidCredentials, "Invalid tenant code, username or password."));
        }

        var now = clock.UtcNow;
        if (user.IsLockedOut(now))
        {
            await transaction.CommitAsync(ct);
            return Result.Failure<LoginResult>(Error.Conflict(ErrorCodes.LockedOut, "This account is temporarily locked. Try again later."));
        }

        var verification = PasswordHasher.VerifyHashedPassword(user, user.PasswordHash, command.Password);
        if (verification == PasswordVerificationResult.Failed || !user.IsActive)
        {
            user.RecordFailedLogin(now);
            await SaveLoginOutcomeAsync(connection, transaction, user, ct);
            await transaction.CommitAsync(ct);
            return Result.Failure<LoginResult>(Error.Conflict(ErrorCodes.InvalidCredentials, "Invalid tenant code, username or password."));
        }

        if (user.MfaEnabled && string.IsNullOrWhiteSpace(command.TotpCode))
        {
            await transaction.CommitAsync(ct);
            return Result.Failure<LoginResult>(Error.Conflict(ErrorCodes.MfaRequired, "A TOTP code is required for this account."));
        }

        if (user.MfaEnabled && !TotpVerifier.Verify(mfaSecretProtector.Unprotect(user.MfaSecretProtected!), command.TotpCode!))
        {
            user.RecordFailedLogin(now);
            await SaveLoginOutcomeAsync(connection, transaction, user, ct);
            await transaction.CommitAsync(ct);
            return Result.Failure<LoginResult>(Error.Conflict(ErrorCodes.InvalidCredentials, "Invalid TOTP code."));
        }

        // Section 10: "Requires Mobile.Use for mobile logins" — a login that registers a device is one.
        if (command.Device is not null && !await HasPermissionAsync(connection, transaction, tenant.TenantId, user.Id, MobileUsePermission, ct))
        {
            await transaction.CommitAsync(ct);
            return Result.Failure<LoginResult>(Error.PermissionDenied("This account isn't allowed to use the mobile app."));
        }

        user.RecordSuccessfulLogin(now);
        await SaveLoginOutcomeAsync(connection, transaction, user, ct);

        Guid? deviceId = null;
        if (command.Device is { } device)
        {
            deviceId = await UpsertDeviceAsync(connection, transaction, tenant.TenantId, user.Id, device, now, ct);
        }

        var accessToken = tokenService.IssueAccessToken(new AccessTokenRequest(
            user.Id, tenant.TenantId, tenant.Code, user.SecurityStamp, deviceId));

        var refreshToken = await IssueRefreshTokenAsync(
            connection, transaction, tenant.TenantId, user.Id, deviceId, now, command.IpAddress, ct);

        await transaction.CommitAsync(ct);

        return Result.Success(new LoginResult(
            accessToken.Token,
            accessToken.ExpiresInSeconds,
            refreshToken,
            user.MustChangePassword,
            MfaRequired: false,
            user.Id,
            user.Username,
            user.DisplayName,
            tenant.TenantId,
            tenant.Code,
            tenant.Name));
    }

    private static async Task<User?> FindUserAsync(
        SqlConnection connection, SqlTransaction transaction, Guid tenantId, string username, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT TenantId, Id, Username, DisplayName, Email, Phone, PasswordHash, SecurityStamp,
                   MustChangePassword, IsActive, FailedLoginCount, LockoutEndUtc, MfaEnabled, MfaSecretProtected
            FROM [identity].[User]
            WHERE TenantId = @tenantId AND Username = @username;
            """;
        command.Parameters.Add(new SqlParameter("@tenantId", tenantId));
        command.Parameters.Add(new SqlParameter("@username", username));

        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            return null;
        }

        return User.Rehydrate(
            reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetString(4), reader.IsDBNull(5) ? null : reader.GetString(5),
            reader.GetString(6), reader.GetGuid(7), reader.GetBoolean(8), reader.GetBoolean(9),
            reader.GetInt32(10), reader.IsDBNull(11) ? null : reader.GetDateTime(11),
            reader.GetBoolean(12), reader.IsDBNull(13) ? null : reader.GetString(13));
    }

    private static async Task SaveLoginOutcomeAsync(SqlConnection connection, SqlTransaction transaction, User user, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE [identity].[User]
            SET FailedLoginCount = @failedLoginCount, LockoutEndUtc = @lockoutEndUtc, LastLoginAtUtc = @lastLoginAtUtc
            WHERE TenantId = @tenantId AND Id = @id;
            """;
        command.Parameters.Add(new SqlParameter("@failedLoginCount", user.FailedLoginCount));
        command.Parameters.Add(new SqlParameter("@lockoutEndUtc", (object?)user.LockoutEndUtc ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@lastLoginAtUtc", (object?)user.LastLoginAtUtc ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@tenantId", user.TenantId));
        command.Parameters.Add(new SqlParameter("@id", user.Id));

        await command.ExecuteNonQueryAsync(ct);
    }

    private static async Task<bool> HasPermissionAsync(
        SqlConnection connection, SqlTransaction transaction, Guid tenantId, Guid userId, string permissionCode, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT CASE WHEN EXISTS (
                SELECT 1
                FROM [identity].UserRole ur
                JOIN [identity].RolePermission rp ON rp.TenantId = ur.TenantId AND rp.RoleId = ur.RoleId
                WHERE ur.TenantId = @tenantId AND ur.UserId = @userId AND rp.PermissionCode = @code)
            THEN 1 ELSE 0 END;
            """;
        command.Parameters.Add(new SqlParameter("@tenantId", tenantId));
        command.Parameters.Add(new SqlParameter("@userId", userId));
        command.Parameters.Add(new SqlParameter("@code", permissionCode));
        return (int)(await command.ExecuteScalarAsync(ct) ?? 0) == 1;
    }

    private static async Task<Guid> UpsertDeviceAsync(
        SqlConnection connection, SqlTransaction transaction, Guid tenantId, Guid userId, LoginDevice device, DateTime now, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            MERGE sync.Device AS target
            USING (SELECT @tenantId AS TenantId, @id AS Id) AS src
            ON target.TenantId = src.TenantId AND target.Id = src.Id
            WHEN MATCHED THEN UPDATE SET
                UserId = @userId, Platform = @platform, DeviceName = @deviceName,
                AppVersion = @appVersion, OsVersion = @osVersion, PushToken = @pushToken, LastSeenAtUtc = @now
            WHEN NOT MATCHED THEN INSERT (TenantId, Id, UserId, Platform, DeviceName, AppVersion, OsVersion, PushToken, LastSeenAtUtc)
                VALUES (@tenantId, @id, @userId, @platform, @deviceName, @appVersion, @osVersion, @pushToken, @now);
            """;
        command.Parameters.Add(new SqlParameter("@tenantId", tenantId));
        command.Parameters.Add(new SqlParameter("@id", device.Id));
        command.Parameters.Add(new SqlParameter("@userId", userId));
        command.Parameters.Add(new SqlParameter("@platform", device.Platform));
        command.Parameters.Add(new SqlParameter("@deviceName", (object?)device.Name ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@appVersion", (object?)device.AppVersion ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@osVersion", (object?)device.OsVersion ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@pushToken", (object?)device.PushToken ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@now", now));

        await command.ExecuteNonQueryAsync(ct);
        return device.Id;
    }

    private async Task<string> IssueRefreshTokenAsync(
        SqlConnection connection, SqlTransaction transaction, Guid tenantId, Guid userId, Guid? deviceId,
        DateTime now, string? ip, CancellationToken ct)
    {
        var tokenId = idGenerator.NewId();
        var familyId = idGenerator.NewId();
        var (plainToken, hash) = RefreshTokenFactory.Create(tenantId);
        var lifetime = deviceId is null ? TimeSpan.FromHours(12) : TimeSpan.FromDays(30);

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT [identity].RefreshToken (TenantId, Id, UserId, DeviceId, TokenHash, FamilyId, CreatedAtUtc, ExpiresAtUtc, CreatedByIp)
            VALUES (@tenantId, @id, @userId, @deviceId, @tokenHash, @familyId, @createdAtUtc, @expiresAtUtc, @ip);
            """;
        command.Parameters.Add(new SqlParameter("@tenantId", tenantId));
        command.Parameters.Add(new SqlParameter("@id", tokenId));
        command.Parameters.Add(new SqlParameter("@userId", userId));
        command.Parameters.Add(new SqlParameter("@deviceId", (object?)deviceId ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@tokenHash", hash));
        command.Parameters.Add(new SqlParameter("@familyId", familyId));
        command.Parameters.Add(new SqlParameter("@createdAtUtc", now));
        command.Parameters.Add(new SqlParameter("@expiresAtUtc", now.Add(lifetime)));
        command.Parameters.Add(new SqlParameter("@ip", (object?)ip ?? DBNull.Value));

        await command.ExecuteNonQueryAsync(ct);
        return plainToken;
    }
}
