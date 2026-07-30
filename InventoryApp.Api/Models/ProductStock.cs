using System.ComponentModel.DataAnnotations;

namespace InventoryApp.Api.Models
{
   

    public class ProductStock
    {
        [MaxLength(50)]
        public string ProductBarcode { get; set; } = string.Empty;
        public Product Product { get; set; } = null!;

        public int StorageLocationId { get; set; }
        public StorageLocation StorageLocation { get; set; } = null!;

        public int Quantity { get; set; }
        public DateTime LastUpdated { get; set; } = DateTime.UtcNow;

        
        [Timestamp]
        public byte[] RowVersion { get; set; } = null!;
    }
}
