using System.Security.Claims;

namespace InventoryApp.Api.Security
{
    public static class InventoryClaims
    {
        public const string Subject = "sub";

        /// <summary>Named "inv_tenant", not "tid": Entra ID's "tid" is the Microsoft directory, not our tenant.</summary>
        public const string Tenant = "inv_tenant";

        public const string Session = "sid";

        public const string Device = "did";

        public const string Permission = "permission";

        public const string MustChangePin = "must_change_pin";

        public const string EntraTenant = "tid";

        /// <summary>Entra object id of the signed-in identity. The stable key; "sub" is pairwise per app.</summary>
        public const string EntraObject = "oid";

        public const string EntraRoles = "roles";

        public const string EntraScope = "scp";

        /// <summary>
        /// Added per request after the administrator was resolved in the database ("tenant" or
        /// "platform"). Tokens can't bring it along: it is removed before the principal is built.
        /// </summary>
        public const string AdminKind = "inv_admin_kind";

        public const string AdminKindTenant = "tenant";

        public const string AdminKindPlatform = "platform";

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
