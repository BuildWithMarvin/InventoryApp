namespace InventoryApp.Api.Security
{
    /// <summary>
    /// Policies for administrators signed in through Microsoft Entra. They all name the Entra scheme
    /// only, so an employee token can never satisfy them, and the employee policies name the employee
    /// scheme only, so an Entra token can never satisfy those.
    ///
    /// Tenant administrator policies are named after the admin permission they require
    /// ("TenantAdmin:Tenant.Settings") and are registered for the whole Admin-scope catalog
    /// (<see cref="Permissions.Of"/>); platform policies likewise for <see cref="PlatformPermissions"/>.
    /// </summary>
    public static class AdminPolicies
    {
        public const string TenantAdminPrefix = "TenantAdmin:";
        public const string PlatformAdminPrefix = "PlatformAdmin:";

        /// <summary>Any resolved administrator (tenant or platform); no particular permission.</summary>
        public const string Session = "AdminSession";

        // Tenant administrator, one per Admin-scope permission that has an endpoint.
        public const string TenantSettings = TenantAdminPrefix + Permissions.TenantSettings;
        public const string EmployeesManage = TenantAdminPrefix + Permissions.EmployeesManage;
        public const string RolesManage = TenantAdminPrefix + Permissions.RolesManage;
        public const string DevicesManage = TenantAdminPrefix + Permissions.DevicesManage;
        public const string SessionsRevoke = TenantAdminPrefix + Permissions.SessionsRevoke;

        // Platform administrator.
        public const string PlatformTenantsRead = PlatformAdminPrefix + PlatformPermissions.TenantsRead;

        public static string ForTenantAdmin(string permission) => TenantAdminPrefix + permission;

        public static string ForPlatformAdmin(string permission) => PlatformAdminPrefix + permission;

        /// <summary>Every administrator policy that is registered.</summary>
        public static IReadOnlyList<string> All { get; } =
        [
            Session,
            ..Permissions.Of(PermissionScope.Admin).Select(ForTenantAdmin),
            ..PlatformPermissions.All.Select(ForPlatformAdmin),
        ];
    }
}
