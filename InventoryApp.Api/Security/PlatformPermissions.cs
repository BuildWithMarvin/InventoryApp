namespace InventoryApp.Api.Security
{
    /// <summary>
    /// What a Platform Admin may do. Deliberately a separate, explicit list: it is not a database role
    /// and never mixes with the tenant permission catalog (<see cref="Permissions"/>).
    ///
    /// Decision D8 and assumption A2: a Platform Admin supports tenants (tenants, devices, employees,
    /// sessions) but never sees inventory data and never edits role definitions or permissions, so
    /// neither exists here. A test keeps it that way.
    /// </summary>
    public static class PlatformPermissions
    {
        public const string TenantsRead = "Platform.Tenants.Read";
        public const string TenantsManage = "Platform.Tenants.Manage";
        public const string DevicesManage = "Platform.Devices.Manage";
        public const string EmployeesManage = "Platform.Employees.Manage";
        public const string SessionsRevoke = "Platform.Sessions.Revoke";

        public static IReadOnlyList<string> All { get; } =
        [
            TenantsRead,
            TenantsManage,
            DevicesManage,
            EmployeesManage,
            SessionsRevoke,
        ];
    }
}
