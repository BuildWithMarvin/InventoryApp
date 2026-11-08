using System.ComponentModel.DataAnnotations;

namespace InventoryApp.Api.Security
{
    /// <summary>
    /// Settings for administrator sign-in through Microsoft Entra ID, bound from the "Entra" section.
    /// None of these are secrets (an API only validates tokens), but all are environment specific, so
    /// they live in configuration. If <see cref="TenantId"/> and <see cref="ClientId"/> are both empty
    /// Entra is switched off: the scheme stays registered but rejects every token, so administrator
    /// endpoints are closed rather than open.
    /// </summary>
    public class EntraOptions : IValidatableObject
    {
        public const string SectionName = "Entra";

        /// <summary>Login host, e.g. https://login.microsoftonline.com/ (a sovereign cloud differs).</summary>
        public string Instance { get; set; } = "https://login.microsoftonline.com/";

        /// <summary>
        /// The Entra directory ("tenant") that signs administrators in: the only accepted <c>tid</c>.
        /// Not an InventoryApp tenant. B2B guests sign in to this directory too, so their tokens carry it.
        /// </summary>
        public string? TenantId { get; set; }

        /// <summary>Application (client) id of the API's app registration.</summary>
        public string? ClientId { get; set; }

        /// <summary>
        /// The accepted <c>aud</c>. Empty means the client id and <c>api://{ClientId}</c>
        /// (v2 tokens carry the client id, v1 tokens the identifier URI).
        /// </summary>
        public string? Audience { get; set; }

        /// <summary>
        /// Delegated scope the token must carry in <c>scp</c>. Only tokens issued to a signed-in user
        /// have one, so app-only tokens (client credentials) are refused.
        /// </summary>
        public string RequiredScope { get; set; } = "access_as_user";

        /// <summary>App role that makes an identity a Platform Admin. Assigned in the Entra portal.</summary>
        public string PlatformAdminRole { get; set; } = "Platform.Admin";

        /// <summary>Tolerated clock difference to Microsoft's token issuer.</summary>
        public TimeSpan ClockSkew { get; set; } = TimeSpan.FromMinutes(1);

        public bool Enabled =>
            !string.IsNullOrWhiteSpace(TenantId) || !string.IsNullOrWhiteSpace(ClientId);

        // Methods, not properties: options validation reads every public property, and these only make
        // sense once the settings are complete.
        public Guid GetDirectoryId() => Guid.Parse(TenantId!);

        /// <summary>The v2.0 issuer of the directory, which is also where discovery starts.</summary>
        public string GetAuthority() => $"{Instance.TrimEnd('/')}/{TenantId}/v2.0";

        public IReadOnlyList<string> GetAudiences() =>
            string.IsNullOrWhiteSpace(Audience) ? [ClientId!, $"api://{ClientId}"] : [Audience];

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (!Enabled)
                yield break;

            if (!Guid.TryParse(TenantId, out var directory) || directory == Guid.Empty)
                yield return new ValidationResult("Entra:TenantId must be the directory id (a GUID).", [nameof(TenantId)]);

            if (!Guid.TryParse(ClientId, out var client) || client == Guid.Empty)
                yield return new ValidationResult("Entra:ClientId must be the application id (a GUID).", [nameof(ClientId)]);

            if (!Uri.TryCreate(Instance, UriKind.Absolute, out var instance) || instance.Scheme != Uri.UriSchemeHttps)
                yield return new ValidationResult("Entra:Instance must be an https URL.", [nameof(Instance)]);

            if (string.IsNullOrWhiteSpace(RequiredScope))
                yield return new ValidationResult("Entra:RequiredScope must not be empty.", [nameof(RequiredScope)]);

            if (string.IsNullOrWhiteSpace(PlatformAdminRole))
                yield return new ValidationResult("Entra:PlatformAdminRole must not be empty.", [nameof(PlatformAdminRole)]);

            if (ClockSkew < TimeSpan.Zero || ClockSkew > TimeSpan.FromMinutes(5))
                yield return new ValidationResult("Entra:ClockSkew must be between 0 and 5 minutes.", [nameof(ClockSkew)]);
        }
    }
}
