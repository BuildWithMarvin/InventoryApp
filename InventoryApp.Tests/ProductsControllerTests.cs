using InventoryApp.Api.Controllers;
using InventoryApp.Api.Dtos;
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

        // Removed again by the database reset before the next test.
        _employee = new Employee(
            "Max-Mustermann",
             "EMP-123",
             "user");

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
            Assert.IsType<ProductDto>(result.Value);

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

        var newProduct = new CreateProductRequest(
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
            Assert.IsType<ProductDto>(createdResult.Value);

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

        var updatedProduct = new UpdateProductRequest(
            "New product",
            _employee.Id,
            originalProduct.RowVersion
        );

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

        var nonExistentProduct = new UpdateProductRequest(
            "Phantom product",
            _employee.Id,
            new byte[8]
        );

        var result =
            await controller.UpdateProduct(
                "10000005",
                nonExistentProduct);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task UpdateProduct_ReturnsConflict_WhenRowVersionIsStale()
    {
        var controller = new ProductsController(Context);

        var product = new Product(
            "10000006",
            "Old product",
            _employee.Id
        );

        Context.Products.Add(product);
        await Context.SaveChangesAsync();

        Context.ChangeTracker.Clear();

        var staleUpdate = new UpdateProductRequest(
            "New product",
            _employee.Id,
            new byte[8]
        );

        var result =
            await controller.UpdateProduct(
                "10000006",
                staleUpdate);

        Assert.IsType<ConflictObjectResult>(result);
    }
}
