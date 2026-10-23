namespace StockOrder.Modules.Identity.Domain;

/// <summary>Maps identity.Permission (002_Identity.sql): global, seeded by script 011, read-only to the app.</summary>
public sealed class Permission
{
    public string Code { get; private set; } = string.Empty;
    public string Module { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public bool IsPlatformOnly { get; private set; }

    private Permission()
    {
    }
}

/// <summary>Maps identity.RolePermission. Composite key join, no surrogate Id or RowVer.</summary>
public sealed class RolePermission
{
    public Guid TenantId { get; private set; }
    public Guid RoleId { get; private set; }
    public string PermissionCode { get; private set; } = string.Empty;

    private RolePermission()
    {
    }

    public static RolePermission Create(Guid tenantId, Guid roleId, string permissionCode) =>
        new() { TenantId = tenantId, RoleId = roleId, PermissionCode = permissionCode };
}

/// <summary>Maps identity.UserRole. BranchId NULL = all branches (section 6). No RowVer column in the schema.</summary>
public sealed class UserRole
{
    public Guid TenantId { get; private set; }
    public Guid Id { get; private set; }
    public long ClusterKey { get; private set; }
    public Guid UserId { get; private set; }
    public Guid RoleId { get; private set; }
    public Guid? BranchId { get; private set; }

    private UserRole()
    {
    }

    public static UserRole Create(Guid tenantId, Guid id, Guid userId, Guid roleId, Guid? branchId) =>
        new() { TenantId = tenantId, Id = id, UserId = userId, RoleId = roleId, BranchId = branchId };
}
