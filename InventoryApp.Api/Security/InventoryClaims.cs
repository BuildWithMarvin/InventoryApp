using System.Security.Claims;

namespace InventoryApp.Api.Security
{
    /// <summary>Claim types of the employee access token. Names are short because they travel in every request.</summary>
    public static class InventoryClaims
    {
        public const string Subject = "sub";

        /// <summary>Named "inv_tenant", not "tid": Entra ID's "tid" is the Microsoft directory, not our tenant.</summary>
        public const string Tenant = "inv_tenant";

        public const string Session = "sid";

        public const string Device = "did";

        /// <summary>Added per request from the database, never stored in the token.</summary>
        public const string Permission = "permission";

        /// <summary>Added per request while the employee still has to replace an admin-set PIN.</summary>
        public const string MustChangePin = "must_change_pin";

        // ---- Administrators (Microsoft Entra) ------------------------------------------------

        /// <summary>Entra directory id of the signed-in identity. Not an InventoryApp tenant.</summary>
        public const string EntraTenant = "tid";

        /// <summary>Entra object id of the signed-in identity. The stable key; "sub" is pairwise per app.</summary>
        public const string EntraObject = "oid";

        /// <summary>Entra app roles of the token.</summary>
        public const string EntraRoles = "roles";

        /// <summary>Delegated scopes of the token, space separated. App-only tokens have none.</summary>
        public const string EntraScope = "scp";

        /// <summary>
        /// Added per request after the administrator was resolved in the database ("tenant" or
        /// "platform"). Tokens can't bring it along: it is removed before the principal is built.
        /// </summary>
        public const string AdminKind = "inv_admin_kind";

        public const string AdminKindTenant = "tenant";

        public const string AdminKindPlatform = "platform";

        /// <summary>A platform permission (see <see cref="PlatformPermissions"/>). Never mixed up with <see cref="Permission"/>.</summary>
        public const string PlatformPermission = "platform_permission";
    }

    public static class ClaimsPrincipalExtensions
    {
        /// <summary>The employee of the validated session. Never read an employee id from a request body.</summary>
        public static int GetEmployeeId(this ClaimsPrincipal user) =>
            int.TryParse(user.FindFirstValue(InventoryClaims.Subject), out var id)
                ? id
                : throw new InvalidOperationException("The request has no authenticated employee.");

        public static Guid GetSessionId(this ClaimsPrincipal user) =>
            Guid.TryParse(user.FindFirstValue(InventoryClaims.Session), out var id)
                ? id
                : throw new InvalidOperationException("The request has no session.");
    }
}
