using InventoryApp.Api.Models;
using InventoryApp.Api.Security;

namespace InventoryApp.Tests.Unit;

[Trait("Category", "Unit")]
public class EmployeeTest
{
    private const int TenantId = 1;

    private static readonly DateTimeOffset Now = new(2026, 11, 1, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Constructor_CreatesActiveEmployee_WithoutPinOrRoles()
    {

        var employee = new Employee(
            TenantId,
            "Tom",
            "EMP-12345"
        );

        Assert.Equal("Tom", employee.Name);
        Assert.Equal("EMP-12345", employee.BadgeBarcode);
        Assert.True(employee.IsActive);
        Assert.False(employee.HasPin);
        Assert.False(employee.MustChangePin);
        Assert.Empty(employee.EmployeeRoles);
    }

    [Fact]
    public void Constructor_TrimsName()
    {

        var employee = new Employee(
                  TenantId,
                  " Tom ",
                  "EMP-12345"
              );

        Assert.Equal("Tom", employee.Name);
    }

    [Fact]
    public void Constructor_TrimsBadgeBarcode()
    {

        var employee = new Employee(
                  TenantId,
                  "Tom",
                  " EMP-12345 "
              );

        Assert.Equal("EMP-12345", employee.BadgeBarcode);
    }

    [Fact]
    public void Constructor_ThrowsArgumentException_WhenNameIsEmpty()
    {

        Assert.Throws<ArgumentException>(() =>
            new Employee(TenantId, "", "EMP-12345"));
    }
    [Fact]
    public void Constructor_ThrowsArgumentException_WhenNameIsWhitespace()
    {

        Assert.Throws<ArgumentException>(() =>
            new Employee(TenantId, "   ", "EMP-12345"));
    }
    [Fact]
    public void Constructor_ThrowsArgumentException_WhenBadgeBarcodeIsEmpty()
    {

        Assert.Throws<ArgumentException>(() =>
            new Employee(TenantId, "Tom", ""));
    }
    [Fact]
    public void Constructor_ThrowsArgumentException_WhenBadgeBarcodeIsWhitespace()
    {

        Assert.Throws<ArgumentException>(() =>
            new Employee(TenantId, "Tom", "   "));
    }

    // ---- Active flag -----------------------------------------------------------------

    [Fact]
    public void DeactivateAndActivate_ChangeIsActive()
    {
        var employee = new Employee(TenantId, "Tom", "EMP-1");

        employee.Deactivate();
        Assert.False(employee.IsActive);

        employee.Activate();
        Assert.True(employee.IsActive);
    }

    // ---- Roles -----------------------------------------------------------------------

    [Fact]
    public void AssignRole_AddsAnInventoryRoleOfTheSameTenant()
    {
        var employee = new Employee(TenantId, "Tom", "EMP-1");
        var role = InventoryRole();

        employee.AssignRole(role);

        var assignment = Assert.Single(employee.EmployeeRoles);
        Assert.Same(role, assignment.Role);
        Assert.Equal(TenantId, assignment.TenantId);
        Assert.Equal(PermissionScope.Inventory, assignment.RoleScope);
    }

    [Fact]
    public void AssignRole_IsIdempotent()
    {
        var employee = new Employee(TenantId, "Tom", "EMP-1");
        var role = InventoryRole();

        employee.AssignRole(role);
        employee.AssignRole(role);

        Assert.Single(employee.EmployeeRoles);
    }

    [Fact]
    public void AssignRole_Throws_ForAnAdminRole()
    {
        var employee = new Employee(TenantId, "Tom", "EMP-1");
        var adminRole = new Role(TenantId, "Admins", PermissionScope.Admin, [Permissions.EmployeesManage]);

        Assert.Throws<ArgumentException>(() => employee.AssignRole(adminRole));
        Assert.Empty(employee.EmployeeRoles);
    }

    [Fact]
    public void AssignRole_Throws_ForARoleOfAnotherTenant()
    {
        var employee = new Employee(TenantId, "Tom", "EMP-1");
        var foreignRole = new Role(TenantId + 1, "Pickers", PermissionScope.Inventory, [Permissions.ProductsRead]);

        Assert.Throws<ArgumentException>(() => employee.AssignRole(foreignRole));
    }

    // ---- PIN and lockout -------------------------------------------------------------

    [Fact]
    public void SetPin_StoresTheHash_AndClearsLockout()
    {
        var employee = EmployeeLockedOut();

        employee.SetPin("hash", mustChangePin: true);

        Assert.True(employee.HasPin);
        Assert.True(employee.MustChangePin);
        Assert.Equal(0, employee.FailedPinAttempts);
        Assert.False(employee.IsLockedOut(Now));
    }

    [Fact]
    public void SetPin_Throws_WhenHashIsBlank()
    {
        var employee = new Employee(TenantId, "Tom", "EMP-1");

        Assert.Throws<ArgumentException>(() => employee.SetPin("  ", mustChangePin: false));
    }

    [Fact]
    public void RegisterFailedPin_LocksOutAfterTheMaximumNumberOfAttempts()
    {
        var employee = new Employee(TenantId, "Tom", "EMP-1");

        for (var i = 1; i < Employee.MaxFailedPinAttempts; i++)
        {
            employee.RegisterFailedPin(Now);
            Assert.False(employee.IsLockedOut(Now));
        }

        employee.RegisterFailedPin(Now);

        Assert.True(employee.IsLockedOut(Now));
        Assert.Equal(Now + Employee.LockoutDuration, employee.LockoutEnd);
    }

    [Fact]
    public void IsLockedOut_EndsWhenTheLockoutHasPassed()
    {
        var employee = EmployeeLockedOut();

        Assert.True(employee.IsLockedOut(Now + Employee.LockoutDuration - TimeSpan.FromSeconds(1)));
        Assert.False(employee.IsLockedOut(Now + Employee.LockoutDuration));
    }

    [Fact]
    public void RegisterFailedPin_DoesNotExtendAnActiveLockout()
    {
        var employee = EmployeeLockedOut();
        var lockoutEnd = employee.LockoutEnd;

        employee.RegisterFailedPin(Now + TimeSpan.FromMinutes(5));

        Assert.Equal(lockoutEnd, employee.LockoutEnd);
        Assert.Equal(0, employee.FailedPinAttempts);
    }

    [Fact]
    public void RegisterFailedPin_StartsCountingAgain_AfterALockoutExpired()
    {
        var employee = EmployeeLockedOut();
        var afterLockout = Now + Employee.LockoutDuration + TimeSpan.FromMinutes(1);

        employee.RegisterFailedPin(afterLockout);

        Assert.False(employee.IsLockedOut(afterLockout));
        Assert.Equal(1, employee.FailedPinAttempts);
    }

    [Fact]
    public void RegisterSuccessfulPin_ResetsTheFailureCount()
    {
        var employee = new Employee(TenantId, "Tom", "EMP-1");
        employee.RegisterFailedPin(Now);
        employee.RegisterFailedPin(Now);

        employee.RegisterSuccessfulPin();

        Assert.Equal(0, employee.FailedPinAttempts);
        Assert.Null(employee.LockoutEnd);
    }

    private static Role InventoryRole() =>
        new(TenantId, "Pickers", PermissionScope.Inventory, [Permissions.ProductsRead]);

    private static Employee EmployeeLockedOut()
    {
        var employee = new Employee(TenantId, "Tom", "EMP-1");

        for (var i = 0; i < Employee.MaxFailedPinAttempts; i++)
            employee.RegisterFailedPin(Now);

        return employee;
    }
}
