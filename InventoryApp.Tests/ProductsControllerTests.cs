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

          
            var newProduct = new Product
            {
                Id = 1,
                Name = "Test-articel",
                Barcode = "12345678"
            };

  
            var result = await controller.CreateProduct(newProduct);

      
            var okResult = Assert.IsType<OkObjectResult>(result.Result);


            var returnedProduct = Assert.IsType<Product>(okResult.Value);
            Assert.Equal("12345678", returnedProduct.Barcode);

            var productInDb = await context.Products.FirstOrDefaultAsync(p => p.Barcode == "12345678");
            Assert.NotNull(productInDb);
        }

        [Fact]
        public async Task UpdateProduct_ChangesData_AndReturnsNoContent()
        {
           
            using var context = GetDatabaseContext();
            var controller = new ProductsController(context);

           
            var originalProduct = new Product
            {
                Id = 1,
                Name = "Old product",
                Barcode = "12345678"
            };

            originalProduct.RowVersion = Guid.NewGuid().ToString();
            context.Products.Add(originalProduct);
            await context.SaveChangesAsync();

            
          context.ChangeTracker.Clear();

        
            var updatedProduct = new Product
            {
                Id = 1,
                Name = "New product (Updated!)",
                Barcode = "12345678",
                RowVersion = originalProduct.RowVersion
            };

            var result = await controller.UpdateProduct(1, updatedProduct);

          
            Assert.IsType<NoContentResult>(result);

        
            var productInDb = await context.Products.FindAsync(1);
            Assert.NotNull(productInDb);
            Assert.Equal("New product (Updated!)", productInDb.Name);
        }

        [Fact]
        public async Task UpdateProduct_ReturnsNotFound_WhenProductDoesNotExist()
        {
            // 1. ARRANGE
            using var context = GetDatabaseContext();
            var controller = new ProductsController(context);

            // Wir erstellen ein Objekt, aber speichern es NICHT in der Datenbank (DB ist leer)
            var nonExistentProduct = new Product
            {
                Id = 999,
                Name = "Phantom-product",
                Barcode = "00000000"
            };

            
            var result = await controller.UpdateProduct(999, nonExistentProduct);

          
            Assert.IsType<NotFoundObjectResult>(result);
        }

        [Fact]
        public async Task UpdateProduct_ReturnsBadRequest_WhenIdsDoNotMatch()
        {
        
            using var context = GetDatabaseContext();
            var controller = new ProductsController(context);

            var mismatchedProduct = new Product
            {
                Id = 2, 
                Name = "Manipulated artikel",
                Barcode = "11112222"
            };

        
            var result = await controller.UpdateProduct(1, mismatchedProduct);

            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task UpdateProduct_ReturnsConflict_WhenConcurrencyOccurs()
        {
            
            using var context = GetDatabaseContext();
            var controller = new ProductsController(context);

            var originalProduct = new Product { Id = 1, Name = "Lager-Artikel", Barcode = "999" };
            originalProduct.RowVersion = Guid.NewGuid().ToString();
            context.Products.Add(originalProduct);
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();

            var productUserA = await context.Products.FindAsync(1);
            productUserA.Name = "Lager-Artikel (Geändert von A)";
            productUserA.RowVersion = Guid.NewGuid().ToString();
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();

          var productUserB = new Product { Id = 1, Name = "Lager-Artikel (Geändert von B)", Barcode = "999", RowVersion = originalProduct.RowVersion };

            var result = await controller.UpdateProduct(1, productUserB);

            Assert.IsType<ConflictObjectResult>(result);
        }
    }


}