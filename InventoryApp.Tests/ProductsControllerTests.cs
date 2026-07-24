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
    public class ProductsControllerTests
    {
        private InventoryDbContext GetDatabaseContext()
        {
            var options = new DbContextOptionsBuilder<InventoryDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;

            var context = new InventoryDbContext(options);
            context.Database.EnsureCreated();
            return context;
        }

        [Fact]
        public async Task GetProductByBarcode_ReturnsNotFound_WhenBarcodeDoesNotExist()
        {
            var context = GetDatabaseContext();
            var controller = new ProductsController(context);

            var result = await controller.GetProductByBarcode("99999999");

            Assert.IsType<NotFoundResult>(result.Result);
        }

        [Fact]
        public async Task CreateProduct_SavesToDatabase_AndReturnsOk()
        {
            var context = GetDatabaseContext();
            var controller = new ProductsController(context);

            
            var newProduct = new Product("12345678", "Test-articel");

            var result = await controller.CreateProduct(newProduct);

            var okResult = Assert.IsType<OkObjectResult>(result.Result);

            var returnedProduct = Assert.IsType<Product>(okResult.Value);
            Assert.Equal("12345678", returnedProduct.InternalBarcode);

            var productInDb = await context.Products.FirstOrDefaultAsync(p => p.InternalBarcode == "12345678");
            Assert.NotNull(productInDb);
        }

        [Fact]
        public async Task UpdateProduct_ChangesData_AndReturnsNoContent()
        {
            using var context = GetDatabaseContext();
            var controller = new ProductsController(context);

            var originalProduct = new Product("12345678", "Old product")
            {
                RowVersion = Guid.NewGuid().ToString()
            };

            context.Products.Add(originalProduct);
            await context.SaveChangesAsync();

            context.ChangeTracker.Clear();

            var updatedProduct = new Product("12345678", "New product (Updated!)")
            {
                RowVersion = originalProduct.RowVersion
            };

           
            var result = await controller.UpdateProduct("12345678", updatedProduct);

            Assert.IsType<NoContentResult>(result);

          
            var productInDb = await context.Products.FindAsync("12345678");
            Assert.NotNull(productInDb);
            Assert.Equal("New product (Updated!)", productInDb.Name);
        }

        [Fact]
        public async Task UpdateProduct_ReturnsNotFound_WhenProductDoesNotExist()
        {
            using var context = GetDatabaseContext();
            var controller = new ProductsController(context);

            var nonExistentProduct = new Product("00000000", "Phantom-product");

            var result = await controller.UpdateProduct("00000000", nonExistentProduct);

            Assert.IsType<NotFoundObjectResult>(result);
        }

        [Fact]
        public async Task UpdateProduct_ReturnsBadRequest_WhenIdsDoNotMatch()
        {
            using var context = GetDatabaseContext();
            var controller = new ProductsController(context);

            var mismatchedProduct = new Product("11112222", "Manipulated artikel");

            var result = await controller.UpdateProduct("12345678", mismatchedProduct);

            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task UpdateProduct_ReturnsConflict_WhenConcurrencyOccurs()
        {
            using var context = GetDatabaseContext();
            var controller = new ProductsController(context);

            var originalProduct = new Product("999", "Lager-Artikel")
            {
                RowVersion = Guid.NewGuid().ToString()
            };
            context.Products.Add(originalProduct);
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();

            var productUserA = await context.Products.FindAsync("999");
            productUserA.Name = "Lager-Artikel (Geändert von A)";
            productUserA.RowVersion = Guid.NewGuid().ToString();
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();

            var productUserB = new Product("999", "Lager-Artikel (Geändert von B)")
            {
                RowVersion = originalProduct.RowVersion
            };

            var result = await controller.UpdateProduct("999", productUserB);

            Assert.IsType<ConflictObjectResult>(result);
        }
    }
}