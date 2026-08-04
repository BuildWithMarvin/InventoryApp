namespace InventoryApp.Api.Models
{
    public class StorageLocation
    {
        public int Id { get; set; }

     
        public string LocationCode { get; set; } = string.Empty;

       
        public string Zone { get; set; } = string.Empty;

        
        public string Barcode { get; set; } = string.Empty;

        public bool IsActive { get; set; } = true;

       
        public ICollection<ProductStock> Stocks { get; set; } = new List<ProductStock>();
    }
}
