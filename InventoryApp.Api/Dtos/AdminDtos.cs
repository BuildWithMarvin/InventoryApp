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
