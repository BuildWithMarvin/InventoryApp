using InventoryApp.Api.Data;
using InventoryApp.Api.Models;

namespace InventoryApp.Tests.Unit;

[Trait("Category", "Unit")]
public class TenantTests
{
    [Fact]
    public void Constructor_CreatesActiveTenant_WithTrimmedName()
    {
        var tenant = new Tenant("  Acme  ");

        Assert.Equal("Acme", tenant.Name);
        Assert.True(tenant.IsActive);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_ThrowsArgumentException_WhenNameIsBlank(string name)
    {
        Assert.Throws<ArgumentException>(() => new Tenant(name));
    }

    [Fact]
    public void Constructor_ThrowsArgumentException_WhenNameIsTooLong()
    {
        Assert.Throws<ArgumentException>(() => new Tenant(new string('a', 101)));
    }

    [Fact]
    public void DeactivateAndActivate_ChangeIsActive()
    {
        var tenant = new Tenant("Acme");

        tenant.Deactivate();
        Assert.False(tenant.IsActive);

        tenant.Activate();
        Assert.True(tenant.IsActive);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void TenantOwnedEntities_RequireAPositiveTenantId(int tenantId)
    {
        Assert.Throws<ArgumentException>(() => new Employee(tenantId, "Tom", "EMP-1"));
        Assert.Throws<ArgumentException>(() => new Product(tenantId, "10000001", "Product", 1));
        Assert.Throws<ArgumentException>(() => new ProductStock(tenantId, "10000001", 1));
    }

    [Fact]
    public void TenantOwnedEntities_KeepTheirTenantId()
    {
        Assert.Equal(7, new Employee(7, "Tom", "EMP-1").TenantId);
        Assert.Equal(7, new Product(7, "10000001", "Product", 1).TenantId);
        Assert.Equal(7, new ProductStock(7, "10000001", 1).TenantId);
    }
}

[Trait("Category", "Unit")]
public class TenantContextTests
{
    [Fact]
    public void TenantId_IsNull_UntilSet()
    {
        Assert.Null(new TenantContext().TenantId);
    }

    [Fact]
    public void Set_StoresTheTenant_AndAcceptsTheSameTenantAgain()
    {
        var context = new TenantContext();

        context.Set(3);
        context.Set(3);

        Assert.Equal(3, context.TenantId);
    }

    [Fact]
    public void Set_Throws_WhenSwitchingToAnotherTenant()
    {
        var context = new TenantContext();
        context.Set(3);

        Assert.Throws<InvalidOperationException>(() => context.Set(4));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Set_Throws_WhenTenantIdIsNotPositive(int tenantId)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TenantContext().Set(tenantId));
    }
}
