using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using InventoryApp.Api.Dtos;
using InventoryApp.Api.Models;
using InventoryApp.Api.Security;
using Microsoft.EntityFrameworkCore;

// The administration endpoints (Phase 4b), through the real pipeline: Entra tokens for Tenant Admins and Platform
// Admins, employee tokens and the device flow where the effect has to be seen from the warehouse side.
[Trait("Category", "Integration")]
[Collection("Integration Tests")]
public class AdministrationTests : EntraTestBase
{
    public AdministrationTests(DatabaseFixture databaseFixture)
        : base(databaseFixture)
    {
    }

    private async Task<string> AdminTokenAsync(Tenant? tenant = null) =>
        TokenFor(await CreateTenantAdminAsync(tenant));

    private async Task<T> ReadAsync<T>(HttpResponseMessage response, HttpStatusCode expected = HttpStatusCode.OK)
    {
        Assert.Equal(expected, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<T>(new JsonSerializerOptions(JsonSerializerDefaults.Web)))!;
    }

    private async Task AssertStatusAsync(HttpStatusCode expected, HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(expected == response.StatusCode, $"Expected {expected} but got {response.StatusCode}: {body}");
    }

    private async Task<Employee> LoadEmployeeAsync(int id, Tenant? tenant = null)
    {
        await using var context = CreateContextFor(tenant ?? Tenant);
        return await context.Employees.Include(e => e.EmployeeRoles).ThenInclude(er => er.Role).SingleAsync(e => e.Id == id);
    }

    private static string DeviceUrl(string tail = "") => $"/api/admin/devices{tail}";

    public static IEnumerable<object[]> AdminEndpoints() =>
    [
        ["GET", "/api/admin/tenant"],
        ["PUT", "/api/admin/tenant/require-pin"],
        ["GET", "/api/admin/devices"],
        ["POST", "/api/admin/devices"],
        ["POST", "/api/admin/devices/1/enrollment-code"],
        ["POST", "/api/admin/devices/1/activate"],
        ["POST", "/api/admin/devices/1/deactivate"],
        ["GET", "/api/admin/employees"],
        ["POST", "/api/admin/employees"],
        ["PUT", "/api/admin/employees/1"],
        ["PUT", "/api/admin/employees/1/roles"],
        ["PUT", "/api/admin/employees/1/pin"],
        ["POST", "/api/admin/employees/1/activate"],
        ["POST", "/api/admin/employees/1/deactivate"],
        ["GET", "/api/admin/sessions"],
        ["POST", "/api/admin/sessions/00000000-0000-0000-0000-000000000001/revoke"],
        ["GET", "/api/admin/permissions"],
        ["GET", "/api/admin/roles"],
        ["POST", "/api/admin/roles"],
        ["PUT", "/api/admin/roles/1"],
        ["DELETE", "/api/admin/roles/1"],
    ];

    public static IEnumerable<object[]> PlatformEndpoints() =>
    [
        ["POST", "/api/platform/tenants"],
        ["PUT", "/api/platform/tenants/1"],
        ["POST", "/api/platform/tenants/1/activate"],
        ["POST", "/api/platform/tenants/1/deactivate"],
        ["GET", "/api/platform/tenants/1"],
        ["PUT", "/api/platform/tenants/1/require-pin"],
        ["GET", "/api/platform/tenants/1/devices"],
        ["POST", "/api/platform/tenants/1/devices"],
        ["POST", "/api/platform/tenants/1/devices/1/enrollment-code"],
        ["POST", "/api/platform/tenants/1/devices/1/deactivate"],
        ["GET", "/api/platform/tenants/1/employees"],
        ["POST", "/api/platform/tenants/1/employees"],
        ["PUT", "/api/platform/tenants/1/employees/1/roles"],
        ["PUT", "/api/platform/tenants/1/employees/1/pin"],
        ["POST", "/api/platform/tenants/1/employees/1/deactivate"],
        ["GET", "/api/platform/tenants/1/sessions"],
        ["POST", "/api/platform/tenants/1/sessions/00000000-0000-0000-0000-000000000001/revoke"],
        ["GET", "/api/platform/tenants/1/administrators"],
        ["POST", "/api/platform/tenants/1/administrators"],
        ["POST", "/api/platform/tenants/1/administrators/1/deactivate"],
    ];

    private static object? BodyFor(string method) => method is "POST" or "PUT" ? new { } : null;

