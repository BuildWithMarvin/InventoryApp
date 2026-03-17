namespace InventoryApp.Api.Models
{
    public class Employee
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string PinCode { get; set; }// Als String, falls mal jemand "0070" hat
    }
}
