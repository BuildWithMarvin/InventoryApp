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
                Name = "Test-Artikel",
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
                Name = "Altes Produkt",
                Barcode = "12345678"
            };
            context.Products.Add(originalProduct);
            await context.SaveChangesAsync();

            
          context.ChangeTracker.Clear();

        
            var updatedProduct = new Product
            {
                Id = 1,
                Name = "Neues Produkt (Geändert!)", // Wir haben den Namen geändert
                Barcode = "12345678"
            };

            var result = await controller.UpdateProduct(1, updatedProduct);

          
            Assert.IsType<NoContentResult>(result);

        
            var productInDb = await context.Products.FindAsync(1);
            Assert.NotNull(productInDb);
            Assert.Equal("Neues Produkt (Geändert!)", productInDb.Name);
        }
    }
}