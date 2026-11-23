namespace InventoryApp.Api.Security
{
    public static class AdminPolicies
    {
        public const string TenantAdminPrefix = "TenantAdmin:";
        public const string PlatformAdminPrefix = "PlatformAdmin:";

        public const string Session = "AdminSession";

        public const string TenantSettings = TenantAdminPrefix + Permissions.TenantSettings;
        public const string EmployeesManage = TenantAdminPrefix + Permissions.EmployeesManage;
        public const string RolesManage = TenantAdminPrefix + Permissions.RolesManage;
        public const string DevicesManage = TenantAdminPrefix + Permissions.DevicesManage;
        public const string SessionsRevoke = TenantAdminPrefix + Permissions.SessionsRevoke;

        public const string PlatformTenantsRead = PlatformAdminPrefix + PlatformPermissions.TenantsRead;
        public const string PlatformTenantsManage = PlatformAdminPrefix + PlatformPermissions.TenantsManage;
        public const string PlatformDevicesManage = PlatformAdminPrefix + PlatformPermissions.DevicesManage;
        public const string PlatformEmployeesManage = PlatformAdminPrefix + PlatformPermissions.EmployeesManage;
        public const string PlatformSessionsRevoke = PlatformAdminPrefix + PlatformPermissions.SessionsRevoke;

        public static string ForTenantAdmin(string permission) => TenantAdminPrefix + permission;

        public static string ForPlatformAdmin(string permission) => PlatformAdminPrefix + permission;

        public static IReadOnlyList<string> All { get; } =
        [
            Session,
            ..Permissions.Of(PermissionScope.Admin).Select(ForTenantAdmin),
            ..PlatformPermissions.All.Select(ForPlatformAdmin),
        ];
    }
}
