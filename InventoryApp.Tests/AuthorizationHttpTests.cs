using System.Net;
using System.Net.Http.Json;
using InventoryApp.Api.Dtos;
using InventoryApp.Api.Models;
using InventoryApp.Api.Security;
using Microsoft.EntityFrameworkCore;

[Trait("Category", "Integration")]
[Collection("Integration Tests")]
public class AuthorizationHttpTests : ApiTestBase
{
    private Employee _worker = null!;
    private Employee _manager = null!;
    private string _workerToken = null!;
    private string _managerToken = null!;

    public AuthorizationHttpTests(DatabaseFixture databaseFixture)
        : base(databaseFixture)
    {
    }

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();

        _worker = await CreateEmployeeAsync("Wanda", "BADGE-W", DefaultRoles.WarehouseEmployee);
        _manager = await CreateEmployeeAsync("Max", "BADGE-M", DefaultRoles.WarehouseManager);

        _workerToken = await LoginTokenAsync(await EnrollPdaAsync("PDA 1"), "BADGE-W");
        _managerToken = await LoginTokenAsync(await EnrollPdaAsync("PDA 2"), "BADGE-M");
    }

    // ---- Permissions -----------------------------------------------------------------

    [Fact]
    public async Task EveryProductEndpoint_NeedsAToken()
    {
        var calls = new (HttpMethod Method, string Url, object? Body)[]
        {
            (HttpMethod.Get, "/api/products", null),
            (HttpMethod.Get, "/api/products/10000001", null),
            (HttpMethod.Post, "/api/products", new CreateProductRequest("10000001", "Product")),
            (HttpMethod.Put, "/api/products/10000001", new UpdateProductRequest("Product", new byte[8])),
            (HttpMethod.Post, "/api/employees", new CreateEmployeeRequest("Tom", "EMP-1")),
            (HttpMethod.Post, "/api/auth/logout", null),
            (HttpMethod.Post, "/api/auth/change-pin", new ChangePinRequest(null, "123456")),
        };

        foreach (var (method, url, body) in calls)
        {
            var response = await SendAsync(method, url, body);
            Assert.True(response.StatusCode == HttpStatusCode.Unauthorized, $"{method} {url}: {response.StatusCode}");
        }
    }

    [Fact]
    public async Task AWarehouseEmployee_CanRead_ButNotChangeProducts()
    {
        Assert.Equal(HttpStatusCode.OK, (await GetAsync("/api/products", _workerToken)).StatusCode);

        var create = await SendAsync(HttpMethod.Post, "/api/products",
            new CreateProductRequest("10000001", "Product"), _workerToken);
        var update = await SendAsync(HttpMethod.Put, "/api/products/10000001",
            new UpdateProductRequest("Product", new byte[8]), _workerToken);

        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, update.StatusCode);
    }

    [Fact]
    public async Task AWarehouseManager_CanCreateAndUpdateProducts_AndTheyAreAttributedToThem()
    {
        var created = await SendAsync(HttpMethod.Post, "/api/products",
            new CreateProductRequest("10000001", "Product", Price: 2.50m), _managerToken);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var product = (await created.Content.ReadFromJsonAsync<ProductDto>())!;
        Assert.Equal(_manager.Id, product.LastUpdatedByEmployeeId);

        var update = await SendAsync(HttpMethod.Put, "/api/products/10000001",
            new UpdateProductRequest("Renamed", product.RowVersion, Price: 3m), _managerToken);

        Assert.Equal(HttpStatusCode.NoContent, update.StatusCode);
    }

    [Fact]
    public async Task TheEmployeeComesFromTheSession_NotFromTheRequest()
    {
        // A client that still sends the old EmployeeId field gets it ignored.
        var created = await SendAsync(HttpMethod.Post, "/api/products",
            new { InternalBarcode = "10000001", Name = "Product", EmployeeId = _worker.Id }, _managerToken);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var product = (await created.Content.ReadFromJsonAsync<ProductDto>())!;
        Assert.Equal(_manager.Id, product.LastUpdatedByEmployeeId);
    }

    [Fact]
    public async Task AdministratorEndpoints_AreClosedToEveryBadgeLogin()
    {
        var request = new CreateEmployeeRequest("Tom", "EMP-1");

        var asManager = await SendAsync(HttpMethod.Post, "/api/employees", request, _managerToken);
        var asWorker = await SendAsync(HttpMethod.Post, "/api/employees", request, _workerToken);

        Assert.Equal(HttpStatusCode.Forbidden, asManager.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, asWorker.StatusCode);
        await using var context = CreateContextFor(Tenant);
        Assert.False(await context.Employees.AnyAsync(e => e.BadgeBarcode == "EMP-1"));
    }

    // ---- Tenant isolation over HTTP --------------------------------------------------

    [Fact]
    public async Task Products_OfAnotherTenant_AreInvisible_AndCannotBeChanged()
    {
        var other = await CreateTenantAsync("Other tenant");
        var ben = await CreateEmployeeAsync("Ben", "BADGE-B", DefaultRoles.WarehouseManager, tenant: other);
        var tokenOfB = await LoginTokenAsync(await EnrollPdaAsync("PDA B", other), "BADGE-B");

        // Same barcode in both tenants, plus one that only tenant B has.
        await SendAsync(HttpMethod.Post, "/api/products", new CreateProductRequest("SAME", "Product of A"), _managerToken);
        await SendAsync(HttpMethod.Post, "/api/products", new CreateProductRequest("SAME", "Product of B"), tokenOfB);
        var onlyB = await SendAsync(HttpMethod.Post, "/api/products", new CreateProductRequest("ONLY-B", "Only B"), tokenOfB);
        var onlyBProduct = (await onlyB.Content.ReadFromJsonAsync<ProductDto>())!;

        var sameForA = await (await GetAsync("/api/products/SAME", _managerToken)).Content.ReadFromJsonAsync<ProductDto>();
        var listForA = await (await GetAsync("/api/products", _managerToken)).Content.ReadFromJsonAsync<List<ProductDto>>();

        Assert.Equal("Product of A", sameForA!.Name);
        Assert.Equal(new[] { "SAME" }, listForA!.Select(p => p.InternalBarcode));
        Assert.Equal(HttpStatusCode.NotFound, (await GetAsync("/api/products/ONLY-B", _managerToken)).StatusCode);

        var hijack = await SendAsync(HttpMethod.Put, "/api/products/ONLY-B",
            new UpdateProductRequest("Hijacked", onlyBProduct.RowVersion), _managerToken);
        Assert.Equal(HttpStatusCode.NotFound, hijack.StatusCode);

        var stillB = await (await GetAsync("/api/products/ONLY-B", tokenOfB)).Content.ReadFromJsonAsync<ProductDto>();
        Assert.Equal("Only B", stillB!.Name);
        Assert.Equal(ben.Id, stillB.LastUpdatedByEmployeeId);
    }

    // ---- An admin-set PIN must be replaced first -------------------------------------

    [Fact]
    public async Task AnAdminSetPin_BlocksEverythingExceptChangingIt()
    {
        await CreateEmployeeAsync("Tina", "BADGE-T", pin: "123456", mustChangePin: true);
        await RequirePinAsync();
        var pda = await EnrollPdaAsync("PDA 3");

        var login = await LoginAsync(pda, "BADGE-T", "123456");
        var body = (await login.Content.ReadFromJsonAsync<LoginResponse>())!;

        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.True(body.MustChangePin);
        Assert.Equal(HttpStatusCode.Forbidden, (await GetAsync("/api/products", body.AccessToken)).StatusCode);

        var change = await SendAsync(HttpMethod.Post, "/api/auth/change-pin",
            new ChangePinRequest("123456", "654321"), body.AccessToken);

        Assert.Equal(HttpStatusCode.NoContent, change.StatusCode);
        // Same token, next request: the requirement is gone.
        Assert.Equal(HttpStatusCode.OK, (await GetAsync("/api/products", body.AccessToken)).StatusCode);
    }
}
