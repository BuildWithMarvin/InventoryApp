using InventoryApp.Api.Controllers;
using InventoryApp.Api.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;


    [Trait("Category", "Integration")]
    [Collection("Integration Tests")]
    public class ProductsControllerTests
    {
        private readonly DatabaseFixture _databaseFixture;

        public ProductsControllerTests(DatabaseFixture databaseFixture)
        {
            _databaseFixture = databaseFixture;
        }

        [Fact]
        public async Task GetProductByBarcode_ReturnsOk_WhenProductExists()
        {
            await using var context =
                _databaseFixture.CreateContext();

            var controller = new ProductsController(context);

            var product = new Product(
                "10000001",
                "Test product",
                1
            );

            context.Products.Add(product);
            await context.SaveChangesAsync();

            var result =
                await controller.GetProductByBarcode("10000001");

            var okResult =
                Assert.IsType<OkObjectResult>(result.Result);

            var returnedProduct =
                Assert.IsType<Product>(okResult.Value);

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
            await using var context =
                _databaseFixture.CreateContext();

            var controller = new ProductsController(context);

            var result =
                await controller.GetProductByBarcode("10000002");

            Assert.IsType<NotFoundResult>(result.Result);
        }

        [Fact]
        public async Task CreateProduct_SavesToDatabase_AndReturnsCreatedAtAction()
        {
            await using var context =
                _databaseFixture.CreateContext();

            var controller = new ProductsController(context);

            var newProduct = new Product(
                "10000003",
                "Test product",
                1
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
                await context.Products
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
            await using var context =
                _databaseFixture.CreateContext();

            var controller = new ProductsController(context);

            var originalProduct = new Product(
                "10000004",
                "Old product",
                1
            );

            context.Products.Add(originalProduct);
            await context.SaveChangesAsync();

            context.ChangeTracker.Clear();

            var updatedProduct = new Product(
                "10000004",
                "New product",
                1
            );

            var result =
                await controller.UpdateProduct(
                    "10000004",
                    updatedProduct);

            Assert.IsType<NoContentResult>(result);

            var productInDb =
                await context.Products
                    .FindAsync("10000004");

            Assert.NotNull(productInDb);

            Assert.Equal(
                "New product",
                productInDb.Name);
        }

        [Fact]
        public async Task UpdateProduct_ReturnsNotFound_WhenProductDoesNotExist()
        {
            await using var context =
                _databaseFixture.CreateContext();

            var controller = new ProductsController(context);

            var nonExistentProduct = new Product(
                "10000005",
                "Phantom product",
                1
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
            await using var context =
                _databaseFixture.CreateContext();

            var controller = new ProductsController(context);

            var mismatchedProduct = new Product(
                "10000006",
                "Manipulated product",
                1
            );

            var result =
                await controller.UpdateProduct(
                    "10000004",
                    mismatchedProduct);

            Assert.IsType<BadRequestObjectResult>(result);
        }
    }
