using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;
using Xunit;
using InventoryApp.Api.Controllers;
using InventoryApp.Api.Data;
using InventoryApp.Api.Models;

namespace InventoryApp.Tests
{
    [Trait("Category", "Integration")]
    public class ProductsControllerTests
    {
        private InventoryDbContext GetDatabaseContext()
        {
            var connectionString =
        Environment.GetEnvironmentVariable(
            "ConnectionStrings__DefaultConnection");

            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException(
                    "ConnectionStrings__DefaultConnection is not configured.");
            }

            var options = new DbContextOptionsBuilder<InventoryDbContext>()
                .UseSqlServer(connectionString)
                .Options;

            var context = new InventoryDbContext(options);

            context.Database.Migrate();

            return context;
        }

        [Fact]
        public async Task GetProductByBarcode_ReturnsNotFound_WhenBarcodeDoesNotExist()
        {
            using var context = GetDatabaseContext();
            var controller = new ProductsController(context);

            var result = await controller.GetProductByBarcode("99999999");

            Assert.IsType<NotFoundResult>(result.Result);
        }

        [Fact]
        public async Task CreateProduct_SavesToDatabase_AndReturnsCreatedAtAction()
        {
            using var context = GetDatabaseContext();
            var controller = new ProductsController(context);

            var newProduct = new Product(
                "12345678",
                "Test-articel",
                1
            );

            var result = await controller.CreateProduct(newProduct);

            var createdResult =
                Assert.IsType<CreatedAtActionResult>(result.Result);

            var returnedProduct =
                Assert.IsType<Product>(createdResult.Value);

            Assert.Equal(
                "12345678",
                returnedProduct.InternalBarcode
            );

            var productInDb = await context.Products
                .FirstOrDefaultAsync(
                    p => p.InternalBarcode == "12345678"
                );

            Assert.NotNull(productInDb);
        }

        [Fact]
        public async Task UpdateProduct_ChangesData_AndReturnsNoContent()
        {
            using var context = GetDatabaseContext();
            var controller = new ProductsController(context);

            var originalProduct = new Product(
                "12345678",
                "Old product",
                1
            );

            context.Products.Add(originalProduct);
            await context.SaveChangesAsync();

            context.ChangeTracker.Clear();

            var updatedProduct = new Product(
                "12345678",
                "New product (Updated!)",
                1
            );

            var result = await controller.UpdateProduct(
                "12345678",
                updatedProduct
            );

            Assert.IsType<NoContentResult>(result);

            var productInDb = await context.Products
                .FindAsync("12345678");

            Assert.NotNull(productInDb);
            Assert.Equal(
                "New product (Updated!)",
                productInDb.Name
            );
        }

        [Fact]
        public async Task UpdateProduct_ReturnsNotFound_WhenProductDoesNotExist()
        {
            using var context = GetDatabaseContext();
            var controller = new ProductsController(context);

            var nonExistentProduct = new Product(
                "00000000",
                "Phantom-product",
                1
            );

            var result = await controller.UpdateProduct(
                "00000000",
                nonExistentProduct
            );

            Assert.IsType<NotFoundObjectResult>(result);
        }

        [Fact]
        public async Task UpdateProduct_ReturnsBadRequest_WhenIdsDoNotMatch()
        {
            using var context = GetDatabaseContext();
            var controller = new ProductsController(context);

            var mismatchedProduct = new Product(
                "11112222",
                "Manipulated article",
                1
            );

            var result = await controller.UpdateProduct(
                "12345678",
                mismatchedProduct
            );

            Assert.IsType<BadRequestObjectResult>(result);
        }
    }
}