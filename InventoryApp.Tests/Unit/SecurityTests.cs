using InventoryApp.Api.Models;
using InventoryApp.Api.Security;

namespace InventoryApp.Tests.Unit;

[Trait("Category", "Unit")]
public class PermissionsTests
{
    [Fact]
    public void EveryPermission_HasExactlyOneScope_AndIsDefined()
    {
        foreach (var permission in Permissions.All)
        {
            Assert.True(Permissions.IsDefined(permission));
            Assert.Contains(permission, Permissions.Of(Permissions.ScopeOf(permission)));
        }
    }

    [Fact]
    public void ScopesAreSplitAsDesigned()
    {
        Assert.Equal(PermissionScope.Inventory, Permissions.ScopeOf(Permissions.StockAdjust));
        Assert.Equal(PermissionScope.Admin, Permissions.ScopeOf(Permissions.EmployeesManage));
        Assert.Equal(PermissionScope.Admin, Permissions.ScopeOf(Permissions.RolesManage));
        Assert.Empty(Permissions.Of(PermissionScope.Inventory).Intersect(Permissions.Of(PermissionScope.Admin)));
    }

    [Fact]
    public void ScopeOf_Throws_ForAnUnknownPermission()
    {
        Assert.False(Permissions.IsDefined("Nope.Nothing"));
        Assert.Throws<ArgumentException>(() => Permissions.ScopeOf("Nope.Nothing"));
    }
}

[Trait("Category", "Unit")]
public class PinHasherTests
{
    [Theory]
    [InlineData("123456")]
    [InlineData("000000")]
    public void IsValidFormat_AcceptsSixDigits(string pin)
    {
        Assert.True(PinHasher.IsValidFormat(pin));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("12345")]
    [InlineData("1234567")]
    [InlineData("12345a")]
    [InlineData("12 456")]
    [InlineData("١٢٣٤٥٦")] // Arabic-Indic digits are digits, but not ASCII ones.
    public void IsValidFormat_RejectsEverythingElse(string? pin)
    {
        Assert.False(PinHasher.IsValidFormat(pin));
    }

    [Fact]
    public void Hash_ThrowsForAMalformedPin()
    {
        Assert.Throws<ArgumentException>(() => PinHasher.Hash("12345"));
    }

    [Fact]
    public void Verify_AcceptsTheRightPin_AndRejectsAWrongOne()
    {
        var hash = PinHasher.Hash("123456");

        Assert.True(PinHasher.Verify("123456", hash));
        Assert.False(PinHasher.Verify("654321", hash));
    }

    [Fact]
    public void Hash_IsSalted_AndDoesNotContainThePin()
    {
        var first = PinHasher.Hash("123456");
        var second = PinHasher.Hash("123456");

        Assert.NotEqual(first, second);
        Assert.DoesNotContain("123456", first);
        Assert.True(first.Length <= 100, "The hash must fit Employee.PinHash (nvarchar(100)).");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a bcrypt hash")]
    public void Verify_ReturnsFalse_ForAMissingOrCorruptHash(string? hash)
    {
        Assert.False(PinHasher.Verify("123456", hash));
    }

    [Fact]
    public void Verify_ReturnsFalse_ForAMalformedPin()
    {
        var hash = PinHasher.Hash("123456");

        Assert.False(PinHasher.Verify("12345", hash));
        Assert.False(PinHasher.Verify(null, hash));
    }
}

[Trait("Category", "Unit")]
public class RoleTests
{
    private const int TenantId = 1;

    [Fact]
    public void Constructor_CreatesRole_WithItsPermissions()
    {
        var role = new Role(TenantId, " Pickers ", PermissionScope.Inventory,
            [Permissions.ProductsRead, Permissions.StockRead, Permissions.ProductsRead]);

        Assert.Equal("Pickers", role.Name);
        Assert.Equal(PermissionScope.Inventory, role.Scope);
        Assert.False(role.IsSystemRole);
        Assert.Equal(
            new[] { Permissions.ProductsRead, Permissions.StockRead }.Order(),
            role.Permissions.Select(p => p.Permission).Order());
    }

    [Fact]
    public void Constructor_Throws_ForAPermissionOfTheOtherScope()
    {
        Assert.Throws<ArgumentException>(() =>
            new Role(TenantId, "Pickers", PermissionScope.Inventory, [Permissions.EmployeesManage]));

        Assert.Throws<ArgumentException>(() =>
            new Role(TenantId, "Admins", PermissionScope.Admin, [Permissions.StockAdjust]));
    }

