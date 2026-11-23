using System.Net;
using InventoryApp.Api.Models;
using InventoryApp.Api.Security;
using Microsoft.EntityFrameworkCore;

public abstract class EntraTestBase : ApiTestBase
{
    protected EntraTestBase(DatabaseFixture databaseFixture)
        : base(databaseFixture)
    {
    }

    protected async Task<TenantAdmin> CreateTenantAdminAsync(
        Tenant? tenant = null,
        Guid? objectId = null,
        Guid? entraTenantId = null,
        string roleName = DefaultRoles.TenantAdmin,
        bool active = true)
    {
        await using var context = CreateContextFor(tenant ?? Tenant);

        var role = await context.Roles.SingleAsync(r => r.Name == roleName);
        var admin = new TenantAdmin(
            role, entraTenantId ?? EntraTestTokens.DirectoryId, objectId ?? Guid.NewGuid(), Time.GetUtcNow(), "Test administrator");

        if (!active)
            admin.Deactivate(Time.GetUtcNow());

        context.TenantAdmins.Add(admin);
        await context.SaveChangesAsync();

        return admin;
    }

    protected async Task<Role> CreateAdminRoleAsync(string name, params string[] permissions)
    {
        await using var context = CreateContextFor(Tenant);

        var role = new Role(Tenant.Id, name, PermissionScope.Admin, permissions);
        context.Roles.Add(role);
        await context.SaveChangesAsync();

        return role;
    }

    protected async Task SetAdminRolePermissionsAsync(string roleName, params string[] permissions)
    {
        await using var context = CreateContextFor(Tenant);

        var role = await context.Roles.Include(r => r.Permissions).SingleAsync(r => r.Name == roleName);
        role.SetPermissions(permissions);
        await context.SaveChangesAsync();
    }

    protected async Task SetMappingActiveAsync(TenantAdmin admin, bool active, Tenant? tenant = null)
    {
        await using var context = CreateContextFor(tenant ?? Tenant);

        var row = await context.TenantAdmins.SingleAsync(a => a.Id == admin.Id);

        if (active)
            row.Activate();
        else
            row.Deactivate(Time.GetUtcNow());

        await context.SaveChangesAsync();
    }

    protected async Task SetTenantActiveAsync(Tenant tenant, bool active)
    {
        await using var context = CreateContextWithoutTenant();

        var row = await context.Tenants.SingleAsync(t => t.Id == tenant.Id);

        if (active)
            row.Activate();
        else
            row.Deactivate();

        await context.SaveChangesAsync();
    }

    protected string TokenFor(TenantAdmin admin) =>
        EntraTestTokens.Create(Time.GetUtcNow(), objectId: admin.EntraObjectId, directoryId: admin.EntraTenantId);

    protected string PlatformAdminToken(Guid? objectId = null) =>
        EntraTestTokens.Create(Time.GetUtcNow(), objectId: objectId, roles: [EntraTestTokens.PlatformAdminRole]);

    protected async Task<HttpStatusCode> StatusAsync(string url, string? token, HttpMethod? method = null) =>
        (await SendAsync(method ?? HttpMethod.Get, url, token: token)).StatusCode;
}
