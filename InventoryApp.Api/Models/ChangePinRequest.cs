namespace InventoryApp.Api.Models
{
    public class ChangePinRequest
    {
        public int EmployeeId { get; set; }
        public string NewPin { get; set; }
    }
}
