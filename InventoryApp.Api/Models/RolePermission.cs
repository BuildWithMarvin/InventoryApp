using System.ComponentModel.DataAnnotations;
using InventoryApp.Api.Security;

namespace InventoryApp.Api.Models
{
    public class RolePermission : ITenantOwned
    {
        private RolePermission() { }

        internal RolePermission(int tenantId, string permission)
        {
            // Throws for permissions that aren't in the catalog.
            Security.Permissions.ScopeOf(permission);

            TenantId = tenantId;
            Permission = permission;
        }

        public int TenantId { get; private set; }

        public int RoleId { get; private set; }

        [Required]
        [MaxLength(50)]
        public string Permission { get; private set; } = string.Empty;
    }
}
