using Dapper;
using Microsoft.Extensions.Caching.Hybrid;

namespace StockOrder.Infrastructure.Database;

/// <summary>
/// A restaurant branch's customer menu link (docs/decisions.md, "Public customer menu"): the code in the QR a waiter
/// shows, stored as tenant setting <c>PublicMenu.Code.{branchId}</c>. An empty value means the
/// link is switched off; a new code replaces the old one, so the old QR stops working.
/// </summary>
public static class PublicMenuSettings
{
    public const string KeyPrefix = "PublicMenu.Code.";

    public static string KeyFor(Guid branchId) => KeyPrefix + branchId.ToString("D");
}

/// <summary>
/// A restaurant branch's customer order screen link (docs/decisions.md, "Tables and kitchen
/// screen"): the hall TV's "Now preparing / Ready" page, stored as tenant setting
/// <c>OrderScreen.Code.{branchId}</c>, the same way as the menu link.
/// </summary>
public static class OrderScreenSettings
{
    public const string KeyPrefix = "OrderScreen.Code.";

    public static string KeyFor(Guid branchId) => KeyPrefix + branchId.ToString("D");
}

public sealed record PublicMenuLink(
    Guid TenantId,
    string TenantName,
    string TenantStatus,
    string Industry,
    string TimeZoneId,
    Guid BranchId,
    string BranchName,
    string? BranchAddress,
    string? BranchPhone,
    bool BranchIsActive);

/// <summary>
/// Finds which company and branch a customer menu code belongs to. The page is opened without a
/// login, so no token names the tenant: this is the one lookup that may read across tenants for
/// it (like the login's tenant-code lookup, section 6), and it returns only the row the code
/// matches. Cached for a minute so a busy menu doesn't open a bypass connection per view.
/// </summary>
public interface IPublicMenuLinkLookup
{
    /// <param name="keyPrefix">Which kind of link: <see cref="PublicMenuSettings.KeyPrefix"/> (default) or <see cref="OrderScreenSettings.KeyPrefix"/>.</param>
    Task<PublicMenuLink?> FindAsync(string code, CancellationToken ct, string keyPrefix = PublicMenuSettings.KeyPrefix);

    /// <summary>Called when a link is switched off or replaced, so the old code stops working at once.</summary>
    Task ForgetAsync(string code, CancellationToken ct, string keyPrefix = PublicMenuSettings.KeyPrefix);
}

internal sealed class PublicMenuLinkLookup(IBypassConnectionFactory bypassFactory, HybridCache cache) : IPublicMenuLinkLookup
{
    private static readonly HybridCacheEntryOptions CacheOptions = new() { Expiration = TimeSpan.FromMinutes(1), LocalCacheExpiration = TimeSpan.FromMinutes(1) };

    public async Task<PublicMenuLink?> FindAsync(string code, CancellationToken ct, string keyPrefix = PublicMenuSettings.KeyPrefix)
    {
        if (!PublicMenuCode.IsWellFormed(code) || !IsKnownPrefix(keyPrefix))
        {
            return null;
        }

        var found = await cache.GetOrCreateAsync(
            CacheKey(code, keyPrefix),
            async token => new CachedLink(await QueryAsync(code, keyPrefix, token)),
            CacheOptions,
            cancellationToken: ct);
        return found.Link;
    }

    public Task ForgetAsync(string code, CancellationToken ct, string keyPrefix = PublicMenuSettings.KeyPrefix) =>
        PublicMenuCode.IsWellFormed(code) ? cache.RemoveAsync(CacheKey(code, keyPrefix), ct).AsTask() : Task.CompletedTask;

    private static bool IsKnownPrefix(string keyPrefix) =>
        keyPrefix is PublicMenuSettings.KeyPrefix or OrderScreenSettings.KeyPrefix;

    private async Task<PublicMenuLink?> QueryAsync(string code, string keyPrefix, CancellationToken ct)
    {
        await using var connection = await bypassFactory.OpenAsync(ct);
        var rows = (await connection.QueryAsync<LinkRow>(new CommandDefinition(
            """
            SELECT TOP (2) t.Id AS TenantId, t.Name AS TenantName, t.Status AS TenantStatus, t.Industry, t.TimeZoneId,
                   s.SettingKey, b.Id AS BranchId, b.Name AS BranchName, b.Address AS BranchAddress, b.Phone AS BranchPhone,
                   b.IsActive AS BranchIsActive
            FROM tenancy.TenantSetting s
            JOIN tenancy.Tenant t ON t.Id = s.TenantId
            JOIN tenancy.Branch b ON b.TenantId = s.TenantId
                 AND b.Id = TRY_CONVERT(uniqueidentifier, SUBSTRING(s.SettingKey, @prefixLength + 1, 36))
            WHERE s.SettingKey LIKE @keyPattern AND s.Value = @code;
            """,
            new { code, prefixLength = keyPrefix.Length, keyPattern = keyPrefix + "%" },
            cancellationToken: ct))).ToList();

        // Codes are random; two matches would mean a collision, and showing either menu could be wrong.
        if (rows.Count != 1)
        {
            return null;
        }

        var r = rows[0];
        return new PublicMenuLink(
            r.TenantId, r.TenantName, r.TenantStatus, r.Industry, r.TimeZoneId,
            r.BranchId, r.BranchName, r.BranchAddress, r.BranchPhone, r.BranchIsActive);
    }

    private static string CacheKey(string code, string keyPrefix) => $"public-link:{keyPrefix}{code}";

    private sealed record CachedLink(PublicMenuLink? Link);

    private sealed record LinkRow(
        Guid TenantId, string TenantName, string TenantStatus, string Industry, string TimeZoneId, string SettingKey,
        Guid BranchId, string BranchName, string? BranchAddress, string? BranchPhone, bool BranchIsActive);
}

/// <summary>Customer menu codes: 16 characters from a-z and 2-9 (about 80 bits), short enough for a QR that scans easily.</summary>
public static class PublicMenuCode
{
    private const string Alphabet = "abcdefghijkmnpqrstuvwxyz23456789";
    public const int Length = 16;

    public static string New() => System.Security.Cryptography.RandomNumberGenerator.GetString(Alphabet, Length);

    public static bool IsWellFormed(string? code) => code is { Length: Length } && code.All(c => Alphabet.Contains(c));
}
