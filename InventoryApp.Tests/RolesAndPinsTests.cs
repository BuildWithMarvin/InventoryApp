using InventoryApp.Api.Data;
using InventoryApp.Api.Models;
using InventoryApp.Api.Security;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

// IntegrationTestBase gives every tenant the default roles, as production will.
[Trait("Category", "Integration")]
[Collection("Integration Tests")]
public class RolesAndPinsTests : IntegrationTestBase
{
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 8, 0, 0, TimeSpan.Zero);

    public RolesAndPinsTests(DatabaseFixture databaseFixture)
        : base(databaseFixture)
    {
    }

    // ---- Roles and permissions -------------------------------------------------------

    [Fact]
    public async Task DefaultRoles_ArePersistedWithTheirPermissions()
    {
        var roles = await Context.Roles.Include(r => r.Permissions).ToListAsync();

        Assert.Equal(3, roles.Count);

        var admin = roles.Single(r => r.Name == DefaultRoles.TenantAdmin);
        Assert.True(admin.IsSystemRole);
        Assert.Equal(PermissionScope.Admin, admin.Scope);
        Assert.Equal(
            Permissions.Of(PermissionScope.Admin).Order(),
            admin.Permissions.Select(p => p.Permission).Order());

        var employee = roles.Single(r => r.Name == DefaultRoles.WarehouseEmployee);
        Assert.Contains(Permissions.StockAdjust, employee.Permissions.Select(p => p.Permission));
    }

    [Fact]
    public async Task RoleNames_AreUniquePerTenant_NotGlobally()
    {
        // The second tenant has the same three names (seeded by CreateTenantAsync) ...
        var other = await CreateTenantAsync("Other tenant");
        await using var otherContext = CreateContextFor(other);
        Assert.Equal(3, await otherContext.Roles.CountAsync());

        // ... but one tenant can't have a name twice.
        Context.Roles.Add(new Role(TenantId, DefaultRoles.WarehouseEmployee, PermissionScope.Inventory, []));
        await Assert.ThrowsAsync<DbUpdateException>(() => Context.SaveChangesAsync());
    }

    [Fact]
    public async Task Roles_AreInvisibleToOtherTenants()
    {
        var other = await CreateTenantAsync("Other tenant");
        await using var otherContext = CreateContextFor(other);

        var ownIds = await Context.Roles.Select(r => r.Id).ToListAsync();
        var otherIds = await otherContext.Roles.Select(r => r.Id).ToListAsync();

        Assert.Equal(3, ownIds.Count);
        Assert.Equal(3, otherIds.Count);
        Assert.Empty(ownIds.Intersect(otherIds));
    }

    [Fact]
    public async Task EditingARole_ReplacesItsPermissionRows()
    {
        var role = new Role(TenantId, "Pickers", PermissionScope.Inventory,
            [Permissions.ProductsRead, Permissions.StockRead]);
        Context.Roles.Add(role);
        await Context.SaveChangesAsync();

        role.SetPermissions([Permissions.StockRead, Permissions.StockAdjust]);
        await Context.SaveChangesAsync();

        await using var fresh = CreateContextFor(Tenant);
        var reloaded = await fresh.Roles.Include(r => r.Permissions).SingleAsync(r => r.Name == "Pickers");
        Assert.Equal(
            new[] { Permissions.StockAdjust, Permissions.StockRead },
            reloaded.Permissions.Select(p => p.Permission).Order());
    }

    // ---- Employees and roles ---------------------------------------------------------

    [Fact]
    public async Task EmployeeWithARole_IsPersistedAndLoaded()
    {
        var role = await Context.Roles.SingleAsync(r => r.Name == DefaultRoles.WarehouseEmployee);
        var employee = new Employee(TenantId, "Tom", "EMP-1");
        employee.AssignRole(role);
        employee.AssignRole(role); // idempotent

        Context.Employees.Add(employee);
        await Context.SaveChangesAsync();

        await using var fresh = CreateContextFor(Tenant);
        var reloaded = await fresh.Employees
            .Include(e => e.EmployeeRoles).ThenInclude(er => er.Role)
            .SingleAsync();

        var assignment = Assert.Single(reloaded.EmployeeRoles);
        Assert.Equal(DefaultRoles.WarehouseEmployee, assignment.Role!.Name);
        Assert.Equal(PermissionScope.Inventory, assignment.RoleScope);
    }

    [Fact]
    public async Task RemovingARole_DeletesTheAssignment()
    {
        var role = await Context.Roles.SingleAsync(r => r.Name == DefaultRoles.WarehouseEmployee);
        var employee = new Employee(TenantId, "Tom", "EMP-1");
        employee.AssignRole(role);
        Context.Employees.Add(employee);
        await Context.SaveChangesAsync();

        employee.RemoveRole(role.Id);
        await Context.SaveChangesAsync();

        Assert.Equal(0, await Context.EmployeeRoles.CountAsync());
    }

    [Fact]
    public async Task AssignRole_Throws_ForARoleLoadedFromAnotherTenant()
    {
        var other = await CreateTenantAsync("Other tenant");
        await using var otherContext = CreateContextFor(other);
        var foreignRole = await otherContext.Roles.SingleAsync(r => r.Name == DefaultRoles.WarehouseEmployee);
        var employee = new Employee(TenantId, "Tom", "EMP-1");

        Assert.Throws<ArgumentException>(() => employee.AssignRole(foreignRole));
    }

    [Fact]
    public async Task ARoleInUse_CannotBeDeleted()
    {
        var role = await Context.Roles.SingleAsync(r => r.Name == DefaultRoles.WarehouseEmployee);
        var employee = new Employee(TenantId, "Tom", "EMP-1");
        employee.AssignRole(role);
        Context.Employees.Add(employee);
        await Context.SaveChangesAsync();

        await using var fresh = CreateContextFor(Tenant);
        fresh.Roles.Remove(await fresh.Roles.SingleAsync(r => r.Name == DefaultRoles.WarehouseEmployee));

        await Assert.ThrowsAsync<DbUpdateException>(() => fresh.SaveChangesAsync());
    }

    // ---- The database enforces the rules on its own ----------------------------------

    [Fact]
    public async Task Database_RejectsAnAdminRoleForAnEmployee()
    {
        var employee = await SaveEmployeeAsync();
        var adminRole = await Context.Roles.SingleAsync(r => r.Name == DefaultRoles.TenantAdmin);
        var inventoryRole = await Context.Roles.SingleAsync(r => r.Name == DefaultRoles.WarehouseManager);

        // Control: an inventory role is accepted.
        await Context.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO EmployeeRoles (TenantId, EmployeeId, RoleId, RoleScope) VALUES ({TenantId}, {employee.Id}, {inventoryRole.Id}, 0)");

        // Claiming the role is an inventory role: the key doesn't match its real scope.
        await Assert.ThrowsAsync<SqlException>(() => Context.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO EmployeeRoles (TenantId, EmployeeId, RoleId, RoleScope) VALUES ({TenantId}, {employee.Id}, {adminRole.Id}, 0)"));

        // Admitting it is an admin role: the key matches, but the check constraint doesn't allow it.
        await Assert.ThrowsAsync<SqlException>(() => Context.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO EmployeeRoles (TenantId, EmployeeId, RoleId, RoleScope) VALUES ({TenantId}, {employee.Id}, {adminRole.Id}, 1)"));
    }

    [Fact]
    public async Task Database_RejectsARoleOfAnotherTenantForAnEmployee()
    {
        var employee = await SaveEmployeeAsync();
        var ownRole = await Context.Roles.SingleAsync(r => r.Name == DefaultRoles.WarehouseManager);

        var other = await CreateTenantAsync("Other tenant");
        await using var otherContext = CreateContextFor(other);
        var foreignRole = await otherContext.Roles.SingleAsync(r => r.Name == DefaultRoles.WarehouseManager);

        // Control: the same statement with a role of the same tenant works.
        await Context.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO EmployeeRoles (TenantId, EmployeeId, RoleId, RoleScope) VALUES ({TenantId}, {employee.Id}, {ownRole.Id}, 0)");
        await Context.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM EmployeeRoles WHERE EmployeeId = {employee.Id}");

        await Assert.ThrowsAsync<SqlException>(() => Context.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO EmployeeRoles (TenantId, EmployeeId, RoleId, RoleScope) VALUES ({TenantId}, {employee.Id}, {foreignRole.Id}, 0)"));
    }

    [Fact]
    public async Task Database_RejectsAPermissionForARoleOfAnotherTenant()
    {
        var ownRole = await Context.Roles.SingleAsync(r => r.Name == DefaultRoles.WarehouseEmployee);

        var other = await CreateTenantAsync("Other tenant");
        await using var otherContext = CreateContextFor(other);
        var foreignRole = await otherContext.Roles.SingleAsync(r => r.Name == DefaultRoles.WarehouseEmployee);

        // Control: a permission the role doesn't have yet can be added to an own role.
        await Context.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO RolePermissions (TenantId, RoleId, Permission) VALUES ({TenantId}, {ownRole.Id}, {Permissions.LocationsManage})");

        await Assert.ThrowsAsync<SqlException>(() => Context.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO RolePermissions (TenantId, RoleId, Permission) VALUES ({TenantId}, {foreignRole.Id}, {Permissions.LocationsManage})"));
    }

    // ---- PIN state and tenant setting ------------------------------------------------

    [Fact]
    public async Task PinState_SurvivesAReload()
    {
        var employee = new Employee(TenantId, "Tom", "EMP-1");
        employee.SetPin(PinHasher.Hash("123456"), mustChangePin: true);
        for (var i = 0; i < Employee.MaxFailedPinAttempts; i++)
            employee.RegisterFailedPin(Now);
        employee.RegisterFailedPin(Now); // ignored while locked out
        Context.Employees.Add(employee);
        await Context.SaveChangesAsync();

        await using var fresh = CreateContextFor(Tenant);
        var reloaded = await fresh.Employees.SingleAsync();

        Assert.True(reloaded.HasPin);
        Assert.True(PinHasher.Verify("123456", reloaded.PinHash));
        Assert.True(reloaded.MustChangePin);
        Assert.Equal(Now + Employee.LockoutDuration, reloaded.LockoutEnd);
        Assert.True(reloaded.IsLockedOut(Now));
        Assert.False(reloaded.IsLockedOut(Now + Employee.LockoutDuration));
    }

    [Fact]
    public async Task NewEmployees_AreActive_AndRequirePinIsPersisted()
    {
        await SaveEmployeeAsync();
        Assert.True((await Context.Employees.SingleAsync()).IsActive);

        await using var noTenant = CreateContextWithoutTenant();
        var tenant = await noTenant.Tenants.SingleAsync(t => t.Id == TenantId);
        Assert.False(tenant.RequirePin);

        tenant.SetRequirePin(true);
        await noTenant.SaveChangesAsync();

        await using var fresh = CreateContextWithoutTenant();
        Assert.True((await fresh.Tenants.SingleAsync(t => t.Id == TenantId)).RequirePin);
    }

    // ---- Creating a tenant (the flow a platform admin will use) ----------------------

    [Fact]
    public async Task ANewTenant_CanBeCreatedAndSeededInOneScope()
    {
        // A platform admin has no tenant to begin with: the tenant is created first,
        // then the scope is bound to it and the default roles are added.
        var tenantContext = new TenantContext();
        await using var context = new InventoryDbContext(
            new DbContextOptionsBuilder<InventoryDbContext>()
                .UseSqlServer(Context.Database.GetConnectionString())
                .Options,
            tenantContext);

        var tenant = new Tenant("Brand new tenant");
        context.Tenants.Add(tenant);
        await context.SaveChangesAsync();

        tenantContext.Set(tenant.Id);
        context.Roles.AddRange(DefaultRoles.CreateFor(tenant.Id));
        await context.SaveChangesAsync();

        Assert.Equal(3, await context.Roles.CountAsync());
    }

    private async Task<Employee> SaveEmployeeAsync()
    {
        var employee = new Employee(TenantId, "Tom", "EMP-1");
        Context.Employees.Add(employee);
        await Context.SaveChangesAsync();
        return employee;
    }
}