    [Fact]
    public void Constructor_Throws_ForAnUnknownPermission()
    {
        Assert.Throws<ArgumentException>(() =>
            new Role(TenantId, "Pickers", PermissionScope.Inventory, ["Made.Up"]));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_Throws_ForABlankName(string name)
    {
        Assert.Throws<ArgumentException>(() =>
            new Role(TenantId, name, PermissionScope.Inventory, []));
    }

    [Fact]
    public void Constructor_Throws_WhenNameIsTooLongOrTenantIsMissing()
    {
        Assert.Throws<ArgumentException>(() =>
            new Role(TenantId, new string('a', 51), PermissionScope.Inventory, []));
        Assert.Throws<ArgumentException>(() =>
            new Role(0, "Pickers", PermissionScope.Inventory, []));
    }

    [Fact]
    public void SetPermissions_ReplacesThePermissions()
    {
        var role = new Role(TenantId, "Pickers", PermissionScope.Inventory,
            [Permissions.ProductsRead, Permissions.StockRead]);

        role.SetPermissions([Permissions.StockRead, Permissions.StockAdjust]);

        Assert.Equal(
            new[] { Permissions.StockAdjust, Permissions.StockRead },
            role.Permissions.Select(p => p.Permission).Order());
    }

    [Fact]
    public void SetPermissions_Throws_ForAPermissionOfTheOtherScope_AndLeavesTheRoleUnchanged()
    {
        var role = new Role(TenantId, "Pickers", PermissionScope.Inventory, [Permissions.ProductsRead]);

        Assert.Throws<ArgumentException>(() => role.SetPermissions([Permissions.RolesManage]));

        Assert.Equal(Permissions.ProductsRead, Assert.Single(role.Permissions).Permission);
    }

    [Fact]
    public void SystemRole_CannotBeRenamedOrEdited()
    {
        var role = new Role(TenantId, "Admins", PermissionScope.Admin,
            [Permissions.EmployeesManage], isSystemRole: true);

        Assert.Throws<InvalidOperationException>(() => role.Rename("Other"));
        Assert.Throws<InvalidOperationException>(() => role.SetPermissions([]));
        Assert.Single(role.Permissions);
    }

    [Fact]
    public void Rename_ChangesTheNameOfAnOrdinaryRole()
    {
        var role = new Role(TenantId, "Pickers", PermissionScope.Inventory, []);

        role.Rename("  Packers ");

        Assert.Equal("Packers", role.Name);
    }
}

[Trait("Category", "Unit")]
public class DefaultRolesTests
{
    private const int TenantId = 4;

    [Fact]
    public void CreateFor_ReturnsTheThreeDefaultRoles_ForTheTenant()
    {
        var roles = DefaultRoles.CreateFor(TenantId);

        Assert.Equal(
            new[] { DefaultRoles.TenantAdmin, DefaultRoles.WarehouseManager, DefaultRoles.WarehouseEmployee }.Order(),
            roles.Select(r => r.Name).Order());
        Assert.All(roles, r => Assert.Equal(TenantId, r.TenantId));
    }

    [Fact]
    public void TenantAdmin_IsAnImmutableAdminRole_WithAllAdminPermissionsOnly()
    {
        var role = Role(DefaultRoles.TenantAdmin);

        Assert.True(role.IsSystemRole);
        Assert.Equal(PermissionScope.Admin, role.Scope);
        Assert.Equal(Permissions.Of(PermissionScope.Admin).Order(), role.Permissions.Select(p => p.Permission).Order());
    }

    [Fact]
    public void WarehouseManager_HasAllInventoryPermissions_AndNoAdminPermission()
    {
        var role = Role(DefaultRoles.WarehouseManager);

        Assert.False(role.IsSystemRole);
        Assert.Equal(PermissionScope.Inventory, role.Scope);
        Assert.Equal(Permissions.Of(PermissionScope.Inventory).Order(), role.Permissions.Select(p => p.Permission).Order());
    }

    [Fact]
    public void WarehouseEmployee_CanReadAndAdjustStock_ButNotChangeProductsOrLocations()
    {
        var granted = Role(DefaultRoles.WarehouseEmployee).Permissions.Select(p => p.Permission).ToList();

        Assert.Contains(Permissions.ProductsRead, granted);
        Assert.Contains(Permissions.StockAdjust, granted);
        Assert.DoesNotContain(Permissions.ProductsWrite, granted);
        Assert.DoesNotContain(Permissions.LocationsManage, granted);
        Assert.All(granted, p => Assert.Equal(PermissionScope.Inventory, Permissions.ScopeOf(p)));
    }

    [Fact]
    public void OnlyTheTenantAdminRole_ManagesEmployeesRolesAndPermissions()
    {
        var rolesWithAdminPermissions = DefaultRoles.CreateFor(TenantId)
            .Where(r => r.Permissions.Any(p => p.Permission is Permissions.EmployeesManage or Permissions.RolesManage))
            .Select(r => r.Name);

        Assert.Equal(new[] { DefaultRoles.TenantAdmin }, rolesWithAdminPermissions);
    }

    private static Role Role(string name) =>
        DefaultRoles.CreateFor(TenantId).Single(r => r.Name == name);
}
