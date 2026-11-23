using InventoryApp.Api.Data;
using InventoryApp.Api.Models;
using InventoryApp.Api.Security;

namespace InventoryApp.Tests.Unit;

[Trait("Category", "Unit")]
public class TenantAdminTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 8, 0, 0, TimeSpan.Zero);
    private static readonly Guid Directory = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid Identity = Guid.Parse("33333333-3333-4333-8333-333333333333");

    // Roles get their ids from the database; for these tests a saved role is faked with reflection-free means:
    // the id is set through EF's private setter via the model's own constructor path, so use a helper.
    private static Role SavedRole(PermissionScope scope, int tenantId = 1, int id = 7)
    {
        var role = scope == PermissionScope.Admin
            ? new Role(tenantId, "Admin role", scope, [Permissions.TenantSettings])
            : new Role(tenantId, "Inventory role", scope, [Permissions.ProductsRead]);

        typeof(Role).GetProperty(nameof(Role.Id))!.SetValue(role, id);
        return role;
    }

    [Fact]
    public void ANewMapping_IsActive_AndTakesTenantAndScopeFromTheRole()
    {
        var admin = new TenantAdmin(SavedRole(PermissionScope.Admin, tenantId: 4, id: 9), Directory, Identity, Now, "  Anna  ");

        Assert.True(admin.IsActive);
        Assert.Equal((4, 9, PermissionScope.Admin), (admin.TenantId, admin.RoleId, admin.RoleScope));
        Assert.Equal((Directory, Identity), (admin.EntraTenantId, admin.EntraObjectId));
        Assert.Equal(Now, admin.CreatedAt);
        Assert.Equal("Anna", admin.DisplayName);
        Assert.Null(admin.DeactivatedAt);
    }

    [Fact]
    public void AnInventoryRole_IsRefused()
    {
        Assert.Throws<ArgumentException>(() => new TenantAdmin(SavedRole(PermissionScope.Inventory), Directory, Identity, Now));
    }

    [Fact]
    public void AnUnsavedRole_IsRefused()
    {
        Assert.Throws<ArgumentException>(() => new TenantAdmin(SavedRole(PermissionScope.Admin, id: 0), Directory, Identity, Now));
    }

    [Fact]
    public void EmptyIdentities_AreRefused()
    {
        var role = SavedRole(PermissionScope.Admin);

        Assert.Throws<ArgumentException>(() => new TenantAdmin(role, Guid.Empty, Identity, Now));
        Assert.Throws<ArgumentException>(() => new TenantAdmin(role, Directory, Guid.Empty, Now));
        Assert.Throws<ArgumentNullException>(() => new TenantAdmin(null!, Directory, Identity, Now));
    }

    [Fact]
    public void ADisplayName_IsOptionalAndBounded()
    {
        var role = SavedRole(PermissionScope.Admin);

        Assert.Null(new TenantAdmin(role, Directory, Identity, Now, "  ").DisplayName);
        Assert.Throws<ArgumentException>(() => new TenantAdmin(role, Directory, Identity, Now, new string('x', 201)));
    }

    [Fact]
    public void Deactivating_RecordsWhen_AndReactivatingClearsIt()
    {
        var admin = new TenantAdmin(SavedRole(PermissionScope.Admin), Directory, Identity, Now);

        admin.Deactivate(Now.AddHours(1));
        Assert.False(admin.IsActive);
        Assert.Equal(Now.AddHours(1), admin.DeactivatedAt);

        admin.Deactivate(Now.AddHours(2));
        Assert.Equal(Now.AddHours(1), admin.DeactivatedAt);

        admin.Activate();
        Assert.True(admin.IsActive);
        Assert.Null(admin.DeactivatedAt);
    }
}

[Trait("Category", "Unit")]
public class AdministrationRestrictionTests
{
    [Fact]
    public void ANewScope_HasInventoryAccess()
    {
        Assert.True(new TenantContext().InventoryAccess);
    }

    [Fact]
    public void RestrictingToAdministration_TakesInventoryAccessAway_ForGood()
    {
        var context = new TenantContext();

        context.RestrictToAdministration();
        context.Set(5);
        context.RestrictToAdministration();

        Assert.False(context.InventoryAccess);
        Assert.Equal(5, context.TenantId);
    }

    [Fact]
    public void BindingATenantFirst_DoesNotGiveUpTheRestriction()
    {
        var context = new TenantContext();
        context.Set(5);

        context.RestrictToAdministration();

        Assert.False(context.InventoryAccess);
    }
}
