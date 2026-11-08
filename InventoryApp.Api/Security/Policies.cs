namespace InventoryApp.Api.Security
{
    public static class Policies
    {
        /// <summary>A logged-in employee, even one who still has to change an admin-set PIN.</summary>
        public const string EmployeeSession = "EmployeeSession";

        /// <summary>
        /// Placeholder that nobody satisfies, so an endpoint on it is closed rather than open. Employee
        /// creation still uses it; the Entra administrator policies are in <see cref="AdminPolicies"/>.
        /// </summary>
        public const string AdminOnly = "AdminOnly";
    }
}
