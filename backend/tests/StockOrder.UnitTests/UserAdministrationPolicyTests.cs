using Shouldly;
using StockOrder.Modules.Identity.Domain;
using StockOrder.SharedKernel.Context;
using StockOrder.SharedKernel.Errors;
using Xunit;

namespace StockOrder.UnitTests;

/// <summary>
/// The delegation chain the operator asked for: the platform admin creates the tenant and
/// its Owner; the Owner creates a branch manager per branch (Lake City, Eden); each branch
/// manager creates their own branch's staff (cashier, store keeper...) and nothing more.
/// </summary>
public sealed class UserAdministrationPolicyTests
{
    private static readonly Guid LakeCity = Guid.NewGuid();
    private static readonly Guid Eden = Guid.NewGuid();

    private static readonly string[] CashierPermissions = ["Identity.Self.Manage", "Sales.Order.Create", "Finance.CashSession.Operate"];
    private static readonly string[] StoreKeeperPermissions = ["Identity.Self.Manage", "Inventory.View", "Inventory.Document.Create"];

    private static readonly string[] ManagerPermissions =
    [
        .. CashierPermissions, .. StoreKeeperPermissions, "Users.View", "Users.Manage", "Branches.Manage", "Sales.Discount.Apply",
    ];

    private static readonly string[] OwnerPermissions = [.. ManagerPermissions, "Roles.Manage", "Settings.Manage", "Finance.Period.Close"];

    private static List<PermissionGrant> GrantsFor(IEnumerable<string> permissions, Guid? branchId) =>
        permissions.Distinct().Select(code => new PermissionGrant(code, branchId)).ToList();

    private static readonly List<PermissionGrant> Owner = GrantsFor(OwnerPermissions, null);
    private static readonly List<PermissionGrant> LakeCityManager = GrantsFor(ManagerPermissions, LakeCity);

    [Fact]
    public void Owner_can_make_a_branch_manager_for_any_branch()
    {
        UserAdministrationPolicy.CanAssignRole(Owner, ManagerPermissions, LakeCity).ShouldBeNull();
        UserAdministrationPolicy.CanAssignRole(Owner, ManagerPermissions, Eden).ShouldBeNull();
    }

    [Fact]
    public void Branch_manager_can_add_staff_roles_at_their_own_branch()
    {
        UserAdministrationPolicy.CanAssignRole(LakeCityManager, CashierPermissions, LakeCity).ShouldBeNull();
        UserAdministrationPolicy.CanAssignRole(LakeCityManager, StoreKeeperPermissions, LakeCity).ShouldBeNull();
    }

    [Fact]
    public void Branch_manager_cannot_assign_roles_at_another_branch_or_all_branches()
    {
        ShouldBeDenied(UserAdministrationPolicy.CanAssignRole(LakeCityManager, CashierPermissions, Eden));
        ShouldBeDenied(UserAdministrationPolicy.CanAssignRole(LakeCityManager, CashierPermissions, null));
    }

    [Fact]
    public void Branch_manager_cannot_create_another_manager_even_at_their_own_branch()
    {
        ShouldBeDenied(UserAdministrationPolicy.CanAssignRole(LakeCityManager, ManagerPermissions, LakeCity));
    }

    [Fact]
    public void Nobody_can_assign_a_role_with_permissions_they_do_not_hold()
    {
        ShouldBeDenied(UserAdministrationPolicy.CanAssignRole(LakeCityManager, [.. CashierPermissions, "Finance.Period.Close"], LakeCity));

        // An all-branch manager (not Owner) can't hand out the Owner role.
        var areaManager = GrantsFor(ManagerPermissions, null);
        ShouldBeDenied(UserAdministrationPolicy.CanAssignRole(areaManager, OwnerPermissions, null));
    }

    [Fact]
    public void Permission_held_only_at_another_branch_does_not_count()
    {
        // Manager at Lake City and cashier at Eden: Eden-only permissions don't help at Lake City.
        var grants = GrantsFor(ManagerPermissions, LakeCity).Concat(GrantsFor(["Sales.Return.Post"], Eden)).ToList();

        ShouldBeDenied(UserAdministrationPolicy.CanAssignRole(grants, [.. CashierPermissions, "Sales.Return.Post"], LakeCity));
    }

    [Fact]
    public void Branch_manager_can_manage_their_own_staff_but_not_other_branches_managers_or_the_owner()
    {
        var lakeCityCashier = new[] { new HeldRole(LakeCity, CashierPermissions) };
        var edenCashier = new[] { new HeldRole(Eden, CashierPermissions) };
        var otherLakeCityManager = new[] { new HeldRole(LakeCity, ManagerPermissions) };
        var owner = new[] { new HeldRole(null, OwnerPermissions) };

        UserAdministrationPolicy.CanManageUser(LakeCityManager, lakeCityCashier, LakeCity).ShouldBeNull();
        ShouldBeDenied(UserAdministrationPolicy.CanManageUser(LakeCityManager, edenCashier, Eden));
        ShouldBeDenied(UserAdministrationPolicy.CanManageUser(LakeCityManager, otherLakeCityManager, LakeCity));
        ShouldBeDenied(UserAdministrationPolicy.CanManageUser(LakeCityManager, owner, LakeCity));
    }

