using System.Net;
using System.Net.Http.Json;
using InventoryApp.Api.Dtos;
using InventoryApp.Api.Models;
using InventoryApp.Api.Security;
using InventoryApp.Tests;
using Microsoft.EntityFrameworkCore;

// What an administrator may do comes from the database on every request: the permissions of the mapped role for a
// tenant administrator, the fixed platform permissions for a platform administrator. And an administrator request
// never reaches inventory data, however careless the endpoint is (decision D8).
[Trait("Category", "Integration")]
[Collection("Integration Tests")]
public class AdminAuthorizationTests : EntraTestBase
{
    private const string AdminMe = "/api/admin/me";
    private const string AdminTenant = "/api/admin/tenant";

    public AdminAuthorizationTests(DatabaseFixture databaseFixture)
        : base(databaseFixture)
    {
    }

    // ---- Permissions of a tenant administrator ---------------------------------------

    [Fact]
    public async Task WithoutThePermission_AnAdministratorGets403_ButIsStillSignedIn()
    {
        await CreateAdminRoleAsync("EmployeesOnly", Permissions.EmployeesManage);
        var admin = await CreateTenantAdminAsync(roleName: "EmployeesOnly");
        var token = TokenFor(admin);

        Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync(AdminTenant, token)); // needs Tenant.Settings
        Assert.Equal(HttpStatusCode.OK, await StatusAsync(AdminMe, token));            // any administrator
    }

    [Fact]
    public async Task RemovingAPermission_TakesEffectOnTheNextRequest_WithTheSameToken()
    {
        await CreateAdminRoleAsync("Custom", Permissions.TenantSettings, Permissions.EmployeesManage);
        var token = TokenFor(await CreateTenantAdminAsync(roleName: "Custom"));
        Assert.Equal(HttpStatusCode.OK, await StatusAsync(AdminTenant, token));

        await SetAdminRolePermissionsAsync("Custom", Permissions.EmployeesManage);

        Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync(AdminTenant, token));
        var profile = await (await GetAsync(AdminMe, token)).Content.ReadFromJsonAsync<AdminProfileDto>();
        Assert.Equal(new[] { Permissions.EmployeesManage }, profile!.Permissions);
    }

    [Fact]
    public async Task GrantingAPermission_TakesEffectOnTheNextRequest_WithTheSameToken()
    {
        await CreateAdminRoleAsync("Custom", Permissions.EmployeesManage);
        var token = TokenFor(await CreateTenantAdminAsync(roleName: "Custom"));
        Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync(AdminTenant, token));

        await SetAdminRolePermissionsAsync("Custom", Permissions.EmployeesManage, Permissions.TenantSettings);

        Assert.Equal(HttpStatusCode.OK, await StatusAsync(AdminTenant, token));
    }

    [Fact]
    public async Task ARoleWithoutPermissions_IsAnAdministratorWhoCanDoNothing()
    {
        await CreateAdminRoleAsync("Nothing");
        var token = TokenFor(await CreateTenantAdminAsync(roleName: "Nothing"));

        Assert.Equal(HttpStatusCode.OK, await StatusAsync(AdminMe, token));
        Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync(AdminTenant, token));
    }

    [Fact]
    public async Task TheTenantAdminRole_HoldsAllAdminPermissions_AndNoInventoryOnes()
    {
        var admin = await CreateTenantAdminAsync();

        var profile = await (await GetAsync(AdminMe, TokenFor(admin))).Content.ReadFromJsonAsync<AdminProfileDto>();

        Assert.Equal(Permissions.Of(PermissionScope.Admin).Order(), profile!.Permissions);
        Assert.Empty(profile.Permissions.Intersect(Permissions.Of(PermissionScope.Inventory)));
    }

    // ---- Decision D8: administrators never reach inventory data ----------------------

    private async Task<(Employee Employee, Product Product)> SeedInventoryAsync()
    {
        var employee = await CreateEmployeeAsync("Anna", "BADGE-1");

        var location = new StorageLocation { TenantId = TenantId, LocationCode = "A-01", Zone = "A", Barcode = "LOC-1" };
        Context.StorageLocations.Add(location);
        var product = new Product(TenantId, "10000001", "Widget", employee.Id);
        Context.Products.Add(product);
        await Context.SaveChangesAsync();
        Context.ProductStocks.Add(new ProductStock(TenantId, product.InternalBarcode, location.Id));
        await Context.SaveChangesAsync();

        return (employee, product);
    }

    [Fact]
    public async Task ATenantAdministrator_CannotReadInventory_EvenThroughACarelessEndpoint()
    {
        await SeedInventoryAsync();
        var token = TokenFor(await CreateTenantAdminAsync());

        // Positive control: the data is there, for a context that is allowed to see it.
        Assert.Equal(1, await Context.Products.CountAsync());
        Assert.Equal(1, await Context.ProductStocks.CountAsync());
        Assert.Equal(1, await Context.StorageLocations.CountAsync());

        var counts = await (await GetAsync("/test-probe/counts", token)).Content.ReadFromJsonAsync<InventoryProbeController.Counts>();

        Assert.Equal(0, counts!.Products);
        Assert.Equal(0, counts.Stocks);
        Assert.Equal(0, counts.Locations);
        Assert.Equal(1, counts.Employees);   // administration data of the mapped tenant is reachable...
        Assert.Equal(TenantId, counts.TenantId); // ...because the tenant is bound
        Assert.Equal("0", await (await GetAsync("/test-probe/employee-products", token)).Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task APlatformAdministrator_CannotReadInventory_EvenThroughACarelessEndpoint()
    {
        await SeedInventoryAsync();

        var counts = await (await GetAsync("/test-probe/counts", PlatformAdminToken())).Content
            .ReadFromJsonAsync<InventoryProbeController.Counts>();

        Assert.Equal(0, counts!.Products);
        Assert.Equal(0, counts.Stocks);
        Assert.Equal(0, counts.Locations);
        Assert.Equal(0, counts.Employees); // no tenant is bound, so no tenant data at all
        Assert.Null(counts.TenantId);
    }

    [Fact]
    public async Task APlatformAdministrator_ThatIsAlsoATenantAdministrator_StillCannotReadInventory()
    {
        await SeedInventoryAsync();
        var objectId = Guid.NewGuid();
        await CreateTenantAdminAsync(objectId: objectId);

        var counts = await (await GetAsync("/test-probe/counts", PlatformAdminToken(objectId))).Content
            .ReadFromJsonAsync<InventoryProbeController.Counts>();

        Assert.Equal(TenantId, counts!.TenantId);
        Assert.Equal(0, counts.Products);
        Assert.Equal(0, counts.Stocks);
        Assert.Equal(0, counts.Locations);
    }

    [Fact]
    public async Task AnAdministrator_CannotWriteInventory()
    {
        await SeedInventoryAsync();
        var token = TokenFor(await CreateTenantAdminAsync());

        var response = await SendAsync(HttpMethod.Post, "/test-probe/product", token: token);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("administrator request", await response.Content.ReadAsStringAsync());
        Assert.Equal(1, await Context.Products.CountAsync()); // only the seeded one
    }

    [Fact]
    public async Task APlatformAdministrator_CannotUseTheEmployeeProductEndpoints()
    {
        await SeedInventoryAsync();
        var token = PlatformAdminToken();

        Assert.Equal(HttpStatusCode.Unauthorized, await StatusAsync("/api/products", token));
        Assert.Equal(HttpStatusCode.Unauthorized, await StatusAsync("/api/products/10000001", token));
    }

    [Fact]
    public async Task AnEmployeeRequest_StillSeesItsInventory()
    {
        // The restriction belongs to administrator requests, not to the data.
        await SeedInventoryAsync();
        await CreateEmployeeAsync("Manager", "BADGE-M", DefaultRoles.WarehouseManager);
        var pda = await EnrollPdaAsync();
        var token = await LoginTokenAsync(pda, "BADGE-M");

        var products = await (await GetAsync("/api/products", token)).Content.ReadFromJsonAsync<List<ProductDto>>();

        Assert.Single(products!);
    }

    // ---- Platform permissions --------------------------------------------------------

    [Fact]
    public void PlatformPermissions_AreSeparateFromTenantPermissions_AndNeverIncludeInventoryOrRoleDefinitions()
    {
        Assert.Empty(PlatformPermissions.All.Where(Permissions.IsDefined));
        Assert.Equal(PlatformPermissions.All.Count, PlatformPermissions.All.Distinct().Count());

        // Decision D8 (no inventory) and assumption A2 (no editing of role definitions or permissions).
        Assert.DoesNotContain(PlatformPermissions.All, p => p.Contains("Product") || p.Contains("Stock.") || p.Contains("Location"));
        Assert.DoesNotContain(PlatformPermissions.All, p => p.Contains("Role"));
    }

    // ---- The database can't be asked -------------------------------------------------

    [Fact]
    public async Task WhenTheDatabaseFails_NothingIsGranted_AndTheClientIsToldToRetry()
    {
        var tenantToken = TokenFor(await CreateTenantAdminAsync());
        var platformToken = PlatformAdminToken();
        Assert.Equal(HttpStatusCode.OK, await StatusAsync(AdminTenant, tenantToken));

        Factory.DatabaseFailure.Fail = true;
        try
        {
            foreach (var (token, url) in new[] { (tenantToken, AdminTenant), (tenantToken, AdminMe), (platformToken, "/api/platform/tenants") })
            {
                var response = await GetAsync(url, token);

                Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
                Assert.Empty(await response.Content.ReadAsStringAsync());
            }

            // Not even a platform administrator gets through on the strength of the token alone.
            Assert.Equal(HttpStatusCode.ServiceUnavailable, await StatusAsync(AdminMe, platformToken));
        }
        finally
        {
            Factory.DatabaseFailure.Fail = false;
        }

        Assert.Equal(HttpStatusCode.OK, await StatusAsync(AdminTenant, tenantToken)); // and it recovers
    }
}
