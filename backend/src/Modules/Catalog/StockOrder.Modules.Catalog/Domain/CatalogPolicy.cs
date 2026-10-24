using StockOrder.SharedKernel.Context;
using StockOrder.SharedKernel.Errors;

namespace StockOrder.Modules.Catalog.Domain;

/// <summary>
/// Who may change which part of the menu (agreed with the owner; see 014_BranchCatalog.sql):
/// <list type="bullet">
/// <item>Shared items (no owner branch), their base price and categories: an all-branches
/// <c>Catalog.Manage</c> role — the Owner.</item>
/// <item>Items sold only at one branch: <c>Catalog.Manage</c> at that branch — its manager.</item>
/// <item>A branch's own price and availability for any item it sells: <c>Pricing.Manage</c> at
/// that branch.</item>
/// </list>
/// Returns null when allowed, otherwise the error to return.
/// </summary>
public static class CatalogPolicy
{
    public const string View = "Catalog.View";
    public const string Manage = "Catalog.Manage";
    public const string Pricing = "Pricing.Manage";

    public static Error? CanManageItem(IReadOnlyCollection<PermissionGrant> grants, Guid? ownerBranchId) =>
        BranchScope.For(grants, Manage).Covers(ownerBranchId)
            ? null
            : Error.PermissionDenied(ownerBranchId is null
                ? "Only an administrator for all branches can change the shared menu."
                : "You can only manage items of branches you manage.");

    public static Error? CanSetBranchPricing(IReadOnlyCollection<PermissionGrant> grants, Guid? ownerBranchId, Guid branchId)
    {
        if (!SoldAt(ownerBranchId, branchId))
        {
            return Error.Validation("This item is not sold at that branch.");
        }

        return BranchScope.For(grants, Pricing).Covers(branchId)
            ? null
            : Error.PermissionDenied("You can only set prices and availability at branches you manage.");
    }

    /// <summary>An item a user may see: shared items, plus branch-only items of branches they can view.</summary>
    public static bool CanSee(BranchScope viewScope, Guid? ownerBranchId) => ownerBranchId is null || viewScope.Covers(ownerBranchId);

    /// <summary>Shared items are sold everywhere; a branch-only item only at its branch.</summary>
    public static bool SoldAt(Guid? ownerBranchId, Guid branchId) => ownerBranchId is null || ownerBranchId == branchId;

    /// <summary>Menu prices are whole rupees or paisa: at most 2 decimals, never negative.</summary>
    public static Error? ValidatePrice(decimal price) =>
        price < 0 || price > 999_999_999m || decimal.Round(price, 2) != price
            ? Error.Validation("Price must be 0 or more, with at most 2 decimals.")
            : null;
}
