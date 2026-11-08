using InventoryApp.Api.Models;
using InventoryApp.Api.Security;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

// The mapping of Entra identities to tenants. The rules are enforced by the database itself, so even code that
// bypasses the domain model (raw SQL) can't create a conflicting or cross-tenant administrator.
[Trait("Category", "Integration")]
[Collection("Integration Tests")]
public class TenantAdminPersistenceTests : IntegrationTestBase
{
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 8, 0, 0, TimeSpan.Zero);

    public TenantAdminPersistenceTests(DatabaseFixture databaseFixture)
        : base(databaseFixture)
    {
    }

    private async Task<Role> RoleAsync(InventoryApp.Api.Data.InventoryDbContext context, string name) =>
        await context.Roles.SingleAsync(r => r.Name == name);

    private async Task<TenantAdmin> AddAdminAsync(Guid directory, Guid objectId)
    {
        var admin = new TenantAdmin(await RoleAsync(Context, DefaultRoles.TenantAdmin), directory, objectId, Now, "Anna");
        Context.TenantAdmins.Add(admin);
        await Context.SaveChangesAsync();
        return admin;
    }

    [Fact]
    public async Task AMapping_IsPersisted_WithItsRoleAndAudit()
    {
        var directory = Guid.NewGuid();
        var objectId = Guid.NewGuid();

        var admin = await AddAdminAsync(directory, objectId);

        await using var fresh = CreateContextFor(Tenant);
        var loaded = await fresh.TenantAdmins.Include(a => a.Role).SingleAsync(a => a.Id == admin.Id);
        Assert.Equal((TenantId, directory, objectId), (loaded.TenantId, loaded.EntraTenantId, loaded.EntraObjectId));
        Assert.Equal(DefaultRoles.TenantAdmin, loaded.Role!.Name);
        Assert.Equal(PermissionScope.Admin, loaded.RoleScope);
        Assert.True(loaded.IsActive);
        Assert.Equal(Now, loaded.CreatedAt);
        Assert.Null(loaded.DeactivatedAt);
        Assert.Equal("Anna", loaded.DisplayName);
    }

    [Fact]
    public async Task AnEntraIdentity_CannotHaveTwoMappings_NotInTheSameTenant_NotInAnother()
    {
        var directory = Guid.NewGuid();
        var objectId = Guid.NewGuid();
        await AddAdminAsync(directory, objectId);

        // Same tenant.
        Context.TenantAdmins.Add(new TenantAdmin(await RoleAsync(Context, DefaultRoles.TenantAdmin), directory, objectId, Now));
        await Assert.ThrowsAsync<DbUpdateException>(() => Context.SaveChangesAsync());
        Context.ChangeTracker.Clear();

        // Another tenant.
        var other = await CreateTenantAsync("Other tenant");
        await using var otherContext = CreateContextFor(other);
        otherContext.TenantAdmins.Add(new TenantAdmin(await RoleAsync(otherContext, DefaultRoles.TenantAdmin), directory, objectId, Now));
        await Assert.ThrowsAsync<DbUpdateException>(() => otherContext.SaveChangesAsync());
    }

    [Fact]
    public async Task TheSameObjectId_InAnotherDirectory_IsAnotherIdentity()
    {
        var objectId = Guid.NewGuid();
        await AddAdminAsync(Guid.NewGuid(), objectId);

        var second = await AddAdminAsync(Guid.NewGuid(), objectId);

        Assert.True(second.Id > 0);
    }

    [Fact]
    public async Task ATenant_CanHaveSeveralAdministrators()
    {
        await AddAdminAsync(Guid.NewGuid(), Guid.NewGuid());
        await AddAdminAsync(Guid.NewGuid(), Guid.NewGuid());

        Assert.Equal(2, await Context.TenantAdmins.CountAsync());
    }

    [Fact]
    public async Task Database_RejectsAnInventoryRoleForAnAdministrator()
    {
        var admin = await RoleAsync(Context, DefaultRoles.TenantAdmin);
        var inventory = await RoleAsync(Context, DefaultRoles.WarehouseManager);
        var (directory, objectId) = (Guid.NewGuid(), Guid.NewGuid());

        // Control: the Admin role, with its real scope, is accepted.
        await Context.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO TenantAdmins (TenantId, EntraTenantId, EntraObjectId, RoleId, RoleScope, IsActive, CreatedAt) VALUES ({TenantId}, {directory}, {objectId}, {admin.Id}, 1, 1, {Now})");

        // Claiming the inventory role is an Admin role: the key doesn't match its real scope.
        await Assert.ThrowsAsync<SqlException>(() => Context.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO TenantAdmins (TenantId, EntraTenantId, EntraObjectId, RoleId, RoleScope, IsActive, CreatedAt) VALUES ({TenantId}, {Guid.NewGuid()}, {Guid.NewGuid()}, {inventory.Id}, 1, 1, {Now})"));

        // Admitting it is an inventory role: the key matches, but the check constraint only allows Admin.
        await Assert.ThrowsAsync<SqlException>(() => Context.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO TenantAdmins (TenantId, EntraTenantId, EntraObjectId, RoleId, RoleScope, IsActive, CreatedAt) VALUES ({TenantId}, {Guid.NewGuid()}, {Guid.NewGuid()}, {inventory.Id}, 0, 1, {Now})"));
    }

    [Fact]
    public async Task Database_RejectsARoleOfAnotherTenantForAnAdministrator()
    {
        var own = await RoleAsync(Context, DefaultRoles.TenantAdmin);
        var other = await CreateTenantAsync("Other tenant");
        await using var otherContext = CreateContextFor(other);
        var foreign = await RoleAsync(otherContext, DefaultRoles.TenantAdmin);

        // Control: an own role works.
        await Context.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO TenantAdmins (TenantId, EntraTenantId, EntraObjectId, RoleId, RoleScope, IsActive, CreatedAt) VALUES ({TenantId}, {Guid.NewGuid()}, {Guid.NewGuid()}, {own.Id}, 1, 1, {Now})");

        await Assert.ThrowsAsync<SqlException>(() => Context.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO TenantAdmins (TenantId, EntraTenantId, EntraObjectId, RoleId, RoleScope, IsActive, CreatedAt) VALUES ({TenantId}, {Guid.NewGuid()}, {Guid.NewGuid()}, {foreign.Id}, 1, 1, {Now})"));
    }

    [Fact]
    public async Task Database_RejectsAMappingForAnUnknownTenant()
    {
        await Assert.ThrowsAsync<SqlException>(() => Context.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO TenantAdmins (TenantId, EntraTenantId, EntraObjectId, RoleId, RoleScope, IsActive, CreatedAt) VALUES (999999, {Guid.NewGuid()}, {Guid.NewGuid()}, 1, 1, 1, {Now})"));
    }

    [Fact]
    public async Task ARoleThatAnAdministratorUses_CannotBeDeleted()
    {
        var role = await RoleAsync(Context, DefaultRoles.TenantAdmin);

        // Control: a role nobody uses can be deleted (the unused inventory role).
        var unused = await RoleAsync(Context, DefaultRoles.WarehouseEmployee);
        await Context.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM RolePermissions WHERE RoleId = {unused.Id}");
        await Context.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM Roles WHERE Id = {unused.Id}");

        await AddAdminAsync(Guid.NewGuid(), Guid.NewGuid());

        // The foreign key is restrictive; this is the database speaking, not the change tracker.
        await Context.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM RolePermissions WHERE RoleId = {role.Id}");
        await Assert.ThrowsAsync<SqlException>(() => Context.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM Roles WHERE Id = {role.Id}"));
    }

    [Fact]
    public async Task Mappings_AreInvisibleToOtherTenants_AndCannotBeWrittenFromThem()
    {
        var admin = await AddAdminAsync(Guid.NewGuid(), Guid.NewGuid());
        var other = await CreateTenantAsync("Other tenant");
        await using var otherContext = CreateContextFor(other);

        Assert.Empty(await otherContext.TenantAdmins.ToListAsync());

        // A mapping of the first tenant can't be smuggled in from the other tenant's context.
        var foreignRole = await RoleAsync(Context, DefaultRoles.TenantAdmin);
        otherContext.TenantAdmins.Add(new TenantAdmin(foreignRole, Guid.NewGuid(), Guid.NewGuid(), Now));
        await Assert.ThrowsAsync<InvalidOperationException>(() => otherContext.SaveChangesAsync());

        Assert.Equal(admin.Id, (await Context.TenantAdmins.SingleAsync()).Id);
    }

    [Fact]
    public async Task ThePrivilegedLookup_FindsAMappingWithoutATenant_OnlyByIgnoringTheFilter()
    {
        var (directory, objectId) = (Guid.NewGuid(), Guid.NewGuid());
        await AddAdminAsync(directory, objectId);

        await using var noTenant = CreateContextWithoutTenant();

        Assert.Empty(await noTenant.TenantAdmins.ToListAsync());
        Assert.Single(await noTenant.TenantAdmins.IgnoreQueryFilters()
            .Where(a => a.EntraTenantId == directory && a.EntraObjectId == objectId).ToListAsync());
    }
}
