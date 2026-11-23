using System.ComponentModel.DataAnnotations;
using InventoryApp.Api.Security;

namespace InventoryApp.Api.Models
{
    public class Role : ITenantOwned
    {
        private Role() { }

        public Role(
            int tenantId,
            string name,
            PermissionScope scope,
            IEnumerable<string> permissions,
            bool isSystemRole = false)
        {
            if (tenantId <= 0)
                throw new ArgumentException("A tenant is required.", nameof(tenantId));

            if (!Enum.IsDefined(scope))
                throw new ArgumentException("Unknown scope.", nameof(scope));

            TenantId = tenantId;
            Name = NormalizeName(name);
            Scope = scope;
            IsSystemRole = isSystemRole;

            foreach (var permission in ValidatePermissions(permissions, scope))
                Permissions.Add(new RolePermission(tenantId, permission));
        }

        public int Id { get; private set; }

        public int TenantId { get; private set; }

        [Required]
        [MaxLength(50)]
        public string Name { get; private set; } = string.Empty;

        public PermissionScope Scope { get; private set; }

        public bool IsSystemRole { get; private set; }

        public ICollection<RolePermission> Permissions { get; } = new List<RolePermission>();

        public void Rename(string name)
        {
            EnsureEditable();
            Name = NormalizeName(name);
        }

        public void SetPermissions(IEnumerable<string> permissions)
        {
            EnsureEditable();

            var wanted = ValidatePermissions(permissions, Scope);

            foreach (var existing in Permissions.Where(p => !wanted.Contains(p.Permission)).ToList())
                Permissions.Remove(existing);

            foreach (var permission in wanted.Where(w => Permissions.All(p => p.Permission != w)))
                Permissions.Add(new RolePermission(TenantId, permission));
        }

        private void EnsureEditable()
        {
            if (IsSystemRole)
                throw new InvalidOperationException($"The system role '{Name}' cannot be changed.");
        }

        private static string NormalizeName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Name is required.", nameof(name));

            var normalized = name.Trim();

            if (normalized.Length > 50)
                throw new ArgumentException("Name cannot exceed 50 characters.", nameof(name));

            return normalized;
        }

        private static HashSet<string> ValidatePermissions(IEnumerable<string> permissions, PermissionScope scope)
        {
            ArgumentNullException.ThrowIfNull(permissions);

            var result = new HashSet<string>();

            foreach (var permission in permissions)
            {
                if (Security.Permissions.ScopeOf(permission) != scope)
                    throw new ArgumentException(
                        $"The permission '{permission}' is not allowed in a {scope} role.",
                        nameof(permissions));

                result.Add(permission);
            }

            return result;
        }
    }
}
