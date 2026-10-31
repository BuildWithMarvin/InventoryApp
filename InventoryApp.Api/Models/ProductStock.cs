using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace InventoryApp.Api.Models
{


    public class ProductStock : ITenantOwned
    {
        
        private ProductStock() { }

        
        public ProductStock(int tenantId, string productInternalBarcode, int storageLocationId)
        {
            if (tenantId <= 0)
                throw new ArgumentException("A tenant is required.", nameof(tenantId));

            TenantId = tenantId;

            if (string.IsNullOrWhiteSpace(productInternalBarcode))
                throw new ArgumentException("Product barcode is required.", nameof(productInternalBarcode));

            ProductInternalBarcode = productInternalBarcode;
            StorageLocationId = storageLocationId;
            Quantity = 0;
        }

        [Key]
        public int Id { get; private set; }

        public int TenantId { get; private set; }

        public int Quantity { get; private set; }

        public int? LastUpdatedByEmployeeId { get; private set; }

        public Employee? LastUpdatedBy { get; private set; }

        [Required]
        [MaxLength(50)]
        public string ProductInternalBarcode { get; private set; }

        public Product? Product { get; private set; }

       
        public int StorageLocationId { get; private set; }

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

