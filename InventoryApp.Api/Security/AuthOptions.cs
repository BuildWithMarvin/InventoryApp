using System.ComponentModel.DataAnnotations;

namespace InventoryApp.Api.Security
{
    /// <summary>Settings for employee authentication, bound from the "Auth" configuration section.</summary>
    public class AuthOptions : IValidatableObject
    {
        public const string SectionName = "Auth";

        /// <summary>
        /// Signs the access tokens. Must come from a secret store (user secrets locally, Key Vault in
        /// Azure), never from the repository. Changing it ends every session.
        /// </summary>
        [Required]
        [MinLength(32, ErrorMessage = "Auth:SigningKey must be at least 32 characters.")]
        public string SigningKey { get; set; } = string.Empty;

        public string Issuer { get; set; } = "InventoryApp";

        public string Audience { get; set; } = "InventoryApp.Employee";

        /// <summary>A session ends after this long without a request.</summary>
        public TimeSpan IdleTimeout { get; set; } = TimeSpan.FromMinutes(30);

        /// <summary>A session ends after this long, whatever the activity.</summary>
        public TimeSpan AbsoluteLifetime { get; set; } = TimeSpan.FromHours(12);

        /// <summary>
        /// Flood control per client IP. The PIN lockout, not this limit, is what stops PIN guessing; the
        /// default is generous because a whole warehouse can sit behind one address.
        /// </summary>
        [Range(1, 10_000)]
        public int LoginRequestsPerMinute { get; set; } = 30;

        [Range(1, 10_000)]
        public int EnrollRequestsPerMinute { get; set; } = 5;

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (IdleTimeout <= TimeSpan.Zero)
                yield return new ValidationResult("Auth:IdleTimeout must be positive.", [nameof(IdleTimeout)]);

            if (AbsoluteLifetime < IdleTimeout)
                yield return new ValidationResult(
                    "Auth:AbsoluteLifetime must not be shorter than Auth:IdleTimeout.", [nameof(AbsoluteLifetime)]);
        }
    }
}
