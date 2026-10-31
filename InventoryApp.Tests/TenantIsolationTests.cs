using InventoryApp.Api.Controllers;
using InventoryApp.Api.Data;
using InventoryApp.Api.Dtos;
using InventoryApp.Api.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

// Tenant A is the default tenant of IntegrationTestBase (Context). Tenant B is "Other".
// The tests show that data of one tenant is invisible to the other at the query level,
// that the context refuses cross-tenant writes, and that the database itself rejects
// rows referencing another tenant's data.
[Trait("Category", "Integration")]
[Collection("Integration Tests")]
public class TenantIsolationTests : IntegrationTestBase
{
    private Tenant _other = null!;
    private Employee _employeeA = null!;
    private Employee _employeeB = null!;

    public TenantIsolationTests(DatabaseFixture databaseFixture)
        : base(databaseFixture)
    {
    }

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();

        _other = await CreateTenantAsync("Other tenant");

        _employeeA = new Employee(TenantId, "Anna", "BADGE-A");
        Context.Employees.Add(_employeeA);
        await Context.SaveChangesAsync();

        await using var otherContext = CreateContextFor(_other);
        _employeeB = new Employee(_other.Id, "Ben", "BADGE-B");
        otherContext.Employees.Add(_employeeB);
        await otherContext.SaveChangesAsync();
    }

    // ---- Query filters ---------------------------------------------------------------

    [Fact]
    public async Task Queries_OnlyReturnRowsOfTheCurrentTenant()
    {
        await using var otherContext = CreateContextFor(_other);

        var location = new StorageLocation { TenantId = _other.Id, LocationCode = "A-01", Zone = "A", Barcode = "LOC-1" };
        otherContext.StorageLocations.Add(location);
        otherContext.Products.Add(new Product(_other.Id, "10000001", "Other product", _employeeB.Id));
        await otherContext.SaveChangesAsync();
        otherContext.ProductStocks.Add(new ProductStock(_other.Id, "10000001", location.Id));
        await otherContext.SaveChangesAsync();

        Assert.Empty(await Context.Products.ToListAsync());
        Assert.Empty(await Context.StorageLocations.ToListAsync());
        Assert.Empty(await Context.ProductStocks.ToListAsync());
        Assert.Equal(new[] { _employeeA.Id }, await Context.Employees.Select(e => e.Id).ToListAsync());

        Assert.Single(await otherContext.Products.ToListAsync());
        Assert.Single(await otherContext.StorageLocations.ToListAsync());
        Assert.Single(await otherContext.ProductStocks.ToListAsync());
    }

    [Fact]
    public async Task ContextWithoutTenant_SeesNoTenantData()
    {
        Context.Products.Add(new Product(TenantId, "10000001", "Product", _employeeA.Id));
        await Context.SaveChangesAsync();

        await using var noTenant = CreateContextWithoutTenant();

        Assert.Empty(await noTenant.Products.ToListAsync());
        Assert.Empty(await noTenant.Employees.ToListAsync());
        Assert.Empty(await noTenant.StorageLocations.ToListAsync());
        Assert.Empty(await noTenant.ProductStocks.ToListAsync());
    }

    [Fact]
    public async Task SameBarcodeCanExistInTwoTenants()
    {
        await using var otherContext = CreateContextFor(_other);

        Context.Products.Add(new Product(TenantId, "10000001", "Product of A", _employeeA.Id));
        otherContext.Products.Add(new Product(_other.Id, "10000001", "Product of B", _employeeB.Id));
        await Context.SaveChangesAsync();
        await otherContext.SaveChangesAsync();

        Assert.Equal("Product of A", (await Context.Products.SingleAsync()).Name);
        Assert.Equal("Product of B", (await otherContext.Products.SingleAsync()).Name);
    }

    [Fact]
    public async Task BadgeBarcode_IsUniquePerTenant_NotGlobally()
    {
        await using var otherContext = CreateContextFor(_other);

        // BADGE-A exists in tenant A. Tenant B may reuse it ...
        otherContext.Employees.Add(new Employee(_other.Id, "Bea", "BADGE-A"));
        await otherContext.SaveChangesAsync();

        // ... but tenant A may not use it twice.
        Context.Employees.Add(new Employee(TenantId, "Anna 2", "BADGE-A"));
        await Assert.ThrowsAsync<DbUpdateException>(() => Context.SaveChangesAsync());
    }

    // ---- Write guard -----------------------------------------------------------------

    [Fact]
    public async Task SaveChanges_Throws_WhenWritingAnEntityOfAnotherTenant()
    {
        Context.Products.Add(new Product(_other.Id, "10000001", "Smuggled product", _employeeB.Id));

        await Assert.ThrowsAsync<InvalidOperationException>(() => Context.SaveChangesAsync());
    }

    [Fact]
    public async Task SaveChanges_Throws_WhenNoTenantIsSet()
    {
        await using var noTenant = CreateContextWithoutTenant();
        noTenant.Employees.Add(new Employee(TenantId, "Tom", "BADGE-T"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => noTenant.SaveChangesAsync());
    }

    [Fact]
    public async Task Tenants_CanBeManagedWithoutATenantContext()
    {
        await using var noTenant = CreateContextWithoutTenant();

        noTenant.Tenants.Add(new Tenant("Third tenant"));

        Assert.Equal(1, await noTenant.SaveChangesAsync());
    }

    // ---- Controllers (IDOR) ----------------------------------------------------------

    [Fact]
    public async Task GetProductByBarcode_ReturnsNotFound_ForAProductOfAnotherTenant()
    {
        await SeedProductOfOtherTenantAsync("10000001");
        var controller = new ProductsController(Context).AsEmployee(_employeeA.Id);

        var result = await controller.GetProductByBarcode("10000001");

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task UpdateProduct_ReturnsNotFound_ForAProductOfAnotherTenant()
    {
        var other = await SeedProductOfOtherTenantAsync("10000001");
        var controller = new ProductsController(Context).AsEmployee(_employeeA.Id);

        var result = await controller.UpdateProduct(
            "10000001",
            new UpdateProductRequest("Hijacked", other.RowVersion));

        Assert.IsType<NotFoundObjectResult>(result);

        await using var otherContext = CreateContextFor(_other);
        Assert.Equal("Other product", (await otherContext.Products.SingleAsync()).Name);
    }

    [Fact]
    public async Task CreateProduct_RecordsTheAuthenticatedEmployee()
    {
        var controller = new ProductsController(Context).AsEmployee(_employeeA.Id);

        var result = await controller.CreateProduct(new CreateProductRequest("10000001", "Product"));

        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        Assert.Equal(_employeeA.Id, Assert.IsType<ProductDto>(created.Value).LastUpdatedByEmployeeId);
    }

    [Fact]
    public async Task CreateProduct_AllowsABarcodeUsedByAnotherTenant()
    {
        await SeedProductOfOtherTenantAsync("10000001");
        var controller = new ProductsController(Context).AsEmployee(_employeeA.Id);

        var result = await controller.CreateProduct(
            new CreateProductRequest("10000001", "Product of A"));

        Assert.IsType<CreatedAtActionResult>(result.Result);
    }

    [Fact]
    public async Task GetProducts_ReturnsOnlyProductsOfTheCurrentTenant()
    {
        await SeedProductOfOtherTenantAsync("10000001");
        Context.Products.Add(new Product(TenantId, "10000002", "Product of A", _employeeA.Id));
        await Context.SaveChangesAsync();
        var controller = new ProductsController(Context).AsEmployee(_employeeA.Id);

        var result = await controller.GetProducts();

        var products = Assert.IsAssignableFrom<IEnumerable<ProductDto>>(result.Value);
        Assert.Equal("10000002", Assert.Single(products).InternalBarcode);
    }

    // ---- The database enforces isolation on its own ----------------------------------

    [Fact]
    public async Task Database_RejectsAStockThatPointsToALocationOfAnotherTenant()
    {
        Context.Products.Add(new Product(TenantId, "10000001", "Product of A", _employeeA.Id));
        await Context.SaveChangesAsync();

        var ownLocation = new StorageLocation { TenantId = TenantId, LocationCode = "A-01", Zone = "A", Barcode = "LOC-A" };
        Context.StorageLocations.Add(ownLocation);
        await Context.SaveChangesAsync();

        await using var otherContext = CreateContextFor(_other);
        var foreignLocation = new StorageLocation { TenantId = _other.Id, LocationCode = "B-01", Zone = "B", Barcode = "LOC-B" };
        otherContext.StorageLocations.Add(foreignLocation);
        await otherContext.SaveChangesAsync();

        // Control: the same statement with a location of the same tenant works.
        await Context.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO ProductStocks (TenantId, ProductInternalBarcode, StorageLocationId, Quantity) VALUES ({TenantId}, '10000001', {ownLocation.Id}, 0)");

        await Assert.ThrowsAsync<SqlException>(() => Context.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO ProductStocks (TenantId, ProductInternalBarcode, StorageLocationId, Quantity) VALUES ({TenantId}, '10000001', {foreignLocation.Id}, 0)"));
    }

    [Fact]
    public async Task Database_RejectsAProductThatPointsToAnEmployeeOfAnotherTenant()
    {
        // Control: the same statement with an employee of the same tenant works.
        await Context.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO Products (TenantId, InternalBarcode, Name, Price, LastUpdatedByEmployeeId) VALUES ({TenantId}, '10000001', 'Product', 0, {_employeeA.Id})");

        await Assert.ThrowsAsync<SqlException>(() => Context.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO Products (TenantId, InternalBarcode, Name, Price, LastUpdatedByEmployeeId) VALUES ({TenantId}, '10000002', 'Product', 0, {_employeeB.Id})"));
    }

    [Fact]
    public async Task Database_RejectsARowForAnUnknownTenant()
    {
        await Assert.ThrowsAsync<SqlException>(() => Context.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO Products (TenantId, InternalBarcode, Name, Price) VALUES (999999, '10000001', 'Product', 0)"));
    }

    // ---- Helpers ---------------------------------------------------------------------

    private async Task<Product> SeedProductOfOtherTenantAsync(string barcode)
    {
        await using var otherContext = CreateContextFor(_other);
        var product = new Product(_other.Id, barcode, "Other product", _employeeB.Id);
        otherContext.Products.Add(product);
        await otherContext.SaveChangesAsync();
        return product;
    }
}