    [Fact]
    public void User_with_a_role_at_another_branch_too_is_off_limits_to_a_single_branch_manager()
    {
        var splitStaff = new[] { new HeldRole(LakeCity, CashierPermissions), new HeldRole(Eden, CashierPermissions) };

        ShouldBeDenied(UserAdministrationPolicy.CanManageUser(LakeCityManager, splitStaff, LakeCity));
        UserAdministrationPolicy.CanManageUser(Owner, splitStaff, LakeCity).ShouldBeNull();
    }

    [Fact]
    public void User_without_roles_is_managed_by_whoever_covers_their_default_branch()
    {
        UserAdministrationPolicy.CanManageUser(LakeCityManager, [], LakeCity).ShouldBeNull();
        ShouldBeDenied(UserAdministrationPolicy.CanManageUser(LakeCityManager, [], Eden));
        ShouldBeDenied(UserAdministrationPolicy.CanManageUser(LakeCityManager, [], null));
        UserAdministrationPolicy.CanManageUser(Owner, [], null).ShouldBeNull();
    }

    [Fact]
    public void Role_permissions_can_only_be_edited_with_all_branch_holdings_of_every_old_and_new_code()
    {
        UserAdministrationPolicy.CanSetRolePermissions(Owner, adminHoldsRole: false, CashierPermissions, ManagerPermissions).ShouldBeNull();

        // Branch-only holdings aren't enough: the role applies at every branch it's assigned for.
        var lakeCityManagerWithRolesManage = GrantsFor([.. ManagerPermissions, "Roles.Manage"], LakeCity);
        ShouldBeDenied(UserAdministrationPolicy.CanSetRolePermissions(
            lakeCityManagerWithRolesManage, adminHoldsRole: false, CashierPermissions, CashierPermissions));

        // Can't strip a permission you don't hold yourself out of a role either.
        var areaManager = GrantsFor([.. ManagerPermissions, "Roles.Manage"], null);
        ShouldBeDenied(UserAdministrationPolicy.CanSetRolePermissions(areaManager, adminHoldsRole: false, OwnerPermissions, CashierPermissions));
    }

    [Fact]
    public void Nobody_can_edit_a_role_they_hold()
    {
        ShouldBeDenied(UserAdministrationPolicy.CanSetRolePermissions(Owner, adminHoldsRole: true, OwnerPermissions, OwnerPermissions));
    }

    [Fact]
    public void Branch_manager_can_manage_devices_of_own_branch_staff_only()
    {
        var lakeCityManager = GrantsFor([.. ManagerPermissions, "Devices.Manage"], LakeCity);

        UserAdministrationPolicy.CanManageDevice(lakeCityManager, [new HeldRole(LakeCity, CashierPermissions)], LakeCity).ShouldBeNull();
        UserAdministrationPolicy.CanManageDevice(lakeCityManager, [], LakeCity).ShouldBeNull();

        ShouldBeDenied(UserAdministrationPolicy.CanManageDevice(lakeCityManager, [new HeldRole(Eden, CashierPermissions)], Eden));
        ShouldBeDenied(UserAdministrationPolicy.CanManageDevice(lakeCityManager, [new HeldRole(LakeCity, ManagerPermissions)], LakeCity));
        ShouldBeDenied(UserAdministrationPolicy.CanManageDevice(lakeCityManager, [new HeldRole(null, OwnerPermissions)], LakeCity));
        ShouldBeDenied(UserAdministrationPolicy.CanManageDevice(
            lakeCityManager, [new HeldRole(LakeCity, CashierPermissions), new HeldRole(Eden, CashierPermissions)], LakeCity));
    }

    [Fact]
    public void All_branch_devices_admin_can_manage_every_device()
    {
        var owner = GrantsFor([.. OwnerPermissions, "Devices.Manage"], null);

        UserAdministrationPolicy.CanManageDevice(owner, [new HeldRole(LakeCity, ManagerPermissions)], LakeCity).ShouldBeNull();
        UserAdministrationPolicy.CanManageDevice(owner, [new HeldRole(null, OwnerPermissions)], null).ShouldBeNull();
    }

    [Fact]
    public void Branch_scope_covers_only_its_branches_and_null_means_all()
    {
        var scope = BranchScope.For(LakeCityManager, "Users.Manage");
        scope.Covers(LakeCity).ShouldBeTrue();
        scope.Covers(Eden).ShouldBeFalse();
        scope.Covers(null).ShouldBeFalse();

        var ownerScope = BranchScope.For(Owner, "Users.Manage");
        ownerScope.Covers(Eden).ShouldBeTrue();
        ownerScope.Covers(null).ShouldBeTrue();

        BranchScope.For(LakeCityManager, "Roles.Manage").IsEmpty.ShouldBeTrue();
    }

    private static void ShouldBeDenied(Error? error)
    {
        error.ShouldNotBeNull();
        error.Code.ShouldBe(ErrorCodes.PermissionDenied);
    }
}
