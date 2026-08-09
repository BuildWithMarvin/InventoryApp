using InventoryApp.Api.Models;
using Xunit;

namespace InventoryApp.Tests.Unit
    {
    [Trait("Category", "Unit")]
    public class ProductTests
        {
            [Fact]
            public void Constructor_CreatesProduct_WithValidData()
            {
                // Arrange & Act
                var product = new Product(
                    "12345678",
                    "Test product",
                    1,
                    "SUP-123"
                );

                // Assert
                Assert.Equal("12345678", product.InternalBarcode);
                Assert.Equal("Test product", product.Name);
                Assert.Equal("SUP-123", product.SupplierBarcode);
                Assert.Equal(1, product.LastUpdatedByEmployeeId);
            }

            [Fact]
            public void Constructor_TrimsInternalBarcode()
            {
                
                var product = new Product(
                    " 12345678 ",
                    "Test product",
                    1
                );

                
                Assert.Equal("12345678", product.InternalBarcode);
            }

            [Fact]
            public void Constructor_ThrowsArgumentException_WhenBarcodeIsEmpty()
            {
                
                Assert.Throws<ArgumentException>(() =>
                    new Product("", "Test product", 1));
            }

            [Fact]
            public void Constructor_ThrowsArgumentException_WhenBarcodeIsWhitespace()
            {
                
                Assert.Throws<ArgumentException>(() =>
                    new Product("   ", "Test product", 1));
            }

            [Fact]
            public void Constructor_ThrowsArgumentException_WhenNameIsEmpty()
            {
                
                Assert.Throws<ArgumentException>(() =>
                    new Product("12345678", "", 1));
            }

            [Fact]
            public void Constructor_ThrowsArgumentException_WhenNameIsWhitespace()
            {
                
                Assert.Throws<ArgumentException>(() =>
                    new Product("12345678", "   ", 1));
            }

            [Fact]
            public void UpdateSupplierBarcode_SetsBarcode()
            {
                
                var product = new Product(
                    "12345678",
                    "Test product",
                    1
                );

              
                product.UpdateSupplierBarcode(" SUP-123 ", 5);

               
                Assert.Equal("SUP-123", product.SupplierBarcode);
                Assert.Equal(5, product.LastUpdatedByEmployeeId);
            }

            [Fact]
            public void UpdateSupplierBarcode_SetsBarcodeToNull_WhenBarcodeIsEmpty()
            {
                
                var product = new Product(
                    "12345678",
                    "Test product",
                    1,
                    "SUP-123"
                );

               
                product.UpdateSupplierBarcode("", 5);

                
                Assert.Null(product.SupplierBarcode);
                Assert.Equal(5, product.LastUpdatedByEmployeeId);
            }

            [Fact]
            public void UpdateDetails_UpdatesNameDescriptionAndEmployee()
            {
                
                var product = new Product(
                    "12345678",
                    "Old name",
                    1
                );

             
                product.UpdateDetails(
                    "New name",
                    "New description",
                    5
                );

                
                Assert.Equal("New name", product.Name);
                Assert.Equal("New description", product.Description);
                Assert.Equal(5, product.LastUpdatedByEmployeeId);
            }

            [Fact]
            public void UpdateDetails_ThrowsArgumentException_WhenNameIsEmpty()
            {
                
                var product = new Product(
                    "12345678",
                    "Old name",
                    1
                );

                
                Assert.Throws<ArgumentException>(() =>
                    product.UpdateDetails("", "Description", 5));
            }

            [Fact]
            public void UpdateDetails_ThrowsArgumentException_WhenNameIsWhitespace()
            {
                
                var product = new Product(
                    "12345678",
                    "Old name",
                    1
                );

               
                Assert.Throws<ArgumentException>(() =>
                    product.UpdateDetails("   ", "Description", 5));
            }

            [Fact]
            public void UpdatePrice_UpdatesPriceAndEmployee()
            {
                
                var product = new Product(
                    "12345678",
                    "Test product",
                    1
                );

               
                product.UpdatePrice(25.50m, 5);

                
                Assert.Equal(25.50m, product.Price);
                Assert.Equal(5, product.LastUpdatedByEmployeeId);
            }

            [Fact]
            public void UpdatePrice_AllowsZero()
            {
                
                var product = new Product(
                    "12345678",
                    "Test product",
                    1
                );

                
                product.UpdatePrice(0m, 5);

            
                Assert.Equal(0m, product.Price);
                Assert.Equal(5, product.LastUpdatedByEmployeeId);
            }

            [Fact]
            public void UpdatePrice_ThrowsArgumentException_WhenPriceIsNegative()
            {
               
                var product = new Product(
                    "12345678",
                    "Test product",
                    1
                );

              
                Assert.Throws<ArgumentException>(() =>
                    product.UpdatePrice(-1m, 5));
            }

            [Fact]
            public void TotalQuantity_ReturnsZero_WhenProductHasNoStocks()
            {
                
                var product = new Product(
                    "12345678",
                    "Test product",
                    1
                );

              
                var totalQuantity = product.TotalQuantity;

              
                Assert.Equal(0, totalQuantity);
            }
        }
    }

