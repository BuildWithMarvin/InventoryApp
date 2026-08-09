using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace InventoryApp.Api.Models
{
    public class Product
    {

        private Product() { }

  
      public Product(string internalBarcode, string name, int createdByEmployeeId, string? supplierBarcode = null)
        {
            if (string.IsNullOrWhiteSpace(internalBarcode))
                throw new ArgumentException("A barcode is required.", nameof(internalBarcode));

            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("A name is required.", nameof(name));

            InternalBarcode = internalBarcode?.Trim();
            Name = name;
            LastUpdatedByEmployeeId = createdByEmployeeId;
            SupplierBarcode = supplierBarcode;
        }
        [Key]
        [MaxLength(50)]
        public string InternalBarcode { get; private set; }

        [MaxLength(50)]
        public string? SupplierBarcode { get; private set; }

        public string Name { get; private set; }

        public string? Description { get; private set; }

        [Precision(18, 2)]
        public decimal Price { get; private set; }

        public int? LastUpdatedByEmployeeId { get; private set; }

        [ForeignKey(nameof(LastUpdatedByEmployeeId))]
        public Employee? LastUpdatedBy { get; private set; }

        public int TotalQuantity => Stocks?.Sum(s => s.Quantity) ?? 0;

     

        [Timestamp]
        public byte[] RowVersion { get; private set; } = null!;


        public ICollection<ProductStock> Stocks { get; } = new List<ProductStock>();

        public void UpdateSupplierBarcode(string supplierBarcode, int employeeId)
        {
            SupplierBarcode = string.IsNullOrWhiteSpace(supplierBarcode) ? null : supplierBarcode.Trim();
            LastUpdatedByEmployeeId = employeeId;
        }

        public void UpdateDetails(string name, string? description, int employeeId)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Name is required.", nameof(name));

            Name = name;
            Description = description;
            LastUpdatedByEmployeeId = employeeId;
        }

        public void UpdatePrice(decimal newPrice, int employeeId)
        {
            if (newPrice < 0)
                throw new ArgumentException("Price cannot be negative.", nameof(newPrice));

            Price = newPrice;
            LastUpdatedByEmployeeId = employeeId;
        }
    }
}
