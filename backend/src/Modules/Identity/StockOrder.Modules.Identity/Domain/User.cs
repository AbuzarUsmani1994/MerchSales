using StockOrder.SharedKernel.Entities;

namespace StockOrder.Modules.Identity.Domain;

/// <summary>Maps identity.User (002_Identity.sql).</summary>
public sealed class User : AuditedTenantEntity
{
    public const int MaxFailedLogins = 5;
    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    public string Username { get; private set; } = string.Empty;
    public string DisplayName { get; private set; } = string.Empty;
    public string? Email { get; private set; }
    public string? Phone { get; private set; }
    public string PasswordHash { get; private set; } = string.Empty;
    public Guid SecurityStamp { get; private set; }
    public bool MustChangePassword { get; private set; }
    public bool IsActive { get; private set; } = true;
    public int FailedLoginCount { get; private set; }
    public DateTime? LockoutEndUtc { get; private set; }
    public bool MfaEnabled { get; private set; }
    public string? MfaSecretProtected { get; private set; }
    public string? PinHash { get; private set; }
    public DateTime? LastLoginAtUtc { get; private set; }
    public Guid? DefaultBranchId { get; private set; }

    private User()
    {
    }

    private User(
        Guid tenantId, Guid id, string username, string displayName, string? email, string? phone,
        string passwordHash, Guid? defaultBranchId, DateTime nowUtc, Guid? createdBy)
        : base(tenantId, id, nowUtc, createdBy)
    {
        Username = username;
        DisplayName = displayName;
        Email = email;
        Phone = phone;
        PasswordHash = passwordHash;
        DefaultBranchId = defaultBranchId;
        SecurityStamp = Guid.NewGuid();
        MustChangePassword = true;
    }

    public static User Create(
        Guid tenantId, Guid id, string username, string displayName, string? email, string? phone,
        string passwordHash, Guid? defaultBranchId, DateTime nowUtc, Guid? createdBy) =>
        new(tenantId, id, username, displayName, email, phone, passwordHash, defaultBranchId, nowUtc, createdBy);

    /// <summary>
    /// Reconstructs a User from a raw ADO.NET row read outside EF Core (used only by the
    /// login flow, which has no tenant context yet to go through the normal DbContext — see
    /// <c>LoginCommandHandler</c>). Not a general-purpose constructor.
    /// </summary>
    public static User Rehydrate(
        Guid tenantId, Guid id, string username, string displayName, string? email, string? phone,
        string passwordHash, Guid securityStamp, bool mustChangePassword, bool isActive,
        int failedLoginCount, DateTime? lockoutEndUtc, bool mfaEnabled, string? mfaSecretProtected)
    {
        var user = new User
        {
            TenantId = tenantId,
            Id = id,
            Username = username,
            DisplayName = displayName,
            Email = email,
            Phone = phone,
            PasswordHash = passwordHash,
            SecurityStamp = securityStamp,
            MustChangePassword = mustChangePassword,
            IsActive = isActive,
            FailedLoginCount = failedLoginCount,
            LockoutEndUtc = lockoutEndUtc,
            MfaEnabled = mfaEnabled,
            MfaSecretProtected = mfaSecretProtected,
        };
        return user;
    }

    public bool IsLockedOut(DateTime nowUtc) => LockoutEndUtc is { } end && end > nowUtc;

    public void RecordFailedLogin(DateTime nowUtc)
    {
        FailedLoginCount++;
        if (FailedLoginCount >= MaxFailedLogins)
        {
            LockoutEndUtc = nowUtc.Add(LockoutDuration);
        }
    }

    public void RecordSuccessfulLogin(DateTime nowUtc)
    {
        FailedLoginCount = 0;
        LockoutEndUtc = null;
        LastLoginAtUtc = nowUtc;
    }

    public void ChangePassword(string newPasswordHash, DateTime nowUtc, Guid? updatedBy)
    {
        PasswordHash = newPasswordHash;
        MustChangePassword = false;
        SecurityStamp = Guid.NewGuid();
        MarkUpdated(nowUtc, updatedBy);
    }

    public void ResetPassword(string newPasswordHash, DateTime nowUtc, Guid? updatedBy)
    {
        PasswordHash = newPasswordHash;
        MustChangePassword = true;
        SecurityStamp = Guid.NewGuid();
        MarkUpdated(nowUtc, updatedBy);
    }

    /// <summary>Stores the pending secret; MFA is not required at login until <see cref="ConfirmMfaEnrollment"/>.</summary>
    public void BeginMfaEnrollment(string protectedSecret, DateTime nowUtc, Guid? updatedBy)
    {
        MfaSecretProtected = protectedSecret;
        MarkUpdated(nowUtc, updatedBy);
    }

    public void ConfirmMfaEnrollment(DateTime nowUtc, Guid? updatedBy)
    {
        MfaEnabled = true;
        MarkUpdated(nowUtc, updatedBy);
    }

    public void SetPin(string pinHash, DateTime nowUtc, Guid? updatedBy)
    {
        PinHash = pinHash;
        MarkUpdated(nowUtc, updatedBy);
    }

    public void RotateSecurityStamp() => SecurityStamp = Guid.NewGuid();

    public void Deactivate(DateTime nowUtc, Guid? updatedBy)
    {
        IsActive = false;
        RotateSecurityStamp();
        MarkUpdated(nowUtc, updatedBy);
    }
}
