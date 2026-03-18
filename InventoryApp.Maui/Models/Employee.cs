using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace InventoryApp.Maui.Models
{
    public class Employee
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string PinCode { get; set; }

        // Indicates if the user needs to update their PIN on their next login
        public bool MustChangePin { get; set; }
    }
}
