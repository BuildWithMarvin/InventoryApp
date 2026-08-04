using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace InventoryApp.Api.Models
{


    public class ProductStock
    {
        
        private ProductStock() { }

        
        public ProductStock(string productInternalBarcode, int storageLocationId)
        {
            if (string.IsNullOrWhiteSpace(productInternalBarcode))
                throw new ArgumentException("Product barcode is required.", nameof(productInternalBarcode));

            ProductInternalBarcode = productInternalBarcode;
            StorageLocationId = storageLocationId;
            Quantity = 0;
        }

        [Key]
        public int Id { get; private set; }

        public int Quantity { get; private set; }

        public int? LastUpdatedByEmployeeId { get; private set; }

        [ForeignKey(nameof(LastUpdatedByEmployeeId))]
        public Employee? LastUpdatedBy { get; private set; }

        [Required]
        [MaxLength(50)]
        public string ProductInternalBarcode { get; private set; }

        [JsonIgnore] 
        [ForeignKey(nameof(ProductInternalBarcode))]
        public Product? Product { get; private set; }

       
        public int StorageLocationId { get; private set; }

        [ForeignKey(nameof(StorageLocationId))]
        public StorageLocation? StorageLocation { get; private set; }

        public void AddStock(int amount, int employeeId)
        {
            if (amount <= 0)
                throw new ArgumentException("Amount to add must be greater than zero.", nameof(amount));

            Quantity += amount;
            LastUpdatedByEmployeeId = employeeId;
        }

  
        public void RemoveStock(int amount, int employeeId)
        {
            if (amount <= 0)
                throw new ArgumentException("Amount to remove must be greater than zero.", nameof(amount));

            if (Quantity - amount < 0)
                throw new InvalidOperationException("Insufficient stock to remove the requested amount.");

            Quantity -= amount;
            LastUpdatedByEmployeeId = employeeId;
        }

     
        public void SetAbsoluteQuantity(int newQuantity, int employeeId)
        {
            if (newQuantity < 0)
                throw new ArgumentException("Quantity cannot be negative.", nameof(newQuantity));

            Quantity = newQuantity;
            LastUpdatedByEmployeeId = employeeId;
        }
    }
}