    [Theory]
    [MemberData(nameof(AdminEndpoints))]
    public async Task AdminEndpoints_RefuseEveryoneWithoutTheRightCredential(string method, string url)
    {
        var employee = await CreateEmployeeAsync("Manager", "MGR", DefaultRoles.WarehouseManager);
        var pda = await EnrollPdaAsync();
        var employeeToken = await LoginTokenAsync(pda, "MGR");

        await CreateAdminRoleAsync("Nothing");
        var nothing = TokenFor(await CreateTenantAdminAsync(roleName: "Nothing"));

        var http = new HttpMethod(method);
        var body = BodyFor(method);

        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(http, url, body)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(http, url, body, token: employeeToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(http, url, body, token: nothing)).StatusCode);
        // A Platform Admin has no tenant here: the tenant endpoints are not theirs.
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(http, url, body, token: PlatformAdminToken())).StatusCode);
        Assert.NotNull(employee);
    }

    [Theory]
    [MemberData(nameof(PlatformEndpoints))]
    public async Task PlatformEndpoints_RefuseEveryoneWithoutTheRightCredential(string method, string url)
    {
        await CreateEmployeeAsync("Manager", "MGR", DefaultRoles.WarehouseManager);
        var pda = await EnrollPdaAsync();
        var employeeToken = await LoginTokenAsync(pda, "MGR");
        var tenantAdminToken = await AdminTokenAsync();

        var http = new HttpMethod(method);
        var body = BodyFor(method);

        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(http, url, body)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(http, url, body, token: employeeToken)).StatusCode);
        // Even a full Tenant Admin (of this very tenant) is not a Platform Admin.
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(http, url, body, token: tenantAdminToken)).StatusCode);
    }

    [Fact]
    public async Task RequirePin_CannotBeSwitchedOn_WhileAnActiveEmployeeHasNoPin()
    {
        await CreateEmployeeAsync("Anna", "A-1", pin: "123456");
        await CreateEmployeeAsync("Ben", "B-1");
        var token = await AdminTokenAsync();

        var refused = await SendAsync(HttpMethod.Put, "/api/admin/tenant/require-pin", new { required = true }, token);

        await AssertStatusAsync(HttpStatusCode.Conflict, refused);
        Assert.Contains("1 active employee", await refused.Content.ReadAsStringAsync());
        await using var context = CreateContextWithoutTenant();
        Assert.False((await context.Tenants.SingleAsync(t => t.Id == TenantId)).RequirePin);
    }

    [Fact]
    public async Task RequirePin_CanBeSwitchedOn_WhenEveryActiveEmployeeHasAPin_AndOff_Again()
    {
        await CreateEmployeeAsync("Anna", "A-1", pin: "123456");
        var inactive = await CreateEmployeeAsync("Ben", "B-1");
        await using (var context = CreateContextFor(Tenant))
        {
            (await context.Employees.SingleAsync(e => e.Id == inactive.Id)).Deactivate();
            await context.SaveChangesAsync();
        }
        var token = await AdminTokenAsync();

        var on = await ReadAsync<TenantDto>(await SendAsync(HttpMethod.Put, "/api/admin/tenant/require-pin", new { required = true }, token));
        Assert.True(on.RequirePin);

        var off = await ReadAsync<TenantDto>(await SendAsync(HttpMethod.Put, "/api/admin/tenant/require-pin", new { required = false }, token));
        Assert.False(off.RequirePin);
    }

    [Fact]
    public async Task RequirePin_NeedsAnExplicitValue()
    {
        var token = await AdminTokenAsync();

        await AssertStatusAsync(HttpStatusCode.BadRequest, await SendAsync(HttpMethod.Put, "/api/admin/tenant/require-pin", new { }, token));
    }

    [Fact]
    public async Task CreateDevice_ReturnsTheEnrollmentCodeOnce_AndTheDeviceCanEnrollWithIt()
    {
        var token = await AdminTokenAsync();

        var created = await ReadAsync<DeviceEnrollmentDto>(
            await SendAsync(HttpMethod.Post, DeviceUrl(), new { name = "Dock PDA" }, token));

        Assert.Equal("Dock PDA", created.Device.Name);
        Assert.False(created.Device.IsEnrolled);
        Assert.Equal(26, created.EnrollmentCode.Length);

        var enroll = await SendAsync(HttpMethod.Post, "/api/auth/enroll", new EnrollRequest(created.EnrollmentCode));
        await AssertStatusAsync(HttpStatusCode.OK, enroll);

        // The list shows the device as enrolled and never carries a secret.
        var listResponse = await SendAsync(HttpMethod.Get, DeviceUrl(), token: token);
        var json = await listResponse.Content.ReadAsStringAsync();
        Assert.Contains("\"isEnrolled\":true", json);
        Assert.DoesNotContain(created.EnrollmentCode, json);
        Assert.DoesNotContain("hash", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(CookieValue(enroll)!, json);
    }

    [Fact]
    public async Task CreateDevice_RejectsABlankName()
    {
        var token = await AdminTokenAsync();

        await AssertStatusAsync(HttpStatusCode.BadRequest, await SendAsync(HttpMethod.Post, DeviceUrl(), new { name = "  " }, token));
    }

    [Fact]
    public async Task IssuingANewEnrollmentCode_CutsOffTheOldBrowser_AndItsSession()
    {
        await CreateEmployeeAsync("Anna", "A-1");
        var pda = await EnrollPdaAsync();
        var employeeToken = await LoginTokenAsync(pda, "A-1");
        var token = await AdminTokenAsync();

        var reissued = await ReadAsync<DeviceEnrollmentDto>(
            await SendAsync(HttpMethod.Post, DeviceUrl($"/{pda.DeviceId}/enrollment-code"), token: token));

        Assert.False(reissued.Device.IsEnrolled);
        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(pda, "A-1")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, await StatusAsync("/api/products/X", employeeToken));
    }

    [Fact]
    public async Task DeactivatingADevice_EndsItsSession_AndBlocksLogin_UntilActivated()
    {
        await CreateEmployeeAsync("Anna", "A-1");
        var pda = await EnrollPdaAsync();
        var employeeToken = await LoginTokenAsync(pda, "A-1");
        var token = await AdminTokenAsync();

        var device = await ReadAsync<DeviceDto>(await SendAsync(HttpMethod.Post, DeviceUrl($"/{pda.DeviceId}/deactivate"), token: token));
        Assert.False(device.IsActive);

        Assert.Equal(HttpStatusCode.Unauthorized, await StatusAsync("/api/products/X", employeeToken));
        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(pda, "A-1")).StatusCode);

        await ReadAsync<DeviceDto>(await SendAsync(HttpMethod.Post, DeviceUrl($"/{pda.DeviceId}/activate"), token: token));
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(pda, "A-1")).StatusCode);
    }

    [Fact]
    public async Task ADeviceOfAnotherTenant_IsNotFound_AndUntouched()
    {
        var other = await CreateTenantAsync("Other");
        var (foreign, _) = await CreateDeviceAsync("Foreign", other);
        var token = await AdminTokenAsync();

        Assert.Equal(HttpStatusCode.NotFound, await StatusAsync(DeviceUrl($"/{foreign.Id}/deactivate"), token, HttpMethod.Post));
        Assert.Equal(HttpStatusCode.NotFound, await StatusAsync(DeviceUrl($"/{foreign.Id}/enrollment-code"), token, HttpMethod.Post));

        var list = await ReadAsync<List<DeviceDto>>(await SendAsync(HttpMethod.Get, DeviceUrl(), token: token));
        Assert.Empty(list);

        await using var context = CreateContextFor(other);
        Assert.True((await context.Devices.SingleAsync()).IsActive);
    }

    [Fact]
    public async Task CreateEmployee_DefaultsToWarehouseEmployee_AndNeverReturnsTheBadge()
    {
        var token = await AdminTokenAsync();

        var response = await SendAsync(HttpMethod.Post, "/api/admin/employees", new { name = "Tom", badgeBarcode = "BADGE-SECRET-1" }, token);

        var created = await ReadAsync<EmployeeDto>(response);
        Assert.Equal(new[] { DefaultRoles.WarehouseEmployee }, created.Roles);
        Assert.False(created.HasPin);
        Assert.DoesNotContain("BADGE-SECRET-1", await response.Content.ReadAsStringAsync());
        Assert.Equal(TenantId, (await LoadEmployeeAsync(created.Id)).TenantId);
    }

    [Fact]
    public async Task CreateEmployee_WithAPin_StoresOnlyAHash_AndForcesAPinChange()
    {
        var token = await AdminTokenAsync();

        var created = await ReadAsync<EmployeeDto>(await SendAsync(HttpMethod.Post, "/api/admin/employees",
            new { name = "Tom", badgeBarcode = "T-1", role = DefaultRoles.WarehouseManager, pin = "246810" }, token));

        Assert.True(created.HasPin);
        Assert.True(created.MustChangePin);
        var row = await LoadEmployeeAsync(created.Id);
        Assert.NotEqual("246810", row.PinHash);
        Assert.True(PinHasher.Verify("246810", row.PinHash));

        // Logged in, the employee can do nothing but change the PIN.
        await RequirePinAsync();
        var pda = await EnrollPdaAsync();
        var employeeToken = await LoginTokenAsync(pda, "T-1", "246810");
        Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync("/api/products/X", employeeToken));
    }

    [Theory]
    [InlineData("12345")]
    [InlineData("1234567")]
    [InlineData("12345a")]
    public async Task CreateEmployee_RejectsAMalformedPin(string pin)
    {
        var token = await AdminTokenAsync();

        await AssertStatusAsync(HttpStatusCode.BadRequest,
            await SendAsync(HttpMethod.Post, "/api/admin/employees", new { name = "Tom", badgeBarcode = "T-1", pin }, token));
        Assert.False(await Context.Employees.AnyAsync());
    }

    [Fact]
    public async Task CreateEmployee_NeedsAPin_WhileTheTenantRequiresPins()
    {
        await RequirePinAsync();
        var token = await AdminTokenAsync();

        await AssertStatusAsync(HttpStatusCode.BadRequest,
            await SendAsync(HttpMethod.Post, "/api/admin/employees", new { name = "Tom", badgeBarcode = "T-1" }, token));
        await AssertStatusAsync(HttpStatusCode.OK,
            await SendAsync(HttpMethod.Post, "/api/admin/employees", new { name = "Tom", badgeBarcode = "T-1", pin = "123456" }, token));
    }

    [Theory]
    [InlineData("NoSuchRole")]
    [InlineData(DefaultRoles.TenantAdmin)]
    public async Task CreateEmployee_RejectsUnknownAndAdminRoles(string role)
    {
        var token = await AdminTokenAsync();

        await AssertStatusAsync(HttpStatusCode.BadRequest,
            await SendAsync(HttpMethod.Post, "/api/admin/employees", new { name = "Tom", badgeBarcode = "T-1", role }, token));
        Assert.False(await Context.Employees.AnyAsync());
    }

    [Fact]
    public async Task CreateEmployee_RefusesADuplicateBadge_ButAnotherTenantMayUseIt()
    {
        await CreateEmployeeAsync("Anna", "SAME");
        var token = await AdminTokenAsync();

        await AssertStatusAsync(HttpStatusCode.Conflict,
            await SendAsync(HttpMethod.Post, "/api/admin/employees", new { name = "Tom", badgeBarcode = "SAME" }, token));

        var other = await CreateTenantAsync("Other");
        var otherToken = await AdminTokenAsync(other);
        await AssertStatusAsync(HttpStatusCode.OK,
            await SendAsync(HttpMethod.Post, "/api/admin/employees", new { name = "Tom", badgeBarcode = "SAME" }, otherToken));
    }

    [Fact]
    public async Task CreateEmployee_IgnoresATenantIdInTheBody()
    {
        var other = await CreateTenantAsync("Other");
        var token = await AdminTokenAsync();

        var created = await ReadAsync<EmployeeDto>(await SendAsync(HttpMethod.Post, "/api/admin/employees",
            new { name = "Tom", badgeBarcode = "T-1", tenantId = other.Id }, token));

        Assert.Equal(TenantId, (await LoadEmployeeAsync(created.Id)).TenantId);
        await using var context = CreateContextFor(other);
        Assert.False(await context.Employees.AnyAsync());
    }

    [Fact]
    public async Task ListEmployees_ShowsOnlyTheOwnTenant()
    {
        await CreateEmployeeAsync("Anna", "A-1");
        var other = await CreateTenantAsync("Other");
        await CreateEmployeeAsync("Foreign", "F-1", tenant: other);
        var token = await AdminTokenAsync();

        var list = await ReadAsync<List<EmployeeDto>>(await SendAsync(HttpMethod.Get, "/api/admin/employees", token: token));

        Assert.Equal(new[] { "Anna" }, list.Select(e => e.Name));
    }

    [Fact]
    public async Task SetEmployeeRoles_ReplacesTheRoles_AndTakesEffectOnTheNextRequest()
    {
        var anna = await CreateEmployeeAsync("Anna", "A-1");
        var pda = await EnrollPdaAsync();
        var employeeToken = await LoginTokenAsync(pda, "A-1");
        Assert.Equal(HttpStatusCode.NotFound, await StatusAsync("/api/products/NOPE", employeeToken));
        var token = await AdminTokenAsync();

        // Products.Read is in both default roles, so use a role without it.
        await using (var context = CreateContextFor(Tenant))
        {
            context.Roles.Add(new Role(TenantId, "Nothing", PermissionScope.Inventory, []));
            await context.SaveChangesAsync();
        }

        var updated = await ReadAsync<EmployeeDto>(await SendAsync(HttpMethod.Put, $"/api/admin/employees/{anna.Id}/roles",
            new { roles = new[] { "Nothing" } }, token));

        Assert.Equal(new[] { "Nothing" }, updated.Roles);
        Assert.Equal(new[] { "Nothing" }, (await LoadEmployeeAsync(anna.Id)).EmployeeRoles.Select(er => er.Role!.Name));
        Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync("/api/products/NOPE", employeeToken));
    }

    public static IEnumerable<object[]> BadRoleSets() =>
    [
        [Array.Empty<string>()],
        [new[] { "NoSuchRole" }],
        [new[] { DefaultRoles.TenantAdmin }],
    ];

    [Theory]
    [MemberData(nameof(BadRoleSets))]
    public async Task SetEmployeeRoles_RejectsEmptyUnknownAndAdminRoles_AndChangesNothing(string[] roles)
    {
        var anna = await CreateEmployeeAsync("Anna", "A-1");
        var token = await AdminTokenAsync();

        await AssertStatusAsync(HttpStatusCode.BadRequest,
            await SendAsync(HttpMethod.Put, $"/api/admin/employees/{anna.Id}/roles", new { roles }, token));

        Assert.Equal(new[] { DefaultRoles.WarehouseEmployee }, (await LoadEmployeeAsync(anna.Id)).EmployeeRoles.Select(er => er.Role!.Name));
    }

    [Fact]
    public async Task SetEmployeePin_ForcesAChange_ClearsALockout_AndEndsTheEmployeesSessions()
    {
        var anna = await CreateEmployeeAsync("Anna", "A-1", pin: "111111");
        await RequirePinAsync();
        var pda = await EnrollPdaAsync();
        var employeeToken = await LoginTokenAsync(pda, "A-1", "111111");
        await using (var context = CreateContextFor(Tenant))
        {
            var row = await context.Employees.SingleAsync(e => e.Id == anna.Id);
            for (var i = 0; i < Employee.MaxFailedPinAttempts; i++)
                row.RegisterFailedPin(Time.GetUtcNow());
            await context.SaveChangesAsync();
        }
        var token = await AdminTokenAsync();

        await AssertStatusAsync(HttpStatusCode.NoContent,
            await SendAsync(HttpMethod.Put, $"/api/admin/employees/{anna.Id}/pin", new { pin = "654321" }, token));

        var row2 = await LoadEmployeeAsync(anna.Id);
        Assert.True(row2.MustChangePin);
        Assert.False(row2.IsLockedOut(Time.GetUtcNow()));
        Assert.True(PinHasher.Verify("654321", row2.PinHash));
        Assert.Equal(HttpStatusCode.Unauthorized, await StatusAsync("/api/products/X", employeeToken));
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(pda, "A-1", "654321")).StatusCode);
    }

    [Fact]
    public async Task SetEmployeePin_RejectsAMalformedPin_AndKeepsTheOldOne()
    {
        var anna = await CreateEmployeeAsync("Anna", "A-1", pin: "111111");
        var token = await AdminTokenAsync();

        await AssertStatusAsync(HttpStatusCode.BadRequest,
            await SendAsync(HttpMethod.Put, $"/api/admin/employees/{anna.Id}/pin", new { pin = "abc" }, token));

        Assert.True(PinHasher.Verify("111111", (await LoadEmployeeAsync(anna.Id)).PinHash));
    }

    [Fact]
    public async Task DeactivatingAnEmployee_EndsTheSession_AndBlocksLogin_UntilActivated()
    {
        var anna = await CreateEmployeeAsync("Anna", "A-1");
        var pda = await EnrollPdaAsync();
        var employeeToken = await LoginTokenAsync(pda, "A-1");
        var token = await AdminTokenAsync();

        var off = await ReadAsync<EmployeeDto>(await SendAsync(HttpMethod.Post, $"/api/admin/employees/{anna.Id}/deactivate", token: token));
        Assert.False(off.IsActive);
        Assert.Equal(HttpStatusCode.Unauthorized, await StatusAsync("/api/products/X", employeeToken));
        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(pda, "A-1")).StatusCode);

        await ReadAsync<EmployeeDto>(await SendAsync(HttpMethod.Post, $"/api/admin/employees/{anna.Id}/activate", token: token));
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(pda, "A-1")).StatusCode);
    }

    [Fact]
    public async Task ActivatingAnEmployeeWithoutAPin_IsRefused_WhileTheTenantRequiresPins()
    {
        var anna = await CreateEmployeeAsync("Anna", "A-1");
        await using (var context = CreateContextFor(Tenant))
        {
            (await context.Employees.SingleAsync(e => e.Id == anna.Id)).Deactivate();
            await context.SaveChangesAsync();
        }
        await RequirePinAsync();
        var token = await AdminTokenAsync();

        await AssertStatusAsync(HttpStatusCode.Conflict,
            await SendAsync(HttpMethod.Post, $"/api/admin/employees/{anna.Id}/activate", token: token));
        Assert.False((await LoadEmployeeAsync(anna.Id)).IsActive);
    }

    [Fact]
    public async Task RenameEmployee_Works_AndRejectsABlankName()
    {
        var anna = await CreateEmployeeAsync("Anna", "A-1");
        var token = await AdminTokenAsync();

        var renamed = await ReadAsync<EmployeeDto>(await SendAsync(HttpMethod.Put, $"/api/admin/employees/{anna.Id}", new { name = " Anna B. " }, token));
        Assert.Equal("Anna B.", renamed.Name);

        await AssertStatusAsync(HttpStatusCode.BadRequest,
            await SendAsync(HttpMethod.Put, $"/api/admin/employees/{anna.Id}", new { name = " " }, token));
    }

    [Fact]
    public async Task AnEmployeeOfAnotherTenant_IsNotFound_ForEveryEmployeeAction_AndUntouched()
    {
        var other = await CreateTenantAsync("Other");
        var foreign = await CreateEmployeeAsync("Foreign", "F-1", pin: "111111", tenant: other);
        var token = await AdminTokenAsync();
        var id = foreign.Id;

        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(HttpMethod.Put, $"/api/admin/employees/{id}", new { name = "X" }, token)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(HttpMethod.Put, $"/api/admin/employees/{id}/roles", new { roles = new[] { DefaultRoles.WarehouseManager } }, token)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(HttpMethod.Put, $"/api/admin/employees/{id}/pin", new { pin = "999999" }, token)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(HttpMethod.Post, $"/api/admin/employees/{id}/deactivate", token: token)).StatusCode);

        var row = await LoadEmployeeAsync(id, other);
        Assert.Equal("Foreign", row.Name);
        Assert.True(row.IsActive);
        Assert.True(PinHasher.Verify("111111", row.PinHash));
        Assert.Equal(new[] { DefaultRoles.WarehouseEmployee }, row.EmployeeRoles.Select(er => er.Role!.Name));
    }

    [Fact]
    public async Task ListRoles_ShowsTheDefaultRoles_WithTheirScope()
    {
        var token = await AdminTokenAsync();

        var roles = await ReadAsync<List<RoleDto>>(await SendAsync(HttpMethod.Get, "/api/admin/roles", token: token));

        Assert.Equal(new[] { DefaultRoles.TenantAdmin, DefaultRoles.WarehouseEmployee, DefaultRoles.WarehouseManager }, roles.Select(r => r.Name));
        var admin = roles.Single(r => r.Name == DefaultRoles.TenantAdmin);
        Assert.Equal("Admin", admin.Scope);
        Assert.True(admin.IsSystemRole);
    }

    [Fact]
    public async Task PermissionCatalog_ListsBothScopes()
    {
        var token = await AdminTokenAsync();

        var catalog = await ReadAsync<PermissionCatalogDto>(await SendAsync(HttpMethod.Get, "/api/admin/permissions", token: token));

        Assert.Equal(Permissions.Of(PermissionScope.Inventory).Order(), catalog.Inventory);
        Assert.Equal(Permissions.Of(PermissionScope.Admin).Order(), catalog.Admin);
    }

    [Fact]
    public async Task CreateRole_MakesAnInventoryRole_ThatCanBeAssigned()
    {
        var anna = await CreateEmployeeAsync("Anna", "A-1");
        var token = await AdminTokenAsync();

        var role = await ReadAsync<RoleDto>(await SendAsync(HttpMethod.Post, "/api/admin/roles",
            new { name = "Reader", permissions = new[] { Permissions.ProductsRead } }, token));

        Assert.Equal("Inventory", role.Scope);
        Assert.False(role.IsSystemRole);
        await AssertStatusAsync(HttpStatusCode.OK, await SendAsync(HttpMethod.Put, $"/api/admin/employees/{anna.Id}/roles", new { roles = new[] { "Reader" } }, token));
    }

    [Theory]
    [InlineData(Permissions.EmployeesManage)]
    [InlineData("Nope.Nothing")]
    public async Task CreateRole_RejectsAdminAndUnknownPermissions(string permission)
    {
        var token = await AdminTokenAsync();

        await AssertStatusAsync(HttpStatusCode.BadRequest,
            await SendAsync(HttpMethod.Post, "/api/admin/roles", new { name = "Bad", permissions = new[] { permission } }, token));
        Assert.False(await Context.Roles.AnyAsync(r => r.Name == "Bad"));
    }

    [Fact]
    public async Task CreateRole_RefusesADuplicateName()
    {
        var token = await AdminTokenAsync();

        await AssertStatusAsync(HttpStatusCode.Conflict,
            await SendAsync(HttpMethod.Post, "/api/admin/roles", new { name = DefaultRoles.WarehouseManager, permissions = Array.Empty<string>() }, token));
    }

    [Fact]
    public async Task UpdateRole_ChangesNameAndPermissions_AndAppliesToEmployeesAtOnce()
    {
        var anna = await CreateEmployeeAsync("Anna", "A-1");
        var pda = await EnrollPdaAsync();
        var employeeToken = await LoginTokenAsync(pda, "A-1");
        var token = await AdminTokenAsync();
        var roleId = (await Context.Roles.SingleAsync(r => r.Name == DefaultRoles.WarehouseEmployee)).Id;

        var updated = await ReadAsync<RoleDto>(await SendAsync(HttpMethod.Put, $"/api/admin/roles/{roleId}",
            new { name = "Stock only", permissions = new[] { Permissions.StockRead } }, token));

        Assert.Equal("Stock only", updated.Name);
        Assert.Equal(new[] { Permissions.StockRead }, updated.Permissions);
        Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync("/api/products/X", employeeToken));
        Assert.NotNull(anna);
    }

    [Fact]
    public async Task UpdateRole_RefusesSystemRoles_AdminRoles_AndDuplicateNames()
    {
        await CreateAdminRoleAsync("CustomAdmin", Permissions.RolesManage);
        var token = await AdminTokenAsync();
        var roles = await Context.Roles.ToListAsync();
        int Id(string name) => roles.Single(r => r.Name == name).Id;

        var body = new { name = "Whatever", permissions = Array.Empty<string>() };

        Assert.Equal(HttpStatusCode.Conflict, (await SendAsync(HttpMethod.Put, $"/api/admin/roles/{Id(DefaultRoles.TenantAdmin)}", body, token)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await SendAsync(HttpMethod.Put, $"/api/admin/roles/{Id("CustomAdmin")}", body, token)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await SendAsync(HttpMethod.Put, $"/api/admin/roles/{Id(DefaultRoles.WarehouseEmployee)}",
            new { name = DefaultRoles.WarehouseManager, permissions = Array.Empty<string>() }, token)).StatusCode);

        Assert.Equal(Permissions.Of(PermissionScope.Admin).Order(),
            (await Context.Roles.Include(r => r.Permissions).SingleAsync(r => r.Name == DefaultRoles.TenantAdmin)).Permissions.Select(p => p.Permission).Order());
    }

    [Fact]
    public async Task AnAdministratorWithRolesManage_CannotGrantThemselvesMoreAdminPermissions()
    {
        // The escalation the API closes: a partial admin editing their own (admin) role.
        var role = await CreateAdminRoleAsync("RolesOnly", Permissions.RolesManage);
        var token = TokenFor(await CreateTenantAdminAsync(roleName: "RolesOnly"));

        await AssertStatusAsync(HttpStatusCode.Conflict, await SendAsync(HttpMethod.Put, $"/api/admin/roles/{role.Id}",
            new { name = "RolesOnly", permissions = Permissions.Of(PermissionScope.Inventory) }, token));
        await AssertStatusAsync(HttpStatusCode.BadRequest, await SendAsync(HttpMethod.Post, "/api/admin/roles",
            new { name = "Sneaky", permissions = Permissions.Of(PermissionScope.Admin) }, token));

        await using var context = CreateContextFor(Tenant);
        Assert.Equal(new[] { Permissions.RolesManage },
            (await context.Roles.Include(r => r.Permissions).SingleAsync(r => r.Id == role.Id)).Permissions.Select(p => p.Permission));
    }

    [Fact]
    public async Task DeleteRole_IsRefusedWhileInUse_AndForSystemRoles_AndWorksOtherwise()
    {
        await CreateEmployeeAsync("Anna", "A-1");
        var token = await AdminTokenAsync();
        var roles = await Context.Roles.ToListAsync();
        int Id(string name) => roles.Single(r => r.Name == name).Id;

        Assert.Equal(HttpStatusCode.Conflict, await StatusAsync($"/api/admin/roles/{Id(DefaultRoles.WarehouseEmployee)}", token, HttpMethod.Delete));
        Assert.Equal(HttpStatusCode.Conflict, await StatusAsync($"/api/admin/roles/{Id(DefaultRoles.TenantAdmin)}", token, HttpMethod.Delete));
        Assert.Equal(HttpStatusCode.NoContent, await StatusAsync($"/api/admin/roles/{Id(DefaultRoles.WarehouseManager)}", token, HttpMethod.Delete));
        Assert.Equal(HttpStatusCode.NotFound, await StatusAsync($"/api/admin/roles/{Id(DefaultRoles.WarehouseManager)}", token, HttpMethod.Delete));
        Assert.False(await Context.Roles.AnyAsync(r => r.Name == DefaultRoles.WarehouseManager));
    }

    [Fact]
    public async Task ARoleOfAnotherTenant_IsNotFound_AndUntouched()
    {
        var other = await CreateTenantAsync("Other");
        int foreignId;
        await using (var context = CreateContextFor(other))
            foreignId = (await context.Roles.SingleAsync(r => r.Name == DefaultRoles.WarehouseManager)).Id;
        var token = await AdminTokenAsync();

        Assert.Equal(HttpStatusCode.NotFound, await StatusAsync($"/api/admin/roles/{foreignId}", token, HttpMethod.Delete));
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(HttpMethod.Put, $"/api/admin/roles/{foreignId}", new { name = "X", permissions = Array.Empty<string>() }, token)).StatusCode);

        await using var check = CreateContextFor(other);
        Assert.True(await check.Roles.AnyAsync(r => r.Id == foreignId));
    }

    [Fact]
    public async Task Sessions_AreListedForTheOwnTenantOnly_AndCanBeRevoked()
    {
        await CreateEmployeeAsync("Anna", "A-1");
        var pda = await EnrollPdaAsync("Dock");
        var employeeToken = await LoginTokenAsync(pda, "A-1");

        var other = await CreateTenantAsync("Other");
        await CreateEmployeeAsync("Foreign", "F-1", tenant: other);
        var foreignPda = await EnrollPdaAsync("Foreign PDA", other);
        await LoginTokenAsync(foreignPda, "F-1");

        var token = await AdminTokenAsync();

        var sessions = await ReadAsync<List<SessionDto>>(await SendAsync(HttpMethod.Get, "/api/admin/sessions", token: token));
        var session = Assert.Single(sessions);
        Assert.Equal("Anna", session.EmployeeName);
        Assert.Equal("Dock", session.DeviceName);
        Assert.True(session.IsActive);

        await AssertStatusAsync(HttpStatusCode.NoContent, await SendAsync(HttpMethod.Post, $"/api/admin/sessions/{session.Id}/revoke", token: token));
        Assert.Equal(HttpStatusCode.Unauthorized, await StatusAsync("/api/products/X", employeeToken));
        await AssertStatusAsync(HttpStatusCode.NoContent, await SendAsync(HttpMethod.Post, $"/api/admin/sessions/{session.Id}/revoke", token: token));

        Assert.Empty(await ReadAsync<List<SessionDto>>(await SendAsync(HttpMethod.Get, "/api/admin/sessions", token: token)));
        var all = await ReadAsync<List<SessionDto>>(await SendAsync(HttpMethod.Get, "/api/admin/sessions?includeInactive=true", token: token));
        Assert.Equal("revoked by administrator", Assert.Single(all).RevokedReason);
    }

    [Fact]
    public async Task ASessionOfAnotherTenant_IsNotFound_AndStaysValid()
    {
        var other = await CreateTenantAsync("Other");
        await CreateEmployeeAsync("Foreign", "F-1", tenant: other);
        var foreignPda = await EnrollPdaAsync("Foreign PDA", other);
        var foreignToken = await LoginTokenAsync(foreignPda, "F-1");
        Guid foreignSession;
        await using (var context = CreateContextFor(other))
            foreignSession = (await context.EmployeeSessions.SingleAsync()).Id;
        var token = await AdminTokenAsync();

        Assert.Equal(HttpStatusCode.NotFound, await StatusAsync($"/api/admin/sessions/{foreignSession}/revoke", token, HttpMethod.Post));
        Assert.Equal(HttpStatusCode.NotFound, await StatusAsync("/api/products/X", foreignToken)); // authenticated: 404 is "no such product"
    }

    [Fact]
    public async Task IdleSessions_AreNotListedAsActive()
    {
        await CreateEmployeeAsync("Anna", "A-1");
        var pda = await EnrollPdaAsync();
        await LoginTokenAsync(pda, "A-1");
        var token = await AdminTokenAsync();
        Time.Advance(TimeSpan.FromMinutes(45));

        Assert.Empty(await ReadAsync<List<SessionDto>>(await SendAsync(HttpMethod.Get, "/api/admin/sessions", token: token)));
        var all = await ReadAsync<List<SessionDto>>(await SendAsync(HttpMethod.Get, "/api/admin/sessions?includeInactive=true", token: token));
        Assert.False(Assert.Single(all).IsActive);
    }

    [Fact]
    public async Task CreateTenant_SeedsTheThreeDefaultRoles_AndRefusesADuplicateName()
    {
        var token = PlatformAdminToken();

        var created = await ReadAsync<TenantDto>(await SendAsync(HttpMethod.Post, "/api/platform/tenants", new { name = "  Acme  " }, token));

        Assert.Equal("Acme", created.Name);
        Assert.True(created.IsActive);
        Assert.False(created.RequirePin);
        await using var noTenant = CreateContextWithoutTenant();
        await using var context = CreateContextFor(await noTenant.Tenants.SingleAsync(t => t.Id == created.Id));
        Assert.Equal(
            new[] { DefaultRoles.TenantAdmin, DefaultRoles.WarehouseEmployee, DefaultRoles.WarehouseManager },
            (await context.Roles.OrderBy(r => r.Name).ToListAsync()).Select(r => r.Name));

        await AssertStatusAsync(HttpStatusCode.Conflict, await SendAsync(HttpMethod.Post, "/api/platform/tenants", new { name = "Acme" }, token));
        await AssertStatusAsync(HttpStatusCode.BadRequest, await SendAsync(HttpMethod.Post, "/api/platform/tenants", new { name = " " }, token));
    }

    [Fact]
    public async Task RenameTenant_Works_AndRefusesAnExistingName()
    {
        var other = await CreateTenantAsync("Other");
        var token = PlatformAdminToken();

        var renamed = await ReadAsync<TenantDto>(await SendAsync(HttpMethod.Put, $"/api/platform/tenants/{other.Id}", new { name = "Renamed" }, token));
        Assert.Equal("Renamed", renamed.Name);

        await AssertStatusAsync(HttpStatusCode.Conflict, await SendAsync(HttpMethod.Put, $"/api/platform/tenants/{other.Id}", new { name = Tenant.Name }, token));
        await AssertStatusAsync(HttpStatusCode.NotFound, await SendAsync(HttpMethod.Put, "/api/platform/tenants/999999", new { name = "X" }, token));
    }

    [Fact]
    public async Task DeactivatingATenant_StopsItsEmployees_AndItsAdministrators_UntilActivated()
    {
        await CreateEmployeeAsync("Anna", "A-1");
        var pda = await EnrollPdaAsync();
        var employeeToken = await LoginTokenAsync(pda, "A-1");
        var tenantAdminToken = await AdminTokenAsync();
        var token = PlatformAdminToken();

        var off = await ReadAsync<TenantDto>(await SendAsync(HttpMethod.Post, $"/api/platform/tenants/{TenantId}/deactivate", token: token));
        Assert.False(off.IsActive);

        Assert.Equal(HttpStatusCode.Unauthorized, await StatusAsync("/api/products/X", employeeToken));
        Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync("/api/admin/tenant", tenantAdminToken));

        await ReadAsync<TenantDto>(await SendAsync(HttpMethod.Post, $"/api/platform/tenants/{TenantId}/activate", token: token));
        Assert.Equal(HttpStatusCode.OK, await StatusAsync("/api/admin/tenant", tenantAdminToken));
    }

    [Fact]
    public async Task APlatformAdmin_WorksInTheTenantOfTheRoute_AndOnlyThere()
    {
        await CreateEmployeeAsync("Anna", "A-1");
        var other = await CreateTenantAsync("Other");
        await CreateEmployeeAsync("Foreign", "F-1", tenant: other);
        var token = PlatformAdminToken();

        var mine = await ReadAsync<List<EmployeeDto>>(await SendAsync(HttpMethod.Get, $"/api/platform/tenants/{TenantId}/employees", token: token));
        var theirs = await ReadAsync<List<EmployeeDto>>(await SendAsync(HttpMethod.Get, $"/api/platform/tenants/{other.Id}/employees", token: token));

        Assert.Equal(new[] { "Anna" }, mine.Select(e => e.Name));
        Assert.Equal(new[] { "Foreign" }, theirs.Select(e => e.Name));

        // An employee id of the other tenant is not reachable through this tenant's route.
        var foreign = theirs.Single();
        Assert.Equal(HttpStatusCode.NotFound,
            (await SendAsync(HttpMethod.Post, $"/api/platform/tenants/{TenantId}/employees/{foreign.Id}/deactivate", token: token)).StatusCode);
        Assert.True((await LoadEmployeeAsync(foreign.Id, other)).IsActive);
    }

    [Fact]
    public async Task APlatformAdmin_CanCreateAnEmployee_InTheTenantOfTheRoute_NotOfTheBody()
    {
        var other = await CreateTenantAsync("Other");
        var token = PlatformAdminToken();

        var created = await ReadAsync<EmployeeDto>(await SendAsync(HttpMethod.Post, $"/api/platform/tenants/{other.Id}/employees",
            new { name = "Tom", badgeBarcode = "T-1", tenantId = TenantId }, token));

        Assert.Equal(other.Id, (await LoadEmployeeAsync(created.Id, other)).TenantId);
        Assert.False(await Context.Employees.AnyAsync());
    }

    [Fact]
    public async Task APlatformAdmin_CanManageDevicesPinsAndSessions_InsideATenant()
    {
        var other = await CreateTenantAsync("Other");
        var anna = await CreateEmployeeAsync("Anna", "A-1", tenant: other);
        var pda = await EnrollPdaAsync("Dock", other);
        var employeeToken = await LoginTokenAsync(pda, "A-1");
        var token = PlatformAdminToken();
        var root = $"/api/platform/tenants/{other.Id}";

        var devices = await ReadAsync<List<DeviceDto>>(await SendAsync(HttpMethod.Get, $"{root}/devices", token: token));
        Assert.Equal("Dock", Assert.Single(devices).Name);

        var sessions = await ReadAsync<List<SessionDto>>(await SendAsync(HttpMethod.Get, $"{root}/sessions", token: token));
        await AssertStatusAsync(HttpStatusCode.NoContent, await SendAsync(HttpMethod.Post, $"{root}/sessions/{Assert.Single(sessions).Id}/revoke", token: token));
        Assert.Equal(HttpStatusCode.Unauthorized, await StatusAsync("/api/products/X", employeeToken));

        await AssertStatusAsync(HttpStatusCode.NoContent, await SendAsync(HttpMethod.Put, $"{root}/employees/{anna.Id}/pin", new { pin = "135790" }, token));
        Assert.True((await LoadEmployeeAsync(anna.Id, other)).MustChangePin);

        await AssertStatusAsync(HttpStatusCode.OK, await SendAsync(HttpMethod.Post, $"{root}/devices", new { name = "New PDA" }, token));
        await AssertStatusAsync(HttpStatusCode.OK, await SendAsync(HttpMethod.Put, $"{root}/employees/{anna.Id}/roles", new { roles = new[] { DefaultRoles.WarehouseManager } }, token));
        await AssertStatusAsync(HttpStatusCode.OK, await SendAsync(HttpMethod.Put, $"{root}/require-pin", new { required = true }, token));
    }

    [Fact]
    public async Task APlatformAdmin_CannotReachRoleDefinitions_NorInventory()
    {
        var token = PlatformAdminToken();

        // No role-definition endpoint exists under the platform routes (A2). An unmatched route is answered by the
        // fallback policy (fail closed): 401, the same as for anyone, not even a 404 that would confirm a route.
        Assert.Equal(HttpStatusCode.Unauthorized, await StatusAsync($"/api/platform/tenants/{TenantId}/roles", token));
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(HttpMethod.Put, $"/api/platform/tenants/{TenantId}/roles/1", new { }, token)).StatusCode);
        // ... and the tenant admin ones refuse a Platform Admin (no tenant, no admin permission).
        Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync("/api/admin/roles", token));
        // Nor does any platform route expose products or stock (D8).
        Assert.Equal(HttpStatusCode.Unauthorized, await StatusAsync($"/api/platform/tenants/{TenantId}/products", token));
        Assert.Equal(HttpStatusCode.Unauthorized, await StatusAsync("/api/products/ANY", token));
    }

    [Fact]
    public async Task AnUnknownTenantInTheRoute_IsNotFound()
    {
        var token = PlatformAdminToken();

        Assert.Equal(HttpStatusCode.NotFound, await StatusAsync("/api/platform/tenants/999999/employees", token));
        Assert.Equal(HttpStatusCode.Unauthorized, await StatusAsync("/api/platform/tenants/abc/employees", token));
    }

    [Fact]
    public async Task TheRoutesTenant_IsOnlyBoundAfterTheCallerWasAuthorized()
    {
        // A Tenant Admin of this tenant uses the platform route of another: refused by the policy (403), and
        // so is a caller without the platform permission. Nothing of the other tenant is read or written.
        var other = await CreateTenantAsync("Other");
        await CreateEmployeeAsync("Foreign", "F-1", tenant: other);
        var tenantAdminToken = await AdminTokenAsync();

        var response = await SendAsync(HttpMethod.Get, $"/api/platform/tenants/{other.Id}/employees", token: tenantAdminToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.DoesNotContain("Foreign", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task AnIdentityThatIsBothTenantAndPlatformAdmin_CannotLeaveItsTenant_ButWorksInIt()
    {
        var other = await CreateTenantAsync("Other");
        var admin = await CreateTenantAdminAsync();
        var token = EntraTestTokens.Create(Time.GetUtcNow(), objectId: admin.EntraObjectId, directoryId: admin.EntraTenantId,
            roles: [EntraTestTokens.PlatformAdminRole]);

        Assert.Equal(HttpStatusCode.OK, await StatusAsync($"/api/platform/tenants/{TenantId}/employees", token));
        Assert.Equal(HttpStatusCode.Conflict, await StatusAsync($"/api/platform/tenants/{other.Id}/employees", token));
        Assert.Equal(HttpStatusCode.Conflict, (await SendAsync(HttpMethod.Post, "/api/platform/tenants", new { name = "New" }, token)).StatusCode);
        Assert.False(await Context.Tenants.AnyAsync(t => t.Name == "New"));
    }

    [Fact]
    public async Task AddingAnAdministrator_LetsThatIdentityAdministerTheTenant_UntilDeactivated()
    {
        var token = PlatformAdminToken();
        var objectId = Guid.NewGuid();
        var identityToken = EntraTestTokens.Create(Time.GetUtcNow(), objectId: objectId);

        // Signed in to Entra, but no administrator yet.
        Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync("/api/admin/me", identityToken));

        var created = await ReadAsync<TenantAdminDto>(await SendAsync(HttpMethod.Post, $"/api/platform/tenants/{TenantId}/administrators",
            new { objectId, displayName = "Anna, IT" }, token));
        Assert.Equal(DefaultRoles.TenantAdmin, created.RoleName);
        Assert.Equal(HttpStatusCode.OK, await StatusAsync("/api/admin/me", identityToken));

        var listed = await ReadAsync<List<TenantAdminDto>>(await SendAsync(HttpMethod.Get, $"/api/platform/tenants/{TenantId}/administrators", token: token));
        Assert.Contains(listed, a => a.EntraObjectId == objectId);

        await AssertStatusAsync(HttpStatusCode.OK, await SendAsync(HttpMethod.Post, $"/api/platform/tenants/{TenantId}/administrators/{created.Id}/deactivate", token: token));
        Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync("/api/admin/me", identityToken));

        await AssertStatusAsync(HttpStatusCode.OK, await SendAsync(HttpMethod.Post, $"/api/platform/tenants/{TenantId}/administrators/{created.Id}/activate", token: token));
        Assert.Equal(HttpStatusCode.OK, await StatusAsync("/api/admin/me", identityToken));
    }

    [Fact]
    public async Task AnIdentity_CanAdministerOnlyOneTenant()
    {
        var other = await CreateTenantAsync("Other");
        var existing = await CreateTenantAdminAsync(other);
        var token = PlatformAdminToken();

        await AssertStatusAsync(HttpStatusCode.Conflict, await SendAsync(HttpMethod.Post, $"/api/platform/tenants/{TenantId}/administrators",
            new { objectId = existing.EntraObjectId }, token));
        await AssertStatusAsync(HttpStatusCode.BadRequest, await SendAsync(HttpMethod.Post, $"/api/platform/tenants/{TenantId}/administrators",
            new { objectId = Guid.Empty }, token));
        await AssertStatusAsync(HttpStatusCode.BadRequest, await SendAsync(HttpMethod.Post, $"/api/platform/tenants/{TenantId}/administrators", new { }, token));
    }

    [Fact]
    public async Task AnAdministratorOfAnotherTenant_IsNotFound_ThroughThisTenantsRoute()
    {
        var other = await CreateTenantAsync("Other");
        var foreign = await CreateTenantAdminAsync(other);
        var token = PlatformAdminToken();

        Assert.Equal(HttpStatusCode.NotFound, await StatusAsync($"/api/platform/tenants/{TenantId}/administrators/{foreign.Id}/deactivate", token, HttpMethod.Post));
        await using var context = CreateContextFor(other);
        Assert.True((await context.TenantAdmins.SingleAsync()).IsActive);
    }
}
