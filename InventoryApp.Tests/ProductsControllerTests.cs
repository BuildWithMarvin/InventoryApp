using InventoryApp.Api.Controllers;
using InventoryApp.Api.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

[Trait("Category", "Integration")]
[Collection("Integration Tests")]
public class ProductsControllerTests : IntegrationTestBase
{
    private Employee _employee = null!;

    public ProductsControllerTests(DatabaseFixture databaseFixture)
        : base(databaseFixture)
    {
    }

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();

        // Created inside the test's transaction, so it is rolled back with it.
        _employee = new Employee
        {
            Name = "Max-Mustermann",
            BadgeBarcode = "EMP-12347",
            Role = "user"
        };

        Context.Employees.Add(_employee);
        await Context.SaveChangesAsync();
    }

    [Fact]
    public async Task GetProductByBarcode_ReturnsOk_WhenProductExists()
    {
        var controller = new ProductsController(Context);

        var product = new Product(
            "10000001",
            "Test product",
            _employee.Id
        );

        Context.Products.Add(product);
        await Context.SaveChangesAsync();

        var result =
            await controller.GetProductByBarcode("10000001");

        var returnedProduct =
            Assert.IsType<Product>(result.Value);

        Assert.Equal(
            "10000001",
            returnedProduct.InternalBarcode);

        Assert.Equal(
            "Test product",
            returnedProduct.Name);
    }

    [Fact]
    public async Task GetProductByBarcode_ReturnsNotFound_WhenBarcodeDoesNotExist()
    {
            var controller = new ProductsController(Context);

        var result =
            await controller.GetProductByBarcode("10000002");

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task CreateProduct_SavesToDatabase_AndReturnsCreatedAtAction()
    {
            var controller = new ProductsController(Context);

        var newProduct = new Product(
            "10000003",
            "Test product",
            _employee.Id
        );

        var result =
            await controller.CreateProduct(newProduct);

        var createdResult =
            Assert.IsType<CreatedAtActionResult>(result.Result);

        Assert.Equal(
            nameof(ProductsController.GetProductByBarcode),
            createdResult.ActionName);

        var returnedProduct =
            Assert.IsType<Product>(createdResult.Value);

        Assert.Equal(
            "10000003",
            returnedProduct.InternalBarcode);

        Assert.Equal(
            "Test product",
            returnedProduct.Name);

        var productInDb =
            await Context.Products
                .FirstOrDefaultAsync(
                    p => p.InternalBarcode == "10000003");

        Assert.NotNull(productInDb);

        Assert.Equal(
            "Test product",
            productInDb.Name);
    }

    [Fact]
    public async Task UpdateProduct_ChangesData_AndReturnsNoContent()
    {
            var controller = new ProductsController(Context);

        var originalProduct = new Product(
            "10000004",
            "Old product",
            _employee.Id
        );

        Context.Products.Add(originalProduct);
        await Context.SaveChangesAsync();

        Context.ChangeTracker.Clear();

        var updatedProduct = new Product(
            "10000004",
            "New product",
            _employee.Id
        );

        Context.Entry(updatedProduct).Property(p => p.RowVersion).CurrentValue =
            originalProduct.RowVersion;

        var result =
            await controller.UpdateProduct(
                "10000004",
                updatedProduct);

        Assert.IsType<NoContentResult>(result);

        var productInDb =
            await Context.Products
                .FindAsync("10000004");

        Assert.NotNull(productInDb);

        Assert.Equal(
            "New product",
            productInDb.Name);
    }

    [Fact]
    public async Task UpdateProduct_ReturnsNotFound_WhenProductDoesNotExist()
    {
            var controller = new ProductsController(Context);

        var nonExistentProduct = new Product(
            "10000005",
            "Phantom product",
            _employee.Id
        );

        var result =
            await controller.UpdateProduct(
                "10000005",
                nonExistentProduct);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task UpdateProduct_ReturnsBadRequest_WhenIdsDoNotMatch()
    {
            var controller = new ProductsController(Context);

        var mismatchedProduct = new Product(
            "10000006",
            "Manipulated product",
            _employee.Id
        );

        var result =
            await controller.UpdateProduct(
                "10000004",
                mismatchedProduct);

        Assert.IsType<BadRequestObjectResult>(result);
    }
}
