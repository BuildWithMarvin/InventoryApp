using System.ComponentModel.DataAnnotations;
using InventoryApp.Api.Models;

namespace InventoryApp.Api.Dtos
{
    public record ProductStockDto(int Id, int StorageLocationId, int Quantity, int? LastUpdatedByEmployeeId)
    {
        public static ProductStockDto FromEntity(ProductStock stock) =>
            new(stock.Id, stock.StorageLocationId, stock.Quantity, stock.LastUpdatedByEmployeeId);
    }

    public record ProductDto(
        string InternalBarcode,
        string? SupplierBarcode,
        string Name,
        string? Description,
        decimal Price,
        int TotalQuantity,
        int? LastUpdatedByEmployeeId,
        byte[] RowVersion,
        IReadOnlyList<ProductStockDto> Stocks)
    {
        public static ProductDto FromEntity(Product product) =>
            new(
                product.InternalBarcode,
                product.SupplierBarcode,
                product.Name,
                product.Description,
                product.Price,
                product.TotalQuantity,
                product.LastUpdatedByEmployeeId,
                product.RowVersion,
                product.Stocks.Select(ProductStockDto.FromEntity).ToList());
    }

    public record CreateProductRequest(
        [Required, MaxLength(50)] string InternalBarcode,
        [Required] string Name,
        int EmployeeId,
        [MaxLength(50)] string? SupplierBarcode = null,
        string? Description = null,
        decimal Price = 0);

    // The barcode comes from the route; RowVersion is the value the client last read.
    public record UpdateProductRequest(
        [Required] string Name,
        int EmployeeId,
        [Required] byte[] RowVersion,
        [MaxLength(50)] string? SupplierBarcode = null,
        string? Description = null,
        decimal Price = 0);
}
