using Shouldly;
using StockOrder.Modules.Catalog.Domain;
using StockOrder.SharedKernel.Context;
using StockOrder.SharedKernel.Errors;
using Xunit;

namespace StockOrder.UnitTests;

/// <summary>Menu ownership agreed with the owner: shared menu = Owner; branch prices, availability and branch-only items = that branch's manager.</summary>
public sealed class CatalogPolicyTests
{
    private static readonly Guid LakeCity = Guid.NewGuid();
    private static readonly Guid Eden = Guid.NewGuid();

    private static readonly PermissionGrant[] Owner =
        [new("Catalog.View", null), new("Catalog.Manage", null), new("Pricing.Manage", null)];

    private static readonly PermissionGrant[] LakeCityManager =
        [new("Catalog.View", LakeCity), new("Catalog.Manage", LakeCity), new("Pricing.Manage", LakeCity)];

    [Fact]
    public void Only_an_all_branch_admin_changes_the_shared_menu()
    {
        CatalogPolicy.CanManageItem(Owner, null).ShouldBeNull();
        Denied(CatalogPolicy.CanManageItem(LakeCityManager, null));
    }

    [Fact]
    public void Branch_manager_manages_items_of_their_own_branch_only()
    {
        CatalogPolicy.CanManageItem(LakeCityManager, LakeCity).ShouldBeNull();
        Denied(CatalogPolicy.CanManageItem(LakeCityManager, Eden));
        CatalogPolicy.CanManageItem(Owner, Eden).ShouldBeNull();
    }

    [Fact]
    public void Branch_manager_prices_shared_items_at_their_branch_but_not_elsewhere()
    {
        CatalogPolicy.CanSetBranchPricing(LakeCityManager, null, LakeCity).ShouldBeNull();
        Denied(CatalogPolicy.CanSetBranchPricing(LakeCityManager, null, Eden));
    }

    [Fact]
    public void A_branch_only_item_has_no_price_at_other_branches()
    {
        CatalogPolicy.CanSetBranchPricing(Owner, LakeCity, Eden)!.Code.ShouldBe(ErrorCodes.ValidationFailed);
        CatalogPolicy.SoldAt(LakeCity, Eden).ShouldBeFalse();
        CatalogPolicy.SoldAt(null, Eden).ShouldBeTrue();
    }

    [Fact]
    public void Branch_users_see_shared_items_and_their_branch_items()
    {
        var scope = BranchScope.For(LakeCityManager, "Catalog.View");
        CatalogPolicy.CanSee(scope, null).ShouldBeTrue();
        CatalogPolicy.CanSee(scope, LakeCity).ShouldBeTrue();
        CatalogPolicy.CanSee(scope, Eden).ShouldBeFalse();
    }

    [Theory]
    [InlineData("0", true)]
    [InlineData("1250", true)]
    [InlineData("99.99", true)]
    [InlineData("99.999", false)]
    [InlineData("-1", false)]
    public void Prices_are_non_negative_with_at_most_two_decimals(string price, bool valid)
    {
        (CatalogPolicy.ValidatePrice(decimal.Parse(price, System.Globalization.CultureInfo.InvariantCulture)) is null).ShouldBe(valid);
    }

    private static void Denied(Error? error)
    {
        error.ShouldNotBeNull();
        error.Code.ShouldBe(ErrorCodes.PermissionDenied);
    }
}
