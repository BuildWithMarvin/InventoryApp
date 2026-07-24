using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace InventoryApp.Api.Models
{
    public class Product
    {
       
        private Product() { }

        
        public Product(string internalBarcode, string name)
        {
            if (string.IsNullOrWhiteSpace(internalBarcode))
                throw new ArgumentException("A barcode is required.", nameof(internalBarcode));

            InternalBarcode = internalBarcode;
            Name = name;
        }

        [Key]
        [MaxLength(50)]
        public string InternalBarcode { get; private set; }

        [MaxLength(50)]
        public string? SupplierBarcode { get; set; }

        public string Name { get; set; }
        public string? Description { get; set; }
        public int Quantity { get; set; }

        [ConcurrencyCheck]
        public string RowVersion { get; set; } = string.Empty;

        [Precision(18, 2)]
        public decimal Price { get; set; }
        public int? LastUpdatedByEmployeeId { get; set; }
    }
}
