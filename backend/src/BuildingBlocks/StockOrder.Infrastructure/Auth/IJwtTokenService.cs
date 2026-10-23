namespace StockOrder.Infrastructure.Auth;

/// <summary>
/// Issues the 15-minute RS256 access token (section 10). Permissions are deliberately not
/// included — they're loaded per request from <see cref="Caching.IPermissionCache"/>.
/// </summary>
public interface IJwtTokenService
{
    IssuedAccessToken IssueAccessToken(AccessTokenRequest request);
}

public sealed record AccessTokenRequest(Guid UserId, Guid TenantId, string TenantCode, Guid SecurityStamp, Guid? DeviceId);

public sealed record IssuedAccessToken(string Token, int ExpiresInSeconds);
