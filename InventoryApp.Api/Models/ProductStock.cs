using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace InventoryApp.Api.Models
{


    public class ProductStock
    {
        
        private ProductStock() { }

        
        public ProductStock(string productInternalBarcode, int storageLocationId, int quantity)
        {
            if (string.IsNullOrWhiteSpace(productInternalBarcode))
                throw new ArgumentException("Product barcode is required.", nameof(productInternalBarcode));

            ProductInternalBarcode = productInternalBarcode;
            StorageLocationId = storageLocationId;
            Quantity = quantity;
        }

        [Key]
        public int Id { get; private set; }

        public int Quantity { get; set; }

        [Required]
        [MaxLength(50)]
        public string ProductInternalBarcode { get; private set; }

        [JsonIgnore] 
        [ForeignKey(nameof(ProductInternalBarcode))]
        public Product? Product { get; private set; }

       
        public int StorageLocationId { get; private set; }

        [ForeignKey(nameof(StorageLocationId))]
        public StorageLocation? StorageLocation { get; private set; }
    }
}
