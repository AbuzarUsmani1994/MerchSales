namespace StockOrder.Modules.Identity.Domain;

/// <summary>
/// Maps identity.RefreshToken (002_Identity.sql). Only the SHA-256 hash is stored; the
/// token value itself is never persisted (section 10). No RowVer column in the schema.
/// </summary>
public sealed class RefreshToken
{
    public Guid TenantId { get; private set; }
    public Guid Id { get; private set; }
    public long ClusterKey { get; private set; }
    public Guid UserId { get; private set; }
    public Guid? DeviceId { get; private set; }
    public string TokenHash { get; private set; } = string.Empty;
    public Guid FamilyId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime ExpiresAtUtc { get; private set; }
    public DateTime? RevokedAtUtc { get; private set; }
    public string? RevokedReason { get; private set; }
    public Guid? ReplacedByTokenId { get; private set; }
    public string? CreatedByIp { get; private set; }

    private RefreshToken()
    {
    }

    public bool IsActive(DateTime nowUtc) => RevokedAtUtc is null && ExpiresAtUtc > nowUtc;

    public static RefreshToken IssueNew(
        Guid tenantId, Guid id, Guid userId, Guid? deviceId, string tokenHash, Guid familyId,
        DateTime nowUtc, DateTime expiresAtUtc, string? createdByIp) =>
        new()
        {
            TenantId = tenantId,
            Id = id,
            UserId = userId,
            DeviceId = deviceId,
            TokenHash = tokenHash,
            FamilyId = familyId,
            CreatedAtUtc = nowUtc,
            ExpiresAtUtc = expiresAtUtc,
            CreatedByIp = createdByIp,
        };

    public void Revoke(DateTime nowUtc, string reason, Guid? replacedByTokenId = null)
    {
        RevokedAtUtc = nowUtc;
        RevokedReason = reason;
        ReplacedByTokenId = replacedByTokenId;
    }
}
