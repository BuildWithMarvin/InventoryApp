using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace InventoryApp.Maui.Models
{
    public class Product
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public int Quantity { get; set; }
        public decimal Price { get; set; }
        public string Barcode { get; set; }

        // Unser neues Tracking-Feld für das Backend!
        public int? LastUpdatedByEmployeeId { get; set; }
    }
}
