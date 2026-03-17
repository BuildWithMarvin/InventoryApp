namespace InventoryApp.Api.Models
{
    public class Product
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public int Quantity { get; set; }
        public decimal Price { get; set; }
        public string? Barcode { get; set; }
        public int? LastUpdatedByEmployeeId { get; set; }
    }
}
