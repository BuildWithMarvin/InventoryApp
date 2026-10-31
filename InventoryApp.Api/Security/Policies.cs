namespace InventoryApp.Api.Security
{
    public static class Policies
    {
        /// <summary>A logged-in employee, even one who still has to change an admin-set PIN.</summary>
        public const string EmployeeSession = "EmployeeSession";

        /// <summary>
        /// Placeholder for operations that belong to Entra ID administrators (Phase 4). Until the Entra
        /// scheme exists nobody satisfies it, so those endpoints are closed rather than open.
        /// </summary>
        public const string AdminOnly = "AdminOnly";
    }
}
