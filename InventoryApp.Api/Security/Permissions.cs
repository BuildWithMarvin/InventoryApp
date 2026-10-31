namespace InventoryApp.Api.Security
{
    /// <summary>
    /// Who may use a permission. Inventory permissions belong to warehouse employees
    /// (badge login). Admin permissions are only usable through Entra ID, so privileged
    /// operations never depend on a badge and a PIN.
    /// </summary>
    public enum PermissionScope
    {
        Inventory = 0,
        Admin = 1
    }

    /// <summary>The fixed catalog of permissions. Roles are per tenant, permissions are not.</summary>
    public static class Permissions
    {
        // Inventory
        public const string ProductsRead = "Products.Read";
        public const string ProductsWrite = "Products.Write";
        public const string LocationsRead = "Locations.Read";
        public const string LocationsManage = "Locations.Manage";
        public const string StockRead = "Stock.Read";
        public const string StockAdjust = "Stock.Adjust";

        // Admin
        public const string EmployeesManage = "Employees.Manage";
        public const string RolesManage = "Roles.Manage";
        public const string DevicesManage = "Devices.Manage";
        public const string SessionsRevoke = "Sessions.Revoke";
        public const string TenantSettings = "Tenant.Settings";

        private static readonly IReadOnlyDictionary<string, PermissionScope> Catalog =
            new Dictionary<string, PermissionScope>
            {
                [ProductsRead] = PermissionScope.Inventory,
                [ProductsWrite] = PermissionScope.Inventory,
                [LocationsRead] = PermissionScope.Inventory,
                [LocationsManage] = PermissionScope.Inventory,
                [StockRead] = PermissionScope.Inventory,
                [StockAdjust] = PermissionScope.Inventory,
                [EmployeesManage] = PermissionScope.Admin,
                [RolesManage] = PermissionScope.Admin,
                [DevicesManage] = PermissionScope.Admin,
                [SessionsRevoke] = PermissionScope.Admin,
                [TenantSettings] = PermissionScope.Admin,
            };

        public static IReadOnlyCollection<string> All => Catalog.Keys.ToList();

        public static IReadOnlyList<string> Of(PermissionScope scope) =>
            Catalog.Where(p => p.Value == scope).Select(p => p.Key).ToList();

        public static bool IsDefined(string? permission) =>
            permission is not null && Catalog.ContainsKey(permission);

        public static PermissionScope ScopeOf(string permission) =>
            Catalog.TryGetValue(permission, out var scope)
                ? scope
                : throw new ArgumentException($"Unknown permission '{permission}'.", nameof(permission));
    }
}
