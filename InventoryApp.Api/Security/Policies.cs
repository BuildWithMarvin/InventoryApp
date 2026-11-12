namespace InventoryApp.Api.Security
{
    public static class Policies
    {
        /// <summary>A logged-in employee, even one who still has to change an admin-set PIN.</summary>
        public const string EmployeeSession = "EmployeeSession";
    }
}
