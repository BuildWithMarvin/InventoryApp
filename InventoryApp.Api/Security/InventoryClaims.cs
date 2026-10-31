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
