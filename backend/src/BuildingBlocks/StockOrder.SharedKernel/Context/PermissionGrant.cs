namespace StockOrder.SharedKernel.Context;

/// <summary>
/// One permission a user holds through one of their roles, at the branch that role was
/// assigned for (<c>identity.UserRole.BranchId</c>; NULL = all branches, section 6).
/// </summary>
public sealed record PermissionGrant(string Code, Guid? BranchId);

/// <summary>Which branches a set of grants reaches: either every branch, or a fixed list.</summary>
public sealed record BranchScope(bool AllBranches, IReadOnlySet<Guid> BranchIds)
{
    public static readonly BranchScope None = new(false, new HashSet<Guid>());

    public bool IsEmpty => !AllBranches && BranchIds.Count == 0;

    /// <summary>A NULL branch means "all branches", which only an all-branches scope covers.</summary>
    public bool Covers(Guid? branchId) => AllBranches || (branchId is { } id && BranchIds.Contains(id));

    /// <summary>The scope of one permission code, or of any permission when <paramref name="permissionCode"/> is null.</summary>
    public static BranchScope For(IEnumerable<PermissionGrant> grants, string? permissionCode)
    {
        var matching = grants.Where(g => permissionCode is null || g.Code == permissionCode).ToList();
        if (matching.Any(g => g.BranchId is null))
        {
            return new BranchScope(true, new HashSet<Guid>());
        }

        return new BranchScope(false, matching.Select(g => g.BranchId!.Value).ToHashSet());
    }
}
