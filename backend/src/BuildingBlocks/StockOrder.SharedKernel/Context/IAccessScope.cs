namespace StockOrder.SharedKernel.Context;

/// <summary>
/// The current user's permissions together with the branch each one was granted for
/// (section 6: "Queries for branch data filter to the user's allowed branches in the
/// application layer"). Endpoint policies only prove the user holds a permission
/// somewhere; handlers of <c>[BranchScoped]</c> endpoints use this to check where.
/// </summary>
public interface IAccessScope
{
    ValueTask<IReadOnlyList<PermissionGrant>> GetGrantsAsync(CancellationToken ct);
}

public static class AccessScopeExtensions
{
    public static async ValueTask<BranchScope> GetBranchScopeAsync(
        this IAccessScope accessScope, string permissionCode, CancellationToken ct) =>
        BranchScope.For(await accessScope.GetGrantsAsync(ct), permissionCode);

    /// <summary>Every branch the user holds any role at — the branches they may see at all.</summary>
    public static async ValueTask<BranchScope> GetAllowedBranchesAsync(this IAccessScope accessScope, CancellationToken ct) =>
        BranchScope.For(await accessScope.GetGrantsAsync(ct), null);
}
