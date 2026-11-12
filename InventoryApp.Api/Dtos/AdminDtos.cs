using System.ComponentModel.DataAnnotations;
using InventoryApp.Api.Models;

namespace InventoryApp.Api.Dtos
{
    public record TenantSummaryDto(int Id, string Name, bool IsActive)
    {
        public static TenantSummaryDto FromEntity(Tenant tenant) =>
            new(tenant.Id, tenant.Name, tenant.IsActive);
    }

    public record TenantDto(int Id, string Name, bool IsActive, bool RequirePin)
    {
        public static TenantDto FromEntity(Tenant tenant) =>
            new(tenant.Id, tenant.Name, tenant.IsActive, tenant.RequirePin);
    }

    /// <summary>
    /// What the signed-in administrator is, resolved by the server: "tenant" and/or "platform", the tenant
    /// they administer (tenant administrators only) and their permissions. The client uses it to decide
    /// what to show; the server enforces it on every endpoint regardless.
    /// </summary>
    public record AdminProfileDto(
        IReadOnlyList<string> Kinds,
        TenantSummaryDto? Tenant,
        IReadOnlyList<string> Permissions,
        IReadOnlyList<string> PlatformPermissions);
}

namespace InventoryApp.Api.Dtos
{
    public record NameRequest([Required, MaxLength(100)] string Name);

    /// <summary>Not a bool: a body without the field must not silently switch the PIN requirement off.</summary>
    public record SetRequirePinRequest([Required] bool? Required);

    // ---- Devices ---------------------------------------------------------------------
    // Neither the credential nor the enrollment code hash is ever returned; the plain enrollment code only
    // once, when it is issued.

    public record DeviceDto(
        int Id,
        string Name,
        string DeviceCode,
        bool IsActive,
        bool IsEnrolled,
        DateTimeOffset? EnrolledAt,
        DateTimeOffset? EnrollmentCodeExpiresAt)
    {
        public static DeviceDto FromEntity(Device device) =>
            new(device.Id, device.Name, device.DeviceCode, device.IsActive, device.IsEnrolled,
                device.EnrolledAt, device.EnrollmentCodeExpiresAt);
    }

    public record CreateDeviceRequest([Required, MaxLength(100)] string Name);

    /// <summary>The one-time enrollment code, shown exactly once. The server keeps only its hash.</summary>
    public record DeviceEnrollmentDto(DeviceDto Device, string EnrollmentCode, DateTimeOffset ExpiresAt);

    // ---- Sessions --------------------------------------------------------------------

    public record SessionDto(
        Guid Id,
        int EmployeeId,
        string EmployeeName,
        int DeviceId,
        string DeviceName,
        DateTimeOffset CreatedAt,
        DateTimeOffset LastSeenAt,
        DateTimeOffset ExpiresAt,
        DateTimeOffset? RevokedAt,
        string? RevokedReason,
        bool IsActive);

    // ---- Roles -----------------------------------------------------------------------

    public record RoleDto(int Id, string Name, string Scope, bool IsSystemRole, IReadOnlyList<string> Permissions)
    {
        public static RoleDto FromEntity(Role role) =>
            new(role.Id, role.Name, role.Scope.ToString(), role.IsSystemRole,
                role.Permissions.Select(p => p.Permission).Order().ToList());
    }

    /// <summary>Create or update an inventory role.</summary>
    public record RoleRequest([Required, MaxLength(50)] string Name, [Required] List<string> Permissions);

    public record PermissionCatalogDto(IReadOnlyList<string> Inventory, IReadOnlyList<string> Admin);

    // ---- Tenant administrators (platform) --------------------------------------------

    public record TenantAdminDto(int Id, Guid EntraObjectId, string? DisplayName, string RoleName, bool IsActive, DateTimeOffset CreatedAt)
    {
        public static TenantAdminDto FromEntity(TenantAdmin admin) =>
            new(admin.Id, admin.EntraObjectId, admin.DisplayName, admin.Role!.Name, admin.IsActive, admin.CreatedAt);
    }

    /// <summary>The identity is the object id (oid) in the one configured Entra directory.</summary>
    public record CreateTenantAdminRequest([Required] Guid? ObjectId, [MaxLength(200)] string? DisplayName = null);
}
