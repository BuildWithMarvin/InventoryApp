using InventoryApp.Api.Models;

namespace InventoryApp.Api.Security
{
    /// <summary>The roles every new tenant starts with.</summary>
    public static class DefaultRoles
    {
        public const string TenantAdmin = "TenantAdmin";
        public const string WarehouseManager = "WarehouseManager";
        public const string WarehouseEmployee = "WarehouseEmployee";

        /// <summary>
        /// TenantAdmin manages the tenant (employees, roles, devices, settings) and is held by
        /// Entra identities, never by badge employees. It is a system role: it can't be edited,
        /// so a tenant can't lock itself out. The two warehouse roles can be edited freely.
        /// </summary>
        public static IReadOnlyList<Role> CreateFor(int tenantId) =>
        [
            new Role(tenantId, TenantAdmin, PermissionScope.Admin,
                Permissions.Of(PermissionScope.Admin), isSystemRole: true),

            new Role(tenantId, WarehouseManager, PermissionScope.Inventory,
                Permissions.Of(PermissionScope.Inventory)),

            new Role(tenantId, WarehouseEmployee, PermissionScope.Inventory,
            [
                Permissions.ProductsRead,
                Permissions.LocationsRead,
                Permissions.StockRead,
                Permissions.StockAdjust,
            ]),
        ];
    }
}
