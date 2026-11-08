using System.ComponentModel.DataAnnotations;
using InventoryApp.Api.Security;

namespace InventoryApp.Api.Models
{
    /// <summary>
    /// Links a Microsoft Entra identity to the one InventoryApp tenant it administers, and to the
    /// Admin-scope role that decides what it may do there. This row, not a claim in the token, is
    /// the authority: it is looked up on every administrator request, so deactivating it (or the
    /// tenant, or changing the role's permissions) applies on the next request.
    ///
    /// The identity is <c>(EntraTenantId, EntraObjectId)</c> = the token's <c>tid</c> and <c>oid</c>.
    /// <c>EntraTenantId</c> is the Microsoft directory, not an InventoryApp tenant. An identity has at
    /// most one row, so it administers exactly one tenant (unique index).
    /// </summary>
    public class TenantAdmin : ITenantOwned
    {
        private TenantAdmin() { }

        public TenantAdmin(Role role, Guid entraTenantId, Guid entraObjectId, DateTimeOffset now, string? displayName = null)
        {
            ArgumentNullException.ThrowIfNull(role);

            if (role.Id <= 0)
                throw new ArgumentException("The role must be saved first.", nameof(role));

            // The database enforces this as well (composite foreign key plus check constraint).
            if (role.Scope != PermissionScope.Admin)
                throw new ArgumentException("A tenant administrator needs an Admin-scope role.", nameof(role));

            if (entraTenantId == Guid.Empty)
                throw new ArgumentException("The Entra directory id (tid) is required.", nameof(entraTenantId));

            if (entraObjectId == Guid.Empty)
                throw new ArgumentException("The Entra object id (oid) is required.", nameof(entraObjectId));

            TenantId = role.TenantId;
            RoleId = role.Id;
            RoleScope = role.Scope;
            Role = role;
            EntraTenantId = entraTenantId;
            EntraObjectId = entraObjectId;
            CreatedAt = now;
            DisplayName = NormalizeDisplayName(displayName);
        }

        public int Id { get; private set; }

        /// <summary>The InventoryApp tenant this identity administers.</summary>
        public int TenantId { get; private set; }

        /// <summary>The <c>tid</c> claim: the Entra directory the identity signed in to.</summary>
        public Guid EntraTenantId { get; private set; }

        /// <summary>The <c>oid</c> claim: the identity's object id in that directory (stable for B2B guests too).</summary>
        public Guid EntraObjectId { get; private set; }

        public int RoleId { get; private set; }

        /// <summary>Repeats the role's scope so a composite foreign key can make the database insist on Admin.</summary>
        public PermissionScope RoleScope { get; private set; }

        public Role? Role { get; private set; }

        /// <summary>A label for people ("Anna, IT"); never used for authorization.</summary>
        [MaxLength(200)]
        public string? DisplayName { get; private set; }

        public bool IsActive { get; private set; } = true;

        public DateTimeOffset CreatedAt { get; private set; }

        public DateTimeOffset? DeactivatedAt { get; private set; }

        public void Deactivate(DateTimeOffset now)
        {
            if (!IsActive)
                return;

            IsActive = false;
            DeactivatedAt = now;
        }

        public void Activate()
        {
            IsActive = true;
            DeactivatedAt = null;
        }

        private static string? NormalizeDisplayName(string? displayName)
        {
            if (string.IsNullOrWhiteSpace(displayName))
                return null;

            var normalized = displayName.Trim();

            if (normalized.Length > 200)
                throw new ArgumentException("Display name cannot exceed 200 characters.", nameof(displayName));

            return normalized;
        }
    }
}
