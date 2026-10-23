using System.Security.Cryptography;

namespace StockOrder.Modules.Identity.Infrastructure.Auth;

/// <summary>
/// Refresh token format <c>{tenantId:N}.{32 random bytes base64url}</c> (section 10). Only
/// the SHA-256 hash is ever persisted.
/// </summary>
public static class RefreshTokenFactory
{
    public static (string PlainToken, string Hash) Create(Guid tenantId)
    {
        var randomBytes = RandomNumberGenerator.GetBytes(32);
        var plainToken = $"{tenantId:N}.{Convert.ToBase64String(randomBytes).TrimEnd('=').Replace('+', '-').Replace('/', '_')}";
        return (plainToken, Hash(plainToken));
    }

    public static string Hash(string plainToken) =>
        Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(plainToken)));
}
